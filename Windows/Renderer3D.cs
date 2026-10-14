using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using Touareg.Core;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Touareg.Windows;

// Draws the car model with Direct3D 11 into an off-screen multisampled target and copies the finished,
// premultiplied pixels into the layered pet window, so per-pixel hit-testing keeps working.
// Falls back to WARP (software) when there is no usable GPU, e.g. on CI runners.
internal sealed class Renderer3D : IDisposable
{
    const float Depth = 4000;
    static readonly Vector3 PaintColor = new(.40f, .42f, .45f);   // linear silver metallic

    [StructLayout(LayoutKind.Sequential)] struct FrameData { public Matrix4x4 ViewProj; public Vector4 Light, Paint; }
    [StructLayout(LayoutKind.Sequential)] struct ObjectData { public Matrix4x4 World; public Vector4 Params; }
    sealed record Mesh(ID3D11Buffer Vertices, ID3D11Buffer Indices, MeshPart Part);

    readonly CarModel model;
    readonly Chassis chassis;
    readonly List<IDisposable> owned = [];
    readonly ID3D11Device device;
    readonly ID3D11DeviceContext context;
    readonly ID3D11VertexShader vertexShader;
    readonly ID3D11PixelShader pixelShader;
    readonly ID3D11InputLayout layout;
    readonly ID3D11Buffer frameBuffer, objectBuffer;
    readonly ID3D11ShaderResourceView albedo, metalRough;
    readonly ID3D11SamplerState sampler;
    readonly ID3D11RasterizerState rasterizer;
    readonly ID3D11BlendState opaque, premultiplied;
    readonly ID3D11DepthStencilState depthWrite, depthRead;
    readonly Mesh body, glass, shadow;
    readonly Mesh[] wheels;
    readonly uint samples;
    ID3D11Texture2D? color, depth, resolved, staging;
    ID3D11RenderTargetView? colorView;
    ID3D11DepthStencilView? depthView;
    int width, height;
    float shadowStrength;
    internal string Adapter { get; }

    internal static Renderer3D? TryCreate()
    {
        try
        {
            using var stream = Assets.Open("car/porsche-930.glb");
            return new Renderer3D(CarModel.Load(stream));
        }
        catch (Exception e) { Log.Write("render3d", e); return null; }
    }

    Renderer3D(CarModel model)
    {
        this.model = model;
        chassis = new(model.Wheelbase, model.WheelRadius);
        FeatureLevel[] levels = [FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0];
        ID3D11Device? created = null; ID3D11DeviceContext? immediate = null;
        if (D3D11.D3D11CreateDevice(IntPtr.Zero, DriverType.Hardware, DeviceCreationFlags.BgraSupport, levels, out created, out immediate).Failure)
        {
            if (D3D11.D3D11CreateDevice(IntPtr.Zero, DriverType.Warp, DeviceCreationFlags.BgraSupport, levels, out created, out immediate).Failure)
                throw new InvalidOperationException("Direct3D 11 is not available");
            Adapter = "WARP";
        }
        else Adapter = "hardware";
        device = created!; context = immediate!;

        samples = new uint[] { 8, 4, 2 }.FirstOrDefault(n =>
            device.CheckMultisampleQualityLevels(Format.B8G8R8A8_UNorm, n) > 0 && device.CheckMultisampleQualityLevels(Format.D24_UNorm_S8_UInt, n) > 0, 1u);

        var vs = Compiler.Compile(Shader, "VS", "car.hlsl", "vs_4_0", ShaderFlags.OptimizationLevel3);
        var ps = Compiler.Compile(Shader, "PS", "car.hlsl", "ps_4_0", ShaderFlags.OptimizationLevel3);
        vertexShader = Own(device.CreateVertexShader(vs.Span));
        pixelShader = Own(device.CreatePixelShader(ps.Span));
        layout = Own(device.CreateInputLayout(
        [
            new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0),
            new InputElementDescription("NORMAL", 0, Format.R32G32B32_Float, 12, 0),
            new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 24, 0),
        ], vs.Span));
        frameBuffer = Own(device.CreateBuffer((uint)Marshal.SizeOf<FrameData>(), BindFlags.ConstantBuffer));
        objectBuffer = Own(device.CreateBuffer((uint)Marshal.SizeOf<ObjectData>(), BindFlags.ConstantBuffer));

        albedo = Texture(model.Albedo); metalRough = Texture(model.MetalRough);
        sampler = Own(device.CreateSamplerState(new SamplerDescription(Filter.Anisotropic, TextureAddressMode.Wrap, 0, 8)));
        // Two-sided: downloaded models are not reliably wound; the shader orients normals towards the viewer.
        rasterizer = Own(device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid) { MultisampleEnable = true, DepthClipEnable = true }));
        opaque = Own(device.CreateBlendState(BlendDescription.Opaque));
        premultiplied = Own(device.CreateBlendState(new BlendDescription(Blend.One, Blend.InverseSourceAlpha, Blend.One, Blend.InverseSourceAlpha)));
        depthWrite = Own(device.CreateDepthStencilState(new DepthStencilDescription(true, DepthWriteMask.All, ComparisonFunction.LessEqual)));
        depthRead = Own(device.CreateDepthStencilState(new DepthStencilDescription(true, DepthWriteMask.Zero, ComparisonFunction.LessEqual)));

        body = Upload(model.Body); glass = Upload(model.Glass);
        wheels = model.Wheels.Select(Upload).ToArray();
        // A soft contact shadow on the ground plane, in the car's own frame (x forward, z towards the viewer).
        float y = -model.WheelRadius + .005f, f = model.Front + .25f, r = model.Rear - .25f;
        shadow = Upload(new MeshPart("shadow", Vector3.Zero,
            [r, y, -1.3f, 0, 1, 0, 0, 0,  f, y, -1.3f, 0, 1, 0, 1, 0,  f, y, 1.3f, 0, 1, 0, 1, 1,  r, y, 1.3f, 0, 1, 0, 0, 1],
            [0, 1, 2, 0, 2, 3], [new(0, 6, MaterialKind.Body)]));
    }

    T Own<T>(T item) where T : IDisposable { owned.Add(item); return item; }

    Mesh Upload(MeshPart part) =>
        new(Own(device.CreateBuffer(part.Vertices, BindFlags.VertexBuffer)), Own(device.CreateBuffer(part.Indices, BindFlags.IndexBuffer)), part);

    ID3D11ShaderResourceView Texture(byte[] encoded)
    {
        int width, height; byte[] bgra;
        if (encoded[0] == 0x89) (width, height, bgra) = Png.Decode(encoded);
        else (width, height, bgra) = Gdi(encoded);   // JPEG: colour data only, so GDI+ decoding is fine
        // Full mip chain, so the 2K atlas stays smooth when the car is only a couple of hundred pixels long.
        var texture = Own(device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 0,
            BindFlags.ShaderResource | BindFlags.RenderTarget, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.GenerateMips)));
        context.UpdateSubresource(bgra, texture, 0, (uint)width * 4);
        var view = Own(device.CreateShaderResourceView(texture));
        context.GenerateMips(view);
        return view;
    }

    static (int, int, byte[]) Gdi(byte[] encoded)
    {
        using var stream = new MemoryStream(encoded);
        using var image = new Bitmap(stream);
        var data = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bgra = new byte[image.Width * image.Height * 4];
            for (int y = 0; y < image.Height; y++) Marshal.Copy(data.Scan0 + y * data.Stride, bgra, y * image.Width * 4, image.Width * 4);
            return (image.Width, image.Height, bgra);
        }
        finally { image.UnlockBits(data); }
    }

    void Targets(int w, int h)
    {
        if (w == width && h == height && color is not null) return;
        ReleaseTargets(); width = w; height = h;
        color = device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)w, (uint)h, 1, 1, BindFlags.RenderTarget, sampleCount: samples));
        depth = device.CreateTexture2D(new Texture2DDescription(Format.D24_UNorm_S8_UInt, (uint)w, (uint)h, 1, 1, BindFlags.DepthStencil, sampleCount: samples));
        resolved = device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)w, (uint)h, 1, 1, BindFlags.None));
        staging = device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)w, (uint)h, 1, 1, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
        colorView = device.CreateRenderTargetView(color);
        depthView = device.CreateDepthStencilView(depth);
    }

    void ReleaseTargets()
    {
        colorView?.Dispose(); depthView?.Dispose(); color?.Dispose(); depth?.Dispose(); resolved?.Dispose(); staging?.Dispose();
        colorView = null; depthView = null; color = depth = resolved = staging = null;
    }

    // Steps the suspension, renders the car into the pet window and returns where the window went.
    internal (int Left, int Top) Draw(World world, double dt, LayerWindow pet, int w, int h)
    {
        var s = world.State;
        var b = chassis.Update(s, dt, CarPlacement.PixelsPerUnit(model, world.Scale), world.WheelSpin);
        var place = CarPlacement.Place(model, s, b, world.Scale, w, h);
        int left = place.Left, top = place.Top;
        shadowStrength += ((s.Grounded ? .42f : 0) - shadowStrength) * (float)Math.Min(1, dt * 10);

        Targets(w, h);
        context.ClearRenderTargetView(colorView!, new Color4(0, 0, 0, 0));
        context.ClearDepthStencilView(depthView!, DepthStencilClearFlags.Depth, 1, 0);
        context.OMSetRenderTargets(colorView!, depthView);
        context.RSSetViewport(0, 0, w, h);
        context.RSSetState(rasterizer);
        context.IASetInputLayout(layout);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.VSSetShader(vertexShader); context.PSSetShader(pixelShader);
        context.VSSetConstantBuffer(0, frameBuffer); context.VSSetConstantBuffer(1, objectBuffer);
        context.PSSetConstantBuffer(0, frameBuffer); context.PSSetConstantBuffer(1, objectBuffer);
        context.PSSetShaderResource(0, albedo); context.PSSetShaderResource(1, metalRough);
        context.PSSetSampler(0, sampler);

        var viewProj = new Matrix4x4(2f / w, 0, 0, 0, 0, 2f / h, 0, 0, 0, 0, -1 / (2 * Depth), 0, -1, 1, .5f, 1);
        var frame = new FrameData { ViewProj = viewProj, Light = new(Vector3.Normalize(new(-.35f, .85f, .45f)), 0), Paint = new(PaintColor, 1) };
        context.UpdateSubresource(in frame, frameBuffer);

        context.OMSetBlendState(premultiplied); context.OMSetDepthStencilState(depthRead);
        if (shadowStrength > .01f) Draw(shadow, place.Unsprung, 3, shadowStrength);
        context.OMSetBlendState(opaque); context.OMSetDepthStencilState(depthWrite);
        Draw(body, place.Sprung, 0, 1);
        for (int i = 0; i < wheels.Length; i++) Draw(wheels[i], place.Wheels[i], 0, 1);
        context.OMSetBlendState(premultiplied); context.OMSetDepthStencilState(depthRead);
        Draw(glass, place.Sprung, 2, .55f);

        context.ResolveSubresource(resolved!, 0, color!, 0, Format.B8G8R8A8_UNorm);
        context.CopyResource(staging!, resolved!);
        pet.Canvas(w, h);
        var mapped = context.Map(staging!, 0, MapMode.Read);
        try
        {
            unsafe
            {
                byte* source = (byte*)mapped.DataPointer, target = (byte*)pet.Pixels;
                for (int row = 0; row < h; row++)
                    Buffer.MemoryCopy(source + row * mapped.RowPitch, target + row * w * 4, w * 4, w * 4);
            }
        }
        finally { context.Unmap(staging!, 0); }
        pet.Present(left, top);
        return (left, top);
    }

    void Draw(Mesh mesh, Matrix4x4 world, float kind, float alpha)
    {
        context.IASetVertexBuffer(0, mesh.Vertices, MeshPart.Stride * 4);
        context.IASetIndexBuffer(mesh.Indices, Format.R32_UInt, 0);
        foreach (var range in mesh.Part.Ranges)
        {
            float k = kind == 0 && range.Material == MaterialKind.Paint ? 1 : kind;
            var data = new ObjectData { World = world, Params = new(k, alpha, 0, 0) };
            context.UpdateSubresource(in data, objectBuffer);
            context.DrawIndexed((uint)range.Count, (uint)range.Start, 0);
        }
    }

    public void Dispose()
    {
        ReleaseTargets();
        foreach (var item in Enumerable.Reverse(owned)) item.Dispose();
        context.ClearState(); context.Dispose(); device.Dispose();
    }

    // Lighting: sun plus sky ambient, GGX highlights, a procedural studio sky for reflections and a clear coat on
    // the paint. Output is gamma encoded and premultiplied, which is what UpdateLayeredWindow expects.
    const string Shader = """
        cbuffer Frame : register(b0) { row_major float4x4 ViewProj; float4 Light; float4 Paint; };
        cbuffer Object : register(b1) { row_major float4x4 World; float4 Params; };
        Texture2D Albedo : register(t0);
        Texture2D MetalRough : register(t1);
        SamplerState Smp : register(s0);
        struct VIn { float3 pos : POSITION; float3 nrm : NORMAL; float2 uv : TEXCOORD0; };
        struct VOut { float4 pos : SV_Position; float3 nrm : NORMAL; float2 uv : TEXCOORD0; float3 at : TEXCOORD1; };

        VOut VS(VIn v)
        {
            VOut o;
            float4 w = mul(float4(v.pos, 1), World);
            o.pos = mul(w, ViewProj);
            o.at = w.xyz;
            o.nrm = mul(v.nrm, (float3x3)World);
            o.uv = v.uv;
            return o;
        }

        float3 Sky(float3 d)
        {
            float3 zenith = float3(0.30, 0.42, 0.62), horizon = float3(0.95, 0.96, 0.98), ground = float3(0.05, 0.05, 0.06);
            float3 c = d.y >= 0 ? lerp(horizon, zenith, pow(saturate(d.y), 0.6)) : lerp(horizon * 0.45, ground, pow(saturate(-d.y), 0.4));
            c += 3.0 * pow(saturate(dot(d, normalize(float3(-0.35, 0.75, 0.55)))), 60);
            c += 1.2 * pow(saturate(dot(d, normalize(float3(0.6, 0.4, 0.7)))), 30);
            return c;
        }

        float4 PS(VOut i) : SV_Target
        {
            float kind = Params.x;
            if (kind > 2.5)
            {
                float2 q = i.uv * 2 - 1;
                float a = (1 - smoothstep(0.3, 1.0, length(q * float2(1.0, 1.15)))) * Params.y;
                return float4(0, 0, 0, a);
            }
            // Orient by the triangle itself, not by winding: the geometric normal is turned towards the viewer and the
            // shading normal follows it, so meshes wound either way (or seen from behind) are lit from the front.
            float3 v = float3(0, 0, 1);
            float3 g = normalize(cross(ddx(i.at), ddy(i.at)));
            if (dot(g, v) < 0) g = -g;
            float3 n = normalize(i.nrm);
            if (dot(n, g) < 0) n = -n;
            float3 base = pow(abs(Albedo.Sample(Smp, i.uv).rgb), 2.2);
            float4 mr = MetalRough.Sample(Smp, i.uv);
            float metal = mr.b, rough = clamp(mr.g, 0.06, 1.0), coat = 0;
            if (kind > 0.5 && kind < 1.5) { base = Paint.rgb; metal = 0.6; rough = 0.3; coat = 1; }
            bool glass = kind > 1.5;
            if (glass) { base = float3(0.015, 0.018, 0.022); metal = 0; rough = 0.04; }

            float3 l = normalize(Light.xyz);
            float ndl = saturate(dot(n, l)), ndv = saturate(dot(n, v)) + 1e-3;
            float3 h = normalize(l + v);
            float ndh = saturate(dot(n, h));
            float a = rough * rough, a2 = a * a, dd = ndh * ndh * (a2 - 1) + 1;
            float D = a2 / (3.14159 * dd * dd);
            float k = (rough + 1) * (rough + 1) / 8;
            float G = (ndl / (ndl * (1 - k) + k)) * (ndv / (ndv * (1 - k) + k));
            float3 F0 = lerp(float3(0.04, 0.04, 0.04), base, metal);
            // Rough surfaces lose their grazing-angle mirror: tyres and plastics stay matte (Karis' approximation).
            float3 F = F0 + (max(1 - rough, F0) - F0) * pow(saturate(1 - ndv), 5);
            float3 spec = D * G * F / max(4 * ndl * ndv, 1e-3);
            float3 kd = (1 - metal) * (1 - F);
            float3 hemi = lerp(float3(0.10, 0.10, 0.10), float3(0.62, 0.66, 0.74), n.y * 0.5 + 0.5);
            float3 r = reflect(-v, n);
            float3 env = lerp(Sky(r), hemi, saturate(rough * 1.4));
            float3 color = kd * base * (ndl * 2.4 + hemi) + spec * ndl * 2.4 + env * F * (1 - rough * 0.6);
            if (coat > 0)
            {
                float fc = 0.04 + 0.96 * pow(saturate(1 - ndv), 5);
                color = color * (1 - fc) + Sky(r) * fc;
            }
            color = color / (1 + color * 0.25);
            color = pow(saturate(color), 1 / 2.2);
            float alpha = 1;
            if (glass) alpha = saturate(Params.y + dot(F, float3(0.33, 0.33, 0.33)) * 0.6);
            return float4(color * alpha, alpha);
        }
        """;
}

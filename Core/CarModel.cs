using System.Numerics;
using System.Text;
using System.Text.Json;

namespace Touareg.Core;

public enum MaterialKind { Body, Paint, Glass }
public readonly record struct DrawRange(int Start, int Count, MaterialKind Material);

// One node of the car: interleaved position, normal, uv (8 floats per vertex) in the node's own space.
public sealed class MeshPart(string name, Vector3 origin, float[] vertices, uint[] indices, List<DrawRange> ranges)
{
    public const int Stride = 8;
    public string Name { get; } = name;
    public Vector3 Origin { get; } = origin;
    public float[] Vertices { get; } = vertices;
    public uint[] Indices { get; } = indices;
    public List<DrawRange> Ranges { get; } = ranges;
    public int VertexCount => Vertices.Length / Stride;
    public (Vector3 Min, Vector3 Max) Bounds()
    {
        Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
        for (int i = 0; i < Vertices.Length; i += Stride)
        {
            var p = new Vector3(Vertices[i], Vertices[i + 1], Vertices[i + 2]);
            lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p);
        }
        return (lo, hi);
    }
}

// The car exported by tools/model/prepare_car.py: glTF axes (+Y up, +Z forward, +X the car's left),
// origin between the axles at axle height. Nodes: body, glass, wheel_FL/FR/RL/RR.
public sealed class CarModel
{
    public required MeshPart Body { get; init; }
    public required MeshPart Glass { get; init; }
    public required MeshPart[] Wheels { get; init; }   // FL, FR, RL, RR
    public required byte[] Albedo { get; init; }       // encoded PNG/JPEG
    public required byte[] MetalRough { get; init; }   // glTF layout: G roughness, B metallic
    public float WheelRadius { get; init; }
    public float Wheelbase { get; init; }
    public float Front { get; init; }   // nose, +Z
    public float Rear { get; init; }    // tail, -Z
    public float Top { get; init; }
    public float Length => Front - Rear;
    public static readonly string[] WheelNames = ["wheel_FL", "wheel_FR", "wheel_RL", "wheel_RR"];
    public static bool IsFront(int wheel) => wheel < 2;

    public static CarModel Load(Stream stream)
    {
        using var reader = new BinaryReader(stream);
        if (reader.ReadUInt32() != 0x46546C67 || reader.ReadUInt32() != 2) throw new InvalidDataException("Not a glTF 2 binary");
        reader.ReadUInt32();
        int jsonLength = reader.ReadInt32(); reader.ReadUInt32();
        using var json = JsonDocument.Parse(Encoding.UTF8.GetString(reader.ReadBytes(jsonLength)));
        int binLength = reader.ReadInt32(); reader.ReadUInt32();
        byte[] bin = reader.ReadBytes(binLength);
        var root = json.RootElement;

        var views = root.GetProperty("bufferViews").EnumerateArray().ToArray();
        var accessors = root.GetProperty("accessors").EnumerateArray().ToArray();
        var materials = root.GetProperty("materials").EnumerateArray().ToArray();
        var meshes = root.GetProperty("meshes").EnumerateArray().ToArray();

        ReadOnlySpan<byte> View(int index, out int stride)
        {
            var v = views[index];
            stride = v.TryGetProperty("byteStride", out var s) ? s.GetInt32() : 0;
            int offset = v.TryGetProperty("byteOffset", out var o) ? o.GetInt32() : 0;
            return bin.AsSpan(offset, v.GetProperty("byteLength").GetInt32());
        }
        float[] Floats(int accessor, int width)
        {
            var a = accessors[accessor];
            if (a.GetProperty("componentType").GetInt32() != 5126) throw new InvalidDataException("Expected float vertex data");
            int count = a.GetProperty("count").GetInt32(), offset = a.TryGetProperty("byteOffset", out var o) ? o.GetInt32() : 0;
            var data = View(a.GetProperty("bufferView").GetInt32(), out int stride);
            if (stride == 0) stride = width * 4;
            var result = new float[count * width];
            for (int i = 0; i < count; i++)
                for (int k = 0; k < width; k++) result[i * width + k] = BitConverter.ToSingle(data.Slice(offset + i * stride + k * 4, 4));
            return result;
        }
        uint[] Indices(int accessor)
        {
            var a = accessors[accessor];
            int type = a.GetProperty("componentType").GetInt32(), count = a.GetProperty("count").GetInt32();
            int offset = a.TryGetProperty("byteOffset", out var o) ? o.GetInt32() : 0;
            var data = View(a.GetProperty("bufferView").GetInt32(), out _);
            var result = new uint[count];
            for (int i = 0; i < count; i++)
                result[i] = type switch
                {
                    5121 => data[offset + i],
                    5123 => BitConverter.ToUInt16(data.Slice(offset + i * 2, 2)),
                    5125 => BitConverter.ToUInt32(data.Slice(offset + i * 4, 4)),
                    _ => throw new InvalidDataException("Unsupported index type " + type),
                };
            return result;
        }
        MaterialKind Kind(int material) => materials[material].GetProperty("name").GetString() switch
        {
            "paint" => MaterialKind.Paint,
            "glass" => MaterialKind.Glass,
            _ => MaterialKind.Body,
        };

        var parts = new Dictionary<string, MeshPart>();
        foreach (var node in root.GetProperty("nodes").EnumerateArray())
        {
            if (!node.TryGetProperty("mesh", out var meshIndex)) continue;
            string name = node.GetProperty("name").GetString() ?? "";
            var origin = Vector3.Zero;
            if (node.TryGetProperty("translation", out var t))
                origin = new(t[0].GetSingle(), t[1].GetSingle(), t[2].GetSingle());
            List<float> vertices = []; List<uint> indices = []; List<DrawRange> ranges = [];
            foreach (var primitive in meshes[meshIndex.GetInt32()].GetProperty("primitives").EnumerateArray())
            {
                var attributes = primitive.GetProperty("attributes");
                float[] position = Floats(attributes.GetProperty("POSITION").GetInt32(), 3);
                float[] normal = Floats(attributes.GetProperty("NORMAL").GetInt32(), 3);
                int count = position.Length / 3;
                float[] uv = attributes.TryGetProperty("TEXCOORD_0", out var uvIndex) ? Floats(uvIndex.GetInt32(), 2) : new float[count * 2];
                uint baseVertex = (uint)(vertices.Count / MeshPart.Stride);
                for (int i = 0; i < count; i++)
                {
                    vertices.AddRange([position[i * 3], position[i * 3 + 1], position[i * 3 + 2]]);
                    vertices.AddRange([normal[i * 3], normal[i * 3 + 1], normal[i * 3 + 2]]);
                    vertices.AddRange([uv[i * 2], uv[i * 2 + 1]]);
                }
                uint[] local = primitive.TryGetProperty("indices", out var ii) ? Indices(ii.GetInt32()) : Enumerable.Range(0, count).Select(i => (uint)i).ToArray();
                var kind = primitive.TryGetProperty("material", out var m) ? Kind(m.GetInt32()) : MaterialKind.Body;
                ranges.Add(new(indices.Count, local.Length, kind));
                indices.AddRange(local.Select(i => i + baseVertex));
            }
            parts[name] = new(name, origin, [.. vertices], [.. indices], ranges);
        }

        byte[] Image(string material, string slot)
        {
            var pbr = materials.First(m => m.GetProperty("name").GetString() == material).GetProperty("pbrMetallicRoughness");
            int texture = pbr.GetProperty(slot).GetProperty("index").GetInt32();
            int image = root.GetProperty("textures")[texture].GetProperty("source").GetInt32();
            return View(root.GetProperty("images")[image].GetProperty("bufferView").GetInt32(), out _).ToArray();
        }

        foreach (string need in WheelNames.Append("body").Append("glass"))
            if (!parts.ContainsKey(need)) throw new InvalidDataException("Car model has no node " + need);
        var wheels = WheelNames.Select(n => parts[n]).ToArray();
        var (bodyMin, bodyMax) = parts["body"].Bounds();
        var (wheelMin, wheelMax) = wheels[0].Bounds();
        return new CarModel
        {
            Body = parts["body"], Glass = parts["glass"], Wheels = wheels,
            Albedo = Image("body", "baseColorTexture"), MetalRough = Image("body", "metallicRoughnessTexture"),
            WheelRadius = (wheelMax.Y - wheelMin.Y) / 2,
            Wheelbase = (wheels[0].Origin.Z + wheels[1].Origin.Z - wheels[2].Origin.Z - wheels[3].Origin.Z) / 2,
            Front = bodyMax.Z, Rear = bodyMin.Z, Top = bodyMax.Y,
        };
    }
}

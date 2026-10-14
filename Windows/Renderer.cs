using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Touareg.Core;

namespace Touareg.Windows;

internal sealed class Renderer(Sprites sprites, Renderer3D? car3D) : IDisposable
{
    // Frames scaled once with high quality for the current car size; each frame then only rotates them (bilinear).
    sealed class Scaled(double scale) : IDisposable
    {
        public readonly double Scale = scale;
        public readonly Dictionary<string, Bitmap[]> Frames = [];
        public void Dispose() { foreach (var image in Frames.Values.SelectMany(f => f)) image.Dispose(); }
    }
    readonly List<Scaled> cache = [];
    readonly Bitmap[] puffs = Puffs();
    bool effectsClear;

    internal void Draw(World world, LayerWindow pet, LayerWindow effectWindow, LayerWindow pipeWindow, double dt)
    {
        double unit = world.Scale / (194.0 / 440);
        DrawPipe(world, pipeWindow, pet, unit);
        int w = (int)Math.Ceiling(590 * unit), h = (int)Math.Ceiling(420 * unit), left, top;
        if (car3D is not null)
        {
            try { (left, top) = car3D.Draw(world, dt, pet, w, h); }
            catch (Exception e)
            {
                // A lost or broken GPU device: keep the pet alive with the sprites.
                Log.Write("render3d", e); car3D.Dispose(); car3D = null; return;
            }
        }
        else (left, top) = DrawSprites(world, pet, w, h);
        DrawEffects(world, effectWindow, unit, w, h, left, top);
    }

    (int Left, int Top) DrawSprites(World world, LayerWindow pet, int w, int h)
    {
        var pose = world.Pose; double scale = world.Scale;
        int left = (int)Math.Floor(pose.X - w / 2.0), top = (int)Math.Floor(pose.Y - h * .70);
        var frames = For(scale); float n = (float)(512 * scale);
        using (var g = Graphics.FromImage(pet.Canvas(w, h)))
        {
            Setup(g); g.Clear(Color.Transparent);
            using var transform = new Matrix();
            transform.Rotate((float)pose.Angle, MatrixOrder.Append);
            transform.Scale((float)(pose.Direction * pose.Flip), 1, MatrixOrder.Append);
            transform.Translate((float)(pose.X - left), (float)(pose.Y - top), MatrixOrder.Append);
            g.Transform = transform;
            var body = new RectangleF((float)(-256 * scale), (float)(-410 * scale), n, n);
            if (pose.Sequence == "drive" && world.Hover > 0) DrawHover(g, world, frames, pose.Frame, scale, body);
            else g.DrawImage(frames.Frames[pose.Sequence][pose.Frame], body, new RectangleF(0, 0, n, n), GraphicsUnit.Pixel);
            if (pose.Sequence == "drive" && world.Phase != Motion.Turn && world.Hover == 0)
            {
                float r = (float)(31 * scale);
                for (int i = 0; i < 2; i++)
                {
                    var at = sprites.Wheels[pose.Frame][i]; var src = sprites.Wheels[0][i];
                    var save = g.Save(); g.TranslateTransform((float)((at.X - 256) * scale), (float)((at.Y - 410) * scale)); g.RotateTransform((float)(world.WheelSpin * 180 / Math.PI));
                    using var clip = new GraphicsPath(); clip.AddEllipse(-r, -r, 2 * r, 2 * r); g.SetClip(clip);
                    g.DrawImage(frames.Frames["drive"][0], new RectangleF(-r, -r, 2 * r, 2 * r), new RectangleF((float)(src.X * scale) - r, (float)(src.Y * scale) - r, 2 * r, 2 * r), GraphicsUnit.Pixel);
                    g.Restore(save);
                }
            }
        }
        pet.Present(left, top);
        return (left, top);
    }

    void DrawEffects(World world, LayerWindow effectWindow, double unit, int w, int h, int left, int top)
    {
        // Nothing to show and the layer is already empty: skip the second window entirely.
        bool empty = world.CurrentRamp is null && world.Particles.Count == 0 && world.Streaks.Count == 0;
        if (empty && effectsClear) return;
        using (var g = Graphics.FromImage(effectWindow.Canvas(w, h)))
        {
            Setup(g); g.Clear(Color.Transparent); g.TranslateTransform(-left, -top);
            if (world.CurrentRamp is { } ramp)
            {
                PointF[] points = [new((float)ramp.X, (float)ramp.Y), new((float)(ramp.X + ramp.Width * ramp.Direction), (float)(ramp.Y - ramp.Height)), new((float)(ramp.X + ramp.Width * ramp.Direction), (float)ramp.Y)];
                using var fill = new SolidBrush(Color.FromArgb(225, 143, 91, 47)); g.FillPolygon(fill, points);
                using var line = new Pen(Color.FromArgb(255, 243, 196, 121), (float)(3 * unit)); g.DrawLine(line, points[0], points[1]);
                using var support = new Pen(Color.FromArgb(220, 76, 48, 25), (float)(2 * unit));
                for (int i = 1; i < 4; i++) g.DrawLine(support, (float)(ramp.X + ramp.Width * ramp.Direction * i / 4), (float)ramp.Y, (float)(ramp.X + ramp.Width * ramp.Direction * i / 4), (float)(ramp.Y - ramp.Height * i / 4));
            }
            foreach (var p in world.Streaks)
            {
                using var pen = new Pen(Color.FromArgb((int)(155 * (1 - p.Age / .3)), 174, 224, 240), (float)(1.5 * unit));
                g.DrawLine(pen, (float)p.X, (float)p.Y, (float)(p.X - p.Direction * p.Length), (float)p.Y);
            }
            foreach (var p in world.Particles)
            {
                int level = (int)Math.Round((puffs.Length - 1) * (1 - p.Age / p.Life));
                if (p.Radius <= 0 || level <= 0) continue;
                g.DrawImage(puffs[level], (float)(p.X - p.Radius), (float)(p.Y - p.Radius), (float)(2 * p.Radius), (float)(2 * p.Radius));
            }
        }
        effectWindow.Present(left, top); effectsClear = empty;
    }

    // The quarter-pipe stands against the wall in its own click-through window: the car flies far above its own canvas.
    void DrawPipe(World world, LayerWindow window, LayerWindow pet, double unit)
    {
        if (world.CurrentPipe is not { } pipe)
        {
            if (window.Visible) window.Hide();
            return;
        }
        double r = pipe.Radius, pad = 8 * unit, cx = pipe.Wall - pipe.Side * r, cy = pipe.Floor - r;
        int left = (int)Math.Floor(pipe.Side > 0 ? pipe.Wall - r - pad : pipe.Wall - pad), top = (int)Math.Floor(cy - pad);
        int w = (int)Math.Ceiling(r + 2 * pad), h = (int)Math.Ceiling(r + 2 * pad);
        using (var g = Graphics.FromImage(window.Canvas(w, h)))
        {
            Setup(g); g.Clear(Color.Transparent); g.TranslateTransform(-left, -top);
            // Profile: the curved riding surface from the floor up to the lip, then down the wall.
            var arc = Enumerable.Range(0, 25).Select(i => Math.PI / 2 * i / 24)
                .Select(t => new PointF((float)(cx + pipe.Side * r * Math.Sin(t)), (float)(cy + r * Math.Cos(t)))).ToArray();
            using var path = new GraphicsPath(); path.AddPolygon([.. arc, new((float)pipe.Wall, (float)pipe.Floor)]);
            using var fill = new LinearGradientBrush(new PointF(0, (float)cy - 1), new PointF(0, (float)pipe.Floor + 1),
                Color.FromArgb(235, 176, 120, 66), Color.FromArgb(235, 112, 70, 34));
            g.FillPath(fill, path);
            using var support = new Pen(Color.FromArgb(200, 76, 48, 25), (float)(2 * unit));
            for (int i = 1; i < 4; i++)
            {
                double x = pipe.Wall - pipe.Side * r * i / 4, y = cy + Math.Sqrt(Math.Max(0, r * r - (x - cx) * (x - cx)));
                g.DrawLine(support, (float)x, (float)y, (float)x, (float)pipe.Floor);
            }
            using var surface = new Pen(Color.FromArgb(255, 243, 196, 121), (float)(3 * unit)); g.DrawLines(surface, arc);
            // Steel coping on the lip.
            float c = (float)(4 * unit);
            using var coping = new SolidBrush(Color.FromArgb(255, 205, 210, 218));
            g.FillEllipse(coping, (float)(pipe.Wall - pipe.Side * c) - c, (float)cy - c, 2 * c, 2 * c);
        }
        window.Present(left, top);
        if (!window.Visible)
        {
            window.Show();
            Native.SetWindowPos(pet.Handle, (IntPtr)(-1), 0, 0, 0, 0, 0x13);   // keep the car above its pipe
        }
    }

    // Hover conversion: the wheels turn flat under the car (as on the time machine) and fire jets downwards.
    void DrawHover(Graphics g, World world, Scaled frames, int frame, double scale, RectangleF body)
    {
        // The sprite's tyres reach ~44 px from the wheel centre (512 px frame).
        float n = body.Width, r = (float)(44 * scale), well = (float)(47 * scale), fold = (float)world.Hover;
        var wheels = sprites.Wheels[frame].Select(w => new PointF((float)((w.X - 256) * scale), (float)((w.Y - 410) * scale))).ToArray();
        // Body without the wheels painted into the sprite.
        using (var outside = new Region(body))
        {
            foreach (var w in wheels) { using var disc = new GraphicsPath(); disc.AddEllipse(w.X - well, w.Y - well, 2 * well, 2 * well); outside.Exclude(disc); }
            var save = g.Save(); g.SetClip(outside, CombineMode.Intersect);
            g.DrawImage(frames.Frames["drive"][frame], body, new RectangleF(0, 0, n, n), GraphicsUnit.Pixel);
            g.Restore(save);
        }
        using var arch = new SolidBrush(Color.FromArgb(255, 18, 18, 20));
        float squash = 1 - .76f * fold, drop = (float)(9 * scale) * fold;
        for (int i = 0; i < wheels.Length; i++)
        {
            var w = wheels[i]; var src = sprites.Wheels[0][i];
            // Dark wheel arch above the folded wheel.
            g.FillPie(arch, w.X - well, w.Y - well, 2 * well, 2 * well, 180, 180);
            var save = g.Save();
            g.TranslateTransform(w.X, w.Y + drop); g.ScaleTransform(1, squash);
            using (var clip = new GraphicsPath()) { clip.AddEllipse(-r, -r, 2 * r, 2 * r); g.SetClip(clip, CombineMode.Intersect); }
            g.DrawImage(frames.Frames["drive"][0], new RectangleF(-r, -r, 2 * r, 2 * r), new RectangleF((float)(src.X * scale) - r, (float)(src.Y * scale) - r, 2 * r, 2 * r), GraphicsUnit.Pixel);
            g.Restore(save);
            if (world.Thrust > 0) Flame(g, w.X, w.Y + drop + r * squash, (float)scale, (float)world.Thrust, world.Age * 37 + i * 2.1);
        }
    }

    static void Flame(Graphics g, float x, float top, float scale, float thrust, double t)
    {
        float length = (float)((130 + 22 * Math.Sin(t) + 12 * Math.Sin(t * 1.7 + 1)) * scale * thrust), width = 46 * scale * (.6f + .4f * thrust);
        if (length < 2) return;
        // Outer flame, then a hot core, then the glow at the nozzle.
        Tongue(g, x, top, width, length, Color.FromArgb(235, 255, 150, 30), Color.FromArgb(0, 230, 40, 0));
        Tongue(g, x, top, width * .5f, length * .62f, Color.FromArgb(255, 255, 250, 210), Color.FromArgb(0, 255, 200, 60));
        using var glow = new SolidBrush(Color.FromArgb((int)(170 * thrust), 255, 235, 160));
        g.FillEllipse(glow, x - width * .55f, top - 5 * scale, width * 1.1f, 10 * scale);
    }

    static void Tongue(Graphics g, float x, float top, float width, float length, Color hot, Color cold)
    {
        using var path = new GraphicsPath();
        path.AddBezier(x - width / 2, top, x - width / 2, top + length * .45f, x - width * .08f, top + length * .8f, x, top + length);
        path.AddBezier(x, top + length, x + width * .08f, top + length * .8f, x + width / 2, top + length * .45f, x + width / 2, top);
        path.CloseFigure();
        using var brush = new LinearGradientBrush(new PointF(x, top - 1), new PointF(x, top + length + 1), hot, cold);
        g.FillPath(brush, path);
    }

    Scaled For(double scale)
    {
        var hit = cache.FirstOrDefault(c => Math.Abs(c.Scale - scale) < 1e-6);
        if (hit is not null) { if (cache[0] != hit) { cache.Remove(hit); cache.Insert(0, hit); } return hit; }
        var made = new Scaled(scale); int size = (int)Math.Ceiling(512 * scale);
        foreach (var (name, source) in sprites.Frames)
            made.Frames[name] = source.Select(frame =>
            {
                var image = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
                using var g = Graphics.FromImage(image);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.CompositingQuality = CompositingQuality.HighQuality;
                g.DrawImage(frame, 0, 0, (float)(512 * scale), (float)(512 * scale));
                return image;
            }).ToArray();
        cache.Insert(0, made);
        // A few sizes stay ready: monitors with different DPI and recent settings.
        while (cache.Count > 3) { cache[^1].Dispose(); cache.RemoveAt(cache.Count - 1); }
        return made;
    }

    // Soft smoke puffs at 16 opacity levels, drawn as images instead of building a gradient per particle.
    static Bitmap[] Puffs() => Enumerable.Range(0, 16).Select(level =>
    {
        var image = new Bitmap(64, 64, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(image); g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = new GraphicsPath(); path.AddEllipse(0, 0, 64, 64);
        using var brush = new PathGradientBrush(path) { CenterColor = Color.FromArgb((int)(175 * level / 15.0), 224, 229, 237), SurroundColors = [Color.Transparent] };
        g.FillPath(brush, path);
        return image;
    }).ToArray();

    static void Setup(Graphics g) { g.CompositingMode = CompositingMode.SourceOver; g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.Bilinear; g.PixelOffsetMode = PixelOffsetMode.Half; }
    public void Dispose() { foreach (var c in cache) c.Dispose(); foreach (var p in puffs) p.Dispose(); car3D?.Dispose(); }
}

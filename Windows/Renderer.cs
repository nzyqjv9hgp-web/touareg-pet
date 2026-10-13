using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Touareg.Core;

namespace Touareg.Windows;

internal sealed class Renderer(Sprites sprites) : IDisposable
{
    Bitmap? car, effects;
    internal void Draw(World world, LayerWindow pet, LayerWindow effectWindow)
    {
        var pose = world.Pose; double unit = world.Scale / (194.0 / 440);
        int w = (int)Math.Ceiling(590 * unit), h = (int)Math.Ceiling(420 * unit);
        if (car?.Width != w || car.Height != h)
        {
            car?.Dispose(); effects?.Dispose();
            car = new(w, h, PixelFormat.Format32bppPArgb); effects = new(w, h, PixelFormat.Format32bppPArgb);
        }
        int left = (int)Math.Floor(pose.X - w / 2.0), top = (int)Math.Floor(pose.Y - h * .70);
        using (var g = Graphics.FromImage(car))
        {
            Setup(g); g.Clear(Color.Transparent);
            using var transform = new Matrix();
            transform.Rotate((float)pose.Angle, MatrixOrder.Append);
            transform.Scale((float)(world.Scale * pose.Direction * pose.Flip), (float)world.Scale, MatrixOrder.Append);
            transform.Translate((float)(pose.X - left), (float)(pose.Y - top), MatrixOrder.Append);
            g.Transform = transform;
            g.DrawImage(sprites.Frames[pose.Sequence][pose.Frame], -256, -410, 512, 512);
            if (pose.Sequence == "drive" && world.Phase != Motion.Turn)
            {
                for (int i = 0; i < 2; i++)
                {
                    var at = sprites.Wheels[pose.Frame][i]; var src = sprites.Wheels[0][i];
                    var save = g.Save(); g.TranslateTransform(at.X - 256, at.Y - 410); g.RotateTransform((float)(world.WheelSpin * 180 / Math.PI));
                    using var clip = new GraphicsPath(); clip.AddEllipse(-31, -31, 62, 62); g.SetClip(clip);
                    g.DrawImage(sprites.Frames["drive"][0], new RectangleF(-31, -31, 62, 62), new RectangleF(src.X - 31, src.Y - 31, 62, 62), GraphicsUnit.Pixel);
                    g.Restore(save);
                }
            }
        }
        using (var g = Graphics.FromImage(effects!))
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
                if (p.Radius <= 0) continue;
                using var path = new GraphicsPath(); path.AddEllipse((float)(p.X - p.Radius), (float)(p.Y - p.Radius), (float)(2 * p.Radius), (float)(2 * p.Radius));
                using var brush = new PathGradientBrush(path) { CenterColor = Color.FromArgb((int)(175 * (1 - p.Age / p.Life)), 224, 229, 237), SurroundColors = [Color.Transparent] };
                g.FillPath(brush, path);
            }
        }
        effectWindow.Present(effects!, left, top); pet.Present(car, left, top);
    }
    static void Setup(Graphics g) { g.CompositingMode = CompositingMode.SourceOver; g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality; }
    public void Dispose() { car?.Dispose(); effects?.Dispose(); }
}

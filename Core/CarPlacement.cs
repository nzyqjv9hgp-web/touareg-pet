using System.Numerics;

namespace Touareg.Core;

// Where every part of the 3D car goes on the canvas. Canvas space is pixels with x right, y up (row 0 at y = 0,
// rows going negative) and z towards the viewer. The car frame is x forward, y up, z towards the viewer at yaw 0.
public sealed class CarPlacement
{
    public const float Elevation = 7 * MathF.PI / 180;
    public Matrix4x4 Sprung { get; private init; }     // body and glass: on the springs
    public Matrix4x4 Unsprung { get; private init; }   // the car frame on the ground: shadow
    public Matrix4x4[] Wheels { get; private init; } = [];
    public int Left { get; private init; }
    public int Top { get; private init; }

    public static double PixelsPerUnit(CarModel model, double scale) => scale * 440 / model.Length;

    // The canvas is w x h pixels; the axle line sits at 70 % of its height like the sprite car's wheels did.
    public static CarPlacement Place(CarModel model, CarState s, Chassis.Body b, double scale, int w, int h)
    {
        // The axle sits one tyre radius off the contact point, along the normal of the surface (a wall when Lean is 90).
        double ppu = PixelsPerUnit(model, scale), lean = s.Lean * Math.PI / 180;
        double axleX = s.X - Math.Sin(lean) * model.WheelRadius * ppu, axle = s.Ground - Math.Cos(lean) * model.WheelRadius * ppu;
        int left = (int)Math.Floor(axleX - w / 2.0), top = (int)Math.Floor(axle - h * .7);
        // glTF (x left, y up, z forward) turns into the car frame by +90° about y.
        var toCar = Matrix4x4.CreateRotationY(MathF.PI / 2);
        const float rad = MathF.PI / 180;
        var unsprung = Matrix4x4.CreateRotationZ((float)s.Pitch * rad) * Matrix4x4.CreateRotationY((float)s.Yaw * rad)
            * Matrix4x4.CreateRotationX((float)s.Spin * rad)
            * Matrix4x4.CreateScale((float)ppu) * Matrix4x4.CreateRotationX(Elevation)
            * Matrix4x4.CreateTranslation((float)(axleX - left), (float)-(axle - top), 0);
        var sprung = toCar * Matrix4x4.CreateRotationX((float)b.Roll * rad) * Matrix4x4.CreateRotationZ((float)b.Pitch * rad)
            * Matrix4x4.CreateTranslation(0, (float)b.Lift, 0) * unsprung;
        var wheels = new Matrix4x4[model.Wheels.Length];
        for (int i = 0; i < wheels.Length; i++)
        {
            bool front = CarModel.IsFront(i);
            // Spin about the axle (glTF x), steer about the vertical, fold flat for hovering (about the car's length,
            // outer face down, like the time machine), then out to the wheel's place.
            float fold = (model.Wheels[i].Origin.X > 0 ? -1 : 1) * MathF.PI / 2 * (float)s.Hover;
            wheels[i] = Matrix4x4.CreateRotationX((float)(front ? b.FrontSpin : b.RearSpin))
                * (front ? Matrix4x4.CreateRotationY((float)b.Steer * rad) : Matrix4x4.Identity)
                * Matrix4x4.CreateRotationZ(fold)
                * Matrix4x4.CreateTranslation(model.Wheels[i].Origin) * toCar * unsprung;
        }
        return new CarPlacement { Sprung = sprung, Unsprung = unsprung, Wheels = wheels, Left = left, Top = top };
    }
}

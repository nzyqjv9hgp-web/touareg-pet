namespace Touareg.Core;

// Body on springs for the 3D car, driven by what the World does. The World decides where the car goes; this
// adds the weight: squat when accelerating, a nose dive when braking, compression and rebound on landing,
// a jolt on wall hits, body roll and spinning rear tyres in the drift turn.
// Works in model units (the car model's own scale); pixels are converted with PixelsPerUnit.
public sealed class Chassis(double wheelbase, double wheelRadius)
{
    public readonly record struct Body(double Lift, double Pitch, double Roll, double FrontSpin, double RearSpin, double Steer);

    // Heave about 1.5 Hz and pitch about 1.4 Hz, a little under-damped like a road car.
    const double K = 89, C = 6.6, Ip = 1.2, Kp = 93, Cp = 8.0, Kr = 120, Cr = 9;
    const double Droop = .07, Bump = .11, KBump = 1600, CogHeight = .55, Tau = Math.PI * 2;
    readonly double half = wheelbase / 2;
    double h, hv, p, pv, r, rv, accel, frontSpin, rearSpin, steer, clock;
    double lastSpeed, lastGround, groundSpeed, lastPitch, lastYaw, yawRate, lastWheelSpin;
    Motion lastPhase = Motion.Drive;
    bool started, wasGrounded = true;

    public Body Current => new(-h, p * 180 / Math.PI, r * 180 / Math.PI, frontSpin, rearSpin, steer);

    // wheelSpin is World.WheelSpin: during a burnout it drives the rear tyres while the car stands still.
    public Body Update(CarState s, double dt, double pixelsPerUnit, double wheelSpin)
    {
        if (dt <= 0) return Current;
        dt = Math.Min(dt, .1);
        double speed = s.Speed / pixelsPerUnit, ground = s.Ground / pixelsPerUnit;
        double forward = speed * Math.Cos(s.Yaw * Math.PI / 180);
        if (!started)
        {
            started = true; lastSpeed = forward; lastGround = ground; lastPitch = s.Pitch; lastYaw = s.Yaw;
            lastPhase = s.Phase; lastWheelSpin = wheelSpin; wasGrounded = s.Grounded;
        }
        double rawAccel = (forward - lastSpeed) / dt;
        accel += (Math.Clamp(rawAccel, -40, 40) - accel) * Math.Min(1, dt * 14);
        groundSpeed += ((ground - lastGround) / dt - groundSpeed) * Math.Min(1, dt * 30);
        yawRate += ((s.Yaw - lastYaw) / dt - yawRate) * Math.Min(1, dt * 20);

        if (s.Grounded && !wasGrounded)
        {
            // Touchdown: the springs take the fall, and the body keeps its flight pitch until they push it level.
            hv += Math.Min(2.2, Math.Max(0, groundSpeed) * .25);
            p += (lastPitch - s.Pitch) * Math.PI / 180;
        }
        if (s.Phase == Motion.Crash && lastPhase != Motion.Crash)
        {
            pv -= Math.Min(4, Math.Abs(lastSpeed) * .35); hv += .6;
        }

        bool burnout = s.Phase == Motion.Burnout, turning = s.Phase == Motion.Turn;
        double turn = turning ? Math.Sin(Math.PI * Math.Clamp(s.PhaseTime / .9, 0, 1)) : 0;
        double rollTarget = -Math.Sign(yawRate) * 3.5 * turn * Math.PI / 180;
        double pitchBias = (burnout ? 1.6 : 0) * Math.PI / 180 + (turning ? 1.2 * turn * Math.PI / 180 : 0);
        for (double left = dt; left > 1e-6;)
        {
            double step = Math.Min(left, 1.0 / 240); left -= step; clock += step;
            double target = s.Grounded ? 0 : -Droop;
            double ha = K * (target - h) - C * hv, pa = 0;
            if (s.Grounded)
            {
                // Weight transfer: acceleration lifts the nose, braking pushes it down.
                pa = (Kp * (pitchBias - p) - Cp * pv + accel * CogHeight) / Ip;
                if (burnout) ha += Math.Sin(clock * 140) * 6;
                // Bump stops: an axle that runs out of travel pushes back hard.
                double front = h - half * Math.Sin(p), rear = h + half * Math.Sin(p);
                if (front > Bump) { ha -= KBump * (front - Bump) / 2; pa += KBump * (front - Bump) * half / Ip; }
                if (rear > Bump) { ha -= KBump * (rear - Bump) / 2; pa -= KBump * (rear - Bump) * half / Ip; }
            }
            else pa = (Kp * (0 - p) - Cp * pv) / Ip;
            double ra = Kr * (rollTarget - r) - Cr * rv;
            hv += ha * step; h += hv * step;
            pv += pa * step; p += pv * step;
            rv += ra * step; r += rv * step;
        }
        h = Math.Clamp(h, -Droop * 1.5, Bump * 1.6);
        p = Math.Clamp(p, -.2, .2);

        // Tyres roll with the ground; in the air they keep turning and slowly lose speed.
        double roll = forward * dt / wheelRadius;
        if (s.Grounded) { frontSpin += roll; rearSpin += roll; }
        else { frontSpin += roll * .6; rearSpin += roll * .6; }
        if (burnout) rearSpin += (wheelSpin - lastWheelSpin) * 1.3;
        if (turning) { rearSpin += 34 * turn * dt; frontSpin += 3 * turn * dt; }
        double steerTarget = turning ? Math.Sign(yawRate) * 30 * Math.Min(1, turn * 2.2) : 0;
        steer += (steerTarget - steer) * Math.Min(1, dt * 12);

        lastSpeed = forward; lastGround = ground; lastPitch = s.Pitch; lastYaw = s.Yaw; lastPhase = s.Phase;
        lastWheelSpin = wheelSpin; wasGrounded = s.Grounded;
        return Current;
    }
}

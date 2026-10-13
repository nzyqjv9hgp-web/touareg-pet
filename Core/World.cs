namespace Touareg.Core;

// All coordinates are physical desktop pixels, including negative monitor coordinates.
public record Surface(long Id, double Left, double Right, double Y, double Dpi = 96, bool Floor = false);
public record Pose(string Sequence, int Frame, double X, double Y, double Angle, int Direction, double Flip);
public record Ramp(double X, double Y, double Width, double Height, int Direction);
public sealed record Particle(double X, double Y, double Vx, double Vy, double Radius, double Age, double Life);
public sealed record Streak(double X, double Y, int Direction, double Age, double Length);
public enum Motion { Drive, Burnout, Jump, Land, Turn }

public sealed class World
{
    static readonly double[] JumpPitch = [0, -12, -22, -5, 12, 7];
    static readonly double[] JumpContact = [49, 68, 91, 54, 66, 52];
    static readonly double[] LandContact = [68, 68, 49, 49, 46, 47];
    readonly Random random = new(42);
    List<Surface> surfaces = [];
    Surface? surface, target;
    double age, phaseAge, smokeClock, streakClock, boost, charge;
    double sx, sy, tx, ty, flightTime, startV, endV;
    double rememberedLeft, rememberedY;
    public double X { get; private set; }
    public double Velocity { get; private set; }
    public int Direction { get; private set; } = 1;
    public Motion Phase { get; private set; } = Motion.Drive;
    public bool Held { get; private set; }
    public bool EngineArmed { get; private set; }
    public bool Paused { get; set; }
    public double CarWidth { get; set; } = 194;
    public double Speed { get; set; } = 1;
    public double Scale => CarWidth / 440 * (surface?.Dpi ?? 96) / 96;
    public double WheelSpin { get; private set; }
    public long? SurfaceId => surface?.Id;
    public int Jumps { get; private set; }
    public int Turns { get; private set; }
    public List<Particle> Particles { get; } = [];
    public List<Streak> Streaks { get; } = [];
    double Half => 220 * Scale;
    double Unit => Scale / (194.0 / 440);
    double ClampX(Surface s, double x) => Math.Clamp(x, s.Left + Half + 10 * Unit, s.Right - Half - 10 * Unit);
    static double Approach(double from, double to, double step) => from < to ? Math.Min(to, from + step) : Math.Max(to, from - step);
    static double Smooth(double p) => p * p * (3 - 2 * p);

    public void SetSurfaces(IEnumerable<Surface> input)
    {
        surfaces = input.Where(s => s.Right - s.Left >= CarWidth * s.Dpi / 96 + 24 * s.Dpi / 96).ToList();
        if (surface is null) { Reset(); return; }
        var match = surfaces.Where(s => s.Id == surface.Id).OrderBy(s => Math.Abs(s.Left - rememberedLeft)).FirstOrDefault();
        if (Phase == Motion.Jump)
        {
            // Do not move the take-off point mid-flight. Revalidate the landing window.
            if (target is not null)
            {
                var next = surfaces.Where(s => s.Id == target.Id).OrderBy(s => Math.Abs(s.Left - target.Left)).FirstOrDefault();
                if (next is not null) { target = next; tx = ClampFor(next, tx); ty = next.Y - 47 * Scale; }
                else if (surfaces.Count > 0)
                {
                    target = surfaces.OrderBy(s => Math.Abs(s.Y - ty) + Math.Abs((s.Left + s.Right) / 2 - tx)).First();
                    tx = ClampFor(target, tx); ty = target.Y - 47 * Scale;
                }
                else Reset();
            }
            return;
        }
        if (match is null) { Reset(); return; }
        X += match.Left - rememberedLeft;
        surface = match; rememberedLeft = match.Left; rememberedY = match.Y;
        X = ClampX(match, X);
    }

    double ClampFor(Surface s, double x)
    {
        double h = CarWidth * s.Dpi / 96 / 2 + 12 * s.Dpi / 96;
        return Math.Clamp(x, s.Left + h, s.Right - h);
    }

    public void Reset()
    {
        surface = surfaces.FirstOrDefault(s => !s.Floor) ?? surfaces.FirstOrDefault();
        target = null; Phase = Motion.Drive; phaseAge = 0; Velocity = 0; Direction = 1;
        Held = false; EngineArmed = false; boost = 0; charge = 0;
        Particles.Clear(); Streaks.Clear();
        if (surface is not null)
        {
            rememberedLeft = surface.Left; rememberedY = surface.Y;
            X = ClampX(surface, surface.Left + Half + 32 * Unit);
        }
    }

    public void Hold(bool held)
    {
        if (Paused && held) return;
        if (held == Held) return;
        Held = held;
        if (held && Phase == Motion.Drive) BeginBurnout();
        else if (!held && Phase == Motion.Burnout)
        {
            Phase = Motion.Drive; phaseAge = 0; boost = 3.4; Velocity = 0;
            for (int i = 0; i < 16; i++) Smoke();
        }
    }

    void BeginBurnout()
    {
        Phase = Motion.Burnout; phaseAge = 0; Velocity = 0; charge = 0; EngineArmed = true;
    }

    Surface? PickTarget()
    {
        if (surface is null) return null;
        double endpoint = Direction > 0 ? surface.Right - Half - 12 * Unit : surface.Left + Half + 12 * Unit;
        return surfaces.Where(s => s.Id != surface.Id && !s.Floor)
            .Select(s => new { S = s, X = ClampFor(s, endpoint + Direction * 210 * Unit) })
            .Where(p => Direction * (p.X - endpoint) >= 80 * Unit && Direction * (p.X - endpoint) <= 470 * Unit
                && Math.Abs(p.S.Y - surface.Y) <= 180 * Unit)
            .OrderBy(p => Math.Abs(p.X - endpoint) + Math.Abs(p.S.Y - surface.Y) * .7)
            .Select(p => p.S).FirstOrDefault();
    }

    public Ramp? CurrentRamp
    {
        get
        {
            if (surface is null || Phase is Motion.Jump or Motion.Land or Motion.Turn) return null;
            if (PickTarget() is null) return null;
            double end = Direction > 0 ? surface.Right - Half - 12 * Unit : surface.Left + Half + 12 * Unit;
            return new Ramp(end - Direction * 78 * Unit, surface.Y, 68 * Unit, 30 * Unit, Direction);
        }
    }

    double GroundAt(double x)
    {
        if (surface is null) return 0;
        var ramp = CurrentRamp;
        return ramp is null ? surface.Y : surface.Y - ramp.Height * Math.Clamp(Direction * (x - ramp.X) / ramp.Width, 0, 1);
    }

    (double Y, double Angle) Ground()
    {
        double f = GroundAt(X + Direction * 133 * Scale), r = GroundAt(X - Direction * 133 * Scale);
        return ((f + r) / 2 - 47 * Scale, Math.Atan2(f - r, 266 * Scale) * 180 / Math.PI);
    }

    void Fly(Surface next)
    {
        var ground = Ground(); target = next; sx = X; sy = ground.Y;
        tx = ClampFor(next, X + Direction * 260 * Unit); ty = next.Y - 47 * Scale;
        flightTime = boost > 0 ? .70 : 1.05;
        startV = Direction * Velocity; endV = Direction * (boost > 0 ? 210 : 135) * Unit * Speed;
        Phase = Motion.Jump; phaseAge = 0; Jumps++;
    }

    (double X, double Y, double P) Flight()
    {
        double p = Math.Clamp(phaseAge / flightTime, 0, 1), p2 = p * p, p3 = p2 * p;
        return ((2 * p3 - 3 * p2 + 1) * sx + (p3 - 2 * p2 + p) * startV * flightTime
            + (-2 * p3 + 3 * p2) * tx + (p3 - p2) * endV * flightTime,
            sy + (ty - sy) * p - 110 * Unit * 4 * p * (1 - p), p);
    }

    public Pose Pose
    {
        get
        {
            var ground = Ground();
            if (Phase == Motion.Jump)
            {
                var f = Flight(); int i = Math.Min(5, (int)(f.P * 6));
                return new("jump", i, f.X, f.Y, -14 + 28 * Smooth(f.P) - JumpPitch[i], Direction, 1);
            }
            if (Phase == Motion.Land)
            {
                int i = Math.Min(5, (int)(phaseAge / .34 * 6));
                return new("land", i, X, (surface?.Y ?? ty) - LandContact[i] * Scale, 0, Direction, 1);
            }
            if (Phase == Motion.Turn) return new("drive", 0, X, ground.Y, 0, Direction, Math.Cos(Math.PI * Math.Clamp(phaseAge / .48, 0, 1)));
            double angle = ground.Angle - (boost > 0 && phaseAge < .35 ? 3 * (1 - phaseAge / .35) : 0);
            double vibration = Phase == Motion.Burnout ? Math.Sin(age * 43) * .7 * Unit : 0;
            return new("drive", (int)(age * (Phase == Motion.Burnout ? 22 : 12)) % 6, X, ground.Y + vibration, angle, Direction, 1);
        }
    }

    void Smoke(bool impact = false)
    {
        var p = Pose;
        Particles.Add(new(p.X - Direction * 133 * Scale, p.Y + 47 * Scale - 3 * Unit,
            impact ? (random.NextDouble() - .5) * 160 * Unit : -Direction * (30 + random.NextDouble() * 35) * Unit,
            -(impact ? 12 : 20) * Unit - random.NextDouble() * 25 * Unit,
            (impact ? 4 : 7) * Unit + random.NextDouble() * 7 * Unit, 0,
            impact ? .6 : 1.4 + random.NextDouble()));
        if (Particles.Count > 120) Particles.RemoveAt(0);
    }

    public void Step(double seconds)
    {
        if (Paused || surface is null) return;
        for (double remaining = Math.Min(seconds, .25); remaining > .000001;)
        {
            double dt = Math.Min(remaining, 1.0 / 120); remaining -= dt; Tick(dt);
        }
    }

    void Tick(double dt)
    {
        age += dt; phaseAge += dt; boost = Math.Max(0, boost - dt);
        for (int i = Particles.Count - 1; i >= 0; i--)
        {
            var p = Particles[i];
            if (p.Age + dt >= p.Life) Particles.RemoveAt(i);
            else Particles[i] = p with { X = p.X + p.Vx * dt, Y = p.Y + p.Vy * dt, Radius = p.Radius + 14 * Unit * dt, Age = p.Age + dt };
        }
        for (int i = Streaks.Count - 1; i >= 0; i--)
        {
            var p = Streaks[i];
            if (p.Age + dt >= .3) Streaks.RemoveAt(i);
            else Streaks[i] = p with { X = p.X - p.Direction * 130 * Unit * dt, Age = p.Age + dt };
        }
        if (boost > 0 && Velocity > 150 * Unit && Phase is Motion.Drive or Motion.Jump)
        {
            streakClock += dt * 42;
            while (streakClock >= 1)
            {
                var p = Pose; streakClock--;
                Streaks.Add(new(p.X - Direction * 80 * Unit, p.Y - (30 + random.NextDouble() * 65) * Unit, Direction, 0, (20 + random.NextDouble() * 45) * Unit));
            }
        }
        if (Phase == Motion.Burnout)
        {
            charge = Math.Clamp(phaseAge / 1.8, 0, 1); WheelSpin += dt * (24 + charge * 10);
            smokeClock += dt * 32; while (smokeClock >= 1) { Smoke(); smokeClock--; } return;
        }
        if (Phase == Motion.Jump)
        {
            X = Flight().X; WheelSpin += dt * 9;
            if (phaseAge >= flightTime && target is not null)
            {
                surface = target; target = null; rememberedLeft = surface.Left; rememberedY = surface.Y;
                X = ClampX(surface, tx); Velocity = Math.Abs(endV); Phase = Motion.Land; phaseAge = 0;
                for (int i = 0; i < 12; i++) Smoke(true);
            }
            return;
        }
        if (Phase == Motion.Land)
        {
            X = ClampX(surface!, X + Direction * Velocity * dt);
            WheelSpin += Velocity / (47 * Scale) * dt;
            if (phaseAge >= .34) { Phase = Motion.Drive; phaseAge = 0; if (Held) BeginBurnout(); }
            return;
        }
        if (Phase == Motion.Turn)
        {
            if (phaseAge >= .48) { Direction *= -1; Turns++; Phase = Motion.Drive; phaseAge = 0; if (Held) BeginBurnout(); }
            return;
        }
        if (Held) { BeginBurnout(); return; }
        double endpoint = Direction > 0 ? surface!.Right - Half - 12 * Unit : surface!.Left + Half + 12 * Unit;
        double distance = Math.Max(0, Direction * (endpoint - X));
        var next = PickTarget();
        double goal = 165 * Unit * Speed * (boost > 0 ? 2.15 + .65 * charge : 1);
        if (next is null) goal = Math.Min(goal, Math.Sqrt(2 * 780 * Unit * distance));
        Velocity = Approach(Velocity, goal, (Velocity < goal ? boost > 0 ? 1000 : 240 : 800) * Unit * dt);
        X += Direction * Velocity * dt; WheelSpin += Velocity / (47 * Scale) * dt;
        if (next is not null && distance <= 65 * Unit) Fly(next);
        else if (Direction * (X - endpoint) >= 0 || (distance < 1.4 * Unit && Velocity < 17 * Unit))
        {
            X = endpoint; Velocity = 0; Phase = Motion.Turn; phaseAge = 0; EngineArmed = false; boost = 0;
        }
    }
}

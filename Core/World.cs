namespace Touareg.Core;

// All coordinates are physical desktop pixels, including negative monitor coordinates.
public record Surface(long Id, double Left, double Right, double Y, double Dpi = 96, bool Floor = false);
// Monitor bounds. Their outer edges are the walls the car bumps into.
public record Area(double Left, double Top, double Right, double Bottom, double Dpi = 96);
public record Pose(string Sequence, int Frame, double X, double Y, double Angle, int Direction, double Flip);
public record Ramp(double X, double Y, double Width, double Height, int Direction);
public sealed record Particle(double X, double Y, double Vx, double Vy, double Radius, double Age, double Life);
public sealed record Streak(double X, double Y, int Direction, double Age, double Length);
public enum Motion { Drive, Burnout, Jump, Land, Turn, Drag, Fall, Crash }

public sealed class World
{
    sealed record Plan(Surface Target, double Takeoff, double LandX);
    enum Edge { Turn, Wall, DriveOff, Link }

    static readonly double[] JumpPitch = [0, -12, -22, -5, 12, 7];
    static readonly double[] JumpContact = [49, 68, 91, 54, 66, 52];
    static readonly double[] LandContact = [68, 68, 49, 49, 46, 47];
    static readonly double[] TiltContact = [89, 49, 47, 65, 84, 45];
    static readonly int[] CrashFrames = [4, 4, 3, 1, 0, 2];
    const double Contact = 47, CrashTime = .7;
    readonly Random random = new(42);
    List<Surface> surfaces = [];
    List<Area> screens = [];
    Surface? surface, target;
    Plan? hop;
    double age, phaseAge, smokeClock, streakClock, boost, charge;
    double sx, sy, tx, ty, flightTime, startV, endV, arc;
    double rememberedLeft, dpi = 96;
    double fy, vx, vy, grabX, grabY, cursorX, cursorY, swing, swingV, airTime, shake, bounce, hopTimer = 2;
    bool driveOff, bounced;
    public double X { get; private set; }
    public double Velocity { get; private set; }
    public int Direction { get; private set; } = 1;
    public Motion Phase { get; private set; } = Motion.Drive;
    public bool Held { get; private set; }
    public bool Dragging => Phase == Motion.Drag;
    public bool EngineArmed { get; private set; }
    public bool Paused { get; set; }
    public double CarWidth { get; set; } = 194;
    public double Speed { get; set; } = 1;
    public double Scale => CarWidth / 440 * dpi / 96;
    public double WheelSpin { get; private set; }
    public long? SurfaceId => surface?.Id;
    public int Jumps { get; private set; }
    public int Turns { get; private set; }
    public int Crashes { get; private set; }
    public List<Particle> Particles { get; } = [];
    public List<Streak> Streaks { get; } = [];
    double Half => 220 * Scale;
    double Unit => Scale / (194.0 / 440);
    double ClampX(Surface s, double x) => Math.Clamp(x, s.Left + Half + 10 * Unit, s.Right - Half - 10 * Unit);
    static double Approach(double from, double to, double step) => from < to ? Math.Min(to, from + step) : Math.Max(to, from - step);
    static double Smooth(double p) => p * p * (3 - 2 * p);

    // Without known monitors (tests, early start) the floors stand in for them.
    List<Area> Screens => screens.Count > 0 ? screens
        : surfaces.Where(s => s.Floor).Select(s => new Area(s.Left - 8, -1e7, s.Right + 8, s.Y + 1, s.Dpi)).ToList();
    bool Covered(double x, double y)
    {
        var all = Screens;
        return all.Count == 0 || all.Any(a => x >= a.Left && x < a.Right && y >= a.Top && y < a.Bottom);
    }
    Area? ScreenAt(double x, double y) => Screens
        .OrderBy(a => Math.Max(0, Math.Max(a.Left - x, x - a.Right)) + Math.Max(0, Math.Max(a.Top - y, y - a.Bottom)))
        .FirstOrDefault();

    void Attach(Surface s) { surface = s; rememberedLeft = s.Left; dpi = s.Dpi; }
    Surface? Find(Surface s, double left) => surfaces.Where(x => x.Id == s.Id).OrderBy(x => Math.Abs(x.Left - left)).FirstOrDefault();

    // Bottom edges of neighbouring monitors: the taskbar continues on the next screen.
    static bool Adjacent(Surface s, Surface t, int direction) =>
        t.Floor && s.Floor && t.Id != s.Id && (direction > 0 ? Math.Abs(t.Left - s.Right) : Math.Abs(s.Left - t.Right)) <= 40;
    Surface? Link(Surface s, int direction) =>
        surfaces.FirstOrDefault(t => Adjacent(s, t, direction) && Math.Abs(t.Y - s.Y) <= 6);

    public void SetScreens(IEnumerable<Area> input) => screens = input.ToList();

    public void SetSurfaces(IEnumerable<Surface> input)
    {
        surfaces = input.Where(s => s.Right - s.Left >= CarWidth * s.Dpi / 96 + 24 * s.Dpi / 96).ToList();
        if (Phase is Motion.Drag or Motion.Fall) return;
        if (surface is null) { Reset(); return; }
        if (Phase == Motion.Jump)
        {
            // Do not move the take-off point mid-flight. Revalidate the landing window.
            if (target is not null)
            {
                var next = Find(target, target.Left);
                if (next is not null) { target = next; tx = ClampFor(next, tx); ty = next.Y - Contact * Scale; }
                else Drop();
            }
            return;
        }
        var match = Find(surface, rememberedLeft);
        // The window under the car closed, minimized or got covered: the car falls.
        if (match is null) { Drop(); return; }
        double shift = match.Left - rememberedLeft;
        X += shift; Attach(match);
        if (hop is not null) hop = hop with { Takeoff = hop.Takeoff + shift };
        if (X < match.Left - 24 || X > match.Right + 24) Drop();
    }

    double ClampFor(Surface s, double x)
    {
        double h = CarWidth * s.Dpi / 96 / 2 + 12 * s.Dpi / 96;
        return Math.Clamp(x, s.Left + h, s.Right - h);
    }

    public void Reset()
    {
        var start = surfaces.FirstOrDefault(s => !s.Floor) ?? surfaces.FirstOrDefault();
        surface = null; target = null; hop = null; Phase = Motion.Drive; phaseAge = 0; Velocity = 0; Direction = 1;
        Held = false; EngineArmed = false; boost = 0; charge = 0; shake = 0;
        Particles.Clear(); Streaks.Clear();
        if (start is not null)
        {
            Attach(start);
            X = ClampX(start, start.Left + Half + 32 * Unit);
        }
        Decide();
    }

    void Decide() { driveOff = random.NextDouble() < .4; }

    public void Hold(bool held)
    {
        if (Paused && held) return;
        if (held == Held) return;
        Held = held;
        if (held && Phase == Motion.Drive) BeginBurnout();
        else if (!held && Phase == Motion.Drag) Throw();
        else if (!held && Phase == Motion.Burnout)
        {
            Phase = Motion.Drive; phaseAge = 0; boost = 3.4; Velocity = 0;
            for (int i = 0; i < 16; i++) Smoke();
        }
    }

    // Where the cursor picked up the car, relative to its pose.
    public void Grab(double x, double y)
    {
        var p = Pose; grabX = x - p.X; grabY = y - p.Y; cursorX = x; cursorY = y;
    }

    public void BeginDrag()
    {
        if (!Held || Paused || Phase == Motion.Drag) return;
        var p = Pose; X = p.X; fy = p.Y;
        surface = null; target = null; hop = null;
        Phase = Motion.Drag; phaseAge = 0; vx = vy = 0; swing = swingV = 0;
        Velocity = 0; boost = 0; charge = 0; EngineArmed = false;
    }

    public void DragTo(double x, double y) { cursorX = x; cursorY = y; }

    void Throw()
    {
        vx = Math.Clamp(vx, -3200 * Unit, 3200 * Unit); vy = Math.Clamp(vy, -3200 * Unit, 3200 * Unit);
        if (Math.Abs(vx) > 60 * Unit) Direction = Math.Sign(vx);
        Phase = Motion.Fall; phaseAge = 0; airTime = 0; bounced = false;
    }

    // Leave the current support and continue ballistically from the present pose.
    void Drop()
    {
        var p = Pose;
        if (Phase == Motion.Jump)
        {
            var a = Flight(phaseAge - 1.0 / 240); var b = Flight(phaseAge);
            vx = (b.X - a.X) * 240; vy = (b.Y - a.Y) * 240;
        }
        else { vx = Phase is Motion.Drive or Motion.Land ? Direction * Velocity : 0; vy = 0; }
        X = p.X; fy = p.Y; surface = null; target = null; hop = null;
        Phase = Motion.Fall; phaseAge = 0; airTime = 0; bounced = false;
    }

    void BeginBurnout()
    {
        Phase = Motion.Burnout; phaseAge = 0; Velocity = 0; charge = 0; EngineArmed = true;
    }

    // A ramp at the end of the support towards a window ahead (or a higher/lower neighbouring screen).
    Plan? EdgeJump()
    {
        if (surface is null) return null;
        var s = surface;
        if (Link(s, Direction) is not null) return null;
        double endpoint = Direction > 0 ? s.Right - Half - 12 * Unit : s.Left + Half + 12 * Unit;
        return surfaces.Where(t => t.Id != s.Id && (!t.Floor || Adjacent(s, t, Direction)))
            .Select(t => new Plan(t, endpoint, ClampFor(t, endpoint + Direction * 260 * Unit)))
            .Where(p =>
            {
                double dx = Direction * (p.LandX - endpoint), dy = p.Target.Y - s.Y;
                return dx >= 60 * Unit && dx <= 620 * Unit && dy >= -420 * Unit && dy <= 520 * Unit;
            })
            .OrderBy(p => Math.Abs(p.LandX - endpoint) + Math.Abs(p.Target.Y - s.Y) * .7)
            .FirstOrDefault();
    }

    // A ramp in the middle of the support towards a higher window, e.g. from the taskbar.
    Plan? Hop()
    {
        if (surface is null) return null;
        var s = surface; double reach = 300 * Unit;
        double lo = s.Left + Half + 12 * Unit, hi = s.Right - Half - 12 * Unit;
        List<Plan> options = [];
        foreach (var t in surfaces)
        {
            if (t.Id == s.Id || t.Floor) continue;
            double rise = s.Y - t.Y;
            if (rise < 90 * Unit || rise > 520 * Unit) continue;
            double h = CarWidth * t.Dpi / 96 / 2 + 12 * t.Dpi / 96;
            if (t.Left + h > t.Right - h) continue;
            double land = Math.Clamp(X + Direction * (160 * Unit + reach), t.Left + h, t.Right - h), take = land - Direction * reach;
            if (Direction * (take - X) < 140 * Unit || take < lo - 1 || take > hi + 1) continue;
            options.Add(new(t, take, land));
        }
        return options.Count == 0 ? null : options[random.Next(options.Count)];
    }

    Plan? Validate(Plan? p)
    {
        if (p is null || surface is null) return null;
        var t = Find(p.Target, p.Target.Left);
        if (t is null || Direction * (p.Takeoff - X) < 0) return null;
        double land = ClampFor(t, p.LandX + t.Left - p.Target.Left);
        if (Direction * (land - p.Takeoff) < 60 * Unit) return null;
        return p with { Target = t, LandX = land };
    }

    Plan? CurrentPlan => Validate(hop) ?? EdgeJump();

    Edge EdgeMode(Surface s)
    {
        if (Link(s, Direction) is not null) return Edge.Link;
        double edge = Direction > 0 ? s.Right : s.Left;
        // Nothing beyond this edge: it is the outer wall of the desktop.
        if (!Covered(edge + Direction * 24, s.Y - 20 * Unit)) return Edge.Wall;
        return s.Floor || driveOff ? Edge.DriveOff : Edge.Turn;
    }

    public Ramp? CurrentRamp
    {
        get
        {
            if (surface is null || Phase is not (Motion.Drive or Motion.Burnout)) return null;
            if (CurrentPlan is not { } plan) return null;
            return new Ramp(plan.Takeoff - Direction * 78 * Unit, surface.Y, 68 * Unit, 30 * Unit, Direction);
        }
    }

    double GroundAt(double x)
    {
        if (surface is null) return fy + Contact * Scale;
        var ramp = CurrentRamp;
        return ramp is null ? surface.Y : surface.Y - ramp.Height * Math.Clamp(Direction * (x - ramp.X) / ramp.Width, 0, 1);
    }

    (double Y, double Angle) Ground()
    {
        double f = GroundAt(X + Direction * 133 * Scale), r = GroundAt(X - Direction * 133 * Scale);
        return ((f + r) / 2 - Contact * Scale, Math.Atan2(f - r, 266 * Scale) * 180 / Math.PI);
    }

    void Fly(Plan plan)
    {
        var ground = Ground(); target = plan.Target; sx = X; sy = ground.Y;
        tx = plan.LandX; ty = target.Y - Contact * Scale;
        double rise = Math.Max(0, sy - ty);
        flightTime = (boost > 0 ? .70 : 1.05) + rise / (520 * Unit) * .35;
        arc = 110 * Unit + rise * .35;
        startV = Direction * Velocity; endV = Direction * (boost > 0 ? 210 : 135) * Unit * Speed;
        Phase = Motion.Jump; phaseAge = 0; Jumps++; hop = null;
    }

    (double X, double Y, double P) Flight(double time)
    {
        double p = Math.Clamp(time / flightTime, 0, 1), p2 = p * p, p3 = p2 * p;
        return ((2 * p3 - 3 * p2 + 1) * sx + (p3 - 2 * p2 + p) * startV * flightTime
            + (-2 * p3 + 3 * p2) * tx + (p3 - p2) * endV * flightTime,
            sy + (ty - sy) * p - arc * 4 * p * (1 - p), p);
    }

    public Pose Pose
    {
        get
        {
            double jolt = shake > 0 ? Math.Sin(age * 80) * 6 * Unit * shake / .3 : 0;
            if (Phase == Motion.Drag)
                return new("jump", 3, X, fy, Direction * swing - JumpPitch[3], Direction, 1);
            if (Phase == Motion.Fall)
            {
                int i = vy < -250 * Unit ? 1 : vy < 250 * Unit ? 3 : 4;
                double pitch = Math.Clamp(Math.Atan2(vy, Math.Abs(vx) + 400 * Unit) * 180 / Math.PI * .6, -28, 28);
                return new("jump", i, X + jolt, fy, pitch - JumpPitch[i], Direction, 1);
            }
            if (Phase == Motion.Jump)
            {
                var f = Flight(phaseAge); int i = Math.Min(5, (int)(f.P * 6));
                return new("jump", i, f.X, f.Y, -14 + 28 * Smooth(f.P) - JumpPitch[i], Direction, 1);
            }
            if (Phase == Motion.Land)
            {
                int i = Math.Min(5, (int)(phaseAge / .34 * 6));
                return new("land", i, X + jolt, (surface?.Y ?? ty) - LandContact[i] * Scale, 0, Direction, 1);
            }
            if (Phase == Motion.Crash)
            {
                int i = CrashFrames[Math.Min(5, (int)(phaseAge / CrashTime * 6))];
                double k = Math.Max(0, 1 - phaseAge / .45);
                return new("tilt", i, X + Math.Sin(phaseAge * 75) * 5 * Unit * k, surface!.Y - TiltContact[i] * Scale, 0, Direction, 1);
            }
            var ground = Ground();
            if (Phase == Motion.Turn) return new("drive", 0, X, ground.Y, 0, Direction, Math.Cos(Math.PI * Math.Clamp(phaseAge / .48, 0, 1)));
            double angle = ground.Angle - (boost > 0 && phaseAge < .35 ? 3 * (1 - phaseAge / .35) : 0);
            double vibration = Phase == Motion.Burnout ? Math.Sin(age * 43) * .7 * Unit : 0;
            return new("drive", (int)(age * (Phase == Motion.Burnout ? 22 : 12)) % 6, X, ground.Y + vibration, angle, Direction, 1);
        }
    }

    void Smoke(bool impact = false)
    {
        var p = Pose;
        Puff(p.X - Direction * 133 * Scale, p.Y + Contact * Scale - 3 * Unit, impact);
    }

    void Puff(double x, double y, bool impact, double drift = 0)
    {
        Particles.Add(new(x, y,
            impact ? (random.NextDouble() - .5) * 160 * Unit + drift : -Direction * (30 + random.NextDouble() * 35) * Unit,
            -(impact ? 12 : 20) * Unit - random.NextDouble() * 25 * Unit,
            (impact ? 4 : 7) * Unit + random.NextDouble() * 7 * Unit, 0,
            impact ? .6 : 1.4 + random.NextDouble()));
        if (Particles.Count > 120) Particles.RemoveAt(0);
    }

    public void Step(double seconds)
    {
        if (Paused || (surface is null && Phase is not (Motion.Drag or Motion.Fall))) return;
        for (double remaining = Math.Min(seconds, .25); remaining > .000001;)
        {
            double dt = Math.Min(remaining, 1.0 / 120); remaining -= dt; Tick(dt);
            if (surface is null && Phase is not (Motion.Drag or Motion.Fall)) return;
        }
    }

    void Tick(double dt)
    {
        age += dt; phaseAge += dt; boost = Math.Max(0, boost - dt); shake = Math.Max(0, shake - dt);
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
        if (Phase == Motion.Drag) { DragTick(dt); return; }
        if (Phase == Motion.Fall) { FallTick(dt); return; }
        if (Phase == Motion.Burnout)
        {
            charge = Math.Clamp(phaseAge / 1.8, 0, 1); WheelSpin += dt * (24 + charge * 10);
            smokeClock += dt * 32; while (smokeClock >= 1) { Smoke(); smokeClock--; } return;
        }
        if (Phase == Motion.Jump)
        {
            X = Flight(phaseAge).X; WheelSpin += dt * 9;
            if (phaseAge >= flightTime && target is not null)
            {
                Attach(target); target = null;
                X = ClampX(surface!, tx); Velocity = Math.Abs(endV); Phase = Motion.Land; phaseAge = 0;
                hopTimer = 2 + random.NextDouble() * 4;
                for (int i = 0; i < 12; i++) Smoke(true);
            }
            return;
        }
        if (Phase == Motion.Land)
        {
            Velocity = Approach(Velocity, Math.Min(Velocity, 210 * Unit * Speed), 600 * Unit * dt);
            X = Math.Clamp(X + Direction * Velocity * dt, surface!.Left, surface.Right);
            WheelSpin += Velocity / (Contact * Scale) * dt;
            if (phaseAge >= .34) { Phase = Motion.Drive; phaseAge = 0; Decide(); if (Held) BeginBurnout(); }
            return;
        }
        if (Phase == Motion.Crash)
        {
            X -= Direction * bounce * dt; bounce = Approach(bounce, 0, 700 * Unit * dt);
            if (phaseAge >= CrashTime) { Phase = Motion.Turn; phaseAge = 0; }
            return;
        }
        if (Phase == Motion.Turn)
        {
            if (phaseAge >= .48) { Direction *= -1; Turns++; Phase = Motion.Drive; phaseAge = 0; Decide(); if (Held) BeginBurnout(); }
            return;
        }
        if (Held) { BeginBurnout(); return; }
        DriveTick(dt);
    }

    void DriveTick(double dt)
    {
        var s = surface!;
        double edge = Direction > 0 ? s.Right : s.Left;
        if (Direction * (X - edge) > 0)
        {
            // Rolled past the end: continue on the next screen's taskbar or fall down.
            if (Link(s, Direction) is { } link) { Attach(link); s = link; edge = Direction > 0 ? s.Right : s.Left; }
            else { Drop(); return; }
        }
        hopTimer -= dt;
        if (hop is null && hopTimer <= 0)
        {
            // Only sometimes take a ramp up; otherwise look again later.
            hop = random.NextDouble() < .55 ? Hop() : null;
            hopTimer = hop is null ? 1.5 : 0;
        }
        hop = Validate(hop);
        var plan = hop ?? EdgeJump();
        var mode = plan is null ? EdgeMode(s) : Edge.Turn;
        double endpoint = Direction > 0 ? s.Right - Half - 12 * Unit : s.Left + Half + 12 * Unit;
        double distance = Math.Max(0, Direction * (endpoint - X));
        double goal = 165 * Unit * Speed * (boost > 0 ? 2.15 + .65 * charge : 1);
        if (plan is null && mode == Edge.Turn) goal = Math.Min(goal, Math.Sqrt(2 * 780 * Unit * distance));
        Velocity = Approach(Velocity, goal, (Velocity < goal ? boost > 0 ? 1000 : 240 : 800) * Unit * dt);
        X += Direction * Velocity * dt; WheelSpin += Velocity / (Contact * Scale) * dt;
        if (plan is not null)
        {
            if (Direction * (plan.Takeoff - X) <= 65 * Unit) Fly(plan);
        }
        else if (mode == Edge.Wall)
        {
            if (Direction * (X + Direction * Half - edge) >= 0)
            {
                X = edge - Direction * Half;
                if (Velocity > 70 * Unit) Crash(); else StartTurn();
            }
        }
        else if (mode == Edge.Turn && (Direction * (X - endpoint) >= 0 || (distance < 1.4 * Unit && Velocity < 17 * Unit)))
            StartTurn();
    }

    void StartTurn() { Velocity = 0; Phase = Motion.Turn; phaseAge = 0; EngineArmed = false; boost = 0; }

    void Crash()
    {
        Crashes++; bounce = Math.Max(60 * Unit, Velocity * .45);
        double front = X + Direction * Half;
        for (int i = 0; i < 14; i++) Puff(front, surface!.Y - (10 + random.NextDouble() * 40) * Unit, true, -Direction * 60 * Unit);
        StartTurn(); Phase = Motion.Crash;
    }

    void DragTick(double dt)
    {
        double nx = cursorX - grabX, ny = cursorY - grabY, k = Math.Min(1, dt * 14);
        vx += ((nx - X) / dt - vx) * k; vy += ((ny - fy) / dt - vy) * k;
        X = nx; fy = ny;
        if (screens.Count > 0 && ScreenAt(X, fy) is { } screen) dpi = screen.Dpi;
        // The car dangles from the cursor and swings against the direction of motion.
        double want = Math.Clamp(-vx * .012 / Unit, -24, 24);
        swingV += ((want - swing) * 90 - swingV * 9) * dt; swing += swingV * dt;
        WheelSpin += dt * 2;
    }

    void FallTick(double dt)
    {
        airTime += dt; WheelSpin += dt * 6;
        vy += 2300 * Unit * dt;
        double before = fy + Contact * Scale;
        X += vx * dt; fy += vy * dt;
        Walls();
        double bottom = fy + Contact * Scale;
        if (vy > 0)
        {
            var hit = surfaces.Where(s => X >= s.Left && X <= s.Right && s.Y >= before - .5 && s.Y <= bottom)
                .OrderBy(s => s.Y).FirstOrDefault()
                // Safety net: never sink through the taskbar.
                ?? surfaces.Where(s => s.Floor && X >= s.Left - 24 && X <= s.Right + 24 && bottom > s.Y + 40 * Unit).OrderBy(s => s.Y).FirstOrDefault();
            if (hit is not null) { Touchdown(hit); return; }
        }
        if (airTime > 6) Reset();
    }

    void Walls()
    {
        if (ScreenAt(X, fy) is not { } a) return;
        double top = 300 * Scale;
        if (X + Half > a.Right && !Covered(a.Right + 1, fy))
        {
            X = a.Right - Half;
            if (vx > 0) { Hit(vx, 1); vx = -vx * .45; }
        }
        if (X - Half < a.Left && !Covered(a.Left - 1, fy))
        {
            X = a.Left + Half;
            if (vx < 0) { Hit(-vx, -1); vx = -vx * .45; }
        }
        if (fy - top < a.Top && !Covered(X, a.Top - 1)) { fy = a.Top + top; if (vy < 0) vy = -vy * .3; }
    }

    void Hit(double speed, int side)
    {
        if (speed < 250 * Unit) return;
        Crashes++; shake = .3;
        for (int i = 0; i < 12; i++) Puff(X + side * Half, fy - random.NextDouble() * 60 * Unit, true, -side * 80 * Unit);
    }

    void Touchdown(Surface s)
    {
        fy = s.Y - Contact * Scale;
        for (int i = 0; i < (vy > 900 * Unit ? 16 : 10); i++) Puff(X + (random.NextDouble() - .5) * 2 * Half, s.Y - 3 * Unit, true);
        if (vy > 1300 * Unit && !bounced) { vy = -vy * .28; vx *= .7; bounced = true; shake = .2; return; }
        Attach(s); X = Math.Clamp(X, s.Left, s.Right);
        Velocity = Math.Sign(vx) == Direction ? Math.Min(Math.Abs(vx), 420 * Unit) : 0;
        Phase = Motion.Land; phaseAge = 0; hopTimer = 2 + random.NextDouble() * 4;
    }
}

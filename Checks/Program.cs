using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Touareg.Core;

int checks = 0;
void Assert(bool test, string what) { if (!test) throw new Exception(what); checks++; Console.WriteLine("PASS " + what); }
Assert(InstallPolicy.ChooseVersion("1.0.0", "1.0.1") == "1.0.1", "A newer downloaded installer upgrades the earlier local build");
Assert(InstallPolicy.ChooseVersion("1.2.0", "1.0.1") == "1.2.0", "An older launcher preserves the newer installed version");
Assert(InstallPolicy.NeedsChannel("{\"url\":\"\"}", "{\"url\":\"https://github.com/release/update.json\"}"), "Migrates the empty pre-publication update channel");
Assert(!InstallPolicy.NeedsChannel("{\"url\":\"https://example.com/update.json\"}", "{\"url\":\"https://github.com/release/update.json\"}"), "Preserves an already configured update channel");
void Tick(World w, double seconds) { for (int i = 0; i < (int)(seconds * 120); i++) w.Step(1.0 / 120); }
Surface[] windows = [new(1, 40, 430, 320), new(2, 550, 980, 270), new(-1, 0, 1100, 800, Floor: true)];
var world = new World(); world.SetSurfaces(windows);
Assert(world.SurfaceId == 1 && world.Direction == 1, "Starts on the first visible window");
world.Hold(true); Tick(world, 1.8);
Assert(world.Phase == Motion.Burnout && world.Velocity == 0 && world.EngineArmed && world.Particles.Count > 20, "Hold: stationary burnout, smoke and engine");
world.Hold(false); Tick(world, .2);
Assert(world.Velocity > 150 && world.EngineArmed && !world.Held, "Release: rapid acceleration with engine still running");
Tick(world, .08); Assert(world.Streaks.Count > 0, "Acceleration produces speed streaks");
bool landed = false, stopped = false, reversed = false;
for (int i = 0; i < 3600; i++)
{
    world.Step(1.0 / 120);
    var p = world.Pose;
    AssertFinite(p);
    if (world.Phase == Motion.Land) landed = true;
    if (world.Phase == Motion.Turn && !stopped)
    {
        Assert(world.Velocity == 0 && !world.EngineArmed, "At endpoint: stops completely and disarms sound"); stopped = true;
    }
    if (world.Direction == -1) reversed = true;
}
Assert(world.Jumps >= 4 && landed && stopped && reversed && world.Turns >= 2, "Continuous round trips: jump, land, stop, reverse");
void AssertFinite(Pose p) { if (!double.IsFinite(p.X + p.Y + p.Angle + p.Flip)) throw new Exception("Nonfinite pose"); }

var moved = new World(); moved.SetSurfaces([new(20, -1200, -300, 400, 144)]);
Tick(moved, .4); double before = moved.X;
moved.SetSurfaces([new(20, -1000, -100, 500, 144)]);
Assert(Math.Abs(moved.X - before - 200) < .001, "Follows a moved window on a negative-coordinate, 150% DPI monitor");
Assert(Math.Abs(moved.Scale - 194.0 / 440 * 1.5) < .001, "Physical car size follows monitor DPI");
moved.SetSurfaces([new(-2, -1400, -100, 900, 144, true)]);
Assert(moved.Phase == Motion.Fall && moved.SurfaceId is null, "Closed support window drops the car");
Tick(moved, 1.2);
Assert(moved.SurfaceId == -2 && moved.Phase == Motion.Drive && !moved.EngineArmed, "Falling car lands on the desktop edge below");
moved.Paused = true; before = moved.X; Tick(moved, 1); moved.Hold(true);
Assert(moved.X == before && !moved.Held, "Pause prevents motion and new burnout");
moved.Paused = false; moved.CarWidth = 280; moved.SetSurfaces([new(7, 0, 220, 300), new(-1, 0, 1500, 1000, Floor: true)]);
Tick(moved, 1.5);
Assert(moved.SurfaceId == -1, "Skips support narrower than the selected pet size");

var flight = new World(); flight.SetSurfaces(windows);
for (int i = 0; i < 600 && flight.Phase != Motion.Jump; i++) flight.Step(1.0 / 120);
Assert(flight.Phase == Motion.Jump, "Finds a neighbouring window and takes off");
flight.SetSurfaces([new(-1, 0, 1600, 800, Floor: true)]); Tick(flight, 2);
Assert(flight.SurfaceId == -1 && flight.Phase != Motion.Jump, "Closing landing window during flight safely retargets desktop");
flight.Reset(); Assert(!flight.EngineArmed && !flight.Held && flight.Particles.Count == 0, "Reset clears sound, hold and effects");

Area monitor = new(0, 0, 1920, 1080);
Surface taskbar = new(-1, 8, 1912, 1040, Floor: true);
var drag = new World(); drag.SetScreens([monitor]); drag.SetSurfaces([new(1, 200, 900, 500), taskbar]);
Tick(drag, .3); var grab = drag.Pose;
drag.Grab(grab.X + 10, grab.Y - 20); drag.Hold(true); drag.BeginDrag();
Assert(drag.Dragging && !drag.EngineArmed && drag.SurfaceId is null && drag.Particles.Count == 0, "Dragging lifts the car off its window instead of a burnout");
for (int i = 1; i <= 30; i++) { drag.DragTo(grab.X + 10 + i * 12, grab.Y - 20 - i * 10); drag.Step(1.0 / 60); AssertFinite(drag.Pose); }
var carried = drag.Pose;
Assert(Math.Abs(carried.X - (grab.X + 360)) < .001 && Math.Abs(carried.Y - (grab.Y - 300)) < .001, "Carried car stays under the cursor at the grab point");
Assert((carried.Angle - 5) * carried.Direction < -3, "Carried car swings against the motion");
drag.Hold(false);
Assert(drag.Phase == Motion.Fall && !drag.Held, "Releasing throws the car");
for (int i = 0; i < 360 && drag.SurfaceId is null; i++) { drag.Step(1.0 / 120); AssertFinite(drag.Pose); }
Assert(drag.SurfaceId is not null, "Thrown car lands on a window or the taskbar");

var wall = new World(); wall.SetScreens([monitor]); wall.SetSurfaces([taskbar]);
var start = wall.Pose; wall.Grab(start.X, start.Y); wall.Hold(true); wall.BeginDrag();
for (int i = 1; i <= 20; i++) { wall.DragTo(start.X + i * 45, start.Y - 400); wall.Step(1.0 / 60); }
wall.Hold(false); double farthest = 0;
for (int i = 0; i < 600; i++) { wall.Step(1.0 / 120); farthest = Math.Max(farthest, wall.Pose.X); AssertFinite(wall.Pose); }
Assert(wall.Crashes >= 1 && farthest < 1920 && wall.SurfaceId == -1, "Thrown car hits the monitor wall, bounces back and lands");

var bump = new World(); bump.SetScreens([monitor]); bump.SetSurfaces([taskbar]);
bool crashed = false; farthest = 0;
for (int i = 0; i < 120 * 14 && bump.Turns == 0; i++)
{
    bump.Step(1.0 / 120); farthest = Math.Max(farthest, bump.Pose.X);
    if (bump.Phase == Motion.Crash) crashed = true;
}
Assert(crashed && bump.Crashes == 1 && bump.Direction == -1 && farthest < 1912, "Drives into the screen wall, crashes and turns back");

var multi = new World(); multi.SetScreens([monitor, new(1920, 0, 3840, 1080, 144)]);
multi.SetSurfaces([taskbar, new(-2, 1928, 3832, 1040, 144, true)]);
for (int i = 0; i < 120 * 20 && multi.SurfaceId != -2; i++) { multi.Step(1.0 / 120); AssertFinite(multi.Pose); }
Assert(multi.SurfaceId == -2 && multi.Turns == 0 && multi.Crashes == 0 && multi.Jumps == 0, "Drives along the taskbar onto the next monitor without stopping");
Assert(Math.Abs(multi.Scale - 194.0 / 440 * 1.5) < .001, "Car takes the DPI of the monitor it crossed to");
for (int i = 0; i < 120 * 40 && multi.SurfaceId != -1; i++) multi.Step(1.0 / 120);
Assert(multi.SurfaceId == -1 && multi.Crashes == 1, "Bounces off the far wall and drives back to the first monitor");

var offset = new World(); offset.SetScreens([monitor, new(1920, 200, 3840, 1280)]);
offset.SetSurfaces([taskbar, new(-2, 1928, 3832, 1240, Floor: true)]);
for (int i = 0; i < 120 * 20 && offset.SurfaceId != -2; i++) { offset.Step(1.0 / 120); AssertFinite(offset.Pose); }
Assert(offset.SurfaceId == -2 && offset.Turns == 0 && offset.Crashes == 0, "Reaches a lower taskbar on a shifted monitor");

var climb = new World(); climb.SetScreens([monitor]); climb.SetSurfaces([taskbar]);
climb.SetSurfaces([new(5, 500, 1400, 760), taskbar]);
bool climbed = false, fellOff = false; long? previous = climb.SurfaceId;
for (int i = 0; i < 120 * 90 && !(climbed && fellOff); i++)
{
    climb.Step(1.0 / 120); AssertFinite(climb.Pose);
    if (climb.SurfaceId == 5) climbed = true;
    if (previous == 5 && climb.Phase == Motion.Fall) fellOff = true;
    previous = climb.SurfaceId;
}
Assert(climbed && climb.Jumps >= 1, "Takes a ramp up from the taskbar onto a window");
Assert(fellOff, "Sometimes drives off the end of a window and drops down");

var click = new World(); click.SetSurfaces(windows); Tick(click, .5);
click.Hold(true); Tick(click, .05); int puffsBefore = click.Particles.Count; click.Hold(false);
Assert(click.Phase == Motion.Drive && click.EngineArmed && click.Particles.Count > puffsBefore, "A quick click launches without a burnout first");
var still = new World(); still.SetSurfaces(windows); Tick(still, .5); still.Hold(true); Tick(still, .1);
var frameA = still.Pose.Frame; Tick(still, .03);
Assert(still.Phase == Motion.Drive && still.Velocity == 0 && !still.EngineArmed && still.Pose.Frame == frameA, "Pressing brakes under the cursor; a stopped car does not animate");
Tick(still, .1); Assert(still.Phase == Motion.Burnout, "Holding still for a moment starts the burnout");

var unfold = new World(); unfold.SetSurfaces(windows);
var heights = new List<double>();
for (int i = 0; i < 60; i++) { unfold.Step(1.0 / 120); heights.Add(unfold.CurrentRamp?.Height ?? 0); }
Assert(heights[0] < heights[20] && heights[20] < heights[^1] && Math.Abs(heights[^1] - 30) < .001, "Ramp unfolds instead of popping in");
for (int i = 0; i < 600 && unfold.Phase != Motion.Jump; i++) unfold.Step(1.0 / 120);
double takeoff = unfold.CurrentRamp?.Height ?? 0; Tick(unfold, .4);
Assert(takeoff > 0 && unfold.CurrentRamp is null, "Ramp folds away after take-off");
for (int i = 0; i < 600 && unfold.Phase != Motion.Land; i++) unfold.Step(1.0 / 120);
Assert(unfold.Velocity >= 165 - .001 && unfold.Impacts.Any(i => !i.Metal), "Lands at cruising speed with a thud");

var turn = new World(); turn.SetSurfaces([new(1, 0, 900, 400), new(-1, 0, 1600, 900, Floor: true)]);
for (int i = 0; i < 120 * 10 && turn.Phase != Motion.Turn; i++) turn.Step(1.0 / 120);
double squashed = 0, lowest = 0; int facing = turn.Direction;
while (turn.Phase == Motion.Turn) { turn.Step(1.0 / 120); var p = turn.Pose; if (Math.Abs(p.Flip) < .5) squashed += 1.0 / 120; lowest = Math.Min(lowest, p.Y - (400 - 47 * turn.Scale)); }
Assert(turn.Direction == -facing && squashed <= .1 && lowest < -5, "Turns with a quick hop instead of a slow paper flip");

Assert(bump.Impacts.Any(i => i.Metal) && wall.Impacts.Any(i => i.Metal), "Wall crashes queue a crash sound");

string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Assets"));

using (var glb = File.OpenRead(Path.Combine(root, "car/porsche-930.glb")))
{
    var car = CarModel.Load(glb);
    var parts = new[] { car.Body, car.Glass }.Concat(car.Wheels).ToList();
    Assert(parts.All(p => p.VertexCount > 100 && p.Indices.Length % 3 == 0 && p.Indices.All(i => i < p.VertexCount) && p.Vertices.All(float.IsFinite))
        && car.Body.Ranges.Any(r => r.Material == MaterialKind.Paint) && car.Glass.Ranges.All(r => r.Material == MaterialKind.Glass),
        "Car model loads: body with paint, glass and four wheels with valid triangles");
    Assert(car.Wheels.Take(2).All(w => w.Origin.Z > 1) && car.Wheels.Skip(2).All(w => w.Origin.Z < -1)
        && car.Wheels[0].Origin.X > 0 && car.Wheels[1].Origin.X < 0 && Math.Abs(car.Wheels.Average(w => w.Origin.Y)) < .01,
        "Wheels sit front/rear and left/right around the axle-height origin");
    Assert(car.WheelRadius is > .3f and < .45f && car.Wheelbase is > 2.4f and < 2.8f && car.Length is > 4.5f and < 5.2f
        && new[] { car.Albedo, car.MetalRough }.All(t => t.Length > 10000 && (t[0] == 0x89 && t[1] == 0x50 || t[0] == 0xFF && t[1] == 0xD8)),
        "Car proportions and embedded PNG/JPEG textures look like a 911");
    var albedoPixels = Png.Decode(car.Albedo); var orm = Png.Decode(car.MetalRough);
    int Count(byte[] bgra, Func<byte, byte, byte, bool> test) { int n = 0; for (int i = 0; i < bgra.Length; i += 4) if (test(bgra[i], bgra[i + 1], bgra[i + 2])) n++; return n; }
    Assert(albedoPixels.Width == 2048 && orm.Width == 1024 && Count(orm.Bgra, (b, g, r) => g is > 215 and < 235 && b < 10) > 1000
        && Count(orm.Bgra, (b, g, r) => b > 240 && g is > 70 and < 85) > 1000,
        "Textures decode without colour management: matte tyres (roughness .88) and metal rims (metallic 1) survive");
}

var drive3d = new World(); drive3d.SetScreens([monitor]); drive3d.SetSurfaces([new(5, 300, 1500, 700), taskbar]);
var chassis = new Chassis(2.6, .36);
double ppu = drive3d.Scale * 440 / 4.88, minPitch = 0, maxLift = 0, minLift = 0, burnRear = 0, burnFront = 0;
bool sawAir = false, sawTurn = false, passed90 = false, compressedAfterLanding = false;
Motion previousPhase = drive3d.Phase;
for (int i = 0; i < 120 * 60; i++)
{
    drive3d.Step(1.0 / 120);
    var s = drive3d.State; var body = chassis.Update(s, 1.0 / 120, ppu, drive3d.WheelSpin);
    if (!double.IsFinite(s.X + s.Ground + s.Pitch + s.Yaw + s.Speed + body.Lift + body.Pitch + body.Roll + body.FrontSpin + body.RearSpin + body.Steer))
        throw new Exception("Nonfinite 3D state");
    if (!s.Grounded) sawAir = true;
    if (s.Phase == Motion.Turn) { sawTurn = true; if (Math.Abs(s.Yaw - 90) < 8) passed90 = true; }
    if (s.Phase is Motion.Drive && drive3d.Velocity > 0) minPitch = Math.Min(minPitch, body.Pitch);
    if (previousPhase is Motion.Jump or Motion.Fall && s.Grounded) compressedAfterLanding = true;
    if (compressedAfterLanding) minLift = Math.Min(minLift, body.Lift);
    maxLift = Math.Max(maxLift, Math.Abs(body.Lift));
    previousPhase = s.Phase;
}
Assert(sawAir && sawTurn && passed90, "3D state: flies, and the turnaround swings the tail round through 90°");
Assert(minPitch < -.5 && minLift < -.02 && maxLift < .25, "Chassis dives when braking, compresses on landing and stays within its travel");
var burn = new World(); burn.SetSurfaces(windows); Tick(burn, .5);
var burnChassis = new Chassis(2.6, .36);
var before3d = burnChassis.Update(burn.State, 1.0 / 120, ppu, burn.WheelSpin);
burn.Hold(true);
for (int i = 0; i < 120; i++) { burn.Step(1.0 / 120); var b3 = burnChassis.Update(burn.State, 1.0 / 120, ppu, burn.WheelSpin); burnRear = b3.RearSpin; burnFront = b3.FrontSpin; }
Assert(burn.Phase == Motion.Burnout && burnRear - before3d.RearSpin > 10 && Math.Abs(burnFront - before3d.FrontSpin) < 2.5,
    "Burnout spins the rear tyres while the front ones stay put");
burn.Hold(false);
using var b = File.OpenRead(Path.Combine(root, "audio/v8-burnout-loop.wav")); using var d = File.OpenRead(Path.Combine(root, "audio/v8-drive-loop.wav"));
var audio = new EngineSamples(Pcm.Load(b), Pcm.Load(d)); float[] buffer = new float[8820];
double Rms(float[] a) => Math.Sqrt(a.Select(x => (double)x * x).Average());
audio.Set("burnout", .45f); audio.Read(buffer, 0, buffer.Length); Assert(Rms(buffer) > .005, "Real V8 burnout recording produces audible PCM");
audio.Set("drive", .45f); var rms = new List<double>();
for (int i = 0; i < 90; i++) { audio.Read(buffer, 0, buffer.Length); rms.Add(Rms(buffer)); }
Assert(rms.Skip(15).Min() > .005 && audio.Mode == "drive", "Driving recording sustains loaded RPM for 9 seconds without silent gaps");
Assert(audio.Position >= (int)(1.08 * 44100) * 2 && audio.Position <= (int)(1.36 * 44100) * 2, "Driving loop stays in the high-load segment");
audio.Set("silent", .45f); audio.Read(buffer, 0, buffer.Length); audio.Read(buffer, 0, buffer.Length);
Assert(buffer.All(x => x == 0), "Engine reaches exact silence after stop");
audio.Set("drive", 0); for (int i = 0; i < 10; i++) audio.Read(buffer, 0, buffer.Length);
Assert(Rms(buffer) < .00001, "Volume zero silences actual output");
audio.Set("drive", .45f); audio.Read(buffer, 0, buffer.Length); Assert(Rms(buffer) > .005, "Changing volume restores real driving audio");

var allImpacts = ImpactSound.Crashes().Concat(ImpactSound.Landings()).ToList();
Assert(ImpactSound.Crashes().Length >= 5 && ImpactSound.Landings().Length >= 4
    && allImpacts.All(x => x.Max(Math.Abs) > .5 && x[^1] == 0 && x.All(float.IsFinite)), "Several synthesized crash and landing sounds, all loud, finite and ending in silence");
Assert(allImpacts.Select(x => x.Length + ":" + string.Join(",", x.Take(4000).Where((_, i) => i % 400 == 0).Select(v => Math.Round(v, 3)))).Distinct().Count() == allImpacts.Count, "Every impact variant sounds different");
var bank = new ImpactBank(1); var picks = Enumerable.Range(0, 200).Select(i => bank.Next(new Impact(i % 3 / 2.0, true))).ToList();
Assert(picks.Zip(picks.Skip(1)).All(p => p.First.Variant != p.Second.Variant) && picks.Select(p => p.Variant).Distinct().Count() == bank.Variants(true)
    && picks.Select(p => p.Pitch).Distinct().Count() > 50, "Crashes rotate through all variants, never repeat back to back, and vary in pitch");
float[] crashSound = ImpactSound.Crashes()[0];
audio.Set("silent", .45f); for (int i = 0; i < 4; i++) audio.Read(buffer, 0, buffer.Length);
audio.Play(crashSound, 1, .9f); audio.Read(buffer, 0, buffer.Length);
Assert(Rms(buffer) > .02, "Crash sound plays over a silent engine");
for (int i = 0; i < 8; i++) audio.Read(buffer, 0, buffer.Length);
Assert(buffer.All(x => x == 0), "Crash sound finishes and leaves silence");

using var rsa = RSA.Create(3072); string publicKey = rsa.ExportSubjectPublicKeyInfoPem();
string package = Path.Combine(Path.GetTempPath(), "touareg-update-test-" + Guid.NewGuid() + ".exe");
try
{
    byte[] file = new byte[1000000]; file[0] = (byte)'M'; file[1] = (byte)'Z'; File.WriteAllBytes(package, file);
    var release = new Release("1.1.0", "https://github.com/example/touareg/releases/download/v1.1.0/TouaregPet.exe", Convert.ToHexString(SHA256.HashData(file)), file.Length);
    string EnvelopeFor(Release r)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(r);
        return JsonSerializer.Serialize(new Envelope(Convert.ToBase64String(payload), Convert.ToBase64String(rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))));
    }
    var verified = UpdateSecurity.Verify(EnvelopeFor(release), publicKey, "1.0.0"); UpdateSecurity.VerifyFile(package, verified);
    Assert(verified.Version == "1.1.0", "Accepts correctly signed newer update and exact file hash");
    void Rejected(Action action, string label) { bool rejected = false; try { action(); } catch { rejected = true; } Assert(rejected, label); }
    using var stranger = RSA.Create(3072);
    Rejected(() => UpdateSecurity.Verify(EnvelopeFor(release), stranger.ExportSubjectPublicKeyInfoPem(), "1.0.0"), "Rejects a forged signing key");
    Rejected(() => UpdateSecurity.Verify(EnvelopeFor(release with { Version = "1.0.0" }), publicKey, "1.0.0"), "Rejects downgrade and repeated version");
    Rejected(() => UpdateSecurity.Verify(EnvelopeFor(release with { Url = "http://example.org/app.exe" }), publicKey, "1.0.0"), "Rejects insecure download URL");
    Rejected(() => UpdateSecurity.Verify(EnvelopeFor(release with { Version = "../../escape" }), publicKey, "1.0.0"), "Rejects version path traversal");
    file[500] = 12; File.WriteAllBytes(package, file);
    Rejected(() => UpdateSecurity.VerifyFile(package, verified), "Rejects a modified package");
}
finally { File.Delete(package); }
// Release verification: EXE, signed update.json, the built TouaregPet.dll and optionally the expected version.
if (args.Length is 3 or 4)
{
    var ownRelease = UpdateSecurity.Verify(File.ReadAllText(args[1]), File.ReadAllText(Path.Combine(root, "update-public.pem")), "0.0.0");
    UpdateSecurity.VerifyFile(args[0], ownRelease);
    Assert(ownRelease.Version == (args.Length == 4 ? args[3] : "1.1.1"), "Verifies the actual delivered EXE using the pinned production key");
    using var assembly = new System.Reflection.PortableExecutable.PEReader(File.OpenRead(args[2]));
    var metadata = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(assembly);
    var names = metadata.ManifestResources.Select(handle => metadata.GetString(metadata.GetManifestResource(handle).Name).Replace('\\', '/')).ToList();
    Assert(names.Count(n => n.StartsWith("Assets/frames/") && n.EndsWith(".png")) == 24
        && names.Contains("Assets/audio/v8-burnout-loop.wav") && names.Contains("Assets/audio/v8-drive-loop.wav")
        && names.Contains("Assets/update-public.pem") && names.Contains("Assets/car/porsche-930.glb")
        && !names.Any(n => n.Contains("private", StringComparison.OrdinalIgnoreCase)),
        "Windows assembly embeds the 3D car, all 24 fallback frames, both recordings and only the public update key");
}
Console.WriteLine($"All {checks} checks passed.");

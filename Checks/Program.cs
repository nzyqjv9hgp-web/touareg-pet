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

string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Assets"));
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
if (args.Length == 3)
{
    var ownRelease = UpdateSecurity.Verify(File.ReadAllText(args[1]), File.ReadAllText(Path.Combine(root, "update-public.pem")), "0.0.0");
    UpdateSecurity.VerifyFile(args[0], ownRelease);
    Assert(ownRelease.Version == "1.1.0", "Verifies the actual delivered EXE using the pinned production key");
    using var assembly = new System.Reflection.PortableExecutable.PEReader(File.OpenRead(args[2]));
    var metadata = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(assembly);
    var names = metadata.ManifestResources.Select(handle => metadata.GetString(metadata.GetManifestResource(handle).Name).Replace('\\', '/')).ToList();
    Assert(names.Count(n => n.StartsWith("Assets/frames/") && n.EndsWith(".png")) == 24
        && names.Contains("Assets/audio/v8-burnout-loop.wav") && names.Contains("Assets/audio/v8-drive-loop.wav")
        && names.Contains("Assets/update-public.pem") && !names.Any(n => n.Contains("private", StringComparison.OrdinalIgnoreCase)),
        "Windows assembly embeds all 24 frames, both recordings and only the public update key");
}
Console.WriteLine($"All {checks} checks passed.");

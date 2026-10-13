# Real-Windows smoke test: starts the pet, grabs the car with the real mouse, carries and throws it,
# and saves screenshots plus a log of where the car window was. Exits non-zero on failure.
param([string]$Exe = "dist/TouaregPet.exe", [string]$Out = "smoke")
$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force $Out | Out-Null
$Out = (Resolve-Path $Out).Path
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type -ReferencedAssemblies System.Drawing @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
public static class Win
{
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint d, IntPtr e);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    public static IntPtr Find(uint pid, string title)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate (IntPtr h, IntPtr l)
        {
            uint p; GetWindowThreadProcessId(h, out p);
            var s = new StringBuilder(64); GetWindowText(h, s, 64);
            if (p == pid && s.ToString() == title && IsWindowVisible(h)) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    // SRCCOPY | CAPTUREBLT: includes layered (per-pixel alpha) windows such as the pet.
    public static void Capture(string path, int left, int top, int width, int height)
    {
        using (var bmp = new Bitmap(width, height))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                IntPtr dst = g.GetHdc(), src = GetDC(IntPtr.Zero);
                BitBlt(dst, 0, 0, width, height, src, left, top, 0x40CC0020);
                ReleaseDC(IntPtr.Zero, src); g.ReleaseHdc(dst);
            }
            bmp.Save(path, ImageFormat.Png);
        }
    }
    public static IntPtr At(int x, int y) { var p = new POINT { X = x, Y = y }; return GetAncestor(WindowFromPoint(p), 2); }
}
"@
[Win]::SetProcessDPIAware() | Out-Null
$failures = New-Object System.Collections.Generic.List[string]
function Fail($text) { Write-Host "FAIL $text"; $failures.Add($text) }
function Pass($text) { Write-Host "PASS $text" }
function Shot($name)
{
    $b = [System.Windows.Forms.SystemInformation]::VirtualScreen
    try { [Win]::Capture("$Out/$name.png", $b.Left, $b.Top, $b.Width, $b.Height) } catch { Write-Host "Screenshot $name failed: $_" }
}
function Car($hwnd)
{
    $r = New-Object Win+RECT; [Win]::GetWindowRect($hwnd, [ref]$r) | Out-Null
    # The pet window is centred on the car horizontally; the wheels sit at 70 % of its height.
    [pscustomobject]@{ X = [int](($r.L + $r.R) / 2); Y = [int]($r.T + ($r.B - $r.T) * .7); W = $r.R - $r.L; H = $r.B - $r.T }
}

$screen = [System.Windows.Forms.Screen]::PrimaryScreen
Write-Host "Screen $($screen.Bounds) working area $($screen.WorkingArea)"
$process = Start-Process -FilePath (Resolve-Path $Exe).Path -ArgumentList "--installed" -PassThru
Start-Sleep -Seconds 8
if ($process.HasExited) { throw "The pet exited during start-up with code $($process.ExitCode)" }
$pet = [Win]::Find([uint32]$process.Id, "Touareg")
if ($pet -eq [IntPtr]::Zero) { throw "The pet window was not found" }
Pass "Started; pet window found"
Shot "01-started"

$a = Car $pet; Start-Sleep -Milliseconds 700; $b = Car $pet
Write-Host "Car at ($($a.X),$($a.Y)) then ($($b.X),$($b.Y)), window $($a.W)x$($a.H)"
if ([math]::Abs($b.X - $a.X) + [math]::Abs($b.Y - $a.Y) -ge 3) { Pass "Car drives on its own" } else { Fail "Car did not move in 0.7 s" }

# Find a point on the car body that hit-tests to the pet window (transparent pixels pass clicks through).
$grab = $null
for ($attempt = 0; $attempt -lt 20 -and -not $grab; $attempt++)
{
    $c = Car $pet
    foreach ($dy in -45, -30, -60, -20, -75) { foreach ($dx in 0, -25, 25, -50, 50) {
        if (-not $grab -and [Win]::At($c.X + $dx, $c.Y + $dy) -eq $pet) { $grab = @{ X = $c.X + $dx; Y = $c.Y + $dy } }
    } }
    if (-not $grab) { Start-Sleep -Milliseconds 100 }
}
if (-not $grab) { Shot "02-no-hit"; throw "No point on the car hit-tests to the pet window" }
Pass "Car body receives the mouse at ($($grab.X),$($grab.Y))"

[Win]::SetCursorPos($grab.X, $grab.Y) | Out-Null; Start-Sleep -Milliseconds 30
$start = Car $pet
[Win]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
$dx = 260; $dy = -240; $steps = 40
for ($i = 1; $i -le $steps; $i++)
{
    [Win]::SetCursorPos([int]($grab.X + $dx * $i / $steps), [int]($grab.Y + $dy * $i / $steps)) | Out-Null
    Start-Sleep -Milliseconds 16
}
Start-Sleep -Milliseconds 150
$carried = Car $pet
Shot "02-carried"
Write-Host "Before ($($start.X),$($start.Y)) carried ($($carried.X),$($carried.Y)), cursor moved ($dx,$dy)"
$moveX = $carried.X - $start.X; $moveY = $carried.Y - $start.Y
if ([math]::Abs($moveX - $dx) -le 60 -and [math]::Abs($moveY - $dy) -le 60) { Pass "Car follows the cursor while held" }
else { Fail "Car did not follow the cursor: moved ($moveX,$moveY) for cursor ($dx,$dy)" }

# Fling to the right and let go.
for ($i = 1; $i -le 6; $i++) { [Win]::SetCursorPos([int]($grab.X + $dx + 30 * $i), [int]($grab.Y + $dy)) | Out-Null; Start-Sleep -Milliseconds 16 }
[Win]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
$released = Car $pet
$track = @()
for ($i = 0; $i -lt 30; $i++) { Start-Sleep -Milliseconds 100; $track += Car $pet; if ($i -eq 3) { Shot "03-flying" } }
Shot "04-landed"
Write-Host ("After release: " + (($track | ForEach-Object { "($($_.X),$($_.Y))" }) -join " "))
$lowest = ($track | Measure-Object -Property Y -Maximum).Maximum
if ($lowest -gt $released.Y + 40) { Pass "Thrown car falls back down" } else { Fail "Car did not fall after release" }
$wa = $screen.WorkingArea
$final = $track[-1]
if ($final.X -ge $wa.Left -and $final.X -le $wa.Right -and $final.Y -le $wa.Bottom + 5) { Pass "Car ends up on screen" } else { Fail "Car ended off screen at ($($final.X),$($final.Y))" }

# A quick click must not break anything.
$c = Car $pet; $hit = $null
foreach ($dy in -45, -30, -60) { foreach ($ox in 0, -25, 25) { if (-not $hit -and [Win]::At($c.X + $ox, $c.Y + $dy) -eq $pet) { $hit = @($c.X + $ox, $c.Y + $dy) } } }
if ($hit) { [Win]::SetCursorPos($hit[0], $hit[1]) | Out-Null; [Win]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 40; [Win]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero) }
[Win]::SetCursorPos(5, 5) | Out-Null
Start-Sleep -Seconds 3
Shot "05-after-click"
if ($process.HasExited) { Fail "The pet crashed (exit code $($process.ExitCode))" } else { Pass "Still running after drag, throw and click" }

$log = Join-Path $env:LOCALAPPDATA "TouaregPet\diagnostics.log"
if (Test-Path $log) { Copy-Item $log "$Out/diagnostics.log"; $text = Get-Content $log -Raw; Write-Host "diagnostics.log:"; Write-Host $text
    if ($text -match " (UI|startup|scan): ") { Fail "Errors in diagnostics.log" } }
else { Pass "No diagnostics.log errors" }
if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
if ($failures.Count) { Write-Host "$($failures.Count) smoke check(s) failed"; exit 1 }
Write-Host "Smoke test passed"

using Touareg.Core;

namespace Touareg.Windows;

internal static class Desktop
{
    internal static List<Surface> Scan(double carWidth, IReadOnlyDictionary<IntPtr, PageWatcher.Page>? pages = null)
    {
        List<Surface> result = []; List<Rectangle> above = [];
        // Parts of a horizontal edge at height y that no window above covers.
        List<(double L, double R)> Uncovered(double left, double right, double y)
        {
            List<(double L, double R)> segments = [(left, right)];
            foreach (var block in above.Where(b => b.Top < y + 2 && b.Bottom > y - 2))
            {
                List<(double, double)> next = [];
                foreach (var (l, rr) in segments)
                {
                    if (block.Right <= l || block.Left >= rr) next.Add((l, rr));
                    else { if (block.Left > l) next.Add((l, block.Left)); if (block.Right < rr) next.Add((block.Right, rr)); }
                }
                segments = next;
            }
            return segments;
        }
        Native.EnumWindows((hwnd, _) =>
        {
            if (!Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd)) return true;
            Native.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == Environment.ProcessId) return true;
            long style = Native.GetWindowLongPtr(hwnd, -20).ToInt64();
            if ((style & 0x80) != 0 || Native.GetWindow(hwnd, 4) != IntPtr.Zero) return true;
            if (Native.DwmInt(hwnd, 14, out int cloaked, 4) == 0 && cloaked != 0) return true;
            if (Native.DwmRect(hwnd, 9, out var rect, 16) != 0 && !Native.GetWindowRect(hwnd, out rect)) return true;
            var r = rect.Rectangle;
            if (r.Width < 160 || r.Height < 80) return true;
            double dpi = Native.GetDpiForWindow(hwnd); if (dpi < 96) dpi = 96;
            double minimum = carWidth * dpi / 96 + 24 * dpi / 96;
            foreach (var (l, rr) in Uncovered(r.Left, r.Right, r.Top))
            {
                var screen = Screen.FromPoint(new((int)((l + rr) / 2), r.Top));
                // Leave enough room above the edge for the character's head at the chosen size.
                if (rr - l >= minimum && r.Top >= screen.WorkingArea.Top + 335 * carWidth / 440 * dpi / 96
                    && r.Top < screen.WorkingArea.Bottom - 20)
                    result.Add(new(hwnd.ToInt64(), Math.Max(l, screen.WorkingArea.Left), Math.Min(rr, screen.WorkingArea.Right), r.Top, dpi));
            }
            // Blocks of the web page in a browser window: ledges inside it, cut by whatever lies above the browser.
            if (pages is not null && pages.TryGetValue(hwnd, out var page) && Native.GetWindowRect(hwnd, out var now))
            {
                double dx = now.Left - page.Window.Left, dy = now.Top - page.Window.Top;
                foreach (var ledge in page.Ledges)
                {
                    double y = ledge.Top + dy;
                    foreach (var (l, rr) in Uncovered(ledge.Left + dx, ledge.Right + dx, y))
                    {
                        var screen = Screen.FromPoint(new((int)((l + rr) / 2), (int)y));
                        if (rr - l >= minimum && y >= screen.WorkingArea.Top + 335 * carWidth / 440 * dpi / 96 && y < screen.WorkingArea.Bottom - 20)
                            result.Add(new(ledge.Key, Math.Max(l, screen.WorkingArea.Left), Math.Min(rr, screen.WorkingArea.Right), y, dpi));
                    }
                }
            }
            above.Add(r); return true;
        }, IntPtr.Zero);
        int i = 1;
        foreach (var screen in Screen.AllScreens)
        {
            var r = screen.WorkingArea;
            result.Add(new(-i++, r.Left + 8, r.Right - 8, r.Bottom - 1, Dpi(r), true));
        }
        return result;
    }

    // Whole monitors: the car can fly between them, their outer edges are walls.
    internal static List<Area> Screens() =>
        Screen.AllScreens.Select(s => new Area(s.Bounds.Left, s.Bounds.Top, s.Bounds.Right, s.Bounds.Bottom, Dpi(s.Bounds))).ToList();

    static double Dpi(Rectangle r)
    {
        var monitor = Native.MonitorFromPoint(new(r.Left + r.Width / 2, r.Top + r.Height / 2), 2);
        Native.GetDpiForMonitor(monitor, 0, out uint dpi, out _);
        return dpi >= 96 ? dpi : 96;
    }

    internal static bool Fullscreen()
    {
        var hwnd = Native.GetForegroundWindow(); Native.GetWindowThreadProcessId(hwnd, out uint pid);
        if (hwnd == IntPtr.Zero || pid == Environment.ProcessId) return false;
        var name = new System.Text.StringBuilder(128); Native.GetClassName(hwnd, name, name.Capacity);
        if (name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        if (!Native.GetWindowRect(hwnd, out var r)) return false;
        var monitor = Screen.FromHandle(hwnd).Bounds;
        return Math.Abs(r.Left - monitor.Left) <= 2 && Math.Abs(r.Top - monitor.Top) <= 2
            && Math.Abs(r.Right - monitor.Right) <= 2 && Math.Abs(r.Bottom - monitor.Bottom) <= 2;
    }
}

using System.Diagnostics;
using Interop.UIAutomationClient;
using Touareg.Core;

namespace Touareg.Windows;

// Reads the blocks of web pages open in browsers (Chrome, Edge, Yandex, Opera, Brave, Vivaldi, Firefox) through
// UI Automation, on its own thread: big pages can take a while and must not hold up window tracking.
// The result is a snapshot per browser window; Desktop.Scan turns it into surfaces with window occlusion applied.
internal sealed class PageWatcher : IDisposable
{
    internal sealed record Page(Rectangle Window, List<Box> Ledges);

    static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
        { "chrome", "msedge", "browser", "opera", "brave", "vivaldi", "chromium", "firefox" };
    // Elements whose top edge can be a ledge. Text covers headings and paragraphs; Group covers sections and cards.
    static readonly int[] BlockTypes = [50026, 50006, 50020, 50008, 50007, 50036, 50033, 50025, 50004, 50000, 50034];
    const int RuntimeId = 30000, Bounds = 30001, ControlType = 30003, Offscreen = 30022, Document = 50030;

    readonly Thread thread;
    readonly Dictionary<uint, bool> browserProcess = [];
    volatile IReadOnlyDictionary<IntPtr, Page> pages = new Dictionary<IntPtr, Page>();
    volatile bool running = true, enabled = true;
    volatile int carWidth;
    internal event Action? Changed;

    internal PageWatcher(int width, bool on)
    {
        carWidth = width; enabled = on;
        thread = new(Loop) { IsBackground = true, Name = "Touareg page scan" };
        thread.SetApartmentState(ApartmentState.MTA);   // UI Automation clients belong on an MTA thread
        thread.Start();
    }

    internal IReadOnlyDictionary<IntPtr, Page> Pages => pages;
    internal int CarWidth { set => carWidth = value; }
    internal bool Enabled { set { enabled = value; if (!value) { pages = new Dictionary<IntPtr, Page>(); Changed?.Invoke(); } } }

    void Loop()
    {
        IUIAutomation2 automation;
        try { automation = (IUIAutomation2)new CUIAutomation8(); }
        catch (Exception e) { Log.Write("pages", e); return; }
        // A hung browser must not freeze the scan: give up on a call after two seconds.
        automation.ConnectionTimeout = 2000; automation.TransactionTimeout = 2000;
        var cache = automation.CreateCacheRequest();
        cache.AddProperty(Bounds); cache.AddProperty(RuntimeId);
        var visible = automation.CreatePropertyCondition(Offscreen, false);
        var documents = automation.CreateAndCondition(automation.CreatePropertyCondition(ControlType, Document), visible);
        var blocks = automation.CreateAndCondition(visible,
            automation.CreateOrConditionFromArray(BlockTypes.Select(t => automation.CreatePropertyCondition(ControlType, t)).ToArray()));
        int pause = 400;
        while (running)
        {
            if (!enabled) { Thread.Sleep(500); continue; }
            var watch = Stopwatch.StartNew();
            var found = new Dictionary<IntPtr, Page>();
            try
            {
                foreach (var (hwnd, rect) in BrowserWindows())
                    if (Scan(automation, hwnd, rect, documents, blocks, cache) is { } page) found[hwnd] = page;
            }
            catch (Exception e) { Log.Write("pages", e); }
            if (!enabled) continue;
            pages = found; Changed?.Invoke();
            // Re-read often enough to follow scrolling, but back off on heavy pages.
            pause = Math.Clamp((int)watch.ElapsedMilliseconds * 3, 250, 2500);
            Thread.Sleep(pause);
        }
    }

    Page? Scan(IUIAutomation2 automation, IntPtr hwnd, Rectangle window, IUIAutomationCondition documents, IUIAutomationCondition blocks, IUIAutomationCacheRequest cache)
    {
        try
        {
            var root = automation.ElementFromHandle(hwnd);
            var docs = root.FindAllBuildCache(TreeScope.TreeScope_Descendants, documents, cache);
            IUIAutomationElement? page = null; tagRECT view = default; long best = 0;
            for (int i = 0; i < docs.Length; i++)
            {
                var r = docs.GetElement(i).CachedBoundingRectangle; long area = (long)(r.right - r.left) * (r.bottom - r.top);
                if (area > best) { best = area; page = docs.GetElement(i); view = r; }
            }
            if (page is null || best < 200 * 200) return null;
            var items = page.FindAllBuildCache(TreeScope.TreeScope_Descendants, blocks, cache);
            var boxes = new List<Box>(items.Length);
            for (int i = 0; i < items.Length; i++)
            {
                var e = items.GetElement(i); var r = e.CachedBoundingRectangle;
                int[] id = e.GetCachedPropertyValue(RuntimeId) as int[] ?? [i];
                boxes.Add(new(PageBlocks.SurfaceId(hwnd.ToInt64(), id), r.left, r.top, r.right, r.bottom));
            }
            double dpi = Math.Max(96, Native.GetDpiForWindow(hwnd));
            var viewport = new Box(0, view.left, view.top, view.right, view.bottom);
            return new(window, PageBlocks.Ledges(boxes, viewport, carWidth * dpi / 96 + 24 * dpi / 96));
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or TimeoutException) { return null; }
    }

    List<(IntPtr, Rectangle)> BrowserWindows()
    {
        List<(IntPtr, Rectangle)> result = [];
        Native.EnumWindows((hwnd, _) =>
        {
            if (!Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd) || Native.GetWindow(hwnd, 4) != IntPtr.Zero) return true;
            if (Native.DwmInt(hwnd, 14, out int cloaked, 4) == 0 && cloaked != 0) return true;
            Native.GetWindowThreadProcessId(hwnd, out uint pid);
            if (!browserProcess.TryGetValue(pid, out bool browser))
            {
                try { using var p = Process.GetProcessById((int)pid); browser = Browsers.Contains(p.ProcessName); } catch { browser = false; }
                browserProcess[pid] = browser;
            }
            if (browser && Native.GetWindowRect(hwnd, out var rect) && rect.Rectangle.Width >= 300 && rect.Rectangle.Height >= 200)
                result.Add((hwnd, rect.Rectangle));
            return true;
        }, IntPtr.Zero);
        return result;
    }

    public void Dispose() => running = false;
}

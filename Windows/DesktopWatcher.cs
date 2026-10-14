using Touareg.Core;

namespace Touareg.Windows;

// Reads window geometry on a background thread so the animation never waits for EnumWindows.
// Window moves, resizes, minimizes and z-order changes wake it at once; otherwise it rescans 4 times a second.
internal sealed class DesktopWatcher : IDisposable
{
    internal sealed record Snapshot(List<Surface> Surfaces, List<Area> Screens, bool Fullscreen);
    readonly AutoResetEvent wake = new(false);
    readonly Thread thread;
    readonly Native.WinEventProc callback;
    readonly IntPtr[] hooks;
    Snapshot? latest;
    volatile bool running = true;
    volatile int carWidth;
    readonly PageWatcher pages;

    internal DesktopWatcher(int width, PageWatcher pages)
    {
        carWidth = width; this.pages = pages;
        pages.Changed += () => wake.Set();
        callback = (_, _, _, idObject, idChild, _, _) => { if (idObject == 0 && idChild == 0) wake.Set(); };
        // Out-of-context hooks are delivered through this (UI) thread's message loop. Own windows are skipped.
        hooks =
        [
            Native.SetWinEventHook(0x0003, 0x0017, IntPtr.Zero, callback, 0, 0, 0x0002), // foreground, move/size, minimize
            Native.SetWinEventHook(0x8001, 0x800B, IntPtr.Zero, callback, 0, 0, 0x0002), // destroy, show, hide, reorder, location
        ];
        thread = new(Loop) { IsBackground = true, Name = "Touareg desktop scan" };
        thread.Start();
    }

    internal int CarWidth { set { if (carWidth != value) { carWidth = value; wake.Set(); } } }
    internal Snapshot? Take() => Interlocked.Exchange(ref latest, null);
    internal Snapshot Scan(int width) => new(Desktop.Scan(width, pages.Pages), Desktop.Screens(), Desktop.Fullscreen());

    void Loop()
    {
        while (running)
        {
            try { Interlocked.Exchange(ref latest, Scan(carWidth)); }
            catch (Exception e) { Log.Write("scan", e); Thread.Sleep(1000); }
            wake.WaitOne(250);
            // Coalesce bursts of events (dragging a window sends hundreds) into at most ~60 scans a second.
            Thread.Sleep(15);
        }
    }

    public void Dispose()
    {
        running = false; wake.Set();
        foreach (var hook in hooks) if (hook != IntPtr.Zero) Native.UnhookWinEvent(hook);
        GC.KeepAlive(callback);
    }
}

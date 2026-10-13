using System.Diagnostics;

namespace Touareg.Windows;

// Paces frames to the display: waits for the desktop compositor's vertical blank and asks the UI thread
// for one frame at a time (at most ~80 per second), so motion is even instead of following the 15.6 ms timer.
internal sealed class FrameClock : IDisposable
{
    readonly Control owner;
    readonly Action frame;
    readonly Thread thread;
    volatile bool running = true;
    int queued;

    internal FrameClock(Control owner, Action frame)
    {
        this.owner = owner; this.frame = frame;
        thread = new(Loop) { IsBackground = true, Name = "Touareg frames", Priority = ThreadPriority.AboveNormal };
        thread.Start();
    }

    void Loop()
    {
        Native.timeBeginPeriod(1);
        try
        {
            var watch = Stopwatch.StartNew(); double last = 0;
            while (running)
            {
                double before = watch.Elapsed.TotalMilliseconds;
                // DwmFlush returns at the next composition. When nothing is composed it may return at once: sleep instead.
                if (Native.DwmFlush() != 0 || watch.Elapsed.TotalMilliseconds - before < 2) Thread.Sleep(2);
                double now = watch.Elapsed.TotalMilliseconds;
                if (now - last < 12) continue;
                last = now;
                // Never queue more than one frame: a busy UI thread skips frames instead of falling behind.
                if (Interlocked.Exchange(ref queued, 1) == 1) continue;
                try { owner.BeginInvoke(Run); }
                catch (Exception) when (!running || owner.IsDisposed) { return; }
                catch (InvalidOperationException) { Volatile.Write(ref queued, 0); Thread.Sleep(50); }
            }
        }
        finally { Native.timeEndPeriod(1); }
    }

    void Run()
    {
        try { if (running) frame(); }
        finally { Volatile.Write(ref queued, 0); }
    }

    public void Dispose() => running = false;
}

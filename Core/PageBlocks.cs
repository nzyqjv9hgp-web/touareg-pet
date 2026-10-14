namespace Touareg.Core;

// A box of a web page in screen pixels, as reported by the browser's accessibility tree. Key identifies the element
// across scans, so the car can stay on the same block while the page scrolls.
public readonly record struct Box(long Key, double Left, double Top, double Right, double Bottom);

public static class PageBlocks
{
    // Turns page boxes into ledges the car can drive on: top edges inside the visible page, wide enough for the car,
    // one per height (nested blocks often share a top edge), the widest first.
    public static List<Box> Ledges(IEnumerable<Box> boxes, Box viewport, double minWidth, int limit = 40)
    {
        var candidates = boxes
            .Where(b => b.Bottom - b.Top >= 8 && b.Top > viewport.Top + 24 && b.Top < viewport.Bottom - 12)
            .Select(b => b with { Left = Math.Max(b.Left, viewport.Left), Right = Math.Min(b.Right, viewport.Right) })
            .Where(b => b.Right - b.Left >= minWidth)
            .OrderByDescending(b => b.Right - b.Left).ThenBy(b => b.Top);
        List<Box> kept = [];
        foreach (var b in candidates)
        {
            bool duplicate = kept.Any(k => Math.Abs(k.Top - b.Top) < 6
                && Math.Min(k.Right, b.Right) - Math.Max(k.Left, b.Left) > .5 * Math.Min(k.Right - k.Left, b.Right - b.Left));
            if (!duplicate) kept.Add(b);
            if (kept.Count == limit) break;
        }
        return kept;
    }

    // Stable surface id for a page element: never collides with window handles (positive, below 2^48) or floors (negative).
    public static long SurfaceId(long windowHandle, int[] runtimeId)
    {
        ulong hash = 1469598103934665603;
        void Mix(long v) { hash ^= (ulong)v; hash *= 1099511628211; }
        Mix(windowHandle);
        foreach (int part in runtimeId) Mix(part);
        return (long)(hash & 0x3FFF_FFFF_FFFF_FFFF) | (1L << 62);
    }
}

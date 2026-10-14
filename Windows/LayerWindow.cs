using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Touareg.Windows;

// Windows hit-tests the car's per-pixel alpha. The separate effects layer passes every click through.
internal sealed class LayerWindow : Form
{
    readonly bool clickThrough;
    IntPtr dc, dib, oldBitmap, pixels;
    Size dibSize;
    Bitmap? canvas;
    internal LayerWindow(bool effects)
    {
        clickThrough = effects;
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
        StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.None;
        ClientSize = new(1, 1); Text = effects ? "Touareg: эффекты" : "Touareg";
    }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x80000 | 0x80 | 0x08000000; if (clickThrough) cp.ExStyle |= 0x20; return cp; }
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x21) { m.Result = (IntPtr)3; return; } // MA_NOACTIVATE
        base.WndProc(ref m);
    }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) { }

    // A drawing surface that lives directly in the window's DIB: no per-frame copies.
    internal Bitmap Canvas(int width, int height)
    {
        if (canvas is null || dibSize.Width != width || dibSize.Height != height)
        {
            FreeDib(); dc = Native.CreateCompatibleDC(IntPtr.Zero);
            var info = new Native.BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, Bits = 32 };
            dib = Native.CreateDIBSection(dc, ref info, 0, out pixels, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero) throw new InvalidOperationException("Не удалось создать прозрачное окно");
            oldBitmap = Native.SelectObject(dc, dib); dibSize = new(width, height);
            canvas = new Bitmap(width, height, width * 4, PixelFormat.Format32bppPArgb, pixels);
        }
        return canvas;
    }

    // Raw premultiplied BGRA rows of the current canvas, for renderers that write pixels directly.
    internal IntPtr Pixels => pixels;

    internal void Present(int x, int y)
    {
        Native.GdiFlush();
        var dest = new Native.Point(x, y); var src = new Native.Point(0, 0); var size = new Native.Size(dibSize.Width, dibSize.Height);
        var blend = new Native.Blend { Alpha = 255, Format = 1 };
        if (!Native.UpdateLayeredWindow(Handle, IntPtr.Zero, ref dest, ref size, dc, ref src, 0, ref blend, 2))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
    void FreeDib()
    {
        canvas?.Dispose(); canvas = null; dibSize = Size.Empty;
        if (dc != IntPtr.Zero && oldBitmap != IntPtr.Zero) Native.SelectObject(dc, oldBitmap);
        if (dib != IntPtr.Zero) Native.DeleteObject(dib);
        if (dc != IntPtr.Zero) Native.DeleteDC(dc);
        dc = dib = oldBitmap = pixels = IntPtr.Zero;
    }
    protected override void Dispose(bool disposing) { FreeDib(); base.Dispose(disposing); }
}

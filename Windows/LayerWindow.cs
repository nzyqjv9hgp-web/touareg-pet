using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Touareg.Windows;

// Windows hit-tests the car's per-pixel alpha. The separate effects layer passes every click through.
internal sealed class LayerWindow : Form
{
    readonly bool clickThrough;
    IntPtr dc, dib, oldBitmap, pixels;
    Size dibSize;
    byte[] bytes = [];
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

    internal void Present(Bitmap bitmap, int x, int y)
    {
        if (dibSize != bitmap.Size)
        {
            FreeDib(); dc = Native.CreateCompatibleDC(IntPtr.Zero);
            var info = new Native.BitmapInfo { Size = 40, Width = bitmap.Width, Height = -bitmap.Height, Planes = 1, Bits = 32 };
            dib = Native.CreateDIBSection(dc, ref info, 0, out pixels, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero) throw new InvalidOperationException("Не удалось создать прозрачное окно");
            oldBitmap = Native.SelectObject(dc, dib); dibSize = bitmap.Size; bytes = new byte[bitmap.Width * bitmap.Height * 4];
        }
        var locked = bitmap.LockBits(new(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            for (int row = 0; row < bitmap.Height; row++)
                Marshal.Copy(locked.Scan0 + row * locked.Stride, bytes, row * bitmap.Width * 4, bitmap.Width * 4);
            Marshal.Copy(bytes, 0, pixels, bytes.Length);
        }
        finally { bitmap.UnlockBits(locked); }
        var dest = new Native.Point(x, y); var src = new Native.Point(0, 0); var size = new Native.Size(bitmap.Width, bitmap.Height);
        var blend = new Native.Blend { Alpha = 255, Format = 1 };
        if (!Native.UpdateLayeredWindow(Handle, IntPtr.Zero, ref dest, ref size, dc, ref src, 0, ref blend, 2))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
    void FreeDib()
    {
        if (dc != IntPtr.Zero && oldBitmap != IntPtr.Zero) Native.SelectObject(dc, oldBitmap);
        if (dib != IntPtr.Zero) Native.DeleteObject(dib);
        if (dc != IntPtr.Zero) Native.DeleteDC(dc);
        dc = dib = oldBitmap = pixels = IntPtr.Zero;
    }
    protected override void Dispose(bool disposing) { FreeDib(); base.Dispose(disposing); }
}

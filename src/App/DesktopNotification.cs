using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

sealed class DesktopNotification : NativeWindow, IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    struct BitmapInfoHeader
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, Bits;
        public uint Compression, ImageSize;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ColorsUsed, ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct BlendFunction
    {
        public byte Operation, Flags, Opacity, Format;
    }

    const int WsPopup = unchecked((int)0x80000000);
    const int WsExTopmost = 0x00000008, WsExTransparent = 0x00000020, WsExToolWindow = 0x00000080;
    const int WsExLayered = 0x00080000, WsExNoActivate = 0x08000000;
    const int WmNcHitTest = 0x0084, WmMouseActivate = 0x0021;
    const int SwHide = 0, SwShowNoActivate = 4;
    const uint UlwAlpha = 2;
    const byte AcSrcAlpha = 1;

    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfoHeader header,
        uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr window, IntPtr targetDc,
        ref Point target, ref Size size, IntPtr sourceDc, ref Point source, uint colorKey, ref BlendFunction blend, uint flags);

    Bitmap sourceImage;
    IntPtr dc, dib, oldBitmap;
    Size size;
    bool visible, disposed;
    int lastAlpha;

    // The caller owns image and can reuse it unchanged on successive animation ticks.
    internal void Update(Bitmap image, float opacity, int position)
    {
        if (disposed) return;
        int alpha = (int)Math.Round(255 * Math.Max(0, Math.Min(1, opacity)));
        if (image == null || alpha <= 0)
        {
            Dismiss();
            return;
        }

        Rectangle work = Screen.PrimaryScreen.WorkingArea;
        int margin = Math.Min(18, Math.Min(work.Width, work.Height) / 12);
        float fit = Math.Min(1f, Math.Min((work.Width - 2 * margin) / (float)image.Width,
            (work.Height - 2 * margin) / (float)image.Height));
        var fittedSize = new Size(Math.Max(1, (int)(image.Width * fit)), Math.Max(1, (int)(image.Height * fit)));
        bool unchanged = ReferenceEquals(image, sourceImage) && size == fittedSize;
        if (!unchanged && !PrepareImage(image, fittedSize))
        {
            Dismiss();
            return;
        }

        if (Handle == IntPtr.Zero)
        {
            CreateHandle(new CreateParams {
                Caption = "DS4 Battery Overlay notification", Style = WsPopup,
                ExStyle = WsExTopmost | WsExTransparent | WsExToolWindow | WsExLayered | WsExNoActivate,
                Width = size.Width, Height = size.Height
            });
        }

        var location = PopupSettings.Locate(work, size, position, margin);
        if (unchanged && visible && alpha == lastAlpha && location == lastLocation) return;
        var source = Point.Empty;
        var blend = new BlendFunction { Opacity = (byte)alpha, Format = AcSrcAlpha };
        if (!UpdateLayeredWindow(Handle, IntPtr.Zero, ref location, ref size, dc, ref source, 0, ref blend, UlwAlpha))
        {
            Dismiss();
            return;
        }
        if (!visible)
        {
            ShowWindow(Handle, SwShowNoActivate);
            visible = true;
        }
        lastLocation = location;
        lastAlpha = alpha;
    }

    Point lastLocation;

    bool PrepareImage(Bitmap image, Size fittedSize)
    {
        ReleaseImage();
        dc = CreateCompatibleDC(IntPtr.Zero);
        if (dc == IntPtr.Zero) return false;
        var header = new BitmapInfoHeader {
            Size = (uint)Marshal.SizeOf(typeof(BitmapInfoHeader)),
            Width = fittedSize.Width, Height = -fittedSize.Height, Planes = 1, Bits = 32
        };
        IntPtr bits;
        dib = CreateDIBSection(dc, ref header, 0, out bits, IntPtr.Zero, 0);
        if (dib == IntPtr.Zero) return false;
        oldBitmap = SelectObject(dc, dib);
        if (oldBitmap == IntPtr.Zero) return false;

        using (var normalized = new Bitmap(fittedSize.Width, fittedSize.Height, PixelFormat.Format32bppPArgb))
        {
            using (var graphics = Graphics.FromImage(normalized))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                if (image.Size == fittedSize) graphics.DrawImageUnscaled(image, 0, 0);
                else
                {
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(image, new Rectangle(Point.Empty, fittedSize));
                }
            }
            var data = normalized.LockBits(new Rectangle(Point.Empty, fittedSize), ImageLockMode.ReadOnly, normalized.PixelFormat);
            try
            {
                var row = new byte[fittedSize.Width * 4];
                for (int y = 0; y < fittedSize.Height; y++)
                {
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                    Marshal.Copy(row, 0, IntPtr.Add(bits, y * row.Length), row.Length);
                }
            }
            finally { normalized.UnlockBits(data); }
        }
        size = fittedSize;
        sourceImage = image;
        return true;
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmNcHitTest) message.Result = new IntPtr(-1); // HTTRANSPARENT
        else if (message.Msg == WmMouseActivate) message.Result = new IntPtr(3); // MA_NOACTIVATE
        else base.WndProc(ref message);
    }

    internal void Dismiss()
    {
        if (visible && Handle != IntPtr.Zero) ShowWindow(Handle, SwHide);
        visible = false;
        lastAlpha = 0;
        ReleaseImage();
    }

    void ReleaseImage()
    {
        if (oldBitmap != IntPtr.Zero) SelectObject(dc, oldBitmap);
        if (dib != IntPtr.Zero) DeleteObject(dib);
        if (dc != IntPtr.Zero) DeleteDC(dc);
        dc = dib = oldBitmap = IntPtr.Zero;
        sourceImage = null;
    }

    public void Dispose()
    {
        if (disposed) return;
        Dismiss();
        if (Handle != IntPtr.Zero) DestroyHandle();
        disposed = true;
    }
}

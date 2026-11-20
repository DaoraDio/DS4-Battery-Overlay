using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

static class TrayBatteryIcon
{
    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr icon);

    internal static Icon Create(int percentage, bool charging)
    {
        var smallIcon = SystemInformation.SmallIconSize;
        var size = new Size(Math.Max(16, smallIcon.Width), Math.Max(16, smallIcon.Height));
        using (var bitmap = Draw(percentage, size, charging))
        {
            IntPtr handle = bitmap.GetHicon();
            try
            {
                using (var borrowed = Icon.FromHandle(handle))
                    return (Icon)borrowed.Clone();
            }
            finally { DestroyIcon(handle); }
        }
    }

    internal static Bitmap Draw(int percentage, Size size, bool charging)
    {
        int level = Math.Max(0, Math.Min(100, percentage));
        int fill = level == 0 ? 0 : Math.Max(1, level / 10);
        var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var shadow = new SolidBrush(Color.FromArgb(14, 20, 32)))
        using (var outline = new SolidBrush(Color.FromArgb(226, 234, 250)))
        using (var inside = new SolidBrush(Color.FromArgb(28, 38, 55)))
        using (var charge = new SolidBrush(level <= 20 ? Color.FromArgb(255, 108, 112)
            : level <= 50 ? Color.FromArgb(255, 210, 99) : Color.FromArgb(79, 228, 141)))
        {
            Fill(graphics, shadow, size, 0, 3, 14, 13);
            Fill(graphics, shadow, size, 13, 5, 16, 11);
            Fill(graphics, outline, size, 1, 4, 13, 12);
            Fill(graphics, outline, size, 13, 6, 15, 10);
            Fill(graphics, inside, size, 2, 5, 12, 11);
            if (fill > 0) Fill(graphics, charge, size, 2, 5, 2 + fill, 11);
            if (charging)
            {
                float sx = size.Width / 16f, sy = size.Height / 16f;
                ChargingBolt.Draw(graphics, new RectangleF(4.4f * sx, 5.5f * sy, 5.2f * sx, 5.0f * sy), true);
            }
        }
        return bitmap;
    }

    static void Fill(Graphics graphics, Brush brush, Size size, int left, int top, int right, int bottom)
    {
        graphics.FillRectangle(brush, Rectangle.FromLTRB(
            (left * size.Width + 8) / 16, (top * size.Height + 8) / 16,
            (right * size.Width + 8) / 16, (bottom * size.Height + 8) / 16));
    }
}

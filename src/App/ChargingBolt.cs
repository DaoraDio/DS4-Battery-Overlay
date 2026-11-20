using System;
using System.Drawing;
using System.Drawing.Drawing2D;

static class ChargingBolt
{
    internal static void Draw(Graphics graphics, RectangleF bounds, bool smooth)
    {
        float x = bounds.X, y = bounds.Y, w = bounds.Width, h = bounds.Height;
        var points = new[] {
            new PointF(x + .65f * w, y), new PointF(x + .04f * w, y + .56f * h),
            new PointF(x + .43f * w, y + .56f * h), new PointF(x + .30f * w, y + h),
            new PointF(x + .96f * w, y + .40f * h), new PointF(x + .56f * w, y + .40f * h)
        };
        var state = graphics.Save();
        graphics.SmoothingMode = smooth ? SmoothingMode.AntiAlias : SmoothingMode.None;
        using (var border = new Pen(Color.FromArgb(16, 22, 33), Math.Max(.75f, w * .18f)) { LineJoin = LineJoin.Round })
        using (var fill = new SolidBrush(Color.FromArgb(255, 246, 177)))
        {
            graphics.DrawPolygon(border, points);
            graphics.FillPolygon(fill, points);
        }
        graphics.Restore(state);
    }
}

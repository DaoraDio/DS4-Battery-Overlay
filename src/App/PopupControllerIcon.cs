using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

static class PopupControllerIcon
{
    static readonly Bitmap icon = Load();

    static Bitmap Load()
    {
        using (var resource = typeof(PopupControllerIcon).Assembly.GetManifestResourceStream("DS4PopupControllerOutline"))
        {
            if (resource == null) throw new InvalidOperationException("The popup controller icon is missing.");
            using (var decoded = new Bitmap(resource)) return new Bitmap(decoded);
        }
    }

    internal static Bitmap CreateDisconnectedBadge(OverlayAppearance appearance)
    {
        const int samples = 4;
        float scale = appearance.Scale / 100f;
        int width = Math.Max(1, (int)Math.Round(48 * scale));
        int height = Math.Max(1, (int)Math.Round(32 * scale));
        using (var glyph = new Bitmap(30 * samples, 23 * samples, PixelFormat.Format32bppArgb))
        using (var large = new Bitmap(width * samples, height * samples, PixelFormat.Format32bppArgb))
        {
            using (var graphics = Graphics.FromImage(glyph))
            using (var attributes = new ImageAttributes())
            {
                Color accent = StudioTheme.Muted;
                graphics.Clear(Color.Transparent);
                graphics.ScaleTransform(samples, samples);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                attributes.SetColorMatrix(new ColorMatrix(new[] {
                    new float[] {0,0,0,0,0}, new float[] {0,0,0,0,0}, new float[] {0,0,0,0,0},
                    new float[] {0,0,0,1,0}, new float[] {accent.R/255f,accent.G/255f,accent.B/255f,0,1}
                }));
                attributes.SetWrapMode(WrapMode.TileFlipXY);
                graphics.DrawImage(icon, new Rectangle(0,0,30,23), 0,0,icon.Width,icon.Height,GraphicsUnit.Pixel,attributes);
                // Clear the gap in the glyph so a badge-free icon stays transparent.
                graphics.CompositingMode = CompositingMode.SourceCopy;
                using (var gap = new Pen(Color.Transparent, 2.8f) { StartCap=LineCap.Round, EndCap=LineCap.Round })
                    graphics.DrawLine(gap,5,21,25,3);
                graphics.CompositingMode = CompositingMode.SourceOver;
                using (var line = new Pen(accent, 1f) { StartCap=LineCap.Round, EndCap=LineCap.Round })
                    graphics.DrawLine(line,5,21,25,3);
            }
            using (var graphics = Graphics.FromImage(large))
            {
                graphics.Clear(appearance.ShowBackground ? appearance.Background : Color.Transparent);
                graphics.ScaleTransform(samples * scale, samples * scale);
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(glyph, new RectangleF(9,4.5f,30,23));
            }
            var badge = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(badge))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(large, new Rectangle(0,0,width,height));
            }
            return badge;
        }
    }

    internal static void Draw(Graphics graphics, NoticeKind kind, Color accent)
    {
        var saved = graphics.Save();
        try
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using (var attributes = new ImageAttributes())
            {
                attributes.SetColorMatrix(new ColorMatrix(new[] {
                    new float[] {0,0,0,0,0}, new float[] {0,0,0,0,0}, new float[] {0,0,0,0,0},
                    new float[] {0,0,0,1,0}, new float[] {accent.R/255f,accent.G/255f,accent.B/255f,0,1}
                }));
                attributes.SetWrapMode(WrapMode.TileFlipXY);
                graphics.DrawImage(icon, new Rectangle(13,18,30,23), 0,0,icon.Width,icon.Height,GraphicsUnit.Pixel,attributes);
            }
            if (kind == NoticeKind.Disconnected)
            {
                Color card = StudioTheme.Card;
                Color tile = Color.FromArgb((card.R*220+accent.R*35)/255,
                    (card.G*220+accent.G*35)/255,(card.B*220+accent.B*35)/255);
                using (var gap = new Pen(tile, 2.8f) {StartCap=LineCap.Round, EndCap=LineCap.Round})
                using (var line = new Pen(accent, 1f) {StartCap=LineCap.Round, EndCap=LineCap.Round})
                {
                    graphics.DrawLine(gap,18,39,38,21);
                    graphics.DrawLine(line,18,39,38,21);
                }
            }
        }
        finally { graphics.Restore(saved); }
    }
}

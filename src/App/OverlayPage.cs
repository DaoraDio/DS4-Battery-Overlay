using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

sealed partial class OverlayPage : UserControl
{
    readonly StudioNumber scale = new StudioNumber(75, 150, "overlay size");
    readonly StudioNumber margin = new StudioNumber(0, 96, "overlay margin");
    readonly StudioNumber corner = new StudioNumber(0, 16, "corner radius");
    readonly StudioSwitch icon = new StudioSwitch("Battery icon");
    readonly StudioSwitch number = new StudioSwitch("Percentage");
    readonly StudioSwitch background = new StudioSwitch("Badge background");
    readonly RadioButton segmented = ChoiceButton("Five bars", "Five-bar battery icon");
    readonly RadioButton continuous = ChoiceButton("Continuous", "Continuous battery icon");
    readonly RadioButton outsideIcon = ChoiceButton("Next to icon", "Show percentage next to the battery icon");
    readonly RadioButton insideIcon = ChoiceButton("Inside icon", "Show percentage inside the battery icon");
    readonly Button backgroundColor = StudioTheme.Button("");
    readonly Button textColor = StudioTheme.Button("");
    readonly ShortcutField shortcut = new ShortcutField();
    readonly Label result = StudioTheme.Label("", 9);
    readonly Label previewState = StudioTheme.Label("", 9);
    readonly Button previewDown = PreviewButton("−", "Decrease preview battery", repeat: true);
    readonly Button previewUp = PreviewButton("+", "Increase preview battery", repeat: true);
    readonly Button previewLive = PreviewButton("Use live", "Use the live controller reading in the preview");
    readonly ToolTip previewTips = new ToolTip();
    readonly BadgePreview preview = new BadgePreview();
    readonly StudioPositionPicker positions = new StudioPositionPicker("Battery badge position");
    int? previewPercent;
    string liveReading = "";
    bool liveCharging;
    Color badgeColor;
    Color inkColor;
    bool readingControls;

    public OverlayPage(OverlayAppearance current, Action<OverlayAppearance> apply)
    {
        BuildRedesigned(current, apply);
    }

    static TableLayoutPanel Card(int rows)
    {
        return new TableLayoutPanel {
            RowCount = rows, ColumnCount = 1, Dock = DockStyle.Fill, BackColor = StudioTheme.Card,
            Padding = new Padding(14), CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
    }

    static Label FullLabel(string text, float size, bool bold, Color? color = null)
    {
        var label = StudioTheme.Label(text, size, bold);
        label.Dock = DockStyle.Fill;
        label.Margin = Padding.Empty;
        if (color.HasValue) label.ForeColor = color.Value;
        return label;
    }

    static Control ChargeDataInfo()
    {
        var info = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            Margin = new Padding(0, 8, 0, 0), Padding = new Padding(11, 7, 11, 7),
            BackColor = StudioTheme.Rail
        };
        info.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        StudioTheme.FillRow(info, 23);
        info.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        info.Controls.Add(FullLabel("How the DS4 reports charge", 9, true, StudioTheme.Accent), 0, 0);
        info.Controls.Add(FullLabel(
            "The DS4 reports coarse charge levels. Unplugged readings use nine steps from 5% to 100%. USB uses the original scale and a separate Full status. These percentages are estimates.",
            9, false, StudioTheme.Muted), 0, 1);
        return info;
    }

    Control PreviewControls()
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, Margin = Padding.Empty };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 65));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 35));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 35));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        previewState.Dock = DockStyle.Fill;
        previewState.Margin = Padding.Empty;
        previewState.ForeColor = StudioTheme.Muted;
        row.Controls.Add(previewState, 0, 0);
        row.Controls.Add(previewLive, 1, 0);
        AddOfflinePreview(row);
        row.Controls.Add(previewDown, 3, 0);
        row.Controls.Add(previewUp, 4, 0);
        previewTips.SetToolTip(previewDown, "Previous reported battery level");
        previewTips.SetToolTip(previewUp, "Next reported battery level");
        previewDown.Click += delegate { StepPreview(false); };
        previewUp.Click += delegate { StepPreview(true); };
        previewLive.Click += delegate { previewPercent = null; previewDisconnected = false; UpdatePreview(); };
        return row;
    }

    static Button PreviewButton(string text, string name, bool repeat = false)
    {
        var button = StudioTheme.Button(text, repeat: repeat);
        button.Dock = DockStyle.Fill;
        button.AutoSize = false;
        button.Padding = Padding.Empty;
        button.MinimumSize = Size.Empty;
        button.Margin = new Padding(4, 2, 0, 2);
        button.AccessibleName = name;
        return button;
    }

    Control ChoiceRow(StudioSwitch toggle, RadioButton first, RadioButton second)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 41));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 29.5f));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 29.5f));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        toggle.Dock = DockStyle.Fill;
        toggle.Margin = Padding.Empty;
        row.Controls.Add(toggle, 0, 0);
        row.Controls.Add(first, 1, 0);
        row.Controls.Add(second, 2, 0);
        toggle.CheckedChanged += delegate { UpdatePreview(); };
        first.CheckedChanged += ChoiceChanged;
        second.CheckedChanged += ChoiceChanged;
        return row;
    }

    static RadioButton ChoiceButton(string text, string accessibleName)
    {
        var choice = new RadioButton {
            Appearance = System.Windows.Forms.Appearance.Button, AutoSize = false, FlatStyle = FlatStyle.Flat,
            Text = text, TextAlign = ContentAlignment.MiddleCenter, Font = StudioTheme.Font(9, true),
            AccessibleName = accessibleName, Dock = DockStyle.Fill, Margin = new Padding(3, 2, 0, 2),
            UseVisualStyleBackColor = false, Cursor = Cursors.Hand
        };
        choice.FlatAppearance.CheckedBackColor = StudioTheme.Selection;
        choice.FlatAppearance.MouseOverBackColor = StudioTheme.Border;
        return choice;
    }

    void ChoiceChanged(object sender, EventArgs e)
    {
        if (((RadioButton)sender).Checked) UpdatePreview();
    }

    static Control Fields(string[] labels, Control[] controls)
    {
        var group = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, RowCount = 2, ColumnCount = controls.Length };
        StudioTheme.FillRow(group, 22);
        StudioTheme.FillRow(group, 38);
        for (int i = 0; i < controls.Length; i++)
        {
            group.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / controls.Length));
            var name = FullLabel(labels[i], 8, false, StudioTheme.Muted);
            group.Controls.Add(name, i, 0);
            controls[i].Dock = DockStyle.Fill;
            controls[i].Margin = new Padding(0, 2, 7, 2);
            group.Controls.Add(controls[i], i, 1);
        }
        return group;
    }

    void Set(OverlayAppearance settings) { SetDraft(settings); }
    internal OverlayAppearance Read() { return ReadDraft(); }

    public void UpdateReading(string reading, bool charging = false)
    {
        if (reading == liveReading && charging == liveCharging) return;
        liveReading = reading;
        liveCharging = charging;
        UpdatePreview();
    }

    int ReadPreviewPercent()
    {
        int live = BatteryPublisher.GetPercent(liveReading ?? "");
        return previewPercent ?? (live < 0 ? 55 : live);
    }

    void StepPreview(bool increase)
    {
        previewDisconnected = false;
        int percent = ReadPreviewPercent();
        var levels = PopupSettings.BatteryLevels;
        int index = increase ? Array.FindIndex(levels, level => level > percent)
            : Array.FindLastIndex(levels, level => level < percent);
        previewPercent = index >= 0 ? levels[index] : levels[increase ? 0 : levels.Length - 1];
        UpdatePreview();
    }

    void UpdatePreview() { RefreshPreview(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) previewTips.Dispose();
        base.Dispose(disposing);
    }

    static void UpdateChoice(RadioButton choice, bool enabled)
    {
        choice.Enabled = enabled;
        bool selected = enabled && choice.Checked;
        choice.BackColor = selected ? StudioTheme.Selection : StudioTheme.Rail;
        choice.ForeColor = selected ? StudioTheme.Accent : StudioTheme.Text;
        choice.FlatAppearance.BorderColor = selected ? StudioTheme.Accent : StudioTheme.Border;
    }

    static void UpdateSwatch(Button button, Color color)
    {
        button.Text = string.Format("#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
        button.BackColor = color;
        button.ForeColor = color.GetBrightness() > .48 ? Color.FromArgb(17, 22, 34) : StudioTheme.Text;
        button.FlatAppearance.MouseOverBackColor = color;
    }

    bool ChooseColor(string title, ref Color color)
    {
        Color selected;
        if (!StudioColorPicker.TryPick(FindForm(), title, color, out selected)) return false;
        color = selected;
        return true;
    }

    sealed class ShortcutField : TextBox
    {
        Keys value;
        public Keys Value
        {
            get { return value; }
            set { this.value = value; Text = OverlayAppearance.FormatShortcut(value); }
        }

        public ShortcutField()
        {
            ReadOnly = true;
            BackColor = StudioTheme.Rail;
            ForeColor = StudioTheme.Text;
            BorderStyle = BorderStyle.FixedSingle;
            Font = StudioTheme.Font(11);
            AccessibleName = "Toggle shortcut";
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keys)
        {
            if (keys == Keys.Tab || keys == (Keys.Shift | Keys.Tab) || keys == Keys.Escape)
                return base.ProcessCmdKey(ref message, keys);
            if (!OverlayAppearance.ValidShortcut(keys)) return base.ProcessCmdKey(ref message, keys);
            Value = keys;
            SelectAll();
            return true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (OverlayAppearance.ValidShortcut(e.KeyData) && e.KeyCode != Keys.Escape)
            {
                Value = e.KeyData;
                SelectAll();
                e.SuppressKeyPress = true;
            }
            base.OnKeyDown(e);
        }
    }

    internal sealed class BadgePreview : Control
    {
        Bitmap badge;
        OverlayAppearance settings;
        internal bool Centered;

        public BadgePreview()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = StudioTheme.Card;
            AccessibleName = "Live preview of the in-game battery badge";
        }

        public void UpdateBadge(string reading, OverlayAppearance style, bool charging)
        {
            var next = reading == null ? (style.ShowDisconnectedIcon ? PopupControllerIcon.CreateDisconnectedBadge(style) : null) : BatteryPublisher.CreateBadge(reading, style, charging);
            if (next != null && style.ShowBackground && style.CornerRadius > 0)
                RoundCorners(next, style.CornerRadius);
            if (badge != null) badge.Dispose();
            badge = next;
            settings = style;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            StudioTheme.PaintPreview(e.Graphics, ClientSize, BackColor, PaintBadge);
        }

        void PaintBadge(Graphics g)
        {
            if (badge == null || settings == null) return;
            int roomX = Math.Max(0, Width - badge.Width), roomY = Math.Max(0, Height - badge.Height);
            int mx = Math.Min(roomX, Math.Max(8, settings.Margin / 2));
            int my = Math.Min(roomY, Math.Max(8, settings.Margin / 2));
            bool left = settings.Position == 1 || settings.Position == 3;
            bool bottom = settings.Position == 2 || settings.Position == 3 || settings.Position == 5;
            int x = settings.Position >= 4 ? roomX / 2 : left ? mx : roomX - mx;
            int y = bottom ? roomY - my : my;
            if (Centered) { x=roomX/2; y=roomY/2; }
            g.DrawImageUnscaled(badge, x, y);
        }

        internal static void RoundCorners(Bitmap image, int radius)
        {
            radius = Math.Min(radius, Math.Min(image.Width, image.Height) / 2);
            int center = 8 * radius;
            // Match the coverage of the OpenGL renderer using four samples per axis.
            for (int y = 0; y < radius; y++)
            for (int x = 0; x < radius; x++)
            {
                int covered = 0;
                for (int sy = 1; sy < 8; sy += 2)
                for (int sx = 1; sx < 8; sx += 2)
                {
                    int dx = center - 8 * x - sx, dy = center - 8 * y - sy;
                    if (dx * dx + dy * dy <= center * center) covered++;
                }
                if (covered == 16) continue;
                int right = image.Width - 1 - x, bottom = image.Height - 1 - y;
                SetCoverage(image, x, y, covered);
                SetCoverage(image, right, y, covered);
                SetCoverage(image, x, bottom, covered);
                SetCoverage(image, right, bottom, covered);
            }
        }

        static void SetCoverage(Bitmap image, int x, int y, int covered)
        {
            var color = image.GetPixel(x, y);
            image.SetPixel(x, y, Color.FromArgb((color.A * covered + 8) / 16, color));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && badge != null) badge.Dispose();
            base.Dispose(disposing);
        }
    }

    public static bool SelfTest()
    {
        var appearance = new OverlayAppearance { Position = 3, Margin = 16, Scale = 125, CornerRadius = 8,
            ShowNumber = false, ShowBackground = false, Shortcut = Keys.Control | Keys.F9 };
        using (var page = new OverlayPage(appearance, delegate { }))
        using (var badge = BatteryPublisher.CreateBadge("55%", page.Read()))
        {
            var got = page.Read();
            return got.Position == 3 && got.Margin == 16 && got.Scale == 125 && got.CornerRadius == 8 &&
                !got.ShowNumber && !got.ShowBackground && got.Shortcut == appearance.Shortcut &&
                badge.Height == 40 && badge.GetPixel(0, 0).A == 0 &&
                CheckPreviewControls(page) && CheckNumberPlacement(page) && CheckBatteryBars() &&
                CheckContinuousBattery() && CheckTransparentOutline() && CheckRoundedPreview();
        }
    }

    static bool CheckRoundedPreview()
    {
        var style = new OverlayAppearance { CornerRadius = 6 };
        using (var badge = BatteryPublisher.CreateBadge("55%", style))
        {
            BadgePreview.RoundCorners(badge, style.CornerRadius);
            int edge = badge.GetPixel(1, 2).A;
            return edge > 0 && edge < 255 && badge.GetPixel(badge.Width - 2, badge.Height - 3).A == edge &&
                badge.GetPixel(0, 0).A == 0 && badge.GetPixel(badge.Width / 2, 0).A == 255;
        }
    }

    static bool CheckPreviewControls(OverlayPage page)
    {
        if (!page.Read().SegmentedIcon || page.ReadPreviewPercent() != 55) return false;
        page.continuous.Checked = true;
        if (page.Read().SegmentedIcon) return false;
        page.UpdateReading("65%");
        if (page.ReadPreviewPercent() != 65) return false;
        page.previewDown.PerformClick();
        page.UpdateReading("75%");
        if (page.ReadPreviewPercent() != 62 || !page.previewLive.Visible) return false;
        page.previewUp.PerformClick();
        if (page.ReadPreviewPercent() != 65) return false;
        page.previewLive.PerformClick();
        if (page.ReadPreviewPercent() != 75 || page.previewLive.Visible) return false;
        page.UpdateReading("5%");
        page.previewDown.PerformClick();
        if (page.ReadPreviewPercent() != 100) return false;
        page.previewUp.PerformClick();
        var levels = PopupSettings.BatteryLevels;
        foreach (int level in levels)
        {
            if (page.ReadPreviewPercent() != level) return false;
            page.previewUp.PerformClick();
        }
        for (int index = levels.Length - 1; index >= 0; index--)
        {
            page.previewDown.PerformClick();
            if (page.ReadPreviewPercent() != levels[index]) return false;
        }
        return page.ReadPreviewPercent() == 5 && page.previewDown.Enabled && page.previewUp.Enabled &&
            page.previewTips.GetToolTip(page.previewDown).Length != 0 && page.previewTips.GetToolTip(page.previewUp).Length != 0;
    }

    static bool CheckTransparentOutline()
    {
        var style = new OverlayAppearance { ShowNumber = false, ShowBackground = false };
        for (int scale = 75; scale <= 150; ++scale)
        {
            style.Scale = scale;
            int left = (9 * scale + 50) / 100, top = (8 * scale + 50) / 100;
            int width = (26 * scale + 50) / 100, height = (16 * scale + 50) / 100;
            int stroke = (scale + 99) / 100;
            using (var badge = BatteryPublisher.CreateBadge("0%", style))
            {
                int outerAlpha = Math.Min(255, (scale * 255 + 50) / 100);
                if (Math.Abs(badge.GetPixel(left + width / 2, top).A - outerAlpha) > 1) return false;
                for (int column = 0; column < width; ++column)
                {
                    int alpha = badge.GetPixel(left + column, top + stroke).A;
                    int opposite = badge.GetPixel(left + width - column - 1, top + stroke).A;
                    if (Math.Abs(alpha - opposite) > 32 ||
                        (column >= stroke && column < width - stroke && alpha != 0)) return false;
                }
                for (int row = 0; row < height; ++row)
                {
                    int alpha = badge.GetPixel(left + width / 2, top + row).A;
                    int opposite = badge.GetPixel(left + width / 2, top + height - row - 1).A;
                    if (Math.Abs(alpha - opposite) > 32 || (row >= stroke && row < height - stroke && alpha != 0)) return false;
                }
            }
        }
        return true;
    }

    static bool CheckNumberPlacement(OverlayPage page)
    {
        if (page.Read().NumberInsideIcon || page.insideIcon.Enabled) return false;
        page.number.Checked = true;
        if (!page.insideIcon.Enabled || !page.outsideIcon.Checked) return false;
        page.insideIcon.Checked = true;
        if (!page.Read().NumberInsideIcon || page.outsideIcon.Checked) return false;
        page.icon.Checked = false;
        if (page.insideIcon.Enabled || !page.Read().NumberInsideIcon) return false;
        page.icon.Checked = true;
        page.number.Checked = false;
        if (page.insideIcon.Enabled || !page.Read().NumberInsideIcon) return false;
        page.Set(new OverlayAppearance { NumberInsideIcon = true });
        return page.insideIcon.Enabled && page.Read().NumberInsideIcon;
    }

    static bool CheckBatteryBars()
    {
        var style = new OverlayAppearance { ShowNumber = false, ShowBackground = false };
        int[] expectedBars = { 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5 };
        foreach (int scale in new[] { 75, 85, 100, 125, 150 })
        {
            style.Scale = scale;
            for (int level = 0; level < expectedBars.Length; level++)
            foreach (int percent in new[] { level * 10, Math.Min(100, level * 10 + 5) })
            {
                using (var badge = BatteryPublisher.CreateBadge(percent + "%", style))
                {
                    int y = (int)Math.Round(16 * scale / 100.0);
                    int count = 0;
                    bool previous = false;
                    for (int x = (int)Math.Round(11 * scale / 100.0); x < 32 * scale / 100; x++)
                    {
                        bool filled = badge.GetPixel(x, y).A > 0;
                        if (filled && !previous) count++;
                        previous = filled;
                    }
                    if (count != expectedBars[level]) return false;
                }
            }
        }
        return true;
    }

    static bool CheckContinuousBattery()
    {
        var style = new OverlayAppearance { ShowNumber = false, ShowBackground = false, SegmentedIcon = false };
        foreach (int percent in new[] { 0, 25, 55, 100 })
        {
            int width = (percent + 4) / 5;
            using (var badge = BatteryPublisher.CreateBadge(percent + "%", style))
                for (int x = 11; x <= 32; x++)
                    if ((badge.GetPixel(x, 16).A > 0) != (x >= 12 && x < 12 + width)) return false;
        }
        return true;
    }
}

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

sealed class StudioColorPicker : Form
{
    const int PaletteColumns = 8;
    static readonly int[] PresetColors = {
        0x000000, 0x242A38, 0x40495C, 0x657184, 0x8794A8, 0xAEB8C8, 0xD7E0EC, 0xFFFFFF,
        0xFF9BA5, 0xFFBB8F, 0xFFE59A, 0xCBF094, 0x92EDB2, 0x8DE4F0, 0x97B8FF, 0xC2B3FF,
        0xE44655, 0xF58042, 0xF3C94A, 0x9CD957, 0x35CE83, 0x27C9DB, 0x4787F5, 0xA98BFF,
        0xB9253D, 0xDC5726, 0xCC9F21, 0x68AD35, 0x0DAA76, 0x099BB0, 0x365CD9, 0x814CF1,
        0xF3489E, 0xFB6B82, 0xB96C42, 0xD7A879, 0x26D6B3, 0x007FC7, 0x4B3AB5, 0xB967D6
    };

    readonly Button[] palette = new Button[PresetColors.Length];
    readonly ColorField plane = new ColorField(false);
    readonly ColorField hueStrip = new ColorField(true);
    readonly Panel selectedChip = ColorChip();
    readonly Label hexError = StudioTheme.Label("", 8);
    readonly TextBox hex = new TextBox();
    readonly Button useColor = StudioTheme.Button("Use color", true);
    readonly ToolTip swatchTip = new ToolTip();
    Button paletteTab;
    Color selected;
    double hue = 0.74;
    double saturation, value;
    bool writingHex;

    internal static bool TryPick(IWin32Window owner, string title, Color initial, out Color selected)
    {
        using (var dialog = new StudioColorPicker(title, initial))
        {
            bool accepted = dialog.ShowDialog(owner) == DialogResult.OK;
            selected = accepted ? dialog.selected : initial;
            return accepted;
        }
    }

    StudioColorPicker(string title, Color initial)
    {
        Text = title;
        BackColor = StudioTheme.Canvas;
        ForeColor = StudioTheme.Text;
        Font = StudioTheme.Font(9);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = MaximizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        ClientSize = new Size(672, 466);

        var layout = Table(1, 3);
        layout.Padding = new Padding(22, 12, 22, 16);
        StudioTheme.FillRow(layout, 54);
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        StudioTheme.FillRow(layout, 47);
        layout.Controls.Add(Heading(title, 21), 0, 0);
        Controls.Add(layout);

        var cards = Table(2, 1);
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        cards.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(cards, 0, 1);
        cards.Controls.Add(BuildPresets(initial), 0, 0);
        cards.Controls.Add(BuildCustom(), 1, 0);

        var actions = new FlowLayoutPanel {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false,
            Margin = Padding.Empty, Padding = new Padding(0, 10, 0, 0)
        };
        var cancel = StudioTheme.Button("Cancel");
        useColor.Margin = Padding.Empty;
        cancel.Margin = new Padding(0, 0, 9, 0);
        useColor.MinimumSize = new Size(106, 35);
        cancel.MinimumSize = new Size(88, 35);
        useColor.DialogResult = DialogResult.OK;
        cancel.DialogResult = DialogResult.Cancel;
        actions.Controls.Add(useColor);
        actions.Controls.Add(cancel);
        layout.Controls.Add(actions, 0, 2);
        AcceptButton = useColor;
        CancelButton = cancel;
        SetColor(Color.FromArgb(initial.R, initial.G, initial.B), true);
        Shown += delegate { hex.Focus(); hex.SelectAll(); };
    }

    Control BuildPresets(Color initial)
    {
        var card = Card(4, new Padding(0, 0, 7, 0));
        StudioTheme.FillRow(card, 29);
        StudioTheme.FillRow(card, 180);
        StudioTheme.FillRow(card, 15);
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.Controls.Add(Heading("Preset colors", 11), 0, 0);

        int rows = palette.Length / PaletteColumns;
        var grid = Table(PaletteColumns, rows);
        for (int c = 0; c < PaletteColumns; c++)
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / PaletteColumns));
        for (int r = 0; r < rows; r++)
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rows));
        for (int i = 0; i < palette.Length; i++)
        {
            int index = i;
            Color color = Color.FromArgb(unchecked((int)0xFF000000) | PresetColors[i]);
            string name = Hex(color);
            var swatch = StudioTheme.Button("");
            swatch.AutoSize = false;
            swatch.MinimumSize = Size.Empty;
            swatch.Padding = Padding.Empty;
            swatch.Margin = new Padding(2, 2, 3, 3);
            swatch.Dock = DockStyle.Fill;
            swatch.TabStop = i == 0;
            swatch.BackColor = swatch.FlatAppearance.MouseOverBackColor = swatch.FlatAppearance.MouseDownBackColor = color;
            swatch.AccessibleName = name + " preset";
            swatchTip.SetToolTip(swatch, name);
            swatch.Click += delegate { SetColor(color, true); };
            swatch.GotFocus += delegate {
                if (paletteTab != null) paletteTab.TabStop = false;
                paletteTab = swatch;
                swatch.TabStop = true;
            };
            swatch.PreviewKeyDown += delegate(object sender, PreviewKeyDownEventArgs e) {
                if (IsDirection(e.KeyCode)) e.IsInputKey = true;
            };
            swatch.KeyDown += delegate(object sender, KeyEventArgs e) {
                if (!IsDirection(e.KeyCode)) return;
                int column = index % PaletteColumns;
                int row = index / PaletteColumns;
                if (e.KeyCode == Keys.Left) column--; else if (e.KeyCode == Keys.Right) column++;
                if (e.KeyCode == Keys.Up) row--; else if (e.KeyCode == Keys.Down) row++;
                if (row >= 0 && row < rows && column >= 0 && column < PaletteColumns)
                    palette[row * PaletteColumns + column].Focus();
                e.SuppressKeyPress = true;
            };
            palette[i] = swatch;
            grid.Controls.Add(swatch, i % PaletteColumns, i / PaletteColumns);
        }
        paletteTab = palette[0];
        card.Controls.Add(grid, 0, 1);

        var change = Table(2, 2);
        change.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        change.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        StudioTheme.FillRow(change, 24);
        StudioTheme.FillRow(change, 42);
        var currentChip = ColorChip();
        currentChip.BackColor = Color.FromArgb(initial.R, initial.G, initial.B);
        currentChip.AccessibleName = "Current " + Hex(initial);
        currentChip.Margin = new Padding(0, 0, 5, 0);
        selectedChip.Margin = new Padding(5, 0, 0, 0);
        var right = Caption("Selected");
        right.Margin = new Padding(5, 0, 0, 0);
        change.Controls.Add(Caption("Current"), 0, 0);
        change.Controls.Add(right, 1, 0);
        change.Controls.Add(currentChip, 0, 1);
        change.Controls.Add(selectedChip, 1, 1);
        card.Controls.Add(change, 0, 3);
        return card;
    }

    Control BuildCustom()
    {
        var card = Card(5, new Padding(7, 0, 0, 0));
        StudioTheme.FillRow(card, 29);
        StudioTheme.FillRow(card, 174);
        StudioTheme.FillRow(card, 37);
        StudioTheme.FillRow(card, 22);
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.Controls.Add(Heading("Custom color", 11), 0, 0);
        card.Controls.Add(plane, 0, 1);
        hueStrip.Margin = new Padding(0, 8, 0, 4);
        card.Controls.Add(hueStrip, 0, 2);
        plane.Changed += delegate(double x, double y) { saturation = x; value = 1 - y; SetFromFields(); };
        hueStrip.Changed += delegate(double x, double y) { hue = x; SetFromFields(); };

        hex.ForeColor = StudioTheme.Text;
        hex.BackColor = StudioTheme.Rail;
        hex.BorderStyle = BorderStyle.None;
        hex.Font = StudioTheme.Font(11, true);
        hex.AccessibleName = "Hex color";
        hex.CharacterCasing = CharacterCasing.Upper;
        hex.MaxLength = 12;
        hex.Dock = DockStyle.Top;
        hex.Margin = new Padding(9, 7, 9, 0);
        hex.TextChanged += delegate {
            if (writingHex) return;
            Color color;
            if (TryHex(hex.Text, out color)) SetColor(color, false);
            else
            {
                useColor.Enabled = false;
                hexError.Text = "Enter #RGB or #RRGGBB";
            }
        };
        var input = Table(1, 1);
        input.BackColor = StudioTheme.Rail;
        input.Controls.Add(hex, 0, 0);
        var fields = Table(2, 1);
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 109));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        hexError.Dock = DockStyle.Fill;
        hexError.ForeColor = StudioTheme.Warning;
        hexError.Margin = new Padding(10, 0, 0, 0);
        fields.Controls.Add(input, 0, 0);
        fields.Controls.Add(hexError, 1, 0);
        card.Controls.Add(Caption("Hex color"), 0, 3);
        card.Controls.Add(fields, 0, 4);
        return card;
    }

    void SetColor(Color color, bool writeHex)
    {
        int high = Math.Max(color.R, Math.Max(color.G, color.B));
        int low = Math.Min(color.R, Math.Min(color.G, color.B));
        value = high / 255.0;
        saturation = high == 0 ? 0 : (double)(high - low) / high;
        if (high != low) hue = color.GetHue() / 360.0;
        UpdateSelection(color, writeHex);
    }

    void SetFromFields()
    {
        UpdateSelection(FromHsv(hue, saturation, value), true);
    }

    void UpdateSelection(Color color, bool writeHex)
    {
        selected = color;
        selectedChip.BackColor = color;
        selectedChip.AccessibleName = "Selected " + Hex(color);
        plane.ShowValue(hue, saturation, 1 - value);
        hueStrip.ShowValue(hue, hue, 0);
        foreach (var swatch in palette)
        {
            bool active = swatch.BackColor.ToArgb() == color.ToArgb();
            if (active == (swatch.Text.Length != 0)) continue;
            swatch.FlatAppearance.BorderSize = active ? 2 : 1;
            swatch.FlatAppearance.BorderColor = active ? StudioTheme.Accent : StudioTheme.Border;
            swatch.ForeColor = color.GetBrightness() > .52f ? StudioTheme.Canvas : Color.White;
            swatch.Text = active ? "✓" : "";
        }
        if (writeHex)
        {
            writingHex = true;
            hex.Text = Hex(color);
            writingHex = false;
        }
        hexError.Text = "";
        useColor.Enabled = true;
    }

    static bool TryHex(string text, out Color color)
    {
        color = Color.Empty;
        text = text.Trim();
        if (text.StartsWith("#", StringComparison.Ordinal)) text = text.Substring(1);
        if (text.Length != 3 && text.Length != 6) return false;
        int rgb;
        if (!int.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out rgb)) return false;
        if (text.Length == 3)
            rgb = ((rgb >> 8) & 15) * 0x110000 + ((rgb >> 4) & 15) * 0x1100 + (rgb & 15) * 0x11;
        color = Color.FromArgb(unchecked((int)0xFF000000) | rgb);
        return true;
    }

    static string Hex(Color color) { return "#" + (color.ToArgb() & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture); }

    static Color FromHsv(double hue, double saturation, double value)
    {
        double sector = hue * 6 % 6;
        double chroma = value * saturation;
        double middle = chroma * (1 - Math.Abs(sector % 2 - 1));
        double low = value - chroma;
        double r = 0, g = 0, b = 0;
        switch ((int)sector)
        {
            case 0: r = chroma; g = middle; break;
            case 1: r = middle; g = chroma; break;
            case 2: g = chroma; b = middle; break;
            case 3: g = middle; b = chroma; break;
            case 4: r = middle; b = chroma; break;
            case 5: r = chroma; b = middle; break;
        }
        return Color.FromArgb((int)Math.Round((r + low) * 255), (int)Math.Round((g + low) * 255),
            (int)Math.Round((b + low) * 255));
    }

    static bool IsDirection(Keys key) { return key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down; }

    static TableLayoutPanel Table(int columns, int rows)
    {
        return new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = columns, RowCount = rows, Margin = Padding.Empty };
    }

    static TableLayoutPanel Card(int rows, Padding margin)
    {
        var card = Table(1, rows);
        card.BackColor = StudioTheme.Card;
        card.Padding = new Padding(14);
        card.Margin = margin;
        return card;
    }

    static Label Heading(string text, float size)
    {
        var label = StudioTheme.Label(text, size, true);
        label.Dock = DockStyle.Fill;
        label.Margin = Padding.Empty;
        return label;
    }

    static Label Caption(string text)
    {
        var label = StudioTheme.Label(text, 9);
        label.Dock = DockStyle.Fill;
        label.ForeColor = StudioTheme.Muted;
        label.Margin = Padding.Empty;
        return label;
    }

    static Panel ColorChip()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        panel.Paint += delegate(object sender, PaintEventArgs e) {
            using (var pen = new Pen(StudioTheme.Border))
                e.Graphics.DrawRectangle(pen, 0, 0, panel.Width - 1, panel.Height - 1);
        };
        return panel;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        StudioTheme.StyleWindow(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) swatchTip.Dispose();
        base.Dispose(disposing);
    }

    sealed class ColorField : Control
    {
        readonly bool strip;
        double hue, x, y;
        internal event Action<double, double> Changed;

        internal ColorField(bool strip)
        {
            this.strip = strip;
            AccessibleName = strip ? "Hue" : "Saturation and brightness";
            AccessibleDescription = strip ? "Use the left and right arrows to adjust hue."
                : "Use the left and right arrows for saturation and up and down for brightness.";
            TabStop = true;
            Dock = DockStyle.Fill;
            Margin = Padding.Empty;
            Cursor = Cursors.Cross;
            DoubleBuffered = ResizeRedraw = true;
        }

        internal void ShowValue(double baseHue, double selectedX, double selectedY)
        {
            hue = baseHue;
            x = selectedX;
            y = selectedY;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var bounds = Rectangle.Inflate(ClientRectangle, -1, -1);
            if (bounds.Width < 2 || bounds.Height < 2) return;
            Graphics g = e.Graphics;
            if (strip)
            {
                using (var spectrum = new LinearGradientBrush(bounds, Color.Red, Color.Red, LinearGradientMode.Horizontal))
                {
                    spectrum.InterpolationColors = new ColorBlend {
                        Colors = new[] { Color.Red, Color.Yellow, Color.Lime, Color.Cyan, Color.Blue, Color.Magenta, Color.Red },
                        Positions = new[] { 0f, 1f / 6, 2f / 6, .5f, 4f / 6, 5f / 6, 1f }
                    };
                    g.FillRectangle(spectrum, bounds);
                }
            }
            else
            {
                using (var tint = new SolidBrush(FromHsv(hue, 1, 1))) g.FillRectangle(tint, bounds);
                using (var light = new LinearGradientBrush(bounds, Color.White, Color.FromArgb(0, Color.White), LinearGradientMode.Horizontal))
                using (var dark = new LinearGradientBrush(bounds, Color.FromArgb(0, Color.Black), Color.Black, LinearGradientMode.Vertical))
                {
                    g.FillRectangle(light, bounds);
                    g.FillRectangle(dark, bounds);
                }
            }
            using (var frame = new Pen(Focused ? StudioTheme.Accent : StudioTheme.Border))
                g.DrawRectangle(frame, 0, 0, Width - 1, Height - 1);

            float mx = bounds.Left + (float)x * (bounds.Width - 1);
            float my = bounds.Top + (float)y * (bounds.Height - 1);
            int inset = strip ? 2 : 7;
            mx = Math.Max(bounds.Left + inset, Math.Min(bounds.Right - inset, mx));
            my = Math.Max(bounds.Top + inset, Math.Min(bounds.Bottom - inset, my));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var dark = new Pen(StudioTheme.Canvas, 4))
            using (var light = new Pen(Color.White, 2))
            {
                if (strip)
                {
                    g.DrawLine(dark, mx, bounds.Top + 3, mx, bounds.Bottom - 4);
                    g.DrawLine(light, mx, bounds.Top + 3, mx, bounds.Bottom - 4);
                }
                else
                {
                    g.DrawEllipse(dark, mx - 5, my - 5, 10, 10);
                    g.DrawEllipse(light, mx - 5, my - 5, 10, 10);
                }
            }
        }

        void PickAt(Point point)
        {
            Change((point.X - 1.0) / Math.Max(1, Width - 3), strip ? 0 : (point.Y - 1.0) / Math.Max(1, Height - 3));
        }

        void Change(double nextX, double nextY)
        {
            var changed = Changed;
            if (changed != null)
                changed(Math.Max(0, Math.Min(1, nextX)), Math.Max(0, Math.Min(1, nextY)));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Focus();
            Capture = true;
            PickAt(e.Location);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (Capture && e.Button == MouseButtons.Left) PickAt(e.Location);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && Capture)
            {
                PickAt(e.Location);
                Capture = false;
            }
            base.OnMouseUp(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return IsDirection(keyData & Keys.KeyCode) || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (IsDirection(e.KeyCode))
            {
                double step = e.Shift ? .1 : .02;
                double nextX = x, nextY = y;
                if (e.KeyCode == Keys.Left) nextX -= step;
                if (e.KeyCode == Keys.Right) nextX += step;
                if (!strip && e.KeyCode == Keys.Up) nextY -= step;
                if (!strip && e.KeyCode == Keys.Down) nextY += step;
                Change(nextX, nextY);
                e.SuppressKeyPress = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnEnter(EventArgs e) { base.OnEnter(e); Invalidate(); }
        protected override void OnLeave(EventArgs e) { base.OnLeave(e); Invalidate(); }
    }
}

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

sealed class LightbarPage : UserControl
{
    const int PreviewCharge = 75;
    static readonly int PreviewBand = Array.IndexOf(LightbarSettings.Thresholds, PreviewCharge);
    readonly CheckBox controlLightbar = new LightbarSwitch();
    readonly Button fixedMode = ModeButton("Fixed color");
    readonly Button batteryMode = ModeButton("Battery level");
    readonly LightbarColorButton fixedColor = new LightbarColorButton("Fixed lightbar color");
    readonly LightbarColorButton[] bandColors = new LightbarColorButton[LightbarSettings.Thresholds.Length];
    readonly LightbarSlider brightness = new LightbarSlider();
    readonly LightbarSample sample = new LightbarSample();
    readonly Label percent = StudioTheme.Label("", 11, true);
    readonly Label hint = StudioTheme.Label("", 9, false);
    readonly Label status = StudioTheme.Label("", 9, false);
    readonly Button save = StudioTheme.Button("Apply lightbar", true);
    bool automatic;

    public LightbarPage(LightbarSettings current, Func<LightbarSettings, Task> apply)
    {
        Dock = DockStyle.Fill;
        BackColor = StudioTheme.Canvas;
        ForeColor = StudioTheme.Text;
        Font = StudioTheme.Font(10, false);
        automatic = current.Automatic;

        var layout = Grid(1, 3);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 53));
        Controls.Add(layout);

        var controlCard = new LightbarCard { Padding = new Padding(16, 5, 16, 5) };
        layout.Controls.Add(controlCard, 0, 0);
        var controlRow = Grid(2, 1);
        controlRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        controlRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
        controlCard.Controls.Add(controlRow);

        var controlText = Grid(1, 2);
        controlText.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        controlText.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        controlText.Controls.Add(Heading("Lightbar control", 12), 0, 0);
        controlText.Controls.Add(Secondary("Turn off to let Steam or DS4Windows control the lightbar."), 0, 1);
        controlRow.Controls.Add(controlText, 0, 0);
        controlLightbar.Checked = current.Enabled;
        controlLightbar.AccessibleName = "Control lightbar from this app";
        controlLightbar.Dock = DockStyle.Fill;
        controlLightbar.Margin = Padding.Empty;
        controlRow.Controls.Add(controlLightbar, 1, 0);

        var body = Grid(2, 1);
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 47));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 53));
        body.Margin = new Padding(0, 12, 0, 0);
        layout.Controls.Add(body, 0, 1);

        var colorCard = new LightbarCard { Margin = new Padding(0, 0, 6, 0) };
        var colorLayout = Grid(1, 8);
        colorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
        colorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        colorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        colorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        colorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 39));
        colorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
        colorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        colorLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        colorCard.Controls.Add(colorLayout);
        body.Controls.Add(colorCard, 0, 0);

        colorLayout.Controls.Add(Heading("Color mode", 12), 0, 0);
        var mode = Grid(2, 1);
        mode.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        mode.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        fixedMode.Margin = new Padding(0, 0, 4, 4);
        batteryMode.Margin = new Padding(4, 0, 0, 4);
        mode.Controls.Add(fixedMode, 0, 0);
        mode.Controls.Add(batteryMode, 1, 0);
        colorLayout.Controls.Add(mode, 0, 1);

        colorLayout.Controls.Add(Secondary("Fixed color"), 0, 2);
        fixedColor.Value = current.ManualColor;
        fixedColor.AccessibleName = "Choose fixed lightbar color";
        colorLayout.Controls.Add(fixedColor, 0, 3);

        var level = Grid(2, 1);
        level.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        level.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 53));
        level.Controls.Add(Heading("Brightness", 11), 0, 0);
        percent.ForeColor = StudioTheme.Accent;
        percent.TextAlign = ContentAlignment.MiddleRight;
        percent.Dock = DockStyle.Fill;
        level.Controls.Add(percent, 1, 0);
        colorLayout.Controls.Add(level, 0, 4);
        brightness.Value = current.Brightness;
        brightness.AccessibleName = "Lightbar brightness";
        brightness.Dock = DockStyle.Fill;
        colorLayout.Controls.Add(brightness, 0, 5);
        hint.Dock = DockStyle.Fill;
        hint.ForeColor = StudioTheme.Muted;
        hint.TextAlign = ContentAlignment.MiddleLeft;
        colorLayout.Controls.Add(hint, 0, 6);
        sample.Dock = DockStyle.Fill;
        sample.Margin = new Padding(0, 5, 0, 0);
        colorLayout.Controls.Add(sample, 0, 7);

        var bands = new LightbarCard { Margin = new Padding(6, 0, 0, 0) };
        body.Controls.Add(bands, 1, 0);
        var bandLayout = Grid(2, bandColors.Length + 2);
        bandLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
        bandLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
        bandLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
        bandLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        var bandHeading = Heading("Battery colors", 12);
        bandLayout.Controls.Add(bandHeading, 0, 0);
        bandLayout.SetColumnSpan(bandHeading, 2);
        var description = Secondary("The highest matching level sets the color.");
        bandLayout.Controls.Add(description, 0, 1);
        bandLayout.SetColumnSpan(description, 2);
        for (int index = 0; index != bandColors.Length; index++)
        {
            bandLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / bandColors.Length));
            int threshold = LightbarSettings.Thresholds[index];
            string name = threshold == 0 ? "Below " + LightbarSettings.Thresholds[index - 1] + "%" : threshold + "% and up";
            if (threshold == 100) name = "100%";
            var label = Heading(name, 10);
            bandLayout.Controls.Add(label, 0, index + 2);
            var swatch = new LightbarColorButton("Lightbar color · " + name) {
                Value = current.Colors[index], AccessibleName = "Choose lightbar color for " + name
            };
            swatch.Margin = new Padding(4, 3, 0, 3);
            swatch.ValueChanged += delegate { RefreshState(); };
            bandColors[index] = swatch;
            bandLayout.Controls.Add(swatch, 1, index + 2);
        }
        bands.Controls.Add(bandLayout);

        var footer = Grid(2, 1);
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148));
        status.Dock = DockStyle.Fill;
        status.ForeColor = StudioTheme.Muted;
        status.TextAlign = ContentAlignment.MiddleLeft;
        status.AutoEllipsis = true;
        footer.Controls.Add(status, 0, 0);
        save.Dock = DockStyle.Fill;
        save.Margin = new Padding(4, 11, 0, 3);
        footer.Controls.Add(save, 1, 0);
        layout.Controls.Add(footer, 0, 2);

        controlLightbar.CheckedChanged += delegate { RefreshState(); };
        fixedMode.Click += delegate { automatic = false; RefreshState(); };
        batteryMode.Click += delegate { automatic = true; RefreshState(); };
        brightness.ValueChanged += delegate { RefreshState(); };
        fixedColor.ValueChanged += delegate { RefreshState(); };
        save.Click += async delegate { await Apply(apply); };
        RefreshState();
    }

    LightbarSettings Read()
    {
        var settings = new LightbarSettings {
            Enabled = controlLightbar.Checked, Automatic = automatic,
            Brightness = brightness.Value, ManualColor = fixedColor.Value
        };
        for (int index = 0; index < bandColors.Length; index++) settings.Colors[index] = bandColors[index].Value;
        return settings;
    }

    async Task Apply(Func<LightbarSettings, Task> apply)
    {
        var settings = Read();
        save.Enabled = false;
        status.ForeColor = StudioTheme.Muted;
        status.Text = "Applying lightbar settings…";
        try
        {
            await apply(settings);
            if (IsDisposed) return;
            status.ForeColor = StudioTheme.Green;
            status.Text = !settings.Enabled ? "Saved. Other apps can control the lightbar."
                : settings.Automatic ? "Saved. The lightbar will follow the battery level."
                : "Saved. The color applies when the controller connects.";
        }
        catch (Exception error)
        {
            if (IsDisposed) return;
            status.ForeColor = Color.LightSalmon;
            status.Text = error.Message;
        }
        finally
        {
            if (!IsDisposed) save.Enabled = true;
        }
    }

    void RefreshState()
    {
        bool enabled = controlLightbar.Checked;
        fixedMode.Enabled = batteryMode.Enabled = brightness.Enabled = enabled;
        fixedColor.Enabled = enabled && !automatic;
        foreach (var swatch in bandColors) swatch.Enabled = enabled && automatic;
        StyleMode(fixedMode, !automatic);
        StyleMode(batteryMode, automatic);
        percent.Text = brightness.Value + "%";
        hint.Text = automatic ? "Preview at " + PreviewCharge + "% charge" : "Fixed color preview";
        sample.Color = automatic ? bandColors[PreviewBand].Value : fixedColor.Value;
        sample.Brightness = enabled ? brightness.Value : 0;
        sample.Invalidate();
    }

    static TableLayoutPanel Grid(int columns, int rows)
    {
        var table = new TableLayoutPanel { ColumnCount = columns, RowCount = rows, Dock = DockStyle.Fill,
            Margin = Padding.Empty, BackColor = Color.Transparent };
        if (rows == 1) table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        return table;
    }

    static Label Heading(string text, float size)
    {
        var result = StudioTheme.Label(text, size, true);
        result.Dock = DockStyle.Fill;
        result.Margin = Padding.Empty;
        result.TextAlign = ContentAlignment.MiddleLeft;
        result.AutoEllipsis = true;
        return result;
    }

    static Label Secondary(string text)
    {
        var result = Heading(text, 9);
        result.Font = StudioTheme.Font(9, false);
        result.ForeColor = StudioTheme.Muted;
        return result;
    }

    static Button ModeButton(string text)
    {
        var button = new Button { Text = text, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false, Font = StudioTheme.Font(9, true), Cursor = Cursors.Hand };
        return button;
    }

    static void StyleMode(Button button, bool selected)
    {
        button.BackColor = selected ? Color.FromArgb(54, 44, 83) : Color.FromArgb(22, 29, 43);
        button.ForeColor = selected && button.Enabled ? StudioTheme.Accent : StudioTheme.Muted;
        button.FlatAppearance.BorderColor = selected ? StudioTheme.Accent : StudioTheme.Border;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(59, 48, 89);
    }

    internal static GraphicsPath Rounded(RectangleF rectangle, float radius)
    {
        var path = new GraphicsPath();
        float diameter = radius * 2;
        if (radius <= 0 || rectangle.Width <= diameter || rectangle.Height <= diameter)
        {
            path.AddRectangle(rectangle);
            return path;
        }
        path.AddArc(rectangle.X, rectangle.Y, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Y, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.X, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    sealed class LightbarCard : Panel
    {
        public LightbarCard()
        {
            Dock = DockStyle.Fill;
            Margin = Padding.Empty;
            Padding = new Padding(15);
            BackColor = StudioTheme.Card;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(StudioTheme.Canvas);
            if (Width < 2 || Height < 2) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var shape = Rounded(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 13))
            using (var fill = new SolidBrush(StudioTheme.Card))
            using (var edge = new Pen(StudioTheme.Border))
            {
                e.Graphics.FillPath(fill, shape);
                e.Graphics.DrawPath(edge, shape);
            }
        }
    }

    sealed class LightbarSwitch : CheckBox
    {
        public LightbarSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = StudioTheme.Card;
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rail = new RectangleF((Width - 42) / 2f, (Height - 24) / 2f, 42, 24);
            using (var track = Rounded(rail, 11))
            using (var fill = new SolidBrush(Checked ? StudioTheme.Accent : StudioTheme.Border))
            using (var thumb = new SolidBrush(Checked ? StudioTheme.Canvas : StudioTheme.Muted))
            {
                e.Graphics.FillPath(fill, track);
                e.Graphics.FillEllipse(thumb, Checked ? rail.Right - 19 : rail.X + 3, rail.Y + 4, 16, 16);
            }
            if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(Rectangle.Round(rail), 3, 3));
        }

        protected override void OnCheckedChanged(EventArgs e)
        {
            base.OnCheckedChanged(e);
            Invalidate();
        }
    }

    sealed class LightbarSlider : Control
    {
        int value;
        public event EventHandler ValueChanged;

        public int Value
        {
            get { return value; }
            set
            {
                int next = Math.Max(0, Math.Min(100, value));
                if (next == this.value) return;
                this.value = next;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        public LightbarSlider()
        {
            SetStyle(ControlStyles.Selectable | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);
            TabStop = true;
            BackColor = StudioTheme.Card;
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float x = 9, y = Height / 2f, length = Math.Max(1, Width - 18);
            float thumb = x + length * Value / 100f;
            using (var track = new Pen(StudioTheme.Border, 5))
            using (var fill = new Pen(Enabled ? StudioTheme.Accent : StudioTheme.Muted, 5))
            using (var knob = new SolidBrush(Enabled ? StudioTheme.Accent : StudioTheme.Muted))
            {
                track.StartCap = track.EndCap = fill.StartCap = fill.EndCap = LineCap.Round;
                e.Graphics.DrawLine(track, x, y, x + length, y);
                if (Value > 0) e.Graphics.DrawLine(fill, x, y, thumb, y);
                e.Graphics.FillEllipse(knob, thumb - 6, y - 6, 12, 12);
            }
            if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Focus();
            Capture = true;
            SetFromMouse(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (Capture) SetFromMouse(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left) Capture = false;
        }

        void SetFromMouse(int x)
        {
            Value = (int)Math.Round((x - 9) * 100.0 / Math.Max(1, Width - 18));
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            return key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down ||
                key == Keys.Home || key == Keys.End || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Left: case Keys.Down: Value -= 5; break;
                case Keys.Right: case Keys.Up: Value += 5; break;
                case Keys.Home: Value = 0; break;
                case Keys.End: Value = 100; break;
                default: base.OnKeyDown(e); return;
            }
            e.Handled = true;
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    }

    sealed class LightbarColorButton : Button
    {
        readonly string pickerTitle;
        Color value;
        public event EventHandler ValueChanged;

        public Color Value
        {
            get { return value; }
            set
            {
                if (this.value == value) return;
                this.value = value;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        public LightbarColorButton(string pickerTitle)
        {
            this.pickerTitle = pickerTitle;
            Dock = DockStyle.Fill;
            Margin = new Padding(0, 0, 0, 3);
            FlatStyle = FlatStyle.Flat;
            Cursor = Cursors.Hand;
            BackColor = StudioTheme.Card;
            Font = StudioTheme.Font(9, false);
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnClick(EventArgs e)
        {
            Color selected;
            if (StudioColorPicker.TryPick(FindForm(), pickerTitle, Value, out selected)) Value = selected;
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            if (Width < 2 || Height < 2) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool hover = Enabled && ClientRectangle.Contains(PointToClient(Cursor.Position));
            using (var outline = Rounded(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 7))
            using (var fill = new SolidBrush(hover ? Color.FromArgb(42, 49, 73) : Color.FromArgb(23, 30, 43)))
            using (var border = new Pen(hover || Focused ? StudioTheme.Accent : StudioTheme.Border))
            using (var swatch = new SolidBrush(Enabled ? Value : Color.FromArgb(
                (Value.R + StudioTheme.Card.R * 2) / 3, (Value.G + StudioTheme.Card.G * 2) / 3, (Value.B + StudioTheme.Card.B * 2) / 3)))
            {
                g.FillPath(fill, outline);
                g.DrawPath(border, outline);
                using (var tile = Rounded(new RectangleF(9, (Height - 15) / 2f, 22, 15), 4)) g.FillPath(swatch, tile);
            }
            string hex = string.Format("#{0:X2}{1:X2}{2:X2}", Value.R, Value.G, Value.B);
            TextRenderer.DrawText(g, hex, Font, new Rectangle(39, 0, Math.Max(1, Width - 61), Height),
                Enabled ? StudioTheme.Text : StudioTheme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "›", Font, new Rectangle(Width - 23, 0, 15, Height),
                StudioTheme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
        }
    }

    sealed class LightbarSample : Control
    {
        public Color Color;
        public int Brightness;
        public LightbarSample()
        {
            BackColor = StudioTheme.Card;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            if (Width < 10 || Height < 10) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int r = Color.R * Brightness / 100, g = Color.G * Brightness / 100, b = Color.B * Brightness / 100;
            Color glow = Color.FromArgb(r, g, b);
            float railWidth = Math.Min(180, Width * 0.70f);
            var rail = new RectangleF((Width - railWidth) / 2, Height / 2f - 3, railWidth, 6);
            using (var frame = Rounded(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 10))
            using (var frameFill = new SolidBrush(Color.FromArgb(18, 24, 37)))
            using (var edge = new Pen(StudioTheme.Border))
            {
                e.Graphics.FillPath(frameFill, frame);
                e.Graphics.DrawPath(edge, frame);
            }
            for (int halo = 8; Brightness > 0 && halo > 0; halo -= 2)
            {
                using (var shape = Rounded(RectangleF.Inflate(rail, halo, halo), 3 + halo))
                using (var fill = new SolidBrush(Color.FromArgb(12, glow)))
                    e.Graphics.FillPath(fill, shape);
            }
            using (var shape = Rounded(rail, 2.9f))
            using (var fill = new SolidBrush(Brightness > 0 ? glow : StudioTheme.Border))
                e.Graphics.FillPath(fill, shape);
        }
    }
}

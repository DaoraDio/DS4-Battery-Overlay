using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

static class StudioTheme
{
    public static readonly Color Canvas = Color.FromArgb(17, 22, 34);
    public static readonly Color Rail = Color.FromArgb(21, 27, 42);
    public static readonly Color Card = Color.FromArgb(27, 34, 50);
    public static readonly Color Border = Color.FromArgb(48, 58, 80);
    public static readonly Color Text = Color.FromArgb(241, 244, 251);
    public static readonly Color Muted = Color.FromArgb(164, 175, 197);
    public static readonly Color Accent = Color.FromArgb(169, 139, 255);
    public static readonly Color Green = Color.FromArgb(155, 227, 122);
    public static readonly Color Warning = Color.FromArgb(255, 189, 141);
    public static readonly Color Selection = Color.FromArgb(49, 41, 76);

    public static Font Font(float size, bool bold = false)
    {
        return new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular);
    }

    public static Label Label(string text, float size = 10, bool bold = false)
    {
        return new Label { Text = text, ForeColor = Text, Font = Font(size, bold), AutoSize = false,
            BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft };
    }

    public static Button Button(string text, bool primary = false, bool repeat = false)
    {
        Button button = repeat ? new StudioRepeatButton() : new Button();
        button.Text = text;
        button.AutoSize = true;
        button.FlatStyle = FlatStyle.Flat;
        button.Cursor = Cursors.Hand;
        button.ForeColor = primary ? Canvas : Text;
        button.BackColor = primary ? Accent : Card;
        button.Font = Font(9, true);
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.UseVisualStyleBackColor = false;
        button.Padding = new Padding(11, 2, 11, 2);
        button.MinimumSize = new Size(78, 34);
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(194, 174, 255) : Border;
        return button;
    }

    public static void FillRow(TableLayoutPanel table, int height)
    {
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
    }

    public static bool Confirm(IWin32Window owner, string title, string question, string detail,
        string action, Size clientSize)
    {
        using (var dialog = new Form {
            Text = title, ClientSize = clientSize, BackColor = Canvas, ForeColor = Text, Font = Font(9),
            FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            ShowInTaskbar = false, MinimizeBox = false, MaximizeBox = false,
            AutoScaleDimensions = new SizeF(96, 96), AutoScaleMode = AutoScaleMode.Dpi
        })
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16, 10, 16, 12),
                RowCount = 3, ColumnCount = 1 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            FillRow(layout, 34);
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            FillRow(layout, 40);
            var heading = Label(question, 14, true);
            var description = Label(detail, 9);
            description.ForeColor = Muted;
            heading.Dock = description.Dock = DockStyle.Fill;
            heading.Margin = description.Margin = Padding.Empty;
            layout.Controls.Add(heading, 0, 0);
            layout.Controls.Add(description, 0, 1);

            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = false,
                FlowDirection = FlowDirection.RightToLeft };
            var confirm = Button(action, true);
            var cancel = Button("Cancel");
            confirm.DialogResult = DialogResult.OK;
            cancel.DialogResult = DialogResult.Cancel;
            actions.Controls.Add(confirm);
            actions.Controls.Add(cancel);
            layout.Controls.Add(actions, 0, 2);
            dialog.Controls.Add(layout);
            dialog.AcceptButton = dialog.CancelButton = cancel;
            dialog.ActiveControl = cancel;
            StyleWindow(dialog);
            return dialog.ShowDialog(owner) == DialogResult.OK;
        }
    }

    public static Icon LoadIcon()
    {
        using (var resource = typeof(StudioTheme).Assembly.GetManifestResourceStream("DS4StudioIcon"))
            return resource == null ? (Icon)SystemIcons.Information.Clone() : new Icon(resource);
    }

    public static Bitmap LoadMark()
    {
        using (var resource = typeof(StudioTheme).Assembly.GetManifestResourceStream("DS4StudioMark"))
        {
            if (resource == null) return SystemIcons.Information.ToBitmap();
            using (var source = Image.FromStream(resource)) return new Bitmap(source);
        }
    }

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr handle, uint attribute, ref int value, uint size);

    public static void StyleWindow(Form form)
    {
        int enabled = 1;
        try { DwmSetWindowAttribute(form.Handle, 20, ref enabled, 4); }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    public static GraphicsPath RoundedPath(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float diameter = radius * 2;
        path.AddArc(r.X, r.Y, diameter, diameter, 180, 90);
        path.AddArc(r.Right - diameter, r.Y, diameter, diameter, 270, 90);
        path.AddArc(r.Right - diameter, r.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(r.X, r.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void PaintPreview(Graphics graphics, Size size, Color surround, Action<Graphics> paintContent)
    {
        graphics.Clear(surround);
        int width = size.Width, height = size.Height;
        if (width < 24 || height < 24) return;
        var bounds = new Rectangle(0, 0, width - 1, height - 1);
        var state = graphics.Save();
        using (var outline = RoundedPath(bounds, 11))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var frame = graphics.Save();
            graphics.SetClip(outline, CombineMode.Intersect);
            using (var shade = new LinearGradientBrush(bounds,
                Color.FromArgb(18, 26, 48), Color.FromArgb(42, 40, 75), 47f))
                graphics.FillRectangle(shade, bounds);
            int horizon = height * 57 / 100;
            using (var sky = new SolidBrush(Color.FromArgb(29, 35, 62)))
                graphics.FillPolygon(sky, new[] {
                    new Point(0, horizon + 16), new Point(width / 4, horizon - 11),
                    new Point(width * 3 / 8, horizon + 2), new Point(width * 3 / 4, horizon - 31),
                    new Point(width, horizon + 9), new Point(width, height), new Point(0, height)
                });
            using (var grid = new Pen(Color.FromArgb(47, 156, 151, 215)))
            {
                for (int x = 18; x <= width + 24; x += 26)
                    graphics.DrawLine(grid, width / 2 + (x - width / 2) / 3, horizon + 15, x, height);
                for (int y = horizon + 19; y < height; y += 14)
                    graphics.DrawLine(grid, 0, y, width, y);
            }
            paintContent(graphics);
            graphics.Restore(frame);
            using (var border = new Pen(Border)) graphics.DrawPath(border, outline);
        }
        graphics.Restore(state);
    }
}

sealed class StudioRepeatButton : Button
{
    const int HoldDelay = 350;
    const int RepeatInterval = 65;
    readonly Timer timer = new Timer();
    bool repeated;
    bool suppressRelease;

    public StudioRepeatButton()
    {
        timer.Tick += delegate {
            if (!Capture || !Enabled || !Visible)
            {
                timer.Stop();
                return;
            }
            repeated = true;
            timer.Interval = RepeatInterval;
            base.OnClick(EventArgs.Empty);
        };
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !Capture || !Enabled) return;
        repeated = false;
        timer.Interval = HoldDelay;
        timer.Start();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            timer.Stop();
            // Button raises its ordinary Click during mouse-up for this flat style.
            suppressRelease = repeated;
        }
        try { base.OnMouseUp(e); }
        finally { suppressRelease = false; }
    }

    protected override void OnClick(EventArgs e)
    {
        if (!suppressRelease) base.OnClick(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!ClientRectangle.Contains(e.Location)) timer.Stop();
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        timer.Stop();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        if (!Capture) timer.Stop();
        base.OnMouseCaptureChanged(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        timer.Stop();
        base.OnLostFocus(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        if (!Enabled) timer.Stop();
        base.OnEnabledChanged(e);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        if (!Visible) timer.Stop();
        base.OnVisibleChanged(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) timer.Dispose();
        base.Dispose(disposing);
    }
}

sealed class StudioTrayColors : ProfessionalColorTable
{
    public StudioTrayColors() { UseSystemColors = false; }
    public override Color ToolStripDropDownBackground { get { return StudioTheme.Card; } }
    public override Color ImageMarginGradientBegin { get { return StudioTheme.Card; } }
    public override Color ImageMarginGradientMiddle { get { return StudioTheme.Card; } }
    public override Color ImageMarginGradientEnd { get { return StudioTheme.Card; } }
    public override Color MenuBorder { get { return StudioTheme.Border; } }
    public override Color MenuItemBorder { get { return StudioTheme.Selection; } }
    public override Color MenuItemSelected { get { return StudioTheme.Selection; } }
    public override Color SeparatorDark { get { return StudioTheme.Border; } }
    public override Color SeparatorLight { get { return StudioTheme.Border; } }
}

sealed class StudioPositionPicker : TableLayoutPanel
{
    static readonly string[] captions = { "Top right", "Top left", "Bottom right", "Bottom left", "Top center", "Bottom center" };
    static readonly int[] order = { 1, 4, 0, 3, 5, 2 };
    readonly Button[] buttons = new Button[6];
    int selected = -1;
    internal event EventHandler PositionChanged;

    internal StudioPositionPicker(string name)
    {
        AccessibleName = name;
        ColumnCount = 3;
        RowCount = 2;
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
        for (int column = 0; column < 3; column++) ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        for (int row = 0; row < 2; row++) RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        for (int slot = 0; slot < order.Length; slot++)
        {
            int id = order[slot];
            var button = StudioTheme.Button(captions[id]);
            button.Dock = DockStyle.Fill;
            button.Padding = Padding.Empty;
            button.MinimumSize = Size.Empty;
            button.Margin = new Padding(0, 3, 5, 3);
            button.Font = StudioTheme.Font(8);
            button.Click += delegate { SelectedPosition = id; };
            buttons[id] = button;
            Controls.Add(button, slot % 3, slot / 3);
        }
        SelectedPosition = 0;
    }

    internal int SelectedPosition
    {
        get { return selected; }
        set
        {
            if (selected == value) return;
            selected = value;
            for (int i = 0; i < buttons.Length; i++)
            {
                bool active = i == selected;
                buttons[i].BackColor = active ? StudioTheme.Selection : StudioTheme.Rail;
                buttons[i].ForeColor = active ? StudioTheme.Accent : StudioTheme.Text;
                buttons[i].FlatAppearance.BorderColor = active ? StudioTheme.Accent : StudioTheme.Border;
            }
            if (PositionChanged != null) PositionChanged(this, EventArgs.Empty);
        }
    }
}

sealed class StudioSwitch : CheckBox
{
    public StudioSwitch(string label)
    {
        Text = label;
        Font = StudioTheme.Font(9);
        ForeColor = StudioTheme.Text;
        BackColor = StudioTheme.Card;
        TabStop = true;
        Cursor = Cursors.Hand;
        AutoSize = false;
        DoubleBuffered = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int y = (Height - 17) / 2;
        Color track = !Enabled ? StudioTheme.Border : Checked ? StudioTheme.Accent : StudioTheme.Border;
        using (var b = new SolidBrush(track))
        using (var p = StudioTheme.RoundedPath(new RectangleF(0, y, 30, 17), 8.5f))
            g.FillPath(b, p);
        using (var b = new SolidBrush(Checked ? StudioTheme.Canvas : StudioTheme.Muted))
            g.FillEllipse(b, Checked ? 15 : 3, y + 3, 11, 11);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(38, 0, Math.Max(0, Width - 38), Height),
            Enabled ? ForeColor : StudioTheme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(g, new Rectangle(36, 3, Math.Max(0, Width - 39), Height - 6));
    }

    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
}

sealed class StudioNumber : UserControl
{
    readonly int minimum, maximum;
    readonly TextBox input;
    readonly ToolTip tips = new ToolTip();
    int value;
    public event EventHandler ValueChanged;

    public StudioNumber(int min, int max, string field = null)
    {
        minimum = min;
        maximum = max;
        value = min;
        BackColor = StudioTheme.Rail;
        var parts = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
            Margin = Padding.Empty, Padding = new Padding(1), BackColor = StudioTheme.Rail };
        parts.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 27));
        parts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        parts.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 27));
        parts.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(parts);
        input = new TextBox {
            Text = value.ToString(), BackColor = StudioTheme.Rail, ForeColor = StudioTheme.Text,
            Font = StudioTheme.Font(10, true), TextAlign = HorizontalAlignment.Center,
            BorderStyle = BorderStyle.None, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0)
        };
        input.KeyPress += delegate(object sender, KeyPressEventArgs e) {
            e.Handled = !char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar);
        };
        input.KeyDown += delegate(object sender, KeyEventArgs e) {
            if (e.KeyCode != Keys.Up && e.KeyCode != Keys.Down) return;
            Value += e.KeyCode == Keys.Up ? 1 : -1;
            e.SuppressKeyPress = true;
        };
        input.TextChanged += delegate {
            int entered;
            if (int.TryParse(input.Text, out entered) && entered >= minimum && entered <= maximum)
                Set(entered);
        };
        input.Leave += delegate { Value = Value; };
        parts.Controls.Add(Arrow("−", -1, field), 0, 0);
        parts.Controls.Add(input, 1, 0);
        parts.Controls.Add(Arrow("+", 1, field), 2, 0);
    }

    Button Arrow(string caption, int change, string field)
    {
        var button = StudioTheme.Button(caption, repeat: true);
        button.Dock = DockStyle.Fill;
        button.Padding = Padding.Empty;
        button.MinimumSize = Size.Empty;
        button.Margin = new Padding(1);
        button.Font = StudioTheme.Font(10, true);
        button.AccessibleName = (change < 0 ? "Decrease " : "Increase ") + (field ?? "value");
        tips.SetToolTip(button, button.AccessibleName);
        button.Click += delegate { Value += change; };
        return button;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) tips.Dispose();
        base.Dispose(disposing);
    }

    public int Value
    {
        get
        {
            int entered;
            return int.TryParse(input.Text, out entered) ? Math.Max(minimum, Math.Min(maximum, entered)) : value;
        }
        set
        {
            int next = Math.Max(minimum, Math.Min(maximum, value));
            if (input.Text != next.ToString()) input.Text = next.ToString();
            Set(next);
        }
    }

    void Set(int next)
    {
        if (value == next) return;
        value = next;
        var handler = ValueChanged;
        if (handler != null) handler(this, EventArgs.Empty);
    }
}

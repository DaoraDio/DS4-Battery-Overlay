using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

sealed partial class EventLogPage : UserControl
{
    readonly ControllerEventLog log;
    readonly ListBox entries;
    readonly Label empty;
    readonly Label warning = StudioTheme.Label("", 9);
    readonly Button clear = StudioTheme.Button("Clear events");
    readonly Button filter = StudioTheme.Button("Filter ▾");
    readonly ContextMenuStrip filterMenu;
    readonly ToolStripMenuItem[] filterItems;
    readonly RowStyle warningRow = new RowStyle(SizeType.Absolute, 0);
    readonly Font rowFont = StudioTheme.Font(10);
    int timeWidth;
    ControllerEvent pressedDelete;
    static readonly Color deleteColor = Color.FromArgb(255, 110, 125);
    static readonly string[] eventPrefixes = {
        "DualShock 4 connected", "DualShock 4 disconnected", "Battery reading", "Battery changed",
        "Charging started", "Charging stopped", "Charging complete"
    };

    internal EventLogPage(ControllerEventLog log)
    {
        this.log = log;
        BackColor = StudioTheme.Canvas;
        Margin = Padding.Empty;

        var frame = new Panel { Dock = DockStyle.Fill, BackColor = StudioTheme.Border, Padding = new Padding(1) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        StudioTheme.FillRow(layout, 43);
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(warningRow);
        Controls.Add(frame);
        frame.Controls.Add(layout);

        var header = new TableLayoutPanel {
            BackColor = StudioTheme.Card, Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, Margin = Padding.Empty
        };
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 192));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 117));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
        var localTime = ColumnHeading("Local time");
        header.Controls.Add(localTime, 0, 0);
        header.Controls.Add(ColumnHeading("Event"), 1, 0);
        clear.Dock = DockStyle.Fill;
        clear.Margin = new Padding(0, 4, 12, 4);
        clear.Click += delegate { ClearEvents(); };
        header.Controls.Add(clear, 4, 0);
        AddExportButton(header);
        filter.Dock = DockStyle.Fill;
        filter.Margin = new Padding(0, 4, 8, 4);
        filter.AccessibleName = "Filter events by type";
        header.Controls.Add(filter, 2, 0);
        filterMenu = new ContextMenuStrip {
            BackColor = StudioTheme.Card, ForeColor = StudioTheme.Text,
            Font = StudioTheme.Font(9), ShowImageMargin = false, ShowCheckMargin = true,
            Renderer = new FilterMenuRenderer()
        };
        filterItems = new[] { "Connected", "Disconnected", "Battery readings", "Battery changes",
            "Charging started", "Charging stopped", "Charging complete", "Other" }
            .Select(name => new ToolStripMenuItem(name) { CheckOnClick = true, Checked = true }).ToArray();
        filterMenu.Items.AddRange(filterItems);
        filterMenu.Closing += delegate(object sender, ToolStripDropDownClosingEventArgs e) {
            if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked) e.Cancel = true;
        };
        foreach (var item in filterItems)
            item.CheckedChanged += delegate { UpdateEntries(); };
        filter.Click += delegate { filterMenu.Show(filter, new Point(0, filter.Height)); };
        layout.Controls.Add(header, 0, 0);

        var history = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = StudioTheme.Canvas };
        layout.Controls.Add(history, 0, 1);
        entries = new ListBox {
            AccessibleName = "Controller event history",
            AccessibleDescription = "Press Delete to remove the selected event.",
            BackColor = StudioTheme.Canvas, ForeColor = StudioTheme.Text, Font = rowFont,
            BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed,
            Dock = DockStyle.Fill, IntegralHeight = false
        };
        entries.ItemHeight = Math.Max(39, rowFont.Height + 18);
        entries.DrawItem += DrawEntry;
        entries.MouseDown += delegate(object sender, MouseEventArgs e) {
            pressedDelete = e.Button == MouseButtons.Left ? DeleteTarget(e.Location) : null;
        };
        entries.MouseUp += delegate(object sender, MouseEventArgs e) {
            var target = pressedDelete;
            pressedDelete = null;
            if (target != null && e.Button == MouseButtons.Left && ReferenceEquals(target, DeleteTarget(e.Location)))
                RemoveEvent(target);
        };
        entries.MouseMove += delegate(object sender, MouseEventArgs e) {
            entries.Cursor = DeleteTarget(e.Location) == null ? Cursors.Default : Cursors.Hand;
        };
        entries.KeyDown += delegate(object sender, KeyEventArgs e) {
            var row = entries.SelectedItem as Row;
            if (e.KeyCode != Keys.Delete || row == null) return;
            e.SuppressKeyPress = true;
            RemoveEvent(row.Event);
        };
        header.Layout += delegate {
            timeWidth = header.GetColumnWidths()[0];
            entries.Invalidate();
        };
        empty = StudioTheme.Label("No controller events yet.", 11);
        empty.ForeColor = StudioTheme.Muted;
        empty.TextAlign = ContentAlignment.MiddleCenter;
        empty.Dock = DockStyle.Fill;
        history.Controls.Add(entries);
        history.Controls.Add(empty);
        warning.Dock = DockStyle.Fill;
        warning.Margin = Padding.Empty;
        warning.Padding = new Padding(14, 0, 14, 0);
        warning.BackColor = StudioTheme.Card;
        warning.ForeColor = StudioTheme.Warning;
        warning.AutoEllipsis = true;
        layout.Controls.Add(warning, 0, 2);

        log.Changed += UpdateEntries;
        UpdateEntries();
    }

    static Label ColumnHeading(string text)
    {
        var label = StudioTheme.Label(text, 9, true);
        label.ForeColor = StudioTheme.Text;
        label.Padding = new Padding(14, 0, 0, 0);
        label.Margin = Padding.Empty;
        label.Dock = DockStyle.Fill;
        return label;
    }

    void UpdateEntries()
    {
        var selected = entries.SelectedItem as Row;
        int previousTop = entries.Items.Count > 0 ? entries.TopIndex : 0;
        var top = previousTop > 0 ? (Row)entries.Items[previousTop] : null;
        var history = log.Snapshot();
        var snapshot = history.Where(item => filterItems[EventType(item.Message)].Checked).ToArray();
        int enabled = filterItems.Count(item => item.Checked);
        filter.Text = enabled == filterItems.Length ? "Filter ▾" : "Filter (" + enabled + ") ▾";
        int selectedIndex = -1, topIndex = Math.Min(previousTop, Math.Max(0, snapshot.Length - 1));
        entries.BeginUpdate();
        try
        {
            entries.Items.Clear();
            for (int index = 0; index < snapshot.Length; index++)
            {
                var controllerEvent = snapshot[index];
                entries.Items.Add(new Row(controllerEvent));
                if (selected != null && Equals(selected.Event, controllerEvent)) selectedIndex = index;
                if (top != null && Equals(top.Event, controllerEvent)) topIndex = index;
            }
            entries.SelectedIndex = selectedIndex;
            if (snapshot.Length > 0) entries.TopIndex = topIndex;
        }
        finally { entries.EndUpdate(); }
        entries.Visible = snapshot.Length != 0;
        clear.Enabled = history.Length != 0;
        export.Enabled = snapshot.Length != 0;
        empty.Text = history.Length == 0 ? "No controller events yet." : "No events match the selected filters.";
        empty.Visible = snapshot.Length == 0;
        if (empty.Visible) empty.BringToFront();
        warning.Text = log.StorageError ?? "";
        bool hasStorageError = warning.Text.Length != 0;
        warning.Visible = hasStorageError;
        warningRow.Height = hasStorageError ? warning.Font.Height + 14 : 0;
    }

    static int EventType(string message)
    {
        if (message.StartsWith("Controller connected", StringComparison.OrdinalIgnoreCase) ||
            message.StartsWith("Controller reconnected", StringComparison.OrdinalIgnoreCase)) return 0;
        if (message.StartsWith("Controller disconnected", StringComparison.OrdinalIgnoreCase)) return 1;
        if (message.StartsWith("Battery level", StringComparison.OrdinalIgnoreCase)) return 2;
        for (int index = 0; index < eventPrefixes.Length; ++index)
            if (message.StartsWith(eventPrefixes[index], StringComparison.OrdinalIgnoreCase)) return index;
        return eventPrefixes.Length;
    }

    void ClearEvents()
    {
        if (!StudioTheme.Confirm(FindForm(), "Clear events", "Clear all events?",
            "This permanently deletes the saved controller events.", "Clear events", new Size(420, 136))) return;
        try { log.Clear(); }
        catch (Exception error)
        {
            MessageBox.Show(FindForm(), "Could not clear event history. " + error.Message, "Clear events",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    ControllerEvent DeleteTarget(Point location)
    {
        int index = entries.IndexFromPoint(location);
        if (index < 0 || !DeleteArea(entries.GetItemRectangle(index)).Contains(location)) return null;
        return ((Row)entries.Items[index]).Event;
    }

    void RemoveEvent(ControllerEvent target)
    {
        try { log.Remove(target); }
        catch (Exception error)
        {
            MessageBox.Show(FindForm(), "Could not delete this event. " + error.Message, "Delete event",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    static Rectangle DeleteArea(Rectangle row)
    {
        int side = Math.Max(22, row.Height - 9);
        return new Rectangle(row.Right - side - 10, row.Top + (row.Height - side) / 2, side, side);
    }

    void DrawEntry(object sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= entries.Items.Count) return;
        var row = (Row)entries.Items[e.Index];
        Rectangle bounds = e.Bounds;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using (var fill = new SolidBrush(selected ? StudioTheme.Selection : StudioTheme.Canvas))
            e.Graphics.FillRectangle(fill, bounds);
        using (var rule = new Pen(StudioTheme.Border))
            e.Graphics.DrawLine(rule, bounds.Left + 14, bounds.Bottom - 1, bounds.Right - 14, bounds.Bottom - 1);

        const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
        var timestampArea = new Rectangle(bounds.Left + 14, bounds.Top, Math.Max(0, timeWidth - 24), bounds.Height);
        var eventArea = new Rectangle(bounds.Left + timeWidth + 14, bounds.Top,
            Math.Max(0, DeleteArea(bounds).Left - bounds.Left - timeWidth - 23), bounds.Height);
        TextRenderer.DrawText(e.Graphics, row.Time, rowFont, timestampArea, StudioTheme.Muted, flags);
        TextRenderer.DrawText(e.Graphics, row.Event.Message, rowFont, eventArea, StudioTheme.Text, flags);
        DrawTrash(e.Graphics, DeleteArea(bounds));
        if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
    }

    static void DrawTrash(Graphics graphics, Rectangle area)
    {
        float x = area.Left + area.Width / 2f, y = area.Top + area.Height / 2f;
        float s = area.Height / 30f;
        var previous = graphics.Save();
        graphics.TranslateTransform(x, y);
        graphics.ScaleTransform(s, s);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var pen = new Pen(deleteColor, 1.5f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            graphics.DrawLines(pen, new[] { new PointF(-5, -3), new PointF(-4, 7), new PointF(4, 7), new PointF(5, -3) });
            graphics.DrawLine(pen, -7, -5, 7, -5);
            graphics.DrawLines(pen, new[] { new PointF(-2, -5), new PointF(-2, -8), new PointF(2, -8), new PointF(2, -5) });
            graphics.DrawLine(pen, -1.5f, -1, -1.5f, 4);
            graphics.DrawLine(pen, 1.5f, -1, 1.5f, 4);
        }
        graphics.Restore(previous);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            log.Changed -= UpdateEntries;
            filterMenu.Dispose();
            rowFont.Dispose();
        }
        base.Dispose(disposing);
    }

    sealed class FilterMenuRenderer : ToolStripProfessionalRenderer
    {
        public FilterMenuRenderer() : base(new StudioTrayColors()) { }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            base.OnRenderMenuItemBackground(e);
            var item = (ToolStripMenuItem)e.Item;
            int size = Math.Min(14, item.Height - 4);
            var box = new Rectangle(5, (item.Height - size) / 2, size, size);
            using (var fill = new SolidBrush(item.Checked ? StudioTheme.Accent : StudioTheme.Canvas))
            using (var border = new Pen(item.Checked ? StudioTheme.Accent : StudioTheme.Muted))
            {
                e.Graphics.FillRectangle(fill, box);
                e.Graphics.DrawRectangle(border, box);
            }
            if (item.Checked)
                using (var mark = new Pen(StudioTheme.Canvas, 1.5f))
                    e.Graphics.DrawLines(mark, new[] { new Point(box.Left + 3, box.Top + 7),
                        new Point(box.Left + 6, box.Top + 10), new Point(box.Right - 3, box.Top + 4) });
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) { }
    }

    sealed class Row
    {
        public readonly ControllerEvent Event;
        public readonly string Time;

        public Row(ControllerEvent controllerEvent)
        {
            Event = controllerEvent;
            Time = controllerEvent.Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        public override string ToString()
        {
            return Time + "   " + Event.Message;
        }
    }
}

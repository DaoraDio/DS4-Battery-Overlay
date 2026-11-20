using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

sealed class PopupsPage : UserControl
{
    readonly StudioPositionPicker windowsPosition = new StudioPositionPicker("Windows popup position");
    readonly StudioPositionPicker gamePosition = new StudioPositionPicker("In-game popup position");
    readonly StudioSwitch automatic = new StudioSwitch("Enable popups");
    readonly StudioNumber popupSize = new StudioNumber(PopupSettings.MinScale, PopupSettings.MaxScale, "popup size");
    readonly StudioNumber testDelay = new StudioNumber(0, 30, "test delay");
    readonly StudioDecimalNumber duration = new StudioDecimalNumber(
        PopupSettings.MinDuration / 1000m, PopupSettings.MaxDuration / 1000m, 0.25m, "popup duration") {
        Value = PopupSettings.DefaultDuration / 1000m
    };
    readonly ComboBox testEvent = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
    Bitmap card = ControllerNotifications.TestImage();
    int previewSize = 100, previewEvent;
    bool previewSegmented = true;
    int runningDelay, runningDuration;
    readonly PopupPreview desktopPreview;
    readonly PopupPreview gamePreview;
    readonly Timer timer = new Timer { Interval = 15 };
    readonly Stopwatch animation = new Stopwatch();
    readonly System.Collections.Generic.Dictionary<int, StudioSwitch> triggers = new System.Collections.Generic.Dictionary<int, StudioSwitch>();

    internal PopupsPage(ControllerNotifications notifications)
    {
        BackColor = StudioTheme.Canvas;
        desktopPreview = new PopupPreview(card, false);
        gamePreview = new PopupPreview(card, true);
        var layout = Grid(1, 5);
        StudioTheme.FillRow(layout, 55);
        StudioTheme.FillRow(layout, 43);
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        StudioTheme.FillRow(layout, 47);
        StudioTheme.FillRow(layout, 53);
        Controls.Add(layout);

        var options = Grid(5, 1);
        options.BackColor = StudioTheme.Card;
        options.Margin = new Padding(0, 0, 0, 9);
        options.Padding = new Padding(14, 5, 14, 5);
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 106));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        options.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        automatic.Dock = DockStyle.Fill;
        automatic.Margin = Padding.Empty;
        var sizeCaption = StudioTheme.Label("Size (%)", 9, true);
        sizeCaption.Dock = DockStyle.Fill;
        sizeCaption.Margin = Padding.Empty;
        popupSize.Dock = DockStyle.Fill;
        popupSize.Margin = Padding.Empty;
        popupSize.AccessibleName = "Popup size in percent";
        options.Controls.Add(automatic, 0, 0);
        options.Controls.Add(sizeCaption, 1, 0);
        options.Controls.Add(popupSize, 2, 0);
        var durationCaption = StudioTheme.Label("Duration (s)", 9, true);
        durationCaption.Dock = DockStyle.Fill;
        durationCaption.Margin = new Padding(12, 0, 0, 0);
        duration.Dock = DockStyle.Fill; duration.Margin = Padding.Empty;
        duration.BackColor = StudioTheme.Rail;
        duration.ForeColor = StudioTheme.Text;
        duration.Font = StudioTheme.Font(10, true);
        duration.AccessibleName = "Popup duration in seconds";
        options.Controls.Add(durationCaption, 3, 0);
        options.Controls.Add(duration, 4, 0);
        layout.Controls.Add(options, 0, 0);

        var surfaces = Grid(2, 1);
        surfaces.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        surfaces.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        surfaces.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        surfaces.Controls.Add(Surface("Windows", windowsPosition, desktopPreview, new Padding(0, 0, 7, 0)), 0, 0);
        surfaces.Controls.Add(Surface("In game", gamePosition, gamePreview, new Padding(7, 0, 0, 0)), 1, 0);
        var views = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        var triggerPanel = BuildTriggers();
        views.Controls.Add(surfaces);
        views.Controls.Add(triggerPanel);
        layout.Controls.Add(views, 0, 2);
        var tabs = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = false };
        var previewTab = StudioTheme.Button("Preview");
        var triggersTab = StudioTheme.Button("Triggers");
        tabs.Controls.Add(previewTab);
        tabs.Controls.Add(triggersTab);
        layout.Controls.Add(tabs, 0, 1);
        Action<bool> selectTriggers = show => {
            surfaces.Visible = !show;
            triggerPanel.Visible = show;
            previewTab.BackColor = show ? StudioTheme.Card : StudioTheme.Selection;
            triggersTab.BackColor = show ? StudioTheme.Selection : StudioTheme.Card;
            previewTab.FlatAppearance.BorderColor = show ? StudioTheme.Border : StudioTheme.Accent;
            triggersTab.FlatAppearance.BorderColor = show ? StudioTheme.Accent : StudioTheme.Border;
        };
        previewTab.Click += delegate { selectTriggers(false); };
        triggersTab.Click += delegate { selectTriggers(true); };
        selectTriggers(false);

        var footer = Grid(3, 1);
        footer.Margin = new Padding(0, 12, 0, 0);
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 167));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var status = StudioTheme.Label("", 9);
        status.Dock = DockStyle.Fill;
        status.Margin = Padding.Empty;
        var test = StudioTheme.Button("Test popup");
        var apply = StudioTheme.Button("Apply popup settings", true);
        foreach (var button in new[] { test, apply })
        {
            button.Dock = DockStyle.Fill;
            button.AutoSize = false;
            button.Margin = new Padding(8, 0, 0, 0);
        }
        footer.Controls.Add(status, 0, 0);
        footer.Controls.Add(test, 1, 0);
        footer.Controls.Add(apply, 2, 0);
        layout.Controls.Add(footer, 0, 4);
        layout.Controls.Add(BuildTestControls(), 0, 3);

        PopupSettings current = notifications == null ? new PopupSettings(2, 2) : notifications.Settings;
        windowsPosition.SelectedPosition = current.Desktop;
        gamePosition.SelectedPosition = current.Game;
        automatic.Checked = current.Enabled;
        popupSize.Value = current.Scale;
        duration.Value = current.Duration / 1000m;
        foreach (var choice in triggers)
        {
            choice.Value.Checked = (current.Triggers & choice.Key) != 0;
            choice.Value.CheckedChanged += delegate { status.Text = ""; };
        }
        EventHandler updatePositions = delegate {
            desktopPreview.Position = windowsPosition.SelectedPosition;
            gamePreview.Position = gamePosition.SelectedPosition;
            int selected = testEvent.SelectedIndex;
            bool segmented = notifications == null || notifications.SegmentedIcon;
            if (!animation.IsRunning &&
                (previewSize != popupSize.Value || previewEvent != selected || previewSegmented != segmented))
            {
                var resized = ControllerNotifications.TestImage(popupSize.Value, selected, segmented);
                desktopPreview.Card = gamePreview.Card = resized;
                card.Dispose();
                card = resized;
                previewSize = popupSize.Value;
                previewEvent = selected;
                previewSegmented = segmented;
            }
            if (!animation.IsRunning) status.Text = "";
            RefreshPreviews(animation.IsRunning
                ? ControllerNotifications.OpacityAt(animation.ElapsedMilliseconds - runningDelay, runningDuration) : 1);
        };
        windowsPosition.PositionChanged += updatePositions;
        gamePosition.PositionChanged += updatePositions;
        popupSize.ValueChanged += updatePositions;
        duration.ValueChanged += updatePositions;
        testEvent.SelectedIndexChanged += updatePositions;
        automatic.CheckedChanged += updatePositions;
        VisibleChanged += updatePositions;
        updatePositions(this, EventArgs.Empty);

        test.Click += delegate {
            PopupSettings settings = ReadSettings();
            int seconds = testDelay.Value;
            runningDelay = seconds * 1000;
            runningDuration = settings.Duration;
            animation.Restart();
            RefreshPreviews(0);
            test.Enabled = false;
            timer.Start();
            if (notifications != null) notifications.ShowTest(settings, testEvent.SelectedIndex, seconds);
        };
        timer.Tick += delegate {
            long elapsed = animation.ElapsedMilliseconds - runningDelay;
            if (elapsed < 0)
            {
                status.Text = "Test in " + ((-elapsed + 999) / 1000) + " s. Switch to the game.";
                return;
            }
            if (elapsed < runningDuration)
            {
                status.Text = "Test popup triggered.";
                RefreshPreviews(ControllerNotifications.OpacityAt(elapsed, runningDuration));
                return;
            }
            timer.Stop();
            animation.Reset();
            updatePositions(this, EventArgs.Empty);
            test.Enabled = true;
        };
        apply.Click += delegate {
            if (notifications == null) return;
            try
            {
                var updated = ReadSettings();
                updated.Save();
                notifications.Apply(updated);
                status.ForeColor = StudioTheme.Green;
                status.Text = "Popup settings saved.";
            }
            catch (Exception error)
            {
                status.ForeColor = StudioTheme.Warning;
                status.Text = error.Message;
            }
        };
    }

    Control BuildTestControls()
    {
        var row = Grid(4, 1);
        row.BackColor = StudioTheme.Card;
        row.Padding = new Padding(14, 6, 14, 6);
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 83));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 102));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var eventLabel = StudioTheme.Label("Test event", 9, true);
        var delayLabel = StudioTheme.Label("Delay (s)", 9, true);
        eventLabel.Dock = delayLabel.Dock = DockStyle.Fill;
        delayLabel.Padding = new Padding(13, 0, 0, 0);
        testEvent.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        testEvent.BackColor = StudioTheme.Rail;
        testEvent.ForeColor = StudioTheme.Text;
        testEvent.Font = StudioTheme.Font(10);
        testEvent.AccessibleName = "Test popup event";
        testEvent.FlatStyle = FlatStyle.Flat;
        testEvent.DrawMode = DrawMode.OwnerDrawFixed;
        testEvent.DrawItem += delegate(object sender, DrawItemEventArgs args) {
            if (args.Index < 0) return;
            bool selected = (args.State & DrawItemState.Selected) != 0;
            using (var fill = new SolidBrush(selected ? StudioTheme.Selection : StudioTheme.Rail))
                args.Graphics.FillRectangle(fill, args.Bounds);
            var text = Rectangle.Inflate(args.Bounds, -4, 0);
            TextRenderer.DrawText(args.Graphics, testEvent.Items[args.Index].ToString(), testEvent.Font, text,
                StudioTheme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        };
        testEvent.Items.AddRange(ControllerNotifications.TestEvents);
        foreach (int level in PopupSettings.BatteryLevels) testEvent.Items.Add("Battery at " + level + "%");
        testEvent.SelectedIndex = 0;
        testDelay.Dock = DockStyle.Fill;
        testDelay.Margin = Padding.Empty;
        testDelay.Value = 3;
        row.Controls.Add(eventLabel, 0, 0);
        row.Controls.Add(testEvent, 1, 0);
        row.Controls.Add(delayLabel, 2, 0);
        row.Controls.Add(testDelay, 3, 0);
        return row;
    }

    PopupSettings ReadSettings()
    {
        int mask = 0;
        foreach (var choice in triggers)
            if (choice.Value.Checked) mask |= choice.Key;
        return new PopupSettings(windowsPosition.SelectedPosition, gamePosition.SelectedPosition,
            popupSize.Value, automatic.Checked, mask, (int)(duration.Value * 1000));
    }

    Control BuildTriggers()
    {
        var layout = Grid(2, 1);
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57));
        var states = TriggerCard("Controller states", "Shown when the controller changes state.", 1, 6);
        states.Margin = new Padding(0, 0, 7, 0);
        string[] labels = { "Connected", "Disconnected",
            "Low battery warning (≤" + ControllerConnection.LowBatteryPercent + "%, unplugged)",
            "Charging started", "Charging stopped", "Charging complete" };
        for (int i = 0; i < labels.Length; i++)
            states.Controls.Add(Trigger(labels[i], (NoticeKind)i), 0, i + 2);
        layout.Controls.Add(states, 0, 0);

        var levels = TriggerCard("Battery percentages", "Shown when the reading changes to a selected level.", 3, (PopupSettings.BatteryLevels.Length + 2) / 3);
        levels.Margin = new Padding(7, 0, 0, 0);
        for (int i = 0; i < PopupSettings.BatteryLevels.Length; i++)
        {
            int level = PopupSettings.BatteryLevels[i];
            levels.Controls.Add(Trigger(level + "%", NoticeKind.BatteryLevel, level), i % 3, 2 + i / 3);
        }
        layout.Controls.Add(levels, 1, 0);
        return layout;
    }

    StudioSwitch Trigger(string label, NoticeKind kind, int level = 0)
    {
        var option = new StudioSwitch(label) { Dock = DockStyle.Fill, Margin = Padding.Empty };
        triggers.Add(PopupSettings.TriggerBit(kind, level), option);
        return option;
    }

    static TableLayoutPanel TriggerCard(string title, string detail, int columns, int rows)
    {
        var card = Grid(columns, rows + 3);
        card.BackColor = StudioTheme.Card;
        card.Padding = new Padding(14);
        for (int i = 0; i < columns; i++) card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
        StudioTheme.FillRow(card, 31);
        StudioTheme.FillRow(card, 40);
        for (int i = 0; i < rows; i++) StudioTheme.FillRow(card, 36);
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var heading = StudioTheme.Label(title, 12, true);
        var description = StudioTheme.Label(detail, 9);
        description.ForeColor = StudioTheme.Muted;
        foreach (var label in new[] { heading, description })
        {
            label.Dock = DockStyle.Fill;
            label.Margin = Padding.Empty;
            card.Controls.Add(label, 0, label == heading ? 0 : 1);
            card.SetColumnSpan(label, columns);
        }
        return card;
    }

    void RefreshPreviews(float opacity)
    {
        desktopPreview.SetOpacity(opacity);
        gamePreview.SetOpacity(opacity);
    }

    static TableLayoutPanel Grid(int columns, int rows)
    {
        return new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = columns, RowCount = rows, Margin = Padding.Empty };
    }

    static Control Surface(string title, StudioPositionPicker position, Control preview, Padding margin)
    {
        var pane = Grid(1, 3);
        pane.BackColor = StudioTheme.Card;
        pane.Padding = new Padding(14);
        pane.Margin = margin;
        StudioTheme.FillRow(pane, 32);
        StudioTheme.FillRow(pane, 87);
        pane.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var heading = StudioTheme.Label(title, 12, true);
        heading.Dock = DockStyle.Fill;
        heading.Margin = Padding.Empty;
        position.Margin = new Padding(0, 0, 0, 5);
        pane.Controls.Add(heading, 0, 0);
        pane.Controls.Add(position, 0, 1);
        pane.Controls.Add(preview, 0, 2);
        return pane;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) timer.Dispose();
        base.Dispose(disposing);
        if (disposing) card.Dispose();
    }

    sealed class PopupPreview : Control
    {
        internal Bitmap Card;
        readonly bool game;
        float opacity = 1;
        internal int Position;

        internal PopupPreview(Bitmap card, bool game)
        {
            Card = card;
            this.game = game;
            AccessibleName = game ? "In-game popup preview" : "Windows popup preview";
            BackColor = StudioTheme.Card;
            Dock = DockStyle.Fill;
            Margin = Padding.Empty;
            DoubleBuffered = ResizeRedraw = true;
        }

        internal void SetOpacity(float value) { opacity = value; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            StudioTheme.PaintPreview(e.Graphics, ClientSize, BackColor, PaintPopup);
        }

        void PaintPopup(Graphics g)
        {
            if (!game)
                using (var taskbar = new SolidBrush(StudioTheme.Rail))
                    g.FillRectangle(taskbar, 0, Math.Max(0, Height - 14), Width, 14);
            if (opacity <= 0) return;

            var screen = new Rectangle(1, 1, Width - 2, Math.Max(1, Height - (game ? 2 : 15)));
            if (screen.Width < 1) return;
            float scale = Math.Min(1, Math.Min((screen.Width - 16f) / Card.Width, (screen.Height - 16f) / Card.Height));
            if (scale <= 0) return;
            var size = new Size((int)(Card.Width * scale), (int)(Card.Height * scale));
            var destination = new Rectangle(PopupSettings.Locate(screen, size, Position, 10), size);
            if (scale == 1 && opacity == 1)
            {
                g.DrawImageUnscaled(Card, destination.Location);
                return;
            }
            using (var attributes = new ImageAttributes())
            {
                var tint = new ColorMatrix { Matrix33 = opacity };
                attributes.SetColorMatrix(tint);
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(Card, destination, 0, 0, Card.Width, Card.Height, GraphicsUnit.Pixel, attributes);
            }
        }
    }
}

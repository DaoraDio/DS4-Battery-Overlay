using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

sealed class StudioStatus
{
    public readonly bool Connected;
    public readonly string Reading;
    public readonly bool Charging;
    public readonly bool Stale;
    public readonly bool CanTurnOff, TurningOff, CableConnected;
    public StudioStatus(bool connected, string reading, bool charging, bool stale = false, bool canTurnOff = false, bool turningOff = false, bool cableConnected = false)
    {
        Connected = connected;
        Reading = reading;
        Charging = charging;
        Stale = stale;
        CanTurnOff = canTurnOff; TurningOff = turningOff; CableConnected = cableConnected;
    }
}

sealed class StudioWindow : Form
{
    internal const int GamesPage = 4;
    static readonly string[] pages = { "Overlay", "Popups", "Lightbar", "Events", "How to Setup" };
    static readonly string[] captions = {
        "Design your game overlay and desktop widget.",
        "Choose where notifications appear and test the fade animation.",
        "Set a fixed color or follow the controller’s battery level.",
        "See when your controller connects, disconnects, or reports a battery change.",
        "Install the RTSS plugin and set up its shortcuts."
    };
    readonly Button[] navigation = new Button[pages.Length];
    readonly Control[] views = new Control[pages.Length];
    readonly OverlayPage overlay;
    readonly LightbarSettings lightbar;
    readonly Func<LightbarSettings, Task> applyLightbar;
    readonly Func<StudioStatus> readStatus;
    readonly ControllerEventLog events;
    readonly ControllerNotifications notifications;
    readonly GameLibrary gameLibrary;
    readonly bool previewMode;
    readonly Label title = StudioTheme.Label("", 24, true);
    readonly Label subtitle = StudioTheme.Label("", 10);
    readonly Label connection = StudioTheme.Label("", 9, true);
    readonly Label battery = StudioTheme.Label("", 19, true);
    readonly BatteryMeter meter = new BatteryMeter();
    readonly ControllerPowerButton powerOff = new ControllerPowerButton();
    readonly ToolTip powerHint = new ToolTip();
    readonly Action turnOffController;
    readonly Panel viewport = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
    readonly Timer refresh = new Timer { Interval = 350 };
    readonly Icon mark;
    readonly Bitmap railMark;
    int current;

    public StudioWindow(OverlayAppearance appearance, LightbarSettings lightbar, GameLibrary gameLibrary,
        Action<OverlayAppearance> applyOverlay, Func<LightbarSettings, Task> applyLightbar,
        Func<StudioStatus> readStatus, ControllerEventLog events, int startPage = 0, bool previewMode = false,
        ControllerNotifications notifications = null, Action turnOffController = null)
    {
        this.gameLibrary = gameLibrary;
        this.lightbar = lightbar;
        this.applyLightbar = applyLightbar;
        this.readStatus = readStatus;
        this.events = events;
        this.notifications = notifications;
        this.turnOffController = turnOffController;
        this.previewMode = previewMode;
        Text = "DS4 Battery Overlay";
        BackColor = StudioTheme.Canvas;
        ForeColor = StudioTheme.Text;
        Font = StudioTheme.Font(9);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        MinimumSize = new Size(1010, 685);
        ClientSize = new Size(1118, 720);
        mark = StudioTheme.LoadIcon();
        railMark = StudioTheme.LoadMark();
        Icon = mark;

        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 226));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(shell);

        var rail = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = pages.Length + 3, ColumnCount = 1,
            Margin = Padding.Empty, Padding = new Padding(18, 21, 18, 21), BackColor = StudioTheme.Rail };
        StudioTheme.FillRow(rail, 83);
        for (int i = 0; i < pages.Length; i++) StudioTheme.FillRow(rail, 50);
        rail.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        StudioTheme.FillRow(rail, 118);
        shell.Controls.Add(rail, 0, 0);

        var brand = new TableLayoutPanel { Dock = DockStyle.Top, Height = 69, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
        brand.RowStyles.Add(new RowStyle(SizeType.Absolute,49));
        brand.RowStyles.Add(new RowStyle(SizeType.Absolute,20));
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var icon = new PictureBox { Image = railMark, SizeMode = PictureBoxSizeMode.Zoom, Dock = DockStyle.Fill, Margin = new Padding(0, 3, 8, 3) };
        brand.Controls.Add(icon, 0, 0);
        var name = StudioTheme.Label("DS4 Battery\nOverlay", 12, true);
        name.Dock = DockStyle.Fill;
        name.Margin = Padding.Empty;
        brand.Controls.Add(name, 1, 0);
        var version = StudioTheme.Label(AppRelease.DisplayVersion, 8);
        version.ForeColor = StudioTheme.Muted;
        version.Dock = DockStyle.Fill;
        version.Margin = Padding.Empty;
        brand.Controls.Add(version, 1, 1);
        rail.Controls.Add(brand, 0, 0);

        for (int i = 0; i < pages.Length; i++)
        {
            int id = i;
            var link = StudioTheme.Button(pages[i]);
            link.Dock = DockStyle.Fill;
            link.Margin = new Padding(0, 2, 0, 5);
            link.TextAlign = ContentAlignment.MiddleLeft;
            link.Padding = new Padding(13, 0, 0, 0);
            link.Font = StudioTheme.Font(10, true);
            link.Click += delegate { SelectPage(id); };
            navigation[i] = link;
            rail.Controls.Add(link, 0, i + 1);
        }

        var status = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = StudioTheme.Card, Padding = new Padding(12, 8, 12, 10),
            ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        status.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        foreach (int h in new[] { 26, 42, 20 }) StudioTheme.FillRow(status, h);
        connection.Margin = battery.Margin = meter.Margin = Padding.Empty;
        connection.Dock = battery.Dock = meter.Dock = DockStyle.Fill;
        status.Controls.Add(connection, 0, 0);
        var batteryRow = new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=2, RowCount=1, Margin=Padding.Empty };
        batteryRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        batteryRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,32));
        batteryRow.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        batteryRow.Controls.Add(battery,0,0);
        powerOff.Dock=DockStyle.Fill; powerOff.Margin=new Padding(2,6,0,6);
        powerOff.Click+=delegate { if(this.turnOffController!=null) { this.turnOffController(); RefreshStatus(); } };
        batteryRow.Controls.Add(powerOff,1,0);
        status.Controls.Add(batteryRow, 0, 1);
        status.Controls.Add(meter, 0, 2);
        rail.Controls.Add(status, 0, pages.Length + 2);

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = StudioTheme.Canvas, Margin = Padding.Empty,
            Padding = new Padding(25, 18, 25, 16), ColumnCount = 1, RowCount = 2 };
        StudioTheme.FillRow(body, 89);
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.Controls.Add(body, 1, 0);

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 1, RowCount = 2 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        StudioTheme.FillRow(header, 47);
        StudioTheme.FillRow(header, 28);
        title.Dock = subtitle.Dock = DockStyle.Fill;
        title.Margin = subtitle.Margin = Padding.Empty;
        subtitle.ForeColor = StudioTheme.Muted;
        header.Controls.Add(title, 0, 0);
        header.Controls.Add(subtitle, 0, 1);
        body.Controls.Add(header, 0, 0);
        body.Controls.Add(viewport, 0, 1);

        overlay = new OverlayPage(appearance, applyOverlay);
        views[0] = overlay;
        viewport.Controls.Add(overlay);
        SelectPage(startPage);
        RefreshStatus();
        refresh.Tick += delegate { RefreshStatus(); };
        if (!previewMode) refresh.Start();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        StudioTheme.StyleWindow(this);
    }

    internal void SelectPage(int page)
    {
        current = page;
        if (views[page] == null)
        {
            if (page == 1) views[page] = new PopupsPage(notifications);
            else if (page == 2) views[page] = new LightbarPage(lightbar, applyLightbar);
            else if (page == GamesPage) views[page] = new RtssPage();
            else views[page] = new EventLogPage(events);
            views[page].Dock = DockStyle.Fill;
            viewport.Controls.Add(views[page]);
        }
        for (int i = 0; i < navigation.Length; i++)
        {
            bool selected = i == current;
            var link = navigation[i];
            link.BackColor = selected ? StudioTheme.Selection : StudioTheme.Rail;
            link.ForeColor = selected ? StudioTheme.Accent : StudioTheme.Muted;
            link.FlatAppearance.BorderColor = selected ? StudioTheme.Accent : StudioTheme.Rail;
            if (views[i] != null) views[i].Visible = selected;
        }
        views[page].BringToFront();
        title.Text = pages[page];
        subtitle.Text = captions[page];
    }

    void RefreshStatus()
    {
        StudioStatus state = readStatus();
        int percent = BatteryPublisher.GetPercent(state.Reading);
        connection.Text = state.Stale ? "Battery updates paused" : state.Connected ? "Controller connected" : "Controller offline";
        connection.ForeColor = state.Stale ? StudioTheme.Warning : state.Connected ? StudioTheme.Green : StudioTheme.Muted;
        battery.Text = percent >= 0 ? percent + "%" : "—";
        meter.Value = percent;
        powerOff.Enabled = turnOffController!=null && state.CanTurnOff && !state.TurningOff;
        string hint=state.TurningOff?"Turning off controller…":state.CableConnected?"Unplug the USB cable to turn off the controller.":state.CanTurnOff?"Turn off controller (hold PS for 8 seconds)":
            state.Connected?"Turning off is available over Bluetooth only.":"Connect a DualShock 4 using Bluetooth.";
        powerOff.AccessibleDescription=hint;
        powerHint.SetToolTip(powerOff,hint);
        overlay.UpdateReading(state.Reading, state.Charging);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (refresh != null) refresh.Dispose();
            powerHint.Dispose();
            if (railMark != null) railMark.Dispose();
            if (mark != null) mark.Dispose();
        }
        base.Dispose(disposing);
    }

    internal static void RenderPreview(string directory)
    {
        Directory.CreateDirectory(directory);
        var appearance = new OverlayAppearance { Scale = 125, CornerRadius = 8 };
        var lights = new LightbarSettings { Automatic = true, Brightness = 68 };
        var events = new ControllerEventLog();
        events.Add("Controller connected via Bluetooth");
        events.Add("Battery changed from 55% to 65%");
        using (var studio = new StudioWindow(appearance, lights, null, value => { },
            value => Task.FromResult(0), () => new StudioStatus(true, "65%", true, canTurnOff: true), events, 0, true, turnOffController: () => {}))
        {
            studio.StartPosition = FormStartPosition.Manual;
            studio.Location = new Point(-25000, -25000);
            studio.ShowInTaskbar = false;
            studio.Opacity = 0;
            studio.Show();
            Application.DoEvents();
            for (int page = 0; page < pages.Length; page++)
            {
                studio.SelectPage(page);
                studio.PerformLayout();
                Application.DoEvents();
                using (var bitmap = new Bitmap(studio.Width, studio.Height))
                {
                    studio.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                    bitmap.Save(Path.Combine(directory, "studio-" + page + ".png"), ImageFormat.Png);
                    if (page == 0) {
                        studio.overlay.SwitchTarget(true);
                        studio.PerformLayout(); Application.DoEvents();
                        studio.DrawToBitmap(bitmap, new Rectangle(0,0,bitmap.Width,bitmap.Height));
                        bitmap.Save(Path.Combine(directory, "studio-widget.png"), ImageFormat.Png);
                        studio.overlay.SwitchTarget(false);
                    }
                }
            }
        }
    }

    sealed class BatteryMeter : Control
    {
        int value = -1;
        public int Value { get { return value; } set { if (this.value != value) { this.value = value; Invalidate(); } } }

        public BatteryMeter()
        {
            DoubleBuffered = true;
            BackColor = StudioTheme.Card;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            if (Width < 8 || Height < 7) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var bar = new RectangleF(0, (Height - 6) / 2f, Width - 2, 6);
            using (var shape = StudioTheme.RoundedPath(bar, 3))
            using (var empty = new SolidBrush(StudioTheme.Border))
            {
                g.FillPath(empty, shape);
                if (value <= 0) return;
                var state = g.Save();
                g.SetClip(shape);
                using (var filled = new SolidBrush(value <= 20 ? StudioTheme.Warning : StudioTheme.Green))
                    g.FillRectangle(filled, bar.X, bar.Y, bar.Width * Math.Min(100, value) / 100f, bar.Height);
                g.Restore(state);
            }
        }
    }
}

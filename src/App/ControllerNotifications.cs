using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

enum NoticeKind { Connected, Disconnected, LowBattery, ChargingStarted, ChargingStopped, ChargingComplete, BatteryLevel }

struct PopupSettings
{
    internal const int MinScale = 75, MaxScale = 125;
    internal const int MinDuration = 1000, DefaultDuration = 3380, MaxDuration = 15000;
    internal const int DefaultTriggers = 7, MaxTriggers = (1 << 22) - 1;
    internal static readonly int[] BatteryLevels = Ds4BatteryScale.DisplayLevels;
    internal readonly int Desktop, Game, Scale;
    internal readonly int Triggers, Duration;
    internal readonly bool Enabled;
    internal PopupSettings(int desktop, int game, int scale = 100, bool enabled = true, int triggers = DefaultTriggers,
        int duration = DefaultDuration)
    { Desktop = desktop; Game = game; Scale = scale; Enabled = enabled; Triggers = triggers; Duration = duration; }

    internal static int TriggerBit(NoticeKind kind, int percent = 0)
    {
        if (kind != NoticeKind.BatteryLevel) return 1 << (int)kind;
        int index = Array.IndexOf(Ds4BatteryScale.TriggerLevels, percent);
        return index < 0 ? 0 : 1 << ((int)NoticeKind.BatteryLevel + index);
    }

    internal bool Allows(NoticeKind kind, int percent = 0)
    {
        return Enabled && (Triggers & TriggerBit(kind, percent)) != 0;
    }

    internal static PopupSettings Load()
    {
        using (var key = Registry.CurrentUser.OpenSubKey(OverlayAppearance.PreferencesPath))
        {
            int value = OverlayAppearance.Read(key, "PopupPositions", 18, 0, 0x3fff);
            int desktop = value & 7, game = (value >> 3) & 7, scale = (value >> 6) & 127;
            return new PopupSettings(desktop <= 5 ? desktop : 2, game <= 5 ? game : 2,
                scale >= MinScale && scale <= MaxScale ? scale : 100, (value & 0x2000) == 0,
                OverlayAppearance.Read(key, "PopupTriggers", DefaultTriggers, 0, MaxTriggers),
                OverlayAppearance.Read(key, "PopupDurationMs", DefaultDuration, MinDuration, MaxDuration));
        }
    }

    internal void Save()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(OverlayAppearance.PreferencesPath))
        {
            key.SetValue("PopupPositions", Desktop | (Game << 3) | (Scale << 6) | (Enabled ? 0 : 0x2000));
            key.SetValue("PopupTriggers", Triggers);
            key.SetValue("PopupDurationMs", Duration);
        }
    }

    internal static Point Locate(Rectangle bounds, Size size, int position, int margin)
    {
        int x = Math.Max(0, bounds.Width - size.Width), y = Math.Max(0, bounds.Height - size.Height);
        bool left = position == 1 || position == 3;
        bool bottom = position == 2 || position == 3 || position == 5;
        return new Point(bounds.X + (position >= 4 ? x / 2 : left ? Math.Min(x, margin) : Math.Max(0, x - margin)),
            bounds.Y + (bottom ? Math.Max(0, y - margin) : Math.Min(y, margin)));
    }
}

sealed class ControllerNotifications : IDisposable
{
    const int FadeIn = 260, FadeOut = 420, Gap = 140;
    internal static readonly string[] TestEvents = {
        "Sample notification", "Connected", "Disconnected", "Low battery warning",
        "Charging started", "Charging stopped", "Charging complete"
    };
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly Timer timer = new Timer { Interval = 15 };
    readonly DesktopNotification desktop = new DesktopNotification();
    readonly BatteryPublisher inGame;
    readonly Func<bool> readSegmentedIcon;
    readonly PopupPresentation presentation = null;
    readonly Queue<Notice> pending = new Queue<Notice>();
    Bitmap image;
    Notice active;
    Notice scheduledTest;
    long startedAt, nextAt, testAt;
    int activeDuration;
    uint popupId;
    uint popupTarget;
    PopupDestination destination;
    internal PopupSettings Settings { get; private set; }

    internal ControllerNotifications(string mappingName = null) : this(mappingName, null) { }

    internal ControllerNotifications(string mappingName, Func<bool> readSegmentedIcon)
    {
        this.readSegmentedIcon = readSegmentedIcon;
        Settings = new PopupSettings(2, 2);
        inGame = BatteryPublisher.OpenNotificationFrame(mappingName);
        timer.Tick += delegate { Advance(clock.ElapsedMilliseconds); };
    }

    internal bool SegmentedIcon { get { return readSegmentedIcon == null || readSegmentedIcon(); } }

    internal void Enqueue(NoticeKind kind, string title, string detail = null, int percent = -1, bool charging = false)
    {
        if (Settings.Allows(kind, percent)) Enqueue(new Notice(kind, title, detail, percent: percent, charging: charging));
    }

    internal void Apply(PopupSettings settings)
    {
        Settings = settings;
        for (int count = pending.Count; count > 0; count--)
        {
            Notice notice = pending.Dequeue();
            if (notice.Override.HasValue || settings.Allows(notice.Kind, notice.Percent)) pending.Enqueue(notice);
        }
        if (active != null && !active.Override.HasValue && !settings.Allows(active.Kind, active.Percent)) Hide();
    }

    internal void ShowTest(PopupSettings settings, int eventIndex, int delaySeconds)
    {
        scheduledTest = TestNotice(settings, eventIndex);
        testAt = clock.ElapsedMilliseconds + delaySeconds * 1000L;
        if (!timer.Enabled)
        {
            timer.Start();
            Advance(clock.ElapsedMilliseconds);
        }
    }

    internal static Bitmap TestImage(int percent = 100, int eventIndex = 0, bool segmented = true)
    {
        return Render(TestNotice(new PopupSettings(2, 2), eventIndex), percent, segmented);
    }

    static Notice TestNotice(PopupSettings settings, int eventIndex)
    {
        switch (eventIndex)
        {
            case 1: return new Notice(NoticeKind.Connected, "Dualshock 4 connected", null, settings);
            case 2: return new Notice(NoticeKind.Disconnected, "Dualshock 4 disconnected", null, settings);
            case 3: return new Notice(NoticeKind.LowBattery, "DualShock 4 battery low", "15% remaining. Connect a cable.", settings, 15);
            case 4: return new Notice(NoticeKind.ChargingStarted, "Charging started", "DualShock 4 battery at 55%.", settings, 55, true);
            case 5: return new Notice(NoticeKind.ChargingStopped, "Charging stopped", "DualShock 4 battery at 65%.", settings, 65);
            case 6: return new Notice(NoticeKind.ChargingComplete, "Charging complete", "DualShock 4 battery at 100%.", settings, 100);
            default:
                int index = eventIndex - TestEvents.Length;
                if (index >= 0 && index < PopupSettings.BatteryLevels.Length)
                {
                    int level = PopupSettings.BatteryLevels[index];
                    return new Notice(NoticeKind.BatteryLevel, "DualShock 4 battery at " + level + "%",
                        null, settings, level);
                }
                return new Notice(NoticeKind.Connected, "Test notification", "Preview of your popup settings.", settings);
        }
    }

    void Enqueue(Notice notice)
    {
        pending.Enqueue(notice);
        if (timer.Enabled) return;
        Advance(clock.ElapsedMilliseconds);
        timer.Start();
    }

    void Advance(long now)
    {
        if (scheduledTest != null && now >= testAt)
        {
            pending.Enqueue(scheduledTest);
            scheduledTest = null;
        }
        if (image == null)
        {
            if (pending.Count == 0)
            {
                if (scheduledTest == null) timer.Stop();
                return;
            }
            if (now < nextAt) return;
            active = pending.Dequeue();
            PopupSettings settings = active.Override ?? Settings;
            image = Render(active, settings.Scale, SegmentedIcon);
            activeDuration = settings.Duration;
            startedAt = now;
            popupId = BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(), 0) | 1u;
            popupTarget = PopupPresentation.ForegroundProcess();
            destination = PopupDestination.Waiting;
        }

        if (now - startedAt >= activeDuration)
        {
            Hide();
            nextAt = now + Gap;
            if (pending.Count == 0 && scheduledTest == null) timer.Stop();
            return;
        }
        PopupSettings positions = active.Override ?? Settings;
        if (destination == PopupDestination.Waiting)
        {
            destination = RtssOutput.Current != null && RtssOutput.Current.ForegroundGame ? PopupDestination.Game : PopupDestination.Desktop;
            if (destination == PopupDestination.Desktop)
            {
                inGame.PublishNotification(null, 0);
                startedAt = now;
            }
        }
        float opacity = OpacityAt(now - startedAt, activeDuration);
        if (destination == PopupDestination.Desktop) desktop.Update(image, opacity, positions.Desktop);
        else if (RtssOutput.Current != null) RtssOutput.Current.PublishPopup(image, (byte)Math.Round(255 * opacity), positions.Game, active.Title, active.Detail);
    }

    internal static float OpacityAt(long elapsed, int duration)
    {
        if (elapsed < FadeIn) return Ease(elapsed / (float)FadeIn);
        int fadeOutAt = duration - FadeOut;
        if (elapsed < fadeOutAt) return 1;
        return 1 - Ease((elapsed - fadeOutAt) / (float)FadeOut);
    }

    static float Ease(float t)
    {
        t = Math.Max(0, Math.Min(1, t));
        return t * t * (3 - 2 * t);
    }

    void Hide()
    {
        desktop.Dismiss();
        if (RtssOutput.Current != null) RtssOutput.Current.PublishPopup(null, 0, 0);
        inGame.PublishNotification(null, 0);
        if (image != null) image.Dispose();
        image = null;
        active = null;
    }

    public void Dispose()
    {
        timer.Dispose();
        Hide();
        desktop.Dispose();
        inGame.Dispose();
        if (presentation != null) presentation.Dispose();
    }

    static Bitmap Render(Notice notice, int percent, bool segmented)
    {
        float scale = percent / 100f;
        var card = new Bitmap((int)Math.Round(248 * scale), (int)Math.Round(60 * scale), PixelFormat.Format32bppArgb);
        Color accent = notice.Percent >= 0 ? BatteryPublisher.BatteryColor(notice.Percent)
            : notice.Kind == NoticeKind.Connected ? StudioTheme.Green : StudioTheme.Muted;
        using (var graphics = Graphics.FromImage(card))
        using (var body = StudioTheme.RoundedPath(new RectangleF(1, 1, 246, 58), 10))
        using (var tile = StudioTheme.RoundedPath(new RectangleF(12, 14, 32, 32), 8))
        using (var marker = StudioTheme.RoundedPath(new RectangleF(2.5f, 20, 3, 20), 1.5f))
        using (var surface = new SolidBrush(StudioTheme.Card))
        using (var edge = new Pen(StudioTheme.Border))
        using (var tint = new SolidBrush(Color.FromArgb(35, accent)))
        using (var signal = new SolidBrush(accent))
        using (var main = new SolidBrush(StudioTheme.Text))
        using (var secondary = new SolidBrush(Color.FromArgb(197, 207, 225)))
        using (var title = new Font("Segoe UI", (float)Math.Floor(14 * scale), FontStyle.Bold, GraphicsUnit.Pixel))
        using (var detail = new Font("Segoe UI", (float)Math.Round(12 * scale), FontStyle.Regular, GraphicsUnit.Pixel))
        using (var line = new StringFormat {
            FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter })
        {
            graphics.Clear(Color.Transparent);
            graphics.ScaleTransform(scale, scale);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            graphics.FillPath(surface, body);
            graphics.DrawPath(edge, body);
            graphics.FillPath(tint, tile);
            graphics.FillPath(signal, marker);
            if (notice.Percent >= 0) BatteryPublisher.DrawPopupBattery(graphics, notice.Percent, notice.Charging, segmented);
            else DrawControllerIcon(graphics, notice.Kind, accent);
            graphics.ResetTransform();
            bool hasDetail = !string.IsNullOrEmpty(notice.Detail);
            var titleBounds = Rectangle.Round(new RectangleF(52 * scale, (hasDetail ? 10 : 19) * scale, 190 * scale, 22 * scale));
            string measureTitle = notice.Kind == NoticeKind.BatteryLevel ? "DualShock 4 battery at 100%" : notice.Title;
            float width = graphics.MeasureString(measureTitle, title, PointF.Empty, line).Width;
            if (width > titleBounds.Width - 2)
            {
                using (var fitted = new Font(title.FontFamily, title.Size * (titleBounds.Width - 2) / width, title.Style, title.Unit))
                    graphics.DrawString(notice.Title, fitted, main, titleBounds, line);
            }
            else graphics.DrawString(notice.Title, title, main, titleBounds, line);
            if (hasDetail)
                graphics.DrawString(notice.Detail, detail, secondary, Rectangle.Round(new RectangleF(52 * scale, 32 * scale, 185 * scale, 18 * scale)), line);
        }
        return card;
    }

    static void DrawControllerIcon(Graphics graphics, NoticeKind kind, Color accent)
    {
        PopupControllerIcon.Draw(graphics, kind, accent);
    }

    sealed class Notice
    {
        internal readonly NoticeKind Kind;
        internal readonly string Title, Detail;
        internal readonly PopupSettings? Override;
        internal readonly int Percent;
        internal readonly bool Charging;
        internal Notice(NoticeKind kind, string title, string detail, PopupSettings? settings = null, int percent = -1, bool charging = false)
        {
            Kind = kind;
            Title = title;
            Detail = detail;
            Override = settings;
            Percent = percent;
            Charging = charging;
        }
    }
}

enum PopupDestination { Waiting, Game, Desktop }

sealed class PopupPresentation : IDisposable
{
    const int WaitMs = 220;
    readonly MemoryMappedFile mapping;
    readonly MemoryMappedViewAccessor view;

    [DllImport("user32")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);

    internal PopupPresentation()
    {
        try
        {
            mapping = MemoryMappedFile.CreateOrOpen(@"Local\DS4Overlay.PopupPresented", sizeof(long));
            view = mapping.CreateViewAccessor(0, sizeof(long), MemoryMappedFileAccess.Read);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    internal bool IsAvailable { get { return view != null; } }

    internal bool WasPresented(uint popupId)
    {
        if (view == null || popupId == 0) return false;
        uint foreground = ForegroundProcess();
        return foreground != 0 && unchecked((ulong)view.ReadInt64(0)) == ((ulong)foreground << 32 | popupId);
    }

    internal static uint ForegroundProcess()
    {
        uint foreground;
        GetWindowThreadProcessId(GetForegroundWindow(), out foreground);
        return foreground;
    }

    internal static PopupDestination Choose(long elapsed, bool available, bool presented)
    {
        if (presented) return PopupDestination.Game;
        return available && elapsed < WaitMs ? PopupDestination.Waiting : PopupDestination.Desktop;
    }

    internal static bool SelfTest()
    {
        return Choose(0, true, false) == PopupDestination.Waiting &&
            Choose(WaitMs - 1, true, false) == PopupDestination.Waiting &&
            Choose(WaitMs, true, false) == PopupDestination.Desktop &&
            Choose(85, true, true) == PopupDestination.Game &&
            Choose(0, false, false) == PopupDestination.Desktop;
    }

    public void Dispose()
    {
        if (view != null) view.Dispose();
        if (mapping != null) mapping.Dispose();
    }
}

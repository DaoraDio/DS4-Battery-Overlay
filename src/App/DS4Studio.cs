using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Text;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Collections.Generic;
using Microsoft.Win32;

sealed class Overlay : Form
{
    const string LiveTrayIconPreference = "ShowLiveTrayIcon";

    [StructLayout(LayoutKind.Sequential)]
    struct Registration { public ushort page, usage; public uint flags; public IntPtr window; }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    struct DeviceInfo
    {
        [FieldOffset(0)] public uint size;
        [FieldOffset(4)] public uint type;
        [FieldOffset(8)] public uint vendor;
        [FieldOffset(12)] public uint product;
        [FieldOffset(20)] public ushort usagePage;
        [FieldOffset(22)] public ushort usage;
    }

    [DllImport("user32", EntryPoint = "GetRawInputDeviceInfoW", ExactSpelling = true)]
    static extern uint ReadDeviceInfo(IntPtr device, uint command, ref DeviceInfo info, ref uint size);

    [DllImport("user32", SetLastError=true)] static extern bool RegisterRawInputDevices(Registration[] devices, uint count, uint size);
    [DllImport("user32")] static extern uint GetRawInputData(IntPtr input, uint command, IntPtr data, ref uint size, uint header);
    [DllImport("user32", CharSet=CharSet.Unicode)] static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, StringBuilder name, ref uint size);
    [DllImport("user32")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32")] static extern IntPtr GetLastActivePopup(IntPtr window);
    [DllImport("user32")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32")] static extern int GetMessageTime();
    [DllImport("user32")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32", SetLastError=true)] static extern bool RegisterHotKey(IntPtr window, int id, uint mods, uint key);
    [DllImport("user32")] static extern bool UnregisterHotKey(IntPtr window, int id);

    readonly Dictionary<IntPtr, ControllerConnection> controllers = new Dictionary<IntPtr, ControllerConnection>();
    readonly ControllerEventLog controllerEvents;
    readonly HashSet<IntPtr> ignoredDevices = new HashSet<IntPtr>();
    readonly ControllerNotifications notifications;
    readonly Stopwatch schedulerClock = Stopwatch.StartNew();
    readonly bool notificationsEnabled;
    ControllerConnection activeController;
    long nextDeviceScan;
    StudioWindow studio;
    readonly GameLibrary gameLibrary;
    LightbarSettings lightbar = new LightbarSettings();
    readonly Timer timer = new Timer { Interval = 200 };
    readonly uint headerSize = (uint)(8 + IntPtr.Size * 2);
    readonly IntPtr buffer = Marshal.AllocHGlobal(65536);
    readonly BatteryPublisher publisher;
    readonly LegacyDisplay legacy;
    OverlayAppearance legacySource;
    readonly NotifyIcon tray;
    readonly Icon trayIcon;
    Icon liveTrayIcon;
    int trayPercent = -1;
    bool trayCharging;
    readonly ContextMenuStrip trayMenu;
    readonly ToolStripMenuItem toggleItem;
    readonly ToolStripMenuItem powerOffItem = new ToolStripMenuItem("Turn off controller") { Enabled = false };
    bool turningOffController;
    readonly ToolStripMenuItem trayBatteryItem = new ToolStripMenuItem("Live battery icon in tray");
    readonly ToolStripMenuItem startupItem = new ToolStripMenuItem("Launch on Windows startup");
    bool showBattery = true;
    int hotkeyId;
    Keys registeredShortcut;
    readonly ShortcutPress shortcutPress = new ShortcutPress();
    bool cleaned;

    Overlay(BatteryPublisher publisher, bool loadPreferences = true, LegacyDisplay legacy = null)
    {
        this.publisher = publisher;
        this.legacy = legacy;
        notificationsEnabled = loadPreferences;
        controllerEvents = new ControllerEventLog(loadPreferences ? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DS4OverlayRTSSTest", "controller-events.log")
            : null);
        ShowInTaskbar = false;
        if (loadPreferences)
        {
            gameLibrary = null;
            publisher.Appearance = OverlayAppearance.Load();
            lightbar = LightbarSettings.Load();
            using (var preferences = Registry.CurrentUser.OpenSubKey(OverlayAppearance.PreferencesPath))
                trayBatteryItem.Checked = OverlayAppearance.Read(preferences, LiveTrayIconPreference, 0, 0, 1) == 1;
        }
        else lightbar.Enabled = false;

        var registrations = new[] {
            new Registration { page = 1, usage = 5, flags = 0x2100, window = Handle },
            new Registration { page = 1, usage = 4, flags = 0x2100, window = Handle }
        };
        if (!RegisterRawInputDevices(registrations, 2, (uint)Marshal.SizeOf(typeof(Registration))))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        // Raw keyboard input supplements the registered shortcut for games that swallow WM_HOTKEY.
        var keyboard = new[] { new Registration { page = 1, usage = 6, flags = 0x100, window = Handle } };
        RegisterRawInputDevices(keyboard, 1, (uint)Marshal.SizeOf(typeof(Registration)));
        Keys requested = publisher.Appearance.Shortcut;
        if (!TryChangeShortcut(requested, delegate { }) &&
            (requested == OverlayAppearance.DefaultShortcut || !TryChangeShortcut(OverlayAppearance.DefaultShortcut, delegate { })))
            registeredShortcut = Keys.None;

        trayMenu = new ContextMenuStrip { BackColor = StudioTheme.Card, ForeColor = StudioTheme.Text,
            Font = StudioTheme.Font(9), ShowImageMargin = false, ShowCheckMargin = true,
            Renderer = new ToolStripProfessionalRenderer(new StudioTrayColors()) };
        toggleItem = new ToolStripMenuItem();
        toggleItem.Click += delegate { Toggle(); };
        trayMenu.Items.Add(toggleItem);
        trayBatteryItem.Click += delegate { ToggleLiveTrayIcon(); };
        trayMenu.Items.Add(trayBatteryItem);
        trayMenu.Items.Add("Customize…", null, delegate { Customize(); });
        powerOffItem.Click += delegate { TurnOffController(); };
        trayMenu.Items.Add(powerOffItem);
        trayMenu.ShowItemToolTips = true;
        trayMenu.Opening += delegate { UpdatePowerOffItem(); };
        if (loadPreferences)
        {
            try { startupItem.Checked = WindowsStartup.IsEnabled(); }
            catch (UnauthorizedAccessException) { }
            catch (System.Security.SecurityException) { }
            startupItem.Click += delegate {
                try
                {
                    bool enable = !startupItem.Checked;
                    WindowsStartup.SetEnabled(enable, Application.ExecutablePath);
                    startupItem.Checked = enable;
                }
                catch (Exception error)
                {
                    MessageBox.Show(error.Message, "DS4 Battery Overlay", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
            trayMenu.Items.Add(startupItem);
        }
        trayMenu.Items.Add("Quit", null, delegate { Application.ExitThread(); });
        trayIcon = StudioTheme.LoadIcon();
        tray = new NotifyIcon { Icon = trayIcon, ContextMenuStrip = trayMenu, Visible = true };
        tray.MouseClick += delegate(object sender, MouseEventArgs e) {
            if (e.Button == MouseButtons.Left) Customize();
        };
        if (notificationsEnabled)
        {
            notifications = new ControllerNotifications("DS4.RTSS.Notification." + Guid.NewGuid().ToString("N"), () => publisher.Appearance.SegmentedIcon);
            notifications.Apply(PopupSettings.Load());
        }
        timer.Tick += delegate {
            CheckControllerActivity();
            controllerEvents.RetryStorage();
            if (gameLibrary != null) gameLibrary.Tick(schedulerClock.ElapsedMilliseconds);
            Publish();
            UpdateLightbars();
        };
        timer.Start();
        Publish();
    }

    void Customize() { ShowStudio(0, false); }

    void UpdatePowerOffItem()
    {
        powerOffItem.Text = turningOffController ? "Turning off controller…" : "Turn off controller";
        powerOffItem.Enabled = !turningOffController && Ds4Power.CanTurnOff(activeController);
        powerOffItem.ToolTipText = activeController == null ? "Connect a DualShock 4 using Bluetooth." :
            activeController.CableConnected ? "Unplug the USB cable to turn off the controller." :
            Ds4Power.CanTurnOff(activeController) ? "Turn off controller (hold PS for 8 seconds)." :
            "Turning off is available over Bluetooth only.";
    }

    void TurnOffController() { TurnOffController(activeController); }

    async void TurnOffController(ControllerConnection target)
    {
        if (turningOffController || !Ds4Power.CanTurnOff(target)) return;
        turningOffController = true; UpdatePowerOffItem();
        string path = target.Path;
        target.CancelRead(); target.CancelWrite();
        try
        {
            await System.Threading.Tasks.Task.Run(() => Ds4Power.TurnOff(path, true));
        }
        catch (Exception error)
        {
            if (!cleaned) MessageBox.Show(studio, "Could not turn off the controller.\n\n" + error.Message,
                "DS4 Battery Overlay", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { turningOffController = false; if (!cleaned) UpdatePowerOffItem(); }
    }

    StudioStatus ReadStudioStatus()
    {
        var controller = activeController;
        long now = schedulerClock.ElapsedMilliseconds;
        bool fresh = controller != null && controller.HasReading(now);
        string reading = controller == null ? "" : controller.DisplayReading(now);
        return new StudioStatus(controller != null, reading, fresh && controller.Charging, !fresh && reading.Length != 0, Ds4Power.CanTurnOff(controller), turningOffController, controller != null && controller.CableConnected);
    }

    void ShowStudio(int page, bool switchPage = true)
    {
        if (studio != null)
        {
            if (switchPage && studio.Enabled) studio.SelectPage(page);
            if (studio.WindowState == FormWindowState.Minimized) studio.WindowState = FormWindowState.Normal;
            IntPtr window = studio.Handle;
            IntPtr popup = GetLastActivePopup(window);
            SetForegroundWindow(popup != IntPtr.Zero && IsWindowVisible(popup) ? popup : window);
            if (studio.Enabled) studio.Activate();
            return;
        }
        try
        {
            var dialog = new StudioWindow(publisher.Appearance, lightbar, gameLibrary, value => {
                if (value.Shortcut == publisher.Appearance.Shortcut) value.Save();
                else if (!TryChangeShortcut(value.Shortcut, value.Save))
                    throw new InvalidOperationException("That shortcut is already in use or reserved by Windows. Choose another shortcut.");
                publisher.Appearance = value;
                Publish();
            }, ApplyLightbarSettings, ReadStudioStatus, controllerEvents, page, notifications: notifications, turnOffController: TurnOffController);
            studio = dialog;
            StudioWindowPresentation.Show(dialog, closed => {
                if (ReferenceEquals(studio, closed)) studio = null;
            });
        }
        catch (Exception error)
        {
            var failed = studio; studio = null;
            if (failed != null) failed.Dispose();
            MessageBox.Show(error.Message, "DS4 Battery Overlay", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    System.Threading.Tasks.Task ApplyLightbarSettings(LightbarSettings settings)
    {
        settings.Save();
        foreach (ControllerConnection controller in controllers.Values) controller.CancelWrite();
        lightbar = settings;
        UpdateLightbars();
        return System.Threading.Tasks.Task.FromResult(0);
    }

    void UpdateLightbars()
    {
        long now = schedulerClock.ElapsedMilliseconds;
        foreach (ControllerConnection controller in controllers.Values)
        {
            Color color;
            if (controller.TryPlan(lightbar, now, out color))
                ApplyControllerLightbar(controller, color, lightbar.Brightness);
        }
    }

    async void ApplyControllerLightbar(ControllerConnection controller, Color color, int brightness)
    {
        var cancellation = new System.Threading.CancellationTokenSource();
        controller.Pending = cancellation;
        bool sent = false;
        try
        {
            await Ds4Lightbar.Send(controller.Path, controller.LightbarBluetooth, color, brightness, cancellation.Token);
            sent = true;
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (InvalidOperationException) { }
        catch (UnauthorizedAccessException) { }
        catch (System.ComponentModel.Win32Exception) { }
        catch (System.Security.SecurityException) { }
        catch (NotSupportedException) { }
        catch (ArgumentException) { }
        finally
        {
            if (!cancellation.IsCancellationRequested)
                controller.EndWrite(sent, schedulerClock.ElapsedMilliseconds);
            controller.Pending = null;
            cancellation.Dispose();
        }
    }

    void QueueNotice(NoticeKind kind, string title, string detail = null, int percent = -1)
    {
        if (notifications != null) notifications.Enqueue(kind, title, detail, percent);
    }

    bool TryChangeShortcut(Keys shortcut, Action save)
    {
        if (hotkeyId != 0 && shortcut == registeredShortcut)
        {
            save();
            return true;
        }

        uint modifiers = 0x4000; // MOD_NOREPEAT
        if ((shortcut & Keys.Alt) != 0) modifiers |= 1;
        if ((shortcut & Keys.Control) != 0) modifiers |= 2;
        if ((shortcut & Keys.Shift) != 0) modifiers |= 4;
        int nextId = hotkeyId == 1 ? 2 : 1;
        if (!RegisterHotKey(Handle, nextId, modifiers, (uint)(shortcut & Keys.KeyCode))) return false;

        try { save(); }
        catch
        {
            UnregisterHotKey(Handle, nextId);
            throw;
        }
        if (hotkeyId != 0) UnregisterHotKey(Handle, hotkeyId);
        hotkeyId = nextId;
        registeredShortcut = shortcut;
        shortcutPress.Reset();
        return true;
    }

    void Toggle()
    {
        showBattery = !showBattery;
        Publish();
    }

    void ToggleLiveTrayIcon()
    {
        bool enabled = !trayBatteryItem.Checked;
        try
        {
            using (var preferences = Registry.CurrentUser.CreateSubKey(OverlayAppearance.PreferencesPath))
                preferences.SetValue(LiveTrayIconPreference, enabled ? 1 : 0);
            trayBatteryItem.Checked = enabled;
            Publish();
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "DS4 Battery Overlay", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    void UpdateTrayIcon(string status, bool charging)
    {
        int percent = trayBatteryItem.Checked ? BatteryPublisher.GetPercent(status) : -1;
        charging = percent >= 0 && charging;
        if (percent == trayPercent && charging == trayCharging) return;
        Icon replacement = percent < 0 ? null : TrayBatteryIcon.Create(percent, charging);
        try { tray.Icon = replacement ?? trayIcon; }
        catch
        {
            if (replacement != null) replacement.Dispose();
            throw;
        }
        if (liveTrayIcon != null) liveTrayIcon.Dispose();
        liveTrayIcon = replacement;
        trayPercent = percent;
        trayCharging = charging;
    }

    void Publish()
    {
        var current = activeController;
        long now = schedulerClock.ElapsedMilliseconds;
        bool charging = current != null && current.HasReading(now) && current.Charging;
        string status = current != null ? current.DisplayReading(now) : "Waiting for controller";
        if (status.Length == 0) status = "Battery data unavailable";
        tray.Text = "DS4 Battery Overlay · " + status + (charging ? " · Charging" : "");
        UpdateTrayIcon(status, charging);
        UpdatePowerOffItem();
        toggleItem.Text = (showBattery ? "Hide battery bar" : "Show battery bar") + (hotkeyId != 0 ? "  (" + OverlayAppearance.FormatShortcut(registeredShortcut) + ")" : "");
        string caption = showBattery && current != null ? status : null;
        publisher.Publish(caption, charging);
        if (RtssOutput.Current != null) RtssOutput.Current.Publish(caption, publisher.Appearance, charging, showBattery && current == null);
        if (legacy != null)
        {
            if (legacySource != publisher.Appearance)
            {
                legacySource = publisher.Appearance;
                legacy.SetAppearance(legacySource);
            }
            uint foreground;
            GetWindowThreadProcessId(GetForegroundWindow(), out foreground);
            legacy.Publish(caption, foreground, charging);
        }
    }

    ControllerConnection FindController(IntPtr handle)
    {
        ControllerConnection controller;
        if (controllers.TryGetValue(handle, out controller)) return controller;
        if (ignoredDevices.Contains(handle)) return null;

        uint infoSize = (uint)Marshal.SizeOf(typeof(DeviceInfo));
        var info = new DeviceInfo { size = infoSize };
        bool hasInfo = ReadDeviceInfo(handle, 0x2000000b, ref info, ref infoSize) != uint.MaxValue;
        if (hasInfo && (info.type != 2 || info.usagePage != 1 || (info.usage != 4 && info.usage != 5)))
        {
            ignoredDevices.Add(handle);
            return null;
        }

        string path = ReadDevicePath(handle);
        if (path == null) return null;
        bool identified = hasInfo && info.vendor == 0x054c && (info.product == 0x05c4 || info.product == 0x09cc);
        if (!identified && !IsController(path))
        {
            ignoredDevices.Add(handle);
            return null;
        }
        controller = new ControllerConnection(path, controllerEvents, notifications);
        controllers.Add(handle, controller);
        return controller;
    }

    static string ReadDevicePath(IntPtr handle)
    {
        uint chars = 0;
        GetRawInputDeviceInfo(handle, 0x20000007, null, ref chars);
        if (chars == 0 || chars > 4096) return null;
        var name = new StringBuilder((int)chars);
        return GetRawInputDeviceInfo(handle, 0x20000007, name, ref chars) == uint.MaxValue ? null : name.ToString();
    }

    void CheckControllerActivity()
    {
        long now = schedulerClock.ElapsedMilliseconds;
        if (now >= nextDeviceScan)
        {
            nextDeviceScan = now + 1000;
            var attached = Ds4Input.ListAttached();
            if (attached != null)
            {
                var handles = new HashSet<IntPtr>();
                foreach (var device in attached)
                    if (device.Type == 2)
                    {
                        handles.Add(device.Handle);
                        FindController(device.Handle);
                    }
                ignoredDevices.IntersectWith(handles);
                var removed = new List<IntPtr>();
                foreach (var controller in controllers)
                    if (controller.Value.ObservePresence(handles.Contains(controller.Key), now)) removed.Add(controller.Key);
                foreach (IntPtr handle in removed) RemoveController(handle);
            }
        }

        foreach (ControllerConnection controller in controllers.Values)
            if (controller.ShouldReadDirectly(now)) ReadControllerDirectly(controller);
    }

    async void ReadControllerDirectly(ControllerConnection controller)
    {
        var cancellation = new System.Threading.CancellationTokenSource();
        controller.PendingRead = cancellation;
        controller.NextDirectRead = schedulerClock.ElapsedMilliseconds + 1200;
        try
        {
            ArraySegment<byte> bytes = await System.Threading.Tasks.Task.Run(() =>
                Ds4Input.ReadSharedReport(controller.Path, cancellation.Token));
            if (cleaned || cancellation.IsCancellationRequested || bytes.Count == 0) return;
            int id = bytes.Array[bytes.Offset], batteryOffset;
            if (Ds4Input.TryLocateReport(id, bytes.Count, controller.BluetoothPath, out batteryOffset))
            {
                int button = Ds4PowerHold.ButtonOffset(id, bytes.Count, bytes.Array[bytes.Offset + 1]);
                ObserveReport(controller, id, batteryOffset < 0 ? -1 : bytes.Array[bytes.Offset + batteryOffset], false,
                    button < 0 ? (bool?)null : (bytes.Array[bytes.Offset + button] & 1) != 0);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (NotSupportedException) { }
        finally
        {
            controller.PendingRead = null;
            cancellation.Dispose();
        }
    }

    void RemoveController(IntPtr handle)
    {
        ignoredDevices.Remove(handle);
        ControllerConnection controller;
        if (!controllers.TryGetValue(handle, out controller)) return;
        controllers.Remove(handle);
        if (controller.Disconnect()) ControllerDisconnected(controller);
    }

    void ControllerDisconnected(ControllerConnection controller)
    {
        if (activeController == controller)
        {
            activeController = null;
            foreach (ControllerConnection candidate in controllers.Values)
                if (candidate.Connected && (activeController == null || candidate.Updated > activeController.Updated))
                    activeController = candidate;
        }
        QueueNotice(NoticeKind.Disconnected, "Dualshock 4 disconnected");
    }

    static bool IsController(string path)
    {
        path = path.ToUpperInvariant();
        if (!path.Contains("VID_054C") && !path.Contains("VID&0002054C")
            && !path.Contains("VID&0001054C")) return false;
        return path.Contains("PID_05C4") || path.Contains("PID_09CC")
            || path.Contains("PID&05C4") || path.Contains("PID&09CC");
    }

    static void RequestBluetoothReports(string path)
    {
        System.Threading.ThreadPool.QueueUserWorkItem(delegate { Ds4Input.RequestFullBluetoothReports(path); });
    }

    static string Decode(byte status, out bool charging) { return Ds4BatteryScale.Decode(status, out charging); }
    static bool CheckBatteryDecode() { return Ds4BatteryScale.SelfTest(); }
    void ReadInput(IntPtr input)
    {
        uint size = 65536;
        uint length = GetRawInputData(input, 0x10000003, buffer, ref size, headerSize);
        if (length == uint.MaxValue || length < headerSize) return;
        int type = Marshal.ReadInt32(buffer);
        if (type == 1 && length >= headerSize + 16)
        {
            ReadShortcutKey((int)headerSize);
            return;
        }
        if (type != 2 || length < headerSize + 8) return;
        IntPtr device = Marshal.ReadIntPtr(buffer, 8);
        int offset = (int)headerSize;
        int reportSize = Marshal.ReadInt32(buffer, offset);
        int count = Marshal.ReadInt32(buffer, offset + 4);
        offset += 8;
        if (reportSize <= 0 || count <= 0 || (long)reportSize * count > length - offset) return;
        ControllerConnection controller = FindController(device);
        if (controller == null) return;
        offset += (count - 1) * reportSize;
        int reportId = Marshal.ReadByte(buffer, offset);
        int batteryOffset;
        if (Ds4Input.TryLocateReport(reportId, reportSize, controller.BluetoothPath, out batteryOffset))
        {
            int button = Ds4PowerHold.ButtonOffset(reportId, reportSize, Marshal.ReadByte(buffer, offset + 1));
            ObserveReport(controller, reportId, batteryOffset < 0 ? -1 : Marshal.ReadByte(buffer, offset + batteryOffset), true,
                button < 0 ? (bool?)null : (Marshal.ReadByte(buffer, offset + button) & 1) != 0);
        }
    }

    void ReadShortcutKey(int offset)
    {
        if (hotkeyId == 0 || Marshal.ReadInt16(buffer, offset + 6) != (int)(registeredShortcut & Keys.KeyCode)) return;
        bool down = (Marshal.ReadInt16(buffer, offset + 2) & 1) == 0;
        Keys modifiers = Keys.None;
        if (down)
        {
            if (GetAsyncKeyState((int)Keys.ControlKey) < 0) modifiers |= Keys.Control;
            if (GetAsyncKeyState((int)Keys.ShiftKey) < 0) modifiers |= Keys.Shift;
            if (GetAsyncKeyState((int)Keys.Menu) < 0) modifiers |= Keys.Alt;
        }
        bool matches = modifiers == (registeredShortcut & Keys.Modifiers) &&
            GetAsyncKeyState((int)Keys.LWin) >= 0 && GetAsyncKeyState((int)Keys.RWin) >= 0;
        if (shortcutPress.FromKeyboard(down, matches, unchecked((uint)GetMessageTime()))) Toggle();
    }

    void ObserveReport(ControllerConnection controller, int reportId, int batteryStatus, bool fromRawInput, bool? psDown)
    {
        long now = schedulerClock.ElapsedMilliseconds;
        if (fromRawInput) controller.LastRawInput = now;
        if (controller.ObserveInput(now))
        {
            if (activeController == null) activeController = controller;
            QueueNotice(NoticeKind.Connected, "Dualshock 4 connected");
        }

        if (batteryStatus >= 0) controller.CableConnected = (batteryStatus & 16) != 0;
        if (psDown.HasValue && controller.PowerHold.Observe(psDown.Value, now, Ds4Power.CanTurnOff(controller)))
            TurnOffController(controller);

        if (batteryStatus < 0)
        {
            if (reportId == 1 && !controller.HasReading(now) && now >= controller.NextBluetoothReportRequest)
            {
                controller.NextBluetoothReportRequest = now + 5000;
                RequestBluetoothReports(controller.Path);
            }
            return;
        }
        controller.Bluetooth = reportId == 0x11;
        bool charging;
        string value = Decode((byte)batteryStatus, out charging);
        if (value.Length == 0) return;
        if (fromRawInput) controller.CancelRead();
        if (controller.Connected) activeController = controller;
        if (controller.RecordBattery(value, (batteryStatus & 16) != 0, charging, now))
        {
            int percent = BatteryPublisher.GetPercent(value);
            QueueNotice(NoticeKind.LowBattery, "DualShock 4 battery low",
                percent + "% remaining. Connect a cable.", percent);
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312 && hotkeyId != 0 && m.WParam.ToInt32() == hotkeyId &&
            shortcutPress.FromWindows(unchecked((uint)GetMessageTime()))) Toggle();
        if (m.Msg == 0x00FF) ReadInput(m.LParam);
        if (m.Msg == 0x00FE)
        {
            if (m.WParam.ToInt32() == 1)
            {
                ignoredDevices.Remove(m.LParam);
                ControllerConnection prior;
                if (controllers.TryGetValue(m.LParam, out prior))
                {
                    string path = ReadDevicePath(m.LParam);
                    if (path != null && !string.Equals(prior.Path, path, StringComparison.OrdinalIgnoreCase))
                        RemoveController(m.LParam);
                }
                nextDeviceScan = 0;
            }
            else if (m.WParam.ToInt32() == 2) RemoveController(m.LParam);
            m.Result = IntPtr.Zero;
            return;
        }
        if (m.Msg == 0x0021) { m.Result = new IntPtr(3); return; }
        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !cleaned)
        {
            cleaned = true;
            timer.Dispose();
            var openStudio = studio; studio = null;
            if (openStudio != null) { openStudio.Close(); openStudio.Dispose(); }
            if (notifications != null) notifications.Dispose();
            foreach (ControllerConnection controller in controllers.Values)
            {
                controller.CancelRead();
                controller.CancelWrite();
            }
            if (hotkeyId != 0) UnregisterHotKey(Handle, hotkeyId);
            tray.Dispose();
            if (liveTrayIcon != null) liveTrayIcon.Dispose();
            trayIcon.Dispose();
            trayMenu.Dispose();
            Marshal.FreeHGlobal(buffer);
        }
        base.Dispose(disposing);
    }

    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.Length == 1 && args[0] == "--install-rtss-plugin") return RtssPluginInstaller.Run();
        if (args.Length == 1 && args[0] == "--rtss-self-test") return RtssDiagnostics.SelfTest();
        if (args.Length == 1 && args[0] == "--widget-self-test") return DesktopBatteryWidget.SelfTest(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "widget-self-test.txt"));
        if (args.Length == 1 && args[0] == "--studio-widget-self-test") return StudioWindowPresentation.SelfTest(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "studio-widget-self-test.txt"));
        if (args.Length == 1 && args[0] == "--power-self-test") return Ds4Power.SelfTest(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "power-self-test.txt"));
        if (args.Length == 1 && args[0] == "--power-hold-self-test") return Ds4PowerHold.SelfTest(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "power-hold-self-test.txt"));
        if (args.Length == 1 && args[0] == "--events-export-self-test") return EventLogPage.ExportSelfTest(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "events-export-self-test.txt"));
        if (args.Length == 1 && args[0] == "--battery-scale-self-test") return Ds4BatteryScale.WriteSelfTest(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "battery-scale-self-test.txt"));
        if (args.Length == 3 && args[0] == "--rtss-demo") return RtssDiagnostics.Demo(args[1], args[2]);
        if (args.Length == 2 && args[0] == "--studio-preview")
        {
            try { StudioWindow.RenderPreview(args[1]); return 0; }
            catch (Exception error) { File.WriteAllText(Path.Combine(args[1], "studio-error.txt"), error.ToString()); return 1; }
        }
        if (args.Length == 3 && args[0] == "--check-opengl-window")
        {
            IntPtr window = new IntPtr(long.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture));
            string detected = OpenGlDiscovery.FindExecutable(window);
            File.WriteAllText(args[2], detected ?? "");
            return detected == null ? 1 : 0;
        }
        if (args.Length >= 2 && args[0] == "--smoke-opengl") return OpenGlIntegration.Smoke(args[1], args.Length > 2 ? args[2] : "");
        if (args.Length == 1 && args[0] == "--appearance-self-test")
        {
            try { return CheckBatteryDecode() && ShortcutPress.SelfTest() && PopupPresentation.SelfTest() &&
                OverlayPage.SelfTest() && LightbarSettings.SelfTest() && Ds4Lightbar.SelfTest() && ControllerConnection.SelfTest() && AutomaticVulkanSession.TestLegacyRegistrationPath() && LegacyDisplay.SelfTest() ? 0 : 1; }
            catch (Exception error)
            {
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appearance-test-error.txt"), error.ToString());
                return 1;
            }
        }
        if (args.Length >= 2 && (args[0] == "--smoke" || args[0] == "--smoke-auto" || args[0] == "--smoke-auto-late"))
        {
            try
            {
                bool late = args[0] == "--smoke-auto-late";
                bool automatic = args[0] != "--smoke";
                using (var starter = new BatteryPublisher())
                using (var publisher = late ? null : new BatteryPublisher(automatic))
                {
                    if (publisher != null) publisher.Publish("55%");
                    var launchPublisher = publisher ?? starter;
                    using (Process child = launchPublisher.Start(args[1], args.Length >= 3 ? args[2] : "", true, automatic))
                    {
                        if (late)
                        {
                            string trace = Environment.GetEnvironmentVariable("DS4_VULKAN_DEMO_TRACE");
                            if (string.IsNullOrEmpty(trace)) throw new InvalidOperationException("A demo trace is required for the late-start test.");
                            var deadline = Stopwatch.StartNew();
                            while (!File.Exists(trace) && deadline.ElapsedMilliseconds < 3000)
                                if (child.WaitForExit(50)) return 4;
                            if (!File.Exists(trace) || !File.ReadAllText(trace).Contains("Vulkan presented its first frame")) return 4;
                        }
                        using (var latePublisher = late ? new BatteryPublisher(true) : null)
                        {
                            var active = latePublisher ?? publisher;
                            if (Environment.GetEnvironmentVariable("DS4_VULKAN_TEST_BOTTOM_LEFT") == "1")
                                active.Appearance = new OverlayAppearance { Position = 3 };
                            if (Environment.GetEnvironmentVariable("DS4_VULKAN_TEST_TRANSPARENT") == "1")
                                active.Appearance = new OverlayAppearance { Position = 3, ShowBackground = false };
                            active.Publish("55%");
                            var elapsed = Stopwatch.StartNew();
                            int holdMilliseconds = args.Length > 3 ? int.Parse(args[3]) : 3500;
                            bool livePosition = Environment.GetEnvironmentVariable("DS4_VULKAN_TEST_LIVE_POSITION") == "1";
                            while (elapsed.ElapsedMilliseconds < holdMilliseconds)
                            {
                                if (child.WaitForExit(100)) return child.ExitCode;
                                if (livePosition && elapsed.ElapsedMilliseconds >= 1000 && active.Appearance.Position == 0)
                                    active.Appearance = new OverlayAppearance { Position = 3, CornerRadius = 12 };
                                active.Publish("55%");
                            }
                            active.Publish(null);
                            if (child.WaitForExit(2000)) return child.ExitCode;
                            if (child.CloseMainWindow() && child.WaitForExit(2000)) return child.ExitCode;
                            child.Kill();
                            child.WaitForExit();
                            return late ? 4 : 0;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "smoke-error.txt"), e.ToString());
                return 1;
            }
        }
        try
        {
            if (args.Length == 1 && args[0] == "--tray-smoke")
            {
                bool hidden = false;
                using (var test = new BatteryPublisher())
                using (var trayApp = new Overlay(test, false))
                using (var check = new Timer { Interval = 300 })
                {
                    check.Tick += delegate {
                        var shortcut = Keys.Control | Keys.Alt | Keys.Shift | Keys.F10;
                        bool changed = trayApp.TryChangeShortcut(shortcut, delegate { });
                        if (!changed) File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                            "shortcut-test-error.txt"), Marshal.GetLastWin32Error().ToString());
                        hidden = trayApp.IsHandleCreated && !trayApp.Visible && changed &&
                            trayApp.registeredShortcut == shortcut;
                        Application.ExitThread();
                    };
                    check.Start();
                    Application.Run();
                }
                return hidden ? 0 : 1;
            }

            using (var session = new RtssTestSession())
            using (var trayApp = new Overlay(session.Publisher, true, session.Legacy))
            {
                trayApp.BeginInvoke(new Action(trayApp.Customize));
                Application.Run();
            }
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "DS4 Battery Overlay", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
        return 0;
    }
}

sealed class BatteryPublisher : IDisposable
{
    const int HeaderSize = 16, MaxWidth = 256, MaxHeight = 64;
    const string LayerName = "VK_LAYER_DS4_battery_display";
    readonly int appearanceOffset;
    public OverlayAppearance Appearance { get; set; }
    OverlayAppearance lastAppearance;
    const string AutomaticName = @"Local\DS4VulkanBattery.Display";
    readonly string name;
    internal string FrameName { get { return name; } }
    readonly bool automatic;
    readonly MemoryMappedFile mapping;
    readonly MemoryMappedViewAccessor view;
    readonly byte[] pixelRow;
    int sequence;
    string lastCaption;
    bool lastCharging;
    Bitmap lastPopup;
    byte lastOpacity;
    uint lastPopupId;
    int lastPopupPosition = 2;

    public BatteryPublisher(bool automatic = false, string existingMapping = null)
        : this(automatic, existingMapping, null) { }

    BatteryPublisher(bool automatic, string existingMapping, string newMapping)
    {
        this.automatic = automatic;
        Appearance = new OverlayAppearance();
        name = existingMapping ?? newMapping ?? (automatic ? AutomaticName : "DS4VulkanBattery." + Guid.NewGuid().ToString("N"));
        int bytes = HeaderSize + MaxWidth * MaxHeight * 4 * (newMapping == null ? 1 : 2);
        appearanceOffset = bytes - 16;
        pixelRow = new byte[(newMapping == null ? MaxWidth : 320) * 4];
        if (existingMapping != null)
            mapping = MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.ReadWrite);
        else
            mapping = automatic ? MemoryMappedFile.CreateOrOpen(name, bytes) : MemoryMappedFile.CreateNew(name, bytes);
        view = mapping.CreateViewAccessor();
        if (automatic)
        {
            sequence = view.ReadInt32(0) & ~1;
            view.Write(0, unchecked(++sequence));
            System.Threading.Thread.MemoryBarrier();
            view.Write(12, 0);
            view.Write(4, 0);
            view.Write(8, 0);
            System.Threading.Thread.MemoryBarrier();
            view.Write(0, unchecked(++sequence));
        }
    }

    internal static BatteryPublisher OpenNotificationFrame(string mappingName)
    {
        return new BatteryPublisher(true, null, mappingName ?? @"Local\DS4VulkanBattery.Notification2");
    }

    public Process Start(string executable, string arguments, bool hidden, bool autoTest = false)
    {
        if (!File.Exists(executable)) throw new FileNotFoundException("The selected executable is missing.", executable);
        using (var input = new BinaryReader(File.OpenRead(executable)))
        {
            if (input.ReadUInt16() != 0x5a4d) throw new IOException("Choose a Windows game executable.");
            input.BaseStream.Position = 0x3c;
            int pe = input.ReadInt32();
            if (pe < 0 || pe > input.BaseStream.Length - 6) throw new IOException("The executable has an invalid Windows header.");
            input.BaseStream.Position = pe;
            if (input.ReadUInt32() != 0x4550 || input.ReadUInt16() != 0x8664)
                throw new IOException("This preview supports x64 (64-bit Intel and AMD) Vulkan games.");
        }

        var info = new ProcessStartInfo(executable, arguments)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executable),
            WindowStyle = hidden ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal,
            CreateNoWindow = hidden
        };
        var environment = info.EnvironmentVariables;
        if (autoTest)
        {
            environment["VK_ADD_IMPLICIT_LAYER_PATH"] = CreateManifestDirectory(true);
            environment.Remove("VK_INSTANCE_LAYERS");
            environment.Remove("DS4_BATTERY_FRAME");
            return Process.Start(info);
        }

        string layerPaths = environment["VK_LAYER_PATH"];
        string layerDirectory = CreateManifestDirectory(false);
        if (!string.IsNullOrEmpty(layerPaths))
        {
            layerDirectory += ";" + layerPaths;
            environment["VK_LAYER_PATH"] = layerDirectory;
        }
        else
        {
            string addedPaths = environment["VK_ADD_LAYER_PATH"];
            environment["VK_ADD_LAYER_PATH"] = string.IsNullOrEmpty(addedPaths) ? layerDirectory : layerDirectory + ";" + addedPaths;
        }
        string layers = environment["VK_INSTANCE_LAYERS"];
        environment["VK_INSTANCE_LAYERS"] = string.IsNullOrEmpty(layers) ? LayerName : LayerName + ";" + layers;
        environment["DS4_BATTERY_FRAME"] = name;
        return Process.Start(info);
    }

    internal static string CreateManifestDirectory(bool automatic)
    {
        // Only the hidden test harness uses this isolated loader directory.
        string directory = Path.Combine(Path.GetTempPath(), "D4V-" +
            EmbeddedLayer.VersionKey + (automatic ? "-auto" : "-explicit"));
        EmbeddedLayer.Install(directory, automatic);
        return directory;
    }

    public void Publish(string caption, bool charging = false)
    {
        if (automatic) view.Write(12, Environment.TickCount);
        if (lastCaption == caption && (caption == null || (lastAppearance == Appearance && lastCharging == charging))) return;
        using (Bitmap image = caption == null ? null : CreateBadge(caption, Appearance, charging))
            WriteFrame(image, Appearance.Position, Appearance.Margin,
                Appearance.CornerRadius | (Appearance.ShowBackground ? 0 : 0x100), true);
        lastCaption = caption;
        lastAppearance = Appearance;
        lastCharging = charging;
    }

    internal void PublishNotification(Bitmap image, byte opacity, int position = 2, uint popupId = 0, uint targetProcess = 0)
    {
        view.Write(12, Environment.TickCount);
        if (opacity == 0) { image = null; popupId = 0; }
        bool pixelsChanged = !ReferenceEquals(lastPopup, image);
        if (!pixelsChanged && opacity == lastOpacity && position == lastPopupPosition && popupId == lastPopupId) return;
        WriteFrame(image, position, 12, 0x100 | (opacity << 16), pixelsChanged, popupId, targetProcess);
        lastPopup = image;
        lastOpacity = opacity;
        lastPopupPosition = position;
        lastPopupId = popupId;
    }

    void WriteFrame(Bitmap image, int position, int margin, int style, bool writePixels,
        uint popupId = 0, uint targetProcess = 0)
    {
        view.Write(0, unchecked(++sequence));
        System.Threading.Thread.MemoryBarrier();
        view.Write(4, image == null ? 0 : image.Width);
        view.Write(8, image == null ? 0 : image.Height);
        view.Write(appearanceOffset - sizeof(uint), popupId);
        view.Write(appearanceOffset - 2 * sizeof(uint), targetProcess);
        view.Write(appearanceOffset, 0x34445350u);
        view.Write(appearanceOffset + 4, position);
        view.Write(appearanceOffset + 8, margin);
        view.Write(appearanceOffset + 12, style);
        if (image != null && writePixels)
        {
            var data = image.LockBits(new Rectangle(Point.Empty, image.Size), ImageLockMode.ReadOnly, image.PixelFormat);
            try
            {
                int stride = image.Width * 4;
                for (int y = 0; y < image.Height; y++)
                {
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), pixelRow, 0, stride);
                    view.WriteArray(HeaderSize + y * stride, pixelRow, 0, stride);
                }
            }
            finally { image.UnlockBits(data); }
        }
        System.Threading.Thread.MemoryBarrier();
        view.Write(0, unchecked(++sequence));
    }

    public void Dispose()
    {
        if (lastPopup != null) PublishNotification(null, 0);
        else Publish(null);
        view.Dispose();
        mapping.Dispose();
    }

    internal static int GetPercent(string caption)
    {
        int end = caption.IndexOf('%'), percent;
        return end > 0 && int.TryParse(caption.Substring(0, end), out percent) ? percent : -1;
    }

    internal static Bitmap CreateBadge(string caption, OverlayAppearance appearance, bool charging = false)
    {
        int percent = appearance.ShowIcon ? GetPercent(caption) : -1;
        charging = charging && percent >= 0;
        bool inside = appearance.ShowNumber && appearance.NumberInsideIcon && percent >= 0;
        string text = appearance.ShowNumber ? caption : "";
        if (inside) text = caption.Substring(caption.IndexOf('%') + 1).TrimStart(' ', '·');
        if (text.Length == 0 && !appearance.ShowIcon) return null;
        float scale = appearance.Scale / 100f;
        int height = ScalePixels(32, scale);
        int bodyWidth = inside ? (charging ? 54 : 44) : 26;
        int iconRight = 9 + bodyWidth + 3;
        int textX = ScalePixels(appearance.ShowIcon ? iconRight + 5 : 9, scale);
        using (var font = new Font("Segoe UI", 10f * appearance.Scale / 100, FontStyle.Bold))
        {
            int width = text.Length == 0 ? ScalePixels(iconRight + 9, scale) :
                Math.Min(MaxWidth, TextRenderer.MeasureText(text, font).Width + textX + ScalePixels(12, scale));
            var image = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(image))
            {
                graphics.Clear(appearance.ShowBackground ? appearance.Background : Color.Transparent);
                if (appearance.ShowIcon)
                    DrawBatteryIcon(graphics, appearance, bodyWidth, percent, inside, charging, scale);
                if (text.Length == 0) return image;
                var textBounds = new Rectangle(textX, 0, width - textX - 5, height);
                DrawBadgeText(graphics, text, font, textBounds, appearance, StringAlignment.Near);
            }
            return TrimTextPadding(image, textX, ScalePixels(9, scale), appearance.ShowBackground);
        }
    }

    static Bitmap TrimTextPadding(Bitmap image, int textX, int padding, bool hasBackground)
    {
        // Preserve the drawn text, regardless of differences between measured and rendered glyph widths.
        int mask = hasBackground ? -1 : unchecked((int)0xff000000);
        int blank = hasBackground ? image.GetPixel(image.Width - 1, 0).ToArgb() : 0;
        int last = textX - 1;
        int width = image.Width;
        var row = new int[width];
        var bounds = new Rectangle(Point.Empty, image.Size);
        var data = image.LockBits(bounds, ImageLockMode.ReadOnly, image.PixelFormat);
        try
        {
            for (int y = 0; y < bounds.Height; y++)
            {
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, width);
                for (int x = width - 1; x > last; x--)
                {
                    if ((row[x] & mask) == blank) continue;
                    last = x;
                    break;
                }
            }
        }
        finally { image.UnlockBits(data); }
        int fittedWidth = last + 1 + padding;
        if (last < textX || fittedWidth >= width) return image;
        using (image)
            return image.Clone(new Rectangle(0, 0, fittedWidth, bounds.Height), image.PixelFormat);
    }

    static int ScalePixels(int pixels, double scale)
    {
        return (int)Math.Round(pixels * scale, MidpointRounding.AwayFromZero);
    }

    static void DrawBatteryIcon(Graphics graphics, OverlayAppearance appearance, int bodyWidth,
        int percent, bool inside, bool charging, float scale)
    {
        // Keep the small outline and gaps aligned to the pixel grid without the badge.
        bool alignToPixels = !appearance.ShowBackground;
        int bodyHeight = inside ? 26 : 16;
        var body = new RectangleF(IconPixels(9, scale, alignToPixels), IconPixels((32 - bodyHeight) / 2, scale, alignToPixels),
            IconPixels(bodyWidth, scale, alignToPixels), IconPixels(bodyHeight, scale, alignToPixels));
        float terminalHeight = IconPixels(inside ? 8 : 6, scale, alignToPixels);
        float terminalInset = (body.Height - terminalHeight) / 2;
        float terminalTop = body.Top + (alignToPixels ? (float)Math.Floor(terminalInset) : terminalInset);
        float terminalBottom = terminalTop + terminalHeight;
        float terminalRight = body.Right + IconPixels(3, scale, alignToPixels);
        // Keep one scaled inset around the entire fill, including the gaps at each end.
        float inset = IconPixels(3, scale, alignToPixels);
        var track = RectangleF.Inflate(body, -inset, -inset);
        if (inside) track = new RectangleF(track.X, track.Bottom - inset, track.Width, inset);
        float gap = IconPixels(1, scale, alignToPixels);
        Color color = BatteryColor(percent);
        var state = graphics.Save();
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        using (var brush = new SolidBrush(color))
        {
            if (alignToPixels)
            {
                int solid = (int)scale;
                int coverage = (int)Math.Round((scale - solid) * 255);
                if (solid > 0) FillOutline(graphics, brush, body, solid);
                if (coverage > 0)
                    using (var edge = new SolidBrush(Color.FromArgb(coverage, color)))
                        FillOutline(graphics, edge, RectangleF.Inflate(body, -solid, -solid), 1);
                graphics.FillRectangle(brush, body.Right, terminalTop, terminalRight - body.Right, terminalHeight);
            }
            else
                using (var outline = new GraphicsPath())
                {
                    outline.AddPolygon(new[] {
                        new PointF(body.Left, body.Top), new PointF(body.Right, body.Top),
                        new PointF(body.Right, terminalTop), new PointF(terminalRight, terminalTop),
                        new PointF(terminalRight, terminalBottom), new PointF(body.Right, terminalBottom),
                        new PointF(body.Right, body.Bottom), new PointF(body.Left, body.Bottom)
                    });
                    outline.AddRectangle(RectangleF.Inflate(body, -scale, -scale));
                    graphics.FillPath(brush, outline);
                }
            DrawBatteryFill(graphics, brush, track, percent, appearance.SegmentedIcon, gap, alignToPixels);
        }
        graphics.Restore(state);
        if (charging)
        {
            float x = inside ? track.Right - 8 * scale : body.X + (body.Width - 8 * scale) / 2;
            float y = inside ? body.Top + 5 * scale : body.Y + (body.Height - 11 * scale) / 2;
            ChargingBolt.Draw(graphics, new RectangleF(x, y, 8 * scale, 11 * scale), true);
        }
        if (!inside) return;

        float numberTop = body.Top + 2 * scale;
        float numberWidth = track.Width - (charging ? 10 * scale : 0);
        var bounds = new RectangleF(track.X, numberTop, numberWidth, track.Top - numberTop - scale);
        using (var font = new Font("Segoe UI", 12 * scale, FontStyle.Bold, GraphicsUnit.Pixel))
            DrawBadgeText(graphics, percent + "%", font, bounds, appearance, StringAlignment.Center);
    }

    static void FillOutline(Graphics graphics, Brush brush, RectangleF bounds, int stroke)
    {
        using (var outline = new GraphicsPath())
        {
            outline.AddRectangle(bounds);
            outline.AddRectangle(RectangleF.Inflate(bounds, -stroke, -stroke));
            graphics.FillPath(brush, outline);
        }
    }

    internal static Color BatteryColor(int percent)
    {
        return percent < 0 ? Color.SlateGray : percent <= 20 ? Color.LightCoral
            : percent <= 50 ? Color.Gold : Color.MediumSpringGreen;
    }

    internal static void DrawPopupBattery(Graphics graphics, int percent, bool charging, bool segmented)
    {
        const float scale = .86f;
        const int bodyWidth = 26, batteryWidth = bodyWidth + 3;
        var state = graphics.Save();
        graphics.TranslateTransform(28 - (9 + batteryWidth / 2f) * scale, 30 - 16 * scale);
        DrawBatteryIcon(graphics, new OverlayAppearance { ShowBackground = true, SegmentedIcon = segmented },
            bodyWidth, percent, false, charging, scale);
        graphics.Restore(state);
    }

    static float IconPixels(int units, float scale, bool alignToPixels)
    {
        return alignToPixels ? ScalePixels(units, scale) : units * scale;
    }

    static void DrawBadgeText(Graphics graphics, string text, Font font, RectangleF bounds,
        OverlayAppearance appearance, StringAlignment alignment)
    {
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using (var brush = new SolidBrush(appearance.Text))
        using (var format = new StringFormat {
            Alignment = alignment, LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter })
            graphics.DrawString(text, font, brush, bounds, format);
    }

    static void DrawBatteryFill(Graphics graphics, Brush brush, RectangleF track, int percent, bool segmented, float gap,
        bool alignToPixels)
    {
        if (percent <= 0) return;
        using (var fill = new GraphicsPath())
        {
            if (!segmented)
            {
                track.Width *= Math.Min(100, percent) / 100f;
                if (alignToPixels)
                    track.Width = Math.Max(1, ScalePixels(1, track.Width));
                fill.AddRectangle(track);
            }
            else
            {
                const int count = 5;
                float barWidth = (track.Width - (count - 1) * gap) / count;
                // Match the bar count for both wired and wireless estimates of each DS4 level.
                int bars = percent < 10 ? 0 : Math.Min(count, 1 + (percent - 10) / 20);
                for (int bar = 0; bar < bars; bar++)
                {
                    float start = bar * barWidth, end = (bar + 1) * barWidth;
                    if (alignToPixels)
                    {
                        start = ScalePixels(bar, barWidth);
                        end = ScalePixels(bar + 1, barWidth);
                    }
                    fill.AddRectangle(new RectangleF(track.X + start + bar * gap, track.Y, end - start, track.Height));
                }
            }
            graphics.FillPath(brush, fill);
        }
    }
}

sealed class AutomaticVulkanSession : IDisposable
{
    const string RegistryPath = @"SOFTWARE\Khronos\Vulkan\ImplicitLayers";
    static readonly string installDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DS4VulkanOverlay");
    static readonly string manifestPath = Path.Combine(installDirectory, "DS4BatteryLayer.json");
    readonly System.Threading.Mutex sessionLock = new System.Threading.Mutex(false, @"Local\DS4VulkanBattery.AutoOwner");
    public BatteryPublisher Publisher { get; private set; }
    public LegacyDisplay Legacy { get; private set; }

    public AutomaticVulkanSession()
    {
        bool acquired = false;
        try
        {
            try { acquired = sessionLock.WaitOne(0); }
            catch (System.Threading.AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new InvalidOperationException("Another DS4 Battery Overlay is running. Quit it from the system tray, then open this version again.");
            EmbeddedLayer.Install(installDirectory, true);
            Publisher = new BatteryPublisher(true);
            Legacy = LegacyDisplay.Connect();
            using (var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
            using (var key = root.CreateSubKey(RegistryPath))
            {
                key.SetValue(manifestPath, 0, RegistryValueKind.DWord);
                foreach (string entry in key.GetValueNames())
                    if (IsLegacyRegistrationPath(entry)) key.DeleteValue(entry, false);
            }
        }
        catch
        {
            if (Legacy != null) Legacy.Dispose();
            if (Publisher != null) Publisher.Dispose();
            if (acquired) sessionLock.ReleaseMutex();
            sessionLock.Dispose();
            throw;
        }
    }

    static bool IsLegacyRegistrationPath(string path)
    {
        string prefix = Path.Combine(Path.GetTempPath(), "DS4VulkanOverlay-");
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

        // Earlier releases registered a manifest in an app-specific temp directory.
        // Keeping it alongside the installed manifest can select the old renderer.
        return System.Text.RegularExpressions.Regex.IsMatch(path.Substring(prefix.Length),
            @"\A[0-9A-Fa-f]{24}\\DS4BatteryLayer\.json\z", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    internal static bool TestLegacyRegistrationPath()
    {
        string valid = Path.Combine(Path.GetTempPath(), "DS4VulkanOverlay-ABCDEF012345678901234567", "DS4BatteryLayer.json");
        return IsLegacyRegistrationPath(valid) && !IsLegacyRegistrationPath(manifestPath) &&
            !IsLegacyRegistrationPath(Path.ChangeExtension(valid, ".dll"));
    }

    public void Dispose()
    {
        try
        {
            if (Legacy != null) Legacy.Dispose();
        }
        finally { DisposePublisher(); }
    }

    void DisposePublisher()
    {
        try { Publisher.Dispose(); }
        finally
        {
            sessionLock.ReleaseMutex();
            sessionLock.Dispose();
        }
    }
}
static class EmbeddedLayer
{
    static readonly byte[] Library = ReadLibrary();
    static readonly string Digest = HashLibrary();
    public static string VersionKey { get { return Digest.Substring(0, 20); } }
    public static string LibraryName { get { return "DS4BatteryLayer-" + VersionKey + ".dll"; } }

    static byte[] ReadLibrary()
    {
        using (var stream = typeof(EmbeddedLayer).Assembly.GetManifestResourceStream("DS4BatteryLayer"))
        using (var result = new MemoryStream())
        {
            if (stream == null) throw new InvalidDataException("The built-in Vulkan renderer is missing.");
            stream.CopyTo(result);
            return result.ToArray();
        }
    }

    static string HashLibrary()
    {
        using (var sha = System.Security.Cryptography.SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(Library)).Replace("-", "");
    }

    public static void Install(string directory, bool automatic)
    {
        Directory.CreateDirectory(directory);
        string library = Path.Combine(directory, LibraryName);
        if (!File.Exists(library)) WriteAtomically(library, Library);
        string manifest;
        using (var stream = typeof(EmbeddedLayer).Assembly.GetManifestResourceStream("DS4BatteryManifest"))
        {
            if (stream == null) throw new InvalidDataException("The built-in Vulkan registration is missing.");
            using (var reader = new StreamReader(stream)) manifest = reader.ReadToEnd();
        }

        manifest = manifest.Replace("./DS4BatteryLayer.dll", library.Replace("\\", "\\\\"));
        if (automatic)
            manifest = manifest.Replace("\"description\":", "\"disable_environment\": {\"DS4_DISABLE_VULKAN_OVERLAY\": \"1\"},\n    \"description\":");
        string path = Path.Combine(directory, "DS4BatteryLayer.json");
        if (!File.Exists(path) || File.ReadAllText(path) != manifest)
            WriteAtomically(path, Encoding.UTF8.GetBytes(manifest));
    }

    static void WriteAtomically(string path, byte[] data)
    {
        string staged = Path.Combine(Path.GetDirectoryName(path), "d4-" + Guid.NewGuid().ToString("N").Substring(0, 12) + ".tmp");
        try
        {
            File.WriteAllBytes(staged, data);
            if (File.Exists(path)) File.Replace(staged, path, null);
            else File.Move(staged, path);
        }
        finally { if (File.Exists(staged)) File.Delete(staged); }
    }
}
sealed class LegacyDisplay : IDisposable
{
    const string LegacyName = @"Local\DS4VulkanBattery.Automatic";
    readonly BatteryPublisher publisher;
    uint lastForeground;
    long nextCheck;
    bool modernGame;
    readonly Stopwatch timer = Stopwatch.StartNew();

    LegacyDisplay(BatteryPublisher publisher)
    {
        this.publisher = publisher;
    }

    public static LegacyDisplay Connect(string name = LegacyName)
    {
        try { return new LegacyDisplay(new BatteryPublisher(true, name)); }
        catch (FileNotFoundException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (System.Security.SecurityException) { return null; }
    }

    public void SetAppearance(OverlayAppearance current)
    {
        publisher.Appearance = new OverlayAppearance {
            Position = current.Position, Margin = current.Margin, Scale = current.Scale,
            ShowIcon = current.ShowIcon, SegmentedIcon = current.SegmentedIcon, ShowNumber = current.ShowNumber,
            NumberInsideIcon = current.NumberInsideIcon,
            Background = current.Background, Text = current.Text
        };
    }

    public void Publish(string caption, uint foreground, bool charging = false)
    {
        // Stop feeding the old layer in a game where the new renderer is present.
        long now = timer.ElapsedMilliseconds;
        if (lastForeground != foreground || now >= nextCheck)
        {
            lastForeground = foreground;
            nextCheck = now + 2000;
            modernGame = HasModernRenderer(foreground);
        }
        publisher.Publish(modernGame ? null : caption, charging);
    }

    static bool HasModernRenderer(uint pid)
    {
        if (pid == 0) return false;
        try
        {
            using (Process process = Process.GetProcessById((int)pid))
                foreach (ProcessModule module in process.Modules)
                    if (string.Equals(module.ModuleName, EmbeddedLayer.LibraryName, StringComparison.OrdinalIgnoreCase))
                        return true;
        }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        catch (System.Security.SecurityException) { }
        return false;
    }

    public void Dispose() { publisher.Dispose(); }

    public static bool SelfTest()
    {
        string name = "DS4LegacyTest." + Guid.NewGuid().ToString("N");
        using (var map = MemoryMappedFile.CreateNew(name, 16 + 256 * 64 * 4))
        using (var read = map.CreateViewAccessor())
        using (var legacy = Connect(name))
        {
            if (legacy == null) return false;
            legacy.SetAppearance(new OverlayAppearance { Position = 1, ShowBackground = false });
            legacy.Publish("55%", 0);
            if (read.ReadInt32(4) <= 0 || read.ReadInt32(8) != 32 || read.ReadByte(16 + 3) != 255) return false;
            legacy.Publish(null, 0);
            return read.ReadInt32(4) == 0 && read.ReadInt32(8) == 0;
        }
    }
}
sealed class ControllerConnection
{
    internal const int LowBatteryPercent = 20;
    const int RearmPercent = 25;
    const long ReadingLifetimeMs = 6000;
    const long RetryDelayMs = 8000;
    const long ConfirmAfterMs = 300;
    const long LightbarConnectDelayMs = 3000;
    const long CandidateGapMs = 1500;
    const long MissingDeviceGraceMs = 6500;

    public readonly string Path;
    public readonly bool BluetoothPath;
    internal readonly Ds4PowerHold PowerHold = new Ds4PowerHold();
    internal bool CableConnected;
    public bool? Bluetooth;
    public bool? LightbarBluetooth { get { return Bluetooth ?? (BluetoothPath ? (bool?)true : null); } }
    public long LastRawInput = -1, NextDirectRead, NextBluetoothReportRequest;
    public System.Threading.CancellationTokenSource PendingRead;
    public string Battery { get; private set; }
    public bool Charging { get; private set; }
    public long Updated { get; private set; }
    public bool Connected { get; private set; }
    public System.Threading.CancellationTokenSource Pending;
    readonly ControllerEventLog events;
    readonly ControllerNotifications notifications;
    int percent = -1;
    int loggedPercent = -1;
    bool loggedCharging;
    bool lowNoticeSent;
    LightbarSettings lastSettings;
    int lastColor;
    bool lastBluetoothReady;
    bool written;
    long retryAt;
    int writeFailures;
    long lightbarReadyAt;
    long firstInput = -1;
    long lastInput = -1;
    int missingScans;

    public ControllerConnection(string path, ControllerEventLog events = null, ControllerNotifications notifications = null)
    {
        Path = path;
        BluetoothPath = path.IndexOf("VID&", StringComparison.OrdinalIgnoreCase) >= 0 ||
            path.IndexOf("BTHENUM", StringComparison.OrdinalIgnoreCase) >= 0;
        this.events = events;
        this.notifications = notifications;
        Battery = "";
    }

    public bool ObserveInput(long now)
    {
        if (firstInput < 0 || (!Connected && now - lastInput >= CandidateGapMs)) firstInput = now;
        lastInput = now;
        if (Connected || now - firstInput < ConfirmAfterMs) return false;
        Connected = true;
        lightbarReadyAt = now + LightbarConnectDelayMs;
        if (events != null) events.Add("DualShock 4 connected");
        return true;
    }

    public bool ObservePresence(bool present, long now)
    {
        missingScans = present ? 0 : Math.Min(2, missingScans + 1);
        return missingScans >= 2 && (lastInput < 0 || now - lastInput >= MissingDeviceGraceMs);
    }

    public bool ShouldReadDirectly(long now)
    {
        return PendingRead == null && now >= NextDirectRead &&
            (LastRawInput < 0 || now - LastRawInput >= 1200 || !HasReading(now));
    }

    public void CancelRead()
    {
        if (PendingRead != null && !PendingRead.IsCancellationRequested) PendingRead.Cancel();
    }

    public bool Disconnect()
    {
        bool wasConnected = Connected;
        Connected = false;
        firstInput = lastInput = -1;
        LastRawInput = -1;
        missingScans = 0;
        Bluetooth = null;
        NextDirectRead = NextBluetoothReportRequest = 0;
        PowerHold.Reset();
        CableConnected = false;
        Battery = "";
        Charging = false;
        Updated = 0;
        percent = -1;
        loggedPercent = -1;
        loggedCharging = false;
        lowNoticeSent = false;
        lastSettings = null;
        lastColor = 0;
        lastBluetoothReady = false;
        written = false;
        retryAt = 0;
        writeFailures = 0;
        lightbarReadyAt = 0;
        CancelRead();
        CancelWrite();
        if (wasConnected && events != null) events.Add("DualShock 4 disconnected");
        return wasConnected;
    }

    public bool HasReading(long now)
    {
        return Connected && percent >= 0 && now - Updated < ReadingLifetimeMs;
    }

    public string DisplayReading(long now)
    {
        if (!Connected || percent < 0) return "";
        return HasReading(now) ? Battery : percent + "% · Last";
    }

    public bool RecordBattery(string reading, bool onCable, bool charging, long now)
    {
        Battery = reading;
        CableConnected = onCable;
        Charging = charging;
        percent = BatteryPublisher.GetPercent(reading);
        Updated = now;
        if (onCable || percent >= RearmPercent) lowNoticeSent = false;
        bool low = Connected && !onCable && percent >= 0 && percent <= LowBatteryPercent && !lowNoticeSent;
        if (low) lowNoticeSent = true;
        TrackBattery(onCable, low);
        return low;
    }

    void TrackBattery(bool onCable, bool low)
    {
        if ((events == null && notifications == null) || !Connected || percent < 0 ||
            (percent == loggedPercent && Charging == loggedCharging)) return;

        string level = percent + "%";
        string connection = Bluetooth.HasValue ? (Bluetooth.Value ? " (Bluetooth)" : " (USB)") : "";
        if (loggedPercent < 0)
        {
            if (events != null) events.Add("Battery reading " + level + connection + (Charging ? " · Charging" : ""));
        }
        else
        {
            if (loggedPercent != percent)
            {
                if (events != null) events.Add("Battery changed from " + loggedPercent + "% to " + level + connection);
                if (notifications != null && !(low && notifications.Settings.Allows(NoticeKind.LowBattery)))
                    notifications.Enqueue(NoticeKind.BatteryLevel, "DualShock 4 battery at " + level,
                        percent: percent, charging: Charging);
            }
            if (loggedCharging != Charging)
            {
                bool full = !Charging && onCable && percent == 100;
                string title = Charging ? "Charging started" : full ? "Charging complete" : "Charging stopped";
                if (events != null) events.Add(full ? title : title + " at " + level);
                if (notifications != null)
                    notifications.Enqueue(Charging ? NoticeKind.ChargingStarted : full ? NoticeKind.ChargingComplete
                        : NoticeKind.ChargingStopped, title, "DualShock 4 battery at " + level + ".", percent, Charging);
            }
        }
        loggedPercent = percent;
        loggedCharging = Charging;
    }

    public bool TryPlan(LightbarSettings settings, long now, out Color color)
    {
        color = Color.Empty;
        if (!Connected || now < lightbarReadyAt || !settings.Enabled || Pending != null) return false;
        color = settings.Automatic && percent >= 0 ? settings.ColorFor(percent) : settings.ManualColor;
        bool bluetoothReady = Bluetooth == true;
        // A write before the first full Bluetooth report gets one more chance
        // once the controller starts sending those reports.
        bool sameRequest = settings == lastSettings && color.ToArgb() == lastColor && bluetoothReady == lastBluetoothReady;
        if (sameRequest && (written || now < retryAt)) return false;

        if (!sameRequest) writeFailures = 0;
        lastSettings = settings;
        lastColor = color.ToArgb();
        lastBluetoothReady = bluetoothReady;
        written = false;
        return true;
    }

    public void EndWrite(bool succeeded, long now)
    {
        written = succeeded;
        if (succeeded)
        {
            writeFailures = 0;
            retryAt = 0;
        }
        else
        {
            writeFailures = Math.Min(writeFailures + 1, 3);
            retryAt = now + (writeFailures == 1 ? 600 : writeFailures == 2 ? 1500 : RetryDelayMs);
        }
    }

    public void CancelWrite()
    {
        if (Pending != null) Pending.Cancel();
    }

    internal static bool SelfTest()
    {
        var controller = new ControllerConnection("test");
        var manual = new LightbarSettings { ManualColor = Color.Purple };
        Color color;

        // Startup discovery, a single packet, and intermittent probes cannot announce a connection.
        if (controller.Disconnect() || controller.ObserveInput(0) ||
            controller.RecordBattery("15%", false, false, 0) || controller.TryPlan(manual, 200, out color) ||
            controller.ObserveInput(2000) || controller.Disconnect()) return false;

        // A quiet device remains connected while Windows lists it. A missed removal needs two absent scans.
        for (int cycle = 0; cycle < 4; ++cycle)
        {
            long time = 10000L + cycle * 10000L;
            if (controller.ObserveInput(time - 3000) || !controller.ObserveInput(time - 2700) ||
                controller.ObserveInput(time + 400)) return false;
            if (!controller.TryPlan(manual, time + 400, out color) ||
                color.ToArgb() != Color.Purple.ToArgb()) return false;
            controller.EndWrite(true, time + 400);
            if (controller.TryPlan(manual, time + 401, out color)) return false;

            var automatic = new LightbarSettings { Automatic = true };
            if (!controller.TryPlan(automatic, time + 401, out color) ||
                color.ToArgb() != automatic.ManualColor.ToArgb()) return false;
            controller.EndWrite(true, time + 401);
            if (!controller.RecordBattery("15%", false, false, time + 402) ||
                controller.RecordBattery("15%", false, false, time + 403) ||
                controller.RecordBattery("15%", true, true, time + 404) || !controller.Charging ||
                !controller.RecordBattery("15%", false, false, time + 405) || controller.Charging) return false;
            if (!controller.TryPlan(automatic, time + 406, out color) ||
                color.ToArgb() != automatic.ColorFor(15).ToArgb()) return false;
            controller.EndWrite(false, time + 406);
            if (controller.TryPlan(automatic, time + 407, out color)) return false;
            automatic.Enabled = false;
            if (controller.TryPlan(automatic, time + 408, out color)) return false;

            using (var pending = new System.Threading.CancellationTokenSource())
            {
                controller.Pending = pending;
                if (cycle % 2 == 0)
                {
                    if (!controller.Disconnect()) return false;
                }
                else
                {
                    controller.LastRawInput = time + 405;
                    if (controller.ShouldReadDirectly(time + 900) || !controller.ShouldReadDirectly(time + 1605) ||
                        controller.ObservePresence(true, time + 6900) || !controller.Connected ||
                        controller.DisplayReading(time + 6900) != "15% · Last" ||
                        controller.ObservePresence(false, time + 6901) ||
                        !controller.ObservePresence(false, time + 7901) || !controller.Disconnect()) return false;
                }
                if (!pending.IsCancellationRequested || controller.Connected ||
                    controller.HasReading(time + 6901) || controller.Disconnect()) return false;
                controller.Pending = null;
            }
        }
        return CheckConnectionLightbar() && CheckReportRecovery() && CheckEventHistory();
    }

    static bool CheckConnectionLightbar()
    {
        var controller = new ControllerConnection("BTHENUM test");
        var settings = new LightbarSettings { Automatic = true, ManualColor = Color.Purple };
        controller.ObserveInput(0);
        controller.ObserveInput(300);
        Color color;
        if (controller.TryPlan(settings, 300, out color) || controller.TryPlan(settings, 3299, out color) ||
            controller.LightbarBluetooth != true || !controller.TryPlan(settings, 3300, out color) ||
            color.ToArgb() != Color.Purple.ToArgb()) return false;
        controller.EndWrite(true, 3300);
        if (controller.TryPlan(settings, 3400, out color)) return false;

        controller.Bluetooth = true;
        if (!controller.TryPlan(settings, 3450, out color)) return false;
        controller.EndWrite(false, 3450);
        if (controller.TryPlan(settings, 4049, out color) || !controller.TryPlan(settings, 4050, out color))
            return false;
        controller.EndWrite(false, 4050);
        if (controller.TryPlan(settings, 5549, out color) || !controller.TryPlan(settings, 5550, out color))
            return false;
        controller.EndWrite(false, 5550);
        if (controller.TryPlan(settings, 8000, out color)) return false;

        controller.RecordBattery("75%", false, false, 8000);
        if (!controller.TryPlan(settings, 8000, out color) ||
            color.ToArgb() != settings.ColorFor(75).ToArgb()) return false;
        controller.EndWrite(true, 8000);
        if (controller.TryPlan(settings, 20000, out color)) return false;
        controller.Disconnect();
        controller.ObserveInput(20100);
        controller.ObserveInput(20400);
        if (controller.TryPlan(settings, 23399, out color) || !controller.TryPlan(settings, 23400, out color)) return false;
        settings.Enabled = false;
        return !controller.TryPlan(settings, 23401, out color);
    }

    static bool CheckReportRecovery()
    {
        int offset;
        if (!Ds4Input.TryLocateReport(0x11, 78, true, out offset) || offset != 32 ||
            !Ds4Input.TryLocateReport(1, 64, false, out offset) || offset != 30 ||
            !Ds4Input.TryLocateReport(1, 64, true, out offset) || offset != -1 ||
            Ds4Input.TryLocateReport(0x11, 77, true, out offset)) return false;

        var log = new ControllerEventLog();
        var controller = new ControllerConnection("bluetooth test", log);
        controller.ObserveInput(0);
        controller.ObserveInput(300);
        controller.LastRawInput = 300;
        controller.RecordBattery("85%", false, false, 300);
        if (!controller.ShouldReadDirectly(1500) || controller.ObservePresence(true, 7500) ||
            controller.DisplayReading(7500) != "85% · Last" || controller.HasReading(7500)) return false;
        controller.ObserveInput(7600);
        controller.RecordBattery("75%", false, false, 7600);
        if (!controller.Connected || !controller.HasReading(7600) || controller.DisplayReading(7600) != "75%") return false;
        var entries = log.Snapshot();
        return entries.Length == 3 && entries[0].Message.StartsWith("Battery changed from 85% to 75%") &&
            entries[2].Message == "DualShock 4 connected";
    }

    static bool CheckEventHistory()
    {
        var log = new ControllerEventLog();
        var controller = new ControllerConnection("events test", log) { Bluetooth = true };
        controller.ObserveInput(0);
        controller.RecordBattery("25%", false, false, 0);
        if (log.Snapshot().Length != 0) return false;
        controller.ObserveInput(300);
        controller.RecordBattery("25%", false, false, 300);
        controller.RecordBattery("25%", false, false, 301);
        controller.RecordBattery("15%", false, false, 302);
        controller.RecordBattery("15%", true, true, 303);
        controller.RecordBattery("15%", true, true, 304);
        controller.RecordBattery("15%", false, false, 305);
        controller.RecordBattery("100%", true, true, 306);
        controller.RecordBattery("100% · Full", true, false, 307);
        controller.Disconnect();
        controller.Disconnect();

        string[] expected = {
            "DualShock 4 disconnected", "Charging complete", "Charging started at 100%",
            "Battery changed from 15% to 100% (Bluetooth)", "Charging stopped at 15%", "Charging started at 15%",
            "Battery changed from 25% to 15% (Bluetooth)", "Battery reading 25% (Bluetooth)", "DualShock 4 connected"
        };
        var entries = log.Snapshot();
        if (entries.Length != expected.Length) return false;
        for (int i = 0; i < entries.Length; i++)
            if (entries[i].Message != expected[i] || entries[i].Time.Kind != DateTimeKind.Local) return false;
        return true;
    }
}

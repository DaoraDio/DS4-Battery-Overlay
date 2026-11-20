using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

sealed class OverlayAppearance
{
    internal const string PreferencesPath = @"Software\DS4OverlayRTSSTest";
    public int Position = 0;
    public int Margin = 12;
    public int Scale = 100;
    public int CornerRadius;
    public const Keys DefaultShortcut = Keys.Control | Keys.Shift | Keys.F8;
    public Keys Shortcut = DefaultShortcut;
    public bool ShowIcon = true;
    public bool SegmentedIcon = true;
    public bool ShowNumber = true;
    public bool NumberInsideIcon;
    public bool ShowDisconnectedIcon;
    public bool ShowDesktopWidget;
    public DesktopWidgetStyle Widget = new DesktopWidgetStyle();
    public bool ShowBackground = true;
    public Color Background = Color.FromArgb(27, 33, 41);
    public Color Text = Color.WhiteSmoke;

    internal OverlayAppearance Copy()
    {
        var copy = (OverlayAppearance)MemberwiseClone();
        copy.Widget = (Widget ?? new DesktopWidgetStyle()).Copy();
        return copy;
    }
    internal static int Read(RegistryKey key, string name, int fallback, int min, int max)
    {
        object value = key == null ? null : key.GetValue(name);
        if (!(value is int)) return fallback;
        int number = (int)value;
        return number < min || number > max ? fallback : number;
    }

    public static OverlayAppearance Load()
    {
        var settings = new OverlayAppearance();
        using (var key = Registry.CurrentUser.OpenSubKey(PreferencesPath))
        {
            settings.Position = Read(key, "Position", settings.Position, 0, 5);
            settings.Margin = Read(key, "Margin", settings.Margin, 0, 96);
            settings.Scale = Read(key, "Scale", settings.Scale, 75, 150);
            settings.CornerRadius = Read(key, "CornerRadius", 0, 0, 16);
            Keys shortcut = (Keys)Read(key, "Shortcut", (int)DefaultShortcut, 0, (int)(Keys.Control | Keys.Alt | Keys.Shift | Keys.KeyCode));
            if (ValidShortcut(shortcut)) settings.Shortcut = shortcut;
            settings.ShowIcon = Read(key, "ShowIcon", 1, 0, 1) != 0;
            settings.SegmentedIcon = Read(key, "SegmentedIcon", 1, 0, 1) != 0;
            settings.ShowNumber = Read(key, "ShowNumber", 1, 0, 1) != 0;
            settings.NumberInsideIcon = Read(key, "NumberInsideIcon", 0, 0, 1) != 0;
            settings.ShowDisconnectedIcon = Read(key, "ShowDisconnectedIcon", 0, 0, 1) != 0;
            settings.ShowDesktopWidget = Read(key, "ShowDesktopWidget", 0, 0, 1) != 0;
            settings.ShowBackground = Read(key, "ShowBackground", 1, 0, 1) != 0;
            settings.Background = Color.FromArgb(Read(key, "Background", settings.Background.ToArgb(), int.MinValue, int.MaxValue));
            settings.Text = Color.FromArgb(Read(key, "Text", settings.Text.ToArgb(), int.MinValue, int.MaxValue));
            settings.Widget = DesktopWidgetStyle.Load(key, settings);
        }
        return settings;
    }

    public void Save()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(PreferencesPath))
        {
            key.SetValue("Position", Position);
            key.SetValue("Margin", Margin);
            key.SetValue("Scale", Scale);
            key.SetValue("CornerRadius", CornerRadius);
            key.SetValue("Shortcut", (int)Shortcut);
            key.SetValue("ShowIcon", ShowIcon ? 1 : 0);
            key.SetValue("SegmentedIcon", SegmentedIcon ? 1 : 0);
            key.SetValue("ShowNumber", ShowNumber ? 1 : 0);
            key.SetValue("NumberInsideIcon", NumberInsideIcon ? 1 : 0);
            key.SetValue("ShowDisconnectedIcon", ShowDisconnectedIcon ? 1 : 0);
            key.SetValue("ShowDesktopWidget", ShowDesktopWidget ? 1 : 0);
            key.SetValue("ShowBackground", ShowBackground ? 1 : 0);
            key.SetValue("Background", Background.ToArgb());
            key.SetValue("Text", Text.ToArgb());
            Widget.Save(key);
        }
    }

    internal static bool ValidShortcut(Keys shortcut)
    {
        var key = shortcut & Keys.KeyCode;
        return (int)key >= 8 && (int)key <= 254 && key != Keys.ShiftKey &&
            key != Keys.ControlKey && key != Keys.Menu && key != Keys.LShiftKey &&
            key != Keys.RShiftKey && key != Keys.LControlKey && key != Keys.RControlKey &&
            key != Keys.LMenu && key != Keys.RMenu && key != Keys.LWin && key != Keys.RWin;
    }

    internal static string FormatShortcut(Keys shortcut)
    {
        return ((shortcut & Keys.Control) != 0 ? "Ctrl+" : "") +
            ((shortcut & Keys.Alt) != 0 ? "Alt+" : "") +
            ((shortcut & Keys.Shift) != 0 ? "Shift+" : "") + (shortcut & Keys.KeyCode);
    }

}

sealed class LightbarSettings
{
    public static readonly int[] Thresholds = { 100, 90, 75, 50, 25, 10, 0 };
    public bool Enabled = true;
    public bool Automatic;
    public int Brightness = 60;
    public Color ManualColor = Color.DeepSkyBlue;
    public readonly Color[] Colors = {
        Color.DeepSkyBlue, Color.Orange, Color.Orange, Color.LimeGreen, Color.Gold, Color.Red, Color.Red
    };

    public Color ColorFor(int percentage)
    {
        for (int index = 0; index < Thresholds.Length; ++index)
            if (percentage >= Thresholds[index]) return Colors[index];
        return Colors[Colors.Length - 1];
    }

    public static LightbarSettings Load()
    {
        var settings = new LightbarSettings();
        using (var key = Registry.CurrentUser.OpenSubKey(OverlayAppearance.PreferencesPath))
        {
            settings.Enabled = OverlayAppearance.Read(key, "LightbarEnabled", 1, 0, 1) == 1;
            settings.Automatic = OverlayAppearance.Read(key, "LightbarAutomatic", 0, 0, 1) == 1;
            settings.Brightness = OverlayAppearance.Read(key, "LightbarBrightness", settings.Brightness, 0, 100);
            settings.ManualColor = Color.FromArgb(OverlayAppearance.Read(key, "LightbarColor",
                settings.ManualColor.ToArgb(), int.MinValue, int.MaxValue));
            settings.ReadColors((threshold, fallback) => OverlayAppearance.Read(key, "LightbarAt" + threshold,
                fallback, int.MinValue, int.MaxValue));
        }
        return settings;
    }

    void ReadColors(Func<int, int, int> read)
    {
        // Load from the bottom so the added thresholds can inherit the previously saved range.
        for (int index = Thresholds.Length - 1; index >= 0; --index)
        {
            int threshold = Thresholds[index];
            Color fallback = threshold == 90 || threshold == 10 ? Colors[index + 1] : Colors[index];
            Colors[index] = Color.FromArgb(read(threshold, fallback.ToArgb()));
        }
    }

    public void Save()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(OverlayAppearance.PreferencesPath))
        {
            key.SetValue("LightbarEnabled", Enabled ? 1 : 0);
            key.SetValue("LightbarAutomatic", Automatic ? 1 : 0);
            key.SetValue("LightbarBrightness", Brightness);
            key.SetValue("LightbarColor", ManualColor.ToArgb());
            for (int index = 0; index < Thresholds.Length; ++index)
                key.SetValue("LightbarAt" + Thresholds[index], Colors[index].ToArgb());
        }
    }

    internal static bool SelfTest()
    {
        var settings = new LightbarSettings();
        int[] levels = { 100, 99, 90, 89, 75, 74, 50, 49, 25, 24, 10, 9, 0 };
        int[] choices = { 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6 };
        settings.Colors[1] = Color.Purple;
        settings.Colors[5] = Color.Cyan;
        for (int i = 0; i < levels.Length; i++)
            if (settings.ColorFor(levels[i]).ToArgb() != settings.Colors[choices[i]].ToArgb()) return false;

        var migrated = new LightbarSettings();
        migrated.ReadColors((threshold, fallback) => {
            if (threshold == 75) return Color.Purple.ToArgb();
            if (threshold == 0) return Color.Cyan.ToArgb();
            return fallback;
        });
        if (migrated.ColorFor(95).ToArgb() != Color.Purple.ToArgb() ||
            migrated.ColorFor(15).ToArgb() != Color.Cyan.ToArgb()) return false;

        migrated.ReadColors((threshold, fallback) => threshold == 10 ? Color.Tomato.ToArgb() : fallback);
        return migrated.ColorFor(10).ToArgb() == Color.Tomato.ToArgb() &&
            migrated.ColorFor(5).ToArgb() == Color.Cyan.ToArgb();
    }
}

static class Ds4Lightbar
{
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    struct HidCapabilities { [FieldOffset(6)] public ushort OutputLength; }

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32", SetLastError = true)]
    static extern bool CancelIoEx(SafeFileHandle handle, IntPtr overlapped);
    [DllImport("hid")] [return: MarshalAs(UnmanagedType.U1)] static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr data);
    [DllImport("hid")] [return: MarshalAs(UnmanagedType.U1)] static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid")] static extern int HidP_GetCaps(IntPtr data, out HidCapabilities caps);

    public static async Task Send(string path, bool? bluetooth, Color color, int brightness, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        using (var handle = CreateFile(path, 0x40000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero))
        {
            if (handle.IsInvalid) throw new IOException("Could not open the DualShock 4 for lightbar control. Another controller app may have exclusive access.");
            IntPtr data;
            if (!HidD_GetPreparsedData(handle, out data)) throw new IOException("Could not read the controller's output capabilities.");
            int length;
            try
            {
                HidCapabilities caps;
                if (HidP_GetCaps(data, out caps) != 0x00110000) throw new IOException("Could not read the controller's HID report size.");
                length = caps.OutputLength;
            }
            finally { HidD_FreePreparsedData(data); }

            bool useBluetooth = bluetooth ?? (length >= 78);
            if (length < (useBluetooth ? 78 : 32) || length > 1024)
                throw new IOException("The controller doesn't expose a compatible lightbar report.");
            byte[] report = BuildReport(useBluetooth, color, brightness, length);
            cancellation.ThrowIfCancellationRequested();
            using (var stream = new FileStream(handle, FileAccess.Write, 1, true))
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                timeout.CancelAfter(2500);
                try
                {
                    var write = stream.WriteAsync(report, 0, report.Length, timeout.Token);
                    using (timeout.Token.Register(() => CancelIoEx(handle, IntPtr.Zero)))
                        await write.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    cancellation.ThrowIfCancellationRequested();
                    throw new IOException("The controller did not respond to the lightbar command.");
                }
                catch (IOException error)
                {
                    cancellation.ThrowIfCancellationRequested();
                    string message = timeout.IsCancellationRequested ? "The controller did not respond to the lightbar command." :
                        "Windows couldn't send the lightbar command. Check that the controller is still connected.";
                    throw new IOException(message, error);
                }
            }
        }
    }

    static byte[] BuildReport(bool bluetooth, Color color, int brightness, int length)
    {
        var report = new byte[length];
        report[0] = bluetooth ? (byte)0x11 : (byte)0x05;
        int flags = bluetooth ? 3 : 1;
        report[flags] = 0x02;
        report[flags + 5] = (byte)((color.R * brightness + 50) / 100);
        report[flags + 6] = (byte)((color.G * brightness + 50) / 100);
        report[flags + 7] = (byte)((color.B * brightness + 50) / 100);
        if (bluetooth)
        {
            report[1] = 0xc4;
            // The 0x11 Bluetooth report payload is 78 bytes, even when HID pads the write.
            uint crc = UpdateCrc(0xffffffffu, 0xa2);
            for (int i = 0; i < 74; i++) crc = UpdateCrc(crc, report[i]);
            crc = ~crc;
            for (int i = 0; i < 4; i++) report[74 + i] = (byte)(crc >> (8 * i));
        }
        return report;
    }

    static uint UpdateCrc(uint crc, byte value)
    {
        crc ^= value;
        for (int i = 0; i < 8; i++)
            crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
        return crc;
    }

    internal static bool SelfTest()
    {
        Color color = Color.FromArgb(200, 100, 50);
        byte[] usb = BuildReport(false, color, 50, 32);
        byte[] bluetooth = BuildReport(true, color, 50, 334);
        byte[] off = BuildReport(false, color, 0, 32);
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            if (!Send(null, null, color, 50, cancellation.Token).IsCanceled) return false;
        }
        return usb[0] == 5 && usb[1] == 2 && usb[4] == 0 && usb[5] == 0 &&
            usb[6] == 100 && usb[7] == 50 && usb[8] == 25 &&
            bluetooth[0] == 0x11 && bluetooth[1] == 0xc4 && bluetooth[3] == 2 &&
            bluetooth[8] == 100 && bluetooth[9] == 50 && bluetooth[10] == 25 &&
            BitConverter.ToUInt32(bluetooth, 74) == 0x4b31c75au && bluetooth[78] == 0 &&
            off[6] == 0 && off[7] == 0 && off[8] == 0;
    }
}

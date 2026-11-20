using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

static class Ds4Input
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Device
    {
        internal IntPtr Handle;
        internal uint Type;
    }

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    struct HidCapabilities
    {
        [FieldOffset(4)] internal ushort InputLength;
        [FieldOffset(8)] internal ushort FeatureLength;
    }

    [DllImport("user32", SetLastError = true)]
    static extern uint GetRawInputDeviceList([Out] Device[] devices, ref uint count, uint size);

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32", SetLastError = true)]
    static extern bool CancelIoEx(SafeFileHandle handle, IntPtr overlapped);

    [DllImport("hid")]
    [return: MarshalAs(UnmanagedType.U1)]
    static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr data);

    [DllImport("hid")]
    [return: MarshalAs(UnmanagedType.U1)]
    static extern bool HidD_FreePreparsedData(IntPtr data);

    [DllImport("hid")] static extern int HidP_GetCaps(IntPtr data, out HidCapabilities capabilities);
    [DllImport("hid")]
    [return: MarshalAs(UnmanagedType.U1)]
    static extern bool HidD_GetFeature(SafeFileHandle device, byte[] report, int length);

    internal static Device[] ListAttached()
    {
        uint size = (uint)Marshal.SizeOf(typeof(Device));
        for (int attempt = 0; attempt < 2; ++attempt)
        {
            uint count = 0;
            if (GetRawInputDeviceList(null, ref count, size) == uint.MaxValue || count > 8192) return null;
            if (count == 0) return new Device[0];
            var devices = new Device[count];
            uint read = GetRawInputDeviceList(devices, ref count, size);
            if (read != uint.MaxValue)
            {
                if (read < devices.Length) Array.Resize(ref devices, (int)read);
                return devices;
            }
            if (Marshal.GetLastWin32Error() != 122) break;
        }
        return null;
    }

    internal static bool TryLocateReport(int id, int length, bool bluetoothPath, out int batteryOffset)
    {
        batteryOffset = -1;
        if (id == 0x11 && length >= 78)
        {
            batteryOffset = 32;
            return true;
        }
        if (id != 1 || length < 10) return false;
        if (length == 64 && !bluetoothPath) batteryOffset = 30;
        return true;
    }

    static HidCapabilities ReadCapabilities(SafeFileHandle device)
    {
        IntPtr data;
        if (!HidD_GetPreparsedData(device, out data)) return default(HidCapabilities);
        try
        {
            HidCapabilities capabilities;
            return HidP_GetCaps(data, out capabilities) == 0x00110000 ? capabilities : default(HidCapabilities);
        }
        finally { HidD_FreePreparsedData(data); }
    }

    internal static void RequestFullBluetoothReports(string path)
    {
        using (var device = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
        {
            if (device.IsInvalid) return;
            int length = ReadCapabilities(device).FeatureLength;
            if (length < 2 || length > 4096) return;
            var report = new byte[length];
            // Reading feature 0x02 puts an official DS4 into full Bluetooth input mode.
            report[0] = 2;
            HidD_GetFeature(device, report, length);
        }
    }

    internal static async Task<ArraySegment<byte>> ReadSharedReport(string path, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        const uint genericRead = 0x80000000, shareReadWrite = 3, openExisting = 3, overlapped = 0x40000000;
        using (var device = CreateFile(path, genericRead, shareReadWrite, IntPtr.Zero, openExisting, overlapped, IntPtr.Zero))
        {
            if (device.IsInvalid) return default(ArraySegment<byte>);
            int length = ReadCapabilities(device).InputLength;
            if (length < 10 || length > 4096) return default(ArraySegment<byte>);
            var bytes = new byte[length];
            using (var stream = new FileStream(device, FileAccess.Read, 1, true))
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                timeout.CancelAfter(900);
                var read = stream.ReadAsync(bytes, 0, length, timeout.Token);
                using (timeout.Token.Register(() => CancelIoEx(device, IntPtr.Zero)))
                    return new ArraySegment<byte>(bytes, 0, await read.ConfigureAwait(false));
            }
        }
    }
}

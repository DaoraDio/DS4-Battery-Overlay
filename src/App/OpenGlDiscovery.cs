using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

sealed class OpenGlDiscovery
{
    IntPtr checkedWindow;
    uint checkedProcess;
    long nextProbe;
    bool currentWindowConfirmed;
    readonly string library;
    public string LastDetected { get; private set; }

    public OpenGlDiscovery(string library = "opengl32.dll") { this.library = library; }

    [DllImport("user32")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    delegate bool WindowCallback(IntPtr window, IntPtr data);
    [DllImport("user32")] static extern bool EnumWindows(WindowCallback callback, IntPtr data);
    [DllImport("user32")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("kernel32")] static extern uint GetCurrentProcessId();
    static readonly uint CurrentProcessId = GetCurrentProcessId();

    [DllImport("kernel32", SetLastError = true)]
    static extern SafeFileHandle OpenProcess(uint access, bool inherit, uint process);
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool QueryFullProcessImageName(SafeFileHandle process, uint flags, StringBuilder path, ref uint length);

    internal static string ExecutablePath(uint process)
    {
        using (var handle = OpenProcess(0x1000, false, process))
        {
            if (handle.IsInvalid) return null;
            var path = new StringBuilder(32768);
            uint length = (uint)path.Capacity;
            return QueryFullProcessImageName(handle, 0, path, ref length) ? path.ToString() : null;
        }
    }

    public bool Poll(IntPtr window, long now)
    {
        if (window == IntPtr.Zero) return false;
        uint pid;
        GetWindowThreadProcessId(window, out pid);
        if (pid == 0 || pid == CurrentProcessId) return false;

        if (window != checkedWindow || pid != checkedProcess)
        {
            checkedWindow = window;
            checkedProcess = pid;
            currentWindowConfirmed = false;
            nextProbe = now;
        }
        if (currentWindowConfirmed || now < nextProbe) return false;
        nextProbe = now + 2000;

        string executable = FindExecutable(pid, library);
        if (executable == null) return false;
        currentWindowConfirmed = true;
        if (string.Equals(executable, LastDetected, StringComparison.OrdinalIgnoreCase)) return false;
        LastDetected = executable;
        return true;
    }

    internal static string FindExecutable(IntPtr window)
    {
        if (window == IntPtr.Zero) return null;
        uint pid;
        GetWindowThreadProcessId(window, out pid);
        return FindExecutable(pid, "opengl32.dll");
    }

    internal static List<string> FindRunningExecutables(string library, string folderPrefix)
    {
        var executables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (uint process in VisibleProcesses())
        {
            string executable = FindExecutable(process, library, folderPrefix);
            if (executable != null) executables.Add(executable);
        }
        return new List<string>(executables);
    }

    internal static HashSet<uint> VisibleProcesses()
    {
        var processes = new HashSet<uint>();
        EnumWindows(delegate(IntPtr window, IntPtr data) {
            if (IsWindowVisible(window))
            {
                uint process;
                GetWindowThreadProcessId(window, out process);
                if (process != 0 && process != CurrentProcessId) processes.Add(process);
            }
            return true;
        }, IntPtr.Zero);

        return processes;
    }

    static string FindExecutable(uint pid, string library, string folderPrefix = null)
    {
        if (pid == 0 || pid == CurrentProcessId) return null;
        try
        {
            using (var process = Process.GetProcessById((int)pid))
            {
                string path = ExecutablePath(pid);
                if (path == null) return null;
                if (folderPrefix != null && !path.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase))
                    return null;

                // Loading the library is a clue. Apps can load it even when they render through another API.
                bool loaded = false;
                foreach (ProcessModule module in process.Modules)
                    if (string.Equals(module.ModuleName, library, StringComparison.OrdinalIgnoreCase))
                    {
                        loaded = true;
                        break;
                    }
                if (!loaded) return null;
                string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd(Path.DirectorySeparatorChar);
                if (windows.Length > 0 && path.StartsWith(windows + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return null;
                OpenGlIntegration.RequireX64Game(path);
                return path;
            }
        }
        catch (Win32Exception) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (ArgumentException) { return null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (System.Security.SecurityException) { return null; }
    }
}

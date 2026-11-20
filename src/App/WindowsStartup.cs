using System;
using System.IO;
using Microsoft.Win32;

static class WindowsStartup
{
    const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Entry = "DS4 Overlay RTSS Test";

    internal static bool IsEnabled()
    {
        using (var key = Registry.CurrentUser.OpenSubKey(RunPath))
            return key != null && key.GetValue(Entry) is string;
    }

    internal static void SetEnabled(bool enabled, string executable)
    {
        if (enabled)
        {
            string command = Command(executable);
            using (var key = Registry.CurrentUser.CreateSubKey(RunPath))
                key.SetValue(Entry, command, RegistryValueKind.String);
        }
        else
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunPath, true))
                if (key != null) key.DeleteValue(Entry, false);
        }
    }

    internal static string Command(string executable)
    {
        string command = "\"" + Path.GetFullPath(executable) + "\"";
        if (command.Length > 260) throw new IOException("Move the app to a shorter path before enabling Windows startup.");
        return command;
    }
}

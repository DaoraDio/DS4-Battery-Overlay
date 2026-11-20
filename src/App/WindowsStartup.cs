using System;
using System.IO;
using Microsoft.Win32;

static class WindowsStartup
{
    const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Entry = "DS4 Overlay RTSS Test";

    internal static bool ShouldOpenSettings(string[] arguments)
    {
        foreach (string argument in arguments)
            if (string.Equals(argument, "--startup", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    internal static void UpgradeRegistration(string executable)
    {
        using (var key = Registry.CurrentUser.OpenSubKey(RunPath, true))
            if (key != null) UpgradeRegistration(key, executable);
    }

    static void UpgradeRegistration(RegistryKey key, string executable)
    {
        string previous = key.GetValue(Entry) as string;
        string legacy = "\"" + Path.GetFullPath(executable) + "\"";
        // Only migrate an enabled entry for this exact copy of the application.
        if (string.Equals(previous, legacy, StringComparison.OrdinalIgnoreCase))
            key.SetValue(Entry, Command(executable), RegistryValueKind.String);
    }

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
        string command = "\"" + Path.GetFullPath(executable) + "\" --startup";
        if (command.Length > 260) throw new IOException("Move the app to a shorter path before enabling Windows startup.");
        return command;
    }

    internal static int SelfTest(string report)
    {
        string testPath = @"Software\DS4OverlayRTSSTest\StartupQA\" + Guid.NewGuid().ToString("N");
        try
        {
            string executable = Path.Combine(Path.GetTempPath(), "DS4 startup test", "DS4-Battery-Overlay.exe");
            string legacy = "\"" + Path.GetFullPath(executable) + "\"";
            string expected = legacy + " --startup";
            if (Command(executable) != expected || !ShouldOpenSettings(new string[0]) ||
                ShouldOpenSettings(new[] { "--startup" }) || ShouldOpenSettings(new[] { "--STARTUP" }))
                throw new InvalidOperationException("Startup must stay in the tray while a manual launch opens settings.");
            using (var key = Registry.CurrentUser.CreateSubKey(testPath))
            {
                UpgradeRegistration(key, executable);
                if (key.GetValue(Entry) != null) throw new InvalidOperationException("Startup was enabled without being requested.");
                key.SetValue(Entry, legacy);
                key.SetValue("Unrelated", "keep");
                UpgradeRegistration(key, executable);
                if ((string)key.GetValue(Entry) != expected || (string)key.GetValue("Unrelated") != "keep")
                    throw new InvalidOperationException("An existing startup entry was not migrated safely.");
                UpgradeRegistration(key, executable);
                if ((string)key.GetValue(Entry) != expected) throw new InvalidOperationException("Startup migration was not idempotent.");
                string otherCopy = "\"" + Path.Combine(Path.GetTempPath(), "Other copy", "DS4-Battery-Overlay.exe") + "\"";
                key.SetValue(Entry, otherCopy);
                UpgradeRegistration(key, executable);
                if ((string)key.GetValue(Entry) != otherCopy) throw new InvalidOperationException("A different app copy's startup entry was replaced.");
            }
            File.WriteAllText(report, "PASS quoted startup command with --startup, tray-only automatic launch, visible manual launch, safe migration of existing entries, disabled startup preserved, unrelated values preserved, idempotence and other app copies left intact.");
            return 0;
        }
        catch (Exception error) { File.WriteAllText(report, error.ToString()); return 1; }
        finally { Registry.CurrentUser.DeleteSubKeyTree(testPath, false); }
    }
}

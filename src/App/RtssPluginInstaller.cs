using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

// Installs only our RTSS client. All game hooking remains RTSS's responsibility.
static class RtssPluginInstaller
{
    internal const string Marker = "<P=100000,100000><L248>";
    const string PayloadHash = "1FECD749A4457AFFBD8AE1325254A39E0AF9B20B49FD12AFC1FC18224C2197DF";

    internal sealed class Result
    {
        internal bool Changed, AfterburnerConfigured;
        internal string BackupDirectory;
    }

    sealed class Change
    {
        internal string Path;
        internal byte[] Before, After;
    }

    // Preserve byte encoding, newlines, unrelated settings and existing shortcuts.
    sealed class Ini
    {
        internal string Text;
        readonly Encoding encoding;
        readonly byte[] preamble;
        internal Ini(byte[] bytes)
        {
            int skip = 0;
            if (bytes.Length >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191)
            { encoding = new UTF8Encoding(false, true); skip = 3; }
            else if (bytes.Length >= 2 && bytes[0] == 255 && bytes[1] == 254)
            { encoding = new UnicodeEncoding(false, false, true); skip = 2; }
            else if (bytes.Length >= 2 && bytes[0] == 254 && bytes[1] == 255)
            { encoding = new UnicodeEncoding(true, false, true); skip = 2; }
            else encoding = Encoding.GetEncoding(28591);
            preamble = bytes.Take(skip).ToArray();
            Text = encoding.GetString(bytes, skip, bytes.Length - skip);
        }
        Regex Section(string name)
        { return new Regex(@"(?ms)(^\[" + Regex.Escape(name) + @"\][ \t]*(?:\r?\n|\z))(.*?)(?=^\[|\z)"); }
        Regex Key(string name)
        { return new Regex(@"(?m)^([ \t]*" + Regex.Escape(name) + @"[ \t]*=[ \t]*)([^\r\n]*)(\r?)$"); }
        internal string Get(string section, string key)
        {
            var blocks = Section(section).Matches(Text);
            if (blocks.Count > 1) throw new InvalidDataException("Duplicate settings section: " + section);
            if (blocks.Count == 0) return null;
            var values = Key(key).Matches(blocks[0].Groups[2].Value);
            if (values.Count > 1) throw new InvalidDataException("Duplicate setting: " + section + "/" + key);
            if (values.Count == 0) return null;
            string value = values[0].Groups[2].Value.Trim();
            return value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"'
                ? value.Substring(1, value.Length - 2) : value;
        }
        internal void Set(string section, string key, string value)
        {
            Get(section, key); // Validate duplicates before modifying anything.
            string newline = Text.Contains("\r\n") ? "\r\n" : "\n";
            var pattern = Section(section);
            if (!pattern.IsMatch(Text))
            {
                Text = Text.TrimEnd('\r', '\n') + newline + "[" + section + "]" + newline + key + "=" + value + newline;
                return;
            }
            Text = pattern.Replace(Text, block => {
                string header = block.Groups[1].Value, body = block.Groups[2].Value;
                if (!header.EndsWith("\n")) header += newline;
                var keys = Key(key);
                if (keys.IsMatch(body)) body = keys.Replace(body, line => {
                    string previous = line.Groups[2].Value.Trim();
                    bool quoted = previous.Length >= 2 && previous[0] == '"' && previous[previous.Length - 1] == '"';
                    return line.Groups[1].Value + (quoted ? "\"" + value + "\"" : value) + line.Groups[3].Value;
                });
                else
                {
                    if (body.Length > 0 && !body.EndsWith("\n")) body += newline;
                    body += key + "=" + value + newline;
                }
                return header + body;
            });
        }
        internal byte[] Bytes() { return preamble.Concat(encoding.GetBytes(Text)).ToArray(); }
    }

    internal static void RequireClosed()
    {
        foreach (string name in new[] { "RTSS", "MSIAfterburner" })
        {
            var processes = Process.GetProcessesByName(name);
            bool running = processes.Length > 0;
            foreach (var process in processes) process.Dispose();
            if (running) throw new InvalidOperationException(
                "Quit RTSS and MSI Afterburner completely, including their tray icons, then try again.");
        }
    }

    internal static async Task<int> Launch()
    {
        RequireClosed();
        // Elevation is limited to this installer mode, before any controller reader starts.
        using (var process = Process.Start(new ProcessStartInfo {
            FileName = Application.ExecutablePath, Arguments = "--install-rtss-plugin",
            UseShellExecute = true, Verb = "runas"
        }))
        {
            if (process == null) throw new IOException("The plugin installer could not start.");
            await Task.Run(() => process.WaitForExit());
            return process.ExitCode;
        }
    }

    static byte[] Payload()
    {
        using (var resource = typeof(RtssPluginInstaller).Assembly.GetManifestResourceStream("DS4PCStatsPlugin"))
        using (var buffer = new MemoryStream())
        {
            if (resource == null) throw new IOException("This build does not contain the RTSS plugin.");
            resource.CopyTo(buffer);
            byte[] bytes = buffer.ToArray();
            using (var hash = SHA256.Create())
                if (BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "") != PayloadHash)
                    throw new InvalidDataException("The embedded RTSS plugin did not pass verification.");
            return bytes;
        }
    }

    static string AfterburnerDirectory()
    {
        foreach (var view in new[] { Microsoft.Win32.RegistryView.Registry32, Microsoft.Win32.RegistryView.Registry64 })
        using (var root = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, view))
        using (var key = root.OpenSubKey(@"SOFTWARE\MSI\Afterburner"))
        {
            string directory = key == null ? null : key.GetValue("InstallDir") as string;
            if (!string.IsNullOrEmpty(directory) && File.Exists(System.IO.Path.Combine(directory, "MSIAfterburner.exe"))) return directory;
        }
        return null;
    }

    internal static int Run()
    {
        try
        {
            RequireClosed();
            string rtss = RtssOutput.FindRtssDirectory();
            if (rtss == null) throw new InvalidOperationException("Install RTSS first, then try again.");
            var result = Install(rtss, AfterburnerDirectory(), Payload(),
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DS4OverlayRTSSTest", "RTSSPluginBackups"), RequireClosed);
            string message = (result.Changed ? "Plugin installed." : "The plugin is already up to date.") +
                "\n\nStart RTSS, open Setup > Plugins, enable DS4PCStats.dll and click OK. Select the plugin and open Setup to set its shortcuts.";
            if (!result.AfterburnerConfigured) message += "\n\nMSI Afterburner was not found. Run this installer again after installing it to prepare independent PC-stats shortcuts.";
            if (result.BackupDirectory != null) message += "\n\nPrevious files were backed up to:\n" + result.BackupDirectory;
            MessageBox.Show(message, "RTSS plugin", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "RTSS plugin installation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }
    }

    internal static Result Install(string rtssDirectory, string afterburnerDirectory, byte[] payload,
        string backupRoot, Action requireClosed)
    {
        requireClosed();
        rtssDirectory = System.IO.Path.GetFullPath(rtssDirectory);
        if (!File.Exists(System.IO.Path.Combine(rtssDirectory, "RTSS.exe")))
            throw new IOException("The RTSS installation folder could not be verified.");
        var changes = new List<Change>();
        string client = System.IO.Path.Combine(rtssDirectory, "Plugins", "Client");
        Add(changes, System.IO.Path.Combine(client, "DS4PCStats.dll"), payload);
        string config = System.IO.Path.Combine(client, "DS4PCStats.cfg");
        var settings = new Ini(File.Exists(config) ? File.ReadAllBytes(config) : new byte[0]);
        foreach (string key in new[] { "ToggleHotkey", "ShowHotkey", "HideHotkey" })
            if (settings.Get("Settings", key) == null) settings.Set("Settings", key, "00000000");
        settings.Set("Settings", "MirrorAfterburner", "1");
        Add(changes, config, settings.Bytes());

        if (afterburnerDirectory != null)
        {
            afterburnerDirectory = System.IO.Path.GetFullPath(afterburnerDirectory);
            string profile = System.IO.Path.Combine(afterburnerDirectory, "Profiles", "MSIAfterburner.cfg");
            string system = System.IO.Path.Combine(afterburnerDirectory, "MSIAfterburner.cfg");
            if (!File.Exists(System.IO.Path.Combine(afterburnerDirectory, "MSIAfterburner.exe")) ||
                !File.Exists(profile) || !File.Exists(system))
                throw new IOException("Open MSI Afterburner once to create its settings, then quit it and try again.");
            var user = new Ini(File.ReadAllBytes(profile));
            var defaults = new Ini(File.ReadAllBytes(system));
            if ((user.Get("Settings", "EnableServer") ?? defaults.Get("Settings", "EnableServer")) == "0")
                throw new InvalidOperationException("Afterburner's OSD support is disabled. Restore it before installing the plugin.");
            var layouts = new SortedSet<int>(new[] { 0, 1, 2, 3 });
            foreach (Match match in Regex.Matches(user.Text + "\n" + defaults.Text, @"(?m)^\[OSDLayout(\d+)\]"))
            {
                int index;
                if (!int.TryParse(match.Groups[1].Value, out index) || index > 64)
                    throw new InvalidDataException("Afterburner has an unsupported layout index.");
                layouts.Add(index);
            }
            foreach (int layout in layouts)
            {
                string section = "OSDLayout" + layout;
                string header = user.Get(section, "FormatHeader") ?? defaults.Get(section, "FormatHeader") ?? "";
                user.Set(section, "FormatHeader", Marker + header.Replace(Marker, ""));
            }
            // Only the plugin should receive these actions; master OSD visibility is shared.
            foreach (string key in new[] { "OSDToggleHotkey", "OSDOnHotkey", "OSDOffHotkey" })
                user.Set("Settings", key, "00000000h");
            Add(changes, profile, user.Bytes());
        }

        var result = new Result { AfterburnerConfigured = afterburnerDirectory != null, Changed = changes.Count > 0 };
        if (changes.Count == 0) return result;
        string backup = System.IO.Path.Combine(System.IO.Path.GetFullPath(backupRoot),
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backup);
        var manifest = new StringBuilder("Previous files for this RTSS plugin installation\r\n\r\n");
        for (int i = 0; i < changes.Count; i++)
        {
            var change = changes[i];
            string name = i + "-" + System.IO.Path.GetFileName(change.Path);
            if (change.Before != null) File.WriteAllBytes(System.IO.Path.Combine(backup, name), change.Before);
            manifest.AppendLine(change.Path + " <- " + (change.Before == null ? "did not previously exist" : name));
        }
        File.WriteAllText(System.IO.Path.Combine(backup, "Files.txt"), manifest.ToString());
        result.BackupDirectory = backup;
        int applied = 0;
        try
        {
            requireClosed();
            foreach (var change in changes)
            {
                requireClosed();
                Write(change.Path, change.Before, change.After);
                applied++;
            }
        }
        catch (Exception error)
        {
            var failures = new List<string>();
            for (int i = applied - 1; i >= 0; i--)
                try { Write(changes[i].Path, changes[i].After, changes[i].Before); }
                catch { failures.Add(changes[i].Path); }
            throw new IOException(error.Message + (failures.Count == 0
                ? "\n\nInstallation was cancelled; previous files were restored."
                : "\n\nSome files could not be restored:\n" + string.Join("\n", failures)) +
                "\n\nBackup: " + backup, error);
        }
        return result;
    }

    static bool Same(byte[] left, byte[] right)
    { return left == null ? right == null : right != null && left.SequenceEqual(right); }

    static void Add(List<Change> changes, string path, byte[] after)
    {
        byte[] before = File.Exists(path) ? File.ReadAllBytes(path) : null;
        if (!Same(before, after)) changes.Add(new Change { Path = path, Before = before, After = after });
    }

    static void Write(string path, byte[] expected, byte[] value)
    {
        byte[] current = File.Exists(path) ? File.ReadAllBytes(path) : null;
        if (!Same(expected, current)) throw new IOException("Settings changed during installation: " + path);
        if (value == null) { File.Delete(path); return; }
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
        string temporary = path + ".ds4-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, value);
            if (expected == null) File.Move(temporary, path);
            else File.Replace(temporary, path, null);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

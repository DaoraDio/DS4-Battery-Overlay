using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Threading.Tasks;
using Microsoft.Win32;

sealed class Direct3D11AutoEnable
{
    internal const string GameList = "Direct3D11Games";
    const string FolderPreference = "Direct3D11GameFolder";
    const string ExclusionsPreference = "Direct3D11AutoExcludedFolders";
    readonly HashSet<string> exclusions;
    readonly HashSet<string> attempted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    Task<List<string>> scan;
    long nextScan;

    public string Folder { get; private set; }
    public event Action<bool, string> GamesChanged;

    public Direct3D11AutoEnable()
    {
        using (var key = Registry.CurrentUser.OpenSubKey(OverlayAppearance.PreferencesPath))
        {
            string savedFolder = key == null ? null : key.GetValue(FolderPreference) as string;
            Folder = savedFolder == null ? FindSteamGameFolder() : savedFolder.Length == 0 ? null : savedFolder;
            var saved = key == null ? null : key.GetValue(ExclusionsPreference) as string[];
            exclusions = new HashSet<string>(saved ?? new string[0], StringComparer.OrdinalIgnoreCase);
        }
    }

    static string FindSteamGameFolder()
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string[] roots = {
            ReadSteamInstallPath(RegistryHive.CurrentUser, RegistryView.Default, "SteamPath"),
            ReadSteamInstallPath(RegistryHive.LocalMachine, RegistryView.Registry32, "InstallPath"),
            programFiles.Length == 0 ? null : Path.Combine(programFiles, "Steam")
        };
        foreach (string root in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Path.IsPathRooted(root)) continue;
            try
            {
                string folder = Path.GetFullPath(Path.Combine(root, "steamapps", "common"));
                if (Directory.Exists(folder)) return folder;
            }
            catch (ArgumentException) { }
            catch (NotSupportedException) { }
            catch (PathTooLongException) { }
        }
        return null;
    }

    static string ReadSteamInstallPath(RegistryHive hive, RegistryView view, string value)
    {
        try
        {
            using (var root = RegistryKey.OpenBaseKey(hive, view))
            using (var key = root.OpenSubKey(@"Software\Valve\Steam"))
                return key == null ? null : key.GetValue(value) as string;
        }
        catch (SecurityException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    internal static string FolderPrefix(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }

    public void SetFolder(string path)
    {
        string folder = null;
        if (path != null)
        {
            folder = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string root = Path.GetPathRoot(Path.GetFullPath(path));
            string windows = FolderPrefix(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            string[] broadFolders = {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
            };
            if (root.Length != 3 || FolderPrefix(folder).Equals(root, StringComparison.OrdinalIgnoreCase) ||
                FolderPrefix(folder).StartsWith(windows, StringComparison.OrdinalIgnoreCase) ||
                Array.Exists(broadFolders, broad => string.Equals(folder, broad, StringComparison.OrdinalIgnoreCase)) ||
                !Directory.Exists(folder))
                throw new IOException("Choose a local folder that contains your games.");
        }

        using (var key = Registry.CurrentUser.CreateSubKey(OverlayAppearance.PreferencesPath))
        {
            key.SetValue(FolderPreference, folder ?? "");
        }
        Folder = folder;
        scan = null;
        attempted.Clear();
        nextScan = 0;
    }

    public void SetExcluded(string executable, bool excluded)
    {
        string folder = Path.GetDirectoryName(executable);
        if (excluded == exclusions.Contains(folder)) return;
        var updated = new HashSet<string>(exclusions, StringComparer.OrdinalIgnoreCase);
        if (excluded) updated.Add(folder);
        else updated.Remove(folder);
        var values = new string[updated.Count];
        updated.CopyTo(values);
        using (var key = Registry.CurrentUser.CreateSubKey(OverlayAppearance.PreferencesPath))
            key.SetValue(ExclusionsPreference, values, RegistryValueKind.MultiString);
        exclusions.Clear();
        exclusions.UnionWith(updated);
    }

    public void Tick(long now)
    {
        if (Folder == null) return;
        if (scan == null)
        {
            if (now < nextScan) return;
            string prefix = FolderPrefix(Folder);
            scan = Task.Run(() => OpenGlDiscovery.FindRunningExecutables("d3d11.dll", prefix));
            return;
        }
        if (!scan.IsCompleted) return;

        var completed = scan;
        scan = null;
        nextScan = now + 5000;
        try { Prepare(completed.GetAwaiter().GetResult()); }
        catch (Exception error) { Report(false, error.Message); }
    }

    internal void Prepare(IEnumerable<string> running)
    {
        if (Folder == null) return;
        var found = new HashSet<string>(running, StringComparer.OrdinalIgnoreCase);
        attempted.IntersectWith(found);
        var games = OpenGlGamesPage.Game.Load(GameList);
        int enabled = 0;
        string errorMessage = null;
        foreach (string executable in found)
        {
            string directory = Path.GetDirectoryName(executable);
            if (exclusions.Contains(directory) || !attempted.Add(executable)) continue;
            try
            {
                if (!IsInLibrary(executable, Folder)) continue;
                if (OpenGlGamesPage.Game.Enable(executable, OpenGlIntegration.Direct3D11, games, GameList))
                    ++enabled;
            }
            catch (Exception error) { errorMessage = Path.GetFileName(executable) + " could not be enabled. " + error.Message; }
        }
        if (enabled == 1) Report(true, "One game enabled. Restart it to see the overlay.");
        else if (enabled > 1) Report(true, enabled + " games enabled. Restart them to see the overlay.");
        else if (errorMessage != null) Report(false, errorMessage);
    }

    internal static bool IsInLibrary(string executable, string folder)
    {
        string prefix = FolderPrefix(folder);
        if (!Path.GetFullPath(executable).StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            (File.GetAttributes(executable) & FileAttributes.ReparsePoint) != 0) return false;
        // Keep directory junctions and symbolic links from expanding the selected scope.
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(executable)); directory != null; directory = directory.Parent)
        {
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) return false;
            if (string.Equals(FolderPrefix(directory.FullName), prefix, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    void Report(bool success, string message)
    {
        if (GamesChanged != null) GamesChanged(success, message);
    }
}

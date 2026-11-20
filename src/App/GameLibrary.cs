using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading.Tasks;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

sealed class GameLibrary
{
    const string AutomaticPreference = "AutomaticallyEnableGames";
    const string FoldersPreference = "GameLibraryFolders";
    const string DisabledPreference = "DisabledGameFolders";
    const string DllSelectionPreference = "GameOverlayDllSelection";
    const string ConfirmedRendererPreference = "GameConfirmedRenderers";
    const string MixedOpenGlMessage = "32-bit and 64-bit OpenGL executables in the same folder need different overlay DLLs.";
    const string MixedDirect3D9Message = "32-bit and 64-bit DirectX 9 executables in the same folder need different overlay DLLs.";
    static readonly GraphicsApi[] ReportedRenderers = {
        GraphicsApi.Direct3D11, GraphicsApi.Direct3D12, GraphicsApi.OpenGl, GraphicsApi.Vulkan, GraphicsApi.Direct3D9
    };
    static readonly GraphicsApi[] AutomaticDllOrder = { GraphicsApi.Direct3D12, GraphicsApi.Direct3D11, GraphicsApi.Direct3D9, GraphicsApi.OpenGl };
    readonly HashSet<string> folders, disabled, previousDirectXExclusions;
    Dictionary<string, GraphicsApi> dllSelections;
    Dictionary<string, GraphicsApi> confirmedRenderers;
    readonly Dictionary<GraphicsApi, List<Registration>> registered = new Dictionary<GraphicsApi, List<Registration>>();
    readonly Func<string[]> steamRoots;
    List<InstalledGame> installed = new List<InstalledGame>();
    List<RunningGame> running = new List<RunningGame>();
    Task<List<InstalledGame>> discovery;
    Task<List<RunningGame>> detection;
    long nextDiscovery, nextDetection;
    bool loaded;

    internal sealed class Entry
    {
        internal InstalledGame Game;
        internal string Apis, Status, Detail, IconExecutable;
        internal GraphicsApi? DllSelection;
        internal GraphicsApi AutomaticDlls;
        internal bool Disabled, CanEnable, CanDisable, RendererConfirmed, RendererRemembered;
        public string Name { get { return Game.Name; } }
    }

    sealed class Registration
    {
        internal readonly string Executable, Digest;
        internal Registration(string executable, string digest) { Executable = executable; Digest = digest; }
    }

    sealed class RunningGame
    {
        internal string Executable;
        internal GraphicsApi Apis, LocalDlls, Presented;
        internal bool Is32Bit;
        internal bool ManagedStartup;
    }

    internal bool Automatic { get; private set; }
    internal List<Entry> Entries { get; private set; }
    internal string[] CustomFolders { get { return folders.OrderBy(folder => folder, GameDiscovery.Paths).ToArray(); } }
    internal event Action Changed;
    internal string ScanStatus { get; private set; }

    internal GameLibrary(Func<string[]> steamRoots = null)
    {
        this.steamRoots = steamRoots ?? GameDiscovery.SteamRoots;
        Entries = new List<Entry>();
        ScanStatus = "Finding installed games…";
        using (var key = Registry.CurrentUser.OpenSubKey(OverlayAppearance.PreferencesPath))
        {
            var prior = key == null ? null : key.GetValue("Direct3D11GameFolder") as string;
            Automatic = OverlayAppearance.Read(key, AutomaticPreference, prior == "" ? 0 : 1, 0, 1) != 0;
            folders = LoadSet(key, FoldersPreference);
            if ((key == null || key.GetValue(FoldersPreference) == null) && !string.IsNullOrEmpty(prior) &&
                !prior.TrimEnd('\\', '/').EndsWith(@"\steamapps\common", StringComparison.OrdinalIgnoreCase))
                folders.Add(prior);
            disabled = LoadSet(key, DisabledPreference);
            previousDirectXExclusions = LoadSet(key, "Direct3D11AutoExcludedFolders");
            dllSelections = LoadApiSettings(key == null ? null : key.GetValue(DllSelectionPreference) as string[], GameDiscovery.LocalApiMask);
            confirmedRenderers = LoadApiSettings(key == null ? null : key.GetValue(ConfirmedRendererPreference) as string[],
                GameDiscovery.LocalApiMask | GraphicsApi.Vulkan);
            foreach (GraphicsApi api in GameDiscovery.LocalApis)
            {
                var items = new List<Registration>();
                var values = key == null ? null : key.GetValue(RegistryList(api)) as string[];
                foreach (string item in values ?? new string[0])
                {
                    int separator = item.IndexOf('\t');
                    if (separator > 0 && separator + 1 < item.Length)
                        items.Add(new Registration(item.Substring(0, separator), item.Substring(separator + 1)));
                }
                registered.Add(api, items);
            }
        }
    }

    static string RegistryList(GraphicsApi api)
    {
        switch (api)
        {
            case GraphicsApi.Direct3D11: return "Direct3D11Games";
            case GraphicsApi.Direct3D12: return "Direct3D12Games";
            case GraphicsApi.OpenGl: return "OpenGLGames";
            case GraphicsApi.Direct3D9: return "Direct3D9Games";
            default: throw new ArgumentOutOfRangeException("api");
        }
    }

    static HashSet<string> LoadSet(RegistryKey key, string name)
    {
        var values = key == null ? null : key.GetValue(name) as string[];
        return new HashSet<string>(values ?? new string[0], GameDiscovery.Paths);
    }

    static void WriteSetting(string name, object value)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(OverlayAppearance.PreferencesPath))
            key.SetValue(name, value);
    }

    static Dictionary<string, GraphicsApi> LoadApiSettings(string[] records, GraphicsApi validApis)
    {
        var selections = new Dictionary<string, GraphicsApi>(GameDiscovery.Paths);
        foreach (string record in records ?? new string[0])
        {
            int separator = record.LastIndexOf('\t');
            int value;
            if (separator > 0 && int.TryParse(record.Substring(separator + 1), NumberStyles.None,
                CultureInfo.InvariantCulture, out value) && (value & ~(int)validApis) == 0)
                selections[record.Substring(0, separator)] = (GraphicsApi)value;
        }
        return selections;
    }

    static void SaveApiSettings(string preference, Dictionary<string, GraphicsApi> values)
    {
        WriteSetting(preference, values.Select(item => item.Key + "\t" +
            ((int)item.Value).ToString(CultureInfo.InvariantCulture)).ToArray());
    }

    internal void SetDllSelection(Entry entry, GraphicsApi? selection)
    {
        var updated = new Dictionary<string, GraphicsApi>(dllSelections, GameDiscovery.Paths);
        if (selection.HasValue) updated[entry.Game.Folder] = selection.Value & GameDiscovery.LocalApiMask;
        else updated.Remove(entry.Game.Folder);
        SaveApiSettings(DllSelectionPreference, updated);
        dllSelections = updated;
        Reconcile();
    }

    internal void ForgetRenderer(Entry entry)
    {
        var updated = new Dictionary<string, GraphicsApi>(confirmedRenderers, GameDiscovery.Paths);
        if (!updated.Remove(entry.Game.Folder)) return;
        SaveApiSettings(ConfirmedRendererPreference, updated);
        confirmedRenderers = updated;
        Reconcile();
    }

    internal void SetAutomatic(bool enabled)
    {
        WriteSetting(AutomaticPreference, enabled ? 1 : 0);
        Automatic = enabled;
        Reconcile();
    }

    internal void SetFolder(string path, bool add)
    {
        string folder = add ? GameDiscovery.ValidateLibrary(path) : path;
        var updated = new HashSet<string>(folders, GameDiscovery.Paths);
        if (!(add ? updated.Add(folder) : updated.Remove(folder))) return;
        WriteSetting(FoldersPreference, updated.ToArray());
        folders.Clear();
        folders.UnionWith(updated);
        Refresh();
    }

    internal void Refresh()
    {
        nextDiscovery = nextDetection = 0;
        discovery = null;
        detection = null;
        running.Clear();
        loaded = false;
        Entries = new List<Entry>();
        ScanStatus = "Finding installed games…";
        Notify();
    }

    internal void SetGameEnabled(Entry entry, bool enabled)
    {
        string folder = entry.Game.Folder;
        var updated = new HashSet<string>(disabled, GameDiscovery.Paths);
        if (enabled) updated.Remove(folder);
        else updated.Add(folder);
        WriteSetting(DisabledPreference, updated.ToArray());
        disabled.Clear();
        disabled.UnionWith(updated);
        if (enabled && previousDirectXExclusions.RemoveWhere(path =>
            GameDiscovery.Paths.Equals(path, folder) || GameDiscovery.ContainsPath(folder, path)) > 0)
            WriteSetting("Direct3D11AutoExcludedFolders", previousDirectXExclusions.ToArray());
        Reconcile(enabled ? folder : null);
    }

    internal void Tick(long now)
    {
        if (discovery == null && now >= nextDiscovery)
        {
            string[] custom = CustomFolders;
            discovery = Task.Run(() => {
                var games = GameDiscovery.FindGames(custom, steamRoots());
                foreach (InstalledGame game in games) GameDiscovery.ReadCandidates(game);
                return games;
            });
        }
        if (discovery != null && discovery.IsCompleted)
        {
            var completed = discovery;
            discovery = null;
            nextDiscovery = now + 120000;
            try
            {
                installed = completed.GetAwaiter().GetResult();
                loaded = true;
                ScanStatus = "";
                Reconcile();
            }
            catch (Exception error)
            {
                ScanStatus = "Couldn’t scan game folders. " + error.Message;
                Notify();
            }
        }

        if (loaded && detection == null && now >= nextDetection)
        {
            string[] roots = installed.Select(game => game.Folder).ToArray();
            detection = Task.Run(() => FindRunning(roots));
        }
        if (detection != null && detection.IsCompleted)
        {
            var completed = detection;
            detection = null;
            nextDetection = now + 5000;
            try
            {
                running = completed.GetAwaiter().GetResult();
                Reconcile();
            }
            catch (Exception error)
            {
                ScanStatus = "Couldn’t check running games. " + error.Message;
                Notify();
            }
        }
    }

    void Notify() { if (Changed != null) Changed(); }

    static InstalledGame FindGame(IEnumerable<InstalledGame> games, string executable)
    {
        return games.Where(game => GameDiscovery.ContainsPath(game.Folder, executable))
            .OrderByDescending(game => game.Folder.Length).FirstOrDefault();
    }

    void Reconcile(string enableFolder = null)
    {
        if (!loaded) return;
        var games = new List<InstalledGame>(installed);
        foreach (var items in registered.Values)
            foreach (Registration item in items)
                if (FindGame(games, item.Executable) == null)
                {
                    string folder = Path.GetDirectoryName(item.Executable);
                    if (!string.IsNullOrEmpty(folder))
                        games.Add(new InstalledGame(Path.GetFileNameWithoutExtension(item.Executable), folder) {
                            IconExecutable = item.Executable
                        });
                }
        var result = new List<Entry>();
        Dictionary<string, GraphicsApi> updatedRenderers = null;
        foreach (InstalledGame game in games.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var active = running.Where(item => FindGame(games, item.Executable) == game).ToArray();
            var row = new Entry { Game = game, Disabled = disabled.Contains(game.Folder),
                IconExecutable = active.Length == 1 ? active[0].Executable : game.IconExecutable };
            GraphicsApi selection;
            if (dllSelections.TryGetValue(game.Folder, out selection)) row.DllSelection = selection;
            GraphicsApi apis = game.DetectedApis;
            GraphicsApi apis32Bit = game.Detected32BitApis;
            GraphicsApi presented = GraphicsApi.None;
            foreach (RunningGame process in active)
            {
                if (process.ManagedStartup) game.ManagedDirect3D9.Add(process.Executable);
                presented |= process.Presented;
                if (process.Is32Bit) apis32Bit |= process.Apis;
                else apis |= process.Apis;
            }
            GraphicsApi remembered;
            confirmedRenderers.TryGetValue(game.Folder, out remembered);
            if ((presented & ~remembered) != 0)
            {
                remembered |= presented;
                if (updatedRenderers == null)
                    updatedRenderers = new Dictionary<string, GraphicsApi>(confirmedRenderers, GameDiscovery.Paths);
                updatedRenderers[game.Folder] = remembered;
            }
            var pathsByApi = new Dictionary<GraphicsApi, HashSet<string>>();
            foreach (GraphicsApi api in GameDiscovery.LocalApis)
            {
                var paths = new HashSet<string>(registered[api].Where(item => FindGame(games, item.Executable) == game)
                    .Select(item => item.Executable), GameDiscovery.Paths);
                if (!row.Disabled)
                {
                    string candidate;
                    if (game.Candidates.TryGetValue(api, out candidate)) paths.Add(candidate);
                    if (api == GraphicsApi.OpenGl && game.OpenGl32Candidate != null) paths.Add(game.OpenGl32Candidate);
                    if (api == GraphicsApi.Direct3D9 && game.Direct3D9Candidate32 != null) paths.Add(game.Direct3D9Candidate32);
                    foreach (RunningGame process in active)
                        if ((process.Apis & api) != 0 && GameDiscovery.Component(api, process.Is32Bit) != null)
                            paths.Add(process.Executable);
                }
                pathsByApi.Add(api, paths);
            }
            bool prepareGame = Automatic || GameDiscovery.Paths.Equals(enableFolder, game.Folder);
            GraphicsApi automaticDlls = remembered != GraphicsApi.None ? remembered & GameDiscovery.LocalApiMask :
                AutomaticDllOrder.FirstOrDefault(api => pathsByApi[api].Any(path => File.Exists(path) &&
                    (api != GraphicsApi.Direct3D11 || !previousDirectXExclusions.Contains(Path.GetDirectoryName(path)))));
            row.AutomaticDlls = automaticDlls;
            GraphicsApi allowed = row.DllSelection ?? automaticDlls;
            var problems = new List<string>();
            if (!row.Disabled && (allowed & GraphicsApi.OpenGl) != 0 && OpenGlIntegration.OpenGl32 != null &&
                game.OpenGlMachines.Values.Contains(GameMachine.Unknown))
                problems.Add(MixedOpenGlMessage);
            if (!row.Disabled && (allowed & GraphicsApi.Direct3D9) != 0 &&
                game.Direct3D9Machines.Values.Contains(GameMachine.Unknown)) problems.Add(MixedDirect3D9Message);
            bool removalPending = false;
            int ready = 0, restart = 0, candidates = 0;
            foreach (GraphicsApi api in GameDiscovery.LocalApis)
            {
                bool apiAllowed = (allowed & api) != 0;
                bool remove = row.Disabled || (!apiAllowed && (row.DllSelection.HasValue || prepareGame));
                if (!apiAllowed && !remove) continue;
                foreach (var group in pathsByApi[api].GroupBy(Path.GetDirectoryName, GameDiscovery.Paths))
                {
                    string path = group.FirstOrDefault(File.Exists) ?? group.First();
                    apis |= api;
                    bool excluded = api == GraphicsApi.Direct3D11 && previousDirectXExclusions.Contains(group.Key);
                    try
                    {
                        if (!remove && (api == GraphicsApi.Direct3D9 || api == GraphicsApi.OpenGl && OpenGlIntegration.OpenGl32 != null) &&
                            HasMixedMachines(game, api, group))
                        {
                            problems.Add(api == GraphicsApi.Direct3D9 ? MixedDirect3D9Message : MixedOpenGlMessage);
                            continue;
                        }
                        if (!remove && !excluded) ++candidates;
                        bool runningHere = active.Any(process => GameDiscovery.Paths.Equals(group.Key,
                            Path.GetDirectoryName(process.Executable)));
                        if (runningHere && !apiAllowed && !row.DllSelection.HasValue && !row.Disabled) continue;
                        if (ReconcileComponent(game, path, api, remove, !excluded && prepareGame, runningHere, !apiAllowed))
                        {
                            bool pendingRestart = active.Any(process => GameDiscovery.Paths.Equals(group.Key, Path.GetDirectoryName(process.Executable)) &&
                                GameDiscovery.Component(api, process.Is32Bit) != null && (process.Apis & api) != 0 && (process.LocalDlls & api) == 0);
                            if (pendingRestart) ++restart;
                            else ++ready;
                        }
                    }
                    catch (Exception error)
                    {
                        removalPending |= remove;
                        problems.Add(error.Message);
                    }
                }
            }

            row.RendererConfirmed = presented != GraphicsApi.None;
            row.RendererRemembered = !row.RendererConfirmed && remembered != GraphicsApi.None;
            row.Apis = ApiNames(row.RendererConfirmed ? presented : row.RendererRemembered ? remembered : apis | apis32Bit);
            row.CanEnable = row.Disabled || (candidates != 0 && ready + restart < candidates) ||
                (((row.DllSelection ?? GameDiscovery.LocalApiMask) & GraphicsApi.Direct3D11) != 0 && previousDirectXExclusions.Any(path =>
                    GameDiscovery.Paths.Equals(path, game.Folder) || GameDiscovery.ContainsPath(game.Folder, path)));
            row.CanDisable = !row.Disabled;
            row.Detail = string.Join(Environment.NewLine, problems.Distinct());
            if (problems.Count != 0)
                row.Status = removalPending ? "Removal pending" : problems.Any(problem => problem.StartsWith("Close the game to update")) ? "Update pending" :
                    ready + restart > 0 ? "Partly enabled" :
                    problems.Any(problem => problem.Contains("from another source") || problem.Contains("is a link")) ? "DLL conflict" : "Setup blocked";
            else if (row.Disabled) row.Status = "Disabled";
            else if (restart > 0) row.Status = "Restart game";
            else if (ready > 0) row.Status = "Ready";
            else if ((apis & GraphicsApi.Vulkan) != 0) row.Status = "Vulkan automatic";
            else if (row.DllSelection == GraphicsApi.None) row.Status = "No DLLs selected";
            else if (candidates != 0) row.Status = "Available";
            else if (apis == GraphicsApi.None && apis32Bit != GraphicsApi.None &&
                (apis32Bit & GraphicsApi.Direct3D9) == 0 &&
                ((apis32Bit & GraphicsApi.OpenGl) == 0 || OpenGlIntegration.OpenGl32 == null))
            {
                row.Status = "32-bit unsupported";
                row.Detail = OpenGlIntegration.OpenGl32 == null ? "In-game overlay rendering requires a 64-bit game in this build." :
                    "The in-game overlay supports DirectX 9 and OpenGL for 32-bit games.";
            }
            else row.Status = "Launch to detect";
            if (row.DllSelection.HasValue && row.Detail.Length == 0)
                row.Detail = allowed == GraphicsApi.None ? "No DirectX or OpenGL overlay DLLs selected." :
                    "Selected DLLs: " + string.Join(", ", GameDiscovery.LocalApis.Where(api => (allowed & api) != 0)
                        .Select(api => GameDiscovery.Component(api).FileName)) + ".";
            else if (Automatic && !row.Disabled && automaticDlls != GraphicsApi.None && row.Detail.Length == 0)
                row.Detail = "Automatic DLLs: " + string.Join(", ", GameDiscovery.LocalApis.Where(api => (automaticDlls & api) != 0)
                    .Select(api => GameDiscovery.Component(api).FileName)) + ".";
            result.Add(row);
        }
        if (updatedRenderers != null)
        {
            SaveApiSettings(ConfirmedRendererPreference, updatedRenderers);
            confirmedRenderers = updatedRenderers;
        }
        Entries = result;
        Notify();
    }

    static bool HasMixedMachines(InstalledGame game, GraphicsApi api, IGrouping<string, string> paths)
    {
        var machines = api == GraphicsApi.Direct3D9 ? game.Direct3D9Machines : game.OpenGlMachines;
        GameMachine machine;
        if (machines.TryGetValue(paths.Key, out machine) && machine == GameMachine.Unknown) return true;
        foreach (string path in paths.Where(File.Exists))
        {
            var observed = OpenGlIntegration.ReadMachine(path);
            if (machine != GameMachine.Unknown && machine != observed) return true;
            machine = observed;
        }
        return false;
    }

    bool ReconcileComponent(InstalledGame game, string executable, GraphicsApi api,
        bool remove, bool prepare, bool running, bool removeUnregistered)
    {
        var list = registered[api];
        string folder = Path.GetDirectoryName(executable);
        Registration prior = list.Find(item => GameDiscovery.Paths.Equals(Path.GetDirectoryName(item.Executable), folder));
        if (remove && prior == null && !removeUnregistered) return false;
        bool exists = File.Exists(executable);
        if (!exists && (!remove || prior == null) || exists && !GameDiscovery.IsSafePath(game.Folder, executable)) return false;
        bool is32Bit = exists && OpenGlIntegration.ReadMachine(executable) == GameMachine.X86;
        GameOverlayComponent component = GameDiscovery.Component(api, is32Bit);
        if (remove)
        {
            if (component == null) component = GameDiscovery.Component(api);
            string dll = Path.Combine(folder, component.FileName);
            if (File.Exists(dll) && !GameDiscovery.IsSafePath(game.Folder, dll))
            {
                if (prior == null) return false;
                throw new IOException("Could not safely remove " + component.FileName + " from this game folder.");
            }
            component.Remove(executable, prior == null ? null : prior.Digest, ignoreForeign: prior == null);
            if (prior != null) SaveRegistration(api, prior, null);
            return false;
        }
        if (component == null) return false;
        bool managedStartup = api == GraphicsApi.Direct3D9 && game.ManagedDirect3D9.Contains(executable);
        bool current;
        bool installedHere = component.IsInstalled(executable, prior == null ? null : prior.Digest, out current, managedStartup);
        if (!installedHere && !prepare) return false;
        if (installedHere && !current && running)
            throw new IOException("Close the game to update the " + component.Name + " overlay.");
        bool added = !current && component.Install(executable, prior == null ? null : prior.Digest, managedStartup);
        if (prior == null || prior.Digest != component.Digest)
        {
            try { SaveRegistration(api, prior, new Registration(executable, component.Digest)); }
            catch
            {
                if (added && !installedHere) component.Remove(executable, component.Digest);
                throw;
            }
        }
        return installedHere || added;
    }

    void SaveRegistration(GraphicsApi api, Registration oldEntry, Registration newEntry)
    {
        var updated = new List<Registration>(registered[api]);
        if (oldEntry != null) updated.Remove(oldEntry);
        if (newEntry != null) updated.Add(newEntry);
        WriteSetting(RegistryList(api), updated.Select(item => item.Executable + "\t" + item.Digest).ToArray());
        registered[api] = updated;
    }

    static string ApiNames(GraphicsApi apis)
    {
        var names = new List<string>();
        if ((apis & GraphicsApi.Direct3D11) != 0) names.Add("DirectX 11");
        if ((apis & GraphicsApi.Direct3D12) != 0) names.Add("DirectX 12");
        if ((apis & GraphicsApi.Direct3D9) != 0) names.Add("DirectX 9");
        if ((apis & GraphicsApi.OpenGl) != 0) names.Add("OpenGL");
        if ((apis & GraphicsApi.Vulkan) != 0) names.Add("Vulkan");
        return names.Count == 0 ? "Unknown" : string.Join(" / ", names);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ModuleEntry
    {
        internal uint Size, ModuleId, ProcessId, GlobalUseCount, ProcessUseCount;
        internal IntPtr BaseAddress;
        internal uint BaseSize;
        internal IntPtr Module;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string Path;
    }

    [DllImport("kernel32", SetLastError = true)]
    static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32", CharSet = CharSet.Unicode)]
    static extern bool Module32First(SafeFileHandle snapshot, ref ModuleEntry entry);

    [DllImport("kernel32", CharSet = CharSet.Unicode)]
    static extern bool Module32Next(SafeFileHandle snapshot, ref ModuleEntry entry);

    [DllImport("kernel32")]
    static extern ulong GetTickCount64();

    static GraphicsApi ReadPresentedRenderers(uint processId)
    {
        GraphicsApi result = GraphicsApi.None;
        foreach (GraphicsApi api in ReportedRenderers)
        {
            string name = @"Local\DS4Overlay.Presented." + processId.ToString(CultureInfo.InvariantCulture) +
                "." + ((int)api).ToString(CultureInfo.InvariantCulture);
            try
            {
                using (var mapping = MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.Read))
                using (var view = mapping.CreateViewAccessor(0, sizeof(ulong), MemoryMappedFileAccess.Read))
                {
                    ulong seen = view.ReadUInt64(0);
                    ulong now = GetTickCount64();
                    if (seen != 0 && seen <= now && now - seen <= 3000) result |= api;
                }
            }
            catch (IOException) { }
            catch (ArgumentException) { }
            catch (UnauthorizedAccessException) { }
            catch (SecurityException) { }
        }
        return result;
    }

    static void AddModule(RunningGame game, string folder, string name, string path)
    {
        GraphicsApi api = GameDiscovery.IdentifyLibrary(name);
        game.Apis |= api;
        if (api != GraphicsApi.None && GameDiscovery.Paths.Equals(folder, Path.GetDirectoryName(path)))
            game.LocalDlls |= api;
    }

    static void Read32BitModules(uint processId, RunningGame game, string folder)
    {
        const uint snapshotModules = 0x08;
        const uint snapshot32BitModules = 0x10;
        const int errorBadLength = 24;
        for (int attempt = 0; attempt < 3; ++attempt)
        {
            using (var snapshot = CreateToolhelp32Snapshot(snapshotModules | snapshot32BitModules, processId))
            {
                if (snapshot.IsInvalid)
                {
                    if (Marshal.GetLastWin32Error() == errorBadLength) continue;
                    return;
                }
                var entry = new ModuleEntry { Size = (uint)Marshal.SizeOf(typeof(ModuleEntry)) };
                if (!Module32First(snapshot, ref entry)) return;
                do { AddModule(game, folder, entry.Name, entry.Path); }
                while (Module32Next(snapshot, ref entry));
                return;
            }
        }
    }

    static List<RunningGame> FindRunning(string[] roots)
    {
        var found = new List<RunningGame>();
        if (roots.Length == 0) return found;
        foreach (uint id in OpenGlDiscovery.VisibleProcesses())
        {
            try
            {
                using (var process = Process.GetProcessById((int)id))
                {
                    string executable = OpenGlDiscovery.ExecutablePath(id);
                    if (executable == null) continue;
                    if (!roots.Any(root => GameDiscovery.ContainsPath(root, executable))) continue;
                    var machine = OpenGlIntegration.ReadMachine(executable);
                    if (machine != GameMachine.X64 && machine != GameMachine.X86) continue;
                    var game = new RunningGame { Executable = executable, Is32Bit = machine == GameMachine.X86,
                        Presented = ReadPresentedRenderers(id) };
                    string folder = Path.GetDirectoryName(executable);
                    try
                    {
                        if (game.Is32Bit) Read32BitModules(id, game, folder);
                        else
                            foreach (ProcessModule module in process.Modules)
                                AddModule(game, folder, module.ModuleName, module.FileName);
                    }
                    catch (Win32Exception) { }
                    if (game.Apis == GraphicsApi.None) game.Apis = GameDiscovery.ReadImports(executable, out game.ManagedStartup);
                    if ((game.Apis | game.Presented) != GraphicsApi.None) found.Add(game);
                }
            }
            catch (Win32Exception) { }
            catch (InvalidOperationException) { }
            catch (ArgumentException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (SecurityException) { }
        }
        return found;
    }
}

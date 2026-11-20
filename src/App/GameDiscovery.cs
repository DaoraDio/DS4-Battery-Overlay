using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

[Flags]
enum GraphicsApi { None = 0, Direct3D11 = 1, OpenGl = 2, Vulkan = 4, Direct3D12 = 8, Direct3D9 = 16 }

sealed class InstalledGame
{
    internal readonly string Name, Folder;
    internal readonly Dictionary<GraphicsApi, string> Candidates = new Dictionary<GraphicsApi, string>();
    internal readonly Dictionary<string, GameMachine> OpenGlMachines = new Dictionary<string, GameMachine>(GameDiscovery.Paths);
    internal readonly Dictionary<string, GameMachine> Direct3D9Machines = new Dictionary<string, GameMachine>(GameDiscovery.Paths);
    internal string OpenGl32Candidate, IconExecutable;
    internal string Direct3D9Candidate32;
    internal readonly HashSet<string> ManagedDirect3D9 = new HashSet<string>(GameDiscovery.Paths);
    internal GraphicsApi DetectedApis;
    internal GraphicsApi Detected32BitApis;

    internal InstalledGame(string name, string folder) { Name = name; Folder = folder; }
}

static class GameDiscovery
{
    internal static readonly StringComparer Paths = StringComparer.OrdinalIgnoreCase;
    static readonly Regex Field = new Regex(@"^\s*""(?<key>[^""]+)""\s+""(?<value>(?:\\.|[^""\\])*)""",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);
    internal static readonly GraphicsApi[] LocalApis = { GraphicsApi.Direct3D11, GraphicsApi.Direct3D12, GraphicsApi.OpenGl, GraphicsApi.Direct3D9 };
    internal const GraphicsApi LocalApiMask = GraphicsApi.Direct3D11 | GraphicsApi.Direct3D12 | GraphicsApi.OpenGl | GraphicsApi.Direct3D9;

    internal static GameOverlayComponent Component(GraphicsApi api, bool is32Bit = false)
    {
        if (is32Bit) return api == GraphicsApi.OpenGl ? OpenGlIntegration.OpenGl32 :
            api == GraphicsApi.Direct3D9 ? OpenGlIntegration.Direct3D9_32 : null;
        switch (api)
        {
            case GraphicsApi.Direct3D11: return OpenGlIntegration.Direct3D11;
            case GraphicsApi.Direct3D12: return OpenGlIntegration.Direct3D12;
            case GraphicsApi.OpenGl: return OpenGlIntegration.OpenGl;
            case GraphicsApi.Direct3D9: return OpenGlIntegration.Direct3D9;
            default: throw new ArgumentOutOfRangeException("api");
        }
    }

    internal static GraphicsApi IdentifyLibrary(string library)
    {
        switch (library.ToLowerInvariant())
        {
            case "d3d11.dll": return GraphicsApi.Direct3D11;
            case "opengl32.dll": return GraphicsApi.OpenGl;
            case "vulkan-1.dll": return GraphicsApi.Vulkan;
            case "d3d12.dll": return GraphicsApi.Direct3D12;
            case "d3d9.dll": return GraphicsApi.Direct3D9;
            default: return GraphicsApi.None;
        }
    }

    internal static string Prefix(string directory) { return directory.TrimEnd('\\', '/') + Path.DirectorySeparatorChar; }
    internal static bool ContainsPath(string folder, string path) { return path.StartsWith(Prefix(folder), StringComparison.OrdinalIgnoreCase); }

    internal static bool IsSafePath(string directory, string executable)
    {
        if (!ContainsPath(directory, Path.GetFullPath(executable)) ||
            (File.GetAttributes(executable) & FileAttributes.ReparsePoint) != 0) return false;
        bool inside = false;
        for (var parent = new DirectoryInfo(Path.GetDirectoryName(executable)); parent != null; parent = parent.Parent)
        {
            if ((parent.Attributes & FileAttributes.ReparsePoint) != 0) return false;
            if (Paths.Equals(parent.FullName, directory)) inside = true;
        }
        return inside;
    }

    internal static string ValidateLibrary(string path)
    {
        string full = Path.GetFullPath(path);
        string folder = full.TrimEnd('\\', '/');
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string[] broadFolders = {
            windows, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
        };
        if (!IsLocalPath(full) || Paths.Equals(Prefix(folder), Path.GetPathRoot(full)) ||
            ContainsPath(windows, folder) || broadFolders.Contains(folder, Paths) || !RegularDirectory(folder))
            throw new IOException("Choose a local folder that contains your games.");
        return folder;
    }

    static bool IsLocalPath(string path)
    {
        return path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/');
    }

    static string SteamPath(RegistryHive hive, RegistryView view, string property)
    {
        try
        {
            using (var root = RegistryKey.OpenBaseKey(hive, view))
            using (var key = root.OpenSubKey(@"Software\Valve\Steam"))
                return key == null ? null : key.GetValue(property) as string;
        }
        catch (SecurityException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    internal static string[] SteamRoots()
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        return new[] { SteamPath(RegistryHive.CurrentUser, RegistryView.Default, "SteamPath"),
            SteamPath(RegistryHive.LocalMachine, RegistryView.Registry32, "InstallPath"),
            programFiles.Length == 0 ? null : Path.Combine(programFiles, "Steam") };
    }

    internal static List<InstalledGame> FindGames(IEnumerable<string> customFolders, IEnumerable<string> steamRoots)
    {
        var games = new Dictionary<string, InstalledGame>(Paths);
        var libraries = new HashSet<string>(Paths);
        foreach (string root in steamRoots)
        {
            AddSteamLibrary(libraries, root);
            if (string.IsNullOrEmpty(root) || !IsLocalPath(root)) continue;
            foreach (Match field in ReadFields(Path.Combine(root, "steamapps", "libraryfolders.vdf")))
            {
                string key = field.Groups["key"].Value;
                if (key == "path" || key.All(char.IsDigit)) AddSteamLibrary(libraries, Decode(field));
            }
        }

        foreach (string library in libraries)
        {
            string common = Path.Combine(library, "common");
            foreach (string manifest in ListFiles(library, "appmanifest_*.acf"))
            {
                string installDir = null, name = null;
                foreach (Match field in ReadFields(manifest))
                {
                    string key = field.Groups["key"].Value;
                    if (key == "installdir" && installDir == null) installDir = Decode(field);
                    if (key == "name" && name == null) name = Decode(field);
                }
                if (string.IsNullOrWhiteSpace(installDir) || installDir.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                    installDir != installDir.TrimEnd(' ', '.')) continue;
                AddGame(games, Path.Combine(common, installDir), name ?? installDir);
            }
        }
        foreach (string custom in customFolders)
        {
            if (!RegularDirectory(custom)) continue;
            if (ListFiles(custom, "*.exe").Length != 0) AddCustomGame(games, custom);
            foreach (string directory in ListDirectories(custom)) AddCustomGame(games, directory);
        }
        return games.Values.OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    static void AddSteamLibrary(HashSet<string> libraries, string folder)
    {
        if (string.IsNullOrEmpty(folder) || !IsLocalPath(folder)) return;
        try
        {
            string path = Path.GetFullPath(Path.Combine(folder, "steamapps"));
            if (RegularDirectory(path)) libraries.Add(path);
        }
        catch (ArgumentException) { }
        catch (IOException) { }
        catch (NotSupportedException) { }
    }

    static void AddGame(Dictionary<string, InstalledGame> games, string folder, string name)
    {
        if (RegularDirectory(folder) && !games.ContainsKey(folder)) games.Add(folder, new InstalledGame(name, folder));
    }

    static void AddCustomGame(Dictionary<string, InstalledGame> games, string folder)
    {
        if (!games.Keys.Any(existing => ContainsPath(folder, existing)))
            AddGame(games, folder, Path.GetFileName(folder));
    }

    static Match[] ReadFields(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 4 * 1024 * 1024) return new Match[0];
            return Field.Matches(File.ReadAllText(path)).Cast<Match>().ToArray();
        }
        catch (IOException) { return new Match[0]; }
        catch (UnauthorizedAccessException) { return new Match[0]; }
    }

    static string Decode(Match match)
    {
        return match.Groups["value"].Value.Replace("\\\\", "\\").Replace("\\\"", "\"");
    }

    static bool RegularDirectory(string path)
    {
        try
        {
            var flags = File.GetAttributes(path);
            return (flags & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == FileAttributes.Directory;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (ArgumentException) { return false; }
    }

    static string[] ListDirectories(string path)
    {
        try { return Directory.GetDirectories(path).Where(RegularDirectory).ToArray(); }
        catch (IOException) { return new string[0]; }
        catch (UnauthorizedAccessException) { return new string[0]; }
    }

    static string[] ListFiles(string path, string mask)
    {
        try { return Directory.GetFiles(path, mask); }
        catch (IOException) { return new string[0]; }
        catch (UnauthorizedAccessException) { return new string[0]; }
    }

    internal static void ReadCandidates(InstalledGame game)
    {
        var remaining = new Stack<string>();
        var executables = new Dictionary<GraphicsApi, List<string>>();
        var openGl32Executables = new List<string>();
        var direct3D9Executables32 = new List<string>();
        string title = IconName(game.Name);
        int iconRank = int.MinValue;
        remaining.Push(game.Folder);
        for (int directories = 0; remaining.Count != 0 && directories < 2500; ++directories)
        {
            string folder = remaining.Pop();
            foreach (string child in ListDirectories(folder)) remaining.Push(child);
            foreach (string executable in ListFiles(folder, "*.exe"))
            {
                if (!string.Equals(Path.GetExtension(executable), ".exe", StringComparison.OrdinalIgnoreCase)) continue;
                GameMachine machine;
                bool managedStartup;
                GraphicsApi apis = ReadImports(executable, out machine, out managedStartup);
                if (managedStartup) game.ManagedDirect3D9.Add(executable);
                if (machine == GameMachine.X64 || machine == GameMachine.X86)
                {
                    string name = IconName(Path.GetFileNameWithoutExtension(executable));
                    int rank = (name == title ? 8 : 0) + (apis != GraphicsApi.None ? 4 : 0) +
                        (Paths.Equals(folder, game.Folder) ? 2 : 0);
                    if (name.Contains("crash") || name.StartsWith("unins", StringComparison.Ordinal) ||
                        name.StartsWith("setup", StringComparison.Ordinal)) rank -= 8;
                    if (rank > iconRank)
                    {
                        iconRank = rank;
                        game.IconExecutable = executable;
                    }
                }
                if (machine == GameMachine.X86)
                    game.Detected32BitApis |= apis;
                else game.DetectedApis |= apis;
                foreach (GraphicsApi api in new[] { GraphicsApi.OpenGl, GraphicsApi.Direct3D9 })
                {
                    if ((apis & api) == 0) continue;
                    var machines = api == GraphicsApi.OpenGl ? game.OpenGlMachines : game.Direct3D9Machines;
                    GameMachine previous;
                    if (machines.TryGetValue(folder, out previous) && previous != machine)
                        machines[folder] = GameMachine.Unknown;
                    else machines[folder] = machine;
                }
                if (machine == GameMachine.X86)
                {
                    if ((apis & GraphicsApi.OpenGl) != 0 && OpenGlIntegration.OpenGl32 != null)
                        openGl32Executables.Add(executable);
                    if ((apis & GraphicsApi.Direct3D9) != 0) direct3D9Executables32.Add(executable);
                    continue;
                }
                foreach (GraphicsApi api in LocalApis)
                {
                    if ((apis & api) == 0) continue;
                    List<string> matches;
                    if (!executables.TryGetValue(api, out matches)) executables.Add(api, matches = new List<string>());
                    matches.Add(executable);
                }
            }
        }
        // Let a running game resolve ambiguous installs or scans that reached the work limit.
        if (remaining.Count == 0)
        {
            foreach (var match in executables)
                if (match.Value.Count == 1) game.Candidates[match.Key] = match.Value[0];
            if (openGl32Executables.Count == 1) game.OpenGl32Candidate = openGl32Executables[0];
            if (direct3D9Executables32.Count == 1) game.Direct3D9Candidate32 = direct3D9Executables32[0];
        }
    }

    static string IconName(string title)
    {
        return new string(title.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    struct Section
    {
        internal uint Address, SizeOnDisk, Offset;
    }

    static long FileOffset(Section[] sections, uint address, int size, long fileLength)
    {
        foreach (Section section in sections)
        {
            if (address < section.Address) continue;
            long relative = address - section.Address;
            long position = section.Offset + relative;
            if (relative + size <= section.SizeOnDisk && position + size <= fileLength) return position;
        }
        return -1;
    }

    static GraphicsApi ReadImportDirectory(BinaryReader image, Section[] sections,
        long optionalHeader, int optionalSize, int dataDirectories, uint directoryCount, uint imageBase, int index)
    {
        bool delayed = index == 13;
        int entryOffset = dataDirectories + index * 8;
        if (directoryCount <= index || optionalSize < entryOffset + 8) return GraphicsApi.None;

        Stream file = image.BaseStream;
        file.Position = optionalHeader + entryOffset;
        uint directory = image.ReadUInt32();
        uint bytes = image.ReadUInt32();
        if (directory == 0) return GraphicsApi.None;

        uint descriptorSize = delayed ? 32u : 20u;
        uint limit = Math.Min(bytes, descriptorSize * 1024);
        var name = new StringBuilder(64);
        GraphicsApi apis = GraphicsApi.None;
        for (uint offset = 0; offset + descriptorSize <= limit && directory <= uint.MaxValue - offset;
            offset += descriptorSize)
        {
            long descriptor = FileOffset(sections, directory + offset, (int)descriptorSize, file.Length);
            if (descriptor < 0) break;
            file.Position = descriptor + (delayed ? 0 : 12);
            uint attributes = delayed ? image.ReadUInt32() : 1;
            uint nameAddress = image.ReadUInt32();
            if (nameAddress == 0) break;
            // Older PE32 delay imports can store virtual addresses instead of RVAs.
            if ((attributes & 1) == 0)
            {
                if (dataDirectories != 96 || nameAddress < imageBase) continue;
                nameAddress -= imageBase;
            }
            name.Clear();
            for (uint characterIndex = 0; characterIndex < 64 && nameAddress <= uint.MaxValue - characterIndex;
                ++characterIndex)
            {
                long character = FileOffset(sections, nameAddress + characterIndex, 1, file.Length);
                if (character < 0) break;
                file.Position = character;
                byte value = image.ReadByte();
                if (value == 0) { apis |= IdentifyLibrary(name.ToString()); break; }
                name.Append((char)value);
            }
        }
        return apis;
    }

    internal static GraphicsApi ReadImports(string executable)
    {
        GameMachine machine;
        bool managedStartup;
        return ReadImports(executable, out machine, out managedStartup);
    }

    internal static GraphicsApi ReadImports(string executable, out bool managedStartup)
    {
        GameMachine machine;
        return ReadImports(executable, out machine, out managedStartup);
    }

    static GraphicsApi ReadImports(string executable, out GameMachine machine, out bool managedStartup)
    {
        machine = GameMachine.Unknown;
        managedStartup = false;
        try
        {
            if ((File.GetAttributes(executable) & FileAttributes.ReparsePoint) != 0) return GraphicsApi.None;
            using (var file = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var image = new BinaryReader(file))
            {
                if (file.Length < 64 || image.ReadUInt16() != 0x5a4d) return GraphicsApi.None;
                file.Position = 0x3c;
                long pe = image.ReadUInt32();
                if (pe < 64 || pe + 24 > file.Length) return GraphicsApi.None;
                file.Position = pe;
                if (image.ReadUInt32() != 0x4550) return GraphicsApi.None;
                machine = (GameMachine)image.ReadUInt16();
                if (machine != GameMachine.X64 && machine != GameMachine.X86) return GraphicsApi.None;
                bool x86 = machine == GameMachine.X86;
                int dataDirectories = x86 ? 96 : 112;
                int sectionCount = image.ReadUInt16();
                file.Position = pe + 20;
                int optionalSize = image.ReadUInt16();
                if ((image.ReadUInt16() & 0x2000) != 0 || sectionCount < 1 || sectionCount > 96 ||
                    optionalSize < dataDirectories + 16 || pe + 24 + optionalSize + sectionCount * 40 > file.Length ||
                    image.ReadUInt16() != (x86 ? 0x10b : 0x20b))
                    return GraphicsApi.None;
                long optionalHeader = pe + 24;
                file.Position = optionalHeader + 28;
                uint imageBase = x86 ? image.ReadUInt32() : 0;
                file.Position = optionalHeader + dataDirectories - 4;
                uint directoryCount = image.ReadUInt32();
                var sections = new Section[sectionCount];
                for (int index = 0; index < sections.Length; ++index)
                {
                    file.Position = pe + 24 + optionalSize + index * 40 + 12;
                    sections[index].Address = image.ReadUInt32();
                    sections[index].SizeOnDisk = image.ReadUInt32();
                    sections[index].Offset = image.ReadUInt32();
                }
                var apis = ReadImportDirectory(image, sections, optionalHeader, optionalSize, dataDirectories, directoryCount, imageBase, 1) |
                    ReadImportDirectory(image, sections, optionalHeader, optionalSize, dataDirectories, directoryCount, imageBase, 13);
                if (apis == GraphicsApi.None && directoryCount > 14 && optionalSize >= dataDirectories + 15 * 8)
                {
                    file.Position = optionalHeader + dataDirectories + 14 * 8;
                    if (image.ReadUInt32() != 0)
                    {
                        apis = ManagedGraphicsReferences.Read(executable);
                        managedStartup = apis == GraphicsApi.Direct3D9;
                    }
                }
                return apis;
            }
        }
        catch (IOException) { return GraphicsApi.None; }
        catch (UnauthorizedAccessException) { return GraphicsApi.None; }
    }
}

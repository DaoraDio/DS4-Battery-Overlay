using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows.Forms;
using Microsoft.Win32;

static class OpenGlIntegration
{
    internal static readonly GameOverlayComponent OpenGl = new GameOverlayComponent("OpenGL", "opengl32.dll", "DS4OpenGLLayer");
    internal static readonly GameOverlayComponent OpenGl32 =
        typeof(OpenGlIntegration).Assembly.GetManifestResourceInfo("DS4OpenGLLayer32") == null ? null :
        new GameOverlayComponent("OpenGL", "opengl32.dll", "DS4OpenGLLayer32", GameMachine.X86);
    internal static readonly GameOverlayComponent Direct3D11 = new GameOverlayComponent("DirectX 11", "d3d11.dll", "DS4Direct3D11Layer");
    internal static readonly GameOverlayComponent Direct3D12 = new GameOverlayComponent("DirectX 12", "d3d12.dll", "DS4Direct3D12Layer");
    internal static readonly GameOverlayComponent Direct3D9 = new GameOverlayComponent("DirectX 9", "d3d9.dll", "DS4Direct3D9Layer");
    internal static readonly GameOverlayComponent Direct3D9_32 = new GameOverlayComponent("DirectX 9", "d3d9.dll", "DS4Direct3D9Layer32", GameMachine.X86);

    public static string Digest { get { return OpenGl.Digest; } }
    public static bool Install(string executable, string previousDigest) { return OpenGl.Install(executable, previousDigest); }
    public static void Remove(string executable, string expectedDigest) { OpenGl.Remove(executable, expectedDigest); }

    internal static Stream OpenResource(string name)
    {
        Stream result = typeof(OpenGlIntegration).Assembly.GetManifestResourceStream(name);
        if (result == null) throw new InvalidDataException("The built-in overlay component is missing.");
        return result;
    }

    internal static void RequireX64Game(string executable)
    {
        if (ReadMachine(executable) != GameMachine.X64)
            throw new IOException("Select a 64-bit Windows game executable for an Intel or AMD processor.");
    }

    internal static GameMachine ReadMachine(string executable)
    {
        using (var file = File.OpenRead(executable))
        using (var image = new BinaryReader(file))
        {
            if (file.Length < 64 || image.ReadUInt16() != 0x5a4d) throw new IOException("Select a Windows game executable.");
            file.Position = 0x3c;
            int offset = image.ReadInt32();
            if (offset < 64 || offset > file.Length - 6) throw new IOException("Select a Windows game executable.");
            file.Position = offset;
            if (image.ReadUInt32() != 0x4550) throw new IOException("Select a Windows game executable.");
            return (GameMachine)image.ReadUInt16();
        }
    }

    internal static int Smoke(string executable, string arguments)
    {
        GameOverlayComponent component = null;
        bool added = false;
        try
        {
            component = ReadMachine(executable) == GameMachine.X86 ? OpenGl32 : OpenGl;
            if (component == null) throw new IOException("The 32-bit OpenGL overlay is unavailable in this build.");
            added = component.Install(executable, null);
            var info = new ProcessStartInfo(executable, arguments) {
                UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable),
                WindowStyle = ProcessWindowStyle.Hidden
            };
            bool live = arguments == "--publisher" || arguments == "--publisher-core";
            using (var publisher = live ? new BatteryPublisher() : null)
            {
                if (live) info.EnvironmentVariables["DS4_BATTERY_FRAME"] = publisher.FrameName;
                using (Process process = Process.Start(info))
                {
                    if (live)
                    {
                        if (process.WaitForExit(1000)) return 3;
                        publisher.Appearance = new OverlayAppearance { Position = 3, ShowBackground = false };
                        publisher.Publish("55%");
                        if (process.WaitForExit(2000)) return 3;
                        publisher.Publish(null);
                    }
                    if (process.WaitForExit(live ? 8000 : 18000)) return process.ExitCode;
                    process.Kill();
                    process.WaitForExit();
                    return 4;
                }
            }
        }
        catch (Exception error)
        {
            string trace = Environment.GetEnvironmentVariable("DS4_OPENGL_TRACE");
            if (!string.IsNullOrEmpty(trace)) File.AppendAllText(trace, error + Environment.NewLine);
            return 5;
        }
        finally
        {
            if (added) component.Remove(executable, component.Digest);
        }
    }
}

enum GameMachine : ushort { Unknown = 0, X86 = 0x014c, X64 = 0x8664 }

sealed class GameOverlayComponent
{
    internal readonly string Name;
    readonly string fileName, resourceName;
    readonly GameMachine machine;
    byte[] binary;
    string binaryDigest;

    internal GameOverlayComponent(string name, string fileName, string resourceName, GameMachine machine = GameMachine.X64)
    {
        Name = name;
        this.fileName = fileName;
        this.resourceName = resourceName;
        this.machine = machine;
    }

    public string Digest
    {
        get
        {
            if (binaryDigest == null)
            {
                using (var resource = OpenGlIntegration.OpenResource(resourceName))
                using (var buffer = new MemoryStream())
                {
                    resource.CopyTo(buffer);
                    binary = buffer.ToArray();
                }
                using (var sha = SHA256.Create()) binaryDigest = BitConverter.ToString(sha.ComputeHash(binary)).Replace("-", "");
            }
            return binaryDigest;
        }
    }

    string ComponentPath(string executable)
    {
        return Path.Combine(Path.GetDirectoryName(executable), fileName);
    }

    internal string FileName { get { return fileName; } }

    internal bool IsInstalled(string executable, string previousDigest, out bool current, bool managedStartup = false)
    {
        current = false;
        string path = ComponentPath(executable);
        if (!File.Exists(path)) return false;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The existing " + fileName + " is a link.");
        string hash = HashFile(path);
        current = hash == Digest;
        if (!current && hash != previousDigest)
            throw new IOException("This game folder already has a " + fileName + " from another source.");
        if (managedStartup) current = current && ManagedGameStartup.IsCurrent(executable);
        return true;
    }

    internal static string HashFile(string path)
    {
        using (var file = File.OpenRead(path))
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
    }

    public bool Install(string executable, string previousDigest, bool managedStartup = false)
    {
        var startup = managedStartup ? ManagedGameStartup.Prepare(executable) : null;
        bool installed = InstallBinary(executable, previousDigest);
        if (startup != null) startup.Apply();
        return installed;
    }

    bool InstallBinary(string executable, string previousDigest)
    {
        if (OpenGlIntegration.ReadMachine(executable) != machine)
            throw new IOException("The " + Name + " overlay DLL does not match the game's executable architecture.");
        string path = ComponentPath(executable);
        bool existed = File.Exists(path);
        string expected = Digest;
        if (existed)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The existing " + fileName + " is a link.");
            string installed = HashFile(path);
            if (installed == expected) return false;
            if (installed != previousDigest)
                throw new IOException("This game folder already has a " + fileName + " from another source.");
        }

        string temporary = Path.Combine(Path.GetDirectoryName(path), "ds4-overlay-" + Guid.NewGuid().ToString("N").Substring(0, 12) + ".tmp");
        try
        {
            File.WriteAllBytes(temporary, binary);
            if (existed)
            {
                if (HashFile(path) != previousDigest) throw new IOException(fileName + " changed while the app was updating it.");
                File.Replace(temporary, path, null);
            }
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return true;
    }

    public void Remove(string executable, string expectedDigest, bool ignoreForeign = false)
    {
        if (fileName == "d3d9.dll") ManagedGameStartup.Remove(executable);
        string path = ComponentPath(executable);
        if (!File.Exists(path)) return;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The existing " + fileName + " is a link.");
        string installed = HashFile(path);
        if (installed != expectedDigest && installed != Digest)
        {
            if (ignoreForeign) return;
            throw new IOException(fileName + " was changed outside the overlay app.");
        }
        File.Delete(path);
    }

}

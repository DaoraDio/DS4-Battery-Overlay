using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

static class ManagedGameStartup
{
    const string AssemblyName = "DS4Overlay.ManagedBootstrap, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";
    const string TypeName = "DS4Overlay.ManagedBootstrap.Manager";
    const string MarkerPrefix = "DS4Overlay.ManagedBootstrap|";
    static readonly GameOverlayComponent Bootstrap32 = new GameOverlayComponent("XNA startup helper",
        "DS4Overlay.ManagedBootstrap.dll", "DS4ManagedBootstrap", GameMachine.X86);
    static readonly GameOverlayComponent Bootstrap64 = new GameOverlayComponent("XNA startup helper",
        "DS4Overlay.ManagedBootstrap.dll", "DS4ManagedBootstrap", GameMachine.X64);

    internal static Installation Prepare(string executable)
    {
        var installation = new Installation(executable);
        installation.Validate();
        return installation;
    }

    internal static bool IsCurrent(string executable) { return new Installation(executable).IsCurrent; }

    internal static void Remove(string executable)
    {
        string path = executable + ".config";
        if (!File.Exists(path) || File.ReadAllText(path).IndexOf(MarkerPrefix, StringComparison.Ordinal) < 0) return;
        new Installation(executable).Remove();
    }

    internal sealed class Installation
    {
        readonly string executable, path;
        readonly GameOverlayComponent bootstrap;
        readonly XmlDocument document;
        readonly XmlElement runtime;
        readonly bool created;
        XmlComment marker;
        string previousDigest, expectedConfigDigest;

        internal Installation(string executable)
        {
            this.executable = executable;
            path = executable + ".config";
            bootstrap = File.Exists(executable) && OpenGlIntegration.ReadMachine(executable) == GameMachine.X64
                ? Bootstrap64 : Bootstrap32;
            document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
            bool exists = File.Exists(path);
            if (exists)
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("The game's .NET configuration is a link.");
                expectedConfigDigest = GameOverlayComponent.HashFile(path);
                try
                {
                    using (var reader = XmlReader.Create(path, new XmlReaderSettings {
                        DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) document.Load(reader);
                }
                catch (XmlException error) { throw new IOException("Could not read the game's .NET configuration.", error); }
            }
            else document.AppendChild(document.CreateElement("configuration"));
            if (document.DocumentElement == null || document.DocumentElement.Name != "configuration")
                throw new IOException("The game's .NET configuration has an unsupported root element.");
            runtime = document.DocumentElement.SelectSingleNode("runtime") as XmlElement;
            if (runtime == null)
            {
                runtime = document.CreateElement("runtime");
                document.DocumentElement.AppendChild(runtime);
            }
            marker = runtime.ChildNodes.OfType<XmlComment>().FirstOrDefault(comment => comment.Value.StartsWith(MarkerPrefix, StringComparison.Ordinal));
            string[] ownership = marker == null ? new string[0] : marker.Value.Split('|');
            previousDigest = ownership.Length >= 2 ? ownership[1] : null;
            created = ownership.Length >= 3 ? ownership[2] == "1" : !exists;
        }

        string Value(string name)
        {
            var element = runtime.SelectSingleNode(name) as XmlElement;
            return element == null ? null : element.GetAttribute("value");
        }

        internal void Validate()
        {
            if ((Value("appDomainManagerAssembly") != null && Value("appDomainManagerAssembly") != AssemblyName) ||
                (Value("appDomainManagerType") != null && Value("appDomainManagerType") != TypeName))
                throw new IOException("This game already uses a different .NET startup manager.");
            bool current;
            bootstrap.IsInstalled(executable, previousDigest, out current);
        }

        internal bool IsCurrent
        {
            get
            {
                if (marker == null || Value("appDomainManagerAssembly") != AssemblyName || Value("appDomainManagerType") != TypeName)
                    return false;
                bool current;
                return bootstrap.IsInstalled(executable, previousDigest, out current) && current;
            }
        }

        void SetValue(string name, string value)
        {
            var element = runtime.SelectSingleNode(name) as XmlElement;
            if (element == null)
            {
                element = document.CreateElement(name);
                runtime.AppendChild(element);
            }
            element.SetAttribute("value", value);
        }

        internal void Apply()
        {
            Validate();
            bootstrap.Install(executable, previousDigest);
            SetValue("appDomainManagerAssembly", AssemblyName);
            SetValue("appDomainManagerType", TypeName);
            if (marker == null)
            {
                marker = document.CreateComment("");
                runtime.AppendChild(marker);
            }
            marker.Value = MarkerPrefix + bootstrap.Digest + "|" + (created ? "1" : "0");
            Save();
        }

        internal void Remove()
        {
            if (marker == null) return;
            bool changed = false;
            foreach (string name in new[] { "appDomainManagerAssembly", "appDomainManagerType" })
            {
                if (Value(name) != (name == "appDomainManagerAssembly" ? AssemblyName : TypeName)) continue;
                runtime.RemoveChild(runtime.SelectSingleNode(name));
                changed = true;
            }
            // Keep the ownership marker until a locked helper can be removed on a later retry.
            if (changed) Save();
            bootstrap.Remove(executable, previousDigest);
            runtime.RemoveChild(marker);
            if (!runtime.HasAttributes && !runtime.ChildNodes.Cast<XmlNode>().Any(node => node.NodeType != XmlNodeType.Whitespace))
                document.DocumentElement.RemoveChild(runtime);
            bool empty = !document.DocumentElement.HasAttributes &&
                !document.DocumentElement.ChildNodes.Cast<XmlNode>().Any(node => node.NodeType != XmlNodeType.Whitespace);
            if (created && empty)
            {
                VerifyConfiguration();
                File.Delete(path);
            }
            else Save();
        }

        void VerifyConfiguration()
        {
            bool exists = File.Exists(path);
            if (exists != (expectedConfigDigest != null) || exists &&
                ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || GameOverlayComponent.HashFile(path) != expectedConfigDigest))
                throw new IOException("The game's .NET configuration changed during overlay setup. Try again.");
        }

        void Save()
        {
            string temporary = Path.Combine(Path.GetDirectoryName(path), "ds4-config-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var writer = XmlWriter.Create(temporary, new XmlWriterSettings { Encoding = new UTF8Encoding(false) }))
                    document.Save(writer);
                VerifyConfiguration();
                if (expectedConfigDigest == null) File.Move(temporary, path);
                else File.Replace(temporary, path, null);
                expectedConfigDigest = GameOverlayComponent.HashFile(path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}

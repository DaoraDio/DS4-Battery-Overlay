using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

// Uses the public shared-memory interface supplied with RTSS, never RTSSHooks.dll.
sealed unsafe class RtssClient : IDisposable
{
    internal const uint Signature = 0x52545353;
    internal const string Owner = "DS4Overlay.RTSSTest";
    readonly string mappingName;
    readonly string owner;
    MemoryMappedFile mapping;
    MemoryMappedViewAccessor view;
    byte* memory;
    long capacity, nextConnect;
    readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    int slot = -1;
    string lastText;
    internal string Status { get; private set; }
    internal bool Connected { get { return memory != null && Read(0) == Signature; } }

    internal RtssClient(string mappingName = "RTSSSharedMemoryV2", string owner = Owner)
    { this.mappingName = mappingName; this.owner = owner; Status = "Waiting for RTSS"; }

    internal int Zoom
    {
        get
        {
            if (!Connect() || capacity < 72) return 1;
            uint size = Read(8), offset = Read(12), count = Read(16), index = Read(64);
            if (size < 328 || count > 1024 || index >= count || !Region(offset, (ulong)size * count)) return 1;
            ulong at = (ulong)offset + (ulong)size * index + 324;
            if (at > int.MaxValue) return 1;
            uint zoom = Read((int)at);
            return zoom >= 1 && zoom <= 8 ? (int)zoom : 1;
        }
    }

    internal Point Origin
    {
        get
        {
            if (!Connect() || capacity < 72) return Point.Empty;
            uint size = Read(8), offset = Read(12), count = Read(16), index = Read(64);
            if (size < 328 || count > 1024 || index >= count || !Region(offset, (ulong)size * count)) return Point.Empty;
            ulong at = (ulong)offset + (ulong)size * index;
            if (at > int.MaxValue - 328) return Point.Empty;
            return new Point(unchecked((int)Read((int)at + 316)), unchecked((int)Read((int)at + 320)));
        }
    }

    uint Read(int at) { return *(uint*)(memory + at); }
    bool Region(uint at, ulong bytes) { return at <= (ulong)capacity && bytes <= (ulong)capacity - at; }

    bool Validate()
    {
        if (capacity < 40 || Read(0) != Signature || Read(4) < 0x0002000e || Read(4) >= 0x00030000)
            return false;
        uint size = Read(20), offset = Read(24), count = Read(28);
        uint required = Read(4) >= 0x00020014 ? 299520u : 4608u;
        return size >= required && size <= 1048576 && offset >= 40 && count >= 2 && count <= 256 &&
            Region(offset, (ulong)size * count);
    }

    bool Connect()
    {
        if (memory != null && Validate()) return true;
        Close();
        long now = clock.ElapsedMilliseconds;
        if (now < nextConnect) return false;
        nextConnect = now + 1000;
        try
        {
            mapping = MemoryMappedFile.OpenExisting(mappingName, MemoryMappedFileRights.ReadWrite);
            view = mapping.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite);
            capacity = view.Capacity;
            view.SafeMemoryMappedViewHandle.AcquirePointer(ref memory);
            if (!Validate()) { Status = "RTSS is starting, or its interface is unsupported"; Close(); return false; }
            Status = "RTSS connected";
            return true;
        }
        catch (FileNotFoundException) { Status = "Waiting for RTSS — start RivaTuner Statistics Server"; }
        catch (UnauthorizedAccessException) { Status = "RTSS access denied — run this test with the same privileges as RTSS"; }
        catch (IOException) { Status = "Waiting for RTSS to finish starting"; }
        Close();
        return false;
    }

    string ReadOwner(byte* entry)
    {
        int count = 0;
        while (count < 256 && entry[256 + count] != 0) count++;
        return Encoding.ASCII.GetString(entry + 256, count);
    }

    static void WriteString(byte* destination, string text, int maximum)
    {
        byte[] bytes = Encoding.Default.GetBytes(text);
        if (bytes.Length >= maximum) throw new InvalidDataException("The RTSS display is too large.");
        for (int index = 0; index < bytes.Length; index++) destination[index] = bytes[index];
        destination[bytes.Length] = 0;
    }

    internal bool Write(string text)
    {
        if (!Connect()) return false;
        // RTSS and other OSD clients use this lock too. Never wait on the render thread.
        if (Interlocked.CompareExchange(ref *(int*)(memory + 36), 1, 0) != 0) return false;
        try
        {
            if (!Validate()) return false;
            uint size = Read(20), offset = Read(24), count = Read(28);
            byte* entry = null;
            if (slot > 0 && slot < count)
            {
                byte* candidate = memory + offset + slot * size;
                if (ReadOwner(candidate) == owner) entry = candidate;
            }
            if (entry == null)
            {
                slot = -1;
                lastText = null;
                for (int pass = 0; pass < 2 && entry == null; pass++)
                for (int index = 1; index < count; index++)
                {
                    byte* candidate = memory + offset + index * size;
                    string currentOwner = ReadOwner(candidate);
                    if (pass == 0 ? currentOwner == owner : currentOwner.Length == 0)
                    {
                        entry = candidate;
                        slot = index;
                        if (currentOwner.Length == 0) WriteString(entry + 256, owner, 256);
                        break;
                    }
                }
            }
            if (entry == null) { Status = "RTSS has no free display slots"; return false; }
            if (text != lastText)
            {
                // Modern RTSS renderers read the 32KB field rather than the older 4KB field.
                if (Read(4) >= 0x00020014)
                {
                    WriteString(entry + 266752, text, 32768);
                    entry[512] = 0;
                }
                else WriteString(entry + 512, text, 4096);
                // Keep the legacy text field empty: all output uses the extended field.
                entry[0] = 0;
                Thread.MemoryBarrier();
                Interlocked.Increment(ref *(int*)(memory + 32));
                lastText = text;
            }
            Status = text.Length == 0 ? "RTSS connected · battery display hidden" : "RTSS connected · display published";
            return true;
        }
        finally { Volatile.Write(ref *(int*)(memory + 36), 0); }
    }

    internal bool IsForegroundGame(uint process)
    {
        if(process==0||!Connect()||Read(4)<0x00020010||capacity<72||Read(68)!=process)return false;
        uint size=Read(8),offset=Read(12),count=Read(16);
        if(size<268||count>4096||!Region(offset,(ulong)size*count))return false;
        for(uint index=0;index<count;index++)
        {
            ulong at=(ulong)offset+(ulong)index*size;
            if(at>int.MaxValue-268)return false;
            if(Read((int)at)!=process)continue;
            uint api=Read((int)at+264)&0xffff;
            if(api>=1&&api<=10)return true;
        }
        return false;
    }

    internal bool Release()
    {
        if (!Connected || !Validate()) return true;
        if (Interlocked.CompareExchange(ref *(int*)(memory + 36), 1, 0) != 0) return false;
        try
        {
            uint size = Read(20), offset = Read(24), count = Read(28);
            for (int index = 1; index < count; index++)
            {
                byte* entry = memory + offset + index * size;
                if (ReadOwner(entry) != owner) continue;
                entry[0] = entry[256] = entry[512] = 0;
                if (Read(4) >= 0x00020014) entry[266752] = 0;
                Interlocked.Increment(ref *(int*)(memory + 32));
            }
            slot = -1;
            lastText = null;
            return true;
        }
        finally { Volatile.Write(ref *(int*)(memory + 36), 0); }
    }

    void Close()
    {
        if (memory != null) { view.SafeMemoryMappedViewHandle.ReleasePointer(); memory = null; }
        if (view != null) view.Dispose();
        if (mapping != null) mapping.Dispose();
        view = null; mapping = null; slot = -1; lastText = null; capacity = 0;
    }

    public void Dispose()
    {
        // A renderer can hold its lock during shutdown. Retry briefly to avoid a stuck badge.
        for (int attempt = 0; attempt < 50; attempt++)
        {
            if (Release()) break;
            Thread.Sleep(5);
        }
        Close();
    }
}

sealed class RtssOutput : IDisposable
{
    internal static RtssOutput Current;
    readonly RtssClient client;
    readonly string imageDirectory;
    readonly string imageReferenceDirectory;
    Bitmap badge, popup, widgetBadge;
    string widgetBadgeKey;
    DesktopBatteryWidget desktopWidget;
    Bitmap popupSource;
    string badgeKey, atlasPath;
    bool dirty = true;
    int badgePosition, badgeMargin, popupPosition;
    int atlasPadding, popupCellY;
    byte popupOpacity;
    string caption, popupTitle, popupDetail;
    OverlayAppearance appearance;
    bool charging;
    bool displayDisconnected;
    internal bool CompatibleText;
    long demoUntil;
    string imageError;
    readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    internal string Status { get { return (desktopWidget == null ? null : desktopWidget.Error) ?? imageError ?? client.Status; } }
    internal bool ForegroundGame { get { return client.IsForegroundGame(PopupPresentation.ForegroundProcess()); } }

    internal RtssOutput(RtssClient client = null, string imageDirectory = null)
    {
        this.client = client ?? new RtssClient();
        if (imageDirectory != null)
        {
            this.imageDirectory = imageDirectory;
            imageReferenceDirectory = imageDirectory;
        }
        else
        {
            string rtss = FindRtssDirectory();
            if (rtss == null) throw new IOException("RTSS could not be found. Install or start RTSS, then reopen this version.");
            imageReferenceDirectory = @"Plugins\Client\Overlays\DS4Battery";
            this.imageDirectory = Path.Combine(rtss, imageReferenceDirectory);
        }
    }

    internal static string FindRtssDirectory()
    {
        foreach (var view in new[] { Microsoft.Win32.RegistryView.Registry32, Microsoft.Win32.RegistryView.Registry64 })
        {
            using (var root = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, view))
            using (var key = root.OpenSubKey(@"SOFTWARE\Unwinder\RTSS"))
            {
                string path = key == null ? null : key.GetValue("InstallDir") as string;
                if (!string.IsNullOrEmpty(path) && File.Exists(Path.Combine(path, "RTSS.exe"))) return path;
            }
        }
        foreach (var process in System.Diagnostics.Process.GetProcessesByName("RTSS"))
        using (process)
        {
            string path = OpenGlDiscovery.ExecutablePath((uint)process.Id);
            if (!string.IsNullOrEmpty(path)) return Path.GetDirectoryName(path);
        }
        return null;
    }

    internal void ShowDemo() { demoUntil = clock.ElapsedMilliseconds + 8000; badgeKey = null; }

    internal void Publish(string caption, OverlayAppearance appearance, bool charging, bool disconnected = false)
    {
        if (clock.ElapsedMilliseconds < demoUntil) caption = "65%";
        this.caption = caption; this.appearance = appearance; this.charging = charging;
        displayDisconnected = disconnected && appearance.ShowDisconnectedIcon && caption == null;
        badgePosition = appearance.Position;
        if (badgeMargin != appearance.Margin) dirty = true;
        badgeMargin = appearance.Margin;
        string key = string.Join("|", new object[] { caption ?? "hidden", displayDisconnected, charging, appearance.Scale,
            appearance.ShowIcon, appearance.SegmentedIcon, appearance.ShowNumber, appearance.NumberInsideIcon,
            appearance.ShowBackground, appearance.CornerRadius, appearance.Background.ToArgb(), appearance.Text.ToArgb() });
        if (key != badgeKey)
        {
            if (badge != null) badge.Dispose();
            badge = displayDisconnected ? PopupControllerIcon.CreateDisconnectedBadge(appearance)
                : caption == null ? null : BatteryPublisher.CreateBadge(caption, appearance, charging);
            if (badge != null && appearance.ShowBackground && appearance.CornerRadius > 0)
                OverlayPage.BadgePreview.RoundCorners(badge, appearance.CornerRadius);
            badgeKey = key;
            dirty = true;
        }
        Flush();
        UpdateDesktopWidget(caption,appearance,charging,disconnected);
    }

    void UpdateDesktopWidget(string caption,OverlayAppearance settings,bool charging,bool disconnected)
    {
        if(!settings.ShowDesktopWidget)
        {
            if(desktopWidget!=null)desktopWidget.Update(null,false,false);
            if(widgetBadge!=null)widgetBadge.Dispose();widgetBadge=null;widgetBadgeKey=null;
            return;
        }
        var style=settings.Widget.ToAppearance();
        bool offline=disconnected&&style.ShowDisconnectedIcon&&caption==null;
        string key=string.Join("|",new object[]{caption??"hidden",offline,charging,style.Scale,style.CornerRadius,
            style.ShowIcon,style.ShowNumber,style.SegmentedIcon,style.NumberInsideIcon,style.ShowBackground,style.Background.ToArgb(),style.Text.ToArgb()});
        if(key!=widgetBadgeKey)
        {
            if(widgetBadge!=null)widgetBadge.Dispose();
            widgetBadge=offline?PopupControllerIcon.CreateDisconnectedBadge(style):
                caption==null?null:BatteryPublisher.CreateBadge(caption,style,charging);
            if(widgetBadge!=null&&style.ShowBackground&&style.CornerRadius>0)
                OverlayPage.BadgePreview.RoundCorners(widgetBadge,style.CornerRadius);
            widgetBadgeKey=key;
        }
        if(widgetBadge!=null&&desktopWidget==null)desktopWidget=new DesktopBatteryWidget();
        if(desktopWidget!=null)desktopWidget.Update(widgetBadge,true,ForegroundGame,settings.Widget.HideInGames,settings.Widget.ClickThrough);
    }

    internal void PublishPopup(Bitmap image, byte opacity, int position, string title = "Controller notification", string detail = null)
    {
        popupTitle = title; popupDetail = detail;
        if (image != popupSource)
        {
            if (popup != null) popup.Dispose();
            popup = image == null ? null : new Bitmap(image);
            popupSource = image;
            dirty = true;
        }
        popupOpacity = opacity;
        popupPosition = position;
        Flush();
    }

    void PrepareAtlas()
    {
        if (!dirty) return;
        if (badge == null && popup == null) { atlasPath = null; dirty = false; return; }
        atlasPadding = Math.Max(badgeMargin, popup == null ? 0 : 12 + (badge == null ? 0 : badge.Height + 6)) + 8;
        popupCellY = badge == null ? 0 : badge.Height + 2 * atlasPadding;
        int width = TextureSize(Math.Max(badge == null ? 0 : badge.Width, popup == null ? 0 : popup.Width) + 2 * atlasPadding, 256);
        int height = TextureSize(popupCellY + (popup == null ? 0 : popup.Height + 2 * atlasPadding), 128);
        using (var atlas = new Bitmap(width, height, PixelFormat.Format32bppArgb))
        using (var graphics = Graphics.FromImage(atlas))
        using (var encoded = new MemoryStream())
        {
            graphics.Clear(Color.Transparent);
            if (badge != null) graphics.DrawImageUnscaled(badge, atlasPadding, atlasPadding);
            if (popup != null) graphics.DrawImageUnscaled(popup, atlasPadding, popupCellY + atlasPadding);
            atlas.Save(encoded, ImageFormat.Png);
            byte[] bytes = encoded.ToArray();
            string digest;
            using (var hash = SHA256.Create()) digest = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "");
            Directory.CreateDirectory(imageDirectory);
            // Keep the RTSS reference compact, while verifying the complete digest
            // before reusing an existing image.
            string fileName = "ds4-" + digest.Substring(0, 16) + ".png";
            string filePath = Path.Combine(imageDirectory, fileName);
            if (!File.Exists(filePath)) File.WriteAllBytes(filePath, bytes);
            else
            {
                using (var hash = SHA256.Create())
                    if (BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(filePath))).Replace("-", "") != digest)
                        throw new IOException("A cached RTSS image has changed. The file was not overwritten.");
            }
            atlasPath = Path.Combine(imageReferenceDirectory, fileName);
        }
        dirty = false;
    }

    static int TextureSize(int value, int minimum)
    {
        int size = minimum;
        while (size < value) size *= 2;
        return size;
    }

    internal static int Sticky(int position)
    { return new[] { 2, 0, 8, 6, 1, 7 }[Math.Max(0, Math.Min(5, position))]; }

    string Layer(Bitmap image, int cellY, int position, int margin, int layer, byte opacity, int verticalOffset = 0)
    {
        int zoom = client.Zoom;
        bool left = position == 1 || position == 3, right = position == 0 || position == 2;
        bool bottom = position == 2 || position == 3 || position == 5;
        int sourceWidth = image.Width + (left || right ? margin : 0);
        int sourceHeight = image.Height + margin + verticalOffset;
        int width = (sourceWidth + zoom - 1) / zoom, height = (sourceHeight + zoom - 1) / zoom;
        int extraX = width * zoom - sourceWidth, extraY = height * zoom - sourceHeight;
        int x = atlasPadding - (left ? margin : right ? extraX : extraX / 2);
        int y = cellY + atlasPadding - (bottom ? extraY : margin + verticalOffset);
        sourceWidth = width * zoom; sourceHeight = height * zoom;
        return string.Format(CultureInfo.InvariantCulture,
            "<P{0}><L{1}><S=100><A=0><C={2:X2}FFFFFF><LI={3}><I={4},{5},{6},{7},{8},{9}><C><A><S>",
            Sticky(position), layer, opacity, atlasPath, width, height, x, y, sourceWidth, sourceHeight);
    }

    static string Escape(string value) { return (value ?? "").Replace('<', '[').Replace('>', ']').Replace('\r', ' '); }
    static string ColorTag(Color value) { return "<C=" + (value.ToArgb() & 0xffffff).ToString("X6") + ">"; }

    string TextDisplay()
    {
        var text = new StringBuilder();
        if (caption != null && appearance != null && (appearance.ShowIcon || appearance.ShowNumber))
        {
            int percent = BatteryPublisher.GetPercent(caption);
            // Plain third-party OSD clients join RTSS's normal text flow. This avoids
            // changing the shared layer anchor used by other statistics clients.
            text.Append('\n');
            if (appearance.ShowIcon)
            {
                int count = appearance.SegmentedIcon ? 5 : 10;
                int filled = appearance.SegmentedIcon
                    ? percent >= 100 ? 5 : percent < 10 ? 0 : Math.Min(5, 1 + (percent - 10) / 20)
                    : Math.Max(0, Math.Min(10, (percent + 5) / 10));
                text.Append(ColorTag(percent < 0 ? StudioTheme.Muted : BatteryPublisher.BatteryColor(percent)));
                text.Append('[');
                if (appearance.NumberInsideIcon && appearance.ShowNumber) text.Append(Escape(caption));
                else text.Append(new string('|', filled)).Append(new string('.', count - filled));
                text.Append(charging ? "+]" : "]").Append("<C>");
            }
            if (appearance.ShowNumber && !(appearance.ShowIcon && appearance.NumberInsideIcon))
                text.Append(appearance.ShowIcon ? " " : "").Append(ColorTag(appearance.Text)).Append(Escape(caption)).Append("<C>");
            text.Append('\n');
        }
        if (popup != null && popupOpacity != 0)
        {
            text.Append("\n<C=")
                .Append(popupOpacity.ToString("X2")).Append("FFFFFF>").Append(Escape(popupTitle));
            if (!string.IsNullOrEmpty(popupDetail)) text.Append("\n").Append(Escape(popupDetail));
            text.Append("<C>\n");
        }
        return text.ToString();
    }

    void Flush()
    {
        try
        {
            if (CompatibleText && !displayDisconnected) { client.Write(TextDisplay()); return; }
            PrepareAtlas();
            imageError = null;
            var text = new StringBuilder();
            if (atlasPath != null)
            {
                // Bind after the layer attributes; older RTSS versions can reset the
                // active image while switching layers. Unquoted relative paths work there.
                if (badge != null) text.Append(Layer(badge, 0, badgePosition, badgeMargin, 250, 255));
                if (popup != null && popupOpacity != 0)
                {
                    int offset = badge != null && badgePosition == popupPosition ? badge.Height + 6 : 0;
                    text.Append(Layer(popup, popupCellY, popupPosition, 12, 251, popupOpacity, offset));
                }
            }
            // RTSS inserts a text separator before subsequent clients. Leave that
            // separator in a separate invisible layer so it cannot enlarge the
            // badge/popup bounds and shift a bottom-anchored image upward.
            if (text.Length != 0) text.Append("<P=0,0><L249>\r");
            client.Write(text.ToString());
        }
        catch (IOException) { imageError = "Could not save the RTSS badge image. Check available disk space."; }
        catch (UnauthorizedAccessException) { imageError = "Run this version as administrator so it can save artwork in RTSS's image folder."; }
    }

    public void Dispose()
    {
        if (desktopWidget != null) desktopWidget.Dispose();
        if (widgetBadge != null) widgetBadge.Dispose();
        client.Dispose();
        if (badge != null) badge.Dispose();
        if (popup != null) popup.Dispose();
        if (Current == this) Current = null;
    }
}

sealed class RtssTestSession : IDisposable
{
    // The same single-instance guard prevents two controller readers and competing shortcuts.
    readonly Mutex guard = new Mutex(false, @"Local\DS4VulkanBattery.AutoOwner");
    internal BatteryPublisher Publisher { get; private set; }
    internal LegacyDisplay Legacy { get { return null; } }
    readonly RtssOutput output;
    internal RtssTestSession()
    {
        bool acquired;
        try { acquired = guard.WaitOne(0); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) { guard.Dispose(); throw new InvalidOperationException(
            "Quit the other DS4 Battery Overlay from its tray icon, then open this version."); }
        try
        {
            Publisher = new BatteryPublisher(); // Private mapping: never enables standalone game renderers.
            RtssOutput.Current = output = new RtssOutput();
            using (var preferences = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(OverlayAppearance.PreferencesPath))
            {
                if (OverlayAppearance.Read(preferences, "RtssArtworkVersion", 0, 0, 1) == 0)
                {
                    preferences.SetValue("RtssTextDisplay", 0);
                    preferences.SetValue("RtssArtworkVersion", 1);
                    using (var original = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\DS4VulkanOverlay"))
                    if (original != null)
                    foreach (string name in new[] { "Position", "Margin", "Scale", "CornerRadius", "Shortcut", "ShowIcon", "SegmentedIcon",
                        "ShowNumber", "NumberInsideIcon", "ShowBackground", "Background", "Text" })
                    {
                        object value = original.GetValue(name);
                        if (value != null && preferences.GetValue(name) == null)
                            preferences.SetValue(name, value, original.GetValueKind(name));
                    }
                }
                output.CompatibleText = OverlayAppearance.Read(preferences, "RtssTextDisplay", 0, 0, 1) != 0;
            }
        }
        catch { guard.ReleaseMutex(); guard.Dispose(); throw; }
    }
    public void Dispose()
    {
        try { output.Dispose(); Publisher.Dispose(); }
        finally { guard.ReleaseMutex(); guard.Dispose(); }
    }
}

sealed class RtssPage : UserControl
{
    readonly Label status = StudioTheme.Label("", 9);
    readonly System.Windows.Forms.Timer refresh = new System.Windows.Forms.Timer { Interval = 500 };
    readonly TableLayoutPanel guide = new TableLayoutPanel {
        Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        ColumnCount = 1, RowCount = 0, Margin = Padding.Empty, Padding = new Padding(0, 0, 12, 12)
    };
    readonly System.Collections.Generic.List<Label> paragraphs = new System.Collections.Generic.List<Label>();
    bool fitting;
    internal RtssPage()
    {
        Dock = DockStyle.Fill; BackColor = StudioTheme.Canvas; AutoScroll = true;
        guide.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(guide);
        Paragraph(guide, "RivaTuner Statistics Server (RTSS) is required to show the battery overlay in games. RTSS handles game hooking and draws the battery display sent by this app. MSI Afterburner is optional.");
        status.ForeColor = StudioTheme.Muted;
        AddParagraph(guide, status);
        Heading(guide, "1  Install RTSS");
        Paragraph(guide, "Install RivaTuner Statistics Server (RTSS). You can install it separately or select it in the optional MSI Afterburner installer below.");
        var download = new LinkLabel {
            Text = "Download MSI Afterburner (official MSI website)", Font = StudioTheme.Font(10),
            LinkColor = StudioTheme.Accent, ActiveLinkColor = StudioTheme.Text,
            VisitedLinkColor = StudioTheme.Accent, BackColor = Color.Transparent,
            LinkBehavior = LinkBehavior.HoverUnderline, TabStop = true
        };
        download.LinkClicked += delegate {
            try {
                using (System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                    FileName = "https://www.msi.com/Landing/afterburner", UseShellExecute = true
                })) { }
                download.LinkVisited = true;
            }
            catch (Exception error) {
                MessageBox.Show(FindForm(), "Could not open the download page.\n\nhttps://www.msi.com/Landing/afterburner\n\n" + error.Message,
                    "MSI Afterburner download", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        };
        AddParagraph(guide, download);
        Heading(guide, "2  Install the RTSS plugin");
        Paragraph(guide, "Quit RTSS and, if installed, MSI Afterburner completely, including their tray icons, then click Install plugin. This copies DS4PCStats.dll into RTSS's Plugins\\Client folder and, if Afterburner is installed, prepares separate PC-stats shortcuts. Windows may ask for administrator permission.");
        var install = StudioTheme.Button("Install / update RTSS plugin", true);
        install.Margin = new Padding(0, 0, 0, 10);
        AddControl(install);
        var installStatus = StudioTheme.Label("Existing plugin shortcuts are kept. Previous files are backed up.", 9);
        installStatus.ForeColor = StudioTheme.Muted;
        AddParagraph(guide, installStatus);
        install.Click += async delegate {
            install.Enabled = false;
            try {
                installStatus.Text = "Waiting for the installer...";
                int result = await RtssPluginInstaller.Launch();
                if (IsDisposed) return;
                installStatus.Text = result == 0 ? "Installed. Start RTSS and enable the plugin below." : "Installation did not finish. See the installer's message.";
            }
            catch (System.ComponentModel.Win32Exception error) {
                if (IsDisposed) return;
                installStatus.Text = error.NativeErrorCode == 1223 ? "Installation cancelled." : error.Message;
            }
            catch (Exception error) { if (!IsDisposed) installStatus.Text = error.Message; }
            finally { if (!IsDisposed) install.Enabled = true; }
        };
        Heading(guide, "3  Enable it in RTSS");
        Paragraph(guide, "Start RTSS. Open Setup > Plugins, tick DS4PCStats.dll, then click OK.");
        Heading(guide, "4  Set PC-stats shortcuts (if using Afterburner)");
        Paragraph(guide, "In MSI Afterburner > Settings > On-Screen Display, clear the Toggle / Show / Hide On-Screen Display hotkeys. In RTSS > Setup > Plugins, select DS4PCStats.dll and click Setup. Assign Toggle PC stats, Show PC stats and Hide PC stats there. Keep RTSS's master Show On-Screen Display ON so it can draw the battery.");
        Paragraph(guide, "For the battery display, set RTSS's Application detection level to Low or higher, keep RTSS and this app running, and connect your controller.");
        ClientSizeChanged += delegate { FitGuide(); };
        VisibleChanged += delegate {
            if (Visible && IsHandleCreated) BeginInvoke(new Action(delegate {
                if (!IsDisposed) AutoScrollPosition = Point.Empty;
            }));
        };
        FitGuide();
        refresh.Tick += delegate { UpdateStatus(); };
        UpdateStatus(); refresh.Start();
    }
    void Heading(TableLayoutPanel parent, string text)
    {
        var label = StudioTheme.Label(text, 11, true);
        label.ForeColor = StudioTheme.Accent;
        AddParagraph(parent, label);
    }
    void Paragraph(TableLayoutPanel parent, string text) { AddParagraph(parent, StudioTheme.Label(text, 10)); }
    void AddParagraph(TableLayoutPanel parent, Label label)
    {
        label.AutoSize = true; label.UseMnemonic = false;
        label.TextAlign = ContentAlignment.TopLeft;
        label.Margin = new Padding(0, 0, 0, 14);
        paragraphs.Add(label); AddControl(label);
    }
    void AddControl(Control control)
    {
        int row = guide.RowCount++;
        guide.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        guide.Controls.Add(control, 0, row);
    }
    void FitGuide()
    {
        if (fitting) return;
        fitting = true;
        try {
            int width = Math.Max(1, ClientSize.Width - guide.Padding.Horizontal - 8);
            foreach (var label in paragraphs)
                if (label.MaximumSize.Width != width) label.MaximumSize = new Size(width, 0);
        }
        finally { fitting = false; }
    }
    void UpdateStatus() { status.Text = RtssOutput.Current == null ? "RTSS status" : RtssOutput.Current.Status; }
    protected override void Dispose(bool disposing) { if (disposing) refresh.Dispose(); base.Dispose(disposing); }
}

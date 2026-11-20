using System;
using System.Drawing;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Threading;

static class RtssDiagnostics
{
    const int EntrySize = 299520, EntryOffset = 256, Count = 4;
    static void Assert(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    static void Put(MemoryMappedViewAccessor view, long at, string text)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(text + "\0");
        view.WriteArray(at, bytes, 0, bytes.Length);
    }
    static string Read(MemoryMappedViewAccessor view, long at, int capacity)
    {
        byte[] bytes = new byte[capacity]; view.ReadArray(at, bytes, 0, bytes.Length);
        int length = Array.IndexOf(bytes, (byte)0);
        return Encoding.Default.GetString(bytes, 0, length < 0 ? capacity : length);
    }
    static long Entry(int index) { return EntryOffset + index * EntrySize; }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr window);
    static bool WidgetVisible(RtssOutput output)
    {
        var field=typeof(RtssOutput).GetField("desktopWidget",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        var widget=(System.Windows.Forms.NativeWindow)field.GetValue(output);
        return widget!=null && widget.Handle!=IntPtr.Zero && IsWindowVisible(widget.Handle);
    }

    internal static int SelfTest()
    {
        string report = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rtss-self-test.txt");
        try
        {
            string name = "Local\\DS4.RTSS.Contract." + Guid.NewGuid().ToString("N");
            string folder = Path.Combine(Path.GetTempPath(), "DS4RtssTest-" + Guid.NewGuid().ToString("N"));
            using (var mapping = MemoryMappedFile.CreateNew(name, EntryOffset + Count * EntrySize + 512))
            using (var view = mapping.CreateViewAccessor())
            {
                view.Write(0, RtssClient.Signature); view.Write(4, 0x00020014u);
                view.Write(20, EntrySize); view.Write(24, EntryOffset); view.Write(28, Count);
                int appOffset = EntryOffset + Count * EntrySize;
                view.Write(8, 512); view.Write(12, appOffset); view.Write(16, 1); view.Write(64, 0);
                view.Write(appOffset + 316, 96); view.Write(appOffset + 320, 1); view.Write(appOffset + 324, 2);
                Put(view, Entry(0) + 256, "MSIAfterburner"); Put(view, Entry(0) + 512, "GPU 55 C");
                Put(view, Entry(1) + 256, "ExistingStats"); Put(view, Entry(1) + 512, "Existing output");
                using (var client = new RtssClient(name))
                {
                    Assert(client.Write("DS4 65%"), "Could not claim a free RTSS slot.");
                    Assert(client.Zoom == 2 && client.Origin == new Point(96, 1), "RTSS zoom or origin was not read correctly.");
                    Assert(Read(view, Entry(2) + 256, 256) == RtssClient.Owner, "Incorrect slot ownership.");
                    Assert(Read(view, Entry(2) + 512, 4096) == "", "Legacy text would duplicate the modern display.");
                    Assert(Read(view, Entry(2) + 266752, 32768) == "DS4 65%", "Modern RTSS display text was omitted.");
                    Assert(Read(view, Entry(0) + 512, 4096) == "GPU 55 C" &&
                        Read(view, Entry(1) + 512, 4096) == "Existing output", "Another display was changed.");
                    int frame = view.ReadInt32(32);
                    Assert(client.Write("DS4 65%") && view.ReadInt32(32) == frame, "Unchanged output was republished.");
                    view.Write(36, 1);
                    Assert(!client.Write("busy") && view.ReadInt32(36) == 1 &&
                        Read(view, Entry(2) + 266752, 32768) == "DS4 65%", "A busy render lock was overwritten.");
                    view.Write(36, 0);
                    view.Write(68, 1234u);
                    Assert(!client.IsForegroundGame(1234),"An unhooked desktop process was treated as a game.");
                    view.Write(appOffset,1234u);view.Write(appOffset+264,0x00010000u);
                    Assert(!client.IsForegroundGame(1234),"A process without a graphics device was treated as a game.");
                    view.Write(appOffset+264,8u);
                    Assert(client.IsForegroundGame(1234) && !client.IsForegroundGame(1235), "Popup destination is incorrect.");
                    using (var output = new RtssOutput(new RtssClient(name), folder))
                    {
                        output.CompatibleText = false;
                        var appearance = new OverlayAppearance { Position = 2, CornerRadius = 8 };
                        output.Publish("100%", appearance, true);
                        string text = Read(view, Entry(2) + 266752, 32768);
                        Assert(text.StartsWith("<P8><L250>") && text.Contains("<LI=") && !text.Contains("<LI=\"") && text.Contains("<I="), "Badge image or native corner anchor was omitted.");
                        Assert(!text.Contains("<M=") && !text.Contains("<E=") && text.IndexOf("<LI=") > text.IndexOf("<L250>"),
                            "Image binding or margin compatibility is incorrect.");
                        Assert(text.Contains("<L250><S=100><A=0>") && text.EndsWith("<P=0,0><L249>\r"),
                            "Image layout can inherit another client's font size or alignment.");
                        string[] images = Directory.GetFiles(folder, "*.png");
                        Assert(images.Length == 1, "The badge atlas was not written.");
                        using (var image = new Bitmap(images[0]))
                        {
                            Assert(image.Height >= 128 && image.Width >= 256 && (image.Height & (image.Height - 1)) == 0 &&
                                (image.Width & (image.Width - 1)) == 0 && image.GetPixel(0, 0).A == 0,
                                "The badge image has incorrect dimensions or rough square corners.");
                            Assert(image.GetPixel(20, 20).A == 0 && image.GetPixel(28, 28).A > 0,
                                "The badge's rounded corner or interior was lost in the texture atlas.");
                        }
                        using (var popup = ControllerNotifications.TestImage(100, 17))
                        {
                            output.PublishPopup(popup, 128, 1);
                            text = Read(view, Entry(2) + 266752, 32768);
                            Assert(text.Contains("<P0><L251>") && text.Contains("<C=80FFFFFF>") && text.EndsWith("<P=0,0><L249>\r"), "Popup native anchor, fade, or layout isolation was omitted.");
                            output.Publish(null, appearance, false);
                            text = Read(view, Entry(2) + 266752, 32768);
                            Assert(text.Contains("<L251>") && !text.Contains("<L250>") && text.EndsWith("<P=0,0><L249>\r"), "A disconnected controller also hid its notification.");
                            output.PublishPopup(null, 0, 1);
                        }
                        output.Publish(null, appearance, false);
                        Assert(Read(view, Entry(2) + 266752, 32768) == "", "No-controller badge did not hide.");
                        appearance.ShowDisconnectedIcon = true;
                        output.Publish(null, appearance, false, true);
                        string offline = Read(view, Entry(2) + 266752, 32768);
                        Assert(offline.Contains("<L250>") && offline.Contains("<I="), "The disconnected icon was not published.");
                        frame = view.ReadInt32(32);
                        output.Publish(null, appearance, false, true);
                        Assert(view.ReadInt32(32) == frame, "An unchanged disconnected icon was republished.");
                        output.Publish(null, appearance, false);
                        Assert(Read(view, Entry(2) + 266752, 32768) == "", "The toggle could not hide the disconnected icon.");
                        for (int cycle = 0; cycle < 3; cycle++)
                        {
                            output.Publish("65%", appearance, false);
                            Assert(Read(view, Entry(2) + 266752, 32768) != offline, "Reconnecting left the disconnected artwork active.");
                            output.Publish(null, appearance, false, true);
                            Assert(Read(view, Entry(2) + 266752, 32768) == offline, "Repeated disconnections lost the icon.");
                        }
                        using (var notification = ControllerNotifications.TestImage())
                        {
                            output.PublishPopup(notification, 255, 2);
                            text = Read(view, Entry(2) + 266752, 32768);
                            Assert(text.Contains("<L250>") && text.Contains("<L251>"), "The offline icon hid a popup.");
                            output.PublishPopup(null, 0, 2);
                        }
                        appearance.ShowDisconnectedIcon = false;
                        output.Publish(null, appearance, false, true);
                        Assert(Read(view, Entry(2) + 266752, 32768) == "", "Show nothing did not hide the offline icon.");
                        foreach (int scale in new[] { 75, 100, 150 })
                        using (var disconnected = PopupControllerIcon.CreateDisconnectedBadge(new OverlayAppearance {
                            Scale = scale, ShowBackground = false }))
                        {
                            Assert(disconnected.Width == (int)Math.Round(48 * scale / 100f) &&
                                disconnected.Height == (int)Math.Round(32 * scale / 100f) && disconnected.GetPixel(0,0).A == 0,
                                "The disconnected icon lost its scale or transparent background.");
                            bool visible = false;
                            for (int y = 0; y < disconnected.Height; y++)
                            for (int x = 0; x < disconnected.Width; x++) visible |= disconnected.GetPixel(x,y).A > 0;
                            Assert(visible, "The disconnected icon is empty.");
                        }
                        Assert(OverlayPage.DisconnectedOptionsSelfTest(), "Disconnected overlay controls did not preserve their selection.");
                        appearance.ShowDesktopWidget=true;
                        appearance.Widget.Scale=200;appearance.Widget.CornerRadius=12;
                        view.Write(68,0u);
                        output.Publish("65%",appearance,false);
                        Assert(WidgetVisible(output),"Desktop widget did not appear on Windows.");
                        var widgetField=typeof(RtssOutput).GetField("widgetBadge",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                        var gameField=typeof(RtssOutput).GetField("badge",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                        Assert(((Bitmap)widgetField.GetValue(output)).Height==64&&((Bitmap)gameField.GetValue(output)).Height==32,
                            "Widget and game sizes are coupled.");
                        string gameBeforeWidgetChange=Read(view,Entry(2)+266752,32768);
                        appearance.Widget.Scale=150;
                        output.Publish("65%",appearance,false);
                        Assert(((Bitmap)widgetField.GetValue(output)).Height==48&&Read(view,Entry(2)+266752,32768)==gameBeforeWidgetChange,
                            "Changing widget size changed the game overlay.");
                        uint foreground=PopupPresentation.ForegroundProcess();
                        if (foreground!=0)
                        {
                            view.Write(68,foreground);
                            view.Write(appOffset,foreground);
                            appearance.Widget.HideInGames=false;
                            output.Publish("65%",appearance,false);
                            Assert(WidgetVisible(output),"Keep visible did not retain the widget in a game.");
                            appearance.Widget.HideInGames=true;
                            output.Publish("65%",appearance,false);
                            Assert(!WidgetVisible(output) && Read(view,Entry(2)+266752,32768).Contains("<L250>"),
                                "Game handoff left a desktop widget above the game or removed its game overlay.");
                        }
                        view.Write(68,0u);
                        output.Publish("65%",appearance,false);
                        Assert(WidgetVisible(output),"Widget did not return after leaving the game.");
                        output.Publish(null,appearance,false);
                        Assert(!WidgetVisible(output),"The overlay toggle did not hide the widget.");
                        appearance.ShowDisconnectedIcon=true;appearance.Widget.ShowDisconnectedIcon=true;
                        output.Publish(null,appearance,false,true);
                        Assert(WidgetVisible(output),"Offline controller preference was lost on the desktop.");
                        appearance.ShowDesktopWidget=false;appearance.ShowDisconnectedIcon=false;
                        output.Publish("65%",appearance,false);
                        Assert(!WidgetVisible(output) && Read(view,Entry(2)+266752,32768).Contains("<L250>"),
                            "Disabling the widget changed the game display.");
                        appearance.Margin = 96; appearance.Scale = 150;
                        for (int position = 0; position < 6; position++)
                        {
                            appearance.Position = position;
                            output.Publish("100%", appearance, true);
                            text = Read(view, Entry(2) + 266752, 32768);
                            Assert(text.StartsWith("<P" + RtssOutput.Sticky(position) + "><L250>") && text.EndsWith("<P=0,0><L249>\r"), "A screen position used game-size-dependent coordinates or leaked its layout.");
                            var crop = System.Text.RegularExpressions.Regex.Match(text, @"<I=(\d+),(\d+),(\d+),(\d+),(\d+),(\d+)>");
                            Assert(crop.Success, "A screen position omitted the image crop.");
                            int x = int.Parse(crop.Groups[3].Value), y = int.Parse(crop.Groups[4].Value);
                            int width = int.Parse(crop.Groups[5].Value), height = int.Parse(crop.Groups[6].Value);
                            int targetWidth = int.Parse(crop.Groups[1].Value), targetHeight = int.Parse(crop.Groups[2].Value);
                            string reference = text.Substring(text.IndexOf("<LI=") + 4).Split('>')[0];
                            using (var image = new Bitmap(reference))
                                Assert(x + width <= image.Width && y + height <= image.Height &&
                                    targetWidth * 2 == width && targetHeight * 2 == height,
                                    "The badge crop exceeds its texture or changes the original pixel scale.");
                        }
                        output.CompatibleText = true;
                        output.Publish("45%", appearance, true);
                        text = Read(view, Entry(2) + 266752, 32768);
                        Assert(text.Contains("45%") && text.Contains("[||...+]") && !text.Contains("<P") && !text.Contains("<L") && !text.Contains("<I="),
                            "The compatible RTSS battery meter was not published correctly.");
                        output.Publish(null, appearance, false);
                    }
                    Assert(Read(view, Entry(2) + 256, 256) == "", "Shutdown did not release the slot.");
                    Assert(Read(view, Entry(2) + 266752, 32768) == "", "Shutdown left modern display text behind.");
                    Assert(Read(view, Entry(0) + 512, 4096) == "GPU 55 C", "Shutdown changed Afterburner's output.");
                    Put(view, Entry(2) + 256, "ThirdClient"); Put(view, Entry(3) + 256, "FourthClient");
                    Assert(!client.Write("full"), "A full RTSS display table was overwritten.");
                    Put(view, Entry(2) + 256, "");
                    Assert(client.Write("recovered"), "The client did not recover when a slot became available.");
                    client.Release();
                    view.Write(20, int.MaxValue);
                    Assert(!client.Write("invalid"), "An invalid memory layout was accepted.");
                }
            }
            int[] expected = { 2, 0, 8, 6, 1, 7 };
            for (int index = 0; index < expected.Length; index++) Assert(RtssOutput.Sticky(index) == expected[index], "Incorrect anchor.");
            File.WriteAllText(report, "PASS RTSS SDK layout, dedicated slot, Afterburner coexistence, render lock, change-only updates, slot recovery, bounds validation, badge PNG, rounded edges, all six positions, popup fade, disconnected icon, reconnection cycles, toggle, transparent scaling, UI choices, desktop widget, game handoff, hidden state and cleanup.");
            return 0;
        }
        catch (Exception error) { File.WriteAllText(report, error.ToString()); return 1; }
    }

    internal static int Demo(string secondsText, string report)
    {
        try
        {
            int seconds = Math.Max(1, Math.Min(120, int.Parse(secondsText)));
            bool connected = false;
            using (var output = new RtssOutput())
            {
                var appearance = new OverlayAppearance { Position = 0, Scale = 125, CornerRadius = 8 };
                var clock = System.Diagnostics.Stopwatch.StartNew();
                while (clock.ElapsedMilliseconds < seconds * 1000)
                {
                    output.Publish("65%", appearance, false);
                    string status = output.Status;
                    connected |= status.StartsWith("RTSS connected");
                    File.WriteAllText(report, status);
                    Thread.Sleep(100);
                }
            }
            return connected ? 0 : 2;
        }
        catch (Exception error) { File.WriteAllText(report, error.ToString()); return 1; }
    }
}

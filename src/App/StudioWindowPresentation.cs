using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

static class StudioWindowPresentation
{
    [DllImport("user32.dll")] static extern bool IsWindowEnabled(IntPtr window);

    internal static void Show(StudioWindow window, Action<StudioWindow> closed)
    {
        window.FormClosed += delegate { closed(window); };
        // ShowDialog disables every existing window on this UI thread, including the widget.
        window.Show();
    }

    internal static int SelfTest(string report)
    {
        try
        {
            using (var image = PopupControllerIcon.CreateDisconnectedBadge(new OverlayAppearance()))
            using (var widget = new DesktopBatteryWidget(@"Software\DS4OverlayRTSSTest\StudioWidgetQA"))
            {
                widget.Update(image, true, false);
                if (widget.Error != null) throw new InvalidOperationException(widget.Error);
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    bool closed = false;
                    using (var window = new StudioWindow(new OverlayAppearance(), new LightbarSettings(), null,
                        value => {}, value => System.Threading.Tasks.Task.FromResult(0),
                        () => new StudioStatus(false, "", false), new ControllerEventLog(null), previewMode: true))
                    {
                        Show(window, value => closed = true);
                        Application.DoEvents();
                        if (window.Modal || !window.Visible || window.IsDisposed || !IsWindowEnabled(widget.Handle))
                            throw new InvalidOperationException("Opening settings disabled the desktop widget.");
                        window.Close();
                        Application.DoEvents();
                        if (!closed || !window.IsDisposed || !IsWindowEnabled(widget.Handle))
                            throw new InvalidOperationException("Closing settings did not release the window cleanly.");
                    }
                }
            }
            File.WriteAllText(report, "PASS settings opened, closed and reopened with an existing desktop widget; widget stayed enabled and settings windows were disposed after closing.");
            return 0;
        }
        catch (Exception error) { File.WriteAllText(report, error.ToString()); return 1; }
    }
}

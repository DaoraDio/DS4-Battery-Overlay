using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

sealed class OpenGlGamesPage : UserControl
{
    readonly List<Game> games;
    readonly bool previewMode;
    readonly ListBox list;
    readonly Panel emptyState;
    readonly Label status;
    readonly Button remove;
    readonly Font rowTitle;
    readonly GameOverlayComponent component;
    readonly string gameList;
    readonly Direct3D11AutoEnable automatic;

    public OpenGlGamesPage(string detected, bool previewMode = false, GameOverlayComponent component = null,
        Direct3D11AutoEnable automatic = null)
    {
        this.previewMode = previewMode;
        this.component = component ?? OpenGlIntegration.OpenGl;
        this.automatic = automatic;
        bool directX = this.component == OpenGlIntegration.Direct3D11;
        gameList = directX ? Direct3D11AutoEnable.GameList : "OpenGLGames";
        games = previewMode ? new List<Game>() : Game.Load(gameList);
        rowTitle = StudioTheme.Font(10, true);
        BackColor = StudioTheme.Canvas;
        ForeColor = StudioTheme.Text;
        Font = StudioTheme.Font(10);
        Dock = DockStyle.Fill;

        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = directX ? 4 : 3, Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        if (directX) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 105));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 127));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        Controls.Add(layout);
        if (directX) layout.Controls.Add(CreateAutomaticCard(), 0, 0);
        layout.Controls.Add(CreateDetectionCard(detected), 0, directX ? 1 : 0);
        int libraryRow = directX ? 2 : 1;

        var library = Card(new Padding(18, 15, 18, 13));
        library.Margin = new Padding(0, 0, 0, 10);
        var libraryLayout = new TableLayoutPanel {
            Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 1, RowCount = 2
        };
        libraryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        libraryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        libraryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        library.Controls.Add(libraryLayout);
        layout.Controls.Add(library, 0, libraryRow);

        var browse = StudioTheme.Button("Browse for executable");
        browse.Size = new Size(176, 34);
        browse.Dock = DockStyle.Right;
        browse.Click += delegate { AddGame(); };
        var libraryTitle = StudioTheme.Label("Game library", 13, true);
        libraryTitle.Dock = DockStyle.Fill;
        libraryTitle.TextAlign = ContentAlignment.MiddleLeft;
        var header = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 9) };
        header.Controls.Add(libraryTitle);
        header.Controls.Add(browse);
        libraryLayout.Controls.Add(header, 0, 0);

        var listArea = Card(new Padding(1));
        listArea.BackColor = StudioTheme.Canvas;
        listArea.Margin = Padding.Empty;
        libraryLayout.Controls.Add(listArea, 0, 1);

        list = new ListBox {
            Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 59, BackColor = StudioTheme.Canvas, ForeColor = StudioTheme.Text,
            Font = StudioTheme.Font(9), IntegralHeight = false,
            AccessibleName = "Managed " + this.component.Name + " games", DisplayMember = "Executable"
        };
        list.DrawItem += DrawGame;
        list.SelectedIndexChanged += delegate { UpdateRemove(); };
        emptyState = CreateEmptyState();
        listArea.Controls.Add(list);
        listArea.Controls.Add(emptyState);

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(footer, 0, libraryRow + 1);

        var notices = StudioTheme.Button("Third-party notices");
        notices.Size = new Size(139, 34);
        notices.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        notices.Click += delegate { ShowNotices(); };
        footer.Controls.Add(notices, 0, 0);

        status = StudioTheme.Label("", 9);
        status.ForeColor = StudioTheme.Muted;
        status.Dock = DockStyle.Fill;
        status.AutoEllipsis = true;
        status.TextAlign = ContentAlignment.MiddleLeft;
        footer.Controls.Add(status, 1, 0);

        remove = StudioTheme.Button("Remove support");
        remove.Size = new Size(126, 34);
        remove.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
        remove.Click += delegate { RemoveGame(); };
        footer.Controls.Add(remove, 2, 0);
        if (automatic != null) automatic.GamesChanged += AutomaticChanged;
        RefreshList(null);
    }

    Control CreateAutomaticCard()
    {
        var card = Card(new Padding(18, 11, 18, 11));
        card.Margin = new Padding(0, 0, 0, 10);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 156));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.Controls.Add(layout);

        var title = StudioTheme.Label("Automatically enable games", 12, true);
        title.Dock = DockStyle.Fill;
        layout.Controls.Add(title, 0, 0);
        var detail = StudioTheme.Label("", 9);
        detail.Dock = DockStyle.Fill;
        detail.AutoEllipsis = true;
        detail.ForeColor = StudioTheme.Muted;
        layout.Controls.Add(detail, 0, 1);

        var choose = StudioTheme.Button("Choose game folder");
        choose.Width = 150;
        choose.Anchor = AnchorStyles.Right;
        layout.Controls.Add(choose, 1, 0);
        layout.SetRowSpan(choose, 2);

        var stop = StudioTheme.Button("Stop");
        stop.AccessibleName = "Stop automatic game detection";
        stop.Width = 104;
        stop.Anchor = AnchorStyles.Right;
        layout.Controls.Add(stop, 2, 0);
        layout.SetRowSpan(stop, 2);

        Action refresh = delegate {
            string folder = automatic == null ? null : automatic.Folder;
            detail.Text = folder ?? "Choose a games folder to enable the overlay when detected games restart.";
            choose.Enabled = !previewMode && automatic != null;
            choose.Text = folder == null ? "Choose game folder" : "Change game folder";
            stop.Enabled = folder != null;
        };
        choose.Click += delegate {
            using (var picker = new FolderBrowserDialog {
                Description = "Choose the folder containing your DirectX 11 games. Detected games in this folder will have the overlay enabled for their next launch.",
                ShowNewFolderButton = false, SelectedPath = automatic.Folder ?? ""
            })
                if (picker.ShowDialog(this) == DialogResult.OK)
                    ChangeAutomaticFolder(picker.SelectedPath, refresh);
        };
        stop.Click += delegate { ChangeAutomaticFolder(null, refresh); };
        refresh();
        return card;
    }

    void ChangeAutomaticFolder(string folder, Action refresh)
    {
        try
        {
            automatic.SetFolder(folder);
            refresh();
            SetStatus(folder == null ? "Automatic detection stopped." : "Watching for running games in this folder.", true);
        }
        catch (Exception error) { SetStatus(error.Message, false); }
    }

    void AutomaticChanged(bool success, string message)
    {
        var selected = list.SelectedItem as Game;
        games.Clear();
        games.AddRange(Game.Load(gameList));
        RefreshList(selected == null ? null : games.Find(game => game.Executable == selected.Executable));
        SetStatus(message, success);
    }

    Control CreateDetectionCard(string detected)
    {
        var card = Card(new Padding(18, 13, 18, 13));
        card.Margin = new Padding(0, 0, 0, 10);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 53));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 169));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 41));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.Controls.Add(layout);
        var screen = new Glyph(false) { Margin = new Padding(0, 5, 9, 0), Anchor = AnchorStyles.Top | AnchorStyles.Left };
        layout.Controls.Add(screen, 0, 0);
        layout.SetRowSpan(screen, 2);

        string name = detected == null ? "Find a running game" : Path.GetFileNameWithoutExtension(detected);
        var title = StudioTheme.Label(name, 14, true);
        title.Dock = DockStyle.Fill;
        title.TextAlign = ContentAlignment.BottomLeft;
        title.AutoEllipsis = true;
        layout.Controls.Add(title, 1, 0);

        string tip = detected == null
            ? "Focus a 64-bit " + component.Name + " game, then return to this page."
            : "This app loaded " + component.Name + ". Enable support, then restart the game.";
        var detail = StudioTheme.Label(tip, 9);
        detail.ForeColor = StudioTheme.Muted;
        detail.Dock = DockStyle.Fill;
        detail.Margin = new Padding(3, 5, 7, 0);
        detail.TextAlign = ContentAlignment.TopLeft;
        layout.Controls.Add(detail, 1, 1);

        var enable = StudioTheme.Button("Enable detected game", true);
        enable.Size = new Size(159, 36);
        enable.Anchor = AnchorStyles.Right;
        enable.Enabled = detected != null && !previewMode;
        enable.Click += delegate { EnableGame(detected); };
        layout.Controls.Add(enable, 2, 0);
        layout.SetRowSpan(enable, 2);

        if (detected != null)
        {
            var tooltip = new ToolTip();
            tooltip.SetToolTip(title, detected);
            card.Disposed += delegate { tooltip.Dispose(); };
        }
        return card;
    }

    Panel CreateEmptyState()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = StudioTheme.Canvas };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        panel.Controls.Add(layout);
        layout.Controls.Add(new Glyph(true) { Anchor = AnchorStyles.None, Margin = Padding.Empty }, 0, 1);

        var empty = StudioTheme.Label("No " + component.Name + " games added", 12, true);
        empty.Dock = DockStyle.Fill;
        empty.TextAlign = ContentAlignment.MiddleCenter;
        layout.Controls.Add(empty, 0, 2);
        var hint = StudioTheme.Label("Use a detected game or browse for its executable.", 9);
        hint.ForeColor = StudioTheme.Muted;
        hint.Dock = DockStyle.Fill;
        hint.TextAlign = ContentAlignment.TopCenter;
        layout.Controls.Add(hint, 0, 3);
        return panel;
    }

    void DrawGame(object sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= list.Items.Count) return;
        var game = (Game)list.Items[e.Index];
        bool selected = (e.State & DrawItemState.Selected) != 0;
        Rectangle row = e.Bounds;
        using (var fill = new SolidBrush(selected ? StudioTheme.Card : StudioTheme.Canvas))
            e.Graphics.FillRectangle(fill, row);
        using (var separator = new Pen(StudioTheme.Border))
            e.Graphics.DrawLine(separator, row.Left + 14, row.Bottom - 1, row.Right - 14, row.Bottom - 1);
        if (selected)
            using (var line = new SolidBrush(StudioTheme.Accent))
                e.Graphics.FillRectangle(line, row.Left + 2, row.Top + 7, 3, row.Height - 14);

        var title = new Rectangle(row.Left + 17, row.Top + 8, Math.Max(1, row.Width - 33), 21);
        var path = new Rectangle(row.Left + 17, row.Top + 31, Math.Max(1, row.Width - 34), 18);
        TextRenderer.DrawText(e.Graphics, Path.GetFileNameWithoutExtension(game.Executable), rowTitle, title,
            StudioTheme.Text, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(e.Graphics, game.Executable, list.Font, path,
            StudioTheme.Muted, TextFormatFlags.SingleLine | TextFormatFlags.PathEllipsis | TextFormatFlags.NoPrefix);
        if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
    }

    void UpdateRemove()
    {
        if (remove != null) remove.Enabled = list.SelectedIndex >= 0 && !previewMode;
    }

    void RefreshList(Game selected)
    {
        list.BeginUpdate();
        try
        {
            list.Items.Clear();
            foreach (Game game in games) list.Items.Add(game);
            if (selected != null) list.SelectedItem = selected;
        }
        finally { list.EndUpdate(); }
        list.Visible = games.Count > 0;
        emptyState.Visible = games.Count == 0;
        if (games.Count == 0) emptyState.BringToFront();
        UpdateRemove();
    }

    void AddGame()
    {
        if (previewMode) return;
        using (var picker = new OpenFileDialog {
            Filter = "Game executables (*.exe)|*.exe",
            Title = "Select the " + component.Name + " game executable"
        })
            if (picker.ShowDialog(this) == DialogResult.OK) EnableGame(picker.FileName);
    }

    void EnableGame(string path)
    {
        if (previewMode) return;
        try
        {
            string executable = Path.GetFullPath(path);
            Game.Enable(executable, component, games, gameList);
            if (automatic != null) automatic.SetExcluded(executable, false);
            RefreshList(games.Find(game => game.Executable == executable));
            SetStatus(component.Name + " enabled. Restart the game in " + component.Name + " mode.", true);
        }
        catch (Exception error) { SetStatus(error.Message, false); }
    }

    void RemoveGame()
    {
        var selected = list.SelectedItem as Game;
        if (selected == null || previewMode) return;
        try
        {
            component.Remove(selected.Executable, selected.Digest);
            if (automatic != null) automatic.SetExcluded(selected.Executable, true);
            games.Remove(selected);
            RefreshList(null);
            Game.Save(games, gameList);
            SetStatus(component.Name + " support removed.", true);
        }
        catch (Exception error) { SetStatus(error.Message, false); }
    }

    void SetStatus(string message, bool success)
    {
        status.Text = message;
        status.ForeColor = success ? StudioTheme.Green : Color.FromArgb(255, 166, 166);
    }

    void ShowNotices()
    {
        using (var form = new Form { Text = "Third-party notices", ClientSize = new Size(650, 480),
            StartPosition = FormStartPosition.CenterParent, MinimizeBox = false,
            BackColor = StudioTheme.Canvas, ForeColor = StudioTheme.Text, Padding = new Padding(15) })
        using (var reader = new StreamReader(OpenGlIntegration.OpenResource("DS4OpenGLNotices")))
        {
            form.Controls.Add(new TextBox {
                Dock = DockStyle.Fill, Text = reader.ReadToEnd(), BackColor = StudioTheme.Card,
                ForeColor = StudioTheme.Text, Font = StudioTheme.Font(10), BorderStyle = BorderStyle.FixedSingle,
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical
            });
            StudioTheme.StyleWindow(form);
            form.ShowDialog(this);
        }
    }

    static Panel Card(Padding padding)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = StudioTheme.Card, Padding = padding };
        panel.Paint += delegate(object sender, PaintEventArgs e) {
            if (panel.ClientSize.Width < 2 || panel.ClientSize.Height < 2) return;
            using (var edge = new Pen(StudioTheme.Border))
                e.Graphics.DrawRectangle(edge, 0, 0, panel.ClientSize.Width - 1, panel.ClientSize.Height - 1);
        };
        return panel;
    }

    sealed class Glyph : Control
    {
        readonly bool subdued;

        public Glyph(bool subdued)
        {
            this.subdued = subdued;
            Size = new Size(42, 42);
            SetStyle(ControlStyles.UserPaint | ControlStyles.DoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var background = new SolidBrush(subdued ? StudioTheme.Card : Color.FromArgb(46, 43, 71)))
            using (var stroke = new Pen(subdued ? StudioTheme.Muted : StudioTheme.Accent, 1.6f))
            {
                var tile = new Rectangle(2, 2, 38, 38);
                e.Graphics.FillRectangle(background, tile);
                e.Graphics.DrawRectangle(stroke, 11, 13, 20, 14);
                e.Graphics.DrawLine(stroke, 21, 27, 21, 32);
                e.Graphics.DrawLine(stroke, 16, 32, 26, 32);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (automatic != null) automatic.GamesChanged -= AutomaticChanged;
            if (rowTitle != null) rowTitle.Dispose();
        }
        base.Dispose(disposing);
    }

    internal sealed class Game
    {
        public string Executable { get; private set; }
        public string Digest { get; private set; }

        public Game(string executable, string digest) { Executable = executable; Digest = digest; }

        public static bool Enable(string executable, GameOverlayComponent component, List<Game> games, string gameList)
        {
            string directory = Path.GetDirectoryName(executable);
            int index = games.FindIndex(game => string.Equals(Path.GetDirectoryName(game.Executable), directory,
                StringComparison.OrdinalIgnoreCase));
            Game prior = index < 0 ? null : games[index];
            bool installed = component.Install(executable, prior == null ? null : prior.Digest);
            if (!installed && prior != null && prior.Executable == executable && prior.Digest == component.Digest)
                return false;

            var entry = new Game(executable, component.Digest);
            if (prior == null) games.Add(entry);
            else games[index] = entry;
            try { Save(games, gameList); }
            catch
            {
                if (prior == null)
                {
                    games.Remove(entry);
                    if (installed) component.Remove(executable, entry.Digest);
                }
                else games[index] = prior;
                throw;
            }
            return true;
        }

        public static List<Game> Load(string gameList)
        {
            var games = new List<Game>();
            using (var key = Registry.CurrentUser.OpenSubKey(OverlayAppearance.PreferencesPath))
            {
                string[] items = key == null ? null : key.GetValue(gameList) as string[];
                if (items == null) return games;
                foreach (string item in items)
                {
                    int separator = item.IndexOf('\t');
                    if (separator > 0 && separator + 1 < item.Length)
                        games.Add(new Game(item.Substring(0, separator), item.Substring(separator + 1)));
                }
            }
            return games;
        }

        public static void Save(List<Game> games, string gameList)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(OverlayAppearance.PreferencesPath))
                key.SetValue(gameList, games.Select(game => game.Executable + "\t" + game.Digest).ToArray(),
                    RegistryValueKind.MultiString);
        }
    }
}

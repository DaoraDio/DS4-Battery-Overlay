using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

sealed class GameLibraryPage : UserControl
{
    readonly GameLibrary library;
    readonly CheckBox automatic;
    readonly ListView list;
    readonly TableLayoutPanel tableHeader;
    readonly Button dllOptions, enable, disable;
    readonly Label status;
    readonly ContextMenuStrip gameMenu;
    readonly Dictionary<string, Bitmap> gameIcons = new Dictionary<string, Bitmap>(GameDiscovery.Paths);
    readonly Bitmap defaultGameIcon = SystemIcons.Application.ToBitmap();
    bool refreshing;

    internal GameLibraryPage(GameLibrary library)
    {
        this.library = library;
        Dock = DockStyle.Fill;
        BackColor = StudioTheme.Canvas;
        Font = StudioTheme.Font(9);

        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        StudioTheme.FillRow(layout, 50);
        StudioTheme.FillRow(layout, 48);
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        StudioTheme.FillRow(layout, 42);
        StudioTheme.FillRow(layout, 42);
        Controls.Add(layout);

        var tools = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = Padding.Empty };
        tools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 144));
        tools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 111));
        layout.Controls.Add(tools, 0, 0);
        automatic = new CheckBox {
            Text = "Automatically enable overlays", AutoSize = true, Anchor = AnchorStyles.Left,
            ForeColor = StudioTheme.Text, Font = StudioTheme.Font(10, true), Checked = true
        };
        automatic.CheckedChanged += delegate {
            if (!refreshing && library != null) Run(() => library.SetAutomatic(automatic.Checked));
        };
        tools.Controls.Add(automatic, 0, 0);
        AddButton(tools, 1, "Library folders…", () => ChooseFolders());
        AddButton(tools, 2, "Rescan", () => library.Refresh());
        foreach (Control control in tools.Controls) control.Enabled = library != null;

        var setup = StudioTheme.Label(
            "Vulkan loads the overlay automatically. DirectX and OpenGL load a small DLL from the game folder at launch. " +
            "Leave automatic setup on or select Enable game. If you see Restart game, close and reopen it.", 9);
        setup.Dock = DockStyle.Fill;
        setup.ForeColor = StudioTheme.Muted;
        setup.Margin = new Padding(4, 0, 4, 6);
        layout.Controls.Add(setup, 0, 1);

        list = new ListView {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false,
            MultiSelect = false, OwnerDraw = true, ShowItemToolTips = true,
            BackColor = StudioTheme.Card, ForeColor = StudioTheme.Text,
            BorderStyle = BorderStyle.None, Margin = Padding.Empty, HeaderStyle = ColumnHeaderStyle.None
        };
        list.Columns.Add("Game");
        list.Columns.Add("Graphics API");
        list.Columns.Add("Status");
        list.DrawSubItem += delegate(object sender, DrawListViewSubItemEventArgs args) {
            bool selected = args.Item.Selected;
            using (var fill = new SolidBrush(selected ? StudioTheme.Selection : StudioTheme.Card))
                args.Graphics.FillRectangle(fill, args.Bounds);
            Color color = StudioTheme.Text;
            var entry = args.Item.Tag as GameLibrary.Entry;
            if (args.ColumnIndex == 1 && entry != null && entry.RendererConfirmed) color = StudioTheme.Green;
            if (args.ColumnIndex == 2)
            {
                if (args.SubItem.Text == "Ready" || args.SubItem.Text == "Vulkan automatic") color = StudioTheme.Green;
                else if (args.SubItem.Text == "Restart game" || args.SubItem.Text == "DLL conflict" ||
                    args.SubItem.Text == "Removal pending") color = StudioTheme.Warning;
                else color = StudioTheme.Muted;
            }
            Rectangle text = args.Bounds;
            if (args.ColumnIndex == 0)
            {
                var icon = GameIcon(entry == null ? null : entry.IconExecutable);
                int size = Math.Min(16, text.Height - 2);
                if (size > 0 && text.Width > 28)
                    args.Graphics.DrawImage(icon, new Rectangle(text.Left + 9, text.Top + (text.Height - size) / 2, size, size));
                text.Width -= 23;
                text.X += 23;
            }
            DrawCell(args.Graphics, args.SubItem.Text, text, color);
        };
        gameMenu = new ContextMenuStrip {
            BackColor = StudioTheme.Card, ForeColor = StudioTheme.Text, Font = StudioTheme.Font(9),
            ShowImageMargin = false, Renderer = new ToolStripProfessionalRenderer(new StudioTrayColors())
        };
        gameMenu.Items.Add("Open game folder", null, delegate { Run(OpenSelectedFolder); });
        gameMenu.Items.Add("Overlay DLLs…", null, delegate { Run(ChooseDlls); });
        var forgetRenderer = gameMenu.Items.Add("Forget confirmed API", null, delegate {
            Run(() => library.ForgetRenderer(Selected()));
        });
        gameMenu.Opening += delegate(object sender, System.ComponentModel.CancelEventArgs e) {
            var entry = Selected();
            e.Cancel = entry == null;
            forgetRenderer.Visible = entry != null && entry.RendererRemembered;
        };
        list.ContextMenuStrip = gameMenu;
        list.MouseDown += delegate(object sender, MouseEventArgs e) {
            if (e.Button != MouseButtons.Right) return;
            var item = list.GetItemAt(e.X, e.Y);
            if (item == null) list.SelectedItems.Clear();
            else { item.Selected = true; item.Focused = true; }
        };
        list.SizeChanged += delegate { ResizeColumns(); };
        list.SelectedIndexChanged += delegate { if (!refreshing) UpdateActions(); };
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1,
            Margin = Padding.Empty, Padding = new Padding(1), BackColor = StudioTheme.Border };
        StudioTheme.FillRow(table, 26);
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        tableHeader = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 1, ColumnCount = 3,
            Margin = Padding.Empty, BackColor = StudioTheme.Rail };
        for (int i = 0; i < list.Columns.Count; ++i)
        {
            tableHeader.ColumnStyles.Add(i == 2 ? new ColumnStyle(SizeType.Percent, 100) : new ColumnStyle(SizeType.Absolute));
            var label = StudioTheme.Label(list.Columns[i].Text, 9);
            label.Dock = DockStyle.Fill;
            label.Margin = Padding.Empty;
            label.Padding = new Padding(8, 0, 0, 0);
            label.ForeColor = StudioTheme.Muted;
            tableHeader.Controls.Add(label, i, 0);
        }
        table.Controls.Add(tableHeader, 0, 0);
        table.Controls.Add(list, 0, 1);
        layout.Controls.Add(table, 0, 2);

        status = StudioTheme.Label("", 9);
        status.ForeColor = StudioTheme.Muted;
        status.AutoEllipsis = true;
        status.Dock = DockStyle.Fill;
        layout.Controls.Add(status, 0, 3);

        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Margin = Padding.Empty };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
        layout.Controls.Add(actions, 0, 4);
        AddButton(actions, 0, "Third-party notices", ShowNotices);
        actions.Controls[0].Anchor = AnchorStyles.Left;
        dllOptions = AddButton(actions, 1, "Overlay DLLs…", ChooseDlls);
        enable = AddButton(actions, 2, "Enable game", () => library.SetGameEnabled(Selected(), true));
        disable = AddButton(actions, 3, "Disable game", () => library.SetGameEnabled(Selected(), false));

        if (library == null)
        {
            list.Items.Add(new ListViewItem(new[] { "Example game", "DirectX 12", "Ready" }));
            list.Items.Add(new ListViewItem(new[] { "Example OpenGL game", "OpenGL", "Restart game" }));
            list.Items.Add(new ListViewItem(new[] { "Example Vulkan game", "Vulkan", "Vulkan automatic" }));
            status.Text = "Steam libraries are discovered automatically. Add other libraries under Library folders.";
            UpdateActions();
        }
        else
        {
            library.Changed += RefreshEntries;
            RefreshEntries();
        }
        ResizeColumns();
    }

    Button AddButton(TableLayoutPanel layout, int column, string title, Action action)
    {
        var button = StudioTheme.Button(title);
        button.Anchor = AnchorStyles.Right;
        button.Margin = new Padding(4, 2, 0, 2);
        button.Click += delegate { Run(action); };
        layout.Controls.Add(button, column, 0);
        return button;
    }

    void Run(Action action)
    {
        try { action(); }
        catch (Exception error) { status.Text = error.Message; }
    }

    GameLibrary.Entry Selected()
    {
        return list.SelectedItems.Count == 0 ? null : list.SelectedItems[0].Tag as GameLibrary.Entry;
    }

    void OpenSelectedFolder()
    {
        var entry = Selected();
        if (entry == null) return;
        string folder = entry.Game.Folder;
        if (!Directory.Exists(folder)) throw new IOException("This game folder is no longer available.");
        using (Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true })) { }
    }

    void ChooseDlls()
    {
        var entry = Selected();
        if (entry == null || library == null) return;
        using (var dialog = new Form {
            Text = "Overlay DLLs", ClientSize = new Size(460, 303), StartPosition = FormStartPosition.CenterParent,
            BackColor = StudioTheme.Canvas, ForeColor = StudioTheme.Text, Font = StudioTheme.Font(10),
            FormBorderStyle = FormBorderStyle.FixedDialog, ShowInTaskbar = false,
            MinimizeBox = false, MaximizeBox = false, AutoScaleMode = AutoScaleMode.Dpi,
            AutoScaleDimensions = new SizeF(96, 96), Padding = new Padding(16, 10, 16, 12)
        })
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, Margin = Padding.Empty };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            StudioTheme.FillRow(layout, 36);
            StudioTheme.FillRow(layout, 33);
            var heading = StudioTheme.Label(entry.Name, 13, true);
            heading.Dock = DockStyle.Fill;
            heading.AutoEllipsis = true;
            layout.Controls.Add(heading, 0, 0);
            var detected = DllCheckBox("Choose the overlay DLL automatically", !entry.DllSelection.HasValue);
            layout.Controls.Add(detected, 0, 1);
            var choices = new Dictionary<GraphicsApi, CheckBox>();
            int row = 2;
            foreach (var api in GameDiscovery.LocalApis)
            {
                var component = GameDiscovery.Component(api);
                var choice = DllCheckBox(component.Name + "  (" + component.FileName + ")",
                    ((entry.DllSelection ?? entry.AutomaticDlls) & api) != 0);
                choice.Margin = new Padding(14, 0, 0, 0);
                choices.Add(api, choice);
                StudioTheme.FillRow(layout, 29);
                layout.Controls.Add(choice, 0, row++);
            }

            Action updateChoices = () => {
                foreach (var choice in choices.Values) choice.Enabled = !detected.Checked;
            };
            detected.CheckedChanged += delegate { updateChoices(); };
            updateChoices();

            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var hint = StudioTheme.Label("Close the game before testing a new selection. Vulkan loads automatically.", 9);
            hint.ForeColor = StudioTheme.Muted;
            hint.Dock = DockStyle.Fill;
            layout.Controls.Add(hint, 0, row++);
            StudioTheme.FillRow(layout, 39);
            var buttons = new FlowLayoutPanel {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = Padding.Empty, WrapContents = false
            };
            var save = StudioTheme.Button("Save changes", true);
            save.DialogResult = DialogResult.OK;
            var cancel = StudioTheme.Button("Cancel");
            cancel.DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(save);
            buttons.Controls.Add(cancel);
            layout.Controls.Add(buttons, 0, row);
            dialog.Controls.Add(layout);
            dialog.AcceptButton = save;
            dialog.CancelButton = cancel;
            StudioTheme.StyleWindow(dialog);
            if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

            GraphicsApi selected = GraphicsApi.None;
            foreach (var choice in choices)
                if (choice.Value.Checked) selected |= choice.Key;
            library.SetDllSelection(entry, detected.Checked ? (GraphicsApi?)null : selected);
        }
    }

    static CheckBox DllCheckBox(string text, bool selected)
    {
        return new CheckBox { Text = text, Checked = selected, Dock = DockStyle.Fill, Margin = Padding.Empty,
            ForeColor = StudioTheme.Text };
    }

    Bitmap GameIcon(string executable)
    {
        if (string.IsNullOrEmpty(executable)) return defaultGameIcon;
        Bitmap bitmap;
        if (!gameIcons.TryGetValue(executable, out bitmap))
        {
            if (File.Exists(executable))
                try
                {
                    using (var icon = Icon.ExtractAssociatedIcon(executable))
                        if (icon != null) bitmap = icon.ToBitmap();
                }
                catch (ArgumentException) { }
                catch (IOException) { }
                catch (System.Runtime.InteropServices.ExternalException) { }
            gameIcons.Add(executable, bitmap);
        }
        return bitmap ?? defaultGameIcon;
    }

    void RefreshEntries()
    {
        string previous = Selected() == null ? null : Selected().Game.Folder;
        var topEntry = list.TopItem == null ? null : list.TopItem.Tag as GameLibrary.Entry;
        ListViewItem top = null;
        refreshing = true;
        list.BeginUpdate();
        try
        {
            automatic.Checked = library.Automatic;
            list.Items.Clear();
            foreach (GameLibrary.Entry entry in library.Entries)
            {
                var row = new ListViewItem(new[] { entry.Name, entry.Apis, entry.Status }) {
                    Tag = entry, ToolTipText = RendererDetail(entry) + entry.Game.Folder
                };
                list.Items.Add(row);
                if (GameDiscovery.Paths.Equals(entry.Game.Folder, previous)) row.Selected = true;
                if (topEntry != null && GameDiscovery.Paths.Equals(entry.Game.Folder, topEntry.Game.Folder)) top = row;
            }
            if (top != null) list.TopItem = top;
        }
        finally
        {
            list.EndUpdate();
            refreshing = false;
        }
        ResizeColumns();
        UpdateActions();
    }

    void UpdateActions()
    {
        var entry = Selected();
        dllOptions.Enabled = library != null && entry != null;
        enable.Enabled = entry != null && entry.CanEnable;
        disable.Enabled = entry != null && entry.CanDisable;
        if (library == null) return;
        status.Text = entry != null && !string.IsNullOrEmpty(entry.Detail) ? entry.Detail :
            entry != null && entry.Status == "Restart game" ? "Restart this game once to load the overlay." :
            entry != null && entry.Status == "Ready" ? "Overlay files are installed. The game loads them at launch." :
            !string.IsNullOrEmpty(library.ScanStatus) ? library.ScanStatus :
            list.Items.Count == 0 ? "No games found. Add a library folder to get started." :
            "Steam libraries are discovered automatically. Add other libraries under Library folders.";
        if (entry != null) status.Text = RendererDetail(entry) + status.Text;
    }

    static string RendererDetail(GameLibrary.Entry entry)
    {
        if (entry.Apis == "Unknown") return "";
        string label = entry.RendererConfirmed ? "Confirmed in game: " :
            entry.RendererRemembered ? "Previously confirmed: " : "Detected libraries: ";
        return label + entry.Apis + ". ";
    }

    void ResizeColumns()
    {
        int width = list.ClientSize.Width;
        list.Columns[0].Width = Math.Max(55, width * 44 / 100);
        list.Columns[1].Width = Math.Max(55, width * 29 / 100);
        list.Columns[2].Width = Math.Max(55, width - list.Columns[0].Width - list.Columns[1].Width);
        tableHeader.ColumnStyles[0].Width = list.Columns[0].Width;
        tableHeader.ColumnStyles[1].Width = list.Columns[1].Width;
    }

    void DrawCell(Graphics graphics, string text, Rectangle bounds, Color color)
    {
        if (bounds.Width <= 14) return;
        bounds.Inflate(-7, 0);
        TextRenderer.DrawText(graphics, text, Font, bounds, color,
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    void ChooseFolders()
    {
        using (var dialog = new Form {
            Text = "Game library folders", ClientSize = new Size(650, 308), StartPosition = FormStartPosition.CenterParent,
            BackColor = StudioTheme.Canvas, ForeColor = StudioTheme.Text, Padding = new Padding(14),
            MinimizeBox = false, MaximizeBox = false
        })
        {
            var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            StudioTheme.FillRow(content, 42);
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            StudioTheme.FillRow(content, 45);
            dialog.Controls.Add(content);
            var hint = StudioTheme.Label("Steam libraries are included automatically. Add folders for games from other launchers.", 9);
            hint.Dock = DockStyle.Fill;
            content.Controls.Add(hint, 0, 0);
            var folders = new ListBox { Dock = DockStyle.Fill, BackColor = StudioTheme.Card, ForeColor = StudioTheme.Text };
            content.Controls.Add(folders, 0, 1);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            content.Controls.Add(buttons, 0, 2);
            var close = StudioTheme.Button("Done");
            close.DialogResult = DialogResult.OK;
            buttons.Controls.Add(close);
            var remove = StudioTheme.Button("Remove");
            buttons.Controls.Add(remove);
            var add = StudioTheme.Button("Add folder…", true);
            buttons.Controls.Add(add);
            Action refresh = () => {
                folders.Items.Clear();
                folders.Items.AddRange(library.CustomFolders);
                remove.Enabled = false;
            };
            folders.SelectedIndexChanged += delegate { remove.Enabled = folders.SelectedItem != null; };
            remove.Click += delegate {
                try { library.SetFolder((string)folders.SelectedItem, false); refresh(); }
                catch (Exception error) { MessageBox.Show(dialog, error.Message, dialog.Text); }
            };
            add.Click += delegate {
                using (var picker = new FolderBrowserDialog { Description = "Choose a folder containing your games.", ShowNewFolderButton = false })
                    if (picker.ShowDialog(dialog) == DialogResult.OK)
                    {
                        try { library.SetFolder(picker.SelectedPath, true); refresh(); }
                        catch (Exception error) { MessageBox.Show(dialog, error.Message, dialog.Text); }
                    }
            };
            refresh();
            dialog.AcceptButton = close;
            StudioTheme.StyleWindow(dialog);
            dialog.ShowDialog(this);
        }
    }

    void ShowNotices()
    {
        using (var form = new Form { Text = "Third-party notices", ClientSize = new Size(630, 440),
            StartPosition = FormStartPosition.CenterParent, BackColor = StudioTheme.Canvas, Padding = new Padding(14) })
        using (var reader = new System.IO.StreamReader(OpenGlIntegration.OpenResource("DS4OpenGLNotices")))
        {
            form.Controls.Add(new TextBox {
                Dock = DockStyle.Fill, Text = reader.ReadToEnd(), BackColor = StudioTheme.Card,
                ForeColor = StudioTheme.Text, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical
            });
            StudioTheme.StyleWindow(form);
            form.ShowDialog(this);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (library != null) library.Changed -= RefreshEntries;
            gameMenu.Dispose();
            foreach (var bitmap in gameIcons.Values)
                if (bitmap != null) bitmap.Dispose();
            defaultGameIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}

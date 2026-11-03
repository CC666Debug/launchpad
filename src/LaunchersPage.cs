using System.Diagnostics;
using System.Text;

namespace LaunchPad;

/// <summary>Drag things in, save one Desktop shortcut + .exe that opens them all.</summary>
class LaunchersPage : UserControl
{
    readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.None, AllowDrop = true };
    readonly ImageList _images = new() { ImageSize = new Size(32, 32), ColorDepth = ColorDepth.Depth32Bit };
    readonly TextBox _link = new() { Dock = DockStyle.Fill, PlaceholderText = "https://... or a program path, then Add" };
    readonly PictureBox _iconBox = new() { Size = new Size(36, 36), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(14, 3, 2, 3), Cursor = Cursors.Hand };
    readonly ToolTip _tip = new();
    string _iconLoc;     // chosen "file,index"; null = automatic (first item's icon)
    string _lastSaved;   // Desktop .lnk this list came from / was saved to

    public LaunchersPage()
    {
        Dock = DockStyle.Fill;
        _list.SmallImageList = _images;
        _list.Columns.Add("Name", 240); _list.Columns.Add("Path", 520);
        Theme.HookListView(_list);

        var hint = new Label { Text = "Drag icons, files, folders, shortcuts or web links here. Or paste a link below.", Dock = DockStyle.Top, Height = 30, Padding = new Padding(8, 8, 8, 0), Tag = "dim" };

        var linkRow = new Panel { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(6) };
        var add = Theme.MakeButton("Add", 70); add.Dock = DockStyle.Right;
        linkRow.Controls.Add(_link); linkRow.Controls.Add(add);

        var row = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(4), WrapContents = false };
        var up = Theme.MakeButton("Up", 50); var down = Theme.MakeButton("Down", 60); var remove = Theme.MakeButton("Remove");
        var test = Theme.MakeButton("Test run", 85); var newB = Theme.MakeButton("New", 60); var open = Theme.MakeButton("Open", 65);
        var icon = Theme.MakeButton("Icon...", 75); var save = Theme.MakeButton("Save", 80);
        row.Controls.AddRange(new Control[] { up, down, remove, test, newB, open, _iconBox, icon, save });

        Controls.Add(_list); Controls.Add(hint); Controls.Add(linkRow); Controls.Add(row);

        _tip.SetToolTip(_iconBox, "Click to go back to the first item's icon");
        _tip.SetToolTip(newB, "Start a new, empty list");

        foreach (Control c in new Control[] { this, _list })
        {
            c.AllowDrop = true;
            c.DragEnter += (s, e) =>
            {
                if (e.Data.GetDataPresent(typeof(ListViewItem))) e.Effect = DragDropEffects.Move;
                else if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Text)) e.Effect = DragDropEffects.Copy;
            };
            c.DragDrop += (s, e) =>
            {
                if (e.Data.GetData(typeof(ListViewItem)) is ListViewItem moving) DropMove(moving, _list.PointToClient(new Point(e.X, e.Y)));
                else OnDrop(e.Data);
            };
        }
        // drag items within the list to rearrange them
        _list.ItemDrag += (s, e) => { if (e.Item is ListViewItem it) _list.DoDragDrop(it, DragDropEffects.Move); };
        _list.DragOver += (s, e) =>
        {
            if (!e.Data.GetDataPresent(typeof(ListViewItem))) return;
            e.Effect = DragDropEffects.Move;
            var p = _list.PointToClient(new Point(e.X, e.Y));
            int at = _list.InsertionMark.NearestIndex(p);
            if (at >= 0) { var b = _list.GetItemRect(at); _list.InsertionMark.AppearsAfterItem = p.Y > b.Top + b.Height / 2; }
            _list.InsertionMark.Index = at;
        };
        _list.DragLeave += (s, e) => _list.InsertionMark.Index = -1;
        add.Click += (s, e) => { AddItem(_link.Text); _link.Clear(); };
        _link.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { add.PerformClick(); e.SuppressKeyPress = true; } };
        remove.Click += (s, e) => RemoveSelected();
        _list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) RemoveSelected(); };
        up.Click += (s, e) => MoveItem(-1);
        down.Click += (s, e) => MoveItem(1);
        test.Click += (s, e) => { foreach (ListViewItem i in _list.Items) Try(() => Process.Start(new ProcessStartInfo((string)i.Tag) { UseShellExecute = true })); };
        newB.Click += (s, e) => { _list.Items.Clear(); _iconLoc = null; _lastSaved = null; UpdatePreview(); };
        icon.Click += (s, e) => PickIcon();
        _iconBox.Click += (s, e) => { _iconLoc = null; UpdatePreview(); };
        save.Click += (s, e) => Save();
        open.Click += (s, e) => OpenFile();
        UpdatePreview();
    }

    static void Try(Action a) { try { a(); } catch { } }

    void DropMove(ListViewItem moving, Point p)
    {
        int at = _list.InsertionMark.Index;
        bool after = _list.InsertionMark.AppearsAfterItem;
        _list.InsertionMark.Index = -1;
        if (at < 0) at = _list.Items.Count - 1;
        if (after) at++;
        int from = moving.Index;
        if (from < at) at--;
        if (at == from) return;
        _list.Items.RemoveAt(from);
        _list.Items.Insert(Math.Clamp(at, 0, _list.Items.Count), moving);
        moving.Selected = true;
        UpdatePreview();
    }

    void OnDrop(IDataObject d)
    {
        if (d.GetDataPresent(DataFormats.FileDrop)) foreach (var f in (string[])d.GetData(DataFormats.FileDrop)) AddItem(f);
        else if (d.GetDataPresent(DataFormats.Text)) foreach (var l in ((string)d.GetData(DataFormats.Text)).Split('\n')) AddItem(l);
    }

    public void AddItem(string p)
    {
        p = (p ?? "").Trim().Trim('"');
        if (p == "") return;
        foreach (ListViewItem it in _list.Items) if ((string)it.Tag == p) return;
        var name = Icons.IsUrl(p) ? p : Path.GetFileNameWithoutExtension(p.TrimEnd('\\'));
        if (string.IsNullOrEmpty(name)) name = p;
        var key = "k" + _images.Images.Count;
        var bmp = Icons.Shell32(p);
        if (bmp != null) _images.Images.Add(key, bmp);
        var item = new ListViewItem(name, key) { Tag = p };
        item.SubItems.Add(p);
        _list.Items.Add(item);
        UpdatePreview();
    }

    void RemoveSelected()
    {
        foreach (ListViewItem i in _list.SelectedItems) _list.Items.Remove(i);
        if (_list.Items.Count == 0) { _lastSaved = null; _iconLoc = null; }
        UpdatePreview();
    }

    void MoveItem(int d)
    {
        if (_list.SelectedItems.Count != 1) return;
        var item = _list.SelectedItems[0]; int i = item.Index, j = i + d;
        if (j < 0 || j >= _list.Items.Count) return;
        _list.Items.RemoveAt(i); _list.Items.Insert(j, item);
        item.Selected = true; item.Focused = true; _list.Focus();
        UpdatePreview();
    }

    string CurrentIcon()
    {
        if (_iconLoc != null) return _iconLoc;
        foreach (ListViewItem it in _list.Items) { var l = Icons.DefaultLocation((string)it.Tag); if (l != null) return l; }
        return Icons.Fallback;
    }

    void UpdatePreview()
    {
        var old = _iconBox.Image;
        _iconBox.Image = Icons.FromLocation(CurrentIcon(), 32);
        old?.Dispose();
    }

    void PickIcon()
    {
        var loc = CurrentIcon();
        int k = loc.LastIndexOf(',');
        var sb = new StringBuilder(1024);
        sb.Append(k >= 0 ? loc[..k] : loc);
        int idx = 0; if (k >= 0) int.TryParse(loc[(k + 1)..], out idx);
        if (Native.PickIconDlg(FindForm().Handle, sb, 1024, ref idx) != 0)
        {
            _iconLoc = Environment.ExpandEnvironmentVariables(sb.ToString()) + "," + idx;
            UpdatePreview();
        }
    }

    string Shorthand()
    {
        var parts = new List<string>();
        foreach (ListViewItem it in _list.Items)
        {
            var n = System.Text.RegularExpressions.Regex.Replace(it.Text, @"^https?://(www\.)?", "");
            foreach (var c in Path.GetInvalidFileNameChars()) n = n.Replace(c.ToString(), "");
            if (n.Length > 0) parts.Add(n[..Math.Min(3, n.Length)]);
        }
        return string.Join("·", parts);
    }

    void Save()
    {
        if (_list.Items.Count == 0) { MessageBox.Show("Nothing to save yet. Drag some things in first.", "LaunchPad"); return; }
        using var dlg = new SaveFileDialog
        {
            Filter = "Desktop shortcut (*.lnk)|*.lnk",
            InitialDirectory = _lastSaved != null ? Path.GetDirectoryName(_lastSaved) : Paths.Desktop,
            FileName = _lastSaved != null ? Path.GetFileName(_lastSaved) : Shorthand() + ".lnk"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        UseWaitCursor = true;
        var exe = Path.Combine(Paths.Launchers, Path.GetFileNameWithoutExtension(dlg.FileName) + ".exe");
        var items = _list.Items.Cast<ListViewItem>().Select(i => (string)i.Tag).ToList();
        var names = _list.Items.Cast<ListViewItem>().Select(i => i.Text).ToList();
        var err = LauncherBuilder.Build(exe, items, names, CurrentIcon(), _iconLoc != null);
        UseWaitCursor = false;
        if (err != null) { MessageBox.Show(err, "LaunchPad"); return; }
        Shortcut.Write(dlg.FileName, exe, "", Paths.Launchers, exe + ",0", "Opens: " + string.Join(", ", names));
        _lastSaved = dlg.FileName;
        MessageBox.Show($"Saved:\n{dlg.FileName}\n\nDouble-click it to open everything at once.", "LaunchPad");
    }

    void OpenFile()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Launcher (*.lnk;*.exe;*.vbs;*.bat)|*.lnk;*.exe;*.vbs;*.bat",
            InitialDirectory = Paths.Desktop, DereferenceLinks = false
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        LoadFile(dlg.FileName);
    }

    public void LoadFile(string file)
    {
        var picked = file;
        string iconLoc = null;
        if (file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var s = Shortcut.Read(file);
            if (s.Target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) file = s.Target;
            else if (System.Text.RegularExpressions.Regex.Match(s.Arguments ?? "", "\"([^\"]+\\.vbs)\"") is { Success: true } m) { file = m.Groups[1].Value; iconLoc = s.IconLocation; }
            else { MessageBox.Show("That shortcut wasn't made by LaunchPad.", "LaunchPad"); return; }
        }
        var items = new List<string>();
        if (file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            var data = LauncherBuilder.Read(file);
            if (data == null) { MessageBox.Show("That program wasn't made by LaunchPad.", "LaunchPad"); return; }
            items.AddRange(data.items);
            iconLoc = data.custom ? data.icon : null;
        }
        else
        {
            foreach (var l in File.ReadAllLines(file))
            {
                var m1 = System.Text.RegularExpressions.Regex.Match(l, "^sh\\.ShellExecute\\s+\"(.*)\"\\s*$");
                var m2 = System.Text.RegularExpressions.Regex.Match(l, "^start\\s+\"\"\\s+\"(.*)\"\\s*$");
                if (m1.Success) items.Add(m1.Groups[1].Value.Replace("\"\"", "\""));
                else if (m2.Success) items.Add(m2.Groups[1].Value.Replace("%%", "%"));
            }
        }
        _list.Items.Clear();
        foreach (var i in items) AddItem(i);
        _iconLoc = iconLoc;
        _lastSaved = picked.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? picked
            : Path.Combine(Paths.Desktop, Path.GetFileNameWithoutExtension(file) + ".lnk");
        UpdatePreview();
    }
}

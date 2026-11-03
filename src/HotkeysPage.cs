using System.Diagnostics;

namespace LaunchPad;

class HotkeysPage : UserControl
{
    readonly HotkeyStore _store;
    readonly HotkeyManager _mgr;
    readonly Action _changed;   // save + re-register
    readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = true, ShowGroups = true };
    readonly ImageList _images = new() { ImageSize = new Size(20, 20), ColorDepth = ColorDepth.Depth32Bit };
    readonly TextBox _search = new() { Width = 170, PlaceholderText = "Search", Margin = new Padding(3, 8, 3, 3) };
    readonly ComboBox _cat = new() { Width = 130, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(3, 8, 3, 3) };
    readonly Button _pause = Theme.MakeButton("Pause all", 90);
    readonly Label _summary = new() { Dock = DockStyle.Bottom, Height = 26, Padding = new Padding(8, 4, 8, 0), Tag = "dim" };
    readonly Dictionary<string, string> _iconKeys = new();

    public HotkeysPage(HotkeyStore store, HotkeyManager mgr, Action changed)
    {
        _store = store; _mgr = mgr; _changed = changed;
        Dock = DockStyle.Fill;
        _list.SmallImageList = _images;
        _list.Columns.Add("Name", 220); _list.Columns.Add("Hotkey", 170); _list.Columns.Add("Does", 320); _list.Columns.Add("Status", 170);
        Theme.HookListView(_list);
        // last column fills the rest, so there's no unpainted strip at the end of the header
        _list.ClientSizeChanged += (s, e) =>
        {
            int used = _list.Columns[0].Width + _list.Columns[1].Width + _list.Columns[2].Width;
            _list.Columns[3].Width = Math.Max(140, _list.ClientSize.Width - used);
        };

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4), WrapContents = true };
        var add = Theme.MakeButton("Add", 60); var edit = Theme.MakeButton("Edit", 60); var del = Theme.MakeButton("Delete", 70);
        var cats = Theme.MakeButton("Categories...", 110); var sheet = Theme.MakeButton("Cheat sheet", 100); var imp = Theme.MakeButton("Import...", 85); var exp = Theme.MakeButton("Export...", 85);
        var up = Theme.MakeButton("Up", 50); var down = Theme.MakeButton("Down", 60);
        bar.Controls.AddRange(new Control[] { _search, _cat, add, edit, del, up, down, cats, sheet, imp, exp, _pause });
        up.Click += (s, e) => MoveSel(-1);
        down.Click += (s, e) => MoveSel(1);

        // drag hotkeys to rearrange; dropping on another category's hotkey moves it into that category
        _list.AllowDrop = true;
        _list.ItemDrag += (s, e) => _list.DoDragDrop(new DragIds(_list.SelectedItems.Cast<ListViewItem>().Select(i => (string)i.Tag).ToList()), DragDropEffects.Move);
        _list.DragOver += (s, e) =>
        {
            e.Effect = e.Data.GetDataPresent(typeof(DragIds)) ? DragDropEffects.Move
                     : e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Text) ? DragDropEffects.Copy : DragDropEffects.None;
            var p = _list.PointToClient(new Point(e.X, e.Y));
            var it = _list.GetItemAt(p.X, p.Y);
            foreach (ListViewItem i in _list.Items) i.Focused = i == it;
        };
        _list.DragDrop += (s, e) =>
        {
            // a file, shortcut, folder or web link dropped in: make a hotkey that opens it (like Keyboard Launchpad did)
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) { var f = ((string[])e.Data.GetData(DataFormats.FileDrop))[0]; AddWith(new HotAction { Kind = ActionKind.Open, Target = f }, Path.GetFileNameWithoutExtension(f.TrimEnd('\\'))); return; }
            if (e.Data.GetDataPresent(DataFormats.Text)) { var t = ((string)e.Data.GetData(DataFormats.Text)).Trim(); if (Icons.IsUrl(t)) AddWith(new HotAction { Kind = ActionKind.Open, Target = t }, new Uri(t).Host); return; }
            if (e.Data.GetData(typeof(DragIds)) is not DragIds d) return;
            var p = _list.PointToClient(new Point(e.X, e.Y));
            DropOn(d.Ids, _list.GetItemAt(p.X, p.Y), p);
        };
        Controls.Add(_list); Controls.Add(bar); Controls.Add(_summary);

        var tip = new ToolTip();
        tip.SetToolTip(imp, "Put back a LaunchPad backup, or bring in Keyboard Launchpad hotkeys");
        tip.SetToolTip(exp, "Save everything (hotkeys, launchers, settings) to one backup file");
        tip.SetToolTip(_pause, "Turn every hotkey off/on for a while");

        var menu = new ContextMenuStrip();
        menu.Items.Add("Edit", null, (s, e) => EditSelected());
        menu.Items.Add("Run it now", null, (s, e) => { if (Selected() is { } h) _ = Runner.Run(h); });
        menu.Items.Add("Turn on / off", null, (s, e) => ToggleSelected());
        menu.Items.Add("Copy", null, (s, e) => DuplicateSelected());
        menu.Items.Add("Delete", null, (s, e) => DeleteSelected());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sort this category A–Z", null, (s, e) => SortCategory());
        _list.ContextMenuStrip = menu;

        add.Click += (s, e) => AddNew();
        edit.Click += (s, e) => EditSelected();
        del.Click += (s, e) => DeleteSelected();
        cats.Click += (s, e) => { using var d = new CategoriesDialog(_store); d.ShowDialog(this); _changed(); Refill(); };
        sheet.Click += (s, e) => CheatSheet.Open(_store);
        imp.Click += (s, e) => ImportAny();
        exp.Click += (s, e) => ExportAny();
        _pause.Click += (s, e) => { _store.Paused = !_store.Paused; _changed(); Refill(); };
        _list.DoubleClick += (s, e) => EditSelected();
        _list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) DeleteSelected(); if (e.KeyCode == Keys.Enter) EditSelected(); };
        _search.TextChanged += (s, e) => Refill();
        _cat.SelectedIndexChanged += (s, e) => Refill();
        Refill();
    }

    record DragIds(List<string> Ids);

    static bool SameCat(Hotkey a, Hotkey b) => string.Equals(a.Category, b.Category, StringComparison.OrdinalIgnoreCase);

    /// <summary>Move the selected hotkey up/down within its category.</summary>
    void MoveSel(int d)
    {
        var h = Selected(); if (h == null) return;
        var list = _store.Hotkeys;
        int i = list.IndexOf(h), j = i + d;
        while (j >= 0 && j < list.Count && !SameCat(list[j], h)) j += d;
        if (j < 0 || j >= list.Count) return;
        (list[i], list[j]) = (list[j], list[i]);
        _changed(); Refill();
    }

    void DropOn(List<string> ids, ListViewItem target, Point p)
    {
        var moving = _store.Hotkeys.Where(h => ids.Contains(h.Id)).ToList();
        if (moving.Count == 0) return;
        var t = target == null ? null : _store.Hotkeys.FirstOrDefault(h => h.Id == (string)target.Tag);
        if (t != null && ids.Contains(t.Id)) return;
        _store.Hotkeys.RemoveAll(h => ids.Contains(h.Id));
        if (t == null) _store.Hotkeys.AddRange(moving);   // dropped on empty space: to the end
        else
        {
            bool after = p.Y > target.Bounds.Top + target.Bounds.Height / 2;
            int at = _store.Hotkeys.IndexOf(t) + (after ? 1 : 0);
            foreach (var m in moving) m.Category = t.Category;
            _store.Hotkeys.InsertRange(at, moving);
        }
        _changed(); Refill();
    }

    void SortCategory()
    {
        var h = Selected(); if (h == null) return;
        var idx = _store.Hotkeys.Select((x, i) => (x, i)).Where(p => SameCat(p.x, h)).Select(p => p.i).ToList();
        var sorted = idx.Select(i => _store.Hotkeys[i]).OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        for (int k = 0; k < idx.Count; k++) _store.Hotkeys[idx[k]] = sorted[k];
        _changed(); Refill();
    }

    Hotkey Selected() => _list.SelectedItems.Count > 0 ? _store.Hotkeys.FirstOrDefault(h => h.Id == (string)_list.SelectedItems[0].Tag) : null;

    string IconFor(Hotkey h)
    {
        var a = h.Actions.FirstOrDefault();
        string src = a == null ? null : a.Kind switch
        {
            ActionKind.Open => a.Target,
            ActionKind.PasteText => "@clip",
            ActionKind.System => "@sys",
            ActionKind.Media => "@media",
            _ => "@other"
        };
        if (src == null) return null;
        if (_iconKeys.TryGetValue(src, out var k)) return k;
        Bitmap bmp = src switch
        {
            "@clip" => Icons.FromLocation(@"%SystemRoot%\System32\shell32.dll,260", 32) ?? Icons.FromLocation(@"%SystemRoot%\System32\imageres.dll,-5302", 32),
            "@sys" => Icons.FromLocation(@"%SystemRoot%\System32\shell32.dll,21", 32),
            "@media" => Icons.FromLocation(@"%SystemRoot%\System32\shell32.dll,116", 32),
            "@other" => Icons.FromLocation(@"%SystemRoot%\System32\shell32.dll,24", 32),
            _ => Icons.Shell32(Icons.Resolve(src))
        };
        if (bmp == null) return null;
        k = "i" + _iconKeys.Count;
        _images.Images.Add(k, bmp);
        _iconKeys[src] = k;
        return k;
    }

    bool _filling;

    public void Refill()
    {
        if (_filling) return;   // resetting the category box fires its change event
        _filling = true;
        try { DoRefill(); } finally { _filling = false; }
    }

    void DoRefill()
    {
        var keepSel = _list.SelectedItems.Cast<ListViewItem>().Select(i => (string)i.Tag).ToHashSet();
        var cats = _store.AllCategories();
        var curCat = _cat.SelectedItem as string;
        _cat.BeginUpdate(); _cat.Items.Clear(); _cat.Items.Add("All categories"); foreach (var c in cats) _cat.Items.Add(c);
        _cat.SelectedItem = curCat != null && _cat.Items.Contains(curCat) ? curCat : "All categories"; _cat.EndUpdate();

        _list.BeginUpdate();
        _list.Items.Clear(); _list.Groups.Clear();
        var groups = new Dictionary<string, ListViewGroup>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in cats) groups[c] = _list.Groups.Add(c, c);
        var q = _search.Text.Trim();
        int shown = 0, working = 0, problems = 0;
        foreach (var h in _store.Hotkeys)   // your own order (drag or Up/Down to change it)
        {
            _mgr.Status.TryGetValue(h.Id, out var st); st ??= "";
            if (st == "") working++; else if (st != "Off" && st != "No key" && st != "Paused") problems++;
            if (_cat.SelectedIndex > 0 && !string.Equals(h.Category, (string)_cat.SelectedItem, StringComparison.OrdinalIgnoreCase)) continue;
            var does = string.Join(", then ", h.Actions.Select(a => a.Summary()));
            if (q != "" && !(h.DisplayName + " " + h.Combo + " " + does + " " + h.Description).Contains(q, StringComparison.OrdinalIgnoreCase)) continue;
            var it = new ListViewItem(h.DisplayName, IconFor(h)) { Tag = h.Id, UseItemStyleForSubItems = false, ToolTipText = h.Description };
            it.SubItems.Add(h.Combo == "" ? "—" : h.Combo);
            it.SubItems.Add(does);
            var stItem = it.SubItems.Add(st == "" ? "On" : st);
            stItem.ForeColor = st == "" ? Theme.Good : (st is "Off" or "No key" or "Paused") ? Theme.Dim : Theme.Bad;
            foreach (ListViewItem.ListViewSubItem si in it.SubItems) si.BackColor = Theme.Box;
            it.SubItems[0].ForeColor = it.SubItems[1].ForeColor = it.SubItems[2].ForeColor = Theme.Fore;
            if (!groups.TryGetValue(h.Category ?? "General", out var g)) { g = _list.Groups.Add(h.Category, h.Category); groups[h.Category] = g; }
            it.Group = g;
            if (keepSel.Contains(h.Id)) it.Selected = true;
            _list.Items.Add(it); shown++;
        }
        _list.ShowItemToolTips = true;
        _list.EndUpdate();
        _pause.Text = _store.Paused ? "Resume all" : "Pause all";
        _summary.Text = $"{_store.Hotkeys.Count} hotkeys · {working} working" + (problems > 0 ? $" · {problems} need attention" : "") + (_store.Paused ? " · ALL PAUSED" : "")
                        + (shown != _store.Hotkeys.Count ? $" · showing {shown}" : "");
    }

    /// <summary>New hotkey that already knows what it does; you just pick the keys.</summary>
    public void AddWith(HotAction a, string name)
    {
        var h = new Hotkey { Name = name ?? "", Category = _cat.SelectedIndex > 0 ? (string)_cat.SelectedItem : "General" };
        h.Actions.Add(a);
        if (Edit(h)) { _store.Hotkeys.Add(h); _changed(); Refill(); }
    }

    void AddNew()
    {
        var h = new Hotkey { Category = _cat.SelectedIndex > 0 ? (string)_cat.SelectedItem : "General" };
        if (Edit(h)) { _store.Hotkeys.Add(h); _changed(); Refill(); }
    }

    void EditSelected()
    {
        var h = Selected(); if (h == null) return;
        var copy = h.Clone();
        if (Edit(copy)) { _store.Hotkeys[_store.Hotkeys.IndexOf(h)] = copy; _changed(); Refill(); }
    }

    bool Edit(Hotkey h)
    {
        _mgr.Suspend();   // so pressing an existing combo in the editor doesn't fire it
        try { using var d = new HotkeyEditor(h, _store); return d.ShowDialog(this) == DialogResult.OK; }
        finally { _changed(); }
    }

    void ToggleSelected()
    {
        foreach (ListViewItem i in _list.SelectedItems) { var h = _store.Hotkeys.First(x => x.Id == (string)i.Tag); h.Enabled = !h.Enabled; }
        _changed(); Refill();
    }

    void DuplicateSelected()
    {
        var h = Selected(); if (h == null) return;
        var c = h.Clone(); c.Id = Guid.NewGuid().ToString("N"); c.Name = h.DisplayName + " (copy)"; c.Key = 0;
        _store.Hotkeys.Add(c); _changed(); Refill();
    }

    void DeleteSelected()
    {
        var ids = _list.SelectedItems.Cast<ListViewItem>().Select(i => (string)i.Tag).ToList();
        if (ids.Count == 0) return;
        if (MessageBox.Show(ids.Count == 1 ? "Delete this hotkey?" : $"Delete these {ids.Count} hotkeys?", "LaunchPad",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        _store.Hotkeys.RemoveAll(h => ids.Contains(h.Id));
        _changed(); Refill();
    }


    /// <summary>Import button: a LaunchPad backup, or Keyboard Launchpad's hotkeys / backup file.</summary>
    void ImportAny()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Backups and Keyboard Launchpad files|*.lpbackup;*.keysettings;*.ini|" + Backup.Filter + "|Keyboard Launchpad (*.keysettings;*.ini)|*.keysettings;*.ini",
            InitialDirectory = Directory.Exists(KlpImport.VirtualStore) ? KlpImport.VirtualStore : Paths.Desktop
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (dlg.FileName.EndsWith(".lpbackup", StringComparison.OrdinalIgnoreCase))
        {
            if (MessageBox.Show(this, "Put this backup back?\n\nYour hotkeys, launchers and settings get replaced by the ones in the backup. (What's here now is saved first, in LaunchPad\backups.)",
                    "LaunchPad", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            try { Backup.Restore(dlg.FileName); }
            catch (Exception ex) { MessageBox.Show(this, "Couldn't restore: " + ex.Message, "LaunchPad"); return; }
            MessageBox.Show(this, "Restored. LaunchPad restarts now.", "LaunchPad");
            Restart?.Invoke();
            return;
        }
        Import(true, dlg.FileName);
    }

    public Action Restart;

    void ExportAny()
    {
        using var dlg = new SaveFileDialog { Filter = Backup.Filter, InitialDirectory = Paths.Desktop, FileName = $"LaunchPad backup {DateTime.Now:yyyy-MM-dd}.lpbackup" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try { Backup.Export(dlg.FileName); MessageBox.Show(this, "Saved:\n" + dlg.FileName + "\n\nIt has your hotkeys, settings and all your launchers.", "LaunchPad"); }
        catch (Exception ex) { MessageBox.Show(this, "Couldn't export: " + ex.Message, "LaunchPad"); }
    }

    /// <summary>
    /// Bring in Keyboard Launchpad hotkeys. Ones already here (same name, or same keys when unnamed) get
    /// Keyboard Launchpad's keys and category; what they do stays as you set it here. New ones are added.
    /// </summary>
    public int Import(bool ask, string file = null)
    {
        file ??= KlpImport.FindFile();
        if (file == null) { if (ask) MessageBox.Show(this, "Couldn't find Keyboard Launchpad's hotkeys.", "LaunchPad"); return 0; }
        var incoming = KlpImport.Read(file);
        var plan = new List<(Hotkey inc, Hotkey match)>();
        var used = new HashSet<Hotkey>();
        foreach (var inc in incoming)
        {
            Hotkey m = null;
            if (!string.IsNullOrWhiteSpace(inc.Name))
                m = _store.Hotkeys.FirstOrDefault(h => !used.Contains(h) && string.Equals(h.Name, inc.Name, StringComparison.OrdinalIgnoreCase));
            else if (inc.Key != 0)
                m = _store.Hotkeys.FirstOrDefault(h => !used.Contains(h) && string.IsNullOrWhiteSpace(h.Name) && h.Key == inc.Key && h.Mods == inc.Mods);
            if (m != null) used.Add(m);
            plan.Add((inc, m));
        }
        int adds = plan.Count(p => p.match == null);
        int changes = plan.Count(p => p.match != null && (p.match.Key != p.inc.Key || p.match.Mods != p.inc.Mods || !string.Equals(p.match.Category, p.inc.Category, StringComparison.OrdinalIgnoreCase)));
        if (ask && MessageBox.Show(this, $"Found {incoming.Count} hotkeys in {Path.GetFileName(file)}.\n\n{adds} are new here and get added.\n{changes} already here get Keyboard Launchpad's keys/category.\n\nGo ahead?",
                "LaunchPad", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return 0;
        foreach (var (inc, m) in plan)
        {
            if (m == null) { _store.Hotkeys.Add(inc); continue; }
            m.Key = inc.Key; m.Mods = inc.Mods; m.Category = inc.Category;
            if (string.IsNullOrWhiteSpace(m.Description)) m.Description = inc.Description;
        }
        _store.Fixups();
        _store.ImportedKlp = true;
        _changed(); Refill();
        if (ask && Process.GetProcessesByName("Keys").Length > 0)
            MessageBox.Show(this, "Keyboard Launchpad is still running, so it's holding those keys and they show \"In use by another app\".\n\nRight-click its tray icon and exit it, then LaunchPad takes over.", "LaunchPad");
        return adds + changes;
    }
}

namespace LaunchPad;

/// <summary>Base for small themed dialogs with OK / Cancel.</summary>
class ThemedDialog : Form
{
    protected readonly FlowLayoutPanel Buttons = new() { Dock = DockStyle.Bottom, Height = 50, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
    protected readonly Button Ok = Theme.MakeButton("OK", 90), Cancel = Theme.MakeButton("Cancel", 90);

    public ThemedDialog(string title, int w, int h)
    {
        Text = title; ClientSize = new Size(w, h);
        StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.Sizable; MinimizeBox = false; MaximizeBox = false;
        ShowInTaskbar = false; Font = new Font("Segoe UI", 10); Icon = Icons.AppIcon();
        MinimumSize = new Size(Math.Min(w, 420), Math.Min(h, 300));
        Ok.Click += (s, e) => { if (Check()) { DialogResult = DialogResult.OK; Close(); } };
        Cancel.DialogResult = DialogResult.Cancel;
        Buttons.Controls.Add(Cancel); Buttons.Controls.Add(Ok);
        CancelButton = Cancel;
        Controls.Add(Buttons);
        HandleCreated += (s, e) => Theme.Apply(this);
    }

    protected virtual bool Check() => true;

    protected static Label L(string t) => new() { Text = t, AutoSize = true, Margin = new Padding(3, 9, 8, 3) };
}

class HotkeyEditor : ThemedDialog
{
    readonly Hotkey _h;
    readonly HotkeyStore _store;
    readonly TextBox _name = new() { Dock = DockStyle.Fill };
    readonly ComboBox _cat = new() { Dock = DockStyle.Fill };
    readonly TextBox _desc = new() { Dock = DockStyle.Fill };
    readonly CheckBox _win = new() { Text = "Win", AutoSize = true }, _ctrl = new() { Text = "Ctrl", AutoSize = true },
                      _alt = new() { Text = "Alt", AutoSize = true }, _shift = new() { Text = "Shift", AutoSize = true };
    readonly KeyBox _key = new() { Width = 150 };
    readonly CheckBox _enabled = new() { Text = "On", AutoSize = true, Checked = true };
    readonly Label _clash = new() { AutoSize = true, Tag = "keepcolor", Margin = new Padding(3, 6, 3, 3) };
    readonly ListBox _acts = new() { Dock = DockStyle.Fill, IntegralHeight = false };

    public HotkeyEditor(Hotkey h, HotkeyStore store) : base(string.IsNullOrEmpty(h.Name) ? "New hotkey" : "Edit hotkey", 640, 520)
    {
        _h = h; _store = store;
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10), RowCount = 6 };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 5; i++) t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        t.Controls.Add(L("Name"), 0, 0); t.Controls.Add(_name, 1, 0);
        t.Controls.Add(L("Category"), 0, 1); t.Controls.Add(_cat, 1, 1);
        t.Controls.Add(L("Note"), 0, 2); t.Controls.Add(_desc, 1, 2);

        var keys = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
        foreach (var c in new Control[] { _win, _ctrl, _alt, _shift }) { c.Margin = new Padding(3, 7, 6, 3); keys.Controls.Add(c); }
        keys.Controls.Add(new Label { Text = "+", AutoSize = true, Margin = new Padding(3, 7, 6, 3) });
        keys.Controls.Add(_key);
        var clear = Theme.MakeButton("Clear", 60); clear.Height = 28; keys.Controls.Add(clear);
        _enabled.Margin = new Padding(16, 7, 3, 3); keys.Controls.Add(_enabled);
        t.Controls.Add(L("Keys"), 0, 3); t.Controls.Add(keys, 1, 3);
        t.Controls.Add(new Label(), 0, 4); t.Controls.Add(_clash, 1, 4);

        var actPanel = new Panel { Dock = DockStyle.Fill };
        var actBtns = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 100, FlowDirection = FlowDirection.TopDown };
        var aAdd = Theme.MakeButton("Add...", 90); var aEdit = Theme.MakeButton("Edit...", 90); var aDel = Theme.MakeButton("Remove", 90);
        var aUp = Theme.MakeButton("Up", 90); var aDown = Theme.MakeButton("Down", 90); var aTest = Theme.MakeButton("Test", 90);
        actBtns.Controls.AddRange(new Control[] { aAdd, aEdit, aDel, aUp, aDown, aTest });
        actPanel.Controls.Add(_acts); actPanel.Controls.Add(actBtns);
        var actLabel = L("Does"); actLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        t.Controls.Add(actLabel, 0, 5); t.Controls.Add(actPanel, 1, 5);
        Controls.Add(t); t.BringToFront();

        _name.Text = h.Name; _desc.Text = h.Description;
        foreach (var c in store.AllCategories()) _cat.Items.Add(c);
        _cat.Text = h.Category;
        _win.Checked = (h.Mods & Native.MOD_WIN) != 0; _ctrl.Checked = (h.Mods & Native.MOD_CONTROL) != 0;
        _alt.Checked = (h.Mods & Native.MOD_ALT) != 0; _shift.Checked = (h.Mods & Native.MOD_SHIFT) != 0;
        _key.Vk = h.Key; _enabled.Checked = h.Enabled;
        _key.Captured += (mods) =>
        {
            if (mods != 0) { _win.Checked = (mods & Native.MOD_WIN) != 0; _ctrl.Checked = (mods & Native.MOD_CONTROL) != 0; _alt.Checked = (mods & Native.MOD_ALT) != 0; _shift.Checked = (mods & Native.MOD_SHIFT) != 0; }
            ShowClash();
        };
        foreach (var cb in new[] { _win, _ctrl, _alt, _shift }) cb.CheckedChanged += (s, e) => ShowClash();
        clear.Click += (s, e) => { _key.Vk = 0; ShowClash(); };
        FillActs();

        aAdd.Click += (s, e) => { var a = new HotAction(); using var d = new ActionEditor(a); if (d.ShowDialog(this) == DialogResult.OK) { _h.Actions.Add(a); FillActs(_h.Actions.Count - 1); } };
        aEdit.Click += (s, e) => EditAct();
        _acts.DoubleClick += (s, e) => EditAct();
        aDel.Click += (s, e) => { int i = _acts.SelectedIndex; if (i >= 0) { _h.Actions.RemoveAt(i); FillActs(Math.Min(i, _h.Actions.Count - 1)); } };
        aUp.Click += (s, e) => MoveAct(-1);
        aDown.Click += (s, e) => MoveAct(1);
        aTest.Click += (s, e) => { int i = _acts.SelectedIndex; if (i >= 0) _ = Runner.Do(_h.Actions[i]); };
        ShowClash();
        Shown += (s, e) => { if (_h.Actions.Count == 0) aAdd.PerformClick(); };
    }

    uint Mods => (_win.Checked ? Native.MOD_WIN : 0) | (_ctrl.Checked ? Native.MOD_CONTROL : 0) | (_alt.Checked ? Native.MOD_ALT : 0) | (_shift.Checked ? Native.MOD_SHIFT : 0);

    void ShowClash()
    {
        _clash.Text = "";
        if (_key.Vk == 0) { _clash.ForeColor = Theme.Dim; _clash.Text = "Click the key box and press the keys you want."; return; }
        var combo = KeyNames.Combo(Mods, _key.Vk);
        var other = _store.Hotkeys.FirstOrDefault(x => x.Id != _h.Id && x.Key == _key.Vk && x.Mods == Mods);
        var (level, why) = KeyNames.Warn(Mods, _key.Vk);
        if (other != null) { _clash.ForeColor = Theme.Bad; _clash.Text = $"Already used by your hotkey \"{other.DisplayName}\"."; }
        else if (Mods == _store.Search.Mods && _key.Vk == _store.Search.Key) { _clash.ForeColor = Theme.Bad; _clash.Text = $"{combo} opens Quick Search."; }
        else if (level == 2) { _clash.ForeColor = Theme.Bad; _clash.Text = why + " Pick other keys."; }
        else if (KeyNames.HeldByOtherApp(Handle, Mods, _key.Vk)) { _clash.ForeColor = Theme.Bad; _clash.Text = $"Another program on this PC already uses {combo}."; }
        else if (level == 1) { _clash.ForeColor = Theme.Warn; _clash.Text = why; }
        else { _clash.ForeColor = Theme.Good; _clash.Text = combo + " is free."; }
    }

    void FillActs(int select = -1)
    {
        _acts.Items.Clear();
        foreach (var a in _h.Actions) _acts.Items.Add(a.Summary());
        if (select >= 0 && select < _acts.Items.Count) _acts.SelectedIndex = select;
    }

    void EditAct()
    {
        int i = _acts.SelectedIndex; if (i < 0) return;
        var copy = _h.Actions[i].Clone();
        using var d = new ActionEditor(copy);
        if (d.ShowDialog(this) == DialogResult.OK) { _h.Actions[i] = copy; FillActs(i); }
    }

    void MoveAct(int d)
    {
        int i = _acts.SelectedIndex, j = i + d;
        if (i < 0 || j < 0 || j >= _h.Actions.Count) return;
        (_h.Actions[i], _h.Actions[j]) = (_h.Actions[j], _h.Actions[i]);
        FillActs(j);
    }

    protected override bool Check()
    {
        if (_h.Actions.Count == 0) { MessageBox.Show(this, "Add at least one thing for it to do.", "LaunchPad"); return false; }
        _h.Name = _name.Text.Trim();
        _h.Category = string.IsNullOrWhiteSpace(_cat.Text) ? "General" : _cat.Text.Trim();
        _h.Description = _desc.Text.Trim();
        _h.Mods = Mods; _h.Key = _key.Vk; _h.Enabled = _enabled.Checked;
        return true;
    }
}

/// <summary>Click it, press a key: it shows the key.</summary>
class KeyBox : TextBox
{
    uint _vk;
    public event Action<uint> Captured;   // gives the modifiers that were held
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public uint Vk { get => _vk; set { _vk = value; Text = value == 0 ? "" : KeyNames.Key(value); } }

    public KeyBox() { ReadOnly = true; PlaceholderText = "press a key"; Cursor = Cursors.Hand; ShortcutsEnabled = false; }

    protected override bool IsInputKey(Keys keyData) => true;
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // grab Tab, Enter, Esc, F-keys and friends too
        if (msg.Msg is 0x0100 or 0x0104) { Take((Keys)(int)msg.WParam); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    void Take(Keys k)
    {
        if (KeyNames.IsModifier(k)) return;
        uint mods = 0;
        if ((Native.GetKeyState(0x5B) & 0x8000) != 0 || (Native.GetKeyState(0x5C) & 0x8000) != 0) mods |= Native.MOD_WIN;
        if ((ModifierKeys & Keys.Control) != 0) mods |= Native.MOD_CONTROL;
        if ((ModifierKeys & Keys.Alt) != 0) mods |= Native.MOD_ALT;
        if ((ModifierKeys & Keys.Shift) != 0) mods |= Native.MOD_SHIFT;
        Vk = (uint)k;
        Captured?.Invoke(mods);
    }
}

class ActionEditor : ThemedDialog
{
    readonly HotAction _a;
    readonly ComboBox _kind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    readonly Panel _body = new() { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 4) };
    // open
    readonly TextBox _target = new() { Dock = DockStyle.Fill, AllowDrop = true }, _args = new() { Dock = DockStyle.Fill }, _startIn = new() { Dock = DockStyle.Fill };
    readonly CheckBox _front = new() { Text = "If it's already open, just bring it to the front", AutoSize = true };
    // paste
    readonly TextBox _text = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true };
    readonly CheckBox _pasteNow = new() { Text = "Paste it right away (otherwise just copy it)", AutoSize = true };
    // system / media
    readonly ComboBox _sys = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 }, _media = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
    // mouse
    readonly NumericUpDown _x = new() { Minimum = -20000, Maximum = 20000, Width = 90 }, _y = new() { Minimum = -20000, Maximum = 20000, Width = 90 };
    readonly ComboBox _btn = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    // workspace
    readonly ComboBox _ws = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
    // wait
    readonly NumericUpDown _ms = new() { Minimum = 0, Maximum = 60000, Increment = 100, Width = 110 };

    static readonly (ActionKind k, string label)[] Kinds =
    {
        (ActionKind.Open, "Open a program, file, folder or website"), (ActionKind.PasteText, "Paste saved text"),
        (ActionKind.System, "Windows command"), (ActionKind.Media, "Music / volume"),
        (ActionKind.MouseClick, "Mouse click"), (ActionKind.Wait, "Wait a moment"), (ActionKind.Workspace, "Open a workspace"),
    };

    public ActionEditor(HotAction a) : base("What should it do?", 620, 380)
    {
        _a = a;
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(10, 6, 10, 0) };
        top.Controls.Add(_kind);
        Controls.Add(_body); Controls.Add(top); _body.BringToFront();
        foreach (var k in Kinds) _kind.Items.Add(k.label);
        foreach (SysCmd s in Enum.GetValues<SysCmd>()) if (s != SysCmd.Unknown) _sys.Items.Add(HotAction.Words(s.ToString()));
        foreach (MediaCmd m in Enum.GetValues<MediaCmd>()) _media.Items.Add(HotAction.Words(m.ToString()));
        _btn.Items.AddRange(new object[] { "Left", "Right", "Middle", "Double" });

        _target.Text = a.Target; _args.Text = a.Arguments; _startIn.Text = a.StartIn; _front.Checked = a.BringToFront;
        _text.Text = a.Text; _pasteNow.Checked = a.PasteNow;
        _sys.SelectedIndex = a.System == SysCmd.Unknown ? -1 : (int)a.System; _media.SelectedIndex = (int)a.Media;
        _x.Value = a.X; _y.Value = a.Y; _btn.SelectedItem = a.Button ?? "Left"; _ms.Value = Math.Clamp(a.Ms, 0, 60000);
        foreach (var w in Runner.Store?.Workspaces ?? new()) _ws.Items.Add(w.Name);
        _ws.SelectedIndex = Runner.Store?.Workspaces.FindIndex(w => w.Id == a.WorkspaceId) ?? -1;
        _target.DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
        _target.DragDrop += (s, e) => _target.Text = ((string[])e.Data.GetData(DataFormats.FileDrop))[0];

        _kind.SelectedIndexChanged += (s, e) => Build();
        _kind.SelectedIndex = Array.FindIndex(Kinds, k => k.k == a.Kind);
    }

    TableLayoutPanel Grid()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = false };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        return t;
    }

    void Build()
    {
        _body.Controls.Clear();
        var kind = Kinds[_kind.SelectedIndex].k;
        var t = Grid();
        switch (kind)
        {
            case ActionKind.Open:
            {
                var file = Theme.MakeButton("File...", 80); var folder = Theme.MakeButton("Folder...", 80); var launcher = Theme.MakeButton("Launcher...", 95);
                file.Click += (s, e) => { using var d = new OpenFileDialog { DereferenceLinks = false, Filter = "Anything (*.*)|*.*" }; if (d.ShowDialog(this) == DialogResult.OK) _target.Text = d.FileName; };
                folder.Click += (s, e) => { using var d = new FolderBrowserDialog(); if (d.ShowDialog(this) == DialogResult.OK) _target.Text = d.SelectedPath; };
                launcher.Click += (s, e) =>
                {
                    Directory.CreateDirectory(Paths.Launchers);
                    using var d = new OpenFileDialog { InitialDirectory = Paths.Launchers, Filter = "LaunchPad launchers (*.exe)|*.exe" };
                    if (d.ShowDialog(this) == DialogResult.OK) _target.Text = d.FileName;
                };
                var pick = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
                pick.Controls.AddRange(new Control[] { file, folder, launcher });
                t.Controls.Add(L("Open"), 0, 0); t.Controls.Add(_target, 1, 0); t.SetColumnSpan(_target, 2);
                t.Controls.Add(new Label(), 0, 1); t.Controls.Add(pick, 1, 1);
                t.Controls.Add(L("Arguments"), 0, 2); t.Controls.Add(_args, 1, 2); t.SetColumnSpan(_args, 2);
                t.Controls.Add(L("Start in"), 0, 3); t.Controls.Add(_startIn, 1, 3); t.SetColumnSpan(_startIn, 2);
                t.Controls.Add(new Label(), 0, 4); t.Controls.Add(_front, 1, 4);
                t.Controls.Add(new Label { Text = "Tip: drag a file or shortcut onto the Open box. Websites work too (https://...).", AutoSize = true, Tag = "dim", Margin = new Padding(3, 10, 3, 3) }, 1, 5);
                break;
            }
            case ActionKind.PasteText:
                t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                t.Controls.Add(L("Text"), 0, 0); t.Controls.Add(_text, 1, 0); t.SetColumnSpan(_text, 2);
                t.Controls.Add(new Label(), 0, 1); t.Controls.Add(_pasteNow, 1, 1);
                break;
            case ActionKind.System:
                t.Controls.Add(L("Command"), 0, 0); t.Controls.Add(_sys, 1, 0);
                if (_a.System == SysCmd.Unknown && !string.IsNullOrEmpty(_a.Note))
                    t.Controls.Add(new Label { Text = $"This came from Keyboard Launchpad ({_a.Note}). Pick what it should do.", AutoSize = true, Tag = "dim", Margin = new Padding(3, 10, 3, 3) }, 1, 1);
                t.Controls.Add(new Label { Text = "Shut down, restart, sign out and emptying the Recycle Bin ask first.", AutoSize = true, Tag = "dim", Margin = new Padding(3, 10, 3, 3) }, 1, 2);
                break;
            case ActionKind.Media:
                t.Controls.Add(L("Do"), 0, 0); t.Controls.Add(_media, 1, 0);
                t.Controls.Add(new Label { Text = "Works with any player that listens to the keyboard's media keys.", AutoSize = true, Tag = "dim", Margin = new Padding(3, 10, 3, 3) }, 1, 1);
                break;
            case ActionKind.MouseClick:
            {
                var grab = Theme.MakeButton("Grab spot in 3 s", 140);
                grab.Click += async (s, e) =>
                {
                    for (int i = 3; i > 0; i--) { grab.Text = $"Point… {i}"; await Task.Delay(1000); }
                    var p = Cursor.Position; _x.Value = p.X; _y.Value = p.Y; grab.Text = "Grab spot in 3 s";
                };
                var xy = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
                xy.Controls.AddRange(new Control[] { new Label { Text = "X", AutoSize = true, Margin = new Padding(3, 8, 3, 3) }, _x, new Label { Text = "Y", AutoSize = true, Margin = new Padding(10, 8, 3, 3) }, _y, grab });
                t.Controls.Add(L("Where"), 0, 0); t.Controls.Add(xy, 1, 0);
                t.Controls.Add(L("Button"), 0, 1); t.Controls.Add(_btn, 1, 1);
                break;
            }
            case ActionKind.Workspace:
                t.Controls.Add(L("Workspace"), 0, 0); t.Controls.Add(_ws, 1, 0);
                if (_ws.Items.Count == 0)
                    t.Controls.Add(new Label { Text = "No workspaces yet. Make one in the Workspaces tab first.", AutoSize = true, Tag = "dim", Margin = new Padding(3, 10, 3, 3) }, 1, 1);
                break;
            case ActionKind.Wait:
                var w = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
                w.Controls.AddRange(new Control[] { _ms, new Label { Text = "milliseconds (1000 = 1 second)", AutoSize = true, Margin = new Padding(6, 8, 3, 3) } });
                t.Controls.Add(L("Wait"), 0, 0); t.Controls.Add(w, 1, 0);
                break;
        }
        _body.Controls.Add(t);
        Theme.Apply(_body);
    }

    protected override bool Check()
    {
        var kind = Kinds[_kind.SelectedIndex].k;
        if (kind == ActionKind.Open && string.IsNullOrWhiteSpace(_target.Text)) { MessageBox.Show(this, "Pick what to open.", "LaunchPad"); return false; }
        if (kind == ActionKind.System && _sys.SelectedIndex < 0) { MessageBox.Show(this, "Pick a command.", "LaunchPad"); return false; }
        if (kind == ActionKind.Workspace && _ws.SelectedIndex < 0) { MessageBox.Show(this, "Pick a workspace.", "LaunchPad"); return false; }
        if (kind == ActionKind.Workspace) _a.WorkspaceId = Runner.Store.Workspaces[_ws.SelectedIndex].Id;
        _a.Kind = kind;
        _a.Target = _target.Text.Trim(); _a.Arguments = _args.Text.Trim(); _a.StartIn = _startIn.Text.Trim(); _a.BringToFront = _front.Checked;
        _a.Text = _text.Text; _a.PasteNow = _pasteNow.Checked;
        if (_sys.SelectedIndex >= 0) { _a.System = (SysCmd)_sys.SelectedIndex; }
        _a.Media = (MediaCmd)Math.Max(0, _media.SelectedIndex);
        _a.X = (int)_x.Value; _a.Y = (int)_y.Value; _a.Button = _btn.SelectedItem as string ?? "Left"; _a.Ms = (int)_ms.Value;
        return true;
    }
}

/// <summary>Rename, add or remove categories. Renaming moves every hotkey with it.</summary>
class CategoriesDialog : ThemedDialog
{
    readonly HotkeyStore _store;
    readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false };

    public CategoriesDialog(HotkeyStore store) : base("Categories", 440, 400)
    {
        _store = store;
        Cancel.Visible = false; Ok.Text = "Done";
        var btns = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 110, FlowDirection = FlowDirection.TopDown, Padding = new Padding(4, 8, 4, 4) };
        var ren = Theme.MakeButton("Rename...", 100); var add = Theme.MakeButton("Add...", 100); var del = Theme.MakeButton("Delete", 100);
        var up = Theme.MakeButton("Up", 100); var down = Theme.MakeButton("Down", 100);
        btns.Controls.AddRange(new Control[] { ren, add, del, up, down });
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 10, 0, 0) };
        body.Controls.Add(_list);
        Controls.Add(body); Controls.Add(btns); body.BringToFront();
        _store.Categories = _store.AllCategories();
        Fill();
        ren.Click += (s, e) => Rename();
        _list.DoubleClick += (s, e) => Rename();
        add.Click += (s, e) =>
        {
            var n = Ask("New category", "");
            if (n != null && !_store.Categories.Contains(n, StringComparer.OrdinalIgnoreCase)) { _store.Categories.Add(n); Fill(n); }
        };
        del.Click += (s, e) => Delete();
        up.Click += (s, e) => MoveItem(-1);
        down.Click += (s, e) => MoveItem(1);
    }

    string Cur => _list.SelectedIndex >= 0 ? _store.Categories[_list.SelectedIndex] : null;

    void Fill(string select = null)
    {
        _list.Items.Clear();
        foreach (var c in _store.Categories)
        {
            int n = _store.Hotkeys.Count(h => string.Equals(h.Category, c, StringComparison.OrdinalIgnoreCase));
            _list.Items.Add($"{c}   ({n})");
        }
        int i = select == null ? -1 : _store.Categories.FindIndex(c => string.Equals(c, select, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) _list.SelectedIndex = i; else if (_list.Items.Count > 0) _list.SelectedIndex = 0;
    }

    void Rename()
    {
        var c = Cur; if (c == null) return;
        var n = Ask("Rename category", c);
        if (n == null || n == c) return;
        _store.RenameCategory(c, n);
        Fill(n);
    }

    void Delete()
    {
        var c = Cur; if (c == null) return;
        int n = _store.Hotkeys.Count(h => string.Equals(h.Category, c, StringComparison.OrdinalIgnoreCase));
        var others = _store.Categories.Where(x => x != c).ToList();
        if (n > 0)
        {
            var to = others.FirstOrDefault(x => x.Equals("General", StringComparison.OrdinalIgnoreCase)) ?? others.FirstOrDefault() ?? "General";
            if (MessageBox.Show(this, $"\"{c}\" has {n} hotkeys. Move them to \"{to}\" and delete the category?", "LaunchPad",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            foreach (var h in _store.Hotkeys.Where(h => string.Equals(h.Category, c, StringComparison.OrdinalIgnoreCase))) h.Category = to;
            if (!_store.Categories.Contains(to)) _store.Categories.Add(to);
        }
        _store.Categories.Remove(c);
        Fill();
    }

    void MoveItem(int d)
    {
        int i = _list.SelectedIndex, j = i + d;
        if (i < 0 || j < 0 || j >= _store.Categories.Count) return;
        (_store.Categories[i], _store.Categories[j]) = (_store.Categories[j], _store.Categories[i]);
        Fill(_store.Categories[j]);
    }

    string Ask(string title, string start)
    {
        using var d = new ThemedDialog(title, 380, 130);
        var tb = new TextBox { Text = start, Dock = DockStyle.Top, Margin = new Padding(10) };
        var p = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 14, 12, 0) };
        p.Controls.Add(tb); d.Controls.Add(p); p.BringToFront();
        d.AcceptButton = d.Controls.OfType<FlowLayoutPanel>().First().Controls[1] as Button;
        d.Shown += (s, e) => { tb.Focus(); tb.SelectAll(); };
        if (d.ShowDialog(this) != DialogResult.OK) return null;
        var n = tb.Text.Trim();
        return n == "" ? null : n;
    }
}

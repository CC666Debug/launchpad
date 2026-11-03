using System.Diagnostics;
using System.Text;

namespace LaunchPad;

/// <summary>Finds open windows, and puts a workspace's windows back where they were saved.</summary>
static class Workspaces
{
    public record Win(IntPtr Handle, string Title, string Exe, Native.RECT Rect, string State);

    static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
    { "explorer.exe:Program Manager", "TextInputHost.exe", "ShellExperienceHost.exe", "SearchHost.exe", "StartMenuExperienceHost.exe", "LockApp.exe" };

    /// <summary>Normal app windows people see (no tool windows, hidden or cloaked ones).</summary>
    public static List<Win> OpenWindows()
    {
        var list = new List<Win>();
        int me = Environment.ProcessId;
        Native.EnumWindows((h, l) =>
        {
            if (!Native.IsWindowVisible(h) || Native.GetWindow(h, 4) != IntPtr.Zero) return true;            // owned popups
            if ((Native.GetWindowLong(h, -20) & 0x80) != 0) return true;                                      // tool windows
            if (Native.DwmGetWindowAttribute(h, 14, out int cloaked, 4) == 0 && cloaked != 0) return true;   // other desktops / suspended
            var title = Native.Title(h);
            if (title == "") return true;
            Native.GetWindowThreadProcessId(h, out int pid);
            if (pid == me) return true;
            var exe = ImagePath(pid) ?? "";
            var name = Path.GetFileName(exe);
            if (Skip.Contains(name) || Skip.Contains(name + ":" + title)) return true;
            var state = Native.IsIconic(h) ? "Minimized" : Native.IsZoomed(h) ? "Maximized" : "Normal";
            var r = state == "Normal" && Native.GetWindowRect(h, out var cur) ? cur : RestoredRect(h);
            list.Add(new Win(h, title, exe, r, state));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>Where a minimized/maximized window goes when restored, in screen pixels.</summary>
    static Native.RECT RestoredRect(IntPtr h)
    {
        var wp = new Native.WINDOWPLACEMENT { length = System.Runtime.InteropServices.Marshal.SizeOf<Native.WINDOWPLACEMENT>() };
        Native.GetWindowPlacement(h, ref wp);
        var r = wp.rcNormal;
        // placement uses "workspace" pixels (shifted by a taskbar on the top/left); turn them into screen pixels
        var scr = Screen.FromRectangle(Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom));
        int dx = scr.WorkingArea.Left - scr.Bounds.Left, dy = scr.WorkingArea.Top - scr.Bounds.Top;
        return new Native.RECT { Left = r.Left + dx, Top = r.Top + dy, Right = r.Right + dx, Bottom = r.Bottom + dy };
    }

    public static string ImagePath(int pid)
    {
        var h = Native.OpenProcess(0x1000, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024); int size = sb.Capacity;
            return Native.QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { Native.CloseHandle(h); }
    }

    /// <summary>The command line a process was started with (via Windows' own WMI), minus the program itself.</summary>
    public static string ArgsOf(IntPtr hwnd)
    {
        try
        {
            Native.GetWindowThreadProcessId(hwnd, out int pid);
            dynamic loc = Activator.CreateInstance(Type.GetTypeFromProgID("WbemScripting.SWbemLocator"));
            dynamic svc = loc.ConnectServer(".", "root\\cimv2");
            foreach (dynamic p in svc.ExecQuery($"SELECT CommandLine FROM Win32_Process WHERE ProcessId={pid}"))
            {
                string cmd = p.CommandLine;
                if (string.IsNullOrEmpty(cmd)) return "";
                cmd = cmd.Trim();
                if (cmd.StartsWith("\"")) { int e = cmd.IndexOf('"', 1); return e > 0 ? cmd[(e + 1)..].Trim() : ""; }
                int sp = cmd.IndexOf(' ');
                return sp > 0 ? cmd[(sp + 1)..].Trim() : "";
            }
        }
        catch { }
        return "";
    }

    /// <summary>Turn a list of open windows into workspace items.</summary>
    public static List<WinItem> ToItems(IEnumerable<Win> wins)
    {
        var all = wins.ToList();
        var items = new List<WinItem>();
        foreach (var w in all)
        {
            bool shared = all.Count(x => string.Equals(x.Exe, w.Exe, StringComparison.OrdinalIgnoreCase)) > 1;
            bool store = w.Exe.EndsWith("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase);
            var it = new WinItem
            {
                Name = w.Title, Exe = w.Exe,
                // when one program has several windows (browsers), tell them apart by the end of the title
                TitleContains = shared || store ? TitleKey(w.Title) : "",
                Launch = store ? "" : w.Exe, Args = store ? "" : ArgsOf(w.Handle),
                OpenIfClosed = !store,
                X = w.Rect.Left, Y = w.Rect.Top, W = w.Rect.Right - w.Rect.Left, H = w.Rect.Bottom - w.Rect.Top, State = w.State
            };
            if (w.State != "Normal") { /* keep the restored size from now; it re-maximizes after moving */ }
            items.Add(it);
        }
        return items;
    }

    /// <summary>"Inbox (3) - Gmail" -> "Gmail": the part after the last dash is usually the app.</summary>
    public static string TitleKey(string t)
    {
        foreach (var sep in new[] { " - ", " — ", " – ", " | " })
        {
            int k = t.LastIndexOf(sep, StringComparison.Ordinal);
            if (k > 0 && k + sep.Length < t.Length) return t[(k + sep.Length)..].Trim();
        }
        return t;
    }

    static bool Matches(Win w, WinItem it) =>
        string.Equals(w.Exe, it.Exe, StringComparison.OrdinalIgnoreCase) &&
        (string.IsNullOrEmpty(it.TitleContains) || w.Title.Contains(it.TitleContains, StringComparison.OrdinalIgnoreCase));

    public static void Place(IntPtr h, WinItem it)
    {
        if (Native.IsIconic(h) || Native.IsZoomed(h)) Native.ShowWindow(h, 9);   // restore first so the size sticks
        Native.SetWindowPos(h, IntPtr.Zero, it.X, it.Y, it.W, it.H, 0x0004 | 0x0010); // NOZORDER | NOACTIVATE
        if (it.State == "Maximized") Native.ShowWindow(h, 3);
        else if (it.State == "Minimized") Native.ShowWindow(h, 6);
    }

    /// <summary>Open what's missing, then move every window into place. Runs a second pass because apps like to move themselves while starting.</summary>
    public static async Task Open(Workspace ws)
    {
        var used = new HashSet<IntPtr>();
        var placed = new Dictionary<WinItem, IntPtr>();
        var now = OpenWindows();
        foreach (var it in ws.Items)
        {
            var w = now.FirstOrDefault(x => !used.Contains(x.Handle) && Matches(x, it));
            if (w != null) { used.Add(w.Handle); placed[it] = w.Handle; Place(w.Handle, it); continue; }
            if (it.OpenIfClosed && !string.IsNullOrWhiteSpace(it.Launch))
            {
                try
                {
                    Native.AllowSetForegroundWindow(-1);
                    var target = Environment.ExpandEnvironmentVariables(it.Launch);
                    var psi = new ProcessStartInfo(target) { UseShellExecute = true, Arguments = it.Args ?? "" };
                    if (File.Exists(target)) psi.WorkingDirectory = Path.GetDirectoryName(target);
                    Process.Start(psi);
                }
                catch (Exception e) { Tray.Tip("LaunchPad", $"Couldn't open {it.Name}: {e.Message}", true); }
            }
        }
        // wait up to 20 s for the new windows to show up
        for (int t = 0; t < 40 && placed.Count < ws.Items.Count; t++)
        {
            await Task.Delay(500);
            now = OpenWindows();
            foreach (var it in ws.Items.Where(i => !placed.ContainsKey(i)))
            {
                var w = now.FirstOrDefault(x => !used.Contains(x.Handle) && Matches(x, it));
                if (w == null) continue;
                used.Add(w.Handle); placed[it] = w.Handle; Place(w.Handle, it);
            }
        }
        await Task.Delay(1500);
        foreach (var (it, h) in placed) Place(h, it);   // second pass
        int missing = ws.Items.Count - placed.Count;
        if (missing > 0) Tray.Tip("LaunchPad", $"{ws.Name}: {missing} window(s) didn't show up.", true);
    }
}

class WorkspacesPage : UserControl
{
    readonly HotkeyStore _store;
    readonly Action _changed;
    readonly ListBox _spaces = new() { Dock = DockStyle.Fill, IntegralHeight = false, Font = new Font("Segoe UI", 11) };
    readonly ListView _items = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false };
    readonly ImageList _img = new() { ImageSize = new Size(20, 20), ColorDepth = ColorDepth.Depth32Bit };

    public WorkspacesPage(HotkeyStore store, Action changed)
    {
        _store = store; _changed = changed; Dock = DockStyle.Fill;
        _items.SmallImageList = _img;
        _items.Columns.Add("Window", 260); _items.Columns.Add("Program", 150); _items.Columns.Add("Where", 150); _items.Columns.Add("Size", 110); _items.Columns.Add("Opens if closed", 120);
        Theme.HookListView(_items);
        _items.ClientSizeChanged += (s, e) =>   // last column fills the rest: no sideways scrollbar
        {
            int used = 0; for (int i = 0; i < _items.Columns.Count - 1; i++) used += _items.Columns[i].Width;
            _items.Columns[^1].Width = Math.Max(110, _items.ClientSize.Width - used - 2);
        };
        _items.Columns[0].Width = 230;

        var hint = new Label { Text = "Arrange your windows how you like, then Save current windows. Open puts them all back (and opens what's closed).", Dock = DockStyle.Top, Height = 30, Padding = new Padding(8, 8, 8, 0), Tag = "dim" };

        var left = new Panel { Dock = DockStyle.Left, Width = 250, Padding = new Padding(6) };
        var lbtns = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 4, 0, 0) };
        var save = Theme.MakeButton("Save current windows...", 232); var open = Theme.MakeButton("Open", 70);
        var ren = Theme.MakeButton("Rename", 78); var del = Theme.MakeButton("Delete", 70); var hk = Theme.MakeButton("Make a hotkey...", 232);
        lbtns.Controls.AddRange(new Control[] { save, open, ren, del, hk });
        left.Controls.Add(_spaces); left.Controls.Add(lbtns);

        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
        var rbtns = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
        var edit = Theme.MakeButton("Edit window...", 120); var rem = Theme.MakeButton("Remove", 80);
        var addw = Theme.MakeButton("Add open windows...", 160); var upd = Theme.MakeButton("Update positions", 140);
        rbtns.Controls.AddRange(new Control[] { edit, rem, addw, upd });
        right.Controls.Add(_items); right.Controls.Add(rbtns);

        Controls.Add(right); Controls.Add(left); Controls.Add(hint);
        var tip = new ToolTip();
        tip.SetToolTip(upd, "Use where these windows are right now");
        tip.SetToolTip(hk, "A hotkey that opens this workspace");

        _spaces.SelectedIndexChanged += (s, e) => FillItems();
        _spaces.DoubleClick += (s, e) => OpenSel();
        save.Click += (s, e) => SaveNew();
        open.Click += (s, e) => OpenSel();
        ren.Click += (s, e) => { var w = Cur; if (w == null) return; var n = Ask("Rename workspace", w.Name); if (n != null) { w.Name = n; _changed(); Fill(); } };
        del.Click += (s, e) =>
        {
            var w = Cur; if (w == null) return;
            if (MessageBox.Show(this, $"Delete the workspace \"{w.Name}\"?", "LaunchPad", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            _store.Workspaces.Remove(w); _changed(); Fill();
        };
        hk.Click += (s, e) => MakeHotkey?.Invoke(Cur);
        edit.Click += (s, e) => EditItem();
        _items.DoubleClick += (s, e) => EditItem();
        rem.Click += (s, e) => { var w = Cur; if (w == null || _items.SelectedIndices.Count == 0) return; w.Items.RemoveAt(_items.SelectedIndices[0]); _changed(); FillItems(); };
        addw.Click += (s, e) =>
        {
            var w = Cur; if (w == null) return;
            using var d = new CaptureDialog(null);
            if (d.ShowDialog(this) == DialogResult.OK) { w.Items.AddRange(Workspaces.ToItems(d.Picked)); _changed(); FillItems(); }
        };
        upd.Click += (s, e) => UpdatePositions();
        Fill();
    }

    public Action<Workspace> MakeHotkey;

    Workspace Cur => _spaces.SelectedIndex >= 0 && _spaces.SelectedIndex < _store.Workspaces.Count ? _store.Workspaces[_spaces.SelectedIndex] : null;

    public void Fill(string selectId = null)
    {
        var keep = selectId ?? Cur?.Id;
        _spaces.Items.Clear();
        foreach (var w in _store.Workspaces) _spaces.Items.Add($"{w.Name}   ({w.Items.Count})");
        int i = _store.Workspaces.FindIndex(w => w.Id == keep);
        if (i >= 0) _spaces.SelectedIndex = i; else if (_spaces.Items.Count > 0) _spaces.SelectedIndex = 0; else FillItems();
    }

    void FillItems()
    {
        _items.BeginUpdate(); _items.Items.Clear();
        var w = Cur;
        if (w != null)
            foreach (var it in w.Items)
            {
                var key = it.Exe;
                if (!_img.Images.ContainsKey(key)) { var b = Icons.Shell32(it.Exe); if (b != null) _img.Images.Add(key, b); }
                var li = new ListViewItem(string.IsNullOrEmpty(it.TitleContains) ? it.Name : it.TitleContains, key);
                li.SubItems.Add(Path.GetFileNameWithoutExtension(it.Exe));
                li.SubItems.Add(it.State == "Maximized" ? "Maximized" : it.State == "Minimized" ? "Minimized" : $"{it.X}, {it.Y}");
                li.SubItems.Add($"{it.W} × {it.H}");
                li.SubItems.Add(it.OpenIfClosed && !string.IsNullOrEmpty(it.Launch) ? "Yes" : "No (only moves it)");
                _items.Items.Add(li);
            }
        _items.EndUpdate();
    }

    void SaveNew()
    {
        using var d = new CaptureDialog("My workspace " + (_store.Workspaces.Count + 1));
        if (d.ShowDialog(this) != DialogResult.OK) return;
        var ws = new Workspace { Name = d.WorkspaceName, Items = Workspaces.ToItems(d.Picked) };
        _store.Workspaces.Add(ws); _changed(); Fill(ws.Id);
    }

    async void OpenSel()
    {
        var w = Cur; if (w == null) return;
        UseWaitCursor = true;
        try { await Workspaces.Open(w); } finally { UseWaitCursor = false; }
    }

    void UpdatePositions()
    {
        var w = Cur; if (w == null) return;
        var now = Workspaces.OpenWindows(); var used = new HashSet<IntPtr>(); int n = 0;
        foreach (var it in w.Items)
        {
            var win = now.FirstOrDefault(x => !used.Contains(x.Handle) && string.Equals(x.Exe, it.Exe, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrEmpty(it.TitleContains) || x.Title.Contains(it.TitleContains, StringComparison.OrdinalIgnoreCase)));
            if (win == null) continue;
            used.Add(win.Handle); n++;
            it.X = win.Rect.Left; it.Y = win.Rect.Top; it.W = win.Rect.Right - win.Rect.Left; it.H = win.Rect.Bottom - win.Rect.Top; it.State = win.State;
        }
        _changed(); FillItems();
        MessageBox.Show(this, $"Updated {n} of {w.Items.Count} windows from where they are now." + (n < w.Items.Count ? "\nThe others aren't open, so they kept their old spot." : ""), "LaunchPad");
    }

    void EditItem()
    {
        var w = Cur; if (w == null || _items.SelectedIndices.Count == 0) return;
        int i = _items.SelectedIndices[0];
        var copy = System.Text.Json.JsonSerializer.Deserialize<WinItem>(System.Text.Json.JsonSerializer.Serialize(w.Items[i]));
        using var d = new WinItemDialog(copy);
        if (d.ShowDialog(this) == DialogResult.OK) { w.Items[i] = copy; _changed(); FillItems(); }
    }

    string Ask(string title, string start)
    {
        using var d = new TextAskDialog(title, start);
        return d.ShowDialog(this) == DialogResult.OK ? d.Value : null;
    }
}

/// <summary>Pick which open windows go in the workspace.</summary>
class CaptureDialog : ThemedDialog
{
    readonly CheckedListBox _list = new() { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
    readonly TextBox _name = new() { Dock = DockStyle.Fill };
    readonly List<Workspaces.Win> _wins;
    public List<Workspaces.Win> Picked { get; private set; } = new();
    public string WorkspaceName => _name.Text.Trim();

    public CaptureDialog(string name) : base(name == null ? "Add open windows" : "Save current windows", 640, 460)
    {
        _wins = Workspaces.OpenWindows();
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize)); t.RowStyles.Add(new RowStyle(SizeType.AutoSize)); t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        if (name != null) { t.Controls.Add(L("Name"), 0, 0); t.Controls.Add(_name, 1, 0); _name.Text = name; }
        t.Controls.Add(new Label { Text = "Tick the windows to keep:", AutoSize = true, Tag = "dim", Margin = new Padding(3, 8, 3, 3) }, 1, 1);
        t.Controls.Add(_list, 0, 2); t.SetColumnSpan(_list, 2);
        Controls.Add(t); t.BringToFront();
        foreach (var w in _wins)
            _list.Items.Add($"{w.Title}   —   {Path.GetFileNameWithoutExtension(w.Exe)}" + (w.State != "Normal" ? $"  ({w.State.ToLower()})" : ""), w.State != "Minimized");
    }

    protected override bool Check()
    {
        Picked = _list.CheckedIndices.Cast<int>().Select(i => _wins[i]).ToList();
        if (Picked.Count == 0) { MessageBox.Show(this, "Tick at least one window.", "LaunchPad"); return false; }
        if (Text.StartsWith("Save") && WorkspaceName == "") { MessageBox.Show(this, "Give it a name.", "LaunchPad"); return false; }
        return true;
    }
}

class WinItemDialog : ThemedDialog
{
    readonly WinItem _it;
    readonly TextBox _title = new() { Dock = DockStyle.Fill }, _launch = new() { Dock = DockStyle.Fill, AllowDrop = true }, _args = new() { Dock = DockStyle.Fill };
    readonly CheckBox _open = new() { Text = "Open it if it isn't running", AutoSize = true };
    readonly NumericUpDown _x = N(), _y = N(), _w = N(), _h = N();
    readonly ComboBox _state = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };

    static NumericUpDown N() => new() { Minimum = -30000, Maximum = 30000, Width = 85 };

    public WinItemDialog(WinItem it) : base("Window", 620, 400)
    {
        _it = it;
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.Controls.Add(L("Program"), 0, 0); t.Controls.Add(new Label { Text = Path.GetFileName(it.Exe), AutoSize = true, Margin = new Padding(3, 9, 3, 3) }, 1, 0);
        t.Controls.Add(L("Title has"), 0, 1); t.Controls.Add(_title, 1, 1);
        t.Controls.Add(new Label(), 0, 2); t.Controls.Add(new Label { Text = "Leave empty to take any window of this program.", AutoSize = true, Tag = "dim" }, 1, 2);
        t.Controls.Add(L("Open with"), 0, 3); t.Controls.Add(_launch, 1, 3);
        t.Controls.Add(L("Arguments"), 0, 4); t.Controls.Add(_args, 1, 4);
        t.Controls.Add(new Label(), 0, 5); t.Controls.Add(_open, 1, 5);
        var pos = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        pos.Controls.AddRange(new Control[] { Small("X"), _x, Small("Y"), _y, Small("W"), _w, Small("H"), _h });
        t.Controls.Add(L("Where"), 0, 6); t.Controls.Add(pos, 1, 6);
        var st = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        var grab = Theme.MakeButton("Use where it is now", 160);
        st.Controls.AddRange(new Control[] { _state, grab });
        t.Controls.Add(L("Shows"), 0, 7); t.Controls.Add(st, 1, 7);
        t.Controls.Add(new Label(), 0, 8); t.Controls.Add(new Label { Text = "Tip: drag a shortcut onto \"Open with\" (Chrome web apps, Store apps).", AutoSize = true, Tag = "dim", Margin = new Padding(3, 8, 3, 3) }, 1, 8);
        Controls.Add(t); t.BringToFront();

        _state.Items.AddRange(new object[] { "Normal", "Maximized", "Minimized" });
        _title.Text = it.TitleContains; _launch.Text = it.Launch; _args.Text = it.Args; _open.Checked = it.OpenIfClosed;
        _x.Value = it.X; _y.Value = it.Y; _w.Value = Math.Max(0, it.W); _h.Value = Math.Max(0, it.H); _state.SelectedItem = it.State ?? "Normal";
        _launch.DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
        _launch.DragDrop += (s, e) => { _launch.Text = ((string[])e.Data.GetData(DataFormats.FileDrop))[0]; _args.Text = ""; };
        grab.Click += (s, e) =>
        {
            var w = Workspaces.OpenWindows().FirstOrDefault(x => string.Equals(x.Exe, it.Exe, StringComparison.OrdinalIgnoreCase)
                && (_title.Text == "" || x.Title.Contains(_title.Text, StringComparison.OrdinalIgnoreCase)));
            if (w == null) { MessageBox.Show(this, "That window isn't open right now.", "LaunchPad"); return; }
            _x.Value = w.Rect.Left; _y.Value = w.Rect.Top; _w.Value = w.Rect.Right - w.Rect.Left; _h.Value = w.Rect.Bottom - w.Rect.Top; _state.SelectedItem = w.State;
        };
    }

    static Label Small(string t) => new() { Text = t, AutoSize = true, Margin = new Padding(6, 8, 2, 3) };

    protected override bool Check()
    {
        _it.TitleContains = _title.Text.Trim(); _it.Launch = _launch.Text.Trim(); _it.Args = _args.Text.Trim(); _it.OpenIfClosed = _open.Checked;
        _it.X = (int)_x.Value; _it.Y = (int)_y.Value; _it.W = (int)_w.Value; _it.H = (int)_h.Value; _it.State = _state.SelectedItem as string ?? "Normal";
        return true;
    }
}

/// <summary>Small "type a name" box.</summary>
class TextAskDialog : ThemedDialog
{
    readonly TextBox _tb = new() { Dock = DockStyle.Top };
    public string Value => _tb.Text.Trim() == "" ? null : _tb.Text.Trim();

    public TextAskDialog(string title, string start) : base(title, 380, 130)
    {
        var p = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 14, 12, 0) };
        _tb.Text = start; p.Controls.Add(_tb); Controls.Add(p); p.BringToFront();
        AcceptButton = Ok;
        Shown += (s, e) => { _tb.Focus(); _tb.SelectAll(); };
    }
}

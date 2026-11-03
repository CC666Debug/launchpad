using System.Diagnostics;

namespace LaunchPad;

/// <summary>One thing Quick Search can find.</summary>
class Found
{
    public string Key;        // for counting how often you pick it
    public string Title, Sub;
    public string IconSrc;    // path for the icon, or "@kind"
    public Func<Task> Run;
    public int Score;
}

/// <summary>The pop-up search box. One hotkey anywhere, type, Enter.</summary>
static class QuickSearch
{
    static SearchForm _form;
    public static HotkeyStore Store;
    public static Action Save;

    public static void Show()
    {
        if (Store == null) return;
        if (_form == null || _form.IsDisposed) _form = new SearchForm();
        _form.Popup();
    }

    // ---------- what it searches ----------
    static List<Found> _apps; static DateTime _appsAt;

    static List<Found> Apps()
    {
        if (_apps != null && (DateTime.Now - _appsAt).TotalMinutes < 5) return _apps;
        var list = new List<Found>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Start menu shortcuts (desktop programs)
        foreach (var dir in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Programs), Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms) })
            if (Directory.Exists(dir))
                foreach (var f in SafeFiles(dir, "*.lnk"))
                {
                    var n = Path.GetFileNameWithoutExtension(f);
                    if (n.Contains("uninstall", StringComparison.OrdinalIgnoreCase) || !seen.Add(n)) continue;
                    var path = f;
                    list.Add(new Found { Key = "app:" + n, Title = n, Sub = "App", IconSrc = f, Run = () => { Start(path); return Task.CompletedTask; } });
                }
        // Store apps and anything else Windows lists under "All apps"
        try
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
            dynamic folder = shell.Namespace("shell:AppsFolder");
            foreach (dynamic item in folder.Items())
            {
                string n = item.Name, id = item.Path;
                if (string.IsNullOrEmpty(n) || !seen.Add(n)) continue;
                list.Add(new Found { Key = "app:" + n, Title = n, Sub = "App", IconSrc = "@app",
                    Run = () => { Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\" + id) { UseShellExecute = true }); return Task.CompletedTask; } });
            }
        }
        catch { }
        _apps = list; _appsAt = DateTime.Now;
        return list;
    }

    static IEnumerable<string> SafeFiles(string dir, string pattern)
    {
        try { return Directory.EnumerateFiles(dir, pattern, new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).ToList(); }
        catch { return Array.Empty<string>(); }
    }

    static void Start(string target)
    {
        Native.AllowSetForegroundWindow(-1);
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }

    public static List<Found> Everything()
    {
        var s = Store.Search;
        var all = new List<Found>();
        if (s.Hotkeys)
            foreach (var h in Store.Hotkeys.Where(h => h.Actions.Count > 0))
            {
                var hk = h;
                var first = h.Actions[0];
                string icon = first.Kind == ActionKind.Open ? Icons.Resolve(first.Target) : "@hotkey";
                all.Add(new Found { Key = "hk:" + h.Id, Title = h.DisplayName, Sub = "Hotkey" + (h.Combo != "" ? " · " + h.Combo : "") + " · " + h.Category, IconSrc = icon, Run = () => Runner.Run(hk) });
            }
        if (s.Launchers && Directory.Exists(Paths.Launchers))
            foreach (var exe in Directory.GetFiles(Paths.Launchers, "*.exe"))
            {
                var path = exe;
                all.Add(new Found { Key = "ln:" + Path.GetFileName(exe), Title = Path.GetFileNameWithoutExtension(exe), Sub = "Launcher · " + (FileVersionInfo.GetVersionInfo(exe).FileDescription ?? ""), IconSrc = exe,
                    Run = () => { Start(path); return Task.CompletedTask; } });
            }
        if (s.Workspaces)
            foreach (var w in Store.Workspaces)
            {
                var ws = w;
                all.Add(new Found { Key = "ws:" + w.Id, Title = w.Name, Sub = $"Workspace · {w.Items.Count} windows", IconSrc = "@workspace", Run = () => Workspaces.Open(ws) });
            }
        if (s.Snippets)
            foreach (var sn in Store.Snippets)
            {
                var snip = sn;
                all.Add(new Found { Key = "sn:" + sn.Id, Title = string.IsNullOrEmpty(sn.Name) ? sn.Abbrev : sn.Name, Sub = "Snippet · " + sn.Abbrev + " · pastes it", IconSrc = "@snippet",
                    Run = async () => { await Task.Delay(150); await SnippetEngine.Expand(0, snip.Text); } });
            }
        if (s.Desktop)
            foreach (var dir in new[] { Paths.Desktop, Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory) })
                if (Directory.Exists(dir))
                    foreach (var f in Directory.GetFiles(dir).Where(f => !f.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)))
                    {
                        var path = f;
                        all.Add(new Found { Key = "dt:" + Path.GetFileName(f), Title = Path.GetFileNameWithoutExtension(f), Sub = "Desktop", IconSrc = f, Run = () => { Start(path); return Task.CompletedTask; } });
                    }
        if (s.Apps) all.AddRange(Apps());
        return all;
    }

    /// <summary>Best matches first: exact, starts with, word starts, initials, contains, letters in order. Things you use often float up.</summary>
    public static List<Found> Find(List<Found> all, string q)
    {
        q = q.Trim();
        var uses = Store.Search.Uses;
        var res = new List<Found>();
        if (q == "")
        {
            res = all.Where(f => uses.ContainsKey(f.Key)).OrderByDescending(f => uses[f.Key]).Take(10).ToList();
            return res;
        }
        var ql = q.ToLowerInvariant();
        foreach (var f in all)
        {
            var t = f.Title.ToLowerInvariant();
            int sc = 0;
            if (t == ql) sc = 100;
            else if (t.StartsWith(ql)) sc = 85;
            else if (Words(t).Any(w => w.StartsWith(ql))) sc = 70;
            else if (Initials(t).StartsWith(ql)) sc = 62;
            else if (t.Contains(ql)) sc = 50;
            else if (InOrder(t, ql)) sc = 25;
            else if (f.Sub != null && f.Sub.ToLowerInvariant().Contains(ql)) sc = 15;
            if (sc == 0) continue;
            if (uses.TryGetValue(f.Key, out var u)) sc += Math.Min(u * 3, 30);
            if (f.Sub == "App" || f.Sub == "Desktop") sc -= 1;   // your own stuff wins ties
            f.Score = sc; res.Add(f);
        }
        res = res.OrderByDescending(f => f.Score).ThenBy(f => f.Title.Length)
                 .GroupBy(f => f.Title.ToLowerInvariant() + "|" + f.Sub).Select(g => g.First()).Take(12).ToList();

        // typed a web address or a path? offer it. Otherwise: search the web.
        if (q.Contains('.') && !q.Contains(' ') && !Path.IsPathRooted(q))
        {
            var url = q.Contains("://") ? q : "https://" + q;
            res.Insert(0, new Found { Key = "", Title = "Open " + url, Sub = "Website", IconSrc = "@web", Run = () => { Start(url); return Task.CompletedTask; } });
        }
        else if ((Path.IsPathRooted(q) || q.StartsWith("\\\\")) && (File.Exists(q) || Directory.Exists(q)))
            res.Insert(0, new Found { Key = "", Title = "Open " + q, Sub = "File or folder", IconSrc = q, Run = () => { Start(q); return Task.CompletedTask; } });
        if (Store.Search.Web)
        {
            var url = Store.Search.WebUrl + Uri.EscapeDataString(q);
            res.Add(new Found { Key = "", Title = $"Search the web for “{q}”", Sub = "Web search", IconSrc = "@web", Run = () => { Start(url); return Task.CompletedTask; } });
        }
        return res;
    }

    static IEnumerable<string> Words(string t) => t.Split(new[] { ' ', '-', '_', '.', '(', ')', '[', ']', '·' }, StringSplitOptions.RemoveEmptyEntries);
    static string Initials(string t) => new(Words(t).Select(w => w[0]).ToArray());
    static bool InOrder(string t, string q) { int i = 0; foreach (var c in t) if (i < q.Length && c == q[i]) i++; return i == q.Length && q.Length >= 2; }

    public static void Used(Found f)
    {
        if (string.IsNullOrEmpty(f.Key)) return;
        var u = Store.Search.Uses;
        u[f.Key] = u.GetValueOrDefault(f.Key) + 1;
        if (u.Count > 400) foreach (var k in u.OrderBy(p => p.Value).Take(100).Select(p => p.Key).ToList()) u.Remove(k);
        Save?.Invoke();
    }
}

class SearchForm : Form
{
    readonly TextBox _box = new() { Dock = DockStyle.Top, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 16) };
    readonly ListBox _list = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 46, IntegralHeight = false };
    readonly Panel _boxPanel = new() { Dock = DockStyle.Top, Height = 54, Padding = new Padding(16, 12, 16, 6) };
    readonly Dictionary<string, Bitmap> _icons = new();
    List<Found> _all = new(), _shown = new();

    public SearchForm()
    {
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; KeyPreview = true;
        StartPosition = FormStartPosition.Manual; Size = new Size(680, 470); Text = "LaunchPad Quick Search";
        Font = new Font("Segoe UI", 10); Padding = new Padding(1);
        _box.PlaceholderText = "Search apps, hotkeys, launchers, workspaces, snippets…";
        _boxPanel.Controls.Add(_box);
        var line = new Panel { Dock = DockStyle.Top, Height = 2, Tag = "keepcolor" };
        Controls.Add(_list); Controls.Add(line); Controls.Add(_boxPanel);
        _box.TextChanged += (s, e) => Refill();
        _list.DrawItem += DrawRow;
        _list.MouseClick += (s, e) => { int i = _list.IndexFromPoint(e.Location); if (i >= 0) { _list.SelectedIndex = i; Go(); } };
        _list.MouseMove += (s, e) => { int i = _list.IndexFromPoint(e.Location); if (i >= 0 && i != _list.SelectedIndex) _list.SelectedIndex = i; };
        KeyDown += OnKeys;
        // click away = close, but only once it has really been in front (so it never vanishes on its own)
        Activated += (s, e) => _wasActive = true;
        Deactivate += (s, e) => { if (_wasActive) Hide(); };
        Paint += (s, e) => { using var p = new Pen(Theme.Accent); e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1); };
        HandleCreated += (s, e) =>
        {
            int round = 2; Native.DwmSetWindowAttribute(Handle, 33, ref round, 4);   // rounded corners on Windows 11
            Recolor();
        };
        Theme.Changed += Recolor;
        void Recolor() { Theme.Apply(this); BackColor = Theme.Accent; _boxPanel.BackColor = _box.BackColor = _list.BackColor = Theme.Box; line.BackColor = Theme.Accent; _box.ForeColor = Theme.Fore; _list.Invalidate(); }
    }

    protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x80; return cp; } } // no Alt+Tab entry

    bool _wasActive;

    public void Popup()
    {
        _wasActive = false;
        _all = QuickSearch.Everything();
        var scr = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(scr.Left + (scr.Width - Width) / 2, scr.Top + scr.Height / 5);
        _box.Text = "";
        Refill();
        Show(); Activate(); Native.SetForegroundWindow(Handle);
        _box.Focus();
    }

    void Refill()
    {
        _shown = QuickSearch.Find(_all, _box.Text);
        _list.BeginUpdate(); _list.Items.Clear();
        foreach (var f in _shown) _list.Items.Add(f.Title);
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        _list.EndUpdate();
    }

    void OnKeys(object s, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) { Hide(); e.Handled = e.SuppressKeyPress = true; }
        else if (e.KeyCode == Keys.Enter) { Go(); e.Handled = e.SuppressKeyPress = true; }
        else if (e.KeyCode is Keys.Down or Keys.Up && _list.Items.Count > 0)
        {
            int i = _list.SelectedIndex + (e.KeyCode == Keys.Down ? 1 : -1);
            _list.SelectedIndex = (i + _list.Items.Count) % _list.Items.Count;
            e.Handled = e.SuppressKeyPress = true;
        }
    }

    async void Go()
    {
        int i = _list.SelectedIndex;
        if (i < 0 || i >= _shown.Count) return;
        var f = _shown[i];
        Hide();
        QuickSearch.Used(f);
        try { await f.Run(); } catch (Exception ex) { Tray.Tip("LaunchPad", "Couldn't open " + f.Title + ": " + ex.Message, true); }
    }

    Bitmap IconFor(string src)
    {
        if (src == null) return null;
        if (_icons.TryGetValue(src, out var b)) return b;
        b = src switch
        {
            "@hotkey" => Icons.FromLocation(@"%SystemRoot%\System32\shell32.dll,173", 32) ?? Icons.FromLocation(@"%SystemRoot%\System32\shell32.dll,24", 32),
            "@workspace" => Icons.FromLocation(@"%SystemRoot%\System32\imageres.dll,-27", 32) ?? Icons.FromLocation(@"%SystemRoot%\System32\shell32.dll,34", 32),
            "@snippet" => Icons.FromLocation(@"%SystemRoot%\System32\shell32.dll,260", 32) ?? Icons.FromLocation(@"%SystemRoot%\System32\shell32.dll,70", 32),
            "@web" => Icons.Shell32("https://x"),
            "@app" => Icons.FromLocation(@"%SystemRoot%\System32\imageres.dll,-15", 32) ?? Icons.FromLocation(@"%SystemRoot%\System32\shell32.dll,2", 32),
            _ => Icons.Shell32(src)
        };
        _icons[src] = b;
        return b;
    }

    void DrawRow(object s, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _shown.Count) return;
        var f = _shown[e.Index];
        bool sel = (e.State & DrawItemState.Selected) != 0;
        var g = e.Graphics;
        using (var bg = new SolidBrush(sel ? Theme.Mix(Theme.Accent, Theme.Box, 0.40) : Theme.Box)) g.FillRectangle(bg, e.Bounds);
        if (sel) using (var bar = new SolidBrush(Theme.Accent)) g.FillRectangle(bar, e.Bounds.X, e.Bounds.Y + 6, 3, e.Bounds.Height - 12);
        var icon = IconFor(f.IconSrc);
        if (icon != null) g.DrawImage(icon, e.Bounds.X + 16, e.Bounds.Y + 7, 32, 32);
        var x = e.Bounds.X + 60;
        TextRenderer.DrawText(g, f.Title, new Font("Segoe UI", 11), new Rectangle(x, e.Bounds.Y + 3, e.Bounds.Width - x - 10, 24), Theme.Fore, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, f.Sub ?? "", Font, new Rectangle(x, e.Bounds.Y + 24, e.Bounds.Width - x - 10, 20), Theme.Dim, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

/// <summary>Search tab: the hotkey and what Quick Search looks through.</summary>
class SearchPage : UserControl
{
    readonly HotkeyStore _store;
    readonly Action _changed;
    readonly CheckBox _win = new() { Text = "Win", AutoSize = true }, _ctrl = new() { Text = "Ctrl", AutoSize = true },
                      _alt = new() { Text = "Alt", AutoSize = true }, _shift = new() { Text = "Shift", AutoSize = true };
    readonly KeyBox _key = new() { Width = 120 };
    readonly Label _status = new() { AutoSize = true, Tag = "keepcolor", Margin = new Padding(3, 6, 3, 3) };
    public Func<string> StatusText;

    public SearchPage(HotkeyStore store, Action changed)
    {
        _store = store; _changed = changed; Dock = DockStyle.Fill;
        var s = store.Search;
        var body = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(16, 12, 16, 12), AutoScroll = true };
        Controls.Add(body);

        body.Controls.Add(new Label { Text = "Quick Search", AutoSize = true, Font = new Font("Segoe UI Semibold", 14) });
        body.Controls.Add(new Label { Text = "Press the keys below anywhere in Windows, type a few letters, press Enter. Esc closes it.", AutoSize = true, Tag = "dim", Margin = new Padding(3, 2, 3, 12) });

        var on = new CheckBox { Text = "Quick Search on", AutoSize = true, Checked = s.Enabled };
        on.CheckedChanged += (o, e) => { s.Enabled = on.Checked; _changed(); ShowStatus(); };
        body.Controls.Add(on);

        var keys = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
        keys.Controls.Add(new Label { Text = "Keys:", AutoSize = true, Margin = new Padding(3, 7, 10, 3) });
        foreach (var c in new Control[] { _win, _ctrl, _alt, _shift }) { c.Margin = new Padding(3, 7, 6, 3); keys.Controls.Add(c); }
        keys.Controls.Add(new Label { Text = "+", AutoSize = true, Margin = new Padding(3, 7, 6, 3) });
        keys.Controls.Add(_key);
        var apply = Theme.MakeButton("Use these keys", 130); keys.Controls.Add(apply);
        var tryIt = Theme.MakeButton("Try it", 80); keys.Controls.Add(tryIt);
        body.Controls.Add(keys);
        body.Controls.Add(_status);

        _win.Checked = (s.Mods & Native.MOD_WIN) != 0; _ctrl.Checked = (s.Mods & Native.MOD_CONTROL) != 0; _alt.Checked = (s.Mods & Native.MOD_ALT) != 0; _shift.Checked = (s.Mods & Native.MOD_SHIFT) != 0;
        _key.Vk = s.Key;
        _key.Captured += mods => { if (mods != 0) { _win.Checked = (mods & 8) != 0; _ctrl.Checked = (mods & 2) != 0; _alt.Checked = (mods & 1) != 0; _shift.Checked = (mods & 4) != 0; } };
        apply.Click += (o, e) =>
        {
            uint m = (_win.Checked ? Native.MOD_WIN : 0) | (_ctrl.Checked ? Native.MOD_CONTROL : 0) | (_alt.Checked ? Native.MOD_ALT : 0) | (_shift.Checked ? Native.MOD_SHIFT : 0);
            if (_key.Vk == 0 || m == 0) { MessageBox.Show(this, "Pick a key and at least one of Win, Ctrl, Alt.", "LaunchPad"); return; }
            var clash = _store.Hotkeys.FirstOrDefault(h => h.Key == _key.Vk && h.Mods == m);
            if (clash != null) { MessageBox.Show(this, $"\"{clash.DisplayName}\" already uses those keys.", "LaunchPad"); return; }
            var (level, why) = KeyNames.Warn(m, _key.Vk);
            if (level == 2) { MessageBox.Show(this, why + " Pick other keys.", "LaunchPad"); return; }
            bool mine = m == s.Mods && _key.Vk == s.Key;
            if (!mine && KeyNames.HeldByOtherApp(Handle, m, _key.Vk)) { MessageBox.Show(this, $"Another program on this PC already uses {KeyNames.Combo(m, _key.Vk)}.", "LaunchPad"); return; }
            if (level == 1 && MessageBox.Show(this, why + "\n\nUse it anyway?", "LaunchPad", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            s.Mods = m; s.Key = _key.Vk; _changed(); ShowStatus();
        };
        tryIt.Click += (o, e) => QuickSearch.Show();

        body.Controls.Add(new Label { Text = "Look through:", AutoSize = true, Margin = new Padding(3, 16, 3, 4) });
        void Opt(string text, Func<bool> get, Action<bool> set)
        {
            var cb = new CheckBox { Text = text, AutoSize = true, Checked = get(), Margin = new Padding(16, 2, 3, 2) };
            cb.CheckedChanged += (o, e) => { set(cb.Checked); _changed(); };
            body.Controls.Add(cb);
        }
        Opt("Apps (Start menu and Store apps)", () => s.Apps, v => s.Apps = v);
        Opt("Desktop shortcuts and files", () => s.Desktop, v => s.Desktop = v);
        Opt("Your hotkeys (runs what they do)", () => s.Hotkeys, v => s.Hotkeys = v);
        Opt("Your launchers", () => s.Launchers, v => s.Launchers = v);
        Opt("Your workspaces", () => s.Workspaces, v => s.Workspaces = v);
        Opt("Your snippets (pastes them)", () => s.Snippets, v => s.Snippets = v);
        Opt("Offer a web search", () => s.Web, v => s.Web = v);
        var clear = Theme.MakeButton("Forget what I picked most", 210); clear.Margin = new Padding(16, 12, 3, 3);
        clear.Click += (o, e) => { s.Uses.Clear(); _changed(); };
        body.Controls.Add(clear);
        ShowStatus();
    }

    public void ShowStatus()
    {
        var s = _store.Search;
        var st = StatusText?.Invoke() ?? "";
        if (!s.Enabled) { _status.ForeColor = Theme.Dim; _status.Text = "Off."; }
        else if (st == "") { _status.ForeColor = Theme.Good; _status.Text = $"Ready: press {KeyNames.Combo(s.Mods, s.Key)}."; }
        else { _status.ForeColor = Theme.Bad; _status.Text = $"{KeyNames.Combo(s.Mods, s.Key)}: {st}. Pick other keys."; }
    }
}

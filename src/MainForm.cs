namespace LaunchPad;

class MainForm : Form
{
    readonly HotkeyStore _store = HotkeyStore.Load();
    readonly HotkeyManager _mgr = new();
    readonly Panel _content = new() { Dock = DockStyle.Fill };
    readonly Button _theme = new() { Width = 44, Height = 36, Font = new Font("Segoe UI Symbol", 14), Margin = new Padding(3, 6, 8, 3) };
    readonly LaunchersPage _launchers;
    readonly HotkeysPage _hotkeys;
    readonly WorkspacesPage _workspaces;
    readonly SnippetsPage _snippets;
    readonly SearchPage _search;
    readonly List<(Button tab, Control page)> _tabs = new();
    SnippetEngine _snip;
    readonly NotifyIcon _tray = new();
    readonly ToolStripMenuItem _miPause = new("Pause all hotkeys"), _miSnip = new("Snippets on"), _miStartup = new("Start with Windows");
    readonly System.Windows.Forms.Timer _retry = new() { Interval = 10000 };
    bool _quitting, _toldTray;

    // default tab order (you can drag them around; the order is remembered)
    static readonly string[] DefaultOrder = { "Hotkeys", "Launchers", "Workspaces", "Snippets", "Search" };

    public MainForm(bool startHidden)
    {
        Text = "LaunchPad"; Icon = Icons.AppIcon(); Font = new Font("Segoe UI", 10);
        ClientSize = new Size(1040, 620); MinimumSize = new Size(800, 440); StartPosition = FormStartPosition.CenterScreen;
        Runner.Owner = this; Runner.Store = _store;
        QuickSearch.Store = _store; QuickSearch.Save = () => { try { _store.Save(); } catch { } };

        _mgr.Apply(_store);
        _launchers = new LaunchersPage();
        _hotkeys = new HotkeysPage(_store, _mgr, Changed);
        _hotkeys.Restart = () => { Program.RestartRequested = true; _quitting = true; Close(); };
        _workspaces = new WorkspacesPage(_store, Changed);
        _workspaces.MakeHotkey = ws => { if (ws != null) { _hotkeys.AddWith(new HotAction { Kind = ActionKind.Workspace, WorkspaceId = ws.Id }, ws.Name); ShowPage(_hotkeys); } };
        _snippets = new SnippetsPage(_store, Changed);
        _search = new SearchPage(_store, Changed) { StatusText = () => _mgr.SearchStatus };

        // header: tabs on the left, sun/moon on the right
        var header = new Panel { Dock = DockStyle.Top, Height = 48 };
        var tabs = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, Padding = new Padding(6, 4, 0, 0), WrapContents = false };
        var pages = new Dictionary<string, Control> { ["Hotkeys"] = _hotkeys, ["Launchers"] = _launchers, ["Workspaces"] = _workspaces, ["Snippets"] = _snippets, ["Search"] = _search };
        var order = new List<string>();
        try { order.AddRange(File.ReadAllText(Paths.TabsFile).Trim().Split(',').Where(pages.ContainsKey)); } catch { }
        foreach (var n in DefaultOrder) if (!order.Contains(n)) order.Add(n);   // new tabs show up at the end
        var tip = new ToolTip();
        foreach (var name in order)
        {
            var b = new Button { Text = name, Tag = "tab", Width = 118, Height = 40, FlatStyle = FlatStyle.Flat, Margin = new Padding(2, 0, 2, 0), Font = new Font("Segoe UI Semibold", 11) };
            b.FlatAppearance.BorderSize = 0;
            b.Paint += PaintTab; tabs.Controls.Add(b);
            var page = pages[name];
            _tabs.Add((b, page));
            b.Click += (s, e) => { if (!_dragged) ShowPage(page); };
            DragTab(b, tabs);
            tip.SetToolTip(b, "Drag a tab left or right to reorder");
        }
        var right = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        right.Controls.Add(_theme);
        header.Controls.Add(tabs); header.Controls.Add(right);
        Controls.Add(_content); Controls.Add(header);

        // tap: light/dark. Hold it (or right-click) for colors and accent, like Markpad
        var hold = new System.Windows.Forms.Timer { Interval = 500 };
        bool held = false;
        hold.Tick += (s, e) => { hold.Stop(); held = true; OpenThemes(); };
        _theme.MouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Right) { held = true; OpenThemes(); return; }
            held = false; hold.Start();
        };
        _theme.MouseUp += (s, e) => hold.Stop();
        _theme.MouseLeave += (s, e) => hold.Stop();
        _theme.Click += (s, e) => { if (held) { held = false; return; } Theme.Toggle(); };
        tip.SetToolTip(_theme, "Light/dark (hold for theme colors)");
        Theme.Changed += ApplyTheme;
        ShowPage(pages[order[0]]);

        // tray
        _tray.Icon = Icon; _tray.Text = "LaunchPad"; _tray.Visible = true;
        Tray.Icon = _tray;
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open LaunchPad", null, (s, e) => ShowMe());
        menu.Items.Add("Quick Search", null, (s, e) => QuickSearch.Show());
        var wsMenu = new ToolStripMenuItem("Open workspace");
        menu.Items.Add(wsMenu);
        menu.Items.Add("Hotkey cheat sheet", null, (s, e) => CheatSheet.Open(_store));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_miPause); menu.Items.Add(_miSnip); menu.Items.Add(_miStartup);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (s, e) => { _quitting = true; Close(); });
        _tray.ContextMenuStrip = menu;
        Theme.Menu(menu);
        _tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowMe(); };
        _miPause.Click += (s, e) => { _store.Paused = !_store.Paused; Changed(); _hotkeys.Refill(); };
        _miSnip.Click += (s, e) => { _store.SnippetsOn = !_store.SnippetsOn; Changed(); };
        _miStartup.Click += (s, e) => { Startup.Enabled = !Startup.Enabled; _miStartup.Checked = Startup.Enabled; };
        menu.Opening += (s, e) =>
        {
            _miPause.Checked = _store.Paused; _miSnip.Checked = _store.SnippetsOn; _miStartup.Checked = Startup.Enabled;
            wsMenu.DropDownItems.Clear();
            foreach (var w in _store.Workspaces) { var ws = w; wsMenu.DropDownItems.Add(w.Name, null, async (o, a) => await Workspaces.Open(ws)); }
            wsMenu.Enabled = _store.Workspaces.Count > 0;
        };

        // keys another app was holding (e.g. Keyboard Launchpad): try again now and then
        _retry.Tick += (s, e) => { if (_mgr.AnyBlocked) { _mgr.Apply(_store); _hotkeys.Refill(); _search.ShowStatus(); } };
        _retry.Start();

        if (startHidden) { WindowState = FormWindowState.Minimized; ShowInTaskbar = false; _toldTray = true; }
        Load += (s, e) => { if (startHidden) Hide(); };
        HandleCreated += (s, e) => { ApplyTheme(); _snip ??= new SnippetEngine(_store, this); };
    }

    bool _dragged;
    ThemeDialog _themeDlg;

    public void OpenThemes()
    {
        if (_themeDlg is { IsDisposed: false }) { _themeDlg.Activate(); return; }
        _themeDlg = new ThemeDialog { StartPosition = FormStartPosition.Manual };
        // pin it just under the sun/moon button, kept on the same screen
        var btn = _theme.PointToScreen(new Point(_theme.Width, _theme.Height + 4));
        var area = Screen.FromControl(this).WorkingArea;
        int x = Math.Clamp(btn.X - _themeDlg.Width, area.Left, area.Right - _themeDlg.Width);
        int y = Math.Clamp(btn.Y, area.Top, area.Bottom - _themeDlg.Height);
        _themeDlg.Location = new Point(x, y);
        _themeDlg.Show(this);
    }

    /// <summary>Hold a tab and slide it past another to swap them.</summary>
    void DragTab(Button b, FlowLayoutPanel tabs)
    {
        Point start = Point.Empty; bool down = false;
        b.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { down = true; _dragged = false; start = e.Location; } };
        b.MouseMove += (s, e) =>
        {
            if (!down || Math.Abs(e.X - start.X) < 8) return;
            _dragged = true;
            var p = tabs.PointToClient(b.PointToScreen(e.Location));
            foreach (Control other in tabs.Controls)
                if (other != b && other.Bounds.Contains(new Point(p.X, other.Top + 5)))
                {
                    tabs.Controls.SetChildIndex(b, tabs.Controls.GetChildIndex(other));
                    break;
                }
        };
        b.MouseUp += (s, e) =>
        {
            down = false;
            if (!_dragged) return;
            var names = tabs.Controls.Cast<Control>().Select(c => c.Text);
            try { Directory.CreateDirectory(Paths.Root); File.WriteAllText(Paths.TabsFile, string.Join(",", names)); } catch { }
        };
    }

    void Changed()
    {
        try { _store.Save(); } catch (Exception e) { MessageBox.Show("Couldn't save: " + e.Message, "LaunchPad"); }
        _mgr.Apply(_store);
        _snip?.Refresh();
        _search?.ShowStatus();
    }

    void ShowPage(Control page)
    {
        _content.Controls.Clear();
        _content.Controls.Add(page);
        foreach (var t in _tabs) t.tab.Invalidate();
        if (page == _workspaces) _workspaces.Fill();
        if (page == _snippets) _snippets.Fill();
        if (page == _search) _search.ShowStatus();
        Theme.Apply(page);
    }

    void PaintTab(object sender, PaintEventArgs e)
    {
        var b = (Button)sender;
        var page = _tabs.FirstOrDefault(t => t.tab == b).page;
        bool on = _content.Controls.Count > 0 && _content.Controls[0] == page;
        e.Graphics.Clear(Theme.Back);
        TextRenderer.DrawText(e.Graphics, b.Text, b.Font, b.ClientRectangle, on ? Theme.Fore : Theme.Dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        if (on) using (var br = new SolidBrush(Theme.Accent)) e.Graphics.FillRectangle(br, 14, b.Height - 4, b.Width - 28, 3);
    }

    void ApplyTheme()
    {
        _theme.Text = Theme.Dark ? char.ConvertFromUtf32(0x2600) : char.ConvertFromUtf32(0x1F319); // sun in dark, moon in light
        Theme.Apply(this);
        foreach (var t in _tabs) { Theme.Apply(t.page); t.tab.Invalidate(); }
        _hotkeys.Refill(); _snippets.Fill(); _search.ShowStatus();
    }

    public void ShowMe()
    {
        ShowInTaskbar = true;
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // closing the window keeps hotkeys running in the tray
        if (!_quitting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true; Hide();
            if (!_toldTray) { Tray.Tip("LaunchPad is still running", "Your hotkeys keep working. Right-click the tray icon to exit.", false); _toldTray = true; }
            return;
        }
        _tray.Visible = false; _tray.Dispose();
        _snip?.Dispose();
        _mgr.Dispose();
        base.OnFormClosing(e);
    }

    public HotkeysPage Hotkeys => _hotkeys;
    public HotkeyStore Store => _store;
}

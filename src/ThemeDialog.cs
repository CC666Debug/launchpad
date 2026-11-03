namespace LaunchPad;

/// <summary>Theme window, like Markpad's: color, light/dark, accent. Changes show right away.</summary>
class ThemeDialog : ThemedDialog
{
    readonly List<(Button b, string group, string val)> _sw = new();

    public ThemeDialog() : base("Theme", 600, 380)
    {
        Cancel.Visible = false; Ok.Text = "Done";
        var body = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(14, 10, 14, 0), AutoScroll = true };
        Controls.Add(body); body.BringToFront();

        body.Controls.Add(Head("Color"));
        body.Controls.Add(Row(Theme.Palettes.Select(p => Swatch("palette", p.Key, p.Name, p.Swatch))));
        body.Controls.Add(Head("Light or dark"));
        body.Controls.Add(Row(new[] { Swatch("mode", "light", "Light", "#f3f2ef"), Swatch("mode", "dark", "Dark", "#25262a"), Swatch("mode", "", "Like Windows", "half") }));
        body.Controls.Add(Head("Accent (tabs, highlights)"));
        var accents = Theme.Accents.Select(a => Swatch("accent", a.Hex, a.Name, a.Hex == "" ? "own" : a.Hex)).ToList();
        var custom = Theme.MakeButton("Custom…", 100); custom.Height = 32;
        custom.Click += (s, e) =>
        {
            using var cd = new ColorDialog { FullOpen = true, Color = Theme.Accent };
            if (cd.ShowDialog(this) == DialogResult.OK) { Theme.SetAccent(ColorTranslator.ToHtml(cd.Color)); Refresh2(); }
        };
        var accRow = Row(accents); accRow.Controls.Add(custom);
        body.Controls.Add(accRow);

        Theme.Changed += OnChanged;
        FormClosed += (s, e) => Theme.Changed -= OnChanged;
    }

    void OnChanged() { Theme.Apply(this); Refresh2(); }

    static Label Head(string t) => new() { Text = t, AutoSize = true, Tag = "dim", Margin = new Padding(2, 10, 2, 4) };

    static FlowLayoutPanel Row(IEnumerable<Control> items)
    {
        var r = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(560, 0), Margin = new Padding(0) };
        foreach (var c in items) r.Controls.Add(c);
        return r;
    }

    Button Swatch(string group, string val, string name, string color)
    {
        var b = new Button { Text = name, Tag = "tab", Height = 32, AutoSize = true, Padding = new Padding(26, 0, 8, 0), Margin = new Padding(3), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
        b.FlatAppearance.BorderSize = 0;
        b.Paint += (s, e) =>
        {
            bool on = IsOn(group, val);
            var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Theme.Btn);
            using (var pen = new Pen(on ? Theme.Accent : Theme.Border, on ? 2 : 1)) g.DrawRectangle(pen, 1, 1, b.Width - 3, b.Height - 3);
            var dot = new Rectangle(8, (b.Height - 16) / 2, 16, 16);
            if (color == "half")
            {
                using var l = new SolidBrush(ColorTranslator.FromHtml("#f3f2ef")); using var d = new SolidBrush(ColorTranslator.FromHtml("#25262a"));
                g.FillPie(l, dot, 90, 180); g.FillPie(d, dot, 270, 180);
            }
            else
            {
                using var br = new SolidBrush(color == "own" ? ThemeOwnAccent() : ColorTranslator.FromHtml(color));
                g.FillEllipse(br, dot);
            }
            using (var pen = new Pen(Theme.Border)) g.DrawEllipse(pen, dot);
            TextRenderer.DrawText(g, b.Text, b.Font, new Rectangle(28, 0, b.Width - 30, b.Height), Theme.Fore, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        };
        b.Click += (s, e) =>
        {
            if (group == "palette") Theme.SetPalette(val);
            else if (group == "mode") Theme.SetMode(val);
            else Theme.SetAccent(val);
            Refresh2();
        };
        _sw.Add((b, group, val));
        return b;
    }

    static Color ThemeOwnAccent()
    {
        var keep = Theme.AccentHex; Theme.AccentHex = "";
        var c = Theme.Accent; Theme.AccentHex = keep;
        return c;
    }

    static bool IsOn(string group, string val) => group switch
    {
        "palette" => Theme.Palette == val,
        "mode" => Theme.Mode == val,
        _ => string.Equals(Theme.AccentHex, val, StringComparison.OrdinalIgnoreCase)
    };

    void Refresh2() { foreach (var s in _sw) s.b.Invalidate(); }
}

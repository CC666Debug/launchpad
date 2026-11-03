using System.Diagnostics;
using System.Net;
using System.Text;

namespace LaunchPad;

/// <summary>Printable page listing every hotkey, grouped by category.</summary>
static class CheatSheet
{
    public static void Open(HotkeyStore store)
    {
        string E(string s) => WebUtility.HtmlEncode(s ?? "");
        var sb = new StringBuilder();
        sb.Append("""
            <!doctype html><html><head><meta charset="utf-8"><title>LaunchPad hotkeys</title><style>
            :root{--bg:#fff;--fg:#1b1b1b;--dim:#666;--line:#ddd;--key:#f1ecf7;--acc:#7a24c9}
            @media (prefers-color-scheme:dark){:root{--bg:#1c1c1c;--fg:#eee;--dim:#aaa;--line:#3a3a3a;--key:#2d2238;--acc:#b65cff}}
            body{background:var(--bg);color:var(--fg);font:14px/1.4 "Segoe UI",sans-serif;margin:24px}
            h1{font-size:22px;margin:0 0 4px} p.sub{color:var(--dim);margin:0 0 18px}
            .cols{columns:2 340px;column-gap:28px}
            section{break-inside:avoid;margin:0 0 18px}
            h2{font-size:15px;color:var(--acc);border-bottom:2px solid var(--acc);padding-bottom:3px;margin:0 0 6px}
            .row{display:flex;justify-content:space-between;gap:12px;padding:3px 0;border-bottom:1px solid var(--line)}
            kbd{background:var(--key);border:1px solid var(--line);border-radius:4px;padding:0 6px;font:12px Consolas,monospace;white-space:nowrap}
            button{float:right;font:inherit;padding:4px 14px}
            @media print{button{display:none}:root{--bg:#fff;--fg:#000;--key:#eee;--line:#ccc;--acc:#000} body{margin:0}}
            </style></head><body>
            <button onclick="print()">Print</button>
            <h1>LaunchPad hotkeys</h1>
            """);
        var withKeys = store.Hotkeys.Where(h => h.Key != 0 && h.Enabled).ToList();
        sb.Append($"<p class=\"sub\">{withKeys.Count} hotkeys · {DateTime.Now:MMMM d, yyyy}</p><div class=\"cols\">");
        foreach (var cat in store.AllCategories())
        {
            var hs = withKeys.Where(h => string.Equals(h.Category, cat, StringComparison.OrdinalIgnoreCase)).OrderBy(h => h.DisplayName).ToList();
            if (hs.Count == 0) continue;
            sb.Append($"<section><h2>{E(cat)}</h2>");
            foreach (var h in hs) sb.Append($"<div class=\"row\"><span>{E(h.DisplayName)}</span><kbd>{E(h.Combo)}</kbd></div>");
            sb.Append("</section>");
        }
        sb.Append("</div></body></html>");
        Directory.CreateDirectory(Paths.Root);
        File.WriteAllText(Paths.CheatSheet, sb.ToString(), Encoding.UTF8);
        Process.Start(new ProcessStartInfo(Paths.CheatSheet) { UseShellExecute = true });
    }
}

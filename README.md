# LaunchPad

Free Windows tray app: **hotkeys that work anywhere**, **launchers** that open many things with one double-click, **workspaces** (saved window layouts), **Quick Search** and **snippets**.

**Download page:** https://cc666debug.github.io/launchpad/

| Download | |
|---|---|
| [LaunchPad-Standalone.exe](https://github.com/CC666Debug/launchpad/releases/latest/download/LaunchPad-Standalone.exe) | Runs on any Windows 10/11 PC |
| [LaunchPad.exe](https://github.com/CC666Debug/launchpad/releases/latest/download/LaunchPad.exe) | Small; needs the free [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) |

## Features
- **Hotkeys:** Ctrl / Alt / Shift / Win combos, system-wide. Each can open programs, files, folders or websites (with arguments, start-in folder, bring-to-front), paste saved text, run Windows commands (lock, sleep, show desktop, show/hide hidden files, close/minimize window, always on top…), press music/volume keys, click the mouse, or wait. Several actions per hotkey.
- **Categories** you can rename, drag-to-rearrange, search, Pause all, printable cheat sheet.
- **Launchers:** drag things in → one small `.exe` + Desktop shortcut with your own icon that opens them all. Hover shows what it opens.
- **Workspaces:** save where your windows are; one click / hotkey / `LaunchPad.exe --workspace "Name"` reopens and places them.
- **Quick Search:** Win+Alt+Space anywhere → type → Enter. Finds apps, Desktop items, hotkeys, launchers, workspaces, snippets, websites; learns what you pick.
- **Snippets:** type a short code (e.g. `;sig`) in any program and it becomes your saved text. `{date}`, `{time}`, `{longdate}`, `{clipboard}`.
- **Themes:** light / dark / like Windows, five color palettes, any accent.
- **Backups:** Export / Import one `.lpbackup` file. Imports Stardock Keyboard Launchpad hotkeys (`.ini`, `.keysettings`).

Your data lives in `%LOCALAPPDATA%\LaunchPad` (never in this repo).

## Build
.NET 10 SDK:

```
dotnet publish src -c Release -o out
```

Every push to `main` builds both downloads on GitHub Actions, posts them to Releases as **latest**, and refreshes the download page from `docs/`.

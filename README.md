# LaunchPad

Free Windows tray app: **hotkeys that work anywhere** plus **launchers** that open many things with one double-click.

**Download page:** https://cc666debug.github.io/launchpad/

| Download | |
|---|---|
| [LaunchPad-Standalone.exe](https://github.com/CC666Debug/launchpad/releases/latest/download/LaunchPad-Standalone.exe) | Runs on any Windows 10/11 PC |
| [LaunchPad.exe](https://github.com/CC666Debug/launchpad/releases/latest/download/LaunchPad.exe) | Small; needs the free [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) |

## Features
- **Hotkeys:** Ctrl / Alt / Shift / Win combos, system-wide. Each can open programs, files, folders or websites (with arguments, start-in folder, bring-to-front), paste saved text, run Windows commands (lock, sleep, show desktop, show/hide hidden files, close/minimize window, always on top…), press music/volume keys, click the mouse, or wait. Several actions per hotkey.
- **Categories** you can rename, drag-to-rearrange, search, Pause all, printable cheat sheet.
- **Launchers:** drag things in → one small `.exe` + Desktop shortcut with your own icon that opens them all. Hover shows what it opens.
- **Themes:** light / dark / like Windows, five color palettes, any accent.
- **Backups:** Export / Import one `.lpbackup` file. Imports Stardock Keyboard Launchpad hotkeys (`.ini`, `.keysettings`).

Your data lives in `%LOCALAPPDATA%\LaunchPad` (never in this repo).

## Build
.NET 10 SDK:

```
dotnet publish src -c Release -o out
```

Every push to `main` builds both downloads on GitHub Actions, posts them to Releases as **latest**, and refreshes the download page from `docs/`.

# Live Wallpaper

[![CI](https://github.com/noahfgarrett/live-wallpaper/actions/workflows/ci.yml/badge.svg)](https://github.com/noahfgarrett/live-wallpaper/actions/workflows/ci.yml)

Live Wallpaper is a native Windows 10/11 app that turns a still photo into a living desktop wallpaper. Real-time effects, starting with snowfall, are drawn over your image behind the desktop icons using Direct3D 11. Effects can be confined to regions of the picture, such as snow falling only outside a window.

## Status

**Early development: Milestone 1 (foundation).** The engine can show a still image behind the desktop icons on each monitor. There's no settings window and no snow yet. Those come in later milestones; see [the design](docs/superpowers/specs/2026-11-19-live-wallpaper-design.md).

A browser preview of the snow effect and the region editor lives in [`prototype/`](prototype) on the `feat/m0-preview` branch. It's a design reference, not part of the app.

### Verified on Windows

Nothing yet. Each behaviour is listed here only after it has been checked on a real machine with [the test checklist](docs/windows-test-checklist.md).

## Trying a build

1. Open the latest green [CI run](https://github.com/noahfgarrett/live-wallpaper/actions/workflows/ci.yml) and download the **LiveWallpaper-win-x64** artifact (you need to be signed in to GitHub).
2. Unzip it. The build isn't signed, so Windows SmartScreen will warn you on first launch: choose **More info → Run anyway**.
3. Use it from PowerShell in that folder:

| Command | What it does |
|---|---|
| `.\LiveWallpaper.Engine.exe --set <image> [--monitor <id>\|all] [--scaling fill\|fit\|stretch\|center]` | Chooses the image for one or all monitors. A running engine picks it up immediately. |
| `.\LiveWallpaper.Engine.exe` | Starts the engine. |
| `.\LiveWallpaper.Engine.exe --stop` | Stops the engine and restores your normal wallpaper. |
| `.\LiveWallpaper.Engine.exe --list-monitors` | Lists monitor ids, bounds and DPI. |
| `.\LiveWallpaper.Engine.exe --dump-desktop desktop.json` | Saves the desktop window layout, for diagnosing attach problems. |

Settings live in `%LOCALAPPDATA%\LiveWallpaper\settings.json`, and logs in `%LOCALAPPDATA%\LiveWallpaper\logs\`.

## System requirements

- Windows 10 or 11, x64.
- A GPU with Direct3D 11 (feature level 11.0). If none is available, the engine falls back to software rendering (WARP).

## Building

You need the .NET 10 SDK (the version is pinned in `global.json`).

```bash
dotnet build
dotnet test
dotnet publish src/LiveWallpaper.Engine -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o artifacts/LiveWallpaper
```

The solution also builds on macOS and Linux (`EnableWindowsTargeting`). There, the Windows-only tests are skipped; CI runs them on `windows-latest`.

## How it works

The engine places one window per monitor behind the desktop icons. It attaches to an undocumented part of Explorer's window tree, which changed in Windows 11 24H2. The engine handles both layouts and refuses to attach rather than ever covering your icons. See [docs/desktop-integration.md](docs/desktop-integration.md) for the details and limitations.

## Known limitations

- Relies on undocumented Explorer behaviour, which may change in future Windows updates.
- Milestone 1 shows still images only and is controlled from the command line.
- Behaviour on real desktops is unverified until it appears in "Verified on Windows" above.

## License

[MIT](LICENSE)

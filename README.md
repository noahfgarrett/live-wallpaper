# Live Wallpaper

[![CI](https://github.com/noahfgarrett/live-wallpaper/actions/workflows/ci.yml/badge.svg)](https://github.com/noahfgarrett/live-wallpaper/actions/workflows/ci.yml)

Live Wallpaper is a native Windows 10/11 app that turns a still photo into a living desktop wallpaper. Import a photo and add real-time snow, rain, rain on a window or flowing water in the editor, watching a live preview as you go. Then apply it, and the effects keep running behind your desktop icons. It's drawn with Direct3D 11 and costs little while it runs.

## Status

**Early test builds (Milestone 5).** The app, live snow, rain, rain on glass, flowing water, effect regions, tray icon and settings are built. Automatic pausing comes next. Nothing is verified on a real desktop yet; see [the test checklist](docs/windows-test-checklist.md).

### Verified on Windows

Nothing yet. Each behaviour is listed here only after it has been checked on a real machine.

## Getting it

1. Download the latest zip from [Releases](https://github.com/noahfgarrett/live-wallpaper/releases) (pre-releases are untested test builds).
2. Unzip it anywhere and run **LiveWallpaper.exe**. The build isn't signed, so Windows SmartScreen will warn you on first launch: choose **More info → Run anyway**.

## Using it

- **Library:** choose **Import images**, or drop photos onto the window. Each image is copied into the library; your original file is never changed or deleted.
- **Edit:** opens the editor with a live preview and a tab per effect. Every effect you turn on runs at once, and if the image is on your desktop, it changes there too.
  - **Snow:** density, speed, wind, flake size and motion blur.
  - **Rain:** falling streaks in depth layers that slant with the wind. Density, speed, wind, streak length and brightness.
  - **Glass:** rain on a window. Drops sit on the glass, slide down it leaving trails, and show an upside-down view of the scene behind them. Drops, drop size, sliding and fog.
  - **Water:** makes the photo's own waterfall or river flow. Stretch, foam, and mist that softens the base of a waterfall.
- **Regions:** each effect has its own. **Add region** and drag its corners onto part of the photo, such as the sky, so snow or rain falls only there. Turn on **Snow everywhere except the regions** to keep snow off a window instead, and use **Soft edge** to blend the border. On the Glass tab, **Add pane** and drag its four corners onto a window, so the drops follow its perspective. On the Water tab, add a region on the water and drag its arrow the way the water flows; a longer arrow flows faster. See [docs/regions.md](docs/regions.md).
- **Apply:** pick all displays or one, and a scaling mode (Fill, Fit, Stretch, Center).
- **Tray icon:** right-click it to open the app, pause or resume the animation, switch to a recent wallpaper, open settings, or exit.
- **Settings:** start with Windows (off by default), keep the wallpaper running when the app is closed, frame rate (30, 60 or Adaptive) and default scaling.
- **Performance:** live frame rate, memory, CPU and particle count (snowflakes plus rain streaks) from the wallpaper engine.

The wallpaper itself is drawn by a small background program, `LiveWallpaper.Engine.exe`, which keeps running in the tray after you close the app. Settings and the library live in `%LOCALAPPDATA%\LiveWallpaper\`, and logs in `%LOCALAPPDATA%\LiveWallpaper\logs\`.

## System requirements

- Windows 10 or 11, x64.
- A GPU with Direct3D 11 (feature level 11.0). Without one, the engine falls back to software rendering.

## Building

You need the .NET 10 SDK (the version is pinned in `global.json`).

```bash
dotnet build
dotnet test
```

Publish both programs into one folder:

```bash
dotnet publish src/LiveWallpaper.Engine -c Release -r win-x64 --self-contained -o artifacts/LiveWallpaper
dotnet publish src/LiveWallpaper.App -c Release -r win-x64 --self-contained -o artifacts/LiveWallpaper
```

The solution, WPF app included, also builds on macOS and Linux (`EnableWindowsTargeting`). There, Windows-only tests are skipped; CI runs them on `windows-latest`.

## How it works

- **Engine:** one window per monitor behind the desktop icons, attached to Explorer's window tree. The tree changed in Windows 11 24H2; both layouts are handled, and the engine refuses to attach rather than ever covering your icons. See [docs/desktop-integration.md](docs/desktop-integration.md).
- **Snow:** stateless GPU particles, with every flake's position computed in the vertex shader. It's built to stay smooth over days of uptime. See [docs/snow-model.md](docs/snow-model.md).
- **Rain, glass and water:** stateless too. Rain streaks are GPU particles like snow, glass drops are drawn per pixel in each pane's perspective over a copy of the frame, and water redraws the photo shifted along its arrows. They follow the same rules for days of uptime. See [docs/effects-model.md](docs/effects-model.md).
- **App:** talks to the engine over a named pipe that only your user account can open.

## Known limitations

- Relies on undocumented Explorer behaviour, which may change in future Windows updates.
- No automatic pausing for fullscreen apps or battery saver yet (planned).
- Behaviour on real desktops is unverified until it appears in "Verified on Windows" above.

## License

[MIT](LICENSE)

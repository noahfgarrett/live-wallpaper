# Windows test checklist

Everything the engine does on a real desktop has to be checked on a real Windows machine. CI can only build the engine and run tests. Work through each item, write what you saw in **Result**, and send the results back (plus `desktop.json` from step 3).

Logs are in `%LOCALAPPDATA%\LiveWallpaper\logs\engine.log`. Set `LIVEWALLPAPER_DEBUG=1` before starting the engine for more detail.

## Milestone 1: still image behind the icons

Run the commands from PowerShell in the folder you unzipped.

1. **Get the build.** Download `LiveWallpaper-win-x64` from the latest green run on the repo's Actions page, or run `gh run download -R noahfgarrett/live-wallpaper -n LiveWallpaper-win-x64`. Unzip it. The first launch shows a SmartScreen warning, because the build isn't signed. Click **More info → Run anyway**.
   - Result:
2. **List monitors.** Run `.\LiveWallpaper.Engine.exe --list-monitors`.
   - Expected: all three displays are listed, each with a long id, its bounds and a DPI (for example 96, 120 or 144). One is marked primary.
   - Result:
3. **Dump the desktop.** Run `.\LiveWallpaper.Engine.exe --dump-desktop desktop.json`.
   - Expected: it prints `Desktop layout: ProgmanChild { ... }` on 24H2, and `desktop.json` is created. Send the file back.
   - Result:
4. **Show a photo.** Run `.\LiveWallpaper.Engine.exe --set "C:\path\to\photo.jpg" --monitor all --scaling fill`, then `.\LiveWallpaper.Engine.exe`.
   - Expected: the photo fills all three displays, behind the icons. The icons are still visible on top, and nothing flickers.
   - Result:
5. **Icons and shell still work.**
   - Expected: you can click, drag and rename icons. Right-clicking the desktop opens the menu. The taskbar is unaffected. Alt+Tab shows no Live Wallpaper window. The engine doesn't appear in the taskbar.
   - Result:
6. **Scaling modes.** Run `--set <photo> --monitor <one id> --scaling fit`, then repeat with `stretch` and `center`.
   - Expected: the change appears on that monitor only, within about a second. Fit shows black bars; Center shows the photo at its native size.
   - Result:
7. **Explorer restart.** In Task Manager, find **Windows Explorer** and choose **Restart**.
   - Expected: the wallpaper comes back within about 2–6 seconds, still behind the icons.
   - Result:
8. **Unplug and replug a monitor.** Unplug one external monitor, wait, then plug it back in.
   - Expected: the remaining displays keep their wallpaper. After replugging, the returning monitor gets its wallpaper again, with no window stranded in the wrong place.
   - Result:
9. **Change DPI.** In Settings → Display, change one monitor's scale (for example to 150%).
   - Expected: the image still exactly fills that monitor, with no gap and no overflow onto its neighbour.
   - Result:
10. **Second launch.** Run `.\LiveWallpaper.Engine.exe` again while it's already running.
    - Expected: nothing changes, and Task Manager still shows exactly one `LiveWallpaper.Engine`.
    - Result:
11. **Stop.** Run `.\LiveWallpaper.Engine.exe --stop`.
    - Expected: it prints "Live Wallpaper stopped.", your normal Windows wallpaper is back, and no `LiveWallpaper` process remains.
    - Result:
12. **Crash recovery.** Start the engine, then end `LiveWallpaper.Engine` in Task Manager.
    - Expected: after the kill the desktop may show stale content until Explorer repaints. Start the engine again: the desktop is restored and the wallpaper reappears.
    - Result:
13. **Bad files.** Run `--set` with a path that doesn't exist, then with a `.txt` file renamed to `.jpg`.
    - Expected: each gives a clear one-line error, and a running engine is unaffected.
    - Result:
14. **Resource use.** With the wallpaper showing on all monitors for 1 minute, read Task Manager's **Details** tab.
    - Expected: note the **Memory (private working set)** and **CPU** for `LiveWallpaper.Engine.exe`. CPU should be about 0%, since M1 draws once.
    - Result:

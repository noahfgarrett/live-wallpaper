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
14. **Phone photo orientation.** Set a portrait photo taken on a phone.
    - Expected: it's upright, the same way the Photos app shows it.
    - Result:
15. **Slideshow or Spotlight users.** If your normal background is a slideshow or Windows Spotlight, start and then `--stop` the engine.
    - Expected: the slideshow or Spotlight is still active afterwards (Settings → Personalization → Background).
    - Result:
16. **New monitor to the left.** With the engine running, add or arrange a monitor to the left of the primary in Settings → Display.
    - Expected: every display's wallpaper lines up with its own screen afterwards.
    - Result:
17. **Resource use.** With the wallpaper showing on all monitors for 1 minute, read Task Manager's **Details** tab.
    - Expected: note the **Memory (private working set)** and **CPU** for `LiveWallpaper.Engine.exe`. CPU should be about 0%, since M1 draws once.
    - Result:

## Milestones 2 + 3: the app and live snow

Start fresh: run `LiveWallpaper.Engine.exe --stop` from an older build if one is running, then open **LiveWallpaper.exe** from the new zip.

1. **First launch.**
   - Expected: a dark window with Library, Settings and Performance on the left. The status line at the bottom turns green within a few seconds, and a snowflake icon appears in the tray.
   - Result:
2. **Import.** Use **Import images** to add `photo.png`, then drag another photo onto the window.
   - Expected: both appear as cards with thumbnails.
   - Result:
3. **Bad file.** Import a `.txt` file renamed to `.jpg`.
   - Expected: a "file was skipped" message naming it, and no card for it.
   - Result:
4. **Editor preview.** Click **Edit** on a card.
   - Expected: the photo fills the preview and snow falls smoothly. Dragging any slider changes it immediately, with no stutter.
   - Result:
5. **Apply to all displays.** In the editor, choose **Apply to desktop → All displays → Apply**.
   - Expected: snow falls behind the icons on all three screens, and the card shows "On desktop".
   - Result:
6. **Apply to one display.** Apply a different photo to one display only.
   - Expected: only that screen changes.
   - Result:
7. **Live changes.** With the image applied, move the Density and Wind sliders.
   - Expected: the desktop snow changes within a moment.
   - Result:
8. **Icons and shell.**
   - Expected: icons stay clickable and draggable, right-click and drag-select on the desktop still work, and nothing shows in Alt+Tab.
   - Result:
9. **Close the app.**
   - Expected: the window closes, the snow keeps falling, and the tray icon stays.
   - Result:
10. **Second launch.** Open LiveWallpaper.exe twice.
    - Expected: the second launch brings the existing window forward. There's still one app window.
    - Result:
11. **Tray menu.** Right-click the tray icon and try each item: Open, Pause animation (snow freezes), Resume, Change wallpaper, Settings.
    - Expected: each does what it says.
    - Result:
12. **Remove while applied.** Remove the applied image from the library.
    - Expected: that display goes back to your normal wallpaper, and the original photo file is still where it was.
    - Result:
13. **Settings.** Turn on Start with Windows, sign out and back in, then turn it off again. Set the frame rate to 30 fps.
    - Expected: the wallpaper comes back after signing in. At 30 fps the Performance page shows about 30 fps.
    - Result:
14. **Exit.** Choose **Exit** from the tray.
    - Expected: the app and engine both close, your normal wallpaper is back, and no LiveWallpaper process remains in Task Manager.
    - Result:
15. **Resources.** With snow on all three displays at the default settings for 5 minutes, open the Performance page.
    - Expected: note the frame rate, engine memory and engine CPU. The targets are roughly 60 fps, under 150 MB and a few percent CPU.
    - Result:

## Milestone 4: effect regions

Use the v0.3.0-m4 zip. Stop the older engine first (tray → **Exit**), then open **LiveWallpaper.exe**. Pick a photo with a window or a clear sky, and open it in the editor.

1. **Old library.** Before anything else, check the library from v0.2.0-m3.
   - Expected: every image is still there with its snow settings. Nothing was reset.
   - Result:
2. **Add a region.** Click **Add region**.
   - Expected: a 4-corner outline with round handles appears in the middle of the preview, and snow now falls only inside it.
   - Result:
3. **Keystone.** Drag each corner onto the corners of a window or the sky.
   - Expected: only the dragged corner moves, the shape follows like a projector keystone, and the handles stay exactly under the mouse.
   - Result:
4. **Invert.** Turn on **Snow everywhere except the regions**.
   - Expected: snow falls everywhere except inside the outline.
   - Result:
5. **Soft edge.** Move **Soft edge** from 0 to 10%.
   - Expected: at 0% the snow stops at a sharp line, and higher values fade it more and more gradually across the outline.
   - Result:
6. **Add and remove corners.** Hover an edge of the selected region until a dot appears, click and drag it. Then right-click a corner. Then click a corner and press Delete. Try to go below 3 corners.
   - Expected: a corner is added where you clicked, right-click and Delete each remove one, and a triangle can't lose another corner.
   - Result:
7. **Move and limits.** Set scaling to **Fill** on a photo of a different shape than your screen, drag the region body far past the edge of the preview, then drag a corner outside the preview.
   - Expected: the region stops at the edge of the visible picture, and the corner stops at the edge. Nothing ends up out of sight.
   - Result:
8. **Several regions.** Add a second region overlapping the first.
   - Expected: snow falls in both, including where they overlap.
   - Result:
9. **Undo and redo.** Make three changes (a drag, invert, a soft-edge change), then press **Undo** three times and **Redo** three times, and try Ctrl+Z, Ctrl+Y and Ctrl+Shift+Z. Then start dragging a corner and press Ctrl+Z before releasing.
   - Expected: each step undoes one whole change (one per drag), no handle stays selected after undo or redo, and Ctrl+Z mid-drag puts the corner back and stops the drag.
   - Result:
10. **Hide outlines.** Turn off **Show outlines**, then click and drag on the preview.
    - Expected: no outlines or handles, and nothing changes when you click or drag. **Add region** turns the outlines back on.
    - Result:
11. **Scaling.** Switch scaling between Fill, Fit, Stretch and Center.
    - Expected: the regions stay on the same part of the photo in every mode.
    - Result:
12. **Apply.** **Apply to desktop → All displays**.
    - Expected: on every display, snow follows the regions exactly as in the preview, on the same part of the photo even on a display with a different shape.
    - Result:
13. **Live drag.** With the image applied, drag a corner and move **Soft edge** to 10% while watching the desktop. Repeat with **Soft edge** at 0% and at 0.1%, ideally on a 4K display.
    - Expected: the desktop follows within a moment and the snow keeps falling smoothly at every soft edge, with no long freezes.
    - Result:
14. **Saved.** Close the app, reopen it and edit the same image. Then choose tray → **Exit**, start the app again and look at the desktop.
    - Expected: the regions, invert and soft edge are all as you left them, both in the editor and on the desktop.
    - Result:
15. **High DPI.** Set a display to 150% scaling, move the app there and edit regions.
    - Expected: the handles are a comfortable size and sit exactly under the mouse.
    - Result:
16. **Resources.** With regions on all three displays for 5 minutes, open the Performance page.
    - Expected: note the frame rate, engine memory and CPU. They should be close to the Milestone 2 + 3 readings.
    - Result:

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

Use the v0.3.1-m4 zip. Pick a photo with a window or a clear sky, and open it in the editor.

0. **Upgrade over a running older engine.** Start an older build's engine (for example `LiveWallpaper.Engine.exe` from v0.1.0-m1 or v0.2.0-m3) and leave it running. Then, without stopping it, open **LiveWallpaper.exe** from the new zip.
   - Expected: within about 10 seconds the status line turns green and the old engine is gone from Task Manager (one `LiveWallpaper.Engine` process remains, from the new folder). Applying an image works. You never see "The wallpaper engine couldn't be started."
   - Result:
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

## Milestone 5: rain, glass and water

Use the v0.4.0-m5 zip. Three photos help: a street or landscape for rain, a window seen at an angle for glass, and a waterfall for water. The editor now has four tabs, **Snow**, **Rain**, **Glass** and **Water**. Each tab has its own on/off switch, sliders, regions and undo history, and every effect that's switched on runs in the preview at the same time.

0. **Upgrade over a running v0.3.1-m4 engine.** Apply an image with v0.3.1-m4 and leave its engine running. Then, without stopping it, open **LiveWallpaper.exe** from the new zip.
   - Expected: within about 10 seconds the status line turns green, and Task Manager shows exactly one `LiveWallpaper.Engine` process, from the new folder. The image that was on the desktop comes back with its snow. You never see "The wallpaper engine couldn't be started." or "An older Live Wallpaper engine is running and couldn't be replaced."
   - Result:
1. **Old library.** Before changing anything, look at the library from v0.3.1-m4, then edit an image that has snow regions.
   - Expected: every image is still there with its snow settings, and the Snow tab shows its regions, invert and soft edge as before. Rain, Glass and Water are all off.
   - Result:
2. **Tabs.** Look at the four tabs at the window's default size, then drag the window as narrow as it goes. Switch each effect on and off, and open each tab.
   - Expected: at both sizes the four pills fit on one row, with icon and label uncut. A small dot shows on the tab of each effect that's on, and goes away when it's turned off. The editor opens on Snow. On **Glass** there's no "everywhere except" switch and no line about adding corners. On **Water** there's no "everywhere except" switch. On **Rain** the switch reads "Rain everywhere except the regions".
   - Result:
3. **Rain look.** On the Rain tab, turn rain on and move each slider: Density, Speed, Wind, Streak length and Brightness.
   - Expected: thin streaks fall in depth layers at different speeds, not identical lines at one speed. Each slider changes the preview immediately, with no stutter.
   - Result:
4. **Rain and wind.** Move **Wind** from 0 to 100% and watch for a minute.
   - Expected: at 0% the rain falls straight down. Higher values slant the streaks more, and gusts sweep the slant back and forth. The streaks point the way they move.
   - Result:
5. **Rain regions.** Add a region, drag its corners onto the sky, then turn on **Rain everywhere except the regions**.
   - Expected: rain falls only inside the region, then everywhere except inside it, just as snow does in Milestone 4.
   - Result:
6. **Glass in perspective.** On a photo of a window seen at an angle, open the Glass tab, turn glass on, click **Add pane** and drag the four corners onto the window's corners.
   - Expected: drops appear only inside the pane, and they look smaller and squashed toward the far side of the window, following its perspective. Each drop shows a small, upside-down view of the scene behind it, with a bright rim. Without any pane, the whole picture is the glass.
   - Result:
7. **Fog.** Move **Fog** from 0 to 100%.
   - Expected: at 0% the glass between drops is clear. Higher values blur and lighten the scene behind it more and more, until at 100% it's heavily misted. The drops still show the scene behind them.
   - Result:
8. **Sliding drops.** Set **Sliding** to 100% and watch for a minute, then set it to 0%.
   - Expected: larger drops sit still for a while, then slide down in stop-start runs with a slight sideways wobble. They don't all move at once. Each leaves a trail of tiny beads and a clear streak through the fog that fades the further it is behind the drop. After a drop slides off the bottom, its trail and beads fade away rather than vanish at once, and the next sliding drop fades in where it sits. No drop, trail or bead pops in or out. At 0% no drop slides, and the small beads still come and go.
   - Result:
9. **Panes keep 4 corners.** Select a pane, hover its edges, click an edge, right-click a corner, then click a corner and press Delete. Drag a corner and drag inside the pane.
   - Expected: no dot appears on the edges, and no corner is ever added or removed. Dragging a corner moves only that corner (keystone), and dragging inside moves the whole pane.
   - Result:
10. **Water flows.** On a waterfall photo, open the Water tab, turn water on and click **Add water region**. Drag its corners around the falling water, keeping a little above the top of the fall in view. Watch it for a minute.
    - Expected: the region comes with an arrow pointing down. The water flows along the arrow smoothly and continuously, with no visible reset, pulse or jump. Rock or sky above the region isn't dragged down into the water. Outside the region the photo doesn't move.
    - Result:
11. **Arrow.** Drag the arrow's round head to point along the river or the fall. Make it as short as it goes, then as long as it goes, then drag it past the edge of the preview.
    - Expected: the water flows the way the arrow points. A longer arrow flows faster. The arrow stops at a shortest and a longest length, and its head stops at the edge of the visible picture.
    - Result:
12. **Water regions.** Add a second water region with its own arrow, for example where the river turns. Add and remove corners as on the Snow tab. Click elsewhere to deselect.
    - Expected: each region flows its own way. Corners are added and removed as for snow (at least 3). Every region shows its arrow, and only the selected one has the round head to drag. With water on but no regions, nothing in the photo moves.
    - Result:
13. **Stretch and foam.** Move **Stretch** from 0 to 100%, then **Foam** from 0 to 100%.
    - Expected: low Stretch gives a tight shimmer, and high Stretch long streaming streaks, while the water flows at the same speed. Foam adds churning white streaks on bright white water, and dark water stays calm.
    - Result:
14. **Mist.** Move **Mist** from 0 to 100% and look at the base of the waterfall, where its region ends.
    - Expected: at 0% there's no spray. Higher values add a drifting spray that rises from the base and softens the hard line where the region ends. It reaches further at higher values, and moving the slider never makes the water jump.
    - Result:
15. **Regions per tab.** Give Snow a region, then switch to the Rain, Glass and Water tabs in turn.
    - Expected: only the open tab's outlines and arrows are drawn, and you can only edit those. Snow still falls only in its own region while another tab is open. Each tab keeps its own regions and soft edge, and Snow and Rain their own invert.
    - Result:
16. **Undo per tab.** Make a change on the Snow tab and another on the Water tab. On Water, press **Undo** (and Ctrl+Z), then switch to Snow and press Undo. Try Redo (Ctrl+Y) on each.
    - Expected: Undo and Redo on each tab change only that tab's regions, one whole change at a time, and Snow's change can still be undone after working on Water.
    - Result:
17. **All four at once.** Turn on snow, rain, glass and water on one image, each with its region.
    - Expected: all four run together in the preview. Water flows under the rain and snow, and the glass drops show the rain and snow behind them.
    - Result:
18. **Live on the desktop.** **Apply to desktop → All displays**. Then, on each tab in turn, switch the effect off and on, move every slider, and drag a region's corner (and the water arrow).
    - Expected: on every display, the desktop matches the preview and follows each change within a moment, with the effects on the same part of the photo.
    - Result:
19. **Library badges.** Go back to the library.
    - Expected: the image's card shows a badge for each effect that's on: Snow and Rain when on with a density above 0, Glass when on, Water when on with at least one region. Turning an effect off removes its badge.
    - Result:
20. **Saved.** Close the app, reopen it and edit the same image. Then choose tray → **Exit**, start the app again and look at the desktop.
    - Expected: every tab's settings, regions and arrows are as you left them, both in the editor and on the desktop.
    - Result:
21. **Resources.** With snow only on all three displays for 5 minutes, write down the GPU and CPU of `LiveWallpaper.Engine` from Task Manager's **Processes** tab (add the GPU column if it's hidden) and the frame rate from the Performance page. Then turn all four effects on and repeat. Do this on the laptop's own screen and on the external 4K or 1440p monitor.
    - Expected: record all the numbers for both cases. The frame rate stays near 60 fps with all four on. (The Performance page's Particles count is snowflakes plus rain streaks.)
    - Result:
22. **Overnight.** Leave all four effects running on the desktop overnight.
    - Expected: in the morning the snow, rain, glass drops and water all still move as smoothly as when you started, with no jumps, jitter or stalls.
    - Result:

### Sharp, zoomable preview

Use a large photo with fine detail (leaves, fur, or text), and run these on the laptop and on an external monitor.

23. **Sharp at Fit.** Open the photo in the editor, with the zoom at **Fit**. Apply it to the desktop and compare fine detail (leaves or text) in the preview with the same spot on the desktop.
    - Expected: the preview is sharp, with no blur or shimmering, and shows the same detail as the desktop, just smaller. Snow, rain, glass drops and water look the same size relative to the photo as on the desktop.
    - Then resize the window so Fit is around 60–70 %, compare fine detail (text or leaves) again, and zoom slowly across 50 % with the wheel or the bar.
    - Expected: at 60–70 % fine detail shows no shimmer, moiré or beat pattern. Sharpness doesn't jump when the zoom crosses 50 %.
    - Result:
24. **Wheel zoom at the cursor.** Put the cursor on a small feature (a leaf, a letter) and roll the wheel up a few notches, then down.
    - Expected: each notch zooms by about a quarter, and the feature under the cursor stays under it. A notch that would cross 100 % stops at exactly 100 % (the bar reads "100 %"), and the next notch carries on. Zooming out stops at Fit, and zooming in stops at 400 %. At 100 % the picture is pixel-exact: one screen pixel per desktop pixel, and finer detail than at Fit.
    - Result:
25. **Zoom bar.** Look at the bar under the preview at Fit, then click **+** until it stops, then **−** until it stops, then zoom in and click **Fit**.
    - Expected: the bar shows the zoom as a percentage of the desktop's size (for example "47 %"). At Fit, **−** and **Fit** are disabled. At the maximum, **+** is disabled. **Fit** returns to the fitted view.
    - Result:
26. **Zoom shortcuts.** Open the editor and, without clicking anything, press Ctrl+= and Ctrl+− a few times, then Ctrl++ (Ctrl+Shift+=), then Ctrl+1, then Ctrl+0.
    - Expected: the shortcuts work straight away, with no click first. Ctrl+= and Ctrl++ zoom in and Ctrl+− zooms out. Ctrl+1 shows 100 %, centred on the preview. Ctrl+0 returns to Fit. The percentage on the bar follows each.
    - Result:
27. **Pan with the right and middle buttons.** Zoom in to 200 %. Drag the picture with the right button held, then with the middle button. Then add a region, and right-click one of its corners without moving the mouse.
    - Expected: both drags move the picture, and it can't be dragged past its edges. A right-click on a corner without a drag removes the corner (not below 3, and never on a glass pane), and does not move the picture. A right-drag that starts on a corner pans instead of removing it.
    - Result:
28. **Pan with Space.** Zoom in, hold Space and drag with the left button. Then click one of the switches in the side panel (for example the effect's on/off switch), move the cursor onto the preview, and hold Space and drag again.
    - Expected: Space plus a left-drag pans the picture, and no region, corner or arrow moves. After clicking the switch, Space and a drag over the preview still pans, and the switch does **not** flip.
    - Result:
29. **Space still toggles a switch.** Click a switch in the side panel so it has focus, keep the cursor over the side panel, and press Space.
    - Expected: the switch flips, as it always did. Space only pans when the cursor is on the preview.
    - Result:
30. **Wheel with "Scroll inactive windows" off.** In Windows Settings, go to **Bluetooth & devices → Mouse** and turn **Scroll inactive windows when I hover over them** off. With the editor window focused, roll the wheel over the preview. Then click on a different window, bring the editor back without clicking the preview, and roll the wheel over the preview again. Turn the setting back on afterwards.
    - Expected: the wheel zooms the preview at the cursor each time, whichever part of the editor had focus. It doesn't scroll the side panel while the cursor is on the preview.
    - Result:
31. **Handles at 400 %.** Zoom to 400 % on a region corner. Drag the corner, drag the region's inside, then switch to the Water tab and drag an arrowhead. Drag each one toward and past the edge of the preview.
    - Expected: the handles, outlines and arrows stay the same on-screen size as at Fit and line up with the photo. Dragging is smooth and precise. A drag stops at the edge of the visible part of the zoomed view, so a corner can't be lost off-screen, and a click-and-pan afterwards reveals the rest.
    - Result:
32. **Resize while zoomed.** Zoom to 200 % on a feature near the middle and drag the window's edge wider and narrower. Then press Ctrl+0 and resize it again.
    - Expected: while zoomed, the feature at the centre of the preview stays at the centre as the window changes. At Fit, the picture stays fitted to the new size and the bar reads the new fit percentage.
    - Result:
33. **Zoom resets.** Zoom in, then switch to another image in the library and back, then zoom in again and change **Scaling** (for example from Fill to Center).
    - Expected: the zoom is Fit each time the editor opens, and after an image change or a scaling change. It isn't remembered between sessions.
    - Result:
34. **Center scaling crop.** Use a photo larger than your primary display, set **Scaling** to **Center**, and compare the preview at Fit with the desktop.
    - Expected: the preview shows the same crop of the photo as the desktop, and the region outlines sit on the same features. Do this on a 4K monitor too, if you have one.
    - Result:
35. **GPU with the editor open.** Close the editor (go back to the library) and note the GPU of `LiveWallpaper.exe` and `LiveWallpaper.Engine` in Task Manager's **Processes** tab. Then open an image with snow, rain, glass and water on, leave the editor open for a minute at Fit, and note it again. Repeat at 400 %.
    - Expected: record the numbers. The preview renders at the size of your primary display, so the editor uses more GPU than the library, and more on a 4K display. The preview stays smooth, and the desktop engine doesn't slow down. GPU drops back when you return to the library.
    - On a 4K screen, set **Soft edge** to 0 and drag a region corner around the preview for a few seconds.
    - Expected: the drag follows the mouse with no hitching.
    - Result:

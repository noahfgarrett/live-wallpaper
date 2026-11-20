# Effect regions

Regions confine an effect to part of the photo, or keep it out of part of the photo. Each of the four effects (snow, rain, glass and water) has its own regions. The rules here come from the approved browser preview (`prototype/` on `feat/m0-preview`), section 5.8 of the design, and the Milestone 5 design (`docs/superpowers/specs/2026-11-19-m5-rain-glass-water-design.md`).

## Model

- A **region** is a polygon in normalized image coordinates: 0–1 across the whole source image after EXIF orientation. It stays on the same part of the picture under every scaling mode, including the crop that Center makes when the photo is larger than the display.
- Each library item has one region set per effect, saved in `library.json`:
  - Snow: `LibraryItem.Regions` (the name is kept so older files still load).
  - Rain, Glass and Water: `Rain.Regions`, `Glass.Regions` and `Water.Regions`, next to each effect's settings.
- A region set holds:
  - `regions`: the polygons.
  - `invert`: the effect everywhere *except* the regions. Only snow and rain offer it; for glass and water it is always off.
  - `featherPct`: soft edge, 0–10 % (default 1 %).
- Limits, per effect: at most 16 regions and 64 points per region, at least 3 points. Data outside these limits, or with points outside the image or not finite, is rejected when the library loads and when the engine receives it.
- **Glass panes** have exactly 4 corners, in the order they were created: top-left, top-right, bottom-right, bottom-left.
- **Water regions** each carry a `flow`: the arrow from the region's centroid (the average of its corners) to the arrowhead, in normalized image coordinates. Other effects' regions have none.
- Shape rules on load and over IPC:
  - When the library loads, a set that an old or hand-edited file could hold but the editor can't produce is repaired: glass and water have `invert` turned off, a glass region without exactly 4 corners is dropped, and a water region without a flow gets the default arrow (straight down, 0.12 image heights).
  - A valid flow is finite, with a length of 0.002–1 in normalized coordinates. On load, a finite flow outside that length is scaled to fit, keeping its direction; a zero flow gets the default arrow.
  - Anything unsafe still rejects the file, as above, including a flow that isn't finite.
  - The engine never repairs. It answers `Failed` to a region set that breaks these rules.
- A library saved before regions existed loads with no regions, and one saved before Milestone 5 loads with rain, glass and water off and without regions.

## Mask

The engine and the editor preview build the same mask (`RegionMask` in Core, `RegionMaskTexture` in Rendering). Each effect has its own mask, rebuilt only when that effect's regions or the target size change.

- The mask covers the render target, letterbox bars included. Each polygon is mapped through the rectangle where the **whole** image lands (`ScalingMath.ComputeDestination(imageSize, target, scaling)`), not the rectangle of the cropped texture.
- Each region is filled on its own (nonzero winding, 4 sub-scanlines and exact horizontal coverage for anti-aliased edges), and the fills are united.
- Soft edge: `featherPx = featherPct / 100 × displayed image width`. The mask is blurred with σ = `featherPx / 2`, approximated by three box blurs per axis. Edges under 0.5 px aren't blurred.
- Inverted: `255 − value`, so the regions become holes in white.
- How each effect uses its mask:
  - Snow and rain multiply their alpha by the mask's red channel.
  - Glass multiplies its whole contribution by it, so a soft edge fades the drops and fog into the window frame.
  - Water blends its moving pixels over the photo by the mask, and also weights each displaced sample by the mask at the sample point, so rock above a waterfall isn't dragged into the water.
- With no regions, the mask is one white pixel: snow and rain show everywhere, and the whole picture is the glass. Water with no regions draws nothing.
- A change of target size or scaling rebuilds the scene, and with it every mask. While running, snow, rain and glass read their mask once per pixel they draw; water also reads it at its displaced samples and along the flow for the mist.

### Deliberate differences from the preview

- **Overlapping regions are united.** The preview filled every region in one path with the nonzero rule, so two overlapping regions drawn in opposite directions cancelled where they overlapped.
- **The blur extends the frame border** (clamp to edge). A region that touches the edge of the screen stays solid there; the preview faded it.
- **Soft edges use a smaller mask.** When σ is over 1 px, the mask is rasterized at `max(1/8, 1/σ)` of the render size, so σ is about one mask pixel, and the GPU's linear filtering softens it a little more as it scales the mask up. The difference doesn't show on a soft edge, and it keeps the mask cheap to rebuild while you drag a corner with the image on the desktop. A hard edge (0 %) is always rasterized at full resolution.

The mask is built on the render thread. On an Apple-silicon Mac, a 4K mask takes about 2–7 ms for one or two regions at any soft edge. The worst case, 16 frame-sized regions of 64 points with a hard edge, takes about 50 ms, which shows as a brief hitch while dragging.

### Water flow field

- Next to its mask, water has a flow texture (`FlowField` in Core, `FlowTexture` in Rendering) with one cell per 4 × 4 target pixels. Each cell inside a water region holds that region's flow speed in target pixels per second. Where regions overlap, the later region wins.
- Cells outside every region, but within 0.14 × the displayed image height of one, take the nearest region's flow, so the mist at the base of a waterfall knows which way to drift. That reach is the mist's maximum, so moving the Mist slider never rebuilds the texture.
- It isn't blurred; the mask provides the soft edge. It is rebuilt with the water mask.

## Editing

The editor has a tab per effect, and each tab's **Regions** panel edits that effect's regions only. All effects that are on run in the preview, but only the open tab's outlines, handles and arrows are drawn and can be edited.

In the **Regions** panel (Snow and Rain; Glass and Water differ as described below):

- **Add region** drops a 4-corner quad over the middle of the visible picture (35–65 % of it). Drag a corner to move just that corner, like a projector keystone. Drag inside a region to move it.
- Hovering an edge of the selected region shows a dot; click there to add a corner and drag it. Right-click a corner, or click it and press Delete, to remove it (minimum 3).
- **Snow everywhere except the regions** (on the Rain tab, **Rain everywhere except the regions**), **Soft edge** and **Show outlines** do what they say. **Show outlines** is shared by all tabs.
- **Undo**/**Redo** (Ctrl+Z, Ctrl+Y or Ctrl+Shift+Z) go back up to 99 steps, one per completed drag. Each effect has its own history, and the keys act on the open tab.
- Switching tabs commits a pending soft-edge change and ends a drag in progress on the tab being left.
- **Zoom and pan** let you place corners and arrows precisely:
  - Zoom with the mouse wheel (the point under the cursor stays put), the zoom bar under the preview (**−**, the percentage, **+** and **Fit**), or Ctrl+= and Ctrl+− to step, Ctrl+1 for 100 % and Ctrl+0 for Fit. The percentage is measured against desktop pixels, so 100 % is one screen pixel per desktop pixel.
  - The range runs from Fit to 400 %. (On a display larger than 4K, 100 % is one screen pixel per pixel of the preview, which is rendered at most 3840 × 2160.)
  - Pan by dragging with the middle button, dragging with the right button (a right-click without a drag still removes a corner), or holding Space and dragging with the left button. The picture can't be dragged past its edges.
  - Handles, outlines and arrows keep their on-screen size at every zoom, and drags are limited to the part of the image visible in the zoomed view.
  - The zoom returns to Fit when the editor opens and when the image or the scaling changes. It isn't saved.
- The preview shows the photo as your primary display will, scaled down. Under Center scaling it crops at the display's size, not the preview's, so the part you can edit is the part the desktop shows.
- Changes show in the preview right away, are saved half a second after you stop, and go to the desktop live when the image is applied.

### Glass panes

- **Add pane** drops a 4-corner quad, the same as **Add region**. Drag a corner to keystone it onto a window, or drag inside to move it.
- A pane always has exactly 4 corners: hovering an edge shows no dot, clicking an edge adds nothing, and right-click or Delete on a corner does nothing. There is no invert switch.
- Core maps the unit square onto the pane's corners with a homography (`Homography` in Core), and the glass shader maps each pixel back through its inverse. The drops are made in pane space, so they foreshorten with the window's perspective, and they slide from the pane's top edge (its first two corners) toward the bottom edge.
- A pane that isn't convex (corners dragged across each other or folded inward) has no homography and isn't drawn until it is convex again.
- Without panes, one pane covers the whole target.

### Water regions

- **Add water region** drops a 4-corner quad with the default arrow, pointing down. Corners are added, removed and dragged as for snow, and there is no invert switch.
- Every water region draws its arrow from its centroid. The selected region's arrowhead has a round handle; drag it to aim and lengthen the arrow. The handle is checked before the corners, so it wins where they overlap.
- **The arrow's length is the speed:** the water crosses one arrow length every 4 seconds. The length is measured in image heights after correcting for the image's aspect ratio, so a horizontal and a vertical arrow of the same on-screen length flow at the same speed.
- While dragging, the arrowhead is held to the visible part of the image, and the editor clamps the aspect-corrected length to 0.02–0.5 image heights. On a very tall or wide image it then also keeps the normalized length within 0.002–1, so every arrow it saves is valid.
- The arrow is stored on its region, so moving, undoing or deleting a region carries its arrow with it.

### Fixes over the preview

These preview bugs are fixed in the app (tests in `RegionEditorTests`):

- Undo and Redo drop the selection, because the selected region may no longer exist.
- Undo during a drag cancels the drag. Undo with an uncommitted soft-edge change reverts only that change instead of also stepping back.
- Drags stay on the visible part of the image, so a corner can't be lost off-screen under Fill or Center cropping.
- With **Show outlines** off nothing can be edited, because you can't see what you'd be changing. **Add region** turns the outlines back on.

## Why the handles are drawn by the preview renderer

The preview is a Win32 child window in an `HwndHost`, and WPF can't draw over it (airspace). The two options were a transparent layered window over the preview, or drawing the handles in the preview's own Direct3D renderer.

The app draws them in the renderer (`RegionOverlayRenderer`) and reads the mouse from the child window's messages (`PreviewHost.WndProc`), because:

- **It always lines up.** The renderer draws the handles with the same image rectangle it uses for the photo and the snow, so they stay pixel-aligned at any DPI. The scene is drawn at the desktop's resolution and doesn't depend on the preview's size, so resizing the window only resizes the swap chain, and the handles stay aligned throughout.
- **There's no second window to keep in sync.** A layered window would have to follow every move, resize, minimize, DPI change and virtual-desktop switch, hide when the Apply panel hides the preview, and it would visibly lag behind while the main window moves.

The cost is a small SDF shader for lines and circles, and mouse capture through `SetCapture`.

## Homography

Rain on glass is the effect that uses 4-corner planes. Its panes are mapped with the square-to-quad homography described under **Glass panes** above, and the shader's pane-space constants are in `docs/effects-model.md`. Snow, rain and water use only their masks (and water its flow field).

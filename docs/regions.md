# Effect regions

Regions confine an effect (today, snow) to part of the photo, or keep it out of part of the photo. The rules here come from the approved browser preview (`prototype/` on `feat/m0-preview`) and section 5.8 of the design.

## Model

- A **region** is a polygon in normalized image coordinates: 0–1 across the whole source image after EXIF orientation. It stays on the same part of the picture under every scaling mode, including the crop that Center makes when the photo is larger than the display.
- Each library item stores its regions next to its snow settings (`LibraryItem.Regions`, saved in `library.json`):
  - `regions`: the polygons.
  - `invert`: snow everywhere *except* the regions.
  - `featherPct`: soft edge, 0–10 % (default 1 %).
- Limits: at most 16 regions and 64 points per region, at least 3 points. Data outside these limits, or with points outside the image or not finite, is rejected when the library loads and when the engine receives it.
- A library saved before regions existed loads with no regions.

## Mask

The engine and the editor preview build the same mask (`RegionMask` in Core, `RegionMaskTexture` in Rendering).

- The mask covers the render target, letterbox bars included. Each polygon is mapped through the rectangle where the **whole** image lands (`ScalingMath.ComputeDestination(imageSize, target, scaling)`), not the rectangle of the cropped texture.
- Each region is filled on its own (nonzero winding, 4 sub-scanlines and exact horizontal coverage for anti-aliased edges), and the fills are united.
- Soft edge: `featherPx = featherPct / 100 × displayed image width`. The mask is blurred with σ = `featherPx / 2`, approximated by three box blurs per axis. Edges under 0.5 px aren't blurred.
- Inverted: `255 − value`, so the regions become holes in white.
- The snow pixel shader multiplies its alpha by the mask's red channel. With no regions, the mask is one white pixel, so snow shows everywhere.
- The mask is rebuilt only when the regions or the target size change, which also covers a change of scaling (that rebuilds the scene). While running, it costs one texture sample per snow pixel.

### Deliberate differences from the preview

- **Overlapping regions are united.** The preview filled every region in one path with the nonzero rule, so two overlapping regions drawn in opposite directions cancelled where they overlapped.
- **The blur extends the frame border** (clamp to edge). A region that touches the edge of the screen stays solid there; the preview faded it.
- **Soft edges use a smaller mask.** When σ is over 1 px, the mask is rasterized at `max(1/8, 1/σ)` of the render size, so σ is about one mask pixel, and the GPU's linear filtering softens it a little more as it scales the mask up. The difference doesn't show on a soft edge, and it keeps the mask cheap to rebuild while you drag a corner with the image on the desktop. A hard edge (0 %) is always rasterized at full resolution.

The mask is built on the render thread. On an Apple-silicon Mac, a 4K mask takes about 2–7 ms for one or two regions at any soft edge. The worst case, 16 frame-sized regions of 64 points with a hard edge, takes about 50 ms, which shows as a brief hitch while dragging.

## Editing

In the editor's **Regions** panel:

- **Add region** drops a 4-corner quad over the middle of the visible picture (35–65 % of it). Drag a corner to move just that corner, like a projector keystone. Drag inside a region to move it.
- Hovering an edge of the selected region shows a dot; click there to add a corner and drag it. Right-click a corner, or click it and press Delete, to remove it (minimum 3).
- **Snow everywhere except the regions**, **Soft edge** and **Show outlines** do what they say. **Undo**/**Redo** (Ctrl+Z, Ctrl+Y or Ctrl+Shift+Z) go back up to 99 steps, one per completed drag.
- Changes show in the preview right away, are saved half a second after you stop, and go to the desktop live when the image is applied.

These preview bugs are fixed in the app (tests in `RegionEditorTests`):

- Undo and Redo drop the selection, because the selected region may no longer exist.
- Undo during a drag cancels the drag. Undo with an uncommitted soft-edge change reverts only that change instead of also stepping back.
- Drags stay on the visible part of the image, so a corner can't be lost off-screen under Fill or Center cropping.
- With **Show outlines** off nothing can be edited, because you can't see what you'd be changing. **Add region** turns the outlines back on.

## Why the handles are drawn by the preview renderer

The preview is a Win32 child window in an `HwndHost`, and WPF can't draw over it (airspace). The two options were a transparent layered window over the preview, or drawing the handles in the preview's own Direct3D renderer.

The app draws them in the renderer (`RegionOverlayRenderer`) and reads the mouse from the child window's messages (`PreviewHost.WndProc`), because:

- **It always lines up.** The renderer draws the handles with the same image rectangle it uses for the photo and the snow, so they stay pixel-aligned at any DPI. While the window is being resized, the preview rebuilds its scene only once the size settles, so for that moment clicks can land slightly off the drawn handles.
- **There's no second window to keep in sync.** A layered window would have to follow every move, resize, minimize, DPI change and virtual-desktop switch, hide when the Apply panel hides the preview, and it would visibly lag behind while the main window moves.

The cost is a small SDF shader for lines and circles, and mouse capture through `SetCapture`.

## Not built yet

A region with exactly four corners counts as a *plane* (`Region.IsPlane`), so that a later effect, such as rain on glass, can draw in its perspective using a homography. Snow uses only the mask, and no homography code exists yet.

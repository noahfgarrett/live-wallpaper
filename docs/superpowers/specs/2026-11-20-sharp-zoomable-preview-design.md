# Sharp, zoomable editor preview — design (short)

Date: 2026-11-20
Status: approved 2026-11-20 ("Yes, build it").

## Intent

The user said: "the image preview within the app while you're modifying it can lose a lot of quality. need to keep good quality and allow for zooming in and minor adjustments". They described the loss as **soft or blurry**.

## Cause

`PreviewRenderer.BuildScene` decodes the photo at the size of the preview window (about 900 px wide), using WIC's Fant filter.
- Fine detail from a large photo is gone before it's drawn, so there is nothing to zoom into.
- The destination rectangle is fractional, so the bilinear stretch softens the picture slightly more.
- The effects are sized for the small window (`scale = targetHeight / 1080`), so they don't match the desktop either.

## Goals

- **Sharp at Fit:** the preview looks sharp at its normal size, using a proper downscale filter rather than a soft one.
- **Zoom:** from Fit up to 400 %, where 100 % means one screen pixel per desktop pixel. Region corners and water arrows can be placed precisely at any zoom.
- **WYSIWYG:** the preview shows what the desktop will show, including effect sizes.

Non-goals:
- zoom beyond 400 %;
- saving the zoom between sessions;
- zoom in the library thumbnails.

## Approaches

| | Approach | Trade-off |
|---|---|---|
| **A** | **Render the scene at the primary monitor's resolution into an offscreen texture, then show a zoomed and panned view of it with a high-quality filter.** | **Chosen (builder's call).** The effects are exactly the desktop's at any zoom, and the effects and masks need no changes. It costs more GPU while the editor is open, about 4× the pixels on a 4K monitor. |
| B | Keep rendering at the preview's size, and zoom by enlarging the image destination and region bounds. Re-decode the visible part at the new size after the zoom settles. | It's the sharpest image at every zoom and the cheapest to run. But the effects keep their screen size while the photo is magnified, so the preview stops matching the desktop, and every zoom step costs a WIC decode. |

## Design

### Render size
- R is the primary monitor's pixel size (`_monitorSize`), scaled down proportionally to fit within 3840×2160.
- The scene, its masks and its frame copy are built for R, exactly as the engine builds them for a monitor.
- The photo is decoded at the size it lands on R.

### Camera (Core, `PreviewCamera`, pure and unit-tested)
- **State:** `Scale` (view pixels per R pixel) and `Offset` (the R-pixel point at the view's top-left).
- **Fit:** `min(view.W / R.W, view.H / R.H)`, centred.
- **Range:** the scale is clamped to `[fit, max(fit, 4)]`. When the view (in R pixels) is larger than R, the offset centres; otherwise it is clamped so the view stays inside R.
- **Operations:**
  - `ZoomAt(viewPoint, factor)`: the R point under the cursor stays put.
  - `Pan(viewDelta)`.
  - `Fit()`.
  - `Actual()`: 100 % around the view's centre.
  - `StepIn()` / `StepOut()`: ×1.25, snapping to Fit and to 100 % when it passes them.
  - `Resize(view)`: keeps the centre, and stays at Fit if it was at Fit.
- **Mapping:** `MapRect(RectangleF inR) → RectangleF inView` and `ToR(viewPoint)`.

### Presenting (Rendering, `PreviewPresenter`)
1. The scene renders into an offscreen B8G8R8A8 texture of size R with a full mip chain (render target and shader resource, with GenerateMips). Mips are regenerated each frame.
2. A blit pass draws the camera's view of R into the swap chain:
   - **Minifying** (scale < 1): sample the mip `floor(log2(1/scale))` with a Catmull-Rom bicubic filter for the remaining reduction of up to 2×.
   - **Magnifying:** Catmull-Rom from mip 0.
   - Areas outside R are cleared to the editor's dark background.
3. The region overlay is drawn after the blit, in view pixels.
   - Its image bounds are `camera.MapRect(imageBoundsOnR)`.
   - Handles keep their on-screen size at every zoom.

### Input (`PreviewHost`)
- **Wheel:** `WM_MOUSEWHEEL` zooms ×1.25 per notch at the cursor. Its lParam is in screen coordinates, so convert it to client coordinates.
- **Right button:** a right-drag of more than 4 px pans. A right-click without a drag stays `SecondaryClicked`, which removes a corner.
- **Middle button:** a middle-drag pans.
- **Space:** Space plus a left-drag pans, read with `GetKeyState(VK_SPACE)` at button-down.
- Events go to the editor: `ZoomRequested(viewPoint, factor)` and `PanRequested(delta)`.

### Editor
- **Zoom bar:** a compact bar under the preview, right-aligned, with these controls:
  - a −button;
  - the zoom percentage, measured against desktop pixels (for example "47 %");
  - a + button;
  - a **Fit** button.
- **Shortcuts:** Ctrl+0 fits, Ctrl+1 shows 100 %, and Ctrl+= / Ctrl+− step the zoom.
- **Region view:** `RegionView` uses the camera-mapped image bounds. The existing "drags stay on the visible part of the image" rule then means the part visible at the current zoom.
- **Resets:** the zoom returns to Fit when the image or the scaling changes, and when the editor opens.

## Testing

- **Core:** `PreviewCamera` covers Fit, the clamps, zoom at a point, pan limits, the step snapping, resize keeping Fit, and mapping round trips.
- **WARP:**
  - At 100 % the blit reproduces source pixels exactly.
  - At Fit on a 1-px checkerboard it gives near-uniform grey with no aliasing.
  - Areas outside R show the background colour.
- **Checklist:** sharpness at Fit, zooming and panning, handles at zoom, and the GPU reading with the editor open.

## Left to the builder

The exact shader taps, the bar's styling within the dark Fluent look, and the class layout.

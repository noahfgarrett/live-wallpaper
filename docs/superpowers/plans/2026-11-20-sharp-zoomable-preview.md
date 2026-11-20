# Sharp, zoomable editor preview — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The editor preview renders at desktop resolution, stays sharp at Fit, and zooms from Fit to 400 % with panning, so regions can be adjusted precisely.

**Architecture:**
- A pure Core `PreviewCamera` holds the zoom and pan maths.
- `PreviewPresenter` in Rendering owns an offscreen render target of the desktop's size and blits the camera's view of it with a Catmull-Rom-over-mips filter.
- `PreviewRenderer`, `PreviewHost` and `EditorView` in the App wire in the camera, the mouse and keyboard input, and a zoom bar.

**Tech Stack:** C# / .NET 10, xUnit, Vortice 3.8.3 (D3D11), HLSL ps/vs_5_0, WPF + WPF-UI 4.3.0.

**Spec:** `docs/superpowers/specs/2026-11-20-sharp-zoomable-preview-design.md`

## Global Constraints

- No new packages, nullable on, explicit return types, 0 warnings on CI. Conventional commits on `feat/m5-rain-water` ending with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- **Render size:** R is the primary monitor size, scaled proportionally to fit within 3840×2160.
- **Zoom range:** the scale is clamped to `[fit, max(fit, 4)]`. 100 % means 1 view pixel per R pixel. Steps are ×1.25, snapping to Fit and to 100 % when crossing them. A wheel notch is ×1.25 at the cursor.
- **Mouse:** a right-drag of more than 4 px pans, and a right-click without a drag still removes a corner. Middle-drag pans, and Space plus a left-drag pans.
- **Keyboard:** Ctrl+0 fits, Ctrl+1 shows 100 %, and Ctrl+= / Ctrl+− step the zoom.
- **Background:** areas outside R are cleared to (0.05, 0.08, 0.13), the preview's existing clear colour.
- **No per-frame allocations** in the render loop.
- **Local commands:**
  - First: `export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"`.
  - Build: `dotnet build -c Release`.
  - Core tests: `dotnet test tests/LiveWallpaper.Core.Tests -c Release`.
  - Windows tests compile on the Mac and run on CI.

## Review Focus

1. **Resizing the window while zoomed in** keeps the same centre and doesn't jump to Fit. At Fit it stays at Fit. (Task 1)
2. **Zooming out past Fit** stops at Fit, and the picture is centred, never stuck at a corner. (Task 1)
3. **Panning at Fit** does nothing. Panning while zoomed stops at the edges of the picture. (Task 1)
4. **A right-click with a 1–3 px jitter** still removes a corner and does not pan. (Task 3)
5. **A 1-px checkerboard at Fit** shows no moiré. (Task 2)

---

### Task 1: PreviewCamera (Core)

**Files:** create `src/LiveWallpaper.Core/Geometry/PreviewCamera.cs`; test `tests/LiveWallpaper.Core.Tests/PreviewCameraTests.cs`.

**Interfaces (produces):**
- `public sealed record PreviewCamera(Size Render, Size View, double Scale, PointF Offset)`, immutable. Each operation returns a new, already-clamped camera.
- Factories:
  - `static PreviewCamera Fit(Size render, Size view)`;
  - `static Size RenderSizeFor(Size monitor)`: proportional, within 3840×2160, minimum 1×1.
- Properties: `double FitScale`, `bool IsFit` (Scale within 1e-6 of FitScale), `int ZoomPercent` (`round(Scale × 100)`).
- Operations:
  - `ZoomAt(PointF viewPoint, double factor)`;
  - `Pan(SizeF viewDelta)`: dragging right by d moves the content right, so the Offset decreases by d / Scale;
  - `ToFit()`, `ToActual()` (100 % around the view's centre), `StepIn()`, `StepOut()`;
  - `Resize(Size view)`.
- Mapping: `RectangleF MapRect(RectangleF inRender)`, `PointF ToRender(PointF view)`, `PointF ToView(PointF render)`.
- **Clamping:** if `View / Scale` is wider than `Render.Width` on an axis, the offset on that axis is `(Render − View / Scale) / 2`, which centres it (negative is allowed). Otherwise the offset is clamped to `[0, Render − View / Scale]`.
- **Degenerate sizes:** zero or negative sizes are treated as 1.

- [ ] **Step 1:** Write the failing tests:
  - `Fit_CentresAndScales`: render 1920×1080, view 960×540 → Scale 0.5, offset (0,0); view 1000×540 → centred horizontally with a negative offset.
  - `RenderSizeFor_CapsAt4K`: 5120×2880 → 3840×2160; 1920×1080 is unchanged; 2560×1600 is unchanged.
  - `ZoomAt_KeepsThePointUnderTheCursor`.
  - `ZoomAt_ClampsToFitAndTo400`.
  - `ZoomOutPastFit_IsCentred`.
  - `Pan_AtFit_DoesNothing`.
  - `Pan_StopsAtEdges`.
  - `StepIn_SnapsTo100_WhenCrossing`: from Fit 0.47, the steps go 0.5875, 0.734, 0.918, then 1.0 (snapped), then 1.25.
  - `StepOut_SnapsToFit`.
  - `ToActual_KeepsCentre`.
  - `Resize_AtFit_StaysFit`.
  - `Resize_Zoomed_KeepsCentre`.
  - `MapRect_RoundTrips`.
  - `ZoomPercent_IsRelativeToRenderPixels`.
- [ ] **Step 2:** Run the Core tests. Expected: they fail to compile (RED).
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the Core tests. Expected: all pass (GREEN).
- [ ] **Step 5:** Commit `feat(core): preview camera for zoom and pan`.

### Task 2: PreviewPresenter (Rendering)

**Files:** create `src/LiveWallpaper.Rendering/PreviewPresenter.cs` and `src/LiveWallpaper.Rendering/Shaders/PreviewBlit.hlsl`; test `tests/LiveWallpaper.Windows.Tests/PreviewPresenterTests.cs` and `ShaderCompilerTests.cs`.

**Interfaces (produces):**
- `public sealed class PreviewPresenter(GpuDevice gpu) : IDisposable`.
- `ID3D11RenderTargetView Begin(Size render)`: lazily creates or recreates the offscreen texture when R changes (B8G8R8A8, MipLevels 0, RenderTarget|ShaderResource, GenerateMips) and returns its RTV. The scene renders into it.
- `void Present(ID3D11RenderTargetView target, Size view, PreviewCamera camera)`:
  - generates the mips;
  - clears the target to the background colour;
  - draws one quad covering `camera.MapRect(new RectangleF(0, 0, R.W, R.H))`, clipped to the view;
  - the pixel shader maps each view pixel to R with `ToRender` maths and samples with Catmull-Rom (9-tap, using bilinear taps) at mip `max(0, floor(log2(1 / Scale)))`, with SampleLevel at that mip and texel size scaled by `2^mip`;
  - unbinds its shader resource after drawing.
- Constant buffer: `RenderSize` (float2), `ViewToRenderScale` (float), `Offset` (float2), `Mip` (float), padding to 16 bytes.

- [ ] **Step 1:** Write the failing tests:
  - `PreviewBlit_Compiles`.
  - `Present_At100Percent_CopiesPixelsExactly`: an R-sized gradient, a view of 256×256 at Scale 1 with offset (100,50), view pixel (x,y) equals source (x+100, y+50) within ±1 per channel.
  - `Present_AtFit_CheckerboardBecomesGrey`: a 1-px black/white checker in a 2048×2048 R, a 256×256 view; every pixel is in 110–145 per channel.
  - `Present_OutsideRender_IsBackground`: fit with a wider view, so the side bars equal the background colour.
- [ ] **Step 2:** Build. Expected: compile errors (RED).
- [ ] **Step 3:** Implement. The Catmull-Rom taps follow the standard 9-tap bilinear-optimised form; clamp to the texture.
- [ ] **Step 4:** Build. Expected: 0 warnings.
- [ ] **Step 5:** Commit `feat(rendering): present a zoomed view of a desktop-sized preview`.

### Task 3: App wiring (renderer, input, zoom bar)

**Files:** modify `src/LiveWallpaper.App/Preview/PreviewRenderer.cs`, `src/LiveWallpaper.App/Preview/PreviewHost.cs`, `src/LiveWallpaper.App/Views/EditorView.xaml` and `src/LiveWallpaper.App/Views/EditorView.xaml.cs`.

**Interfaces:**
- `PreviewRenderer`:
  - builds the scene for R (`PreviewCamera.RenderSizeFor(reference)`), decoding as the engine does for a monitor of size R (`ScalingMath.ComputeDecodeSize(imageSize, R, scaling)` and `ComputeDestination`);
  - each frame calls `presenter.Begin(R)` → `scene.Update/Render(rtv, R)` → `presenter.Present(backBuffer, viewSize, camera)` → `overlay.Draw(backBuffer, viewSize, camera.MapRect(scene.ImageBounds), shapes, scale)`;
  - a resize no longer rebuilds the scene, because R doesn't depend on the view; it only resizes the swap chain;
  - gains `SetCamera(PreviewCamera camera)` (Volatile handoff).
- `PreviewHost`:
  - gains `SetCamera`;
  - gains `event EventHandler<PreviewZoomEventArgs> ZoomRequested` (view point, factor) and `event EventHandler<PreviewPanEventArgs> PanRequested` (delta);
  - handles `WM_MOUSEWHEEL` (0x020A; lParam is in screen coordinates → `ScreenToClient`), `WM_RBUTTONDOWN`/`UP` with the 4-px drag threshold, `WM_MBUTTONDOWN`/`UP` (0x0207/0x0208), and Space plus left-drag (`GetKeyState(0x20) < 0` at button-down);
  - captures the mouse while panning.
- `EditorView`:
  - holds `PreviewCamera _camera` (Fit when it opens, and on an image or scaling change);
  - recomputes `RegionView` from `_camera.MapRect(imageBoundsOnR)` with viewport = view pixels;
  - pushes the camera to the preview on every change;
  - zoom bar under the preview frame (right-aligned, compact, dark Fluent style): `−` button, `TextBlock` "47 %", `+` button, `Fit` button. Tooltips: "Zoom out (Ctrl+−)", "Zoom in (Ctrl+=)", "Fit (Ctrl+0)", plus the percentage's tooltip "Ctrl+1 for 100 %";
  - shortcuts in `OnPreviewKeyDown`.

- [ ] **Step 1:** Build. Expected: 0 warnings. WPF can't run on the Mac, so the checklist covers runtime behaviour.
- [ ] **Step 2:** Self-review the Review Focus items 1–4 against the code paths.
- [ ] **Step 3:** Commit `feat(app): sharp preview with zoom and pan`.

### Task 4: Docs, CI, PR

- [ ] **Step 1:** Add steps to the Milestone 5 checklist section ("Sharp, zoomable preview"):
  - sharpness at Fit compared with the desktop;
  - wheel zoom at the cursor;
  - zoom bar and shortcuts;
  - right-click still removes a corner, and right-drag, middle-drag and Space-drag pan;
  - dragging handles at 400 %;
  - resizing while zoomed;
  - GPU with the editor open.

  Update `docs/regions.md` "Editing" with zoom and pan. Commit `docs: zoomable preview checklist and editing notes`.
- [ ] **Step 2:** Push. CI is green with 0 warnings. Update the PR #5 description with a "Sharp, zoomable preview" bullet.

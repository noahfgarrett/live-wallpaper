# Live Wallpaper — Design

Date: 2026-11-19
Status: approved 2026-11-19

## 1. Intent

A native Windows 10/11 app that turns a still photo into a living desktop wallpaper. Real-time effects (snowfall first) are drawn over the image behind the desktop icons, costing almost nothing while it runs. It should feel like a finished consumer product, not a developer tool, and be published as a public GitHub repo named `live-wallpaper`.

What makes it different from existing tools (e.g. Lively): **effects can be confined to regions of the image**. Snow falls only outside the window in a photo of a room; later, rain runs down the window glass itself. Regions are picked with a projector-style corner tool that can grow extra vertices, with AI-assisted "click to select" as a later upgrade.

This document replaces the ChatGPT-drafted spec the user started from. Everything from that spec that still applies is restated here, so a builder needs nothing else.

### 1.1 Performance targets (carried over from the original spec)

For one 1080p monitor showing a still image with snow, these are the targets. They are not guaranteed numbers: they must be measured on the user's machine and reported as measured, never claimed.
- Engine RAM under 150 MB (private bytes; the UI process is measured separately and is not running most of the time).
- Average CPU under 2% on a modern desktop.
- Smooth 60 FPS.
- Minimal disk activity when idle, no network requests, no unbounded memory growth, and minimal GC during animation (target: zero allocations per frame in the steady state).

## 2. Goals, non-goals, anti-goals

**Goals (v1)**
- Import images (PNG, JPG, BMP, GIF first frame, TIFF, WebP where WIC supports it) into a library; preview, apply, configure, remove.
- Display the wallpaper behind desktop icons on every monitor, with per-monitor wallpaper and scaling (Fill / Fit / Stretch / Center).
- Realistic real-time snowfall with these settings: snow on/off, density 0–100%, speed 0–100%, wind intensity 0–100%, particle size 0–100%, motion blur on/off, and animation FPS 30 / 60 / Adaptive.
- Region masks: confine an effect to one or more regions of the image (or everything except them), with soft edges.
- Tray icon, settings persistence, optional start with Windows (off by default), auto-pause (fullscreen apps, lock, display off, battery saver).
- Performance view (FPS, RAM, CPU, active particles, render resolution).
- CI on GitHub Actions (Windows) that builds, tests and produces a downloadable zip.
- Honest README with measured numbers once the user has measured them on Windows.

**Non-goals (v1)**
- Effects other than snow (the architecture must make them easy; rain is the first planned follow-up).
- AI smart selection and depth-aware effects (planned milestone after v1, section 9).
- macOS or Linux versions of the wallpaper itself.
- An installer (v1 ships a portable zip). Code signing.
- Audio from video wallpapers.

**Anti-goals** (failure even if it technically works)
- Covering, hiding or breaking desktop icons, the taskbar, or icon click/drag/rename.
- Showing up in Alt+Tab, stealing focus, flickering on start/stop.
- Leaving orphan windows or processes, or a black/garbled desktop after exit or crash.
- Noticeable CPU/GPU/battery drain; unbounded memory growth; per-frame GC churn.
- Snow that looks like identical white dots sliding straight down.
- Any network traffic the user didn't explicitly trigger.
- UI controls that look functional but do nothing.

## 3. Constraints

- **Development happens on a Mac.** No Windows machine is reachable from the dev environment. Consequences:
  - Portable code (models, settings, snow parameter math, region geometry) is unit-tested locally with the .NET 10 SDK.
  - Windows-only code is verified by GitHub Actions on `windows-latest` (build + tests, including tests that can run headless on Windows such as shader compilation and WIC decoding).
  - Anything that needs a real desktop (embedding, Explorer restart, fullscreen pause, multi-monitor, tray, real performance numbers) is **verified by the user on their Windows machine**, guided by `docs/windows-test-checklist.md`. The builder must never claim these work without the user's report.
- **Audience:** the user and possibly a few coworkers. It's free and not sold, but it should still feel finished. Coworkers will download an unsigned portable zip, so the README explains the Windows SmartScreen "More info → Run anyway" step.
- **The user's test machine** is a Windows 11 24H2+ x64 laptop with its own screen plus two 27" external monitors. That's three displays and probably mixed DPI scaling.
  - Verifiable there: the 24H2+ embedding path, multi-monitor with mixed DPI, docking and undocking (displays added and removed), battery and battery-saver rules, and fullscreen/lock/display-off pausing.
  - The pre-24H2 layout (Windows 10 and older Windows 11) is still implemented, so coworkers on older builds are covered. It stays **unverified** and is listed as such in the README until someone tests it.
  - Multi-monitor is a first-class v1 path, not an afterthought, since it's the user's daily setup.
- **Repository:** public from the start, `live-wallpaper` under the user's GitHub account (`gh` is already authenticated), **MIT license**. It is created in Milestone 1 so CI runs from the first push. Because it's public from the first commit, the no-secrets / no-personal-paths / no-copyrighted-images rules apply to every commit, not just the final one.
- The look of the snow and the feel of the region editor are tuned first in a **browser preview** on the Mac (section 6), then ported.
- Stack (user-approved): C#, .NET 10, WPF for the UI, Direct3D 11 via Vortice.Windows, Win32 P/Invoke, Media Foundation for video, local JSON config, x64. Not Electron/Chromium/WebView2 for rendering.
- Code style: 4-space indentation, few inline comments, minimal abstraction, no DI container.
- Do not invent APIs or package versions; check upstream (Vortice.Windows repo/NuGet, Microsoft Learn).
- Lively Wallpaper is GPL-3.0 and this project is MIT, so **no Lively code is copied**. Its issues and source may be read to understand WorkerW behaviour; the implementation is written from Microsoft docs and observed behaviour.
- No copyrighted sample wallpapers in the repo. Any bundled sample must be the builder's own generated image or a clearly public-domain/CC0 source with attribution.
- No secrets, personal paths, or unrelated files in the public repo.

## 4. Approaches considered

| | Approach | Trade-off |
|---|---|---|
| A | Windows-native as specced; Windows CI only | Best Windows result, but nothing visual can be judged until the user runs it on Windows. |
| **B** | **A + throwaway browser preview of the snow and region editor on the Mac** | **Chosen.** Small duplicate effort; lets the most subjective parts (snow look, editor feel) be iterated visually before porting. |
| C | Cross-platform core (Avalonia + portable GPU layer), Windows-only embedding | Real app runs on the Mac, but departs from WPF/D3D, feels less native, and makes the RAM/CPU targets harder. |

The user picked **B**.

## 5. Architecture

### 5.1 Two processes (builder's call)

- **`LiveWallpaper.Engine.exe`**: always running while wallpapers are active. Pure Win32 + D3D11, **no WPF loaded**. Owns the wallpaper windows, rendering threads, tray icon, auto-pause logic, single-instance mutex, and the IPC server. This is what "Start with Windows" launches.
- **`LiveWallpaper.exe`** (WPF): library, editor, settings, performance view. Launched from the tray or Start menu, closed freely. If launched when the engine isn't running, it starts the engine. A second UI launch activates the existing window instead.

Reason: WPF alone consumes a large share of the 150 MB target. Keeping it out of the always-on process keeps steady-state RAM low, and a UI crash can't take down the wallpaper.

"Minimize to system tray" in the spec therefore means: closing the UI window just closes the UI process (the engine and tray keep running). The setting controls whether closing the UI with **no** active wallpaper also stops the engine.

### 5.2 Solution layout

```
live-wallpaper/
    src/
        LiveWallpaper.Core/        net10.0           models, settings, library, IPC contracts, snow parameter mapping, region geometry
        LiveWallpaper.Rendering/   net10.0-windows   D3D11 device, swapchains, effect system, snow effect, region masks, image scaling
        LiveWallpaper.Windows/     net10.0-windows   WorkerW embedding, monitors/DPI, tray, power/session/fullscreen notifications, startup registry
        LiveWallpaper.Media/       net10.0-windows   WIC image loading, thumbnails, Media Foundation video
        LiveWallpaper.Engine/      net10.0-windows   engine exe (composes Rendering + Windows + Media)
        LiveWallpaper.App/         net10.0-windows   WPF exe
    tests/
        LiveWallpaper.Core.Tests/      net10.0       runs on Mac and CI
        LiveWallpaper.Windows.Tests/   net10.0-windows  runs on CI only (shader compile, WIC decode, D3D WARP device smoke tests)
    prototype/                    browser preview (section 6); see 5.10 on publishing
    docs/
    .github/workflows/
    LiveWallpaper.sln
```

Portable logic lives in Core deliberately so most behaviour is testable on the Mac.

### 5.3 IPC

Named pipe (`\\.\pipe\LiveWallpaper.<user SID>`), length-prefixed UTF-8 JSON messages, contracts in Core. Messages: apply wallpaper to monitor, update effect settings (sent on slider change, coalesced to ≤30/s), update regions, pause/resume, get status/diagnostics, shutdown. The engine is the single writer of `settings.json`; the UI sends changes through IPC so the two never race.

### 5.4 Desktop embedding (highest-risk area)

- Spawn the WorkerW by sending `0x052C` to `Progman` with `SendMessageTimeout`.
- Support **both** desktop layouts:
  - Older Windows 10/11: WorkerW is a top-level sibling behind the one hosting `SHELLDLL_DefView`; parent the wallpaper window to that WorkerW.
  - Windows 11 24H2 and later: the WorkerW is reported to be a child of `Progman`; the parenting and z-order rules differ. The builder must research the current behaviour (Microsoft docs, Lively's open issues and source for understanding only) before implementing, and document findings in `docs/desktop-integration.md`.
- One wallpaper window per monitor, positioned in the WorkerW's client coordinates, per-monitor-DPI-aware v2 process, `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`, so no Alt+Tab, no focus, no taskbar entry.
- Recovery: listen for `TaskbarCreated` (Explorer restart) and `WM_DISPLAYCHANGE` / monitor add-remove; tear down and re-attach without restarting the process; debounce bursts.
- Fallback when attach fails: show a notification and keep the image visible by setting it as the normal Windows wallpaper (`IDesktopWallpaper`) without animation, rather than ever placing a window above the icons.
- On exit or crash: destroy wallpaper windows, then force Explorer to repaint the real wallpaper (re-apply the user's current Windows wallpaper via `SystemParametersInfo(SPI_SETDESKWALLPAPER)`) so the desktop is never left black. The engine records the user's original Windows wallpaper at first run and never permanently changes it (builder's call).
- Hard crash (builder's call, no watchdog process): an unhandled-exception handler runs the restore when it can. A marker file records an unclean shutdown, so the next engine start restores the desktop first before attaching again. When Windows destroys a crashed process's windows, the stale paint may stay until Explorer repaints. This is a documented limitation, and the checklist includes a forced-kill test.

### 5.5 Rendering

- One D3D11 device shared by all monitors in the engine; a flip-model DXGI swapchain per wallpaper window. WARP device used only in CI tests.
- A render thread drives all monitors: wait on the swapchain's frame-latency waitable object (no busy-wait), update, draw, present. FPS 30 / 60 / Adaptive (Adaptive = monitor refresh rate capped at 60 when on AC power, 30 on battery; drops to 0 while paused).
- The background image is decoded once via WIC into a GPU texture at the size it is displayed (not the source size when larger), with scaling applied by the draw transform. No per-frame image copies or resource recreation; swapchains are resized only on display changes.
- While paused nothing is presented and the thread sleeps on an event.

### 5.6 Effect system

```csharp
interface IEffect : IDisposable
{
    string Id { get; }
    void Initialize(RenderContext ctx);          // create GPU resources
    void ApplySettings(EffectSettings settings);  // cheap, called from IPC
    void Resize(Size2 pixelSize, float dpiScale);
    void Update(in FrameTime time);               // CPU-side per-frame state only
    void Render(RenderContext ctx, RegionMask? mask);
}
```

Each effect has its own settings record (JSON-serialized with a type discriminator), its own shaders, and is registered in a small static registry. A wallpaper holds an ordered list of **effect instances**; each instance has its own settings and its own region set (section 5.8). Adding rain later = one new `IEffect` plus its settings record, no engine changes.

### 5.7 Snowfall

GPU-instanced, **stateless** particles: each flake's position is computed in the vertex shader from its index, a per-flake random seed, and a few per-frame constants. Zero per-frame allocations, no CPU particle loop, recycling is a modulo wrap.

- **Layers:** three depth layers (far, mid, near) with different size, fall speed, opacity, sharpness and wind response. Near flakes are larger, faster, softer (bokeh-like) and fewer.
- **Per-flake variation:** size, fall speed, sway amplitude/frequency/phase, rotation and spin, sprite variant (several soft flake sprites in a small atlas generated at startup, not loaded from files).
- **Wind:** CPU computes a smooth wind value each frame (base wind from the setting plus occasional gusts from layered low-frequency noise) and integrates it into a horizontal offset passed to the shader, so gusts never cause flakes to jump.
- **Motion blur (optional setting):** quads stretched along each flake's velocity with a fade; off by default.
- **Settings mapping (0–100%):** density → active instance count per megapixel of render area (capped); speed → fall-speed multiplier; wind → base wind + gust strength; size → size multiplier. Mapping functions live in Core and are unit-tested.
- The exact look (curves, counts, sprite softness) is tuned in the browser preview first; the preview's GLSL and the HLSL share the same math and constants, recorded in `docs/snow-model.md`.

### 5.8 Regions

- A **region** is a polygon in **normalized image coordinates** (0–1 relative to the source image), so it stays attached to the right part of the picture under any scaling or crop.
- Each effect instance has a list of regions plus `Invert` (show the effect everywhere *except* the regions) and `Feather` (soft edge width, % of image size).
- Editing UX (to be tuned in the preview):
  - "Add region" drops a 4-corner quad over the image. Dragging corners works like a projector keystone.
  - Click an edge to add a vertex; right-click/Delete on a vertex removes it (minimum 3).
  - Drag the region body to move it; handles stay usable at any zoom; Undo/Redo.
- A region with exactly 4 vertices is also stored as a **plane**, so future effects (rain on glass) can map their drawing into its perspective via a homography. v1 snow ignores this and uses only the mask.
- At render time the engine rasterizes the regions into a single-channel mask texture at render resolution (rebuilt only when regions, monitor size or scaling change), with feather as a blur pass. The effect multiplies its output by the mask in the pixel shader. Cost while running: one texture sample per pixel.
- Polygon rasterization and homography math live in Core with unit tests.

### 5.9 Video (later milestone, isolated)

Media Foundation `IMFMediaEngine` in DXGI mode, sharing the engine's D3D11 device (hardware decoding where available), frames transferred into a texture each frame, looping, muted, same scaling modes. Effects may draw over video. Unsupported codec or corrupt file → clear error in the UI, wallpaper not applied. If this can't be made stable, it ships later and the README says so.

### 5.10 App UI (WPF)

Dark Windows 11 style; WPF-UI (lepoco/wpf-ui) is the default choice for Fluent styling if its current release supports .NET 10, otherwise hand-built styles.
- **Library:** card grid with thumbnails (cached JPEGs in the data folder), with Preview, Apply (choose monitor), Edit effects, and Remove actions on each card. Import by button and drag-and-drop.
- **Editor:** large live preview hosted as a Win32 child window (`HwndHost`) running the same Rendering code in the UI process, plus the snow controls and the region tool overlaid on the preview. Slider changes update the preview immediately and the engine after "Apply" (or live, when the wallpaper being edited is the one applied).
- **Settings:** start with Windows, close behaviour, pause rules (when not visible, fullscreen apps, configurable app list, lock, display off, battery saver), FPS, performance mode, default scaling, monitor assignment.
  - **Performance mode** (builder's call) is a **Low-power mode** toggle. When on, or when Windows battery saver is active and the rule is set to "reduce": 30 FPS cap, effect density capped at 50%, motion blur off, and the render resolution capped at 1440p per monitor. The pause rules can also pause rendering entirely in low-power mode.
- **Performance:** engine FPS, private bytes, CPU %, active particles, per-monitor render resolution; polled once per second only while the page is visible.

### 5.11 Library and data

- Data folder `%LOCALAPPDATA%\LiveWallpaper\` holds `settings.json`, `library.json`, `library\<id>\` (copied source file + thumbnail + regions), and `logs\` (rolling, size-capped).
- Import **copies** the file into the library (builder's call: robust if the original is moved). Remove deletes only the library copy, never the original.
- JSON has a schema version field; corrupt files are backed up and replaced with defaults, never crash.

### 5.12 Auto-pause

Engine-side and event-driven, with no polling loops. Each monitor pauses independently.
- **Pause when not visible (on by default, user-requested):** a monitor stops rendering whenever its wallpaper can't be seen. That means a maximized or fullscreen window is on it, or visible windows together cover it almost completely (≥ 95% of its area). It resumes as soon as any part of the desktop shows again (a window is minimized, moved or closed, or the user presses Win+D).
  - Triggered by `SetWinEventHook`: foreground change, minimize start/end, and location change filtered to top-level windows, debounced to about 250 ms.
  - Coverage is computed by walking the visible top-level windows in z-order, skipping cloaked windows (`DWMWA_CLOAKED`, e.g. windows on other virtual desktops), tool windows and the taskbar.
  - With three displays this is the biggest saving: usually only the monitors showing the desktop render at all.
- Fullscreen apps and games: `SHQueryUserNotificationState` (D3D fullscreen / presentation mode / busy) pauses **all** monitors, configurable. A fullscreen game is the case where GPU time matters most.
- User app list: pause while any listed process is foreground (or running, per setting).
- Lock / session switch: `WTSRegisterSessionNotification`.
- Display off: `RegisterPowerSettingNotification(GUID_CONSOLE_DISPLAY_STATE)`.
- Battery saver / on battery: `GUID_POWER_SAVING_STATUS`, `GUID_ACDC_POWER_SOURCE`; behaviour = pause, or drop to 30 FPS and halve density (setting).
- Resume is tested for every path: the last frame stays on screen while paused.

### 5.13 Tray

Engine-owned `Shell_NotifyIcon` with a Win32 popup menu: Open Live Wallpaper, Pause/Resume, Change wallpaper (submenu of recent library items), Settings, Exit. Re-added on `TaskbarCreated`. Exit stops the UI process if running, tears down windows, restores the desktop, removes the icon.

## 6. Browser preview (throwaway spike, Milestone 0)

- A single `prototype/index.html` with WebGL2. No build step and no external network requests; opens from disk on the Mac.
- The user drags in their own photo. It shows Fill/Fit scaling, snow with all v1 sliders, and the region tool (quad, add/remove vertex, move, feather, invert, multiple regions).
- Purpose: agree on the snow look and region editing feel. The resulting constants and interaction rules are written into `docs/snow-model.md` and section 5.8, then ported. The preview is not maintained afterwards.
- Builder's call: the preview stays in the repo under `prototype/` as a design reference (it contains no user images), clearly labelled as not part of the app.

## 7. Testing and verification

- **Mac (local):** `dotnet test tests/LiveWallpaper.Core.Tests` covers settings round-trip and corruption recovery, library operations, IPC message serialization, snow parameter mapping, wind integration continuity, polygon rasterization, feather, homography, and normalized ↔ screen coordinate mapping under each scaling mode.
- **CI (windows-latest):** build the whole solution (Release, x64); run all tests including Windows tests (HLSL shaders compile, WIC decodes test images generated at test time, D3D11 WARP device renders a snow frame without errors, a mask texture builds); `dotnet publish` the engine and app into a zip artifact.
- **User on Windows:** `docs/windows-test-checklist.md` lists each runtime behaviour to check (icons clickable, no Alt+Tab, Explorer restart, monitor unplug/replug, DPI changes, fullscreen game pause, lock, sleep, rapid wallpaper switching, repeated launches, tray exit leaves no processes, RAM/CPU readings from Task Manager and the perf view). Results are reported back and fixed iteratively. The README's performance section uses only these measured numbers.

## 8. Milestones

0. **Browser preview:** snow look and region editor tuned with the user on the Mac.
1. **Foundation:** solution, Core with tests, CI pipeline green, engine shows a static image behind the icons on all monitors, clean exit and restore. *User checks it on Windows.*
2. **Snow:** D3D11 renderer, stateless snow ported from the preview, frame pacing, settings via a debug IPC path. *User checks look and performance.*
3. **App:** WPF library, editor with live preview, settings, performance view, tray, IPC, persistence, single instance, start with Windows.
4. **Regions:** region editor in the app, masks in the engine.
5. **Robustness:** auto-pause, Explorer/display recovery hardening, error handling, rapid-switch and leak testing.
6. **Video:** isolated, may slip.
7. **Polish for publishing:** README, screenshots (from the user's machine), checklist results, CI artifact. The repo has been public since Milestone 1. No GitHub Release until the user has validated a build.

## 9. After v1 (planned, not built now)

- **Rain** (first new effect), including **rain on glass** using 4-point plane regions: streaks and droplets that refract the image behind them.
- **Smart selection:** click a point to get a suggested region outline from a compact Segment Anything model (e.g. MobileSAM, Apache-2.0) via ONNX Runtime + DirectML. The outline is converted to an editable polygon. Runs only in the editor; the model is downloaded on explicit user action.
- **Depth-aware effects:** a depth-estimation model (e.g. Depth Anything V2 Small, Apache-2.0) computes a depth map once per image, letting snow fall between foreground and background objects.
- Fog, fireflies, floating particles, leaves, twinkling lights, clouds, aurora, lighting.

## 10. Left to the builder

- Exact Vortice.Windows and other package versions (latest stable that supports .NET 10).
- Internal class structure within each project, logging library (or a small hand-rolled logger).
- Shader organisation and whether HLSL is compiled at build time or at startup with a bytecode cache.
- Precise IPC message shapes.
- Thumbnail size and cache format.
- Visual polish details within the dark Windows 11 direction.

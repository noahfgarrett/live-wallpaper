# Milestones 2 + 3 — Live Snow and the Live Wallpaper App Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (the user chose Native execution). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A real app. `LiveWallpaper.exe` (WPF, dark Windows 11 style) lets the user:
- import images into a library
- open one in an editor with a live snow preview and adjust the sliders while watching
- click **Apply** to set it as the desktop background on one or all monitors

There the engine renders the snow live behind the icons. A tray icon and settings round it out.

**Architecture:** Two processes, as designed.
- **The engine** (Win32 + D3D11, no WPF) gains a paced render loop, the snow effect, a named-pipe server and a tray icon.
- **The app** is a WPF client using WPF-UI. It talks to the engine over the pipe, owns the image library, and renders its own preview with the same Rendering code in an `HwndHost`, on its own render thread.
- **Portable logic stays in Core and is tested on the Mac:** the snow model, bounded shader constants, frame pacing, the library, and the IPC contracts and framing.

**Tech Stack:** .NET 10, WPF + WPF-UI 4.3.0, Vortice 3.8.3 (D3D11/DXGI/D3DCompiler/WIC), `System.IO.Pipes`, xUnit v3.

**Spec:** `docs/superpowers/specs/2026-11-19-live-wallpaper-design.md` (sections 5.1, 5.3, 5.5–5.7, 5.10, 5.11, 5.13). The snow constants come from `docs/snow-model.md` on PR #2 and are restated in Task 1.

**Branch:** `feat/m2-m3-app`, cut from `feat/m1-foundation` (PR #1 is not merged yet, so this PR will stack on it).

## Global Constraints

- **Dependencies:** add only `WPF-UI` 4.3.0. The user approved it on 2026-11-19, and it brings `WPF-UI.Abstractions` transitively. Everything else is the BCL or already approved. Pin the exact version in `Directory.Packages.props`, and the lock files must stay stable (`dotnet restore --locked-mode` passes).
- **Platform split:** WPF builds on the Mac, but tests that touch Win32, D3D or WPF run only on CI, through `WindowsOnly.Require()`.
- **No phantom features:** every control in the UI must work. Settings not built in this milestone are left out entirely: pause rules, low-power mode, video and regions.
- **No settings migration:** the settings schema becomes version 2, and a version 1 file is treated as corrupt (backed up, defaults used). This is acceptable because only test builds exist.
- **The engine stays the only writer of `settings.json`. The app is the only writer of `library.json`** and the library folder (the engine's `--set` imports through the same `LibraryStore` with atomic writes).
- **Removing a library item never deletes the original file**, only the library copy.
- **Rendering performance:**
  - The engine's frame loop does no per-frame managed allocations.
  - Shader inputs stay bounded for multi-day uptime, per the `docs/snow-model.md` requirements.
  - With snow disabled, the engine presents once and then idles.
  - Frames are presented with `Present(0)` after timer pacing, so three monitors don't serialise on vsync.
- **UI:** dark theme, WPF-UI `FluentWindow` with Mica, sentence-case copy, and every action named for what it does.
- 4-space indent, `TreatWarningsAsErrors`, nullable on, conventional commits on `feat/m2-m3-app`.

## Review Focus

1. **Opening the app while the engine is stopped, or after it crashed:** the app starts the engine and connects within about 3 s. If it can't, it shows "The wallpaper engine isn't running" with a **Start** button, and never hangs the UI. Covered by `EngineClient_ConnectTimesOut_ReturnsNotRunning` (Task 3) and the UI state in Task 9.
2. **Dragging a slider fast while that wallpaper is applied:** the engine gets coalesced updates (at most 30/s), with no pipe backlog, and the last value wins. Covered by `UpdateCoalescer_SendsLatestOnly` (Task 3).
3. **Removing the library item that's currently applied:** the engine stops showing that monitor and the normal desktop returns there, and the original file is untouched. Covered by `Remove_KeepsOriginalFile` (Task 2) and the engine's handling of a missing item in Task 6.
4. **Running for a week:** snow positions stay smooth, with no float32 drift. Covered by `SnowFrameBuilder_AfterAWeek_StaysBoundedAndSmooth` (Task 1).
5. **Opening the app twice, or closing it while a wallpaper is applied:** there is one app window (a second launch brings the first to the front), and the wallpaper keeps running after the app closes. Covered by the app single-instance test (Task 8) and the manual checklist.

---

### Task 1: Snow model and bounded frame constants (Core)

**Files:**
- Create: `src/LiveWallpaper.Core/Snow/SnowModel.cs`, `src/LiveWallpaper.Core/Snow/FallClock.cs`, `src/LiveWallpaper.Core/Snow/WindModel.cs`, `src/LiveWallpaper.Core/Snow/SnowFrameBuilder.cs`, `src/LiveWallpaper.Core/Snow/FlakeAtlas.cs`
- Test: `tests/LiveWallpaper.Core.Tests/Snow/SnowModelTests.cs`, `SnowFrameBuilderTests.cs`, `FlakeAtlasTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed record SnowLayer(string Name, float Share, float SizeMin, float SizeMax, float FallMin, float FallMax,
      float OpacityMin, float OpacityMax, float Softness, float WindResponse, float SwayMin, float SwayMax, float SwayHzMin, float SwayHzMax);
  public static class SnowModel
  {
      public const float ReferenceHeightPx = 1080;
      public const int MaxFlakes = 12000;
      public const int MaxFlakesPerMegapixel = 1500;
      public const double PeriodSeconds = 3600;
      public static IReadOnlyList<SnowLayer> Layers { get; }
      public static int FlakeCount(double densityPct, double megapixels);
      public static double SpeedMultiplier(double speedPct);
      public static double SizeMultiplier(double sizePct);
      public static double WrapMarginPx(double scale);
  }
  public sealed class FallClock { public double Step(double dtSec, double speedPct); }
  public sealed class WindModel { public WindModel(uint seed); public (double WindPxPerSec, double OffsetPx) Step(double dtSec, double windPct, double heightPx); }
  public struct SnowFrameConstants
  {
      public float FallPhase;
      public float TimePhase;
      public float WindPxPerSec;
      public float SpeedMul;
      public float SizeMul;
      public float Margin;
      public float Scale;
      public float MotionBlur;
      public Vector4 WindOffsets;
  }
  public static class SnowFrameBuilder
  {
      public static void Build(double fallTime, double windOffsetPx, double windPxPerSec, double timeSec,
          double speedPct, double sizePct, bool motionBlur, Size target, ref SnowFrameConstants result);
  }
  public static class FlakeAtlas
  {
      public const int CellPx = 64;
      public const int Cells = 4;
      public static byte[] GenerateBgra();
  }
  ```
  - `GenerateBgra` returns 256×64, premultiplied, white with alpha.
  - `WindOffsets` holds `xyz` = the per-layer `mod(offset × windResponse, spanX)`.
- Values: `Layers`, the mapping formulas and the wind model are exactly those in `docs/snow-model.md`:
  - layer table: far 0.55, mid 0.35, near 0.10, with the listed ranges
  - flake count: `min(12000, round(1500·(d/100)^1.5·MP))`
  - speed multiplier: `0.25 + 1.5·s/100`
  - size multiplier: `0.5 + z/100`
  - wind:
    - base `120·w/100·h/1080`
    - gust frequencies 0.071, 0.113 and 0.029 Hz, weights 1, 0.6 and 0.4, the weighted sum divided by 2
    - gust = `max(0, g)²·0.8·base`
  - the wind phases come from a ported `mulberry32(seed)`
  - margin: `(12·1.5·1.5 + 40)·scale`
- Bounded constants:
  - `FallPhase = fract(fallTime / P)`
  - `TimePhase = fract(timeSec / P)` (the shader multiplies it by P and quantises frequencies to whole cycles per P)

- [ ] **Step 1: Write the failing tests.** Port every `prototype/tests/snowModel.test.js` case with the same values (1100 / 3110 / 12000 flakes, the multipliers, the shares summing to 1, fall-clock and wind smoothness, determinism, gust variation). Add:
  - `WrapMarginPx_IsIndependentOfSize`
  - `SnowFrameBuilder_PhasesStayInUnitRange`
  - `SnowFrameBuilder_AfterAWeek_StaysBoundedAndSmooth`: start at `fallTime = 7·24·3600·1.0`, then step 600 frames at 1/60 s. Every `FallPhase` must be in [0,1), every `WindOffsets` component must be in [0, spanX), and the frame-to-frame `ΔFallPhase·P` must be ≤ `(1/60)·1.75 + 1e-6`, wrap excepted.
  - `SnowFrameBuilder_DoesNotAllocate`: `GC.GetAllocatedBytesForCurrentThread()` delta over 10,000 calls is 0.
  - `FlakeAtlas_CentreOpaqueCornersClear`: each cell's centre alpha is ≥ 200 and its corner alpha is 0.
- [ ] **Step 2: Run** `dotnet test tests/LiveWallpaper.Core.Tests` → FAIL (types missing). **Step 3: Implement.** The atlas draws the same four sprites as `prototype/flakeAtlas.js`:
  - radial falloffs with stops (0:1, 0.35:0.8, 1:0) and (0:1, 0.5:0.92, 0.78:0.25, 1:0)
  - a six-armed star made of distance-to-segment strokes, 4.5 px wide with a 1.6 px soft edge
  - a 5-blob cluster with a 2.5 px soft edge

  The sprites are computed analytically per pixel, with no canvas.
- [ ] **Step 4: Run** → PASS. **Step 5: Commit** — `feat(core): port snow model with bounded frame constants`

### Task 2: Settings v2 and the image library (Core)

**Files:**
- Modify: `src/LiveWallpaper.Core/Models/AppSettings.cs`, `src/LiveWallpaper.Core/Settings/SettingsStore.cs` (validation), `src/LiveWallpaper.Core/Settings/CoreJsonContext.cs`
- Create: `src/LiveWallpaper.Core/Models/SnowSettings.cs`, `src/LiveWallpaper.Core/Models/LibraryItem.cs`, `src/LiveWallpaper.Core/Library/LibraryStore.cs`
- Modify: `src/LiveWallpaper.Core/AppPaths.cs` (add `LibraryDirectory`, `LibraryFile`)
- Test: `tests/LiveWallpaper.Core.Tests/LibraryStoreTests.cs`; update `SettingsStoreTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public enum FrameRateMode { Fps30, Fps60, Adaptive }
  public sealed record SnowSettings(bool Enabled = true, double DensityPct = 50, double SpeedPct = 50, double WindPct = 30, double SizePct = 50, bool MotionBlur = false);
  public sealed record MonitorAssignment(string MonitorId, Guid LibraryItemId, WallpaperScaling Scaling);
  public sealed record AppSettings
  {
      public int SchemaVersion { get; init; } = 2;
      public IReadOnlyList<MonitorAssignment> Monitors { get; init; } = [];
      public FrameRateMode FrameRate { get; init; } = FrameRateMode.Fps60;
      public WallpaperScaling DefaultScaling { get; init; } = WallpaperScaling.Fill;
      public bool KeepRunningWhenAppCloses { get; init; } = true;
  }
  public sealed record LibraryItem(Guid Id, string DisplayName, string FileName, DateTimeOffset ImportedAt, int PixelWidth, int PixelHeight, SnowSettings Snow);
  public sealed class LibraryStore(string libraryDirectory, TimeProvider timeProvider)
  {
      public IReadOnlyList<LibraryItem> Load();
      public LibraryItem Import(string sourcePath, int pixelWidth, int pixelHeight);
      public void Update(LibraryItem item);
      public bool Remove(Guid id);
      public string ImagePath(LibraryItem item);
      public string ThumbnailPath(LibraryItem item);
  }
  ```
  - The JSON enum names are `"fps30" | "fps60" | "adaptive"`.
  - `Import` copies the file to `<dir>/<id:N>/source<ext>`; the display name is the file name without its extension.
  - `ThumbnailPath` is `<dir>/<id:N>/thumb.jpg`.
- Validation:
  - Snow percentages must be in 0–100.
  - A schema version other than 2, or an empty `LibraryItemId`, makes the file corrupt.

- [ ] **Step 1: Write the failing tests:**
  - `Import_CopiesFileIntoLibraryAndSaves`
  - `Import_UsesFileNameAsDisplayName`
  - `Remove_KeepsOriginalFile`
  - `Remove_DeletesLibraryCopy`
  - `Remove_UnknownId_ReturnsFalse`
  - `Update_PersistsSnowSettings`
  - `Load_CorruptLibrary_BacksUpAndReturnsEmpty`
  - `Load_SnowPercentOutOfRange_TreatsAsCorrupt`
  - `SettingsV1File_IsTreatedAsCorrupt`
  - `Settings_RoundTripsFrameRateAndPreferences`
  - Update the existing assignment tests to the `LibraryItemId` shape.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement** (atomic writes, as in `SettingsStore`). **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** — `feat(core): add image library and settings v2`

### Task 3: Engine IPC contracts, framing, client and coalescer (Core)

**Files:**
- Create: `src/LiveWallpaper.Core/Ipc/EngineMessages.cs`, `src/LiveWallpaper.Core/Ipc/PipeFraming.cs`, `src/LiveWallpaper.Core/Ipc/EnginePipeServer.cs`, `src/LiveWallpaper.Core/Ipc/EngineClient.cs`, `src/LiveWallpaper.Core/Ipc/UpdateCoalescer.cs`
- Test: `tests/LiveWallpaper.Core.Tests/Ipc/*.cs`

**Interfaces:**
- Produces:
  ```csharp
  [JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
  [JsonDerivedType(typeof(ApplyWallpaper), "apply")] [JsonDerivedType(typeof(UpdateSnow), "snow")]
  [JsonDerivedType(typeof(SetPaused), "pause")] [JsonDerivedType(typeof(SetPreferences), "prefs")]
  [JsonDerivedType(typeof(GetStatus), "status")] [JsonDerivedType(typeof(Reload), "reload")] [JsonDerivedType(typeof(Shutdown), "shutdown")]
  public abstract record EngineRequest;
  public sealed record ApplyWallpaper(Guid LibraryItemId, string MonitorSelector, WallpaperScaling Scaling) : EngineRequest;
  public sealed record UpdateSnow(Guid LibraryItemId, SnowSettings Snow) : EngineRequest;
  public sealed record SetPaused(bool Paused) : EngineRequest;
  public sealed record SetPreferences(FrameRateMode FrameRate, WallpaperScaling DefaultScaling, bool KeepRunningWhenAppCloses) : EngineRequest;
  public sealed record GetStatus : EngineRequest;
  public sealed record Reload : EngineRequest;
  public sealed record Shutdown : EngineRequest;

  [JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
  [JsonDerivedType(typeof(Ok), "ok")] [JsonDerivedType(typeof(StatusReport), "status")] [JsonDerivedType(typeof(Failed), "error")]
  public abstract record EngineResponse;
  public sealed record Ok : EngineResponse;
  public sealed record Failed(string Message) : EngineResponse;
  public sealed record StatusReport(EngineStatus Status) : EngineResponse;
  public sealed record MonitorStatus(string MonitorId, string DeviceName, int Width, int Height, Guid? LibraryItemId, WallpaperScaling? Scaling, bool IsShown);
  public sealed record EngineStatus(bool IsPaused, double Fps, long PrivateBytes, double CpuPercent, int ActiveParticles, IReadOnlyList<MonitorStatus> Monitors, AppSettings Settings);

  public static class PipeFraming
  {
      public static Task WriteAsync<T>(Stream stream, T message, JsonTypeInfo<T> info, CancellationToken ct);
      public static Task<T?> ReadAsync<T>(Stream stream, JsonTypeInfo<T> info, CancellationToken ct);
  }
  public sealed class EnginePipeServer(string pipeName, Func<NamedPipeServerStream> createPipe, Func<EngineRequest, Task<EngineResponse>> handle) : IAsyncDisposable
  {
      public void Start();
  }
  public sealed class EngineClient(string pipeName)
  {
      public Task<EngineResponse?> SendAsync(EngineRequest request, TimeSpan timeout, CancellationToken ct);
  }
  public sealed class UpdateCoalescer<T>(Func<T, Task> send, TimeSpan minInterval) : IAsyncDisposable
  {
      public void Post(T value);
  }
  public static class EnginePipe { public static string Name { get; } }
  ```
  - Framing: one UTF-8 JSON object per line, ending in `\n`. Each message is capped at 1 MiB, and a longer one throws `InvalidDataException`.
  - Pipe name: `LiveWallpaper.Engine.<Environment.UserName>`.
  - The server accepts one request per connection, sends one response, and loops; it supports several clients one after another.
  - `SendAsync` returns `null` when connecting times out (the engine isn't running).
  - `UpdateCoalescer`: at most one in flight; while one is in flight, only the newest pending value is kept and is sent next. Send errors are swallowed and logged.

- [ ] **Step 1: Write the failing tests:**
  - `Requests_RoundTripWithTypeDiscriminator`, for each request type
  - `Framing_ReadsSequentialMessages`
  - `Framing_RejectsOversizedMessage`
  - `Server_HandlesRequestAndReplies`, over a real named pipe; this works on the Mac too
  - `Server_ServesSequentialClients`
  - `EngineClient_ConnectTimesOut_ReturnsNotRunning`, with no server and a 200 ms timeout, returns `null` in under 2 s
  - `UpdateCoalescer_SendsLatestOnly`: post 1..100 rapidly while the sender takes 50 ms; the values sent end with 100 and number at most 5
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** — `feat(core): add engine IPC over named pipes`

### Task 4: Snow renderer and wallpaper scene (Rendering)

**Files:**
- Create: `src/LiveWallpaper.Rendering/Shaders/Snow.hlsl`, `src/LiveWallpaper.Rendering/IEffect.cs`, `src/LiveWallpaper.Rendering/SnowEffect.cs`, `src/LiveWallpaper.Rendering/WallpaperScene.cs`
- Modify: `src/LiveWallpaper.Rendering/ImageQuadRenderer.cs`. It splits its clear out into `WallpaperScene`; `Draw` gains a `clear` parameter, default `true`, so the existing tests keep passing.
- Test: `tests/LiveWallpaper.Windows.Tests/SnowEffectTests.cs`, `WallpaperSceneTests.cs`
- Add a `ProjectReference` from Rendering to Core.

**Interfaces:**
- Consumes: `SnowModel`, `SnowFrameBuilder`, `FlakeAtlas`, `SnowSettings` (Tasks 1 and 2).
- Produces:
  ```csharp
  public readonly record struct FrameTime(double DeltaSeconds, double TotalSeconds);
  public interface IEffect : IDisposable
  {
      string Id { get; }
      int ActiveParticles { get; }
      bool IsAnimated { get; }
      void Update(in FrameTime time, Size targetPixels);
      void Render(GpuDevice gpu, ID3D11RenderTargetView target, Size targetPixels);
  }
  public sealed class SnowEffect : IEffect
  {
      public SnowEffect(GpuDevice gpu, uint seed);
      public void Apply(SnowSettings settings);
  }
  public sealed class WallpaperScene : IDisposable
  {
      public WallpaperScene(GpuDevice gpu, ImageQuadRenderer background, ID3D11ShaderResourceView image, RectangleF destination);
      public IList<IEffect> Effects { get; }
      public bool IsAnimated { get; }
      public void Update(in FrameTime time, Size targetPixels);
      public void Render(ID3D11RenderTargetView target, Size targetPixels);
  }
  ```
  - `SnowEffect.Apply` is thread-safe: it swaps in an immutable `SnowSettings` with `Volatile.Write`.
  - `IsAnimated` is `Enabled && FlakeCount > 0`.
  - `WallpaperScene.Render` clears to black, draws the background, then each effect.
- Shader: a port of `prototype/renderer.js` `SNOW_VS` / `SNOW_FS` into HLSL `vs_5_0` / `ps_5_0`, with the bounded-precision changes from `docs/snow-model.md`:
  - y: `span.y·frac(r3 + k·FallPhase) − margin`, with `k = round(fall·P / span.y)`
  - x: uses `WindOffsets[layer]`
  - sway and spin: their frequencies are quantised to `round(hz·P)/P`, and the time used is `TimePhase·P`
  - `SV_VertexID` / `SV_InstanceID`, a triangle strip of 4 vertices, and `DrawInstanced(4, count, 0, 0)`
  - a premultiplied blend state (`One`, `InverseSourceAlpha`)
  - the atlas from `FlakeAtlas.GenerateBgra()` as an immutable texture

  Per frame, the CPU updates one constant buffer (`SnowFrameConstants` plus a static layer table buffer created once) and makes no allocations.

- [ ] **Step 1: Write the failing tests** (WARP, offscreen 256×144 target, background a 2×2 dark-blue image):
  - `Snow_Disabled_LeavesBackgroundUntouched`: every pixel equals the background-only render.
  - `Snow_Density100_LightensSomePixels`: at least 1% and at most 60% of the pixels are brighter than the background.
  - `Snow_SameSeedAndTime_RendersIdentically`
  - `Snow_ShadersCompile`
  - `Scene_IsAnimatedOnlyWithSnowEnabled`
- [ ] **Step 2: Implement**; build on the Mac with 0 warnings. **Step 3: Push**; CI green.
- [ ] **Step 4: Commit** — `feat(rendering): add snow effect and wallpaper scene`

### Task 5: Frame pacing and the render loop (Core + Windows)

**Files:**
- Create: `src/LiveWallpaper.Core/Rendering/FramePacer.cs`; `src/LiveWallpaper.Windows/RenderLoop.cs`; `src/LiveWallpaper.Windows/Interop/Kernel32.cs` (add the waitable timer and `GetSystemPowerStatus`); `src/LiveWallpaper.Windows/Interop/User32.cs` (add `MsgWaitForMultipleObjectsEx`, `PeekMessageW`)
- Test: `tests/LiveWallpaper.Core.Tests/FramePacerTests.cs`, `tests/LiveWallpaper.Windows.Tests/RenderLoopTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public static class FramePacer { public static TimeSpan IntervalFor(FrameRateMode mode, double refreshHz, bool onBattery); }
  public sealed class RenderLoop : IDisposable
  {
      public RenderLoop();
      public void Run(Func<TimeSpan?> onFrame);
      public void RequestFrame();
      public void Stop();
  }
  public static class PowerStatus { public static bool IsOnBattery(); }
  ```
  - `RequestFrame` triggers an immediate frame, for example after a settings change while static.
  - `IntervalFor` values:
    - `Fps30` → 1/30 s
    - `Fps60` → 1/60 s
    - `Adaptive` → `1/min(refreshHz, 60)` on AC and 1/30 s on battery
    - a refresh rate ≤ 0 is treated as 60
- How `RenderLoop` works:
  - It uses a high-resolution waitable timer (`CREATE_WAITABLE_TIMER_HIGH_RESOLUTION`) plus `MsgWaitForMultipleObjectsEx(QS_ALLINPUT, MWMO_INPUTAVAILABLE)`.
  - Messages are pumped with `PeekMessage` while it waits.
  - `onFrame` returns the delay until the next frame, or `null` to idle until `RequestFrame`.
  - It replaces `MessageWindow.RunMessageLoop` in the engine. `Stop()` is what `MessageWindow.Quit()` now triggers.

- [ ] **Step 1: Write the failing tests:**
  - `FramePacer` cases: 30 / 60; adaptive at 144 Hz on AC gives 60; adaptive on battery gives 30; refresh 0 gives 60.
  - `RenderLoop_CallsFrameAtRequestedInterval`, on CI: about 30 frames over 1 s with a 33 ms interval, within ±30%.
  - `RenderLoop_IdlesWhenFrameReturnsNull`
  - `RenderLoop_StopEndsRun`
- [ ] **Step 2: Run** the Core tests → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS, and CI is green.
- [ ] **Step 5: Commit** — `feat(windows): add paced render loop`

### Task 6: Engine — live snow, library assignments, IPC server, diagnostics

**Files:**
- Modify: `src/LiveWallpaper.Engine/EngineHost.cs`, `src/LiveWallpaper.Engine/Program.cs`
- Create: `src/LiveWallpaper.Engine/EngineRequests.cs` (request handling), `src/LiveWallpaper.Engine/Diagnostics.cs`
- Create: `src/LiveWallpaper.Windows/PipeSecurityFactory.cs`: `NamedPipeServerStreamAcl.Create` restricted to the current user, async and byte mode.

**Interfaces:**
- Consumes: everything above.

Behaviour:
- **Building:**
  - Each assignment resolves its `LibraryItemId` to `LibraryStore.ImagePath`. A missing item or file skips that monitor and logs it.
  - Each shown monitor gets a `WallpaperScene` with a `SnowEffect`, whose seed is the hash of the monitor id, and the item's `SnowSettings` are applied.
- **Frames:** `onFrame` updates the shared `FallClock` / `WindModel`, then updates, renders and `Present(0)`s every animated scene. It returns `FramePacer.IntervalFor(settings.FrameRate, refresh of the primary monitor, PowerStatus.IsOnBattery())`. If no scene is animated, it returns `null` after one final present.
- **IPC:** the pipe server runs on the thread pool. Requests are queued with a `TaskCompletionSource`, the engine thread is woken with `RequestFrame()`, and requests are handled at the start of the next loop iteration.
  - `ApplyWallpaper`: write the assignments (`all` means every current monitor, otherwise that id), save `settings.json`, then rebuild.
  - `UpdateSnow`: live-apply to the scenes showing that item. Nothing is persisted, because the app persists the library.
  - `SetPaused`: stop animating; the last frame stays.
  - `SetPreferences`: save, and the new pacing applies from the next frame.
  - `GetStatus`: fill `EngineStatus` from `Diagnostics`.
  - `Reload`: rebuild.
  - `Shutdown`: `Stop()`.
- **Diagnostics:**
  - FPS over a 1 s window
  - `Process.PrivateMemorySize64`, read on request only
  - CPU % from the `TotalProcessorTime` delta divided by elapsed time and core count, between two status requests
  - active particles: the sum of `SnowEffect.ActiveParticles`
- **CLI `--set`:** imports the file into the library (reading its size with `WicImageLoader.ReadSize`), assigns it, and sends `Reload` over the pipe, or saves the settings directly if the engine isn't running. The other verbs are unchanged.
- **Removed:** the M1 `LiveWallpaper.Reload` window message, replaced by IPC.

- [ ] **Step 1: Implement.** Build with 0 warnings.
- [ ] **Step 2: Add `EngineRequestsTests`** to Windows.Tests (CI): drive the request handler against a fake scene registry. `UpdateSnow` for an item that isn't shown is a no-op that returns `Ok`; `ApplyWallpaper` with an unknown monitor id returns `Failed`.
- [ ] **Step 3: Push**; CI green. **Step 4: Commit** — `feat(engine): render live snow and serve the app over IPC`

### Task 7: Tray icon

**Files:**
- Create: `assets/LiveWallpaper.ico`, generated by `tools/make-icon.py`. It's an original six-armed snowflake on a rounded night-blue tile at 16/24/32/48/256 px, using Pillow on the dev machine only; the `.ico` is committed and the script is kept for regeneration.
- Create: `src/LiveWallpaper.Windows/TrayIcon.cs`; add `Shell_NotifyIconW`, `CreatePopupMenu`, `AppendMenuW`, `TrackPopupMenuEx`, `SetForegroundWindow`, `LoadImageW` and `DestroyMenu` to `User32` / `Shell32`.
- Modify: `src/LiveWallpaper.Engine/LiveWallpaper.Engine.csproj` (`ApplicationIcon`, and embed the `.ico`), `EngineHost.cs`

**Interfaces:**
- Produces:
  ```csharp
  public enum TrayCommand { OpenApp, PauseResume, OpenSettings, Exit, ApplyRecent }
  public sealed class TrayIcon : IDisposable
  {
      public TrayIcon(nint messageWindow, nint icon, string tooltip);
      public void Show();
      public void ShowMenu(bool isPaused, IReadOnlyList<(Guid Id, string Name)> recent);
      public bool TryHandle(uint msg, nuint wParam, nint lParam, out TrayCommand command, out Guid recentId);
  }
  ```
  - `Show` is called again on `TaskbarCreated`.
- Menu:
  - Open Live Wallpaper
  - Pause animation / Resume animation
  - Change wallpaper ▸ (up to 8 most recent library items)
  - Settings
  - Exit
- Behaviour:
  - Open launches `LiveWallpaper.exe` from `AppContext.BaseDirectory`, or focuses it if it's already running.
  - Settings launches `LiveWallpaper.exe --page settings`.
  - Exit asks a running app to close (`WM_CLOSE` to its main window), then stops the engine.
  - Double-clicking the icon opens the app.

- [ ] **Step 1: Implement.** **Step 2: Build.** **Step 3: Commit** — `feat(engine): add tray icon`

### Task 8: App shell (WPF + WPF-UI)

**Files:**
- Create: `src/LiveWallpaper.App/LiveWallpaper.App.csproj` (`WinExe`, `UseWPF`, `AssemblyName LiveWallpaper`, `ApplicationIcon`, PerMonitorV2 `app.manifest`), `App.xaml(.cs)`, `MainWindow.xaml(.cs)`, `Services/EngineConnection.cs`, `Services/SingleAppInstance.cs`
- Modify: `Directory.Packages.props` (add `WPF-UI` 4.3.0), `LiveWallpaper.sln`
- Test: `tests/LiveWallpaper.Windows.Tests/SingleAppInstanceTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed class EngineConnection
  {
      public EngineConnection(EngineClient client, string engineExePath);
      public Task<bool> EnsureRunningAsync(CancellationToken ct);
      public Task<EngineStatus?> GetStatusAsync(CancellationToken ct);
      public Task<EngineResponse?> SendAsync(EngineRequest request, CancellationToken ct);
  }
  public sealed class SingleAppInstance : IDisposable
  {
      public static SingleAppInstance? TryAcquire(string name);
      public static void ActivateExisting(string windowTitle);
  }
  ```
  - `EnsureRunningAsync` starts the engine if its pipe doesn't answer within 300 ms, then waits up to 3 s.
- Shell:
  - `ui:FluentWindow` with Mica, 1100×720 with a minimum of 860×560, titled "Live Wallpaper"
  - `ui:NavigationView` (left, compact) with Library, Settings and Performance
  - dark theme only
  - an engine status line in the footer
  - `--page settings` opens Settings
- Closing: if `KeepRunningWhenAppCloses` is false, send `Shutdown`; either way the app process exits.
- [ ] **Step 1: Write the failing test** `TryAcquire_SecondReturnsNull`. **Step 2: Implement the shell**; it builds on the Mac. **Step 3: Push**; CI green.
- [ ] **Step 4: Commit** — `feat(app): add app shell with engine connection`

### Task 9: Library page with import, thumbnails and Apply

**Files:**
- Create: `src/LiveWallpaper.App/Pages/LibraryPage.xaml(.cs)`, `src/LiveWallpaper.App/Pages/ApplyDialog.xaml(.cs)`, `src/LiveWallpaper.Media/ThumbnailWriter.cs`
- Test: `tests/LiveWallpaper.Windows.Tests/ThumbnailWriterTests.cs`

**Interfaces:**
- Produces: `public static class ThumbnailWriter { public static void Write(string sourcePath, string destinationJpeg, int maxEdge = 480); }`
  - It uses `WicImageLoader` (honouring orientation) and the WIC JPEG encoder at quality 0.85.

UI:
- **Grid of cards:** a 16:9 thumbnail, the name, and a snow badge when snow is enabled.
- **Card actions:**
  - **Apply** opens a dialog with "All monitors", each monitor listed by number and resolution from `MonitorEnumerator` with the primary first, and a scaling selector defaulting to `DefaultScaling`. It sends `ApplyWallpaper`.
  - **Edit** opens the editor.
  - **Remove** confirms with "Remove from library? The original file isn't deleted.", then sends `Reload` if the item was applied.
- **Import:** an **Import images** button (`OpenFileDialog`, multi-select, `*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp`) and drag-and-drop onto the page.
- **Bad files:** a file that fails `ReadSize` shows an info bar naming it ("photo.txt isn't an image Windows can read") and is skipped.
- **Empty state:** "Your library is empty. Import a photo to get started." with the import button.
- **Applied:** an applied item shows an "On desktop" badge, using `GetStatus`.
- **Engine not running:** an info bar reads "The wallpaper engine isn't running." with a **Start** button.

- [ ] **Step 1: Write the failing tests**:
  - `ThumbnailWriter_WritesJpegWithinMaxEdge`
  - `ThumbnailWriter_HonoursOrientation`: an EXIF-6 source gives a portrait thumbnail
- [ ] **Step 2: Implement.** **Step 3: Push**; CI green. **Step 4: Commit** — `feat(app): add library page with import and apply`

### Task 10: Editor page with live preview

**Files:**
- Create: `src/LiveWallpaper.App/Pages/EditorPage.xaml(.cs)`, `src/LiveWallpaper.App/Preview/PreviewHost.cs` (an `HwndHost`), `src/LiveWallpaper.App/Preview/PreviewRenderer.cs` (the render thread)

**Interfaces:**
- Produces:
  ```csharp
  public sealed class PreviewHost : HwndHost
  {
      public void SetImage(string path, WallpaperScaling scaling);
      public void SetSnow(SnowSettings settings);
      public void SetFrameRate(FrameRateMode mode);
  }
  ```
  - `BuildWindowCore` creates a child window with `WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS` using the `WallpaperWindow` class registration pattern, and `DestroyWindowCore` stops the render thread first.
- `PreviewRenderer`:
  - It owns its own `GpuDevice`, a flip-model `WallpaperSurface`, a `WallpaperScene` and a `SnowEffect` on a dedicated background thread.
  - Pacing is `Present(1)` (60 fps) or `Present(2)` (30 fps).
  - Size changes arrive through a volatile size field: the UI thread reports the child's physical pixel size (WPF size × DPI scale), and the renderer resizes on its thread.
  - Settings arrive via `SnowEffect.Apply`.
  - The preview decodes the image at preview size.
- Page layout:
  - the preview fills the left, at a 16:9 aspect of the primary monitor
  - a right panel of 320 px holds:
    - a Snow toggle and Density, Speed, Wind and Flake size sliders (0–100, each showing its value)
    - a Motion blur toggle
    - a Scaling selector
    - an **Apply to desktop** button that opens the same dialog as the library
    - a **Back to library** button
- Slider changes go to the preview immediately. They're persisted to the library item after 500 ms of quiet, and sent to the engine through `UpdateCoalescer` (30/s) when the item is on the desktop.

- [ ] **Step 1: Implement.** Build on the Mac with 0 warnings. **Step 2: Push**; CI green.
- [ ] **Step 3: Commit** — `feat(app): add editor with live snow preview`

### Task 11: Settings and performance pages

**Files:**
- Create: `src/LiveWallpaper.App/Pages/SettingsPage.xaml(.cs)`, `src/LiveWallpaper.App/Pages/PerformancePage.xaml(.cs)`, `src/LiveWallpaper.Windows/StartupRegistration.cs`
- Test: `tests/LiveWallpaper.Windows.Tests/StartupRegistrationTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed class StartupRegistration(string runKeyPath = StartupRegistration.RunKey, string valueName = "LiveWallpaper")
  {
      public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
      public bool IsEnabled();
      public void Enable(string engineExePath);
      public void Disable();
  }
  ```
  - The registration lives under HKCU. The value is the quoted engine path; startup with Windows launches the engine only.
- Settings page:
  - Start with Windows: off by default
  - Frame rate: 30 fps, 60 fps or Adaptive
  - Default scaling
  - Keep the wallpaper running when the app is closed: on

  Each change sends `SetPreferences`. Startup is written to the registry directly.
- Performance page: while visible, it polls `GetStatus` at 1 Hz and stops when hidden. It shows:
  - Current FPS
  - Engine memory (MB)
  - CPU %
  - Active particles
  - per-monitor render resolution, and whether each is shown
- [ ] **Step 1: Write the failing tests**: `Enable_WritesQuotedPath`, `Disable_RemovesValue` and `IsEnabled_ReflectsValue`, all against a unique test key under `HKCU\Software\LiveWallpaper.Tests\<guid>` that is deleted afterwards.
- [ ] **Step 2: Implement.** **Step 3: Push**; CI green. **Step 4: Commit** — `feat(app): add settings and performance pages`

### Task 12: Packaging, docs and pre-release

**Files:**
- Modify: `.github/workflows/ci.yml`. Publish both executables, self-contained `win-x64` and not single-file, into one folder: the engine first, then the app. Upload `LiveWallpaper-win-x64`.
- Modify: `README.md` (it's an app now, with screenshots once the user provides them) and `docs/windows-test-checklist.md` (add a Milestone 2 + 3 section)

- [ ] **Step 1: Update CI**, push, and confirm the artifact contains `LiveWallpaper.exe` and `LiveWallpaper.Engine.exe`, and that both start: CI runs `LiveWallpaper.Engine.exe --list-monitors` as a smoke step.
- [ ] **Step 2: Add the Milestone 2 + 3 checklist section:**
  - import (button, drag-and-drop, bad file)
  - editor preview responsiveness and slider feel
  - Apply to all monitors and to one monitor
  - live slider changes while applied
  - Remove while applied
  - app closed while the wallpaper keeps running
  - second app launch
  - tray menu items
  - Exit leaves no processes
  - start with Windows
  - FPS, memory and CPU from the Performance page after 5 minutes on all three displays
- [ ] **Step 3: Commit, open a PR** `feat/m2-m3-app` → `main` (stacked on PR #1), **then publish a pre-release** `v0.2.0-m3` with the artifact zip, labelled untested, as with `v0.1.0-m1`.

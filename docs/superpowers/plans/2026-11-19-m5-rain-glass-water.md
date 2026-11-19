# Milestone 5: rain, glass and water — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add three effects (falling rain, rain on glass, flowing water), each in its own fixed slot per library item with its own regions, rendered by the engine and the editor preview.

**Architecture:**
- Core holds the settings, slots, validation, and the math: models, frame builders, homography, flow field and editor rules. All of it is tested on the Mac.
- Rendering gets a mask per effect, an `EffectInputs` struct (image, destination, frame copy), and three new effects with HLSL ported from `docs/effects-model.md`. They are tested with WARP on CI.
- The engine and the app wire the slots through new IPC requests and a protocol version. The editor gets one tab per effect.

**Tech Stack:** C# / .NET 10, xUnit, Vortice 3.8.3 (D3D11), HLSL `vs_5_0`/`ps_5_0`, WPF + WPF-UI 4.3.0.

**Spec:** `docs/superpowers/specs/2026-11-19-m5-rain-glass-water-design.md`. The constants come from `docs/effects-model.md`. The precision rules come from `docs/snow-model.md` and spec section 5.7.

## Global Constraints

- No new packages. WPF-UI is the only UI dependency. Pin nothing new.
- No `any`-style escapes: nullable is enabled, there are no `!` suppressions without a reason, and every function declares its return type. Warnings are errors on CI (0 warnings).
- **TDD:** a failing test first for all Core logic. Windows rendering is tested with WARP in `tests/LiveWallpaper.Windows.Tests`. Those tests compile on the Mac and run only on CI.
- **Period P = 3600 s** (`SnowModel.PeriodSeconds`).
  - No float32 shader input may grow with running time. Pass `frac(clock / P)` or wrap on the CPU in `double`.
  - Every per-instance rate is quantized to whole cycles per P.
- **Wrap spans and margins depend only on the target size**, never on a slider.
- **The library file stays at schema version 1.** The new properties are additive and default when missing.
- **Defaults:**
  - Rain: off, density 50, speed 50, wind 20, length 50, brightness 50.
  - Glass: off, drops 50, drop size 50, sliding 40, fog 30.
  - Water: off, stretch 50, foam 30, mist 40 (MistPct, added after the user tried the spike; Task 5 adds it to `WaterSettings`).
  - Snow keeps `Enabled = true`.
- **Draw order:** background → water → rain → snow → glass.
- **Region limits:** 16 regions and 64 points per region, as today. A glass pane has exactly 4 points. A water region has a Flow with finite components and a length (normalized, no aspect correction) of 0.002–1. The editor clamps the aspect-corrected arrow length to 0.02–0.5. The default arrow is (0, 0.12).
- **Commits:** conventional, on `feat/m5-rain-water`, each ending with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Commit only the task's files.
- **Local commands:**
  - First: `export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"`.
  - Build: `dotnet build -c Release` (the whole solution builds on the Mac through EnableWindowsTargeting).
  - Core tests: `dotnet test tests/LiveWallpaper.Core.Tests -c Release`.

## Review Focus

1. **A v0.3.1 `library.json` (snow and regions, no new keys) loads.** Snow keeps its regions, and rain, glass and water are off with no regions. Pinned in Task 1.
2. **Dragging a corner, moving a region or undoing keeps the water region's arrow.** `RegionGeometry` functions must not drop `Flow`. Pinned in Task 1, with an editor test in Task 6.
3. **A pane dragged into a degenerate or bow-tie shape** draws no glass for that pane and produces no NaN or crash. The other panes still draw. Pinned in Task 4.
4. **A water arrow dragged onto its own centre** (zero length) is clamped to the minimum length. There is no divide by zero, and flow never goes to NaN. Pinned in Task 5, plus the Task 6 editor clamp.
5. **A wallpaper left running for 30 days:**
  - every value the rain, glass and water builders pass to a shader stays bounded;
  - a slider change never moves the rain wrap span.

  Pinned in Tasks 3, 4 and 5.

---

### Task 1: Effect settings, slots, flow on regions, load repair

**Files:**
- Create: `src/LiveWallpaper.Core/Models/EffectKind.cs`, `src/LiveWallpaper.Core/Models/RainSettings.cs`, `src/LiveWallpaper.Core/Models/GlassSettings.cs`, `src/LiveWallpaper.Core/Models/WaterSettings.cs`, `src/LiveWallpaper.Core/Models/EffectSlots.cs`, `src/LiveWallpaper.Core/Models/EffectRules.cs`
- Modify: `src/LiveWallpaper.Core/Models/LibraryItem.cs`, `src/LiveWallpaper.Core/Regions/RegionModel.cs`, `src/LiveWallpaper.Core/Regions/RegionGeometry.cs`, `src/LiveWallpaper.Core/Library/LibraryStore.cs`
- Test: `tests/LiveWallpaper.Core.Tests/EffectSettingsTests.cs`, `tests/LiveWallpaper.Core.Tests/LibraryStoreTests.cs`, `tests/LiveWallpaper.Core.Tests/Regions/RegionGeometryTests.cs`

**Interfaces (produces):**
- `public enum EffectKind { Snow, Rain, Glass, Water }`. It is JSON camelCase strings ("snow", "rain", "glass", "water"), using the `WallpaperScaling` converter pattern (`JsonStringEnumMemberName`, `allowIntegerValues: false`).
- The settings records, each with `public bool IsValid` (all percentages finite and within 0–100):
  - `record RainSettings(bool Enabled = false, double DensityPct = 50, double SpeedPct = 50, double WindPct = 20, double LengthPct = 50, double BrightnessPct = 50)`;
  - `record GlassSettings(bool Enabled = false, double DropsPct = 50, double DropSizePct = 50, double SlidingPct = 40, double FogPct = 30)`;
  - `record WaterSettings(bool Enabled = false, double StretchPct = 50, double FoamPct = 30)`.
- The slots, each with `static X Default { get; }` (default settings, `RegionSet.Empty`):
  - `record RainSlot(RainSettings Settings, RegionSet Regions)`;
  - `record GlassSlot(GlassSettings Settings, RegionSet Regions)`;
  - `record WaterSlot(WaterSettings Settings, RegionSet Regions)`.
- `Region` gains `public RegionPoint? Flow { get; init; }`, serialized with `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]`. `Equals` and `GetHashCode` include `Flow`.
- `LibraryItem` gains init properties `Rain`, `Glass` and `Water`, in the same null-coalescing backing-field pattern as `Regions`.
- `public static class EffectRules`:
  - `public static readonly RegionPoint DefaultFlow = new(0, 0.12);`
  - `public static RegionSet Repair(EffectKind kind, RegionSet set)`: for Glass and Water, forces `Invert = false`. For Glass, drops regions without exactly 4 points. For Water, gives regions without a valid Flow `DefaultFlow`. Snow and Rain are returned unchanged.
  - `public static bool IsValid(EffectKind kind, RegionSet set)`: `set.IsValid`, plus for Glass every region has 4 points and `!Invert`, and for Water every region has a valid flow and `!Invert`.
  - `public static bool IsValidFlow(RegionPoint? flow)`.

- [ ] **Step 1: Write the failing tests**
  - `EffectSettingsTests`:
    - `Defaults_MatchTheSpec`: asserts every default listed in Global Constraints.
    - `IsValid_RejectsOutOfRangeAndNaN`: a theory over each record with `-1`, `101` and `double.NaN`.
    - `Repair_Glass_DropsNonQuadsAndClearsInvert`.
    - `Repair_Water_GivesMissingFlowTheDefaultArrow`.
    - `Repair_Snow_ReturnsTheSameInstance`.
    - `IsValid_Water_RejectsZeroAndInfiniteFlow`: `(0,0)`, `(∞,0)` and length 1.5 are rejected; `(0,0.12)` is accepted.
  - `LibraryStoreTests`:
    - `Load_LibraryFromM4WithoutEffects_KeepsSnowRegionsAndTurnsNewEffectsOff`: a literal v0.3 JSON item with `snow` and `regions` only.
    - `RoundTrip_AllSlots`.
    - `Load_WaterRegionWithoutFlow_GetsDefaultArrow`.
    - `Load_GlassRegionWithFivePoints_IsDropped`.
    - `Load_NaNRainDensity_IsTreatedAsCorrupt`.
  - `RegionGeometryTests`: `MoveVertex_KeepsFlow`, `Translate_KeepsFlow`, `InsertVertex_KeepsFlow`, `RemoveVertex_KeepsFlow`.
- [ ] **Step 2:** Run `dotnet test tests/LiveWallpaper.Core.Tests -c Release`. Expected: it doesn't compile, or the new tests FAIL.
- [ ] **Step 3: Implement.**
  - `RegionGeometry` returns `region with { Points = … }` instead of `new Region(…)`, everywhere except `CreateQuad`.
  - `LibraryStore` runs `EffectRules.Repair` on each slot's regions right after deserializing, before `Validate`.
  - `Validate` adds the new settings' `IsValid` and `EffectRules.IsValid` for each slot.
- [ ] **Step 4:** Run the Core tests. Expected: all PASS (190+ existing tests plus the new ones).
- [ ] **Step 5:** Commit `feat(core): rain, glass and water slots per library item`.

### Task 2: IPC for the new effects and a protocol version

**Files:**
- Modify: `src/LiveWallpaper.Core/Ipc/EngineMessages.cs`
- Create: `src/LiveWallpaper.Core/Ipc/EngineProtocol.cs`
- Test: `tests/LiveWallpaper.Core.Tests/Ipc/IpcTests.cs`

**Interfaces (produces):**
- New requests: `record UpdateRain(Guid LibraryItemId, RainSettings Rain)` ("rain"), `record UpdateGlass(Guid LibraryItemId, GlassSettings Glass)` ("glass") and `record UpdateWater(Guid LibraryItemId, WaterSettings Water)` ("water").
- `record UpdateRegions(Guid LibraryItemId, EffectKind Effect, RegionSet Regions)`.
- `EngineStatus` gains a trailing `int ProtocolVersion = 0` constructor parameter.
- `public static class EngineProtocol`:
  - `public const int Current = 1;`
  - `public static bool NeedsReplacement(EngineStatus status) => status.ProtocolVersion < Current;`

- [ ] **Step 1: Write the failing tests**
  - `RoundTrip_UpdateRain/Glass/Water`.
  - `RoundTrip_UpdateRegions_CarriesEffect`: asserts the JSON contains `"effect":"water"`.
  - `Status_WithoutProtocolVersion_ReadsAsZero`: a literal v0.3 status JSON.
  - `NeedsReplacement_TrueForZero_FalseForCurrent`.
- [ ] **Step 2:** Run the Core tests. Expected: FAIL.
- [ ] **Step 3:** Implement the records. Register any types the source generator needs in `IpcJsonContext`.
- [ ] **Step 4:** Run the Core tests. Expected: PASS. Then build the whole solution. Expected: compile errors only in `EngineHost` and `EditorView` callers of `UpdateRegions`. Fix those callers minimally with `EffectKind.Snow`, so `main` behaviour is unchanged until Tasks 12–13.
- [ ] **Step 5:** Commit `feat(core): IPC for rain, glass and water, with a protocol version`.

### Task 3: Rain model and frame builder

**Files:**
- Create: `src/LiveWallpaper.Core/Rain/RainModel.cs`, `src/LiveWallpaper.Core/Rain/RainFrameBuilder.cs`
- Modify: `src/LiveWallpaper.Core/Snow/WindModel.cs`: add a constructor `WindModel(uint seed, double basePxPerSecAtFull)`. The existing `WindModel(uint seed)` keeps 120.
- Test: `tests/LiveWallpaper.Core.Tests/Rain/RainModelTests.cs`, `tests/LiveWallpaper.Core.Tests/Rain/RainFrameBuilderTests.cs`

**Interfaces (produces):**
- `record RainLayer(string Name, float Share, float WidthMin, float WidthMax, float FallMin, float FallMax, float OpacityMin, float OpacityMax, float WindResponse)`.
- `static class RainModel`:
  - `Layers` (the table in `docs/effects-model.md`);
  - `const int MaxStreaks = 9000`;
  - `const double WindBaseAtFull = 520`;
  - `int StreakCount(double densityPct, double megapixels)`;
  - `double SpeedMultiplier(double speedPct)`;
  - `double ExposureSeconds(double lengthPct)`;
  - `double OpacityMultiplier(double brightnessPct)`;
  - `double WrapMarginPx(double scale)`.
- `[StructLayout(Sequential)] struct RainFrameConstants`, a multiple of 16 bytes: `Vector4 WindOffsets; Vector2 Resolution; float FallPhase; float WindPxPerSec; float SpeedMul; float Exposure; float Margin; float Scale; float Opacity; float Period; float StreakCount; float Padding;`.
- `static class RainFrameBuilder`: `void Build(double fallTime, double windOffsetPx, double windPxPerSec, RainSettings settings, Size target, ref RainFrameConstants result)`.

- [ ] **Step 1: Write the failing tests**
  - `StreakCount_FollowsTheCurveAndCap`: `(50, 2.0736)` gives `round(1400 × 0.5^1.4 × 2.0736)`, and `(100, 50)` gives 9000.
  - `Multipliers_MatchTheModel`: speed 0 → 0.5 and 100 → 1.5; exposure 0 → 0.008 and 100 → 0.05; opacity 0 → 0.4 and 100 → 2.0.
  - `WrapMargin_IsIndependentOfSettings`: building with length 0 and with length 100 gives the same `Margin`.
  - `Build_After30Days_KeepsShaderValuesBounded`: fallTime = 30 × 86400 × 1.5 and windOffset = 1e9. Then FallPhase is in [0,1), each WindOffsets component is in [0, spanX), and everything is finite.
  - `Build_WindOffsetsUseLayerResponse`.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement, following `SnowFrameBuilder`'s `Fract`/`PositiveMod` pattern.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat(core): rain model and frame constants`.

### Task 4: Homography and glass frame builder

**Files:**
- Create: `src/LiveWallpaper.Core/Geometry/Homography.cs`, `src/LiveWallpaper.Core/Glass/GlassModel.cs`, `src/LiveWallpaper.Core/Glass/GlassFrameBuilder.cs`
- Test: `tests/LiveWallpaper.Core.Tests/HomographyTests.cs`, `tests/LiveWallpaper.Core.Tests/Glass/GlassFrameBuilderTests.cs`

**Interfaces (produces):**
- `readonly record struct Homography(double M11 … double M33)`, row-major:
  - `static Homography? SquareToQuad(RegionPoint tl, RegionPoint tr, RegionPoint br, RegionPoint bl)` returns null when the quad is degenerate (|denominator| < 1e-9), non-finite or not convex;
  - `RegionPoint Map(RegionPoint p)`;
  - `Homography? Inverse()`.
- `static class GlassModel`: `const int MaxPanes = 16`; `double SizeMultiplier(double pct) => 0.6 + 0.8 × pct/100`.
- `[StructLayout(Sequential)] struct GlassPane`: `Vector4 InvRow0, InvRow1, InvRow2, FwdRow0, FwdRow1, FwdRow2; Vector4 BoundsPx /* x, y, w, h */; Vector2 PanePx; Vector2 Padding;`.
- `[StructLayout(Sequential)] struct GlassFrameConstants`: `Vector2 Resolution; float TimePhase; float Scale; float Drops; float SizeMul; float Sliding; float Fog; float PaneCount; Vector3 Padding;`.
- `static class GlassFrameBuilder`: `int BuildPanes(RegionSet panes, RectangleF imageBounds, Size target, Span<GlassPane> result)`. It maps each 4-point region to target pixels through `imageBounds` and skips degenerate ones. With zero regions it writes one pane covering the target. It returns the count. The second method is `void BuildFrame(double timeSec, GlassSettings settings, Size target, int paneCount, ref GlassFrameConstants result)`. `PanePx` is the average length of opposite edges. `BoundsPx` is the quad's bounding box, clamped to the target.

- [ ] **Step 1: Write the failing tests**
  - `SquareToQuad_MapsCornersExactly`: within 1e-9.
  - `Inverse_RoundTrips`: random interior points.
  - `SquareToQuad_Collinear_IsNull`.
  - `SquareToQuad_BowTie_IsNull`.
  - `BuildPanes_NoRegions_CoversTheTarget`.
  - `BuildPanes_SkipsDegeneratePane_KeepsOthers`.
  - `BuildPanes_PanePxIsAverageEdgeLength`.
  - `BuildFrame_After30Days_TimePhaseInUnitRange`.
  - `BuildFrame_MapsSettings`: drops 50 → 0.5, size 100 → 1.4, fog 30 → 0.3.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement Heckbert's square-to-quad, with the formulas in the spike (`docs/effects-model.md`, Glass). The inverse is the adjugate divided by the determinant, and returns null when |det| < 1e-12.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat(core): homography and glass pane constants`.

### Task 5: Water model, flow field and frame builder

**Files:**
- Create: `src/LiveWallpaper.Core/Water/WaterModel.cs`, `src/LiveWallpaper.Core/Water/FlowField.cs`, `src/LiveWallpaper.Core/Water/WaterPhaseClock.cs`, `src/LiveWallpaper.Core/Water/WaterFrameBuilder.cs`
- Test: `tests/LiveWallpaper.Core.Tests/Water/WaterModelTests.cs`, `tests/LiveWallpaper.Core.Tests/Water/FlowFieldTests.cs`

**Interfaces (produces):**
- `static class WaterModel`:
  - `const double ArrowSeconds = 4`, `const double MinArrow = 0.02`, `const double MaxArrow = 0.5`;
  - `double CycleSeconds(double stretchPct) => 0.5 + 2.5 × pct/100`;
  - `RegionPoint ClampArrow(RegionPoint flow, double imageAspect)`: the aspect-corrected length is clamped to [MinArrow, MaxArrow], and a zero vector becomes `(0, MinArrow)`;
  - `(double X, double Y) SpeedPxPerSec(RegionPoint flow, SizeF displayedImagePx)`: the direction comes from `(flow.X × w, flow.Y × h)`, and the magnitude is `|(flow.X × w/h, flow.Y)| / ArrowSeconds × h`. A zero vector returns (0, 0).
- `record FlowImage(int Width, int Height, float[] Pixels)`, RG interleaved, px/s.
- `static class FlowField`:
  - `const int Downscale = 4`;
  - `FlowImage Build(RegionSet water, RectangleF imageBounds, Size target)`: size `ceil(target / 4)`. Each pixel centre takes the speed of the last region containing it (`RegionGeometry.ContainsPoint`, tested only within each region's bounding box); everything else is (0,0).
- `sealed class WaterPhaseClock`: `double Step(double dtSec, double stretchPct)` returns the phase in [0, 1), integrated as `phase = frac(phase + dt / Cycle)`.
- `[StructLayout(Sequential)] struct WaterFrameConstants`: `Vector4 ImageDest; Vector2 Resolution; float Phase; float Cycle; float Foam; float Scale; Vector2 Padding;`.
- `static class WaterFrameBuilder`: `void Build(double phase, WaterSettings settings, RectangleF imageDestination, Size target, ref WaterFrameConstants result)`.

- **Mist amendment (user request):**
  - Add `double MistPct = 40` as the last parameter of `WaterSettings`, included in `IsValid`. A file without it loads as 40.
  - Add `WaterModel.MistReachPx(double mistPct, double displayedImageHeightPx) => (0.04 + 0.10 × pct/100) × h` and `WaterModel.MaxMistReachFraction = 0.14`.
  - `FlowField.Build` gains a `double dilatePx` parameter. Pixels outside every region but within `dilatePx` (in target pixels; work at the 1/4 grid) of a region take the flow of the nearest region (two-pass chamfer or BFS distance transform), so the mist plume knows its direction. Callers pass `MaxMistReachFraction × imageBounds.Height`.
  - `WaterFrameConstants` gains `float Mist; float MistReach;`, keeping 16-byte alignment.
  - Tests: `WaterSettings_MissingMist_LoadsAs40`, `FlowField_DilatesNearestRegionFlow_WithinReach`, `FlowField_BeyondReach_IsZero`, `MistReach_MatchesModel`.
- [ ] **Step 1: Write the failing tests**
  - `SpeedPxPerSec_DefaultArrowOnSquareImage`: flow (0, 0.12) and 1000×1000 px give (0, 30).
  - `SpeedPxPerSec_SameScreenLengthSameSpeed`: on a 2:1 image, a horizontal arrow (0.06, 0) and a vertical arrow (0, 0.12) give equal speeds.
  - `ClampArrow_ZeroBecomesMinimumDown`.
  - `ClampArrow_TooLongIsShortened`.
  - `PhaseClock_StaysInUnitRange_After30Days`: `Step(1/60)` repeated, or one large dt.
  - `PhaseClock_StretchChangeDoesNotJump`: the phase after a slider change differs from the previous by `dt / C` only.
  - `FlowField_PaintsEachRegionsSpeed`.
  - `FlowField_LastRegionWins`.
  - `FlowField_ZeroOutside`.
  - `FlowField_EmptySet_AllZero`.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat(core): water flow model and flow field`.

### Task 6: Region editor modes: fixed panes and flow arrows

**Files:**
- Modify: `src/LiveWallpaper.Core/Regions/RegionEditor.cs`
- Test: `tests/LiveWallpaper.Core.Tests/Regions/RegionEditorTests.cs`

**Interfaces (produces):**
- `public enum RegionEditMode { Free, Plane, Flow }`.
- The constructor becomes `RegionEditor(RegionSet initial, RegionEditMode mode = RegionEditMode.Free, double imageAspect = 1)`.
- `OverlayShapeKind` gains `Arrow` (A = tail, B = head) and `ArrowHandle` (A == B).
- **Plane:** no edge ghost, no `InsertVertex`; `RemoveVertexAt` and `DeleteSelectedVertex` do nothing (the latter returns false); `SetInvert` is ignored.
- **Flow:**
  - `AddRegion` sets `Flow = EffectRules.DefaultFlow`.
  - The overlay adds an `Arrow` from each region's centroid to centroid + Flow, plus an `ArrowHandle` at the head of the selected region.
  - `PointerDown` on the handle of the selected region (within `HandleHitDip`) starts a `Drag.Arrow`. Its moves set `Flow = WaterModel.ClampArrow(imagePoint − centroid, imageAspect)`, and it is committed on `PointerUp` as one undo step.
  - `SetInvert` is ignored.
- `public RegionEditMode Mode { get; }`.

- [ ] **Step 1: Write the failing tests**
  - `Plane_EdgeClick_DoesNotAddCorner`.
  - `Plane_RightClick_DoesNotRemoveCorner`.
  - `Plane_SetInvert_IsIgnored`.
  - `Flow_AddRegion_HasDefaultArrow`.
  - `Flow_DragArrowHead_SetsFlow`.
  - `Flow_DragArrowOntoCentre_ClampsToMinimum`.
  - `Flow_ArrowDrag_IsOneUndoStep`.
  - `Flow_MovingRegion_KeepsArrow`.
  - `Flow_Overlay_ShowsArrowForEveryRegion_HandleOnlyForSelected`.
  - `Free_Overlay_HasNoArrows`.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement. The handle hit is tested before vertex handles, so a head sitting on a corner stays draggable.
- [ ] **Step 4:** Run the whole Core suite. Expected: PASS.
- [ ] **Step 5:** Commit `feat(core): region editor panes and flow arrows`.

### Task 7: Scene with a mask per effect, effect inputs and a frame copy

**Files:**
- Modify: `src/LiveWallpaper.Rendering/IEffect.cs`, `src/LiveWallpaper.Rendering/WallpaperScene.cs`, `src/LiveWallpaper.Rendering/SnowEffect.cs`
- Create: `src/LiveWallpaper.Rendering/FrameCopy.cs`
- Test: `tests/LiveWallpaper.Windows.Tests/MaskedSnowTests.cs` (adapt), `tests/LiveWallpaper.Windows.Tests/SceneTests.cs` (new)

**Interfaces (produces):**
- `public readonly record struct EffectInputs(ID3D11ShaderResourceView Mask, ID3D11ShaderResourceView Image, RectangleF ImageDestination, ID3D11ShaderResourceView? Frame);`
- `IEffect`:
  - `Id` is replaced by `EffectKind Kind { get; }`;
  - adds `bool NeedsFrameCopy { get; }`;
  - adds `void SetGeometry(RegionSet regions, RectangleF imageBounds, Size targetPixels)`, called by the scene whenever that effect's regions or the target change (snow ignores it);
  - `Render(ID3D11RenderTargetView target, Size targetPixels, in EffectInputs inputs)`.
- `WallpaperScene`:
  - `SetRegions(EffectKind kind, RegionSet regions)` replaces `SetRegions(RegionSet)`;
  - one `RegionMaskTexture` per kind that has an effect;
  - `Render` draws the effects sorted by `DrawOrder(kind)`: Water 0, Rain 1, Snow 2, Glass 3. Before the first effect with `NeedsFrameCopy`, it calls `FrameCopy.Capture`.
- `sealed class FrameCopy : IDisposable`: `ID3D11ShaderResourceView Capture(ID3D11RenderTargetView target, Size size)`. It uses a lazily created B8G8R8A8 texture with a full mip chain (`BindFlags.RenderTarget | ShaderResource`, `ResourceOptionFlags.GenerateMips`), recreated on a size change, then `CopySubresourceRegion` into mip 0 and `GenerateMips`. `Release()` frees it.

- [ ] **Step 1: Write the failing tests.** These compile on the Mac and run on CI.
  - `MaskedSnowTests`: update its calls to `SetRegions(EffectKind.Snow, …)`; the existing assertions are unchanged.
  - `SceneTests.NoEffects_FrameEqualsBackground`.
  - `SceneTests.RegionsForAnotherEffect_DoNotMaskSnow`: rain regions are set, and snow still covers both halves.
  - `SceneTests.FrameCopy_MatchesTarget`: captures a solid frame and reads mip 0 back equal.
- [ ] **Step 2:** Build. Expected: compile errors at the callers (`EngineHost`, `PreviewRenderer`). Fix them minimally (`EffectKind.Snow`).
- [ ] **Step 3:** Implement. `SnowEffect.Kind = EffectKind.Snow`, `NeedsFrameCopy = false`, and the mask comes from `inputs.Mask`.
- [ ] **Step 4:** `dotnet build -c Release`. Expected: 0 warnings. The Core tests pass. The Windows tests run on CI in Task 14.
- [ ] **Step 5:** Commit `refactor(rendering): a region mask per effect and effect inputs`.

### Task 8: Rain effect

**Files:**
- Create: `src/LiveWallpaper.Rendering/RainEffect.cs`, `src/LiveWallpaper.Rendering/Shaders/Rain.hlsl` (embedded like `Snow.hlsl`)
- Test: `tests/LiveWallpaper.Windows.Tests/RainEffectTests.cs`, `tests/LiveWallpaper.Windows.Tests/ShaderCompilerTests.cs`

**Interfaces:**
- Consumes `RainModel` and `RainFrameBuilder` (Task 3), `WindModel(seed, RainModel.WindBaseAtFull)`, `FallClock` (with `RainModel.SpeedMultiplier`; add a `Step(double dtSec, double multiplier)` overload if needed) and `IEffect` (Task 7).
- Produces `sealed class RainEffect(GpuDevice gpu, uint seed) : IEffect` with `void Apply(RainSettings settings)`. `Kind = Rain`. `IsAnimated = Enabled && DensityPct > 0`. `ActiveParticles` is the streak count.

- [ ] **Step 1: Write the failing tests**
  - `Rain_Compiles`: `vs_5_0` and `ps_5_0`.
  - `Rain_BrightensOnlyInsideMask`: a left-half region, density 100; right-half pixels equal the background.
  - `Rain_Disabled_LeavesBackground`.
  - `Rain_FramesDiffer`: t = 1 s versus t = 1.1 s.
- [ ] **Step 2:** Build. Expected: compile errors.
- [ ] **Step 3: Implement.**
  - Port `RAIN_VS`/`RAIN_FS` from `docs/effects-model.md` (Rain) to HLSL. Use the same `Hash`/`Rnd` as `Snow.hlsl`; layer table cbuffer b1; frame cbuffer b0; mask at t1 sampled at `position.xy / resolution`; `TriangleStrip`, 4 vertices, instanced; premultiplied alpha blend (`BlendDescription.AlphaBlend` as snow does, since the outputs are premultiplied).
  - The velocity uses the *quantized* fall speed `k × span.y / P`.
- [ ] **Step 4:** Build. Expected: 0 warnings.
- [ ] **Step 5:** Commit `feat(rendering): rain streaks`.

### Task 9: Water effect

**Files:**
- Create: `src/LiveWallpaper.Rendering/WaterEffect.cs`, `src/LiveWallpaper.Rendering/FlowTexture.cs`, `src/LiveWallpaper.Rendering/Shaders/Water.hlsl`
- Test: `tests/LiveWallpaper.Windows.Tests/WaterEffectTests.cs`, `ShaderCompilerTests.cs`

**Interfaces:**
- Consumes `WaterModel`, `FlowField`, `WaterPhaseClock` and `WaterFrameBuilder` (Task 5), plus `EffectInputs.Image` and `ImageDestination`.
- Produces `sealed class WaterEffect(GpuDevice gpu) : IEffect`, with `void Apply(WaterSettings settings)`:
  - `SetGeometry` rebuilds the `R32G32_Float` flow texture (`FlowTexture.Update(FlowImage)`) and records whether any region exists;
  - `IsAnimated = Enabled && hasRegions`;
  - `Render` does nothing without regions;
  - it draws a full-target triangle (`Draw(3)`, `SV_VertexID` trick) with premultiplied alpha blend.

- [ ] **Step 1: Write the failing tests**
  - Use a striped test image (vertical bands of two colours, horizontal stripes so downward flow changes pixels) and one water region over the middle third, flowing down.
  - `Water_Compiles`.
  - `Water_OutsideRegion_EqualsBackground`: an exact byte match outside, using feather 0.
  - `Water_InsideRegion_ChangesOverTime`: frames at t = 0.5 s and t = 1.3 s differ inside.
  - `Water_NoRegions_DrawsNothing`.
- [ ] **Step 2:** Build. Expected: compile errors.
- [ ] **Step 3: Implement**, porting `WATER_FS` from `docs/effects-model.md` (Water).
  - The flow texture is sampled at `px / resolution` (it covers the target).
  - The flow passed in is in px/s. Multiply it by `Cycle` in the shader.
  - The mask is sampled at the sample points `a` and `b`, as well as at `px`.
  - Mist: port the Mist block from `docs/effects-model.md` (Water → Mist), with premultiplied output and a discard when there is neither water nor mist. Add the test `Water_Mist_BrightensBelowTheRegion_OnlyWhenMistOn`: with flow down and mist 100, pixels just below the region are brighter than the background; with mist 0 they are equal.
- [ ] **Step 4:** Build. Expected: 0 warnings.
- [ ] **Step 5:** Commit `feat(rendering): flowing water`.

### Task 10: Glass effect

**Files:**
- Create: `src/LiveWallpaper.Rendering/GlassEffect.cs`, `src/LiveWallpaper.Rendering/Shaders/Glass.hlsl`
- Test: `tests/LiveWallpaper.Windows.Tests/GlassEffectTests.cs`, `ShaderCompilerTests.cs`

**Interfaces:**
- Consumes `GlassFrameBuilder` (Task 4), `EffectInputs.Frame` (Task 7), and `GlassPane` (passed through a structured buffer or a cbuffer array of 16).
- Produces `sealed class GlassEffect(GpuDevice gpu) : IEffect`, with `void Apply(GlassSettings settings)`:
  - `NeedsFrameCopy = Enabled`;
  - `SetGeometry` rebuilds the panes;
  - `Render` draws `paneCount` instanced quads, each covering its `BoundsPx`, with an opaque blend, writing `lerp(frame mip0, glass colour, mask)`;
  - `ActiveParticles` is 0. The drops are per-pixel, not instances, so no count is reported.

- [ ] **Step 1: Write the failing tests**
  - `Glass_Compiles`.
  - `Glass_OutsidePane_EqualsFrame`: one pane on the left half, feather 0; right-half bytes are equal.
  - `Glass_InsidePane_ChangesPixels`: fog 100 over a high-contrast checker changes more than 50% of the pane's pixels.
  - `Glass_NoPanes_CoversEverything`.
  - `Glass_DegeneratePane_IsSkipped_NoNaN`: no pixel is NaN-black where the frame isn't black.
- [ ] **Step 2:** Build. Expected: compile errors.
- [ ] **Step 3: Implement**, porting `GLASS_FS` (beads, sliding drops, lens, fog) from `docs/effects-model.md` (Glass).
  - The frame copy is sampled with `SampleLevel` (explicit mip).
  - Pane `i` comes from `SV_InstanceID`, passed to the pixel shader with `nointerpolation`.
  - Loops are bounded (`[unroll]` on the ±1 column loop).
- [ ] **Step 4:** Build. Expected: 0 warnings.
- [ ] **Step 5:** Commit `feat(rendering): rain on glass`.

### Task 11: Arrows in the region overlay

**Files:**
- Modify: `src/LiveWallpaper.Rendering/RegionOverlayRenderer.cs`, `src/LiveWallpaper.Rendering/Shaders/RegionOverlay.hlsl`
- Test: `tests/LiveWallpaper.Windows.Tests/RegionOverlayRendererTests.cs`

**Interfaces:** consumes `OverlayShapeKind.Arrow` and `ArrowHandle` (Task 6).
- An `Arrow` is a shaft: the SDF of the segment, 2.5 DIP wide, in light blue (#7DD3FC), with a dark outline like the edges. It has a 10 DIP triangular head at B.
- An `ArrowHandle` is a handle circle in the same blue.

- [ ] **Step 1:** Write the failing test `Arrow_DrawsBlueAlongTheShaft`: a pixel at the shaft's midpoint is blue-dominant, and a pixel 20 DIP away is unchanged.
- [ ] **Step 2:** Build.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Build. Expected: 0 warnings.
- [ ] **Step 5:** Commit `feat(rendering): draw flow arrows`.

### Task 12: Engine wiring and replacing an older engine

**Files:**
- Modify: `src/LiveWallpaper.Engine/EngineHost.cs`, `src/LiveWallpaper.App/Services/EngineConnection.cs`

**Interfaces:**
- Consumes everything above. `ActiveWallpaper` holds `SnowEffect Snow`, `RainEffect Rain`, `GlassEffect Glass` and `WaterEffect Water`.
- `AttachAndShow` adds all four (rain is seeded with `Seed(monitor.Id) ^ 0x9e3779b9`), applies each slot's settings, and sets each kind's regions.
- `Handle`:
  - `UpdateRain`, `UpdateGlass` and `UpdateWater` validate with `IsValid` and answer `Failed("… settings must be between 0 and 100%.")` when invalid;
  - `UpdateRegions` validates with `EffectRules.IsValid(update.Effect, update.Regions)` and calls `Scene.SetRegions(update.Effect, …)`.
- `BuildStatus` passes `ProtocolVersion: EngineProtocol.Current`.
- `EngineConnection.EnsureRunningAsync`:
  - if the ping returns a status with `EngineProtocol.NeedsReplacement`, log `event=engine-outdated action=replace`;
  - send `Shutdown`, then poll the ping every `StartPollMs` for up to 3 s until it stops answering;
  - then continue down the existing start path, with `otherEngineRunning` evaluated afterwards.
  - `PingAsync` becomes `Task<EngineStatus?> TryGetStatusAsync` with the ping timeout.

- [ ] **Step 1:** There's no unit test for the WPF and engine host; the logic under it is tested in Tasks 1–2. Build. Expected: 0 warnings.
- [ ] **Step 2:** Self-review against the edge case: the old engine ignores `Shutdown` (a stuck engine). The loop ends after 3 s, and the takeover budget path (`OlderEngineInTheWay`) still applies.
- [ ] **Step 3:** Commit `feat(engine): render rain, glass and water and replace an outdated engine`.

### Task 13: Editor tabs, preview wiring, library badges

**Files:**
- Modify: `src/LiveWallpaper.App/Preview/PreviewRenderer.cs`, `src/LiveWallpaper.App/Preview/PreviewHost.cs`, `src/LiveWallpaper.App/Views/EditorView.xaml`, `src/LiveWallpaper.App/Views/EditorView.xaml.cs`, `src/LiveWallpaper.App/Views/LibraryCard.cs`, `src/LiveWallpaper.App/Views/LibraryView.xaml`

**Interfaces:**
- `PreviewHost` and `PreviewRenderer` gain `SetRain(RainSettings)`, `SetGlass(GlassSettings)`, `SetWater(WaterSettings)` and `SetRegions(EffectKind, RegionSet)`. `BuildScene` adds all four effects.
- **The editor:**
  - a four-pill tab strip at the top of the right column (RadioButtons styled as pills: icons WeatherSnow24, WeatherRain24, Window24, Drop24, each with a label and a small accent dot when that effect is on);
  - one settings panel per tab (only the active one is visible);
  - one shared Regions panel bound to `RegionEditor`s held in a `Dictionary<EffectKind, RegionEditor>` with modes Free, Free, Plane, Flow (`imageAspect = PixelWidth / PixelHeight`);
  - an `UpdateCoalescer` per message type.
- **Copy per tab:**

| Tab | Invert label | Add button | Help |
|---|---|---|---|
| Snow | "Snow everywhere except the regions" | "Add region" | today's text |
| Rain | "Rain everywhere except the regions" | "Add region" | "Without regions, rain covers the whole picture." |
| Glass | (hidden) | "Add pane" | "Without panes, the whole picture is the glass. Add a pane and drag its corners onto a window." |
| Water | (hidden) | "Add water region" | "Add a region on a waterfall or river, then drag the arrow the way the water flows. A longer arrow flows faster." |

- **Corner help:** the "Click an edge to add a corner…" line is hidden on the Glass tab.
- **Sliders:**
  - Rain: Density, Speed, Wind, Streak length, Brightness.
  - Glass: Drops, Drop size, Sliding, Fog.
  - Water: Stretch, Foam, Mist.
- **Behaviour:**
  - A tab switch cancels any drag (`SetEnabled(false)` then `true` on the old editor) and redraws the overlay from the new editor.
  - Ctrl+Z and Ctrl+Y act on the active editor.
  - Save writes all four slots.
- **Library cards:** `RainBadge`, `GlassBadge` and `WaterBadge`, matching `SnowBadge`. Water shows only when it is enabled and has regions.

- [ ] **Step 1:** Build. Expected: 0 warnings. The app can't run on the Mac, so the Windows checklist (Task 14) covers runtime behaviour.
- [ ] **Step 2:** Self-review: every slider changes the preview; every control has a handler; no control is shown without effect (no phantom controls); null and empty checks on `LastStatus`.
- [ ] **Step 3:** Commit `feat(app): editor tabs for snow, rain, glass and water`.

### Task 14: Docs, CI, PR and pre-release

**Files:**
- Modify: `docs/windows-test-checklist.md` (new section "Milestone 5: rain, glass and water"), `docs/regions.md` (per-effect regions, panes, arrows; remove the "Not built yet" homography note), `docs/superpowers/specs/2026-11-19-live-wallpaper-design.md` (5.6 effect slots, 9 rain done), `README.md` (feature list)

- [ ] **Step 1: Write the checklist section.** It covers:
  - starting with v0.3.1 still running (the outdated engine is replaced);
  - the library from v0.3.1 loads, and snow regions are kept;
  - each tab's controls change the preview and the desktop live;
  - rain slants with wind;
  - glass on a window at an angle shows perspective drops; fog 0 versus 100; drops slide and leave trails;
  - water on a waterfall flows with no visible reset and no rock dragged in; arrow drag, length and speed;
  - all four effects at once;
  - Task Manager GPU and CPU with all four on versus snow only, on the laptop and on an external 4K/1440p monitor;
  - left running overnight: motion is still smooth.
- [ ] **Step 2:** Push the branch and open the PR → `feat/m4-regions`. Watch CI on `windows-latest`. Expected: build with 0 warnings and all tests passing. Fix failures with root-cause diagnosis (superpowers:systematic-debugging).
- [ ] **Step 3:** Publish the untested pre-release `v0.4.0-m5` in the `v0.3.0-m4` format. Include:
  - the zip `LiveWallpaper-win-x64-v0.4.0-m5.zip` from the green CI artifact;
  - the SHA-256;
  - the CI run, the commit and the test count;
  - the "untested" warning;
  - a link to the checklist section.
- [ ] **Step 4:** Commit the docs (`docs: Milestone 5 checklist and region docs`) before step 2's final push.

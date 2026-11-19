# Milestone 5: rain, rain on glass and flowing water — design

Date: 2026-11-19
Status: approved 2026-11-19. The user approved each part in conversation, then said to build straight after writing the design: "I trust your sense of product direction and overall artistic ability." So the builder tunes the look, not the user.

## 1. Intent

The user asked: "we should go ahead and add the rain and also a cool effect to place on waterfalls to make it look like its flowing."

Live Wallpaper turns a still photo into a living desktop. Until now it has had one effect, snow, confined to regions. This milestone adds three more effects, all real-time on the GPU and all confined to their own regions:

- **Rain:** falling streaks.
- **Glass:** rain on a window: drops that sit on the glass, slide down it and bend the scene behind them. The original design (section 9) calls this "rain on glass" and reserved 4-point plane regions for it.
- **Water:** the photo's own waterfall or river flows. The user places a region on the water and points an arrow along the flow.

Success: a photo of a waterfall that visibly flows on the desktop, and a photo of a window that looks rained on. All four effects can run on one image at once, at the same near-zero cost as snow.

## 2. Goals, non-goals, anti-goals

**Goals**
- Four effects per library item, in fixed slots: Snow, Rain, Glass and Water. Each slot has its own settings and its own regions.
- The editor has one tab per effect. All the effects run in the preview at once, and only the open tab's regions are drawn and editable.
- Rain: depth layers, wind slant with gusts, plus density, speed, length and brightness.
- Glass: 4-corner panes in perspective. Beads sit still, and larger drops slide in stop-start runs and leave trails. The drops refract and flip the finished scene behind them. A fog slider sets how misted the glass is, and sliding drops wipe clear paths through the fog.
- Water: regions with one arrow each. The arrow's direction is the flow direction and its length is the speed. Settings are stretch and foam.
- The desktop and the editor preview render the same thing.
- A library saved by v0.3 loads unchanged: its snow settings and regions become the Snow slot, and the other three start switched off.

**Non-goals (this milestone)**
- Splashes on the ground. They look fake without knowing where the ground is.
- Mist or spray at the base of a waterfall. It could be a later effect.
- Direction strokes or curved flow inside a single water region. A bend takes two or three regions.
- Two instances of the same effect on one image (approach B, section 4).
- Low-power mode. It doesn't exist yet (Milestone 5 "Robustness" in the original plan).

**Anti-goals** (failure even if it technically works)
- Rain that reads as identical white lines sliding at one speed.
- Water that visibly pulses, resets or smears into a blur.
- Glass drops that look like flat circles stamped on the picture, or that all move in step.
- Any effect whose motion degrades after the wallpaper has run for days (section 5.7).
- A tab or control that looks functional but does nothing.
- Per-frame allocations, or a noticeable rise in GPU time on the user's laptop when the new effects are off.

## 3. Constraints

- `docs/snow-model.md` exists only on `feat/m0-preview`, so this branch copies it in, since section 5.7 and `docs/effects-model.md` refer to it.
- Everything in the original design's section 3 still applies: development on a Mac, Windows-only behaviour verified by CI and by the user's checklist, the public MIT repo, and no copied GPL or non-commercial shader code. Well-known rain-on-glass shaders on Shadertoy are CC BY-NC-SA, so the drop shader is written from first principles.
- No new dependencies.
- TDD for Core logic. Shaders are covered by WARP pixel tests on `windows-latest`.
- This branch, `feat/m5-rain-water`, is stacked on `feat/m4-regions` (PR #4).

## 4. Approaches

| | Approach | Trade-off |
|---|---|---|
| **A** | **A fixed slot per effect (Snow, Rain, Glass, Water), each holding settings and regions; fixed draw order** | **Chosen by the user.** Matches the four tabs one to one, migrates simply, keeps IPC simple. No duplicate effects. |
| B | An open, ordered list of effect instances with a type discriminator (the shape the original section 5.6 described) | More flexible, but only worth it with add/remove/reorder UI that nobody asked for; without that UI the flexibility is a phantom feature. |

Each slot already has the settings-plus-regions shape a list entry would have, so A can grow into B later without breaking saved files. The original design's section 5.6 is updated to say so.

Other choices made in conversation:
- **Water direction:** one arrow per region (user's pick) rather than painted direction strokes.
- **Rain scope:** streaks plus rain on glass (user's pick), with no splashes.
- **Glass gets its own tab** (builder's proposal, accepted). Falling rain belongs outside a window and drops belong on it, so they need separate regions.
- **Look tuning:** a throwaway WebGL2 page in the session scratchpad lets the builder see each effect on real photos before porting (section 6). It isn't committed. The tuned constants are recorded in `docs/effects-model.md`.

## 5. Design

### 5.1 Data model (Core)

```
LibraryItem
    Snow     : SnowSettings              (existing constructor parameter)
    Regions  : RegionSet                 (existing; Snow's regions; name kept for file compatibility)
    Rain     : RainSlot  { Settings: RainSettings,  Regions: RegionSet }
    Glass    : GlassSlot { Settings: GlassSettings, Regions: RegionSet }   every region has exactly 4 points
    Water    : WaterSlot { Settings: WaterSettings, Regions: RegionSet }   every region has a Flow
```

- The new slots are init properties with non-null defaults, following the `Regions` pattern: a missing property loads as the default.
- `Region` gains an optional `Flow` (a `RegionPoint?`). It is the arrow's vector in normalized image coordinates, from the region's centroid to the arrowhead. It is null for snow, rain and glass regions. Keeping it on the region means the existing undo history, moves and deletes carry the arrow with the region for free.
- **The library file stays schema version 1** (builder's call). The changes are additive, and System.Text.Json skips unknown properties, so v0.3 can still open a library that v0.4 saved; it just ignores the new effects. Bumping the version would make v0.3 treat the file as corrupt and back it up.
- **Validation and repair** (builder's call, after the design review):
  - **On library load, repair what the editor can't produce but an old or hand-edited file might.** For Glass and Water, `Invert` is forced false. A Glass region without exactly 4 points is dropped. A Water region without a valid Flow gets the default arrow.
  - **Anything that is unsafe** (non-finite numbers, points outside the image, limits exceeded, settings out of range) **still rejects the file**, as today.
  - **The engine never repairs.** It rejects an invalid IPC message with `Failed`.
  - The rules: each settings record has `IsValid`, like `SnowSettings`, with percentages finite and within 0–100. A Glass region has exactly 4 points. A Water Flow has finite components and a length of 0.002–1 in normalized coordinates. That is a loose bound with no aspect correction, so it needs no image size; the editor clamps the aspect-corrected length more tightly (below). Flow on a snow, rain or glass region is ignored.

Settings (percentages unless noted). The defaults are starting points, to be tuned in the spike:

| Record | Fields (default) |
|---|---|
| `RainSettings` | Enabled (false), DensityPct (50), SpeedPct (50), WindPct (20), LengthPct (50), BrightnessPct (50) |
| `GlassSettings` | Enabled (false), DropsPct (50), DropSizePct (50), SlidingPct (40), FogPct (30) |
| `WaterSettings` | Enabled (false), StretchPct (50), FoamPct (30) |

Snow's `Enabled` default stays true, so imports keep today's behaviour.

`Invert` stays in the shared `RegionSet` but only Snow and Rain offer it. For Glass and Water it is forced false, both on load and in the editor. "Glass everywhere except the panes" means nothing, and water needs a place to flow.

**Flow units.** A flow vector is stored in normalized image coordinates, so a waterfall's arrow stays on the waterfall under any scaling. The speed is the arrow length measured in image heights, after correcting for aspect ratio (x scaled by width/height). That way a horizontal arrow and a vertical arrow of the same on-screen length flow at the same speed. One image height of arrow means the water crosses one arrow length every `WaterModel.ArrowSeconds` (to be tuned, about 4 s).

### 5.2 IPC

- `UpdateSnow` is unchanged.
- New requests: `UpdateRain(LibraryItemId, RainSettings)`, `UpdateGlass(LibraryItemId, GlassSettings)` and `UpdateWater(LibraryItemId, WaterSettings)`.
- `UpdateRegions` gains `Effect` (`EffectKind`: `Snow | Rain | Glass | Water`, serialized as a camelCase string). It is a required parameter.
- **Protocol version** (found by the design review: a v0.3 engine that still answers would otherwise be kept, and it would apply glass and water regions to snow):
  - `EngineStatus` gains `ProtocolVersion` (an int, default 0 when missing, so a v0.3 engine reads as 0). `EngineProtocol.Current` is 1.
  - When the app's `EnsureRunningAsync` gets a status with an older protocol, it sends `Shutdown`, which every engine since v0.2 understands. It waits for the pipe to close, then starts the bundled engine through the existing start path.
  - If the old engine doesn't exit within the existing takeover budget, the v0.3.1 takeover path handles it.
- The engine validates each request with the same `IsValid` rules as the library load, and answers `Failed` with a plain message.

### 5.3 Rendering pipeline

The draw order is fixed:

1. **Background:** the photo, as today.
2. **Water:** redraws the photo's pixels inside its mask, displaced along the flow.
3. **Rain**, then **Snow:** premultiplied particles, each multiplied by its own mask.
4. **Glass:** samples a copy of the frame so far and draws the drops and fog over it.

Changes:

- **A mask per effect.** `WallpaperScene` owns one `RegionMaskTexture` per effect slot, each rebuilt only when that slot's regions or the target size change. `SetRegions(EffectKind, RegionSet)` replaces `SetRegions(RegionSet)`. The empty-regions rule differs by effect:
  - Snow, Rain and Glass with no regions cover everything (one white pixel);
  - Water with no regions draws nothing.
- **What effects can see.** `IEffect.Render` receives a small `EffectInputs` struct instead of just the mask:
  - the mask;
  - the background image view;
  - the image destination rectangle (where the decoded crop lands, in target pixels);
  - the scene's "frame so far" view, null unless an effect asked for it.

  Each effect declares whether it needs the frame copy (only Glass does, and only while enabled).
- **Frame copy for Glass.**
  - When Glass is enabled, the scene copies the back buffer into a scene-owned texture with a mip chain (`CopySubresourceRegion` into mip 0, then `GenerateMips`) before drawing Glass.
  - Mips give the fog's blur and the drops' slight defocus without a separate blur pass.
  - The texture is created lazily at target size and released when Glass is disabled or the scene is disposed.
  - Cost: one full-frame copy plus the mip generation per frame, only while Glass is on.
  - The layered-window (non-flip) surface path supports copies from its back buffer too; this is covered by the WARP test, which uses an offscreen target.
- **A flow texture for Water.**
  - Core rasterizes the water regions into an RG16F flow field at mask resolution.
  - Each pixel holds the flow vector of the region covering it, already converted to target pixels per cycle; the last region wins where regions overlap.
  - It is rebuilt alongside the water mask. The mask provides the soft edge; the flow field is unblurred.
  - `RegionMask`'s polygon fill is reused, with no new rasterizer.
- `ActiveParticles` counts rain streaks plus glass drops plus snow flakes. Water reports 0.
- `IsAnimated` is true when any enabled effect is animated. Water counts only when it has regions; Glass counts when it is enabled.

### 5.4 Rain

Rain uses the snow machinery, with the same stateless instancing, the same period quantization and the same `WindModel` integration.

- **Three layers (far, mid, near):** these vary width (about 1–3 px at 1080p), length, fall speed (several times snow's), opacity and wind response. Near streaks are longer, softer and fewer.
- **Each streak is a thin quad aligned with its velocity** (fall plus wind). Its length is `velocity × exposure`, with the exposure set by `LengthPct`, so wind visibly slants the rain and gusts sweep it.
- **Look:** alpha fades in along the streak (tapered head and tail) with a soft cross-section, in a cool white. Tuning showed that taking colour from the photo isn't needed: low premultiplied alpha already makes rain faint on a bright sky and visible against dark backgrounds, as in real rain photos. `BrightnessPct` scales the opacity.
- **Settings mapping (functions in `RainModel`, unit-tested):**
  - density sets the streaks per megapixel, capped;
  - speed sets the fall multiplier;
  - wind sets the base wind plus gusts, the same curve as snow, scaled for rain;
  - length sets the exposure.
- **Wrap margin:** it must cover the longest possible streak, computed from the setting maxima, never from the current settings (section 5.7).

### 5.5 Glass

- **Panes.** Each glass region is a 4-corner plane. Core computes the homography from the unit square to the pane's corners in target pixels (`Homography` in Core, unit-tested: corners map exactly, the inverse round-trips, degenerate quads are rejected).
  - The Glass shader draws each pane as its bounding box and maps each pixel back into pane space with the inverse homography. The drops are generated in pane space, so they foreshorten with the window's perspective.
  - With no panes, one pane covers the whole target.
  - At most 16 panes (the `RegionSet` limit). Each pane's inverse homography goes in a constant buffer.
- **Drops, all stateless.** Pane space is divided into a jittered grid at two scales.
  - **Small static beads** fade in and out over long cycles.
  - **Larger drops** stay stuck for a hashed random time, then slide down in stop-start runs. The slide follows a hashed curve built from the bounded time phase, with slight sideways wobble. Each sliding drop leaves a trail of tiny beads and a clear streak through the fog that fades over a few seconds.
  - Every timing is quantized to whole cycles per period (section 5.7).
- **Refraction.** Each drop is a small lens. The shader computes a normal from the drop's distance field and samples the frame copy at an offset that is flipped and magnified (as real drops show an inverted view of the scene behind). It also adds a thin bright rim and a soft dark edge.
- **Fog.** Between drops, the shader blends toward a blurred mip of the frame copy and a slight brightening, by `FogPct`. Trails and drops cut clear paths through it.
- **Settings:** DropsPct sets the bead and drop density; DropSizePct sets the radius range; SlidingPct sets the share of drops that slide and how often; FogPct sets the fog amount.
- **The mask:** the pane's soft edge (Feather) multiplies the whole Glass contribution, so the effect fades into the window frame.

### 5.6 Water

- The water pass draws a full-target triangle. Pixels where the water mask is 0 are discarded.
- **Flow mapping, the classic two-phase method:**
  - `phase = frac(t / cycle)` is computed on the CPU in double precision. Two samples of the background image are taken at `uv − flow × (phase)` and `uv − flow × (phase + 0.5 wrapped)`, cross-faded with a triangle weight so neither sample's reset is ever visible.
  - `flow` comes from the flow texture: the arrow's speed vector in target pixels per second, multiplied by C in the shader.
  - A low-frequency hash noise, sampled once per pixel, offsets the phase spatially, so the reset blend never pulses in step across the region.
  - The displacement is clamped so the samples stay inside the decoded image's bounds (sampler clamp).
  - Each sample is weighted by the water mask at the sample point, not at the pixel, so rock above a waterfall isn't dragged into the water. That was the most visible artifact in tuning. The undisplaced pixel is a small fallback.
- **Speed comes only from the arrow.** **Stretch** (the slider; named `StretchPct` in `WaterSettings`, replacing the earlier "Strength" from conversation) sets the cycle length C, from about 0.5 s to 3 s.
  - The distance per cycle is speed × C, so a low setting gives a tight shimmer and a high one gives long streaming streaks, while the water flows at the same speed either way.
  - The phase is integrated on the CPU like `FallClock` (`phase = frac(phase + dt / C)`, in double precision). A slider change alters the rate, never the position.
- **Foam:** a scrolling, flow-aligned streak noise (generated in the shader, no texture), weighted by the photo's own brightness, so white water gets churning highlights and dark water stays calm. `FoamPct` scales it.
- The result is blended over the background by the water mask, so the edges are soft.

### 5.7 Precision rules (binding; from `docs/snow-model.md`)

- **No ever-growing float32 values reach a shader.** All clocks (rain fall, glass drop timing, water phase, foam scroll) are kept in double precision on the CPU and passed as `frac(clock / period)`.
  - Every per-instance speed and frequency that multiplies that phase is quantized to a whole number of cycles per period P = 3600 s, so `frac` wraps seamlessly.
  - The water phase is integrated and wrapped on the CPU (section 5.6), so it is bounded by construction.
- **No setting-dependent wrap spans.** Rain's wrap margin is computed from the largest possible streak (all sliders at 100 %) and changes only on resize.
- **Unit tests** assert, for each `*FrameBuilder`, that the values passed to the shader stay bounded after a simulated 30 days, and that a slider change doesn't move a wrap span.

### 5.8 Editor

```
┌ Preview ─────────────────────────────┐ ┌──────────────────────────────┐
│                                      │ │ [Snow] [Rain] [Glass] [Water]│
│   photo with all effects running,    │ │ Water                  (on)  │
│   outlines + handles + arrows for    │ │ Stretch           ───●── 50% │
│   the open tab's regions only        │ │ Foam              ──●─── 30% │
│                                      │ │ Regions                      │
│                                      │ │ [Add water region] [Delete]  │
└──────────────────────────────────────┘ │ Soft edge  ● Show outlines   │
                                         │ [Undo] [Redo]                │
                                         │ Scaling ▾   [Apply to desktop]│
                                         └──────────────────────────────┘
```

- **The tab strip** is four pill buttons, each an icon plus a label: WeatherSnow, WeatherRain, Window, Drop. An effect that is switched on shows a small dot on its tab. The choice of tab isn't saved; the editor opens on Snow.
- **Each tab** shows its own on/off switch, its sliders, and the shared Regions controls bound to that effect's own `RegionEditor`. Each effect has its own undo history.
  - Ctrl+Z and Ctrl+Y apply to the open tab.
  - Switching tabs ends any drag in progress.
  - Scaling and Apply sit below the tabs, shared.
- **Snow / Rain:**
  - region controls as today;
  - the invert label names the effect ("Rain everywhere except the regions");
  - the help text is the same as today's, adjusted for the effect.
- **Glass:**
  - **Add pane** drops a 4-corner quad;
  - corners drag (keystone) and the body moves;
  - clicking an edge does **not** add a corner, and right-click/Delete on a corner does nothing (a pane is always 4 corners), so the edge-hover dot isn't drawn;
  - no invert switch;
  - help text: "Without panes, the whole picture is the glass. Add a pane and drag its corners onto a window."
- **Water:**
  - **Add water region** drops a 4-corner quad with a default arrow pointing down at about 0.12 image heights;
  - the corner tool works as for snow (add and remove corners);
  - the selected region draws an arrow from its centroid with a draggable head handle, dragged to aim and lengthen it, with its length clamped to the valid range;
  - unselected water regions show their arrow without a handle, so the flow of every region is visible at a glance;
  - no invert switch;
  - help text: "Add a region on a waterfall or river, then drag the arrow the way the water flows. A longer arrow flows faster."
- **The overlay** draws the arrows: `OverlayShapeKind` gains `Arrow` (a shaft plus a head) and the existing `Handle`, both drawn by `RegionOverlayRenderer`'s SDF shader.
- **Saving:** each change shows in the preview at once, saves after half a second, and goes to the desktop live (coalesced, about 30 per second) when the image is applied, through the slot's own message.
- **Library cards** show a badge per enabled effect, like today's snow badge.

### 5.9 Testing

- **Core (Mac):**
  - settings validation and defaults;
  - a v0.3 library loads with the snow and regions intact and the new slots off;
  - round trips;
  - Glass regions are forced to 4 points and Water regions must have a Flow;
  - `RainModel` and `WaterModel` mappings;
  - homography;
  - flow-field rasterization (direction per region, the last region wins, empty outside);
  - each frame builder's bounded values after 30 days and spans independent of the sliders;
  - `RegionEditor`'s plane mode (no corner add or remove) and arrow drag (hit, aim, clamp, undo);
  - IPC serialization of the new messages.
- **Windows (CI, WARP):**
  - each new shader compiles;
  - a rain frame adds light only inside its mask;
  - water moves pixels only inside its mask, and the output differs between two times while matching the photo outside;
  - glass changes pixels inside a pane and leaves pixels outside it identical;
  - with every effect disabled, the frame equals the background.
- **The user on Windows:** a new "Milestone 5: rain, glass and water" section in `docs/windows-test-checklist.md`, covering:
  - the look of each effect;
  - all four together;
  - the library from v0.3.1 loading;
  - live desktop updates;
  - GPU and CPU readings with all effects on versus snow only.

### 5.10 Delivery

- One PR, `feat/m5-rain-water` → `feat/m4-regions`, with conventional commits in a sensible order: model, rendering, editor, docs.
- CI is green on `windows-latest` with 0 warnings.
- An untested pre-release `v0.4.0-m5`, in the same format as `v0.3.0-m4`, from the CI artifact.

## 6. Tuning the look (builder's call)

The builder can't run the Windows app on the Mac, so each new shader's look is first written in GLSL in a throwaway WebGL2 page in the session scratchpad. It's tested on real photos (a CC0 or generated waterfall, a window, a street scene) and viewed in the browser pane. The page is then ported to HLSL with the same math. The constants that come out of it are recorded in `docs/effects-model.md`, which is committed, in the style of `docs/snow-model.md`. The page itself isn't committed or maintained.

## 7. Left to the builder

- Exact layer tables, curves and constants (tuned in the spike).
- Shader organisation, constant-buffer layouts, and whether Glass uses mips or a separate blur, provided the cost is about one frame copy.
- The internal class layout, for example whether each slot type is a generic `EffectSlot<T>` or a separate record.
- Icon choices within WPF-UI's Fluent symbols, and the exact wording of the help text.

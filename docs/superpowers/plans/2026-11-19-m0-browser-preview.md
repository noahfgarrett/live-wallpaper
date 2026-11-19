# Milestone 0 — Browser Preview Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A throwaway WebGL2 page, opened on the Mac, where the user drops in their own photo and tunes realistic snow and the region editor until both feel right. The tuned constants are then recorded for the Windows port.

**Architecture:** Plain ES modules served by a local static server, with no build step and no dependencies. Pure logic (snow parameter mapping, wind/fall clocks, scaling geometry, region operations, undo history) lives in small modules tested with `node --test`. The WebGL renderer, mask builder and SVG region editor are verified visually in the built-in browser.

**Tech Stack:** HTML, CSS, JavaScript (ES2022 modules, `// @ts-check` + JSDoc types), WebGL2, Node 24 built-in test runner, `python3 -m http.server` for serving.

**Spec:** `docs/superpowers/specs/2026-11-19-live-wallpaper-design.md` (sections 5.7, 5.8, 6)

**Depends on:** Milestone 1 Task 1 (the git repository and `main` branch exist). Work on branch `feat/m0-preview`.

## Global Constraints

- No npm packages, CDNs, fonts or any network request; the page must work offline. (The CLAUDE.md "no new dependencies" rule and the spec's "no network requests".)
- No user images are committed. The page only accepts an image by file picker or drag-and-drop.
- JavaScript with `// @ts-check` at the top of every module and JSDoc types on every exported function, including the return type. TypeScript itself is not added: that would be a dependency, and this is a spike.
- Files: `camelCase.js` for modules. The directory is `prototype/`.
- No `console.log` left in. A `?debug=1` query flag gates any diagnostics.
- Scaling math must give exactly the same numbers as the C# `ScalingMath` in Milestone 1 (same test values).
- Snow sizes and speeds are defined at a 1080 px reference height and scaled by `canvasHeightPx / 1080`.
- 4-space indentation.

## Review Focus

1. **Settings changed mid-animation** (speed or wind slider dragged): flakes must not jump. They should only smoothly change velocity → tests on `FallClock` and `WindModel` continuity (Task 2).
2. **Density at 0%, or wind at 0%:** density 0% draws no flakes, and wind 0% never moves flakes sideways (gusts included) → tests in Task 2.
3. **Window resized or browser zoomed while regions exist:** regions stay on the same part of the photo → test of the screen↔image round trip under Fill and Fit (Task 3).
4. **Deleting vertices down to a triangle, or dragging a region off the image:** a region never has fewer than 3 points, and points are clamped to the image → tests in Task 4.
5. **Very large photo (e.g. a 12000×8000 panorama):** loads without freezing the tab and is drawn downscaled → Task 5 caps the texture at `gl.MAX_TEXTURE_SIZE` and at 2× the canvas size, checked manually with a generated large image.

---

### Task 1: Page shell and local server

**Files:**
- Create: `prototype/index.html`, `prototype/styles.css`, `prototype/main.js`
- Create: `.claude/launch.json`

- [ ] **Step 1: Create `.claude/launch.json`**

```json
{
    "version": "0.0.1",
    "configurations": [
        {
            "name": "preview",
            "runtimeExecutable": "python3",
            "runtimeArgs": ["-m", "http.server", "5173", "--directory", "prototype", "--bind", "127.0.0.1"],
            "port": 5173
        }
    ]
}
```

- [ ] **Step 2: Build the shell**

`index.html` has a full-window `<canvas id="stage">`, an `<svg id="regions">` overlaid at the same size, and a collapsible right-hand `<aside id="panel">`.
- The panel has two sections, **Snow** and **Regions**. Their controls are listed in Tasks 6 and 7.
- There's an empty-state drop zone that reads "Drop a photo here or choose one". The picker accepts `image/*`.
- Dark styling (background `#111`, panel `#1c1c1e`, 8px radius, system font stack).
- `main.js` exports nothing. It wires up the file input and drag-and-drop, and loads the image into an `HTMLImageElement` via `URL.createObjectURL` (revoked after decode). It also handles resizing (sizing the canvas to `clientWidth * devicePixelRatio`) and the `requestAnimationFrame` loop.

- [ ] **Step 3: Verify**

Start the `preview` server and open `http://127.0.0.1:5173`. Expected: the empty state is visible, and dropping an image hides the drop zone. There are no console errors.

- [ ] **Step 4: Commit** — `chore: add browser preview shell`

---

### Task 2: Snow model (pure logic)

**Files:**
- Create: `prototype/snowModel.js`
- Test: `prototype/tests/snowModel.test.js`

**Interfaces:**
- Produces:
  - `LAYERS: readonly LayerSpec[]`
  - `MAX_FLAKES = 12000`
  - `MAX_FLAKES_PER_MEGAPIXEL = 1500`
  - `flakeCount(densityPct: number, megapixels: number): number`
  - `speedMultiplier(speedPct: number): number`
  - `sizeMultiplier(sizePct: number): number`
  - `class FallClock { step(dtSec: number, speedPct: number): number }`, which returns the accumulated fall time
  - `class WindModel { constructor(seed: number); step(dtSec: number, windPct: number, heightPx: number): { windPxPerSec: number, offsetPx: number } }`

`LayerSpec` = `{ name, share, sizePx: [min,max], fallPxPerSec: [min,max], opacity: [min,max], softness, windResponse, swayPx: [min,max], swayHz: [min,max] }`, with values at the 1080 px reference. Starting values, to be tuned with the user in Task 8:

| layer | share | sizePx | fallPxPerSec | opacity | softness | windResponse | swayPx | swayHz |
|---|---|---|---|---|---|---|---|---|
| far | 0.55 | 1.2–2.4 | 30–55 | 0.35–0.60 | 0.35 | 0.5 | 4–10 | 0.10–0.25 |
| mid | 0.35 | 2.5–4.5 | 60–95 | 0.60–0.85 | 0.50 | 0.8 | 8–18 | 0.15–0.35 |
| near | 0.10 | 6–12 | 110–170 | 0.35–0.60 | 0.90 | 1.2 | 14–30 | 0.20–0.45 |

- [ ] **Step 1: Write the failing tests**

```js
// @ts-check
import { test } from "node:test";
import assert from "node:assert/strict";
import { flakeCount, speedMultiplier, sizeMultiplier, FallClock, WindModel, LAYERS } from "../snowModel.js";

test("density 0% draws no flakes", () => assert.equal(flakeCount(0, 2.0736), 0));
test("density 50% at 1080p is 1100 flakes", () => assert.equal(flakeCount(50, 2.0736), 1100));
test("density 100% at 1080p is 3110 flakes", () => assert.equal(flakeCount(100, 2.0736), 3110));
test("flake count is capped at MAX_FLAKES", () => assert.equal(flakeCount(100, 33.2), 12000));
test("speed 0/50/100% maps to 0.25/1.0/1.75", () => {
    assert.equal(speedMultiplier(0), 0.25);
    assert.equal(speedMultiplier(50), 1);
    assert.equal(speedMultiplier(100), 1.75);
});
test("size 0/50/100% maps to 0.5/1.0/1.5", () => {
    assert.equal(sizeMultiplier(0), 0.5);
    assert.equal(sizeMultiplier(50), 1);
    assert.equal(sizeMultiplier(100), 1.5);
});
test("layer shares sum to 1", () => assert.equal(LAYERS.reduce((s, l) => s + l.share, 0), 1));
test("fall clock does not jump when speed changes", () => {
    const clock = new FallClock();
    let t = 0;
    for (let i = 0; i < 60; i++) t = clock.step(1 / 60, 10);
    const next = clock.step(1 / 60, 100);
    assert.ok(next - t <= (1 / 60) * 1.75 + 1e-9);
});
test("wind 0% never moves flakes sideways, gusts included", () => {
    const wind = new WindModel(42);
    for (let i = 0; i < 600; i++) assert.equal(wind.step(1 / 60, 0, 1080).offsetPx, 0);
});
test("wind offset changes smoothly between frames", () => {
    const wind = new WindModel(7);
    let prev = wind.step(1 / 60, 100, 1080);
    for (let i = 0; i < 3600; i++) {
        const cur = wind.step(1 / 60, 100, 1080);
        assert.ok(Math.abs(cur.offsetPx - prev.offsetPx) <= Math.abs(cur.windPxPerSec) / 60 + 1e-6);
        prev = cur;
    }
});
test("wind model is deterministic for a seed", () => {
    const a = new WindModel(3), b = new WindModel(3);
    for (let i = 0; i < 100; i++) assert.deepEqual(a.step(0.016, 60, 1080), b.step(0.016, 60, 1080));
});
```

- [ ] **Step 2: Run and confirm failure** — `node --test prototype/tests/` → FAIL (module not found).

- [ ] **Step 3: Implement `prototype/snowModel.js`**

- `flakeCount = Math.min(MAX_FLAKES, Math.round(MAX_FLAKES_PER_MEGAPIXEL * (d/100) ** 1.5 * megapixels))`
- `speedMultiplier = 0.25 + 1.5 * (s/100)`
- `sizeMultiplier = 0.5 + s/100`
- `FallClock` accumulates `dt * speedMultiplier(pct)`.
- `WindModel`:
  - `base = 120 * (pct/100) * heightPx/1080`
  - `gust(t) = max(0, g(t))² × 0.8 × base`, where `g` is a sum of three sines with incommensurate frequencies (0.071, 0.113, 0.029 Hz) and phases derived from `seed`
  - `windPxPerSec = base + gust`
  - `offsetPx += windPxPerSec * dt`

- [ ] **Step 4: Run** — `node --test prototype/tests/` → all PASS.

- [ ] **Step 5: Commit** — `feat(preview): add snow parameter model with tests`

---

### Task 3: Scaling geometry (pure logic, mirrors C#)

**Files:**
- Create: `prototype/scaling.js`
- Test: `prototype/tests/scaling.test.js`

**Interfaces:**
- Produces:
  - `computeDestination(image: {w,h}, target: {w,h}, mode: "fill"|"fit"|"stretch"|"center"): {x,y,w,h}`
  - `imageToScreen(p: {x,y}, dest): {x,y}`, where `p` is normalized 0–1 image coordinates
  - `screenToImage(p, dest): {x,y}`, not clamped

- [ ] **Step 1: Write the failing tests** (the same values as the C# `ScalingMathTests`)

```js
test("fill 1000x1000 into 1920x1080", () =>
    assert.deepEqual(computeDestination({ w: 1000, h: 1000 }, { w: 1920, h: 1080 }, "fill"), { x: 0, y: -420, w: 1920, h: 1920 }));
test("fit 1000x1000 into 1920x1080", () =>
    assert.deepEqual(computeDestination({ w: 1000, h: 1000 }, { w: 1920, h: 1080 }, "fit"), { x: 420, y: 0, w: 1080, h: 1080 }));
test("stretch fills the target exactly", () =>
    assert.deepEqual(computeDestination({ w: 1000, h: 1000 }, { w: 1920, h: 1080 }, "stretch"), { x: 0, y: 0, w: 1920, h: 1080 }));
test("center keeps native size", () =>
    assert.deepEqual(computeDestination({ w: 1000, h: 1000 }, { w: 1920, h: 1080 }, "center"), { x: 460, y: 40, w: 1000, h: 1000 }));
test("center crops a larger image symmetrically", () =>
    assert.deepEqual(computeDestination({ w: 4000, h: 3000 }, { w: 1920, h: 1080 }, "center"), { x: -1040, y: -960, w: 4000, h: 3000 }));
test("screen/image round trip is stable under fill", () => {
    const dest = computeDestination({ w: 4000, h: 3000 }, { w: 1280, h: 720 }, "fill");
    const p = screenToImage(imageToScreen({ x: 0.3, y: 0.7 }, dest), dest);
    assert.ok(Math.abs(p.x - 0.3) < 1e-9 && Math.abs(p.y - 0.7) < 1e-9);
});
test("zero-size image throws RangeError", () =>
    assert.throws(() => computeDestination({ w: 0, h: 10 }, { w: 10, h: 10 }, "fill"), RangeError));
```

- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** — `feat(preview): add scaling geometry`

---

### Task 4: Region operations and undo history (pure logic)

**Files:**
- Create: `prototype/regions.js`, `prototype/history.js`
- Test: `prototype/tests/regions.test.js`, `prototype/tests/history.test.js`

**Interfaces:**
- Produces, in `regions.js` (regions are immutable; every function returns a new object):
  - `Region = { id: string, points: {x,y}[] }`, in normalized image coordinates
  - `RegionSet = { regions: Region[], invert: boolean, featherPct: number }`
  - `createQuad(id: string, center = {x:0.5,y:0.5}, size = 0.3): Region` gives points in order TL, TR, BR, BL
  - `moveVertex(region, index, p): Region` clamps to [0,1]
  - `insertVertex(region, edgeIndex, p): Region` inserts after `points[edgeIndex]`
  - `removeVertex(region, index): Region` leaves the region unchanged when it has ≤ 3 points
  - `translate(region, dx, dy): Region` limits dx/dy so every point stays inside [0,1]
  - `nearestEdge(points: {x,y}[], p, maxDist): { edgeIndex, distance } | null`, in whatever space the caller passes (the editor passes screen pixels)
  - `isPlane(region): boolean` is true when the region has exactly 4 points
- Produces, in `history.js`: `class History<T> { constructor(initial: T); get current(): T; push(state: T): void; undo(): T; redo(): T; canUndo; canRedo }`, capped at 100 entries.

- [ ] **Step 1: Write the failing tests.** Cover at least:
  - `createQuad("a")` gives points `[(0.35,0.35),(0.65,0.35),(0.65,0.65),(0.35,0.65)]`, with `isPlane` true.
  - `insertVertex(q, 0, {x:0.5,y:0.35})` gives 5 points, with the new one at index 1, and `isPlane` false.
  - `removeVertex` on a triangle returns the same 3 points.
  - `moveVertex(q, 0, {x:-0.2, y:1.4})` puts that point at `(0, 1)`.
  - `translate(q, 0.5, 0)` gives a maximum x of exactly 1, and the shape is preserved (width 0.3).
  - `nearestEdge` returns edge 0 for a point 2px above the top edge with `maxDist` 6, and `null` with `maxDist` 1.
  - History: after push/push/undo, `current` is the first pushed state; `redo` restores it; a push after an undo clears redo; the history holds at most 100 entries.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** — `feat(preview): add region operations and undo history`

---

### Task 5: WebGL2 renderer: background and stateless snow

**Files:**
- Create: `prototype/renderer.js`, `prototype/flakeAtlas.js`
- Modify: `prototype/main.js`

**Interfaces:**
- Consumes: `LAYERS`, `flakeCount`, `sizeMultiplier`, `FallClock`, `WindModel` (Task 2); `computeDestination` (Task 3).
- Produces:
  - `createRenderer(canvas: HTMLCanvasElement): Renderer`
  - `Renderer = { setImage(img: HTMLImageElement, mode): void, setMask(source: HTMLCanvasElement | null): void, frame(state: FrameState): void, dispose(): void }`
  - `FrameState = { fallTime, windOffsetPx, timeSec, count, sizeMul, motionBlur: boolean, enabled: boolean }`
  - `buildFlakeAtlas(): HTMLCanvasElement`

Approach:
- **Background:** one textured quad drawn into `computeDestination`'s rectangle, with the canvas cleared to black.
- **Image upload:** downscale through a 2D canvas when the image is larger than `min(gl.MAX_TEXTURE_SIZE, 2 × canvas size)`.
- **Flake atlas:** `buildFlakeAtlas` draws 4 soft flake sprites into a 256×64 canvas: two radial-gradient blobs with different falloff, one six-armed soft star, and one irregular cluster. The shader picks one per flake.
- **Snow:** one `drawArraysInstanced(TRIANGLE_STRIP, 0, 4, count)` call with no per-flake buffers. The vertex shader derives 6 randoms from `gl_InstanceID` with a PCG-style integer hash, and picks the layer by comparing a random value against the cumulative `share`s. It computes:
  - `y = mod(y0·(H+2m) + fallTime·fall, H+2m) − m`
  - `x = mod(x0·(W+2m) + windOffset·windResponse + sway·sin(2π·swayHz·timeSec + phase), W+2m) − m`
  - The margin `m` is the largest flake size, so flakes wrap in and out off-screen.
  - Rotation is `spin·timeSec`.
  - With motion blur on, the quad stretches along the velocity vector by `fall·0.04` and the fragment alpha fades along the stretch.
- **Fragment shader:** atlas sample × layer opacity, with near-layer softness applied as a wider falloff. The result is multiplied by the mask texture (`texture(mask, gl_FragCoord.xy / resolution).r`; with no mask, a 1×1 white texture). Premultiplied-alpha blending.
- Sizes and speeds are multiplied by `canvasHeight/1080`.

- [ ] **Step 1: Implement** the renderer and atlas, and wire `frame()` into the rAF loop in `main.js`, using fixed settings (density 50, speed 50, wind 30, size 50).
- [ ] **Step 2: Verify visually.** Open the preview with a test photo generated locally into the scratchpad (not committed). Take a screenshot and check:
  - flakes are visibly different in size and softness across three depths
  - the near flakes are soft and large
  - flakes sway, and none move in perfectly straight lines
  - the background is correctly filled
  - there are no console errors
- [ ] **Step 3: Verify the large-image path.** Load a generated 12000×8000 PNG. The tab stays responsive, and the texture size is logged under `?debug=1` and is ≤ the cap.
- [ ] **Step 4: Commit** — `feat(preview): render background and stateless snow in WebGL2`

---

### Task 6: Snow controls and FPS

**Files:**
- Modify: `prototype/index.html`, `prototype/main.js`, `prototype/styles.css`

- [ ] **Step 1: Add the Snow panel controls**:
  - Enable snow (checkbox, on)
  - Density, Speed, Wind and Particle size (range 0–100, defaults 50/50/30/50, each showing its value)
  - Motion blur (checkbox, off)
  - FPS (30 / 60 / Adaptive; Adaptive = display rate here)
  - Scaling (Fill / Fit / Stretch / Center)
  - "Copy settings" button: copies a JSON of all current values plus `LAYERS` to the clipboard
  - A small live readout: FPS and active flakes

  30 FPS is done by skipping alternate rAF callbacks once the elapsed time is under 1/30 s. Slider input updates the next frame immediately.
- [ ] **Step 2: Verify.** Drag each slider and confirm it changes the animation live and that flakes never jump (Review Focus 1). Density 0 shows no flakes; Wind 0 shows no sideways drift. Take screenshots at density 20 and 90.
- [ ] **Step 3: Commit** — `feat(preview): add live snow controls`

---

### Task 7: Region editor and mask

**Files:**
- Create: `prototype/regionEditor.js`, `prototype/mask.js`
- Modify: `prototype/main.js`, `prototype/index.html`, `prototype/styles.css`

**Interfaces:**
- Consumes: everything in Tasks 3–5.
- Produces:
  - `createRegionEditor(svg: SVGSVGElement, getDest: () => {x,y,w,h}, history: History<RegionSet>, onChange: (set: RegionSet) => void): { render(): void, setEnabled(on: boolean): void }`
  - `buildMask(set: RegionSet, dest, width: number, height: number): HTMLCanvasElement | null`, which returns `null` when there are no regions (the effect then shows everywhere)

Interaction rules (from the spec, section 5.8):
- **Add region** drops `createQuad` at the image centre.
- Corner handles are 10px circles that drag single vertices (projector-style keystone).
- Hovering within 6px of an edge shows a "+" ghost handle, and clicking it inserts a vertex there.
- Selecting a vertex and pressing Delete/Backspace removes it, or right-clicking the vertex does the same.
- Dragging inside a polygon moves the whole region. Clicking a region selects it.
- **Delete region** removes the selected region.
- The panel also has an Invert checkbox, a Feather slider (0–10% of image width, default 1%) and "Show outlines" (on).
- Ctrl/Cmd+Z undoes; Shift+Ctrl/Cmd+Z redoes.
- History gets one entry per completed drag, not one per pointer move.
- Handles stay at constant screen size at any canvas size.

Mask building:
- Draw the polygons, filled white, in screen space onto a canvas at render resolution, with `ctx.filter = blur(featherPx)` where `featherPx = featherPct/100 × dest.w`.
- When `invert` is on, fill white first and cut the polygons out with `destination-out`.
- Rebuild the mask only when the region set, the canvas size or the scaling changes.

- [ ] **Step 1: Implement** the editor and mask, and feed `buildMask` into `renderer.setMask`.
- [ ] **Step 2: Verify the window scenario.** With a photo of a window, add a region, keystone its 4 corners onto the glass, and add a 5th vertex on an edge. Check that snow appears only inside the region, and only outside it with Invert on. Feather visibly softens the edge. Undo and redo walk back through each drag. Resize the browser window: the region stays on the glass (Review Focus 3).
- [ ] **Step 3: Verify the edge cases.** You can't delete below 3 vertices, and a region can't be dragged off the image (Review Focus 4). Take screenshots.
- [ ] **Step 4: Commit** — `feat(preview): add region editor with feathered masks`

---

### Task 8: Tuning session and record

**Files:**
- Create: `docs/snow-model.md`
- Modify: `prototype/snowModel.js` (tuned `LAYERS`), `docs/superpowers/specs/2026-11-19-live-wallpaper-design.md` section 5.8 only if the interaction rules changed

- [ ] **Step 1: Hand the preview to the user.** Give the command to start it, and ask them to try their own photos and say what looks wrong: flake shapes, speeds, depth, wind feel, and anything about the region tool.
- [ ] **Step 2: Iterate** on `LAYERS`, the mapping curves and the editor behaviour from their feedback. Re-run `node --test prototype/tests/` after every change. Update any test values the user deliberately changed, and say so.
- [ ] **Step 3: Write `docs/snow-model.md`** containing:
  - the final layer table
  - the mapping formulas
  - the hash and the position formula
  - the wind model
  - the mask rules
  - the "Copy settings" JSON of the user's preferred defaults

  This is the single source for the HLSL port in Milestone 2.
- [ ] **Step 4: Commit** — `docs: record tuned snow model and region rules`. Then push `feat/m0-preview` and open a PR to `main` (the user decides on the merge).

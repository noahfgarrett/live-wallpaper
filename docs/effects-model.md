# Rain, glass and water model

The source for the Milestone 5 shaders, in the style of `docs/snow-model.md`. The builder tuned these values in a throwaway WebGL2 page on public photos (a twin waterfall, forest light), then ported them to HLSL with the same math. All sizes are at a 1080 px reference height and are multiplied by `scale = targetHeight / 1080`. P = 3600 s throughout.

## Defaults

| Effect | Setting | Default |
|---|---|---|
| Rain | on, density, speed, wind, length, brightness | off, 50, 50, 20, 50, 50 |
| Glass | on, drops, drop size, sliding, fog | off, 50, 50, 40, 30 |
| Water | on, stretch, foam, mist | off, 50, 30, 40 |

## Rain

Rain uses the same stateless instancing as snow: hash randoms per instance, a layer picked by cumulative share, and `y = span.y × fract(r3 + k × fallPhase) − margin` with `k = max(1, round(fall × P / span.y))`.

| Layer | Share | Width (px) | Fall (px/s) | Opacity | Wind response |
|---|---|---|---|---|---|
| far | 0.62 | 0.9–1.2 | 900–1200 | 0.10–0.20 | 0.7 |
| mid | 0.30 | 1.2–1.7 | 1300–1700 | 0.16–0.28 | 0.9 |
| near | 0.08 | 2.0–3.0 | 1900–2500 | 0.10–0.20 | 1.1 |

**Settings mapping**
- Count: `min(9000, round(1400 × (density/100)^1.4 × megapixels))`.
- Speed multiplier: `0.5 + speed/100`. The fall clock is `fallTime += dt × speedMultiplier`.
- Exposure (s): `0.008 + 0.042 × length/100`.
- Opacity multiplier: `0.4 + 1.6 × brightness/100`.
- Wind follows snow's model, but with `base = 520 × wind/100 × height/1080` px/s. The gusts and the integration are the same.

**Per-streak geometry**
- The velocity is `(windPxPerSec × windResponse, (k × span.y / P) × speedMultiplier)`, using the *quantized* fall speed, so the streak points where it actually moves.
- `length = max(|v| × exposure × lerp(0.8, 1.2, r5), 2 × width)`.
- The quad runs from the head back along the velocity, and is `2 × width` wide.

**Look**
- Cross-section: `(1 − |2u − 1|)²`.
- Along the streak: `smoothstep(0, 0.25, v) × smoothstep(1, 0.45, v)`, where v = 0 at the tail end.
- Colour: (0.82, 0.87, 0.94), premultiplied.
- The streak doesn't take its colour from the photo. Low premultiplied alpha already makes rain near-invisible on a bright sky and visible on dark backgrounds, as in real rain photos.

**Wrap margin:** `(2500 × speedMul(100) × exposure(100) × 1.2 + 9 + 40) × scale`. It is independent of every setting.

## Glass

**Pane space**
- Each pane maps the unit square to its 4 corners, in order top-left, top-right, bottom-right, bottom-left, using the square-to-quad homography (Heckbert).
- The shader takes the inverse to find `uv`, then works in `Pref = uv × panePx / scale`. `panePx` is the average length of the pane's opposite edges, so drops are measured in reference pixels of the pane's apparent size, and they foreshorten with perspective.
- With no panes, one pane covers the whole target.

**Beads:** two jittered grids, one bead per cell.
- Each cell's life lasts `cycles = max(1, round(P / lerp(25, 70, r1)))` cycles per period. A new hash is drawn per life (`floor(timePhase × cycles + r2)`), so beads reappear in new places.
- Alpha over a life: `smoothstep(0, 0.04, lp) × smoothstep(1, 0.55, lp)`. The bead shrinks to 75 % as it evaporates.

| Grid | Cell (px) | Radius (px) | Density |
|---|---|---|---|
| small | 16 | 1.5–4.5 (r² bias) | drops/100 |
| large | 44 | 4–12 | 0.8 × drops/100 |

The bead is placed so it stays inside its cell.

**Sliding drops:** one column per `80 × sizeMul` px. Each pixel checks its own column and the two next to it.
- **Timing:** each column runs `cycles = max(1, round(P / lerp(6, 16, r1)))` cycles per period, with a new hash per run. A run slides only if `r3 < sliding/100`.
- **Path:**
  - The drop stays stuck until `start = lerp(0.25, 0.55, r4)` of the cycle.
  - Then `u` goes from 0 to 1, and `prog = u − sin(2π·hops·u) / (2π·hops) × 0.9`, with hops 3–7. That gives stop-start motion that never reverses.
  - `y = y0 + prog × (1.15 × paneH − y0 + 20)`, where `y0 = r6 × 0.6 × paneH − 10`.
  - `x = baseX + sin(y × f + φ) × a`, with f 0.03–0.07 and a 1–4 px.
- **Drop shape:** radius 7–13 × sizeMul px. Above its centre it is stretched by 1/0.72 into a teardrop.
- **Trail:**
  - It fades with `exp(−behind / (0.35 × paneH))`.
  - It clears fog within `0.55 r` of the path (soft from 0.4 r).
  - Trail beads are spaced every `7 × sizeMul` px with probability 0.55, radius 0.14–0.32 r.

**Drop shading**
- **Lens:** the drop shows the frame at `centre − (p − centre) × 2.6 + (0, −1.2 r)`, at mip 0.6. That is a flipped and magnified view of the scene behind.
- **Rim:** darkened by `lerp(1.08, 0.55, smoothstep(0.55, 1, d))`.
- **Highlight:** `0.45 × smoothstep(0.32, 0, |n − (−0.32, −0.42)|)`.
- **Edge anti-aliasing:** `smoothstep(0, 1, (1 − d) × rPx / 1.2)`.

**Fog**
- `fogged = frame(mip lerp(1.5, 6, fog)) × 0.9 + 0.08 × fog + 0.02`.
- `amount = smoothstep(0, 0.15, fog) × 0.92 × (1 − trailClear)`.
- The slider mostly sets the blur radius. A blend weight alone looked unfogged at 30 %.

**Size multiplier:** `0.6 + 0.8 × size/100`.

## Water

**Flow**
- The arrow is stored in normalized image coordinates. Its speed in image heights per second is `|(flow.x × imageAspect, flow.y)| / 4` (`ArrowSeconds = 4`).
- In target pixels per second, that speed is multiplied by the image's displayed height, along the arrow's on-screen direction.
- The editor clamps the aspect-corrected length to 0.02–0.5.

**Cycle (Stretch):** `C = 0.5 + 2.5 × stretch/100` seconds. The phase is integrated on the CPU: `phase = frac(phase + dt / C)`.

**Shader**, in screen pixels `px`:
- `q = (px·perp, px·dir) / scale`.
- Phase noise: `n = Noise(q / (70, 140))`, `p0 = fract(phase + n)`, `p1 = fract(p0 + 0.5)`, and the cross-fade weight is `w = |2·p0 − 1|`.
- Wobble across the flow, per sample with its own phase p: `wobble(p) = perp × (Noise(q / (9, 30) − (0, 4·p)) − 0.5) × 2 × scale`.
- The two samples are `a = px − flow·C·p0 + wobble(p0)` and `b = px − flow·C·p1 + wobble(p1)`, so each sample's wobble resets while its weight is 0.
- Each sample is weighted by the water mask *at the sample point*: `wa = (1 − w) × mask(a)`, `wb = w × mask(b)`, plus `0.02 ×` the undisplaced pixel.
  - This stops rock above a waterfall being dragged into the water. It was the most visible artifact in tuning.
- **Foam:**
  - The same two-phase trick runs on a stretched noise: `Noise(q.x / 3.5, (q.y − |flow·C|/scale × p) / 26)` at p0 and p1, cross-faded with w.
  - It is weighted by `smoothstep(0.55, 0.95, foam) × smoothstep(0.35, 0.85, luminance) × foam/100`.
  - The result is mixed toward (0.97, 0.98, 1.0) by `0.55 ×` that weight.
- **Blend:** `(colour × m, m)`, premultiplied over the background, where m is the water mask at `px`.

**Noise:** value noise over the integer-hashed lattice (`Hash(x + 32768) × 0x10001 ^ Hash(y + 32768)`), smoothstep-interpolated. The inputs are screen pixels divided by fixed scales, so they stay small for any running time.

**Mist** (`mist` = MistPct / 100):
- `reach = (0.04 + 0.10 × mist) × displayed image height` (the whole source image as displayed, as for the flow speed).
- **Plume:** for k = 1..6, with f = k/6 and `up = px − dir × reach × f`: `plume = max(plume, max(mask(up), mask(up ± perp × reach × f × 0.45)) × (1 − 0.8 f))`.
- **Base:** `base = plume × (1 − mask(px + dir × reach × 0.3))`.
- **Haze:** a two-phase cross-fade (`mp0 = fract(phase + 0.13)`, `mp1 = fract(mp0 + 0.5)`, weight `|2·mp0 − 1|`) of `0.6 × Noise((s + (0, 19·mp) + dir·10·mp) / 46) + 0.4 × Noise((s + (0, 19·mp)) / 17)`, where `s = px / scale`. The mp1 octaves are offset by 7.3 and 3.1 so the two phases differ.
- **Opacity:** `mistA = mist × smoothstep(0, 0.85, base) × (0.35 + 0.65 × haze) × 0.8`.
- **Colour:** `lerp((0.9, 0.93, 0.97), photo × 0.5 + 0.55, 0.3)`.
- **Output, premultiplied:** `rgb = water × m × (1 − mistA) + mistColour × mistA`, `a = m × (1 − mistA) + mistA`.
- **Discard** when `m < 0.002 && mistA < 0.004`.
- `dir` comes from the flow texture, which is dilated outside the regions to the nearest region's flow, up to 0.14 × the displayed image height.
- The flow texture has one cell per 4 × 4 target pixels (`ceil(target / 4)` cells), so it is sampled at `px / (4 × flowSize)`, which maps exactly for any target size.
- Where the flow is ≈ 0 (beyond the dilation), the pixel is discarded when `m < 0.002`; otherwise it is the mask's soft tail and draws as still water: `(photo × m, m)`.

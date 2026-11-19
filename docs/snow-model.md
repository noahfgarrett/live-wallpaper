# Snow model

The single source for porting the preview's snow to the Windows renderer (Milestone 2). The user approved the preview's look as-is on 2026-11-19 ("exactly what I had in mind"), so the values below are the tuned defaults. The code lives in `prototype/snowModel.js` and `prototype/renderer.js`.

## Defaults

| Setting | Default |
|---|---|
| Snow | on |
| Density | 50% |
| Speed | 50% |
| Wind | 30% |
| Flake size | 50% |
| Motion blur | off |
| Frame rate | 60 fps |

## Layers

All sizes and speeds are at a 1080 px reference height and are multiplied by `canvasHeight / 1080`.

| Layer | Share | Size (px) | Fall (px/s) | Opacity | Softness | Wind response | Sway (px) | Sway (Hz) |
|---|---|---|---|---|---|---|---|---|
| far | 0.55 | 1.2–2.4 | 30–55 | 0.35–0.60 | 0.35 | 0.5 | 4–10 | 0.10–0.25 |
| mid | 0.35 | 2.5–4.5 | 60–95 | 0.60–0.85 | 0.50 | 0.8 | 8–18 | 0.15–0.35 |
| near | 0.10 | 6–12 | 110–170 | 0.35–0.60 | 0.90 | 1.2 | 14–30 | 0.20–0.45 |

## Settings to parameters

- Flake count: `min(12000, round(1500 × (density/100)^1.5 × megapixels))`.
- Speed multiplier: `0.25 + 1.5 × speed/100`.
- Size multiplier: `0.5 + size/100`.
- Fall clock: `fallTime += dt × speedMultiplier`, so a speed change alters velocity, never position.
- Wind, in px/s:
  - `base = 120 × wind/100 × height/1080`
  - `g(t) = (sin(2π·0.071t + φ1) + 0.6·sin(2π·0.113t + φ2) + 0.4·sin(2π·0.029t + φ3)) / 2`, with the phases φ derived from a seed
  - `gust = max(0, g)² × 0.8 × base`
  - `windOffset += (base + gust) × dt`
  - Wind 0% means no drift at all, gusts included.

## Per-flake position (stateless, computed in the vertex shader)

Six or more hash randoms come from the instance id (a PCG-style integer hash). The layer is picked by comparing random 0 against the cumulative shares. Then:

```
span = resolution + 2 × margin
y = mod(r3 × span.y + fallTime × fall, span.y) − margin
x = mod(r7 × span.x + windOffset × windResponse + sway × sin(2π × swayHz × t + phase), span.x) − margin
```

- Rotation is `r9 × 2π + (r10 − 0.5) × 3 × t`.
- The sprite is one of 4 atlas cells:
  - two soft radial blobs
  - a soft six-armed star
  - an irregular cluster
- Softness blends the sprite with a gaussian blob: `exp(−3.2 · d²)`.
- Snow colour is (0.95, 0.975, 1.0), drawn with premultiplied alpha.
- **Motion blur:** the quad is stretched along velocity by `|v| × 0.04` and alpha is scaled by `size / (size + 0.6 × streak)`.

## Regions

- **Storage:** polygons in normalized image coordinates, plus `invert` and `featherPct`.
- **Mask:** rasterized at render resolution. The polygons are filled white on black (inverted: black holes cut from white), then blurred with σ = `featherPx / 2`, where `featherPx = featherPct/100 × image width`.
- **Effect:** the snow's alpha is multiplied by the mask's red channel.
- **Editing:**
  - a new region is a 4-corner quad, and dragging a corner works like a projector keystone;
  - clicking an edge adds a point, and Delete or right-click removes one (minimum 3 points);
  - dragging the body moves the region;
  - Cmd/Ctrl+Z undoes, with one history entry per completed drag.

The requirements below come from the Milestone 0 review and are binding for the port.

## Requirements for the port

- **The wrap margin must not depend on any setting.** Flakes wrap with `mod(… , span)`, where `span = resolution + 2 × margin`. If the margin changes with the size slider, every flake teleports. The jump is about (wraps so far × Δspan), so it grows with running time. The margin is `wrapMarginPx(scale) = (largestSizePx × sizeMultiplier(100) × 1.5 + 40) × scale` and changes only on resize.
- **No ever-growing float32 values in the shader.** A wallpaper runs for days. In float32:
  - after about a day, `fallTime × fall` (about 2e7) snaps positions to a 2 px grid;
  - after about a week, `fallTime` itself resolves to only 0.0625 s, so flakes stall for a few frames and then jump.

  The port keeps these values bounded, computing them on the CPU in double precision:
  - **Wind:** pass `mod(windOffset × windResponse[layer], span.x)` per layer.
  - **Fall:** quantize each flake's speed to a whole number of spans per period P (for example P = 3600 s, `k = round(fall × P / span)`). Pass `phase = fract(fallTime / P)`, and compute `y = span × fract(r + k × phase) − margin`.
  - **Sway and spin:** wrap `time` to the same period, or quantize the sway and spin frequencies to whole cycles per P.

## Open items from the review (decide during or after tuning)

- **Far flakes:** at 1080p they are 1.2–2.4 px and can shimmer. Consider mipmapping the sprite atlas and a 2 px minimum quad size, with alpha scaled by `(size / clamped)²`.
- **Feather blur:** the blur σ is `featherPx / 2`, where `featherPx = featherPct / 100 × image width`. The blur is centred on the polygon edge, so the effect bleeds about one feather width outside the region.
- **Motion blur:** the preview dims a stretched flake uniformly. The plan described a fade along the streak; pick one when tuning.

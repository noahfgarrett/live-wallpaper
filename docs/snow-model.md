# Snow model

The single source for porting the preview's snow to the Windows renderer (Milestone 2). The tuned constants and formulas are added after the tuning session (Milestone 0, Task 8). The requirements below come from the Milestone 0 review and are binding for the port.

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

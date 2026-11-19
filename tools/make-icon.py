"""Generates assets/LiveWallpaper.ico: a six-armed snowflake on a rounded night-blue tile.

Run from the repository root: python3 tools/make-icon.py (needs Pillow).
"""
import math
import sys

from PIL import Image, ImageDraw, ImageFilter

SIZE = 1024
NIGHT_TOP = (22, 38, 66)
NIGHT_BOTTOM = (10, 18, 34)
ICE = (212, 233, 255)


def tile() -> Image.Image:
    gradient = Image.new("RGBA", (SIZE, SIZE))
    draw = ImageDraw.Draw(gradient)
    for y in range(SIZE):
        t = y / (SIZE - 1)
        color = tuple(round(a + (b - a) * t) for a, b in zip(NIGHT_TOP, NIGHT_BOTTOM))
        draw.line([(0, y), (SIZE, y)], fill=color + (255,))
    mask = Image.new("L", (SIZE, SIZE), 0)
    ImageDraw.Draw(mask).rounded_rectangle([40, 40, SIZE - 40, SIZE - 40], radius=220, fill=255)
    out = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    out.paste(gradient, (0, 0), mask)
    return out


def flake() -> Image.Image:
    layer = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    draw = ImageDraw.Draw(layer)
    cx = cy = SIZE / 2
    arm = SIZE * 0.34
    width = round(SIZE * 0.05)
    for i in range(6):
        a = i / 6 * math.tau - math.pi / 2
        end = (cx + math.cos(a) * arm, cy + math.sin(a) * arm)
        draw.line([(cx, cy), end], fill=ICE + (255,), width=width)
        for frac, length in ((0.55, 0.30), (0.80, 0.20)):
            bx = cx + math.cos(a) * arm * frac
            by = cy + math.sin(a) * arm * frac
            for side in (-1, 1):
                b = a + side * math.radians(50)
                draw.line([(bx, by), (bx + math.cos(b) * arm * length, by + math.sin(b) * arm * length)], fill=ICE + (255,), width=round(width * 0.8))
        draw.ellipse([end[0] - width * 0.6, end[1] - width * 0.6, end[0] + width * 0.6, end[1] + width * 0.6], fill=ICE + (255,))
    r = width * 1.4
    draw.ellipse([cx - r, cy - r, cx + r, cy + r], fill=ICE + (255,))
    glow = layer.filter(ImageFilter.GaussianBlur(SIZE * 0.02))
    return Image.alpha_composite(glow, layer)


def main(path: str) -> None:
    icon = Image.alpha_composite(tile(), flake())
    icon.save(path, sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "assets/LiveWallpaper.ico")

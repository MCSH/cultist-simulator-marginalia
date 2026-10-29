"""Draws mod/cover.png: a page with a margin note and a quill, in ink and brass.

Rendered at 8x and downscaled for clean edges. Run: python3 tools/make_cover.py
"""
import math
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

SIZE = 100
S = 8  # supersampling factor
W = SIZE * S

INK = (18, 15, 12)
BRASS = (184, 148, 82)
BRASS_DARK = (122, 94, 48)
PARCHMENT = (226, 211, 178)
PARCHMENT_EDGE = (176, 154, 112)
TEXT = (98, 86, 70)
RUBRIC = (140, 52, 38)


def rotate(points, angle, origin):
    ox, oy = origin
    c, s = math.cos(angle), math.sin(angle)
    return [(ox + (x - ox) * c - (y - oy) * s, oy + (x - ox) * s + (y - oy) * c) for x, y in points]


def main():
    img = Image.new("RGB", (W, W), INK)

    # Soft warm glow behind the page.
    glow = Image.new("L", (W, W), 0)
    ImageDraw.Draw(glow).ellipse((W * 0.12, W * 0.1, W * 0.88, W * 0.9), fill=70)
    glow = glow.filter(ImageFilter.GaussianBlur(W * 0.12))
    img.paste(Image.new("RGB", (W, W), (90, 66, 34)), mask=glow)

    page = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    d = ImageDraw.Draw(page)
    centre = (W / 2, W / 2)
    tilt = math.radians(-7)

    # The page.
    left, top, right, bottom = W * 0.24, W * 0.14, W * 0.78, W * 0.86
    d.polygon(rotate([(left, top), (right, top), (right, bottom), (left, bottom)], tilt, centre),
              fill=PARCHMENT, outline=PARCHMENT_EDGE, width=S * 2)

    # Margin rule and lines of text.
    margin = left + (right - left) * 0.3
    d.line(rotate([(margin, top + S * 6), (margin, bottom - S * 6)], tilt, centre), fill=RUBRIC, width=S)
    y = top + (bottom - top) * 0.14
    widths = [1.0, 0.92, 1.0, 0.7, 1.0, 0.96, 0.84, 1.0, 0.6]
    for frac in widths:
        x0 = margin + S * 5
        x1 = x0 + (right - S * 6 - x0) * frac
        d.line(rotate([(x0, y), (x1, y)], tilt, centre), fill=TEXT, width=int(S * 2.2))
        y += (bottom - top) * 0.085

    # A brass squiggle written in the margin.
    squiggle = []
    for i in range(60):
        t = i / 59
        sy = top + (bottom - top) * (0.3 + 0.32 * t)
        sx = left + (margin - left) * (0.5 + 0.28 * math.sin(t * math.pi * 3.2))
        squiggle.append((sx, sy))
    d.line(rotate(squiggle, tilt, centre), fill=BRASS_DARK, width=int(S * 2.6), joint="curve")

    # Quill: a feather vane along a shaft, nib resting at the end of the squiggle.
    nib = rotate([squiggle[-1]], tilt, centre)[0]
    angle = math.radians(-58)
    length = W * 0.62
    tip = (nib[0] + math.cos(angle) * length, nib[1] + math.sin(angle) * length)
    ux, uy = math.cos(angle), math.sin(angle)
    nx, ny = -uy, ux
    vane = []
    for i in range(41):
        t = i / 40
        along = 0.22 + 0.78 * t
        half = math.sin(math.pi * t) ** 0.8 * W * 0.075 * (0.6 + 0.4 * t)
        vane.append((nib[0] + ux * length * along + nx * half, nib[1] + uy * length * along + ny * half))
    for i in range(40, -1, -1):
        t = i / 40
        along = 0.22 + 0.78 * t
        half = math.sin(math.pi * t) ** 0.8 * W * 0.045 * (0.6 + 0.4 * t)
        vane.append((nib[0] + ux * length * along - nx * half, nib[1] + uy * length * along - ny * half))
    d.polygon(vane, fill=BRASS, outline=BRASS_DARK, width=S)
    d.line([nib, tip], fill=BRASS_DARK, width=int(S * 1.6))
    d.line([nib, (nib[0] + ux * length * 0.22, nib[1] + uy * length * 0.22)], fill=(60, 46, 26), width=int(S * 2.4))

    shadow = page.split()[3].filter(ImageFilter.GaussianBlur(S * 3))
    img.paste(Image.new("RGB", (W, W), (0, 0, 0)), (S * 2, S * 3), mask=shadow.point(lambda a: a * 0.6))
    img.paste(page, mask=page)

    # Brass frame.
    frame = ImageDraw.Draw(img)
    frame.rectangle((S * 3, S * 3, W - S * 3 - 1, W - S * 3 - 1), outline=BRASS, width=S * 2)
    frame.rectangle((S * 7, S * 7, W - S * 7 - 1, W - S * 7 - 1), outline=BRASS_DARK, width=S)

    out = Path(__file__).resolve().parent.parent / "mod" / "cover.png"
    img.resize((SIZE, SIZE), Image.LANCZOS).save(out, optimize=True)
    img.resize((400, 400), Image.LANCZOS).save(Path("/tmp/claude-1000/-home-sajjad-src-2026-37-cultist/a23ce749-6d8d-4ad4-b0cb-d1c33e4f3ae8/scratchpad/cover_preview.png"))
    print(out)


if __name__ == "__main__":
    main()

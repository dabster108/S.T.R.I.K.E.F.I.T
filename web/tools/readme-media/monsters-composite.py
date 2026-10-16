"""Composite of the three guardian portraits for the README: same height, dark neutral background, labelled.

  python3 monsters-composite.py --out ../../../docs/images/monsters.jpg

Reads the portraits the site already ships (web/frontend/public/monsters/level{1,2,3}-1440.webp, rendered from the Unity
models by ../render-portraits) and the guardian names from content/story.json. Needs Python 3 and Pillow.
"""
import argparse
import json
import os

from PIL import Image, ImageDraw, ImageFont

here = os.path.dirname(os.path.abspath(__file__))
root = os.path.abspath(os.path.join(here, "..", "..", ".."))
ap = argparse.ArgumentParser()
ap.add_argument("--out", default=os.path.join(root, "docs", "images", "monsters.jpg"))
ap.add_argument("--width", type=int, default=1600)
ap.add_argument("--height", type=int, default=640, help="height of each portrait")
args = ap.parse_args()

story = json.load(open(os.path.join(root, "content", "story.json"), encoding="utf-8"))
levels = story["levels"]
BG = (22, 24, 29)
TEXT = (236, 238, 242)
MUTED = (150, 156, 168)


def font(size, bold=False):
    for path in ("/System/Library/Fonts/Helvetica.ttc", "/System/Library/Fonts/Supplemental/Arial.ttf", "/Library/Fonts/Arial.ttf"):
        if os.path.exists(path):
            try:
                return ImageFont.truetype(path, size, index=1 if bold and path.endswith(".ttc") else 0)
            except OSError:
                pass
    return ImageFont.load_default()


label_h = 96
pad = 24
panel_w = args.width // len(levels)
canvas = Image.new("RGB", (args.width, args.height + label_h + pad), BG)
draw = ImageDraw.Draw(canvas)
for i, lv in enumerate(levels):
    img = Image.open(os.path.join(root, "web", "frontend", "public", "monsters", f"level{lv['level']}-1440.webp")).convert("RGBA")
    h = args.height
    w = round(img.width * h / img.height)
    img = img.resize((w, h), Image.LANCZOS)
    # fade the bottom edge into the background (the Level 3 render is a bust crop with a straight cut)
    fade = int(h * 0.14)
    alpha = img.getchannel("A")
    ramp = Image.linear_gradient("L").resize((w, fade)).transpose(Image.FLIP_TOP_BOTTOM)
    mask = Image.new("L", (w, h), 255)
    mask.paste(ramp, (0, h - fade))
    from PIL import ImageChops
    img.putalpha(ImageChops.multiply(alpha, mask))
    x0 = i * panel_w + (panel_w - w) // 2
    layer = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    layer.paste(img, (x0, 0))
    canvas.paste(layer, (0, 0), layer)
    cx = i * panel_w + panel_w // 2
    for text, f, fill, y in ((f"Level {lv['level']}  {lv['key']}", font(26), MUTED, args.height + 6), (lv["guardian"], font(34, True), TEXT, args.height + 42)):
        tw = draw.textlength(text, font=f)
        draw.text((cx - tw / 2, y), text, font=f, fill=fill)
os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
canvas.save(args.out, quality=86, optimize=True, progressive=True)
print(f"{args.out}: {canvas.size[0]}x{canvas.size[1]}, {os.path.getsize(args.out) / 1024:.0f} KB")

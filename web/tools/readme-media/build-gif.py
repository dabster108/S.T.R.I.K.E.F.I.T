"""Builds the README hero GIF and a poster frame from the PNG frames written by hero-frames.mjs.

  python3 build-gif.py --frames /tmp/hero-frames --out ../../../docs/images/hero.gif --poster ../../../docs/images/hero-poster.png --fps 13

Needs Python 3 and Pillow only (no ffmpeg or gifski). One palette is built from frames sampled across the whole clip and
every frame is mapped to it without dithering, so identical frames stay identical and Pillow stores only the changed
region of each frame. Loops forever. Reduce --colors if the file is too large.
"""
import argparse
import glob
import os

from PIL import Image

ap = argparse.ArgumentParser()
ap.add_argument("--frames", required=True)
ap.add_argument("--out", required=True)
ap.add_argument("--poster")
ap.add_argument("--fps", type=float, default=13)
ap.add_argument("--colors", type=int, default=256)
ap.add_argument("--dither", action="store_true", help="Floyd-Steinberg dithering (smoother glows, bigger file)")
ap.add_argument("--width", type=int, default=1280)
args = ap.parse_args()

files = sorted(glob.glob(os.path.join(args.frames, "f*.png")))
if not files:
    raise SystemExit("no frames found")


def load(path):
    im = Image.open(path).convert("RGB")
    if im.width != args.width:
        im = im.resize((args.width, round(im.height * args.width / im.width)), Image.LANCZOS)
    return im


# One shared palette from a sample of frames (every 6th, tiled into one image).
sample = [load(f) for f in files[:: max(1, len(files) // 30)]]
sw, sh = sample[0].size
tile = Image.new("RGB", (sw // 2, sh // 2 * len(sample)))
for i, im in enumerate(sample):
    tile.paste(im.resize((sw // 2, sh // 2), Image.LANCZOS), (0, i * (sh // 2)))
palette_img = tile.quantize(colors=args.colors, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE)

DITHER = Image.Dither.FLOYDSTEINBERG if args.dither else Image.Dither.NONE
frames = [load(f).quantize(palette=palette_img, dither=DITHER) for f in files]
duration = round(1000 / args.fps / 10) * 10  # GIF delays are in 10 ms units
os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
frames[0].save(args.out, save_all=True, append_images=frames[1:], duration=duration, loop=0, optimize=True, disposal=1)
size = os.path.getsize(args.out)
print(f"{args.out}: {len(frames)} frames, {len(frames) * duration / 1000:.1f} s, {sample[0].size[0]}x{sample[0].size[1]}, {size / 1048576:.2f} MB")

if args.poster:
    load(files[0]).save(args.poster, optimize=True)
    print(f"{args.poster}: {os.path.getsize(args.poster) / 1024:.0f} KB")

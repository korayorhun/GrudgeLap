"""Draws the road wear sheet, game/assets/textures/road_wear.png: a tile the asphalt material multiplies in over
its second UV set (11 m across the road by 40 m along it). Mostly white, with the marks of a road that has been
used: tar snakes (sealed cracks, meandering along the road), the seams of patches, oil stains in the wheel tracks
and a slow blotching of the tone. Deterministic; no source material.
Usage: python make-road-wear.py [out.png]
"""
import math
import random
import site
import sys

sys.path.append(site.getusersitepackages())
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

SIZE = 1024
out = sys.argv[1] if len(sys.argv) > 1 else "game/assets/textures/road_wear.png"
rng = random.Random(1989)
img = Image.new("L", (SIZE, SIZE), 250)
draw = ImageDraw.Draw(img)


def wrap(x, y):
    return x % SIZE, y % SIZE


# Slow blotches: the tone drifts a few percent over metres.
blotch = Image.new("L", (SIZE, SIZE), 0)
bd = ImageDraw.Draw(blotch)
for _ in range(60):
    x, y, r = rng.randrange(SIZE), rng.randrange(SIZE), rng.randrange(60, 200)
    bd.ellipse((x - r, y - r, x + r, y + r), fill=rng.randrange(40, 120))
blotch = blotch.filter(ImageFilter.GaussianBlur(45))
base = np.asarray(img, dtype=np.float32)
base -= (np.asarray(blotch, dtype=np.float32) - 60) * 0.12
img = Image.fromarray(np.clip(base, 0, 255).astype(np.uint8))
draw = ImageDraw.Draw(img)

# Tar snakes: a few slow random walks biased along the road (the y axis), two or three pixels wide, a shade
# darker than the asphalt (not black: the first cut, 26 near-black walks, read as a spider's web from the car).
for _ in range(9):
    x, y = rng.uniform(0, SIZE), rng.uniform(0, SIZE)
    heading = rng.choice((-1, 1)) * math.pi / 2 + rng.uniform(-0.5, 0.5)
    width = rng.choice((2, 2, 3))
    tone = rng.randrange(120, 165)
    for _ in range(rng.randrange(80, 260)):
        heading += rng.uniform(-0.16, 0.16)
        heading = heading * 0.92 + (math.pi / 2 if math.sin(heading) > 0 else -math.pi / 2) * 0.08
        nx, ny = x + math.cos(heading) * 3, y + math.sin(heading) * 3
        if abs(nx - x) < SIZE / 2 and abs(ny - y) < SIZE / 2:
            draw.line((*wrap(x, y), *wrap(nx, ny)), fill=tone, width=width)
        x, y = nx % SIZE, ny % SIZE

# Patch seams: rectangles outlined in tar, their inside a shade off.
for _ in range(7):
    x, y = rng.randrange(SIZE), rng.randrange(SIZE)
    w, h = rng.randrange(60, 170), rng.randrange(80, 260)
    inside = rng.choice((228, 238, 255, 255))
    for dx in (0, -SIZE):
        for dy in (0, -SIZE):
            draw.rectangle((x + dx, y + dy, x + dx + w, y + dy + h), fill=inside, outline=rng.randrange(110, 150), width=2)

# Oil in the wheel tracks: soft dark spots at about 0.3 and 0.7 of the width.
spots = Image.new("L", (SIZE, SIZE), 0)
sd = ImageDraw.Draw(spots)
for _ in range(40):
    lane = rng.choice((0.31, 0.69)) + rng.uniform(-0.05, 0.05)
    x, y, r = int(lane * SIZE), rng.randrange(SIZE), rng.randrange(5, 14)
    sd.ellipse((x - r, y - r, x + r, y + r), fill=rng.randrange(120, 220))
spots = spots.filter(ImageFilter.GaussianBlur(4))
arr = np.asarray(img, dtype=np.float32) - np.asarray(spots, dtype=np.float32) * 0.28
img = Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8)).convert("RGB")
img.save(out, optimize=True)
print(f"{out}: {SIZE}x{SIZE}, mean {np.asarray(img.convert('L')).mean():.0f}")

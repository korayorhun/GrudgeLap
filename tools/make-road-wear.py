"""Draws the road wear sheet, game/assets/textures/road_wear.png: a tile the asphalt material multiplies in over
its second UV set, 11 m across the road by 160 m along it (40 m until 0.13; four times as long now, so that the
same tar snake does not come round every forty metres). Mostly white, with the marks of a road that has been
used: tar snakes (sealed cracks, meandering along the road), repair patches, oil darkening the wheel tracks and a
slow blotching of the tone. Since 0.36 the patches are filled (a newer, darker asphalt with a ragged sealed seam:
until then they were rectangles outlined in a thin dark line, pencil marks on the road), the blotches and the
wheel tracks are broad enough to be seen a hundred metres off, so the far road is not a flat grey that turns into a
drawing as it comes near, and the tar snakes are a shade lighter. Deterministic; no source material.
Usage: python make-road-wear.py [out.png]
"""
import math
import random
import site
import sys

sys.path.append(site.getusersitepackages())
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

W, H = 1024, 4096
out = sys.argv[1] if len(sys.argv) > 1 else "game/assets/textures/road_wear.png"
rng = random.Random(1989)
img = Image.new("L", (W, H), 250)
draw = ImageDraw.Draw(img)


def wrap(x, y):
    return x % W, y % H


# Slow blotches: the tone drifts a few percent over metres.
blotch = Image.new("L", (W, H), 0)
bd = ImageDraw.Draw(blotch)
for _ in range(240):
    x, y, r = rng.randrange(W), rng.randrange(H), rng.randrange(60, 200)
    bd.ellipse((x - r, y - r, x + r, y + r), fill=rng.randrange(40, 120))
blotch = blotch.filter(ImageFilter.GaussianBlur(45))
base = np.asarray(img, dtype=np.float32)
base -= (np.asarray(blotch, dtype=np.float32) - 60) * 0.20
img = Image.fromarray(np.clip(base, 0, 255).astype(np.uint8))
draw = ImageDraw.Draw(img)

# Tar snakes: slow random walks biased along the road (the y axis), two or three pixels wide, a shade darker
# than the asphalt (not black: the first cut, 26 near-black walks on a 40 m sheet, read as a spider's web from the car).
for _ in range(36):
    x, y = rng.uniform(0, W), rng.uniform(0, H)
    heading = rng.choice((-1, 1)) * math.pi / 2 + rng.uniform(-0.5, 0.5)
    width = rng.choice((2, 2, 3))
    tone = rng.randrange(150, 190)
    for _ in range(rng.randrange(80, 260)):
        heading += rng.uniform(-0.16, 0.16)
        heading = heading * 0.92 + (math.pi / 2 if math.sin(heading) > 0 else -math.pi / 2) * 0.08
        nx, ny = x + math.cos(heading) * 3, y + math.sin(heading) * 3
        # Compared after wrapping (0.36): a step across the sheet's edge wrapped one end and not the other, and drew a
        # straight line the whole height of the sheet, the long ruled lines along the road.
        x0, y0 = wrap(x, y)
        x1, y1 = wrap(nx, ny)
        if abs(x1 - x0) < W / 2 and abs(y1 - y0) < H / 2:
            draw.line((x0, y0, x1, y1), fill=tone, width=width)
        x, y = nx % W, ny % H

# Repair patches: a newer, darker asphalt in a ragged outline (its corners rounded by the roller, its edges
# wandering a little), sealed with a soft tar seam; some a lane wide, some a trench across the road.
patches = Image.new("L", (W, H), 0)
seams = Image.new("L", (W, H), 0)
pd = ImageDraw.Draw(patches)
sd2 = ImageDraw.Draw(seams)
for _ in range(22):
    across = rng.random() < 0.25
    w = rng.randrange(380, 760) if across else rng.randrange(90, 260)
    h = rng.randrange(40, 110) if across else rng.randrange(120, 420)
    x = rng.randrange(W) if not across else rng.randrange(0, W - w)
    y = rng.randrange(H)
    shade = rng.randrange(30, 70)
    pts = []
    for k in range(24):
        a = k / 24 * 2 * math.pi
        # A rounded rectangle, its edge jittered by a few pixels.
        cx, cy = math.cos(a), math.sin(a)
        px_ = x + w / 2 + (w / 2) * max(-1, min(1, cx * 1.25)) + rng.uniform(-4, 4)
        py_ = y + h / 2 + (h / 2) * max(-1, min(1, cy * 1.25)) + rng.uniform(-4, 4)
        pts.append((px_, py_))
    for dx in (0, -W, W):
        for dy in (0, -H, H):
            moved = [(px_ + dx, py_ + dy) for px_, py_ in pts]
            pd.polygon(moved, fill=shade)
            sd2.line(moved + [moved[0]], fill=rng.randrange(60, 110), width=3)
patches = patches.filter(ImageFilter.GaussianBlur(1.5))
seams = seams.filter(ImageFilter.GaussianBlur(1.2))
arr = np.asarray(img, dtype=np.float32) - np.asarray(patches, dtype=np.float32) - np.asarray(seams, dtype=np.float32) * 0.5
img = Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8))
draw = ImageDraw.Draw(img)

# The wheel tracks: the oil and rubber of the traffic darken two broad bands in each lane a little, all the way along.
tracks = np.zeros((H, W), dtype=np.float32)
xs = np.arange(W) / W
for centre in (0.19, 0.31, 0.69, 0.81):
    tracks += np.exp(-((xs - centre) / 0.035) ** 2)[None, :]
img = Image.fromarray(np.clip(np.asarray(img, dtype=np.float32) - tracks * 14, 0, 255).astype(np.uint8))
draw = ImageDraw.Draw(img)

# Oil in the wheel tracks: soft dark spots at about 0.3 and 0.7 of the width.
spots = Image.new("L", (W, H), 0)
sd = ImageDraw.Draw(spots)
for _ in range(160):
    lane = rng.choice((0.31, 0.69)) + rng.uniform(-0.05, 0.05)
    x, y, r = int(lane * W), rng.randrange(H), rng.randrange(5, 14)
    sd.ellipse((x - r, y - r, x + r, y + r), fill=rng.randrange(120, 220))
spots = spots.filter(ImageFilter.GaussianBlur(4))
arr = np.asarray(img, dtype=np.float32) - np.asarray(spots, dtype=np.float32) * 0.28
img = Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8)).convert("RGB")
img.save(out, optimize=True)
print(f"{out}: {W}x{H}, mean {np.asarray(img.convert('L')).mean():.0f}")

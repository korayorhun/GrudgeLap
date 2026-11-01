"""Draws the road's wear decals and the weather's masks for the Unreal level, deterministically and from no source
material: cracks, asphalt patches, oil stains in the wheel tracks, skid marks and a pothole (RGBA, the alpha the
decal's coverage), a tileable puddle mask (grey, the wet road's standing water) and a tileable sheet of rain
streaks for the rain cylinder round the camera.
Usage: python make_decals.py <out folder>
"""
import math
import os
import random
import site
import sys

sys.path.append(site.getusersitepackages())
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), "..", "Import", "Decals")
os.makedirs(out, exist_ok=True)
rng = random.Random(1989)


def save(name, rgb, alpha=None):
    """rgb: (h, w, 3) uint8; alpha: (h, w) float 0..1 or None for an opaque grey image."""
    image = Image.fromarray(rgb, "RGB")
    if alpha is not None:
        image.putalpha(Image.fromarray(np.clip(alpha * 255, 0, 255).astype(np.uint8), "L"))
    image.save(os.path.join(out, f"{name}.png"), optimize=True)
    print(f"{name}.png {image.size[0]}x{image.size[1]}")


def noise(w, h, cells, octaves=3, seed=0):
    """Tileable value noise in 0..1 on a w by h grid: smooth interpolation of random lattices, halving each octave."""
    r = np.random.default_rng(seed)
    total = np.zeros((h, w), dtype=np.float32)
    weight = 1.0
    norm = 0.0
    for o in range(octaves):
        n = cells * (2 ** o)
        lattice = r.random((n, n)).astype(np.float32)
        ys = (np.arange(h) / h) * n
        xs = (np.arange(w) / w) * n
        y0 = np.floor(ys).astype(int) % n
        x0 = np.floor(xs).astype(int) % n
        fy = (ys - np.floor(ys))[:, None]
        fx = (xs - np.floor(xs))[None, :]
        fy = fy * fy * (3 - 2 * fy)
        fx = fx * fx * (3 - 2 * fx)
        a = lattice[y0][:, x0]
        b = lattice[y0][:, (x0 + 1) % n]
        c = lattice[(y0 + 1) % n][:, x0]
        d = lattice[(y0 + 1) % n][:, (x0 + 1) % n]
        total += weight * ((a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy)
        norm += weight
        weight *= 0.5
    return total / norm


def grey(w, h, tone, variation=6, seed=0):
    r = np.random.default_rng(seed)
    base = np.full((h, w, 3), tone, dtype=np.float32)
    base += (r.random((h, w, 1)) - 0.5) * variation
    return np.clip(base, 0, 255).astype(np.uint8)


# ---- cracks: a few branching random walks, the alpha fading at their ends and sides ------------------------
for index in (1, 2, 3):
    w, h = 512, 1024
    alpha = Image.new("L", (w, h), 0)
    draw = ImageDraw.Draw(alpha)
    walks = [(rng.uniform(0.3 * w, 0.7 * w), rng.uniform(0.05 * h, 0.25 * h), rng.uniform(math.pi * 0.35, math.pi * 0.65))]
    while walks:
        x, y, heading = walks.pop()
        width = rng.choice((2, 3, 3, 4))
        for step in range(rng.randrange(120, 260)):
            heading += rng.uniform(-0.22, 0.22)
            nx, ny = x + math.cos(heading) * 3.5, y + math.sin(heading) * 3.5
            if not (8 < nx < w - 8 and 8 < ny < h - 8):
                break
            fade = min(1.0, step / 30.0)
            draw.line((x, y, nx, ny), fill=int(230 * fade), width=width)
            x, y = nx, ny
            if rng.random() < 0.012 and len(walks) < 3:
                walks.append((x, y, heading + rng.choice((-1, 1)) * rng.uniform(0.5, 1.1)))
    alpha = alpha.filter(ImageFilter.GaussianBlur(0.8))
    a = np.asarray(alpha, dtype=np.float32) / 255.0
    save(f"crack_{index}", grey(w, h, 24, 8, seed=index), a * 0.9)

# ---- patches: a rectangle of other asphalt with a tar seam round it ----------------------------------------
for index, tone in ((1, 48), (2, 96)):
    w, h = 512, 768
    alpha = Image.new("L", (w, h), 0)
    draw = ImageDraw.Draw(alpha)
    draw.rounded_rectangle((28, 28, w - 28, h - 28), radius=18, fill=150)
    draw.rounded_rectangle((28, 28, w - 28, h - 28), radius=18, outline=230, width=7)
    alpha = alpha.filter(ImageFilter.GaussianBlur(2.5))
    a = np.asarray(alpha, dtype=np.float32) / 255.0
    a *= 0.75 + 0.25 * noise(w, h, 4, 2, seed=20 + index)
    rgb = grey(w, h, tone, 10, seed=30 + index)
    # The seam is tar: near black where the alpha is strongest.
    seam = np.clip((a - 0.62) / 0.3, 0, 1)[:, :, None]
    rgb = (rgb * (1 - seam) + np.array([22, 21, 20], dtype=np.float32) * seam).astype(np.uint8)
    save(f"patch_{index}", rgb, a)

# ---- oil: soft dark blobs, as a leak leaves them in the wheel track ------------------------------------
for index in (1, 2):
    w = h = 512
    alpha = Image.new("L", (w, h), 0)
    draw = ImageDraw.Draw(alpha)
    for _ in range(rng.randrange(3, 7)):
        x, y = rng.uniform(0.3 * w, 0.7 * w), rng.uniform(0.25 * h, 0.75 * h)
        r = rng.uniform(40, 120)
        draw.ellipse((x - r, y - r * rng.uniform(0.6, 1.0), x + r, y + r * rng.uniform(0.6, 1.0)), fill=rng.randrange(120, 220))
    alpha = alpha.filter(ImageFilter.GaussianBlur(26))
    a = np.asarray(alpha, dtype=np.float32) / 255.0 * 0.85
    save(f"oil_{index}", grey(w, h, 16, 6, seed=40 + index), a)

# ---- skid: two streaks of rubber, ragged, fading in and out -----------------------------------------------
w, h = 512, 2048
a = np.zeros((h, w), dtype=np.float32)
ys = np.arange(h, dtype=np.float32) / h
along = np.clip(np.sin(ys * math.pi) * 1.4, 0, 1) * (0.7 + 0.3 * noise(w, h, 6, 2, seed=51)[:, 0])
for centre in (0.27, 0.73):
    xs = np.arange(w, dtype=np.float32) / w
    across = np.exp(-((xs - centre) / 0.07) ** 2)
    a += along[:, None] * across[None, :]
a *= 0.6 + 0.4 * noise(w, h, 12, 3, seed=52)
save("skid", grey(w, h, 20, 6, seed=53), np.clip(a, 0, 1) * 0.8)

# ---- pothole: a dark broken hollow with a pale, crumbling rim --------------------------------------------
w = h = 512
alpha = Image.new("L", (w, h), 0)
draw = ImageDraw.Draw(alpha)
points = [(w / 2 + math.cos(t) * (180 + rng.uniform(-28, 28)), h / 2 + math.sin(t) * (170 + rng.uniform(-28, 28))) for t in np.linspace(0, 2 * math.pi, 28, endpoint=False)]
draw.polygon(points, fill=240)
alpha = alpha.filter(ImageFilter.GaussianBlur(3))
a = np.asarray(alpha, dtype=np.float32) / 255.0
yy, xx = np.mgrid[0:h, 0:w]
radial = np.sqrt(((xx - w / 2) / (w / 2)) ** 2 + ((yy - h / 2) / (h / 2)) ** 2)
rim = np.clip(1 - np.abs(radial - 0.66) / 0.1, 0, 1)
rgb = np.full((h, w, 3), 30, dtype=np.float32)
rgb += rim[:, :, None] * np.array([80, 76, 70], dtype=np.float32)
rgb += (np.random.default_rng(60).random((h, w, 1)) - 0.5) * 12
save("pothole", np.clip(rgb, 0, 255).astype(np.uint8), a * 0.97)

# ---- the puddle mask: tileable noise, high where the water stands --------------------------------------
puddles = noise(512, 512, 3, 3, seed=101)
save("puddles", np.repeat((puddles * 255).astype(np.uint8)[:, :, None], 3, axis=2))

# ---- rain streaks: thin bright falling lines on black, tileable both ways ------------------------------
w, h = 256, 1024
streaks = np.zeros((h, w), dtype=np.float32)
for _ in range(260):
    x = rng.randrange(w)
    y = rng.randrange(h)
    length = rng.randrange(40, 140)
    bright = rng.uniform(0.35, 1.0)
    for k in range(length):
        yy = (y + k) % h
        fade = math.sin(k / length * math.pi)
        streaks[yy, x] = max(streaks[yy, x], bright * fade)
        if rng.random() < 0.3:
            streaks[yy, (x + 1) % w] = max(streaks[yy, (x + 1) % w], bright * fade * 0.5)
save("rain_streaks", np.repeat((streaks * 255).astype(np.uint8)[:, :, None], 3, axis=2))

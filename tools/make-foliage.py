"""Builds the foliage atlases the world uses as alpha-scissor cards (game/assets/textures):
 - grass_blades.png: eight grass tufts (tapered blades, colour variation) in a 4x2 grid, drawn here
 - leaf_clusters.png: four leaf clusters composited from the CC0 ambientCG LeafSet001 cutouts
   (tools/leafset001.png, the 1K colour sheet: six leaves in a 3x2 grid) in a 2x2 grid
Run with a Python that has Pillow: python -I tools/make-foliage.py"""
import os, sys, site, math, random
sys.path.append(site.getusersitepackages())
from PIL import Image, ImageDraw

here = os.path.dirname(os.path.abspath(__file__))
dst = os.path.join(here, '..', 'game', 'assets', 'textures')
random.seed(7)

# ---- grass tufts --------------------------------------------------------------------------------
W = 1024
atlas = Image.new('RGBA', (W, W // 2), (0, 0, 0, 0))
cell = W // 4
for c in range(8):
    ox = (c % 4) * cell
    oy = (c // 4) * cell
    tuft = Image.new('RGBA', (cell, cell), (0, 0, 0, 0))
    d = ImageDraw.Draw(tuft)
    base_x = cell // 2
    base_y = cell - 6
    blades = 26 + c * 2
    for b in range(blades):
        h = random.uniform(0.45, 1.0) * (cell - 20)
        lean = random.uniform(-0.55, 0.55)
        width = random.uniform(5, 11)
        g = random.uniform(0.35, 0.65)
        colour = (int(60 + 70 * g), int(110 + 100 * g), int(30 + 40 * g), 255)
        tip_colour = (int(120 + 60 * g), int(150 + 80 * g), int(50 + 40 * g), 255)
        x0 = base_x + random.uniform(-cell * 0.3, cell * 0.3)
        pts = []
        segs = 7
        for s in range(segs + 1):
            t = s / segs
            x = x0 + lean * h * t * t + math.sin(t * 3 + b) * 2
            y = base_y - h * t
            w = width * (1 - t) ** 0.8
            pts.append((x, y, w))
        left = [(x - w, y) for x, y, w in pts]
        right = [(x + w, y) for x, y, w in reversed(pts)]
        d.polygon(left + right, fill=colour)
        d.line([(x, y) for x, y, w in pts[3:]], fill=tip_colour, width=2)
    atlas.paste(tuft, (ox, oy), tuft)
atlas.save(f'{dst}/grass_blades.png', optimize=True)

# ---- leaf clusters ----------------------------------------------------------------------------
leaves = Image.open(os.path.join(here, 'leafset001.png')).convert('RGBA')   # LeafSet001: six green leaves in a 3x2 grid
lw, lh = leaves.size
cells = [leaves.crop((i % 3 * lw // 3, i // 3 * lh // 2, (i % 3 + 1) * lw // 3, (i // 3 + 1) * lh // 2)) for i in range(6)]
# Tighten each leaf to its opaque bounds so rotations stay centred.
cells = [c.crop(c.getbbox()) for c in cells]
C = 512
atlas = Image.new('RGBA', (C * 2, C * 2), (0, 0, 0, 0))
for k in range(4):
    cluster = Image.new('RGBA', (C, C), (0, 0, 0, 0))
    count = 70 + k * 15
    for i in range(count):
        leaf = random.choice(cells).copy()
        scale = random.uniform(0.18, 0.34) * C / max(leaf.size)
        leaf = leaf.resize((max(2, int(leaf.width * scale)), max(2, int(leaf.height * scale))), Image.LANCZOS)
        # Slight colour variation per leaf: darker inside the cluster, lighter at the rim.
        r = random.uniform(0, 1)
        ang = random.uniform(0, 2 * math.pi)
        rad = C * 0.42 * math.sqrt(r)
        cx = C / 2 + rad * math.cos(ang)
        cy = C / 2 + rad * math.sin(ang) * 0.85
        tint = 0.75 + 0.45 * r
        px = leaf.load()
        for y in range(leaf.height):
            for x in range(leaf.width):
                pr, pg, pb, pa = px[x, y]
                px[x, y] = (min(255, int(pr * tint)), min(255, int(pg * tint)), min(255, int(pb * tint * 0.9)), pa)
        leaf = leaf.rotate(random.uniform(0, 360), expand=True, resample=Image.BICUBIC)
        cluster.alpha_composite(leaf, (int(cx - leaf.width / 2), int(cy - leaf.height / 2)))
    atlas.paste(cluster, ((k % 2) * C, (k // 2) * C))
atlas.save(f'{dst}/leaf_clusters.png', optimize=True)
print('foliage atlases written')

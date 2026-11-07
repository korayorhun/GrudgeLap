"""Packs the frames tools/bake-impostors.py rendered into the game's impostor atlases (0.39).

Usage: python tools/pack-impostors.py <bake prefix> <out dir> <name> [frame px, default 768]

Writes <out dir>/<name>_albedo.png (RGBA: the colour in sRGB, the alpha), <name>_normal.png (RGB: the normal in each
view's camera frame as Blender gives it, x right, y up, z along the view, mapped to 0..1) and <name>.json (the
frame's size in metres, the views and the atlas's grid) for FoliageBuilder. Frames go left to right, top to bottom.
The renders are scaled down with a Lanczos filter: their leaves' edges, dithered at 1024 px, come out clean.
"""
import json
import os
import sys

from PIL import Image

prefix, out_dir, name = sys.argv[1], sys.argv[2], sys.argv[3]
px = int(sys.argv[4]) if len(sys.argv) > 4 else 768
meta = json.load(open(prefix + ".json", encoding="utf-8"))
views, columns, rows = meta["views"], meta["columns"], meta["rows"]
os.makedirs(out_dir, exist_ok=True)
for kind in ("albedo", "normal"):
    atlas = Image.new("RGBA" if kind == "albedo" else "RGB", (columns * px, rows * px), (0, 0, 0, 0) if kind == "albedo" else (128, 128, 0))
    for k, path in enumerate(meta[kind]):
        frame = Image.open(path).convert("RGBA")
        if kind == "albedo":
            # Premultiplied while scaling, so the transparent ground's black does not darken the leaves' edges.
            premultiplied = Image.alpha_composite(Image.new("RGBA", frame.size, (0, 0, 0, 0)), frame)
            small = premultiplied.resize((px, px), Image.LANCZOS)
        else:
            small = frame.convert("RGB").resize((px, px), Image.LANCZOS)
        atlas.paste(small, ((k % columns) * px, (k // columns) * px))
    atlas.save(os.path.join(out_dir, f"{name}_{kind}.png"), optimize=True)
json.dump({"views": views, "columns": columns, "rows": rows, "frame_metres": round(meta["frame_metres"], 3),
           "height": round(meta["height"], 3), "source": meta["source"], "object": meta.get("object")},
          open(os.path.join(out_dir, f"{name}.json"), "w", encoding="utf-8"), indent=1)
print("packed", name, f"{columns * px}x{rows * px}")

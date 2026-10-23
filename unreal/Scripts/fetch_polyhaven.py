"""Downloads CC0 Poly Haven models (glTF) and textures for the feasibility scene into Import/PolyHaven.
Usage: python -I fetch_polyhaven.py <out dir> [resolution]
Every file is listed in https://api.polyhaven.com/files/<asset>; models come as .gltf + .bin + textures.
The glTF packages ship the base colour as a JPEG without alpha; the cut-out of leaves and grass blades is a
separate map ("Alpha", "leaves_alpha"), fetched as PNG into the model's textures folder for prepare_foliage.py."""
import json, os, sys, urllib.request

out = sys.argv[1]
res = sys.argv[2] if len(sys.argv) > 2 else "2k"
MODELS = ["grass_medium_01", "grass_medium_02", "shrub_02", "shrub_03", "shrub_04", "island_tree_02", "jacaranda_tree",
          "tree_small_02", "rock_07", "rock_09", "boulder_01"]
TEXTURES = ["asphalt_02", "aerial_grass_rock", "brown_mud_dry", "grass_path_2", "dirt_floor"]
HEADERS = {"User-Agent": "GrudgeLap feasibility scene (CC0 assets)"}


def get(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers=HEADERS), timeout=120) as r:
        return r.read()


def save(url, path):
    if os.path.exists(path) and os.path.getsize(path) > 0:
        return
    os.makedirs(os.path.dirname(path), exist_ok=True)
    data = get(url)
    with open(path, "wb") as f:
        f.write(data)
    print(f"  {os.path.relpath(path, out)}  {len(data) // 1024} KB")


for asset in MODELS:
    files = json.loads(get(f"https://api.polyhaven.com/files/{asset}"))
    entry = files["gltf"][res]["gltf"]
    folder = os.path.join(out, "models", asset)
    save(entry["url"], os.path.join(folder, os.path.basename(entry["url"])))
    for rel, inc in entry.get("include", {}).items():
        save(inc["url"], os.path.join(folder, rel))
    for kind in (k for k in files if k.lower().endswith("alpha") and res in files[k] and "png" in files[k][res]):
        url = files[kind][res]["png"]["url"]
        save(url, os.path.join(folder, "textures", os.path.basename(url)))
    print(asset, "ok")

for asset in TEXTURES:
    files = json.loads(get(f"https://api.polyhaven.com/files/{asset}"))
    folder = os.path.join(out, "textures", asset)
    for kind in ["Diffuse", "nor_gl", "Rough", "AO", "Displacement", "arm"]:
        if kind in files and res in files[kind]:
            fmt = "jpg" if "jpg" in files[kind][res] else next(iter(files[kind][res]))
            url = files[kind][res][fmt]["url"]
            save(url, os.path.join(folder, os.path.basename(url)))
    print(asset, "ok")

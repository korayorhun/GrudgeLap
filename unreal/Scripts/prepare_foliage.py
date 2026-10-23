"""Writes <model>_ue.gltf beside each Poly Haven model, with cut-out foliage the importer can use.

Usage: python prepare_foliage.py <Import/PolyHaven dir>

Poly Haven's glTF packages give leaves and grass blades a JPEG base colour without alpha and mark the material as
alpha-blended; the cut-out is a separate map (<material>_alpha_2k.png, fetched by fetch_polyhaven.py). Blended
leaves cannot be Nanite and a meadow of them sorts badly, and without the alpha every card draws as a solid
rectangle. So each such material gets an RGBA PNG (base colour + alpha) and alpha testing (MASK) instead.
Existing PNGs are kept; the .gltf copies are rewritten every run.
"""
import json
import os
import sys

from PIL import Image

CUTOFF = 0.5


def rgba(folder, colour_uri, alpha_name):
    """The base colour with the alpha map in its alpha channel, as textures/<colour>_alpha.png."""
    target_uri = os.path.splitext(colour_uri)[0] + "_alpha.png"
    target = os.path.join(folder, target_uri)
    if not os.path.exists(target):
        colour = Image.open(os.path.join(folder, colour_uri)).convert("RGB")
        alpha = Image.open(os.path.join(folder, "textures", alpha_name)).convert("L")
        if alpha.size != colour.size:
            alpha = alpha.resize(colour.size, Image.LANCZOS)
        colour.putalpha(alpha)
        colour.save(target, optimize=False)
    return target_uri


def prepare(folder):
    source = next((f for f in os.listdir(folder) if f.endswith(".gltf") and not f.endswith("_ue.gltf")), None)
    if source is None:
        return None
    doc = json.load(open(os.path.join(folder, source), encoding="utf-8"))
    cut = []
    for material in doc.get("materials", []):
        alpha_name = f"{material.get('name')}_alpha_2k.png"
        texture = material.get("pbrMetallicRoughness", {}).get("baseColorTexture")
        if texture is None or not os.path.exists(os.path.join(folder, "textures", alpha_name)):
            continue
        old = doc["textures"][texture["index"]]
        doc["images"].append({"uri": rgba(folder, doc["images"][old["source"]]["uri"], alpha_name)})
        doc["textures"].append({key: value for key, value in old.items() if key != "source"} | {"source": len(doc["images"]) - 1})
        texture["index"] = len(doc["textures"]) - 1
        material["alphaMode"] = "MASK"
        material["alphaCutoff"] = CUTOFF
        cut.append(material.get("name"))
    target = os.path.join(folder, source.replace(".gltf", "_ue.gltf"))
    json.dump(doc, open(target, "w", encoding="utf-8"))
    return os.path.basename(target), cut


def main():
    models = os.path.join(sys.argv[1], "models")
    for name in sorted(os.listdir(models)):
        result = prepare(os.path.join(models, name))
        if result:
            print(f"{name}: {result[0]} cut-out {result[1]}")


main()

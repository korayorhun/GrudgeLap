"""Copies one node and everything under it out of a glTF binary into a file of its own.

Usage: python extract_car.py <in.glb> <out.glb> [node name, default PlayerCar]

The game exports a whole world per car (--car=v8 --export-world=...); the level takes its track from the first
export and only the car from the others. The copy keeps the node's place in the world and carries only what the
subtree uses: its meshes, their materials, textures, images and samplers, and the bytes of their accessors, so
the importer has nothing else to translate.
"""
import json
import struct
import sys

TEXTURE_KEYS = ("baseColorTexture", "metallicRoughnessTexture", "normalTexture", "occlusionTexture", "emissiveTexture")


def load(path):
    with open(path, "rb") as f:
        _, _, _ = struct.unpack("<III", f.read(12))
        length, _ = struct.unpack("<II", f.read(8))
        doc = json.loads(f.read(length))
        bin_length, _ = struct.unpack("<II", f.read(8))
        blob = f.read(bin_length)
    return doc, blob


def texture_refs(material):
    """The texture-info dictionaries of a material, wherever the core spec puts them."""
    found = [material[k] for k in TEXTURE_KEYS if k in material]
    pbr = material.get("pbrMetallicRoughness", {})
    found += [pbr[k] for k in TEXTURE_KEYS if k in pbr]
    return found


def main():
    source, target = sys.argv[1:3]
    name = sys.argv[3] if len(sys.argv) > 3 else "PlayerCar"
    doc, blob = load(source)
    nodes = doc["nodes"]
    root = next(i for i, node in enumerate(nodes) if node.get("name") == name)

    order = []
    stack = [root]
    while stack:
        index = stack.pop()
        order.append(index)
        stack.extend(reversed(nodes[index].get("children", [])))
    node_map = {old: new for new, old in enumerate(order)}

    mesh_map, material_map, texture_map, image_map, sampler_map, accessor_map, view_map = {}, {}, {}, {}, {}, {}, {}

    def remap(table, old):
        if old not in table:
            table[old] = len(table)
        return table[old]

    new_nodes = []
    for old in order:
        node = dict(nodes[old])
        node.pop("skin", None)
        node.pop("camera", None)
        if "children" in node:
            node["children"] = [node_map[c] for c in node["children"]]
        if "mesh" in node:
            node["mesh"] = remap(mesh_map, node["mesh"])
        new_nodes.append(node)

    new_meshes = [None] * len(mesh_map)
    for old, new in mesh_map.items():
        mesh = json.loads(json.dumps(doc["meshes"][old]))
        for primitive in mesh["primitives"]:
            primitive["attributes"] = {k: remap(accessor_map, v) for k, v in primitive["attributes"].items()}
            if "indices" in primitive:
                primitive["indices"] = remap(accessor_map, primitive["indices"])
            if "material" in primitive:
                primitive["material"] = remap(material_map, primitive["material"])
            primitive.pop("targets", None)
        new_meshes[new] = mesh

    new_materials = [None] * len(material_map)
    for old, new in material_map.items():
        material = json.loads(json.dumps(doc["materials"][old]))
        for ref in texture_refs(material):
            ref["index"] = remap(texture_map, ref["index"])
        new_materials[new] = material

    new_textures = [None] * len(texture_map)
    for old, new in texture_map.items():
        texture = dict(doc["textures"][old])
        if "source" in texture:
            texture["source"] = remap(image_map, texture["source"])
        if "sampler" in texture:
            texture["sampler"] = remap(sampler_map, texture["sampler"])
        new_textures[new] = texture

    new_images = [None] * len(image_map)
    for old, new in image_map.items():
        image = dict(doc["images"][old])
        if "bufferView" in image:
            image["bufferView"] = remap(view_map, image["bufferView"])
        new_images[new] = image

    new_accessors = [None] * len(accessor_map)
    for old, new in accessor_map.items():
        accessor = dict(doc["accessors"][old])
        accessor.pop("sparse", None)
        if "bufferView" in accessor:
            accessor["bufferView"] = remap(view_map, accessor["bufferView"])
        new_accessors[new] = accessor

    # The bytes of every buffer view still used, each aligned to 8 bytes in the new buffer.
    out = bytearray()
    new_views = [None] * len(view_map)
    for old, new in sorted(view_map.items(), key=lambda kv: kv[1]):
        view = dict(doc["bufferViews"][old])
        start = view.get("byteOffset", 0)
        out += b"\0" * (-len(out) % 8)
        view["byteOffset"] = len(out)
        view["buffer"] = 0
        out += blob[start:start + view["byteLength"]]
        new_views[new] = view
    out += b"\0" * (-len(out) % 4)

    result = {"asset": doc["asset"], "scene": 0, "scenes": [{"nodes": [0]}], "nodes": new_nodes, "meshes": new_meshes,
              "accessors": new_accessors, "bufferViews": new_views, "buffers": [{"byteLength": len(out)}]}
    for key, value in (("materials", new_materials), ("textures", new_textures), ("images", new_images),
                       ("samplers", [doc["samplers"][old] for old, _ in sorted(sampler_map.items(), key=lambda kv: kv[1])])):
        if value:
            result[key] = value
    for key in ("extensionsUsed", "extensionsRequired"):
        if key in doc:
            result[key] = doc[key]

    chunk = json.dumps(result, separators=(",", ":")).encode("utf-8")
    chunk += b" " * (-len(chunk) % 4)
    with open(target, "wb") as f:
        f.write(struct.pack("<III", 0x46546C67, 2, 12 + 8 + len(chunk) + 8 + len(out)))
        f.write(struct.pack("<II", len(chunk), 0x4E4F534A))
        f.write(chunk)
        f.write(struct.pack("<II", len(out), 0x004E4942))
        f.write(out)
    print(f"{name}: {len(new_nodes)} nodes, {len(new_meshes)} meshes, {len(new_materials)} materials, "
          f"{len(new_images)} images, {len(out) // 1024} KiB -> {target}")


main()

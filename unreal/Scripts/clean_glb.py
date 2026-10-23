"""Makes the game's glTF export importable by Unreal: Usage: python clean_glb.py <in.glb> <out.glb>

1. Godot marks KHR_node_visibility as a required extension as soon as one node is hidden, and Unreal's Interchange
   refuses a file with a required extension it does not support. The extension is dropped; hidden nodes are
   unlinked from the hierarchy, as the game showed them (at export time: the train between its runs), except the
   car's glass: the cockpit view hides it, but build_scene.py gives it a clear glass every view can keep.
2. Some primitives come out wound clockwise against their own normals (the terrain, the railings, some props: 101
   of 1202 in the export of 2026-10-22). glTF culls the clockwise side of a single-sided material, so Unreal drew
   no terrain at all from above. Where most triangles disagree with their vertex normals, the index order is
   reversed in place; the binary chunk keeps its size and layout, so every other accessor stays valid.
"""
import json
import struct
import sys

import numpy as np

COMPONENTS = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}
TYPES = {5126: np.float32, 5125: np.uint32, 5123: np.uint16, 5121: np.uint8}


def view(doc, blob, index):
    """A writable numpy view of an accessor's elements in the binary chunk (rows, components)."""
    accessor = doc["accessors"][index]
    buffer_view = doc["bufferViews"][accessor["bufferView"]]
    dtype = np.dtype(TYPES[accessor["componentType"]])
    width = COMPONENTS[accessor["type"]]
    offset = buffer_view.get("byteOffset", 0) + accessor.get("byteOffset", 0)
    stride = buffer_view.get("byteStride", 0) or width * dtype.itemsize
    return np.lib.stride_tricks.as_strided(np.frombuffer(blob, dtype=dtype, offset=offset),
                                           shape=(accessor["count"], width), strides=(stride, dtype.itemsize))


KEEP_HIDDEN = {"Glass"}


def unhide(doc):
    hidden = {i for i, node in enumerate(doc["nodes"])
              if node.get("extensions", {}).get("KHR_node_visibility", {}).get("visible") is False
              and node.get("name") not in KEEP_HIDDEN}
    for node in doc["nodes"]:
        if "children" in node:
            node["children"] = [c for c in node["children"] if c not in hidden]
            if not node["children"]:
                del node["children"]
        node.get("extensions", {}).pop("KHR_node_visibility", None)
        if node.get("extensions") == {}:
            del node["extensions"]
    for scene in doc.get("scenes", []):
        scene["nodes"] = [n for n in scene["nodes"] if n not in hidden]
    for key in ("extensionsRequired", "extensionsUsed"):
        if key in doc:
            doc[key] = [e for e in doc[key] if e != "KHR_node_visibility"]
            if not doc[key]:
                del doc[key]
    return [doc["nodes"][i].get("name") for i in sorted(hidden)]


def rewind(doc, blob):
    flipped, done = [], set()
    for mesh in doc["meshes"]:
        for primitive in mesh["primitives"]:
            index = primitive.get("indices")
            attributes = primitive["attributes"]
            if index is None or index in done or "NORMAL" not in attributes or primitive.get("mode", 4) != 4:
                continue
            triangles = view(doc, blob, index).reshape(-1, 3)
            positions = view(doc, blob, attributes["POSITION"]).astype(np.float64)
            normals = view(doc, blob, attributes["NORMAL"]).astype(np.float64)
            a, b, c = (positions[triangles[:, k]] for k in range(3))
            agree = np.einsum("ij,ij->i", np.cross(b - a, c - a), normals[triangles].sum(axis=1)) > 0
            if agree.mean() < 0.1:
                triangles[:, [1, 2]] = triangles[:, [2, 1]].copy()
                flipped.append(mesh.get("name") or f"mesh {doc['meshes'].index(mesh)}")
            done.add(index)
    return flipped


def main():
    source, target = sys.argv[1:3]
    with open(source, "rb") as f:
        magic, version, _ = struct.unpack("<III", f.read(12))
        length, kind = struct.unpack("<II", f.read(8))
        doc = json.loads(f.read(length))
        bin_length, bin_kind = struct.unpack("<II", f.read(8))
        blob = bytearray(f.read(bin_length))
        tail = f.read()

    hidden = unhide(doc)
    flipped = rewind(doc, blob)

    chunk = json.dumps(doc, separators=(",", ":")).encode("utf-8")
    chunk += b" " * (-len(chunk) % 4)  # JSON chunks are padded with spaces to 4 bytes
    total = 12 + 8 + len(chunk) + 8 + len(blob) + len(tail)
    with open(target, "wb") as f:
        f.write(struct.pack("<III", magic, version, total))
        f.write(struct.pack("<II", len(chunk), kind))
        f.write(chunk)
        f.write(struct.pack("<II", len(blob), bin_kind))
        f.write(blob)
        f.write(tail)
    print(f"{len(hidden)} hidden node(s) unlinked {hidden}; {len(flipped)} primitive(s) rewound -> {target}")


main()

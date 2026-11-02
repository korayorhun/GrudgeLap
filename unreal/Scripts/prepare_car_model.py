"""Puts a real car model (a CC-licensed glTF, Import/Models) in place of the game's lofted body in an export.

Usage: python prepare_car_model.py <in.glb> <out.glb> <car name> [models.json]

The export's car (the node PlayerCar, in track_ue.glb or a car_<name>.glb from extract_car.py) keeps its root, its
cabin (dashboard, dials, steering column, seats, mirror) and its four wheel pivots (Wheel0..3 with a Spin child
each, the simulation's order: front left, front right, rear left, rear right); its lofted exterior (Shell, Bonnet,
Glass, both Outside nodes) and the lofted tyres and rims under the Spin nodes go. The model's body meshes come in
under a new node ModelShell (build_scene.py tags it GLP.Exterior; its glass, found by name or by a translucent
material, is named Mdl_Glass_* and given the clear glass), its wheel meshes under the Spin nodes, each centred on
its own hub, with the pivots moved to the model's hubs. The model is turned to face the game's -Z, scaled so that
its wheelbase is the simulation's (VehicleSpec: the axles' distances from the centre of gravity), and set down with
its hubs at the simulation's rest hub height, so the car the player drives is the car the player sees.

models.json names the model file of each catalogue car, its forward axis, its tyre nodes (or "auto" for the four
wheel-like meshes at the corners), the node-name substrings that stay with the body though they sit at a wheel
(calipers, springs) and the ones to drop (a second interior). A car without an entry passes through unchanged.
"""
import json
import math
import os
import site
import struct
import sys

sys.path.append(site.getusersitepackages())
import numpy as np

COMPONENTS = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}
TYPES = {5126: np.float32, 5125: np.uint32, 5123: np.uint16, 5121: np.uint8, 5122: np.int16, 5120: np.int8}
GRAVITY = 9.81

# The simulation's geometry per catalogue car (physics/Vehicle/VehicleSpec.cs): the axles' distances from the body's
# reference point (the car node's origin; VehicleSpec.CentreOfMassOffset puts the centre of gravity from it), the half
# track, and what the rest hub height is made of (the hub plane below the reference point, the suspension's
# travel at full droop, the mass on four springs).
SPECS = {
    "flat12": dict(front=1.27, rear=1.28, track=0.78, hub=-0.12, rest=0.26, mass=1580.0, stiffness=38000.0, radius=0.33),
    "v8": dict(front=1.32, rear=1.33, track=0.80, hub=-0.12, rest=0.26, mass=1485.0, stiffness=46000.0, radius=0.33),
    "hothatch": dict(front=0.931, rear=1.489, track=0.68, hub=-0.20, rest=0.22, mass=985.0, stiffness=25920.0, radius=0.292),
    "five": dict(front=0.928, rear=1.552, track=0.75, hub=-0.20, rest=0.23, mass=1375.0, stiffness=31490.0, radius=0.293),
    "turbohatch": dict(front=0.956, rear=1.562, track=0.75, hub=-0.20, rest=0.22, mass=1550.0, stiffness=39800.0, radius=0.319),
    "saloon": dict(front=1.17, rear=1.32, track=0.678, hub=-0.20, rest=0.25, mass=1050.0, stiffness=24000.0, radius=0.2875),
    "estate": dict(front=1.00, rear=1.441, track=0.656, hub=-0.20, rest=0.26, mass=1045.0, stiffness=22000.0, radius=0.289),
    "fleet": dict(front=1.121, rear=1.753, track=0.80, hub=-0.20, rest=0.24, mass=1770.0, stiffness=45385.0, radius=0.326),
}


def load(path):
    with open(path, "rb") as f:
        f.read(12)
        length, _ = struct.unpack("<II", f.read(8))
        doc = json.loads(f.read(length))
        bin_length, _ = struct.unpack("<II", f.read(8))
        blob = f.read(bin_length)
    return doc, bytearray(blob)


def save(path, doc, blob):
    blob += b"\0" * (-len(blob) % 4)
    doc["buffers"] = [{"byteLength": len(blob)}]
    chunk = json.dumps(doc, separators=(",", ":")).encode("utf-8")
    chunk += b" " * (-len(chunk) % 4)
    with open(path, "wb") as f:
        f.write(struct.pack("<III", 0x46546C67, 2, 12 + 8 + len(chunk) + 8 + len(blob)))
        f.write(struct.pack("<II", len(chunk), 0x4E4F534A))
        f.write(chunk)
        f.write(struct.pack("<II", len(blob), 0x004E4942))
        f.write(blob)


def node_matrix(node):
    if "matrix" in node:
        return np.array(node["matrix"], dtype=float).reshape(4, 4).T
    t = node.get("translation", [0.0, 0.0, 0.0])
    x, y, z, w = node.get("rotation", [0.0, 0.0, 0.0, 1.0])
    s = node.get("scale", [1.0, 1.0, 1.0])
    r = np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                  [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                  [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])
    m = np.eye(4)
    m[:3, :3] = r * np.array(s)
    m[:3, 3] = t
    return m


def world_matrices(doc):
    """World matrix of every node reachable from the scenes, and each node's parent."""
    worlds = {}
    parents = {}
    roots = [n for scene in doc.get("scenes", []) for n in scene.get("nodes", [])]
    stack = [(r, np.eye(4), None) for r in roots]
    while stack:
        index, parent_matrix, parent = stack.pop()
        world = parent_matrix @ node_matrix(doc["nodes"][index])
        worlds[index] = world
        parents[index] = parent
        for child in doc["nodes"][index].get("children", []):
            stack.append((child, world, index))
    return worlds, parents


def mesh_bounds(doc, mesh_index, world):
    """World bounding box of a mesh from its POSITION accessors' min/max (every corner transformed)."""
    low = np.full(3, np.inf)
    high = np.full(3, -np.inf)
    for primitive in doc["meshes"][mesh_index]["primitives"]:
        accessor = doc["accessors"][primitive["attributes"]["POSITION"]]
        if "min" not in accessor or "max" not in accessor:
            continue
        lo, hi = np.array(accessor["min"], dtype=float), np.array(accessor["max"], dtype=float)
        for corner in ((lo[0], lo[1], lo[2]), (hi[0], lo[1], lo[2]), (lo[0], hi[1], lo[2]), (hi[0], hi[1], lo[2]),
                       (lo[0], lo[1], hi[2]), (hi[0], lo[1], hi[2]), (lo[0], hi[1], hi[2]), (hi[0], hi[1], hi[2])):
            p = world @ np.array([*corner, 1.0])
            low = np.minimum(low, p[:3])
            high = np.maximum(high, p[:3])
    return low, high


def is_glass_material(material):
    if material.get("alphaMode") == "BLEND":
        base = material.get("pbrMetallicRoughness", {}).get("baseColorFactor", [1, 1, 1, 1])
        if len(base) == 4 and base[3] < 0.85:
            return True
    extensions = material.get("extensions", {})
    return "KHR_materials_transmission" in extensions


def merge(base, base_blob, model, model_blob):
    """Appends the model's meshes, materials, textures, images, samplers, accessors and buffer views (and its binary
    chunk) to the base document; returns the index offsets. Skins, animations and cameras are left behind."""
    base_blob += b"\0" * (-len(base_blob) % 8)
    blob_offset = len(base_blob)
    base_blob += model_blob
    offsets = {key: len(base.get(key, [])) for key in ("meshes", "materials", "textures", "images", "samplers", "accessors", "bufferViews")}
    for view in model.get("bufferViews", []):
        view = dict(view)
        view["buffer"] = 0
        view["byteOffset"] = view.get("byteOffset", 0) + blob_offset
        base.setdefault("bufferViews", []).append(view)
    for accessor in model.get("accessors", []):
        accessor = dict(accessor)
        accessor.pop("sparse", None)
        if "bufferView" in accessor:
            accessor["bufferView"] += offsets["bufferViews"]
        base.setdefault("accessors", []).append(accessor)
    for sampler in model.get("samplers", []):
        base.setdefault("samplers", []).append(dict(sampler))
    for image in model.get("images", []):
        image = dict(image)
        if "bufferView" in image:
            image["bufferView"] += offsets["bufferViews"]
        base.setdefault("images", []).append(image)
    for texture in model.get("textures", []):
        texture = dict(texture)
        if "source" in texture:
            texture["source"] += offsets["images"]
        if "sampler" in texture:
            texture["sampler"] += offsets["samplers"]
        # Texture extensions (KHR_texture_basisu, EXT_texture_webp) point at images too.
        for extension in texture.get("extensions", {}).values():
            if isinstance(extension, dict) and "source" in extension:
                extension["source"] += offsets["images"]
        base.setdefault("textures", []).append(texture)

    def shift_textures(obj):
        if isinstance(obj, dict):
            if "index" in obj and "texCoord" in obj or (set(obj) & {"index"} and "scale" in obj) or (set(obj) & {"index"} and "strength" in obj):
                obj["index"] += offsets["textures"]
            elif "index" in obj and len(obj) == 1:
                obj["index"] += offsets["textures"]
            for value in obj.values():
                shift_textures(value)
        elif isinstance(obj, list):
            for value in obj:
                shift_textures(value)

    for material in model.get("materials", []):
        material = json.loads(json.dumps(material))
        shift_textures(material)
        base.setdefault("materials", []).append(material)
    for mesh in model.get("meshes", []):
        mesh = json.loads(json.dumps(mesh))
        for primitive in mesh["primitives"]:
            primitive["attributes"] = {k: v + offsets["accessors"] for k, v in primitive["attributes"].items()}
            if "indices" in primitive:
                primitive["indices"] += offsets["accessors"]
            if "material" in primitive:
                primitive["material"] += offsets["materials"]
            primitive.pop("targets", None)
        base.setdefault("meshes", []).append(mesh)
    used = set(base.get("extensionsUsed", [])) | set(model.get("extensionsUsed", []))
    if used:
        base["extensionsUsed"] = sorted(used)
    # Nothing is required: an importer without an extension draws the model without it rather than refusing the file.
    base.pop("extensionsRequired", None)
    return offsets


def find_node(doc, name):
    return next(i for i, node in enumerate(doc["nodes"]) if node.get("name") == name)


def subtree(doc, index):
    out = []
    stack = [index]
    while stack:
        i = stack.pop()
        out.append(i)
        stack.extend(doc["nodes"][i].get("children", []))
    return out


def main():
    source, target, car = sys.argv[1:4]
    config_path = sys.argv[4] if len(sys.argv) > 4 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Import", "Models", "models.json")
    config = json.load(open(config_path, encoding="utf-8")) if os.path.exists(config_path) else {}
    entry = config.get(car)
    doc, blob = load(source)
    if entry is None or not os.path.exists(os.path.join(os.path.dirname(config_path), entry["file"])):
        if source != target:
            save(target, doc, blob)
        print(f"{car}: no model configured, the lofted body stays")
        return
    spec = SPECS[car]
    model, model_blob = load(os.path.join(os.path.dirname(config_path), entry["file"]))
    # The rigging is shared with the game's own preparation (tools/car_models.py): models lying along x, wheels kept
    # in one mesh per axle (split), unlit exports and node selectors (#index, =name) work the same in both.
    sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "tools"))
    import car_models as cm
    if entry.get("split"):
        cm.split_nodes(model, model_blob, [s.lower() for s in entry["split"]])
    cm.strip_unlit(model)
    r = cm.rig(model, entry, spec, car)
    worlds, nodes, name_of = r["worlds"], model["nodes"], r["name_of"]
    tyres, assemblies, pivots, body_nodes, dropped, to_car = r["tyres"], r["assemblies"], r["pivots"], r["body_nodes"], r["dropped"], r["to_car"]
    print(f"{car}: {entry['file']} wheelbase {r['wheelbase']:.3f} -> scale {r['scale']:.3f} ({r['length']:.2f} m long); hubs at y={r['hub_y']:.3f}; "
          f"tyres {[name_of(i) for i in tyres]}; {len(body_nodes)} body node(s), {sum(len(a) - 1 for a in assemblies)} wheel part(s), {len(dropped)} dropped")

    # The export's car: its lofted exterior and tyres go, the model comes in.
    root = find_node(doc, "PlayerCar")
    car_nodes = subtree(doc, root)
    for i in car_nodes:
        node = doc["nodes"][i]
        if "children" in node:
            node["children"] = [c for c in node["children"] if doc["nodes"][c].get("name") not in ("Shell", "Bonnet", "Glass", "Outside")]
            if not node["children"]:
                del node["children"]
    offsets = merge(doc, blob, model, model_blob)
    glass_names = [s.lower() for s in entry.get("glass", ["glass"])]
    glass_materials = {i for i, m in enumerate(model.get("materials", [])) if is_glass_material(m)}

    def is_glass(i):
        if any(s in name_of(i).lower() for s in glass_names):
            return True
        return all(p.get("material") in glass_materials for p in model["meshes"][nodes[i]["mesh"]]["primitives"])

    def new_node(name, matrix, mesh=None):
        node = {"name": name, "matrix": [float(v) for v in matrix.T.flatten()]}
        if mesh is not None:
            node["mesh"] = mesh + offsets["meshes"]
        doc["nodes"].append(node)
        return len(doc["nodes"]) - 1

    shell = new_node("ModelShell", np.eye(4))
    doc["nodes"][root].setdefault("children", []).append(shell)
    doc["nodes"][shell]["children"] = []
    for i in body_nodes:
        prefix = "Mdl_Glass_" if is_glass(i) else "Mdl_"
        doc["nodes"][shell]["children"].append(new_node(prefix + name_of(i)[:48], to_car @ worlds[i], nodes[i]["mesh"]))
    for w in range(4):
        wheel = next(i for i in car_nodes if doc["nodes"][i].get("name") == f"Wheel{w}")
        spin = next(c for c in doc["nodes"][wheel].get("children", []) if doc["nodes"][c].get("name", "").startswith("Spin"))
        pivot = to_car @ np.array([*pivots[w], 1.0])
        doc["nodes"][wheel].pop("matrix", None)
        doc["nodes"][wheel]["translation"] = [float(v) for v in pivot[:3]]
        recentre = np.eye(4)
        recentre[:3, 3] = -pivot[:3]
        doc["nodes"][spin]["children"] = [new_node("Mdl_Wheel_" + name_of(i)[:40], recentre @ to_car @ worlds[i], nodes[i]["mesh"]) for i in assemblies[w]]
    save(target, doc, blob)
    print(f"-> {target}")


main()

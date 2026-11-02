"""Shared by the two car model preparations: the game's (tools/prepare_car_models.py) and the Unreal level's
(unreal/Scripts/prepare_car_model.py).

A real car model (a CC-licensed glTF, unreal/Import/Models, fetched by unreal/Scripts/fetch_models.py) is rigged
here the same way for both: its four tyres found (by name, or as the four wheel-like meshes at the corners), each
with what rolls with it (rims, discs, caps), and the transform that puts it where the simulation's car is: turned
to face the game's -Z, scaled so that its wheelbase is the simulation's, set down with its hubs at the rest hub
height, in the car node's frame (origin at the centre of gravity).
"""
import json
import math
import struct

import numpy as np

COMPONENTS = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}
TYPES = {5126: np.float32, 5125: np.uint32, 5123: np.uint16, 5121: np.uint8, 5122: np.int16, 5120: np.int8}
GRAVITY = 9.81

# The simulation's geometry per catalogue car (physics/Vehicle/VehicleSpec.cs): the axles' distances from the body's
# reference point (the car node's origin; VehicleSpec.CentreOfMassOffset puts the centre of gravity from it), the half
# track, and what the rest hub height is made of (the hub plane below the reference point, the suspension's
# travel at full droop, the mass on four springs). The game checks its models against the simulation when it loads
# them (CarView logs a model whose hubs are off), so a change there that is not made here shows up.
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
        # A piece split off a mesh (split_nodes) shares its vertex list: its own box is kept in its extras.
        own = primitive.get("extras", {}).get("glp_bounds")
        if own is None and ("min" not in accessor or "max" not in accessor):
            continue
        lo, hi = (np.array(own[0], dtype=float), np.array(own[1], dtype=float)) if own else (np.array(accessor["min"], dtype=float), np.array(accessor["max"], dtype=float))
        for corner in ((lo[0], lo[1], lo[2]), (hi[0], lo[1], lo[2]), (lo[0], hi[1], lo[2]), (hi[0], hi[1], lo[2]),
                       (lo[0], lo[1], hi[2]), (hi[0], lo[1], hi[2]), (lo[0], hi[1], hi[2]), (hi[0], hi[1], hi[2])):
            p = world @ np.array([*corner, 1.0])
            low = np.minimum(low, p[:3])
            high = np.maximum(high, p[:3])
    return low, high


def accessor_array(doc, blob, index):
    """An accessor's values as a float array (count x components), honouring the buffer view's stride."""
    accessor = doc["accessors"][index]
    dtype = np.dtype(TYPES[accessor["componentType"]])
    components = COMPONENTS[accessor["type"]]
    count = accessor["count"]
    if "bufferView" not in accessor:
        return np.zeros((count, components))
    view = doc["bufferViews"][accessor["bufferView"]]
    offset = view.get("byteOffset", 0) + accessor.get("byteOffset", 0)
    stride = view.get("byteStride", 0) or dtype.itemsize * components
    values = np.ndarray(shape=(count, components), dtype=dtype, buffer=bytes(blob), offset=offset, strides=(stride, dtype.itemsize))
    values = values.astype(float)
    if accessor.get("normalized") and dtype.kind in "iu":
        values /= float(np.iinfo(dtype).max)
    return values


def mesh_points(doc, blob, mesh_index, matrix):
    """Every vertex of a mesh, transformed by matrix."""
    out = []
    for primitive in doc["meshes"][mesh_index]["primitives"]:
        p = accessor_array(doc, blob, primitive["attributes"]["POSITION"])
        if "indices" in primitive:
            # Only the vertices its triangles use: a piece split off a mesh (split_nodes) shares the whole vertex list.
            p = p[np.unique(accessor_array(doc, blob, primitive["indices"]).astype(np.int64).reshape(-1))]
        out.append((np.c_[p, np.ones(len(p))] @ matrix.T)[:, :3])
    return np.vstack(out) if out else np.zeros((0, 3))


def matches(index, name, words):
    """A node picked by a model's entry: "#12" is node 12 of the source file, "=Object_10" that name exactly, and
    anything else a part of the name (all without regard to case). Many models name every node alike
    ("defaultMaterial") or number them (Object_10, Object_100), so a part of a name is not always enough."""
    lower = name.lower()
    for word in words:
        if word.startswith("#") and word[1:].isdigit():
            if word[1:] == str(index) or lower.endswith("@" + word[1:]):
                return True
        elif word.startswith("="):
            if lower == word[1:]:
                return True
        elif word in lower:
            return True
    return False


def split_nodes(doc, blob, words):
    """Splits the meshes of the picked nodes by the corner each triangle lies in (left or right of the model's middle,
    ahead of or behind it), so that one mesh holding the rims or the tyres of an axle, or of the whole car, becomes a
    part per wheel. The pieces replace the node under its parent, named after it with the corner (and its index after
    an @, so #-selectors still find them)."""
    worlds, parents = world_matrices(doc)
    mesh_nodes = [i for i in worlds if "mesh" in doc["nodes"][i]]
    lows, highs = [], []
    for i in mesh_nodes:
        lo, hi = mesh_bounds(doc, doc["nodes"][i]["mesh"], worlds[i])
        lows.append(np.minimum(lo, hi))
        highs.append(np.maximum(lo, hi))
    middle = (np.min(lows, axis=0) + np.max(highs, axis=0)) / 2
    split = 0
    for i in list(mesh_nodes):
        node = doc["nodes"][i]
        if not matches(i, node.get("name", ""), words):
            continue
        mesh = doc["meshes"][node["mesh"]]
        groups = {}
        for primitive in mesh["primitives"]:
            positions = accessor_array(doc, blob, primitive["attributes"]["POSITION"])
            p = (np.c_[positions, np.ones(len(positions))] @ worlds[i].T)[:, :3]
            if "indices" in primitive:
                indices = accessor_array(doc, blob, primitive["indices"]).astype(np.int64).reshape(-1)
            else:
                indices = np.arange(len(positions))
            triangles = indices[: len(indices) // 3 * 3].reshape(-1, 3)
            c = p[triangles].mean(axis=1)
            keys = (c[:, 0] > middle[0]).astype(int) * 2 + (c[:, 2] > middle[2]).astype(int)
            for key in np.unique(keys):
                chosen = triangles[keys == key].reshape(-1).astype(np.uint32)
                data = chosen.tobytes()
                offset = len(blob) + (-len(blob) % 4)
                blob += b"\0" * (offset - len(blob))
                blob += data
                doc["bufferViews"].append({"buffer": 0, "byteOffset": offset, "byteLength": len(data), "target": 34963})
                doc["accessors"].append({"bufferView": len(doc["bufferViews"]) - 1, "componentType": 5125, "count": int(len(chosen)), "type": "SCALAR"})
                part = dict(primitive)
                part["indices"] = len(doc["accessors"]) - 1
                used = positions[chosen]
                part["extras"] = {"glp_bounds": [[float(v) for v in used.min(axis=0)], [float(v) for v in used.max(axis=0)]]}
                groups.setdefault(int(key), []).append(part)
        if len(groups) < 2:
            continue
        names = {0: "RL", 1: "FL", 2: "RR", 3: "FR"}   # +x, +z: the glTF model's own axes, named for a model facing +z
        pieces = []
        for key, primitives in sorted(groups.items()):
            doc["meshes"].append({"name": f"{mesh.get('name', 'mesh')}_{names[key]}", "primitives": primitives})
            piece = {k: v for k, v in node.items() if k not in ("children", "mesh")}
            piece["name"] = f"{node.get('name', 'node')}_{names[key]}@{i}"
            piece["mesh"] = len(doc["meshes"]) - 1
            doc["nodes"].append(piece)
            pieces.append(len(doc["nodes"]) - 1)
        parent = parents.get(i)
        siblings = doc["nodes"][parent]["children"] if parent is not None else next(s["nodes"] for s in doc["scenes"] if i in s["nodes"])
        at = siblings.index(i)
        siblings[at:at + 1] = pieces
        split += 1
    return split


def strip_unlit(doc):
    """A model drawn unlit (KHR_materials_unlit, as some scans and game-ready exports are) takes the game's light."""
    count = 0
    for material in doc.get("materials", []):
        if material.get("extensions", {}).pop("KHR_materials_unlit", None) is not None:
            count += 1
    for key in ("extensionsUsed", "extensionsRequired"):
        if key in doc:
            doc[key] = [e for e in doc[key] if e != "KHR_materials_unlit"]
    return count


def merge(base, base_blob, model, model_blob):
    """Appends a second model's meshes, materials, textures, images, samplers, accessors and buffer views (and its
    binary chunk) to the first; returns the index offsets. Its nodes are left to the caller."""
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
        for extension in texture.get("extensions", {}).values():
            if isinstance(extension, dict) and "source" in extension:
                extension["source"] += offsets["images"]
        base.setdefault("textures", []).append(texture)

    def shift_textures(obj, key=""):
        if isinstance(obj, dict):
            if key.lower().endswith("texture") and "index" in obj:
                obj["index"] += offsets["textures"]
            for k, value in obj.items():
                shift_textures(value, k)
        elif isinstance(obj, list):
            for value in obj:
                shift_textures(value, key)

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
    base.pop("extensionsRequired", None)
    return offsets


def is_glass_material(material):
    if material.get("alphaMode") == "BLEND":
        base = material.get("pbrMetallicRoughness", {}).get("baseColorFactor", [1, 1, 1, 1])
        if len(base) == 4 and base[3] < 0.85:
            return True
    extensions = material.get("extensions", {})
    return "KHR_materials_transmission" in extensions


def rig(model, entry, spec, car):
    """Finds the model's tyres and what rolls with them, and the transform into the game's car frame.

    Returns a dict: worlds (node world matrices), mesh_nodes (kept mesh nodes), bounds (world boxes), dropped,
    tyres (FL, FR, RL, RR), assemblies (per wheel: the tyre and what rolls with it), pivots (hub centres, model
    space), body_nodes (the rest), to_car (4x4), and the measures printed for the log.
    """
    worlds, _ = world_matrices(model)
    # A model lying along x is turned about y first so it faces +z, then rigged as one that does.
    axis = entry.get("forward", "+z")
    if axis in ("+x", "-x"):
        angle = -np.pi / 2 if axis == "+x" else np.pi / 2
        turn_y = np.eye(4)
        turn_y[0, 0] = turn_y[2, 2] = np.cos(angle)
        turn_y[0, 2] = np.sin(angle)
        turn_y[2, 0] = -np.sin(angle)
        worlds = {i: turn_y @ w for i, w in worlds.items()}
    nodes = model["nodes"]
    mesh_nodes = [i for i in worlds if "mesh" in nodes[i]]
    bounds = {i: mesh_bounds(model, nodes[i]["mesh"], worlds[i]) for i in mesh_nodes}
    name_of = lambda i: nodes[i].get("name", f"node{i}")
    drop = [s.lower() for s in entry.get("drop", [])]
    fixed = [s.lower() for s in entry.get("fixed", [])]
    dropped = {i for i in mesh_nodes if matches(i, name_of(i), drop)}
    mesh_nodes = [i for i in mesh_nodes if i not in dropped]

    # The tyres: the named nodes, or the four wheel-like meshes (two equal extents, a smaller third, low) nearest the corners.
    forward = -1.0 if entry.get("forward", "+z") == "-z" else 1.0
    if entry.get("tyres") == "auto" or "tyres" not in entry:
        candidates = []
        for i in mesh_nodes:
            size = bounds[i][1] - bounds[i][0]
            order = np.sort(size)
            if order[2] > 0 and order[1] / order[2] > 0.93 and order[0] / order[2] < 0.6 and order[2] > 0.08 * max(bounds[j][1][2] - bounds[j][0][2] for j in mesh_nodes):
                candidates.append(i)
        tyre_nodes = candidates
    else:
        # A tyre by its name or the start of it, or by a selector (#index, =exact name).
        tyre_nodes = [i for i in mesh_nodes if any(matches(i, name_of(i), [n.lower()]) if n.startswith("=") or (n.startswith("#") and n[1:].isdigit()) else (name_of(i) == n or name_of(i).startswith(n)) for n in entry["tyres"])]
    centres = {i: (bounds[i][0] + bounds[i][1]) * 0.5 for i in tyre_nodes}
    if len(tyre_nodes) < 4:
        raise SystemExit(f"{car}: only {len(tyre_nodes)} tyre node(s) found: {[name_of(i) for i in tyre_nodes]}")
    # One tyre per corner, by position: front by the forward axis, left by x (right-handed glTF: left is +x when the
    # car faces +z, -x when it faces -z), the largest tyre of each corner's cluster.
    corners = {}
    for i in tyre_nodes:
        c = centres[i]
        front = (c[2] * forward) > np.mean([centres[j][2] * forward for j in tyre_nodes])
        left = (c[0] * forward) > 0
        key = (front, left)
        size = np.max(bounds[i][1] - bounds[i][0])
        if key not in corners or size > corners[key][1]:
            corners[key] = (i, size)
    order = [(True, True), (True, False), (False, True), (False, False)]   # FL, FR, RL, RR
    if any(k not in corners for k in order):
        raise SystemExit(f"{car}: tyres do not cover four corners: {[(k, name_of(v[0])) for k, v in corners.items()]}")
    tyres = [corners[k][0] for k in order]
    pivots = [centres[i] for i in tyres]
    # Everything that sits inside a wheel's reach and is no bigger than the tyre rolls with it (rims, discs, caps),
    # unless its name says it stays (calipers, springs).
    assemblies = [[i] for i in tyres]
    assigned = set(tyres)
    for i in mesh_nodes:
        if i in assigned:
            continue
        size = bounds[i][1] - bounds[i][0]
        centre = (bounds[i][0] + bounds[i][1]) * 0.5
        for w, pivot in enumerate(pivots):
            tyre_size = bounds[tyres[w]][1] - bounds[tyres[w]][0]
            reach = 0.5 * np.max(tyre_size)
            if np.linalg.norm(centre - pivot) < reach * 0.6 and np.max(size) <= np.max(tyre_size) * 1.05 and not matches(i, name_of(i), fixed):
                assemblies[w].append(i)
                assigned.add(i)
                break
    body_nodes = [i for i in mesh_nodes if i not in assigned]

    # The transform into the game's car frame: faced to -Z, scaled to the simulation's wheelbase, hubs at rest height.
    front_z = np.mean([pivots[0][2], pivots[1][2]])
    rear_z = np.mean([pivots[2][2], pivots[3][2]])
    wheelbase = abs(front_z - rear_z)
    scale = (spec["front"] + spec["rear"]) / wheelbase
    sag = spec["mass"] * GRAVITY / 4.0 / spec["stiffness"]
    hub_y = spec["hub"] - (spec["rest"] - sag)
    turn = np.eye(4)
    if forward > 0:
        turn[0, 0] = -1.0
        turn[2, 2] = -1.0
    scaling = np.diag([scale, scale, scale, 1.0])
    placed = scaling @ turn
    front_after = placed @ np.array([0.0, 0.0, front_z, 1.0])
    hub_after = placed @ np.array([0.0, np.mean([p[1] for p in pivots]), 0.0, 1.0])
    centre_x = placed @ np.array([np.mean([p[0] for p in pivots]), 0.0, 0.0, 1.0])
    translation = np.eye(4)
    translation[0, 3] = -centre_x[0]
    translation[1, 3] = hub_y - hub_after[1]
    translation[2, 3] = -spec["front"] - front_after[2]
    to_car = translation @ placed
    length = (max(b[1][2] for b in bounds.values()) - min(b[0][2] for b in bounds.values())) * scale
    tyre_radius = float(np.mean([0.5 * (bounds[i][1][1] - bounds[i][0][1]) for i in tyres])) * scale
    return dict(worlds=worlds, mesh_nodes=mesh_nodes, bounds=bounds, dropped=dropped, tyres=tyres, assemblies=assemblies,
                pivots=pivots, body_nodes=body_nodes, to_car=to_car, scale=scale, hub_y=hub_y, wheelbase=wheelbase,
                length=length, tyre_radius=tyre_radius, name_of=name_of)

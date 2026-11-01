"""Prepares the real car models the game draws in place of its lofted bodies (CarView.Model.cs):
game/assets/models/<car>.glb for every catalogue car that tools/car_models.json names, from the CC BY source files
in unreal/Import/Models (fetched by unreal/Scripts/fetch_models.py, each with its LICENSE.txt; credited in
THIRD_PARTY.md). The prepared files are build output (tools/build.ps1 runs this), not kept in the repository.

Usage: python tools/prepare_car_models.py [--inspect] [car ...]

Each file holds the model in the car node's frame (car_models.rig: faced to -Z, scaled to the simulation's
wheelbase, hubs at the rest height, origin at the centre of gravity), its scene:

  CarModel
    Body            every mesh that does not turn; the paint's materials renamed GLP_Paint, the windows' GLP_Glass
                    and clear lamp lenses GLP_Lens, for CarView to give them the game's own
    Wheel0..3       at the hubs, in the simulation's order (front left, front right, rear left, rear right), the
                    tyre and what rolls with it centred on each (CarView hangs them on its spinning pivots)
    Steering        at the steering wheel's centre, its +Y the column pointing at the driver; its child Spin holds
                    the wheel's meshes and turns about +Y (CarView.TurnSteeringWheel)
    Eye             the driver's eyes (CarView.Eye)
    Dials           the instrument panel's face: +Z towards the eyes, its x scale the face's width in metres
                    (CarView puts the live InstrumentCluster there)
    Mirror          the rear-view mirror's glass: +Z towards the driver (CarView hangs RearMirror's glass there)

A model without a cabin of its own ("interior": false) gets neither Steering, Eye, Dials nor Mirror: the game
builds its own cabin inside the model's shell. --inspect prints the meshes around the driver's seat with their
boxes in the car frame, for writing a model's entry.
"""
import json
import math
import os
import re
import site
import sys

sys.path.append(site.getusersitepackages())
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np

import car_models as cm

ROOT = os.environ.get("GLP_ROOT") or os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
SOURCE = os.path.join(ROOT, "unreal", "Import", "Models")
TARGET = os.path.join(ROOT, "game", "assets", "models")
CONFIG = os.path.join(ROOT, "tools", "car_models.json")

# Paint is found by whole words of a material's name: "lack" (German for paint) inside "Black" had every black part
# of a model painted in the body colour, and "Painted Black" is trim. A name that is only "body" with "m", "mat" or
# "color" around it (M_Body, mat_body, Body_MAT, Body Color) is the paint too.
PAINT_WORDS = {"paint", "pintura", "carrosserie", "lack", "carpaint", "bodypaint"}
BODY_WORDS = {"m", "mat", "material", "body", "color", "colour", "paint"}
GLASS_WORDS = ("glass", "vidrio", "window", "cristal", "windscreen", "windshield", "vitre", "glas")
LAMP_WORDS = ("light", "lamp", "luz", "luces", "faro", "optic", "lens", "indicator", "signal", "blinker")


def contains(name, words):
    name = name.lower()
    return any(w in name for w in words)


def is_paint(name):
    tokens = {w for w in re.split(r"[^a-z]+", name.lower()) if w}
    return bool(tokens & PAINT_WORDS) or ("body" in tokens and tokens <= BODY_WORDS)


def basis_facing(direction, up=(0.0, 1.0, 0.0)):
    """A rotation whose +Z is direction and whose +X stays level (towards the car's right for a face looking back)."""
    z = np.array(direction, dtype=float)
    z /= np.linalg.norm(z)
    x = np.cross(np.array(up, dtype=float), z)
    if np.linalg.norm(x) < 1e-6:
        x = np.array([1.0, 0.0, 0.0])
    x /= np.linalg.norm(x)
    y = np.cross(z, x)
    return np.column_stack([x, y, z])


def matrix(rotation=None, translation=(0.0, 0.0, 0.0), scale=(1.0, 1.0, 1.0)):
    m = np.eye(4)
    r = np.eye(3) if rotation is None else rotation
    m[:3, :3] = r @ np.diag(scale)
    m[:3, 3] = translation
    return m


def ray_down(model, blob, mesh_index, matrix_, x, z):
    """The heights where a vertical line at (x, z) meets a mesh's triangles (car frame)."""
    hits = []
    for primitive in model["meshes"][mesh_index]["primitives"]:
        if primitive.get("mode", 4) != 4:
            continue
        positions = cm.accessor_array(model, blob, primitive["attributes"]["POSITION"])
        p = (np.c_[positions, np.ones(len(positions))] @ matrix_.T)[:, :3]
        if "indices" in primitive:
            idx = cm.accessor_array(model, blob, primitive["indices"]).astype(np.int64).reshape(-1)
        else:
            idx = np.arange(len(p))
        tri = p[idx[: len(idx) // 3 * 3].reshape(-1, 3)]
        a, b, c = tri[:, 0], tri[:, 1], tri[:, 2]
        # Barycentric coordinates of (x, z) in each triangle's projection on the ground plane.
        v0x, v0z = b[:, 0] - a[:, 0], b[:, 2] - a[:, 2]
        v1x, v1z = c[:, 0] - a[:, 0], c[:, 2] - a[:, 2]
        v2x, v2z = x - a[:, 0], z - a[:, 2]
        den = v0x * v1z - v1x * v0z
        ok = np.abs(den) > 1e-12
        u = np.where(ok, (v2x * v1z - v1x * v2z) / np.where(ok, den, 1.0), -1.0)
        v = np.where(ok, (v0x * v2z - v2x * v0z) / np.where(ok, den, 1.0), -1.0)
        inside = ok & (u >= 0) & (v >= 0) & (u + v <= 1)
        y = a[:, 1] + u * (b[:, 1] - a[:, 1]) + v * (c[:, 1] - a[:, 1])
        hits.extend(float(h) for h in y[inside])
    return hits


def ray_hit(model, blob, mesh_index, matrix_, origin, direction):
    """The nearest distance along a ray (car frame) at which it meets a mesh's triangles, or None."""
    best = None
    for primitive in model["meshes"][mesh_index]["primitives"]:
        if primitive.get("mode", 4) != 4:
            continue
        positions = cm.accessor_array(model, blob, primitive["attributes"]["POSITION"])
        p = (np.c_[positions, np.ones(len(positions))] @ matrix_.T)[:, :3]
        if "indices" in primitive:
            idx = cm.accessor_array(model, blob, primitive["indices"]).astype(np.int64).reshape(-1)
        else:
            idx = np.arange(len(p))
        tri = p[idx[: len(idx) // 3 * 3].reshape(-1, 3)]
        # Moller-Trumbore, every triangle at once.
        e1 = tri[:, 1] - tri[:, 0]
        e2 = tri[:, 2] - tri[:, 0]
        h = np.cross(direction, e2)
        a = (e1 * h).sum(axis=1)
        ok = np.abs(a) > 1e-12
        f = np.where(ok, 1.0 / np.where(ok, a, 1.0), 0.0)
        s = origin - tri[:, 0]
        u = f * (s * h).sum(axis=1)
        q = np.cross(s, e1)
        v = f * (q * direction).sum(axis=1)
        dist = f * (e2 * q).sum(axis=1)
        hit = ok & (u >= 0) & (v >= 0) & (u + v <= 1) & (dist > 1e-4)
        if hit.any():
            nearest = float(dist[hit].min())
            best = nearest if best is None else min(best, nearest)
    return best


def node_world(nodes, index, parents):
    """A prepared node's matrix in the car frame (the prepared scene's root is the car frame)."""
    m = cm.node_matrix(nodes[index])
    parent = parents.get(index)
    while parent is not None:
        m = cm.node_matrix(nodes[parent]) @ m
        parent = parents.get(parent)
    return m


def append_accessor(model, blob, values, component_type, kind, target):
    """Appends an array to the binary chunk as a new accessor (and buffer view); returns its index."""
    data = values.tobytes()
    offset = len(blob) + (-len(blob) % 4)
    blob += b"\0" * (offset - len(blob))
    blob += data
    model["bufferViews"].append({"buffer": 0, "byteOffset": offset, "byteLength": len(data), "target": target})
    accessor = {"bufferView": len(model["bufferViews"]) - 1, "componentType": component_type, "count": int(len(values)), "type": kind}
    if kind == "VEC3" and component_type == 5126:
        accessor["min"] = [float(v) for v in values.min(axis=0)]
        accessor["max"] = [float(v) for v in values.max(axis=0)]
    model["accessors"].append(accessor)
    return len(model["accessors"]) - 1


def orient_paint(model, blob, nodes):
    """Turns painted parts that face into the car outwards: some models mirror a door or a wing without flipping
    it, and the paint, drawn from both faces with a dark lining behind (CarView.ModelPaint), showed that side's
    door as black. A part counts as turned when less than a third of its area faces away from the car's middle;
    its triangles are wound the other way and its normals (and tangents) reversed."""
    parents = {c: i for i, n in enumerate(nodes) for c in n.get("children", [])}
    paint = {i for i, m in enumerate(model["materials"]) if m.get("name") == "GLP_Paint"}
    middle = np.array([0.0, 0.1, 0.0])
    turned = []
    for index, node in enumerate(nodes):
        if "mesh" not in node:
            continue
        mesh = model["meshes"][node["mesh"]]
        if not any(q.get("material") in paint for q in mesh["primitives"]):
            continue
        world = node_world(nodes, index, parents)
        sign = np.sign(np.linalg.det(world[:3, :3]))
        primitives = []
        changed = False
        for primitive in mesh["primitives"]:
            if primitive.get("material") not in paint or primitive.get("mode", 4) != 4 or "NORMAL" not in primitive["attributes"]:
                primitives.append(primitive)
                continue
            positions = cm.accessor_array(model, blob, primitive["attributes"]["POSITION"])
            if "indices" in primitive:
                indices = cm.accessor_array(model, blob, primitive["indices"]).astype(np.int64).reshape(-1)
            else:
                indices = np.arange(len(positions))
            triangles = indices[: len(indices) // 3 * 3].reshape(-1, 3)
            p = (np.c_[positions, np.ones(len(positions))] @ world.T)[:, :3]
            a, b, c = p[triangles[:, 0]], p[triangles[:, 1]], p[triangles[:, 2]]
            wound = np.cross(b - a, c - a) * sign
            area = np.linalg.norm(wound, axis=1)
            outward = (area * (((a + b + c) / 3 - middle) * wound).sum(axis=1).__gt__(0)).sum() / max(area.sum(), 1e-12)
            if outward >= 1.0 / 3.0:
                primitives.append(primitive)
                continue
            part = dict(primitive)
            part["attributes"] = dict(primitive["attributes"])
            part["indices"] = append_accessor(model, blob, triangles[:, [0, 2, 1]].reshape(-1).astype(np.uint32), 5125, "SCALAR", 34963)
            normals = cm.accessor_array(model, blob, primitive["attributes"]["NORMAL"]).astype(np.float32)
            part["attributes"]["NORMAL"] = append_accessor(model, blob, -normals, 5126, "VEC3", 34962)
            if "TANGENT" in primitive["attributes"]:
                tangents = cm.accessor_array(model, blob, primitive["attributes"]["TANGENT"]).astype(np.float32)
                tangents[:, :3] *= -1.0
                part["attributes"]["TANGENT"] = append_accessor(model, blob, tangents, 5126, "VEC4", 34962)
            primitives.append(part)
            changed = True
            turned.append(f"{node.get('name', '?')} ({outward:.2f})")
        if changed:
            copy = dict(mesh)
            copy["primitives"] = primitives
            model["meshes"].append(copy)
            node["mesh"] = len(model["meshes"]) - 1
    if turned:
        print(f"  turned outwards: {turned}")


def split_lining(model, blob, nodes, eye, wheel):
    """The painted faces inside the cabin that face into it (the pillars' and the door frames' inner sides, the roof's
    underside, the screen header) take a dark trim, GLP_Lining: models paint whole shells, and from the seat the
    pillars showed in the body colour, with the sun's shadow flickering on them. A face counts when its centre is in
    the glasshouse (over the beltline, between the dashboard and the seats' backs) and it faces the cabin."""
    parents = {c: i for i, n in enumerate(nodes) for c in n.get("children", [])}
    materials = model["materials"]
    paint = {i for i, m in enumerate(materials) if m.get("name") == "GLP_Paint"}
    if not paint:
        return
    materials.append({"name": "GLP_Lining", "pbrMetallicRoughness": {"baseColorFactor": [0.05, 0.05, 0.055, 1.0], "metallicFactor": 0.0, "roughnessFactor": 0.85}, "doubleSided": True})
    lining = len(materials) - 1
    z_from, z_to = wheel[2] - 0.45, eye[2] + 0.55
    split = 0
    for index, node in enumerate(nodes):
        if "mesh" not in node:
            continue
        mesh = model["meshes"][node["mesh"]]
        if not any(p.get("material") in paint for p in mesh["primitives"]):
            continue
        world = node_world(nodes, index, parents)
        normal_matrix = np.linalg.inv(world[:3, :3]).T
        primitives = []
        changed = False
        for primitive in mesh["primitives"]:
            if primitive.get("material") not in paint or primitive.get("mode", 4) != 4 or "NORMAL" not in primitive["attributes"]:
                primitives.append(primitive)
                continue
            positions = cm.accessor_array(model, blob, primitive["attributes"]["POSITION"])
            normals = cm.accessor_array(model, blob, primitive["attributes"]["NORMAL"])
            if "indices" in primitive:
                indices = cm.accessor_array(model, blob, primitive["indices"]).astype(np.int64).reshape(-1)
            else:
                indices = np.arange(len(positions))
            triangles = indices[: len(indices) // 3 * 3].reshape(-1, 3)
            p = (np.c_[positions, np.ones(len(positions))] @ world.T)[:, :3]
            n = normals @ normal_matrix.T
            c = p[triangles].mean(axis=1)
            fn = n[triangles].sum(axis=1)
            fn /= np.maximum(np.linalg.norm(fn, axis=1, keepdims=True), 1e-9)
            # In the glasshouse: between the dashboard and the seats' backs, inside the doors, over the beltline.
            inside = (np.abs(c[:, 0]) < 0.85) & (c[:, 2] > z_from) & (c[:, 2] < z_to) & (c[:, 1] > eye[1] - 0.25)
            # The pillars' and the doors' frames inner sides (facing the centre line), the roof's underside (facing
            # down, over the eyes), the screen header's back (facing the driver) and the rear bulkhead's front;
            # never a face looking up, so the bonnet, the beltline and the roof's top keep their paint.
            side = (np.abs(fn[:, 1]) < 0.75) & (fn[:, 0] * np.sign(c[:, 0]) < -0.2)
            ceiling = (fn[:, 1] < -0.5) & (c[:, 1] > eye[1] + 0.02)
            header = (fn[:, 2] > 0.5) & (c[:, 2] < eye[2] - 0.2) & (c[:, 1] > eye[1] - 0.1) & (fn[:, 1] < 0.3)
            bulkhead = (fn[:, 2] < -0.5) & (c[:, 2] > eye[2] + 0.1) & (fn[:, 1] < 0.3)
            inner = inside & (side | ceiling | header | bulkhead)
            if not inner.any():
                primitives.append(primitive)
                continue
            for keep, material in ((~inner, primitive["material"]), (inner, lining)):
                chosen = triangles[keep].reshape(-1).astype(np.uint32)
                if len(chosen) == 0:
                    continue
                part = dict(primitive)
                part["indices"] = append_accessor(model, blob, chosen, 5125, "SCALAR", 34963)
                part["material"] = material
                primitives.append(part)
            changed = True
            split += int(inner.sum())
        if changed:
            # The mesh may be drawn by other nodes too: this node gets its own copy.
            copy = dict(mesh)
            copy["primitives"] = primitives
            model["meshes"].append(copy)
            node["mesh"] = len(model["meshes"]) - 1
    print(f"  {split} painted face(s) inside the cabin take the lining")


def borrow_cabin(model, blob, r, world, boxes, car, spec, donor_entry, cabin):
    """Puts another model's cabin into this one (cabin_from): a body with no interior of its own (the Testarossas
    found are shells) takes the named parts of a donor of its kind, rigged for this car's simulation like the
    donor's own model would be, then moved and scaled by the entry's offset and scale (car frame, metres)."""
    donor, donor_blob = cm.load(os.path.join(SOURCE, donor_entry["file"]))
    if donor_entry.get("split"):
        cm.split_nodes(donor, donor_blob, [s.lower() for s in donor_entry["split"]])
    cm.strip_unlit(donor)
    rd = cm.rig(donor, donor_entry, spec, car)
    to_car = rd["to_car"]
    if donor_entry.get("mirror_x"):
        to_car = np.diag([-1.0, 1.0, 1.0, 1.0]) @ to_car
    shift = matrix(translation=cabin.get("offset", [0.0, 0.0, 0.0]), scale=cabin.get("scale", [1.0, 1.0, 1.0]))
    words = [s.lower() for s in cabin["nodes"]]
    picked = [i for i in rd["body_nodes"] if cm.matches(i, rd["name_of"](i), words)]
    offsets = cm.merge(model, blob, donor, donor_blob)
    added = []
    for i in picked:
        model["nodes"].append({"name": rd["name_of"](i), "mesh": donor["nodes"][i]["mesh"] + offsets["meshes"]})
        k = len(model["nodes"]) - 1
        world[k] = shift @ to_car @ rd["worlds"][i]
        lo, hi = cm.mesh_bounds(model, model["nodes"][k]["mesh"], world[k])
        boxes[k] = (np.minimum(lo, hi), np.maximum(lo, hi))
        r["body_nodes"].append(k)
        added.append(rd["name_of"](i))
    print(f"  cabin from {donor_entry['file']}: {len(added)} part(s) {added[:12]}")


def prepare(car, entry, inspect=False, config=None):
    spec = cm.SPECS[car]
    source = os.path.join(SOURCE, entry["file"])
    if not os.path.exists(source):
        print(f"{car}: {entry['file']} missing (unreal/Scripts/fetch_models.py fetches it); the lofted body stays")
        return False
    model, blob = cm.load(source)
    if entry.get("split"):
        print(f"  {cm.split_nodes(model, blob, [s.lower() for s in entry['split']])} merged wheel mesh(es) split by corner")
    if cm.strip_unlit(model):
        print("  unlit materials made lit")
    r = cm.rig(model, entry, spec, car)
    nodes = model["nodes"]
    name_of = r["name_of"]
    to_car = r["to_car"]
    if entry.get("mirror_x"):
        # A right-hand-drive model mirrored to the game's left-hand drive: x flips, and with it the wheels' sides.
        to_car = np.diag([-1.0, 1.0, 1.0, 1.0]) @ to_car
        r["assemblies"] = [r["assemblies"][1], r["assemblies"][0], r["assemblies"][3], r["assemblies"][2]]
        r["pivots"] = [r["pivots"][1], r["pivots"][0], r["pivots"][3], r["pivots"][2]]
    world = {i: to_car @ r["worlds"][i] for i in r["mesh_nodes"]}
    boxes = {}
    for i in r["mesh_nodes"]:
        lo, hi = cm.mesh_bounds(model, nodes[i]["mesh"], world[i])
        boxes[i] = (np.minimum(lo, hi), np.maximum(lo, hi))
    if entry.get("cabin_from"):
        cabin = entry["cabin_from"]
        borrow_cabin(model, blob, r, world, boxes, car, spec, config[cabin["car"]], cabin)
    print(f"{car}: {entry['file']} wheelbase {r['wheelbase']:.3f} -> scale {r['scale']:.4f} ({r['length']:.2f} m long), "
          f"tyre radius {r['tyre_radius']:.3f} m (simulation {spec['radius']:.3f}); {len(r['body_nodes'])} body node(s), "
          f"{sum(len(a) - 1 for a in r['assemblies'])} wheel part(s), {len(r['dropped'])} dropped")

    if inspect:
        # The cabin: what lies between the axles, above the floor and inside the track.
        print("  meshes in the cabin (car frame: x right, y up from the centre of gravity, z back; metres):")
        for i in sorted(r["body_nodes"], key=lambda i: boxes[i][0][2]):
            lo, hi = boxes[i]
            centre = (lo + hi) / 2
            if abs(centre[0]) < 0.9 and -1.3 < centre[2] < 1.3 and centre[1] > -0.3:
                print(f"    {name_of(i)[:44]:44s} centre ({centre[0]:+.2f} {centre[1]:+.2f} {centre[2]:+.2f})  size ({hi[0]-lo[0]:.2f} {hi[1]-lo[1]:.2f} {hi[2]-lo[2]:.2f})")
        return True

    out_nodes = []

    def add(name, m=None, mesh=None, children=None):
        node = {"name": name}
        if m is not None and not np.allclose(m, np.eye(4)):
            node["matrix"] = [float(v) for v in m.T.flatten()]
        if mesh is not None:
            node["mesh"] = mesh
        if children:
            node["children"] = children
        out_nodes.append(node)
        return len(out_nodes) - 1

    steering_words = [s.lower() for s in entry.get("steering", [])]
    interior = entry.get("interior", True)
    steering = [i for i in r["body_nodes"] if interior and cm.matches(i, name_of(i), steering_words)] if steering_words else []
    body = [i for i in r["body_nodes"] if i not in steering]

    children = []
    body_children = [add(name_of(i)[:60], world[i], nodes[i]["mesh"]) for i in body]
    children.append(add("Body", children=body_children))
    # The tyres' radius from their vertices (a box would overstate a wheel stored turned about its axle), and each
    # wheel scaled about its hub to the simulation's radius: the hubs sit at the simulation's rest height, so a
    # smaller tyre would float over the road and a larger one sink into it.
    hubs = [to_car @ np.array([*r["pivots"][w], 1.0]) for w in range(4)]
    radii = []
    for w in range(4):
        pts = cm.mesh_points(model, blob, nodes[r["tyres"][w]]["mesh"], world[r["tyres"][w]])
        radii.append(float(np.max(np.hypot(pts[:, 1] - hubs[w][1], pts[:, 2] - hubs[w][2]))))
    radius = float(np.mean(radii))
    grow = min(max(spec["radius"] / radius, 0.85), 1.15)
    print(f"  tyres {radius:.3f} m from their vertices; wheels scaled by {grow:.3f} to the simulation's {spec['radius']:.3f}")
    for w in range(4):
        pivot = hubs[w]
        recentre = matrix(scale=(grow, grow, grow)) @ matrix(translation=-pivot[:3])
        parts = [add(name_of(i)[:60], recentre @ world[i], nodes[i]["mesh"]) for i in r["assemblies"][w]]
        children.append(add(f"Wheel{w}", matrix(translation=pivot[:3]), children=parts))

    if interior and not steering and entry.get("eye_at"):
        # A cabin whose steering wheel is part of a larger mesh (it cannot turn): the entry gives the eyes, and the
        # hub the dials are found from is taken where it would be, half a metre ahead and 28 cm under them.
        eye_at = np.array(entry["eye_at"], dtype=float)
        centre = eye_at - np.array([0.0, 0.28, 0.52])
        entry = dict(entry, eye=list(eye_at - centre - np.array([0.0, 0.28, 0.52])))
    if interior and (steering or entry.get("eye_at")):
        if steering:
            points = np.vstack([cm.mesh_points(model, blob, nodes[i]["mesh"], world[i]) for i in steering])
            lo, hi = points.min(axis=0), points.max(axis=0)
            centre = (lo + hi) / 2
        else:
            points = np.array([centre + [0.17, 0.0, 0.0], centre - [0.17, 0.0, 0.0], centre + [0.0, 0.17, 0.0], centre - [0.0, 0.17, 0.0], centre + [0.0, 0.0, 0.03]])
            lo, hi = points.min(axis=0), points.max(axis=0)
        # The column's axis: the direction in which the wheel is thinnest (the rim's plane is its widest), turned to
        # point back at the driver.
        covariance = np.cov((points - centre).T)
        values, vectors = np.linalg.eigh(covariance)
        axis = vectors[:, 0]
        if axis[2] < 0:
            axis = -axis
        tilt = math.degrees(math.atan2(axis[1], axis[2]))
        x = np.cross(np.array([0.0, 0.0, 1.0]), axis)
        x = np.array([1.0, 0.0, 0.0]) if np.linalg.norm(x) < 1e-6 else x / np.linalg.norm(x)
        if x[0] < 0:
            x = -x
        z = np.cross(x, axis)
        frame = matrix(np.column_stack([x, axis, z]), centre)
        inverse = np.linalg.inv(frame)
        spin = add("Spin", children=[add(name_of(i)[:60], inverse @ world[i], nodes[i]["mesh"]) for i in steering])
        children.append(add("Steering", frame, children=[spin]))
        radius = 0.5 * max(hi[0] - lo[0], hi[1] - lo[1])
        print(f"  steering wheel at ({centre[0]:+.3f} {centre[1]:+.3f} {centre[2]:+.3f}), radius {radius:.3f} m, column {tilt:.0f} degrees over level, "
              f"{len(steering)} mesh(es): {[name_of(i) for i in steering][:6]}")

        # The eyes: half a metre behind the wheel's centre and 28 cm over it, then the entry's offset.
        eye = centre + np.array([0.0, 0.28, 0.52]) + np.array(entry.get("eye", [0.0, 0.0, 0.0]))
        over = []
        for i in body:
            if contains(name_of(i), GLASS_WORDS) or contains(name_of(i), ("seat", "sheat", "asiento", "sitz", "siege")):
                continue
            lo_i, hi_i = boxes[i]
            if lo_i[0] - 0.15 < eye[0] < hi_i[0] + 0.15 and lo_i[2] - 0.15 < eye[2] < hi_i[2] + 0.15 and hi_i[1] > eye[1]:
                pts = cm.mesh_points(model, blob, nodes[i]["mesh"], world[i])
                near = pts[(np.abs(pts[:, 0] - eye[0]) < 0.10) & (np.abs(pts[:, 2] - eye[2]) < 0.10) & (pts[:, 1] > eye[1] + 0.03)]
                if len(near):
                    over.append(near[:, 1].min())
        # The top of the head is some 12 cm over the eyes: the eyes stay 13 cm under the headlining.
        headroom = (min(over) - eye[1]) if over else float("nan")
        if over and headroom < 0.13:
            eye[1] = min(over) - 0.13
        children.append(add("Eye", matrix(translation=eye)))
        print(f"  eyes at ({eye[0]:+.3f} {eye[1]:+.3f} {eye[2]:+.3f}), headroom {headroom:.3f} m{' (lowered)' if over and headroom < 0.13 else ''}")

        # The dials: on the face of the named instrument meshes (their side towards the eyes, 6 mm proud of it so the
        # model's own painted dials stay hidden behind the live ones), or else on the line from the eyes over the top
        # of the hub, 17 cm past the wheel; facing the eyes, as wide as the instruments allow at the panel's shape.
        dials_entry = entry.get("dials", {})
        dial_words = [s.lower() for s in dials_entry.get("nodes", [])]
        instruments = [i for i in body if dial_words and cm.matches(i, name_of(i), dial_words)]
        if instruments:
            lo_d = np.min([boxes[i][0] for i in instruments], axis=0)
            hi_d = np.max([boxes[i][1] for i in instruments], axis=0)
            face = np.array([(lo_d[0] + hi_d[0]) / 2, (lo_d[1] + hi_d[1]) / 2, hi_d[2]])
            face += (eye - face) / np.linalg.norm(eye - face) * 0.006
            width = min((hi_d[0] - lo_d[0]) * 0.95, max(hi_d[1] - lo_d[1], 0.10) / 0.39 * 1.05)
        else:
            # No instrument mesh of its own: the line of sight over the top of the hub, through the wheel's upper
            # opening, meets the instruments (or the dashboard where they are): the panel goes 8 mm before that.
            through = centre + np.array([0.0, 0.075, 0.0])
            sight = through - eye
            reach = np.linalg.norm(sight)
            direction = sight / reach
            hits = []
            for i in body:
                if contains(name_of(i), GLASS_WORDS):
                    continue
                lo_i, hi_i = boxes[i]
                if hi_i[2] < through[2] - 0.8 or lo_i[2] > through[2] + 0.05:
                    continue
                d = ray_hit(model, blob, nodes[i]["mesh"], world[i], eye, direction)
                if d is not None and d > reach + 0.02:
                    hits.append(d)
            distance = (min(hits) - 0.008) if hits else reach + dials_entry.get("past", 0.17)
            face = eye + direction * distance
            width = 0.26
            print(f"  dials found {'on the surface the eyes meet ' + format(min(hits) - reach, '.2f') + ' m past the hub' if hits else 'nothing past the hub: placed 17 cm past it'}")
        face = face + np.array(dials_entry.get("offset", [0.0, 0.0, 0.0]))
        width = dials_entry.get("width", width)
        children.append(add("Dials", matrix(basis_facing(eye - face), face, (width, width, 1.0))))
        print(f"  dials at ({face[0]:+.3f} {face[1]:+.3f} {face[2]:+.3f}), {width:.2f} m wide")

        # The rear-view mirror: the named housing's centre, its glass on the side towards the eyes.
        mirror_words = [s.lower() for s in entry.get("mirror", [])]
        housing = [i for i in body if mirror_words and cm.matches(i, name_of(i), mirror_words)]
        glass_words = [s.lower() for s in entry.get("mirror_glass", [])]
        panes = [i for i in body if glass_words and cm.matches(i, name_of(i), glass_words)]
        box = entry.get("mirror_box")
        if panes or box:
            # The model's own mirror glass (named, or a box found by hand in a mesh that holds the whole cabin): the
            # live glass goes 6 mm in front of it, facing the eyes, its size the node's x and y scale.
            if panes:
                lo_g = np.min([boxes[i][0] for i in panes], axis=0)
                hi_g = np.max([boxes[i][1] for i in panes], axis=0)
                centre_g = (lo_g + hi_g) / 2
                size = (hi_g[0] - lo_g[0], hi_g[1] - lo_g[1])
                face = np.array([centre_g[0], centre_g[1], hi_g[2]])
            else:
                face = np.array(box["centre"], dtype=float)
                size = tuple(box["size"])
            glass = face + (eye - face) / np.linalg.norm(eye - face) * 0.006 + np.array(entry.get("mirror_offset", [0.0, 0.0, 0.0]))
            children.append(add("Mirror", matrix(basis_facing(eye - glass), glass, (size[0], size[1], 1.0))))
            # The model's own glass goes: the live glass replaces it, and where the two crossed it hid half the picture.
            dropped_panes = {name_of(i)[:60] for i in panes}
            body_node = next(n for n in out_nodes if n.get("name") == "Body")
            body_node["children"] = [k for k in body_node["children"] if out_nodes[k].get("name") not in dropped_panes]
            print(f"  mirror glass at ({glass[0]:+.3f} {glass[1]:+.3f} {glass[2]:+.3f}), {size[0]:.3f} x {size[1]:.3f} m, on {'the model glass ' + str([name_of(i) for i in panes][:2]) if panes else 'the box given'}")
        elif housing:
            lo_m = np.min([boxes[i][0] for i in housing], axis=0)
            hi_m = np.max([boxes[i][1] for i in housing], axis=0)
            centre_m = (lo_m + hi_m) / 2
            towards = (eye - centre_m) / np.linalg.norm(eye - centre_m)
            glass = centre_m + towards * (0.5 * (hi_m[2] - lo_m[2]) + 0.004) + np.array(entry.get("mirror_offset", [0.0, 0.0, 0.0]))
            children.append(add("Mirror", matrix(basis_facing(eye - glass), glass)))
            print(f"  mirror glass at ({glass[0]:+.3f} {glass[1]:+.3f} {glass[2]:+.3f}) from {[name_of(i) for i in housing][:3]}")
        else:
            # No housing of the model's to use (none, or out of the driver's view, as in a low wedge whose screen
            # header is over the driver's head): the game hangs its own mirror on the windscreen's centre line where
            # it is 36 degrees right of straight ahead, just under the glass (MirrorHung: with its housing and stalk).
            ahead = abs(eye[0]) / math.tan(math.radians(36.0))
            z_m = eye[2] - ahead
            top = None
            for i in body:
                glassy = contains(name_of(i), GLASS_WORDS) or any(
                    contains(model["materials"][q["material"]].get("name", ""), GLASS_WORDS)
                    for q in model["meshes"][nodes[i]["mesh"]]["primitives"] if "material" in q)
                if not glassy:
                    continue
                for y_hit in ray_down(model, blob, nodes[i]["mesh"], world[i], 0.0, z_m):
                    if y_hit > eye[1] - 0.2:
                        top = y_hit if top is None else min(top, y_hit)
            y_m = (top - 0.06) if top is not None else eye[1] + 0.08
            glass = np.array([0.0, y_m, z_m]) + np.array(entry.get("mirror_offset", [0.0, 0.0, 0.0]))
            children.append(add("MirrorHung", matrix(basis_facing(eye - glass), glass)))
            print(f"  mirror hung at ({glass[0]:+.3f} {glass[1]:+.3f} {glass[2]:+.3f}), the glass over it at {None if top is None else round(float(top), 3)}")

    root = add("CarModel", children=children)

    # Materials: the paint, the windows and the clear lenses get the game's own (CarView); the rest stays the model's.
    paint_words = [s.lower() for s in entry.get("paint", [])]
    for material in model.get("materials", []):
        name = material.get("name", "")
        lower = name.lower()
        glassy = contains(lower, GLASS_WORDS) or "KHR_materials_transmission" in material.get("extensions", {})
        # Some exporters mark every material see-through (the VAZ's): an opaque one is drawn alpha-tested instead, so
        # it casts its shadow and sorts with the glass; a texture's holes (a grille) still show through.
        pbr = material.setdefault("pbrMetallicRoughness", {})
        alpha = pbr.get("baseColorFactor", [1, 1, 1, 1])
        # Nothing real is black as 0: black plastic, rubber and leather reflect 3-5 % of the light. An untextured
        # pure black (the Delta's whole cabin) drew as a void with no shape; it is lifted to 0.035.
        if "baseColorTexture" not in pbr and len(alpha) >= 3 and max(alpha[:3]) < 0.02:
            pbr["baseColorFactor"] = [0.035, 0.035, 0.037] + list(alpha[3:4] or [1.0])
        if material.get("alphaMode") == "BLEND" and not glassy and not contains(lower, LAMP_WORDS) and (len(alpha) < 4 or alpha[3] >= 0.99):
            material["alphaMode"] = "MASK"
            material["alphaCutoff"] = 0.5
        # "==Name" names a paint material exactly, case and all (the Renault 12 has both "Material" and "material").
        # The rear lamps' lenses that light up when the car brakes (CarView.Braking), and materials left as the model
        # has them though their names say glass (the Testarossa's orange indicators, red_glass its tail lamps).
        if any((name == w[2:]) if w.startswith("==") else (w in lower) for w in [s.lower() if not s.startswith("==") else s for s in entry.get("brake", [])]):
            material["name"] = "GLP_Brake"
            continue
        if any(w.lower() in lower for w in entry.get("keep", [])):
            continue
        painted = any((name == w[2:]) if w.startswith("==") else (w in lower) for w in entry.get("paint", [])) if paint_words else is_paint(name)
        if painted:
            material["name"] = "GLP_Paint"
        elif glassy and contains(lower, LAMP_WORDS):
            material["name"] = "GLP_Lens"
        elif glassy:
            material["name"] = "GLP_Glass"

    orient_paint(model, blob, out_nodes)
    if interior and (steering or entry.get("eye_at")):
        split_lining(model, blob, out_nodes, eye, centre)

    model["nodes"] = out_nodes
    model["scenes"] = [{"nodes": [root]}]
    model["scene"] = 0
    for key in ("animations", "skins", "cameras"):
        model.pop(key, None)
    for mesh in model.get("meshes", []):
        for primitive in mesh["primitives"]:
            primitive.pop("targets", None)
        mesh.pop("weights", None)
    model.pop("extensionsRequired", None)
    os.makedirs(TARGET, exist_ok=True)
    target = os.path.join(TARGET, f"{car}.glb")
    cm.save(target, model, blob)
    print(f"  -> {target}")
    return True


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    inspect = "--inspect" in sys.argv
    config = json.load(open(CONFIG, encoding="utf-8"))
    cars = args or [k for k in config if not k.startswith("_")]
    for car in cars:
        entry = config.get(car)
        if entry is None:
            print(f"{car}: no model in {CONFIG}; the lofted body stays")
            continue
        if "same_as" in entry:
            # Drawn with another car's model (CarView.ModelKeys): nothing of its own, and no stale file left behind.
            stale = os.path.join(TARGET, f"{car}.glb")
            if os.path.exists(stale):
                os.remove(stale)
            print(f"{car}: drawn as {entry['same_as']}")
            continue
        prepare(car, entry, inspect, config)


main()

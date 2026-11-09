"""Puts the game's own prepared car model, cabin and all, in place of the export's lofted car (0.74).

Usage: python prepare_model_car.py <in.glb> <out.glb> <car name> [<models folder>]

The Godot game draws every car from a prepared model (tools/prepare_car_models.py writes game/assets/models/<key>.glb:
the body with its own cabin, the four wheels on the simulation's hubs, the steering wheel, the driver's eye, the place
and size of the dials and of the rear-view mirror's glass). The world export (--export-world) still carries the lofted
stand-in, which the level replaced on the outside only (prepare_car_model.py, an older set of models), so the Unreal
cockpit was the lofted cabin: a flat brown dashboard round two dials. This takes the very file the game draws, so both
versions show the same car inside and out:

- PlayerCar keeps its root (the car's place at the start, its origin the car's reference point, as CarView's) and
  loses everything under it.
- ModelBody: the body and its cabin (build_scene.py tags it GLP.Exterior; unlike a lofted car's shell it stays in view
  from the seat, since the cabin is part of it).
- Wheel0..3, each with a Spin child holding that wheel's meshes (the simulation's order: front left, front right, rear
  left, rear right), the pivot on the prepared hub.
- Steering with its Spin: the steering wheel, which the game turns about the Spin's local y, as CarView does.
- Eye: the driver's eye; the game's cockpit camera starts there (AGlpGame).
- Dials: a quad as wide as the prepared node's x scale and as tall as the instrument panel's 1024 x 400 gives, facing
  the driver (+z), with the texture coordinates of Godot's QuadMesh, on which the game shows its instrument panel.
- RearMirror: the glass (MirrorGlass) on the prepared Mirror node, sized by its x and y scale (RearMirror.FitGlass);
  where the game hangs its own mirror (MirrorHung, the model's is missing or out of view) a housing and a stalk too,
  as RearMirror.cs builds them.
- LampHeadL, LampHeadR, LampTailL, LampTailR (0.77): empty nodes on the lamps' lenses, where the Unreal night hangs its
  beams and glows (lamp_sockets); the parts of the brake lamps' material that are no tail lamp get materials of their own
  (brake_parts: GLP_BrakeStop a centre stop lamp, GLP_BrakeOff a front marker).

Mesh nodes are named Mdl_<n> (Mdl_Wheel_<n>, Mdl_Steer_<n>), so the level's checks for a lofted car's parts (the side
mirrors found by their polished material, the cabin's triplanar photos) leave them alone; build_scene.py gives the
model's GLP_Paint, GLP_Glass and GLP_Lens materials the level's clear-coated paint and clear glass. A car without a
prepared model passes through unchanged and keeps whatever it had.
"""
import copy
import io
import json
import math
import os
import site
import struct
import sys

sys.path.append(site.getusersitepackages())
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from prepare_car_model import COMPONENTS, SPECS, TYPES, find_node, load, mesh_bounds, merge, save, world_matrices  # noqa: E402

# The prepared models a car is drawn with, its own first (CarView.ModelKeys): another car's model is fitted to this
# car's tyres. The keys are the file names tools/prepare_car_models.py writes.
MODEL_KEYS = {
    "flat12": ["flat12"], "v8": ["v8"], "hothatch": ["hothatch"], "five": ["five", "hothatch"],
    "turbohatch": ["turbohatch", "hothatch"], "saloon": ["saloon"], "estate": ["estate"], "fleet": ["fleet"],
    "458": ["458"], "bravohgt": ["bravohgt"], "bravo16": ["bravo16"], "leoncupra": ["leoncupra"], "brz": ["brz"],
    "clio": ["clio"],
}

PANEL = (1024.0, 400.0)                 # InstrumentCluster.PanelSize, GlpInstruments' PanelWidth and PanelHeight
MIRROR_GLASS = (0.21, 0.21 * 144 / 512)  # RearMirror.GlassWidth and its 512 x 144 picture
MIRROR_OFFSET = 0.016                    # the glass 16 mm ahead of RearMirror's origin (CarView.MountMirror)


def orthonormal(matrix):
    """The matrix without its scale (Basis.Orthonormalized: the prepared nodes carry sizes in their scale)."""
    m = matrix.copy()
    for c in range(3):
        length = np.linalg.norm(m[:3, c])
        if length > 1e-9:
            m[:3, c] /= length
    return m


def translation(x, y, z):
    m = np.eye(4)
    m[:3, 3] = (x, y, z)
    return m


class Builder:
    """Appends nodes, meshes, accessors and materials to a glTF document and its binary chunk."""

    def __init__(self, doc, blob):
        self.doc = doc
        self.blob = blob

    def node(self, name, matrix=None, mesh=None, children=None):
        node = {"name": name}
        if matrix is not None and not np.allclose(matrix, np.eye(4)):
            node["matrix"] = [float(v) for v in matrix.T.flatten()]
        if mesh is not None:
            node["mesh"] = mesh
        if children:
            node["children"] = list(children)
        self.doc["nodes"].append(node)
        return len(self.doc["nodes"]) - 1

    def _view(self, data, target):
        self.blob += b"\0" * (-len(self.blob) % 4)
        offset = len(self.blob)
        self.blob += data.tobytes()
        self.doc.setdefault("bufferViews", []).append({"buffer": 0, "byteOffset": offset, "byteLength": data.nbytes, "target": target})
        return len(self.doc["bufferViews"]) - 1

    def accessor(self, data, kind, component, target, bounds=False):
        return self._accessor(data, kind, component, target, bounds)

    def _accessor(self, data, kind, component, target, bounds=False):
        accessor = {"bufferView": self._view(data, target), "componentType": component, "count": int(data.shape[0]), "type": kind}
        if bounds:
            accessor["min"] = [float(v) for v in data.min(axis=0)]
            accessor["max"] = [float(v) for v in data.max(axis=0)]
        self.doc.setdefault("accessors", []).append(accessor)
        return len(self.doc["accessors"]) - 1

    def material(self, name, colour, metallic=0.0, roughness=0.5):
        self.doc.setdefault("materials", []).append({
            "name": name,
            "pbrMetallicRoughness": {"baseColorFactor": [*colour, 1.0], "metallicFactor": metallic, "roughnessFactor": roughness},
        })
        return len(self.doc["materials"]) - 1

    def mesh(self, name, positions, normals, uvs, indices, material):
        primitive = {
            "attributes": {
                "POSITION": self._accessor(np.asarray(positions, dtype=np.float32), "VEC3", 5126, 34962, bounds=True),
                "NORMAL": self._accessor(np.asarray(normals, dtype=np.float32), "VEC3", 5126, 34962),
                "TEXCOORD_0": self._accessor(np.asarray(uvs, dtype=np.float32), "VEC2", 5126, 34962),
            },
            "indices": self._accessor(np.asarray(indices, dtype=np.uint16), "SCALAR", 5123, 34963),
            "material": material,
        }
        self.doc.setdefault("meshes", []).append({"name": name, "primitives": [primitive]})
        return len(self.doc["meshes"]) - 1

    def quad(self, name, width, height, material):
        """Godot's QuadMesh: in the xy plane facing +z, v down the face (the top edge at v = 0)."""
        w, h = width / 2.0, height / 2.0
        positions = [(-w, h, 0.0), (w, h, 0.0), (w, -h, 0.0), (-w, -h, 0.0)]
        uvs = [(0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0)]
        return self.mesh(name, positions, [(0.0, 0.0, 1.0)] * 4, uvs, [0, 3, 2, 0, 2, 1], material)

    def box(self, name, size, material):
        sx, sy, sz = (s / 2.0 for s in size)
        positions, normals, uvs, indices = [], [], [], []
        for axis in range(3):
            for sign in (1.0, -1.0):
                normal = np.zeros(3)
                normal[axis] = sign
                u = np.zeros(3)
                u[(axis + 1) % 3] = 1.0
                v = np.cross(normal, u)
                base = len(positions)
                for a, b in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
                    p = normal + a * u + b * v
                    positions.append((p[0] * sx, p[1] * sy, p[2] * sz))
                    normals.append(tuple(normal))
                    uvs.append(((a + 1) / 2.0, (b + 1) / 2.0))
                indices += [base, base + 1, base + 2, base, base + 2, base + 3]
        return self.mesh(name, positions, normals, uvs, indices, material)


def model_path(folder, car):
    for key in MODEL_KEYS.get(car, [car]):
        path = os.path.join(folder, f"{key}.glb")
        if os.path.exists(path):
            return key, path
    return None, None


def mesh_nodes(doc, index):
    """The mesh-bearing nodes of a subtree, the subtree's root included."""
    out = []
    stack = [index]
    while stack:
        i = stack.pop()
        if "mesh" in doc["nodes"][i]:
            out.append(i)
        stack.extend(doc["nodes"][i].get("children", []))
    return out


def is_glass(model, mesh_index):
    names = [(model["materials"][p["material"]].get("name") or "") if "material" in p else "" for p in model["meshes"][mesh_index]["primitives"]]
    return bool(names) and all(n.startswith(("GLP_Glass", "GLP_Lens")) for n in names)


METAL_WORDS = ("chrome", "croom", "metal", "metel", "steel", "alu", "alminum", "zinc", "brushed", "exhaust", "muffler")


def srgb(c):
    return [12.92 * v if v <= 0.0031308 else 1.055 * v ** (1 / 2.4) - 0.055 for v in c]


def linear(c):
    return [v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4 for v in c]


def spec_gloss_to_metal_rough(material):
    """KHR_materials_pbrSpecularGlossiness as Godot's glTF importer reads factors (GLTFDocument: the diffuse colour is
    the albedo, the roughness one minus the glossiness, the metallic the material's default 0; its full conversion runs
    only for a specular-glossiness texture). Unreal's importer ignores the extension and drew those materials at their
    metallic-roughness defaults, white metal, so the Lotus Esprit's cabin (the Testarossa's, the 205's seats) came in
    pale grey where the game shows it black (0.75)."""
    sg = material.get("extensions", {}).pop("KHR_materials_pbrSpecularGlossiness", None)
    if sg is None:
        return
    if not material["extensions"]:
        material.pop("extensions")
    diffuse = sg.get("diffuseFactor", [1.0, 1.0, 1.0, 1.0])
    pbr = material.setdefault("pbrMetallicRoughness", {})
    pbr["baseColorFactor"] = list(diffuse[:3]) + [diffuse[3] if len(diffuse) > 3 else 1.0]
    pbr["metallicFactor"] = 0.0
    pbr["roughnessFactor"] = 1.0 - sg.get("glossinessFactor", 1.0)
    if "diffuseTexture" in sg and "baseColorTexture" not in pbr:
        pbr["baseColorTexture"] = sg["diffuseTexture"]


def correct_material(material):
    """CarView.CorrectModelMaterial (the Godot game) on a model's own material, so the Unreal level draws the cabins as the
    game does (0.74). glTF reads a missing metallic or roughness as 1: Unreal keeps that, and an untextured plastic, leather
    or trim left at the exporter's defaults came out as grey metal (the Testarossa's black leather, the Clio's cabin); the
    game draws it as a matt dielectric. Also its rubber, its silvered wing-mirror glass, its glowing white rear-lamp blocks
    and its grey painted wheels. The game's own GLP_ materials are left to build_scene.py."""
    spec_gloss_to_metal_rough(material)
    name = (material.get("name") or "").lower()
    if name.startswith("glp_"):
        return
    pbr = material.setdefault("pbrMetallicRoughness", {})
    metallic = pbr.get("metallicFactor", 1.0)
    roughness = pbr.get("roughnessFactor", 1.0)
    driven = "metallicRoughnessTexture" in pbr
    default = not driven and metallic >= 0.999 and roughness >= 0.999
    textured = "baseColorTexture" in pbr
    says = lambda *words: any(w in name for w in words)
    colour = srgb(pbr.get("baseColorFactor", [1.0, 1.0, 1.0, 1.0])[:3])
    alpha = pbr.get("baseColorFactor", [1.0, 1.0, 1.0, 1.0])[3:] or [1.0]
    hi, lo = max(colour), min(colour)
    saturation = (hi - lo) / hi if hi > 0 else 0.0
    emissive = material.get("emissiveFactor", [0.0, 0.0, 0.0])
    e_hi = max(emissive)
    e_sat = (e_hi - min(emissive)) / e_hi if e_hi > 0 else 0.0
    if e_hi > 0 and says("back", "rear", "tail") and e_sat < 0.2:
        pbr["baseColorFactor"] = linear([0.45, 0.02, 0.02]) + alpha
        material["emissiveFactor"] = [1.0, 0.05, 0.03]
        strength = material.get("extensions", {}).get("KHR_materials_emissive_strength", {})
        if strength:
            strength["emissiveStrength"] = min(strength.get("emissiveStrength", 1.0), 0.6)
        pbr["metallicFactor"], pbr["roughnessFactor"] = 0.0, 0.1
    elif says("tyre", "tire", "pneu", "rubber"):
        pbr["metallicFactor"] = 0.0
        if not driven:
            pbr["roughnessFactor"] = max(roughness, 0.86)
    elif says("mirror") and default and not textured and 0.2126 * colour[0] + 0.7152 * colour[1] + 0.0722 * colour[2] > 0.6:
        pbr["roughnessFactor"] = 0.03
        pbr["baseColorFactor"] = linear([0.86, 0.87, 0.88]) + alpha
    elif default and not says(*METAL_WORDS):
        pbr["metallicFactor"], pbr["roughnessFactor"] = 0.0, 0.6
    elif says("wheel", "weel", "rim") and not driven and not textured and metallic < 0.5 and saturation < 0.15 and hi > 0.3:
        pbr["metallicFactor"], pbr["roughnessFactor"] = 0.75, 0.32
    # Dark dielectrics (the cabins' plastics and leathers, the trims) get a grained surface here (0.75): under Lumen the
    # dashboard tops, at 0.5-0.6, mirrored the bright sky through the windscreen and read pale grey from the seat; the
    # Godot game hides that with a dark reflection probe in the cabin (CarView.BuildCabinShade), which Lumen has no
    # counterpart of. Textured plastics are rough; a black dashboard's sheen is faint.
    # A dark "metal" without a texture is a black plastic or rubber exported as metal (the Testarossa's black_blactec,
    # metallic 0.7 over 138 000 triangles of its trim and cabin): under the sun through the windscreen its specular lobe
    # lit the dashboard pale grey (measured: switching the sun off took it from 85 to 10 grey levels, the sky light and the
    # reflections barely mattered), where the game, its cabin in a dark reflection probe, shows it black.
    pbr = material["pbrMetallicRoughness"]
    base = pbr.get("baseColorFactor", [1.0, 1.0, 1.0, 1.0])
    if not driven and not textured and 0.2126 * base[0] + 0.7152 * base[1] + 0.0722 * base[2] < 0.1:
        if pbr.get("metallicFactor", 1.0) < 0.9:
            pbr["metallicFactor"] = 0.0
        if pbr.get("metallicFactor", 1.0) < 0.2:
            pbr["roughnessFactor"] = max(pbr.get("roughnessFactor", 1.0), 0.8)


def glassy(material):
    """Drawn translucent in the level: the game's glass and lenses, and anything the model blends."""
    return (material.get("name") or "").startswith(("GLP_Glass", "GLP_Lens")) or material.get("alphaMode") == "BLEND"


def read_accessor(doc, blob, index):
    """An accessor's elements as float32, normalised integers scaled to their range; shape (count, components)."""
    accessor = doc["accessors"][index]
    components = COMPONENTS[accessor["type"]]
    count = accessor["count"]
    dtype = np.dtype(TYPES[accessor["componentType"]])
    if "bufferView" not in accessor:
        return np.zeros((count, components), dtype=np.float32)
    view = doc["bufferViews"][accessor["bufferView"]]
    offset = view.get("byteOffset", 0) + accessor.get("byteOffset", 0)
    item = dtype.itemsize * components
    stride = view.get("byteStride", item)
    raw = np.frombuffer(blob, dtype=np.uint8, count=stride * (count - 1) + item, offset=offset)
    rows = np.lib.stride_tricks.as_strided(raw, shape=(count, item), strides=(stride, 1)) if stride != item else raw.reshape(count, item)
    data = np.frombuffer(np.ascontiguousarray(rows).tobytes(), dtype=dtype).reshape(count, components).astype(np.float32)
    if accessor.get("normalized"):
        limits = {np.dtype(np.uint8): 255.0, np.dtype(np.uint16): 65535.0, np.dtype(np.int8): 127.0, np.dtype(np.int16): 32767.0}
        if dtype in limits:
            data = np.maximum(data / limits[dtype], -1.0)
    return data


class Merger:
    """Bakes a group of the model's mesh nodes into one mesh with a primitive per material (0.74): the nodes' transforms go
    into the vertices (mirrored nodes keep their faces' facing by reversing the winding), the tangents are left for the
    importer to compute. A 205 came in as 160 actors, each a draw and a ray-tracing instance of its own, and with eight
    drones the render thread took 19 ms a frame; a car is now a handful."""

    def __init__(self, model, blob, parts=None):
        self.model = model
        self.blob = blob
        self.buckets = {}
        # brake_parts (0.77): {material: [(another material, boxes in the car's frame)]}, the triangles whose middle lies in
        # one of the boxes going to the other material.
        self.parts = parts or {}

    def split(self, material, world, indices):
        """The primitive's triangles by material: [(material, its indices)], the brake_parts boxes taking theirs."""
        if material not in self.parts:
            return [(material, indices)]
        triangles = indices.reshape(-1, 3)
        middle = world[triangles].mean(axis=1)
        owner = np.full(len(triangles), material)
        for other, boxes in self.parts[material]:
            for low, high in boxes:
                owner[np.all((middle >= low) & (middle <= high), axis=1)] = other
        return [(int(m), triangles[owner == m].ravel()) for m in np.unique(owner)]

    def add(self, mesh_index, matrix):
        linear = matrix[:3, :3]
        flip = np.linalg.det(linear) < 0
        normal_matrix = np.linalg.inv(linear).T
        for primitive in self.model["meshes"][mesh_index]["primitives"]:
            attributes = primitive["attributes"]
            if primitive.get("mode", 4) != 4 or "POSITION" not in attributes:
                continue
            position = read_accessor(self.model, self.blob, attributes["POSITION"])
            count = len(position)
            normal = None
            if "NORMAL" in attributes:
                normal = read_accessor(self.model, self.blob, attributes["NORMAL"]) @ normal_matrix.T
                normal /= np.maximum(np.linalg.norm(normal, axis=1, keepdims=True), 1e-12)
            uv0 = read_accessor(self.model, self.blob, attributes["TEXCOORD_0"]) if "TEXCOORD_0" in attributes else np.zeros((count, 2), np.float32)
            uv1 = read_accessor(self.model, self.blob, attributes["TEXCOORD_1"]) if "TEXCOORD_1" in attributes else None
            colour = None
            if "COLOR_0" in attributes:
                colour = read_accessor(self.model, self.blob, attributes["COLOR_0"])
                if colour.shape[1] == 3:
                    colour = np.hstack([colour, np.ones((count, 1), np.float32)])
            indices = read_accessor(self.model, self.blob, primitive["indices"]).astype(np.uint32).ravel() if "indices" in primitive else np.arange(count, dtype=np.uint32)
            indices = indices[: len(indices) // 3 * 3]
            if flip:
                indices = indices.reshape(-1, 3)[:, ::-1].ravel()
            world = (position @ linear.T + matrix[:3, 3]).astype(np.float32)
            groups = self.split(primitive.get("material", -1), world, indices)
            for material, subset in groups:
                if not len(subset):
                    continue
                if len(groups) == 1:
                    used, local = slice(None), subset
                else:
                    # A primitive split between materials: each keeps only the vertices its triangles use.
                    used = np.unique(subset)
                    remap = np.full(count, -1, np.int64)
                    remap[used] = np.arange(len(used))
                    local = remap[subset].astype(np.uint32)
                kept = world[used]
                bucket = self.buckets.setdefault(material, {
                    "position": [], "normal": [], "uv0": [], "uv1": [], "colour": [], "indices": [], "count": 0,
                    "normals": True, "uv1s": True, "colours": False})
                bucket["position"].append(kept)
                bucket["normal"].append(normal[used] if normal is not None else np.zeros((len(kept), 3), np.float32))
                bucket["uv0"].append(uv0[used])
                bucket["uv1"].append(uv1[used] if uv1 is not None else np.zeros((len(kept), 2), np.float32))
                bucket["colour"].append(colour[used] if colour is not None else np.ones((len(kept), 4), np.float32))
                bucket["indices"].append(local + bucket["count"])
                bucket["count"] += len(kept)
                bucket["normals"] &= normal is not None
                bucket["uv1s"] &= uv1 is not None
                bucket["colours"] |= colour is not None

    def build(self, builder, name, material_offset):
        """The baked mesh's index in the builder's document, or None when the group drew nothing."""
        primitives = []
        for material, bucket in self.buckets.items():
            attributes = {"POSITION": builder.accessor(np.vstack(bucket["position"]), "VEC3", 5126, 34962, bounds=True)}
            if bucket["normals"]:
                attributes["NORMAL"] = builder.accessor(np.vstack(bucket["normal"]).astype(np.float32), "VEC3", 5126, 34962)
            attributes["TEXCOORD_0"] = builder.accessor(np.vstack(bucket["uv0"]).astype(np.float32), "VEC2", 5126, 34962)
            if bucket["uv1s"]:
                attributes["TEXCOORD_1"] = builder.accessor(np.vstack(bucket["uv1"]).astype(np.float32), "VEC2", 5126, 34962)
            if bucket["colours"]:
                attributes["COLOR_0"] = builder.accessor(np.vstack(bucket["colour"]).astype(np.float32), "VEC4", 5126, 34962)
            primitive = {"attributes": attributes, "indices": builder.accessor(np.concatenate(bucket["indices"]).astype(np.uint32).reshape(-1, 1), "SCALAR", 5125, 34963)}
            if material >= 0:
                primitive["material"] = material + material_offset
            primitives.append(primitive)
        if not primitives:
            return None
        self.builder_doc = builder.doc
        builder.doc.setdefault("meshes", []).append({"name": name, "primitives": primitives})
        return len(builder.doc["meshes"]) - 1

    def drop_inner_panes(self, eye):
        """Keeps one face of each window the model draws as two parallel layers (0.76): the level's glass is a thin
        translucent surface standing for a whole pane, both its faces, so a pane modelled as its inner and outer faces
        let the light through it twice. Through the Testarossa's windscreen, raked 72 degrees to the eye's line, the
        road came out at a ninth of its light (measured against the cockpit with the glass hidden), where one layer lets
        through about a third, as the 458's and the McLaren's single-layer screens do; the BRZ, the saloon and the
        turbo hatch had the same doubled glass. Only the outermost face stays; the game's own glass only, every
        material of it at once (the turbo hatch's rear window has its two faces in two materials named GLP_Glass)."""
        glass = [m for m, bucket in self.buckets.items()
                 if m >= 0 and bucket["indices"] and (self.model["materials"][m].get("name") or "").startswith("GLP_Glass")]
        if not glass:
            return 0
        every, owner, base = [], [], 0
        for material in glass:
            bucket = self.buckets[material]
            every.append((np.vstack(bucket["position"]).astype(np.float64),
                          np.concatenate(bucket["indices"]).astype(np.int64).reshape(-1, 3) + base))
            owner.append(np.full(len(every[-1][1]), material))
            base += len(every[-1][0])
        inner = inner_layer(np.vstack([p for p, _ in every]), np.vstack([t for _, t in every]), np.asarray(eye, np.float64))
        owner = np.concatenate(owner)
        dropped = 0
        for material in glass:
            bucket = self.buckets[material]
            positions = np.vstack(bucket["position"]).astype(np.float64)
            triangles = np.concatenate(bucket["indices"]).astype(np.int64).reshape(-1, 3)
            keep = ~inner[owner == material]
            if keep.all():
                continue
            dropped += int((~keep).sum())
            triangles = triangles[keep]
            if not len(triangles):
                # A material that held only inner faces goes whole.
                del self.buckets[material]
                continue
            used = np.unique(triangles)
            remap = np.full(len(positions), -1, np.int64)
            remap[used] = np.arange(len(used))
            for key in ("position", "normal", "uv0", "uv1", "colour"):
                bucket[key] = [np.vstack(bucket[key])[used]]
            bucket["indices"] = [remap[triangles].astype(np.uint32).ravel()]
            bucket["count"] = len(used)
        return dropped


def inner_layer(positions, triangles, eye, reach=0.035, cell=0.1):
    """Which triangles are an inner face of a pane drawn as parallel layers: a ray from a triangle's centre along its
    normal, either way, meets a near-parallel triangle of the same glass within reach (3.5 cm; the models' panes are
    1 to 28 mm thick) lying farther from the driver's eye. Any such twin, not only the nearest, so that of three
    layers only the outermost stays. Candidates come from a grid of cells, each triangle filed in every cell its box,
    grown by reach, touches."""
    a, b, c = (positions[triangles[:, k]] for k in range(3))
    e1, e2 = b - a, c - a
    normal = np.cross(e1, e2)
    length = np.linalg.norm(normal, axis=1)
    valid = np.nonzero(length > 1e-12)[0]
    unit = normal / np.maximum(length, 1e-12)[:, None]
    centre = (a + b + c) / 3.0
    low = np.floor((np.minimum(np.minimum(a, b), c) - reach) / cell).astype(np.int64)
    high = np.floor((np.maximum(np.maximum(a, b), c) + reach) / cell).astype(np.int64)
    filed = {}
    for t in valid:
        for x in range(low[t, 0], high[t, 0] + 1):
            for y in range(low[t, 1], high[t, 1] + 1):
                for z in range(low[t, 2], high[t, 2] + 1):
                    filed.setdefault((x, y, z), []).append(t)
    home = np.floor(centre / cell).astype(np.int64)
    sources = {}
    for t in valid:
        sources.setdefault(tuple(home[t]), []).append(t)
    inner = np.zeros(len(triangles), bool)
    for key, members in sources.items():
        s = np.array(members)
        targets = np.array(filed.get(key, []))
        if len(targets) == 0:
            continue
        # Moller-Trumbore, every source's ray against every target triangle of its cell; t is signed along the normal.
        d = unit[s][:, None, :]
        h = np.cross(d, e2[targets][None])
        det = (e1[targets][None] * h).sum(-1)
        ok = (np.abs(det) > 1e-12) & (np.abs(unit[s] @ unit[targets].T) > 0.95) & (s[:, None] != targets[None, :])
        f = np.where(ok, 1.0 / np.where(ok, det, 1.0), 0.0)
        offset = centre[s][:, None, :] - a[targets][None]
        u = f * (offset * h).sum(-1)
        q = np.cross(offset, e1[targets][None])
        v = f * (q * d).sum(-1)
        t = f * (q * e2[targets][None]).sum(-1)
        hit = ok & (u >= -1e-6) & (v >= -1e-6) & (u + v <= 1.0 + 1e-6) & (np.abs(t) > 5e-4) & (np.abs(t) <= reach)
        # A twin sits at centre + t * normal: farther from the eye when that step points away from it.
        farther = hit & (((centre[s] - eye) * unit[s]).sum(1)[:, None] * t > 0)
        inner[s[farther.any(1)]] = True
    return inner


LAMPS = ("LampHeadL", "LampHeadR", "LampTailL", "LampTailR")

# Lamps a model's GLP_Lens and GLP_Brake do not mark (0.77), by the prepared model's file name (MODEL_KEYS): the materials
# its heads or its tails are made of instead, and the cells its lamps' triangles are joined into clusters by where one
# lamp unit holds several lamps (lens_clusters).
LAMP_MATERIALS = {
    # The 2021 BRZ's GLP_Lens is the fog lamps in its bumper, 0.42 m up, and its head glows and beams sat 23 cm under its
    # dark headlamps. Those are LED emitters in a chrome housing under a GLP_Glass cover (emission_white.001: the low
    # beam's projector, a small inner lamp and the running-light strip), which 3 cm cells tell apart; 6 cm cells joined
    # them round the housing's middle, 9 cm inboard of the projector.
    "brz": {"heads": ("emission_white.001",), "cell": 0.03},
    # The estate's headlamps are a material of their own (Phare_avt, French for headlamp); its tails are its GLP_Brake.
    "estate": {"heads": ("Phare_avt",)},
    # The saloon's lamps, front and rear, are one textured material (Headlights) under GLP_Glass covers; its heads are
    # measured (MEASURED_LAMPS).
    "saloon": {"tails": ("Headlights",)},
}

# Lamps measured on the model where neither a material nor its clusters tell them (0.77): in the car's frame (x right,
# y up, -z forward), on the lens. The hot hatch's lamps are regions of its body's texture (uv_2), found by their colour on
# the surface seen from each end: the headlamps' pale lens (the box rule put the glows on the amber indicators outboard of
# them) and the tail lamps' red (the box rule put those glows 18 cm over it, at the edge of the body), leaving out the
# amber and white lamps under the red. The saloon's heads are its outer round lamps, the dipped beams of a four-lamp
# front: its inner pair is the main beam alone, and its material's clusters join all four at any cell size.
MEASURED_LAMPS = {
    "hothatch": {"LampHeadL": (-0.520, -0.010, -1.438), "LampHeadR": (0.520, -0.010, -1.438),
                 "LampTailL": (-0.622, 0.022, 1.987), "LampTailR": (0.622, 0.022, 1.987)},
    "saloon": {"LampHeadL": (-0.613, 0.031, -1.632), "LampHeadR": (0.598, 0.030, -1.632)},
}

# A dipped beam stands at least half a metre up (ECE R48); a cluster lower in front is a fog lamp and gives way to one
# at a headlamp's height (0.77: the BRZ's only GLP_Lens is its fog lamps).
HEADLAMP_HEIGHT = 0.48


def texture_image(model, blob, index, cache):
    """A texture's image as linear RGB floats (the glb's own, from its buffer view), or None."""
    if index not in cache:
        cache[index] = None
        source = model["textures"][index].get("source")
        image = model["images"][source] if source is not None else {}
        if "bufferView" in image:
            from PIL import Image   # the build's other scripts (make_decals.py, prepare_foliage.py) use it too
            view = model["bufferViews"][image["bufferView"]]
            start = view.get("byteOffset", 0)
            data = bytes(blob[start:start + view["byteLength"]])
            srgb = np.asarray(Image.open(io.BytesIO(data)).convert("RGB"), dtype=np.float32) / 255.0
            cache[index] = np.where(srgb <= 0.04045, srgb / 12.92, ((srgb + 0.055) / 1.055) ** 2.4)
    return cache[index]


def lens_triangles(model, blob, nodes, worlds, names, colours=False):
    """The triangles (n, 3, 3) of the mesh nodes' primitives whose material name starts with one of names, in the car's
    frame. With colours, also each triangle's linear colour (its texture at the triangle's middle times the material's
    factor, or the factor alone) and whether a texture gave it (0.77)."""
    found, tints, textured = [], [], []
    cache = {}
    for i in nodes:
        for primitive in model["meshes"][model["nodes"][i]["mesh"]]["primitives"]:
            if primitive.get("mode", 4) != 4 or "material" not in primitive or "POSITION" not in primitive["attributes"]:
                continue
            material = model["materials"][primitive["material"]]
            if not (material.get("name") or "").startswith(names):
                continue
            position = read_accessor(model, blob, primitive["attributes"]["POSITION"])
            indices = read_accessor(model, blob, primitive["indices"]).astype(np.int64).ravel() if "indices" in primitive else np.arange(len(position))
            indices = indices[: len(indices) // 3 * 3].reshape(-1, 3)
            world = (np.hstack([position, np.ones((len(position), 1))]) @ worlds[i].T)[:, :3]
            found.append(world[indices])
            if not colours:
                continue
            pbr = material.get("pbrMetallicRoughness", {})
            factor = np.array(pbr.get("baseColorFactor", [1.0, 1.0, 1.0, 1.0])[:3], dtype=np.float32)
            texture = pbr.get("baseColorTexture")
            uv = primitive["attributes"].get(f"TEXCOORD_{texture.get('texCoord', 0)}") if texture else None
            image = texture_image(model, blob, texture["index"], cache) if uv is not None else None
            if image is None:
                tints.append(np.tile(factor, (len(indices), 1)))
                textured.append(np.zeros(len(indices), bool))
                continue
            middle = read_accessor(model, blob, uv)[indices].mean(axis=1)
            h, w = image.shape[:2]
            texel = image[np.mod(np.floor(middle[:, 1] * h).astype(np.int64), h), np.mod(np.floor(middle[:, 0] * w).astype(np.int64), w)]
            tints.append(texel * factor)
            textured.append(np.ones(len(indices), bool))
    triangles = np.concatenate(found) if found else np.zeros((0, 3, 3))
    if not colours:
        return triangles
    return triangles, (np.concatenate(tints) if tints else np.zeros((0, 3))), (np.concatenate(textured) if textured else np.zeros(0, bool))


def lens_clusters(triangles, cell=0.06):
    """The triangles joined into clusters by their centres' neighbouring cells (6 cm unless given): each (members, area
    m2, the area-weighted centre)."""
    centre = triangles.mean(axis=1)
    area = 0.5 * np.linalg.norm(np.cross(triangles[:, 1] - triangles[:, 0], triangles[:, 2] - triangles[:, 0]), axis=1)
    parent = list(range(len(triangles)))

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    cells = {}
    for i, key in enumerate(map(tuple, np.floor(centre / cell).astype(np.int64))):
        cells.setdefault(key, []).append(i)
    for key, members in cells.items():
        root = find(members[0])
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                for dz in (-1, 0, 1):
                    for j in cells.get((key[0] + dx, key[1] + dy, key[2] + dz), ()):
                        other = find(j)
                        if other != root:
                            parent[other] = root
    groups = {}
    for i in range(len(triangles)):
        groups.setdefault(find(i), []).append(i)
    return [(np.array(g), float(area[g].sum()), (centre[g] * area[g][:, None]).sum(0) / max(area[g].sum(), 1e-12)) for g in groups.values()]


def on_surface(triangles, x, y, front):
    """The lens's surface seen at (x, y) along the car (0.77): where the line through it along z crosses the triangles
    first from ahead (front) or from behind. Where the line passes through a lamp round the point instead (the 458's
    ring of a tail lamp), the point keeps its place in the ring at the depth of the ring's nearest part. The cluster's
    middle at the extreme z of its tip, as before, left a swept lamp's socket up to 23 cm in the air ahead of the lens
    (the 458's heads), its glow outboard of the lamp at the body's edge."""
    a, b, c = triangles[:, 0], triangles[:, 1], triangles[:, 2]
    e0, e1, p = b[:, :2] - a[:, :2], c[:, :2] - a[:, :2], np.array([x, y]) - a[:, :2]
    d00, d01, d11 = (e0 * e0).sum(1), (e0 * e1).sum(1), (e1 * e1).sum(1)
    d20, d21 = (p * e0).sum(1), (p * e1).sum(1)
    det = d00 * d11 - d01 * d01
    edge_on = np.abs(det) < 1e-14
    det = np.where(edge_on, 1.0, det)
    v = (d11 * d20 - d01 * d21) / det
    w = (d00 * d21 - d01 * d20) / det
    u = 1.0 - v - w
    hit = ~edge_on & (u >= -1e-6) & (v >= -1e-6) & (w >= -1e-6)
    if hit.any():
        z = (u * a[:, 2] + v * b[:, 2] + w * c[:, 2])[hit]
        return (float(x), float(y), float(z.min() if front else z.max()))
    points = triangles.reshape(-1, 3)
    reach = np.hypot(points[:, 0] - x, points[:, 1] - y)
    near = points[reach <= reach.min() + 0.03, 2]
    return (float(x), float(y), float(near.min() if front else near.max()))


def end_width(doc, blob, nodes, matrices, low, high):
    """The body's sides at its ends, the front and the rear fifth of its length (0.77): its box round the side mirrors,
    which stand out from the doors, put the 205's head glows on its indicators outboard of its headlamps and its tail
    glows at the edge of the body. Returns (left, right) in x."""
    length = high[2] - low[2]
    left, right = np.inf, -np.inf
    for i in nodes:
        for primitive in doc["meshes"][doc["nodes"][i]["mesh"]]["primitives"]:
            if "POSITION" not in primitive["attributes"]:
                continue
            points = read_accessor(doc, blob, primitive["attributes"]["POSITION"]) @ matrices[i][:3, :3].T + matrices[i][:3, 3]
            ends = (points[:, 2] < low[2] + 0.2 * length) | (points[:, 2] > high[2] - 0.2 * length)
            if ends.any():
                left, right = min(left, float(points[ends, 0].min())), max(right, float(points[ends, 0].max()))
    return (left, right) if left < right else (float(low[0]), float(high[0]))


def box_rule(left, right, ground, front, rear):
    """The Godot version's lamp rule (CarView.BuildLamps): a quarter of a metre in from the sides, 0.65 m up 2 cm ahead of
    the nose, 0.82 m up 5 cm in from the tail, the tails a little wider."""
    centre_x = (left + right) / 2.0
    side = max((right - left) / 2.0 - 0.25, 0.3)
    return {"LampHeadL": (centre_x - side, ground + 0.65, front - 0.02), "LampHeadR": (centre_x + side, ground + 0.65, front - 0.02),
            "LampTailL": (centre_x - side * 1.05, ground + 0.82, rear - 0.05), "LampTailR": (centre_x + side * 1.05, ground + 0.82, rear - 0.05)}


def is_red(tints, textured):
    """A textured lens's red parts (0.77): the estate's and the five's brake lamp clusters hold amber indicators and white
    reversing lamps in their textures (linear green and blue at most 4.5 % of the red: their reds reach 3 %, their ambers
    7 % and more). A plain lens is its lamp's colour, whatever its hue (the V8's is a dark orange)."""
    r = tints[:, 0]
    return ~textured | ((r > 1e-3) & (np.maximum(tints[:, 1], tints[:, 2]) < 0.045 * r))


def lamp_sockets(model, blob, worlds, body_nodes, wheel_nodes, hubs, key=None):
    """Where a car's lamps are (0.77), for the Unreal night's beams and glows: the four empty nodes LampHeadL,
    LampHeadR, LampTailL and LampTailR (the car's frame: x right, y up, -z forward). From the model's own lamps where it
    has them: the heads from its GLP_Lens triangles in the front 30 % of its length, the tails from the red of its
    GLP_Brake (else its rear GLP_Lens) in the rear 30 %, or the materials LAMP_MATERIALS names for the model; all 5 cm
    over the hubs' plane (the turbo hatch's rims at hub height carry a lens material). A side's cluster is chosen by its
    area times its distance from the centre line (the five's outer lamps over its inner pair, a tail lamp over the Clio's
    high brake lamp), a head at a headlamp's height first (HEADLAMP_HEIGHT); the socket is on its lens, under the
    cluster's area-weighted middle (on_surface). MEASURED_LAMPS overrides any. Otherwise the Godot version's rule from
    the body's box without its mirrors (box_rule, end_width). Returns [(name, position, source)]."""
    low, high = np.full(3, np.inf), np.full(3, -np.inf)
    for i in body_nodes:
        lo, hi = mesh_bounds(model, model["nodes"][i]["mesh"], worlds[i])
        low, high = np.minimum(low, lo), np.maximum(high, hi)
    ground = min((mesh_bounds(model, model["nodes"][i]["mesh"], worlds[i])[0][1] for i in wheel_nodes), default=low[1])
    hub = float(np.mean([h[1] for h in hubs])) if hubs else low[1] + 0.3
    length = high[2] - low[2]
    centre_x = (low[0] + high[0]) / 2.0
    own = LAMP_MATERIALS.get(key, {})
    cell = own.get("cell", 0.06)
    found = {}
    for front in (True, False):
        if front:
            sources = ((own["heads"][0], own["heads"]),) if "heads" in own else (("lens", ("GLP_Lens",)),)
        else:
            sources = ((own["tails"][0], own["tails"]),) if "tails" in own else (("brake", ("GLP_Brake",)), ("lens", ("GLP_Lens",)))
        for source, names in sources:
            triangles, tints, textured = lens_triangles(model, blob, body_nodes, worlds, names, colours=True)
            if not len(triangles):
                continue
            centre = triangles.mean(axis=1)
            area = 0.5 * np.linalg.norm(np.cross(triangles[:, 1] - triangles[:, 0], triangles[:, 2] - triangles[:, 0]), axis=1)
            keep = (centre[:, 2] < low[2] + 0.3 * length) if front else (centre[:, 2] > high[2] - 0.3 * length)
            keep &= centre[:, 1] > hub + 0.05
            red = is_red(tints, textured) if source == "brake" else np.ones(len(triangles), bool)
            picked = {}
            for name, on_side in zip(("LampHeadL", "LampHeadR") if front else ("LampTailL", "LampTailR"), (centre[:, 0] < centre_x, centre[:, 0] > centre_x)):
                chosen = keep & on_side
                if area[chosen & red].sum() >= 0.002:
                    chosen &= red
                side = triangles[chosen]
                if not len(side):
                    continue
                groups = [g for g in lens_clusters(side, cell) if g[1] >= 0.002]   # 20 cm2 and more
                if front:
                    groups = [g for g in groups if g[2][1] - ground >= HEADLAMP_HEIGHT] or groups
                if not groups:
                    continue
                members, _, middle = max(groups, key=lambda g: g[1] * abs(g[2][0] - centre_x))
                picked[name] = on_surface(side[members], middle[0], middle[1], front)
            if len(picked) == 2:
                for name, position in picked.items():
                    found[name] = (position, source)
                break
    for name, position in MEASURED_LAMPS.get(key, {}).items():
        found[name] = (position, "measured")
    box = {}
    if len(found) < len(LAMPS):
        box = box_rule(*end_width(model, blob, body_nodes, worlds, low, high), ground, low[2], high[2])
    return [(name, *(found[name] if name in found else (box[name], "box"))) for name in LAMPS]


def box_lamp_sockets(doc, blob, root):
    """The Godot version's lamp rule for a car with no prepared model (CarView.MeasureLamps on its lofted shell): the
    body's box from the lofted Shell, Bonnet and Outside meshes in the car's frame, its sides at its ends (end_width), the
    ground under its wheels."""
    worlds, _ = world_matrices(doc)
    inverse = np.linalg.inv(worlds[root])
    body, wheels = [], []
    stack = [(c, None) for c in doc["nodes"][root].get("children", [])]
    while stack:
        i, group = stack.pop()
        name = doc["nodes"][i].get("name", "")
        group = group or ("body" if name in ("Shell", "Bonnet", "Outside", "ModelShell") else "wheel" if name.startswith("Wheel") and name[5:].isdigit() else None)
        if "mesh" in doc["nodes"][i] and group:
            (body if group == "body" else wheels).append(i)
        stack.extend((c, group) for c in doc["nodes"][i].get("children", []))
    local = {i: inverse @ worlds[i] for i in body + wheels}
    if not body:
        return []
    low, high = np.full(3, np.inf), np.full(3, -np.inf)
    for i in body:
        lo, hi = mesh_bounds(doc, doc["nodes"][i]["mesh"], local[i])
        low, high = np.minimum(low, lo), np.maximum(high, hi)
    ground = min((mesh_bounds(doc, doc["nodes"][i]["mesh"], local[i])[0][1] for i in wheels), default=low[1])
    rule = box_rule(*end_width(doc, blob, body, local, low, high), ground, low[2], high[2])
    return [(name, rule[name], "box") for name in LAMPS]


def brake_parts(model, blob, worlds, body_nodes):
    """The parts of a model's brake lamps' material that are no tail lamp (0.77), each given a copy of the material of
    its own, appended to the model's: a cluster in the front half of the car (the V8's side markers, the five's front
    indicators and repeaters) GLP_BrakeOff, which never lights; one on the centre line behind (the Clio's high-mounted
    stop lamp, the 458's) GLP_BrakeStop, which lights with the brake alone. The tail lamps keep GLP_Brake, lit while the
    lamps are (M_BrakeLamp: until 0.77 every slot of the material lit as a tail lamp all night, the Clio's as if it braked
    all the time, and red with the brake by day). Returns the Merger's parts, {material: [(part's material, boxes)]},
    and a count of the parts' clusters."""
    low, high = np.full(3, np.inf), np.full(3, -np.inf)
    for i in body_nodes:
        lo, hi = mesh_bounds(model, model["nodes"][i]["mesh"], worlds[i])
        low, high = np.minimum(low, lo), np.maximum(high, hi)
    centre_x, middle_z = (low[0] + high[0]) / 2.0, (low[2] + high[2]) / 2.0
    parts, counts = {}, {"GLP_BrakeStop": 0, "GLP_BrakeOff": 0}
    for index, material in enumerate(list(model.get("materials", []))):
        if (material.get("name") or "") != "GLP_Brake":
            continue
        triangles = []
        for i in body_nodes:
            for primitive in model["meshes"][model["nodes"][i]["mesh"]]["primitives"]:
                if primitive.get("material") != index or primitive.get("mode", 4) != 4 or "POSITION" not in primitive["attributes"]:
                    continue
                position = read_accessor(model, blob, primitive["attributes"]["POSITION"])
                indices = read_accessor(model, blob, primitive["indices"]).astype(np.int64).ravel() if "indices" in primitive else np.arange(len(position))
                indices = indices[: len(indices) // 3 * 3].reshape(-1, 3)
                triangles.append(((np.hstack([position, np.ones((len(position), 1))]) @ worlds[i].T)[:, :3])[indices])
        if not triangles:
            continue
        triangles = np.concatenate(triangles)
        boxes = {"GLP_BrakeStop": [], "GLP_BrakeOff": []}
        for members, _, middle in lens_clusters(triangles):
            points = triangles[members].reshape(-1, 3)
            box = (points.min(0) - 0.005, points.max(0) + 0.005)
            if middle[2] < middle_z:
                boxes["GLP_BrakeOff"].append(box)
            elif abs(middle[0] - centre_x) < 0.2 and points[:, 0].max() - points[:, 0].min() < 1.0:
                boxes["GLP_BrakeStop"].append(box)
        for name, found in boxes.items():
            if found:
                model["materials"].append(dict(copy.deepcopy(material), name=name))
                parts.setdefault(index, []).append((len(model["materials"]) - 1, found))
                counts[name] += len(found)
    return parts, counts


def describe_lamps(sockets, parts=None):
    """One line for the build's log: where the heads and tails came from, and the brake lamps' other parts."""
    kinds = {name: source for name, _, source in sockets}
    line = f"lamps: heads {kinds.get('LampHeadL', '-')}, tails {kinds.get('LampTailL', '-')}"
    if parts:
        line += f", brake lens parts: {parts['GLP_BrakeStop']} stop lamp(s), {parts['GLP_BrakeOff']} front marker(s)"
    return line


def compact(doc, blob):
    """The binary chunk with only what the remaining meshes and images use: the model's own meshes, baked into new ones,
    would otherwise travel in the file a second time (a 205 went from 46 to 110 MB)."""
    used = set()
    for mesh in doc.get("meshes", []):
        for primitive in mesh["primitives"]:
            used.update(primitive["attributes"].values())
            if "indices" in primitive:
                used.add(primitive["indices"])
            for target in primitive.get("targets", []):
                used.update(target.values())
    for skin in doc.get("skins", []):
        if "inverseBindMatrices" in skin:
            used.add(skin["inverseBindMatrices"])
    accessor_map = {old: new for new, old in enumerate(sorted(used))}
    accessors = [doc["accessors"][old] for old in sorted(used)]
    views = {a["bufferView"] for a in accessors if "bufferView" in a} | {i["bufferView"] for i in doc.get("images", []) if "bufferView" in i}
    view_map = {}
    packed = bytearray()
    new_views = []
    for old in sorted(views):
        view = dict(doc["bufferViews"][old])
        start = view.get("byteOffset", 0)
        packed += b"\0" * (-len(packed) % 8)
        data = blob[start:start + view["byteLength"]]
        view["byteOffset"] = len(packed)
        view["buffer"] = 0
        packed += data
        view_map[old] = len(new_views)
        new_views.append(view)
    for accessor in accessors:
        if "bufferView" in accessor:
            accessor["bufferView"] = view_map[accessor["bufferView"]]
    for image in doc.get("images", []):
        if "bufferView" in image:
            image["bufferView"] = view_map[image["bufferView"]]
    for mesh in doc.get("meshes", []):
        for primitive in mesh["primitives"]:
            primitive["attributes"] = {k: accessor_map[v] for k, v in primitive["attributes"].items()}
            if "indices" in primitive:
                primitive["indices"] = accessor_map[primitive["indices"]]
            if "targets" in primitive:
                primitive["targets"] = [{k: accessor_map[v] for k, v in t.items()} for t in primitive["targets"]]
    for skin in doc.get("skins", []):
        if "inverseBindMatrices" in skin:
            skin["inverseBindMatrices"] = accessor_map[skin["inverseBindMatrices"]]
    doc["accessors"] = accessors
    doc["bufferViews"] = new_views
    return packed


def prune(doc):
    """Drops the nodes no scene reaches (the lofted car taken off PlayerCar) and the meshes no node draws, re-indexing
    the rest. Left in the file, the importer still reads them: their names (Wheel0, Spin, Dials, RearMirror) made it
    give the model's nodes of the same names a unique suffix, and the level found no wheels and no dials (0.74)."""
    reachable = []
    stack = [n for scene in doc.get("scenes", []) for n in scene.get("nodes", [])]
    seen = set()
    while stack:
        i = stack.pop()
        if i in seen:
            continue
        seen.add(i)
        reachable.append(i)
        stack.extend(doc["nodes"][i].get("children", []))
    node_map = {old: new for new, old in enumerate(sorted(reachable))}
    nodes = []
    for old in sorted(reachable):
        node = doc["nodes"][old]
        if "children" in node:
            node["children"] = [node_map[c] for c in node["children"]]
        nodes.append(node)
    doc["nodes"] = nodes
    for scene in doc.get("scenes", []):
        scene["nodes"] = [node_map[n] for n in scene.get("nodes", [])]
    for skin in doc.get("skins", []):
        skin["joints"] = [node_map[j] for j in skin.get("joints", []) if j in node_map]
        if "skeleton" in skin:
            skin["skeleton"] = node_map.get(skin["skeleton"], skin["joints"][0] if skin["joints"] else 0)
    if doc.get("animations"):
        doc.pop("animations")   # the export has none; a stale target would point at a removed node
    used_meshes = sorted({n["mesh"] for n in nodes if "mesh" in n})
    mesh_map = {old: new for new, old in enumerate(used_meshes)}
    doc["meshes"] = [doc["meshes"][old] for old in used_meshes]
    for node in nodes:
        if "mesh" in node:
            node["mesh"] = mesh_map[node["mesh"]]
    return len(seen), len(used_meshes)


def main():
    source, target, car = sys.argv[1:4]
    folder = sys.argv[4] if len(sys.argv) > 4 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "game", "assets", "models")
    doc, blob = load(source)
    key, path = model_path(folder, car)
    if path is None:
        # The lamps' places from the lofted body's box (0.77), the rest as it was.
        root = find_node(doc, "PlayerCar")
        names = {doc["nodes"][c].get("name", "") for c in doc["nodes"][root].get("children", [])}
        sockets = [] if "LampHeadL" in names else box_lamp_sockets(doc, blob, root)
        for name, position, _ in sockets:
            doc["nodes"].append({"name": name, "translation": [float(v) for v in position]})
            doc["nodes"][root].setdefault("children", []).append(len(doc["nodes"]) - 1)
        if source != target or sockets:
            save(target, doc, blob)
        print(f"{car}: no prepared model in {folder}, the car stays as it was{', ' + describe_lamps(sockets) if sockets else ''}")
        return
    model, model_blob = load(path)
    worlds, _ = world_matrices(model)
    names = {i: n.get("name", "") for i, n in enumerate(model["nodes"])}
    car_model = next((i for i, n in names.items() if n == "CarModel"), None)
    if car_model is None:
        sys.exit(f"{path}: no CarModel node (not a prepared car model)")
    top = {names[c]: c for c in model["nodes"][car_model].get("children", [])}
    for required in ("Wheel0", "Wheel1", "Wheel2", "Wheel3"):
        if required not in top:
            sys.exit(f"{path}: no {required}")

    root = find_node(doc, "PlayerCar")
    doc["nodes"][root]["children"] = []
    for material in model.get("materials", []):
        correct_material(material)
    special = {"Wheel0", "Wheel1", "Wheel2", "Wheel3", "Steering", "Eye", "Dials", "Mirror", "MirrorHung"}
    body_nodes = [i for name, c in top.items() if name not in special for i in mesh_nodes(model, c)]
    # The brake lamps' material's parts that are no tail lamp get materials of their own before the model's are merged
    # (0.77); the body's bake sorts their triangles out.
    brake, brake_counts = brake_parts(model, model_blob, worlds, body_nodes)
    # GLP_DEBUG_COLOURS="name=r,g,b;...": paints the named materials flat, to find which one a pixel is (a diagnosis aid).
    for item in filter(None, os.environ.get("GLP_DEBUG_COLOURS", "").split(";")):
        name, rgb = item.split("=")
        for material in model.get("materials", []):
            if material.get("name") == name:
                pbr = material.setdefault("pbrMetallicRoughness", {})
                pbr.pop("baseColorTexture", None)
                pbr["baseColorFactor"] = [float(v) for v in rgb.split(",")] + [1.0]
                pbr["metallicFactor"], pbr["roughnessFactor"] = 0.0, 1.0
    offsets = merge(doc, blob, model, model_blob)
    # The level paints a flat GLP_Paint in the model's colour and lays a clear coat over a textured one (a livery or a
    # colour atlas); the imported material always carries a texture parameter, so the name says which it is.
    for material in doc["materials"][offsets["materials"]:]:
        if material.get("name") == "GLP_Paint":
            textured = "baseColorTexture" in material.get("pbrMetallicRoughness", {})
            material["name"] = "GLP_PaintTex" if textured else "GLP_PaintFlat"
    b = Builder(doc, blob)
    count = {"body": 0, "glass": 0}
    glass_materials = {i for i, m in enumerate(model.get("materials", [])) if glassy(m)}

    def baked(label, items, eye=None, parts=None):
        """One node per kind of surface: the opaque parts (Nanite) and the glass (translucent, which Nanite cannot draw).
        With the driver's eye (the body), a window drawn as two layers keeps the outer one (Merger.drop_inner_panes);
        with parts, the brake lamps' material is split (brake_parts)."""
        opaque, glass = Merger(model, model_blob, parts), Merger(model, model_blob, parts)
        for i, matrix in items:
            mesh = model["meshes"][model["nodes"][i]["mesh"]]
            solid = [p for p in mesh["primitives"] if p.get("material", -1) not in glass_materials]
            clear = [p for p in mesh["primitives"] if p.get("material", -1) in glass_materials]
            for target, primitives in ((opaque, solid), (glass, clear)):
                if primitives:
                    model["meshes"].append({"primitives": primitives})
                    target.add(len(model["meshes"]) - 1, matrix)
                    model["meshes"].pop()
        if eye is not None:
            count["inner panes"] = glass.drop_inner_panes(eye)
        nodes = []
        for prefix, merger in (("Mdl_", opaque), ("Mdl_Glass_", glass)):
            mesh = merger.build(b, prefix + label, offsets["materials"])
            if mesh is not None:
                nodes.append(b.node(prefix + label, None, mesh))
                count["glass" if prefix == "Mdl_Glass_" else "body"] += 1
        return nodes

    children = []

    # The body, its cabin and whatever else hangs on it (arch liners, the plate), baked in the car's frame.
    body = baked("Body", [(i, worlds[i]) for i in body_nodes], worlds[top["Eye"]][:3, 3] if "Eye" in top else None, brake)
    children.append(b.node("ModelBody", None, None, body))

    # The wheels on their hubs, each spinning in a Spin node of its own; another car's model is fitted to these tyres.
    spec = SPECS.get(car)
    for w in range(4):
        hub = worlds[top[f"Wheel{w}"]][:3, 3]
        recentre = translation(*(-hub))
        parts = baked(f"Wheel{w}", [(i, recentre @ worlds[i]) for i in mesh_nodes(model, top[f"Wheel{w}"])])
        spin_matrix = None
        if key != car and spec is not None:
            radius = 0.0
            for i in mesh_nodes(model, top[f"Wheel{w}"]):
                low, high = mesh_bounds(model, model["nodes"][i]["mesh"], worlds[i])
                radius = max(radius, max(high[1] - low[1], high[2] - low[2]) / 2.0)
            if radius > 0.1:
                spin_matrix = np.diag([*([float(np.clip(spec["radius"] / radius, 0.85, 1.15))] * 3), 1.0])
        spin = b.node("Spin", spin_matrix, None, parts)
        children.append(b.node(f"Wheel{w}", translation(*hub), None, [spin]))

    # The steering wheel: CarView turns Steering/Spin about its local y.
    if "Steering" in top:
        steering = top["Steering"]
        spin_index = next((c for c in model["nodes"][steering].get("children", []) if names[c] == "Spin"), None)
        if spin_index is not None:
            parts = baked("Steer", [(i, np.linalg.inv(worlds[spin_index]) @ worlds[i]) for i in mesh_nodes(model, spin_index)])
            spin = b.node("Spin", np.linalg.inv(worlds[steering]) @ worlds[spin_index], None, parts)
            children.append(b.node("Steering", worlds[steering], None, [spin]))

    if "Eye" in top:
        children.append(b.node("Eye", translation(*worlds[top["Eye"]][:3, 3])))

    # The instrument panel's face, as CarView.MountModelDials hangs it.
    if "Dials" in top:
        frame = worlds[top["Dials"]]
        width = float(np.linalg.norm(frame[:3, 0]))
        face = b.material("GLP_DialsFace", (0.035, 0.035, 0.04), 0.0, 0.85)
        children.append(b.node("Dials", orthonormal(frame), b.quad("DialsFace", width, width * PANEL[1] / PANEL[0], face)))

    # The rear-view mirror's glass on the prepared node (the game's housing and stalk too where it hangs its own).
    mirror_key = "Mirror" if "Mirror" in top else "MirrorHung" if "MirrorHung" in top else None
    if mirror_key is not None:
        frame = worlds[top[mirror_key]]
        sx, sy = float(np.linalg.norm(frame[:3, 0])), float(np.linalg.norm(frame[:3, 1]))
        glass_w, glass_h = (sx, sy) if abs(sx - 1.0) > 0.001 and sx > 0.01 and sy > 0.005 else MIRROR_GLASS
        dark = b.material("GLP_MirrorGlass", (0.02, 0.02, 0.022), 0.0, 0.35)
        parts = [b.node("MirrorGlass", translation(0.0, 0.0, MIRROR_OFFSET), b.quad("MirrorGlass", glass_w, glass_h, dark))]
        if mirror_key == "MirrorHung":
            housing = b.material("GLP_MirrorHousing", (0.03, 0.03, 0.032), 0.0, 0.55)
            parts.insert(0, b.node("MirrorStalk", translation(0.0, 0.045, -0.01), b.box("MirrorStalk", (0.016, 0.07, 0.016), housing)))
            parts.insert(1, b.node("MirrorHousing", None, b.box("MirrorHousing", (glass_w + 0.02, glass_h + 0.02, 0.03), housing)))
        children.append(b.node("RearMirror", orthonormal(frame) @ translation(0.0, 0.0, -MIRROR_OFFSET), None, parts))

    # The lamps' places (0.77): empty nodes the level tags for the game's beams and glows (build_scene.py rig_car).
    wheel_nodes = [i for w in range(4) for i in mesh_nodes(model, top[f"Wheel{w}"])]
    hubs = [worlds[top[f"Wheel{w}"]][:3, 3] for w in range(4)]
    sockets = lamp_sockets(model, model_blob, worlds, body_nodes, wheel_nodes, hubs, key)
    for name, position, _ in sockets:
        children.append(b.node(name, translation(*position)))

    doc["nodes"][root]["children"] = children
    prune(doc)
    save(target, doc, compact(doc, b.blob))
    print(f"{car}: {os.path.basename(path)}{' (fitted to this car)' if key != car else ''}: {count['body']} body mesh node(s), "
          f"{count['glass']} glass ({count.get('inner panes', 0)} inner pane triangles dropped), steering {'yes' if 'Steering' in top else 'no'}, eye {'yes' if 'Eye' in top else 'no'}, "
          f"dials {'yes' if 'Dials' in top else 'no'}, mirror {mirror_key or 'none'}, {describe_lamps(sockets, brake_counts)} -> {target}")


if __name__ == "__main__":
    main()

"""Prepares the real car models the game draws in place of its lofted bodies (CarView.Model.cs):
game/assets/models/<car>.glb for every catalogue car that tools/car_models.json names, from the CC BY source files
in unreal/Import/Models (fetched by unreal/Scripts/fetch_models.py, each with its LICENSE.txt; credited in
THIRD_PARTY.md). The prepared files are build output (tools/build.ps1 runs this), not kept in the repository.

Usage: python tools/prepare_car_models.py [--inspect] [car ...]

Each file holds the model in the car node's frame (car_models.rig: faced to -Z, scaled to the simulation's
wheelbase, hubs at the rest height, origin at the centre of gravity), its scene:

  CarModel
    Body            every mesh that does not turn; the paint's materials renamed GLP_Paint, the windows' GLP_Glass
                    and clear lamp lenses GLP_Lens, for CarView to give them the game's own (a paint drawn from a
                    texture carries the mask CarView recolours it by in its emissive slot: paint_mask)
    Wheel0..3       at the hubs, in the simulation's order (front left, front right, rear left, rear right), the
                    tyre and what rolls with it centred on each (CarView hangs them on its spinning pivots)
    Steering        at the steering wheel's centre, its +Y the column pointing at the driver; its child Spin holds
                    the wheel's meshes and turns about +Y (CarView.TurnSteeringWheel)
    Eye             the driver's eyes (CarView.Eye)
    Dials           the instrument panel's face: +Z towards the eyes, its x scale the face's width in metres
                    (CarView puts the live InstrumentCluster there)
      Gear          (optional) where the gear readout's box starts on the panel (dials gear_top in the entry)
    Mirror          the rear-view mirror's glass: +Z towards the driver (CarView hangs RearMirror's glass there)
    Liner0..3       (entry liners: true) at the hubs, scaled (outboard reach, radius, inboard reach): CarView builds
                    a dark wheel-arch liner over each, for a model whose arches are open behind the tyres (the 86's)
    Plate           (entry plate: {centre, size}) the rear number plate's centre, +Z facing back, scaled to its size

A model without a cabin of its own ("interior": false) gets neither Steering, Eye, Dials nor Mirror: the game
builds its own cabin inside the model's shell. --inspect prints the meshes around the driver's seat with their
boxes in the car frame, for writing a model's entry.

Where the preparation put each car's eyes, dials and mirror goes to tools/car_cockpits.json (kept in the
repository, rewritten for every car prepared), for the tests: the models themselves are not in the repository.
"""
import io
import json
import math
import os
import re
import site
import sys

sys.path.append(site.getusersitepackages())
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from scipy import ndimage
from scipy.sparse import coo_matrix
from scipy.sparse.csgraph import connected_components

import car_models as cm

ROOT = os.environ.get("GLP_ROOT") or os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
SOURCE = os.path.join(ROOT, "unreal", "Import", "Models")
TARGET = os.path.join(ROOT, "game", "assets", "models")
CONFIG = os.path.join(ROOT, "tools", "car_models.json")
COCKPITS = os.path.join(ROOT, "tools", "car_cockpits.json")

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


def is_entry_glass(entry, name):
    """A material the entry names as a window (glass: material-name parts, "==Name" exactly), for the models whose
    windows are an opaque material named like any other (the first 458 model's black "Material.048")."""
    return any((name == w[2:]) if w.startswith("==") else (w.lower() in name.lower()) for w in entry.get("glass", []))


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


def srgb_to_linear(c):
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def linear_to_srgb(c):
    c = np.clip(c, 0.0, 1.0)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * c ** (1.0 / 2.4) - 0.055)


LUMA = np.array([0.2126, 0.7152, 0.0722])


def texture_pixels(model, blob, texture_index):
    """A texture's image as linear RGB floats, its rows top first (as glTF's texture coordinates count them)."""
    image = model["images"][model["textures"][texture_index]["source"]]
    view = model["bufferViews"][image["bufferView"]]
    start = view.get("byteOffset", 0)
    data = bytes(blob[start: start + view["byteLength"]])
    return srgb_to_linear(np.asarray(Image.open(io.BytesIO(data)).convert("RGB"), dtype=np.float64) / 255.0)


def uv_coverage(model, blob, primitives, size):
    """The texels a set of primitives' triangles cover (TEXCOORD_0, wrapped into the unit square)."""
    width, height = size
    cover = Image.new("L", (width, height), 0)
    draw = ImageDraw.Draw(cover)
    for primitive in primitives:
        if primitive.get("mode", 4) != 4 or "TEXCOORD_0" not in primitive["attributes"]:
            continue
        uv = cm.accessor_array(model, blob, primitive["attributes"]["TEXCOORD_0"])
        if "indices" in primitive:
            indices = cm.accessor_array(model, blob, primitive["indices"]).astype(np.int64).reshape(-1)
        else:
            indices = np.arange(len(uv))
        triangles = np.mod(uv[indices[: len(indices) // 3 * 3].reshape(-1, 3)], 1.0) * [width, height]
        for t in triangles:
            draw.polygon([(float(t[0, 0]), float(t[0, 1])), (float(t[1, 0]), float(t[1, 1])), (float(t[2, 0]), float(t[2, 1]))], fill=255)
    return np.asarray(cover) > 0


def chromaticity(rgb):
    return rgb / np.maximum(rgb.sum(axis=-1, keepdims=True), 1e-6)


def paint_mask(model, blob, material, primitives, settings, label):
    """Which texels of a paint texture are the body colour, and how light each is against it.

    Two of the models carry their paint in a texture atlas with everything else (the 205's lamps, badges, pinstripe
    and black trim; the VAZ's baked shading, grime and chrome), and the Delta's paint texture is white with the
    Martini lettering and its grime on it. The game recolours them (CarView.TexturePaint) from this mask: R the
    paint's weight (the texels of the key colour, the rest left as the model has them), G the texel's lightness
    against the key, halved (a texel half as light as the key is 0.25: a panel line, baked shade or grime keeps its
    depth in any colour). The key is the colour the entry names (paint_atlas), refined to the median of the texels
    the paint's triangles cover that match it, or, with none named, the median of all they cover (a white paint
    texture). Shading leaves a texel's chromaticity (r, g and b over their sum, in linear light) as it is, so that is
    what the match is made on; a texel too dark to have a hue, or far lighter than the key (a white badge on it), is
    not paint: black trim under a navy paint is nearly as light as the paint and told from it by its grey alone.
    Returns the mask as an RGB image (8 bits a channel) and the key (linear RGB)."""
    pbr = material["pbrMetallicRoughness"]
    pixels = texture_pixels(model, blob, pbr["baseColorTexture"]["index"]) * np.array(pbr.get("baseColorFactor", [1.0, 1.0, 1.0, 1.0])[:3])
    height, width = pixels.shape[:2]
    covered = uv_coverage(model, blob, primitives, (width, height))
    luma = pixels @ LUMA
    tolerance = settings.get("tolerance", 0.06)
    key = np.median(pixels[covered], axis=0) if covered.any() else np.median(pixels.reshape(-1, 3), axis=0)
    if "key" in settings:
        guess = srgb_to_linear(np.array(settings["key"], dtype=np.float64) / 255.0)
        near = covered & (np.linalg.norm(chromaticity(pixels) - chromaticity(guess), axis=-1) < tolerance) \
            & (luma > 0.3 * (guess @ LUMA)) & (luma < 3.0 * (guess @ LUMA))
        key = np.median(pixels[near], axis=0) if near.sum() > 100 else guess
    key_luma = float(key @ LUMA)
    distance = np.linalg.norm(chromaticity(pixels) - chromaticity(key), axis=-1)
    hue = 1.0 - np.clip((distance - tolerance) / tolerance, 0.0, 1.0)
    lit = np.clip((luma / key_luma - 0.12) / 0.18, 0.0, 1.0)
    not_badge = 1.0 - np.clip(luma / key_luma - 3.0, 0.0, 1.0)
    weight = hue * lit * not_badge
    # A median over 3 x 3 texels drops lone matches in the noise (a JPEG's blocks in a dark atlas) and keeps the
    # edges of the paint sharp.
    weight = np.asarray(Image.fromarray(np.round(weight * 255).astype(np.uint8)).filter(ImageFilter.MedianFilter(3)), dtype=np.float64) / 255.0
    weight, specks = fill_specks(weight, luma, key_luma)
    # The lightness, smoothed a little more where the key is dark: a JPEG's noise in a navy is a tenth of its light,
    # which a light colour would show as mottling.
    ratio = Image.fromarray(np.round(np.clip(luma / key_luma, 0.0, 2.0) * 127.5).astype(np.uint8))
    shade = np.asarray(ratio.filter(ImageFilter.GaussianBlur(0.9 if key_luma < 0.05 else 0.4)), dtype=np.uint8)
    image = np.zeros((height, width, 3), dtype=np.uint8)
    image[..., 0] = np.round(weight * 255).astype(np.uint8)
    image[..., 1] = shade
    painted = covered & (weight > 0.5)
    spread = (np.percentile(luma[painted] / key_luma, 5), np.percentile(luma[painted] / key_luma, 95)) if painted.any() else (0.0, 0.0)
    print(f"  paint texture {label}: key {np.round(linear_to_srgb(key) * 255).astype(int).tolist()} (sRGB), "
          f"{painted.sum() / max(covered.sum(), 1):.0%} of the texels its triangles cover are paint, "
          f"{spread[0]:.2f}-{spread[1]:.2f} of the key's lightness, {specks} specks of grime on it painted")
    return image, key


def fill_specks(weight, luma, key_luma, largest=128, lightest=1.5):
    """The grime on a paint texture counted as paint: the small patches that are not paint (or only partly), wholly
    inside the paint and no lighter than it. The 205's navy has dozens of dirt specks and a smudge or two, greyer than
    the paint, and the VAZ's beige hundreds: they kept their texel, invisible in the model's own colour but dark navy
    dots and blotches in a white, a yellow or a silver. The paint's lightness (the mask's green) carries their
    depth, so the grime darkens the new colour instead. A badge, chrome or a lamp is lighter than the paint and the
    black trim and vents larger (the vent on the 205's nose is 508 texels of its 1024 x 1024, a speck at most 128); a
    "no lighter" of half again the key's luminance (a fifth in sRGB) takes in the specks' grey on a navy as dark as
    the 205's, a few times the JPEG's noise in it. Nor is a patch too dark to have a hue (the paint's own cut,
    paint_mask's lit): the Delta's Martini lettering is a white texture's black, and a letter is a speck's size.
    Returns the weight and how many patches were filled."""
    patches, count = ndimage.label(weight < 0.98)
    if count == 0:
        return weight, 0
    # A patch's size is that of its core, the texels that are not paint at all: the soft rim the median leaves round a
    # speck would otherwise count a 96-texel smudge as 144.
    sizes = np.bincount(patches.ravel(), weights=(weight < 0.5).ravel(), minlength=count + 1)
    lightness = np.zeros(count + 1)
    lightness[1:] = ndimage.mean(luma, patches, np.arange(1, count + 1))
    speck = (sizes <= largest) & (lightness <= lightest * key_luma) & (lightness >= 0.12 * key_luma)
    speck[0] = False
    # A patch at the texture's edge may go on across the wrap: not known to be inside the paint.
    speck[np.unique(np.concatenate([patches[0], patches[-1], patches[:, 0], patches[:, -1]]))] = False
    return np.where(speck[patches], 1.0, weight), int(speck.sum())


def embed_paint_mask(model, blob, material, image, key):
    """Puts a paint mask (paint_mask) in the material's emissive slot with a black emission factor, so that Godot's
    importer extracts and mipmaps it with the model's other textures and CarView finds it on the imported material
    (nothing glows if the material is drawn as it is); the key colour (linear RGB) goes in the material's extras, for
    the game's factory paint (the colour of the debris of a crash)."""
    buffer = io.BytesIO()
    Image.fromarray(image).save(buffer, format="PNG", optimize=True)
    data = buffer.getvalue()
    offset = len(blob) + (-len(blob) % 4)
    blob += b"\0" * (offset - len(blob))
    blob += data
    model["bufferViews"].append({"buffer": 0, "byteOffset": offset, "byteLength": len(data)})
    model.setdefault("images", []).append({"name": "glp_paint_mask", "mimeType": "image/png", "bufferView": len(model["bufferViews"]) - 1})
    source = model["textures"][material["pbrMetallicRoughness"]["baseColorTexture"]["index"]]
    texture = {"source": len(model["images"]) - 1}
    if "sampler" in source:
        texture["sampler"] = source["sampler"]
    model["textures"].append(texture)
    material["emissiveTexture"] = {"index": len(model["textures"]) - 1}
    material["emissiveFactor"] = [0.0, 0.0, 0.0]
    material.setdefault("extras", {})["glp_paint_key"] = [round(float(v), 5) for v in key]


def mask_weight(mask, uv):
    """A paint mask's weight (0-1) at texture coordinates (wrapped), from the nearest texel."""
    height, width = mask.shape[:2]
    x = (np.mod(uv[:, 0], 1.0) * width).astype(np.int64).clip(0, width - 1)
    y = (np.mod(uv[:, 1], 1.0) * height).astype(np.int64).clip(0, height - 1)
    return mask[y, x, 0] / 255.0


def orient_paint(model, blob, nodes, leave=()):
    """Turns painted parts that face into the car outwards: some models mirror a door or a wing without flipping
    it, and the paint, drawn from both faces with a dark lining behind (CarView.ModelPaint), showed that side's
    door as black. A part counts as turned when less than a third of its area faces away from the car's middle;
    its triangles are wound the other way and its normals (and tangents) reversed. A paint atlas (leave) is left as
    it is: its parts are anything the texture holds, a dashboard as well as a door, and are drawn from their front."""
    parents = {c: i for i, n in enumerate(nodes) for c in n.get("children", [])}
    paint = {i for i, m in enumerate(model["materials"]) if m.get("name") == "GLP_Paint" and i not in leave}
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


def turn_pieces(model, blob, nodes, sources, name_of, words):
    """Turns outwards the pieces of the named parts (turn: node selectors) that face into the car, after orient_paint.

    orient_paint turns a whole part, but a model may join pieces modelled right and pieces modelled inside out in one
    mesh: the Renault 12's four doors are one mesh, and the lower half of each door, under the crease where the
    model's white side strips lie (dropped), faces into the car. A piece is a run of triangles that share vertices;
    one of 0.01 m2 or more counts as inside out when less than a third of its area faces away from the car's middle,
    by its winding as drawn and by its normals alike, and its triangles are then wound the other way and its normals
    (and tangents) reversed. Smaller slivers at a part's edges and lips stay as they are: the car's middle cannot
    tell them from faces meant to look into the cabin.

    The winding is taken as drawn, not reversed for a node whose matrix mirrors (the doors' does). With culling off
    (ModelPaint's cull_disabled) Godot keeps a mirrored instance's winding as it is and swaps only the side it culls
    (scene_shader_forward_clustered.cpp), so FRONT_FACING was false on those halves seen from outside, and inside its
    cabin box (CarView.SetPaintCabin) the paint drew them in its dark lining: a black band along both sides, from the
    box's floor, 0.55 m under the eyes, up to the crease."""
    parents = {c: i for i, n in enumerate(nodes) for c in n.get("children", [])}
    middle = np.array([0.0, 0.1, 0.0])
    for index, source in sources.items():
        if not cm.matches(source, name_of(source), words):
            continue
        node = nodes[index]
        mesh = model["meshes"][node["mesh"]]
        world = node_world(nodes, index, parents)
        inverse = np.linalg.inv(world[:3, :3])
        primitives = []
        pieces = 0
        area_turned = 0.0
        for primitive in mesh["primitives"]:
            attributes = primitive["attributes"]
            if primitive.get("mode", 4) != 4 or "NORMAL" not in attributes:
                primitives.append(primitive)
                continue
            positions = cm.accessor_array(model, blob, attributes["POSITION"])
            if "indices" in primitive:
                indices = cm.accessor_array(model, blob, primitive["indices"]).astype(np.int64).reshape(-1)
            else:
                indices = np.arange(len(positions))
            triangles = indices[: len(indices) // 3 * 3].reshape(-1, 3)
            normals = cm.accessor_array(model, blob, attributes["NORMAL"]).astype(np.float32)
            p = (np.c_[positions, np.ones(len(positions))] @ world.T)[:, :3]
            a, b, c = p[triangles[:, 0]], p[triangles[:, 1]], p[triangles[:, 2]]
            wound = np.cross(b - a, c - a)
            area = np.linalg.norm(wound, axis=1) / 2
            away = (a + b + c) / 3 - middle
            by_winding = (wound * away).sum(axis=1) > 0
            # Normals go to the car frame by the matrix's inverse transpose (rows times the inverse).
            by_normals = ((normals.astype(np.float64) @ inverse)[triangles].sum(axis=1) * away).sum(axis=1) > 0
            edges = np.concatenate([triangles[:, [0, 1]], triangles[:, [1, 2]]])
            graph = coo_matrix((np.ones(len(edges)), (edges[:, 0], edges[:, 1])), shape=(len(positions), len(positions)))
            label = connected_components(graph, directed=False)[1][triangles[:, 0]]
            count = int(label.max()) + 1
            total = np.bincount(label, weights=area, minlength=count)
            inside_out = (total >= 0.01) & (np.bincount(label, weights=area * by_winding, minlength=count) < total / 3) \
                & (np.bincount(label, weights=area * by_normals, minlength=count) < total / 3)
            flip = inside_out[label]
            if not flip.any():
                primitives.append(primitive)
                continue
            order = triangles.copy()
            order[flip] = order[flip][:, [0, 2, 1]]
            # A piece's vertices are its own (pieces are joined by shared vertices), so reversing them leaves the rest.
            turned = np.unique(triangles[flip])
            normals[turned] *= -1.0
            part = dict(primitive)
            part["attributes"] = dict(attributes)
            part["indices"] = append_accessor(model, blob, order.reshape(-1).astype(np.uint32), 5125, "SCALAR", 34963)
            part["attributes"]["NORMAL"] = append_accessor(model, blob, normals, 5126, "VEC3", 34962)
            if "TANGENT" in attributes:
                tangents = cm.accessor_array(model, blob, attributes["TANGENT"]).astype(np.float32)
                tangents[turned, :3] *= -1.0
                part["attributes"]["TANGENT"] = append_accessor(model, blob, tangents, 5126, "VEC4", 34962)
            primitives.append(part)
            pieces += int(np.count_nonzero(inside_out))
            area_turned += float(area[flip].sum())
        if pieces:
            # The mesh may be drawn by other nodes too: this node gets its own copy.
            model["meshes"].append(dict(mesh, primitives=primitives))
            node["mesh"] = len(model["meshes"]) - 1
            print(f"  {node.get('name', '?')}: {pieces} piece(s) facing into the car turned outwards ({area_turned:.3f} m2)")


def split_lining(model, blob, nodes, eye, wheel, masks=None):
    """The painted faces inside the cabin that face into it (the pillars' and the door frames' inner sides, the roof's
    underside, the screen header) take a dark trim, GLP_Lining: models paint whole shells, and from the seat the
    pillars showed in the body colour, with the sun's shadow flickering on them. A face counts when its centre is in
    the glasshouse (over the beltline, between the dashboard and the seats' backs) and it faces the cabin; on a paint
    texture (masks, by material) only where its texture is paint, so the trim and the seals the texture draws stay."""
    masks = masks or {}
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
            if primitive["material"] in masks and "TEXCOORD_0" in primitive["attributes"]:
                uv = cm.accessor_array(model, blob, primitive["attributes"]["TEXCOORD_0"])
                inner &= mask_weight(masks[primitive["material"]], uv[triangles].mean(axis=1)) > 0.5
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
    rd = cm.rig(donor, donor_entry, spec, car, donor_blob)
    to_car = rd["to_car"]
    if donor_entry.get("mirror_x"):
        to_car = np.diag([-1.0, 1.0, 1.0, 1.0]) @ to_car
    shift = matrix(translation=cabin.get("offset", [0.0, 0.0, 0.0]), scale=cabin.get("scale", [1.0, 1.0, 1.0]))
    words = [s.lower() for s in cabin["nodes"]]
    # Parts drawn only from the driver's seat (CarView: nodes named "Inside_..."): a donor's part that pokes through
    # the shell, as the Esprit's door cards stand out over the Testarossa's doors as a cream bar from outside.
    inside_words = [s.lower() for s in cabin.get("inside_only", [])]
    # The donor's seats where their names do not say so (seats: its parts named "Seat_..."), for the eyes to pass over:
    # the 570S's two buckets are one mesh, Object_16, 1.34 m across, and the 458 that borrows them had its eyes set 13 cm
    # under their tops, 0.86 m over the road, where the binnacle hid the road (models13).
    seat_words = [s.lower() for s in cabin.get("seats", [])]
    picked = [i for i in rd["body_nodes"] if cm.matches(i, rd["name_of"](i), words)]
    offsets = cm.merge(model, blob, donor, donor_blob)
    added = []
    for i in picked:
        name = rd["name_of"](i)
        if inside_words and cm.matches(i, name, inside_words):
            name = "Inside_" + name
        elif seat_words and cm.matches(i, name, seat_words):
            name = "Seat_" + name
        model["nodes"].append({"name": name, "mesh": donor["nodes"][i]["mesh"] + offsets["meshes"]})
        k = len(model["nodes"]) - 1
        world[k] = shift @ to_car @ rd["worlds"][i]
        lo, hi = cm.mesh_bounds(model, model["nodes"][k]["mesh"], world[k])
        boxes[k] = (np.minimum(lo, hi), np.maximum(lo, hi))
        r["body_nodes"].append(k)
        added.append(name)
    print(f"  cabin from {donor_entry['file']}: {len(added)} part(s) {added[:12]}")


def crop_parts(model, blob, r, world, boxes, crops):
    """Cuts out of the named parts the triangles whose centres lie in a box (crop: [{nodes, min, max}], car frame,
    metres, after a borrowed cabin is placed): a donor's parts that a shell of another shape would show, or a shell's
    own plain cabin where the borrowed one goes. The BRZ's model (cars13 bodies) is a coupe 1.26 m high with the
    W212 saloon's cabin: the saloon's rear bench rose through the coupe's rear side windows, and the model's own
    dashboard top, a plain shell, lay over the borrowed one's. A part cut gets a mesh of its own (another node may
    draw the same one), and its box is measured again by the vertices it keeps; a part with nothing left goes."""
    nodes = model["nodes"]
    for crop in crops:
        words = [s.lower() for s in crop["nodes"]]
        low, high = np.array(crop["min"], dtype=float), np.array(crop["max"], dtype=float)
        cut = 0
        for i in list(r["body_nodes"]):
            if not cm.matches(i, r["name_of"](i), words):
                continue
            mesh = model["meshes"][nodes[i]["mesh"]]
            primitives = []
            for primitive in mesh["primitives"]:
                if primitive.get("mode", 4) != 4:
                    primitives.append(primitive)
                    continue
                positions = cm.accessor_array(model, blob, primitive["attributes"]["POSITION"])
                if "indices" in primitive:
                    indices = cm.accessor_array(model, blob, primitive["indices"]).astype(np.int64).reshape(-1)
                else:
                    indices = np.arange(len(positions))
                triangles = indices[: len(indices) // 3 * 3].reshape(-1, 3)
                p = (np.c_[positions, np.ones(len(positions))] @ world[i].T)[:, :3]
                c = p[triangles].mean(axis=1)
                inside = np.all((c > low) & (c < high), axis=1)
                if not inside.any():
                    primitives.append(primitive)
                    continue
                cut += int(inside.sum())
                if inside.all():
                    continue
                part = dict(primitive)
                part["indices"] = append_accessor(model, blob, triangles[~inside].reshape(-1).astype(np.uint32), 5125, "SCALAR", 34963)
                primitives.append(part)
            if primitives == mesh["primitives"]:
                continue
            if not primitives:
                r["body_nodes"].remove(i)
                continue
            model["meshes"].append(dict(mesh, primitives=primitives))
            nodes[i]["mesh"] = len(model["meshes"]) - 1
            points = cm.mesh_points(model, blob, nodes[i]["mesh"], world[i])
            boxes[i] = (points.min(axis=0), points.max(axis=0))
        print(f"  crop {crop['nodes'][:3]} in ({crop['min']}, {crop['max']}): {cut} triangle(s) cut")


def steering_part(model, blob, r, world, boxes, part):
    """Takes a steering wheel that a model has modelled into a larger mesh (steering_part: {nodes, min, max}, car frame,
    metres) out into a part of its own, "SteeringPart_<node>", that the entry's steering can name, so that it turns:
    the Clio's own wheel is one piece of a mesh that holds its whole cabin, and the eyes given by hand (eye_at) left it
    standing still. Only the connected pieces of the named parts that lie wholly in the box go (vertices joined where
    they meet within 0.1 mm): a box drawn round the rim also holds the ends of the dashboard pieces behind it, which
    must stay where they are."""
    nodes = model["nodes"]
    words = [s.lower() for s in part["nodes"]]
    low, high = np.array(part["min"], dtype=float), np.array(part["max"], dtype=float)
    for i in list(r["body_nodes"]):
        if not cm.matches(i, r["name_of"](i), words):
            continue
        mesh = model["meshes"][nodes[i]["mesh"]]
        kept, taken = [], []
        for primitive in mesh["primitives"]:
            if primitive.get("mode", 4) != 4:
                kept.append(primitive)
                continue
            positions = cm.accessor_array(model, blob, primitive["attributes"]["POSITION"])
            if "indices" in primitive:
                indices = cm.accessor_array(model, blob, primitive["indices"]).astype(np.int64).reshape(-1)
            else:
                indices = np.arange(len(positions))
            triangles = indices[: len(indices) // 3 * 3].reshape(-1, 3)
            p = (np.c_[positions, np.ones(len(positions))] @ world[i].T)[:, :3]
            _, weld = np.unique(np.round(p / 1e-4).astype(np.int64), axis=0, return_inverse=True)
            corners = weld.reshape(-1)[triangles]
            count = int(weld.max()) + 1
            graph = coo_matrix((np.ones(2 * len(corners)), (np.r_[corners[:, 0], corners[:, 1]], np.r_[corners[:, 1], corners[:, 2]])), shape=(count, count))
            _, label = connected_components(graph, directed=False)
            piece = label[corners[:, 0]]
            centres = p[triangles].mean(axis=1)
            outside = ~np.all((centres > low) & (centres < high), axis=1)
            whole = np.bincount(piece, weights=outside.astype(float), minlength=int(label.max()) + 1) == 0
            take = whole[piece]
            if not take.any():
                kept.append(primitive)
                continue
            if not take.all():
                kept.append(dict(primitive, indices=append_accessor(model, blob, triangles[~take].reshape(-1).astype(np.uint32), 5125, "SCALAR", 34963)))
            taken.append(dict(primitive, indices=append_accessor(model, blob, triangles[take].reshape(-1).astype(np.uint32), 5125, "SCALAR", 34963)))
        if not taken:
            continue
        name = r["name_of"](i)
        if kept:
            model["meshes"].append(dict(mesh, primitives=kept))
            nodes[i]["mesh"] = len(model["meshes"]) - 1
            points = cm.mesh_points(model, blob, nodes[i]["mesh"], world[i])
            boxes[i] = (points.min(axis=0), points.max(axis=0))
        else:
            r["body_nodes"].remove(i)
        model["meshes"].append({"name": "SteeringPart_" + name, "primitives": taken})
        nodes.append({"name": "SteeringPart_" + name, "mesh": len(model["meshes"]) - 1})
        k = len(nodes) - 1
        world[k] = world[i]
        points = cm.mesh_points(model, blob, nodes[k]["mesh"], world[k])
        boxes[k] = (points.min(axis=0), points.max(axis=0))
        r["body_nodes"].append(k)
        print(f"  steering part {nodes[k]['name']}: {sum(len(cm.accessor_array(model, blob, q['indices'])) // 3 for q in taken)} triangle(s), "
              f"centre ({(boxes[k][0][0] + boxes[k][1][0]) / 2:+.3f} {(boxes[k][0][1] + boxes[k][1][1]) / 2:+.3f} {(boxes[k][0][2] + boxes[k][1][2]) / 2:+.3f})")


def lens_parts(model, r, words):
    """The named parts' glass drawn as clear lamp lenses (lens: node selectors): a model whose lamp covers share the
    windows' material (the BRZ's "Glass", a near-black tint on the windscreen and the headlamps alike) gets a copy of
    that material for them, named so the materials pass makes it GLP_Lens."""
    nodes = model["nodes"]
    copies = {}
    for i in r["body_nodes"]:
        if not cm.matches(i, r["name_of"](i), words):
            continue
        mesh = model["meshes"][nodes[i]["mesh"]]
        primitives = []
        for primitive in mesh["primitives"]:
            source = primitive.get("material")
            if source is not None and source not in copies:
                copy = json.loads(json.dumps(model["materials"][source]))
                copy["name"] = f"{copy.get('name', 'glass')} lens"
                model["materials"].append(copy)
                copies[source] = len(model["materials"]) - 1
            primitives.append(dict(primitive, material=copies[source]) if source is not None else primitive)
        model["meshes"].append(dict(mesh, primitives=primitives))
        nodes[i]["mesh"] = len(model["meshes"]) - 1
        print(f"  {r['name_of'](i)} drawn as a lamp lens")


def own_materials(model, r, parts, finishes):
    """Parts drawn in an untextured material of their own (own_material: {name: node selectors}), for the entry's
    materials to give them the real car's finish by that name. A texture atlas holds the colour of every part drawn
    with it, and a factor only scales a texel: the 205's rims are a flat red in the atlas its tyres, calipers, grille
    and badges share (the GTi's own were painted silver-grey), so no colour of materials could make them grey without
    tinting the rest. The copy keeps the source's normal map and sidedness and drops its colour and metal-roughness
    textures, so its finish is the factors alone; glTF reads absent ones as white, metallic 1 and roughness 1, which
    CarView takes for an exporter's defaults (a matt dielectric), so the entry gives colour, metallic and roughness.
    Wheel parts count as well as the body's: rims roll with their tyres."""
    nodes = model["nodes"]
    candidates = r["body_nodes"] + [i for assembly in r["assemblies"] for i in assembly]
    for name, selectors in parts.items():
        words = [s.lower() for s in selectors]
        copies = {}
        picked = 0
        for i in candidates:
            if not cm.matches(i, r["name_of"](i), words):
                continue
            mesh = model["meshes"][nodes[i]["mesh"]]
            primitives = []
            for primitive in mesh["primitives"]:
                source = primitive.get("material")
                if source is not None and source not in copies:
                    copy = json.loads(json.dumps(model["materials"][source]))
                    copy["name"] = name
                    for texture in ("baseColorTexture", "metallicRoughnessTexture"):
                        copy.setdefault("pbrMetallicRoughness", {}).pop(texture, None)
                    model["materials"].append(copy)
                    copies[source] = len(model["materials"]) - 1
                primitives.append(dict(primitive, material=copies[source]) if source is not None else primitive)
            model["meshes"].append(dict(mesh, primitives=primitives))
            nodes[i]["mesh"] = len(model["meshes"]) - 1
            picked += 1
        print(f"  {picked} part(s) drawn in a material of their own, {name}" if picked else f"  own_material {name} names no part of the model")
        if name.lower() not in finishes:
            print(f"  own_material {name} has no finish in materials: drawn white")


def in_present_frame(entry, spec):
    """The entry with its hand-given car-frame points (eye_at, mirror_box) carried from the geometry they were read in
    (the entry's frame) to the spec's present one.

    car_models.rig places a model by its front hubs (at -front), its hub plane (at the rest hub height) and the centre
    between its wheels (x 0), and scales it to the wheelbase about them; a point read off the prepared model stays on the
    same spot of it only if it moves with them. Read at one geometry and kept as numbers, it lands elsewhere on the model
    when a spec changes: dims6 lengthened the E-Class from the Egea's wheelbase to the W212's (scale 1.0903) and its
    mirror box, read at the old one, hung the glass 7.5 cm under its housing. An entry without a frame, or one at the
    present geometry, is taken as it is (the same bytes as before frames were given).
    """
    frame = entry.get("frame")
    if not frame:
        return entry
    sag = spec["mass"] * cm.GRAVITY / 4.0 / spec["stiffness"]
    then = np.array([0.0, frame["hub_y"], -frame["front"]])
    now = np.array([0.0, spec["hub"] - (spec["rest"] - sag), -spec["front"]])
    grow = (spec["front"] + spec["rear"]) / (frame["front"] + frame["rear"])
    if abs(grow - 1.0) < 1e-4 and np.allclose(then, now, atol=5e-4):
        return entry
    moved = dict(entry)
    if "eye_at" in entry:
        moved["eye_at"] = [float(v) for v in (np.array(entry["eye_at"], dtype=float) - then) * grow + now]
        print(f"  eyes given at {entry['eye_at']} carried to ({moved['eye_at'][0]:+.3f} {moved['eye_at'][1]:+.3f} {moved['eye_at'][2]:+.3f}), scale {grow:.4f}")
    if "mirror_box" in entry:
        box = entry["mirror_box"]
        centre = (np.array(box["centre"], dtype=float) - then) * grow + now
        moved["mirror_box"] = dict(box, centre=[float(v) for v in centre], size=[float(s) * grow for s in box["size"]])
        print(f"  mirror box given at {box['centre']} carried to ({centre[0]:+.3f} {centre[1]:+.3f} {centre[2]:+.3f}), scale {grow:.4f}")
    return moved


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
    r = cm.rig(model, entry, spec, car, blob)
    entry = in_present_frame(entry, spec)
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
    if entry.get("crop"):
        crop_parts(model, blob, r, world, boxes, entry["crop"])
    if entry.get("steering_part"):
        steering_part(model, blob, r, world, boxes, entry["steering_part"])
    if entry.get("lens"):
        lens_parts(model, r, [s.lower() for s in entry["lens"]])
    if entry.get("own_material"):
        own_materials(model, r, entry["own_material"], {k.lower() for k in entry.get("materials", {})})
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
    cockpit = None

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
    # The source node of each body part, for the entry's node selectors after the scene is rebuilt (turn).
    body_sources = dict(zip(body_children, body))
    children.append(add("Body", children=body_children))
    # The tyres' radius from their vertices (a box would overstate a wheel stored turned about its axle), and each
    # wheel scaled about its hub to the simulation's radius: the hubs sit at the simulation's rest height, so a
    # smaller tyre would float over the road and a larger one sink into it.
    hubs = [to_car @ np.array([*r["pivots"][w], 1.0]) for w in range(4)]
    body_points = np.vstack([cm.mesh_points(model, blob, nodes[i]["mesh"], world[i]) for i in body]) if entry.get("liners") else None
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
        if entry.get("liners"):
            # A liner just inside the wing's arch lip (1.2 cm, measured round the hub over the liner's span of 20 to 160
            # degrees on the wing's outer part), never more than 5 cm over the tyre; from 5 mm past the tyre's outer
            # face to 10 cm past its inner one, open below (CarView.BuildModelLiners). The 86's arches sit 1 to 4 cm
            # over its tyres, so a liner any higher showed through the wings.
            tyre = cm.mesh_points(model, blob, nodes[r["tyres"][w]]["mesh"], world[r["tyres"][w]])
            tyre_half = float(tyre[:, 0].max() - tyre[:, 0].min()) * grow * 0.5
            side = -1.0 if pivot[0] < 0 else 1.0
            skin = body_points[(body_points[:, 0] * side > abs(pivot[0]) + tyre_half - 0.02) & (np.abs(body_points[:, 2] - pivot[2]) < 0.5) & (body_points[:, 1] > pivot[1])]
            lip_r = np.hypot(skin[:, 1] - pivot[1], skin[:, 2] - pivot[2])
            lip_angle = np.degrees(np.arctan2(skin[:, 1] - pivot[1], skin[:, 2] - pivot[2]))
            span = lip_r[(lip_angle >= 20) & (lip_angle <= 160)]
            lip = float(span.min()) if len(span) else spec["radius"] + 0.06
            liner_radius = max(min(lip - 0.012, spec["radius"] + 0.05), spec["radius"] + 0.005)
            print(f"  liner {w}: the arch lip {lip:.3f} m from the hub, the liner {liner_radius:.3f} m (the tyre {spec['radius']:.3f})")
            children.append(add(f"Liner{w}", matrix(translation=pivot[:3], scale=(tyre_half + 0.005, liner_radius, tyre_half + 0.10))))
    if "plate" in entry:
        # The rear plate where the entry puts it (read off the prepared model, car frame: the recess's centre, 6 mm proud).
        plate = entry["plate"]
        plate_size = plate.get("size", [0.52, 0.11])
        children.append(add("Plate", matrix(basis_facing((0.0, 0.0, 1.0)), plate["centre"], (plate_size[0], plate_size[1], 1.0))))
        print(f"  rear plate at ({plate['centre'][0]:+.3f} {plate['centre'][1]:+.3f} {plate['centre'][2]:+.3f})")

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
        # The seat-shaped meshes passed over, for the cockpit record (their tops over the road at rest).
        passed = []
        road = spec["hub"] - (spec["rest"] - spec["mass"] * cm.GRAVITY / 4.0 / spec["stiffness"]) - spec["radius"]
        for i in body:
            if contains(name_of(i), GLASS_WORDS) or contains(name_of(i), ("seat", "sheat", "asiento", "sitz", "siege")):
                continue
            lo_i, hi_i = boxes[i]
            # A seat by its shape where its name does not say so (the 205's meshes are all "defaultMaterial"): no wider
            # than a pair of seats, no deeper than one, and rising from the floor, well below the eyes. Its backrest
            # stands at head height just behind the eyes; at the 205's real size (dims6) its top came 4 cm over them
            # and had the eyes lowered 10 cm, as if under a roof. The 570S's two buckets are one mesh 1.34 m across
            # (v8seat), 1.04 m over the road at their top: their backs, 0.975 m up where they rise behind the eyes, had
            # its eyes set 13 cm under them, at 0.845 m, where the wheel and the dashboard hid the road. A roof
            # lining, a header or a visor hangs over the eyes and never reaches 0.4 m under them, so the width alone
            # tells a seat pair from nothing that is over the head.
            if lo_i[0] - 0.15 < eye[0] < hi_i[0] + 0.15 and lo_i[2] - 0.15 < eye[2] < hi_i[2] + 0.15 and hi_i[1] > eye[1]:
                if hi_i[0] - lo_i[0] < 1.4 and hi_i[2] - lo_i[2] < 0.9 and lo_i[1] < eye[1] - 0.4:
                    passed.append({"mesh": name_of(i), "width": round(float(hi_i[0] - lo_i[0]), 3), "top_over_road": round(float(hi_i[1] - road), 3)})
                    continue
                pts = cm.mesh_points(model, blob, nodes[i]["mesh"], world[i])
                near = pts[(np.abs(pts[:, 0] - eye[0]) < 0.10) & (np.abs(pts[:, 2] - eye[2]) < 0.10) & (pts[:, 1] > eye[1] + 0.03)]
                if len(near):
                    over.append(near[:, 1].min())
        # The top of the head is some 12 cm over the eyes: the eyes stay 13 cm under the headlining.
        headroom = (min(over) - eye[1]) if over else float("nan")
        if over and headroom < 0.13:
            eye[1] = min(over) - 0.13
        children.append(add("Eye", matrix(translation=eye)))
        cockpit = {
            "eye": mm(eye),
            "eye_over_road": mm(eye[1] - road),
            "under_lining": mm(min(over) - eye[1]) if over else None,
            "lowered": bool(over and headroom < 0.13),
            "seats_passed_over": passed,
            "wheel_centre": mm(centre),
            "eye_from_wheel": mm(eye - centre),
        }
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
        # Where the rim crosses the panel just under the gear readout (the 570S's, the eyes over a reclined seat), the
        # entry moves the readout up the panel (gear_top, pixels down the 1024 x 400 InstrumentCluster): a child Gear,
        # at the box's top edge in the panel's units (its width), which CarView hands to the cluster.
        gear = []
        if "gear_top" in dials_entry:
            gear = [add("Gear", matrix(translation=(0.0, (200.0 - float(dials_entry["gear_top"])) / 1024.0, 0.0)))]
        children.append(add("Dials", matrix(basis_facing(eye - face), face, (width, width, 1.0)), children=gear))
        cockpit["dials"] = {"centre": mm(face), "width": mm(width), "gear_top": int(dials_entry.get("gear_top", 120))}
        print(f"  dials at ({face[0]:+.3f} {face[1]:+.3f} {face[2]:+.3f}), {width:.2f} m wide" + (f", the gear readout from {dials_entry['gear_top']} px down" if gear else ""))

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
            cockpit["mirror"] = {"glass": mm(glass), "size": mm(size)}
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
            cockpit["mirror"] = {"glass": mm(glass), "on": "housing"}
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
                    contains(model["materials"][q["material"]].get("name", ""), GLASS_WORDS) or is_entry_glass(entry, model["materials"][q["material"]].get("name", ""))
                    for q in model["meshes"][nodes[i]["mesh"]]["primitives"] if "material" in q)
                if not glassy:
                    continue
                for y_hit in ray_down(model, blob, nodes[i]["mesh"], world[i], 0.0, z_m):
                    if y_hit > eye[1] - 0.2:
                        top = y_hit if top is None else min(top, y_hit)
            y_m = (top - 0.06) if top is not None else eye[1] + 0.08
            glass = np.array([0.0, y_m, z_m]) + np.array(entry.get("mirror_offset", [0.0, 0.0, 0.0]))
            children.append(add("MirrorHung", matrix(basis_facing(eye - glass), glass)))
            cockpit["mirror"] = {"glass": mm(glass), "on": "hung by the game"}
            print(f"  mirror hung at ({glass[0]:+.3f} {glass[1]:+.3f} {glass[2]:+.3f}), the glass over it at {None if top is None else round(float(top), 3)}")

    root = add("CarModel", children=children)

    # Materials: the paint, the windows and the clear lenses get the game's own (CarView); the rest stays the model's.
    paint_words = [s.lower() for s in entry.get("paint", [])]
    # The paint atlases (paint_atlas): materials whose texture holds the paint with the rest of the car's details.
    atlas = {k.lower(): v for k, v in entry.get("paint_atlas", {}).items()}
    atlas_materials = {}
    # The entry's corrections to a material's finish (materials: {name: {roughness, metallic, colour}}), where the model
    # is wrong about the real car: the 570S's dashboard, scuttle and door tops are leather or Alcantara, matt, and its
    # model has them in gloss black (roughness 0.18-0.22), which mirrored the sky from the seat as a grey wash over the
    # right half of the frame (v8seat). Its entry takes them to 0.88, the game's own dark trim (0.85) and rubber (0.86):
    # at 0.55-0.6 the sun ahead still spread over them as a flat grey sheen. colour (sRGB "#RRGGBB") sets a material's
    # base colour, kept linear as glTF has it: the first 458 model's paint was a pure #F70000, and the car's ORIGINAL is
    # its launch colour, Rosso Corsa (cars13 stage 2; the source is cited in CarView.LoftPaint). Only the named
    # materials change; a model without them prepares as before.
    finishes = {k.lower(): v for k, v in entry.get("materials", {}).items()}
    finished = set()
    for index, material in enumerate(model.get("materials", [])):
        name = material.get("name", "")
        lower = name.lower()
        glassy = contains(lower, GLASS_WORDS) or "KHR_materials_transmission" in material.get("extensions", {})
        # Godot reads a material in the specular-glossiness workflow (KHR_materials_pbrSpecularGlossiness: every one of
        # the Esprit's, whose cabin the Testarossa and the 205's seats borrow) from that extension alone and ignores its
        # metallic-roughness values (gltf_document.cpp, 4.7.2), so an entry's finish written to them never showed. A
        # material the entry corrects is first turned into the metallic-roughness workflow from its own diffuse colour
        # and glossiness; one with textures in that workflow is left as it is.
        spec_gloss = material.get("extensions", {}).get("KHR_materials_pbrSpecularGlossiness")
        if lower in finishes and spec_gloss is not None and "diffuseTexture" not in spec_gloss and "specularGlossinessTexture" not in spec_gloss:
            material["pbrMetallicRoughness"] = {
                "baseColorFactor": [float(c) for c in spec_gloss.get("diffuseFactor", [1.0, 1.0, 1.0, 1.0])],
                "metallicFactor": 0.0,
                "roughnessFactor": round(1.0 - float(spec_gloss.get("glossinessFactor", 1.0)), 6),
            }
            del material["extensions"]["KHR_materials_pbrSpecularGlossiness"]
        # Some exporters mark every material see-through (the VAZ's): an opaque one is drawn alpha-tested instead, so
        # it casts its shadow and sorts with the glass; a texture's holes (a grille) still show through.
        pbr = material.setdefault("pbrMetallicRoughness", {})
        if lower in finishes:
            finished.add(lower)
            for key, value in finishes[lower].items():
                if key == "colour":
                    srgb = [int(value.lstrip("#")[k:k + 2], 16) / 255.0 for k in (0, 2, 4)]
                    linear = [c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4 for c in srgb]
                    pbr["baseColorFactor"] = [round(c, 6) for c in linear] + [pbr.get("baseColorFactor", [1, 1, 1, 1.0])[3]]
                elif key == "emissive":
                    srgb = [int(value.lstrip("#")[k:k + 2], 16) / 255.0 for k in (0, 2, 4)]
                    material["emissiveFactor"] = [round(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4, 6) for c in srgb]
                elif key == "emissive_strength":
                    # The BRZ's lamps glow at 10 (KHR_materials_emissive_strength), lit as in a night render: by day
                    # a lamp unlit, or a headlamp's glass catching the light, not a lamp blazing white or amber.
                    if float(value) > 0.0:
                        material.setdefault("extensions", {})["KHR_materials_emissive_strength"] = {"emissiveStrength": float(value)}
                    else:
                        material.get("extensions", {}).pop("KHR_materials_emissive_strength", None)
                        material.pop("emissiveFactor", None)
                else:
                    pbr[f"{key}Factor"] = float(value)
        alpha = pbr.get("baseColorFactor", [1, 1, 1, 1])
        # Nothing real is black as 0: black plastic, rubber and leather reflect 3-5 % of the light. An untextured
        # pure black (the Delta's whole cabin) drew as a void with no shape; it is lifted to 0.035.
        if "baseColorTexture" not in pbr and len(alpha) >= 3 and max(alpha[:3]) < 0.02:
            pbr["baseColorFactor"] = [0.035, 0.035, 0.037] + list(alpha[3:4] or [1.0])
        if material.get("alphaMode") == "BLEND" and not glassy and not contains(lower, LAMP_WORDS) and (len(alpha) < 4 or alpha[3] >= 0.99):
            material["alphaMode"] = "MASK"
            material["alphaCutoff"] = 0.5
        if lower in atlas:
            material["name"] = "GLP_Paint"
            atlas_materials[index] = name
            continue
        # "==Name" names a paint material exactly, case and all (the Renault 12 has both "Material" and "material").
        # The rear lamps' lenses that light up when the car brakes (CarView.Braking), and materials left as the model
        # has them though their names say glass (the Testarossa's orange indicators, red_glass its tail lamps).
        if any((name == w[2:]) if w.startswith("==") else (w in lower) for w in [s.lower() if not s.startswith("==") else s for s in entry.get("brake", [])]):
            material["name"] = "GLP_Brake"
            continue
        if any(w.lower() in lower for w in entry.get("keep", [])):
            continue
        # Windows drawn in an opaque material whose name does not say glass (glass: material-name parts, "==Name"
        # exactly): the first 458 model's whole glasshouse, windscreen to engine cover, was one black "Material.048",
        # and from the seat it hid everything but a sliver of road (models13).
        if is_entry_glass(entry, name):
            material["name"] = "GLP_Glass"
            print(f"  {name} drawn as glass (the entry's glass)")
            continue
        painted = any((name == w[2:]) if w.startswith("==") else (w in lower) for w in entry.get("paint", [])) if paint_words else is_paint(name)
        if painted:
            material["name"] = "GLP_Paint"
        elif glassy and contains(lower, LAMP_WORDS):
            material["name"] = "GLP_Lens"
        elif glassy:
            material["name"] = "GLP_Glass"
    for name in atlas:
        if name not in {n.lower() for n in atlas_materials.values()}:
            print(f"  paint_atlas names {name}, which the model has no material of")
    for name in finishes:
        if name not in finished:
            print(f"  materials names {name}, which the model has no material of")
    if finished:
        print(f"  finish corrected on {sorted(finished)}")

    # A paint drawn from a texture (an atlas, or a paint material with a texture of its own, as the Delta's white
    # with its Martini lettering) gets the mask the game recolours it by: those the prepared scene draws (a borrowed
    # cabin's donor brings materials of its body that nothing uses).
    drawn = [q for n in out_nodes if "mesh" in n for q in model["meshes"][n["mesh"]]["primitives"]]
    masks = {}
    for index, material in enumerate(model.get("materials", [])):
        texture = material.get("pbrMetallicRoughness", {}).get("baseColorTexture")
        primitives = [q for q in drawn if q.get("material") == index]
        if material.get("name") != "GLP_Paint" or texture is None or not primitives:
            continue
        if texture.get("texCoord", 0) != 0 or "KHR_texture_transform" in texture.get("extensions", {}):
            print(f"  paint texture of material {index} is mapped by other coordinates: drawn in the flat paint")
            continue
        # The mask takes the emissive slot: a material that glows already (an atlas with lit lamps) keeps its glow, an
        # atlas then drawn as the model has it and another paint texture in the flat paint, as before the masks.
        if "emissiveTexture" in material or any(v > 0.0 for v in material.get("emissiveFactor", [0.0, 0.0, 0.0])):
            if index in atlas_materials:
                material["name"] = atlas_materials[index]
                print(f"  paint_atlas {atlas_materials[index]} glows (an emissive texture or colour): left as the model has it, not recoloured")
            else:
                print(f"  paint texture of material {index} glows (an emissive texture or colour): drawn in the flat paint")
            continue
        label = atlas_materials.get(index, f"of material {index}")
        image, key = paint_mask(model, blob, material, primitives, atlas.get(label.lower(), {}), label)
        embed_paint_mask(model, blob, material, image, key)
        masks[index] = image

    orient_paint(model, blob, out_nodes, leave=set(atlas_materials))
    if entry.get("turn"):
        turn_pieces(model, blob, out_nodes, body_sources, name_of, [s.lower() for s in entry["turn"]])
    if interior and (steering or entry.get("eye_at")):
        split_lining(model, blob, out_nodes, eye, centre, masks)

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
    record_cockpit(car, cockpit)
    return True


def mm(v):
    """A length or a point, rounded to the millimetre, for the cockpit record (stable from run to run)."""
    if np.ndim(v) == 0:
        return round(float(v), 3)
    return [round(float(x), 3) for x in v]


def record_cockpit(car, cockpit):
    """Writes where the preparation put this car's eyes, dials and mirror into tools/car_cockpits.json (the car frame:
    x right, y up from the centre of gravity, z back; metres), or drops the car's entry when the game builds its
    cabin. The tests read it: the models it describes are build output, not in the repository, and a rule of the
    preparation that broke would otherwise show only on screen (v8seat review: the eyes 0.845 m over the road)."""
    record = {}
    if os.path.exists(COCKPITS):
        with open(COCKPITS, encoding="utf-8") as f:
            record = json.load(f)
    record["_comment"] = ("Written by tools/prepare_car_models.py for every car it prepares (do not edit): where the eyes, the "
                          "dials and the mirror's glass went, in the car frame (x right, y up from the centre of gravity, z back; "
                          "metres). eye_over_road: from the road at rest; under_lining: from the eyes to whatever is over them "
                          "(the eyes stay 13 cm under it; lowered when they had to be); seats_passed_over: seat-shaped meshes "
                          "over the eyes' footprint that are not taken for a roof; eye_from_wheel: "
                          "from the steering wheel's centre; dials gear_top: the gear readout's box, pixels down the 1024 x 400 panel.")
    if cockpit is None:
        record.pop(car, None)
    else:
        record[car] = cockpit
    ordered = {"_comment": record.pop("_comment")}
    ordered.update(sorted(record.items()))
    # Points and sizes on one line each, so that a change to one reads as one line of a diff.
    text = re.sub(r"\[\s+([-0-9., \n]+?)\s+\]", lambda m: "[" + ", ".join(v.strip() for v in m.group(1).split(",")) + "]",
                  json.dumps(ordered, indent=2))
    with open(COCKPITS, "w", encoding="utf-8", newline="\n") as f:
        f.write(text + "\n")


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

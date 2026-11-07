"""Bakes a photoscanned tree into an impostor atlas for the game (0.39, FoliageBuilder's wood and roadside trees).

Run inside Blender (5.2): blender -b --factory-startup --python tools/bake-impostors.py -- <model.gltf> <out prefix>
    [--object <mesh name>] [--views 12] [--frame 1024]

The tree is drawn from --views directions round it (every 30 degrees, 4 degrees above the horizon) by an orthographic
camera, twice: once its colour alone, unlit (each material's base colour as emission, its alpha kept), once its
normals in the camera's frame (x right, y up, z towards the camera, mapped to 0..1). The frames go into two atlases of
4 columns, <out>_albedo.png (sRGB) and <out>_normal.png (linear), and <out>.json records the frame's size in metres
and the tree's foot in the frame, for the game to size and place the card. View k looks from bearing k * 360 / views
degrees, measured in the model's x-y plane from +y towards +x (Blender's frame; the game turns that into its own).
"""
import json
import math
import os
import sys

import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
src, out = argv[0], argv[1]
options = {argv[i]: argv[i + 1] for i in range(2, len(argv) - 1, 2)}
views = int(options.get("--views", 12))
frame = int(options.get("--frame", 1024))
only = options.get("--object")
columns = 4
rows = (views + columns - 1) // columns

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
if only:
    for o in meshes:
        if o.name != only:
            bpy.data.objects.remove(o, do_unlink=True)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
if not meshes:
    raise SystemExit(f"no mesh {only!r} in {src}")

lo = Vector((1e9, 1e9, 1e9))
hi = Vector((-1e9, -1e9, -1e9))
for o in meshes:
    for v in o.bound_box:
        w = o.matrix_world @ Vector(v)
        lo = Vector((min(lo.x, w.x), min(lo.y, w.y), min(lo.z, w.z)))
        hi = Vector((max(hi.x, w.x), max(hi.y, w.y), max(hi.z, w.z)))
# The trunk's foot: the middle of the lowest tenth of the vertices.
lowest = []
for o in meshes:
    for v in o.data.vertices:
        w = o.matrix_world @ v.co
        if w.z < lo.z + 0.1 * (hi.z - lo.z):
            lowest.append(w)
foot = sum(lowest, Vector()) / len(lowest) if lowest else Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z))
foot.z = lo.z
# A square frame wide enough for the crown seen from any side and tall enough for the tree, the foot at its bottom
# centre (the crown is not centred on the trunk).
reach = max(max((Vector((x, y, 0)) - Vector((foot.x, foot.y, 0))).length for x in (lo.x, hi.x) for y in (lo.y, hi.y)), 0.1)
size = max(2.0 * reach, hi.z - lo.z) * 1.02

scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = frame
scene.render.resolution_y = frame
scene.render.film_transparent = True
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGBA"
try:
    scene.eevee.taa_render_samples = 32
except AttributeError:
    pass
world = bpy.data.worlds.new("black")
scene.world = world

camera = bpy.data.objects.new("camera", bpy.data.cameras.new("camera"))
scene.collection.objects.link(camera)
scene.camera = camera
camera.data.type = "ORTHO"
camera.data.ortho_scale = size
camera.data.clip_start = 0.1
camera.data.clip_end = 1000.0
elevation = math.radians(4.0)


def rewire(mode):
    """Every material drawn as emission: its base colour, or its normal in the camera's frame; its alpha kept."""
    for mat in bpy.data.materials:
        if not mat.use_nodes:
            continue
        nt = mat.node_tree
        bsdf = next((n for n in nt.nodes if n.type == "BSDF_PRINCIPLED"), None)
        output = next((n for n in nt.nodes if n.type == "OUTPUT_MATERIAL"), None)
        if bsdf is None or output is None:
            continue
        if "glp_base_link" not in mat:
            mat["glp_base_link"] = 1
        base = bsdf.inputs["Base Color"]
        alpha = bsdf.inputs["Alpha"]
        for name in ("glp_emit", "glp_mix", "glp_transparent", "glp_geom", "glp_xform", "glp_map", "glp_add"):
            if name in nt.nodes:
                nt.nodes.remove(nt.nodes[name])
        emit = nt.nodes.new("ShaderNodeEmission"); emit.name = "glp_emit"
        emit.inputs["Strength"].default_value = 1.0
        if mode == "albedo":
            if base.is_linked:
                nt.links.new(base.links[0].from_socket, emit.inputs["Color"])
            else:
                emit.inputs["Color"].default_value = base.default_value
        else:
            geom = nt.nodes.new("ShaderNodeNewGeometry"); geom.name = "glp_geom"
            xform = nt.nodes.new("ShaderNodeVectorTransform"); xform.name = "glp_xform"
            xform.vector_type = "NORMAL"
            xform.convert_from = "WORLD"
            xform.convert_to = "CAMERA"
            nt.links.new(geom.outputs["Normal"], xform.inputs["Vector"])
            # Blender's camera frame looks down -z: z towards the viewer is the camera's +z already; x right, y up.
            mapping = nt.nodes.new("ShaderNodeVectorMath"); mapping.name = "glp_map"
            mapping.operation = "MULTIPLY_ADD"
            mapping.inputs[1].default_value = (0.5, 0.5, 0.5)
            mapping.inputs[2].default_value = (0.5, 0.5, 0.5)
            nt.links.new(xform.outputs["Vector"], mapping.inputs[0])
            nt.links.new(mapping.outputs["Vector"], emit.inputs["Color"])
        transparent = nt.nodes.new("ShaderNodeBsdfTransparent"); transparent.name = "glp_transparent"
        mix = nt.nodes.new("ShaderNodeMixShader"); mix.name = "glp_mix"
        if alpha.is_linked:
            nt.links.new(alpha.links[0].from_socket, mix.inputs["Fac"])
        else:
            mix.inputs["Fac"].default_value = alpha.default_value
        nt.links.new(transparent.outputs["BSDF"], mix.inputs[1])
        nt.links.new(emit.outputs["Emission"], mix.inputs[2])
        nt.links.new(mix.outputs["Shader"], output.inputs["Surface"])
        try:
            mat.surface_render_method = "DITHERED"
        except AttributeError:
            pass


def render(mode, path):
    rewire(mode)
    scene.view_settings.view_transform = "Standard" if mode == "albedo" else "Raw"
    scene.view_settings.look = "None"
    scene.view_settings.exposure = 0.0
    scene.view_settings.gamma = 1.0
    shots = []
    for k in range(views):
        bearing = 2.0 * math.pi * k / views
        direction = Vector((math.sin(bearing) * math.cos(elevation), math.cos(bearing) * math.cos(elevation), math.sin(elevation)))
        target = Vector((foot.x, foot.y, foot.z + size * 0.5))
        camera.location = target + direction * 200.0
        camera.rotation_euler = (-direction).to_track_quat("-Z", "Y").to_euler()
        shot = f"{path}_{mode}_{k:02d}.png"
        scene.render.filepath = shot
        bpy.ops.render.render(write_still=True)
        shots.append(shot)
    return shots


albedo = render("albedo", out)
normal = render("normal", out)
with open(out + ".json", "w") as f:
    json.dump({"views": views, "columns": columns, "rows": rows, "frame_metres": size, "height": hi.z - lo.z,
               "elevation_degrees": 4.0, "source": os.path.basename(src), "object": only,
               "albedo": albedo, "normal": normal}, f, indent=1)
print("BAKED", out, views, "views, frame", round(size, 2), "m, height", round(hi.z - lo.z, 2), "m")

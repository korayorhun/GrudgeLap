"""Bakes the grass tufts' atlas from photoscanned grass clumps (0.40): game/assets/textures/grass_blades.png.

Run inside Blender (5.2): blender -b --factory-startup --python tools/bake-grass.py -- <out.png> <clumps.gltf> [...]

The tufts (FoliageBuilder.TuftMesh) are crossed cards 1.2 m wide and 0.45 m tall, each showing one cell of a 4 x 2
atlas. Each cell here is a row of four to six of the scans' clumps (Poly Haven's Grass Medium 01 and 02, CC0) set
along the card's width with a little depth, turned and scaled at random, drawn from the side by an orthographic camera
framing exactly the card: their base colour unlit, their alpha kept. Until 0.40 the atlas was drawn blades
(tools/make-foliage.py), flat green strokes.
"""
import math
import os
import random
import sys

import bpy
from mathutils import Matrix, Vector

argv = sys.argv[sys.argv.index("--") + 1:]
out, sources = argv[0], argv[1:]
cell_w, cell_h = 512, 192
width, height = 1.2, 0.45

bpy.ops.wm.read_factory_settings(use_empty=True)
for src in sources:
    bpy.ops.import_scene.gltf(filepath=src)
clumps = [o for o in bpy.context.scene.objects if o.type == "MESH"]
for o in clumps:
    o.hide_render = True

for mat in bpy.data.materials:
    if not mat.use_nodes:
        continue
    nt = mat.node_tree
    bsdf = next((n for n in nt.nodes if n.type == "BSDF_PRINCIPLED"), None)
    output = next((n for n in nt.nodes if n.type == "OUTPUT_MATERIAL"), None)
    if bsdf is None or output is None:
        continue
    emit = nt.nodes.new("ShaderNodeEmission")
    base = bsdf.inputs["Base Color"]
    if base.is_linked:
        nt.links.new(base.links[0].from_socket, emit.inputs["Color"])
    else:
        emit.inputs["Color"].default_value = base.default_value
    transparent = nt.nodes.new("ShaderNodeBsdfTransparent")
    mix = nt.nodes.new("ShaderNodeMixShader")
    alpha = bsdf.inputs["Alpha"]
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

scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = cell_w
scene.render.resolution_y = cell_h
scene.render.film_transparent = True
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGBA"
scene.view_settings.view_transform = "Standard"
scene.view_settings.look = "None"
scene.world = bpy.data.worlds.new("black")
camera = bpy.data.objects.new("camera", bpy.data.cameras.new("camera"))
scene.collection.objects.link(camera)
scene.camera = camera
camera.data.type = "ORTHO"
camera.data.ortho_scale = width          # the larger side of the frame: the card's width
camera.location = Vector((0.0, -50.0, height * 0.5))
camera.rotation_euler = (math.radians(90.0), 0.0, 0.0)

rng = random.Random(2026)
frames = []
for k in range(8):
    copies = []
    count = rng.randint(4, 6)
    for j in range(count):
        src = rng.choice(clumps)
        copy = src.copy()
        copy.data = src.data
        scene.collection.objects.link(copy)
        copy.hide_render = False
        world = [src.matrix_world @ Vector(v) for v in src.bound_box]
        lo_z = min(w.z for w in world)
        hi_z = max(w.z for w in world)
        centre = sum(world, Vector()) / 8.0
        # Scaled so the tallest blades stand 0.25-0.42 m: the cards' 0.45 m with a little headroom.
        scale = rng.uniform(0.25, 0.42) / max(hi_z - lo_z, 0.05)
        x = -0.48 + 0.96 * (j + rng.uniform(0.15, 0.85)) / count
        # The clump moved to the origin by its foot, scaled, turned about the vertical and set in its place on the row.
        copy.matrix_world = (Matrix.Translation((x, rng.uniform(-0.15, 0.15), 0.0)) @ Matrix.Rotation(rng.uniform(0, 2 * math.pi), 4, 'Z')
                             @ Matrix.Scale(scale, 4) @ Matrix.Translation((-centre.x, -centre.y, -lo_z)) @ src.matrix_world)
        copies.append(copy)
    path = os.path.splitext(out)[0] + f"_cell{k}.png"
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    frames.append(path)
    for c in copies:
        bpy.data.objects.remove(c, do_unlink=True)
print("GRASS", " ".join(frames))

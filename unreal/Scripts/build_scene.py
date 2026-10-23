"""Builds the GrudgeLap feasibility level in Unreal Engine 5 from the Godot export and CC0 Poly Haven assets.

Run it through Scripts/build.cmd (the editor commandlet; no window), which first clears the previous run's output
and writes Import/track_ue.glb (clean_glb.py).

Inputs: Import/track.glb (terrain, roads, props, the player's car; imported as track_ue.glb) and Import/track.json
(props by kind, the car, the cockpit camera; metres on Godot axes) written by the game's --export-world;
Import/scatter.json (dense grass, rocks, copses near the start; prepare_scatter.py) and Import/PolyHaven (CC0
photoscans; fetch_polyhaven.py).
Output: /Game/Maps/Feasibility with the sky, sun, clouds and fog of Unreal's default template, the track with
photoscanned ground and asphalt, Nanite grass and trees, cine cameras (the game's cockpit and chase views and a
view from the meadow) with a level sequence each, and a Movie Render Queue manifest
(Saved/MovieRenderPipeline/QueueManifest.utxt) that render.cmd renders off screen.
"""
import itertools
import json
import math
import os
import random
import time

import unreal

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
IMPORT = os.path.join(ROOT, "Import")
POLYHAVEN = os.path.join(IMPORT, "PolyHaven")
RENDER_OUT = os.path.join(ROOT, "Saved", "Renders")
MAP = "/Game/Maps/Feasibility"
TEMPLATE = "/Engine/Maps/Templates/Template_Default"
GAME = "/Game/GrudgeLap"
# Godot's sun (WorldBuilder.SunDirection, towards the sun), so both engines light the scene from the same place.
SUN_DIRECTION = (-0.377, 0.742, -0.554)

eal = unreal.EditorAssetLibrary
mel = unreal.MaterialEditingLibrary
level_sub = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
actor_sub = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
subobjects = unreal.get_engine_subsystem(unreal.SubobjectDataSubsystem)
asset_tools = unreal.AssetToolsHelpers.get_asset_tools()
rng = random.Random(7)
started = time.time()


def log(message):
    # Warnings stand out in the commandlet's output.
    unreal.log_warning(f"[GrudgeLap {time.time() - started:6.0f}s] {message}")


# ---- coordinates -----------------------------------------------------------------------------------------
# Godot (and the glTF it writes): metres, +Y up, -Z forward, +X right, right-handed. Unreal: centimetres, +Z up,
# +X forward, +Y right, left-handed. The importer's mapping is found from props whose positions are known on both
# sides (see calibrate) rather than assumed.
AXES = None


def to_unreal(p, scale=100.0):
    (ix, sx), (iy, sy), (iz, sz) = AXES
    return unreal.Vector(p[ix] * sx * scale, p[iy] * sy * scale, p[iz] * sz * scale)


def direction(p):
    return to_unreal(p, 1.0)


def calibrate(placements, actors_by_label):
    """Chooses the axis permutation and signs that carry the exported prop positions onto the imported actors."""
    global AXES
    pairs = []
    for prop in placements["props"]:
        actor = actors_by_label.get(f"{prop['kind']}-{prop['label']}")
        if actor is not None:
            loc = actor.get_actor_location()
            pairs.append((prop["position"], (loc.x, loc.y, loc.z)))
    if not pairs:
        raise RuntimeError("no prop found on both sides; cannot match the axes")
    best, best_error = None, float("inf")
    for perm in itertools.permutations(range(3)):
        for signs in itertools.product((1, -1), repeat=3):
            error = sum(abs(godot[perm[k]] * signs[k] * 100.0 - ue[k]) for godot, ue in pairs[:30] for k in range(3))
            if error < best_error:
                best, best_error = list(zip(perm, signs)), error
    AXES = best
    log(f"axes {AXES}, mean error {best_error / min(30, len(pairs)) / 3:.1f} cm over {len(pairs)} props")


# ---- level and track -------------------------------------------------------------------------------------

def open_level():
    # Deleting the map and creating it again at the same path in one session fails ("an asset already exists"),
    # so build.cmd clears the previous run's map, track, materials and sequences from disk before starting.
    if eal.does_asset_exist(MAP):
        raise RuntimeError(f"{MAP} exists; run Scripts/build.cmd, which clears the previous output first")
    if not level_sub.new_level_from_template(MAP, TEMPLATE):
        raise RuntimeError("could not create the level from the template")
    for actor in actor_sub.get_all_level_actors():
        if actor.get_actor_label().startswith("Floor"):
            actor_sub.destroy_actor(actor)


def pipeline(source, name, mesh_settings, common_settings=None):
    """A copy of a stock Interchange pipeline with the mesh options this scene needs."""
    path = f"{GAME}/Pipelines/{name}"
    if not eal.does_asset_exist(path):
        eal.duplicate_asset(source, path)
    asset = eal.load_asset(path)
    for group, settings in (("mesh_pipeline", mesh_settings), ("common_meshes_properties", common_settings or {})):
        target = asset.get_editor_property(group)
        for key, value in settings.items():
            target.set_editor_property(key, value)
    eal.save_loaded_asset(asset)
    return unreal.SoftObjectPath(asset.get_path_name())


GLTF_MATERIALS = unreal.SoftObjectPath("/Interchange/Pipelines/DefaultGLTFPipeline.DefaultGLTFPipeline")


def import_track():
    # track_ue.glb is track.glb without the nodes the game had hidden (clean_glb.py, run by build.cmd).
    source = unreal.InterchangeManager.create_source_data(os.path.join(IMPORT, "track_ue.glb"))
    params = unreal.ImportAssetParameters()
    params.is_automated = True
    params.override_pipelines = [
        pipeline("/Interchange/Pipelines/DefaultGLTFSceneAssetsPipeline", "TrackAssets", {"collision": False}),
        GLTF_MATERIALS,
        unreal.SoftObjectPath("/Interchange/Pipelines/DefaultSceneLevelPipeline.DefaultSceneLevelPipeline"),
    ]
    manager = unreal.InterchangeManager.get_interchange_manager_scripted()
    ok = manager.import_scene("/Game/Track/", source, params)
    manager.wait_until_all_tasks_done(False)
    log(f"track imported: {ok}")


def label_map():
    return {a.get_actor_label(): a for a in actor_sub.get_all_level_actors()}


def destroy_with_children(actor):
    for child in actor.get_attached_actors():
        destroy_with_children(child)
    actor_sub.destroy_actor(actor)


def mesh_components(actor):
    found = list(actor.get_components_by_class(unreal.StaticMeshComponent))
    for child in actor.get_attached_actors():
        found += mesh_components(child)
    return found


# ---- textures and materials ------------------------------------------------------------------------------

def assets_in(folder, class_name, contains=""):
    if not eal.does_directory_exist(folder):
        return []
    found = []
    for path in eal.list_assets(folder, recursive=True):
        if contains in path and eal.find_asset_data(path).asset_class_path.asset_name == class_name:
            found.append(eal.load_asset(path))
    return found


def import_texture(folder, stem, kind):
    """Imports one Poly Haven map (diff, nor_gl or arm) once and sets it up for its use."""
    dest = f"{GAME}/Textures/{folder}"
    if not assets_in(dest, "Texture2D", stem):
        task = unreal.AssetImportTask()
        task.filename = os.path.join(POLYHAVEN, "textures", folder, f"{stem}.jpg")
        task.destination_path = dest
        task.automated = True
        task.replace_existing = True
        asset_tools.import_asset_tasks([task])
    texture = assets_in(dest, "Texture2D", stem)[0]
    if kind == "normal":
        texture.set_editor_property("compression_settings", unreal.TextureCompressionSettings.TC_NORMALMAP)
        texture.set_editor_property("srgb", False)
        # Poly Haven's nor_gl is OpenGL convention (+Y up); Unreal reads DirectX (-Y).
        texture.set_editor_property("flip_green_channel", True)
    elif kind == "mask":
        texture.set_editor_property("compression_settings", unreal.TextureCompressionSettings.TC_MASKS)
        texture.set_editor_property("srgb", False)
    eal.save_loaded_asset(texture)
    return texture


def surface_material(name, folder, u_tiling, v_tiling, tint=(1.0, 1.0, 1.0), macro=0.0):
    """Base colour, normal and roughness/AO from one Poly Haven set, tiled on the mesh UVs.

    macro > 0 multiplies in the same texture at macro times the tile size as a slow brightness wobble, so a large
    ground does not show its repeat.
    """
    path = f"{GAME}/Materials/{name}"
    if eal.does_asset_exist(path):
        eal.delete_asset(path)
    material = asset_tools.create_asset(name, f"{GAME}/Materials", unreal.Material, unreal.MaterialFactoryNew())
    diff = import_texture(folder, f"{folder}_diff_2k", "colour")
    nor = import_texture(folder, f"{folder}_nor_gl_2k", "normal")
    arm = import_texture(folder, f"{folder}_arm_2k", "mask")

    def expression(cls, x, y):
        return mel.create_material_expression(material, cls, x, y)

    def sample(texture, sampler, uv, x, y):
        node = expression(unreal.MaterialExpressionTextureSample, x, y)
        node.set_editor_property("texture", texture)
        node.set_editor_property("sampler_type", sampler)
        mel.connect_material_expressions(uv, "", node, "UVs")
        return node

    uv = expression(unreal.MaterialExpressionTextureCoordinate, -1400, 0)
    uv.set_editor_property("u_tiling", u_tiling)
    uv.set_editor_property("v_tiling", v_tiling)
    base = sample(diff, unreal.MaterialSamplerType.SAMPLERTYPE_COLOR, uv, -900, -400)
    normal = sample(nor, unreal.MaterialSamplerType.SAMPLERTYPE_NORMAL, uv, -900, 0)
    masks = sample(arm, unreal.MaterialSamplerType.SAMPLERTYPE_MASKS, uv, -900, 400)

    colour = expression(unreal.MaterialExpressionMultiply, -500, -400)
    mel.connect_material_expressions(base, "RGB", colour, "A")
    tint_node = expression(unreal.MaterialExpressionConstant3Vector, -700, -250)
    tint_node.set_editor_property("constant", unreal.LinearColor(*tint, 1.0))
    if macro > 0:
        far_uv = expression(unreal.MaterialExpressionTextureCoordinate, -1400, -700)
        far_uv.set_editor_property("u_tiling", u_tiling / macro)
        far_uv.set_editor_property("v_tiling", v_tiling / macro)
        far = sample(diff, unreal.MaterialSamplerType.SAMPLERTYPE_COLOR, far_uv, -1100, -700)
        wobble = expression(unreal.MaterialExpressionLinearInterpolate, -800, -650)
        wobble.set_editor_property("const_a", 0.65)
        wobble.set_editor_property("const_b", 1.35)
        mel.connect_material_expressions(far, "G", wobble, "Alpha")
        tinted = expression(unreal.MaterialExpressionMultiply, -600, -600)
        mel.connect_material_expressions(tint_node, "", tinted, "A")
        mel.connect_material_expressions(wobble, "", tinted, "B")
        mel.connect_material_expressions(tinted, "", colour, "B")
    else:
        mel.connect_material_expressions(tint_node, "", colour, "B")
    mel.connect_material_property(colour, "", unreal.MaterialProperty.MP_BASE_COLOR)
    mel.connect_material_property(normal, "RGB", unreal.MaterialProperty.MP_NORMAL)
    mel.connect_material_property(masks, "G", unreal.MaterialProperty.MP_ROUGHNESS)
    mel.connect_material_property(masks, "R", unreal.MaterialProperty.MP_AMBIENT_OCCLUSION)
    mel.recompile_material(material)
    eal.save_loaded_asset(material)
    return material


def mirror_material():
    """Dark glass for the rear-view mirror. A polished-metal mirror filled with Lumen's reflections showed the red
    cabin shell behind the seats (the shell is single-sided from inside, the scene's surface cache is not); the game
    draws the view behind with a second camera, which a still render would need a planar reflection for."""
    mp = unreal.MaterialProperty
    return constant_material("M_Mirror", {mp.MP_BASE_COLOR: (0.02, 0.02, 0.022), mp.MP_METALLIC: 0.0, mp.MP_ROUGHNESS: 0.35})


def linear_vertex_colour(material, x, y):
    """The mesh's vertex colour as the linear value the glTF holds. Unreal stores vertex colours as sRGB-encoded
    bytes (StaticMeshBuilder: ToFColor(true)) and its Vertex Color node returns those bytes undecoded, which paled
    the car's red to pink and every prop colour with it; a 2.2 power undoes the encoding closely enough."""
    colour = mel.create_material_expression(material, unreal.MaterialExpressionVertexColor, x - 200, y)
    power = mel.create_material_expression(material, unreal.MaterialExpressionPower, x, y)
    power.set_editor_property("const_exponent", 2.2)
    mel.connect_material_expressions(colour, "", power, "Base")
    return power


def constant_material(name, inputs, setup=None):
    """A material of constants (and the vertex colour): inputs maps a material property, or an input name of a
    Make Material Attributes node ("ClearCoat": the clear-coat pins are hidden from scripting as properties), to a
    float, an RGB tuple, or "vertex" for the mesh's (linear) vertex colour."""
    material = asset_tools.create_asset(name, f"{GAME}/Materials", unreal.Material, unreal.MaterialFactoryNew())
    if setup:
        setup(material)
    attributes = None
    if any(isinstance(key, str) for key in inputs):
        material.set_editor_property("use_material_attributes", True)
        attributes = mel.create_material_expression(material, unreal.MaterialExpressionMakeMaterialAttributes, -150, 0)
        mel.connect_material_property(attributes, "", unreal.MaterialProperty.MP_MATERIAL_ATTRIBUTES)
    for y, (prop, value) in enumerate(inputs.items()):
        if value == "vertex":
            node = linear_vertex_colour(material, -300, y * 150)
        elif isinstance(value, tuple):
            node = mel.create_material_expression(material, unreal.MaterialExpressionConstant3Vector, -300, y * 150)
            node.set_editor_property("constant", unreal.LinearColor(*value, 1.0))
        else:
            node = mel.create_material_expression(material, unreal.MaterialExpressionConstant, -300, y * 150)
            node.set_editor_property("r", value)
        if attributes is not None:
            mel.connect_material_expressions(node, "", attributes, prop)
        else:
            mel.connect_material_property(node, "", prop)
    mel.recompile_material(material)
    eal.save_loaded_asset(material)
    return material


def descendants(actor):
    for child in actor.get_attached_actors():
        yield child
        yield from descendants(child)


def movable(actor):
    actor.root_component.set_mobility(unreal.ComponentMobility.MOVABLE)
    for child in actor.get_attached_actors():
        movable(child)


def tag(actor, name):
    actor.set_editor_property("tags", list(actor.get_editor_property("tags")) + [unreal.Name(name)])


def car_parts(car):
    """The car's actors by label (the first of each name: "Wheel" also names the steering wheel's node)."""
    parts = {}
    for actor in descendants(car):
        parts.setdefault(actor.get_actor_label(), actor)
    return parts


def mirror_glass(car):
    """RearMirror's glass: a stalk, the housing and the glass are made in that order, and the export names them
    only by number."""
    mirror = car_parts(car).get("RearMirror")
    if mirror is None:
        return None
    meshes = [a for a in mirror.get_attached_actors() if a.get_components_by_class(unreal.StaticMeshComponent)]
    number = lambda a: int(a.get_actor_label().rsplit("_", 1)[-1]) if a.get_actor_label().rsplit("_", 1)[-1].isdigit() else -1
    return max(meshes, key=number) if meshes else None


def rig_car(car, root_tag):
    """Tags what the GrudgeLapUE module's game (AGlpGame, FGlpCarView) moves in one car and makes it movable: the
    root (root_tag: GLP.Car for the first catalogue car, GLP.Car1 for the second), the wheel pivots (GLP.Wheel0..3,
    the simulation's order: front left, front right, rear left, rear right) and their rolling parts
    (GLP.Spin0..3), the steering wheel (GLP.Steering), the exterior the traffic and ghost copies keep
    (GLP.Exterior), what only shows from outside (GLP.OutsideOnly), the binnacle face (GLP.Dials) and the mirror
    glass (GLP.MirrorGlass)."""
    movable(car)
    tag(car, root_tag)
    parts = list(descendants(car))
    wheels = 0
    for index in range(4):
        wheel = next((a for a in parts if a.get_actor_label() == f"Wheel{index}"), None)
        if wheel is None:
            continue
        tag(wheel, f"GLP.Wheel{index}")
        spinner = next((a for a in wheel.get_attached_actors() if a.get_actor_label().startswith("Spin")), None)
        if spinner is not None:
            tag(spinner, f"GLP.Spin{index}")
            wheels += 1
    # CarView.BuildInterior: the steering wheel turns in a node "Wheel" on the "Column".
    steering = next((a for a in parts if a.get_actor_label() == "Wheel" and a.get_attach_parent_actor() is not None
                     and a.get_attach_parent_actor().get_actor_label() == "Column"), None)
    if steering is not None:
        tag(steering, "GLP.Steering")
    exterior = [a for a in parts if a.get_actor_label() in ("Shell", "Bonnet", "Glass", "Outside")]
    for actor in exterior:
        tag(actor, "GLP.Exterior")
        # CarView._outsideOnly: from the driver's seat only their shadows are drawn (the wheels are found by tag).
        if actor.get_actor_label() in ("Shell", "Outside"):
            tag(actor, "GLP.OutsideOnly")
    dials = next((a for a in parts if a.get_actor_label() == "Dials"), None)
    if dials is not None:
        tag(dials, "GLP.Dials")
    glass = mirror_glass(car)
    if glass is not None:
        tag(glass, "GLP.MirrorGlass")
    # The outside mirrors' glass (CarView: on the A-pillar of the early car, on the doors of the later one) is
    # named only by number; its polished-metal material gives it away. From the seat it shows the mirror's view.
    side = 0
    for actor in parts:
        for component in actor.get_components_by_class(unreal.StaticMeshComponent):
            material = component.get_material(0)
            try:
                metallic = mel.get_material_instance_scalar_parameter_value(material, "MetallicFactor")
                roughness = mel.get_material_instance_scalar_parameter_value(material, "RoughnessFactor")
            except Exception:
                continue
            if actor != glass and metallic > 0.99 and roughness < 0.06:
                tag(actor, "GLP.SideMirror")
                side += 1
                break
    log(f"{root_tag} tagged: {wheels} wheels, steering {'found' if steering else 'missing'}, {len(exterior)} exterior "
        f"parts, dials {'found' if dials else 'missing'}, mirror glass {glass.get_actor_label() if glass else 'missing'}, "
        f"{side} side mirror(s)")


def rig_world(actors):
    """Tags the train's cars (GLP.Train0..3) and the signs and crossbucks the car can knock over (GLP.Prop), and
    makes them movable."""
    train = actors.get("Train")
    cars = 0
    if train is not None:
        movable(train)
        for index, label in enumerate(("Locomotive", "Wagon1", "Wagon2", "Wagon3")):
            wagon = next((a for a in descendants(train) if a.get_actor_label() == label), None)
            if wagon is not None:
                tag(wagon, f"GLP.Train{index}")
                cars += 1
    props = 0
    for label, actor in actors.items():
        if label.startswith("Sign-") or label.startswith("Crossbuck-"):
            movable(actor)
            tag(actor, "GLP.Prop")
            props += 1
    log(f"world tagged: {cars} train cars, {props} signs")


def import_car(name):
    """A further catalogue car from Import/car_<name>.glb (extract_car.py takes it out of the game's export with
    --car=<name>), imported into /Game/Cars/<name> at the start beside the first car and hidden until the title
    screen chooses it. Returns its root actor, or None without the file."""
    path = os.path.join(IMPORT, f"car_{name}.glb")
    if not os.path.exists(path):
        log(f"no {path}: only the first car")
        return None
    before = set(a.get_path_name() for a in actor_sub.get_all_level_actors())
    source = unreal.InterchangeManager.create_source_data(path)
    params = unreal.ImportAssetParameters()
    params.is_automated = True
    params.override_pipelines = [
        pipeline("/Interchange/Pipelines/DefaultGLTFSceneAssetsPipeline", "TrackAssets", {"collision": False}),
        GLTF_MATERIALS,
        unreal.SoftObjectPath("/Interchange/Pipelines/DefaultSceneLevelPipeline.DefaultSceneLevelPipeline"),
    ]
    manager = unreal.InterchangeManager.get_interchange_manager_scripted()
    manager.import_scene(f"/Game/Cars/{name}/", source, params)
    manager.wait_until_all_tasks_done(False)
    added = [a for a in actor_sub.get_all_level_actors() if a.get_path_name() not in before]
    # The import hangs the file's nodes under an actor named after the file; the car is the node below it.
    root = next((a for a in added if a.get_actor_label().startswith("PlayerCar")), None)
    if root is None:
        log(f"car {name}: no PlayerCar among {len(added)} imported actors")
        return None
    root.set_actor_label(f"PlayerCar_{name}")
    for actor in [root] + list(descendants(root)):
        actor.set_actor_hidden_in_game(True)
    log(f"car {name} imported: {len(added)} actors")
    return root


def place_game(car, materials):
    """Places the game (AGlpGame) with the materials it sets at run time. Without the compiled module
    (Source/GrudgeLapUE) the level still builds, only without the game."""
    game_class = unreal.load_class(None, "/Script/GrudgeLapUE.GlpGame")
    if game_class is None:
        log("GrudgeLapUE module not compiled: no game placed")
        return
    game = actor_sub.spawn_actor_from_class(game_class, car.get_actor_location())
    game.set_actor_label("GrudgeLapGame")
    game.set_folder_path("GrudgeLap")
    for prop, name in (("paint_material", "M_CarPaint"), ("ghost_material", "M_Ghost"),
                       ("instruments_material", "M_Instruments"), ("mirror_material", "M_MirrorView")):
        game.set_editor_property(prop, materials[name])
    log("game placed")


def paint_material():
    """The car paint: a clear-coated metallic over the colour Godot wrote into the vertex colours, (0.757, 0.027,
    0.047) on the paint and a dark grey on the seams and trim. The vector parameter Paint replaces the red and
    leaves the trim, so the traffic copies (FGlpCarView::Clone) wear the original drones' colours on the same
    material; its default is the red itself, so the player's car is unchanged."""
    material = asset_tools.create_asset("M_CarPaint", f"{GAME}/Materials", unreal.Material, unreal.MaterialFactoryNew())
    material.set_editor_property("shading_model", unreal.MaterialShadingModel.MSM_CLEAR_COAT)
    material.set_editor_property("use_material_attributes", True)
    attributes = mel.create_material_expression(material, unreal.MaterialExpressionMakeMaterialAttributes, 0, 0)
    mel.connect_material_property(attributes, "", unreal.MaterialProperty.MP_MATERIAL_ATTRIBUTES)
    vertex = linear_vertex_colour(material, -1000, 0)

    def channel(name, y):
        mask = mel.create_material_expression(material, unreal.MaterialExpressionComponentMask, -800, y)
        for c in "rgba":
            mask.set_editor_property(c, c == name)
        mel.connect_material_expressions(vertex, "", mask, "")
        return mask

    # Red minus green: 0.73 on the paint, 0 on the grey trim; scaled and clamped, the share of paint.
    difference = mel.create_material_expression(material, unreal.MaterialExpressionSubtract, -650, 0)
    mel.connect_material_expressions(channel("r", -80), "", difference, "A")
    mel.connect_material_expressions(channel("g", 80), "", difference, "B")
    scaled = mel.create_material_expression(material, unreal.MaterialExpressionMultiply, -500, 0)
    scaled.set_editor_property("const_b", 1.0 / 0.7)
    mel.connect_material_expressions(difference, "", scaled, "A")
    share = mel.create_material_expression(material, unreal.MaterialExpressionSaturate, -380, 0)
    mel.connect_material_expressions(scaled, "", share, "")
    paint = mel.create_material_expression(material, unreal.MaterialExpressionVectorParameter, -500, 200)
    paint.set_editor_property("parameter_name", "Paint")
    paint.set_editor_property("default_value", unreal.LinearColor(0.757, 0.027, 0.047, 1.0))
    colour = mel.create_material_expression(material, unreal.MaterialExpressionLinearInterpolate, -250, 0)
    mel.connect_material_expressions(vertex, "", colour, "A")
    mel.connect_material_expressions(paint, "", colour, "B")
    mel.connect_material_expressions(share, "", colour, "Alpha")
    mel.connect_material_expressions(colour, "", attributes, "BaseColor")
    for y, (pin, value) in enumerate((("Metallic", 0.35), ("Roughness", 0.38), ("ClearCoat", 1.0), ("ClearCoatRoughness", 0.04))):
        constant = mel.create_material_expression(material, unreal.MaterialExpressionConstant, -250, 200 + y * 100)
        constant.set_editor_property("r", value)
        mel.connect_material_expressions(constant, "", attributes, pin)
    mel.recompile_material(material)
    eal.save_loaded_asset(material)
    return material


def screen_material(name, parameter, lit, emissive_scale, flip_u=False, screen_units=True):
    """A surface that shows a render target the game draws (the instrument panel, the mirror's view). With
    screen_units its glow is given in the light units of the screen rather than the scene's (Eye Adaptation
    Inverse): the level's fixed exposure is set for sunlight and would leave a plain emissive black. Without, the
    texture already holds the scene's light (the mirror's capture before exposure) and goes through as it is."""
    material = asset_tools.create_asset(name, f"{GAME}/Materials", unreal.Material, unreal.MaterialFactoryNew())
    if not lit:
        material.set_editor_property("shading_model", unreal.MaterialShadingModel.MSM_UNLIT)
    sample = mel.create_material_expression(material, unreal.MaterialExpressionTextureSampleParameter2D, -700, 0)
    sample.set_editor_property("parameter_name", parameter)
    sample.set_editor_property("texture", eal.load_asset("/Engine/EngineResources/DefaultTexture"))
    if flip_u:
        # A mirror shows the scene left-right reversed: u becomes 1 - u.
        coordinates = mel.create_material_expression(material, unreal.MaterialExpressionTextureCoordinate, -1100, 0)
        coordinates.set_editor_property("u_tiling", -1.0)
        offset = mel.create_material_expression(material, unreal.MaterialExpressionConstant2Vector, -1100, 150)
        offset.set_editor_property("r", 1.0)
        offset.set_editor_property("g", 0.0)
        shifted = mel.create_material_expression(material, unreal.MaterialExpressionAdd, -900, 0)
        mel.connect_material_expressions(coordinates, "", shifted, "A")
        mel.connect_material_expressions(offset, "", shifted, "B")
        mel.connect_material_expressions(shifted, "", sample, "UVs")
    glow = mel.create_material_expression(material, unreal.MaterialExpressionMultiply, -450, 100)
    glow.set_editor_property("const_b", emissive_scale)
    mel.connect_material_expressions(sample, "RGB", glow, "A")
    if screen_units:
        screen = mel.create_material_expression(material, unreal.MaterialExpressionEyeAdaptationInverse, -250, 100)
        mel.connect_material_expressions(glow, "", screen, "")
        mel.connect_material_property(screen, "", unreal.MaterialProperty.MP_EMISSIVE_COLOR)
    else:
        mel.connect_material_property(glow, "", unreal.MaterialProperty.MP_EMISSIVE_COLOR)
    if lit:
        mel.connect_material_property(sample, "RGB", unreal.MaterialProperty.MP_BASE_COLOR)
        roughness = mel.create_material_expression(material, unreal.MaterialExpressionConstant, -250, 250)
        roughness.set_editor_property("r", 0.3)
        mel.connect_material_property(roughness, "", unreal.MaterialProperty.MP_ROUGHNESS)
    mel.recompile_material(material)
    eal.save_loaded_asset(material)
    return material


def game_materials(paint):
    """The materials the game sets at run time: the paint (whose Paint parameter colours the traffic), the ghost,
    the binnacle's instrument face and the mirror's glass."""
    mp = unreal.MaterialProperty

    def ghostly(material):
        material.set_editor_property("blend_mode", unreal.BlendMode.BLEND_TRANSLUCENT)
        material.set_editor_property("translucency_lighting_mode", unreal.TranslucencyLightingMode.TLM_SURFACE_PER_PIXEL_LIGHTING)

    # CarView.MakeGhost: pale blue at 30 % opacity with a blue glow, in the screen's light units like the panels.
    ghost = constant_material("M_Ghost", {mp.MP_BASE_COLOR: (0.55, 0.85, 1.0), mp.MP_OPACITY: 0.30, mp.MP_ROUGHNESS: 0.4}, ghostly)
    glow = mel.create_material_expression(ghost, unreal.MaterialExpressionConstant3Vector, -500, 500)
    glow.set_editor_property("constant", unreal.LinearColor(0.25 * 0.9, 0.55 * 0.9, 0.85 * 0.9, 1.0))
    screen = mel.create_material_expression(ghost, unreal.MaterialExpressionEyeAdaptationInverse, -250, 500)
    mel.connect_material_expressions(glow, "", screen, "")
    mel.connect_material_property(screen, "", mp.MP_EMISSIVE_COLOR)
    mel.recompile_material(ghost)
    eal.save_loaded_asset(ghost)
    # CarView.MountInstruments: the panel as albedo with an emission of 0.45. The mirror: unshaded, u flipped, the
    # light behind at the 85 % a silvered glass gives back.
    instruments = screen_material("M_Instruments", "Panel", lit=True, emissive_scale=0.45)
    mirror = screen_material("M_MirrorView", "View", lit=False, emissive_scale=0.85, flip_u=True, screen_units=False)
    return {"M_CarPaint": paint, "M_Ghost": ghost, "M_Instruments": instruments, "M_MirrorView": mirror}


def car_materials():
    """The paint (paint_material), the glass and the mirror glass's stand-in, shared by every car."""
    mp = unreal.MaterialProperty

    def glassy(material):
        material.set_editor_property("blend_mode", unreal.BlendMode.BLEND_TRANSLUCENT)
        material.set_editor_property("translucency_lighting_mode", unreal.TranslucencyLightingMode.TLM_SURFACE_PER_PIXEL_LIGHTING)
        material.set_editor_property("two_sided", True)

    glass = constant_material("M_Glass", {mp.MP_BASE_COLOR: (0.02, 0.025, 0.03), mp.MP_OPACITY: 0.14,
                                          mp.MP_ROUGHNESS: 0.02, mp.MP_SPECULAR: 0.5}, glassy)
    return {"paint": paint_material(), "glass": glass, "mirror": mirror_material()}


def dress_car(car, materials):
    """Paint and glass on one car. The game's paint is flat red in the vertex colour (with darker panel seams);
    here it is a clear-coated metallic paint over that colour (paint_material). The glass, which the cockpit view
    hid at export, becomes a clear windscreen every view keeps; translucency cannot be drawn by Nanite, so its mesh
    is built without. The rear-view mirror's glass gets dark glass for the stills (the game shows its camera)."""
    parts = car_parts(car)
    painted = 0
    for label in ("Shell", "Bonnet"):
        for component in mesh_components(parts[label]) if label in parts else []:
            for slot in range(component.get_num_materials()):
                component.set_material(slot, materials["paint"])
            painted += 1
    glazed = 0
    for component in mesh_components(parts["Glass"]) if "Glass" in parts else []:
        mesh = component.get_editor_property("static_mesh")
        settings = mesh.get_editor_property("nanite_settings")
        if settings.enabled:
            settings.enabled = False
            mesh.modify()
            mesh.set_editor_property("nanite_settings", settings)
            eal.save_loaded_asset(mesh, False)
        for slot in range(component.get_num_materials()):
            component.set_material(slot, materials["glass"])
        glazed += 1
    glass = mirror_glass(car)
    for component in mesh_components(glass) if glass else []:
        component.set_material(0, materials["mirror"])
    # The body shell is single-sided and seen from inside in the cockpit view (the game draws it as shadows only
    # there). Lumen's surface cache does not know it is one-sided and bounced its red paint all over the cabin;
    # the shell keeps its direct shadows and leaves the bounce light to the cabin lining.
    shell = 0
    for label in ("Shell", "Bonnet", "Outside"):
        for component in mesh_components(parts[label]) if label in parts else []:
            component.set_editor_property("affect_dynamic_indirect_lighting", False)
            component.set_editor_property("affect_distance_field_lighting", False)
            shell += 1
    log(f"{car.get_actor_label()} dressed: {painted} painted, {glazed} glass, {shell} shell part(s) out of Lumen, "
        f"mirror glass {'found' if glass else 'missing'}")


def dress_track(actors):
    # Terrain UVs span the 5 km ground (world * 0.0002 + 0.5); road UVs run 0..1 across 11 m and in metres along;
    # shoulder strips run 0..1 across 1.3 m. Tile sizes are the scans' real ones (Poly Haven "dimensions").
    # Mean linear albedos measured from the scans: asphalt 0.10 (kept), the aerial grass (0.17, 0.12, 0.03) is a
    # dry yellow; the tint turns it to a meadow's (0.09, 0.12, 0.03).
    ground = surface_material("M_Ground", "aerial_grass_rock", 5000 / 15.0, 5000 / 15.0, tint=(0.5, 1.0, 1.3), macro=23.0)
    asphalt = surface_material("M_Asphalt", "asphalt_02", 11.0 / 3.0, 1 / 3.0, tint=(0.9, 0.9, 0.9))
    verge = surface_material("M_Verge", "grass_path_2", 1.3, 1.0)
    for label, actor in actors.items():
        material = ground if label == "Terrain" else asphalt if label.startswith("Road-") else verge if label.startswith("Shoulder-") else None
        if material is None:
            continue
        for component in mesh_components(actor):
            for slot in range(component.get_num_materials()):
                component.set_material(slot, material)
            if label == "Terrain":
                # A mesh distance field spans at most a few hundred voxels, 25 to 50 m each across the 6 km terrain:
                # Lumen's software tracing then saw the 1.4 m roadside ditches as inside the ground and left them
                # black. The sun's shadows come from virtual shadow maps either way.
                component.set_editor_property("affect_distance_field_lighting", False)
    log("track dressed")


# ---- photoscanned plants and rocks -----------------------------------------------------------------------

def poly_haven_meshes(name, foliage_pipeline):
    """The model's meshes. Poly Haven's grass and shrub files hold several variants side by side; each is its
    own mesh around its own origin (the pipeline does not bake node transforms), so each can be scattered.

    The <model>_ue.gltf copy is imported: prepare_foliage.py (run by build.cmd) gives its leaves and blades an
    RGBA base colour and alpha testing, which Nanite can draw.
    """
    dest = f"{GAME}/PolyHaven/{name}"
    meshes = assets_in(dest, "StaticMesh")
    if not meshes:
        folder = os.path.join(POLYHAVEN, "models", name)
        gltf = next((f for f in os.listdir(folder) if f.endswith("_ue.gltf")), None)
        if gltf is None:
            log(f"missing prepared Poly Haven model {name} (fetch_polyhaven.py, then prepare_foliage.py)")
            return None
        task = unreal.AssetImportTask()
        task.filename = os.path.join(folder, gltf)
        task.destination_path = dest
        task.automated = True
        task.replace_existing = True
        task.save = True
        options = unreal.InterchangePipelineStackOverride()
        options.set_editor_property("override_pipelines", [foliage_pipeline, GLTF_MATERIALS])
        task.options = options
        asset_tools.import_asset_tasks([task])
        meshes = assets_in(dest, "StaticMesh")
    log(f"{name}: {[m.get_name() for m in meshes]}")
    return meshes


def instanced(label, mesh, transforms, shadows=True, cull=0):
    """One actor holding every instance of a mesh (an instanced static mesh component); instances past cull
    centimetres are not drawn (0: always drawn)."""
    actor = actor_sub.spawn_actor_from_class(unreal.Actor, unreal.Vector(0, 0, 0))
    actor.set_actor_label(label)
    actor.set_folder_path("GrudgeLap/Foliage")
    handles = subobjects.k2_gather_subobject_data_for_instance(actor)
    params = unreal.AddNewSubobjectParams(parent_handle=handles[0], new_class=unreal.InstancedStaticMeshComponent)
    handle, reason = subobjects.add_new_subobject(params)
    component = unreal.SubobjectDataBlueprintFunctionLibrary.get_object(unreal.SubobjectDataBlueprintFunctionLibrary.get_data(handle))
    if component is None:
        raise RuntimeError(f"could not add an instanced mesh component: {reason}")
    component.set_static_mesh(mesh)
    component.set_collision_enabled(unreal.CollisionEnabled.NO_COLLISION)
    component.set_editor_property("cast_shadow", shadows)
    component.add_instances(transforms, False, True)
    if cull:
        component.set_cull_distances(0, cull)
    log(f"{label}: {len(transforms)} instances of {mesh.get_name()}")
    return actor


def transform(position, yaw_degrees, scale, sink=0.0):
    location = to_unreal(position)
    location.z -= sink
    return unreal.Transform(location=location, rotation=unreal.Rotator(0.0, 0.0, yaw_degrees), scale=unreal.Vector(scale, scale, scale))


def scatter(placements, extra, actors):
    foliage = pipeline("/Interchange/Pipelines/DefaultGLTFAssetsPipeline", "Foliage",
                       {"combine_static_meshes_behavior": unreal.InterchangeCombineStaticMeshesBehavior.DO_NOT_COMBINE,
                        "build_nanite": True, "collision": False},
                       {"bake_meshes": False})
    meshes = {name: poly_haven_meshes(name, foliage) or [] for name in
              ("grass_medium_01", "grass_medium_02", "shrub_02", "shrub_03", "shrub_04", "island_tree_02",
               "tree_small_02", "jacaranda_tree", "rock_07", "rock_09", "boulder_01")}

    # Alpha-tested leaves and blades lose coverage as Nanite simplifies them with distance, until a tree 80 m away
    # is bare branches; preserving the area keeps the canopy.
    preserved = 0
    for name in ("grass_medium_01", "grass_medium_02", "shrub_02", "shrub_03", "shrub_04", "island_tree_02",
                 "tree_small_02", "jacaranda_tree"):
        for mesh in meshes[name]:
            settings = mesh.get_editor_property("nanite_settings")
            if settings.shape_preservation != unreal.NaniteShapePreservation.PRESERVE_AREA:
                settings.shape_preservation = unreal.NaniteShapePreservation.PRESERVE_AREA
                mesh.modify()
                mesh.set_editor_property("nanite_settings", settings)
                # Setting a struct property does not mark the package dirty; save it regardless.
                eal.save_loaded_asset(mesh, False)
                preserved += 1

    # Alpha testing against mipmaps that average the cut-out away: a leaf texture with 24 % coverage drops below
    # the 0.5 cut-off a few mips down, and every tree past 30 m stood bare. Scaling each mip's alpha to keep the
    # coverage of the cut-off holds the canopy at any distance.
    for name in ("grass_medium_01", "grass_medium_02", "shrub_02", "shrub_03", "shrub_04", "island_tree_02",
                 "tree_small_02", "jacaranda_tree"):
        for texture in assets_in(f"{GAME}/PolyHaven/{name}", "Texture2D", "_alpha"):
            if not texture.get_editor_property("do_scale_mips_for_alpha_coverage"):
                texture.set_editor_property("do_scale_mips_for_alpha_coverage", True)
                texture.set_editor_property("alpha_coverage_thresholds", unreal.Vector4(0.0, 0.0, 0.0, 0.5))
                eal.save_loaded_asset(texture)

    log(f"Nanite area preservation set on {preserved} foliage mesh(es)")

    def place(label, choices, spots, cull=0):
        """choices: (mesh, scale, weight); spots: (position, yaw degrees, scale, sink cm). One component per mesh."""
        sets = {}
        weights = [w for _, _, w in choices]
        for position, yaw, scale, sink in spots:
            mesh, base, _ = rng.choices(choices, weights)[0]
            sets.setdefault(mesh.get_path_name(), (mesh, []))[1].append(transform(position, yaw, base * scale, sink))
        for i, (mesh, transforms) in enumerate(sets.values()):
            instanced(f"{label}{i}", mesh, transforms, cull=cull)

    # 22 scanned tufts of 10 to 40 cm; the meadow takes them at 1 to 2.8 times (prepare_scatter.py).
    grass = [(m, 1.0, 1.0) for m in meshes["grass_medium_01"] + meshes["grass_medium_02"]]
    # The grass now lines every road (a quarter of a million tufts); a tuft under a metre tall is gone to the eye
    # long before 300 m, so it is not drawn beyond.
    if grass:
        place("Grass", grass, [((x, y, z), math.degrees(yaw), scale, 2.0) for x, y, z, yaw, scale in extra["grass"]], cull=30000)

    for kind in ("rock_07", "rock_09", "boulder_01"):
        if meshes[kind]:
            place(f"Rocks-{kind}", [(meshes[kind][0], 1.0, 1.0)],
                  [(r["position"], math.degrees(r["yaw"]), r["scale"], 0.0) for r in extra["rocks"] if r["kind"] == kind])

    # The game's trees are 10 to 12 m. The jacaranda scan is a 19.5 m tree and shrinks; the two small scans
    # (3.4 and 4.6 m) grow, which looks right from the road but would not up close.
    trees = [(m, s, w) for ms, s, w in ((meshes["jacaranda_tree"], 0.62, 3.0), (meshes["tree_small_02"], 2.4, 2.0),
                                        (meshes["island_tree_02"], 3.0, 1.0)) for m in ms[:1]]
    spots = [(p["position"], rng.uniform(0, 360), rng.uniform(0.9, 1.15), 10.0) for p in placements["props"] if p["kind"] == "Tree"]
    spots += [(t["position"], t["yaw"], t["scale"], 10.0) for t in extra["trees"]]
    if trees:
        place("Trees", trees, spots)

    bushes = [(m, s, 1.0) for ms, s in ((meshes["shrub_02"], 1.0), (meshes["shrub_03"], 2.5), (meshes["shrub_04"], 2.0)) for m in ms]
    if bushes:
        place("Bushes", bushes, [(b["position"], b["yaw"], b["scale"], 3.0) for b in extra["bushes"]], cull=80000)

    # The scanned trees stand where the game's leaf-card trees stood; those go.
    removed = [label for label in actors if label.startswith("Tree-")]
    for label in removed:
        destroy_with_children(actors[label])
    log(f"{len(removed)} of the game's trees removed")


# ---- materials -------------------------------------------------------------------------------------------

USAGES = (unreal.MaterialUsage.MATUSAGE_NANITE, unreal.MaterialUsage.MATUSAGE_INSTANCED_STATIC_MESHES)


def multiply_vertex_colour(material, prop):
    """The colour feeding prop (base colour, or emissive for an unlit material) times the vertex colour."""
    source = mel.get_material_property_input_node(material, prop)
    colour = linear_vertex_colour(material, -500, -200)
    if source is None:
        mel.connect_material_property(colour, "", prop)
        return
    output = mel.get_material_property_input_node_output_name(material, prop)
    product = mel.create_material_expression(material, unreal.MaterialExpressionMultiply, -250, -200)
    mel.connect_material_expressions(source, output, product, "A")
    mel.connect_material_expressions(colour, "", product, "B")
    mel.connect_material_property(product, "", prop)


def screen_units_emissive(material):
    """The emissive in the screen's light units (Eye Adaptation Inverse). The level's exposure is fixed for
    sunlight, under which the glow the game gives its lamps and the colour of its unshaded surfaces came out
    black; the screen's units give them the brightness they have in the Godot version."""
    prop = unreal.MaterialProperty.MP_EMISSIVE_COLOR
    source = mel.get_material_property_input_node(material, prop)
    if source is None:
        return
    output = mel.get_material_property_input_node_output_name(material, prop)
    screen = mel.create_material_expression(material, unreal.MaterialExpressionEyeAdaptationInverse, -150, -350)
    mel.connect_material_expressions(source, output, screen, "")
    mel.connect_material_property(screen, "", prop)


def vertex_colour_parent(parent, copies):
    """A /Game copy of an Interchange glTF parent (an MI_* instance and its M_* base) that multiplies in the
    vertex colour and carries the usage flags this scene needs.

    Godot writes the car's paint, the road lines and the signs as vertex colours. Interchange imports them onto the
    meshes but leaves them out of the materials (InterchangeGltfTranslator works out bUseVertexColor and never
    passes it on), which turns the red car white and the yellow centre line white. glTF defines the base colour
    as the factor times the texture times the vertex colour, so the copies do exactly that. Their glow is given in
    the screen's units (screen_units_emissive).
    """
    name = parent.get_name()
    if name in copies:
        return copies[name]
    base = parent.get_base_material()
    base_name = f"{base.get_name()}_VertexColour"
    if base_name not in copies:
        path = f"{GAME}/Materials/{base_name}"
        material = eal.duplicate_asset(base.get_path_name().split(".")[0], path)
        lit = material.get_editor_property("shading_model") != unreal.MaterialShadingModel.MSM_UNLIT
        multiply_vertex_colour(material, unreal.MaterialProperty.MP_BASE_COLOR if lit else unreal.MaterialProperty.MP_EMISSIVE_COLOR)
        screen_units_emissive(material)
        for usage in USAGES:
            mel.set_material_usage(material, usage)
        mel.recompile_material(material)
        eal.save_loaded_asset(material)
        copies[base_name] = material
    if parent == base:
        copies[name] = copies[base_name]
        return copies[name]
    instance = eal.duplicate_asset(parent.get_path_name().split(".")[0], f"{GAME}/Materials/{name}_VertexColour")
    mel.set_material_instance_parent(instance, copies[base_name])
    mel.update_material_instance(instance)
    eal.save_loaded_asset(instance)
    copies[name] = instance
    return instance


def prepare_materials():
    """Readies every material this scene imported or made for how the render draws it.

    The track and the scans are Nanite meshes and the scans are drawn by instanced components; a material needs
    the matching usage flags. The editor sets them the moment it meets such a mesh, but a -game render cannot save
    a material and draws the default material instead, so the flags are set and saved here: on this scene's own
    materials, on the vertex-colour copies of the glTF parents the track's instances move to, and as overrides on
    the scans' instances (their parents live in the engine's Interchange content).
    """
    copies = {}
    counts = {"materials": 0, "track instances": 0, "scan instances": 0}
    for root in ("/Game/GrudgeLap/Materials", "/Game/Track", "/Game/Cars", "/Game/GrudgeLap/PolyHaven"):
        for path in eal.list_assets(root, recursive=True):
            kind = str(eal.find_asset_data(path).asset_class_path.asset_name)
            if kind == "Material" and root == "/Game/GrudgeLap/Materials":
                material = eal.load_asset(path)
                translucent = material.get_editor_property("blend_mode") == unreal.BlendMode.BLEND_TRANSLUCENT
                for usage in USAGES[1:] if translucent else USAGES:
                    mel.set_material_usage(material, usage)
                mel.recompile_material(material)
                eal.save_loaded_asset(material)
                counts["materials"] += 1
            elif kind == "MaterialInstanceConstant" and root in ("/Game/Track", "/Game/Cars"):
                instance = eal.load_asset(path)
                mel.set_material_instance_parent(instance, vertex_colour_parent(instance.get_editor_property("parent"), copies))
                mel.update_material_instance(instance)
                eal.save_loaded_asset(instance)
                counts["track instances"] += 1
            elif kind == "MaterialInstanceConstant":
                instance = eal.load_asset(path)
                for usage in USAGES:
                    mel.set_material_usage_override(instance, usage, True, True)
                eal.save_loaded_asset(instance)
                counts["scan instances"] += 1
    log(f"materials prepared: {counts}; vertex-colour parents: {sorted(copies)}")


# ---- cameras, light, look, render queue ------------------------------------------------------------------

def cine_camera(label, eye, forward, vertical_fov, exposure=None):
    """A cine camera with the game's field of view. exposure (EV100 min, max) overrides the level's fixed exposure
    for this camera only."""
    actor = actor_sub.spawn_actor_from_class(unreal.CineCameraActor, eye, unreal.MathLibrary.make_rot_from_x(forward))
    actor.set_actor_label(label)
    actor.set_folder_path("GrudgeLap/Cameras")
    camera = actor.get_cine_camera_component()
    filmback = camera.get_editor_property("filmback")
    filmback.sensor_width = 36.0
    filmback.sensor_height = 20.25
    camera.set_editor_property("filmback", filmback)
    # The game's vertical field of view on a 16:9 sensor.
    camera.set_editor_property("current_focal_length", 10.125 / math.tan(math.radians(vertical_fov / 2)))
    focus = camera.get_editor_property("focus_settings")
    focus.focus_method = unreal.CameraFocusMethod.DISABLE
    camera.set_editor_property("focus_settings", focus)
    if exposure:
        settings = camera.get_editor_property("post_process_settings")
        settings.set_editor_property("override_auto_exposure_min_brightness", True)
        settings.set_editor_property("auto_exposure_min_brightness", exposure[0])
        settings.set_editor_property("override_auto_exposure_max_brightness", True)
        settings.set_editor_property("auto_exposure_max_brightness", exposure[1])
        camera.set_editor_property("post_process_settings", settings)
        camera.set_editor_property("post_process_blend_weight", 1.0)
    return actor


def cameras(placements, extra):
    """Cine cameras for the shots: the game's cockpit and chase views, and a photographer's view from the meadow."""
    cam = placements["camera"]
    cockpit = cine_camera("CockpitCamera", to_unreal(cam["eye"]), direction(cam["forward"]), cam["fov"])
    # A plain -game run (the frame-rate measurement in perf.cmd) looks through the cockpit; the renders follow
    # their sequences' camera cuts regardless.
    cockpit.set_editor_property("auto_activate_for_player", unreal.AutoReceiveInput.PLAYER0)
    # The chase camera where CameraRig settles it: 1.9 m over the ground and 6.3 m behind the car, looking at a point
    # 0.7 m up and 2 m ahead. The car's origin is its centre of gravity, 0.61 m over the flat start.
    car = placements["car"]
    origin, (bx, by, bz) = car["position"], car["basis"]
    cg_height = 0.61

    def local(x, y, z):
        return [origin[i] + bx[i] * x + by[i] * y + bz[i] * z for i in range(3)]

    eye = local(0.0, 1.9 - cg_height, 6.3)
    focus = local(0.0, 0.7 - cg_height, -2.0)
    chase = cine_camera("ChaseCamera", to_unreal(eye), direction([f - e for f, e in zip(focus, eye)]), 62.0)
    shots = {"Cockpit": cockpit, "Chase": chase}
    roadside = extra.get("roadside_camera")
    if roadside:
        shots["Roadside"] = cine_camera("RoadsideCamera", to_unreal(roadside["eye"]),
                                        direction([t - e for t, e in zip(roadside["target"], roadside["eye"])]), roadside["fov"])
    return shots


SUN_LUX = 75000.0   # clear-sky sun at about 48 degrees
EXPOSURE_EV100 = 14.0


def light_and_look():
    """Godot's sun direction on the template's sun, at a real sun's strength, and a fixed exposure; returns the sun.

    The sun and the sky light are made movable with a real-time sky capture, so a sequence can move the sun
    (golden_hour_shot) and the sky's ambient light follows it. Auto exposure brightened the cockpit shot to suit
    the dark cabin until the sky and the road burnt out; a camera (and the eye) set for the sunlit road outside
    sees the cabin dark, as the game shows it. EV100 14 puts a sunlit meadow (about 2000 cd/m2 under 75000 lux)
    at middle grey.
    """
    sun_actor = None
    for actor in actor_sub.get_all_level_actors():
        if isinstance(actor, unreal.DirectionalLight):
            sun_actor = actor
            actor.root_component.set_mobility(unreal.ComponentMobility.MOVABLE)
            actor.set_actor_rotation(unreal.MathLibrary.make_rot_from_x(direction([-c for c in SUN_DIRECTION])), False)
            actor.light_component.set_intensity(SUN_LUX)
        elif isinstance(actor, unreal.SkyLight):
            actor.root_component.set_mobility(unreal.ComponentMobility.MOVABLE)
            actor.light_component.set_editor_property("real_time_capture", True)
    volume = actor_sub.spawn_actor_from_class(unreal.PostProcessVolume, unreal.Vector(0, 0, 0))
    volume.set_actor_label("Look")
    volume.set_folder_path("GrudgeLap")
    volume.set_editor_property("unbound", True)
    settings = volume.get_editor_property("settings")
    for name, value in (("override_lumen_final_gather_quality", True), ("lumen_final_gather_quality", 2.0),
                        ("override_lumen_scene_lighting_quality", True), ("lumen_scene_lighting_quality", 2.0),
                        ("override_lumen_reflection_quality", True), ("lumen_reflection_quality", 2.0),
                        ("override_vignette_intensity", True), ("vignette_intensity", 0.25),
                        ("override_auto_exposure_min_brightness", True), ("auto_exposure_min_brightness", EXPOSURE_EV100),
                        ("override_auto_exposure_max_brightness", True), ("auto_exposure_max_brightness", EXPOSURE_EV100),
                        # The default motion blur smeared the grass beside the road into streaks at 130 km/h; a
                        # trace of it keeps the sense of speed (the Godot version has none).
                        ("override_motion_blur_amount", True), ("motion_blur_amount", 0.15),
                        ("override_motion_blur_max", True), ("motion_blur_max", 2.0)):
        try:
            settings.set_editor_property(name, value)
        except Exception as error:  # a renamed setting must not stop the build
            log(f"post-process {name}: {error}")
    volume.set_editor_property("settings", settings)
    return sun_actor


def shot(name, camera):
    path = f"{GAME}/Render/{name}"
    if eal.does_asset_exist(path):
        eal.delete_asset(path)
    sequence = asset_tools.create_asset(name, f"{GAME}/Render", unreal.LevelSequence, unreal.LevelSequenceFactoryNew())
    sequence.set_display_rate(unreal.FrameRate(24, 1))
    sequence.set_playback_start(0)
    sequence.set_playback_end(1)
    binding = sequence.add_possessable(camera)
    cut = sequence.add_track(unreal.MovieSceneCameraCutTrack).add_section()
    cut.set_range(0, 1)
    cut.set_camera_binding_id(sequence.get_binding_id(binding))
    eal.save_loaded_asset(sequence)
    return sequence


def golden_hour_shot(name, cockpit, sun):
    """The cockpit view under a low evening sun ahead and to the left: what the lighting can do, beyond the
    like-for-like pictures. The sequence holds the sun's rotation, so the level itself keeps Godot's sun; a copy
    of the cockpit camera holds an evening exposure (EV100 12; auto exposure brightened it to the dark cabin)."""
    focal = cockpit.get_cine_camera_component().get_editor_property("current_focal_length")
    camera = cine_camera("EveningCamera", cockpit.get_actor_location(), cockpit.get_actor_forward_vector(),
                         math.degrees(2 * math.atan(10.125 / focal)), exposure=(12.0, 12.0))
    sequence = shot(name, camera)
    section = sequence.add_possessable(sun).add_track(unreal.MovieScene3DTransformTrack).add_section()
    section.set_range(0, 1)
    location = sun.get_actor_location()
    # The light shines away from the sun: 7 degrees down, from 50 degrees left of the camera's heading.
    yaw = camera.get_actor_rotation().yaw - 50.0 + 180.0
    values = {"Location.X": location.x, "Location.Y": location.y, "Location.Z": location.z,
              "Rotation.X": 0.0, "Rotation.Y": -7.0, "Rotation.Z": yaw, "Scale.X": 1.0, "Scale.Y": 1.0, "Scale.Z": 1.0}
    for channel in section.get_all_channels():
        key = str(channel.get_editor_property("channel_name"))
        if key in values:
            channel.set_default(values[key])
    eal.save_loaded_asset(sequence)
    return sequence


def render_queue(sequences):
    queue = unreal.MoviePipelineQueue()
    for sequence in sequences:
        job = queue.allocate_new_job(unreal.MoviePipelineExecutorJob)
        job.job_name = sequence.get_name()
        job.map = unreal.SoftObjectPath(f"{MAP}.{MAP.rsplit('/', 1)[1]}")
        job.sequence = unreal.SoftObjectPath(sequence.get_path_name())
        config = job.get_configuration()
        output = config.find_or_add_setting_by_class(unreal.MoviePipelineOutputSetting)
        output.output_directory = unreal.DirectoryPath(RENDER_OUT)
        output.file_name_format = "{sequence_name}"
        output.output_resolution = unreal.IntPoint(1920, 1080)
        output.override_existing_output = True
        config.find_or_add_setting_by_class(unreal.MoviePipelineDeferredPassBase)
        config.find_or_add_setting_by_class(unreal.MoviePipelineImageSequenceOutput_PNG)
        aa = config.find_or_add_setting_by_class(unreal.MoviePipelineAntiAliasingSetting)
        aa.spatial_sample_count = 1
        aa.temporal_sample_count = 8
        aa.override_anti_aliasing = True
        aa.anti_aliasing_method = unreal.AntiAliasingMethod.AAM_NONE
        # Lumen, eye adaptation and the virtual shadow maps settle over frames; a still needs them settled.
        aa.render_warm_up_frames = True
        aa.render_warm_up_count = 96
        aa.engine_warm_up_count = 32
        aa.use_camera_cut_for_warm_up = False
        game = config.find_or_add_setting_by_class(unreal.MoviePipelineGameOverrideSetting)
        game.cinematic_quality_settings = True
        game.texture_streaming = unreal.MoviePipelineTextureStreamingMethod.FULLY_LOAD
        game.use_lod_zero = True
        game.use_high_quality_shadows = True
    _, manifest = unreal.MoviePipelineEditorLibrary.save_queue_to_manifest_file(queue)
    log(f"render queue manifest: {manifest}")


def main():
    placements = json.load(open(os.path.join(IMPORT, "track.json"), encoding="utf-8"))
    extra = json.load(open(os.path.join(IMPORT, "scatter.json"), encoding="utf-8"))
    # A commandlet starts with an empty asset registry; the stock Interchange pipelines and glTF materials are
    # found (and duplicated) only after it has scanned the content.
    unreal.AssetRegistryHelpers.get_asset_registry().search_all_assets(True)
    log("asset registry scanned")
    open_level()
    import_track()
    actors = label_map()
    calibrate(placements, actors)
    dress_track(actors)
    materials = car_materials()
    car = actors.get("PlayerCar")
    if car is None:
        raise RuntimeError("the export has no PlayerCar")
    dress_car(car, materials)
    rig_car(car, "GLP.Car")
    v8 = import_car("v8")
    if v8 is not None:
        dress_car(v8, materials)
        rig_car(v8, "GLP.Car1")
    rig_world(actors)
    place_game(car, game_materials(materials["paint"]))
    scatter(placements, extra, actors)
    prepare_materials()
    shots = cameras(placements, extra)
    sun = light_and_look()
    level_sub.save_current_level()
    sequences = [shot(name, camera) for name, camera in shots.items()]
    try:
        sequences.append(golden_hour_shot("CockpitEvening", shots["Cockpit"], sun))
    except Exception as error:  # a bonus picture; the like-for-like ones render without it
        log(f"evening shot skipped: {error}")
    # The scene import leaves its meshes, materials and textures (/Game/Track) as unsaved packages; saving only the
    # map would leave the level pointing at assets that do not exist on disk.
    if not unreal.EditorLoadingAndSavingUtils.save_dirty_packages(True, True):
        raise RuntimeError("saving the imported assets failed")
    log("level and assets saved")
    render_queue(sequences)
    log("done")


main()

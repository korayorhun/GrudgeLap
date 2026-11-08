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
import re
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
SUN_DIRECTION = (-0.431, 0.682, -0.591)   # WorldBuilder.SunDirection: the sun of the clear-sky HDRI the Godot version shows

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


def import_texture(folder, stem, kind, path=None):
    """Imports one Poly Haven map (diff, nor_gl or arm) once and sets it up for its use; path imports another file
    (the game's own sheets) under the same folder."""
    dest = f"{GAME}/Textures/{folder}"
    if not assets_in(dest, "Texture2D", stem):
        task = unreal.AssetImportTask()
        task.filename = path or os.path.join(POLYHAVEN, "textures", folder, f"{stem}.jpg")
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


WEATHER = {"collection": None, "puddles": None}   # MPC_Weather and the puddle mask (weather_assets), read by the ground materials


GROUND_HUES_HLSL = '''struct FGlpNoise
{
    float Hash(float2 p)
    {
        float3 p3 = frac(float3(p.xyx) * 0.1031);
        p3 += dot(p3, p3.yzx + 33.33);
        return frac((p3.x + p3.y) * p3.z);
    }
    float Value(float2 p)
    {
        float2 i = floor(p);
        float2 f = frac(p);
        float2 u = f * f * (3.0 - 2.0 * f);
        return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
    }
};
FGlpNoise N;
float2 wp = WorldXY / 100.0;
float dry = smoothstep(0.45, 0.75, N.Value(wp / 140.0) * 0.7 + N.Value(wp / 47.0 + 3.1) * 0.3);
float green = smoothstep(0.55, 0.80, N.Value(wp / 6.0 + 11.7));
// The half-metre clumps fade out where a pixel spans several of them, against shimmer at a distance.
float footprint = max(length(ddx(wp)), length(ddy(wp)));
float clump = lerp(N.Value(wp * 2.0 + 5.3), 0.5, saturate(footprint * 1.5));
float3 m = lerp(float3(1.0, 1.0, 1.0), float3(1.55, 1.12, 0.62), dry);
m = lerp(m, float3(0.82, 1.06, 0.85), green * (1.0 - dry * 0.7));
return m * lerp(0.88, 1.10, clump);'''


def surface_material(name, folder, u_tiling, v_tiling, tint=(1.0, 1.0, 1.0), macro=0.0, vertex_colour=False, wear=None, anti_tiling=False, wet=None, hues=False):
    """Base colour, normal and roughness/AO from one Poly Haven set, tiled on the mesh UVs.

    macro > 0 multiplies in the same texture at macro times the tile size as a slow brightness wobble, so a large
    ground does not show its repeat. vertex_colour multiplies the mesh's vertex colour in (the road's slabs,
    wheel tracks, patches and potholes, the shoulders' tones, as the Godot version shades them); wear is a sheet
    multiplied in on the second UV set (the road's tar snakes and stains). anti_tiling (0.13) samples the same scan a
    second time, a quarter turn round and 0.71 times the size, and blends it in where a slow noise says so, which
    breaks the repeat of the tile along a long road. wet (0.13) is {"dark": d, "rough": r} or {"dark": d, "puddles":
    True}: under MPC_Weather's Wetness the surface darkens by d and its roughness drops to r, or, with puddles, to a
    wet sheen with mirror-flat standing water where the world-aligned puddle mask is high.
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

    def sample(texture, sampler, uv, x, y, uv_out=""):
        node = expression(unreal.MaterialExpressionTextureSample, x, y)
        node.set_editor_property("texture", texture)
        node.set_editor_property("sampler_type", sampler)
        mel.connect_material_expressions(uv, uv_out, node, "UVs")
        return node

    def coords(u, v, x, y):
        node = expression(unreal.MaterialExpressionTextureCoordinate, x, y)
        node.set_editor_property("u_tiling", u)
        node.set_editor_property("v_tiling", v)
        return node

    def constant(value, x, y):
        node = expression(unreal.MaterialExpressionConstant, x, y)
        node.set_editor_property("r", value)
        return node

    def lerp(a, b, alpha, x, y):
        """a, b, alpha: (node, output) pairs."""
        node = expression(unreal.MaterialExpressionLinearInterpolate, x, y)
        mel.connect_material_expressions(a[0], a[1], node, "A")
        mel.connect_material_expressions(b[0], b[1], node, "B")
        mel.connect_material_expressions(alpha[0], alpha[1], node, "Alpha")
        return node

    def channel(source, name, x, y):
        node = expression(unreal.MaterialExpressionComponentMask, x, y)
        for c in "rgba":
            node.set_editor_property(c, c in name)
        mel.connect_material_expressions(source[0], source[1], node, "")
        return node

    def threshold(source, low, gain, x, y):
        """saturate((source - low) * gain): a soft step of a noise channel."""
        sub = expression(unreal.MaterialExpressionSubtract, x, y)
        sub.set_editor_property("const_b", low)
        mel.connect_material_expressions(source[0], source[1], sub, "A")
        mul = expression(unreal.MaterialExpressionMultiply, x + 150, y)
        mul.set_editor_property("const_b", gain)
        mel.connect_material_expressions(sub, "", mul, "A")
        sat = expression(unreal.MaterialExpressionSaturate, x + 300, y)
        mel.connect_material_expressions(mul, "", sat, "")
        return sat

    uv = coords(u_tiling, v_tiling, -1400, 0)
    base = sample(diff, unreal.MaterialSamplerType.SAMPLERTYPE_COLOR, uv, -900, -400)
    normal = sample(nor, unreal.MaterialSamplerType.SAMPLERTYPE_NORMAL, uv, -900, 0)
    masks = sample(arm, unreal.MaterialSamplerType.SAMPLERTYPE_MASKS, uv, -900, 400)
    base_rgb, normal_rgb, masks_rgb = (base, "RGB"), (normal, "RGB"), (masks, "RGB")
    if anti_tiling:
        # The quarter turn: the second coordinates' u and v swapped (mask each, append the other way round).
        uv_b = coords(v_tiling * 0.71, u_tiling * 0.71, -1700, 1000)
        turned = expression(unreal.MaterialExpressionAppendVector, -1400, 1000)
        mel.connect_material_expressions(channel((uv_b, ""), "g", -1550, 950), "", turned, "A")
        mel.connect_material_expressions(channel((uv_b, ""), "r", -1550, 1050), "", turned, "B")
        base_b = sample(diff, unreal.MaterialSamplerType.SAMPLERTYPE_COLOR, turned, -900, 800)
        normal_b = sample(nor, unreal.MaterialSamplerType.SAMPLERTYPE_NORMAL, turned, -900, 1100)
        masks_b = sample(arm, unreal.MaterialSamplerType.SAMPLERTYPE_MASKS, turned, -900, 1400)
        mixer = sample(diff, unreal.MaterialSamplerType.SAMPLERTYPE_COLOR, coords(u_tiling / 23.0, v_tiling / 23.0, -1400, 1700), -900, 1700)
        blend = (threshold((mixer, "R"), 0.44, 5.0, -600, 1700), "")
        base_rgb = (lerp(base_rgb, (base_b, "RGB"), blend, -200, 800), "")
        normal_rgb = (lerp(normal_rgb, (normal_b, "RGB"), blend, -200, 1100), "")
        masks_rgb = (lerp(masks_rgb, (masks_b, "RGB"), blend, -200, 1400), "")

    colour = expression(unreal.MaterialExpressionMultiply, -500, -400)
    mel.connect_material_expressions(base_rgb[0], base_rgb[1], colour, "A")
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
    final = colour
    if hues:
        # The ground's colour on three scales (0.76), as the Godot ground's (0.36, ISTERLER P14): dry, straw-coloured
        # stretches of a hundred metres and more, greener hollows a few metres across, half-metre clumps. One tint over
        # the whole plain read as a lawn.
        varied = expression(unreal.MaterialExpressionCustom, -400, -1200)
        varied.set_editor_property("code", GROUND_HUES_HLSL)
        varied.set_editor_property("output_type", unreal.CustomMaterialOutputType.CMOT_FLOAT3)
        varied.set_editor_property("description", "GrudgeLap ground hues")
        entry = unreal.CustomInput()
        entry.set_editor_property("input_name", "WorldXY")
        varied.set_editor_property("inputs", [entry])
        world = expression(unreal.MaterialExpressionWorldPosition, -800, -1200)
        xy = expression(unreal.MaterialExpressionComponentMask, -600, -1200)
        for c in "rgba":
            xy.set_editor_property(c, c in "rg")
        mel.connect_material_expressions(world, "", xy, "")
        mel.connect_material_expressions(xy, "", varied, "WorldXY")
        hued = expression(unreal.MaterialExpressionMultiply, -300, -1000)
        mel.connect_material_expressions(final, "", hued, "A")
        mel.connect_material_expressions(varied, "", hued, "B")
        final = hued
    if vertex_colour:
        painted = expression(unreal.MaterialExpressionMultiply, -300, -400)
        mel.connect_material_expressions(final, "", painted, "A")
        mel.connect_material_expressions(expression(unreal.MaterialExpressionVertexColor, -500, -200), "", painted, "B")
        final = painted
    if wear is not None:
        uv2 = expression(unreal.MaterialExpressionTextureCoordinate, -1400, -1000)
        uv2.set_editor_property("coordinate_index", 1)
        worn = expression(unreal.MaterialExpressionMultiply, -100, -400)
        mel.connect_material_expressions(final, "", worn, "A")
        mel.connect_material_expressions(sample(wear, unreal.MaterialSamplerType.SAMPLERTYPE_COLOR, uv2, -900, -1000), "RGB", worn, "B")
        final = worn
    roughness = (channel(masks_rgb, "g", 200, 400), "")
    occlusion = (channel(masks_rgb, "r", 200, 550), "")
    normal_out = normal_rgb
    if wet is not None and WEATHER["collection"] is not None:
        wetness_node = expression(unreal.MaterialExpressionCollectionParameter, 200, 1000)
        wetness_node.set_editor_property("collection", WEATHER["collection"])
        wetness_node.set_editor_property("parameter_name", "Wetness")
        wetness = (wetness_node, "")
        # Darker when wet: the water fills the pores and the light goes in and does not come back.
        darken = expression(unreal.MaterialExpressionMultiply, 400, 1000)
        darken.set_editor_property("const_b", wet.get("dark", 0.4))
        mel.connect_material_expressions(wetness_node, "", darken, "A")
        kept = expression(unreal.MaterialExpressionOneMinus, 550, 1000)
        mel.connect_material_expressions(darken, "", kept, "")
        darkened = expression(unreal.MaterialExpressionMultiply, 700, -400)
        mel.connect_material_expressions(final, "", darkened, "A")
        mel.connect_material_expressions(kept, "", darkened, "B")
        final = darkened
        if wet.get("puddles") and WEATHER["puddles"] is not None:
            # Standing water where the world-aligned puddle mask is high (a 9 m tile), a wet sheen everywhere else;
            # the water lies flat, so the normal flattens with it.
            world = expression(unreal.MaterialExpressionWorldPosition, 200, 1300)
            plane = channel((world, ""), "rg", 400, 1300)
            puddle_uv = expression(unreal.MaterialExpressionDivide, 550, 1300)
            puddle_uv.set_editor_property("const_b", 900.0)
            mel.connect_material_expressions(plane, "", puddle_uv, "A")
            # The mask is imported as TC_MASKS: a sampler of any other type fails the whole material's compile (the road drew
            # as the default grid until this matched).
            puddle_noise = sample(WEATHER["puddles"], unreal.MaterialSamplerType.SAMPLERTYPE_MASKS, puddle_uv, 700, 1300)
            puddle = (threshold((puddle_noise, "R"), 0.56, 6.0, 900, 1300), "")
            wet_rough = lerp((constant(0.22, 1000, 1500), ""), (constant(0.04, 1000, 1600), ""), puddle, 1200, 1500)
            roughness = (lerp(roughness, (wet_rough, ""), wetness, 1400, 400), "")
            standing = expression(unreal.MaterialExpressionMultiply, 1200, 1700)
            mel.connect_material_expressions(wetness_node, "", standing, "A")
            mel.connect_material_expressions(puddle[0], puddle[1], standing, "B")
            flat = expression(unreal.MaterialExpressionConstant3Vector, 1200, 1850)
            flat.set_editor_property("constant", unreal.LinearColor(0.0, 0.0, 1.0, 1.0))
            normal_out = (lerp(normal_rgb, (flat, ""), (standing, ""), 1400, 0), "")
        else:
            roughness = (lerp(roughness, (constant(wet.get("rough", 0.3), 1000, 500), ""), wetness, 1400, 400), "")
    mel.connect_material_property(final, "", unreal.MaterialProperty.MP_BASE_COLOR)
    mel.connect_material_property(normal_out[0], normal_out[1], unreal.MaterialProperty.MP_NORMAL)
    mel.connect_material_property(roughness[0], roughness[1], unreal.MaterialProperty.MP_ROUGHNESS)
    mel.connect_material_property(occlusion[0], occlusion[1], unreal.MaterialProperty.MP_AMBIENT_OCCLUSION)
    mel.recompile_material(material)
    eal.save_loaded_asset(material)
    return material


def weather_assets():
    """MPC_Weather (Wetness and Rain, set by the game from the simulation's weather, AGlpGame::ApplyWeather), the
    puddle mask the wet ground reads, and M_Rain, the rain's streaks (AGlpGame::FallRain): as the Godot version's
    rain.gdshader, a faint lit streak (alpha 0.3, sRGB 0.78, 0.82, 0.90) gone within 1.2 m of the eye and whole from
    4.5 m; until 0.76 an unlit sheet panning down a cylinder round the camera. Made before the ground materials, which
    read the collection."""
    path = f"{GAME}/Materials/MPC_Weather"
    if eal.does_asset_exist(path):
        eal.delete_asset(path)
    collection = asset_tools.create_asset("MPC_Weather", f"{GAME}/Materials", unreal.MaterialParameterCollection, unreal.MaterialParameterCollectionFactoryNew())
    parameters = []
    for name in ("Wetness", "Rain"):
        parameter = unreal.CollectionScalarParameter()
        parameter.set_editor_property("parameter_name", name)
        parameter.set_editor_property("default_value", 0.0)
        parameters.append(parameter)
    collection.set_editor_property("scalar_parameters", parameters)
    eal.save_loaded_asset(collection)
    WEATHER["collection"] = collection
    decals = os.path.join(IMPORT, "Decals")
    WEATHER["puddles"] = import_texture("Decals", "puddles", "mask", path=os.path.join(decals, "puddles.png"))

    rain_path = f"{GAME}/Materials/M_Rain"
    if eal.does_asset_exist(rain_path):
        eal.delete_asset(rain_path)
    rain = asset_tools.create_asset("M_Rain", f"{GAME}/Materials", unreal.Material, unreal.MaterialFactoryNew())
    rain.set_editor_property("blend_mode", unreal.BlendMode.BLEND_TRANSLUCENT)
    rain.set_editor_property("shading_model", unreal.MaterialShadingModel.MSM_DEFAULT_LIT)
    rain.set_editor_property("translucency_lighting_mode", unreal.TranslucencyLightingMode.TLM_VOLUMETRIC_NON_DIRECTIONAL)
    rain.set_editor_property("two_sided", True)
    colour = mel.create_material_expression(rain, unreal.MaterialExpressionConstant3Vector, -500, -100)
    colour.set_editor_property("constant", unreal.LinearColor(0.571, 0.638, 0.787, 1.0))
    mel.connect_material_property(colour, "", unreal.MaterialProperty.MP_BASE_COLOR)
    where = mel.create_material_expression(rain, unreal.MaterialExpressionWorldPosition, -1000, 200)
    eye = mel.create_material_expression(rain, unreal.MaterialExpressionCameraPositionWS, -1000, 300)
    reach = mel.create_material_expression(rain, unreal.MaterialExpressionDistance, -750, 250)
    mel.connect_material_expressions(where, "", reach, "A")
    mel.connect_material_expressions(eye, "", reach, "B")
    fade = mel.create_material_expression(rain, unreal.MaterialExpressionCustom, -500, 200)
    fade.set_editor_property("output_type", unreal.CustomMaterialOutputType.CMOT_FLOAT1)
    fade.set_editor_property("description", "GrudgeLap rain streak")
    fade.set_editor_property("code", "return 0.30 * smoothstep(120.0, 450.0, Reach);")
    entry = unreal.CustomInput()
    entry.set_editor_property("input_name", "Reach")
    fade.set_editor_property("inputs", [entry])
    mel.connect_material_expressions(reach, "", fade, "Reach")
    mel.connect_material_property(fade, "", unreal.MaterialProperty.MP_OPACITY)
    rough = mel.create_material_expression(rain, unreal.MaterialExpressionConstant, -500, 400)
    rough.set_editor_property("r", 1.0)
    mel.connect_material_property(rough, "", unreal.MaterialProperty.MP_ROUGHNESS)
    mel.recompile_material(rain)
    eal.save_loaded_asset(rain)
    log("weather assets made: MPC_Weather, the puddle mask, M_Rain")


def decal_material(name, texture, roughness, opacity):
    """A deferred decal of one wear sheet (make_decals.py): its colour laid over the road, its alpha the coverage."""
    path = f"{GAME}/Materials/{name}"
    if eal.does_asset_exist(path):
        eal.delete_asset(path)
    material = asset_tools.create_asset(name, f"{GAME}/Materials", unreal.Material, unreal.MaterialFactoryNew())
    material.set_editor_property("material_domain", unreal.MaterialDomain.MD_DEFERRED_DECAL)
    material.set_editor_property("blend_mode", unreal.BlendMode.BLEND_TRANSLUCENT)
    sample = mel.create_material_expression(material, unreal.MaterialExpressionTextureSample, -500, 0)
    sample.set_editor_property("texture", texture)
    sample.set_editor_property("sampler_type", unreal.MaterialSamplerType.SAMPLERTYPE_COLOR)
    mel.connect_material_property(sample, "RGB", unreal.MaterialProperty.MP_BASE_COLOR)
    coverage = mel.create_material_expression(material, unreal.MaterialExpressionMultiply, -250, 200)
    coverage.set_editor_property("const_b", opacity)
    mel.connect_material_expressions(sample, "A", coverage, "A")
    mel.connect_material_property(coverage, "", unreal.MaterialProperty.MP_OPACITY)
    rough = mel.create_material_expression(material, unreal.MaterialExpressionConstant, -250, 350)
    rough.set_editor_property("r", roughness)
    mel.connect_material_property(rough, "", unreal.MaterialProperty.MP_ROUGHNESS)
    mel.recompile_material(material)
    eal.save_loaded_asset(material)
    return material


# Each kind of wear: its sheets, the chance per 8 m of road, the length and width ranges (metres), where it lies
# (anywhere across the road, in a wheel track, along a lane), its roughness and its coverage.
DECALS = {
    "patch": (("patch_1", "patch_2"), 0.07, (2.5, 5.5), (2.0, 4.5), "any", 0.85, 0.9),
    "crack": (("crack_1", "crack_2", "crack_3"), 0.24, (3.0, 6.5), (2.0, 4.0), "any", 0.72, 0.95),
    "oil": (("oil_1", "oil_2"), 0.10, (1.2, 2.4), (0.8, 1.6), "track", 0.35, 0.8),
    "skid": (("skid",), 0.05, (6.0, 11.0), (1.9, 2.3), "lane", 0.8, 0.75),
    "pothole": (("pothole",), 0.025, (0.8, 1.5), (0.8, 1.5), "track", 0.95, 1.0),
}


def road_decals(placements):
    """The wear of a used road, scattered along the exported centrelines (0.13): cracks, patches of other asphalt, oil in
    the wheel tracks, skid marks and the odd pothole, as deferred decals that project down onto the asphalt. None on
    the bridges, the ramps and the loop, which are structures, not earth roads."""
    roads = placements.get("roads", [])
    if not roads:
        log("no road centrelines in the export: no decals (export the world again with --export-world)")
        return
    materials = {}
    for kind, (stems, _, _, _, _, roughness, opacity) in DECALS.items():
        materials[kind] = [decal_material(f"M_Decal_{stem}", import_texture("Decals", stem, "colour", path=os.path.join(IMPORT, "Decals", f"{stem}.png")), roughness, opacity) for stem in stems]
    scatter_rng = random.Random(1989)
    count = 0
    for frame in roads:
        if frame["kind"] not in ("Straight", "Curve", "Hill", "Dip", "Spline", "BankedCurve"):
            continue
        half_width = frame["halfWidth"]
        tangent = frame["tangent"]
        # Right of the road in Godot axes: the tangent turned about +Y.
        right = [-tangent[2], 0.0, tangent[0]]
        length_right = math.sqrt(right[0] ** 2 + right[2] ** 2) or 1.0
        right = [right[0] / length_right, 0.0, right[2] / length_right]
        heading = direction(tangent)
        yaw = math.degrees(math.atan2(heading.y, heading.x))
        for kind, (stems, chance, length, width, where, _, _) in DECALS.items():
            if scatter_rng.random() > chance:
                continue
            if where == "any":
                lateral = scatter_rng.uniform(-half_width + 1.0, half_width - 1.0)
            elif where == "track":
                lateral = scatter_rng.choice((-1.0, 1.0)) * scatter_rng.uniform(0.25 * half_width, 0.75 * half_width) + scatter_rng.uniform(-0.3, 0.3)
            else:
                lateral = scatter_rng.choice((-1.0, 1.0)) * 0.5 * half_width + scatter_rng.uniform(-0.6, 0.6)
            spot = [frame["position"][i] + right[i] * lateral for i in range(3)]
            location = to_unreal(spot)
            location.z += 20.0
            size_along = scatter_rng.uniform(*length)
            size_across = scatter_rng.uniform(*width)
            turn = yaw + scatter_rng.uniform(-4.0, 4.0) if kind in ("skid", "crack", "oil") else yaw + scatter_rng.choice((0.0, 90.0)) + scatter_rng.uniform(-6.0, 6.0)
            actor = actor_sub.spawn_actor_from_class(unreal.DecalActor, location, unreal.Rotator(roll=0.0, pitch=-90.0, yaw=turn))
            decal = actor.get_component_by_class(unreal.DecalComponent)
            decal.set_decal_material(scatter_rng.choice(materials[kind]))
            # Half extents, centimetres: X the depth of the projection, Y across the road, Z along it.
            decal.set_editor_property("decal_size", unreal.Vector(80.0, size_across * 50.0, size_along * 50.0))
            decal.set_editor_property("sort_order", list(DECALS).index(kind))
            actor.set_actor_label(f"Decal-{kind}-{count}")
            actor.set_folder_path("GrudgeLap/Decals")
            count += 1
    log(f"{count} road decals scattered along {len(roads)} centreline frames")


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
    only by number; a prepared model's mirror (prepare_model_car.py) names its glass MirrorGlass."""
    parts = car_parts(car)
    if "MirrorGlass" in parts:
        return parts["MirrorGlass"]
    mirror = parts.get("RearMirror")
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
        # A label the level already had gets a suffix (Wheel0_<hash>): match the name with or without it.
        wheel = next((a for a in parts if a.get_actor_label() == f"Wheel{index}" or a.get_actor_label().startswith(f"Wheel{index}_")), None)
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
    if steering is None:
        # A prepared model's steering wheel (prepare_model_car.py): CarView turns Steering/Spin about its local y.
        steering = next((a for a in parts if a.get_actor_label().startswith("Spin") and a.get_attach_parent_actor() is not None
                         and a.get_attach_parent_actor().get_actor_label().startswith("Steering")), None)
    if steering is not None:
        tag(steering, "GLP.Steering")
    # A prepared model's driver's eye (0.74): the game's cockpit camera starts there instead of its table of eyes.
    eye = next((a for a in parts if a.get_actor_label() == "Eye"), None)
    if eye is not None:
        tag(eye, "GLP.Eye")
    # ModelShell (0.13): a real car model's body in place of the lofted one (prepare_car_model.py), kept in the
    # driver's view too (its roof and pillars frame the windscreen). ModelBody (0.74): a prepared model's body with its
    # own cabin (prepare_model_car.py), which the seat sees as the car's inside.
    exterior = [a for a in parts if a.get_actor_label() in ("Shell", "Bonnet", "Glass", "Outside", "ModelShell", "ModelBody")]
    for actor in exterior:
        tag(actor, "GLP.Exterior")
        # CarView._outsideOnly: from the driver's seat only their shadows are drawn (the wheels are found by tag); a
        # model's body too, so the seat keeps the game's own cabin round it (its inner faces showed through, 0.13).
        if actor.get_actor_label() in ("Shell", "Outside", "ModelShell"):
            tag(actor, "GLP.OutsideOnly")
    dials = next((a for a in parts if a.get_actor_label() == "Dials" or a.get_actor_label().startswith("Dials_")), None)
    if dials is not None:
        tag(dials, "GLP.Dials")
    glass = mirror_glass(car)
    if glass is not None:
        tag(glass, "GLP.MirrorGlass")
    # The outside mirrors' glass (CarView: on the A-pillar of the early car, on the doors of the later one) is
    # named only by number; its polished-metal material gives it away. From the seat it shows the mirror's view.
    side = 0
    for actor in parts:
        if actor.get_actor_label().startswith("Mdl_"):
            continue   # a real car model's chrome is not a mirror (0.13)
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
    log(f"{root_tag} tagged: {wheels} wheels, steering {'found' if steering else 'missing'}, eye {'found' if eye else 'table'}, {len(exterior)} exterior "
        f"parts, dials {'found' if dials else 'missing'}, mirror glass {glass.get_actor_label() if glass else 'missing'}, "
        f"{side} side mirror(s)")


def rig_world(actors):
    """Tags the train's cars (GLP.Train0..n: the locomotive, then Wagon1, Wagon2... as TrainView names them) and the
    signs and crossbucks the car can knock over (GLP.Prop), and makes them movable."""
    train = actors.get("Train")
    cars = 0
    if train is not None:
        movable(train)
        labels = {a.get_actor_label(): a for a in descendants(train)}
        # Every car the game exported, however many the train has (six since 0.8): a car left untagged is never
        # hidden or moved and stood where the export left it, across the road in front of the start gantry.
        index = 0
        while True:
            wagon = labels.get("Locomotive" if index == 0 else f"Wagon{index}")
            if wagon is None:
                break
            tag(wagon, f"GLP.Train{index}")
            cars += 1
            index += 1
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
    for prop, name in (("ghost_material", "M_Ghost"), ("instruments_material", "M_Instruments"), ("mirror_material", "M_MirrorView")):
        game.set_editor_property(prop, materials[name])
    game.set_editor_property("skid_material", skid_material())
    game.set_editor_property("puff_material", puff_material())
    # The rain sheet (weather_assets) by a soft reference, so the package cooks it.
    game.set_editor_property("rain_material", eal.load_asset(f"{GAME}/Materials/M_Rain"))
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


def paint_finish(material, attributes, colour):
    """The clear-coated paint's finish, and its dark lining (0.75): a back face inside the cabin (CabinMin..CabinMax, the
    car's own frame in centimetres, set per car from its eye: CarView.SetPaintCabin's box) is the lining, matt and
    near-black, as the Godot game's ModelPaint draws it. The models draw their panels from both faces and few have a
    headlining, so from the seat the roof's underside and the pillars' inner faces showed in the body's colour. Outside
    the box a back face stays paint, for the panels wound the wrong way (the Renault 12's doors)."""
    position = mel.create_material_expression(material, unreal.MaterialExpressionWorldPosition, -1100, 450)
    local = mel.create_material_expression(material, unreal.MaterialExpressionTransformPosition, -900, 450)
    local.set_editor_property("transform_source_type", unreal.MaterialPositionTransformSource.TRANSFORMPOSSOURCE_WORLD)
    local.set_editor_property("transform_type", unreal.MaterialPositionTransformSource.TRANSFORMPOSSOURCE_LOCAL)
    mel.connect_material_expressions(position, "", local, "")
    side = mel.create_material_expression(material, unreal.MaterialExpressionTwoSidedSign, -900, 550)
    low = mel.create_material_expression(material, unreal.MaterialExpressionVectorParameter, -900, 650)
    low.set_editor_property("parameter_name", "CabinMin")
    low.set_editor_property("default_value", unreal.LinearColor(1.0, 1.0, 1.0, 0.0))
    high = mel.create_material_expression(material, unreal.MaterialExpressionVectorParameter, -900, 750)
    high.set_editor_property("parameter_name", "CabinMax")
    high.set_editor_property("default_value", unreal.LinearColor(-1.0, -1.0, -1.0, 0.0))
    custom = mel.create_material_expression(material, unreal.MaterialExpressionCustom, -650, 600)
    custom.set_editor_property("output_type", unreal.CustomMaterialOutputType.CMOT_FLOAT1)
    custom.set_editor_property("description", "GrudgeLap lining")
    custom.set_editor_property("code", "return (Side < 0.0 && all(Local > Low.xyz) && all(Local < High.xyz)) ? 1.0 : 0.0;")
    inputs = []
    for name in ("Local", "Side", "Low", "High"):
        entry = unreal.CustomInput()
        entry.set_editor_property("input_name", name)
        inputs.append(entry)
    custom.set_editor_property("inputs", inputs)
    for node, pin in ((local, "Local"), (side, "Side"), (low, "Low"), (high, "High")):
        mel.connect_material_expressions(node, "", custom, pin)
    lining = mel.create_material_expression(material, unreal.MaterialExpressionConstant3Vector, -650, 300)
    lining.set_editor_property("constant", unreal.LinearColor(0.045, 0.045, 0.05, 1.0))
    mix = mel.create_material_expression(material, unreal.MaterialExpressionLinearInterpolate, -300, 0)
    mel.connect_material_expressions(colour, "", mix, "A")
    mel.connect_material_expressions(lining, "", mix, "B")
    mel.connect_material_expressions(custom, "", mix, "Alpha")
    mel.connect_material_expressions(mix, "", attributes, "BaseColor")
    for y, (pin, paint_value, lining_value) in enumerate((("Metallic", 0.35, 0.0), ("Roughness", 0.38, 0.9),
                                                         ("ClearCoat", 1.0, 0.0), ("ClearCoatRoughness", 0.04, 0.04))):
        value = mel.create_material_expression(material, unreal.MaterialExpressionLinearInterpolate, -250, 200 + y * 100)
        value.set_editor_property("const_a", paint_value)
        value.set_editor_property("const_b", lining_value)
        mel.connect_material_expressions(custom, "", value, "Alpha")
        mel.connect_material_expressions(value, "", attributes, pin)


def paint_solid_material():
    """The paint of the parts Godot paints with a plain material instead of the vertex colour: the strakes, the
    intakes, the mirror housings (CarView's solidPaint, (0.54, 0.002, 0.004) linear). The same clear coat as
    M_CarPaint and the same Paint parameter, so the traffic copies are painted all over in their own colour."""
    material = asset_tools.create_asset("M_CarPaintSolid", f"{GAME}/Materials", unreal.Material, unreal.MaterialFactoryNew())
    material.set_editor_property("shading_model", unreal.MaterialShadingModel.MSM_CLEAR_COAT)
    # Both faces (0.74): the prepared models' panels are not all wound one way (the Renault 12's doors on mirrored nodes,
    # the Godot game draws its paint from both sides), and a one-sided paint left holes where a panel faced inwards.
    material.set_editor_property("two_sided", True)
    material.set_editor_property("use_material_attributes", True)
    attributes = mel.create_material_expression(material, unreal.MaterialExpressionMakeMaterialAttributes, 0, 0)
    mel.connect_material_property(attributes, "", unreal.MaterialProperty.MP_MATERIAL_ATTRIBUTES)
    paint = mel.create_material_expression(material, unreal.MaterialExpressionVectorParameter, -400, 0)
    paint.set_editor_property("parameter_name", "Paint")
    paint.set_editor_property("default_value", unreal.LinearColor(0.54, 0.0023, 0.0039, 1.0))
    paint_finish(material, attributes, paint)
    mel.recompile_material(material)
    eal.save_loaded_asset(material)
    return material


def paint_textured_material():
    """The paint of a prepared model whose paint is a texture (a livery or a colour atlas, 0.74): the texture times
    the Paint parameter under the same clear coat as M_CarPaintSolid, so the textured cars shine as the flat ones do."""
    material = asset_tools.create_asset("M_CarPaintTextured", f"{GAME}/Materials", unreal.Material, unreal.MaterialFactoryNew())
    material.set_editor_property("shading_model", unreal.MaterialShadingModel.MSM_CLEAR_COAT)
    material.set_editor_property("two_sided", True)   # as M_CarPaintSolid
    material.set_editor_property("use_material_attributes", True)
    attributes = mel.create_material_expression(material, unreal.MaterialExpressionMakeMaterialAttributes, 0, 0)
    mel.connect_material_property(attributes, "", unreal.MaterialProperty.MP_MATERIAL_ATTRIBUTES)
    texture = mel.create_material_expression(material, unreal.MaterialExpressionTextureSampleParameter2D, -700, -100)
    texture.set_editor_property("parameter_name", "Texture")
    paint = mel.create_material_expression(material, unreal.MaterialExpressionVectorParameter, -700, 150)
    paint.set_editor_property("parameter_name", "Paint")
    paint.set_editor_property("default_value", unreal.LinearColor(1.0, 1.0, 1.0, 1.0))
    colour = mel.create_material_expression(material, unreal.MaterialExpressionMultiply, -400, 0)
    mel.connect_material_expressions(texture, "RGB", colour, "A")
    mel.connect_material_expressions(paint, "", colour, "B")
    paint_finish(material, attributes, colour)
    mel.recompile_material(material)
    eal.save_loaded_asset(material)
    return material


def puff_material():
    """The wheels' dust, tyre smoke and spray (0.76, GlpGame's Puffs): a soft round puff on a plane the game turns to
    the camera, lit as translucency is (the sun's light and the sky's, in the translucency volume), in the colour and
    opacity each instance carries (its custom data: 0 the opacity, 1-3 the linear colour). As the Godot version's
    WheelDust: the puff's alpha 0.55 at its centre, none at its rim; soft where it meets the road or the ground (a
    smoothstep over 60 cm of depth behind it, as Godot's proximity fade; the quads cut into it in hard bands), clear
    within 1.5 m of the eye and whole from 3.5 m by the distance from the eye, not the depth along the view (Godot's
    pixel distance fade; from the seat a sliding rear tyre's smoke, its puffs 3 m across, filled the car)."""
    m = asset_tools.create_asset("M_Puff", f"{GAME}/Materials", unreal.Material, unreal.MaterialFactoryNew())
    m.set_editor_property("blend_mode", unreal.BlendMode.BLEND_TRANSLUCENT)
    m.set_editor_property("shading_model", unreal.MaterialShadingModel.MSM_DEFAULT_LIT)
    m.set_editor_property("translucency_lighting_mode", unreal.TranslucencyLightingMode.TLM_VOLUMETRIC_NON_DIRECTIONAL)
    m.set_editor_property("two_sided", True)
    colour = mel.create_material_expression(m, unreal.MaterialExpressionPerInstanceCustomData3Vector, -500, -150)
    colour.set_editor_property("data_index", 1)
    mel.connect_material_property(colour, "", unreal.MaterialProperty.MP_BASE_COLOR)
    alpha = mel.create_material_expression(m, unreal.MaterialExpressionPerInstanceCustomData, -800, 100)
    alpha.set_editor_property("data_index", 0)
    uv = mel.create_material_expression(m, unreal.MaterialExpressionTextureCoordinate, -800, 0)
    depth = mel.create_material_expression(m, unreal.MaterialExpressionPixelDepth, -800, 200)
    scene = mel.create_material_expression(m, unreal.MaterialExpressionSceneDepth, -800, 300)
    where = mel.create_material_expression(m, unreal.MaterialExpressionWorldPosition, -1100, 400)
    eye = mel.create_material_expression(m, unreal.MaterialExpressionCameraPositionWS, -1100, 500)
    reach = mel.create_material_expression(m, unreal.MaterialExpressionDistance, -800, 450)
    mel.connect_material_expressions(where, "", reach, "A")
    mel.connect_material_expressions(eye, "", reach, "B")
    custom = mel.create_material_expression(m, unreal.MaterialExpressionCustom, -500, 100)
    custom.set_editor_property("output_type", unreal.CustomMaterialOutputType.CMOT_FLOAT1)
    custom.set_editor_property("description", "GrudgeLap puff")
    custom.set_editor_property("code", "float rim = length(UV - 0.5) * 2.0; "
                               "float soft = smoothstep(0.0, 60.0, Scene - Depth); "
                               "return 0.55 * saturate(1.0 - rim) * Alpha * soft * smoothstep(150.0, 350.0, Reach);")
    inputs = []
    for name in ("UV", "Alpha", "Depth", "Scene", "Reach"):
        entry = unreal.CustomInput()
        entry.set_editor_property("input_name", name)
        inputs.append(entry)
    custom.set_editor_property("inputs", inputs)
    mel.connect_material_expressions(uv, "", custom, "UV")
    mel.connect_material_expressions(alpha, "", custom, "Alpha")
    mel.connect_material_expressions(depth, "", custom, "Depth")
    mel.connect_material_expressions(scene, "", custom, "Scene")
    mel.connect_material_expressions(reach, "", custom, "Reach")
    mel.connect_material_property(custom, "", unreal.MaterialProperty.MP_OPACITY)
    rough = mel.create_material_expression(m, unreal.MaterialExpressionConstant, -250, 300)
    rough.set_editor_property("r", 1.0)
    mel.connect_material_property(rough, "", unreal.MaterialProperty.MP_ROUGHNESS)
    mel.recompile_material(m)
    eal.save_loaded_asset(m)
    return m


def skid_material():
    """The tyre marks' strips (0.76): a modulating material, so a strip darkens the road beneath it whatever the light, to
    0.45 of its shade in the middle and fading to nothing at its sides (UV.y across the strip), unlit, both faces."""
    m = asset_tools.create_asset("M_SkidMark", f"{GAME}/Materials", unreal.Material, unreal.MaterialFactoryNew())
    m.set_editor_property("blend_mode", unreal.BlendMode.BLEND_MODULATE)
    m.set_editor_property("shading_model", unreal.MaterialShadingModel.MSM_UNLIT)
    m.set_editor_property("two_sided", True)
    custom = mel.create_material_expression(m, unreal.MaterialExpressionCustom, -400, 0)
    custom.set_editor_property("output_type", unreal.CustomMaterialOutputType.CMOT_FLOAT3)
    custom.set_editor_property("description", "GrudgeLap tyre mark")
    custom.set_editor_property("code", "float across = abs(UV.y - 0.5) * 2.0; float v = lerp(0.45, 1.0, smoothstep(0.55, 1.0, across)); return float3(v, v, v);")
    entry = unreal.CustomInput()
    entry.set_editor_property("input_name", "UV")
    custom.set_editor_property("inputs", [entry])
    uv = mel.create_material_expression(m, unreal.MaterialExpressionTextureCoordinate, -700, 0)
    mel.connect_material_expressions(uv, "", custom, "UV")
    mel.connect_material_property(custom, "", unreal.MaterialProperty.MP_EMISSIVE_COLOR)
    mel.recompile_material(m)
    eal.save_loaded_asset(m)
    return m


def brake_lamp_material():
    """A prepared model's brake lamp lens (GLP_Brake, 0.76): its own colour and texture (Lens, Texture), lit by the Glow
    parameter, which the game (FGlpCarView::SetBraking) raises while the car brakes. The glow is in the screen's light
    units (as the instruments'), so a lit lamp reads bright red in daylight at the level's fixed exposure."""
    m = asset_tools.create_asset("M_BrakeLamp", f"{GAME}/Materials", unreal.Material, unreal.MaterialFactoryNew())
    texture = mel.create_material_expression(m, unreal.MaterialExpressionTextureSampleParameter2D, -800, -100)
    texture.set_editor_property("parameter_name", "Texture")
    lens = mel.create_material_expression(m, unreal.MaterialExpressionVectorParameter, -800, 150)
    lens.set_editor_property("parameter_name", "Lens")
    lens.set_editor_property("default_value", unreal.LinearColor(0.6, 0.0, 0.0, 1.0))
    colour = mel.create_material_expression(m, unreal.MaterialExpressionMultiply, -550, 0)
    mel.connect_material_expressions(texture, "RGB", colour, "A")
    mel.connect_material_expressions(lens, "", colour, "B")
    mel.connect_material_property(colour, "", unreal.MaterialProperty.MP_BASE_COLOR)
    glow = mel.create_material_expression(m, unreal.MaterialExpressionScalarParameter, -800, 300)
    glow.set_editor_property("parameter_name", "Glow")
    glow.set_editor_property("default_value", 0.0)
    lit = mel.create_material_expression(m, unreal.MaterialExpressionMultiply, -550, 250)
    mel.connect_material_expressions(colour, "", lit, "A")
    mel.connect_material_expressions(glow, "", lit, "B")
    mel.connect_material_property(lit, "", unreal.MaterialProperty.MP_EMISSIVE_COLOR)
    screen_units_emissive(m)
    roughness = mel.create_material_expression(m, unreal.MaterialExpressionConstant, -300, 400)
    roughness.set_editor_property("r", 0.15)
    mel.connect_material_property(roughness, "", unreal.MaterialProperty.MP_ROUGHNESS)
    mel.recompile_material(m)
    eal.save_loaded_asset(m)
    return m


def cabin_material():
    """The cabin's leather, fabric and carpet. Godot lays their photos on with triplanar mapping and the export
    leaves those meshes without texture coordinates, so the imported materials showed one flat colour. This
    projects the photo (Texture, Normal) along the three axes of the part's own space, blended by its normal, so
    the grain stays put as the car moves; Scale is in repeats per metre (Godot's uv1_scale), Tint multiplies the
    colour (the export's base colour factor). The normal is assembled in local space and handed over in world
    space, as the meshes have no tangents either."""
    m = asset_tools.create_asset("M_CabinTriplanar", f"{GAME}/Materials", unreal.Material, unreal.MaterialFactoryNew())
    m.set_editor_property("tangent_space_normal", False)
    row = [0]

    def node(cls, **props):
        row[0] += 1
        n = mel.create_material_expression(m, cls, -1800 + 120 * (row[0] % 12), -600 + 90 * (row[0] // 12))
        for key, value in props.items():
            n.set_editor_property(key, value)
        return n

    def link(a, b, pin="", out=""):
        mel.connect_material_expressions(a, out, b, pin)

    def mask(source, channels):
        n = node(unreal.MaterialExpressionComponentMask, **{c: c in channels for c in "rgba"})
        link(source, n)
        return n

    def binary(cls, a, b):
        n = node(cls)
        link(a, n, "A")
        link(b, n, "B")
        return n

    def append(*parts):
        result = parts[0]
        for part in parts[1:]:
            result = binary(unreal.MaterialExpressionAppendVector, result, part)
        return result

    scale = node(unreal.MaterialExpressionScalarParameter, parameter_name="Scale", default_value=4.0)
    per_cm = node(unreal.MaterialExpressionMultiply, const_b=0.01)
    link(scale, per_cm, "A")
    position = binary(unreal.MaterialExpressionMultiply, node(unreal.MaterialExpressionLocalPosition), per_cm)
    uvs = (mask(position, "gb"), mask(position, "rb"), mask(position, "rg"))   # projected along x, y, z

    local_normal = node(unreal.MaterialExpressionTransform,
                        transform_source_type=unreal.MaterialVectorCoordTransformSource.TRANSFORMSOURCE_WORLD,
                        transform_type=unreal.MaterialVectorCoordTransform.TRANSFORM_LOCAL)
    link(node(unreal.MaterialExpressionVertexNormalWS), local_normal)
    sharp = node(unreal.MaterialExpressionPower, const_exponent=4.0)
    link(binary(unreal.MaterialExpressionMultiply, local_normal, local_normal), sharp, "Base")
    total = binary(unreal.MaterialExpressionDotProduct, sharp, node(unreal.MaterialExpressionConstant3Vector, constant=unreal.LinearColor(1, 1, 1, 1)))
    weights = binary(unreal.MaterialExpressionDivide, sharp, total)
    w = (mask(weights, "r"), mask(weights, "g"), mask(weights, "b"))

    def blend(samples):
        return binary(unreal.MaterialExpressionAdd, binary(unreal.MaterialExpressionAdd,
                      binary(unreal.MaterialExpressionMultiply, samples[0], w[0]),
                      binary(unreal.MaterialExpressionMultiply, samples[1], w[1])),
                      binary(unreal.MaterialExpressionMultiply, samples[2], w[2]))

    def sample(parameter, default, sampler, uv):
        n = node(unreal.MaterialExpressionTextureSampleParameter2D, parameter_name=parameter,
                 texture=eal.load_asset(default), sampler_type=sampler)
        link(uv, n, "UVs")
        return n

    colour = blend([mask(sample("Texture", "/Engine/EngineResources/DefaultTexture", unreal.MaterialSamplerType.SAMPLERTYPE_COLOR, uv), "rgb") for uv in uvs])
    tint = node(unreal.MaterialExpressionVectorParameter, parameter_name="Tint", default_value=unreal.LinearColor(1, 1, 1, 1))
    mel.connect_material_property(binary(unreal.MaterialExpressionMultiply, colour, tint), "", unreal.MaterialProperty.MP_BASE_COLOR)

    # Each projection's tangent-space normal turned onto its axis (the photo's z along the axis, on the side the
    # surface faces), softened to Godot's normal scale of 0.6, blended like the colour.
    side = node(unreal.MaterialExpressionSign)
    link(local_normal, side)
    soft = node(unreal.MaterialExpressionConstant3Vector, constant=unreal.LinearColor(0.6, 0.6, 1.0, 1.0))
    normals = [binary(unreal.MaterialExpressionMultiply, mask(sample("Normal", "/Engine/EngineMaterials/DefaultNormal", unreal.MaterialSamplerType.SAMPLERTYPE_NORMAL, uv), "rgb"), soft) for uv in uvs]
    facing = lambda i, axis: binary(unreal.MaterialExpressionMultiply, mask(normals[i], "b"), mask(side, axis))
    projected = [append(facing(0, "r"), mask(normals[0], "rg")),
                 append(mask(normals[1], "r"), facing(1, "g"), mask(normals[1], "g")),
                 append(mask(normals[2], "rg"), facing(2, "b"))]
    local = node(unreal.MaterialExpressionNormalize)
    link(blend(projected), local)
    world = node(unreal.MaterialExpressionTransform,
                 transform_source_type=unreal.MaterialVectorCoordTransformSource.TRANSFORMSOURCE_LOCAL,
                 transform_type=unreal.MaterialVectorCoordTransform.TRANSFORM_WORLD)
    link(local, world)
    unit = node(unreal.MaterialExpressionNormalize)
    link(world, unit)
    mel.connect_material_property(unit, "", unreal.MaterialProperty.MP_NORMAL)

    roughness = node(unreal.MaterialExpressionScalarParameter, parameter_name="Roughness", default_value=0.9)
    mel.connect_material_property(roughness, "", unreal.MaterialProperty.MP_ROUGHNESS)
    mel.recompile_material(m)
    eal.save_loaded_asset(m)
    return m


def cabin_instance(source, parent, cache):
    """The cabin material for an imported leather, fabric or carpet instance, or None for anything else. The
    photo decides the repeat (Godot: leather 4 per metre, the matte dashboard leather 5, fabric and carpet 3)."""
    if source is None:
        return None
    if source.get_name() in cache:
        return cache[source.get_name()]
    try:
        texture = mel.get_material_instance_texture_parameter_value(source, "BaseColorTexture")
        normal = mel.get_material_instance_texture_parameter_value(source, "NormalTexture")
        tint = mel.get_material_instance_vector_parameter_value(source, "BaseColorFactor")
        roughness = mel.get_material_instance_scalar_parameter_value(source, "RoughnessFactor")
        grain = mel.get_material_instance_texture_parameter_value(source, "MetallicRoughnessTexture")
    except Exception:
        return None
    photo = texture.get_name() if texture else ""
    if not photo.startswith(("leather_color", "fabric_color", "carpet_color")):
        return None
    matte = roughness < 0.95
    scale = 5.0 if photo.startswith("leather") and matte else 4.0 if photo.startswith("leather") else 3.0
    # Leather with its roughness photo has a soft sheen in Godot; the rest keeps the export's roughness.
    if grain is not None and grain.get_name().startswith("leather_roughness"):
        roughness = 0.6
    instance = asset_tools.create_asset(f"MI_Cabin_{source.get_name()}", f"{GAME}/Materials", unreal.MaterialInstanceConstant,
                                        unreal.MaterialInstanceConstantFactoryNew())
    mel.set_material_instance_parent(instance, parent)
    mel.set_material_instance_texture_parameter_value(instance, "Texture", texture)
    if normal is not None:
        mel.set_material_instance_texture_parameter_value(instance, "Normal", normal)
    mel.set_material_instance_vector_parameter_value(instance, "Tint", tint)
    mel.set_material_instance_scalar_parameter_value(instance, "Scale", scale)
    mel.set_material_instance_scalar_parameter_value(instance, "Roughness", roughness)
    mel.update_material_instance(instance)
    eal.save_loaded_asset(instance)
    cache[source.get_name()] = instance
    return instance


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

    # Thin translucent glass: light passes through tinted (a windscreen lets about 80 % through) and the surface
    # reflects at its full Fresnel strength. As a plain translucent surface at 14 % opacity its reflections were
    # scaled down with it, and the windscreen never showed the sky or the dashboard the way a real one does.
    def glassy(material):
        material.set_editor_property("blend_mode", unreal.BlendMode.BLEND_TRANSLUCENT)
        material.set_editor_property("shading_model", unreal.MaterialShadingModel.MSM_THIN_TRANSLUCENT)
        material.set_editor_property("translucency_lighting_mode", unreal.TranslucencyLightingMode.TLM_SURFACE_PER_PIXEL_LIGHTING)
        material.set_editor_property("two_sided", True)
        transmittance = mel.create_material_expression(material, unreal.MaterialExpressionConstant3Vector, -500, 600)
        transmittance.set_editor_property("constant", unreal.LinearColor(0.78, 0.83, 0.84, 1.0))
        output = mel.create_material_expression(material, unreal.MaterialExpressionThinTranslucentMaterialOutput, -200, 600)
        mel.connect_material_expressions(transmittance, "", output, "")

    glass = constant_material("M_Glass", {mp.MP_BASE_COLOR: (0.0, 0.0, 0.0), mp.MP_OPACITY: 0.03,
                                          mp.MP_ROUGHNESS: 0.03, mp.MP_SPECULAR: 0.5, mp.MP_METALLIC: 0.0}, glassy)
    return {"paint": paint_material(), "paint_solid": paint_solid_material(), "paint_textured": paint_textured_material(),
            "brake": brake_lamp_material(),
            "glass": glass, "mirror": mirror_material(),
            "cabin": cabin_material(), "cabin_instances": {}}



PAINT_WORDS = {"paint", "pintura", "carrosserie", "lack", "carpaint", "bodypaint"}
BODY_WORDS = {"m", "mat", "material", "body", "color", "colour", "paint"}


def paint_material_name(name):
    """A model material that is the body paint, by whole words of its name (as tools/prepare_car_models.py): "lack"
    inside "black" had every black part of a model painted, and "Painted Black" is trim; a name that is only "body"
    with "m", "mat" or "color" around it (M_Body, mat_body, Body_MAT, Body Color) is the paint too."""
    tokens = {w for w in re.split(r"[^a-z]+", name.lower()) if w}
    return bool(tokens & PAINT_WORDS) or ("body" in tokens and tokens <= BODY_WORDS)

def without_nanite(mesh):
    """Translucency cannot be drawn by Nanite: a mesh with a glass slot is built without it."""
    settings = mesh.get_editor_property("nanite_settings")
    if settings.enabled:
        settings.enabled = False
        mesh.modify()
        mesh.set_editor_property("nanite_settings", settings)
        eal.save_loaded_asset(mesh, False)


def translucent(material):
    base = material.get_base_material() if material else None
    return base is not None and base.get_editor_property("blend_mode") not in (unreal.BlendMode.BLEND_OPAQUE, unreal.BlendMode.BLEND_MASKED)


def dress_prepared_model(car, parts, materials):
    """A prepared model's materials (prepare_model_car.py, the game's own model): GLP_Paint becomes the level's
    clear-coated paint in the model's own colour, through the Paint parameter, so the traffic copies can wear
    theirs (a paint with a texture, a livery or a colour atlas, keeps its own material); GLP_Glass and GLP_Lens
    the clear glass, as CarView.DressModel gives the game's windows and lamp lenses. A mesh with any translucent
    slot left is built without Nanite."""
    if "ModelBody" not in parts:
        return
    cabin = None
    if "Eye" in parts:
        eye = car.get_actor_transform().inverse_transform_location(parts["Eye"].get_actor_location())
        cabin = (unreal.LinearColor(-86.0, eye.y - 110.0, eye.z - 55.0, 0.0), unreal.LinearColor(86.0, eye.y + 75.0, eye.z + 60.0, 0.0))
    paints = {}
    painted = glazed = plain = 0
    for component in mesh_components(car):
        if not component.get_owner().get_actor_label().startswith("Mdl_"):
            continue
        mesh = component.get_editor_property("static_mesh")
        for slot in range(component.get_num_materials()):
            material = component.get_material(slot)
            name = material.get_name() if material else ""
            if name.startswith(("GLP_Glass", "GLP_Lens", "MI_GLP_Glass", "MI_GLP_Lens")):
                component.set_material(slot, materials["glass"])
                glazed += 1
            elif name.startswith(("GLP_Brake", "MI_GLP_Brake")) and isinstance(material, unreal.MaterialInstance):
                # The brake lamp's lens in its own colour and texture, lit by the game when braking (0.76).
                try:
                    texture = mel.get_material_instance_texture_parameter_value(material, "BaseColorTexture")
                    colour = mel.get_material_instance_vector_parameter_value(material, "BaseColorFactor")
                except Exception:
                    texture, colour = None, None
                key = ("brake", texture.get_path_name() if texture else "", round(colour.r, 4) if colour else 0, round(colour.g, 4) if colour else 0)
                if key not in paints:
                    instance = asset_tools.create_asset(f"MI_Brake_{car.get_actor_label()}_{len(paints)}", f"{GAME}/Materials",
                                                        unreal.MaterialInstanceConstant, unreal.MaterialInstanceConstantFactoryNew())
                    mel.set_material_instance_parent(instance, materials["brake"])
                    if texture is not None:
                        mel.set_material_instance_texture_parameter_value(instance, "Texture", texture)
                    if colour is not None:
                        mel.set_material_instance_vector_parameter_value(instance, "Lens", unreal.LinearColor(colour.r, colour.g, colour.b, 1.0))
                    mel.update_material_instance(instance)
                    eal.save_loaded_asset(instance)
                    paints[key] = instance
                component.set_material(slot, paints[key])
            elif name.startswith(("GLP_Paint", "MI_GLP_Paint")) and isinstance(material, unreal.MaterialInstance):
                # prepare_model_car.py names a textured paint GLP_PaintTex and a flat one GLP_PaintFlat.
                textured = name.startswith(("GLP_PaintTex", "MI_GLP_PaintTex"))
                try:
                    texture = mel.get_material_instance_texture_parameter_value(material, "BaseColorTexture") if textured else None
                    colour = mel.get_material_instance_vector_parameter_value(material, "BaseColorFactor")
                except Exception:
                    texture, colour = None, None
                if colour is None or (textured and texture is None):
                    plain += 1
                    continue
                key = (texture.get_path_name() if texture else "", round(colour.r, 4), round(colour.g, 4), round(colour.b, 4))
                if key not in paints:
                    instance = asset_tools.create_asset(f"MI_Paint_{car.get_actor_label()}_{len(paints)}", f"{GAME}/Materials",
                                                        unreal.MaterialInstanceConstant, unreal.MaterialInstanceConstantFactoryNew())
                    mel.set_material_instance_parent(instance, materials["paint_textured" if textured else "paint_solid"])
                    mel.set_material_instance_vector_parameter_value(instance, "Paint", unreal.LinearColor(colour.r, colour.g, colour.b, 1.0))
                    if textured:
                        mel.set_material_instance_texture_parameter_value(instance, "Texture", texture)
                    if cabin is not None:
                        mel.set_material_instance_vector_parameter_value(instance, "CabinMin", cabin[0])
                        mel.set_material_instance_vector_parameter_value(instance, "CabinMax", cabin[1])
                    mel.update_material_instance(instance)
                    eal.save_loaded_asset(instance)
                    paints[key] = instance
                component.set_material(slot, paints[key])
                painted += 1
        if mesh is not None and any(translucent(component.get_material(s)) for s in range(component.get_num_materials())):
            without_nanite(mesh)
    log(f"{car.get_actor_label()}: prepared model dressed, {painted} paint slot(s) in {len(paints)} paint(s), "
        f"{plain} paint slot(s) left as imported, {glazed} glass and lens slot(s)")


def dress_car(car, materials):
    """Paint and glass on one car. The game's paint is flat red in the vertex colour (with darker panel seams);
    here it is a clear-coated metallic paint over that colour (paint_material). The glass, which the cockpit view
    hid at export, becomes a clear windscreen every view keeps; translucency cannot be drawn by Nanite, so its mesh
    is built without. The rear-view mirror's glass gets dark glass for the stills (the game shows its camera)."""
    parts = car_parts(car)
    if "ModelBody" in parts:
        # A prepared model (prepare_model_car.py, 0.74) brings its own cabin and materials: only its paint and glass
        # change, and the mirror's glass takes the stills' dark glass. The lofted car's steps below would paint its
        # GLP_Paint in the default red before the model's own colour could be read.
        dress_prepared_model(car, parts, materials)
        glass = mirror_glass(car)
        for component in mesh_components(glass) if glass else []:
            component.set_material(0, materials["mirror"])
        return
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
    # A real car model's glass (Mdl_Glass_*, prepare_car_model.py): the clear glass, and no Nanite (translucent).
    model_glass = 0
    for actor in parts.values():
        if not actor.get_actor_label().startswith("Mdl_Glass_"):
            continue
        for component in mesh_components(actor):
            mesh = component.get_editor_property("static_mesh")
            settings = mesh.get_editor_property("nanite_settings")
            if settings.enabled:
                settings.enabled = False
                mesh.modify()
                mesh.set_editor_property("nanite_settings", settings)
                eal.save_loaded_asset(mesh, False)
            for slot in range(component.get_num_materials()):
                component.set_material(slot, materials["glass"])
            model_glass += 1
    # A model's paint (its materials named for it) becomes the game's clear-coated paint, so the player's car is the
    # red of the original and the traffic copies take their own colours through the Paint parameter (0.13).
    model_paint = 0
    for actor in parts.values():
        if not actor.get_actor_label().startswith("Mdl_") or actor.get_actor_label().startswith("Mdl_Glass_"):
            continue
        for component in mesh_components(actor):
            for slot in range(component.get_num_materials()):
                material = component.get_material(slot)
                name = material.get_name().lower() if material else ""
                if paint_material_name(name):
                    component.set_material(slot, materials["paint_solid"])
                    model_paint += 1
    if model_glass or model_paint:
        log(f"{car.get_actor_label()}: {model_glass} model glass part(s) glazed, {model_paint} painted")
    # The plain-painted parts (CarView's solidPaint: metallic 0.18, roughness 0.26) and the cabin's photo materials.
    solid = cabin = 0
    for component in mesh_components(car):
        if component.get_owner().get_actor_label().startswith("Mdl_"):
            continue   # a model's own materials (dress_prepared_model)
        for slot in range(component.get_num_materials()):
            material = component.get_material(slot)
            if not isinstance(material, unreal.MaterialInstance):
                continue
            try:
                metallic = mel.get_material_instance_scalar_parameter_value(material, "MetallicFactor")
                rough = mel.get_material_instance_scalar_parameter_value(material, "RoughnessFactor")
            except Exception:
                continue
            if abs(metallic - 0.18) < 0.01 and abs(rough - 0.26) < 0.01:
                component.set_material(slot, materials["paint_solid"])
                solid += 1
                continue
            instance = cabin_instance(material, materials["cabin"], materials["cabin_instances"])
            if instance is not None:
                component.set_material(slot, instance)
                cabin += 1
    # The body shell is single-sided and seen from inside in the cockpit view (the game draws it as shadows only
    # there). Lumen's surface cache does not know it is one-sided and bounced its red paint all over the cabin;
    # the shell keeps its direct shadows and leaves the bounce light to the cabin lining.
    shell = 0
    for label in ("Shell", "Bonnet", "Outside"):
        for component in mesh_components(parts[label]) if label in parts else []:
            component.set_editor_property("affect_dynamic_indirect_lighting", False)
            component.set_editor_property("affect_distance_field_lighting", False)
            shell += 1
    log(f"{car.get_actor_label()} dressed: {painted} painted, {solid} plain-painted, {cabin} cabin, {glazed} glass, "
        f"{shell} shell part(s) out of Lumen, mirror glass {'found' if glass else 'missing'}")


SHOULDER_HLSL = '''struct FGlpNoise
{
    float Hash(float2 p)
    {
        float3 p3 = frac(float3(p.xyx) * 0.1031);
        p3 += dot(p3, p3.yzx + 33.33);
        return frac((p3.x + p3.y) * p3.z);
    }
    float Value(float2 p)
    {
        float2 i = floor(p);
        float2 f = frac(p);
        float2 u = f * f * (3.0 - 2.0 * f);
        return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
    }
};
FGlpNoise N;
float across = UV.x;
float2 wp = WorldXY / 100.0;
float fray = across + 0.20 * (N.Value(wp * 2.2) - 0.5) + 0.10 * (N.Value(wp * 8.0) - 0.5);
Opacity = 1.0 - smoothstep(0.84, 0.90, fray);
float2 uv = wp / 2.6;
float l = N.Value(wp * 0.45) * 8.0;
float fl = frac(l);
float ia = floor(l);
float2 oa = sin(float2(3.0, 7.0) * ia);
float2 ob = sin(float2(3.0, 7.0) * (ia + 1.0));
float2 dx = ddx(uv);
float2 dy = ddy(uv);
float3 ca = Stones.SampleGrad(StonesSampler, uv + oa, dx, dy).rgb;
float3 cb = Stones.SampleGrad(StonesSampler, uv + ob, dx, dy).rgb;
float w = smoothstep(0.2, 0.8, fl - 0.1 * dot(ca - cb, float3(1, 1, 1)));
float3 c = lerp(ca, cb, w);
float h = lerp(Height.SampleGrad(HeightSampler, uv + oa, dx, dy).r, Height.SampleGrad(HeightSampler, uv + ob, dx, dy).r, w);
float3 nm = lerp(Normals.SampleGrad(NormalsSampler, uv + oa, dx, dy).rgb, Normals.SampleGrad(NormalsSampler, uv + ob, dx, dy).rgb, w);
float lum = dot(c, float3(0.299, 0.587, 0.114)) / 0.20;
float top = smoothstep(0.22, 0.42, h);
float zone = 1.0 - smoothstep(0.42, 0.95, across + 0.30 * (N.Value(wp * 1.1 + 4.0) - 0.5));
float age = N.Value(wp * 0.7 + 9.0);
float chips = N.Value(wp * 32.0) * 0.6 + N.Value(wp * 90.0 + 3.0) * 0.4;
float keep = zone * step(chips, 0.93 - 0.40 * age - 0.25 * (1.0 - top));
// The Godot shader's colours are sRGB (source_color): (0.60, 0.30, 0.02), (0.60, 0.40, 0.16), (0.20, 0.15, 0.11), linear here.
float3 paint = float3(0.319, 0.073, 0.0015);
float3 faded = float3(0.319, 0.133, 0.022);
float3 soil = float3(0.033, 0.019, 0.012);
float3 coat = lerp(paint, faded, age * 0.65) * lerp(1.0, lum, 0.55);
float3 face = lerp(c * float3(0.80, 0.66, 0.50), coat, keep);
face *= 1.0 - 0.20 * (1.0 - smoothstep(0.0, 0.25, across));
float3 joint = soil * lerp(0.7, 1.3, lum * 0.5);
float3 albedo = lerp(joint, face, top) * Shade;
albedo *= lerp(1.0, 0.65, Wet);
float2 t = (nm.xy * 2.0 - 1.0) * 1.2;
Normal = normalize(VertexNormal + float3(t.x, t.y, 0.0));
Roughness = lerp(lerp(0.95, lerp(0.86, 0.62, keep), top), 0.45, Wet);
Occlusion = lerp(0.45, 1.0, smoothstep(0.12, 0.40, h));
return albedo;'''


def shoulder_material():
    """The verge (0.74), as the Godot game draws it (shoulder.gdshader, 0.36; ISTERLER P12, the owner: the stones at the
    roadside orange, "like painted orange stones", a darker orange, not fake, never repeating): a photographed stone bed
    (ambientCG Gravel020, the game's stones_*.jpg) painted the 1989 cabinet's burnt orange, fully by the asphalt and
    thinning outwards, chipped in flecks and faded unevenly along the road, dark soil in the joints, the outer edge
    fraying into the grass (masked); the photo's tile is broken up by two offset lookups blended where a slow noise
    steps, and its relief bends the normal. The red-brown mud scan it replaces read as a flat orange band (0.13). The
    same HLSL as the Godot shader, in one Custom node; UV.x runs 0 at the asphalt to 1 at the band's outer edge."""
    m = asset_tools.create_asset("M_Shoulder", f"{GAME}/Materials", unreal.Material, unreal.MaterialFactoryNew())
    m.set_editor_property("blend_mode", unreal.BlendMode.BLEND_MASKED)
    m.set_editor_property("two_sided", True)
    m.set_editor_property("tangent_space_normal", False)
    textures = os.path.join(ROOT, "..", "game", "assets", "textures")
    maps = {
        "Stones": import_texture("GrudgeLap", "stones_color", "colour", path=os.path.join(textures, "stones_color.jpg")),
        "Normals": import_texture("GrudgeLap", "stones_normal", "mask", path=os.path.join(textures, "stones_normal.jpg")),
        "Height": import_texture("GrudgeLap", "stones_height", "mask", path=os.path.join(textures, "stones_height.jpg")),
    }
    custom = mel.create_material_expression(m, unreal.MaterialExpressionCustom, -400, 0)
    custom.set_editor_property("code", SHOULDER_HLSL)
    custom.set_editor_property("output_type", unreal.CustomMaterialOutputType.CMOT_FLOAT3)
    custom.set_editor_property("description", "GrudgeLap verge")
    names = ["UV", "WorldXY", "VertexNormal", "Shade", "Wet", "Stones", "Normals", "Height"]
    def custom_input(name):
        entry = unreal.CustomInput()
        entry.set_editor_property("input_name", name)
        return entry

    def custom_output(name, kind):
        entry = unreal.CustomOutput()
        entry.set_editor_property("output_name", name)
        entry.set_editor_property("output_type", kind)
        return entry

    custom.set_editor_property("inputs", [custom_input(n) for n in names])
    outputs = [("Opacity", unreal.CustomMaterialOutputType.CMOT_FLOAT1), ("Normal", unreal.CustomMaterialOutputType.CMOT_FLOAT3),
               ("Roughness", unreal.CustomMaterialOutputType.CMOT_FLOAT1), ("Occlusion", unreal.CustomMaterialOutputType.CMOT_FLOAT1)]
    custom.set_editor_property("additional_outputs", [custom_output(n, k) for n, k in outputs])
    uv = mel.create_material_expression(m, unreal.MaterialExpressionTextureCoordinate, -900, -300)
    world = mel.create_material_expression(m, unreal.MaterialExpressionWorldPosition, -1100, -200)
    xy = mel.create_material_expression(m, unreal.MaterialExpressionComponentMask, -900, -200)
    for c in "rgba":
        xy.set_editor_property(c, c in "rg")
    mel.connect_material_expressions(world, "", xy, "")
    normal = mel.create_material_expression(m, unreal.MaterialExpressionVertexNormalWS, -900, -100)
    shade = linear_vertex_colour(m, -900, 0)
    mel.connect_material_expressions(uv, "", custom, "UV")
    mel.connect_material_expressions(xy, "", custom, "WorldXY")
    mel.connect_material_expressions(normal, "", custom, "VertexNormal")
    mel.connect_material_expressions(shade, "", custom, "Shade")
    if WEATHER["collection"] is not None:
        wet = mel.create_material_expression(m, unreal.MaterialExpressionCollectionParameter, -900, 100)
        wet.set_editor_property("collection", WEATHER["collection"])
        wet.set_editor_property("parameter_name", "Wetness")
    else:
        wet = mel.create_material_expression(m, unreal.MaterialExpressionConstant, -900, 100)
    mel.connect_material_expressions(wet, "", custom, "Wet")
    for i, (name, texture) in enumerate(maps.items()):
        node = mel.create_material_expression(m, unreal.MaterialExpressionTextureObject, -900, 200 + 100 * i)
        node.set_editor_property("texture", texture)
        if name != "Stones":
            node.set_editor_property("sampler_type", unreal.MaterialSamplerType.SAMPLERTYPE_MASKS)
        mel.connect_material_expressions(node, "", custom, name)
    mel.connect_material_property(custom, "", unreal.MaterialProperty.MP_BASE_COLOR)
    mel.connect_material_property(custom, "Opacity", unreal.MaterialProperty.MP_OPACITY_MASK)
    mel.connect_material_property(custom, "Normal", unreal.MaterialProperty.MP_NORMAL)
    mel.connect_material_property(custom, "Roughness", unreal.MaterialProperty.MP_ROUGHNESS)
    mel.connect_material_property(custom, "Occlusion", unreal.MaterialProperty.MP_AMBIENT_OCCLUSION)
    specular = mel.create_material_expression(m, unreal.MaterialExpressionConstant, -200, 300)
    specular.set_editor_property("r", 0.2)   # the sky's blue in a stone's sheen turned the orange pink (shoulder.gdshader)
    mel.connect_material_property(specular, "", unreal.MaterialProperty.MP_SPECULAR)
    mel.recompile_material(m)
    eal.save_loaded_asset(m)
    return m


GRATING_HLSL = '''struct FGlpBars
{
    float Integral(float x, float p, float w)
    {
        float k = floor(x / p + 0.5);
        return k * w + clamp(x - k * p + 0.5 * w, 0.0, w);
    }
    float Cover(float x, float p, float w, float fw)
    {
        fw = max(fw, 1e-5);
        return (Integral(x + 0.5 * fw, p, w) - Integral(x - 0.5 * fw, p, w)) / fw;
    }
};
FGlpBars B;
float2 pitch = float2(0.060, 0.102);
float2 bar = float2(0.010, 0.006);
float2 depth = float2(0.076, 0.010);
float3 up = normalize(Up);
float3 across = normalize(Across);
float3 along = normalize(cross(up, across));
float3 ray = -normalize(ToEye);
float rn = dot(ray, up);
float inv = 1.0 / max(abs(rn), 1e-3);
float2 halfWalk = float2(dot(ray, across), dot(ray, along)) * inv * depth * (rn < 0.0 ? 0.5 : -0.5);
float2 fw = fwidth(UV) * 1.6;
float2 top = float2(B.Cover(UV.x, pitch.x, bar.x, fw.x), B.Cover(UV.y, pitch.y, bar.y, fw.y));
float2 band = min(pitch, bar + 2.0 * abs(halfWalk));
float2 blocked = float2(B.Cover(UV.x + halfWalk.x, pitch.x, band.x, fw.x), B.Cover(UV.y + halfWalk.y, pitch.y, band.y, fw.y));
float topCover = 1.0 - (1.0 - top.x) * (1.0 - top.y);
float solid = 1.0 - (1.0 - blocked.x) * (1.0 - blocked.y);
float wall = saturate((solid - topCover) / max(solid, 1e-4));
float3 face = rn < 0.0 ? up : -up;
float3 wallN = normalize(-sign(dot(ray, across) + 1e-6) * across * max(blocked.x - top.x, 0.0)
    - sign(dot(ray, along) + 1e-6) * along * max(blocked.y - top.y, 0.0) + face * 1e-3);
Normal = normalize(lerp(face, wallN, wall * 0.7));
float far = smoothstep(0.3, 1.0, max(fw.x / pitch.x, fw.y / pitch.y));
Roughness = max(lerp(0.45, 0.22, Wet), lerp(0.0, 0.6, far));
// The coverage dithered in time: a per-pixel, per-frame threshold the temporal upscaler (DLSS or TSR) averages back to
// the deck's opacity; the mask keeps the pixel where the coverage passes it (clip value 0.5).
float dither = InterleavedGradientNoise(Parameters.SvPosition.xy, View.StateFrameIndexMod8);
Coverage = solid + 0.5 - dither;
// The Godot shader's steel is sRGB (0.62, 0.63, 0.64): linear here.
return float3(0.342, 0.354, 0.367) * lerp(1.0, 0.5, wall) * lerp(1.0, 0.72, Wet);'''


def grating_material():
    """The loop's running surface (0.74), as the Godot game draws it (loop_grating.gdshader, 0.72; ISTERLER P16, the
    owner: a perforated steel deck to see through, as in the original): a galvanised bar grating computed per pixel,
    bearing bars 10 mm thick every 60 mm across, cross bars 6 mm every 102 mm along, the bars' depth closing the deck up
    at a grazing angle; the export carries the deck as plain white (its material is Godot's shader). Masked here: the
    coverage, box-filtered over the pixel's footprint, is dithered in time and resolved by DLSS or TSR into a veil of
    the deck's opacity, so the deck still writes depth and casts its ladder of shadows in the ray tracing. UV.x is
    metres across the deck, UV.y metres along it (WorldBuilder.BuildLoopDeck)."""
    m = asset_tools.create_asset("M_LoopGrating", f"{GAME}/Materials", unreal.Material, unreal.MaterialFactoryNew())
    m.set_editor_property("blend_mode", unreal.BlendMode.BLEND_MASKED)
    m.set_editor_property("two_sided", True)
    m.set_editor_property("tangent_space_normal", False)
    custom = mel.create_material_expression(m, unreal.MaterialExpressionCustom, -400, 0)
    custom.set_editor_property("code", GRATING_HLSL)
    custom.set_editor_property("output_type", unreal.CustomMaterialOutputType.CMOT_FLOAT3)
    custom.set_editor_property("description", "GrudgeLap grating")

    def custom_input(name):
        entry = unreal.CustomInput()
        entry.set_editor_property("input_name", name)
        return entry

    def custom_output(name, kind):
        entry = unreal.CustomOutput()
        entry.set_editor_property("output_name", name)
        entry.set_editor_property("output_type", kind)
        return entry

    names = ["UV", "Up", "Across", "ToEye", "Wet"]
    custom.set_editor_property("inputs", [custom_input(n) for n in names])
    custom.set_editor_property("additional_outputs", [custom_output("Normal", unreal.CustomMaterialOutputType.CMOT_FLOAT3),
                                                      custom_output("Roughness", unreal.CustomMaterialOutputType.CMOT_FLOAT1),
                                                      custom_output("Coverage", unreal.CustomMaterialOutputType.CMOT_FLOAT1)])
    uv = mel.create_material_expression(m, unreal.MaterialExpressionTextureCoordinate, -900, -300)
    up = mel.create_material_expression(m, unreal.MaterialExpressionVertexNormalWS, -900, -200)
    across = mel.create_material_expression(m, unreal.MaterialExpressionVertexTangentWS, -900, -100)
    eye = mel.create_material_expression(m, unreal.MaterialExpressionCameraVectorWS, -900, 0)
    if WEATHER["collection"] is not None:
        wet = mel.create_material_expression(m, unreal.MaterialExpressionCollectionParameter, -900, 100)
        wet.set_editor_property("collection", WEATHER["collection"])
        wet.set_editor_property("parameter_name", "Wetness")
    else:
        wet = mel.create_material_expression(m, unreal.MaterialExpressionConstant, -900, 100)
    for node, pin in ((uv, "UV"), (up, "Up"), (across, "Across"), (eye, "ToEye"), (wet, "Wet")):
        mel.connect_material_expressions(node, "", custom, pin)
    m.set_editor_property("opacity_mask_clip_value", 0.5)
    mel.connect_material_property(custom, "", unreal.MaterialProperty.MP_BASE_COLOR)
    mel.connect_material_property(custom, "Normal", unreal.MaterialProperty.MP_NORMAL)
    mel.connect_material_property(custom, "Roughness", unreal.MaterialProperty.MP_ROUGHNESS)
    mel.connect_material_property(custom, "Coverage", unreal.MaterialProperty.MP_OPACITY_MASK)
    metallic = mel.create_material_expression(m, unreal.MaterialExpressionConstant, -150, 350)
    metallic.set_editor_property("r", 0.85)
    mel.connect_material_property(metallic, "", unreal.MaterialProperty.MP_METALLIC)
    mel.recompile_material(m)
    eal.save_loaded_asset(m)
    return m


def land_material():
    """The real land round the circuit (the Godot version's RealLand, 0.37): Jackson Hole's satellite image on the mesh's
    UV, turning to the plain's green by the export's rim weight (vertex colour B) where it meets the game's terrain,
    the sky's share (G) as its ambient occlusion. The export's vertex colours are the land's light, not its paint:
    multiplied in as the glTF parents do, they turned the mountains pink. The sun's visibility (R) stays out: the
    virtual shadow maps cast the mountains' own shadows here, where the Godot version's shadow maps end at 350 m.
    """
    path = f"{GAME}/Materials/M_Land"
    if eal.does_asset_exist(path):
        eal.delete_asset(path)
    material = asset_tools.create_asset("M_Land", f"{GAME}/Materials", unreal.Material, unreal.MaterialFactoryNew())
    albedo = import_texture("GrudgeLap", "jackson_albedo", "colour",
                            path=os.path.join(ROOT, "..", "game", "assets", "terrain", "jackson_albedo.jpg"))
    # 5120 texels square: Unreal builds no mip chain for a texture that is not a power of two, and the far land then
    # shimmered; stretched to 8192 it has one.
    try:
        albedo.set_editor_property("power_of_two_mode", unreal.TexturePowerOfTwoSetting.STRETCH_TO_POWER_OF_TWO)
        eal.save_loaded_asset(albedo)
    except Exception as error:  # an engine without the setting draws it without mips
        log(f"land albedo: {error}")

    def node(cls, x, y, **props):
        made = mel.create_material_expression(material, cls, x, y)
        for key, value in props.items():
            made.set_editor_property(key, value)
        return made

    image = node(unreal.MaterialExpressionTextureSample, -900, -300, texture=albedo,
                 sampler_type=unreal.MaterialSamplerType.SAMPLERTYPE_COLOR)
    mel.connect_material_expressions(node(unreal.MaterialExpressionTextureCoordinate, -1100, -300), "", image, "UVs")
    # M_Ground's meadow (the aerial scan under its tint) averages this linear colour.
    plain = node(unreal.MaterialExpressionConstant3Vector, -900, -500, constant=unreal.LinearColor(0.09, 0.12, 0.03, 1.0))
    vertex = linear_vertex_colour(material, -900, 0)
    rim = node(unreal.MaterialExpressionComponentMask, -600, 0, r=False, g=False, b=True, a=False)
    mel.connect_material_expressions(vertex, "", rim, "")
    sky = node(unreal.MaterialExpressionComponentMask, -600, 150, r=False, g=True, b=False, a=False)
    mel.connect_material_expressions(vertex, "", sky, "")
    colour = node(unreal.MaterialExpressionLinearInterpolate, -300, -300)
    mel.connect_material_expressions(plain, "", colour, "A")
    mel.connect_material_expressions(image, "RGB", colour, "B")
    mel.connect_material_expressions(rim, "", colour, "Alpha")
    mel.connect_material_property(colour, "", unreal.MaterialProperty.MP_BASE_COLOR)
    mel.connect_material_property(node(unreal.MaterialExpressionConstant, -300, 0, r=0.95), "", unreal.MaterialProperty.MP_ROUGHNESS)
    mel.connect_material_property(node(unreal.MaterialExpressionConstant, -300, 100, r=0.3), "", unreal.MaterialProperty.MP_SPECULAR)
    mel.connect_material_property(sky, "", unreal.MaterialProperty.MP_AMBIENT_OCCLUSION)
    mel.recompile_material(material)
    eal.save_loaded_asset(material)
    return material


def dress_track(actors):
    # Terrain UVs span the 5 km ground (world * 0.0002 + 0.5); road UVs run 0..1 across 11 m and in metres along;
    # shoulder strips run 0..1 across 2.2 m. Tile sizes are the scans' real ones (Poly Haven "dimensions").
    # Mean linear albedos measured from the scans: asphalt 0.10 (kept), the aerial grass (0.17, 0.12, 0.03) is a
    # dry yellow; the tint turns it to a meadow's (0.09, 0.12, 0.03).
    ground = surface_material("M_Ground", "aerial_grass_rock", 5000 / 15.0, 5000 / 15.0, tint=(0.5, 1.0, 1.3), macro=23.0, wet={"dark": 0.3, "rough": 0.55}, hues=True)
    # The road's wear sheet is the game's own (tools/make-road-wear.py); the road mesh carries its UV2 and the
    # vertex colours of the slabs, wheel tracks, patches and potholes.
    wear = import_texture("GrudgeLap", "road_wear", "colour", path=os.path.join(ROOT, "..", "game", "assets", "textures", "road_wear.png"))
    # Anti-tiling (0.13): the asphalt scan sampled again a quarter turn round and blended in by a slow noise, a 57 m
    # brightness wobble on top, and the wear decals (road_decals) over it all. Wet under the weather, with puddles.
    asphalt = surface_material("M_Asphalt", "asphalt_02", 11.0 / 3.0, 1 / 3.0, tint=(0.9, 0.9, 0.9), macro=19.0, vertex_colour=True, wear=wear,
                               anti_tiling=True, wet={"dark": 0.5, "puddles": True})
    # The painted stones of the Godot version's verge (0.74; 0.13-0.73 a red-brown mud scan; the grass path read as lawn).
    verge = shoulder_material()
    # The earthworks (the fork hill, the cuttings' walls) are bare earth in the game: its TerrainEarth mesh shares
    # the terrain's UVs (5 km across), so the scan tiles every 4 m there. Compacted grey-brown dirt (0.74), as the Godot
    # game's cuttings (Ground067 since 0.36): the mud scan tinted red-brown read as orange clay hills beside the road, the
    # dirt scan at its own shade as dunes of sand.
    earth = surface_material("M_Earth", "dirt_floor", 5000 / 4.0, 5000 / 4.0, tint=(0.62, 0.58, 0.52), macro=23.0)
    land = land_material() if "Land" in actors else None
    grating = grating_material() if any(label.startswith("LoopDeck-") for label in actors) else None
    for label, actor in actors.items():
        material = ground if label == "Terrain" else earth if label == "TerrainEarth" else asphalt if label.startswith("Road-") else verge if label.startswith("Shoulder-") else land if label == "Land" else grating if label.startswith("LoopDeck-") else None
        if material is None:
            continue
        for component in mesh_components(actor):
            for slot in range(component.get_num_materials()):
                component.set_material(slot, material)
            if label in ("Terrain", "TerrainEarth", "Land"):
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


def instanced(label, mesh, transforms, shadows=True, cull=0, ray_traced=True):
    """One actor holding every instance of a mesh (an instanced static mesh component); instances past cull
    centimetres are not drawn (0: always drawn). ray_traced=False keeps them out of the ray-tracing scene (0.74):
    the quarter of a million grass tufts would cost more to keep there than they give to reflections or bounce
    light (Lumen warns above 100 000 instances)."""
    actor = actor_sub.spawn_actor_from_class(unreal.Actor, unreal.Vector(0, 0, 0))
    actor.set_actor_label(label)
    actor.set_folder_path("GrudgeLap/Foliage")
    handles = subobjects.k2_gather_subobject_data_for_instance(actor)
    # Hierarchical (0.76): the instances sit in a cluster tree culled as a whole, where the plain instanced component's
    # quarter of a million grass tufts were culled one by one (the render thread waited 5-7 ms a frame on visibility).
    params = unreal.AddNewSubobjectParams(parent_handle=handles[0], new_class=unreal.HierarchicalInstancedStaticMeshComponent)
    handle, reason = subobjects.add_new_subobject(params)
    component = unreal.SubobjectDataBlueprintFunctionLibrary.get_object(unreal.SubobjectDataBlueprintFunctionLibrary.get_data(handle))
    if component is None:
        raise RuntimeError(f"could not add an instanced mesh component: {reason}")
    component.set_static_mesh(mesh)
    # Static (0.74): the scattered plants and rocks never move. Measured: no change in the frame time (the grass stays
    # among the scene's dynamic instances, 230 000 of them, for its wind; the render thread waits 5-7 ms on visibility).
    component.set_mobility(unreal.ComponentMobility.STATIC)
    component.set_collision_enabled(unreal.CollisionEnabled.NO_COLLISION)
    component.set_editor_property("cast_shadow", shadows)
    if not ray_traced:
        component.set_editor_property("visible_in_ray_tracing", False)
    component.add_instances(transforms, False, True)
    if cull:
        component.set_cull_distances(0, cull)
    log(f"{label}: {len(transforms)} instances of {mesh.get_name()}")
    return actor


def transform(position, yaw_degrees, scale, sink=0.0):
    location = to_unreal(position)
    location.z -= sink
    return unreal.Transform(location=location, rotation=unreal.Rotator(0.0, 0.0, yaw_degrees), scale=unreal.Vector(scale, scale, scale))


def godot_transform(t):
    """An instance transform the export wrote (Godot's basis columns and origin, metres) as Unreal's: the importer maps
    Godot's (x, y, z) to Unreal's (x, z, y), so the basis is conjugated by that swap and the origin swapped and scaled.
    Plain Python: the editor's Python has no numpy."""
    columns = [(t[0], t[1], t[2]), (t[3], t[4], t[5]), (t[6], t[7], t[8])]
    # Swap y and z in each column, then swap the second and third columns.
    swapped = [(c[0], c[2], c[1]) for c in columns]
    axes = [swapped[0], swapped[2], swapped[1]]
    scale = [math.sqrt(sum(v * v for v in a)) for a in axes]
    unit = [tuple(v / max(s, 1e-9) for v in a) for a, s in zip(axes, scale)]
    rotation = unreal.MathLibrary.make_rotation_from_axes(unreal.Vector(*unit[0]), unreal.Vector(*unit[1]), unreal.Vector(*unit[2]))
    origin = (t[9] * 100.0, t[11] * 100.0, t[10] * 100.0)
    return unreal.Transform(unreal.Vector(*origin), rotation, unreal.Vector(*scale))


def place_instanced(placements, actors):
    """The fences, the power poles and the marker posts (0.76): the export carries each kind as placements of one template
    mesh (Instanced-<n>, WorldExport); they are instanced here with that mesh and its material, the marker posts'
    reflectors in their amber (right) and white (left), and each pair of neighbouring poles strung with the three wires
    the game draws, parabolas of 0.9 m sag between the insulators (PowerLineBuilder: 9.72 m up, 1.05 m apart), out of
    one-metre pieces of the export's Instanced-Wire. The templates go once placed."""
    entries = placements.get("instanced", [])
    templates = {}
    for label, actor in actors.items():
        if label.startswith("Instanced-"):
            components = actor.get_components_by_class(unreal.StaticMeshComponent)
            if components:
                c = components[0]
                templates[label] = (c.get_editor_property("static_mesh"), [c.get_material(i) for i in range(c.get_num_materials())])
    counts = {}
    tints = {}

    def tinted(material, colour, key):
        if key not in tints:
            instance = asset_tools.create_asset(f"MI_Reflector_{key}", f"{GAME}/Materials", unreal.MaterialInstanceConstant,
                                                unreal.MaterialInstanceConstantFactoryNew())
            mel.set_material_instance_parent(instance, material)
            try:
                mel.set_material_instance_vector_parameter_value(instance, "BaseColorFactor", unreal.LinearColor(*colour, 1.0))
            except Exception:
                pass
            mel.update_material_instance(instance)
            eal.save_loaded_asset(instance)
            tints[key] = instance
        return tints[key]

    for n, entry in enumerate(entries):
        mesh, materials = templates.get(entry["mesh"], (None, None))
        if mesh is None:
            continue
        group = entry["group"]
        transforms = [godot_transform(t) for t in entry["transforms"]]
        colours = entry.get("colours")
        sets = [(transforms, None)]
        if colours:
            amber = [tr for tr, c in zip(transforms, colours) if c[1] < 0.8]
            white = [tr for tr, c in zip(transforms, colours) if c[1] >= 0.8]
            sets = [(amber, ("amber", (1.0, 0.31, 0.007))), (white, ("white", (0.89, 0.89, 0.89)))]
        for part, (subset, tint) in enumerate(sets):
            if not subset:
                continue
            actor = instanced(f"{group}{n}_{part}", mesh, subset, cull=70000 if group != "PowerLines" else 250000,
                              ray_traced=group == "PowerLines")
            for component in actor.get_components_by_class(unreal.InstancedStaticMeshComponent):
                for slot, material in enumerate(materials):
                    if material is not None:
                        component.set_material(slot, tinted(material, tint[1], tint[0]) if tint else material)
            counts[group] = counts.get(group, 0) + len(subset)

    # The wires between neighbouring poles, in the order the export lists them along each line.
    wire = templates.get("Instanced-Wire")
    poles = [t for entry in entries if entry["group"] == "PowerLines" for t in entry["transforms"]]
    pieces = []
    if wire is not None and poles:
        def add(u, v, s=1.0):
            return [x + y * s for x, y in zip(u, v)]

        for a, b in zip(poles, poles[1:]):
            if math.dist(a[9:12], b[9:12]) > 55.0 * 1.5:
                continue
            for across in (-1.05, 0.0, 1.05):
                # The insulator's top in each pole's frame: x along the line, y up, z across (Godot's).
                ends = [add(add(list(t[9:12]), t[3:6], 9.72), t[6:9], across) for t in (a, b)]
                points = []
                for i in range(17):
                    f = i / 16.0
                    p = add(ends[0], [e1 - e0 for e0, e1 in zip(ends[0], ends[1])], f)
                    p[1] -= 0.9 * 4.0 * f * (1.0 - f)
                    points.append(p)
                for p0, p1 in zip(points, points[1:]):
                    u0 = (p0[0] * 100.0, p0[2] * 100.0, p0[1] * 100.0)
                    u1 = (p1[0] * 100.0, p1[2] * 100.0, p1[1] * 100.0)
                    direction = unreal.Vector(u1[0] - u0[0], u1[1] - u0[1], u1[2] - u0[2])
                    length = math.dist(u0, u1) / 100.0
                    middle = unreal.Vector((u0[0] + u1[0]) / 2.0, (u0[1] + u1[1]) / 2.0, (u0[2] + u1[2]) / 2.0)
                    # The template is a metre of wire along Godot's +Y, which is Unreal's +Z.
                    rotation = unreal.MathLibrary.make_rot_from_z(direction)
                    pieces.append(unreal.Transform(middle, rotation, unreal.Vector(1.0, 1.0, length)))
        if pieces:
            actor = instanced("PowerLineWires", wire[0], pieces, cull=250000, shadows=False, ray_traced=False)
            for component in actor.get_components_by_class(unreal.InstancedStaticMeshComponent):
                for slot, material in enumerate(wire[1]):
                    if material is not None:
                        component.set_material(slot, material)
    for label in [label for label in actors if label.startswith("Instanced-")]:
        destroy_with_children(actors[label])
    log(f"instanced dressing: {counts}, {len(pieces)} wire pieces")


WIND_HLSL = """struct FGlpWind
{
    float Hash(float2 p)
    {
        float3 p3 = frac(float3(p.xyx) * 0.1031);
        p3 += dot(p3, p3.yzx + 33.33);
        return frac((p3.x + p3.y) * p3.z);
    }
    float Value(float2 p)
    {
        float2 i = floor(p);
        float2 f = frac(p);
        float2 u = f * f * (3.0 - 2.0 * f);
        return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
    }
};
FGlpWind W;
// Godot's wind (wind.gdshaderinc) on its (x, z) plane, which is Unreal's (x, y): downwind to -x and -z there.
const float2 Dir = float2(-0.944, -0.330);
float2 p = Origin.xy / 100.0;
float2 across = float2(-Dir.y, Dir.x);
float along = dot(p, Dir) - 4.5 * 1.3 * T;
float side = dot(p, across);
float n = W.Value(float2(along / 24.0, side / 70.0)) * 0.65 + W.Value(float2(along / 8.0, side / 18.0) + 17.0) * 0.35;
float gust = smoothstep(0.38, 0.82, n);
float flutter = sin(T * 6.3 + dot(p, float2(1.7, 2.3))) * (0.3 + 0.7 * gust);
float lean = (0.06 + 0.22 * gust + 0.05 * flutter) / max(Stiffness, 0.1);
// The plant's own height at this instance's scale; the tip leans lean times it, the foot stays put.
float tall = max(Height * length(Up), 1.0);
float h = saturate(Offset.z / tall);
return float3(Dir.x, Dir.y, 0.0) * (lean * h * h * tall);"""


def add_wind(material):
    """Adds the ground's wind (WIND_HLSL) to a material's world position offset: each vertex leans downwind by the square
    of its height over the instance's foot, as a share of the plant's height (scalar parameters WindHeight, cm, the
    mesh's height before the instance's scale, and WindStiffness, 1 for the grass as Godot's tufts, 9 for a tree as its
    impostors). The instance's foot and scale come from the local position in instance space taken to the world."""
    mp = unreal.MaterialProperty

    def expression(cls, x, y):
        return mel.create_material_expression(material, cls, x, y)

    local = expression(unreal.MaterialExpressionLocalPosition, -1600, 1400)
    offset = expression(unreal.MaterialExpressionTransform, -1350, 1400)
    offset.set_editor_property("transform_source_type", unreal.MaterialVectorCoordTransformSource.TRANSFORMSOURCE_LOCAL)
    offset.set_editor_property("transform_type", unreal.MaterialVectorCoordTransform.TRANSFORM_WORLD)
    mel.connect_material_expressions(local, "", offset, "")
    unit = expression(unreal.MaterialExpressionConstant3Vector, -1600, 1550)
    unit.set_editor_property("constant", unreal.LinearColor(0.0, 0.0, 1.0, 1.0))
    up = expression(unreal.MaterialExpressionTransform, -1350, 1550)
    up.set_editor_property("transform_source_type", unreal.MaterialVectorCoordTransformSource.TRANSFORMSOURCE_LOCAL)
    up.set_editor_property("transform_type", unreal.MaterialVectorCoordTransform.TRANSFORM_WORLD)
    mel.connect_material_expressions(unit, "", up, "")
    where = expression(unreal.MaterialExpressionWorldPosition, -1350, 1250)
    foot = expression(unreal.MaterialExpressionSubtract, -1100, 1300)
    mel.connect_material_expressions(where, "", foot, "A")
    mel.connect_material_expressions(offset, "", foot, "B")
    time = expression(unreal.MaterialExpressionTime, -1100, 1700)
    stiffness = expression(unreal.MaterialExpressionScalarParameter, -1100, 1800)
    stiffness.set_editor_property("parameter_name", "WindStiffness")
    stiffness.set_editor_property("default_value", 1.0)
    height = expression(unreal.MaterialExpressionScalarParameter, -1100, 1900)
    height.set_editor_property("parameter_name", "WindHeight")
    height.set_editor_property("default_value", 45.0)
    custom = expression(unreal.MaterialExpressionCustom, -800, 1500)
    custom.set_editor_property("output_type", unreal.CustomMaterialOutputType.CMOT_FLOAT3)
    custom.set_editor_property("description", "GrudgeLap wind")
    custom.set_editor_property("code", WIND_HLSL)
    inputs = []
    for name in ("Origin", "Offset", "Up", "T", "Stiffness", "Height"):
        entry = unreal.CustomInput()
        entry.set_editor_property("input_name", name)
        inputs.append(entry)
    custom.set_editor_property("inputs", inputs)
    for source, name in ((foot, "Origin"), (offset, "Offset"), (up, "Up"), (time, "T"), (stiffness, "Stiffness"), (height, "Height")):
        mel.connect_material_expressions(source, "", custom, name)
    existing = mel.get_material_property_input_node(material, mp.MP_WORLD_POSITION_OFFSET)
    if existing is not None:
        total = expression(unreal.MaterialExpressionAdd, -500, 1500)
        mel.connect_material_expressions(existing, "", total, "A")
        mel.connect_material_expressions(custom, "", total, "B")
        mel.connect_material_property(total, "", mp.MP_WORLD_POSITION_OFFSET)
    else:
        mel.connect_material_property(custom, "", mp.MP_WORLD_POSITION_OFFSET)
    mel.recompile_material(material)


def windy(parent, copies):
    """A copy of a scanned plant's material chain whose master carries the wind (add_wind): a master is duplicated and
    given it, a material instance duplicated onto the windy copy of its own parent, so every parameter the glTF import
    set on the way stays. The engine's Interchange content itself is left alone."""
    key = parent.get_path_name()
    if key in copies:
        return copies[key]
    name = f"W_{parent.get_name()}"
    path = f"{GAME}/Materials/{name}"
    if eal.does_asset_exist(path):
        eal.delete_asset(path)
    copy = asset_tools.duplicate_asset(name, f"{GAME}/Materials", parent)
    if isinstance(copy, unreal.Material):
        add_wind(copy)
    else:
        mel.set_material_instance_parent(copy, windy(parent.get_editor_property("parent"), copies))
        mel.update_material_instance(copy)
    eal.save_loaded_asset(copy)
    copies[key] = copy
    return copy


# The wind's share by kind of plant (label prefix of scatter()'s actors): stiffness as Godot's (1 for the tufts, 9 for the
# trees; the bushes between), and how far out it is drawn (cm), past which the plant stands still.
WIND_KINDS = {"Grass": (1.0, 3000), "Sage": (3.0, 3000), "Bushes": (3.0, 5000), "Trees": (9.0, 15000), "Wood": (9.0, 15000)}


def wind_on_plants():
    """The plants scatter() placed lean and sway in the wind (0.76). Each scanned material on their components gives way to
    a windy copy (W_<kind>_<name>, made afresh with every build in the level's materials folder): a duplicate of the
    scanned instance, so it keeps the import's textures and factors, on a windy copy of its parent chain (windy), told the
    tallest of its meshes' heights and the kind's stiffness. The sage's tinted copies move onto the windy copy of their
    scanned material. The scanned materials themselves, kept between builds with the Poly Haven imports, stay as imported:
    a build that reparented them left them pointing at copies the next build deletes. The components evaluate the offset
    only near the eye and keep their virtual shadow pages (the shadows hold still: a swaying shadow would invalidate them
    every frame)."""
    copies = {}
    plants = {}   # (scanned material, kind) -> [scanned material, kind, tallest mesh in cm]
    uses = []     # (component, slot, key, the sage's tinted copy or None)
    for actor in actor_sub.get_all_level_actors():
        label = actor.get_actor_label()
        kind = next((k for k in WIND_KINDS if label.startswith(k)), None)
        if kind is None:
            continue
        distance = WIND_KINDS[kind][1]
        for component in actor.get_components_by_class(unreal.InstancedStaticMeshComponent):
            mesh = component.get_editor_property("static_mesh")
            tall = mesh.get_bounds().box_extent.z * 2.0 if mesh else 45.0
            for setting, value in (("world_position_offset_disable_distance", distance), ("evaluate_world_position_offset", True),
                                   ("shadow_cache_invalidation_behavior", unreal.ShadowCacheInvalidationBehavior.RIGID)):
                try:
                    component.set_editor_property(setting, value)
                except Exception as error:  # a renamed setting must not stop the build
                    log(f"wind {setting}: {error}")
            for slot in range(component.get_num_materials()):
                material = component.get_material(slot)
                if not isinstance(material, unreal.MaterialInstanceConstant):
                    continue
                sage = material if material.get_name().startswith("MI_Sage_") else None
                scanned = material.get_editor_property("parent") if sage else material
                if not isinstance(scanned, unreal.MaterialInstanceConstant) or scanned.get_editor_property("parent") is None:
                    continue
                key = (scanned.get_path_name(), kind)
                entry = plants.setdefault(key, [scanned, kind, 0.0])
                entry[2] = max(entry[2], tall)
                uses.append((component, slot, key, sage))
    made = {}
    for key, (scanned, kind, tall) in plants.items():
        name = f"W_{kind}_{scanned.get_name()}"
        path = f"{GAME}/Materials/{name}"
        if eal.does_asset_exist(path):
            eal.delete_asset(path)
        copy = asset_tools.duplicate_asset(name, f"{GAME}/Materials", scanned)
        mel.set_material_instance_parent(copy, windy(scanned.get_editor_property("parent"), copies))
        mel.set_material_instance_scalar_parameter_value(copy, "WindStiffness", WIND_KINDS[kind][0])
        mel.set_material_instance_scalar_parameter_value(copy, "WindHeight", max(tall, 10.0))
        mel.update_material_instance(copy)
        eal.save_loaded_asset(copy)
        made[key] = copy
    moved = set()
    for component, slot, key, sage in uses:
        if sage is None:
            component.set_material(slot, made[key])
        elif sage.get_path_name() not in moved:
            moved.add(sage.get_path_name())
            mel.set_material_instance_parent(sage, made[key])
            mel.update_material_instance(sage)
            eal.save_loaded_asset(sage)
    log(f"wind: {len(made)} windy material(s) for {len(uses)} slot(s), {len(moved)} sage tint(s) moved, {len(copies)} windy parent(s)")


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

    def place(label, choices, spots, cull=0, ray_traced=True):
        """choices: (mesh, scale, weight); spots: (position, yaw degrees, scale, sink cm). One component per mesh."""
        sets = {}
        weights = [w for _, _, w in choices]
        for position, yaw, scale, sink in spots:
            mesh, base, _ = rng.choices(choices, weights)[0]
            sets.setdefault(mesh.get_path_name(), (mesh, []))[1].append(transform(position, yaw, base * scale, sink))
        for i, (mesh, transforms) in enumerate(sets.values()):
            instanced(f"{label}{i}", mesh, transforms, cull=cull, ray_traced=ray_traced)

    # 22 scanned tufts of 10 to 40 cm; the meadow takes them at 1 to 2.8 times (prepare_scatter.py).
    grass = [(m, 1.0, 1.0) for m in meshes["grass_medium_01"] + meshes["grass_medium_02"]]
    # The grass now lines every road (a quarter of a million tufts); a tuft under a metre tall is gone to the eye
    # long before 300 m, so it is not drawn beyond.
    if grass:
        place("Grass", grass, [((x, y, z), math.degrees(yaw), scale, 2.0) for x, y, z, yaw, scale in extra["grass"]], cull=30000, ray_traced=False)

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

    # The wood beyond the fields (FoliageBuilder.BuildForest): the game's placements of its card trees, stood here
    # as the scanned trees at the game's sizes, not drawn beyond two and a half kilometres. The one-of-each
    # template meshes the file carries for other importers (ForestVariant-<n>) and the empty Forest node go.
    wood = placements.get("forest", [])
    if trees and wood:
        place("Wood", trees, [((x, y, z), math.degrees(yaw), sx, 10.0) for x, y, z, yaw, sx, sy, variant in wood], cull=250000)
    for label in [label for label in actors if label.startswith("ForestVariant-") or label == "Forest"]:
        destroy_with_children(actors[label])

    bushes = [(m, s, 1.0) for ms, s in ((meshes["shrub_02"], 1.0), (meshes["shrub_03"], 2.5), (meshes["shrub_04"], 2.0)) for m in ms]
    if bushes:
        place("Bushes", bushes, [(b["position"], b["yaw"], b["scale"], 3.0) for b in extra["bushes"]], cull=80000, ray_traced=False)
    # Sagebrush along every road (0.76, prepare_scatter.sagebrush): the same scanned shrubs, small and in the sage's
    # grey-green (their materials tinted on the instances' components), out to 300 m from the eye.
    if bushes and extra.get("sage"):
        before = {a.get_actor_label() for a in actor_sub.get_all_level_actors()}
        place("Sage", bushes, [(b["position"], b["yaw"], b["scale"], 3.0) for b in extra["sage"]], cull=30000, ray_traced=False)
        sage_tint = unreal.LinearColor(0.62, 0.70, 0.58, 1.0)
        tinted = {}
        for actor in actor_sub.get_all_level_actors():
            if not actor.get_actor_label().startswith("Sage") or actor.get_actor_label() in before:
                continue
            for component in actor.get_components_by_class(unreal.InstancedStaticMeshComponent):
                for slot in range(component.get_num_materials()):
                    source = component.get_material(slot)
                    if not isinstance(source, unreal.MaterialInstance):
                        continue
                    if source.get_name() not in tinted:
                        try:
                            factor = mel.get_material_instance_vector_parameter_value(source, "BaseColorFactor")
                        except Exception:
                            continue
                        copy = asset_tools.create_asset(f"MI_Sage_{source.get_name()}", f"{GAME}/Materials",
                                                        unreal.MaterialInstanceConstant, unreal.MaterialInstanceConstantFactoryNew())
                        mel.set_material_instance_parent(copy, source)
                        mel.set_material_instance_vector_parameter_value(copy, "BaseColorFactor", unreal.LinearColor(
                            factor.r * sage_tint.r, factor.g * sage_tint.g, factor.b * sage_tint.b, factor.a))
                        mel.update_material_instance(copy)
                        eal.save_loaded_asset(copy)
                        tinted[source.get_name()] = copy
                    component.set_material(slot, tinted[source.get_name()])
        log(f"sagebrush: {len(extra['sage'])} bushes, {len(tinted)} tinted material(s)")

    wind_on_plants()

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
                if "/M_Decal_" in path:
                    continue   # decals are neither Nanite nor instanced (the rain's streaks are instanced since 0.76)
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
    # The chase camera where CameraRig settles it (0.11): 1.7 m over the ground and 7.4 m behind the car, looking at a
    # point 0.7 m up and 2 m ahead through a 54 degree lens. The car's origin is its centre of gravity, 0.61 m over the flat start.
    car = placements["car"]
    origin, (bx, by, bz) = car["position"], car["basis"]
    cg_height = 0.61

    def local(x, y, z):
        return [origin[i] + bx[i] * x + by[i] * y + bz[i] * z for i in range(3)]

    eye = local(0.0, 1.7 - cg_height, 7.4)
    focus = local(0.0, 0.7 - cg_height, -2.0)
    chase = cine_camera("ChaseCamera", to_unreal(eye), direction([f - e for f, e in zip(focus, eye)]), 54.0)
    shots = {"Cockpit": cockpit, "Chase": chase}
    roadside = extra.get("roadside_camera")
    if roadside:
        shots["Roadside"] = cine_camera("RoadsideCamera", to_unreal(roadside["eye"]),
                                        direction([t - e for t, e in zip(roadside["target"], roadside["eye"])]), roadside["fov"])
    return shots


SUN_LUX = 75000.0   # clear-sky sun at about 43 degrees
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
        elif isinstance(actor, unreal.ExponentialHeightFog):
            # The template's fog hides anything a few kilometres out; the Godot version's mountain range stands
            # 3 to 6 km from the circuit and should only pick up a little of the air's blue, as it does there.
            fog = actor.get_component_by_class(unreal.ExponentialHeightFogComponent)
            for name, value in (("fog_density", 0.004), ("fog_height_falloff", 0.3), ("start_distance", 2000.0)):
                try:
                    fog.set_editor_property(name, value)
                except Exception as error:  # a renamed setting must not stop the build
                    log(f"fog {name}: {error}")
    volume = actor_sub.spawn_actor_from_class(unreal.PostProcessVolume, unreal.Vector(0, 0, 0))
    volume.set_actor_label("Look")
    volume.set_folder_path("GrudgeLap")
    volume.set_editor_property("unbound", True)
    settings = volume.get_editor_property("settings")
    for name, value in (("override_lumen_final_gather_quality", True), ("lumen_final_gather_quality", 2.0),
                        ("override_lumen_scene_lighting_quality", True), ("lumen_scene_lighting_quality", 2.0),
                        ("override_lumen_reflection_quality", True), ("lumen_reflection_quality", 2.0),
                        ("override_vignette_intensity", True), ("vignette_intensity", 0.35),
                        # The photographic finish (0.13): a little grain, a trace of the lens's colour fringing and
                        # flare, a touch more bloom. Each is what a camera adds to a real scene; none hides it.
                        ("override_film_grain_intensity", True), ("film_grain_intensity", 0.28),
                        ("override_scene_fringe_intensity", True), ("scene_fringe_intensity", 0.45),
                        ("override_lens_flare_intensity", True), ("lens_flare_intensity", 0.12),
                        ("override_bloom_intensity", True), ("bloom_intensity", 0.5),
                        ("override_auto_exposure_min_brightness", True), ("auto_exposure_min_brightness", EXPOSURE_EV100),
                        ("override_auto_exposure_max_brightness", True), ("auto_exposure_max_brightness", EXPOSURE_EV100),
                        # The default motion blur smeared the grass beside the road into streaks at 130 km/h; a
                        # trace of it keeps the sense of speed (the Godot version has none).
                        ("override_motion_blur_amount", True), ("motion_blur_amount", 0.25),
                        ("override_motion_blur_max", True), ("motion_blur_max", 2.0),
                        # Lumen's reflections on the front layer of glass: the windscreen and the windows reflect
                        # the sky, the road and the dashboard instead of only the sky's cube map.
                        ("override_lumen_front_layer_translucency_reflections", True),
                        ("lumen_front_layer_translucency_reflections", True)):
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
    weather_assets()
    dress_track(actors)
    materials = car_materials()
    car = actors.get("PlayerCar")
    if car is None:
        raise RuntimeError("the export has no PlayerCar")
    dress_car(car, materials)
    rig_car(car, "GLP.Car")
    # The other catalogue cars, in the catalogue's order (VehicleSpec.Catalogue): GLP.Car1 the V8, GLP.Car2 the hot
    # hatch, GLP.Car3 the five, GLP.Car4 the turbo hatch, GLP.Car5-7 the everyday cars (0.13), GLP.Car8-12 the five of
    # cars13 (the 458, the two Bravos, the Leon Cupra and the BRZ, which await their exports and models); a missing
    # export leaves its slot empty and the title screen skips it.
    for index, name in enumerate(["v8", "hothatch", "five", "turbohatch", "saloon", "estate", "fleet", "458", "bravohgt", "bravo16", "leoncupra", "brz", "clio"], start=1):
        other = import_car(name)
        if other is not None:
            dress_car(other, materials)
            rig_car(other, f"GLP.Car{index}")
    rig_world(actors)
    place_game(car, game_materials(materials["paint"]))
    scatter(placements, extra, actors)
    place_instanced(placements, actors)
    road_decals(placements)
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

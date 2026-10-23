"""Diagnostic: prints how a material's base colour is computed (expression tree), to compare a good and a bad one."""
import unreal

mel = unreal.MaterialEditingLibrary
eal = unreal.EditorAssetLibrary
unreal.AssetRegistryHelpers.get_asset_registry().search_all_assets(True)


def describe(expr):
    name = expr.get_class().get_name()
    extra = ""
    for prop in ("texture", "constant", "const_a", "const_b", "u_tiling", "v_tiling", "sampler_type", "r"):
        try:
            value = expr.get_editor_property(prop)
            extra += f" {prop}={value.get_name() if hasattr(value, 'get_name') else value}"
        except Exception:
            pass
    return name + extra


def walk(material, expr, depth, seen):
    if expr is None or depth > 8:
        return
    unreal.log_warning("[graph] " + "  " * depth + describe(expr))
    for child in mel.get_inputs_for_material_expression(material, expr):
        if child is not None:
            walk(material, child, depth + 1, seen)


for path in ("/Game/GrudgeLap/Materials/M_Ground", "/Game/GrudgeLap/Materials/M_Asphalt"):
    material = eal.load_asset(path)
    unreal.log_warning(f"[graph] === {path}: {mel.get_num_material_expressions(material)} expressions, usage nanite {mel.has_material_usage(material, unreal.MaterialUsage.MATUSAGE_NANITE)}")
    walk(material, mel.get_material_property_input_node(material, unreal.MaterialProperty.MP_BASE_COLOR), 0, set())
    unreal.log_warning("[graph] roughness <- " + str(mel.get_material_property_input_node(material, unreal.MaterialProperty.MP_ROUGHNESS)))

"""Diagnostic: parameter values of some imported track material instances after the vertex-colour reparenting."""
import unreal

mel = unreal.MaterialEditingLibrary
eal = unreal.EditorAssetLibrary
unreal.AssetRegistryHelpers.get_asset_registry().search_all_assets(True)
for index in (75, 78, 71, 72, 70):
    path = f"/Game/Track/track_ue/Materials/track_ue_material_{index}"
    mi = eal.load_asset(path)
    parent = mi.get_editor_property("parent")
    vectors = {str(n): mel.get_material_instance_vector_parameter_value(mi, n) for n in mel.get_vector_parameter_names(mi)}
    scalars = {str(n): round(mel.get_material_instance_scalar_parameter_value(mi, n), 3) for n in mel.get_scalar_parameter_names(mi)}
    textures = {str(n): (mel.get_material_instance_texture_parameter_value(mi, n).get_name() if mel.get_material_instance_texture_parameter_value(mi, n) else None) for n in mel.get_texture_parameter_names(mi)}
    switches = {str(n): mel.get_material_instance_static_switch_parameter_value(mi, n) for n in mel.get_static_switch_parameter_names(mi)}
    overrides = [str(p.parameter_info.name) for p in mi.get_editor_property("vector_parameter_values")]
    unreal.log_warning(f"[params] #{index} parent={parent.get_name()} overridden vectors={overrides}")
    unreal.log_warning(f"[params]   vectors={ {k: (round(v.r, 2), round(v.g, 2), round(v.b, 2)) for k, v in vectors.items() if 'Color' in k or 'Factor' in k} }")
    unreal.log_warning(f"[params]   textures={ {k: v for k, v in textures.items() if v} } switches={ {k: v for k, v in switches.items() if v} }")

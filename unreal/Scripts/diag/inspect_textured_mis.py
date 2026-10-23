"""Diagnostic: prints the parameter names and values of the imported material instances that carry a texture (to
find the cabin's leather, fabric and carpet, which Godot mapped without UVs)."""
import unreal

mel = unreal.MaterialEditingLibrary
eal = unreal.EditorAssetLibrary
unreal.AssetRegistryHelpers.get_asset_registry().search_all_assets(True)

for root in ("/Game/Track", "/Game/Cars"):
    for path in eal.list_assets(root, recursive=True):
        data = eal.find_asset_data(path)
        if str(data.asset_class_path.asset_name) != "MaterialInstanceConstant":
            continue
        mi = eal.load_asset(path)
        textures = {}
        for name in mel.get_texture_parameter_names(mi):
            value = mel.get_material_instance_texture_parameter_value(mi, name)
            if value is not None:
                textures[str(name)] = value.get_name()
        if not any(t for t in textures.values() if not t.startswith("Default")):
            continue
        vectors = {str(n): mel.get_material_instance_vector_parameter_value(mi, n) for n in mel.get_vector_parameter_names(mi)}
        scalars = {str(n): round(mel.get_material_instance_scalar_parameter_value(mi, n), 3) for n in mel.get_scalar_parameter_names(mi)}
        unreal.log_warning(f"[mi] {path} parent={mi.get_editor_property('parent').get_name()} textures={textures} "
                           f"base={vectors.get('BaseColorFactor')} scalars={scalars}")

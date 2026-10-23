"""Diagnostic: the tree meshes' sections, materials (with blend mode) and Nanite settings."""
import unreal

eal = unreal.EditorAssetLibrary
unreal.AssetRegistryHelpers.get_asset_registry().search_all_assets(True)
for name in ("jacaranda_tree", "tree_small_02", "island_tree_02", "grass_medium_01"):
    for path in eal.list_assets(f"/Game/GrudgeLap/PolyHaven/{name}", recursive=True):
        data = eal.find_asset_data(path)
        kind = str(data.asset_class_path.asset_name)
        asset = eal.load_asset(path)
        if kind == "StaticMesh":
            nanite = asset.get_editor_property("nanite_settings")
            slots = asset.get_editor_property("static_materials")
            unreal.log_warning(f"[trees] {name} mesh {asset.get_name()}: nanite={nanite.enabled} shape={nanite.shape_preservation} "
                               f"tris={asset.get_num_triangles(0)} sections={asset.get_num_sections(0)} slots={[s.material_slot_name for s in slots]}")
            for s in slots:
                m = s.material_interface
                base = m.get_base_material() if m else None
                unreal.log_warning(f"[trees]    slot {s.material_slot_name}: {m.get_name() if m else None} base {base.get_name() if base else None} blend {base.get_editor_property('blend_mode') if base else None} "
                                   f"overrides={m.get_editor_property('base_property_overrides') if m and hasattr(m, 'get_editor_property') and isinstance(m, unreal.MaterialInstanceConstant) else None}")
        elif kind == "Texture2D" and "alpha" in path:
            unreal.log_warning(f"[trees] {name} texture {asset.get_name()}: compression={asset.get_editor_property('compression_settings')} srgb={asset.get_editor_property('srgb')} "
                               f"coverage={asset.get_editor_property('do_scale_mips_for_alpha_coverage')} size={asset.blueprint_get_size_x()}x{asset.blueprint_get_size_y()} has_alpha={asset.get_editor_property('compression_no_alpha') is False}")
        if name == "grass_medium_01" and kind == "StaticMesh":
            break

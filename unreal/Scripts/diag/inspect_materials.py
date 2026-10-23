"""Diagnostic: which parents the imported material instances use, and how a parent's base colour is wired."""
import collections
import unreal

eal = unreal.EditorAssetLibrary
mel = unreal.MaterialEditingLibrary
unreal.AssetRegistryHelpers.get_asset_registry().search_all_assets(True)
parents = collections.Counter()
for root in ("/Game/Track", "/Game/GrudgeLap/PolyHaven"):
    for path in eal.list_assets(root, recursive=True):
        if str(eal.find_asset_data(path).asset_class_path.asset_name) == "MaterialInstanceConstant":
            mi = eal.load_asset(path)
            parents[(root, mi.get_editor_property("parent").get_path_name(), mi.get_base_material().get_path_name())] += 1
for (root, parent, base), n in parents.most_common():
    unreal.log_warning(f"[inspect] {root}: {n} x parent {parent} base {base}")
for base in sorted({b for (_, _, b) in parents}):
    material = eal.load_asset(base)
    node = mel.get_material_property_input_node(material, unreal.MaterialProperty.MP_BASE_COLOR)
    out = mel.get_material_property_input_node_output_name(material, unreal.MaterialProperty.MP_BASE_COLOR)
    unreal.log_warning(f"[inspect] base {base}: BaseColor <- {node.get_class().get_name() if node else None} '{out}', expressions {mel.get_num_material_expressions(material)}")

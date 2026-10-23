"""Diagnostic: lists the inputs and outputs of an engine material function (LocalAlignedTexture by default)."""
import unreal

unreal.AssetRegistryHelpers.get_asset_registry().search_all_assets(True)
path = "/Engine/Functions/Engine_MaterialFunctions01/Texturing/LocalAlignedTexture.LocalAlignedTexture"
function = unreal.load_object(None, path)
unreal.log_warning(f"[fn] {path}: {function}")
for expression in unreal.MaterialEditingLibrary.get_material_function_expressions(function) if hasattr(unreal.MaterialEditingLibrary, "get_material_function_expressions") else []:
    name = expression.get_class().get_name()
    if name in ("MaterialExpressionFunctionInput", "MaterialExpressionFunctionOutput"):
        props = {}
        for prop in ("input_name", "output_name", "input_type", "sort_priority", "use_preview_value_as_default", "preview_value"):
            try:
                props[prop] = expression.get_editor_property(prop)
            except Exception:
                pass
        unreal.log_warning(f"[fn] {name} {props}")

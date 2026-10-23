"""Diagnostic: lists the player car's mesh actors with their material and its glTF factors, to find parts by look
(the export names most of them only by number)."""
import unreal

mel = unreal.MaterialEditingLibrary
actor_sub = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
level_sub = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
unreal.AssetRegistryHelpers.get_asset_registry().search_all_assets(True)
level_sub.load_level("/Game/Maps/Feasibility")


def descendants(actor):
    for child in actor.get_attached_actors():
        yield child
        yield from descendants(child)


def factors(material):
    out = []
    if isinstance(material, unreal.MaterialInstance):
        for name in ("MetallicFactor", "RoughnessFactor", "EmissiveStrength"):
            try:
                out.append(f"{name}={mel.get_material_instance_scalar_parameter_value(material, name):.2f}")
            except Exception:
                pass
        for name in ("BaseColorFactor", "EmissiveFactor"):
            try:
                c = mel.get_material_instance_vector_parameter_value(material, name)
                out.append(f"{name}=({c.r:.2f},{c.g:.2f},{c.b:.2f})")
            except Exception:
                pass
    return " ".join(out)


for car in [a for a in actor_sub.get_all_level_actors() if a.get_actor_label() in ("PlayerCar", "PlayerCar_v8")]:
    for actor in descendants(car):
        for component in actor.get_components_by_class(unreal.StaticMeshComponent):
            material = component.get_material(0)
            bounds = component.get_editor_property("static_mesh").get_bounding_box() if component.get_editor_property("static_mesh") else None
            size = (bounds.max - bounds.min) if bounds else None
            unreal.log_warning(f"[car] {car.get_actor_label()} / {actor.get_attach_parent_actor().get_actor_label() if actor.get_attach_parent_actor() else '-'} / "
                               f"{actor.get_actor_label()}: {material.get_name() if material else None} {factors(material) if material else ''} "
                               f"size {size.x:.0f}x{size.y:.0f}x{size.z:.0f}" if size else "")

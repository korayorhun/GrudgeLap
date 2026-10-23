"""Diagnostic: the saved level's sun, sky light, atmosphere and fog settings."""
import unreal

unreal.AssetRegistryHelpers.get_asset_registry().search_all_assets(True)
unreal.get_editor_subsystem(unreal.LevelEditorSubsystem).load_level("/Game/Maps/Feasibility")
for actor in unreal.get_editor_subsystem(unreal.EditorActorSubsystem).get_all_level_actors():
    cls = actor.get_class().get_name()
    if cls in ("DirectionalLight", "SkyLight", "SkyAtmosphere", "ExponentialHeightFog", "VolumetricCloud", "PostProcessVolume"):
        comp = actor.root_component
        props = {}
        for name in ("intensity", "light_color", "atmosphere_sun_light", "real_time_capture", "source_type", "lower_hemisphere_is_black",
                     "lower_hemisphere_color", "cast_shadows", "mobility", "fog_density", "fog_height_falloff", "volumetric_fog",
                     "indirect_lighting_intensity", "sky_distance_threshold", "cubemap", "source_angle"):
            try:
                value = comp.get_editor_property(name)
                props[name] = value.get_name() if hasattr(value, "get_name") else value
            except Exception:
                pass
        unreal.log_warning(f"[lights] {cls} {actor.get_actor_label()} rot={actor.get_actor_rotation()} {props}")

using UnrealBuildTool;

public class GrudgeLapUEEditorTarget : TargetRules
{
	public GrudgeLapUEEditorTarget(TargetInfo Target) : base(Target)
	{
		Type = TargetType.Editor;
		DefaultBuildSettings = BuildSettingsVersion.Latest;
		IncludeOrderVersion = EngineIncludeOrderVersion.Latest;
		ExtraModuleNames.Add("GrudgeLapUE");
	}
}

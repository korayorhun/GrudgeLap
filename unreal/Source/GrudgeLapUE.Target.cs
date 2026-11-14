using UnrealBuildTool;

public class GrudgeLapUETarget : TargetRules
{
	public GrudgeLapUETarget(TargetInfo Target) : base(Target)
	{
		Type = TargetType.Game;
		DefaultBuildSettings = BuildSettingsVersion.Latest;
		IncludeOrderVersion = EngineIncludeOrderVersion.Latest;
		ExtraModuleNames.Add("GrudgeLapUE");

		// The game opens no network port. A Development build of the engine listens on TCP port 1985 (or, when that
		// is taken, on a random one), on every address, for Unreal Insights to steer its tracing (TraceLog's
		// Control.cpp), and Windows Firewall asks about a program that listens the first time it runs from a folder:
		// every player would be asked on the first start, and every fresh test folder asked again. The engine has a
		// compile-time switch for the listener and none at run time. A definition for the engine's own modules needs
		// the game's own build of the engine: that is what a monolithic target has with an engine built from source
		// (a unique build environment, the default there), and the first build after this line changes compiles the
		// whole engine again (over an hour on the development laptop). With an installed engine the environment is
		// shared with the engine's stock game target and the line is left out: such a build still listens.
		// Tracing to a file (-trace=... -tracefile=...) works as before; only the listener is gone.
		if (BuildEnvironment == TargetBuildEnvironment.Unique)
		{
			GlobalDefinitions.Add("UE_TRACE_ALLOW_TCP_CONTROL=0");
		}
	}
}

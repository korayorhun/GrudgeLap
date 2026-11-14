using System;
using System.IO;
using UnrealBuildTool;

public class GrudgeLapUE : ModuleRules
{
	public GrudgeLapUE(ReadOnlyTargetRules Target) : base(Target)
	{
		PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
		PublicDependencyModuleNames.AddRange(new[] { "Core", "CoreUObject", "Engine", "InputCore" });
		// EngineSettings: the project's version (GeneralProjectSettings), which is the game's.
		PrivateDependencyModuleNames.AddRange(new[] { "AudioMixer", "EngineSettings", "RenderCore", "Slate", "SlateCore" });

		// NVIDIA's DLSS plugin, when it is installed (README step 4): the game asks it whether the card can run Ray
		// Reconstruction and switches it on (AGlpGame::BeginPlay). Without the plugin the game builds and runs on TSR.
		bool dlss = Directory.Exists(Path.Combine(ModuleDirectory, "..", "..", "Plugins", "DLSS"));
		if (dlss)
		{
			PrivateDependencyModuleNames.Add("DLSSBlueprint");
		}
		PrivateDefinitions.Add("WITH_GLP_DLSS=" + (dlss ? "1" : "0"));
		// The engine's Ogg Vorbis decoder: the player's replay tune is kept as replay.ogg by the Godot version (GlpAudio).
		PrivateDependencyModuleNames.AddRange(new[] { "AudioExtensions", "VorbisAudioDecoder" });
		// FSoundQualityInfo, which the decoder fills in, is declared in TargetPlatform's Interfaces/IAudioFormat.h (a header only).
		PrivateIncludePathModuleNames.Add("TargetPlatform");

		// The simulation core: the repository's physics.native, the C# physics built with NativeAOT. Only its C
		// header is compiled against; the DLL is loaded at run time (GlpLibrary) and staged beside the binaries
		// from where `dotnet publish physics.native -c Release -r win-x64 -o artifacts/physics-native` leaves it.
		string repository = Path.GetFullPath(Path.Combine(ModuleDirectory, "..", "..", ".."));
		PublicIncludePaths.Add(Path.Combine(repository, "physics.native", "include"));
		if (Target.Platform == UnrealTargetPlatform.Win64)
		{
			string library = Path.Combine(repository, "artifacts", "physics-native", "GrudgeLap.Physics.Native.dll");
			if (!File.Exists(library))
			{
				Console.WriteLine($"warning: {library} is missing; publish physics.native first (see unreal/README.md)");
			}
			RuntimeDependencies.Add("$(BinaryOutputDir)/GrudgeLap.Physics.Native.dll", library);
		}

		// The updater (0.78): the repository's update.native, the Godot version's updater built with NativeAOT behind a C
		// interface, loaded at run time (GlpUpdate) and staged like the simulation core, from where `dotnet publish
		// update.native -c Release -r win-x64 -o artifacts/update-native` leaves it. Without it the game builds and runs,
		// and its menu says that it cannot update itself.
		PublicIncludePaths.Add(Path.Combine(repository, "update.native", "include"));
		if (Target.Platform == UnrealTargetPlatform.Win64)
		{
			string updater = Path.Combine(repository, "artifacts", "update-native", "GrudgeLap.Update.Native.dll");
			if (File.Exists(updater))
			{
				RuntimeDependencies.Add("$(BinaryOutputDir)/GrudgeLap.Update.Native.dll", updater);
			}
			else
			{
				Console.WriteLine($"warning: {updater} is missing; publish update.native first (see unreal/README.md)");
			}
		}

		// The Godot version's recordings (game/assets/audio, CC0, see SOURCES.md there) and the engine's grain marks
		// beside them, read at run time by GlpAudio and the DLL from beside the binaries so the packaged game carries
		// them too.
		string audio = Path.Combine(repository, "game", "assets", "audio");
		if (Directory.Exists(audio))
		{
			foreach (string pattern in new[] { "*.wav", "*.json" })
			{
				foreach (string file in Directory.EnumerateFiles(audio, pattern, SearchOption.AllDirectories))
				{
					string relative = Path.GetRelativePath(audio, file).Replace(Path.DirectorySeparatorChar, '/');
					RuntimeDependencies.Add("$(BinaryOutputDir)/GrudgeLapAudio/" + relative, file);
				}
			}
		}
	}
}

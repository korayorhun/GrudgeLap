using System;
using System.IO;
using UnrealBuildTool;

public class GrudgeLapUE : ModuleRules
{
	public GrudgeLapUE(ReadOnlyTargetRules Target) : base(Target)
	{
		PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
		PublicDependencyModuleNames.AddRange(new[] { "Core", "CoreUObject", "Engine", "InputCore" });
		PrivateDependencyModuleNames.AddRange(new[] { "AudioMixer", "RenderCore", "Slate", "SlateCore" });

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

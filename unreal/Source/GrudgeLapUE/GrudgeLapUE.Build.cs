using System;
using System.IO;
using UnrealBuildTool;

public class GrudgeLapUE : ModuleRules
{
	public GrudgeLapUE(ReadOnlyTargetRules Target) : base(Target)
	{
		PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
		PublicDependencyModuleNames.AddRange(new[] { "Core", "CoreUObject", "Engine", "InputCore" });

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
	}
}

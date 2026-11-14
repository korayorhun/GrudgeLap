#include "GlpInstance.h"

#include "Engine/Engine.h"
#include "Engine/GameViewportClient.h"
#include "GenericPlatform/GenericWindow.h"
#include "HAL/FileManager.h"
#include "HAL/PlatformMisc.h"
#include "HAL/PlatformProcess.h"
#include "Misc/CommandLine.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Widgets/SWindow.h"

#if PLATFORM_WINDOWS
#include "Windows/AllowWindowsPlatformTypes.h"
#include "Windows/WindowsHWrapper.h"
#include "Windows/HideWindowsPlatformTypes.h"
#endif

DEFINE_LOG_CATEGORY_STATIC(LogGlpInstance, Log, All);

namespace
{
#if PLATFORM_WINDOWS
	// The Godot version's names (SingleInstance.cs): a copy of either version sees the other's.
	const TCHAR* const LockName = TEXT("Global\\GrudgeLap.SingleInstance");
	const TCHAR* const CallName = TEXT("Global\\GrudgeLap.Activate");
	HANDLE Lock = nullptr;
	HANDLE Call = nullptr;
#endif

	// VehicleSpec.Catalogue's body styles in its order (CarStyle): the Godot version names a car's best lap by its
	// style (bestlap-<style>.bin), this one by its place in the catalogue (best_lap_<index>.bin).
	const TCHAR* const Styles[] = { TEXT("FlatTwelve"), TEXT("V8"), TEXT("HotHatch"), TEXT("FiveHatch"), TEXT("TurboHatch"), TEXT("Saloon"),
		TEXT("Estate"), TEXT("Fleet"), TEXT("FlatPlaneV8"), TEXT("HgtFive"), TEXT("HatchSixteen"), TEXT("CupraTsi"), TEXT("Boxer"), TEXT("ClioSixteen") };
}

bool GlpInstance::Packaged()
{
#if WITH_EDITOR
	return false;
#else
	return true;
#endif
}

bool GlpInstance::Claim()
{
#if PLATFORM_WINDOWS
	if (!Packaged() || Lock || FParse::Param(FCommandLine::Get(), TEXT("RenderOffScreen")))
	{
		return true;
	}
	Lock = ::CreateMutexW(nullptr, 0, LockName);
	if (Lock && ::GetLastError() == ERROR_ALREADY_EXISTS)
	{
		// A copy runs: it is asked to come forward, and may take the foreground from this process, which the player
		// has just started and Windows therefore lets pass it on.
		::CloseHandle(Lock);
		Lock = nullptr;
		if (HANDLE Running = ::OpenEventW(EVENT_MODIFY_STATE, 0, CallName))
		{
			::AllowSetForegroundWindow(ASFW_ANY);
			::SetEvent(Running);
			::CloseHandle(Running);
		}
		return false;
	}
	// Auto-reset: one call brings the window forward once.
	Call = ::CreateEventW(nullptr, 0, 0, CallName);
	UE_LOG(LogGlpInstance, Log, TEXT("one-copy lock %s"), Lock ? TEXT("taken") : TEXT("could not be made: the game runs without it"));
#endif
	return true;
}

bool GlpInstance::Summoned()
{
#if PLATFORM_WINDOWS
	return Call && ::WaitForSingleObject(Call, 0) == WAIT_OBJECT_0;
#else
	return false;
#endif
}

void GlpInstance::ComeForward()
{
#if PLATFORM_WINDOWS
	if (!GEngine || !GEngine->GameViewport)
	{
		return;
	}
	const TSharedPtr<SWindow> Window = GEngine->GameViewport->GetWindow();
	const TSharedPtr<FGenericWindow> Native = Window.IsValid() ? Window->GetNativeWindow() : nullptr;
	if (Native.IsValid() && Native->GetOSWindowHandle())
	{
		const HWND Handle = static_cast<HWND>(Native->GetOSWindowHandle());
		if (::IsIconic(Handle))
		{
			::ShowWindow(Handle, SW_RESTORE);
		}
		::SetForegroundWindow(Handle);
	}
#endif
}

const FString& GlpInstance::DataDir()
{
	static const FString Directory = []
	{
		FString Named;
		if (FParse::Value(FCommandLine::Get(), TEXT("GlpData="), Named, false) && !Named.IsEmpty())
		{
			return FPaths::ConvertRelativePathToFull(Named);
		}
		if (Packaged())
		{
			return FPaths::Combine(FPlatformProcess::UserSettingsDir(), TEXT("GrudgeLap"));
		}
		return FPaths::ConvertRelativePathToFull(FPaths::Combine(FPaths::ProjectSavedDir(), TEXT("GrudgeLap")));
	}();
	return Directory;
}

void GlpInstance::Tidy()
{
	FString Root;
	if (!FParse::Value(FCommandLine::Get(), TEXT("GlpTidy="), Root, false) || Root.IsEmpty())
	{
		if (!Packaged())
		{
			return;
		}
		Root = FPaths::RootDir();
	}
	Root = FPaths::ConvertRelativePathToFull(Root);
	IFileManager& Files = IFileManager::Get();
	// The Godot game's pack tells its files: without it this is not such a folder, and nothing in it is touched.
	const FString Pack = FPaths::Combine(Root, TEXT("GrudgeLap.pck"));
	if (!Files.FileExists(*Pack))
	{
		return;
	}
	const FString Console = FPaths::Combine(Root, TEXT("GrudgeLap.console.exe"));
	const FString Data = FPaths::Combine(Root, TEXT("data_GrudgeLap_windows_x86_64"));
	// The pack last: while something of the rest is still held (an antivirus that looks at it), the next start finds
	// the pack and tries again.
	bool bCleared = !Files.DirectoryExists(*Data) || Files.DeleteDirectory(*Data, false, true);
	bCleared &= !Files.FileExists(*Console) || Files.Delete(*Console, false, true, true);
	bCleared = bCleared && Files.Delete(*Pack, false, true, true);
	UE_LOG(LogGlpInstance, Log, TEXT("the Godot version's files in %s: %s"), *Root,
		bCleared ? TEXT("cleared") : TEXT("not all could be cleared; tried again at the next start"));
}

void GlpInstance::Import()
{
	if (!Packaged() && !FParse::Param(FCommandLine::Get(), TEXT("GlpImport")))
	{
		return;
	}
	IFileManager& Files = IFileManager::Get();
	const FString& Target = DataDir();
	const FString Marker = FPaths::Combine(Target, TEXT("imported.txt"));
	if (Files.FileExists(*Marker))
	{
		return;
	}
	Files.MakeDirectory(*Target, true);
	TArray<FString> Taken;
	// Never over a file that is already here: the player's own of this version wins.
	auto Take = [&Files, &Target, &Taken](const FString& From, const FString& As)
	{
		const FString To = FPaths::Combine(Target, As);
		if (Files.FileExists(*From) && !Files.FileExists(*To))
		{
			Files.MakeDirectory(*FPaths::GetPath(To), true);
			if (Files.Copy(*To, *From, false) == COPY_OK)
			{
				Taken.Add(FString::Printf(TEXT("%s <- %s"), *As, *From));
			}
		}
	};

	// The previews (0.44 to 0.77) kept their files in the game's own Saved folder.
	const FString Preview = FPaths::ConvertRelativePathToFull(FPaths::Combine(FPaths::ProjectSavedDir(), TEXT("GrudgeLap")));
	if (!FPaths::IsSamePath(Preview, Target) && Files.DirectoryExists(*Preview))
	{
		TArray<FString> Names;
		Files.FindFilesRecursive(Names, *Preview, TEXT("*.*"), true, false);
		for (const FString& Name : Names)
		{
			FString Relative = Name;
			FPaths::MakePathRelativeTo(Relative, *(Preview + TEXT("/")));
			Take(Name, Relative);
		}
	}

	// The Godot version (to 0.77): the high scores and the best laps are the simulation core's own files in both versions.
	FString Godot = FPaths::Combine(FPlatformMisc::GetEnvironmentVariable(TEXT("APPDATA")), TEXT("Godot"), TEXT("app_userdata"), TEXT("GrudgeLap"));
	FParse::Value(FCommandLine::Get(), TEXT("GlpGodotData="), Godot, false);
	if (Files.DirectoryExists(*Godot))
	{
		Take(FPaths::Combine(Godot, TEXT("scores.txt")), TEXT("scores.txt"));
		for (int32 i = 0; i < UE_ARRAY_COUNT(Styles); i++)
		{
			Take(FPaths::Combine(Godot, FString::Printf(TEXT("bestlap-%s.bin"), Styles[i])), FString::Printf(TEXT("best_lap_%d.bin"), i));
		}
		// Its choices: settings.cfg's [game] section holds the same numbers this version's settings.ini does (the car's
		// place in the catalogue, the gearbox, the weather, the hour), and units as 1 for km/h.
		const FString Settings = FPaths::Combine(Target, TEXT("settings.ini"));
		FString Text;
		if (!Files.FileExists(*Settings) && FFileHelper::LoadFileToString(Text, *FPaths::Combine(Godot, TEXT("settings.cfg"))))
		{
			TArray<FString> Lines;
			Text.ParseIntoArrayLines(Lines);
			bool bGame = false;
			FString Out;
			for (const FString& Raw : Lines)
			{
				const FString Line = Raw.TrimStartAndEnd();
				if (Line.StartsWith(TEXT("[")))
				{
					bGame = Line == TEXT("[game]");
					continue;
				}
				FString Key, Value;
				if (!bGame || !Line.Split(TEXT("="), &Key, &Value))
				{
					continue;
				}
				Key.TrimStartAndEndInline();
				Value.TrimStartAndEndInline();
				if (Key == TEXT("car") || Key == TEXT("transmission") || Key == TEXT("weather") || Key == TEXT("light"))
				{
					Out += FString::Printf(TEXT("%s=%d\n"), *Key, FCString::Atoi(*Value));
				}
				else if (Key == TEXT("units"))
				{
					Out += FString::Printf(TEXT("units=%s\n"), FCString::Atoi(*Value) != 0 ? TEXT("kmh") : TEXT("mph"));
				}
			}
			if (!Out.IsEmpty() && FFileHelper::SaveStringToFile(Out, *Settings))
			{
				Taken.Add(FString::Printf(TEXT("settings.ini <- %s"), *FPaths::Combine(Godot, TEXT("settings.cfg"))));
			}
		}
	}

	FString Report = TEXT("GrudgeLap took over what the earlier versions kept, once:\n");
	for (const FString& Line : Taken)
	{
		Report += Line + TEXT("\n");
		UE_LOG(LogGlpInstance, Log, TEXT("taken over: %s"), *Line);
	}
	if (Taken.IsEmpty())
	{
		Report += TEXT("(nothing was found)\n");
	}
	FFileHelper::SaveStringToFile(Report, *Marker);
}

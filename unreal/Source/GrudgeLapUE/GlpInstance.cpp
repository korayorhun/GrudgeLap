#include "GlpInstance.h"

#include "Engine/Engine.h"
#include "Engine/GameViewportClient.h"
#include "GenericPlatform/GenericWindow.h"
#include "GlpPaint.h"
#include "GlpStyles.h"
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

	/** The keys and values of settings.cfg's [game] section, the Godot version's choices; a string's quotes taken off. */
	TMap<FString, FString> GodotChoices(const FString& File)
	{
		TMap<FString, FString> Choices;
		FString Text;
		if (!FFileHelper::LoadFileToString(Text, *File))
		{
			return Choices;
		}
		TArray<FString> Lines;
		Text.ParseIntoArrayLines(Lines);
		bool bGame = false;
		for (const FString& Raw : Lines)
		{
			const FString Line = Raw.TrimStartAndEnd();
			if (Line.StartsWith(TEXT("[")))
			{
				bGame = Line == TEXT("[game]");
				continue;
			}
			FString Key, Value;
			if (bGame && Line.Split(TEXT("="), &Key, &Value))
			{
				Choices.Add(Key.TrimStartAndEnd(), Value.TrimStartAndEnd().TrimQuotes());
			}
		}
		return Choices;
	}

	/**
	 * The Godot version's paints (0.79), once: its settings.cfg keeps each car's as paint.<style>="<paint>", the text
	 * this version's settings.ini keeps under the same key (FGlpPaintChoice), so a car the player had painted there
	 * stands in the same paint here. A paint already chosen in this version wins. The step has its own marker:
	 * imported.txt is already on every computer that ran 0.78, which had no paints to take them into.
	 */
	void TakePaints(const FString& Godot, const FString& Target)
	{
		IFileManager& Files = IFileManager::Get();
		const FString Marker = FPaths::Combine(Target, TEXT("imported-paints.txt"));
		if (Files.FileExists(*Marker))
		{
			return;
		}
		Files.MakeDirectory(*Target, true);
		const FString Settings = FPaths::Combine(Target, TEXT("settings.ini"));
		FString Kept;
		FFileHelper::LoadFileToString(Kept, *Settings);
		TArray<FString> Lines;
		Kept.ParseIntoArrayLines(Lines);
		const TMap<FString, FString> Choices = GodotChoices(FPaths::Combine(Godot, TEXT("settings.cfg")));
		FString Report = TEXT("GrudgeLap took over the paints the Godot version kept, once:\n");
		FString Added;
		for (int32 Car = 0; Car < GlpStyles::Count; Car++)
		{
			const FString Key = GlpPaint::Key(Car);
			const FString* Text = Choices.Find(Key);
			FGlpPaintChoice Choice;
			if (!Text || !FGlpPaintChoice::TryParse(*Text, Choice) || Choice.IsOriginal()
				|| Lines.ContainsByPredicate([&Key](const FString& Line) { return Line.StartsWith(Key + TEXT("=")); }))
			{
				continue;
			}
			Added += FString::Printf(TEXT("%s=%s\n"), *Key, *Choice.Format());
			Report += FString::Printf(TEXT("%s=%s\n"), *Key, *Choice.Format());
			UE_LOG(LogGlpInstance, Log, TEXT("taken over: %s=%s"), *Key, *Choice.Format());
		}
		if (Added.IsEmpty())
		{
			Report += TEXT("(nothing was found)\n");
		}
		else if (!FFileHelper::SaveStringToFile((Kept.IsEmpty() || Kept.EndsWith(TEXT("\n")) ? Kept : Kept + TEXT("\n")) + Added, *Settings))
		{
			// Not written: tried again at the next start.
			return;
		}
		FFileHelper::SaveStringToFile(Report, *Marker);
	}
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
	// The Godot version's folder (to 0.77), or a check's own.
	FString Godot = FPaths::Combine(FPlatformMisc::GetEnvironmentVariable(TEXT("APPDATA")), TEXT("Godot"), TEXT("app_userdata"), TEXT("GrudgeLap"));
	FParse::Value(FCommandLine::Get(), TEXT("GlpGodotData="), Godot, false);
	const FString Marker = FPaths::Combine(Target, TEXT("imported.txt"));
	if (Files.FileExists(*Marker))
	{
		// The files came over with 0.78; the paints (0.79) may still be owed.
		TakePaints(Godot, Target);
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
	if (Files.DirectoryExists(*Godot))
	{
		Take(FPaths::Combine(Godot, TEXT("scores.txt")), TEXT("scores.txt"));
		// The Godot version names a car's best lap by its style (bestlap-<style>.bin), this one by its place in the
		// catalogue (best_lap_<index>.bin).
		for (int32 i = 0; i < GlpStyles::Count; i++)
		{
			Take(FPaths::Combine(Godot, FString::Printf(TEXT("bestlap-%s.bin"), GlpStyles::Names[i])), FString::Printf(TEXT("best_lap_%d.bin"), i));
		}
		// Its choices: settings.cfg's [game] section holds the same numbers this version's settings.ini does (the car's
		// place in the catalogue, the gearbox, the weather, the hour), and units as 1 for km/h.
		const FString Settings = FPaths::Combine(Target, TEXT("settings.ini"));
		if (!Files.FileExists(*Settings))
		{
			FString Out;
			for (const TPair<FString, FString>& Choice : GodotChoices(FPaths::Combine(Godot, TEXT("settings.cfg"))))
			{
				if (Choice.Key == TEXT("car") || Choice.Key == TEXT("transmission") || Choice.Key == TEXT("weather") || Choice.Key == TEXT("light"))
				{
					Out += FString::Printf(TEXT("%s=%d\n"), *Choice.Key, FCString::Atoi(*Choice.Value));
				}
				else if (Choice.Key == TEXT("units"))
				{
					Out += FString::Printf(TEXT("units=%s\n"), FCString::Atoi(*Choice.Value) != 0 ? TEXT("kmh") : TEXT("mph"));
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
	// After the settings: the paints are added to the file the choices above may just have made.
	TakePaints(Godot, Target);
}

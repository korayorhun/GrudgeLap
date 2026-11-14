#include "GlpUpdate.h"

#include "GlpInstance.h"
#include "HAL/FileManager.h"
#include "HAL/PlatformProcess.h"
#include "Misc/CommandLine.h"
#include "Misc/Paths.h"

DEFINE_LOG_CATEGORY_STATIC(LogGlpUpdate, Log, All);

void* FGlpUpdate::Handle = nullptr;
int32 (*FGlpUpdate::Abi)() = nullptr;
int32 (*FGlpUpdate::Init)(const char*, const char*, const char*, const char*) = nullptr;
int32 (*FGlpUpdate::CheckNow)() = nullptr;
int32 (*FGlpUpdate::DownloadNow)() = nullptr;
int32 (*FGlpUpdate::ApplyNow)() = nullptr;
void (*FGlpUpdate::CancelNow)() = nullptr;
void (*FGlpUpdate::ShutdownNow)() = nullptr;
int32 (*FGlpUpdate::PollNow)(glp_update_state*) = nullptr;
int32 (*FGlpUpdate::LastErrorText)(char*, int32) = nullptr;

bool FGlpUpdate::Load()
{
	if (Handle)
	{
		return true;
	}
	// Staged beside the module's binary by Build.cs, as the simulation core is.
	const FString Path = FPaths::Combine(FPaths::ProjectDir(), TEXT("Binaries"), FPlatformProcess::GetBinariesSubdirectory(), TEXT("GrudgeLap.Update.Native.dll"));
	void* Library = FPlatformProcess::GetDllHandle(*Path);
	if (!Library)
	{
		Reason = TEXT("the updater's library is missing");
		UE_LOG(LogGlpUpdate, Warning, TEXT("updater not found: %s"), *Path);
		return false;
	}
	bool bComplete = true;
	auto Resolve = [Library, &bComplete](auto& Function, const TCHAR* Name)
	{
		Function = reinterpret_cast<std::remove_reference_t<decltype(Function)>>(FPlatformProcess::GetDllExport(Library, Name));
		if (!Function)
		{
			UE_LOG(LogGlpUpdate, Error, TEXT("the updater lacks %s"), Name);
			bComplete = false;
		}
	};
	Resolve(Abi, TEXT("glp_update_abi"));
	Resolve(Init, TEXT("glp_update_init"));
	Resolve(CheckNow, TEXT("glp_update_check"));
	Resolve(DownloadNow, TEXT("glp_update_download"));
	Resolve(ApplyNow, TEXT("glp_update_apply"));
	Resolve(CancelNow, TEXT("glp_update_cancel"));
	Resolve(ShutdownNow, TEXT("glp_update_shutdown"));
	Resolve(PollNow, TEXT("glp_update_poll"));
	Resolve(LastErrorText, TEXT("glp_update_last_error"));
	if (!bComplete || Abi() != GLP_UPDATE_ABI)
	{
		Reason = TEXT("the updater's library does not fit this game");
		UE_LOG(LogGlpUpdate, Error, TEXT("the updater speaks ABI %d, this build expects %d"), bComplete ? Abi() : -1, GLP_UPDATE_ABI);
		// Not unloaded (it cannot be), and not tried again.
		return false;
	}
	Handle = Library;
	return true;
}

bool FGlpUpdate::Start(const FString& Version, const FString& DataDir)
{
	bRunning = false;
	Current = glp_update_state{};
	StatusText.Reset();
	LatestText.Reset();
	PageText.Reset();
	// The root folder holds the launcher, the engine's folder and the game's: what the installer installs and a portable
	// zip is unpacked over. In the editor's build that would be the engine's own folder: never.
	FString Root;
	if (GlpInstance::Packaged())
	{
		Root = FPaths::ConvertRelativePathToFull(FPaths::RootDir());
	}
	else if (!FParse::Value(FCommandLine::Get(), TEXT("GlpUpdateRoot="), Root, false) || Root.IsEmpty())
	{
		Reason = TEXT("only the packaged game updates itself");
		return false;
	}
	FPaths::NormalizeDirectoryName(Root);
	FPaths::MakePlatformFilename(Root);
	FString Launcher = FPaths::Combine(Root, TEXT("GrudgeLap.exe"));
	FPaths::MakePlatformFilename(Launcher);
	FString Data = DataDir;
	FPaths::MakePlatformFilename(Data);
	if (!Load())
	{
		return false;
	}
	if (Init(TCHAR_TO_UTF8(*Version), TCHAR_TO_UTF8(*Launcher), TCHAR_TO_UTF8(*Root), TCHAR_TO_UTF8(*Data)) != 0)
	{
		Reason = LastError();
		UE_LOG(LogGlpUpdate, Warning, TEXT("the updater did not start: %s"), *Reason);
		return false;
	}
	bRunning = true;
	Reason.Reset();
	Poll();
	UE_LOG(LogGlpUpdate, Log, TEXT("updater ready: v%s, %s copy in %s%s%s"), *Version, Current.installed_copy ? TEXT("installed") : TEXT("portable"), *Root,
		StatusText.IsEmpty() ? TEXT("") : TEXT(": "), *StatusText);
	return true;
}

void FGlpUpdate::Shutdown()
{
	if (bRunning && ShutdownNow)
	{
		ShutdownNow();
	}
	bRunning = false;
}

void FGlpUpdate::Poll()
{
	if (!bRunning)
	{
		return;
	}
	glp_update_state Next{};
	if (PollNow(&Next) != 0)
	{
		return;
	}
	// The texts change a few times a second at most: converted only then.
	if (FCStringAnsi::Strncmp(Next.status, Current.status, UE_ARRAY_COUNT(Next.status)) != 0 || (StatusText.IsEmpty() && Next.status[0]))
	{
		StatusText = UTF8_TO_TCHAR(Next.status);
		if (!StatusText.IsEmpty())
		{
			UE_LOG(LogGlpUpdate, Verbose, TEXT("%s"), *StatusText);
		}
	}
	if (FCStringAnsi::Strncmp(Next.latest, Current.latest, UE_ARRAY_COUNT(Next.latest)) != 0)
	{
		LatestText = UTF8_TO_TCHAR(Next.latest);
	}
	if (FCStringAnsi::Strncmp(Next.page, Current.page, UE_ARRAY_COUNT(Next.page)) != 0)
	{
		PageText = UTF8_TO_TCHAR(Next.page);
	}
	if (Next.phase != Current.phase)
	{
		UE_LOG(LogGlpUpdate, Log, TEXT("updater: phase %d -> %d%s%s"), Current.phase, Next.phase, Next.status[0] ? TEXT(": ") : TEXT(""), UTF8_TO_TCHAR(Next.status));
	}
	Current = Next;
}

bool FGlpUpdate::Request(int32 (*Function)(), const TCHAR* What)
{
	if (!bRunning)
	{
		RefusalText = Reason;
		return false;
	}
	const int32 Result = Function();
	if (Result == 0)
	{
		RefusalText.Reset();
		Poll();
		return true;
	}
	RefusalText = LastError();
	UE_LOG(LogGlpUpdate, Log, TEXT("updater: %s %s: %s"), What, Result > 0 ? TEXT("not possible now") : TEXT("failed"), *RefusalText);
	return false;
}

bool FGlpUpdate::Check()
{
	return Request(CheckNow, TEXT("check"));
}

bool FGlpUpdate::Download()
{
	return Request(DownloadNow, TEXT("download"));
}

bool FGlpUpdate::Apply()
{
	return Request(ApplyNow, TEXT("install"));
}

void FGlpUpdate::Cancel()
{
	if (bRunning)
	{
		CancelNow();
		Poll();
	}
}

FString FGlpUpdate::LastError() const
{
	if (!LastErrorText)
	{
		return FString();
	}
	ANSICHAR Buffer[1024];
	Buffer[0] = 0;
	LastErrorText(Buffer, UE_ARRAY_COUNT(Buffer));
	return UTF8_TO_TCHAR(Buffer);
}

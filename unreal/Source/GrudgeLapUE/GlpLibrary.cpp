#include "GlpLibrary.h"

#include "HAL/PlatformProcess.h"
#include "Misc/Paths.h"

DEFINE_LOG_CATEGORY_STATIC(LogGlp, Log, All);

const FGlpLibrary* FGlpLibrary::Get()
{
	// Loaded on first use and kept for the process: the NativeAOT runtime inside the DLL does not support being
	// unloaded and loaded again.
	static FGlpLibrary Library;
	static const bool bLoaded = Library.Load();
	return bLoaded ? &Library : nullptr;
}

bool FGlpLibrary::Load()
{
	// Build.cs stages the DLL beside the module binary: the project's Binaries/Win64 in the editor, the game's
	// binaries folder in a packaged build.
	const FString Path = FPaths::Combine(FPaths::ProjectDir(), TEXT("Binaries"), FPlatformProcess::GetBinariesSubdirectory(), TEXT("GrudgeLap.Physics.Native.dll"));
	Handle = FPlatformProcess::GetDllHandle(*Path);
	if (!Handle)
	{
		UE_LOG(LogGlp, Error, TEXT("simulation core not found: %s"), *Path);
		return false;
	}
	bool bComplete = true;
	auto Resolve = [this, &bComplete](auto& Function, const TCHAR* Name)
	{
		Function = reinterpret_cast<std::remove_reference_t<decltype(Function)>>(FPlatformProcess::GetDllExport(Handle, Name));
		if (!Function)
		{
			UE_LOG(LogGlp, Error, TEXT("simulation core lacks %s"), Name);
			bComplete = false;
		}
	};
	Resolve(AbiVersion, TEXT("glp_abi_version"));
	Resolve(Create, TEXT("glp_create"));
	Resolve(Destroy, TEXT("glp_destroy"));
	Resolve(ChooseTransmission, TEXT("glp_choose_transmission"));
	Resolve(ScriptedInputs, TEXT("glp_scripted_inputs"));
	Resolve(Step, TEXT("glp_step"));
	Resolve(GetState, TEXT("glp_get_state"));
	Resolve(Message, TEXT("glp_message"));
	Resolve(LastErrorText, TEXT("glp_last_error"));
	if (!bComplete)
	{
		return false;
	}
	if (AbiVersion() != GLP_ABI_VERSION)
	{
		UE_LOG(LogGlp, Error, TEXT("simulation core speaks ABI %d, this build expects %d"), AbiVersion(), GLP_ABI_VERSION);
		return false;
	}
	UE_LOG(LogGlp, Log, TEXT("simulation core loaded: %s"), *Path);
	return true;
}

FString FGlpLibrary::LastError() const
{
	ANSICHAR Buffer[1024];
	LastErrorText(Buffer, UE_ARRAY_COUNT(Buffer));
	return UTF8_TO_TCHAR(Buffer);
}

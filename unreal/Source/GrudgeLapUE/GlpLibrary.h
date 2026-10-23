#pragma once

#include "CoreMinimal.h"
#include "grudgelap_physics.h"

/**
 * The simulation core's DLL (GrudgeLap.Physics.Native.dll, the GrudgeLap repo's physics.native) and its glp_*
 * functions, loaded once per process. The DLL carries its own .NET runtime (NativeAOT); nothing else is needed.
 */
class FGlpLibrary
{
public:
	/** The loaded library, or nullptr (with the reason logged) when the DLL is missing or speaks another ABI. */
	static const FGlpLibrary* Get();

	/** The library's last failure on this thread (glp_last_error). */
	FString LastError() const;

	decltype(&glp_abi_version) AbiVersion = nullptr;
	decltype(&glp_create) Create = nullptr;
	decltype(&glp_destroy) Destroy = nullptr;
	decltype(&glp_choose_transmission) ChooseTransmission = nullptr;
	decltype(&glp_scripted_inputs) ScriptedInputs = nullptr;
	decltype(&glp_step) Step = nullptr;
	decltype(&glp_get_state) GetState = nullptr;
	decltype(&glp_message) Message = nullptr;
	decltype(&glp_last_error) LastErrorText = nullptr;

private:
	bool Load();
	void* Handle = nullptr;
};

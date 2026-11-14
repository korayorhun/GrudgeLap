#pragma once

#include "CoreMinimal.h"

/**
 * The cars' styles in the catalogue's order (the simulation core's CarStyle, VehicleSpec.Catalogue): the names the Godot
 * version's files carry, a best lap's (bestlap-<style>.bin) and a paint's settings key (paint.<style>, lower case),
 * and so the names this version reads them by.
 */
namespace GlpStyles
{
	inline constexpr const TCHAR* Names[] = { TEXT("FlatTwelve"), TEXT("V8"), TEXT("HotHatch"), TEXT("FiveHatch"), TEXT("TurboHatch"), TEXT("Saloon"),
		TEXT("Estate"), TEXT("Fleet"), TEXT("FlatPlaneV8"), TEXT("HgtFive"), TEXT("HatchSixteen"), TEXT("CupraTsi"), TEXT("Boxer"), TEXT("ClioSixteen") };

	inline constexpr int32 Count = UE_ARRAY_COUNT(Names);
}

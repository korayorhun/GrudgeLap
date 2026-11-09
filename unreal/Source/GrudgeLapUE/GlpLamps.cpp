#include "GlpLamps.h"

#include "Components/SpotLightComponent.h"
#include "Components/StaticMeshComponent.h"
#include "Engine/StaticMesh.h"
#include "Engine/TextureLightProfile.h"
#include "GameFramework/Actor.h"
#include "Materials/MaterialInterface.h"

USpotLightComponent* GlpLamps::Spot(AActor* Owner, USceneComponent* Parent, const FTransform& Relative, float Candela, const FLinearColor& Colour,
	float InnerDegrees, float OuterDegrees, float RadiusCm, bool bShadows, UTextureLightProfile* Profile, bool bTranslucency)
{
	if (!Owner || !Parent)
	{
		return nullptr;
	}
	USpotLightComponent* Light = NewObject<USpotLightComponent>(Owner);
	Light->SetMobility(EComponentMobility::Movable);
	Light->SetupAttachment(Parent);
	Light->SetRelativeTransform(Relative);
	Light->SetIntensityUnits(ELightUnits::Candelas);
	Light->SetIntensity(Candela);
	Light->SetLightColor(Colour);
	Light->SetInnerConeAngle(InnerDegrees);
	Light->SetOuterConeAngle(OuterDegrees);
	Light->SetAttenuationRadius(RadiusCm);
	Light->SetCastShadows(bShadows);
	// Lit rain and spray (translucency) where asked, and the night rain's volumetric fog with the beams' own shadows in it.
	Light->SetAffectTranslucentLighting(bTranslucency);
	Light->bCastVolumetricShadow = bShadows;
	// Every lamp goes through MegaLights while the post-process volume turns it on (AGlpGame::ApplyWeather); otherwise
	// the classic deferred path.
	Light->bAllowMegaLights = true;
	if (Profile)
	{
		// The profile is the pattern normalised to its peak: the light's own candelas are the peak's.
		Light->SetIESTexture(Profile);
		Light->SetUseIESBrightness(false);
	}
	Light->RegisterComponent();
	return Light;
}

UStaticMeshComponent* GlpLamps::Glow(AActor* Owner, USceneComponent* Parent, const FTransform& Relative, const FGlpLampAssets& Assets,
	const FLinearColor& Colour, float Luminance, float RadiusCm, EGlow Kind)
{
	if (!Owner || !Parent || !Assets.Glow || !Assets.Plane)
	{
		return nullptr;
	}
	UStaticMeshComponent* Card = NewObject<UStaticMeshComponent>(Owner);
	Card->SetMobility(EComponentMobility::Movable);
	Card->SetStaticMesh(Assets.Plane);
	Card->SetMaterial(0, Assets.Glow);
	Card->SetupAttachment(Parent);
	// The plane stands across the lamp's facing: its local Z along the frame's X (forward), X along Y (right), Y along Z
	// (up), which M_LampGlow reads back. Standing so, its bounds face the cameras that see the lamp; the material turns
	// the card to the camera and widens it with distance, so the bounds are scaled to hold it.
	const FTransform Across(FMatrix(FVector(0.0, 1.0, 0.0), FVector(0.0, 0.0, 1.0), FVector(1.0, 0.0, 0.0), FVector::ZeroVector).ToQuat());
	Card->SetRelativeTransform(Across * Relative);
	Card->SetCastShadow(false);
	Card->SetCollisionEnabled(ECollisionEnabled::NoCollision);
	Card->bVisibleInRayTracing = false;
	Card->bVisibleInRealTimeSkyCaptures = false;
	Card->bVisibleInReflectionCaptures = false;
	Card->bAffectDynamicIndirectLighting = false;
	Card->bAffectDistanceFieldLighting = false;
	Card->SetBoundsScale(6.f);
	Card->SetCustomPrimitiveDataFloat(4, RadiusCm);
	Card->SetCustomPrimitiveDataFloat(5, static_cast<float>(Kind));
	Card->SetCustomPrimitiveDataFloat(6, 1.5f);
	SetGlowLuminance(Card, Colour, Luminance);
	Card->RegisterComponent();
	return Card;
}

void GlpLamps::SetGlowLuminance(UStaticMeshComponent* Glow, const FLinearColor& Colour, float Luminance)
{
	if (Glow)
	{
		Glow->SetCustomPrimitiveDataVector4(0, FVector4(Colour.R, Colour.G, Colour.B, Luminance));
	}
}

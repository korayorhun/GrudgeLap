#pragma once

#include "CoreMinimal.h"

class AActor;
class UMaterialInterface;
class USceneComponent;
class USpotLightComponent;
class UStaticMesh;
class UStaticMeshComponent;
class UTextureLightProfile;

/** The night's lamp assets (0.77), loaded once by the game: the glow's material, the low beam's light profile, the card. */
struct FGlpLampAssets
{
	UMaterialInterface* Glow = nullptr;
	UTextureLightProfile* LowBeam = nullptr;
	UStaticMesh* Plane = nullptr;
};

/**
 * The lamps of the night (0.77), the Unreal counterpart of the Godot version's NightLamps and CarView.Headlights: spot
 * lights in candelas, a low beam through its IES profile (Scripts/build_scene.py LOW_BEAM), and the glow a lit lamp shows
 * (M_LampGlow), a card turned to the camera that carries the lamp's real luminance.
 */
namespace GlpLamps
{
	/** The low beam's peak in candelas (build_scene.py LOW_BEAM["peak_cd"]); the profile is the pattern over it. */
	constexpr float LowBeamCandela = 20000.f;

	/** How a glow shows round its lamp (M_LampGlow's Kind). */
	enum class EGlow : uint8
	{
		AllRound = 0,   // a yard lamp's refractor
		LowBeam = 1,    // a headlamp, as its low beam sends its light: bright ahead under the cut-off, faint from the side
		Tail = 2,       // a tail lamp or the train's lamp: the Godot version's facing rule
	};

	/**
	 * A spot light shining along the frame's X (Relative to Parent), in candelas, through Profile when given. bTranslucency:
	 * it lights the rain's streaks and the wheels' spray too (the translucency lighting volume).
	 */
	USpotLightComponent* Spot(AActor* Owner, USceneComponent* Parent, const FTransform& Relative, float Candela, const FLinearColor& Colour,
		float InnerDegrees, float OuterDegrees, float RadiusCm, bool bShadows, UTextureLightProfile* Profile, bool bTranslucency = true);

	/**
	 * A glow card at a lamp facing along the frame's X (Relative to Parent): a disc of RadiusCm at Luminance cd/m2 in a
	 * linear Colour. Null without the material.
	 */
	UStaticMeshComponent* Glow(AActor* Owner, USceneComponent* Parent, const FTransform& Relative, const FGlpLampAssets& Assets,
		const FLinearColor& Colour, float Luminance, float RadiusCm, EGlow Kind);

	/** A glow's disc luminance (cd/m2): the tail lamps flare with the brake. */
	void SetGlowLuminance(UStaticMeshComponent* Glow, const FLinearColor& Colour, float Luminance);

	/** The disc luminance of a lamp of Candela behind a lens of RadiusCm, cd/m2. */
	inline float DiscLuminance(float Candela, float RadiusCm)
	{
		return Candela / (UE_PI * FMath::Square(RadiusCm * 0.01f));
	}
}

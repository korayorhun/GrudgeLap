#pragma once

#include "CoreMinimal.h"
#include "GlpLamps.h"
#include "grudgelap_physics.h"

class AActor;
class UMaterialInterface;
class UPrimitiveComponent;
class USceneComponent;
class USpotLightComponent;
class UStaticMeshComponent;
class UWorld;

/** What a car's lamps show (0.77, FGlpCarView::SetLamps). */
struct FGlpLampState
{
	/** The glows and the tail lamps (dusk to dawn, and in the rain: the Godot version's CarLampsLit). */
	bool bCards = false;
	/** The low beams' light (dusk to dawn): by day in the rain it would not show, and MegaLights stays off by day. */
	bool bBeams = false;
	/**
	 * The player's car: two shadowed beams and the tail lamps' red on the road; a drone one beam between its lamps and
	 * without shadows, under MegaLights too (FGlpCarView::BuildLamps).
	 */
	bool bPlayer = false;
	/** The beams' linear colour by the car's era: halogen, xenon, LED. */
	FLinearColor BeamColour = FLinearColor::White;
};

/**
 * A car on screen: its body, the four wheel pivots (the suspension and the steering), the parts that roll, and
 * the steering wheel in the cabin. The player's car is the imported scene's own actors, found by the tags
 * Scripts/build_scene.py sets (GLP.Car, GLP.Wheel0..3, GLP.Spin0..3, GLP.Steering, GLP.Exterior, GLP.OutsideOnly). Traffic and the
 * ghost are copies of its exterior built into one actor each (Clone): the same meshes, another paint or the
 * ghost's see-through material, and no cabin.
 */
class FGlpCarView
{
public:
	/** The imported car tagged RootTag, or null when the level has none. */
	static TUniquePtr<FGlpCarView> FromLevel(UWorld* World, FName RootTag);

	/**
	 * A copy of this car's exterior and wheels as a single new actor. Paint replaces the colour of every material
	 * with a "Paint" parameter (the body's M_CarPaint, the strakes' and trims' M_CarPaintSolid); Override, when
	 * given, replaces every material.
	 */
	TUniquePtr<FGlpCarView> Clone(UWorld* World, const FString& Name, const FLinearColor* Paint, UMaterialInterface* Override) const;

	/** Places the body: the simulation pose of its centre of gravity. */
	void SetPose(const float Position[3], const float Orientation[4]);

	/** The player's wheels from the simulation: suspension travel, steering, rolling. */
	void SetWheels(const glp_state& State);

	/** Wheels of a car the simulation only moves as a whole (traffic, the ghost): they roll by the distance it went. */
	void AdvanceWheels(float Distance, float SteerAngle, float WheelRadius);

	/** The steering wheel in the cabin follows the road wheels at the game's ratio. */
	void TurnSteeringWheel(float SteerAngle);

	void SetVisible(bool bVisible);

	/**
	 * The brake lamps' glow (0 off, 1 lit): every material of this car with a Glow parameter (M_BrakeLamp, 0.76), and at
	 * night the tail lamps' glows and the red on the road behind (0.77). Each lens lights as its part of the car says
	 * (M_BrakeLamp's StopLamp and TailLamp, 0.77): a centre stop lamp with the brake only, a front marker cut from the same
	 * material never.
	 */
	void SetBraking(float Glow);

	/**
	 * The lamps (0.77), as the Godot version's CarView.Headlights: made the first time they are lit, at the lamp sockets
	 * Scripts/prepare_model_car.py measured (tags GLP.LampHeadL, GLP.LampHeadR, GLP.LampTailL, GLP.LampTailR; the Godot
	 * version's box rule where a level lacks them). Only a change touches the components.
	 */
	void SetLamps(const FGlpLampState& State, const FGlpLampAssets& Assets);

	/** The player's beam I (0 left, 1 right) while lit, for the retroreflectors (MPC_Night); null otherwise. */
	const USpotLightComponent* BeamLight(int32 Index) const;

	/**
	 * From the driver's seat the body shell, the outside details and the wheels are not drawn, only their shadows
	 * (CarView.SetViewpoint): seen from inside, the single-sided shell showed its paint along the door tops.
	 */
	void SetInside(bool bInside);

	AActor* Root() const { return RootActor.Get(); }

	/** A wheel's hub (its pivot) in the world, for the tyre marks (0.76). */
	FVector WheelHub(int32 Index) const;

	/** The actor of this car tagged Tag (the binnacle's dials, the mirror glass), or null. */
	AActor* Find(const TCHAR* Tag) const;

	/** Every actor of this car tagged Tag (the outside mirrors). */
	TArray<AActor*> FindAll(const TCHAR* Tag) const;

private:
	TWeakObjectPtr<AActor> RootActor;
	TArray<TWeakObjectPtr<USceneComponent>> Wheels;
	TArray<TWeakObjectPtr<USceneComponent>> Spinners;
	TWeakObjectPtr<USceneComponent> Steering;
	TArray<FTransform> WheelRest;
	TArray<FTransform> SpinnerRest;
	FTransform SteeringRest;
	TArray<TWeakObjectPtr<AActor>> Owned;   // actors whose visibility SetVisible switches
	TArray<TWeakObjectPtr<UPrimitiveComponent>> OutsideOnly;   // drawn only from outside the car (SetInside)
	TArray<TWeakObjectPtr<class UMaterialInstanceDynamic>> BrakeLamps;   // the lamps SetBraking lights
	float BrakeGlow = -1.f;
	/** Gives a mesh's brake-lamp slots dynamic instances of their own and keeps them in BrakeLamps. */
	void TakeBrakeLamps(class UStaticMeshComponent* Mesh);
	float RollAngle = 0.f;

	// The lamps (0.77). Their places in the root's frame (cm: x right, y back, the nose at -y, z up), the body's box there
	// and the ground under the wheels, measured once on the level's car and handed to its copies.
	FVector LampRest[4] = {};   // head left, head right, tail left, tail right
	FBox Body = FBox(ForceInit);
	float Ground = -60.f;
	void MeasureLamps();
	void BuildLamps(const FGlpLampAssets& Assets, bool bPlayer, const FLinearColor& BeamColour);
	void ShowLamps();
	void ApplyTail();
	TArray<TWeakObjectPtr<USpotLightComponent>> Beams;      // the player's two, or a drone's one
	TWeakObjectPtr<USpotLightComponent> TailPool;           // the player's red on the road behind
	TArray<TWeakObjectPtr<UStaticMeshComponent>> HeadGlows;
	TArray<TWeakObjectPtr<UStaticMeshComponent>> TailGlows;
	FGlpLampState Lamps;
	bool bLampsBuilt = false;
	bool bShown = true;
	bool bBraking = false;
};

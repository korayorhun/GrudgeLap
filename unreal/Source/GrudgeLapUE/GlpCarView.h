#pragma once

#include "CoreMinimal.h"
#include "grudgelap_physics.h"

class AActor;
class UMaterialInterface;
class UPrimitiveComponent;
class USceneComponent;
class UWorld;

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
	 * A copy of this car's exterior and wheels as a single new actor. Paint replaces the paint colour (the
	 * M_CarPaint parameter "Paint") on the painted parts; Override, when given, replaces every material.
	 */
	TUniquePtr<FGlpCarView> Clone(UWorld* World, const FString& Name, UMaterialInterface* PaintMaterial, const FLinearColor* Paint, UMaterialInterface* Override) const;

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
	 * From the driver's seat the body shell, the outside details and the wheels are not drawn, only their shadows
	 * (CarView.SetViewpoint): seen from inside, the single-sided shell showed its paint along the door tops.
	 */
	void SetInside(bool bInside);

	AActor* Root() const { return RootActor.Get(); }

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
	float RollAngle = 0.f;
};

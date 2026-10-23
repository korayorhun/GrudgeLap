#pragma once

#include "CoreMinimal.h"
#include "Math/RandomStream.h"

/** Where the camera sits: the game's CameraRig, frame by frame. */
struct FGlpShot
{
	FVector Location = FVector::ZeroVector;
	FQuat Rotation = FQuat::Identity;
	float HorizontalFov = 90.f;
};

/**
 * The game's camera rig (CameraRig.cs). From the driver's seat the head lags the car (it sways out of a corner
 * and nods under braking), the view picks up a few millimetres of road jitter and widens a little at speed;
 * the chase camera trails the car on a spring; the replay looks down from a crane.
 */
class FGlpCameraRig
{
public:
	bool bCockpit = true;

	/** The driver's eyes in the car's Godot frame (metres; CarView.Eye: right, up from the centre of gravity, back). */
	FVector3f Eye = FVector3f(-0.38f, 0.37f, 0.15f);

	/** The centre of gravity's height over the road at rest (metres), which the chase offsets are measured from. */
	float CgHeight = 0.61f;

	void Reset();

	/**
	 * Car is the body's Unreal transform. Acceleration is lateral (positive right) and longitudinal (positive
	 * forward), m/s2; Roughness is 0 on asphalt and 1 on grass.
	 */
	FGlpShot Follow(const FTransform& Car, float SpeedMps, const FVector2f& Acceleration, float Roughness, float DeltaSeconds);

	/** The replay's crane over the recorded pose: simulation position and forward axis (metres). */
	FGlpShot Replay(const FVector& SimPosition, const FVector& SimForward) const;

private:
	FVector3f Head = FVector3f::ZeroVector;
	FVector ChaseEye = FVector::ZeroVector;
	bool bChaseSet = false;
	FRandomStream Random{3};
};

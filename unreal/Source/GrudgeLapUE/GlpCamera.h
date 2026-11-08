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
 * and nods under braking), the view picks up a few millimetres of road vibration and widens a little at speed;
 * a knock throws the head and it springs back; the chase camera trails the car on a spring, rising towards the
 * loop's centre as the car tilts on its wall; the replay looks down from a crane.
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

	/**
	 * A knock felt in the seat (CameraRig.Jolt): the head is thrown along Push (the car's Godot frame: right, up,
	 * back) by a few centimetres for a hard one, and springs back within half a second. Speed is the closing speed.
	 */
	void AddJolt(const FVector3f& Push, float Speed);

	/** The replay's crane over the recorded pose: simulation position and forward axis (metres). */
	FGlpShot Replay(const FVector& SimPosition, const FVector& SimForward) const;

private:
	FVector3f Head = FVector3f::ZeroVector;
	FVector ChaseEye = FVector::ZeroVector;
	bool bChaseSet = false;
	// The seat's vibration runs on its own clock through smooth noise (CameraRig 0.36).
	float VibrationTime = 0.f;
	// The knock's jolt: the eye's offset in the car's Godot frame and its velocity, on a stiff, well-damped spring.
	FVector3f JoltOffset = FVector3f::ZeroVector;
	FVector3f JoltVelocity = FVector3f::ZeroVector;
};

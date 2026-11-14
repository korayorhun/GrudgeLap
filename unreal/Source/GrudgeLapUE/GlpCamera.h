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

/** The views the camera key steps through (0.78: the film view; the Godot version had the first two). */
enum class EGlpView : uint8
{
	/** The driver's seat. */
	Cockpit,
	/** Close behind the car. */
	Chase,
	/** Far behind and above through a long lens, following the car's way rather than its nose: a film's tracking shot. */
	Film,
};

/**
 * The game's camera rig (CameraRig.cs). From the driver's seat the head lags the car (it sways out of a corner
 * and nods under braking), the view picks up a few millimetres of road vibration and widens a little at speed;
 * a knock throws the head and it springs back; the chase camera trails the car on a spring, rising towards the
 * loop's centre as the car tilts on its wall; the film camera (0.78) hangs far behind on a softer spring; the
 * replay looks down from a crane.
 */
class FGlpCameraRig
{
public:
	EGlpView View = EGlpView::Cockpit;

	bool Cockpit() const { return View == EGlpView::Cockpit; }

	/** The driver's eyes in the car's Godot frame (metres; CarView.Eye: right, up from the centre of gravity, back). */
	FVector3f Eye = FVector3f(-0.38f, 0.37f, 0.15f);

	/** The centre of gravity's height over the road at rest (metres), which the chase offsets are measured from. */
	float CgHeight = 0.61f;

	/**
	 * The seat's vertical field of view at rest, degrees (it opens 6 more at speed), and how far under the eyes the
	 * view is aimed 14 m ahead (metres). 0.78: 50 and 1.0 (four degrees down) where the Godot version had 62 and 0.7.
	 * At 62 the dashboard and the wheel were drawn as far away as a wide-angle lens draws them, the roof's edge and both
	 * pillars stood in the picture, and the road filled a third of it (the owner's report of 14 November). Tried from
	 * every seat at 48, 50 and 54: at 48 the cabin is drawn nearest to its size from a real seat, at 54 the road is
	 * widest; 50 keeps both.
	 */
	static constexpr float SeatFov = 50.f;
	static constexpr float SeatDrop = 1.0f;
	float CockpitFov = SeatFov;
	float CockpitDrop = SeatDrop;

	/**
	 * The ground's height (Unreal cm) under a point, when the game can tell: the cameras outside the car stay over
	 * it (the Godot chase camera's 0.9 m; the level has no collision to trace against).
	 */
	TFunction<TOptional<double>(const FVector&)> Ground;

	void Reset();

	/**
	 * Car is the body's Unreal transform and Velocity its velocity in the world (cm/s). Acceleration is lateral
	 * (positive right) and longitudinal (positive forward), m/s2; Roughness is 0 on asphalt and 1 on grass.
	 */
	FGlpShot Follow(const FTransform& Car, const FVector& Velocity, float SpeedMps, const FVector2f& Acceleration, float Roughness, float DeltaSeconds);

	/**
	 * A knock felt in the seat (CameraRig.Jolt): the head is thrown along Push (the car's Godot frame: right, up,
	 * back) by a few centimetres for a hard one, and springs back within half a second. Speed is the closing speed.
	 */
	void AddJolt(const FVector3f& Push, float Speed);

	/** The replay's crane over the recorded pose: simulation position and forward axis (metres). */
	FGlpShot Replay(const FVector& SimPosition, const FVector& SimForward) const;

private:
	FGlpShot FollowFilm(const FTransform& Car, const FVector& Velocity, float SpeedMps, float DeltaSeconds);
	/** Lifts an eye that is less than Clearance (cm) over the ground. */
	FVector OverGround(const FVector& Point, double Clearance) const;

	FVector3f Head = FVector3f::ZeroVector;
	FVector ChaseEye = FVector::ZeroVector;
	bool bChaseSet = false;
	// The film camera: its eye, the car's way over the ground as it follows it, and the point it looks at.
	FVector FilmEye = FVector::ZeroVector;
	FVector FilmWay = FVector::ZeroVector;
	FVector FilmLook = FVector::ZeroVector;
	bool bFilmSet = false;
	// The seat's vibration runs on its own clock through smooth noise (CameraRig 0.36).
	float VibrationTime = 0.f;
	// The knock's jolt: the eye's offset in the car's Godot frame and its velocity, on a stiff, well-damped spring.
	FVector3f JoltOffset = FVector3f::ZeroVector;
	FVector3f JoltVelocity = FVector3f::ZeroVector;
};

#include "GlpCamera.h"

#include "GlpSpace.h"

void FGlpCameraRig::Reset()
{
	Head = FVector3f::ZeroVector;
	bChaseSet = false;
}

FGlpShot FGlpCameraRig::Follow(const FTransform& Car, float SpeedMps, const FVector2f& Acceleration, float Roughness, float DeltaSeconds)
{
	FGlpShot Shot;
	auto Local = [&Car](const FVector3f& Godot) { return Car.TransformPosition(GlpSpace::GodotLocalToUnreal(Godot.X, Godot.Y, Godot.Z)); };
	const float SpeedFactor = FMath::Clamp(SpeedMps / 60.f, 0.f, 1.f);
	if (bCockpit)
	{
		// A touch of forward lean at speed; road jitter that grows with speed and on rough ground.
		const float Lean = SpeedFactor * 0.06f;
		const float Jitter = (0.0012f + 0.0035f * SpeedFactor + 0.012f * Roughness) * FMath::Clamp(SpeedMps / 4.f, 0.f, 1.f);
		const FVector3f Shake(Random.FRandRange(-1.f, 1.f) * Jitter, Random.FRandRange(-1.f, 1.f) * Jitter, 0.f);
		// The head on the neck lags the car by a few centimetres and settles: the cabin seems to move around the
		// eyes the way the 1989 cabinet slid its painted dashboard (US 5,005,148).
		const FVector3f Target(FMath::Clamp(-Acceleration.X * 0.0035f, -0.045f, 0.045f), 0.f, FMath::Clamp(Acceleration.Y * 0.0028f, -0.04f, 0.03f));
		Head += (Target - Head) * FMath::Clamp(DeltaSeconds * 6.f, 0.f, 1.f);
		const FVector EyePoint = Local(Eye + Head + FVector3f(0.f, 0.f, -Lean) + Shake);
		const FVector LookPoint = Local(Eye + FVector3f(Head.X * 0.5f, -0.70f, -14.f));
		Shot.Location = EyePoint;
		Shot.Rotation = FRotationMatrix::MakeFromXZ(LookPoint - EyePoint, Car.GetUnitAxis(EAxis::Z)).ToQuat();
		// 62 degrees vertical is about what a windscreen frames; a little more at speed.
		Shot.HorizontalFov = GlpSpace::HorizontalFov(62.f + 6.f * FMath::Clamp(SpeedMps / 75.f, 0.f, 1.f));
		return Shot;
	}
	const FVector Target = Local(FVector3f(0.f, 1.9f - CgHeight, 6.3f));
	ChaseEye = bChaseSet ? FMath::Lerp(ChaseEye, Target, FMath::Clamp(DeltaSeconds * 6.f, 0.f, 1.f)) : Target;
	bChaseSet = true;
	const FVector Focus = Local(FVector3f(0.f, 0.7f - CgHeight, -2.f));
	Shot.Location = ChaseEye;
	Shot.Rotation = FRotationMatrix::MakeFromXZ(Focus - ChaseEye, FVector::UpVector).ToQuat();
	Shot.HorizontalFov = GlpSpace::HorizontalFov(62.f);
	return Shot;
}

FGlpShot FGlpCameraRig::Replay(const FVector& SimPosition, const FVector& SimForward) const
{
	// A high crane shot that keeps the crashed car centred, like the original's "God's eye" view
	// (CameraRig.Replay): 26 m back, 16 m up and 10 m to the side, in the simulation frame.
	const FVector Back = (-SimForward).GetSafeNormal();
	const FVector Side(-Back.Z, 0.0, Back.X);
	const FVector Crane = SimPosition + Back * 26.0 + FVector(0.0, 16.0, 0.0) + Side * 10.0;
	const FVector Look = SimPosition + FVector(0.0, 0.8, 0.0);
	FGlpShot Shot;
	Shot.Location = GlpSpace::SimToUnreal(Crane);
	Shot.Rotation = FRotationMatrix::MakeFromXZ(GlpSpace::SimToUnreal(Look) - Shot.Location, FVector::UpVector).ToQuat();
	Shot.HorizontalFov = GlpSpace::HorizontalFov(50.f);
	return Shot;
}

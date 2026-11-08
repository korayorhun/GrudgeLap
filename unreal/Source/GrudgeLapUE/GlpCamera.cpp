#include "GlpCamera.h"

#include "GlpSpace.h"

void FGlpCameraRig::Reset()
{
	Head = FVector3f::ZeroVector;
	bChaseSet = false;
	JoltOffset = JoltVelocity = FVector3f::ZeroVector;
}

void FGlpCameraRig::AddJolt(const FVector3f& Push, float Speed)
{
	if (Push.SizeSquared() < 1e-6f)
	{
		return;
	}
	JoltVelocity += Push.GetSafeNormal() * FMath::Clamp(Speed * 0.06f, 0.04f, 0.9f);
}

FGlpShot FGlpCameraRig::Follow(const FTransform& Car, float SpeedMps, const FVector2f& Acceleration, float Roughness, float DeltaSeconds)
{
	FGlpShot Shot;
	auto Local = [&Car](const FVector3f& Godot) { return Car.TransformPosition(GlpSpace::GodotLocalToUnreal(Godot.X, Godot.Y, Godot.Z)); };
	const float SpeedFactor = FMath::Clamp(SpeedMps / 60.f, 0.f, 1.f);
	// The jolt's spring, sub-stepped: one step of a 30 fps frame would overshoot.
	for (float Left = DeltaSeconds; Left > 0.f; Left -= 1.f / 120.f)
	{
		const float H = FMath::Min(Left, 1.f / 120.f);
		JoltVelocity += (-JoltOffset * 160.f - JoltVelocity * 16.f) * H;
		JoltOffset += JoltVelocity * H;
	}
	if (bCockpit)
	{
		// A touch of forward lean at speed. Road feel: a few millimetres that grow with speed and on rough ground, as
		// smooth noise at about 11 Hz (CameraRig 0.36): a fresh random offset every frame shook the whole cabin by a
		// few pixels a frame and read as trembling shadows (the owner's report of 7 November).
		const float Lean = SpeedFactor * 0.06f;
		const float Jitter = (0.0006f + 0.0018f * SpeedFactor + 0.010f * Roughness) * FMath::Clamp(SpeedMps / 4.f, 0.f, 1.f);
		VibrationTime += DeltaSeconds;
		const FVector3f Shake(FMath::PerlinNoise1D(VibrationTime * 11.f) * Jitter, FMath::PerlinNoise1D(VibrationTime * 11.f + 57.f) * Jitter, 0.f);
		// The head on the neck lags the car by a few centimetres and settles: the cabin seems to move around the
		// eyes the way the 1989 cabinet slid its painted dashboard (US 5,005,148).
		const FVector3f Target(FMath::Clamp(-Acceleration.X * 0.0035f, -0.045f, 0.045f), 0.f, FMath::Clamp(Acceleration.Y * 0.0028f, -0.04f, 0.03f));
		Head += (Target - Head) * FMath::Clamp(DeltaSeconds * 6.f, 0.f, 1.f);
		const FVector EyePoint = Local(Eye + Head + JoltOffset + FVector3f(0.f, 0.f, -Lean) + Shake);
		const FVector LookPoint = Local(Eye + FVector3f(Head.X * 0.5f, -0.70f, -14.f));
		Shot.Location = EyePoint;
		Shot.Rotation = FRotationMatrix::MakeFromXZ(LookPoint - EyePoint, Car.GetUnitAxis(EAxis::Z)).ToQuat();
		// 62 degrees vertical is about what a windscreen frames; a little more at speed.
		Shot.HorizontalFov = GlpSpace::HorizontalFov(62.f + 6.f * FMath::Clamp(SpeedMps / 75.f, 0.f, 1.f));
		return Shot;
	}
	// A little further back and lower with a longer lens (0.11): at 62 degrees from 6.3 m the car was drawn in a fish-eye.
	// On the loop's wall and over its top the eye moves in towards the loop's centre with the tilt (CameraRig 0.35:
	// straight back along the car's axis it sat inside the deck for the whole climb).
	const FVector CarUp = Car.GetUnitAxis(EAxis::Z);
	const float Tilt = 1.f - FMath::Max(static_cast<float>(CarUp.Z), -1.f);
	const FVector Target = Local(FVector3f(0.f, 1.7f - CgHeight + 2.2f * Tilt, 7.4f));
	ChaseEye = bChaseSet ? FMath::Lerp(ChaseEye, Target, FMath::Clamp(DeltaSeconds * 6.f, 0.f, 1.f)) : Target;
	bChaseSet = true;
	const FVector Focus = Local(FVector3f(0.f, 0.7f - CgHeight, -2.f));
	// A knock jolts the chase eye too, two and a half times as far.
	const FVector Chase = ChaseEye + Car.TransformVector(GlpSpace::GodotLocalToUnreal(JoltOffset.X * 2.5, JoltOffset.Y * 2.5, JoltOffset.Z * 2.5));
	// The car's own up is the view's up once the road tilts past 50 degrees (the loop).
	const FVector ViewUp = CarUp.Z < 0.64 ? CarUp : FVector::UpVector;
	Shot.Location = Chase;
	Shot.Rotation = FRotationMatrix::MakeFromXZ(Focus - Chase, ViewUp).ToQuat();
	Shot.HorizontalFov = GlpSpace::HorizontalFov(54.f);
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

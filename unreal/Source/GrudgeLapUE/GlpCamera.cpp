#include "GlpCamera.h"

#include "GlpSpace.h"

void FGlpCameraRig::Reset()
{
	Head = FVector3f::ZeroVector;
	bChaseSet = false;
	bFilmSet = false;
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

FVector FGlpCameraRig::OverGround(const FVector& Point, double Clearance) const
{
	if (Ground)
	{
		if (const TOptional<double> Height = Ground(Point); Height.IsSet() && Point.Z < Height.GetValue() + Clearance)
		{
			return FVector(Point.X, Point.Y, Height.GetValue() + Clearance);
		}
	}
	return Point;
}

FGlpShot FGlpCameraRig::Follow(const FTransform& Car, const FVector& Velocity, float SpeedMps, const FVector2f& Acceleration, float Roughness, float DeltaSeconds)
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
	if (View == EGlpView::Cockpit)
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
		const FVector LookPoint = Local(Eye + FVector3f(Head.X * 0.5f, -CockpitDrop, -14.f));
		Shot.Location = EyePoint;
		Shot.Rotation = FRotationMatrix::MakeFromXZ(LookPoint - EyePoint, Car.GetUnitAxis(EAxis::Z)).ToQuat();
		// A little wider at speed: the eye takes in more of the road as it looks further ahead.
		Shot.HorizontalFov = GlpSpace::HorizontalFov(CockpitFov + 6.f * FMath::Clamp(SpeedMps / 75.f, 0.f, 1.f));
		return Shot;
	}
	if (View == EGlpView::Film)
	{
		return FollowFilm(Car, Velocity, SpeedMps, DeltaSeconds);
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
	// A knock jolts the chase eye too, two and a half times as far. Over a crest the eye stays 0.9 m over the ground
	// (CameraRig's rule), where it used to dip into the hill behind the car.
	const FVector Chase = OverGround(ChaseEye + Car.TransformVector(GlpSpace::GodotLocalToUnreal(JoltOffset.X * 2.5, JoltOffset.Y * 2.5, JoltOffset.Z * 2.5)), 90.0);
	// The car's own up is the view's up once the road tilts past 50 degrees (the loop).
	const FVector ViewUp = CarUp.Z < 0.64 ? CarUp : FVector::UpVector;
	Shot.Location = Chase;
	Shot.Rotation = FRotationMatrix::MakeFromXZ(Focus - Chase, ViewUp).ToQuat();
	Shot.HorizontalFov = GlpSpace::HorizontalFov(54.f);
	return Shot;
}

FGlpShot FGlpCameraRig::FollowFilm(const FTransform& Car, const FVector& Velocity, float SpeedMps, float DeltaSeconds)
{
	// The film view (0.78, the owner's request of 14 November for a camera "from further away, a little like a film"):
	// a tracking shot from 17 m behind and 2.8 m up through a long lens (30 degrees upright, a 75 mm lens on 35 mm
	// film, two degrees wider at speed), which flattens the picture as a film's car chase does: the car small under
	// the middle, the road, the plain and the mountains stacked up behind it. (First tried from 15 m and 3.6 m up: it
	// looked down on the road, which filled the picture, where a film's camera car looks along it.)
	//  - It follows the car's way over the ground, not its nose, so a slide shows the car's flank as a camera car
	//    following the road would see it. The way is taken from the velocity above walking pace and kept while the car
	//    stands, flies, or hangs on the loop's wall or its top (where the way over the ground turns round).
	//  - The eye trails on a soft spring (a second to settle, the chase camera's a sixth) and so does the point it
	//    looks at: the car drifts a little in the frame under braking and in a bend instead of being nailed to it.
	//    Both are first carried along with four fifths of the car's speed: the spring alone left the eye a third of a
	//    second of travel behind its place, 10 m further back at 100 km/h and 20 m at 200, where the car was a dot.
	//    With the fifth that is left the car pulls 2 m ahead at 100 km/h and comes back under braking.
	//  - It stays 1.5 m over the ground: from this far back a crest or a cutting's bank is often in the way.
	//  - Backing out of a spin at a walk, the way is the nose's and not the velocity's: the camera does not swing
	//    round to the car's front for a few metres of reverse. A car that slides backwards at speed is still followed
	//    the way it travels, as a camera car would follow it.
	const FVector CarUp = Car.GetUnitAxis(EAxis::Z);
	FVector Way(Velocity.X, Velocity.Y, 0.0);
	const bool bLevel = CarUp.Z > 0.75;
	FVector Nose = -Car.GetUnitAxis(EAxis::Y);   // an imported car's nose is its local -Y (GlpSpace)
	Nose.Z = 0.0;
	Nose = Nose.GetSafeNormal(UE_SMALL_NUMBER, FVector::ForwardVector);
	if (!bFilmSet)
	{
		FilmWay = Nose;
	}
	if (FVector::DotProduct(Way, Nose) < 0.0 && Way.SizeSquared() < FMath::Square(800.0))
	{
		Way = -Way;
	}
	if (bLevel && Way.SizeSquared() > FMath::Square(300.0))
	{
		// Faster to turn with the car at speed, when the road ahead matters most; slow enough to ride out a twitch.
		const float Rate = FMath::Lerp(1.2f, 2.6f, FMath::Clamp(SpeedMps / 40.f, 0.f, 1.f));
		FilmWay = FMath::Lerp(FilmWay, Way.GetSafeNormal(), FMath::Clamp(DeltaSeconds * Rate, 0.f, 1.f)).GetSafeNormal(UE_SMALL_NUMBER, FilmWay);
	}
	const FVector Centre = Car.GetLocation();
	const FVector Target = Centre - FilmWay * 1700.0 + FVector(0.0, 0.0, (2.8 - CgHeight) * 100.0);
	const FVector LookAt = Centre + FilmWay * 800.0 + FVector(0.0, 0.0, (1.0 - CgHeight) * 100.0);
	if (bFilmSet)
	{
		const FVector Carried = Velocity * (0.8 * DeltaSeconds);
		FilmEye = FMath::Lerp(FilmEye + Carried, Target, FMath::Clamp(DeltaSeconds * 3.f, 0.f, 1.f));
		FilmLook = FMath::Lerp(FilmLook + Carried, LookAt, FMath::Clamp(DeltaSeconds * 5.f, 0.f, 1.f));
	}
	else
	{
		FilmEye = Target;
		FilmLook = LookAt;
		bFilmSet = true;
	}
	FGlpShot Shot;
	// A knock shakes this camera too, by the chase camera's measure.
	Shot.Location = OverGround(FilmEye + Car.TransformVector(GlpSpace::GodotLocalToUnreal(JoltOffset.X * 2.5, JoltOffset.Y * 2.5, JoltOffset.Z * 2.5)), 150.0);
	Shot.Rotation = FRotationMatrix::MakeFromXZ(FilmLook - Shot.Location, FVector::UpVector).ToQuat();
	Shot.HorizontalFov = GlpSpace::HorizontalFov(30.f + 2.f * FMath::Clamp(SpeedMps / 60.f, 0.f, 1.f));
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

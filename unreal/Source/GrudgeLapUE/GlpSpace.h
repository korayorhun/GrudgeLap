#pragma once

#include "CoreMinimal.h"

/**
 * The three coordinate frames the Unreal version juggles, and the conversions between them.
 *
 *  - Simulation (the C interface): metres; +X right, +Y up, +Z forward.
 *  - Godot (the game, and the glTF it exports): metres; Godot shows the simulation as (x, y, -z). Godot's own
 *    local frames (a car's body, its eye point) have -Z forward.
 *  - Unreal: centimetres. The glTF import maps Godot's (x, y, z) to Unreal's (x, z, y), so the simulation's
 *    (x, y, z) lands at (x, -z, y) * 100, and an imported car keeps its right along local +X, its roof along local
 *    +Z and its nose along local -Y.
 */
namespace GlpSpace
{
	inline FVector SimToUnreal(const float V[3], double Scale = 100.0)
	{
		return FVector(V[0], -V[2], V[1]) * Scale;
	}

	inline FVector SimToUnreal(const FVector& V, double Scale = 100.0)
	{
		return FVector(V.X, -V.Z, V.Y) * Scale;
	}

	/** A point or offset in one of Godot's local frames (metres) to the matching Unreal local frame (cm). */
	inline FVector GodotLocalToUnreal(double X, double Y, double Z)
	{
		return FVector(X, Z, Y) * 100.0;
	}

	/**
	 * The Unreal rotation of an imported body whose simulation orientation is Q (x, y, z, w). The quaternion only
	 * rotates simulation vectors here; the body's axes are mapped across and become the columns of its rotation.
	 */
	inline FQuat SimRotation(const float Q[4])
	{
		const FQuat Orientation(Q[0], Q[1], Q[2], Q[3]);
		const FVector Right = SimToUnreal(Orientation.RotateVector(FVector::XAxisVector), 1.0);
		const FVector Up = SimToUnreal(Orientation.RotateVector(FVector::YAxisVector), 1.0);
		const FVector Forward = SimToUnreal(Orientation.RotateVector(FVector::ZAxisVector), 1.0);
		return FMatrix(Right, -Forward, Up, FVector::ZeroVector).ToQuat();
	}

	/** The simulation forward axis of an orientation, in the simulation frame. */
	inline FVector SimForward(const float Q[4])
	{
		return FQuat(Q[0], Q[1], Q[2], Q[3]).RotateVector(FVector::ZAxisVector);
	}

	/** Horizontal field of view (Unreal's) for a vertical one (Godot's) on a 16:9 picture. */
	inline float HorizontalFov(float VerticalDegrees)
	{
		return FMath::RadiansToDegrees(2.f * FMath::Atan(FMath::Tan(FMath::DegreesToRadians(VerticalDegrees) * 0.5f) * 16.f / 9.f));
	}

	constexpr float MphPerMps = 2.2369363f;
	constexpr float KmhPerMps = 3.6f;
}

#pragma once

#include "CoreMinimal.h"

/**
 * What the game changes in one car's driver's seat (0.78). A prepared model carries its own eye, dials and steering
 * wheel (tools/prepare_car_models.py, tools/car_cockpits.json); played from the seat some of them stood wrong (the
 * owner's report of 14 November 2026: dashboards that look far away, a steering wheel over the dials), and those are
 * corrected here, per catalogue car, without building the level again. Lengths are metres in the car's Godot frame
 * (x right, y up, z back).
 */
struct FGlpCockpit
{
	/** Added to the model's eye. */
	FVector3f Eye = FVector3f::ZeroVector;
	/** The seat's upright field of view at rest, degrees; 0 leaves the rig's. */
	float Fov = 0.f;
	/** How far under the eyes the view aims 14 m ahead, metres; negative leaves the rig's. */
	float Drop = -1.f;
	/** The instrument panel's face: moved, and scaled about its middle. */
	FVector3f Dials = FVector3f::ZeroVector;
	float DialsScale = 1.f;
	/** The steering wheel: moved, and scaled about its hub. */
	FVector3f Wheel = FVector3f::ZeroVector;
	float WheelScale = 1.f;

	/**
	 * Reads "eye=0,-0.05,0;fov=50;drop=1.1;dials=0,0.02,0;dialsscale=1.1;wheel=0,-0.02,0;wheelscale=0.95" over this
	 * ("/" may stand for ";"); a name left out keeps its value.
	 */
	void Read(const FString& Text);
};

namespace GlpCockpits
{
	/** The game's own correction of a catalogue car's seat (VehicleSpec.Catalogue's index). */
	FGlpCockpit For(int32 Catalogue);

	/**
	 * A file of "<catalogue index>: <FGlpCockpit::Read's text>" lines read over the game's own values (-GlpCockpits=),
	 * to try values on every car in one run without building the module.
	 */
	TMap<int32, FGlpCockpit> Load(const FString& File);
}

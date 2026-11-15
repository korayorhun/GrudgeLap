#pragma once

#include "CoreMinimal.h"
#include "GlpLook.h"

/**
 * The wipers before the driver (0.80; the Godot version's Windscreen.cs of 0.46): they run while it rains, steadily in
 * a downpour and every few seconds in a light rain, and start with a sweep when they are switched on. The glass's
 * material (M_Windscreen, Scripts/build_scene.py) draws the blades from the clock kept here and clears the drops
 * behind them; the wipers' sound will be cut to the same clock.
 */
struct FGlpWipers
{
	/** A blade's sweep up and back, seconds. */
	static constexpr float Sweep = 1.1f;
	/** Seconds from one sweep's start to the next in the lightest rain. */
	static constexpr float Slowest = 4.5f;

	bool bOn = false;
	/**
	 * Seconds since they started. A double: the clock runs on for as long as it rains before the seat, from one credit
	 * into the next, and a float's steps are 4 ms after nine hours, which the blades would show as a judder.
	 */
	double Clock = 0.0;
	/** Seconds from one sweep's start to the next. */
	float Period = Sweep;

	/** A frame of rain (0 to 1). The clock stands while it does not rain, and starts again from a sweep. */
	void Step(float Rain, float DeltaSeconds);

	/** The clock within its period, which is all the picture needs of it: a float has its milliseconds there. */
	float Phase() const { return static_cast<float>(FMath::Fmod(Clock, static_cast<double>(Period))); }

	/** Whether a blade is on its way now (not parked between two sweeps). */
	bool Sweeping() const { return bOn && Phase() < Sweep; }
};

/**
 * The windscreen as the driver sees it (0.80; the Godot version's Windscreen.cs and windscreen.glsl): what the weather
 * and the hour put on the glass. The picture itself is M_Windscreen's (Scripts/build_scene.py WINDSCREEN_HLSL), which
 * AGlpGame::SyncWindscreen feeds every frame.
 */
namespace GlpWindscreen
{
	/** The water on the glass. */
	struct FWater
	{
		/** How hard it rains, 0 to 1: the drops' rate, and the wipers run. */
		float Rain = 0.f;
		/** The water a wet day leaves on the glass without rain, 0 to 1: a few drops of spray, no wipers. */
		float Wet = 0.f;
		/** The share of the glass's places that can hold a drop: most in the rain, a few on a wet day. */
		float Cover = 0.f;

		bool Any() const { return Rain > 0.f || Wet > 0.f; }
	};

	/** The water for a credit's weather (glp_state.weather) and its road's wetness (glp_state.wetness). */
	FWater Water(int32 Weather, float Wetness);

	/**
	 * How much of the sun reaches the glass's dust: all of it on a clear day and at sunrise, a quarter at dusk (the sun
	 * goes behind the Tetons' ridge), none at night and under any cloud.
	 */
	float SunShare(EGlpHour Hour, int32 Weather);

	/**
	 * The sky's glint in a drop by the hour, as a share of the day's: at night a daylight sky's glint stood on the glass
	 * like snowflakes (the Godot version's 0.06), and at dusk the Godot version's half did the same against this
	 * version's darker rain sky, so it is 0.3 here.
	 */
	float Glint(EGlpHour Hour);

	/**
	 * The haze's strength for the direction to the sun in the camera's frame (x right, y up, z ahead): nothing with the
	 * sun more than 50 degrees off the view, which lights no dust the driver looks through, and all of it within 42.
	 * The Godot version switched it on whole at 50 degrees, where the side of the picture nearest a low sun already
	 * holds a tenth of the screen's white (the material lights each pixel by its own angle to the sun): the haze
	 * popped in as the road turned towards the sun. Here it comes in from nothing over those 8 degrees.
	 */
	float Glare(const FVector3f& SunInView, float Share);

	/**
	 * Whether the haze's picture is drawn: from the haze's first trace, and once drawn (bWasHazy) until the sun stands
	 * 52 degrees off. Putting the picture on and taking it off has the car's parts made anew for the renderer (the
	 * custom depth's marks, FGlpCarView::MarkGlass); without the margin a head swaying at the edge asked for that
	 * several times a second.
	 */
	bool Hazy(const FVector3f& SunInView, float Share, bool bWasHazy);

	/** The glass's own checks (-GlpSelfTest): the wipers' clock and the rules above. Returns how many were made. */
	int32 SelfTest(TArray<FString>& Failures);
}

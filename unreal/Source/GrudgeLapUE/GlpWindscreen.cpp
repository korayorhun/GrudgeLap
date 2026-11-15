#include "GlpWindscreen.h"

void FGlpWipers::Step(float Rain, float DeltaSeconds)
{
	const bool bRun = Rain > 0.02f;
	if (bRun && !bOn)
	{
		Clock = 0.0;
	}
	bOn = bRun;
	if (!bRun)
	{
		return;
	}
	Clock += FMath::Max(DeltaSeconds, 0.f);
	Period = Rain >= 0.6f ? Sweep : FMath::Lerp(Slowest, Sweep, Rain / 0.6f);
}

GlpWindscreen::FWater GlpWindscreen::Water(int32 Weather, float Wetness)
{
	FWater Water;
	if (Weather == GLP_WEATHER_RAIN)
	{
		Water.Rain = 1.f;
		Water.Cover = 0.85f;
	}
	else
	{
		Water.Wet = FMath::Clamp(Wetness, 0.f, 1.f);
		Water.Cover = 0.12f;
	}
	return Water;
}

float GlpWindscreen::SunShare(EGlpHour Hour, int32 Weather)
{
	if (Weather != GLP_WEATHER_DRY || Hour == EGlpHour::Night)
	{
		return 0.f;
	}
	return Hour == EGlpHour::Dusk ? 0.25f : 1.f;
}

float GlpWindscreen::Glint(EGlpHour Hour)
{
	switch (Hour)
	{
	case EGlpHour::Night: return 0.06f;
	case EGlpHour::Dusk: return 0.3f;
	default: return 1.f;
	}
}

float GlpWindscreen::Glare(const FVector3f& SunInView, float Share)
{
	return Share * 0.9f * FMath::SmoothStep(0.64f, 0.74f, SunInView.GetSafeNormal().Z);
}

bool GlpWindscreen::Hazy(const FVector3f& SunInView, float Share, bool bWasHazy)
{
	return Glare(SunInView, Share) > 0.01f || (bWasHazy && Share > 0.f && SunInView.GetSafeNormal().Z > 0.62f);
}

int32 GlpWindscreen::SelfTest(TArray<FString>& Failures)
{
	int32 Checks = 0;
	auto Check = [&Failures, &Checks](bool bHeld, const TCHAR* What)
	{
		Checks++;
		if (!bHeld)
		{
			Failures.Add(FString::Printf(TEXT("windscreen: %s"), What));
		}
	};

	// The wipers: off in the dry, a sweep at once when the rain starts, a steady sweep in a downpour.
	FGlpWipers Wipers;
	Wipers.Step(0.f, 0.5f);
	Check(!Wipers.bOn && Wipers.Clock == 0.0 && !Wipers.Sweeping(), TEXT("the wipers stand in the dry"));
	Wipers.Step(1.f, 0.25f);
	Check(Wipers.bOn && FMath::IsNearlyEqual(Wipers.Clock, 0.25, 1e-6) && Wipers.Sweeping(), TEXT("the rain starts them with a sweep"));
	Check(FMath::IsNearlyEqual(Wipers.Period, FGlpWipers::Sweep), TEXT("a downpour's sweeps follow one another"));
	for (int32 i = 0; i < 40; i++)
	{
		Wipers.Step(1.f, 0.1f);
	}
	Check(FMath::IsNearlyEqual(Wipers.Clock, 4.25, 1e-3) && Wipers.Sweeping(), TEXT("they keep sweeping in a downpour"));
	// A day and a half of rain: the clock still takes a frame's step whole (a float's steps are 8 ms by then).
	FGlpWipers Long;
	Long.Step(1.f, 0.f);
	Long.Clock = 130000.0;
	const float Before = Long.Phase();
	Long.Step(1.f, 0.004f);
	const float Stepped = FMath::Fmod(Long.Phase() - Before + FGlpWipers::Sweep, FGlpWipers::Sweep);
	Check(FMath::IsNearlyEqual(Stepped, 0.004f, 1e-4f), TEXT("the clock keeps its milliseconds through a long rain"));
	// A light rain: a sweep, then a rest until the next.
	Wipers.Step(0.3f, 0.f);
	Check(FMath::IsNearlyEqual(Wipers.Period, 0.5f * (FGlpWipers::Slowest + FGlpWipers::Sweep)), TEXT("a light rain's sweeps come every few seconds"));
	FGlpWipers Light;
	Light.Step(0.3f, 0.5f);
	Check(Light.Sweeping(), TEXT("a light rain's first sweep runs"));
	Light.Step(0.3f, 1.f);
	Check(Light.bOn && !Light.Sweeping(), TEXT("the blades rest between a light rain's sweeps"));
	Light.Step(0.3f, 1.5f);
	Check(Light.Sweeping(), TEXT("the next sweep comes after the rest"));
	// The rain over: they stop where the clock stood, and the next shower starts from a sweep again.
	Wipers.Step(0.f, 1.f);
	Check(!Wipers.bOn && !Wipers.Sweeping(), TEXT("they stop with the rain"));
	Wipers.Step(1.f, 0.1f);
	Check(Wipers.bOn && FMath::IsNearlyEqual(Wipers.Clock, 0.1, 1e-6), TEXT("the next shower starts from a sweep"));
	Wipers.Step(1.f, -1.f);
	Check(FMath::IsNearlyEqual(Wipers.Clock, 0.1, 1e-6), TEXT("a frame of no time leaves the clock"));

	// The water by the weather.
	const FWater Dry = Water(GLP_WEATHER_DRY, 0.f);
	Check(!Dry.Any(), TEXT("a dry day leaves the glass dry"));
	const FWater Grey = Water(GLP_WEATHER_OVERCAST, 0.f);
	Check(!Grey.Any(), TEXT("an overcast day leaves the glass dry"));
	const FWater Wet = Water(GLP_WEATHER_WET, 0.8f);
	Check(Wet.Rain == 0.f && FMath::IsNearlyEqual(Wet.Wet, 0.8f) && FMath::IsNearlyEqual(Wet.Cover, 0.12f), TEXT("a wet day leaves a few drops and no rain"));
	const FWater Raining = Water(GLP_WEATHER_RAIN, 1.f);
	Check(Raining.Rain == 1.f && Raining.Wet == 0.f && FMath::IsNearlyEqual(Raining.Cover, 0.85f), TEXT("the rain beads most of the glass"));
	Check(Water(GLP_WEATHER_WET, 3.f).Wet == 1.f, TEXT("the wetness is held to 1"));

	// The sun on the glass's dust.
	Check(SunShare(EGlpHour::Day, GLP_WEATHER_DRY) == 1.f && SunShare(EGlpHour::Sunrise, GLP_WEATHER_DRY) == 1.f, TEXT("a clear day's and a sunrise's sun reaches the glass"));
	Check(SunShare(EGlpHour::Dusk, GLP_WEATHER_DRY) == 0.25f, TEXT("a quarter of the dusk's sun reaches the glass"));
	Check(SunShare(EGlpHour::Night, GLP_WEATHER_DRY) == 0.f, TEXT("no sun at night"));
	Check(SunShare(EGlpHour::Day, GLP_WEATHER_OVERCAST) == 0.f && SunShare(EGlpHour::Day, GLP_WEATHER_WET) == 0.f && SunShare(EGlpHour::Sunrise, GLP_WEATHER_RAIN) == 0.f,
		TEXT("no sun under cloud"));
	Check(Glare(FVector3f(0.f, 0.f, 1.f), 1.f) == 0.9f, TEXT("the sun dead ahead lights the dust"));
	Check(Glare(FVector3f(0.f, 2.f, 10.f), 0.25f) == 0.225f, TEXT("the haze takes the sun's share"));
	Check(Glare(FVector3f(1.f, 0.f, 0.f), 1.f) == 0.f && Glare(FVector3f(0.f, 0.f, -1.f), 1.f) == 0.f, TEXT("no haze with the sun abeam or behind"));
	Check(Glare(FVector3f(0.77f, 0.f, 0.64f), 1.f) == 0.f && Glare(FVector3f(0.66f, 0.f, 0.75f), 1.f) == 0.9f, TEXT("the haze ends 50 degrees off the sun and is whole by 42"));
	const float Coming = Glare(FVector3f(0.7f, 0.f, 0.72f), 1.f);
	Check(Coming > 0.5f && Coming < 0.9f && Glare(FVector3f(0.764f, 0.f, 0.645f), 1.f) < 0.01f, TEXT("the haze comes in from nothing between them"));
	Check(Glare(FVector3f::ZeroVector, 1.f) == 0.f, TEXT("no direction, no haze"));
	// When the haze's picture is drawn: from its first trace, and then until the sun stands a little further out.
	Check(!Hazy(FVector3f(0.764f, 0.f, 0.645f), 1.f, false) && Hazy(FVector3f(0.7f, 0.f, 0.72f), 1.f, false), TEXT("the haze's picture comes with its first trace"));
	Check(Hazy(FVector3f(0.775f, 0.f, 0.632f), 1.f, true) && !Hazy(FVector3f(0.775f, 0.f, 0.632f), 1.f, false), TEXT("once drawn it stays a little past where it came"));
	Check(!Hazy(FVector3f(0.8f, 0.f, 0.6f), 1.f, true) && !Hazy(FVector3f(0.f, 0.f, 1.f), 0.f, true), TEXT("it goes with the sun 52 degrees off, and under cloud"));
	Check(Glint(EGlpHour::Night) < Glint(EGlpHour::Dusk) && Glint(EGlpHour::Dusk) < Glint(EGlpHour::Day) && Glint(EGlpHour::Sunrise) == 1.f, TEXT("the drops' glint follows the hour"));
	return Checks;
}

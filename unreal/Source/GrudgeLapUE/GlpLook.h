#pragma once

#include "CoreMinimal.h"
#include "grudgelap_physics.h"

/**
 * The hours of a credit (0.77), in the Godot version's order (WorldBuilder.Hour): the day (the sun 43 degrees up), the
 * sunrise (5.85 degrees, over the Gros Ventre), the night (the moon 17 degrees up) and the dusk (the sun 5.85 degrees up
 * behind the Tetons).
 */
enum class EGlpHour : uint8
{
	Day,
	Sunrise,
	Night,
	Dusk,
};

namespace GlpLook
{
	/**
	 * The player's LIGHT choice as the Godot version stores it (Bootstrap._lightChoice, the settings' light=): 0 day,
	 * 1 sunrise, 2 random (a draw of the four for every credit), 3 night, 4 dusk.
	 */
	inline const TCHAR* ChoiceName(int32 Choice)
	{
		static const TCHAR* Names[] = { TEXT("DAY"), TEXT("SUNRISE"), TEXT("RANDOM"), TEXT("NIGHT"), TEXT("DUSK") };
		return Names[FMath::Clamp(Choice, 0, 4)];
	}

	inline const TCHAR* HourName(EGlpHour Hour)
	{
		switch (Hour)
		{
		case EGlpHour::Sunrise: return TEXT("sunrise");
		case EGlpHour::Night: return TEXT("night");
		case EGlpHour::Dusk: return TEXT("dusk");
		default: return TEXT("day");
		}
	}

	/** L on the title screen: day, sunrise, dusk, night, random, as the Godot version's CycleLight ([0, 1, 4, 3, 2]). */
	inline int32 NextChoice(int32 Choice)
	{
		static const int32 Order[] = { 0, 1, 4, 3, 2 };
		for (int32 i = 0; i < 5; i++)
		{
			if (Order[i] == Choice)
			{
				return Order[(i + 1) % 5];
			}
		}
		return 0;
	}

	/** -GlpLight=day|sunrise|dusk|night|random (the Godot version's --light= names) as a choice, or -1. */
	inline int32 ParseChoice(const FString& Name)
	{
		static const TCHAR* Names[] = { TEXT("day"), TEXT("sunrise"), TEXT("random"), TEXT("night"), TEXT("dusk") };
		for (int32 i = 0; i < 5; i++)
		{
			if (Name.Equals(Names[i], ESearchCase::IgnoreCase))
			{
				return i;
			}
		}
		return -1;
	}

	/** The credit's hour for a choice (Bootstrap.ChosenHour): random draws one of the four. */
	inline EGlpHour HourOf(int32 Choice)
	{
		switch (Choice)
		{
		case 1: return EGlpHour::Sunrise;
		case 3: return EGlpHour::Night;
		case 4: return EGlpHour::Dusk;
		case 2: return static_cast<EGlpHour>(FMath::RandRange(0, 3));
		default: return EGlpHour::Day;
		}
	}
}

/**
 * What an hour under a weather does to the level (0.77). Values marked "the level's" keep what build_scene.py set (the
 * clear day), so a day reproduces the 0.76 picture exactly; the rest are absolute.
 */
struct FGlpLook
{
	/** The level's own sun direction (the day's, Godot's SunDirection); otherwise Towards. */
	bool bLevelSun = true;
	/** Towards the light, Godot axes (WorldBuilder's SunriseDirection, DuskDirection, MoonDirection). */
	FVector Towards = FVector::UpVector;
	/** The light's outer-space illuminance as a share of the level's sun (75 000 lux). */
	float SunShare = 1.f;
	/**
	 * Lux on the ground, perpendicular to the light, when above zero: the outer-space illuminance is then whatever the
	 * atmosphere's transmittance asks for (USkyAtmosphereComponent::GetAtmosphericLightToMatchIlluminanceOnGround), the
	 * moon's way; SunShare is ignored.
	 */
	float GroundLux = 0.f;
	FLinearColor Colour = FLinearColor::White;
	/** Degrees across the light's disc (0.5357 the sun's, 12 a sun behind cloud). */
	float SourceAngle = 0.5357f;
	/** The sky draws the light's disc. */
	bool bDisc = true;
	/** Stops from the level's exposure (EV100 14). */
	float EvOffset = 0.f;
	/** The sky and the aerial perspective's luminance (USkyAtmosphereComponent::SetSkyAndAerialPerspectiveLuminanceFactor). */
	float SkyFactor = 1.f;
	/**
	 * Added to a clear sky's cloud coverage: fair-weather cumulus grow with the day's heat. The template cloud material's
	 * Cloud_GlobalCoverage is an offset on its noise (the level's -0.2, the overcast's 0.95), not a share: scaled down it
	 * gave more cloud, so the hours move it by the steps the Godot version's coverage takes from its day's 0.41.
	 */
	float CloudOffset = 0.f;
	/** Bloom, lens flare and film grain; below zero the level's. */
	float Bloom = -1.f;
	float Flare = -1.f;
	float Grain = -1.f;
	/** The height fog's volumetric fog: the rain's air at night, in which the beams show. */
	bool bVolumetricFog = false;
	/** Its extinction, times the height fog's own density, and its phase function's anisotropy (AGlpGame::ApplyWeather). */
	float AirExtinction = 1.f;
	float AirAnisotropy = 0.2f;
};

/**
 * The look of an hour under a weather (glp_state.weather), starting values tuned against the Godot version's pictures
 * (0.77). Exposures follow the horizontal illuminance (EV100 = log2(0.458 E) - 0.7, the offset the day carries); the
 * sunrise and the dusk keep the sun at full strength, as the sky atmosphere reddens and dims a low sun by itself (the
 * colours Godot gave its SunriseEnergy 2.1 and DuskEnergy 0.3 were a stand-in for the atmosphere it has not got). Under
 * any weather but a clear sky the light comes from the day's direction ("high in the grey", as the Godot version's
 * overcast) without a disc, dimmed by the hour (Godot's Dawn: night 0.05, dusk 0.45, sunrise 0.75 of the day's).
 */
inline FGlpLook GlpLookFor(EGlpHour Hour, int32 Weather)
{
	const bool bOvercast = Weather != GLP_WEATHER_DRY;
	const bool bRaining = Weather == GLP_WEATHER_RAIN;
	static const FLinearColor CloudLight(0.86f, 0.89f, 0.95f);
	FGlpLook Look;
	if (bOvercast)
	{
		// The 0.13 overcast for every hour: a wide soft source behind the cloud from the day's direction.
		Look.Colour = CloudLight;
		Look.SourceAngle = 12.f;
	}
	switch (Hour)
	{
	case EGlpHour::Sunrise:
		if (bOvercast)
		{
			// 1800 lux, 2.3 stops under the grey day's light, with 1.7 stops more exposure (1.6 in the rain): the picture as
			// far under the grey day's as the Godot version's (measured from the start line: 0.9 stops, 1.0 in the rain).
			// The first estimate, 1.9 stops more from its Dawn 0.75, left it 0.3 and 0.45 stops too bright.
			Look.SunShare = 0.024f;
			Look.bDisc = false;
			Look.EvOffset = bRaining ? -4.4f : -4.1f;
		}
		else
		{
			Look.bLevelSun = false;
			Look.Towards = FVector(-0.991, 0.102, 0.087);   // WorldBuilder.SunriseDirection: 5.85 degrees, bearing 85
			// Measured against the Godot version's sunrise (the plain and the road by the day's offsets): the low sun
			// through the sky atmosphere gives the land more light than the 2300 lux of the first estimate.
			Look.EvOffset = -2.f;
			Look.CloudOffset = -0.19f;   // Godot's coverage 0.22 against the day's 0.41
		}
		// The dusk's flare (0.77): the low sun dead ahead on the run east threw the day's 0.12 across the road and the
		// windscreen as a green ghost and a blue disc, and in the rain the lamps' glows, two stops brighter against this
		// exposure than the day's, would do the same.
		Look.Flare = 0.04f;
		break;
	case EGlpHour::Dusk:
		if (bOvercast)
		{
			// 900 lux, 3.3 stops under the grey day's light, with 1.4 stops more exposure (1.2 in the rain): the picture as
			// far under the grey day's as the Godot version's (measured from the start line: 3.1 stops, 3.2 in the rain).
			// The first estimate, 1.9 stops more from its Dawn 0.45 and its exposure 0.95, left it a stop too bright.
			Look.SunShare = 0.012f;
			Look.bDisc = false;
			Look.EvOffset = bRaining ? -4.0f : -3.8f;
		}
		else
		{
			Look.bLevelSun = false;
			Look.Towards = FVector(0.961, 0.102, 0.257);   // WorldBuilder.DuskDirection: behind the Tetons' ridge
			// The valley in the range's shadow, lit by the sky alone; measured against the Godot version's dusk.
			Look.EvOffset = -3.4f;
			Look.CloudOffset = -0.09f;   // Godot's 0.32
		}
		Look.Bloom = 0.4f;
		Look.Flare = 0.04f;
		Look.Grain = 0.25f;
		break;
	case EGlpHour::Night:
		if (bOvercast)
		{
			// The grey deck lit from above: at 0.2 lux on the ground, half the clear night's, the sky and the mountains were
			// black where the Godot version shows its deck grey-blue.
			Look.GroundLux = 0.6f;
			Look.Colour = FLinearColor(0.60f, 0.71f, 0.95f);
			Look.bDisc = false;
		}
		else
		{
			// The moon (WorldBuilder.MoonDirection, 17 degrees up, bearing 144) at 0.9 lux on the ground with the exposure
			// at EV100 1: the moonlit land as the Godot version's night (measured: at 1 lux and EV 0 the plain, the mountains
			// and the sky read 2.5 to 3 stops over it, at 0.2 lux 2.6 to 3.8 under; 0.45 at EV 0 matched), and the lamps a
			// stop under where EV 0 put them, where the road 5 to 15 m ahead came out white. Its colour is the Godot
			// version's MoonLight, the eye's night tint.
			Look.bLevelSun = false;
			Look.Towards = FVector(-0.557, 0.293, -0.777);
			Look.GroundLux = 0.9f;
			Look.Colour = FLinearColor(0.70f, 0.80f, 1.f);
			Look.SourceAngle = 0.52f;
			Look.CloudOffset = -0.11f;   // Godot's 0.30
		}
		Look.EvOffset = -13.f;   // EV100 1: middle grey at 0.25 cd/m2
		// The moonlit Rayleigh sky read two stops over the Godot version's night photo at full strength, and its aerial
		// perspective veiled the far mountains pale blue (the 0.64 bug Godot fixed with DeckDawn); at 0.3 it read 1.6 stops
		// under it.
		Look.SkyFactor = 0.6f;
		Look.Bloom = 0.3f;
		// A whisper of flare: the lamps' glows, thousands of times the night's white, cast ghosts across the frame at 0.05.
		Look.Flare = 0.01f;
		Look.Grain = 0.2f;
		// The rain's air at night (Godot's RainNightAir, 0.0012 a metre with its fog's anisotropy 0.2): twice the height
		// fog's rain density, scattering about evenly. The beams go away from the seat and the chase camera, so only the
		// light a drop sends back shows them: at 0.7, the mist's forward scattering, the air took it on along the beams and
		// the night rain looked like a clear night with a wet road.
		Look.bVolumetricFog = bRaining;
		Look.AirExtinction = 2.f;
		Look.AirAnisotropy = 0.2f;
		break;
	default:
		if (bOvercast)
		{
			// The day as 0.76 had it: the sun at an eighth, two and a half stops more exposure (rain: 2.8).
			Look.SunShare = 0.12f;
			Look.EvOffset = bRaining ? -2.8f : -2.4f;
		}
		break;
	}
	return Look;
}

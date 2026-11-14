#pragma once

#include "CoreMinimal.h"

/**
 * The paints of the showroom (0.79), as the Godot version's since 0.25 (PaintChoice.cs, CarView.Paints): a car is in
 * its own colour (ORIGINAL, the model's factory colour and the default), in one of the preset paints, or in a colour
 * the player mixed in a finish. The owner asked for it on 2 November 2026: "varsayılan renkleri olsun (orjinal
 * renkler) + kullanıcı custom seçebilsin".
 *
 * Colours are written here as the menu and the settings name them, in sRGB (a hex code's numbers); Linear() gives
 * what a material takes.
 */

/** The finish of a mixed colour: solid as the classic paints, metallic as the silver, or a pearl, whose mica turns its colour with the angle it is seen at. */
enum class EGlpFinish : uint8
{
	Solid,
	Metallic,
	Pearl,
};

/**
 * A car's paint as it is drawn (the Godot version's CarView.CarPaint): the colour and the base coat under the clear
 * coat. A solid colour is a coated dielectric, hardly any metal in it; a metallic paint's flakes make its base coat
 * partly metal and smoother; a pearl sits between the two and flops towards PearlTint where a panel is seen edge-on
 * (M_CarPaintSolid's and M_CarPaintTextured's Pearl, Scripts/build_scene.py paint_finish).
 */
struct FGlpPaint
{
	/** sRGB components, 0 to 1. */
	FLinearColor Srgb = FLinearColor(0.76f, 0.03f, 0.05f);
	float Metallic = 0.05f;
	float Roughness = 0.38f;
	float Pearl = 0.f;

	/** The colour as a material's parameter takes it. */
	FLinearColor Linear() const;
	/** The colour a pearl turns to edge-on, as a material's parameter takes it; the paint's own colour without a pearl. */
	FLinearColor PearlTint() const;
};

namespace GlpPaint
{
	/** sRGB components as the linear colour a material takes, exactly (FColor's table rounds to 8 bits first). */
	FLinearColor ToLinear(const FLinearColor& Srgb);
	/** The other way: a material's linear colour as sRGB components. */
	FLinearColor ToSrgb(const FLinearColor& Linear);

	/** Hue, saturation and lightness (each 0 to 1) of an sRGB colour and back, for the colour picker (the Godot version's Hsl). */
	FLinearColor FromHsl(float Hue, float Saturation, float Lightness);
	void ToHsl(const FLinearColor& Srgb, float& Hue, float& Saturation, float& Lightness);

	/**
	 * The colour a pearl turns to where its panel is seen edge-on (sRGB): its hue moved on by about a twelfth of the
	 * circle and a little darker, as the interference of a mica's coating shifts it; a white, silver or black pearl,
	 * which has no hue to move, flops to the warm gold of its mica.
	 */
	FLinearColor PearlTintOf(const FLinearColor& Srgb);

	/**
	 * A colour mixed in the showroom in a finish. The colour is held inside what a paint can reflect: about the white
	 * paint's 0.92 at the top and the black's 0.03 at the foot, so the picker's pure white does not glow under the sun
	 * and its pure black is not a hole in the picture.
	 */
	FGlpPaint Mixed(const FLinearColor& Srgb, EGlpFinish Finish);

	const TCHAR* FinishName(EGlpFinish Finish);

	/** The preset paints: 0 is ORIGINAL (the car's own colour), then the classic colours. */
	int32 PresetCount();
	const TCHAR* PresetName(int32 Index);
	/** A preset's paint; unset for ORIGINAL. */
	TOptional<FGlpPaint> Preset(int32 Index);

	/** The settings key of a car's paint, "paint.flattwelve": the car named by its style, as the Godot version's PaintBook.Key. */
	FString Key(int32 Car);
	/** The settings key of the colour a car was last mixed in while it wears another paint, "mixed.flattwelve". */
	FString MixedKey(int32 Car);

	/**
	 * The paints' own checks (-GlpSelfTest, as the Godot version's --paint-book-test): the text the settings keep read and
	 * written both ways, what must not parse, the colour arithmetic and the book. Returns how many checks ran; each one
	 * that failed adds its text to Failures.
	 */
	int32 SelfTest(TArray<FString>& Failures);
}

/**
 * The paint chosen for one car (the Godot version's PaintChoice). The settings keep it as text, the preset's name or
 * the colour as hex with its finish ("#1E4FA0 METALLIC"), so a file stays readable and a preset added or moved later
 * does not change what an old file means; the Godot version's files read the same.
 */
struct FGlpPaintChoice
{
	/** The Preset of a mixed colour. */
	static constexpr int32 Custom = -1;

	int32 Preset = 0;
	/** A mixed colour, to the 8 bits a channel its hex keeps (sRGB). */
	FColor Colour = FColor(0, 0, 0);
	EGlpFinish Finish = EGlpFinish::Solid;

	static FGlpPaintChoice OfPreset(int32 Index);
	static FGlpPaintChoice Mixed(const FLinearColor& Srgb, EGlpFinish Finish);

	bool IsCustom() const { return Preset == Custom; }
	bool IsOriginal() const { return Preset == 0; }
	/** The mixed colour's sRGB components. */
	FLinearColor Srgb() const { return FLinearColor(Colour.R / 255.f, Colour.G / 255.f, Colour.B / 255.f); }
	/** The paint the car is drawn in; unset for its own. */
	TOptional<FGlpPaint> Paint() const;
	/** The name the showroom prints: the preset's, or CUSTOM and the finish. */
	FString Name() const;
	/** The mixed colour as the settings write it: #1E4FA0. */
	FString Hex() const;
	/** The text the settings keep. */
	FString Format() const;
	/**
	 * A paint from its text: a preset's name in any case, with or without its spaces (DARK BLUE, dark-blue, darkblue), or
	 * its number; or a colour as hex with an optional finish after a space, a colon or a comma (#1e4fa0,
	 * #1E4FA0 METALLIC, #1e4fa0:pearl; solid when none is given). False for anything else.
	 */
	static bool TryParse(const FString& Text, FGlpPaintChoice& Out);

	bool operator==(const FGlpPaintChoice& Other) const
	{
		return Preset == Other.Preset && (!IsCustom() || (Colour == Other.Colour && Finish == Other.Finish));
	}
	bool operator!=(const FGlpPaintChoice& Other) const { return !(*this == Other); }
};

/**
 * Every car's paint (the Godot version's PaintBook), kept in the settings as paint.<car>, the car named by its style,
 * so a car added to the catalogue has its own key with nothing else to write, and a car without one is in its own
 * colour. The last colour a car was mixed in is remembered, so stepping from it to a preset and back finds it again.
 * The Godot version remembered it only while the game ran, and a colour mixed with care was gone at the next start
 * once a preset had been tried over it; here the settings keep it too (mixed.<car>) while the car wears another paint.
 */
class FGlpPaintBook
{
public:
	void Reset(int32 Cars);

	const FGlpPaintChoice& Of(int32 Car) const;
	/** The colour the car was last mixed in, if it has been. */
	TOptional<FGlpPaintChoice> LastMixed(int32 Car) const;
	void Set(int32 Car, const FGlpPaintChoice& Choice);
	/** A colour the car was mixed in, remembered without the car wearing it (the showroom's, when a preset was taken after it). */
	void Remember(int32 Car, const FGlpPaintChoice& Mixed);

	/** A line of the settings: true when its key is a car's paint or mixed colour (whether or not its value could be read). */
	bool ReadLine(const FString& Key, const FString& Value);
	/**
	 * The lines the settings keep: a car's paint when it is not in its own colour, and the colour it was last mixed in
	 * when it does not wear it.
	 */
	FString Lines() const;

private:
	TArray<FGlpPaintChoice> Choices;
	TArray<TOptional<FGlpPaintChoice>> Mixed;
};

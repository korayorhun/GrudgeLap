#include "GlpPaint.h"

#include "GlpStyles.h"

namespace
{
	struct FPreset
	{
		const TCHAR* Name;
		bool bOwn;
		FGlpPaint Paint;
	};

	// The Godot version's CarView.Paints: the car's own colour as its model has it, then the classic colours. The
	// silver is a metallic: drawn as a solid it was a flat light grey, hardly told from the white.
	const FPreset Presets[] = {
		{ TEXT("ORIGINAL"), true, {} },
		{ TEXT("RED"), false, { FLinearColor(0.76f, 0.03f, 0.05f) } },
		{ TEXT("WHITE"), false, { FLinearColor(0.90f, 0.90f, 0.87f) } },
		{ TEXT("SILVER"), false, { FLinearColor(0.62f, 0.63f, 0.64f), 0.6f, 0.3f } },
		{ TEXT("BLACK"), false, { FLinearColor(0.035f, 0.035f, 0.04f) } },
		{ TEXT("DARK BLUE"), false, { FLinearColor(0.05f, 0.10f, 0.30f) } },
		{ TEXT("GREEN"), false, { FLinearColor(0.06f, 0.24f, 0.13f) } },
		{ TEXT("YELLOW"), false, { FLinearColor(0.92f, 0.70f, 0.05f) } },
	};

	const TCHAR* const FinishNames[] = { TEXT("SOLID"), TEXT("METALLIC"), TEXT("PEARL") };

	float ChannelToLinear(float C)
	{
		return C <= 0.04045f ? C / 12.92f : FMath::Pow((C + 0.055f) / 1.055f, 2.4f);
	}

	float ChannelToSrgb(float L)
	{
		return L <= 0.0031308f ? L * 12.92f : 1.055f * FMath::Pow(L, 1.f / 2.4f) - 0.055f;
	}

	/** A name as the settings compare it: upper case, without spaces, hyphens or underscores. */
	FString Bare(const FString& Text)
	{
		return Text.Replace(TEXT("-"), TEXT("")).Replace(TEXT("_"), TEXT("")).Replace(TEXT(" "), TEXT("")).ToUpper();
	}
}

FLinearColor FGlpPaint::Linear() const
{
	return GlpPaint::ToLinear(Srgb);
}

FLinearColor FGlpPaint::PearlTint() const
{
	return GlpPaint::ToLinear(Pearl > 0.f ? GlpPaint::PearlTintOf(Srgb) : Srgb);
}

FLinearColor GlpPaint::ToLinear(const FLinearColor& Srgb)
{
	return FLinearColor(ChannelToLinear(Srgb.R), ChannelToLinear(Srgb.G), ChannelToLinear(Srgb.B), 1.f);
}

FLinearColor GlpPaint::ToSrgb(const FLinearColor& Linear)
{
	return FLinearColor(ChannelToSrgb(FMath::Max(Linear.R, 0.f)), ChannelToSrgb(FMath::Max(Linear.G, 0.f)), ChannelToSrgb(FMath::Max(Linear.B, 0.f)), 1.f);
}

FLinearColor GlpPaint::FromHsl(float Hue, float Saturation, float Lightness)
{
	const float Chroma = (1.f - FMath::Abs(2.f * Lightness - 1.f)) * Saturation;
	const float H = (Hue - FMath::FloorToFloat(Hue)) * 6.f;
	const float X = Chroma * (1.f - FMath::Abs(FMath::Fmod(H, 2.f) - 1.f));
	float R = 0.f, G = 0.f, B = 0.f;
	if (H < 1.f) { R = Chroma; G = X; }
	else if (H < 2.f) { R = X; G = Chroma; }
	else if (H < 3.f) { G = Chroma; B = X; }
	else if (H < 4.f) { G = X; B = Chroma; }
	else if (H < 5.f) { R = X; B = Chroma; }
	else { R = Chroma; B = X; }
	const float M = Lightness - Chroma * 0.5f;
	return FLinearColor(R + M, G + M, B + M, 1.f);
}

void GlpPaint::ToHsl(const FLinearColor& Srgb, float& Hue, float& Saturation, float& Lightness)
{
	const float Max = FMath::Max3(Srgb.R, Srgb.G, Srgb.B);
	const float Min = FMath::Min3(Srgb.R, Srgb.G, Srgb.B);
	Lightness = (Max + Min) * 0.5f;
	const float Chroma = Max - Min;
	if (Chroma < 1e-5f)
	{
		Hue = 0.f;
		Saturation = 0.f;
		return;
	}
	Saturation = FMath::Clamp(Chroma / (1.f - FMath::Abs(2.f * Lightness - 1.f)), 0.f, 1.f);
	if (Max == Srgb.R)
	{
		Hue = FMath::Fmod((Srgb.G - Srgb.B) / Chroma, 6.f);
	}
	else if (Max == Srgb.G)
	{
		Hue = (Srgb.B - Srgb.R) / Chroma + 2.f;
	}
	else
	{
		Hue = (Srgb.R - Srgb.G) / Chroma + 4.f;
	}
	Hue /= 6.f;
	if (Hue < 0.f)
	{
		Hue += 1.f;
	}
}

FLinearColor GlpPaint::PearlTintOf(const FLinearColor& Srgb)
{
	float Hue = 0.f, Saturation = 0.f, Lightness = 0.f;
	ToHsl(Srgb, Hue, Saturation, Lightness);
	return Saturation < 0.12f ? FromHsl(0.11f, 0.4f, Lightness) : FromHsl(FMath::Fmod(Hue + 0.085f, 1.f), Saturation, Lightness * 0.88f);
}

FGlpPaint GlpPaint::Mixed(const FLinearColor& Srgb, EGlpFinish Finish)
{
	FGlpPaint Paint;
	Paint.Srgb = FLinearColor(FMath::Clamp(Srgb.R, 0.03f, 0.92f), FMath::Clamp(Srgb.G, 0.03f, 0.92f), FMath::Clamp(Srgb.B, 0.03f, 0.92f));
	if (Finish == EGlpFinish::Metallic)
	{
		Paint.Metallic = 0.6f;
		Paint.Roughness = 0.3f;
	}
	else if (Finish == EGlpFinish::Pearl)
	{
		Paint.Metallic = 0.3f;
		Paint.Roughness = 0.32f;
		Paint.Pearl = 1.f;
	}
	return Paint;
}

const TCHAR* GlpPaint::FinishName(EGlpFinish Finish)
{
	return FinishNames[FMath::Clamp(static_cast<int32>(Finish), 0, 2)];
}

int32 GlpPaint::PresetCount()
{
	return UE_ARRAY_COUNT(Presets);
}

const TCHAR* GlpPaint::PresetName(int32 Index)
{
	return Presets[FMath::Clamp(Index, 0, PresetCount() - 1)].Name;
}

TOptional<FGlpPaint> GlpPaint::Preset(int32 Index)
{
	const FPreset& P = Presets[FMath::Clamp(Index, 0, PresetCount() - 1)];
	return P.bOwn ? TOptional<FGlpPaint>() : TOptional<FGlpPaint>(P.Paint);
}

FString GlpPaint::Key(int32 Car)
{
	return FString::Printf(TEXT("paint.%s"), *FString(GlpStyles::Names[FMath::Clamp(Car, 0, GlpStyles::Count - 1)]).ToLower());
}

FString GlpPaint::MixedKey(int32 Car)
{
	return FString::Printf(TEXT("mixed.%s"), *FString(GlpStyles::Names[FMath::Clamp(Car, 0, GlpStyles::Count - 1)]).ToLower());
}

FGlpPaintChoice FGlpPaintChoice::OfPreset(int32 Index)
{
	FGlpPaintChoice Choice;
	Choice.Preset = FMath::Clamp(Index, 0, GlpPaint::PresetCount() - 1);
	return Choice;
}

FGlpPaintChoice FGlpPaintChoice::Mixed(const FLinearColor& Srgb, EGlpFinish Finish)
{
	auto Byte = [](float Value) { return static_cast<uint8>(FMath::Clamp(FMath::RoundToInt(Value * 255.f), 0, 255)); };
	FGlpPaintChoice Choice;
	Choice.Preset = Custom;
	Choice.Colour = FColor(Byte(Srgb.R), Byte(Srgb.G), Byte(Srgb.B));
	Choice.Finish = Finish;
	return Choice;
}

TOptional<FGlpPaint> FGlpPaintChoice::Paint() const
{
	return IsCustom() ? TOptional<FGlpPaint>(GlpPaint::Mixed(Srgb(), Finish)) : GlpPaint::Preset(Preset);
}

FString FGlpPaintChoice::Name() const
{
	return IsCustom() ? FString::Printf(TEXT("CUSTOM %s"), GlpPaint::FinishName(Finish)) : FString(GlpPaint::PresetName(Preset));
}

FString FGlpPaintChoice::Hex() const
{
	return FString::Printf(TEXT("#%02X%02X%02X"), Colour.R, Colour.G, Colour.B);
}

FString FGlpPaintChoice::Format() const
{
	return IsCustom() ? FString::Printf(TEXT("%s %s"), *Hex(), GlpPaint::FinishName(Finish)) : FString(GlpPaint::PresetName(Preset));
}

bool FGlpPaintChoice::TryParse(const FString& Text, FGlpPaintChoice& Out)
{
	const FString Trimmed = Text.TrimStartAndEnd();
	if (Trimmed.IsEmpty())
	{
		return false;
	}
	TArray<FString> Parts;
	const TCHAR* Separators[] = { TEXT(" "), TEXT(":"), TEXT(",") };
	Trimmed.ParseIntoArray(Parts, Separators, UE_ARRAY_COUNT(Separators));
	if (Parts.IsEmpty())
	{
		return false;
	}
	if (Parts[0].StartsWith(TEXT("#")))
	{
		if (Parts[0].Len() != 7 || Parts.Num() > 2)
		{
			return false;
		}
		int32 Channels[3] = {};
		for (int32 i = 0; i < 3; i++)
		{
			const TCHAR High = Parts[0][1 + i * 2];
			const TCHAR Low = Parts[0][2 + i * 2];
			if (!FChar::IsHexDigit(High) || !FChar::IsHexDigit(Low))
			{
				return false;
			}
			Channels[i] = FParse::HexDigit(High) * 16 + FParse::HexDigit(Low);
		}
		EGlpFinish Finish = EGlpFinish::Solid;
		if (Parts.Num() == 2)
		{
			int32 Found = INDEX_NONE;
			for (int32 i = 0; i < UE_ARRAY_COUNT(FinishNames); i++)
			{
				if (Parts[1].Equals(FinishNames[i], ESearchCase::IgnoreCase))
				{
					Found = i;
				}
			}
			if (Found == INDEX_NONE)
			{
				return false;
			}
			Finish = static_cast<EGlpFinish>(Found);
		}
		Out = FGlpPaintChoice();
		Out.Preset = Custom;
		Out.Colour = FColor(static_cast<uint8>(Channels[0]), static_cast<uint8>(Channels[1]), static_cast<uint8>(Channels[2]));
		Out.Finish = Finish;
		return true;
	}
	const FString Wanted = Bare(Trimmed);
	int32 Index = INDEX_NONE;
	for (int32 i = 0; i < GlpPaint::PresetCount(); i++)
	{
		if (Bare(GlpPaint::PresetName(i)) == Wanted)
		{
			Index = i;
		}
	}
	if (Index == INDEX_NONE && Trimmed.IsNumeric() && !Trimmed.Contains(TEXT(".")) && !Trimmed.StartsWith(TEXT("-")) && !Trimmed.StartsWith(TEXT("+")))
	{
		const int32 Number = FCString::Atoi(*Trimmed);
		if (Number >= 0 && Number < GlpPaint::PresetCount())
		{
			Index = Number;
		}
	}
	if (Index == INDEX_NONE)
	{
		return false;
	}
	Out = OfPreset(Index);
	return true;
}

void FGlpPaintBook::Reset(int32 Cars)
{
	Choices.Init(FGlpPaintChoice(), FMath::Max(Cars, 0));
	Mixed.Init(TOptional<FGlpPaintChoice>(), FMath::Max(Cars, 0));
}

const FGlpPaintChoice& FGlpPaintBook::Of(int32 Car) const
{
	static const FGlpPaintChoice Own;
	return Choices.IsValidIndex(Car) ? Choices[Car] : Own;
}

TOptional<FGlpPaintChoice> FGlpPaintBook::LastMixed(int32 Car) const
{
	return Mixed.IsValidIndex(Car) ? Mixed[Car] : TOptional<FGlpPaintChoice>();
}

void FGlpPaintBook::Set(int32 Car, const FGlpPaintChoice& Choice)
{
	if (!Choices.IsValidIndex(Car))
	{
		return;
	}
	Choices[Car] = Choice;
	if (Choice.IsCustom())
	{
		Mixed[Car] = Choice;
	}
}

void FGlpPaintBook::Remember(int32 Car, const FGlpPaintChoice& InMixed)
{
	if (Mixed.IsValidIndex(Car) && InMixed.IsCustom())
	{
		Mixed[Car] = InMixed;
	}
}

bool FGlpPaintBook::ReadLine(const FString& Key, const FString& Value)
{
	const bool bPaint = Key.StartsWith(TEXT("paint."), ESearchCase::IgnoreCase);
	if (!bPaint && !Key.StartsWith(TEXT("mixed."), ESearchCase::IgnoreCase))
	{
		return false;
	}
	for (int32 Car = 0; Car < Choices.Num() && Car < GlpStyles::Count; Car++)
	{
		FGlpPaintChoice Choice;
		if (!Key.Equals(bPaint ? GlpPaint::Key(Car) : GlpPaint::MixedKey(Car), ESearchCase::IgnoreCase) || !FGlpPaintChoice::TryParse(Value, Choice))
		{
			continue;
		}
		if (bPaint)
		{
			Set(Car, Choice);
		}
		else if (!Choices[Car].IsCustom())
		{
			// A car that wears a mixed colour was last mixed in that one, whichever line comes first.
			Remember(Car, Choice);
		}
	}
	return true;
}

FString FGlpPaintBook::Lines() const
{
	FString Text;
	for (int32 Car = 0; Car < Choices.Num() && Car < GlpStyles::Count; Car++)
	{
		if (!Choices[Car].IsOriginal())
		{
			Text += FString::Printf(TEXT("%s=%s\n"), *GlpPaint::Key(Car), *Choices[Car].Format());
		}
		if (Mixed[Car].IsSet() && !Choices[Car].IsCustom())
		{
			Text += FString::Printf(TEXT("%s=%s\n"), *GlpPaint::MixedKey(Car), *Mixed[Car]->Format());
		}
	}
	return Text;
}

int32 GlpPaint::SelfTest(TArray<FString>& Failures)
{
	int32 Checks = 0;
	auto Check = [&Checks, &Failures](bool bHolds, const TCHAR* What)
	{
		Checks++;
		if (!bHolds)
		{
			Failures.Add(What);
		}
	};
	auto Parses = [](const TCHAR* Text, FGlpPaintChoice& Out) { return FGlpPaintChoice::TryParse(Text, Out); };
	FGlpPaintChoice Choice;

	// Every preset by its name, written back as that name; the names in the ways a hand writes them; its number.
	for (int32 i = 0; i < PresetCount(); i++)
	{
		Check(Parses(PresetName(i), Choice) && !Choice.IsCustom() && Choice.Preset == i, TEXT("a preset parses by its name"));
		Check(FGlpPaintChoice::OfPreset(i).Format() == PresetName(i), TEXT("a preset is written as its name"));
		Check(Parses(*FString::FromInt(i), Choice) && Choice.Preset == i, TEXT("a preset parses by its number"));
	}
	Check(Parses(TEXT("dark-blue"), Choice) && Choice.Name() == TEXT("DARK BLUE"), TEXT("dark-blue"));
	Check(Parses(TEXT("darkblue"), Choice) && Choice.Name() == TEXT("DARK BLUE"), TEXT("darkblue"));
	Check(Parses(TEXT("  Dark_Blue "), Choice) && Choice.Name() == TEXT("DARK BLUE"), TEXT("Dark_Blue with spaces round it"));
	Check(FGlpPaintChoice().IsOriginal() && FGlpPaintChoice().Name() == TEXT("ORIGINAL") && !FGlpPaintChoice().Paint().IsSet(), TEXT("the default is the car's own colour"));
	Check(FGlpPaintChoice::OfPreset(99).Preset == PresetCount() - 1 && FGlpPaintChoice::OfPreset(-3).Preset == 0, TEXT("a preset's index is held in range"));

	// A mixed colour: hex, with a finish after a space, a colon or a comma; solid without one.
	Check(Parses(TEXT("#1e4fa0"), Choice) && Choice.IsCustom() && Choice.Colour == FColor(0x1E, 0x4F, 0xA0) && Choice.Finish == EGlpFinish::Solid, TEXT("#1e4fa0 is a solid"));
	Check(Choice.Format() == TEXT("#1E4FA0 SOLID") && Choice.Hex() == TEXT("#1E4FA0") && Choice.Name() == TEXT("CUSTOM SOLID"), TEXT("a mixed colour's text"));
	Check(Parses(TEXT("#1E4FA0 METALLIC"), Choice) && Choice.Finish == EGlpFinish::Metallic, TEXT("#1E4FA0 METALLIC"));
	Check(Parses(TEXT("#1e4fa0:pearl"), Choice) && Choice.Finish == EGlpFinish::Pearl, TEXT("#1e4fa0:pearl"));
	Check(Parses(TEXT("#1e4fa0,metallic"), Choice) && Choice.Finish == EGlpFinish::Metallic, TEXT("#1e4fa0,metallic"));
	for (int32 Finish = 0; Finish < 3; Finish++)
	{
		const FGlpPaintChoice Mixed = FGlpPaintChoice::Mixed(FLinearColor(0.2f, 0.4f, 0.6f), static_cast<EGlpFinish>(Finish));
		Check(Parses(*Mixed.Format(), Choice) && Choice == Mixed, TEXT("a mixed colour reads back as it was written"));
	}

	// What is not a paint.
	const FGlpPaintChoice Before = FGlpPaintChoice::OfPreset(3);
	for (const TCHAR* Bad : { TEXT(""), TEXT("   "), TEXT("#12345"), TEXT("#1234567"), TEXT("#GGGGGG"), TEXT("#1E4FA0 SHINY"), TEXT("#1E4FA0 PEARL X"), TEXT("99"), TEXT("-1"), TEXT("1.5"), TEXT("PURPLE") })
	{
		Choice = Before;
		Check(!Parses(Bad, Choice) && Choice == Before, TEXT("what is not a paint is refused and leaves the choice alone"));
	}

	// The colour arithmetic: sRGB and linear both ways, hue, saturation and lightness both ways.
	for (const FLinearColor& Colour : { FLinearColor(0.76f, 0.03f, 0.05f), FLinearColor(0.05f, 0.10f, 0.30f), FLinearColor(0.92f, 0.70f, 0.05f), FLinearColor(0.5f, 0.5f, 0.5f), FLinearColor(0.f, 0.f, 0.f), FLinearColor(1.f, 1.f, 1.f) })
	{
		Check(ToSrgb(ToLinear(Colour)).Equals(Colour, 1e-4f), TEXT("sRGB to linear and back"));
		float Hue = 0.f, Saturation = 0.f, Lightness = 0.f;
		ToHsl(Colour, Hue, Saturation, Lightness);
		Check(FromHsl(Hue, Saturation, Lightness).Equals(Colour, 1e-4f), TEXT("hue, saturation and lightness and back"));
	}
	Check(ToLinear(FLinearColor(0.5f, 0.5f, 0.5f)).Equals(FLinearColor(0.21404f, 0.21404f, 0.21404f), 1e-4f), TEXT("sRGB 0.5 is 0.214 linear"));
	Check(FromHsl(0.f, 1.f, 0.5f).Equals(FLinearColor(1.f, 0.f, 0.f), 1e-5f) && FromHsl(1.f / 3.f, 1.f, 0.5f).Equals(FLinearColor(0.f, 1.f, 0.f), 1e-5f)
		&& FromHsl(2.f / 3.f, 1.f, 0.5f).Equals(FLinearColor(0.f, 0.f, 1.f), 1e-5f), TEXT("the three primaries at a third of the circle each"));
	Check(FromHsl(0.3f, 0.7f, 1.f).Equals(FLinearColor(1.f, 1.f, 1.f), 1e-5f) && FromHsl(0.3f, 0.7f, 0.f).Equals(FLinearColor(0.f, 0.f, 0.f), 1e-5f), TEXT("full lightness is white, none is black"));

	// The paints as drawn: a mixed colour held inside what a paint reflects, the finishes' base coats, the pearl's flop.
	const FGlpPaint White = Mixed(FLinearColor(1.f, 1.f, 1.f), EGlpFinish::Solid);
	const FGlpPaint Black = Mixed(FLinearColor(0.f, 0.f, 0.f), EGlpFinish::Solid);
	Check(FMath::IsNearlyEqual(White.Srgb.R, 0.92f) && FMath::IsNearlyEqual(Black.Srgb.G, 0.03f), TEXT("a mixed colour is held between 0.03 and 0.92"));
	Check(White.Pearl == 0.f && FMath::IsNearlyEqual(White.Metallic, 0.05f) && FMath::IsNearlyEqual(White.Roughness, 0.38f), TEXT("a solid's base coat"));
	const FGlpPaint Metallic = Mixed(FLinearColor(0.2f, 0.4f, 0.6f), EGlpFinish::Metallic);
	const FGlpPaint Pearl = Mixed(FLinearColor(0.2f, 0.4f, 0.6f), EGlpFinish::Pearl);
	Check(FMath::IsNearlyEqual(Metallic.Metallic, 0.6f) && FMath::IsNearlyEqual(Metallic.Roughness, 0.3f) && Metallic.Pearl == 0.f, TEXT("a metallic's base coat"));
	Check(FMath::IsNearlyEqual(Pearl.Metallic, 0.3f) && FMath::IsNearlyEqual(Pearl.Roughness, 0.32f) && Pearl.Pearl == 1.f, TEXT("a pearl's base coat"));
	Check(Metallic.PearlTint().Equals(Metallic.Linear(), 1e-5f) && !Pearl.PearlTint().Equals(Pearl.Linear(), 1e-3f), TEXT("only a pearl flops to another colour"));
	{
		float Hue = 0.f, Saturation = 0.f, Lightness = 0.f, TintHue = 0.f, TintSaturation = 0.f, TintLightness = 0.f;
		ToHsl(FLinearColor(0.2f, 0.4f, 0.6f), Hue, Saturation, Lightness);
		ToHsl(PearlTintOf(FLinearColor(0.2f, 0.4f, 0.6f)), TintHue, TintSaturation, TintLightness);
		Check(FMath::IsNearlyEqual(TintHue, Hue + 0.085f, 1e-3f) && TintLightness < Lightness, TEXT("a coloured pearl's flop: the hue moved on, a little darker"));
		ToHsl(PearlTintOf(FLinearColor(0.9f, 0.9f, 0.9f)), TintHue, TintSaturation, TintLightness);
		Check(FMath::IsNearlyEqual(TintHue, 0.11f, 1e-3f) && TintSaturation > 0.3f, TEXT("a white pearl flops to its mica's warm gold"));
	}
	Check(Preset(3).IsSet() && FMath::IsNearlyEqual(Preset(3)->Metallic, 0.6f) && FString(PresetName(3)) == TEXT("SILVER"), TEXT("the silver is a metallic"));
	Check(!Preset(0).IsSet() && Preset(1).IsSet() && Preset(1)->Pearl == 0.f, TEXT("ORIGINAL has no paint of its own, RED has"));

	// The book: the settings' lines in, the lines out, the last mixed colour remembered past a preset.
	Check(Key(0) == TEXT("paint.flattwelve") && Key(2) == TEXT("paint.hothatch") && Key(13) == TEXT("paint.cliosixteen"), TEXT("a car's key is its style in lower case"));
	FGlpPaintBook Book;
	Book.Reset(GlpStyles::Count);
	Check(Book.Lines().IsEmpty() && Book.Of(2).IsOriginal() && Book.Of(-1).IsOriginal() && Book.Of(99).IsOriginal(), TEXT("a new book: every car in its own colour"));
	Check(!Book.ReadLine(TEXT("car"), TEXT("3")) && !Book.ReadLine(TEXT("painting"), TEXT("RED")), TEXT("another key is not the book's"));
	Check(Book.ReadLine(TEXT("paint.hothatch"), TEXT("DARK BLUE")) && Book.Of(2).Name() == TEXT("DARK BLUE"), TEXT("a preset read from the settings"));
	Check(Book.ReadLine(TEXT("paint.flattwelve"), TEXT("#1E4FA0 PEARL")) && Book.Of(0).IsCustom() && Book.LastMixed(0).IsSet(), TEXT("a mixed colour read from the settings"));
	Check(Book.ReadLine(TEXT("paint.saloon"), TEXT("NOT A PAINT")) && Book.Of(5).IsOriginal(), TEXT("a paint that cannot be read leaves the car in its own colour"));
	Check(Book.ReadLine(TEXT("paint.nosuchcar"), TEXT("RED")), TEXT("a car this version does not have is skipped without a complaint"));
	Check(Book.Lines() == TEXT("paint.flattwelve=#1E4FA0 PEARL\npaint.hothatch=DARK BLUE\n"), TEXT("the lines the settings keep"));
	const FGlpPaintChoice Mixed0 = Book.Of(0);
	Book.Set(0, FGlpPaintChoice::OfPreset(1));
	Check(Book.Of(0).Name() == TEXT("RED") && Book.LastMixed(0).IsSet() && Book.LastMixed(0).GetValue() == Mixed0, TEXT("the last mixed colour is remembered past a preset"));
	Book.Set(2, FGlpPaintChoice());
	Check(Book.Lines() == TEXT("paint.flattwelve=RED\nmixed.flattwelve=#1E4FA0 PEARL\n"), TEXT("a car back in its own colour has no line; a mixed colour under a preset has its own"));
	FGlpPaintBook Again;
	Again.Reset(GlpStyles::Count);
	TArray<FString> Lines;
	Book.Lines().ParseIntoArrayLines(Lines);
	for (const FString& Line : Lines)
	{
		FString LineKey, LineValue;
		Check(Line.Split(TEXT("="), &LineKey, &LineValue) && Again.ReadLine(LineKey, LineValue), TEXT("a written line reads back"));
	}
	Check(Again.Lines() == Book.Lines(), TEXT("the book reads back as it was written"));
	Check(Again.Of(0).Name() == TEXT("RED") && Again.LastMixed(0).IsSet() && Again.LastMixed(0).GetValue() == Mixed0, TEXT("the last mixed colour comes back with the settings"));

	// The mixed colour's own line: only a mixed colour, never over the one the car wears, and gone with it.
	Check(MixedKey(0) == TEXT("mixed.flattwelve") && MixedKey(13) == TEXT("mixed.cliosixteen"), TEXT("a car's mixed colour's key"));
	FGlpPaintBook Kept;
	Kept.Reset(GlpStyles::Count);
	Check(Kept.ReadLine(TEXT("mixed.estate"), TEXT("RED")) && !Kept.LastMixed(6).IsSet(), TEXT("a preset is not a mixed colour"));
	Check(Kept.ReadLine(TEXT("mixed.estate"), TEXT("#204060 METALLIC")) && Kept.LastMixed(6).IsSet() && Kept.Of(6).IsOriginal(), TEXT("a mixed colour is remembered without the car wearing it"));
	Check(Kept.Lines() == TEXT("mixed.estate=#204060 METALLIC\n"), TEXT("a car in its own colour keeps its mixed colour's line"));
	Check(Kept.ReadLine(TEXT("paint.estate"), TEXT("#AA2010 SOLID")) && Kept.ReadLine(TEXT("mixed.estate"), TEXT("#204060 METALLIC"))
		&& Kept.LastMixed(6).GetValue() == Kept.Of(6) && Kept.Lines() == TEXT("paint.estate=#AA2010 SOLID\n"), TEXT("the colour a car wears is the one it was last mixed in"));
	Kept.Remember(3, FGlpPaintChoice::OfPreset(2));
	Check(!Kept.LastMixed(3).IsSet(), TEXT("only a mixed colour is remembered"));
	Kept.Remember(3, Mixed0);
	Kept.Remember(99, Mixed0);
	Check(Kept.LastMixed(3).IsSet() && Kept.Of(3).IsOriginal() && Kept.Lines().Contains(TEXT("mixed.fivehatch=#1E4FA0 PEARL\n")), TEXT("a colour mixed in the showroom under a preset is kept"));
	return Checks;
}

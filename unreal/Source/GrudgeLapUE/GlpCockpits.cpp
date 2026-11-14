#include "GlpCockpits.h"

#include "Misc/FileHelper.h"

namespace
{
	bool Triple(const FString& Text, FVector3f& Out)
	{
		TArray<FString> Parts;
		Text.ParseIntoArray(Parts, TEXT(","));
		if (Parts.Num() != 3)
		{
			return false;
		}
		Out = FVector3f(FCString::Atof(*Parts[0]), FCString::Atof(*Parts[1]), FCString::Atof(*Parts[2]));
		return true;
	}
}

void FGlpCockpit::Read(const FString& Text)
{
	TArray<FString> Items;
	const TCHAR* Separators[] = { TEXT(";"), TEXT("/") };
	Text.ParseIntoArray(Items, Separators, UE_ARRAY_COUNT(Separators));
	for (const FString& Item : Items)
	{
		FString Name, Value;
		if (!Item.TrimStartAndEnd().Split(TEXT("="), &Name, &Value))
		{
			continue;
		}
		Name = Name.ToLower();
		if (Name == TEXT("eye"))
		{
			Triple(Value, Eye);
		}
		else if (Name == TEXT("fov"))
		{
			Fov = FCString::Atof(*Value);
		}
		else if (Name == TEXT("drop"))
		{
			Drop = FCString::Atof(*Value);
		}
		else if (Name == TEXT("dials"))
		{
			Triple(Value, Dials);
		}
		else if (Name == TEXT("dialsscale"))
		{
			DialsScale = FCString::Atof(*Value);
		}
		else if (Name == TEXT("wheel"))
		{
			Triple(Value, Wheel);
		}
		else if (Name == TEXT("wheelscale"))
		{
			WheelScale = FCString::Atof(*Value);
		}
	}
}

FGlpCockpit GlpCockpits::For(int32 Catalogue)
{
	// Each car's seat as tried from the seat itself (0.78), in FGlpCockpit::Read's text and by catalogue index
	// (VehicleSpec.Catalogue); an empty line keeps the model's seat as prepared.
	static const TCHAR* const Seats[] = {
		TEXT(""),   // 0 the flat-twelve
		// The McLaren's cabin (this car's and the flat-plane V8's): the steering wheel's rim lay across the dials (the owner's
		// report of 14 November 2026). The eyes sat 33 cm over the wheel's hub, a tall driver's in a seat set high; 6 cm lower
		// and with the column 3.5 cm up, as a driver sets it, the dials stand in the wheel's opening.
		TEXT("eye=0,-0.06,0;wheel=0,0.035,0"),   // 1 the V8
		TEXT(""),   // 2 the hot hatch
		// The dials stood at the picture's lower edge, 27 degrees under the eyes.
		TEXT("eye=0,-0.04,0;drop=1.3"),   // 3 the five
		// A 41 cm wheel whose hub covered the dials' lower third.
		TEXT("eye=0,-0.02,0;wheel=0,-0.03,0;wheelscale=0.92;drop=1.2"),   // 4 the turbo hatch
		TEXT(""),   // 5 the saloon (traffic only)
		TEXT(""),   // 6 the estate
		TEXT(""),   // 7 the fleet saloon
		TEXT("eye=0,-0.06,0;wheel=0,0.035,0"),   // 8 the flat-plane V8
		TEXT(""),   // 9 (traffic only)
		TEXT(""),   // 10 (traffic only)
		TEXT(""),   // 11 (traffic only)
		TEXT(""),   // 12 the boxer
		TEXT(""),   // 13 the sixteen-valve hatch
	};
	FGlpCockpit Cockpit;
	if (Catalogue >= 0 && Catalogue < UE_ARRAY_COUNT(Seats))
	{
		Cockpit.Read(Seats[Catalogue]);
	}
	return Cockpit;
}

TMap<int32, FGlpCockpit> GlpCockpits::Load(const FString& File)
{
	TMap<int32, FGlpCockpit> Cockpits;
	TArray<FString> Lines;
	if (!FFileHelper::LoadFileToStringArray(Lines, *File))
	{
		return Cockpits;
	}
	for (const FString& Line : Lines)
	{
		FString Index, Text;
		if (Line.StartsWith(TEXT("#")) || !Line.Split(TEXT(":"), &Index, &Text))
		{
			continue;
		}
		const int32 Catalogue = FCString::Atoi(*Index);
		FGlpCockpit Cockpit = For(Catalogue);
		Cockpit.Read(Text);
		Cockpits.Add(Catalogue, Cockpit);
	}
	return Cockpits;
}

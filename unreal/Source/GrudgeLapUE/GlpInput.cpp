#include "GlpInput.h"

#include "GameFramework/PlayerController.h"
#include "Misc/CommandLine.h"

namespace
{
	// The keys the game reads (InputSetup.Register); a scripted run can only press these.
	const FKey Watched[] = {
		EKeys::Left, EKeys::A, EKeys::Right, EKeys::D, EKeys::Up, EKeys::W, EKeys::Down, EKeys::S,
		EKeys::LeftShift, EKeys::RightShift, EKeys::E, EKeys::PageUp, EKeys::Q, EKeys::PageDown,
		EKeys::One, EKeys::Two, EKeys::Three, EKeys::Four, EKeys::Five, EKeys::Six, EKeys::Seven,
		EKeys::N, EKeys::R, EKeys::Enter, EKeys::SpaceBar, EKeys::C, EKeys::U, EKeys::F5, EKeys::F8, EKeys::F9,
		EKeys::F11, EKeys::F12,
	};

	float MoveToward(float Current, float Target, float MaxDelta)
	{
		return FMath::Abs(Target - Current) <= MaxDelta ? Target : Current + FMath::Sign(Target - Current) * MaxDelta;
	}

	/** Pedals ramp up over RiseSeconds and down over FallSeconds (KeyboardDriver.Ramp). */
	float Ramp(float Current, bool bPressed, float DeltaSeconds, float RiseSeconds, float FallSeconds)
	{
		return MoveToward(Current, bPressed ? 1.f : 0.f, DeltaSeconds / (bPressed ? RiseSeconds : FallSeconds));
	}
}

FGlpInput::FGlpInput()
{
	FString Keys;
	if (!FParse::Value(FCommandLine::Get(), TEXT("GlpKeys="), Keys, false))
	{
		return;
	}
	// "+" as well as ",": cmd splits batch arguments at commas.
	TArray<FString> Entries;
	const TCHAR* Separators[] = { TEXT(","), TEXT("+") };
	Keys.ParseIntoArray(Entries, Separators, UE_ARRAY_COUNT(Separators));
	for (const FString& Entry : Entries)
	{
		FString When, Name;
		if (!Entry.Split(TEXT(":"), &When, &Name))
		{
			continue;
		}
		FScripted Press;
		Press.Key = FKey(*Name);
		FString From, To;
		if (When.Split(TEXT("-"), &From, &To))
		{
			Press.From = FCString::Atof(*From);
			Press.To = FCString::Atof(*To);
		}
		else
		{
			// A tap: held for a tenth of a second, long enough to be seen by any frame rate.
			Press.From = FCString::Atof(*When);
			Press.To = Press.From + 0.1f;
		}
		if (Press.Key.IsValid())
		{
			Script.Add(Press);
		}
	}
}

void FGlpInput::Update(const APlayerController* Player, float Time)
{
	WasHeld = MoveTemp(Held);
	Held.Reset();
	for (const FKey& Key : Watched)
	{
		if (Player && Player->IsInputKeyDown(Key))
		{
			Held.Add(Key);
		}
	}
	for (const FScripted& Press : Script)
	{
		if (Time >= Press.From && Time < Press.To)
		{
			Held.Add(Press.Key);
		}
	}
}

bool FGlpInput::Down(const FKey& Key) const
{
	return Held.Contains(Key);
}

bool FGlpInput::Pressed(const FKey& Key) const
{
	return Held.Contains(Key) && !WasHeld.Contains(Key);
}

glp_inputs FGlpInput::Drive(float DeltaSeconds, float SpeedMps)
{
	const bool bLeft = Down(EKeys::Left) || Down(EKeys::A);
	const bool bRight = Down(EKeys::Right) || Down(EKeys::D);
	const float Target = (bRight ? 1.f : 0.f) - (bLeft ? 1.f : 0.f);
	// Full lock takes about 0.35 s parked and 0.7 s at speed; centring is quicker, like a self-aligning wheel.
	const float SpeedFactor = FMath::Clamp(SpeedMps / 50.f, 0.f, 1.f);
	const float Rate = Target != 0.f ? FMath::Lerp(2.8f, 1.4f, SpeedFactor) : 4.5f;
	Steer = MoveToward(Steer, Target, Rate * DeltaSeconds);
	Throttle = Ramp(Throttle, Down(EKeys::Up) || Down(EKeys::W), DeltaSeconds, 0.18f, 0.12f);
	Brake = Ramp(Brake, Down(EKeys::Down) || Down(EKeys::S), DeltaSeconds, 0.15f, 0.10f);
	Clutch = Ramp(Clutch, Down(EKeys::LeftShift) || Down(EKeys::RightShift), DeltaSeconds, 0.12f, 0.25f);

	glp_inputs Input{};
	Input.steer = Steer;
	Input.throttle = Throttle;
	Input.brake = Brake;
	Input.clutch = Clutch;
	Input.gear = GLP_NO_GEAR_CHANGE;
	const FKey Gears[] = { EKeys::One, EKeys::Two, EKeys::Three, EKeys::Four, EKeys::Five, EKeys::Six, EKeys::Seven };
	for (int32 i = 0; i < UE_ARRAY_COUNT(Gears); i++)
	{
		if (Pressed(Gears[i]))
		{
			Input.gear = i + 1;
		}
	}
	if (Pressed(EKeys::N))
	{
		Input.gear = 0;
	}
	if (Pressed(EKeys::R))
	{
		Input.gear = -1;
	}
	Input.shift_up = (Pressed(EKeys::E) || Pressed(EKeys::PageUp)) ? 1 : 0;
	Input.shift_down = (Pressed(EKeys::Q) || Pressed(EKeys::PageDown)) ? 1 : 0;
	Input.ignition = Pressed(EKeys::Enter) ? 1 : 0;
	return Input;
}

void FGlpInput::Reset()
{
	Steer = Throttle = Brake = Clutch = 0.f;
}

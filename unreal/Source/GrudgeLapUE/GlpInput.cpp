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
		// The pad's buttons (InputSetup): the stick and the triggers are read as axes.
		EKeys::Gamepad_DPad_Left, EKeys::Gamepad_DPad_Right, EKeys::Gamepad_DPad_Up, EKeys::Gamepad_DPad_Down,
		EKeys::Gamepad_LeftStick_Left, EKeys::Gamepad_LeftStick_Right,
		EKeys::Gamepad_FaceButton_Bottom, EKeys::Gamepad_FaceButton_Right, EKeys::Gamepad_FaceButton_Left, EKeys::Gamepad_FaceButton_Top,
		EKeys::Gamepad_LeftShoulder, EKeys::Gamepad_RightShoulder, EKeys::Gamepad_Special_Left, EKeys::Gamepad_Special_Right,
		EKeys::Gamepad_LeftThumbstick, EKeys::Gamepad_RightThumbstick,
	};

	// Stick travel below this is rest (InputSetup.StickDeadzone).
	constexpr float StickDeadzone = 0.12f;

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
	StickX = Player ? Player->GetInputAnalogKeyState(EKeys::Gamepad_LeftX) : 0.f;
	RightTrigger = Player ? Player->GetInputAnalogKeyState(EKeys::Gamepad_RightTriggerAxis) : 0.f;
	LeftTrigger = Player ? Player->GetInputAnalogKeyState(EKeys::Gamepad_LeftTriggerAxis) : 0.f;
	for (FScripted& Press : Script)
	{
		// Held for its span, and for at least one frame: a slow frame (the machine busy elsewhere) can step over
		// a tap's tenth of a second, and the run then waits on the title screen for a key that never came.
		if (Time >= Press.From && (Time < Press.To || !Press.bSeen))
		{
			Held.Add(Press.Key);
			Press.bSeen = true;
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
	if (FMath::Abs(StickX) > StickDeadzone)
	{
		// The stick is the wheel: its travel past the dead zone, bent a little for finesse near the centre, reached
		// quickly enough that a flick is a flick but without the stick's jitter (KeyboardDriver).
		const float Travel = (FMath::Abs(StickX) - StickDeadzone) / (1.f - StickDeadzone);
		Steer = MoveToward(Steer, FMath::Sign(StickX) * FMath::Pow(Travel, 1.6f), 8.f * DeltaSeconds);
	}
	else
	{
		const bool bLeft = Down(EKeys::Left) || Down(EKeys::A) || Down(EKeys::Gamepad_DPad_Left);
		const bool bRight = Down(EKeys::Right) || Down(EKeys::D) || Down(EKeys::Gamepad_DPad_Right);
		const float Target = (bRight ? 1.f : 0.f) - (bLeft ? 1.f : 0.f);
		// Full lock takes about 0.35 s parked and 0.7 s at speed; centring is quicker, like a self-aligning wheel.
		const float SpeedFactor = FMath::Clamp(SpeedMps / 50.f, 0.f, 1.f);
		const float Rate = Target != 0.f ? FMath::Lerp(2.8f, 1.4f, SpeedFactor) : 4.5f;
		Steer = MoveToward(Steer, Target, Rate * DeltaSeconds);
	}
	// A pedal is the trigger's position while it is pressed at all, the key's ramp otherwise.
	Throttle = RightTrigger > 0.02f ? FMath::Clamp(RightTrigger, 0.f, 1.f) : Ramp(Throttle, Down(EKeys::Up) || Down(EKeys::W) || Down(EKeys::Gamepad_DPad_Up), DeltaSeconds, 0.18f, 0.12f);
	Brake = LeftTrigger > 0.02f ? FMath::Clamp(LeftTrigger, 0.f, 1.f) : Ramp(Brake, Down(EKeys::Down) || Down(EKeys::S) || Down(EKeys::Gamepad_DPad_Down), DeltaSeconds, 0.15f, 0.10f);
	Clutch = Ramp(Clutch, Down(EKeys::LeftShift) || Down(EKeys::RightShift) || Down(EKeys::Gamepad_FaceButton_Left), DeltaSeconds, 0.12f, 0.25f);

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
	if (Pressed(EKeys::N) || Pressed(EKeys::Gamepad_LeftThumbstick))
	{
		Input.gear = 0;
	}
	if (Pressed(EKeys::R) || Pressed(EKeys::Gamepad_RightThumbstick))
	{
		Input.gear = -1;
	}
	Input.shift_up = (Pressed(EKeys::E) || Pressed(EKeys::PageUp) || Pressed(EKeys::Gamepad_RightShoulder)) ? 1 : 0;
	Input.shift_down = (Pressed(EKeys::Q) || Pressed(EKeys::PageDown) || Pressed(EKeys::Gamepad_LeftShoulder)) ? 1 : 0;
	Input.ignition = (Pressed(EKeys::Enter) || Pressed(EKeys::Gamepad_FaceButton_Bottom) || Pressed(EKeys::Gamepad_Special_Right)) ? 1 : 0;
	return Input;
}

void FGlpInput::Reset()
{
	Steer = Throttle = Brake = Clutch = 0.f;
}

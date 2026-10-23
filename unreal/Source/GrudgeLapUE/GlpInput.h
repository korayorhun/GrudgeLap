#pragma once

#include "CoreMinimal.h"
#include "InputCoreTypes.h"
#include "grudgelap_physics.h"

class APlayerController;

/**
 * The keyboard, read the game's way (InputSetup and KeyboardDriver): held keys become control positions that
 * ramp like hands and feet do, gear keys and toggles act on the press.
 *
 * Automated runs can play keys from the command line: -GlpKeys=1:Enter,3-9:Up,4.5:E presses Enter at 1 s, holds
 * Up from 3 to 9 s and taps E at 4.5 s, in seconds since the level started (key names as FKey knows them; "+" may
 * stand for ",", which cmd would split the argument at).
 */
class FGlpInput
{
public:
	FGlpInput();

	/** Reads the frame's keys; call once per tick before the queries. */
	void Update(const APlayerController* Player, float Time);

	bool Down(const FKey& Key) const;
	bool Pressed(const FKey& Key) const;

	/** Driver inputs for this frame (KeyboardDriver.Sample). */
	glp_inputs Drive(float DeltaSeconds, float SpeedMps);

	void Reset();

private:
	struct FScripted
	{
		FKey Key;
		float From = 0.f;
		float To = 0.f;
	};
	TArray<FScripted> Script;
	TSet<FKey> Held;
	TSet<FKey> WasHeld;
	float Steer = 0.f;
	float Throttle = 0.f;
	float Brake = 0.f;
	float Clutch = 0.f;
};

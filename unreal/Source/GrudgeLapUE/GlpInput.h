#pragma once

#include "CoreMinimal.h"
#include "InputCoreTypes.h"
#include "grudgelap_physics.h"

class APlayerController;

/**
 * The keyboard and the gamepad, read the game's way (InputSetup and KeyboardDriver): held keys become control
 * positions that ramp like hands and feet do, the pad's stick and triggers are positions already, gear keys and
 * toggles act on the press. The pad's layout is the Godot version's: left stick steers, the triggers are the
 * pedals, the shoulders shift, A starts, B skips, X is the clutch, Y the camera, the d-pad stands in for all.
 *
 * Automated runs can play keys from the command line: -GlpKeys=1:Enter,3-9:Up,4.5:E presses Enter at 1 s, holds
 * Up from 3 to 9 s and taps E at 4.5 s, in seconds since the level started (key names as FKey knows them; "+" may
 * stand for ",", which cmd would split the argument at). The Esc menu's mouse can be played too:
 * -GlpMouse=6:800,420+7:800,420:click puts it at that point of the HUD's 1600 x 900 canvas at 6 s and clicks there at 7.
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

	/** The command line plays keys (-GlpKeys). */
	bool HasScript() const { return !Script.IsEmpty(); }

	/**
	 * Whether the last Drive took each pedal from a key (all or nothing, which the simulation's core eases at the limit
	 * of grip: glp_feather_inputs) and not from a pad's trigger (a position the player holds).
	 */
	bool ThrottleFromKey() const { return bThrottleKey; }
	bool BrakeFromKey() const { return bBrakeKey; }

	/**
	 * A scripted run's mouse (-GlpMouse): where it stands on the HUD's canvas and whether it clicks on this frame. False
	 * when the run has none, or none yet: the real mouse then counts.
	 */
	bool ScriptedMouse(FVector2f& Point, bool& bClick) const;

private:
	struct FScripted
	{
		FKey Key;
		float From = 0.f;
		float To = 0.f;
		bool bSeen = false;
	};
	TArray<FScripted> Script;
	struct FMouse
	{
		float At = 0.f;
		FVector2f Point = FVector2f::ZeroVector;
		bool bClick = false;
	};
	TArray<FMouse> Mouse;
	int32 MouseNow = INDEX_NONE;   // the last entry whose time has come
	bool bMouseClick = false;      // it clicks on this frame
	TSet<FKey> Held;
	TSet<FKey> WasHeld;
	float Steer = 0.f;
	float Throttle = 0.f;
	float Brake = 0.f;
	float Clutch = 0.f;
	float StickX = 0.f;         // the pad's left stick, -1 to 1
	float RightTrigger = 0.f;   // 0 to 1
	float LeftTrigger = 0.f;
	bool bThrottleKey = true;
	bool bBrakeKey = true;
};

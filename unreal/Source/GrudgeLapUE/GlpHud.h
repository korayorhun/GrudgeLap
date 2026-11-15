#pragma once

#include "CoreMinimal.h"
#include "GameFramework/GameModeBase.h"
#include "GameFramework/HUD.h"
#include "GlpHud.generated.h"

class AGlpGame;

/**
 * The frame rate the HUD shows (F6, the Godot version's counter): the frames drawn in each half second of a clock.
 * A window's count and not one frame's time turned over: the figure stands long enough to be read, and a single slow
 * frame moves it by what it cost and no more.
 */
struct FGlpFrameRate
{
	static constexpr double Window = 0.5;

	/** A frame is being drawn at this time of a clock that runs in seconds. */
	void Frame(double Now);
	/** Frames a second over the last whole window; 0 until the first has passed. */
	int32 Shown() const { return Rate; }

private:
	double Since = -1.0;   // the window's start; negative before the first frame
	int32 Frames = 0;
	int32 Rate = 0;
};

/** What the readouts under the clock say and where they stand, apart from the drawing so that the game can check them. */
namespace GlpHudLines
{
	/** The film view's black bars, top and foot, on the HUD's 1600 x 900 layout. */
	inline constexpr float FilmBar = 94.f;
	/** From one line under the clock to the next. */
	inline constexpr float Pitch = 20.f;

	/** The credit's weather as the HUD names it (WeatherModel.Label): nothing on a clear day. */
	const TCHAR* WeatherLabel(int32 Weather);
	/**
	 * The baseline of the first line under the clock: below the readouts' band, as in the Godot version; in the film
	 * view inside its top bar, close under the clock, where two lines have room.
	 */
	float First(bool bFilm);
	/** The HUD's own checks (-GlpSelfTest). Returns how many were made. */
	int32 SelfTest(TArray<FString>& Failures);
}

/**
 * The race readouts (Hud.cs), drawn on the canvas over the 3D view: time, score, lap and route at the top, the
 * centre messages, the title screen with its car and gearbox choices, the game-over line, the keys for the first
 * seconds of a run, and outside the cockpit the instrument panel at the foot of the screen, sliding sideways with
 * the lateral G like the original's dashboard. Laid out on 1600 x 900 and scaled to the window, as the Godot
 * version's canvas_items stretch does.
 */
UCLASS()
class AGlpHud : public AHUD
{
	GENERATED_BODY()

public:
	virtual void DrawHUD() override;

private:
	AGlpGame* Game();

	TWeakObjectPtr<AGlpGame> GameActor;
	/** Counted on every frame, shown or not: F6 finds a figure that is already right. */
	FGlpFrameRate FrameRate;
};

/** The game's mode: no pawn (AGlpGame owns the camera and reads the keyboard), the HUD above. */
UCLASS()
class AGlpGameMode : public AGameModeBase
{
	GENERATED_BODY()

public:
	AGlpGameMode();
};

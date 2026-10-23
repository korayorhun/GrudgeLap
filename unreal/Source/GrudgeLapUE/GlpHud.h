#pragma once

#include "CoreMinimal.h"
#include "GameFramework/GameModeBase.h"
#include "GameFramework/HUD.h"
#include "GlpHud.generated.h"

class AGlpGame;

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
};

/** The game's mode: no pawn (AGlpGame owns the camera and reads the keyboard), the HUD above. */
UCLASS()
class AGlpGameMode : public AGameModeBase
{
	GENERATED_BODY()

public:
	AGlpGameMode();
};

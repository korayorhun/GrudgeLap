#pragma once

#include "CoreMinimal.h"

/** What a line of the Esc menu is for. */
enum class EGlpMenuLine : uint8
{
	Continue,
	NewGame,
	Car,
	Paint,
	Camera,
	Upscaling,
	RayReconstruction,
	Weather,
	Light,
	Units,
	FullScreen,
	FrameRate,
	Update,
	Quit,
};

/**
 * The Esc menu (0.78), the Godot version's PauseMenu with the Unreal version's own lines: the upscaler's mode (DLSS on
 * an RTX card) and the update button the owner asked for on 14 November 2026. The game fills it, reads the keys and
 * the mouse and acts; the HUD draws it. Both take the layout from here, on the HUD's 1600 x 900 canvas: a panel in the
 * middle of the screen, a title, one line per choice, a note under the lines and the keys along the foot.
 */
struct FGlpMenu
{
	struct FLine
	{
		EGlpMenuLine Kind = EGlpMenuLine::Continue;
		FString Text;
	};

	bool bOpen = false;
	int32 Cursor = 0;
	TArray<FLine> Lines;
	/** One line under the choices: what the updater says, or what a choice waits for. */
	FString Note;
	/** The note reports a failure: drawn in amber, not in the calm blue. */
	bool bNoteWarns = false;

	static constexpr float Width = 680.f;
	static constexpr float Margin = 40.f;       // from the panel's edge to a line's box
	static constexpr float LineTop = 90.f;      // from the panel's top to the first line's box
	static constexpr float LinePitch = 44.f;
	static constexpr float LineHeight = 40.f;
	static constexpr float Foot = 104.f;        // under the last line: the note and the keys

	float Height() const { return LineTop + Lines.Num() * LinePitch + Foot; }

	/** The panel's top left corner on a canvas of the given size. */
	FVector2f Origin(const FVector2f& Canvas) const
	{
		return FVector2f(Canvas.X * 0.5f - Width * 0.5f, FMath::Max(4.f, Canvas.Y * 0.5f - Height() * 0.5f));
	}

	/** The line under a point of the canvas, INDEX_NONE when there is none. */
	int32 LineAt(const FVector2f& Canvas, const FVector2f& Point) const
	{
		const FVector2f Corner = Origin(Canvas);
		if (Point.X < Corner.X + Margin || Point.X > Corner.X + Width - Margin)
		{
			return INDEX_NONE;
		}
		const float Down = Point.Y - Corner.Y - LineTop;
		const int32 Line = FMath::FloorToInt32(Down / LinePitch);
		return Down >= 0.f && Lines.IsValidIndex(Line) && Down - Line * LinePitch <= LineHeight ? Line : INDEX_NONE;
	}

	int32 Find(EGlpMenuLine Kind) const
	{
		return Lines.IndexOfByPredicate([Kind](const FLine& Line) { return Line.Kind == Kind; });
	}
};

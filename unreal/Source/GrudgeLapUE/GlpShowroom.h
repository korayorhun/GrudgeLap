#pragma once

#include "CoreMinimal.h"
#include "GlpPaint.h"

/** A rectangle of the HUD's 1600 x 900 canvas (wider on a wider window). */
struct FGlpRect
{
	float X = 0.f;
	float Y = 0.f;
	float W = 0.f;
	float H = 0.f;

	float Right() const { return X + W; }
	float Bottom() const { return Y + H; }
	/** The point lies in the rectangle, grown by Grow on every side. */
	bool Has(const FVector2f& Point, float Grow = 0.f) const
	{
		return Point.X >= X - Grow && Point.X <= X + W + Grow && Point.Y >= Y - Grow && Point.Y <= Y + H + Grow;
	}
};

/**
 * The showroom's colour picker (the Godot version's ColourPicker, 0.25): a square of saturation (across) and lightness
 * (down) beside a bar of hue, the three as rows the keys step through with the finish under them, so the player can
 * mix a car's colour by keyboard, pad or mouse and see it on the turning car as it changes. HSL rather than HSV: the
 * lightness runs from black through the full colour to white, so the pastels and the dark shades of one hue lie on
 * one line of the square. The layout of what the mouse can hit lives here, so the HUD's drawing and the hit test agree.
 */
class FGlpColourPicker
{
public:
	/** The rows the keys move between: up and down choose one, left and right change it. */
	enum class ERow : uint8 { Hue, Saturation, Lightness, Finish };
	/** What a click on the picker lands on. */
	enum class EHit : uint8 { Nothing, Square, HueBar, Finish, Done, Cancel, Panel };

	/**
	 * Start is the colour it opens on, Before the car's paint before it opened (put back by CANCEL), KeptHue the hue the
	 * picker had when it last closed on this car: a grey has no hue of its own and opened at red wherever the player had
	 * left the bar; the last hue is taken back when it gives the same colour.
	 */
	FGlpColourPicker(const FGlpPaintChoice& Start, const FGlpPaintChoice& InBefore, TOptional<float> KeptHue);

	float Hue = 0.f;
	float Saturation = 0.f;
	float Lightness = 0.f;
	EGlpFinish Finish = EGlpFinish::Solid;
	ERow Focus = ERow::Hue;
	FGlpPaintChoice Before;
	/** The mouse's button went down on the square or the hue bar and is held: the value follows the mouse. */
	EHit Dragging = EHit::Nothing;

	FGlpPaintChoice Choice() const;
	void MoveFocus(int32 Step);
	/**
	 * Left and right on the focused row: a tap moves a value one step (a degree of hue, a per cent of saturation or
	 * lightness) or the finish to the next; held for a quarter of a second, a value runs on, faster the longer it is
	 * held (from 30 to 150 steps a second). True when the colour or the finish changed.
	 */
	bool Nudge(int32 Held, int32 Pressed, float DeltaSeconds);
	/** The mouse on the square or the hue bar, clamped to them while it is dragged. */
	void Pick(const FVector2f& Canvas, EHit Where, const FVector2f& Point);
	void SetFinish(EGlpFinish InFinish);
	/** The picker set to a mixed colour as a whole (automation: -GlpShowroomPaint=#RRGGBB:finish). */
	void Set(const FGlpPaintChoice& Choice);

	// The layout: the panel on the right of the stage, clear of the right chevron, between the bar and the showroom's panel.
	static FGlpRect Panel(const FVector2f& Canvas);
	static FGlpRect Square(const FVector2f& Canvas);
	static FGlpRect HueBar(const FVector2f& Canvas);
	static FGlpRect RowRect(const FVector2f& Canvas, ERow Row);
	static FGlpRect FinishButton(const FVector2f& Canvas, EGlpFinish Finish);
	static FGlpRect DoneButton(const FVector2f& Canvas);
	static FGlpRect CancelButton(const FVector2f& Canvas);
	static EHit HitTest(const FVector2f& Canvas, const FVector2f& Point, EGlpFinish& OutFinish);

private:
	float HeldFor = 0.f;   // seconds left or right has been held on a value row
};

/**
 * The showroom (0.79; the Godot version's Showroom, 0.25): the car the player is choosing seen from outside and turning
 * slowly through a full circle, with every car of the line-up a step to the left or right and the paints a step up or
 * down: the car's own colour, the presets and a colour of the player's own, mixed in the picker in a finish and seen on
 * the car as it is mixed. The owner asked for it on 2 November 2026 ("araba seçtiğimiz bir menü olsa ve arabalar 360
 * derece rotate olsa", "varsayılan renkleri olsun + kullanıcı custom seçebilsin").
 *
 * Where the Godot version turned the car on a turntable in a grey studio of its own, this one leaves the car where the
 * credit starts, on the track in the day's light, and carries the camera round it: the level's sun, sky and road are
 * the photograph this version is built for, and a closed studio inside that level would have had to fight its sun, its
 * sky light, its fog and its fixed exposure. What the player sees is the same turn, and the colour as it will look on
 * the road.
 *
 * This is the state and the layout; the game moves the cars and the camera by it (AGlpGame::RunShowroom) and the HUD
 * draws it (AGlpHud). The paints chosen here are edits until the player takes a car: the game writes them to the
 * settings then, and leaving with Esc forgets them.
 */
struct FGlpShowroom
{
	/** What a click lands on (the picker's own parts are FGlpColourPicker::HitTest's). */
	enum class EHit : uint8 { Nothing, Left, Right, Take, Back, Swatch };

	/** The camera's turn round the car, rad/s: a full circle in about 24 s, slow enough to look at any panel as it passes. */
	static constexpr float TurnRate = 0.26f;
	/** The first view, degrees from straight ahead of the nose towards the driver's side: the front three-quarter. */
	static constexpr float StartBearing = 35.f;
	/** The lens, degrees upright: long, as car photographers use, so the body is not drawn in perspective. */
	static constexpr float CameraFov = 32.f;
	static constexpr float CameraPitch = 11.f;
	/**
	 * How far under the car's middle the camera aims, as a share of its distance: 0.098 stands the car over the middle
	 * of the stage between the bar and the panel (the Godot showroom's measure).
	 */
	static constexpr float AimDrop = 0.098f;
	static constexpr float DragRadiansPerPixel = 0.007f;

	bool bOpen = false;
	/** Opened from the Esc menu rather than from the title screen: Esc returns there. */
	bool bFromMenu = false;
	/** Automation (-GlpTurn): the camera held at its bearing for a picture; a drag still turns it. */
	bool bHeld = false;
	/** The car on show, as an index into the catalogue, and the one that was driven when the showroom opened. */
	int32 Index = 0;
	int32 Driven = 0;
	/** The car's own colour (sRGB), for the ORIGINAL swatch and the picker's first colour; the game sets it with the car. */
	FLinearColor OwnSrgb = FLinearColor(0.76f, 0.03f, 0.05f);
	const FGlpPaintBook* Book = nullptr;

	/**
	 * The camera's place round the car, rad: 0 straight ahead of the nose, growing towards the car's right. As it grows
	 * the side of the car that faces the camera moves to the right of the picture, as it did on the Godot version's
	 * turntable, so a drag to the right carries the car's near side with it.
	 */
	float Yaw = 0.f;
	float Spin = 0.f;           // the drag's angular velocity, rad/s, dying away once the button is up
	float DragTurn = 0.f;       // the turn dragged since the last frame, rad
	float Zoom = 1.f;
	float Reach = 2.4f;         // the radius the car sweeps, m, smoothed: frames the camera
	float Distance = 0.f;       // the camera's distance from its aim, m, smoothed
	float Shift = 0.f;          // the camera's sideways move while the picker is open, m, smoothed
	bool bDragging = false;     // the mouse's button is down on the stage
	FVector2f Mouse = FVector2f(-1.f, -1.f);

	TMap<int32, FGlpPaintChoice> Edits;       // the paints chosen here, by car, until taken
	TMap<int32, FGlpPaintChoice> MixedHere;   // the colour each car was last mixed in here
	TOptional<FGlpPaintChoice> MixedBefore;   // the car's mixed colour when the picker opened
	TMap<int32, float> MixedHue;              // the picker's hue when it last closed, by car
	TOptional<FGlpColourPicker> Picker;

	/** BearingDegrees: where the camera starts, from straight ahead of the nose towards the driver's side (the car's left). */
	void Open(int32 Car, const FGlpPaintBook* InBook, bool bInFromMenu, float BearingDegrees, bool bInHeld);
	void Close();

	/** The swatches: the presets and the mixed colour. */
	static int32 SlotCount() { return GlpPaint::PresetCount() + 1; }
	static int32 CustomSlot() { return GlpPaint::PresetCount(); }

	/** A car's paint: chosen here, or the one its settings keep. */
	FGlpPaintChoice ChoiceOf(int32 Car) const;
	FGlpPaintChoice Choice() const { return ChoiceOf(Index); }
	/** The paint as a place in the row of swatches. */
	int32 PaintSlot() const;
	/**
	 * The colour a car's picker starts from: the one it was last mixed in (here, or kept in the settings), else its own
	 * colour as a solid, so a player who wants the factory colour a shade darker starts from it.
	 */
	FGlpPaintChoice MixedFor(int32 Car) const;
	FGlpPaintChoice MixedChoice() const { return MixedFor(Index); }

	void SetChoice(const FGlpPaintChoice& Choice);
	/** A swatch taken: a preset, or the mixed colour. */
	void ChooseSlot(int32 Slot);
	/** Steps the paint (up or down) through the presets and the mixed colour. */
	void CyclePaint(int32 Step) { ChooseSlot((PaintSlot() + Step + SlotCount()) % SlotCount()); }
	void OpenPicker();
	/** Closes the picker: the mixed colour kept on the car, or (cancelled) the car and its mixed swatch put back. */
	void ClosePicker(bool bKeep);
	/** The picker's colour or finish changed: it is the car's paint as it turns. */
	void ApplyPicker();
	/** Another car comes on show: a colour being mixed stays on the car it was mixed for. */
	void Show(int32 Car);

	/** The mouse dragged across the stage by Pixels (right positive): the view follows, and keeps a little of the motion when let go. */
	void Drag(float Pixels);
	/** The wheel: closer (positive steps) or further. */
	void ZoomBy(int32 Steps);
	/**
	 * A frame: the turn, and the camera's framing eased towards the car on show. TargetReach is the radius that car
	 * sweeps and Middle the height of its middle over the ground, both in metres.
	 */
	void Advance(float DeltaSeconds, const FVector2f& Canvas, float TargetReach, float Middle);

	// The layout, shared by the HUD's drawing and the mouse's hit test.
	static FGlpRect Panel(const FVector2f& Canvas);
	static FGlpRect LeftArrow(const FVector2f& Canvas);
	static FGlpRect RightArrow(const FVector2f& Canvas);
	static FGlpRect Swatch(const FVector2f& Canvas, int32 Slot);
	static FGlpRect TakeButton(const FVector2f& Canvas);
	static FGlpRect BackButton(const FVector2f& Canvas);
	static EHit HitTest(const FVector2f& Canvas, const FVector2f& Point, int32& OutSlot);

	/**
	 * The showroom's and the picker's own checks (-GlpSelfTest): the paints chosen, mixed, cancelled and kept, the
	 * picker's keys and mouse, the layout the mouse hits, the turn and the framing. Returns how many checks ran; each
	 * one that failed adds its text to Failures.
	 */
	static int32 SelfTest(TArray<FString>& Failures);

private:
	/** The camera's distance from its aim, m, for the car's sweep, the panel and the picker. */
	float CameraDistance(const FVector2f& Canvas, float Middle) const;
	/** The distance at which the nearest point the car sweeps on the ground is drawn 16 px over the panel. */
	float FloorFit(const FVector2f& Canvas, float Middle) const;
	/** The camera's sideways move while the picker is open, m: the car centred in the stage left of the picker's panel. */
	float PickerShift(const FVector2f& Canvas) const;
};

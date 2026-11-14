#include "GlpShowroom.h"

namespace
{
	FGlpRect Inside(const FGlpRect& Panel, float X, float Y, float W, float H)
	{
		return { Panel.X + X, Panel.Y + Y, W, H };
	}

	/** The tangent of half the lens's upright angle on a canvas of this shape: the engine holds the angle across (GlpSpace::HorizontalFov, 16:9). */
	float TanHalfUpright(const FVector2f& Canvas)
	{
		const float Across = FMath::Tan(FMath::DegreesToRadians(FGlpShowroom::CameraFov * 0.5f)) * 16.f / 9.f;
		return Across * Canvas.Y / FMath::Max(Canvas.X, 1.f);
	}

	float TanHalfAcross()
	{
		return FMath::Tan(FMath::DegreesToRadians(FGlpShowroom::CameraFov * 0.5f)) * 16.f / 9.f;
	}

	constexpr float SwatchSize = 34.f;
	constexpr float SwatchGap = 12.f;
}

// ---- the colour picker -----------------------------------------------------------------------------------

FGlpColourPicker::FGlpColourPicker(const FGlpPaintChoice& Start, const FGlpPaintChoice& InBefore, TOptional<float> KeptHue)
	: Finish(Start.Finish)
	, Before(InBefore)
{
	GlpPaint::ToHsl(Start.Srgb(), Hue, Saturation, Lightness);
	if (KeptHue.IsSet() && FGlpPaintChoice::Mixed(GlpPaint::FromHsl(KeptHue.GetValue(), Saturation, Lightness), Start.Finish).Colour == Start.Colour)
	{
		Hue = KeptHue.GetValue();
	}
}

FGlpPaintChoice FGlpColourPicker::Choice() const
{
	return FGlpPaintChoice::Mixed(GlpPaint::FromHsl(Hue, Saturation, Lightness), Finish);
}

void FGlpColourPicker::MoveFocus(int32 Step)
{
	Focus = static_cast<ERow>((static_cast<int32>(Focus) + Step + 4) % 4);
}

bool FGlpColourPicker::Nudge(int32 Held, int32 Pressed, float DeltaSeconds)
{
	if (Held == 0 && Pressed == 0)
	{
		HeldFor = 0.f;
		return false;
	}
	if (Focus == ERow::Finish)
	{
		if (Pressed == 0)
		{
			return false;
		}
		Finish = static_cast<EGlpFinish>((static_cast<int32>(Finish) + Pressed + 3) % 3);
		return true;
	}
	const float Unit = Focus == ERow::Hue ? 1.f / 360.f : 0.01f;
	float Delta = 0.f;
	if (Pressed != 0)
	{
		HeldFor = 0.f;
		Delta = Pressed * Unit;
	}
	else
	{
		HeldFor += DeltaSeconds;
		if (HeldFor < 0.25f)
		{
			return false;
		}
		Delta = Held * FMath::Min(30.f + (HeldFor - 0.25f) * 90.f, 150.f) * Unit * DeltaSeconds;
	}
	switch (Focus)
	{
	case ERow::Hue:
		Hue = FMath::Fmod(Hue + Delta + 1.f, 1.f);
		break;
	case ERow::Saturation:
		Saturation = FMath::Clamp(Saturation + Delta, 0.f, 1.f);
		break;
	default:
		Lightness = FMath::Clamp(Lightness + Delta, 0.f, 1.f);
		break;
	}
	return true;
}

void FGlpColourPicker::Pick(const FVector2f& Canvas, EHit Where, const FVector2f& Point)
{
	if (Where == EHit::Square)
	{
		const FGlpRect Box = Square(Canvas);
		Saturation = FMath::Clamp((Point.X - Box.X) / Box.W, 0.f, 1.f);
		Lightness = FMath::Clamp(1.f - (Point.Y - Box.Y) / Box.H, 0.f, 1.f);
		Focus = ERow::Saturation;
	}
	else if (Where == EHit::HueBar)
	{
		const FGlpRect Bar = HueBar(Canvas);
		Hue = FMath::Clamp((Point.Y - Bar.Y) / Bar.H, 0.f, 0.9999f);
		Focus = ERow::Hue;
	}
}

void FGlpColourPicker::SetFinish(EGlpFinish InFinish)
{
	Finish = InFinish;
	Focus = ERow::Finish;
}

void FGlpColourPicker::Set(const FGlpPaintChoice& Choice)
{
	GlpPaint::ToHsl(Choice.Srgb(), Hue, Saturation, Lightness);
	Finish = Choice.Finish;
}

FGlpRect FGlpColourPicker::Panel(const FVector2f& Canvas)
{
	// 900 lines high: 140 to 650 of the stage's 64 to 664, so the car, moved left and framed further off while the
	// picker is open, stays in view.
	return { Canvas.X - 480.f, 140.f, 330.f, 510.f };
}

FGlpRect FGlpColourPicker::Square(const FVector2f& Canvas)
{
	return Inside(Panel(Canvas), 20.f, 52.f, 240.f, 240.f);
}

FGlpRect FGlpColourPicker::HueBar(const FVector2f& Canvas)
{
	return Inside(Panel(Canvas), 276.f, 52.f, 34.f, 240.f);
}

FGlpRect FGlpColourPicker::RowRect(const FVector2f& Canvas, ERow Row)
{
	return Inside(Panel(Canvas), 12.f, 302.f + static_cast<int32>(Row) * 32.f, 306.f, 30.f);
}

FGlpRect FGlpColourPicker::FinishButton(const FVector2f& Canvas, EGlpFinish Finish)
{
	return Inside(Panel(Canvas), 92.f + static_cast<int32>(Finish) * 76.f, 302.f + 3 * 32.f, 72.f, 30.f);
}

FGlpRect FGlpColourPicker::DoneButton(const FVector2f& Canvas)
{
	return Inside(Panel(Canvas), 20.f, 446.f, 140.f, 36.f);
}

FGlpRect FGlpColourPicker::CancelButton(const FVector2f& Canvas)
{
	return Inside(Panel(Canvas), 170.f, 446.f, 140.f, 36.f);
}

FGlpColourPicker::EHit FGlpColourPicker::HitTest(const FVector2f& Canvas, const FVector2f& Point, EGlpFinish& OutFinish)
{
	if (!Panel(Canvas).Has(Point))
	{
		return EHit::Nothing;
	}
	// The square and the bar take a click a few pixels outside them: their edges are the extremes of a value.
	if (Square(Canvas).Has(Point, 6.f))
	{
		return EHit::Square;
	}
	if (HueBar(Canvas).Has(Point, 6.f))
	{
		return EHit::HueBar;
	}
	for (int32 i = 0; i < 3; i++)
	{
		if (FinishButton(Canvas, static_cast<EGlpFinish>(i)).Has(Point))
		{
			OutFinish = static_cast<EGlpFinish>(i);
			return EHit::Finish;
		}
	}
	if (DoneButton(Canvas).Has(Point))
	{
		return EHit::Done;
	}
	if (CancelButton(Canvas).Has(Point))
	{
		return EHit::Cancel;
	}
	return EHit::Panel;
}

// ---- the showroom ----------------------------------------------------------------------------------------

void FGlpShowroom::Open(int32 Car, const FGlpPaintBook* InBook, bool bInFromMenu, float BearingDegrees, bool bInHeld)
{
	*this = FGlpShowroom();
	bOpen = true;
	bFromMenu = bInFromMenu;
	bHeld = bInHeld;
	Index = Car;
	Driven = Car;
	Book = InBook;
	// The driver's side is the car's left: against the way Yaw grows.
	Yaw = -FMath::DegreesToRadians(BearingDegrees);
}

void FGlpShowroom::Close()
{
	bOpen = false;
	Picker.Reset();
	bDragging = false;
}

FGlpPaintChoice FGlpShowroom::ChoiceOf(int32 Car) const
{
	if (const FGlpPaintChoice* Edit = Edits.Find(Car))
	{
		return *Edit;
	}
	return Book ? Book->Of(Car) : FGlpPaintChoice();
}

int32 FGlpShowroom::PaintSlot() const
{
	const FGlpPaintChoice Now = Choice();
	return Now.IsCustom() ? CustomSlot() : Now.Preset;
}

FGlpPaintChoice FGlpShowroom::MixedFor(int32 Car) const
{
	if (const FGlpPaintChoice* Here = MixedHere.Find(Car))
	{
		return *Here;
	}
	if (const TOptional<FGlpPaintChoice> Kept = Book ? Book->LastMixed(Car) : TOptional<FGlpPaintChoice>(); Kept.IsSet())
	{
		return Kept.GetValue();
	}
	return FGlpPaintChoice::Mixed(OwnSrgb, EGlpFinish::Solid);
}

void FGlpShowroom::SetChoice(const FGlpPaintChoice& NewChoice)
{
	Edits.Add(Index, NewChoice);
	if (NewChoice.IsCustom())
	{
		MixedHere.Add(Index, NewChoice);
	}
}

void FGlpShowroom::ChooseSlot(int32 Slot)
{
	if (Picker.IsSet() && Slot != CustomSlot())
	{
		ClosePicker(true);
	}
	SetChoice(Slot == CustomSlot() ? MixedFor(Index) : FGlpPaintChoice::OfPreset(Slot));
}

void FGlpShowroom::OpenPicker()
{
	if (Picker.IsSet())
	{
		return;
	}
	const FGlpPaintChoice Before = Choice();
	MixedBefore = MixedHere.Contains(Index) ? TOptional<FGlpPaintChoice>(MixedHere[Index]) : TOptional<FGlpPaintChoice>();
	if (!Before.IsCustom())
	{
		SetChoice(MixedFor(Index));
	}
	Picker.Emplace(Choice(), Before, MixedHue.Contains(Index) ? TOptional<float>(MixedHue[Index]) : TOptional<float>());
}

void FGlpShowroom::ClosePicker(bool bKeep)
{
	if (!Picker.IsSet())
	{
		return;
	}
	const FGlpColourPicker Closed = Picker.GetValue();
	Picker.Reset();
	MixedHue.Add(Index, Closed.Hue);
	if (bKeep)
	{
		return;
	}
	SetChoice(Closed.Before);
	if (MixedBefore.IsSet())
	{
		MixedHere.Add(Index, MixedBefore.GetValue());
	}
	else
	{
		MixedHere.Remove(Index);
	}
}

void FGlpShowroom::ApplyPicker()
{
	if (Picker.IsSet())
	{
		SetChoice(Picker->Choice());
	}
}

void FGlpShowroom::Show(int32 Car)
{
	if (Picker.IsSet())
	{
		ClosePicker(true);
	}
	Index = Car;
}

void FGlpShowroom::Drag(float Pixels)
{
	const float Turn = Pixels * DragRadiansPerPixel;
	Yaw += Turn;
	DragTurn += Turn;
}

void FGlpShowroom::ZoomBy(int32 Steps)
{
	Zoom = FMath::Clamp(Zoom * FMath::Pow(0.88f, static_cast<float>(Steps)), 0.55f, 1.8f);
}

void FGlpShowroom::Advance(float DeltaSeconds, const FVector2f& Canvas, float TargetReach, float Middle)
{
	if (bDragging)
	{
		// The spin the view keeps when it is let go: the frame's whole drag over the frame's time, eased over about 50 ms
		// (taken from one motion event, it depended on how often the mouse reports, and a drag held still kept the last
		// motion's speed and spun away when let go: the Godot showroom's review).
		Spin += (DragTurn / FMath::Max(DeltaSeconds, 1e-4f) - Spin) * FMath::Clamp(DeltaSeconds * 20.f, 0.f, 1.f);
	}
	else
	{
		// The view turns on its own, the drag's spin on top, dying away in about a second.
		Spin *= FMath::Exp(-DeltaSeconds * 3.f);
		Yaw += ((bHeld ? 0.f : TurnRate) + Spin) * DeltaSeconds;
	}
	DragTurn = 0.f;
	Yaw = FMath::Fmod(Yaw, UE_TWO_PI);
	// The first frame stands where it should; after that the framing eases to a new car, the wheel and the picker.
	const bool bFirst = Distance <= 0.f;
	const float Ease = bFirst ? 1.f : FMath::Clamp(DeltaSeconds * 6.f, 0.f, 1.f);
	Reach += (TargetReach - Reach) * Ease;
	Distance += (CameraDistance(Canvas, Middle) - Distance) * Ease;
	Shift += (PickerShift(Canvas) - Shift) * Ease;
}

float FGlpShowroom::CameraDistance(const FVector2f& Canvas, float Middle) const
{
	// 2.9 times the radius the car sweeps, or as far as its nearest corner needs to stay over the panel; the wheel
	// brings it closer or further from there. With the picker open the car stands in the stage left of it, a narrower
	// frame: the camera steps back as far as the car's sweep needs to fit that frame's half-width with a tenth to spare.
	const float Framed = FMath::Max(Reach * 2.9f, FloorFit(Canvas, Middle)) * Zoom;
	if (!Picker.IsSet())
	{
		return Framed;
	}
	const float Free = (FGlpColourPicker::Panel(Canvas).X - LeftArrow(Canvas).Right()) / Canvas.X;
	return FMath::Max(Framed, Reach / (0.9f * Free * TanHalfAcross()));
}

float FGlpShowroom::FloorFit(const FVector2f& Canvas, float Middle) const
{
	// The camera at D (sin p, cos p) from an aim k D under the car's middle sees a ground point a under the middle and
	// r towards it at y / z = (a cos p + k D cos p - r sin p) / (D (1 - k sin p) - a sin p - r cos p); set to the
	// panel's edge on the screen, that is linear in D (the Godot showroom's FloorFitDistance). The short, tall cars
	// need it: framed by their sweep alone, the 205's and the Delta's near wheel went under the panel as a rear corner
	// came round.
	float S = 0.f, C = 1.f;
	FMath::SinCos(&S, &C, FMath::DegreesToRadians(CameraPitch));
	const float Edge = (1.f - 2.f * (Panel(Canvas).Y - 16.f) / Canvas.Y) * TanHalfUpright(Canvas);
	const float A = -Middle;
	const float Below = AimDrop * C - Edge * (1.f - AimDrop * S);
	return Below <= 0.01f ? 0.f : (Reach * S - A * C - Edge * (A * S + Reach * C)) / Below;
}

float FGlpShowroom::PickerShift(const FVector2f& Canvas) const
{
	if (!Picker.IsSet())
	{
		return 0.f;
	}
	const float Free = (LeftArrow(Canvas).Right() + FGlpColourPicker::Panel(Canvas).X) * 0.5f;
	const float Fraction = (Canvas.X * 0.5f - Free) / (Canvas.X * 0.5f);
	return Fraction * TanHalfAcross() * Distance;
}

FGlpRect FGlpShowroom::Panel(const FVector2f& Canvas)
{
	return { 0.f, Canvas.Y - 236.f, Canvas.X, 236.f };
}

FGlpRect FGlpShowroom::LeftArrow(const FVector2f& Canvas)
{
	return { 24.f, Canvas.Y * 0.5f - 130.f, 96.f, 160.f };
}

FGlpRect FGlpShowroom::RightArrow(const FVector2f& Canvas)
{
	return { Canvas.X - 120.f, Canvas.Y * 0.5f - 130.f, 96.f, 160.f };
}

FGlpRect FGlpShowroom::Swatch(const FVector2f& Canvas, int32 Slot)
{
	const int32 Count = SlotCount();
	const float X0 = Canvas.X * 0.5f - (Count * SwatchSize + (Count - 1) * SwatchGap) * 0.5f;
	return { X0 + Slot * (SwatchSize + SwatchGap), Panel(Canvas).Y + 96.f, SwatchSize, SwatchSize };
}

FGlpRect FGlpShowroom::TakeButton(const FVector2f& Canvas)
{
	return { Canvas.X - 290.f, Panel(Canvas).Y + 92.f, 250.f, 42.f };
}

FGlpRect FGlpShowroom::BackButton(const FVector2f& Canvas)
{
	return { 40.f, Panel(Canvas).Y + 92.f, 250.f, 42.f };
}

FGlpShowroom::EHit FGlpShowroom::HitTest(const FVector2f& Canvas, const FVector2f& Point, int32& OutSlot)
{
	OutSlot = 0;
	if (LeftArrow(Canvas).Has(Point))
	{
		return EHit::Left;
	}
	if (RightArrow(Canvas).Has(Point))
	{
		return EHit::Right;
	}
	if (TakeButton(Canvas).Has(Point))
	{
		return EHit::Take;
	}
	if (BackButton(Canvas).Has(Point))
	{
		return EHit::Back;
	}
	for (int32 i = 0; i < SlotCount(); i++)
	{
		if (Swatch(Canvas, i).Has(Point, 4.f))
		{
			OutSlot = i;
			return EHit::Swatch;
		}
	}
	return EHit::Nothing;
}

int32 FGlpShowroom::SelfTest(TArray<FString>& Failures)
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
	const FVector2f Canvas(1600.f, 900.f);
	auto Middle = [](const FGlpRect& Rect) { return FVector2f(Rect.X + Rect.W * 0.5f, Rect.Y + Rect.H * 0.5f); };

	// The paints: a settings book with one car painted, the showroom's edits on top of it until they are taken.
	FGlpPaintBook Book;
	Book.Reset(14);
	Book.Set(4, FGlpPaintChoice::OfPreset(2));
	FGlpShowroom Room;
	Room.Open(2, &Book, false, StartBearing, false);
	Room.OwnSrgb = FLinearColor(0.2f, 0.4f, 0.6f);
	Check(Room.bOpen && Room.Index == 2 && Room.Driven == 2 && !Room.bFromMenu && Room.Edits.IsEmpty(), TEXT("the showroom opens on the car driven with nothing chosen"));
	Check(FMath::IsNearlyEqual(Room.Yaw, -FMath::DegreesToRadians(StartBearing)), TEXT("the first view is towards the driver's side, against the way the turn grows"));
	Check(Room.Choice().IsOriginal() && Room.PaintSlot() == 0 && Room.ChoiceOf(4).Preset == 2, TEXT("each car starts in the paint its settings keep"));
	Room.CyclePaint(1);
	Check(Room.Choice().Preset == 1 && Room.PaintSlot() == 1 && Book.Of(2).IsOriginal(), TEXT("a step down is the next preset, and an edit only"));
	Room.CyclePaint(-1);
	Room.CyclePaint(-1);
	Check(Room.Choice().IsCustom() && Room.PaintSlot() == CustomSlot() && Room.Choice() == FGlpPaintChoice::Mixed(Room.OwnSrgb, EGlpFinish::Solid),
		TEXT("a step up from ORIGINAL is the mixed colour, which starts as the car's own as a solid"));
	Room.CyclePaint(1);
	Check(Room.Choice().IsOriginal(), TEXT("and a step down from the mixed colour is ORIGINAL again"));
	Room.ChooseSlot(5);
	Check(Room.Choice().Name() == TEXT("DARK BLUE"), TEXT("a swatch taken"));

	// The picker: opens on the mixed colour, changes the car as it changes, CANCEL puts the paint back.
	Room.OpenPicker();
	Check(Room.Picker.IsSet() && Room.Choice().IsCustom() && Room.Picker->Before.Name() == TEXT("DARK BLUE"), TEXT("the picker opens on the car's mixed colour and remembers the paint before it"));
	Room.OpenPicker();
	Check(Room.Picker.IsSet() && Room.Picker->Before.Name() == TEXT("DARK BLUE"), TEXT("opening it again changes nothing"));
	Room.Picker->Focus = FGlpColourPicker::ERow::Hue;
	const float HueBefore = Room.Picker->Hue;
	Check(Room.Picker->Nudge(0, 1, 0.016f) && FMath::IsNearlyEqual(Room.Picker->Hue, FMath::Fmod(HueBefore + 1.f / 360.f, 1.f), 1e-5f), TEXT("a tap moves the hue a degree"));
	Check(!Room.Picker->Nudge(1, 0, 0.1f) && !Room.Picker->Nudge(1, 0, 0.1f), TEXT("a key held less than a quarter of a second moves nothing more"));
	Check(Room.Picker->Nudge(1, 0, 0.1f) && Room.Picker->Hue > HueBefore + 1.f / 360.f, TEXT("held longer, the value runs on"));
	Check(!Room.Picker->Nudge(0, 0, 0.1f), TEXT("no key, no change"));
	Room.Picker->MoveFocus(1);
	Check(Room.Picker->Focus == FGlpColourPicker::ERow::Saturation, TEXT("down moves to the next row"));
	Room.Picker->MoveFocus(-2);
	Check(Room.Picker->Focus == FGlpColourPicker::ERow::Finish, TEXT("up from the first row is the last"));
	Check(Room.Picker->Nudge(0, 1, 0.016f) && Room.Picker->Finish == EGlpFinish::Metallic && !Room.Picker->Nudge(1, 0, 1.f), TEXT("the finish steps on a tap, not while held"));
	Check(Room.Picker->Nudge(0, -1, 0.016f) && Room.Picker->Nudge(0, -1, 0.016f) && Room.Picker->Finish == EGlpFinish::Pearl, TEXT("and round the other way"));
	Room.ApplyPicker();
	Check(Room.Choice() == Room.Picker->Choice() && Room.Choice().Finish == EGlpFinish::Pearl, TEXT("the picker's colour is the car's as it changes"));
	Room.ClosePicker(false);
	Check(!Room.Picker.IsSet() && Room.Choice().Name() == TEXT("DARK BLUE") && Room.MixedHere.Contains(2)
		&& Room.MixedHere[2] == FGlpPaintChoice::Mixed(Room.OwnSrgb, EGlpFinish::Solid), TEXT("CANCEL puts the paint and the mixed swatch back as they were"));

	// DONE keeps it; the mouse on the square and the bar; the hue kept for a colour that has none.
	Room.OpenPicker();
	const FGlpRect Square = FGlpColourPicker::Square(Canvas);
	const FGlpRect Bar = FGlpColourPicker::HueBar(Canvas);
	Room.Picker->Pick(Canvas, FGlpColourPicker::EHit::HueBar, FVector2f(Bar.X + 5.f, Bar.Y + Bar.H * 0.25f));
	Room.Picker->Pick(Canvas, FGlpColourPicker::EHit::Square, FVector2f(Square.X + Square.W * 0.5f, Square.Y + Square.H * 0.25f));
	Check(FMath::IsNearlyEqual(Room.Picker->Hue, 0.25f, 1e-4f) && FMath::IsNearlyEqual(Room.Picker->Saturation, 0.5f, 1e-4f) && FMath::IsNearlyEqual(Room.Picker->Lightness, 0.75f, 1e-4f),
		TEXT("the mouse on the bar is the hue, on the square the saturation across and the lightness down"));
	Room.Picker->Pick(Canvas, FGlpColourPicker::EHit::Square, FVector2f(Square.X - 500.f, Square.Y + Square.H + 500.f));
	Check(Room.Picker->Saturation == 0.f && Room.Picker->Lightness == 0.f, TEXT("a drag past the square's corner stays in it"));
	Room.ApplyPicker();
	Room.ClosePicker(true);
	Check(Room.Choice().IsCustom() && Room.Choice().Colour == FColor(0, 0, 0) && Room.MixedHue.Contains(2), TEXT("DONE keeps the colour; the picker's hue is remembered"));
	Room.OpenPicker();
	Check(FMath::IsNearlyEqual(Room.Picker->Hue, 0.25f, 1e-4f), TEXT("a black has no hue of its own: the picker opens where its bar was left"));
	Room.ClosePicker(true);

	// Another car: a colour being mixed stays on the car it was mixed for.
	Room.OpenPicker();
	Room.Picker->Set(FGlpPaintChoice::Mixed(FLinearColor(0.8f, 0.2f, 0.1f), EGlpFinish::Metallic));
	Room.ApplyPicker();
	Room.Show(4);
	Check(!Room.Picker.IsSet() && Room.Index == 4 && Room.Choice().Preset == 2 && Room.ChoiceOf(2).Finish == EGlpFinish::Metallic, TEXT("stepping to another car keeps the colour mixed on the one before"));
	Check(Room.Edits.Num() == 1 && Room.Edits.Contains(2) && Room.Driven == 2, TEXT("only the car painted here has an edit"));
	Room.ChooseSlot(CustomSlot());
	Check(Room.Choice().IsCustom() && Room.Edits.Num() == 2, TEXT("the mixed swatch on a car never mixed is its own colour as a solid"));
	Room.Close();
	Check(!Room.bOpen && !Room.Picker.IsSet() && !Room.bDragging, TEXT("closed"));

	// The layout: what is drawn is what the mouse hits, on the 16:9 canvas and on a wider and a taller one.
	for (const FVector2f& Size : { FVector2f(1600.f, 900.f), FVector2f(2150.f, 900.f), FVector2f(1600.f, 1200.f) })
	{
		int32 Slot = INDEX_NONE;
		Check(HitTest(Size, Middle(LeftArrow(Size)), Slot) == EHit::Left && HitTest(Size, Middle(RightArrow(Size)), Slot) == EHit::Right, TEXT("the chevrons"));
		Check(HitTest(Size, Middle(TakeButton(Size)), Slot) == EHit::Take && HitTest(Size, Middle(BackButton(Size)), Slot) == EHit::Back, TEXT("TAKE IT and BACK"));
		bool bSwatches = true;
		for (int32 i = 0; i < SlotCount(); i++)
		{
			bSwatches &= HitTest(Size, Middle(Swatch(Size, i)), Slot) == EHit::Swatch && Slot == i;
			bSwatches &= i == 0 || Swatch(Size, i).X >= Swatch(Size, i - 1).Right() + 8.f;
		}
		Check(bSwatches, TEXT("each swatch takes its own click, clear of its neighbours"));
		Check(HitTest(Size, FVector2f(Size.X * 0.5f, Size.Y * 0.4f), Slot) == EHit::Nothing, TEXT("the stage is nothing's: a press there starts a drag"));
		Check(Swatch(Size, 0).X > BackButton(Size).Right() && Swatch(Size, SlotCount() - 1).Right() < TakeButton(Size).X, TEXT("the swatches lie between the two buttons"));
		Check(Panel(Size).Bottom() == Size.Y && Panel(Size).W == Size.X, TEXT("the panel runs along the foot of the screen"));
		EGlpFinish Finish = EGlpFinish::Solid;
		const FGlpRect Picker = FGlpColourPicker::Panel(Size);
		Check(Picker.Bottom() < Panel(Size).Y && Picker.Y > 64.f && Picker.Right() < RightArrow(Size).X, TEXT("the picker stands on the stage, clear of the bar, the panel and the right chevron"));
		Check(FGlpColourPicker::HitTest(Size, Middle(FGlpColourPicker::Square(Size)), Finish) == FGlpColourPicker::EHit::Square
			&& FGlpColourPicker::HitTest(Size, Middle(FGlpColourPicker::HueBar(Size)), Finish) == FGlpColourPicker::EHit::HueBar, TEXT("the picker's square and bar"));
		Check(FGlpColourPicker::HitTest(Size, Middle(FGlpColourPicker::DoneButton(Size)), Finish) == FGlpColourPicker::EHit::Done
			&& FGlpColourPicker::HitTest(Size, Middle(FGlpColourPicker::CancelButton(Size)), Finish) == FGlpColourPicker::EHit::Cancel, TEXT("DONE and CANCEL"));
		Check(FGlpColourPicker::HitTest(Size, Middle(FGlpColourPicker::FinishButton(Size, EGlpFinish::Pearl)), Finish) == FGlpColourPicker::EHit::Finish && Finish == EGlpFinish::Pearl, TEXT("a finish's button names its finish"));
		Check(FGlpColourPicker::HitTest(Size, FVector2f(Picker.X + 4.f, Picker.Y + 4.f), Finish) == FGlpColourPicker::EHit::Panel
			&& FGlpColourPicker::HitTest(Size, FVector2f(Picker.X - 20.f, Picker.Y + 4.f), Finish) == FGlpColourPicker::EHit::Nothing, TEXT("the picker's panel takes a press on it and none beside it"));
	}

	// The turn and the framing.
	FGlpShowroom Turning;
	Turning.Open(0, &Book, false, 0.f, false);
	Turning.Advance(0.f, Canvas, 2.4f, 0.6f);
	const float FirstDistance = Turning.Distance;
	Check(FirstDistance >= 2.4f * 2.9f - 1e-3f && FMath::IsNearlyEqual(Turning.Reach, 2.4f) && Turning.Shift == 0.f, TEXT("the first frame stands at the car's framing: at least 2.9 times its sweep away"));
	for (int32 i = 0; i < 60; i++)
	{
		Turning.Advance(1.f / 60.f, Canvas, 2.4f, 0.6f);
	}
	Check(FMath::IsNearlyEqual(Turning.Yaw, TurnRate, 1e-3f), TEXT("left alone the view goes round at the turn's rate"));
	FGlpShowroom Held;
	Held.Open(0, &Book, false, 90.f, true);
	for (int32 i = 0; i < 60; i++)
	{
		Held.Advance(1.f / 60.f, Canvas, 2.4f, 0.6f);
	}
	Check(FMath::IsNearlyEqual(Held.Yaw, -UE_HALF_PI, 1e-4f), TEXT("held for a picture it stays where it was put"));
	Held.bDragging = true;
	Held.Drag(100.f);
	Held.Advance(1.f / 60.f, Canvas, 2.4f, 0.6f);
	Check(FMath::IsNearlyEqual(Held.Yaw, -UE_HALF_PI + 100.f * DragRadiansPerPixel, 1e-4f) && Held.Spin > 0.f, TEXT("a drag to the right turns the view the way the turn grows, and leaves it some spin"));
	Held.bDragging = false;
	const float Let = Held.Yaw;
	for (int32 i = 0; i < 600; i++)
	{
		Held.Advance(1.f / 60.f, Canvas, 2.4f, 0.6f);
	}
	Check(Held.Yaw > Let && FMath::Abs(Held.Spin) < 1e-3f, TEXT("let go, the spin carries it on a little and dies away"));
	Turning.ZoomBy(100);
	Check(FMath::IsNearlyEqual(Turning.Zoom, 0.55f), TEXT("the wheel brings the car no closer than 0.55 of its framing"));
	Turning.ZoomBy(-100);
	Check(FMath::IsNearlyEqual(Turning.Zoom, 1.8f), TEXT("and no further than 1.8"));
	Turning.Zoom = 1.f;
	Turning.OpenPicker();
	for (int32 i = 0; i < 300; i++)
	{
		Turning.Advance(1.f / 60.f, Canvas, 2.4f, 0.6f);
	}
	Check(Turning.Shift > 0.5f && Turning.Distance >= FirstDistance - 1e-3f, TEXT("with the picker open the camera steps aside and no closer"));
	Turning.ClosePicker(false);
	for (int32 i = 0; i < 300; i++)
	{
		Turning.Advance(1.f / 60.f, Canvas, 2.4f, 0.6f);
	}
	Check(FMath::Abs(Turning.Shift) < 0.01f && FMath::IsNearlyEqual(Turning.Distance, FirstDistance, 0.01f), TEXT("and comes back when it closes"));
	// A short, tall car framed by its sweep alone had its near wheel under the panel: the floor's fit takes over.
	FGlpShowroom Tall;
	Tall.Open(0, &Book, false, 0.f, true);
	Tall.Advance(0.f, Canvas, 2.0f, 0.75f);
	Check(Tall.Distance >= 2.0f * 2.9f - 1e-3f && FMath::IsFinite(Tall.Distance) && Tall.Distance < 20.f, TEXT("a short, tall car's framing is finite and no closer than its sweep's"));
	return Checks;
}

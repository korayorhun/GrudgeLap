#include "GlpHud.h"

#include "Engine/Canvas.h"
#include "EngineUtils.h"
#include "GlpDraw.h"
#include "GlpGame.h"
#include "GlpSpace.h"

namespace
{
	using EAlign = FGlpPainter::EAlign;

	// The layout's reference canvas (project.godot: window/size 1600 x 900, stretch canvas_items, aspect expand).
	constexpr float LayoutWidth = 1600.f;
	constexpr float LayoutHeight = 900.f;

	/** RaceSession.FormatLapTime: m:ss.s, or dashes before the first lap. */
	FString LapTime(float Seconds)
	{
		if (!FMath::IsFinite(Seconds))
		{
			return TEXT("-:--.-");
		}
		const int32 Minutes = static_cast<int32>(Seconds / 60.f);
		return FString::Printf(TEXT("%d:%04.1f"), Minutes, Seconds - Minutes * 60.f);
	}

	/** A score with thousands separators ("N0" in the invariant culture). */
	FString Thousands(int64 Value)
	{
		FString Digits = FString::Printf(TEXT("%lld"), FMath::Abs(Value));
		for (int32 i = Digits.Len() - 3; i > 0; i -= 3)
		{
			Digits.InsertAt(i, TEXT(','));
		}
		return Value < 0 ? TEXT("-") + Digits : Digits;
	}

	/** A paint's sRGB components as the canvas takes a colour. */
	FLinearColor Swatch(const FLinearColor& Srgb)
	{
		return FGlpPainter::Srgb(Srgb.R, Srgb.G, Srgb.B);
	}

	/** A hue at its fullest, for the bar of hues and the strip under the mixed colour's swatch. */
	FLinearColor PureHue(float Hue)
	{
		return Swatch(GlpPaint::FromHsl(Hue, 1.f, 0.5f));
	}

	/**
	 * The colour picker (FGlpColourPicker; the Godot version's Hud.DrawPicker, measure for measure): the square of
	 * saturation and lightness in the picker's hue with a ring at the colour, the bar of hues with a mark at its hue,
	 * the three values and the finish as rows (the focused one lit, as the menu's line under its cursor), DONE and CANCEL.
	 */
	void DrawPicker(const FGlpPainter& P, const FVector2f& Size, const FGlpColourPicker& Picker)
	{
		using ERow = FGlpColourPicker::ERow;
		const FLinearColor White = FGlpPainter::Srgb(1.f, 1.f, 1.f);
		const FLinearColor Grey = FGlpPainter::Srgb(0.72f, 0.75f, 0.8f);
		const FLinearColor Gold = FGlpPainter::Srgb(1.f, 0.85f, 0.3f);
		const FLinearColor Lit = FGlpPainter::Srgb(1.f, 0.85f, 0.3f, 0.16f);
		const FGlpRect Panel = FGlpColourPicker::Panel(Size);
		P.Rect(Panel.X, Panel.Y, Panel.W, Panel.H, FGlpPainter::Srgb(0.03f, 0.03f, 0.04f, 0.88f));
		const FGlpPaintChoice Choice = Picker.Choice();
		P.Text(TEXT("MIX A COLOUR"), Panel.X + 20.f, Panel.Y + 36.f, EAlign::Left, -1.f, 22.f, White);
		P.Rect(Panel.Right() - 58.f, Panel.Y + 16.f, 38.f, 26.f, Swatch(Choice.Srgb()));
		P.Text(Choice.Hex(), Panel.X, Panel.Y + 36.f, EAlign::Right, Panel.W - 68.f, 16.f, Grey);

		// The square: saturation across, lightness from white at the top through the full colour to black. 24 cells a
		// side put a cell's edge on the middle row, where the full colour's ridge runs.
		const FGlpRect Square = FGlpColourPicker::Square(Size);
		const float Hue = Picker.Hue;
		P.Shade(Square.X, Square.Y, Square.W, Square.H, 24, 24, [Hue](float Across, float Down) { return Swatch(GlpPaint::FromHsl(Hue, Across, 1.f - Down)); });
		// A ring that shows on any colour: black inside white.
		const FVector2D At(Square.X + Picker.Saturation * Square.W, Square.Y + (1.f - Picker.Lightness) * Square.H);
		P.Arc(At, 8.f, 0.f, UE_TWO_PI, 24, FGlpPainter::Srgb(0.f, 0.f, 0.f, 0.8f), 3.f);
		P.Arc(At, 6.f, 0.f, UE_TWO_PI, 24, White, 2.f);
		// The bar of hues, a cell's edge on each of the six pure colours, and the mark at the picker's.
		const FGlpRect Bar = FGlpColourPicker::HueBar(Size);
		P.Shade(Bar.X, Bar.Y, Bar.W, Bar.H, 1, 36, [](float Across, float Down) { return PureHue(Down); });
		const float HueY = Bar.Y + Picker.Hue * Bar.H;
		P.Frame(Bar.X - 4.f, HueY - 3.f, Bar.W + 8.f, 6.f, FGlpPainter::Srgb(0.f, 0.f, 0.f, 0.8f), 2.f);
		P.Rect(Bar.X - 2.f, HueY - 1.5f, Bar.W + 4.f, 3.f, White);

		const TCHAR* Names[] = { TEXT("HUE"), TEXT("SATURATION"), TEXT("LIGHTNESS") };
		// The hue is a place on a circle: the foot of the bar, a hair under a full turn, reads 0 as its head does.
		const FString Values[] = { FString::Printf(TEXT("%d°"), FMath::RoundToInt(Picker.Hue * 360.f) % 360),
			FString::Printf(TEXT("%d %%"), FMath::RoundToInt(Picker.Saturation * 100.f)), FString::Printf(TEXT("%d %%"), FMath::RoundToInt(Picker.Lightness * 100.f)) };
		for (int32 i = 0; i < 3; i++)
		{
			const FGlpRect Row = FGlpColourPicker::RowRect(Size, static_cast<ERow>(i));
			const bool bFocused = Picker.Focus == static_cast<ERow>(i);
			if (bFocused)
			{
				P.Rect(Row.X, Row.Y, Row.W, Row.H, Lit);
			}
			P.Text(Names[i], Row.X + 8.f, Row.Y + 22.f, EAlign::Left, -1.f, 16.f, bFocused ? Gold : Grey);
			P.Text(Values[i], Row.X, Row.Y + 22.f, EAlign::Right, Row.W - 8.f, 16.f, bFocused ? Gold : White);
		}
		const FGlpRect FinishRow = FGlpColourPicker::RowRect(Size, ERow::Finish);
		const bool bFinishFocused = Picker.Focus == ERow::Finish;
		if (bFinishFocused)
		{
			P.Rect(FinishRow.X, FinishRow.Y, FinishRow.W, FinishRow.H, Lit);
		}
		P.Text(TEXT("FINISH"), FinishRow.X + 8.f, FinishRow.Y + 22.f, EAlign::Left, -1.f, 16.f, bFinishFocused ? Gold : Grey);
		for (int32 i = 0; i < 3; i++)
		{
			const EGlpFinish Finish = static_cast<EGlpFinish>(i);
			const FGlpRect Button = FGlpColourPicker::FinishButton(Size, Finish);
			const bool bChosen = Finish == Picker.Finish;
			P.Rect(Button.X, Button.Y, Button.W, Button.H, bChosen ? FGlpPainter::Srgb(1.f, 0.85f, 0.3f, 0.28f) : FGlpPainter::Srgb(1.f, 1.f, 1.f, 0.06f));
			P.Text(GlpPaint::FinishName(Finish), Button.X, Button.Y + 21.f, EAlign::Centre, Button.W, 13.f, bChosen ? Gold : Grey);
		}
		const FGlpRect Done = FGlpColourPicker::DoneButton(Size);
		const FGlpRect Cancel = FGlpColourPicker::CancelButton(Size);
		P.Rect(Done.X, Done.Y, Done.W, Done.H, Lit);
		P.Text(TEXT("DONE  (ENTER)"), Done.X, Done.Y + 24.f, EAlign::Centre, Done.W, 15.f, Gold);
		P.Rect(Cancel.X, Cancel.Y, Cancel.W, Cancel.H, FGlpPainter::Srgb(1.f, 1.f, 1.f, 0.06f));
		P.Text(TEXT("CANCEL  (ESC)"), Cancel.X, Cancel.Y + 24.f, EAlign::Centre, Cancel.W, 15.f, Grey);
		P.Text(TEXT("up / down: row     left / right: change"), Panel.X, Panel.Bottom() - 14.f, EAlign::Centre, Panel.W, 13.f, Grey);
	}

	/**
	 * The showroom (FGlpShowroom; the Godot version's Hud.DrawShowroom, measure for measure): the car's name in the bar
	 * over the stage, the chevrons at the sides, and on a panel at the foot the facts the core gives about the car, the
	 * paints as swatches (the chosen one framed, the ORIGINAL one in the car's own colour, the last one the car's mixed
	 * colour over a strip of hues), the paint's name and the keys; the colour picker over the stage while it is open.
	 * The rectangles are the showroom's and the picker's own, so the mouse hits what is drawn.
	 */
	void DrawShowroom(const FGlpPainter& P, const AGlpGame& G)
	{
		const FGlpShowroom& Room = G.Showroom();
		const FVector2f Size(P.Size());
		const FLinearColor White = FGlpPainter::Srgb(1.f, 1.f, 1.f);
		const FLinearColor Grey = FGlpPainter::Srgb(0.72f, 0.75f, 0.8f);
		const FLinearColor Gold = FGlpPainter::Srgb(1.f, 0.85f, 0.3f);
		auto Centred = [&](const FString& Text, float Y, float FontSize, const FLinearColor& Colour) { P.Text(Text, 0.f, Y, EAlign::Centre, Size.X, FontSize, Colour); };
		P.Rect(0.f, 0.f, Size.X, 64.f, FGlpPainter::Srgb(0.05f, 0.05f, 0.06f, 0.5f));
		P.Text(TEXT("SHOWROOM"), 20.f, 44.f, EAlign::Left, -1.f, 30.f, White);
		P.Text(G.ShownPlace(), Size.X - 320.f, 44.f, EAlign::Right, 300.f, 22.f, Grey);
		// The name in the bar: over the stage it lay on the roofs of the tall cars and of a car brought closer.
		Centred(G.ShownName(), 45.f, 34.f, White);
		const FGlpRect Left = FGlpShowroom::LeftArrow(Size);
		const FGlpRect Right = FGlpShowroom::RightArrow(Size);
		P.Text(TEXT("<"), Left.X, Left.Y + 112.f, EAlign::Centre, Left.W, 96.f, Gold);
		P.Text(TEXT(">"), Right.X, Right.Y + 112.f, EAlign::Centre, Right.W, 96.f, Gold);

		const FGlpRect Panel = FGlpShowroom::Panel(Size);
		P.Rect(Panel.X, Panel.Y, Panel.W, Panel.H, FGlpPainter::Srgb(0.03f, 0.03f, 0.04f, 0.80f));
		const float Y = Panel.Y;
		Centred(G.ShownFacts(0), Y + 38.f, 19.f, Grey);
		Centred(G.ShownFacts(1), Y + 70.f, 20.f, White);
		const FGlpRect First = FGlpShowroom::Swatch(Size, 0);
		P.Text(TEXT("PAINT"), First.X - 112.f, First.Y + 25.f, EAlign::Right, 96.f, 20.f, Grey);
		const int32 Chosen = Room.PaintSlot();
		for (int32 i = 0; i < FGlpShowroom::SlotCount(); i++)
		{
			const FGlpRect Box = FGlpShowroom::Swatch(Size, i);
			FLinearColor Colour = Room.OwnSrgb;
			if (i == FGlpShowroom::CustomSlot())
			{
				Colour = Room.MixedChoice().Srgb();
			}
			else if (const TOptional<FGlpPaint> Preset = GlpPaint::Preset(i); Preset.IsSet())
			{
				Colour = Preset->Srgb;
			}
			P.Rect(Box.X, Box.Y, Box.W, Box.H, Swatch(Colour));
			// A faint edge on every swatch: the black one was lost on the dark panel.
			P.Frame(Box.X, Box.Y, Box.W, Box.H, FGlpPainter::Srgb(1.f, 1.f, 1.f, 0.3f), 1.f);
			if (i == FGlpShowroom::CustomSlot())
			{
				// The mixed colour's swatch stands on a strip of every hue: the one that opens the picker.
				P.Shade(Box.X, Box.Bottom() - 6.f, Box.W, 6.f, 12, 1, [](float Across, float Down) { return PureHue(Across); });
			}
			if (i == Chosen)
			{
				P.Frame(Box.X - 4.f, Box.Y - 4.f, Box.W + 8.f, Box.H + 8.f, Gold, 3.f);
			}
		}
		const FGlpPaintChoice Choice = Room.Choice();
		Centred(Choice.IsCustom() ? FString::Printf(TEXT("%s   %s"), *Choice.Name(), *Choice.Hex()) : Choice.Name(), Y + 160.f, 18.f, Gold);
		// While the picker is open the buttons wait for its DONE or CANCEL (the game ignores a click on them): dimmed.
		const float Waiting = Room.Picker.IsSet() ? 0.35f : 1.f;
		const FGlpRect Take = FGlpShowroom::TakeButton(Size);
		const FGlpRect Back = FGlpShowroom::BackButton(Size);
		P.Rect(Take.X, Take.Y, Take.W, Take.H, FGlpPainter::Srgb(1.f, 0.85f, 0.3f, 0.16f * Waiting));
		P.Text(TEXT("TAKE IT   (ENTER)"), Take.X, Take.Y + 29.f, EAlign::Centre, Take.W, 20.f, FGlpPainter::Srgb(1.f, 0.85f, 0.3f, Waiting));
		P.Rect(Back.X, Back.Y, Back.W, Back.H, FGlpPainter::Srgb(1.f, 1.f, 1.f, 0.06f * Waiting));
		P.Text(TEXT("BACK   (ESC)"), Back.X, Back.Y + 29.f, EAlign::Centre, Back.W, 20.f, FGlpPainter::Srgb(0.72f, 0.75f, 0.8f, Waiting));
		// Space as on the screen under the showroom: it starts the game on the title screen and goes back on the menu.
		Centred(FString::Printf(TEXT("left / right or A / D: car     up / down or W / S: paint     C or Y: mix a colour     drag the car to turn it, the wheel brings it closer     %s"),
			Room.bFromMenu ? TEXT("ENTER or A: take it     ESC, SPACE or B: back") : TEXT("ENTER, SPACE or A: take it     ESC or B: back")), Y + 190.f, 15.f, Grey);
		// The figures are the simulation's, read from the car's specification (CarFacts), and say so: a mass there carries
		// a driver and fuel, and the power and torque are its curve's peaks, which can sit off the brochure's.
		Centred(FString::Printf(TEXT("%s     ·     the figures are the simulation's, the mass with a driver and fuel"),
			Room.bFromMenu ? TEXT("another car starts a new game; the car and every car's paint are kept for next time") : TEXT("the car and every car's paint are kept for next time")),
			Y + 216.f, 14.f, FGlpPainter::Srgb(0.55f, 0.58f, 0.62f));
		if (Room.Picker.IsSet())
		{
			DrawPicker(P, Size, Room.Picker.GetValue());
		}
	}
}

void FGlpFrameRate::Frame(double Now)
{
	if (Since < 0.0 || Now < Since)
	{
		// The first frame, or a clock that stepped back: the count starts here.
		Since = Now;
		Frames = 0;
		return;
	}
	Frames++;
	if (const double Span = Now - Since; Span >= Window)
	{
		// By the time that really passed: a window closes on the first frame past its half second, not on the tick.
		Rate = FMath::RoundToInt32(Frames / Span);
		Since = Now;
		Frames = 0;
	}
}

const TCHAR* GlpHudLines::WeatherLabel(int32 Weather)
{
	return Weather == GLP_WEATHER_OVERCAST ? TEXT("OVERCAST") : Weather == GLP_WEATHER_WET ? TEXT("WET ROAD") : Weather == GLP_WEATHER_RAIN ? TEXT("RAIN") : TEXT("");
}

float GlpHudLines::First(bool bFilm)
{
	return bFilm ? 66.f : 84.f;
}

int32 GlpHudLines::SelfTest(TArray<FString>& Failures)
{
	int32 Checks = 0;
	auto Check = [&Failures, &Checks](bool bHeld, const TCHAR* What)
	{
		Checks++;
		if (!bHeld)
		{
			Failures.Add(FString::Printf(TEXT("hud: %s"), What));
		}
	};

	// The frame rate: nothing before a window has passed, then the frames of each half second.
	FGlpFrameRate Rate;
	Rate.Frame(100.0);
	Check(Rate.Shown() == 0, TEXT("no frame rate before the first half second"));
	for (int32 i = 1; i <= 120; i++)
	{
		Rate.Frame(100.0 + i / 60.0);
	}
	Check(Rate.Shown() == 60, TEXT("sixty frames a second read 60"));
	for (int32 i = 1; i <= 90; i++)
	{
		Rate.Frame(102.0 + i / 30.0);
	}
	Check(Rate.Shown() == 30, TEXT("thirty frames a second read 30"));
	// One frame of two seconds among sixty a second: the window it closes reads what it cost, the next one 60 again.
	FGlpFrameRate Hitch;
	for (int32 i = 0; i <= 15; i++)
	{
		Hitch.Frame(i / 60.0);
	}
	Hitch.Frame(15.0 / 60.0 + 2.0);
	Check(Hitch.Shown() == 7, TEXT("a frame of two seconds shows in its window"));
	for (int32 i = 1; i <= 40; i++)
	{
		Hitch.Frame(15.0 / 60.0 + 2.0 + i / 60.0);
	}
	Check(Hitch.Shown() == 60, TEXT("the window after a slow frame is right again"));
	// A clock that steps back (a suspended machine's, a test's) starts the count anew and leaves no absurd figure.
	Hitch.Frame(1.0);
	for (int32 i = 1; i <= 60; i++)
	{
		Hitch.Frame(1.0 + i / 120.0);
	}
	Check(Hitch.Shown() == 120, TEXT("a clock that stepped back starts the count anew"));
	// Hours into a session the clock's steps are still whole: 144 frames a second after a week of seconds.
	FGlpFrameRate Late;
	for (int32 i = 0; i <= 144; i++)
	{
		Late.Frame(604800.0 + i / 144.0);
	}
	Check(Late.Shown() == 144, TEXT("the count is right a week into the clock"));

	// The weather's line: the core's names, nothing on a clear day or for a number it does not know.
	Check(FCString::Strcmp(WeatherLabel(GLP_WEATHER_OVERCAST), TEXT("OVERCAST")) == 0 && FCString::Strcmp(WeatherLabel(GLP_WEATHER_WET), TEXT("WET ROAD")) == 0
		&& FCString::Strcmp(WeatherLabel(GLP_WEATHER_RAIN), TEXT("RAIN")) == 0, TEXT("the weather's names"));
	Check(!*WeatherLabel(GLP_WEATHER_DRY) && !*WeatherLabel(7) && !*WeatherLabel(-1), TEXT("no weather line on a clear day"));

	// The lines under the clock: clear of the readouts' band in the other views, both inside the film view's bar.
	Check(First(false) - 16.f >= 64.f, TEXT("the lines under the clock stand below the readouts' band"));
	Check(First(true) - 16.f >= 50.f && First(true) + Pitch + 5.f <= FilmBar, TEXT("two lines fit in the film view's bar under the clock"));
	return Checks;
}

AGlpGameMode::AGlpGameMode()
{
	HUDClass = AGlpHud::StaticClass();
	DefaultPawnClass = nullptr;
}

AGlpGame* AGlpHud::Game()
{
	if (!GameActor.IsValid())
	{
		for (TActorIterator<AGlpGame> It(GetWorld()); It; ++It)
		{
			GameActor = *It;
			break;
		}
	}
	return GameActor.Get();
}

void AGlpHud::DrawHUD()
{
	Super::DrawHUD();
	FrameRate.Frame(FPlatformTime::Seconds());
	// -GlpNoHud: pictures of the scene alone (the comparisons with the Godot version, whose --no-hud does the same).
	static const bool bNoHud = FParse::Param(FCommandLine::Get(), TEXT("GlpNoHud"));
	if (bNoHud)
	{
		return;
	}
	AGlpGame* G = Game();
	if (!G || !G->HasSession() || !Canvas)
	{
		return;
	}
	const FGlpPainter P(Canvas, FMath::Min(Canvas->ClipX / LayoutWidth, Canvas->ClipY / LayoutHeight));
	if (G->Showroom().bOpen)
	{
		// The showroom has the screen (0.79): the title, the readouts and the menu it was opened from wait behind it undrawn.
		DrawShowroom(P, *G);
		return;
	}
	const FVector2D Size = P.Size();
	const glp_state& S = G->State();
	const float Flash = GetWorld()->GetRealTimeSeconds();
	const FLinearColor White = FGlpPainter::Srgb(1.f, 1.f, 1.f);
	const FLinearColor Band = FGlpPainter::Srgb(0.05f, 0.05f, 0.06f, 0.55f);

	// ---- the film view's frame (0.78) -------------------------------------------------------------------
	// A film's black bars (2.25 to 1 on a 16:9 screen) with the readouts set in them as plain type: the speed, the gear
	// and the revs as a thin line that reddens towards the limiter, where the other views draw the instrument cluster.
	const bool bFilm = G->InFilmView();
	constexpr float FilmBar = GlpHudLines::FilmBar;
	if (bFilm)
	{
		const FLinearColor Black = FGlpPainter::Srgb(0.f, 0.f, 0.f);
		P.Rect(0.f, 0.f, Size.X, FilmBar, Black);
		P.Rect(0.f, Size.Y - FilmBar, Size.X, FilmBar, Black);
		if (S.phase == GLP_PHASE_DRIVING || S.phase == GLP_PHASE_START_ENGINE)
		{
			const float Speed = FMath::Abs(S.speed) * (G->UsesKmh() ? GlpSpace::KmhPerMps : GlpSpace::MphPerMps);
			const FString Gear = S.gear < 0 ? FString(TEXT("R")) : S.gear == 0 ? FString(TEXT("N")) : FString::FromInt(S.gear);
			// Set in the bar's upper two thirds: the keys' line of a run's first seconds is written along its foot.
			const float Middle = Size.Y - FilmBar * 0.5f - 12.f;
			P.Text(FString::Printf(TEXT("%d"), FMath::RoundToInt(Speed)), Size.X * 0.5f - 170.f, Middle + 16.f, EAlign::Right, 160.f, 44.f, White);
			P.Text(G->UsesKmh() ? TEXT("KM/H") : TEXT("MPH"), Size.X * 0.5f + 2.f, Middle + 14.f, EAlign::Left, 80.f, 15.f, FGlpPainter::Srgb(0.72f, 0.75f, 0.8f));
			P.Text(Gear, Size.X * 0.5f + 90.f, Middle + 16.f, EAlign::Left, 60.f, 44.f, FGlpPainter::Srgb(1.f, 0.85f, 0.3f));
			const float Revs = FMath::Clamp(S.rpm / FMath::Max(G->Spec().redline_rpm, 1000.f), 0.f, 1.f);
			const float Line = 420.f;
			P.Rect(Size.X * 0.5f - Line * 0.5f, Middle + 28.f, Line, 3.f, FGlpPainter::Srgb(1.f, 1.f, 1.f, 0.16f));
			P.Rect(Size.X * 0.5f - Line * 0.5f, Middle + 28.f, Line * Revs, 3.f,
				Revs > 0.9f ? FGlpPainter::Srgb(1.f, 0.3f, 0.25f) : FGlpPainter::Srgb(0.92f, 0.92f, 0.92f));
		}
	}

	// ---- instrument panel at the foot of the screen, except from the driver's seat ---------------------
	float BandTop = Size.Y;
	if (!bFilm && !G->InCockpit() && G->InstrumentPanel())
	{
		const float Width = FMath::Min(Size.X * 0.36f, 580.f);
		const float Height = Width * 400.f / 1024.f;
		BandTop = Size.Y - Height - 16.f;
		P.Rect(0.f, BandTop, Size.X, Size.Y - BandTop, Band);
		P.Texture(G->InstrumentPanel(), Size.X * 0.5f - Width * 0.5f + G->DashOffset(), Size.Y - Height - 8.f, Width, Height);
	}

	// ---- top readouts -------------------------------------------------------------------------
	if (!bFilm)
	{
		P.Rect(0.f, 0.f, Size.X, 64.f, FGlpPainter::Srgb(0.05f, 0.05f, 0.06f, 0.5f));
	}
	const bool bHurry = S.time_remaining < 10.f && static_cast<int32>(Flash * 4.f) % 2 == 0;
	P.Text(FString::Printf(TEXT("TIME %04.1f"), FMath::Max(S.time_remaining, 0.f)), 20.f, 44.f, EAlign::Left, -1.f, 34.f, bHurry ? FGlpPainter::Srgb(1.f, 0.3f, 0.25f) : White);
	P.Text(TEXT("SCORE ") + Thousands(S.score), Size.X * 0.5f - 200.f, 44.f, EAlign::Centre, 400.f, 30.f, FGlpPainter::Srgb(1.f, 0.85f, 0.3f));
	P.Text(FString::Printf(TEXT("LAP %d  %.1fs"), S.lap, S.lap_time), Size.X - 320.f, 44.f, EAlign::Right, 300.f, 26.f, FGlpPainter::Srgb(0.9f, 0.9f, 0.9f));
	const TCHAR* Route = S.route == GLP_ROUTE_SPEED ? TEXT("SPEED TRACK") : S.route == GLP_ROUTE_STUNT ? TEXT("STUNT TRACK") : TEXT("");
	P.Text(Route, Size.X - 320.f, 62.f, EAlign::Right, 300.f, 14.f, FGlpPainter::Srgb(0.7f, 0.85f, 1.f));
	// Under the band the picture is the sky as often as not, and pale type on a pale sky does not read: every line there
	// stands on a plate of the band's tone (0.81; the Godot version printed them bare). The film view's bar is black
	// already. A plate is as tall as the lines' pitch, so two lines' plates meet without a seam.
	auto Plated = [&P, &Band, bFilm](const FString& Text, float X, float Y, float Width, float FontSize, const FLinearColor& Colour)
	{
		if (Text.IsEmpty())
		{
			return;
		}
		if (!bFilm)
		{
			const float Wide = P.TextWidth(Text, FontSize);
			P.Rect(X + Width - Wide - 8.f, Y - 15.f, Wide + 16.f, GlpHudLines::Pitch, Band);
		}
		P.Text(Text, X, Y, EAlign::Right, Width, FontSize, Colour);
	};
	if (S.grudge_match)
	{
		Plated(FString::Printf(TEXT("%sBEST %s"), S.ghost_visible ? TEXT("GHOST  ") : TEXT(""), *LapTime(S.best_lap_time)), Size.X - 320.f, 84.f, 300.f, 16.f, FGlpPainter::Srgb(0.6f, 0.9f, 1.f));
	}
	// The updater's word in the corner while a download the player asked for runs or waits to be installed (0.78); the
	// title screen has its own line.
	if (const int32 Update = G->UpdatePhase(); S.phase != GLP_PHASE_SELECT_TRANSMISSION && Update >= GLP_UPDATE_DOWNLOADING && Update <= GLP_UPDATE_QUIT_NOW)
	{
		// In the film view the bar has no line left under the best lap's: the notice stands on that line, to its left
		// (until 0.80 it was printed over it in a grudge match).
		const float Right = bFilm && S.grudge_match ? 230.f : 20.f;
		Plated(G->UpdateNotice(), Size.X - Right - 600.f, bFilm ? 84.f : 104.f, 600.f, 14.f, FGlpPainter::Srgb(0.6f, 0.9f, 1.f));
	}
	// Under the clock (Hud.cs): the frame rate when F6 or the menu asked for it, and the credit's weather when it is
	// not a clear day (the grip is down on a wet road). The damage gauge (T6) belongs above them.
	struct FLeftLine
	{
		FString Text;
		FLinearColor Colour;
	};
	TArray<FLeftLine, TInlineAllocator<2>> LeftLines;
	// The plate is as wide as its widest line; the counter's by three figures, so that it does not breathe with them.
	float LeftWide = 0.f;
	if (G->ShowsFrameRate())
	{
		// Dashes for the game's first half second: there is no count yet, and a nought would be a figure.
		LeftLines.Add({ FrameRate.Shown() > 0 ? FString::Printf(TEXT("%d FPS"), FrameRate.Shown()) : FString(TEXT("-- FPS")), FGlpPainter::Srgb(0.7f, 0.9f, 0.7f) });
		LeftWide = P.TextWidth(TEXT("000 FPS"), 16.f);
	}
	if (const TCHAR* Weather = GlpHudLines::WeatherLabel(S.weather); *Weather)
	{
		LeftLines.Add({ Weather, FGlpPainter::Srgb(0.75f, 0.85f, 1.f) });
		LeftWide = FMath::Max(LeftWide, P.TextWidth(Weather, 16.f));
	}
	const float LeftFirst = GlpHudLines::First(bFilm);
	if (!bFilm && !LeftLines.IsEmpty())
	{
		P.Rect(12.f, LeftFirst - 15.f, LeftWide + 16.f, LeftLines.Num() * GlpHudLines::Pitch, Band);
	}
	for (int32 i = 0; i < LeftLines.Num(); i++)
	{
		P.Text(LeftLines[i].Text, 20.f, LeftFirst + i * GlpHudLines::Pitch, EAlign::Left, -1.f, 16.f, LeftLines[i].Colour);
	}

	// ---- centre messages ------------------------------------------------------------------------
	// The game-over panel says it all; the session's OUT OF TIME would print over the table.
	const FString Message = S.phase == GLP_PHASE_FINISHED ? FString() : G->Message();
	if (!Message.IsEmpty())
	{
		const bool bBlink = Message.StartsWith(TEXT("WRONG WAY"), ESearchCase::CaseSensitive) || Message.StartsWith(TEXT("OFF ROAD"), ESearchCase::CaseSensitive);
		if (!bBlink || static_cast<int32>(Flash * 3.f) % 2 == 0)
		{
			P.Text(Message, 0.f, Size.Y * 0.28f, EAlign::Centre, Size.X, 44.f, FGlpPainter::Srgb(1.f, 0.95f, 0.6f));
		}
	}

	// The Esc menu stands in the same place as the title's and the game-over panel: they wait behind it undrawn (their
	// type showed through the menu's panel).
	const bool bMenu = G->Menu().bOpen;
	if (S.phase == GLP_PHASE_SELECT_TRANSMISSION && !G->Attract() && !bMenu)
	{
		// The title on a dark panel, readable over the cockpit behind it, with one thing to do: press Enter.
		const FLinearColor Grey = FGlpPainter::Srgb(0.72f, 0.75f, 0.8f);
		const FLinearColor Gold = FGlpPainter::Srgb(1.f, 0.85f, 0.3f);
		auto Centred = [&](const FString& Text, float Y, float FontSize, const FLinearColor& Colour) { P.Text(Text, 0.f, Y, EAlign::Centre, Size.X, FontSize, Colour); };
		const float PanelY = Size.Y * 0.22f;
		P.Rect(Size.X * 0.5f - 440.f, PanelY, 880.f, 474.f, FGlpPainter::Srgb(0.03f, 0.03f, 0.04f, 0.8f));
		const float Y = PanelY + 70.f;
		Centred(TEXT("GRUDGELAP"), Y, 58.f, White);
		Centred(FString::Printf(TEXT("<   %s   >"), *G->CarName()), Y + 62.f, 28.f, White);
		// The car's figures and the way to the others, as the Godot version's title (0.79: the figures, the car's place in
		// the line-up and the showroom came over; until 0.78 the line read "left / right or the stick: car").
		int32 Number = 0, Count = 0;
		G->Place(G->DrivenCar(), Number, Count);
		Centred(FString::Printf(TEXT("%s     left / right: car %d of %d     down: showroom and paints"), *G->TitleFacts(), Number, Count), Y + 90.f, 15.f, Grey);
		const TCHAR* Labels[] = { TEXT("1  AUTOMATIC"), TEXT("2  STICK"), TEXT("3  STICK + CLUTCH") };
		for (int32 i = 0; i < 3; i++)
		{
			const bool bChosen = G->Gearbox() == i;
			const float X = Size.X * 0.5f - 420.f + i * 280.f;
			if (bChosen)
			{
				P.Rect(X + 10.f, Y + 112.f, 260.f, 46.f, FGlpPainter::Srgb(1.f, 0.85f, 0.3f, 0.16f));
			}
			P.Text(Labels[i], X, Y + 144.f, EAlign::Centre, 280.f, 22.f, bChosen ? Gold : Grey);
		}
		Centred(TEXT("STICK: you shift, the clutch is worked for you     STICK + CLUTCH: you can stall"), Y + 186.f, 15.f, Grey);
		Centred(TEXT("pad: stick steers   RT gas   LT brake   RB / LB shift (here: gearbox)   X clutch   A start   B skip   Y camera"), Y + 212.f, 14.f, Grey);
		const float Pulse = 0.7f + 0.3f * FMath::Sin(Flash * 4.f);
		Centred(TEXT("PRESS ENTER OR A TO START"), Y + 258.f, 38.f, FGlpPainter::Srgb(1.f, 0.85f, 0.3f, Pulse));
		Centred(S.grudge_match ? FString::Printf(TEXT("grudge match: the ghost of your best lap, %s, is waiting"), *LapTime(S.best_lap_time)) : FString(TEXT("finish a lap and its ghost will race you next time")),
			Y + 312.f, 18.f, FGlpPainter::Srgb(0.6f, 0.9f, 1.f));
		Centred(FString::Printf(TEXT("ESC menu   C camera: %s   U units   H weather: %s   L light: %s   M replay music folder   F11 full screen"), G->ViewName(), G->WeatherName(), G->LightName()), Y + 346.f, 15.f, Grey);
		// This copy's version and what its updater has to say (0.78). Nothing is looked for until the player asks.
		const FString Notice = G->UpdateNotice();
		if (Notice.IsEmpty())
		{
			Centred(FString::Printf(TEXT("v%s   F6 frame rate   F10 or the menu: check for updates"), *G->Version()), Y + 370.f, 15.f, Grey);
		}
		else
		{
			Centred(FString::Printf(TEXT("v%s   %s"), *G->Version(), *Notice), Y + 370.f, 15.f, G->UpdatePhase() == GLP_UPDATE_FAILED ? FGlpPainter::Srgb(1.f, 0.7f, 0.35f) : FGlpPainter::Srgb(0.6f, 0.9f, 1.f));
		}
		// The engine's and NVIDIA's credits, as their licences ask of a product's splash or credits (0.76; THIRD_PARTY.md).
		Centred(TEXT("Made with the Unreal(R) Engine   NVIDIA DLSS on NVIDIA RTX graphics cards"), Y + 394.f, 13.f, FGlpPainter::Srgb(0.55f, 0.58f, 0.62f));
	}
	else if (S.phase == GLP_PHASE_FINISHED && !bMenu)
	{
		// The end of the credit (Hud.DrawFinished): the score, the entry of the initials when it made the table, and the table.
		const FLinearColor Grey = FGlpPainter::Srgb(0.72f, 0.75f, 0.8f);
		const FLinearColor Gold = FGlpPainter::Srgb(1.f, 0.85f, 0.3f);
		auto Centred = [&](const FString& Text, float Y, float FontSize, const FLinearColor& Colour) { P.Text(Text, 0.f, Y, EAlign::Centre, Size.X, FontSize, Colour); };
		const float PanelX = Size.X * 0.5f - 440.f;
		const float PanelY = Size.Y * 0.12f;
		P.Rect(PanelX, PanelY, 880.f, 580.f, FGlpPainter::Srgb(0.03f, 0.03f, 0.04f, 0.8f));
		const float Y = PanelY + 58.f;
		Centred(TEXT("GAME OVER   SCORE ") + Thousands(S.score), Y, 40.f, White);
		const FGlpScores& Board = G->HighScores();
		if (Board.Entering())
		{
			Centred(TEXT("YOU MADE THE TOP TEN - ENTER YOUR INITIALS"), Y + 70.f, 22.f, Gold);
			const float Pulse = 0.55f + 0.45f * FMath::Sin(Flash * 6.f);
			const FString Initials = Board.Initials();
			for (int32 i = 0; i < Initials.Len(); i++)
			{
				const float X = Size.X * 0.5f + (i - 1) * 90.f - 45.f;
				const bool bCurrent = i == Board.Cursor();
				P.Text(FString::Chr(Initials[i]), X, Y + 180.f, EAlign::Centre, 90.f, 72.f, bCurrent ? FGlpPainter::Srgb(1.f, 0.85f, 0.3f, Pulse) : White);
				if (bCurrent)
				{
					P.Rect(X + 20.f, Y + 194.f, 50.f, 4.f, Gold);
				}
			}
			Centred(TEXT("up / down: letter     left / right: place     ENTER or A: take it"), Y + 250.f, 16.f, Grey);
		}
		else
		{
			const float Top = Y + 56.f;
			const float X0 = PanelX + 40.f;
			P.Text(TEXT("NAME"), X0 + 70.f, Top, EAlign::Left, 100.f, 15.f, Grey);
			P.Text(TEXT("SCORE"), X0 + 170.f, Top, EAlign::Right, 160.f, 15.f, Grey);
			P.Text(TEXT("BEST LAP"), X0 + 370.f, Top, EAlign::Left, 120.f, 15.f, Grey);
			P.Text(TEXT("ROUTE"), X0 + 500.f, Top, EAlign::Left, 120.f, 15.f, Grey);
			P.Text(TEXT("CAR"), X0 + 620.f, Top, EAlign::Left, 200.f, 15.f, Grey);
			const TArray<FGlpScore>& Entries = Board.Entries();
			for (int32 i = 0; i < Entries.Num(); i++)
			{
				const FGlpScore& E = Entries[i];
				const float Row = Top + 34.f + i * 34.f;
				const FLinearColor Colour = i == Board.Rank() ? Gold : i % 2 == 0 ? White : FGlpPainter::Srgb(0.84f, 0.85f, 0.88f);
				P.Text(FString::Printf(TEXT("%d."), i + 1), X0, Row, EAlign::Right, 44.f, 22.f, Colour);
				P.Text(E.Initials, X0 + 70.f, Row, EAlign::Left, 100.f, 22.f, Colour);
				P.Text(Thousands(E.Score), X0 + 170.f, Row, EAlign::Right, 160.f, 22.f, Colour);
				P.Text(E.HasLap() ? LapTime(E.BestLap) : TEXT("-:--.-"), X0 + 370.f, Row, EAlign::Left, 120.f, 22.f, Colour);
				P.Text(E.Route == GLP_ROUTE_SPEED ? TEXT("SPEED") : E.Route == GLP_ROUTE_STUNT ? TEXT("STUNT") : TEXT("-"), X0 + 500.f, Row, EAlign::Left, 120.f, 22.f, Colour);
				P.Text(E.Car, X0 + 620.f, Row, EAlign::Left, 220.f, 15.f, Colour);
			}
		}
		Centred(TEXT("F5: new game     ESC or BACK: menu"), PanelY + 580.f - 34.f, 20.f, Grey);
	}

	// The keys: on the instrument band outside the cockpit; from the driver's seat only for the first seconds of
	// a run, on a thin strip over the dashboard.
	if (S.phase == GLP_PHASE_DRIVING && (bFilm ? G->DrivingSeconds() < 20.f : !G->InCockpit() || G->DrivingSeconds() < 20.f))
	{
		const float Y = BandTop < Size.Y ? BandTop + 18.f : Size.Y - 8.f;
		if (BandTop >= Size.Y && !bFilm)
		{
			P.Rect(0.f, Size.Y - 26.f, Size.X, 26.f, Band);
		}
		P.Text(FString::Printf(TEXT("arrows/WASD drive   E/Q or 1-%d gears   N neutral   shift = clutch   ENTER key   SPACE abort/skip   C camera   U units   ESC menu"), S.forward_gears),
			20.f, Y, EAlign::Left, Size.X - 40.f, 13.f, FGlpPainter::Srgb(0.8f, 0.8f, 0.8f, 0.8f));
	}

	// ---- the Esc menu, over everything (0.78; Hud.DrawMenu's measures) -------------------------------------
	if (const FGlpMenu& Menu = G->Menu(); Menu.bOpen)
	{
		const FLinearColor Grey = FGlpPainter::Srgb(0.72f, 0.75f, 0.8f);
		const FLinearColor Gold = FGlpPainter::Srgb(1.f, 0.85f, 0.3f);
		P.Rect(0.f, 0.f, Size.X, Size.Y, FGlpPainter::Srgb(0.f, 0.f, 0.f, 0.45f));
		const FVector2f Corner = Menu.Origin(FVector2f(static_cast<float>(Size.X), static_cast<float>(Size.Y)));
		const float Height = Menu.Height();
		P.Rect(Corner.X, Corner.Y, FGlpMenu::Width, Height, FGlpPainter::Srgb(0.03f, 0.03f, 0.04f, 0.95f));
		P.Text(S.phase == GLP_PHASE_SELECT_TRANSMISSION ? TEXT("GRUDGELAP") : TEXT("PAUSED"), Corner.X, Corner.Y + 62.f, EAlign::Centre, FGlpMenu::Width, 40.f, White);
		for (int32 i = 0; i < Menu.Lines.Num(); i++)
		{
			const float Top = Corner.Y + FGlpMenu::LineTop + i * FGlpMenu::LinePitch;
			const bool bCurrent = i == Menu.Cursor;
			if (bCurrent)
			{
				P.Rect(Corner.X + FGlpMenu::Margin, Top, FGlpMenu::Width - 2.f * FGlpMenu::Margin, FGlpMenu::LineHeight, FGlpPainter::Srgb(1.f, 0.85f, 0.3f, 0.16f));
			}
			P.Text(Menu.Lines[i].Text, Corner.X + FGlpMenu::Margin + 20.f, Top + 29.f, EAlign::Left, FGlpMenu::Width - 2.f * FGlpMenu::Margin - 40.f, 24.f, bCurrent ? Gold : Grey);
		}
		const float Under = Corner.Y + FGlpMenu::LineTop + Menu.Lines.Num() * FGlpMenu::LinePitch;
		P.Text(Menu.Note, Corner.X + 20.f, Under + 30.f, EAlign::Centre, FGlpMenu::Width - 40.f, 15.f, Menu.bNoteWarns ? FGlpPainter::Srgb(1.f, 0.7f, 0.35f) : FGlpPainter::Srgb(0.6f, 0.9f, 1.f));
		P.Text(TEXT("up / down: choose     ENTER, A or a click: take it     left / right: change"), Corner.X, Under + 62.f, EAlign::Centre, FGlpMenu::Width, 14.f, Grey);
		P.Text(TEXT("ESC, SPACE or B: back to the game"), Corner.X, Under + 84.f, EAlign::Centre, FGlpMenu::Width, 14.f, Grey);
	}
}

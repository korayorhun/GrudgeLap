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
	const FVector2D Size = P.Size();
	const glp_state& S = G->State();
	const float Flash = GetWorld()->GetRealTimeSeconds();
	const FLinearColor White = FGlpPainter::Srgb(1.f, 1.f, 1.f);
	const FLinearColor Band = FGlpPainter::Srgb(0.05f, 0.05f, 0.06f, 0.55f);

	// ---- the film view's frame (0.78) -------------------------------------------------------------------
	// A film's black bars (2.25 to 1 on a 16:9 screen) with the readouts set in them as plain type: the speed, the gear
	// and the revs as a thin line that reddens towards the limiter, where the other views draw the instrument cluster.
	const bool bFilm = G->InFilmView();
	constexpr float FilmBar = 94.f;
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
	if (S.grudge_match)
	{
		P.Text(FString::Printf(TEXT("%sBEST %s"), S.ghost_visible ? TEXT("GHOST  ") : TEXT(""), *LapTime(S.best_lap_time)), Size.X - 320.f, 84.f, EAlign::Right, 300.f, 16.f, FGlpPainter::Srgb(0.6f, 0.9f, 1.f));
	}
	// The updater's word in the corner while a download the player asked for runs or waits to be installed (0.78); the
	// title screen has its own line.
	if (const int32 Update = G->UpdatePhase(); S.phase != GLP_PHASE_SELECT_TRANSMISSION && Update >= GLP_UPDATE_DOWNLOADING && Update <= GLP_UPDATE_QUIT_NOW)
	{
		P.Text(G->UpdateNotice(), Size.X - 620.f, bFilm ? 84.f : 104.f, EAlign::Right, 600.f, 14.f, FGlpPainter::Srgb(0.6f, 0.9f, 1.f));
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
		Centred(TEXT("left / right or the stick: car"), Y + 90.f, 15.f, Grey);
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
			Centred(FString::Printf(TEXT("v%s   F10 or the menu: check for updates"), *G->Version()), Y + 370.f, 15.f, Grey);
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

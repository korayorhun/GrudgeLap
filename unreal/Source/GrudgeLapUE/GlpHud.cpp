#include "GlpHud.h"

#include "Engine/Canvas.h"
#include "EngineUtils.h"
#include "GlpDraw.h"
#include "GlpGame.h"

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

	// ---- instrument panel at the foot of the screen, except from the driver's seat ---------------------
	float BandTop = Size.Y;
	if (!G->InCockpit() && G->InstrumentPanel())
	{
		const float Width = FMath::Min(Size.X * 0.36f, 580.f);
		const float Height = Width * 400.f / 1024.f;
		BandTop = Size.Y - Height - 16.f;
		P.Rect(0.f, BandTop, Size.X, Size.Y - BandTop, Band);
		P.Texture(G->InstrumentPanel(), Size.X * 0.5f - Width * 0.5f + G->DashOffset(), Size.Y - Height - 8.f, Width, Height);
	}

	// ---- top readouts -------------------------------------------------------------------------
	P.Rect(0.f, 0.f, Size.X, 64.f, FGlpPainter::Srgb(0.05f, 0.05f, 0.06f, 0.5f));
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

	// ---- centre messages ------------------------------------------------------------------------
	const FString& Message = G->Message();
	if (!Message.IsEmpty())
	{
		const bool bBlink = Message.StartsWith(TEXT("WRONG WAY"), ESearchCase::CaseSensitive) || Message.StartsWith(TEXT("OFF ROAD"), ESearchCase::CaseSensitive);
		if (!bBlink || static_cast<int32>(Flash * 3.f) % 2 == 0)
		{
			P.Text(Message, 0.f, Size.Y * 0.28f, EAlign::Centre, Size.X, 44.f, FGlpPainter::Srgb(1.f, 0.95f, 0.6f));
		}
	}

	if (S.phase == GLP_PHASE_SELECT_TRANSMISSION && !G->Attract())
	{
		// The title on a dark panel, readable over the cockpit behind it, with one thing to do: press Enter.
		const FLinearColor Grey = FGlpPainter::Srgb(0.72f, 0.75f, 0.8f);
		const FLinearColor Gold = FGlpPainter::Srgb(1.f, 0.85f, 0.3f);
		auto Centred = [&](const FString& Text, float Y, float FontSize, const FLinearColor& Colour) { P.Text(Text, 0.f, Y, EAlign::Centre, Size.X, FontSize, Colour); };
		const float PanelY = Size.Y * 0.22f;
		P.Rect(Size.X * 0.5f - 440.f, PanelY, 880.f, 450.f, FGlpPainter::Srgb(0.03f, 0.03f, 0.04f, 0.8f));
		const float Y = PanelY + 70.f;
		Centred(TEXT("GRUDGELAP"), Y, 58.f, White);
		Centred(FString::Printf(TEXT("<   %s   >"), *G->CarName()), Y + 62.f, 28.f, White);
		Centred(TEXT("left / right: car"), Y + 90.f, 15.f, Grey);
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
		const float Pulse = 0.7f + 0.3f * FMath::Sin(Flash * 4.f);
		Centred(TEXT("PRESS ENTER TO START"), Y + 258.f, 38.f, FGlpPainter::Srgb(1.f, 0.85f, 0.3f, Pulse));
		Centred(S.grudge_match ? FString::Printf(TEXT("grudge match: the ghost of your best lap, %s, is waiting"), *LapTime(S.best_lap_time)) : FString(TEXT("finish a lap and its ghost will race you next time")),
			Y + 312.f, 18.f, FGlpPainter::Srgb(0.6f, 0.9f, 1.f));
		Centred(TEXT("Unreal Engine version   C camera   U units   F11 full screen"), Y + 346.f, 15.f, Grey);
	}
	else if (S.phase == GLP_PHASE_FINISHED)
	{
		P.Text(TEXT("GAME OVER   SCORE ") + Thousands(S.score), 0.f, Size.Y * 0.45f, EAlign::Centre, Size.X, 40.f, White);
		P.Text(TEXT("F5 for a new game"), 0.f, Size.Y * 0.45f + 44.f, EAlign::Centre, Size.X, 24.f, FGlpPainter::Srgb(0.8f, 0.8f, 0.8f));
	}

	// The keys: on the instrument band outside the cockpit; from the driver's seat only for the first seconds of
	// a run, on a thin strip over the dashboard.
	if (S.phase == GLP_PHASE_DRIVING && (!G->InCockpit() || G->DrivingSeconds() < 20.f))
	{
		const float Y = BandTop < Size.Y ? BandTop + 18.f : Size.Y - 8.f;
		if (BandTop >= Size.Y)
		{
			P.Rect(0.f, Size.Y - 26.f, Size.X, 26.f, Band);
		}
		P.Text(FString::Printf(TEXT("arrows/WASD drive   E/Q or 1-%d gears   N neutral   shift = clutch   ENTER key   SPACE abort/skip   C camera   U units   F11 fullscreen"), S.forward_gears),
			20.f, Y, EAlign::Left, Size.X - 40.f, 13.f, FGlpPainter::Srgb(0.8f, 0.8f, 0.8f, 0.8f));
	}
}

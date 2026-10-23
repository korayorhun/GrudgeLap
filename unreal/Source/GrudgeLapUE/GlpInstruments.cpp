#include "GlpInstruments.h"

#include "Engine/Canvas.h"
#include "Engine/CanvasRenderTarget2D.h"
#include "GlpDraw.h"
#include "GlpSpace.h"

namespace
{
	// The panel is laid out on 1024 x 400 (InstrumentCluster.PanelSize) and drawn at twice that: the mip chain
	// then smooths the edges where the canvas draws none, both on the small binnacle face and in the HUD.
	constexpr int32 PanelWidth = 1024;
	constexpr int32 PanelHeight = 400;
	constexpr int32 Supersample = 2;

	using EAlign = FGlpPainter::EAlign;

	const FLinearColor& Ink()
	{
		static const FLinearColor Colour = FGlpPainter::Srgb(0.92f, 0.92f, 0.9f);
		return Colour;
	}

	void Dial(const FGlpPainter& P, const FVector2D& Centre, float Radius, float Value, float Max, int32 Majors, int32 MinorsPerMajor, const TCHAR* Caption, float RedlineFrom)
	{
		constexpr float Start = UE_PI * 0.75f;
		constexpr float Sweep = UE_PI * 1.5f;
		P.Circle(Centre, Radius + 8.f, FGlpPainter::Srgb(0.16f, 0.16f, 0.17f));
		P.Circle(Centre, Radius + 3.f, FGlpPainter::Srgb(0.005f, 0.005f, 0.008f));
		if (RedlineFrom > 0.f)
		{
			P.Arc(Centre, Radius - 9.f, Start + Sweep * (RedlineFrom / Max), Start + Sweep, 16, FGlpPainter::Srgb(0.85f, 0.12f, 0.08f), 12.f);
		}
		const int32 Steps = Majors * MinorsPerMajor;
		for (int32 i = 0; i <= Steps; i++)
		{
			const bool bMajor = i % MinorsPerMajor == 0;
			const float A = Start + Sweep * i / Steps;
			const FVector2D Dir(FMath::Cos(A), FMath::Sin(A));
			P.Line(Centre + Dir * (Radius - (bMajor ? 22.f : 11.f)), Centre + Dir * (Radius - 2.f), Ink(), bMajor ? 4.f : 2.f);
			if (bMajor)
			{
				const FVector2D At = Centre + Dir * (Radius - 48.f) - FVector2D(24.f, -9.f);
				P.Text(FString::Printf(TEXT("%.0f"), Max * i / Steps), At.X, At.Y, EAlign::Centre, 48.f, 24.f, Ink());
			}
		}
		P.Text(Caption, Centre.X - 80.f, Centre.Y + 50.f, EAlign::Centre, 160.f, 16.f, FGlpPainter::Srgb(0.7f, 0.7f, 0.7f));
		const float T = FMath::Clamp(Value / Max, 0.f, 1.f);
		const float Angle = Start + Sweep * T;
		const FVector2D Along(FMath::Cos(Angle), FMath::Sin(Angle));
		const FVector2D Across(-Along.Y, Along.X);
		P.Triangle(Centre - Along * 24.f + Across * 5.f, Centre + Along * (Radius - 14.f), Centre - Along * 24.f - Across * 5.f, FGlpPainter::Srgb(1.f, 0.42f, 0.12f));
		P.Circle(Centre, 13.f, FGlpPainter::Srgb(0.12f, 0.12f, 0.13f));
	}

	void Readout(const FGlpPainter& P, const FVector2D& Centre, const FString& Text, float Size)
	{
		const float X = Centre.X - 56.f;
		const float Y = Centre.Y - 15.f;
		P.Rect(X, Y, 112.f, 30.f, FLinearColor::Black);
		P.Frame(X, Y, 112.f, 30.f, FGlpPainter::Srgb(0.25f, 0.25f, 0.27f), 1.5f);
		P.Text(Text, X, Y + 30.f - 7.f, EAlign::Centre, 112.f, Size, FGlpPainter::Srgb(0.85f, 0.85f, 0.8f));
	}

	void Lamp(const FGlpPainter& P, const FVector2D& Centre, const TCHAR* Label, bool bLit)
	{
		P.Circle(Centre, 16.f, bLit ? FGlpPainter::Srgb(0.95f, 0.12f, 0.08f) : FGlpPainter::Srgb(0.12f, 0.03f, 0.03f));
		P.Text(Label, Centre.X - 20.f, Centre.Y + 5.f, EAlign::Centre, 40.f, 12.f, bLit ? FGlpPainter::Srgb(1.f, 0.9f, 0.85f) : FGlpPainter::Srgb(0.35f, 0.25f, 0.25f));
	}
}

void UGlpInstruments::Create(UObject* WorldContext)
{
	Target = UCanvasRenderTarget2D::CreateCanvasRenderTarget2D(WorldContext, UCanvasRenderTarget2D::StaticClass(), PanelWidth * Supersample, PanelHeight * Supersample);
	if (!Target)
	{
		return;
	}
	Target->ClearColor = FGlpPainter::Srgb(0.025f, 0.025f, 0.03f);
	Target->bAutoGenerateMips = true;
	Target->OnCanvasRenderTargetUpdate.AddDynamic(this, &UGlpInstruments::Draw);
	Target->UpdateResource();
}

UTexture* UGlpInstruments::Texture() const
{
	return Target;
}

void UGlpInstruments::Update(const glp_state& InState, const glp_car_spec& InSpec, bool bInKmh, float DeltaSeconds)
{
	State = InState;
	Spec = InSpec;
	bKmh = bInKmh;
	bHasState = true;
	Odometer += FMath::Abs(State.speed) * DeltaSeconds;
	if (Target)
	{
		Target->FastUpdateResource();
	}
}

void UGlpInstruments::Draw(UCanvas* Canvas, int32 Width, int32 Height)
{
	const FGlpPainter P(Canvas, static_cast<float>(Width) / PanelWidth);
	P.Rect(0.f, 0.f, PanelWidth, PanelHeight, FGlpPainter::Srgb(0.025f, 0.025f, 0.03f));
	if (!bHasState)
	{
		return;
	}
	const bool bRunning = State.engine_running != 0;

	const float Speed = FMath::Abs(State.speed) * (bKmh ? GlpSpace::KmhPerMps : GlpSpace::MphPerMps);
	const float SpeedMax = bKmh ? FMath::RoundToFloat(Spec.speedo_max_mph * 1.6f / 20.f) * 20.f : Spec.speedo_max_mph;
	const FVector2D SpeedCentre(262.f, 206.f);
	// Numbers every 20 (every 40 on a long km/h scale) with four ticks between: denser scales are unreadable at a glance.
	const float Step = bKmh && SpeedMax > 280.f ? 40.f : 20.f;
	Dial(P, SpeedCentre, 176.f, Speed, SpeedMax, FMath::RoundToInt(SpeedMax / Step), 4, bKmh ? TEXT("km/h") : TEXT("MPH"), -1.f);
	const double Distance = bKmh ? Odometer / 1000.0 : Odometer / 1609.344;
	Readout(P, SpeedCentre + FVector2D(0.f, 92.f), FString::Printf(TEXT("%07.1f"), Distance), 22.f);

	const float Redline = Spec.redline_rpm;
	const float TachMax = FMath::CeilToFloat((Redline + 800.f) / 1000.f);
	Dial(P, FVector2D(762.f, 206.f), 176.f, FMath::Max(State.rpm, 0.f) / 1000.f, TachMax, FMath::RoundToInt(TachMax), 5, TEXT("RPM x 1000"), Redline / 1000.f);

	// Gear readout and warning lights in the column between the dials.
	const FString Gear = State.gear < 0 ? TEXT("R") : State.gear == 0 ? TEXT("N") : FString::FromInt(State.gear);
	P.Rect(474.f, 120.f, 76.f, 76.f, FGlpPainter::Srgb(0.01f, 0.03f, 0.02f));
	P.Frame(474.f, 120.f, 76.f, 76.f, FGlpPainter::Srgb(0.3f, 0.3f, 0.32f), 2.f);
	P.Text(Gear, 474.f, 196.f - 14.f, EAlign::Centre, 76.f, 54.f, FGlpPainter::Srgb(1.f, 0.75f, 0.2f));
	const TCHAR* Mode = State.transmission == GLP_AUTOMATIC ? TEXT("AUTO") : State.transmission == GLP_MANUAL_AUTO_CLUTCH ? TEXT("STICK") : TEXT("CLUTCH");
	P.Text(Mode, 442.f, 222.f, EAlign::Centre, 140.f, 16.f, FGlpPainter::Srgb(0.65f, 0.65f, 0.65f));
	// Oil pressure and charge lamps light up with the ignition on and the engine stopped, as in any car.
	Lamp(P, FVector2D(490.f, 268.f), TEXT("OIL"), !bRunning);
	Lamp(P, FVector2D(534.f, 268.f), TEXT("BAT"), !bRunning);
}

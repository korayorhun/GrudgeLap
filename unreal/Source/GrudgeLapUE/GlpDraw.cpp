#include "GlpDraw.h"

#include "CanvasItem.h"
#include "Engine/Canvas.h"
#include "Engine/Font.h"
#include "Engine/Texture.h"
#include "Fonts/FontMeasure.h"
#include "Framework/Application/SlateApplication.h"
#include "Rendering/SlateRenderer.h"
#include "TextureResource.h"

namespace
{
	// Godot's default theme font is a semibold sans; the engine's Roboto in its bold face reads the same at a glance.
	const UFont* HudFont()
	{
		static TWeakObjectPtr<UFont> Font;
		if (!Font.IsValid())
		{
			Font = LoadObject<UFont>(nullptr, TEXT("/Engine/EngineFonts/Roboto.Roboto"));
		}
		return Font.Get();
	}

	// Slate sizes fonts in points at 96 dots per inch; Godot in pixels.
	constexpr float PointsPerPixel = 72.f / 96.f;
}

FGlpPainter::FGlpPainter(UCanvas* InCanvas, float InScale)
	: Canvas(InCanvas)
	, Scale(InScale)
	, Font(HudFont())
{
}

FLinearColor FGlpPainter::Srgb(float R, float G, float B, float A)
{
	FLinearColor Colour = FLinearColor::FromSRGBColor(FLinearColor(R, G, B).ToFColor(false));
	Colour.A = A;
	return Colour;
}

FVector2D FGlpPainter::Size() const
{
	return FVector2D(Canvas->ClipX, Canvas->ClipY) / Scale;
}

void FGlpPainter::Rect(float X, float Y, float W, float H, const FLinearColor& Colour) const
{
	FCanvasTileItem Item(Map(FVector2D(X, Y)), Map(FVector2D(W, H)), Colour);
	Item.BlendMode = SE_BLEND_Translucent;
	Canvas->DrawItem(Item);
}

void FGlpPainter::Frame(float X, float Y, float W, float H, const FLinearColor& Colour, float Thickness) const
{
	FCanvasBoxItem Item(Map(FVector2D(X, Y)), Map(FVector2D(W, H)));
	Item.LineThickness = Thickness * Scale;
	Item.SetColor(Colour);
	Canvas->DrawItem(Item);
}

void FGlpPainter::Line(const FVector2D& A, const FVector2D& B, const FLinearColor& Colour, float Thickness) const
{
	FCanvasLineItem Item(Map(A), Map(B));
	Item.LineThickness = Thickness * Scale;
	Item.SetColor(Colour);
	Canvas->DrawItem(Item);
}

void FGlpPainter::Circle(const FVector2D& Centre, float Radius, const FLinearColor& Colour) const
{
	const int32 Sides = FMath::Clamp(FMath::RoundToInt(Radius * Scale * 0.6f), 16, 96);
	FCanvasNGonItem Item(Map(Centre), FVector2D(Radius * Scale), Sides, Colour);
	Item.BlendMode = SE_BLEND_Translucent;
	Canvas->DrawItem(Item);
}

void FGlpPainter::Arc(const FVector2D& Centre, float Radius, float From, float To, int32 Segments, const FLinearColor& Colour, float Width) const
{
	TArray<FCanvasUVTri> Triangles;
	const float Inner = Radius - Width * 0.5f;
	const float Outer = Radius + Width * 0.5f;
	auto Point = [&](float Angle, float R) { return Map(Centre + FVector2D(FMath::Cos(Angle), FMath::Sin(Angle)) * R); };
	for (int32 i = 0; i < Segments; i++)
	{
		const float A0 = FMath::Lerp(From, To, static_cast<float>(i) / Segments);
		const float A1 = FMath::Lerp(From, To, static_cast<float>(i + 1) / Segments);
		FCanvasUVTri First;
		First.V0_Pos = Point(A0, Inner);
		First.V1_Pos = Point(A0, Outer);
		First.V2_Pos = Point(A1, Outer);
		First.V0_Color = First.V1_Color = First.V2_Color = Colour;
		FCanvasUVTri Second;
		Second.V0_Pos = Point(A0, Inner);
		Second.V1_Pos = Point(A1, Outer);
		Second.V2_Pos = Point(A1, Inner);
		Second.V0_Color = Second.V1_Color = Second.V2_Color = Colour;
		Triangles.Add(First);
		Triangles.Add(Second);
	}
	FCanvasTriangleItem Item(Triangles, GWhiteTexture);
	Item.BlendMode = SE_BLEND_Translucent;
	Canvas->DrawItem(Item);
}

void FGlpPainter::Triangle(const FVector2D& A, const FVector2D& B, const FVector2D& C, const FLinearColor& Colour) const
{
	FCanvasTriangleItem Item(Map(A), Map(B), Map(C), GWhiteTexture);
	Item.SetColor(Colour);
	Item.BlendMode = SE_BLEND_Translucent;
	Canvas->DrawItem(Item);
}

void FGlpPainter::Texture(UTexture* Texture, float X, float Y, float W, float H) const
{
	if (!Texture || !Texture->GetResource())
	{
		return;
	}
	FCanvasTileItem Item(Map(FVector2D(X, Y)), Texture->GetResource(), Map(FVector2D(W, H)), FLinearColor::White);
	Item.BlendMode = SE_BLEND_Opaque;
	Canvas->DrawItem(Item);
}

void FGlpPainter::Text(const FString& Text, float X, float Y, EAlign Align, float Width, float Size, const FLinearColor& Colour) const
{
	if (Text.IsEmpty() || !Font || !FSlateApplication::IsInitialized())
	{
		return;
	}
	const FSlateFontInfo Info(Font, Size * Scale * PointsPerPixel, TEXT("Bold"));
	const TSharedRef<FSlateFontMeasure> Measure = FSlateApplication::Get().GetRenderer()->GetFontMeasureService();
	const FVector2D Extent = Measure->Measure(Text, Info);
	// Slate places text by the top of its line; the baseline sits the ascent below it (Baseline is the negative descent).
	const float Ascent = Measure->GetMaxCharacterHeight(Info) + Measure->GetBaseline(Info);
	float Left = X * Scale;
	if (Width > 0.f && Align != EAlign::Left)
	{
		const float Room = Width * Scale - static_cast<float>(Extent.X);
		Left += Align == EAlign::Centre ? Room * 0.5f : Room;
	}
	FCanvasTextItem Item(FVector2D(Left, Y * Scale - Ascent), FText::FromString(Text), Info, Colour);
	Item.BlendMode = SE_BLEND_Translucent;
	Canvas->DrawItem(Item);
}

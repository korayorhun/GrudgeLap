#pragma once

#include "CoreMinimal.h"

class UCanvas;
class UFont;

/**
 * Canvas drawing with the conventions of Godot's CanvasItem, which the HUD and the instrument panel were written
 * against (Hud.cs, InstrumentCluster.cs), so their numbers carry over unchanged: colours given as sRGB
 * components, text placed by its baseline inside a box of a given width, font sizes in pixels, and everything
 * in a virtual canvas that Scale maps onto the real one (Godot's "canvas_items" stretch of a 1600 x 900 layout).
 */
struct FGlpPainter
{
	enum class EAlign : uint8
	{
		Left,
		Centre,
		Right,
	};

	FGlpPainter(UCanvas* InCanvas, float InScale);

	/** A colour as Godot writes it (sRGB components), as the linear colour the canvas expects. */
	static FLinearColor Srgb(float R, float G, float B, float A = 1.f);

	void Rect(float X, float Y, float W, float H, const FLinearColor& Colour) const;
	void Frame(float X, float Y, float W, float H, const FLinearColor& Colour, float Thickness) const;
	void Line(const FVector2D& A, const FVector2D& B, const FLinearColor& Colour, float Thickness) const;
	void Circle(const FVector2D& Centre, float Radius, const FLinearColor& Colour) const;
	/** A band of the given width along the circle of Radius, from angle From to To (radians, clockwise from +x as on screen). */
	void Arc(const FVector2D& Centre, float Radius, float From, float To, int32 Segments, const FLinearColor& Colour, float Width) const;
	void Triangle(const FVector2D& A, const FVector2D& B, const FVector2D& C, const FLinearColor& Colour) const;
	/** A texture stretched over the rectangle. */
	void Texture(class UTexture* Texture, float X, float Y, float W, float H) const;

	/**
	 * Text with its baseline at Y, aligned inside [X, X + Width] (Godot's DrawString). Width <= 0 places it from X
	 * whatever the alignment. Size is the font size in pixels.
	 */
	void Text(const FString& Text, float X, float Y, EAlign Align, float Width, float Size, const FLinearColor& Colour) const;

	/** The virtual canvas: the real one divided by Scale. */
	FVector2D Size() const;

private:
	FVector2D Map(const FVector2D& P) const { return P * Scale; }

	UCanvas* Canvas;
	float Scale;
	const UFont* Font;
};

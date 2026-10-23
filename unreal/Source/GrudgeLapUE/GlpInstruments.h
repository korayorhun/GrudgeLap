#pragma once

#include "CoreMinimal.h"
#include "UObject/Object.h"
#include "grudgelap_physics.h"
#include "GlpInstruments.generated.h"

class UCanvas;
class UCanvasRenderTarget2D;
class UTexture;

/**
 * The car's instrument panel (InstrumentCluster.cs), drawn into a texture every frame: a speedometer with an
 * odometer, a tachometer with its red zone, a gear readout between them and the warning lights that glow while
 * the engine is off. The texture is the face of the binnacle in the cockpit and the cluster at the foot of the
 * screen in the other views (AGlpHud), so both always agree.
 */
UCLASS()
class UGlpInstruments : public UObject
{
	GENERATED_BODY()

public:
	/** Creates the render target; WorldContext is any object of the world it is drawn for. */
	void Create(UObject* WorldContext);

	UTexture* Texture() const;

	/** A new credit: the odometer starts again from zero. */
	void ResetOdometer() { Odometer = 0.0; }

	/** Takes the frame's readings and redraws the panel. */
	void Update(const glp_state& InState, const glp_car_spec& InSpec, bool bInKmh, float DeltaSeconds);

private:
	UFUNCTION()
	void Draw(UCanvas* Canvas, int32 Width, int32 Height);

	UPROPERTY()
	TObjectPtr<UCanvasRenderTarget2D> Target;

	glp_state State{};
	glp_car_spec Spec{};
	bool bKmh = false;
	bool bHasState = false;
	double Odometer = 0.0;   // metres since the credit started
};

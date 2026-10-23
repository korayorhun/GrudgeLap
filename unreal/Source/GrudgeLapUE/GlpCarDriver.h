#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "grudgelap_physics.h"
#include "GlpCarDriver.generated.h"

class UCameraComponent;

/**
 * Drives the imported player car with the GrudgeLap simulation (GlpLibrary): steps a race session every frame
 * with the keyboard or the attract-mode driver, and moves the car, its wheels and the camera to the result.
 *
 * The car is found by actor tags that Scripts/build_scene.py sets: GLP.Car on the car's root (the glTF's
 * PlayerCar), GLP.Wheel0..3 on the wheel pivots and GLP.Spin0..3 on the parts that roll, in the simulation's
 * wheel order (front left, front right, rear left, rear right).
 *
 * Command line, for automated runs: -GlpAttract (the scripted driver drives), -GlpView=chase, -GlpShots=3,6,10
 * (screenshots that many seconds into the drive, in Saved/Screenshots/Glp), -GlpQuitAfter=12.
 */
UCLASS()
class GRUDGELAPUE_API AGlpCarDriver : public AActor
{
	GENERATED_BODY()

public:
	AGlpCarDriver();
	virtual void Tick(float DeltaSeconds) override;

	/** The game's car catalogue: 0 flat-12, 1 V8. */
	UPROPERTY(EditAnywhere, Category = "GrudgeLap")
	int32 Car = 0;

	/** The scripted driver drives (as the game's attract mode); -GlpAttract sets it too. */
	UPROPERTY(EditAnywhere, Category = "GrudgeLap")
	bool bAttract = false;

	/** Behind the car rather than in the driver's seat; -GlpView=chase sets it too. */
	UPROPERTY(EditAnywhere, Category = "GrudgeLap")
	bool bChaseView = false;

protected:
	virtual void BeginPlay() override;
	virtual void EndPlay(const EEndPlayReason::Type EndPlayReason) override;

private:
	glp_inputs ReadKeyboard(float DeltaSeconds);
	void ApplyState(const glp_state& State, float DeltaSeconds);
	void Automate(const glp_state& State, float DeltaSeconds);

	UPROPERTY(VisibleAnywhere, Category = "GrudgeLap")
	TObjectPtr<UCameraComponent> Camera;

	UPROPERTY()
	TObjectPtr<AActor> Body;

	UPROPERTY()
	TArray<TObjectPtr<AActor>> Wheels;

	UPROPERTY()
	TArray<TObjectPtr<AActor>> Spinners;

	TArray<FTransform> WheelRest;     // the pivots' relative transforms as imported
	TArray<FTransform> SpinnerRest;

	glp_session Session = 0;
	float Steer = 0.f;
	float DriveTime = 0.f;            // seconds since the session reached the driving phase
	float LogTimer = 0.f;
	TArray<float> ShotTimes;
	int32 NextShot = 0;
	float QuitAfter = 0.f;
	FVector ChaseEye = FVector::ZeroVector;
	bool bChaseEyeSet = false;
	bool bViewSet = false;
};

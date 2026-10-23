#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "GlpCamera.h"
#include "GlpCarView.h"
#include "GlpInput.h"
#include "grudgelap_physics.h"
#include "GlpGame.generated.h"

class FGlpLibrary;
class UCameraComponent;
class UGlpAudio;
class UGlpInstruments;
class UMaterialInterface;
class USceneCaptureComponent2D;

/**
 * The game, as Bootstrap.cs runs it in Godot: a race session of the simulation core stepped every frame with
 * the keyboard (or the attract-mode driver), the title screen's choices, the cars, the ghost and the train moved
 * to the result, the camera, the sound and the HUD fed from it, the best lap and the player's choices kept.
 *
 * Scripts/build_scene.py places one in the level and tags what it moves (see FGlpCarView). Command line for
 * automated runs: -GlpAttract (the scripted driver drives), -GlpView=chase, -GlpNoTraffic, -GlpKeys=... (see
 * FGlpInput), -GlpShots=3+8+15 (screenshots that many seconds into the drive, in Saved/Screenshots/Glp),
 * -GlpQuitAfter=12, and -GlpTitleShots (both clocks run from the start instead, to photograph the title screen).
 */
UCLASS()
class GRUDGELAPUE_API AGlpGame : public AActor
{
	GENERATED_BODY()

public:
	AGlpGame();
	virtual void Tick(float DeltaSeconds) override;

	/** M_CarPaint: the painted parts of the traffic copies get their own colour from it. */
	UPROPERTY(EditAnywhere, Category = "GrudgeLap")
	TSoftObjectPtr<UMaterialInterface> PaintMaterial;

	/** M_Ghost: the see-through material of the best lap's ghost. */
	UPROPERTY(EditAnywhere, Category = "GrudgeLap")
	TSoftObjectPtr<UMaterialInterface> GhostMaterial;

	/** M_Instruments: the binnacle face, drawn from the instrument panel's render target. */
	UPROPERTY(EditAnywhere, Category = "GrudgeLap")
	TSoftObjectPtr<UMaterialInterface> InstrumentsMaterial;

	/** M_MirrorView: the rear-view mirror's glass, showing what the camera behind sees. */
	UPROPERTY(EditAnywhere, Category = "GrudgeLap")
	TSoftObjectPtr<UMaterialInterface> MirrorMaterial;

	// What the HUD shows.
	bool HasSession() const { return Session != 0; }
	const glp_state& State() const { return Current; }
	const glp_car_spec& Spec() const { return CarSpec; }
	const FString& Message() const { return MessageText; }
	const FString& CarName() const { return CarNameText; }
	int32 Gearbox() const { return Transmission; }
	bool InCockpit() const;
	bool UsesKmh() const { return bKmh; }
	float DrivingSeconds() const { return DrivingTime; }
	float DashOffset() const { return Dash; }
	bool Attract() const { return bAttract; }
	UTexture* InstrumentPanel() const;

protected:
	virtual void BeginPlay() override;
	virtual void EndPlay(const EEndPlayReason::Type EndPlayReason) override;

private:
	void NewSession();
	void CloseSession(bool bDestroyCopies);
	void BuildGhost();
	bool HandleMenu();
	void HandleGlobalKeys();
	void SyncViews(float DeltaSeconds);
	void SyncTrafficAndTrain(float DeltaSeconds);
	void UpdateTipping();
	void MapProps();
	void Automate(float DeltaSeconds);
	void LoadSettings();
	void SaveSettings() const;
	void SaveBestLap();
	bool LoadBestLap();
	FString SavedFile(const TCHAR* Name) const;
	FGlpCarView* PlayerCar() const;
	void FitOutCockpit();

	UPROPERTY(VisibleAnywhere, Category = "GrudgeLap")
	TObjectPtr<UCameraComponent> Camera;

	UPROPERTY(VisibleAnywhere, Category = "GrudgeLap")
	TObjectPtr<UGlpAudio> Audio;

	UPROPERTY()
	TObjectPtr<UGlpInstruments> Instruments;

	UPROPERTY()
	TObjectPtr<USceneCaptureComponent2D> Mirror;

	const FGlpLibrary* Library = nullptr;
	glp_session Session = 0;
	glp_state Current{};
	glp_car_spec CarSpec{};
	FString MessageText;
	FString CarNameText;
	TArray<FString> CarNames;

	/** The imported car of each catalogue entry (the level may hold only some). */
	TArray<TUniquePtr<FGlpCarView>> Cars;
	TArray<TUniquePtr<FGlpCarView>> TrafficCars;
	TUniquePtr<FGlpCarView> Ghost;
	TArray<TWeakObjectPtr<AActor>> TrainCars;
	TArray<glp_body> TrainBodies;
	float TrainDistance = 1e9f;   // m from the player to the nearest car of the running train

	/** A prop the car can knock over (signs, crossbucks), by simulation prop index, with its standing pose. */
	struct FSign
	{
		TWeakObjectPtr<AActor> Actor;
		FQuat Rest = FQuat::Identity;
	};
	TMap<int32, FSign> Signs;
	struct FTip
	{
		int32 Prop = INDEX_NONE;
		float Start = 0.f;
		FVector Axis = FVector::ZeroVector;
	};
	TArray<FTip> Tipping;
	TArray<glp_body> Bodies;

	FGlpInput Input;
	FGlpCameraRig Rig;
	int32 CarIndex = 0;
	int32 Transmission = GLP_MANUAL_AUTO_CLUTCH;
	bool bKmh = false;
	bool bAttract = false;
	bool bTraffic = true;
	float Time = 0.f;
	float DrivingTime = 0.f;
	float Dash = 0.f;
	FVector GhostLast = FVector::ZeroVector;
	bool bInsideShown = false;   // the player's car is drawn for the driver's seat (FGlpCarView::SetInside)

	// Automated runs (see the class comment).
	TArray<float> ShotTimes;
	int32 NextShot = 0;
	float QuitAfter = 0.f;
	bool bClockFromStart = false;
	float LogTimer = 0.f;
	float DriveTime = 0.f;
};

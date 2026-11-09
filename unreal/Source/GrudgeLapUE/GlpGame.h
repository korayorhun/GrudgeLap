#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "GlpCamera.h"
#include "GlpCarView.h"
#include "GlpInput.h"
#include "GlpLook.h"
#include "GlpScores.h"
#include "grudgelap_physics.h"
#include "GlpGame.generated.h"

class FGlpLibrary;
class UCameraComponent;
class UGlpAudio;
class UGlpInstruments;
class UMaterialInterface;
class USceneCaptureComponent2D;
class UStaticMeshComponent;
class UMaterialInstanceDynamic;

/**
 * The game, as Bootstrap.cs runs it in Godot: a race session of the simulation core stepped every frame with
 * the keyboard (or the attract-mode driver), the title screen's choices, the cars, the ghost and the train moved
 * to the result, the camera, the sound and the HUD fed from it, the best lap and the player's choices kept.
 *
 * Scripts/build_scene.py places one in the level and tags what it moves (see FGlpCarView). Command line for
 * automated runs: -GlpAttract (the scripted driver drives), -GlpView=chase, -GlpNoTraffic, -GlpKeys=... (see
 * FGlpInput), -GlpShots=3+8+15 (screenshots that many seconds into the drive, in Saved/Screenshots/Glp),
 * -GlpQuitAfter=12, -GlpTitleShots (both clocks run from the start instead, to photograph the title screen) and
 * -GlpSaveBest (an attract-mode lap may become the saved best lap, for a ghost to test against); -GlpAuto gives
 * the attract-mode driver the automatic gearbox and -GlpSpeedRoute the speed route (past the level crossing); with
 * -GlpKeys the scripted keys nudge its steering and hold its brake. -GlpLight=day|sunrise|dusk|night|random fixes the
 * hour (0.77); -GlpNoMegaLights keeps the night's lamps on the classic lighting path; -GlpCamera=x,y,z,tx,ty,tz[,fov]
 * (simulation metres, a vertical field of view) and -GlpOrbit=yaw,pitch,distance (on the car: 0 from behind, 90 from
 * its right) hold the camera as the Godot version's --camera= and --orbit= do, so the two can be shot alike.
 */
UCLASS()
class GRUDGELAPUE_API AGlpGame : public AActor
{
	GENERATED_BODY()

public:
	AGlpGame();
	virtual void Tick(float DeltaSeconds) override;

	/** M_Ghost: the see-through material of the best lap's ghost. */
	UPROPERTY(EditAnywhere, Category = "GrudgeLap")
	TSoftObjectPtr<UMaterialInterface> GhostMaterial;

	/** M_Instruments: the binnacle face, drawn from the instrument panel's render target. */
	UPROPERTY(EditAnywhere, Category = "GrudgeLap")
	TSoftObjectPtr<UMaterialInterface> InstrumentsMaterial;

	/** M_MirrorView: the rear-view mirror's glass, showing what the camera behind sees. */
	UPROPERTY(EditAnywhere, Category = "GrudgeLap")
	TSoftObjectPtr<UMaterialInterface> MirrorMaterial;

	/** M_SkidMark: the tyre marks' strips, darkening the road beneath them (0.76). */
	UPROPERTY(EditAnywhere, Category = "GrudgeLap")
	TSoftObjectPtr<UMaterialInterface> SkidMaterial;

	/** M_Puff: the wheels' dust, tyre smoke and spray, soft lit puffs turned to the camera (0.76). */
	UPROPERTY(EditAnywhere, Category = "GrudgeLap")
	TSoftObjectPtr<UMaterialInterface> PuffMaterial;

	/**
	 * M_Rain: the rain's streaks, lit, faint, gone near the eye. A soft reference so that the packaged game cooks it:
	 * loaded by its path alone it was left out of the 0.75 package, which showed no rain.
	 */
	UPROPERTY(EditAnywhere, Category = "GrudgeLap")
	TSoftObjectPtr<UMaterialInterface> RainMaterial;

	/** M_LampGlow (0.77): a lit lamp's glow, a small card turned to the camera in the lamp's real brightness. */
	UPROPERTY(EditAnywhere, Category = "GrudgeLap")
	TSoftObjectPtr<UMaterialInterface> LampGlowMaterial;

	/** IES_LowBeam (0.77): the low beam's light profile, a flat cut-off on the oncoming side stepping up over the verge. */
	UPROPERTY(EditAnywhere, Category = "GrudgeLap")
	TSoftObjectPtr<class UTextureLightProfile> LowBeamProfile;

	/** M_NightStars (0.77): the stars, a post-process material added to the look on a clear night. */
	UPROPERTY(EditAnywhere, Category = "GrudgeLap")
	TSoftObjectPtr<UMaterialInterface> NightSkyMaterial;

	// What the HUD shows.
	bool HasSession() const { return Session != 0; }
	const glp_state& State() const { return Current; }
	const glp_car_spec& Spec() const { return CarSpec; }
	const FString& Message() const { return MessageText; }
	const FString& CarName() const { return CarNameText; }
	int32 Gearbox() const { return Transmission; }
	/** The player's weather choice for the title screen (H steps it). */
	const TCHAR* WeatherName() const
	{
		static const TCHAR* Names[] = { TEXT("CLEAR"), TEXT("OVERCAST"), TEXT("WET ROAD"), TEXT("RAIN"), TEXT("RANDOM") };
		return Names[FMath::Clamp(WeatherChoice, 0, 4)];
	}
	/** The player's hour for the title screen (L steps it, 0.77): RANDOM stays RANDOM; -GlpLight shows its own. */
	const TCHAR* LightName() const { return GlpLook::ChoiceName(FixedLight >= 0 ? FixedLight : LightChoice); }
	/** The credit's hour. */
	EGlpHour CurrentHour() const { return Hour; }
	/** The lamps are lit, dusk to dawn (WorldBuilder.LampsLit): the cars' beams, the train's lamp, the yards'. */
	bool LampsLit() const { return Hour == EGlpHour::Dusk || Hour == EGlpHour::Night; }
	bool InCockpit() const;
	bool UsesKmh() const { return bKmh; }
	float DrivingSeconds() const { return DrivingTime; }
	float DashOffset() const { return Dash; }
	bool Attract() const { return bAttract; }
	const FGlpScores& HighScores() const { return Scores; }
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
	void LoadScores();
	void SaveScores() const;
	FString SavedFile(const TCHAR* Name) const;
	FGlpCarView* PlayerCar() const;
	void FitOutCockpit();
	/** DLSS Ray Reconstruction where the card and the plugin support it (0.74). */
	void EnableRayReconstruction();
	/** The chosen car's best-lap file in Saved/GrudgeLap (0.74: one per car). */
	FString BestLapFile() const;
	void ShowSideMirrors(bool bInside);
	void ApplyFrameCap();
	/** The credit's weather (glp_state.weather) on the level: the sun, the fog, the clouds, the exposure, MPC_Weather and the rain. */
	void ApplyWeather();

	UPROPERTY(VisibleAnywhere, Category = "GrudgeLap")
	TObjectPtr<UCameraComponent> Camera;

	UPROPERTY(VisibleAnywhere, Category = "GrudgeLap")
	TObjectPtr<UGlpAudio> Audio;

	UPROPERTY()
	TObjectPtr<UGlpInstruments> Instruments;

	UPROPERTY()
	TObjectPtr<USceneCaptureComponent2D> Mirror;

	/** The tyre marks (0.76): strips from each sliding wheel's last contact to its present one, in a ring of instances. */
	UPROPERTY()
	TObjectPtr<class UInstancedStaticMeshComponent> Skids;
	TOptional<FVector> SkidLast[4];
	int32 SkidNext = 0;
	void LaySkidMarks();
	void ClearSkidMarks();

	/**
	 * The wheels' dust, tyre smoke and wet-road spray (0.76): a fixed ring of puffs, each a unit plane the game moves,
	 * grows, fades and turns to the camera every frame (a CPU particle system: a Niagara system cannot be authored from
	 * the level's build script). A slot whose puff has died is a zero-scale instance.
	 */
	UPROPERTY()
	TObjectPtr<class UInstancedStaticMeshComponent> Puffs;
	struct FPuff
	{
		FVector Position = FVector::ZeroVector;    // cm
		FVector Velocity = FVector::ZeroVector;    // cm/s
		float Age = 0.f;                            // s
		float Life = 0.f;                           // s; 0 for a free slot
		float StartSize = 0.f;                      // m across
		float EndSize = 0.f;
		float Damping = 0.f;                        // cm/s2 off its speed
		float Opacity = 0.f;
		FLinearColor Colour = FLinearColor::White;  // linear
	};
	TArray<FPuff> PuffPool;
	TArray<FTransform> PuffTransforms;
	TArray<float> PuffData;
	int32 PuffNext = 0;
	float PuffDue[4][3] = {};                       // puffs owed per wheel: dust, smoke, spray
	TOptional<FVector> PuffLast[4];
	TArray<float> TrafficSprayDue;                  // per drone, its left and right rear wheel
	TArray<TOptional<FVector>> TrafficSprayLast;
	bool bPuffsShown = false;
	FRandomStream PuffRandom;
	void ThrowPuffs(float DeltaSeconds, const FVector& Eye);
	void ClearPuffs();

	/** The outside mirrors of the player's car: their own polished glass from outside, the mirror's view from the seat. */
	TArray<TWeakObjectPtr<UStaticMeshComponent>> SideMirrors;
	UPROPERTY()
	TArray<TObjectPtr<UMaterialInterface>> SideMirrorGlass;
	UPROPERTY()
	TObjectPtr<UMaterialInstanceDynamic> SideMirrorView;

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
	int32 DroneSerial = 0;
	TArray<float> DroneSpeeds;    // each drone's speed a frame ago, for its brake lamps (0.76)
	TArray<float> DroneBraking;   // seconds its brake lamps stay lit   // the next drone's car and colour (0.74, as Bootstrap._droneSerial)
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
	FGlpScores Scores;
	bool bScoreOffered = false;   // the credit's end has been put to the high-score table
	// A first start (no settings yet) shows the hot hatch (0.74): its own cabin is the best of the line-up's, where the
	// first catalogue car's borrowed cream cabin glared in the windscreen.
	int32 CarIndex = 2;
	int32 Transmission = GLP_MANUAL_AUTO_CLUTCH;
	bool bKmh = true;   // metric by default since 0.14 (the Godot version too); U toggles, the choice is kept
	bool bAttract = false;
	bool bTraffic = true;
	bool bSpeedRoute = false;
	float Time = 0.f;
	float DrivingTime = 0.f;
	float Dash = 0.f;
	FVector GhostLast = FVector::ZeroVector;
	bool bInsideShown = false;   // the player's car is drawn for the driver's seat (FGlpCarView::SetInside)
	uint32 MirrorFrame = 0;

	// The weather (ApplyWeather): the level's own values, kept for a clear day, and the rain's streaks (FallRain).
	UPROPERTY()
	TObjectPtr<class UInstancedStaticMeshComponent> Rain;
	struct FDrop
	{
		FVector Position = FVector::ZeroVector;   // cm
		FVector Velocity = FVector::ZeroVector;   // cm/s
		float Age = 0.f;                           // s
	};
	TArray<FDrop> Drops;
	TArray<FTransform> DropTransforms;
	FRandomStream DropRandom;
	FVector RainEye = FVector::ZeroVector;
	FVector RainVelocity = FVector::ZeroVector;
	bool bRainEyeKnown = false;
	bool bDropsSeeded = false;
	/** Moves the streaks and hangs fresh ones in the box above and ahead of the eye (View the camera's rotation). */
	void FallRain(float DeltaSeconds, const FVector& Eye, const FQuat& View);
	UPROPERTY()
	TObjectPtr<UMaterialInterface> CloudMaterial;   // the level's cloud material, put back on a clear day
	/**
	 * One dynamic instance of the cloud material for every other sky (0.77): the overcast deck, or a clear sky's fewer
	 * clouds at sunrise, dusk and night. Until 0.77 every overcast credit made a new one.
	 */
	UPROPERTY()
	TObjectPtr<UMaterialInstanceDynamic> CloudInstance;
	float SunLux = -1.f;
	float FogDensity = -1.f;
	FLinearColor FogColour = FLinearColor::White;
	float ExposureEv = -1.f;
	int32 WeatherShown = -1;
	int32 FixedWeather = -1;   // -GlpWeather=0..4 fixes the credit's weather for automated runs; otherwise WeatherChoice
	int32 WeatherChoice = 0;   // the player's weather (settings): 0 clear, 1 overcast, 2 wet, 3 rain, 4 random for every credit

	// The hour (0.77, ApplyWeather): the player's choice, an automated run's, the credit's, and the level's own day kept.
	int32 LightChoice = 0;   // the settings' light=: 0 day, 1 sunrise, 2 random, 3 night, 4 dusk (the Godot version's numbers)
	int32 FixedLight = -1;   // -GlpLight= for automated runs
	EGlpHour Hour = EGlpHour::Day;
	TOptional<FRotator> DayRotation;   // the level's sun, Godot's SunDirection, which the day keeps bit for bit
	TOptional<FLinearColor> SkyFactor; // the level's sky and aerial perspective luminance
	float DayBloom = -1.f;
	float DayFlare = -1.f;
	float DayGrain = -1.f;
	float CloudCoverage = -1.f;     // the level's cloud coverage (Cloud_GlobalCoverage), which the hours scale
	FVector2f CloudLayer = FVector2f(-1.f, -1.f);   // the level's cloud layer, km: bottom and height (the overcast's lower and thicker)
	bool bLevelVolumetricFog = false;
	bool bMegaLightsAvailable = false;
	FString LookShown;              // the last look logged, so a new credit in the same look logs nothing

	// -GlpCamera and -GlpOrbit (0.77): the Godot version's fixed views, for pictures shot alike in both versions.
	TOptional<FGlpShot> FixedCamera;
	TOptional<FVector> Orbit;       // yaw, pitch (degrees), distance (m)
	TOptional<FVector2f> RainAir;   // -GlpRainAir=extinction,anisotropy (0.77): the night rain's air (GlpLookFor's) for checks

	// The night's lamps (0.77): the cars' (FGlpCarView::SetLamps), the train's lamp and lit windows, the farms' sodium
	// yard lamps, the player's beams for the retroreflectors (MPC_Night) and the starry dome of a clear night.
	FGlpLampAssets LampAssets;
	UPROPERTY()
	TArray<TObjectPtr<UObject>> NightAssets;   // the lamps' material, profile and card held while the game runs
	UPROPERTY()
	TObjectPtr<class UMaterialParameterCollection> NightCollection;
	UPROPERTY()
	TObjectPtr<UMaterialInstanceDynamic> TrainWindowLight;
	TWeakObjectPtr<class USpotLightComponent> TrainLamp;
	TWeakObjectPtr<UStaticMeshComponent> TrainGlow;
	struct FYardLamp
	{
		TWeakObjectPtr<class USpotLightComponent> Light;
		TWeakObjectPtr<UStaticMeshComponent> Glow;
	};
	TArray<FYardLamp> YardLamps;
	UPROPERTY()
	TObjectPtr<UMaterialInstanceDynamic> NightStars;   // M_NightStars with -GlpStarGain's gain
	bool bStarsShown = false;
	TArray<int32> TrafficModels;   // each drone's catalogue car, for its lamps' era
	bool bRetroShown = false;      // MPC_Night holds the player's lit beams
	/** The train's headlamp and windows and the yards' lamps (BeginPlay), made once. */
	void BuildNightLamps();
	/** The session's hour on them: lit dusk to dawn, their shadows with MegaLights. */
	void ShowNightLamps();
	/** A car's lamps for the hour and the weather. */
	FGlpLampState LampState(int32 Catalogue, bool bPlayer) const;
	/** MPC_Night: the player's beams where they are this frame. */
	void SyncRetroreflection();

	// Automated runs (see the class comment).
	TArray<float> ShotTimes;
	int32 NextShot = 0;
	float QuitAfter = 0.f;
	bool bClockFromStart = false;
	bool bSaveBest = false;
	bool bHold = false;   // -GlpHold: the simulation does not step (the renderer's motion alone, for checks)
	int32 AttractGearbox = GLP_MANUAL_AUTO_CLUTCH;
	bool bFrameCap = false;      // the game holds its frame rate (a window on a screen, not the off-screen runs)
	int32 FrameCap = 0;          // fps asked for at the last look
	float FrameCapCheck = 0.f;   // game time of the next look at the screen and the focus
	float FrameCapBefore = 0.f;  // t.MaxFPS before the game took it over, handed back at the end
	float LogTimer = 0.f;
	float DriveTime = 0.f;
};

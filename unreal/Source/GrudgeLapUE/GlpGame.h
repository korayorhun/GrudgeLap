#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "GlpCamera.h"
#include "GlpCarView.h"
#include "GlpCockpits.h"
#include "GlpInput.h"
#include "GlpLook.h"
#include "GlpMenu.h"
#include "GlpPaint.h"
#include "GlpScores.h"
#include "GlpShowroom.h"
#include "GlpUpdate.h"
#include "GlpWindscreen.h"
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
 * automated runs: -GlpAttract (the scripted driver drives), -GlpView=cockpit|chase|film, -GlpNoTraffic, -GlpKeys=... (see
 * FGlpInput), -GlpShots=3+8+15 (screenshots that many seconds into the drive, in Saved/Screenshots/Glp),
 * -GlpQuitAfter=12, -GlpTitleShots (both clocks run from the start instead, to photograph the title screen) and
 * -GlpSaveBest (an attract-mode lap may become the saved best lap, for a ghost to test against); -GlpAuto gives
 * the attract-mode driver the automatic gearbox and -GlpSpeedRoute the speed route (past the level crossing); with
 * -GlpKeys the scripted keys nudge its steering and hold its brake. -GlpLight=day|sunrise|dusk|night|random fixes the
 * hour (0.77); -GlpNoMegaLights keeps the night's lamps on the classic lighting path; -GlpCamera=x,y,z,tx,ty,tz[,fov]
 * (simulation metres, a vertical field of view) and -GlpOrbit=yaw,pitch,distance (on the car: 0 from behind, 90 from
 * its right) hold the camera as the Godot version's --camera= and --orbit= do, so the two can be shot alike.
 *
 * The driver's seat (0.78): -GlpFov=<degrees upright> and -GlpDrop=<m> for every car, -GlpCockpit=<FGlpCockpit::Read's
 * text> for the car driven and -GlpCockpits=<file> for each car try a seat without building the module;
 * -GlpTour[=seconds] photographs each car to drive in turn after that many seconds of its credit (Saved/Screenshots/Glp/
 * tour_<index>_<view>.png) and quits, -GlpTourCars=1+8 only those; -GlpLocal=<shadow>,<highlight>[,<detail>] and
 * -GlpCabinEv=<stops> set the seat's local exposure and its extra exposure, -GlpGlass=<transmittance>,<specular>,<opacity>
 * the glass the windows wear from the seat.
 *
 * The menu, the upscaler and the updater (0.78): -GlpUpscaling=off|native|quality|balanced|performance|ultra sets the
 * upscaler's mode for a run; -GlpData=<folder> names the folder the settings, scores and best laps are kept in (a
 * check's own, so that it leaves the player's alone), -GlpImport and -GlpGodotData=<folder> try the take-over of the
 * earlier versions' files (GlpInstance::Import), -GlpUpdateRoot=<folder> lets the editor's build run the updater
 * (FGlpUpdate). The scripted keys reach the menu: -GlpKeys=2:Escape,3:Down,3.5:Enter.
 *
 * The showroom and the paints (0.79; on the title screen Down opens it, in the menu the CAR line): -GlpShowroom[=<catalogue
 * index>] opens the showroom at the start,
 * -GlpTurn=<degrees> holds its camera at that bearing round the car (0 ahead of the nose, 90 on the driver's side),
 * -GlpShowroomPaint=<paint> chooses a paint there (a preset's name or number, or "#RRGGBB:finish", which opens the
 * colour picker on it), and -GlpPaint=<paint> puts the driven car in a paint for this run without keeping it. The
 * scripted keys and the scripted mouse (-GlpMouse, with :down and :up for a drag) reach the showroom too.
 * -GlpSelfTest runs the paints', the showroom's and the windscreen's own checks (GlpPaint::SelfTest,
 * FGlpShowroom::SelfTest, GlpWindscreen::SelfTest), logs "self test: N checks, M failed" and quits with 0 when all held.
 *
 * The windscreen from the seat (0.80, SyncWindscreen): -GlpWindscreen=0 leaves the glass clear, for comparisons;
 * -GlpWindscreen=<film>,<lens>,<check> tries the water film's strength (0.55) and the drops' lens (2.2), and with a
 * check draws that instead of the picture (1 the glass found, 2 its side windows, 3 a grid of 10 cm on the glass with
 * the wipers' pivots and reach, 4 the glass alone, white on black, for measuring how still its edge stands).
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

	/**
	 * M_GlassInside (0.78): what the player's windows wear from the driver's seat. The level's glass is a car's glass
	 * seen from the road: tinted, with the full reflection of a pane. From the seat, under an exposure held for the
	 * open road, it took half the road's light on its way through a raked windscreen and laid the dashboard's
	 * reflection over the rest (the owner's report of 14 November 2026); this one lets nearly all of it through and
	 * reflects about a sixth as much, as the eye behind a windscreen, adapted and focused far ahead, sees it.
	 */
	UPROPERTY(EditAnywhere, Category = "GrudgeLap")
	TSoftObjectPtr<UMaterialInterface> InsideGlassMaterial;

	/**
	 * M_Windscreen (0.80): what the weather puts on the glass before the driver, a post-process material on the
	 * player's own camera in the seat's view: the rain's drops, the wipers and the film of water between their sweeps,
	 * and on a dry day the dust's haze with the sun ahead (SyncWindscreen).
	 */
	UPROPERTY(EditAnywhere, Category = "GrudgeLap")
	TSoftObjectPtr<UMaterialInterface> WindscreenMaterial;

	/**
	 * M_Pane (0.80): what the unseen copies of the player's panes wear to write the glass into the custom depth
	 * (FGlpCarView::MarkGlass), from which M_Windscreen knows where the glass is. Opaque and two-sided: a pane is seen
	 * from its inner side.
	 */
	UPROPERTY(EditAnywhere, Category = "GrudgeLap")
	TSoftObjectPtr<UMaterialInterface> GlassPaneMaterial;

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
	/** The view the camera key chose (the replay's crane aside). */
	EGlpView View() const { return Rig.View; }
	/** The film view is on screen: the HUD frames it with a film's black bars. */
	bool InFilmView() const;
	const TCHAR* ViewName() const
	{
		return Rig.View == EGlpView::Cockpit ? TEXT("DRIVER'S SEAT") : Rig.View == EGlpView::Chase ? TEXT("BEHIND THE CAR") : TEXT("FILM");
	}
	/** The view's name in file names and the settings: cockpit, chase, film. */
	const TCHAR* ViewTag() const
	{
		return Rig.View == EGlpView::Cockpit ? TEXT("cockpit") : Rig.View == EGlpView::Chase ? TEXT("chase") : TEXT("film");
	}
	bool UsesKmh() const { return bKmh; }
	float DrivingSeconds() const { return DrivingTime; }
	float DashOffset() const { return Dash; }
	bool Attract() const { return bAttract; }
	const FGlpScores& HighScores() const { return Scores; }
	UTexture* InstrumentPanel() const;
	/** The Esc menu, for the HUD to draw. */
	const FGlpMenu& Menu() const { return PauseMenu; }
	/** The showroom (0.79), for the HUD to draw: its state, and what it prints about the car on show. */
	const FGlpShowroom& Showroom() const { return Room; }
	const FString& ShownName() const { return CarNames.IsValidIndex(Room.Index) ? CarNames[Room.Index] : CarNameText; }
	/** The two lines of the car's facts (glp_car_facts): its layout and its figures. */
	const FString& ShownFacts(int32 Line) const;
	/** The car's place in the line-up, "CAR 3 OF 10"; a car that is not to drive (automation only) has none. */
	FString ShownPlace() const;
	/** The paint the car to drive wears, for the title screen's line. */
	FString PaintName() const { return Paints.Of(CarIndex).Name(); }
	/** The title screen's line about the car to drive (glp_car_facts' third): power, mass, driven wheels, gears. */
	const FString& TitleFacts() const;
	/** A car's place among the cars to drive (from 1) and how many those are; false for a car out of the line-up. */
	bool Place(int32 Car, int32& OutNumber, int32& OutCount) const;
	int32 DrivenCar() const { return CarIndex; }
	/** This copy's version, "0.78.0": the project's (Config/DefaultGame.ini), which tools/release.ps1 stamps. */
	const FString& Version() const { return VersionText; }
	/** The updater's line for the title screen and the HUD's corner; empty when it has nothing to say. */
	FString UpdateNotice() const;
	/** Where the updater stands (GLP_UPDATE_IDLE .. GLP_UPDATE_FAILED), -1 when this copy does not update itself. */
	int32 UpdatePhase() const { return Updater.Running() ? Updater.Phase() : -1; }

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
	/** What the card and NVIDIA's plugin can do: DLSS, and Ray Reconstruction with it (BeginPlay). */
	void ProbeUpscaler();
	/** The upscaler's mode on the renderer: DLSS and its Ray Reconstruction on an RTX card, TSR's share of the window anywhere else. */
	void ApplyUpscaling();
	/** The next mode this card offers, or the one before. */
	void StepUpscaling(int32 Direction);
	/** The menu's line: "DLSS: QUALITY", or "UPSCALING: TSR QUALITY" on a card without DLSS. */
	FString UpscalingLine() const;
	/** The chosen car's best-lap file in Saved/GrudgeLap (0.74: one per car). */
	FString BestLapFile() const;
	void ShowSideMirrors(bool bInside);
	void ApplyFrameCap();
	/** The credit's weather (glp_state.weather) on the level: the sun, the fog, the clouds, the exposure, MPC_Weather and the rain. */
	void ApplyWeather();
	/** What the view asks of the picture (0.78): the seat's local exposure and its share more exposure. */
	void ApplyViewLook();
	/** The camera key: the driver's seat, behind the car, the film view, and round again (or the other way). */
	void StepView(int32 Direction = 1);
	/** The chosen car's seat as corrected: GlpCockpits' own, -GlpCockpits' file over it, -GlpCockpit over both. */
	FGlpCockpit Cockpit() const;

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
	TMap<int32, FGlpCockpit> CockpitFile;   // -GlpCockpits=
	FString CockpitText;                    // -GlpCockpit=
	float FixedFov = 0.f;                   // -GlpFov=
	float FixedDrop = -1.f;                 // -GlpDrop=
	/** The windows' glass from the seat: an instance of InsideGlassMaterial, so -GlpGlass can set it. */
	UPROPERTY()
	TObjectPtr<UMaterialInstanceDynamic> InsideGlass;
	/**
	 * The seat's picture (ApplyViewLook). A cabin in the shade stands four or five stops under the sunlit road; the eye
	 * sees both at once, a fixed exposure does not. Local exposure (the engine's local tone mapping) lifts the shade
	 * towards the eye's picture: its contrast below middle grey scaled by CabinShadow, above it by CabinHighlight.
	 */
	float CabinShadow = 0.62f;
	float CabinHighlight = 0.9f;
	float CabinDetail = 1.f;
	/** Stops more exposure from the seat by day: the cabin frames the picture and the eye opens to it a little. */
	float CabinEv = 0.25f;
	float LookEv = 14.f;         // the hour's and the weather's exposure, EV100 (ApplyWeather)
	bool bViewLookInside = false;

	/**
	 * The windscreen from the seat (0.80): an instance of WindscreenMaterial on the camera's own blendables while the
	 * glass has something to show, so that neither the mirror's camera nor a view from outside gets it. The picture is
	 * laid on the glass itself, not on the screen: the panes' custom depth (FGlpCarView::MarkGlass) says where the
	 * glass is, and the car's axes and the windscreen's lower edge, given in the camera's frame every frame, turn that
	 * into a place on the pane. The drops and the blades so stay with the cabin when the head moves in it.
	 */
	UPROPERTY()
	TObjectPtr<UMaterialInstanceDynamic> Windscreen;
	UPROPERTY()
	TObjectPtr<UMaterialInterface> GlassPane;   // GlassPaneMaterial, loaded
	bool bWindscreenShown = false;
	FGlpWipers Wipers;
	/** The windscreen's lower edge on the car's middle line, in the car's frame, cm (GLP.Cowl; without one, from the eye). */
	FVector CowlLocal = FVector::ZeroVector;
	/** Towards the sun or the moon, world (ApplyWeather). */
	FVector LightTowards = FVector::UpVector;
	/** -GlpWindscreen=<film>,<lens>,<check>; -GlpWindscreen=0 switches the glass's picture off. */
	FVector3f GlassTune = FVector3f(0.55f, 2.2f, 0.f);
	bool bGlassPicture = true;
	/** The glass's picture for this frame (after the camera is placed). */
	void SyncWindscreen(float DeltaSeconds);
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
	/** MPC_Night: the beams of the car on the road (the player's, or the showroom's) where they are this frame. */
	void SyncRetroreflection(const FGlpCarView* Car);

	// The Esc menu (0.78, FGlpMenu). While it is open the session is not stepped, nothing moves and the sound is held.
	FGlpMenu PauseMenu;
	FVector2f MenuMouse = FVector2f(-1.f, -1.f);   // where the mouse was a frame ago on the HUD's canvas: only a move chooses
	bool bRefreshViews = false;                     // a choice changed the picture behind the menu
	int32 SessionWeather = 0;                       // the choices the running credit was made with: another one waits
	int32 SessionLight = 0;                         // for the next game
	void OpenMenu(EGlpMenuLine At = EGlpMenuLine::Continue);
	void CloseMenu();
	/** A frame with the menu open: the keys, the mouse, the choice taken. */
	void RunMenu(APlayerController* Player);
	/** The lines as they read now. */
	void FillMenu();
	/** A line taken (Direction 0: Enter, A or a click) or changed (-1 left, 1 right). */
	void TakeMenuLine(EGlpMenuLine Line, int32 Direction);
	/** The next car to drive, or the one before; the session is rebuilt around it. */
	void StepCar(int32 Direction);
	void ToggleFullScreen();
	bool OnTitle() const { return Current.phase == GLP_PHASE_SELECT_TRANSMISSION; }

	// The showroom and the paints (0.79, FGlpShowroom): the Godot version's Showroom and PaintBook. While it is open the
	// session is not stepped, as under the menu; the car on show stands where the credit starts and the camera goes
	// round it. The cars it steps through are the level's own, shown without a session: only taking one starts a game.
	FGlpShowroom Room;
	FGlpPaintBook Paints;
	TArray<FGlpPaintChoice> Worn;           // the paint each car's view wears now (SyncPaints)
	TOptional<FGlpPaintChoice> RunPaint;    // -GlpPaint=: the driven car's paint for this run, not kept
	TArray<glp_car_spec> Specs;             // every catalogue car's, for the cars shown without a session
	TArray<FString> Facts[3];               // every car's lines (glp_car_facts): the showroom's two and the title screen's
	float StartPosition[3] = {};            // where a credit starts: the simulation's pose of the car's centre of gravity
	float StartOrientation[4] = { 0.f, 0.f, 0.f, 1.f };
	float StartGround = 0.f;                // the road's height under it (simulation metres)
	bool bStartKnown = false;
	TOptional<float> HeldTurn;              // -GlpTurn=
	FString ShowroomPaintText;              // -GlpShowroomPaint=
	bool bShowroomAtStart = false;          // -GlpShowroom
	int32 ShowroomStartCar = INDEX_NONE;
	int32 RoomCar = INDEX_NONE;             // the car the showroom has stood on the road (INDEX_NONE: the cars are the session's)
	/**
	 * The showroom's two floodlights, lit at night only. By day, at dusk and at sunrise the car on show stands in the
	 * credit's own light; a night's exposure leaves a car on an unlit road nearly black, and its own dipped beams dazzle
	 * the camera as it passes the nose (in the night's rain they fill the fog), so a paint could not be judged. At night
	 * the car's lamps are out and these two light it, as a car parked on show is lit.
	 */
	TWeakObjectPtr<class USpotLightComponent> RoomLights[2];
	bool ShowroomFloodlit() const { return Room.bOpen && Hour == EGlpHour::Night; }
	/**
	 * The floodlights on their stands round the car on show (Foot: the ground under its middle, Unreal units) and aimed
	 * at its middle, or out.
	 */
	void LightShowroom(bool bOn, const FVector& Foot = FVector::ZeroVector, const FVector& Nose = FVector::ForwardVector,
		const FVector& Right = FVector::RightVector, double MiddleCm = 0.0);
	/** Opens the showroom on the car driven; Step moves on to the next car (1) or the one before (-1) at once. */
	void OpenShowroom(bool bFromMenu, int32 Step = 0);
	/** Another car on show: the showroom's index, and the car's own colour for its ORIGINAL swatch. */
	void PutOnShow(int32 Car);
	/** The menu's PAINT line (the Godot version's CyclePaint): the driven car's next paint, or the one before, kept at once. */
	void StepPaint(int32 Step);
	/** Closes it: the car on show and every paint chosen taken (another car starts a new game), or all of it forgotten. */
	void CloseShowroom(bool bTake);
	/** A frame with the showroom open: the keys, the mouse, the turn, the cars and the camera. */
	void RunShowroom(APlayerController* Player, float DeltaSeconds);
	void StepShowroom(int32 Direction);
	/** The car on show where a credit starts and every other car out of sight; the camera on its way round it. */
	void SyncShowroom(float DeltaSeconds);
	/** Every car's view in the paint it should wear: the showroom's choice while it is open, the settings' otherwise. */
	void SyncPaints();
	/** The HUD's canvas (1600 x 900 scaled to the window) and the mouse on it: the real one, or a scripted run's. */
	FVector2f HudCanvas() const;
	bool HudMouse(const APlayerController* Player, FVector2f& Point, bool& bPress, bool& bDown) const;
	/** The view from inside or outside the car as the camera needs it (the glass, the parts hidden from the seat, the mirrors, the picture). */
	void ShowInside(bool bInside);

	// The upscaler (0.78; the owner's request of 14 November 2026 for DLSS in the menu). DLSS in its Quality mode and its
	// Ray Reconstruction have been on since 0.44 and 0.74 wherever the card ran them; the mode is now the player's.
	// The settings' upscaling=: 0 off (DLSS cards only: TSR takes over), 1 native (DLAA), 2 quality, 3 balanced,
	// 4 performance, 5 ultra performance (DLSS only).
	int32 UpscaleChoice = 2;
	bool bRayReconstruction = true;   // the settings' rr=
	bool bDlss = false;               // the card and the plugin run DLSS
	bool bDlssRR = false;             // and Ray Reconstruction, with hardware ray tracing
	int32 BilateralBefore = -1;       // r.Lumen.Reflections.BilateralFilter as the game found it

	// The updater (0.78): a button in the menu, and nothing fetched until it is pressed (the owner's rule of 14 November 2026).
	FGlpUpdate Updater;
	FString VersionText;
	bool bInstallAsked = false;       // DOWNLOAD AND INSTALL was chosen: the verified package is handed over without another press
	bool bQuitForUpdate = false;      // the installer runs: the game has asked to quit
	/** Every frame: the updater moved on, the hand-over when it is due, the way out when the installer waits. */
	void RunUpdater();
	/** The menu's line for where the updater stands, and what taking it does. */
	FString UpdateLine() const;
	void TakeUpdateLine();

	// Automated runs (see the class comment).
	float TourSeconds = 0.f;     // -GlpTour: each car's credit is photographed this far in
	float TourClock = 0.f;
	bool bTourShot = false;
	TArray<int32> TourCars;      // the cars still to photograph
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

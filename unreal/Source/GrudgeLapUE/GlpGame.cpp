#include "GlpGame.h"

#include "Camera/CameraComponent.h"
#include "Components/SceneCaptureComponent2D.h"
#include "Components/StaticMeshComponent.h"
#include "Engine/TextureRenderTarget2D.h"
#include "EngineUtils.h"
#include "GameFramework/GameUserSettings.h"
#include "GameFramework/PlayerController.h"
#include "GlpAudio.h"
#include "GlpHud.h"
#include "GlpInstruments.h"
#include "GlpLibrary.h"
#include "GlpSpace.h"
#include "HAL/FileManager.h"
#include "Materials/MaterialInstanceDynamic.h"
#include "Misc/CommandLine.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "UnrealClient.h"

DEFINE_LOG_CATEGORY_STATIC(LogGlpGame, Log, All);

namespace
{
	// The drones are the period cars of the original whatever the player drives, in these paints
	// (CarView.DronePaints, sRGB).
	const FColor DronePaints[] = { FColor(31, 64, 179), FColor(224, 224, 214), FColor(230, 184, 26) };

	// The driver's eye height over the road and distance behind the centre of gravity, per body style
	// (CarView.BodyStyle.EyeH / EyeZ); the driver sits 0.38 m left of the centre line (CarView.DriverX).
	struct FEyePoint
	{
		float Height;
		float Back;
	};
	const FEyePoint EyePoints[] = { { 0.98f, 0.15f }, { 1.02f, 0.12f } };

	void HideTree(AActor* Actor, bool bHidden)
	{
		if (!Actor)
		{
			return;
		}
		Actor->SetActorHiddenInGame(bHidden);
		TArray<AActor*> Children;
		Actor->GetAttachedActors(Children, true, true);
		for (AActor* Child : Children)
		{
			Child->SetActorHiddenInGame(bHidden);
		}
	}

	AActor* Tagged(UWorld* World, const FString& Tag)
	{
		const FName Name(*Tag);
		for (TActorIterator<AActor> It(World); It; ++It)
		{
			if (It->ActorHasTag(Name))
			{
				return *It;
			}
		}
		return nullptr;
	}

	bool Event(const glp_state& State, int32 Event)
	{
		return (State.events & (1u << Event)) != 0;
	}
}

AGlpGame::AGlpGame()
{
	PrimaryActorTick.bCanEverTick = true;
	PrimaryActorTick.TickGroup = TG_PrePhysics;
	Camera = CreateDefaultSubobject<UCameraComponent>(TEXT("Camera"));
	RootComponent = Camera;
	Camera->SetConstraintAspectRatio(false);
	Audio = CreateDefaultSubobject<UGlpAudio>(TEXT("Audio"));
	Audio->SetupAttachment(Camera);
}

void AGlpGame::BeginPlay()
{
	Super::BeginPlay();
	// Movie Render Queue renders the level under its own game mode with the sequences' cameras; the game stays out.
	if (!GetWorld()->GetAuthGameMode<AGlpGameMode>())
	{
		SetActorTickEnabled(false);
		return;
	}
	Library = FGlpLibrary::Get();
	if (!Library)
	{
		return;
	}

	bAttract = FParse::Param(FCommandLine::Get(), TEXT("GlpAttract"));
	bTraffic = !FParse::Param(FCommandLine::Get(), TEXT("GlpNoTraffic"));
	bSpeedRoute = FParse::Param(FCommandLine::Get(), TEXT("GlpSpeedRoute"));
	FString View;
	if (FParse::Value(FCommandLine::Get(), TEXT("GlpView="), View))
	{
		Rig.bCockpit = !View.Equals(TEXT("chase"), ESearchCase::IgnoreCase);
	}
	FString Shots;
	if (FParse::Value(FCommandLine::Get(), TEXT("GlpShots="), Shots, false))
	{
		// "4+9+16" as well as "4,9,16": cmd splits batch arguments at commas.
		TArray<FString> Parts;
		const TCHAR* Separators[] = { TEXT(","), TEXT("+") };
		Shots.ParseIntoArray(Parts, Separators, UE_ARRAY_COUNT(Separators));
		for (const FString& Part : Parts)
		{
			ShotTimes.Add(FCString::Atof(*Part));
		}
		ShotTimes.Sort();
	}
	FParse::Value(FCommandLine::Get(), TEXT("GlpQuitAfter="), QuitAfter);
	bClockFromStart = FParse::Param(FCommandLine::Get(), TEXT("GlpTitleShots"));
	bSaveBest = FParse::Param(FCommandLine::Get(), TEXT("GlpSaveBest"));
	// The attract-mode driver changes gear itself, as in the Godot version (Bootstrap._attractMode, --auto).
	AttractGearbox = FParse::Param(FCommandLine::Get(), TEXT("GlpAuto")) ? GLP_AUTOMATIC : GLP_MANUAL_AUTO_CLUTCH;

	// The imported car of each catalogue entry: GLP.Car for the first, GLP.Car1 and so on for the others.
	const int32 CarCount = Library->CarCount();
	Cars.SetNum(CarCount);
	for (int32 i = 0; i < CarCount; i++)
	{
		Cars[i] = FGlpCarView::FromLevel(GetWorld(), FName(i == 0 ? FString(TEXT("GLP.Car")) : FString::Printf(TEXT("GLP.Car%d"), i)));
		CarNames.Add(Library->CarName(i));
	}
	if (!Cars.IsValidIndex(0) || !Cars[0])
	{
		UE_LOG(LogGlpGame, Error, TEXT("no actor tagged GLP.Car: the level was not built by Scripts/build_scene.py"));
		return;
	}
	for (int32 i = 0;; i++)
	{
		AActor* Car = Tagged(GetWorld(), FString::Printf(TEXT("GLP.Train%d"), i));
		if (!Car)
		{
			break;
		}
		TrainCars.Add(Car);
	}

	Instruments = NewObject<UGlpInstruments>(this);
	Instruments->Create(this);
	Mirror = NewObject<USceneCaptureComponent2D>(this, TEXT("Mirror"));
	Mirror->bCaptureEveryFrame = false;
	Mirror->bCaptureOnMovement = false;
	Mirror->RegisterComponent();

	LoadSettings();
	if (!Cars.IsValidIndex(CarIndex) || !Cars[CarIndex])
	{
		CarIndex = 0;
	}
	NewSession();
}

void AGlpGame::EndPlay(const EEndPlayReason::Type EndPlayReason)
{
	// The world is going away with every actor in it: only the simulation's session needs closing.
	CloseSession(false);
	Super::EndPlay(EndPlayReason);
}

void AGlpGame::CloseSession(bool bDestroyCopies)
{
	TrafficCars.Reset();
	Ghost.Reset();
	if (Session && Library)
	{
		Library->Destroy(Session);
	}
	Session = 0;
	if (bDestroyCopies)
	{
		TArray<AActor*> Copies;
		for (AActor* Actor : TActorRange<AActor>(GetWorld()))
		{
			if (Actor->ActorHasTag(TEXT("GLP.Copy")))
			{
				Copies.Add(Actor);
			}
		}
		for (AActor* Copy : Copies)
		{
			Copy->Destroy();
		}
	}
}

void AGlpGame::NewSession()
{
	CloseSession(true);
	Session = Library->Create(CarIndex, (bTraffic ? GLP_TRAFFIC : 0) | (bSpeedRoute ? GLP_SPEED_ROUTE : 0));
	if (!Session)
	{
		UE_LOG(LogGlpGame, Error, TEXT("no session: %s"), *Library->LastError());
		return;
	}
	Library->CarSpec(CarIndex, &CarSpec);
	CarNameText = CarNames[CarIndex];
	LoadBestLap();
	for (int32 i = 0; i < Cars.Num(); i++)
	{
		if (Cars[i])
		{
			Cars[i]->SetVisible(i == CarIndex);
			Cars[i]->SetInside(false);
		}
	}
	bInsideShown = false;
	const FEyePoint& Eye = EyePoints[FMath::Clamp(CarSpec.style, 0, 1)];
	Rig.Eye = FVector3f(-0.38f, Eye.Height - CarSpec.cg_height, Eye.Back);
	Rig.CgHeight = CarSpec.cg_height;
	Rig.Reset();
	Input.Reset();
	DrivingTime = 0.f;
	DriveTime = 0.f;
	Dash = 0.f;

	// Knocked signs stand up again for the new credit, as the simulation's do.
	if (Signs.IsEmpty())
	{
		MapProps();
	}
	Tipping.Reset();
	for (const TPair<int32, FSign>& Sign : Signs)
	{
		if (AActor* Actor = Sign.Value.Actor.Get())
		{
			Actor->SetActorRotation(Sign.Value.Rest);
		}
	}

	// The drones: copies of the period car's exterior in the original's paints.
	const int32 Drones = Library->Traffic(Session, nullptr, 0);
	Bodies.SetNum(FMath::Max(Drones, 1));
	Library->Traffic(Session, Bodies.GetData(), Bodies.Num());
	for (int32 i = 0; i < Drones; i++)
	{
		const FLinearColor Colour(DronePaints[Bodies[i].colour % UE_ARRAY_COUNT(DronePaints)]);
		if (TUniquePtr<FGlpCarView> Copy = Cars[0]->Clone(GetWorld(), FString::Printf(TEXT("Drone%d"), i), &Colour, nullptr))
		{
			Copy->Root()->Tags.Add(TEXT("GLP.Copy"));
			TrafficCars.Add(MoveTemp(Copy));
		}
	}
	Library->GetState(Session, &Current);
	if (Current.grudge_match)
	{
		BuildGhost();
	}
	FitOutCockpit();
	if (Audio)
	{
		Audio->SetCar(CarSpec.style);
	}
	if (Instruments)
	{
		Instruments->ResetOdometer();
	}
	if (bAttract)
	{
		Library->ChooseTransmission(Session, AttractGearbox);
	}
	Library->GetState(Session, &Current);
	MessageText = Library->Message(Session);
	UE_LOG(LogGlpGame, Log, TEXT("new session: %s, %d drones, %s, %s view"), *CarNameText, Drones,
		Current.grudge_match ? TEXT("ghost of the best lap") : TEXT("no ghost"), Rig.bCockpit ? TEXT("cockpit") : TEXT("chase"));
}

void AGlpGame::BuildGhost()
{
	// The best lap's ghost: the player's car again, see-through and without a shadow (CarView.MakeGhost).
	if (Ghost || !PlayerCar())
	{
		return;
	}
	Ghost = PlayerCar()->Clone(GetWorld(), TEXT("Ghost"), nullptr, GhostMaterial.LoadSynchronous());
	if (Ghost)
	{
		Ghost->Root()->Tags.Add(TEXT("GLP.Copy"));
		Ghost->SetVisible(false);
	}
}

FGlpCarView* AGlpGame::PlayerCar() const
{
	return Cars.IsValidIndex(CarIndex) ? Cars[CarIndex].Get() : nullptr;
}

void AGlpGame::FitOutCockpit()
{
	FGlpCarView* Car = PlayerCar();
	if (!Car || !Car->Root())
	{
		return;
	}
	// The binnacle face shows the instrument panel (CarView.MountInstruments), lit from within.
	if (AActor* Dials = Car->Find(TEXT("GLP.Dials")); Dials && Instruments)
	{
		if (UMaterialInterface* Base = InstrumentsMaterial.LoadSynchronous())
		{
			TArray<UStaticMeshComponent*> Meshes;
			Dials->GetComponents<UStaticMeshComponent>(Meshes);
			for (UStaticMeshComponent* Mesh : Meshes)
			{
				UMaterialInstanceDynamic* Face = UMaterialInstanceDynamic::Create(Base, Mesh);
				Face->SetTextureParameterValue(TEXT("Panel"), Instruments->Texture());
				Mesh->SetMaterial(0, Face);
			}
		}
	}

	// The rear-view mirror (RearMirror): a camera at the glass looking straight back along the car and a touch
	// down to the road, its picture on the glass flipped left to right by the material. It starts 2.7 m out,
	// past the car's own tail, so the cabin does not fill the glass; it renders without Lumen, which keeps a
	// second view of the world cheap at this size.
	AActor* Glass = Car->Find(TEXT("GLP.MirrorGlass"));
	UMaterialInterface* GlassBase = MirrorMaterial.LoadSynchronous();
	if (!Glass || !GlassBase || !Mirror)
	{
		return;
	}
	if (!Mirror->TextureTarget)
	{
		// The scene's light before exposure and tone mapping: the glass then shows it through the main view's own
		// camera settings, so the picture behind matches the one ahead.
		UTextureRenderTarget2D* Target = NewObject<UTextureRenderTarget2D>(this);
		Target->RenderTargetFormat = RTF_RGBA16f;
		Target->ClearColor = FLinearColor::Black;
		Target->InitAutoFormat(512, 144);
		Mirror->TextureTarget = Target;
		Mirror->CaptureSource = ESceneCaptureSource::SCS_SceneColorHDRNoAlpha;
		// 26 degrees vertical (the Godot mirror camera's Fov) on 512 x 144.
		Mirror->FOVAngle = FMath::RadiansToDegrees(2.f * FMath::Atan(FMath::Tan(FMath::DegreesToRadians(13.f)) * 512.f / 144.f));
		Mirror->bOverride_CustomNearClippingPlane = true;
		Mirror->CustomNearClippingPlane = 270.f;
		Mirror->PostProcessSettings.bOverride_DynamicGlobalIlluminationMethod = true;
		Mirror->PostProcessSettings.DynamicGlobalIlluminationMethod = EDynamicGlobalIlluminationMethod::None;
		Mirror->PostProcessSettings.bOverride_ReflectionMethod = true;
		Mirror->PostProcessSettings.ReflectionMethod = EReflectionMethod::None;
		Mirror->ShowFlags.SetMotionBlur(false);
		// A second view of the world is paid for in GPU time (the profile of a lap: 53 fps with it drawn in full
		// every frame). The small picture leaves out shadows, clouds and the screen-space effects and is drawn
		// every other frame (SyncViews), as the Godot version gives its mirror a lighter environment.
		Mirror->ShowFlags.SetDynamicShadows(false);
		Mirror->ShowFlags.SetCloud(false);
		Mirror->ShowFlags.SetDistanceFieldAO(false);
		Mirror->ShowFlags.SetVolumetricFog(false);
		Mirror->ShowFlags.SetAmbientOcclusion(false);
		Mirror->ShowFlags.SetScreenSpaceReflections(false);
	}
	const AActor* Root = Car->Root();
	const FVector Back = Root->GetActorRightVector();   // the car's local +Y: its nose is -Y
	const FVector Up = Root->GetActorUpVector();
	const FVector Look = Back * FMath::Cos(0.03f) - Up * FMath::Sin(0.03f);
	Mirror->AttachToComponent(Root->GetRootComponent(), FAttachmentTransformRules::KeepRelativeTransform);
	Mirror->SetWorldLocationAndRotation(Glass->GetActorLocation(), FRotationMatrix::MakeFromXZ(Look, Up).ToQuat());
	TArray<UStaticMeshComponent*> Meshes;
	Glass->GetComponents<UStaticMeshComponent>(Meshes);
	for (UStaticMeshComponent* Mesh : Meshes)
	{
		UMaterialInstanceDynamic* View = UMaterialInstanceDynamic::Create(GlassBase, Mesh);
		View->SetTextureParameterValue(TEXT("View"), Mirror->TextureTarget);
		Mesh->SetMaterial(0, View);
	}

	// The outside mirrors borrow the same picture of the road behind: in Lumen's reflections their polished glass
	// showed a dark, noisy smear from the seat. Outside, where the picture is not kept current, they keep their glass.
	ShowSideMirrors(false);
	SideMirrors.Reset();
	SideMirrorGlass.Reset();
	SideMirrorView = UMaterialInstanceDynamic::Create(GlassBase, this);
	SideMirrorView->SetTextureParameterValue(TEXT("View"), Mirror->TextureTarget);
	for (AActor* Side : Car->FindAll(TEXT("GLP.SideMirror")))
	{
		TArray<UStaticMeshComponent*> SideMeshes;
		Side->GetComponents<UStaticMeshComponent>(SideMeshes);
		for (UStaticMeshComponent* Mesh : SideMeshes)
		{
			SideMirrors.Add(Mesh);
			SideMirrorGlass.Add(Mesh->GetMaterial(0));
		}
	}
}

void AGlpGame::ShowSideMirrors(bool bInside)
{
	for (int32 i = 0; i < SideMirrors.Num(); i++)
	{
		if (UStaticMeshComponent* Mesh = SideMirrors[i].Get())
		{
			Mesh->SetMaterial(0, bInside && SideMirrorView ? static_cast<UMaterialInterface*>(SideMirrorView) : SideMirrorGlass[i].Get());
		}
	}
}

bool AGlpGame::InCockpit() const
{
	return Rig.bCockpit && Current.phase != GLP_PHASE_REPLAY;
}

UTexture* AGlpGame::InstrumentPanel() const
{
	return Instruments ? Instruments->Texture() : nullptr;
}

void AGlpGame::Tick(float DeltaSeconds)
{
	Super::Tick(DeltaSeconds);
	if (!Session || !Library || !PlayerCar())
	{
		return;
	}
	Time += DeltaSeconds;
	APlayerController* Player = GetWorld()->GetFirstPlayerController();
	if (Player && Player->GetViewTarget() != this)
	{
		// A camera actor that activates itself for the player would otherwise win the view.
		Player->SetViewTarget(this);
	}
	Input.Update(Player, Time);
	HandleGlobalKeys();
	if (!Session)
	{
		return;
	}

	glp_inputs Controls{};
	if (bAttract)
	{
		Library->ScriptedInputs(Session, &Controls);
		// Automated runs may nudge the attract-mode driver with scripted keys: steering to put it into a sign, the
		// brake to hold it back until the train comes.
		if (Input.HasScript())
		{
			Controls.steer = FMath::Clamp(Controls.steer + Input.Drive(DeltaSeconds, FMath::Abs(Current.speed)).steer, -1.f, 1.f);
			if (Input.Down(EKeys::Down))
			{
				Controls.throttle = 0.f;
				Controls.brake = 1.f;
			}
		}
	}
	else
	{
		Controls = Input.Drive(DeltaSeconds, FMath::Abs(Current.speed));
		if (HandleMenu())
		{
			Controls.ignition = 1;
		}
	}
	if (Input.Pressed(EKeys::SpaceBar) && (Current.phase == GLP_PHASE_REPLAY || (Current.phase == GLP_PHASE_DRIVING && Current.off_road)))
	{
		Library->Abort(Session);
	}
	if (Library->Step(Session, &Controls, DeltaSeconds) != 0)
	{
		UE_LOG(LogGlpGame, Error, TEXT("step failed: %s"), *Library->LastError());
		return;
	}
	Library->GetState(Session, &Current);
	MessageText = Library->Message(Session);

	if (Event(Current, GLP_EVENT_NEW_BEST_LAP) && (!bAttract || bSaveBest))
	{
		SaveBestLap();
		BuildGhost();
	}
	if (Current.events & ((1u << GLP_EVENT_SIGN_HIT) | (1u << GLP_EVENT_COW_HIT) | (1u << GLP_EVENT_CRASHED)))
	{
		UE_LOG(LogGlpGame, Log, TEXT("%s%s%s (prop %d, crash reason %d)"), Event(Current, GLP_EVENT_SIGN_HIT) ? TEXT("sign hit ") : TEXT(""),
			Event(Current, GLP_EVENT_COW_HIT) ? TEXT("cow hit ") : TEXT(""), Event(Current, GLP_EVENT_CRASHED) ? TEXT("crashed ") : TEXT(""),
			Current.last_soft_hit, Current.last_crash);
	}
	if (Event(Current, GLP_EVENT_SIGN_HIT) && Signs.Contains(Current.last_soft_hit))
	{
		// Knock the sign over: it tips away from the car over the next third of a second.
		if (const AActor* Sign = Signs[Current.last_soft_hit].Actor.Get())
		{
			FVector Away = Sign->GetActorLocation() - PlayerCar()->Root()->GetActorLocation();
			Away.Z = 0.0;
			// Up x away: turning about this axis leans the sign away from the car.
			const FVector Axis = FVector::CrossProduct(FVector::UpVector, Away.GetSafeNormal());
			if (Axis.SizeSquared() > 1e-4)
			{
				Tipping.Add({ Current.last_soft_hit, Time, Axis.GetSafeNormal() });
			}
		}
	}
	if (Current.phase == GLP_PHASE_DRIVING)
	{
		DrivingTime += DeltaSeconds;
	}
	// The instrument cluster at the foot of the screen lags the lateral G and settles back (Hud.cs).
	Dash += (-Current.lateral_acceleration * 6.f - Dash) * FMath::Clamp(DeltaSeconds * 5.f, 0.f, 1.f);

	SyncViews(DeltaSeconds);
	UpdateTipping();
	if (Audio)
	{
		const bool bMusic = Current.phase == GLP_PHASE_SELECT_TRANSMISSION || Current.phase == GLP_PHASE_REPLAY || Current.phase == GLP_PHASE_FINISHED;
		Audio->Update(Current, TrainDistance, bMusic, DeltaSeconds);
	}
	if (Instruments)
	{
		Instruments->Update(Current, CarSpec, bKmh, DeltaSeconds);
	}
	Automate(DeltaSeconds);
}

bool AGlpGame::HandleMenu()
{
	if (Current.phase != GLP_PHASE_SELECT_TRANSMISSION)
	{
		return false;
	}
	// Left/right flips between the cars the level has; the session is rebuilt around the new one.
	const bool bLeft = Input.Pressed(EKeys::Left) || Input.Pressed(EKeys::A);
	if (bLeft || Input.Pressed(EKeys::Right) || Input.Pressed(EKeys::D))
	{
		for (int32 i = 1; i < Cars.Num(); i++)
		{
			const int32 Candidate = (CarIndex + (bLeft ? -i : i) + Cars.Num() * i) % Cars.Num();
			if (Cars[Candidate])
			{
				CarIndex = Candidate;
				break;
			}
		}
		SaveSettings();
		NewSession();
		return false;
	}
	if (Input.Pressed(EKeys::One))
	{
		Transmission = GLP_AUTOMATIC;
		SaveSettings();
	}
	else if (Input.Pressed(EKeys::Two))
	{
		Transmission = GLP_MANUAL_AUTO_CLUTCH;
		SaveSettings();
	}
	else if (Input.Pressed(EKeys::Three))
	{
		Transmission = GLP_MANUAL_CLUTCH;
		SaveSettings();
	}
	// One press goes from the title to a running engine: the choice and the key on the same frame.
	if (!Input.Pressed(EKeys::Enter) && !Input.Pressed(EKeys::SpaceBar))
	{
		return false;
	}
	Library->ChooseTransmission(Session, Transmission);
	return true;
}

void AGlpGame::HandleGlobalKeys()
{
	if (Input.Pressed(EKeys::C))
	{
		Rig.bCockpit = !Rig.bCockpit;
		Rig.Reset();
	}
	if (Input.Pressed(EKeys::U))
	{
		bKmh = !bKmh;
		SaveSettings();
	}
	if (Input.Pressed(EKeys::F5))
	{
		NewSession();
	}
	if (Input.Pressed(EKeys::F9))
	{
		bAttract = !bAttract;
		if (bAttract && Current.phase == GLP_PHASE_SELECT_TRANSMISSION)
		{
			Library->ChooseTransmission(Session, AttractGearbox);
		}
	}
	if (Input.Pressed(EKeys::F11))
	{
		if (UGameUserSettings* Settings = GEngine ? GEngine->GetGameUserSettings() : nullptr)
		{
			Settings->SetFullscreenMode(Settings->GetFullscreenMode() == EWindowMode::Windowed ? EWindowMode::WindowedFullscreen : EWindowMode::Windowed);
			Settings->ApplyResolutionSettings(false);
		}
	}
	if (Input.Pressed(EKeys::F12))
	{
		const FString File = FPaths::Combine(FPaths::ProjectSavedDir(), TEXT("Screenshots"), FString::Printf(TEXT("grudgelap-%s.png"), *FDateTime::Now().ToString(TEXT("%Y%m%d-%H%M%S"))));
		FScreenshotRequest::RequestScreenshot(File, true, false);
	}
}

void AGlpGame::SyncViews(float DeltaSeconds)
{
	FGlpCarView* Car = PlayerCar();
	const bool bInside = InCockpit();
	if (bInside != bInsideShown)
	{
		Car->SetInside(bInside);
		ShowSideMirrors(bInside);
		bInsideShown = bInside;
	}
	if (Mirror && bInside && (++MirrorFrame & 1) == 0)
	{
		// From outside the glass keeps its last picture.
		Mirror->CaptureSceneDeferred();
	}
	FGlpShot Shot;
	if (Current.phase == GLP_PHASE_REPLAY)
	{
		Car->SetPose(Current.replay_position, Current.replay_orientation);
		Shot = Rig.Replay(FVector(Current.replay_position[0], Current.replay_position[1], Current.replay_position[2]), GlpSpace::SimForward(Current.replay_orientation));
	}
	else
	{
		Car->SetPose(Current.position, Current.orientation);
		Car->SetWheels(Current);
		bool bRough = false;
		for (int32 i = 0; i < 4; i++)
		{
			bRough |= Current.wheel_contact[i] && Current.wheel_surface[i] != GLP_SURFACE_ROAD;
		}
		Shot = Rig.Follow(Car->Root()->GetActorTransform(), FMath::Abs(Current.speed),
			FVector2f(Current.lateral_acceleration, Current.longitudinal_acceleration), bRough ? 1.f : 0.f, DeltaSeconds);
	}
	Camera->SetWorldLocationAndRotation(Shot.Location, Shot.Rotation);
	Camera->SetFieldOfView(Shot.HorizontalFov);

	SyncTrafficAndTrain(DeltaSeconds);
	if (Ghost)
	{
		const bool bVisible = Current.ghost_visible && Current.phase == GLP_PHASE_DRIVING;
		const FVector Now = GlpSpace::SimToUnreal(Current.ghost_position);
		if (bVisible)
		{
			Ghost->SetPose(Current.ghost_position, Current.ghost_orientation);
			Ghost->AdvanceWheels(static_cast<float>(FVector::Dist(Now, GhostLast)) / 100.f, Current.ghost_steer, CarSpec.wheel_radius);
		}
		GhostLast = Now;
		Ghost->SetVisible(bVisible);
	}
}

void AGlpGame::SyncTrafficAndTrain(float DeltaSeconds)
{
	const int32 Drones = Library->Traffic(Session, Bodies.GetData(), Bodies.Num());
	const bool bMoving = Current.phase == GLP_PHASE_DRIVING || Current.phase == GLP_PHASE_START_ENGINE;
	for (int32 i = 0; i < Drones && i < Bodies.Num() && i < TrafficCars.Num(); i++)
	{
		TrafficCars[i]->SetPose(Bodies[i].position, Bodies[i].orientation);
		if (bMoving)
		{
			TrafficCars[i]->AdvanceWheels(Bodies[i].speed * DeltaSeconds, 0.f, 0.33f);
		}
	}

	TrainDistance = 1e9f;
	if (TrainCars.IsEmpty())
	{
		return;
	}
	TrainBodies.SetNum(TrainCars.Num());
	const int32 Count = Library->Train(Session, TrainBodies.GetData(), TrainBodies.Num());
	const FVector Player(Current.position[0], Current.position[1], Current.position[2]);
	for (int32 i = 0; i < TrainCars.Num(); i++)
	{
		AActor* Car = TrainCars[i].Get();
		const bool bShown = Current.train_running && i < Count;
		if (Car)
		{
			// The train waits off the circuit between runs; it is only drawn while it runs (TrainView).
			HideTree(Car, !bShown);
			if (bShown)
			{
				Car->SetActorLocationAndRotation(GlpSpace::SimToUnreal(TrainBodies[i].position), GlpSpace::SimRotation(TrainBodies[i].orientation));
			}
		}
		if (bShown)
		{
			const FVector Where(TrainBodies[i].position[0], TrainBodies[i].position[1], TrainBodies[i].position[2]);
			TrainDistance = FMath::Min(TrainDistance, static_cast<float>(FVector::Dist(Where, Player)));
		}
	}
}

void AGlpGame::UpdateTipping()
{
	for (int32 i = Tipping.Num() - 1; i >= 0; i--)
	{
		const FTip& Tip = Tipping[i];
		const float T = FMath::Clamp((Time - Tip.Start) / 0.35f, 0.f, 1.f);
		const float Angle = FMath::Lerp(0.f, 1.45f, 1.f - (1.f - T) * (1.f - T));
		if (const FSign* Sign = Signs.Find(Tip.Prop); Sign && Sign->Actor.IsValid())
		{
			// The sign keeps its heading and leans over about its base, away from the car.
			Sign->Actor->SetActorRotation(FQuat(Tip.Axis, Angle) * Sign->Rest);
		}
		if (T >= 1.f)
		{
			Tipping.RemoveAt(i);
		}
	}
}

void AGlpGame::MapProps()
{
	// The simulation's props by index, matched by position to the imported props that Scripts/build_scene.py
	// tagged GLP.Prop (the export names them Sign-..., Crossbuck-... but labels do not survive cooking). Only the
	// ones that tip over need an actor.
	TArray<glp_prop> Props;
	Props.SetNum(FMath::Max(Library->Props(Session, nullptr, 0), 1));
	const int32 Count = Library->Props(Session, Props.GetData(), Props.Num());
	TArray<AActor*> Candidates;
	for (AActor* Actor : TActorRange<AActor>(GetWorld()))
	{
		if (Actor->ActorHasTag(TEXT("GLP.Prop")))
		{
			Candidates.Add(Actor);
		}
	}
	for (int32 i = 0; i < Count && i < Props.Num(); i++)
	{
		if (Props[i].kind != GLP_PROP_SIGN && Props[i].kind != GLP_PROP_CROSSBUCK)
		{
			continue;
		}
		const FVector Where = GlpSpace::SimToUnreal(Props[i].position);
		AActor* Best = nullptr;
		double BestDistance = 250.0;   // cm
		for (AActor* Actor : Candidates)
		{
			const double Distance = FVector::Dist2D(Actor->GetActorLocation(), Where);
			if (Distance < BestDistance)
			{
				Best = Actor;
				BestDistance = Distance;
			}
		}
		if (Best)
		{
			Best->GetRootComponent()->SetMobility(EComponentMobility::Movable);
			Signs.Add(i, { Best, Best->GetActorQuat() });
			Candidates.Remove(Best);
		}
	}
	UE_LOG(LogGlpGame, Log, TEXT("%d props, %d of them can be knocked over"), Count, Signs.Num());
}

void AGlpGame::Automate(float DeltaSeconds)
{
	if (bClockFromStart || Current.phase == GLP_PHASE_DRIVING || DriveTime > 0.f)
	{
		DriveTime += DeltaSeconds;
	}
	LogTimer += DeltaSeconds;
	if (LogTimer >= 1.f)
	{
		LogTimer = 0.f;
		UE_LOG(LogGlpGame, Log, TEXT("t %.1f s (clock %.1f), phase %d, %.0f km/h, gear %d, %.0f rpm, at (%.1f, %.1f, %.1f), lap %d, score %lld, %s%s%s"),
			DriveTime, Time, Current.phase, Current.speed * 3.6f, Current.gear, Current.rpm, Current.position[0], Current.position[1], Current.position[2],
			Current.lap, static_cast<long long>(Current.score), Current.on_road ? TEXT("on road") : TEXT("off road"),
			Current.train_running ? *FString::Printf(TEXT(", train %.0f m away"), TrainDistance) : TEXT(""),
			MessageText.IsEmpty() ? TEXT("") : *(TEXT(", \"") + MessageText + TEXT("\"")));
	}
	if (NextShot < ShotTimes.Num() && DriveTime >= ShotTimes[NextShot])
	{
		const FString Directory = FPaths::Combine(FPaths::ProjectSavedDir(), TEXT("Screenshots"), TEXT("Glp"));
		IFileManager::Get().MakeDirectory(*Directory, true);
		const FString File = FPaths::Combine(Directory, FString::Printf(TEXT("%s_%02d.png"), InCockpit() ? TEXT("cockpit") : TEXT("chase"), FMath::RoundToInt(ShotTimes[NextShot])));
		FScreenshotRequest::RequestScreenshot(File, true, false);
		UE_LOG(LogGlpGame, Log, TEXT("screenshot requested: %s"), *File);
		NextShot++;
	}
	if (QuitAfter > 0.f && DriveTime >= QuitAfter)
	{
		UE_LOG(LogGlpGame, Log, TEXT("quitting after %.1f s of driving"), DriveTime);
		FPlatformMisc::RequestExit(false);
		QuitAfter = 0.f;
	}
}

FString AGlpGame::SavedFile(const TCHAR* Name) const
{
	return FPaths::Combine(FPaths::ProjectSavedDir(), TEXT("GrudgeLap"), Name);
}

void AGlpGame::LoadSettings()
{
	FString Text;
	if (!FFileHelper::LoadFileToString(Text, *SavedFile(TEXT("settings.ini"))))
	{
		return;
	}
	TArray<FString> Lines;
	Text.ParseIntoArrayLines(Lines);
	for (const FString& Line : Lines)
	{
		FString Key, Value;
		if (!Line.Split(TEXT("="), &Key, &Value))
		{
			continue;
		}
		if (Key == TEXT("car"))
		{
			CarIndex = FCString::Atoi(*Value);
		}
		else if (Key == TEXT("transmission"))
		{
			Transmission = FMath::Clamp(FCString::Atoi(*Value), 0, 2);
		}
		else if (Key == TEXT("kmh"))
		{
			bKmh = FCString::Atoi(*Value) != 0;
		}
	}
}

void AGlpGame::SaveSettings() const
{
	const FString Text = FString::Printf(TEXT("car=%d\ntransmission=%d\nkmh=%d\n"), CarIndex, Transmission, bKmh ? 1 : 0);
	FFileHelper::SaveStringToFile(Text, *SavedFile(TEXT("settings.ini")));
}

void AGlpGame::SaveBestLap()
{
	const int32 Size = Library->BestLap(Session, nullptr, 0);
	if (Size <= 0)
	{
		return;
	}
	TArray<uint8> Bytes;
	Bytes.SetNum(Size);
	Library->BestLap(Session, Bytes.GetData(), Size);
	if (FFileHelper::SaveArrayToFile(Bytes, *SavedFile(TEXT("best_lap.bin"))))
	{
		UE_LOG(LogGlpGame, Log, TEXT("best lap saved: %.2f s"), Current.best_lap_time);
	}
}

bool AGlpGame::LoadBestLap()
{
	TArray<uint8> Bytes;
	if (!FFileHelper::LoadFileToArray(Bytes, *SavedFile(TEXT("best_lap.bin")), FILEREAD_Silent))
	{
		return false;
	}
	if (Library->LoadBestLap(Session, Bytes.GetData(), Bytes.Num()) != 0)
	{
		UE_LOG(LogGlpGame, Warning, TEXT("best lap could not be loaded: %s"), *Library->LastError());
		return false;
	}
	return true;
}

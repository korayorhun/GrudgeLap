#include "GlpGame.h"

#include "Camera/CameraComponent.h"
#include "Components/DirectionalLightComponent.h"
#include "Components/ExponentialHeightFogComponent.h"
#include "Components/SceneCaptureComponent2D.h"
#include "Components/StaticMeshComponent.h"
#include "Components/InstancedStaticMeshComponent.h"
#include "Components/VolumetricCloudComponent.h"
#include "Engine/DirectionalLight.h"
#include "Engine/ExponentialHeightFog.h"
#include "Engine/PostProcessVolume.h"
#include "Engine/StaticMesh.h"
#include "Engine/TextureRenderTarget2D.h"
#include "EngineUtils.h"
#include "Kismet/KismetMaterialLibrary.h"
#include "Materials/MaterialParameterCollection.h"
#include "GameFramework/GameUserSettings.h"
#include "GameFramework/PlayerController.h"
#include "HAL/FileManager.h"
#include "HAL/PlatformProcess.h"
#include "GlpAudio.h"
#include "GlpHud.h"
#include "GlpInstruments.h"
#include "GlpLibrary.h"
#include "GlpSpace.h"
#include "HAL/FileManager.h"
#include "Materials/MaterialInstanceDynamic.h"
#include "HAL/PlatformMisc.h"
#include "Misc/App.h"
#include "Misc/CommandLine.h"
#if WITH_GLP_DLSS
#include "DLSSLibrary.h"
#endif
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "UnrealClient.h"

DEFINE_LOG_CATEGORY_STATIC(LogGlpGame, Log, All);

namespace
{

	// The driver's eye height over the road and distance behind the centre of gravity, per body style
	// (CarView.BodyStyle.EyeH / EyeZ); the driver sits 0.38 m left of the centre line (CarView.DriverX).
	struct FEyePoint
	{
		float Height;
		float Back;
	};
	const FEyePoint EyePoints[] = { { 0.98f, 0.15f }, { 1.02f, 0.12f }, { 1.14f, 0.42f }, { 1.13f, 0.55f }, { 1.17f, 0.45f },
		{ 1.18f, 0.22f }, { 1.16f, 0.50f }, { 1.17f, 0.48f } };

	// The cars the title screen offers, as the Godot game's line-up (Lineup.cs): the 1970s saloon, the two Bravos and
	// the Leon Cupra drive in the traffic only, at the owner's word of 8 November 2026 (the saloon out of the cars to
	// drive; the Bravos and the Cupra looked "drawn by a child"). Their catalogue indices (VehicleSpec.Catalogue).
	bool IsDrivable(int32 Index)
	{
		return Index != 5 && Index != 9 && Index != 10 && Index != 11;
	}

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

void AGlpGame::EnableRayReconstruction()
{
#if WITH_GLP_DLSS
	// DLSS Ray Reconstruction (0.74): on an RTX card with hardware ray tracing one trained denoiser takes the place of
	// Lumen's reflection denoisers and the shadow denoiser, which keeps the hit-lit reflections sharp where those
	// smeared them. The plugin's switch turns those denoisers off itself; the bilateral filter of Lumen's reflections
	// is NVIDIA's further recommendation (safe to change at run time since 5.4). Any other card keeps every
	// built-in denoiser, as DefaultEngine.ini leaves them. -GlpNoRR keeps them on an RTX card too, for a comparison.
	if (FParse::Param(FCommandLine::Get(), TEXT("GlpNoRR")))
	{
		UE_LOG(LogGlpGame, Log, TEXT("DLSS Ray Reconstruction: off (-GlpNoRR)"));
		return;
	}
	if (!UDLSSLibrary::IsDLSSRRSupported() || !UDLSSLibrary::IsRayTracingAvailable())
	{
		UE_LOG(LogGlpGame, Log, TEXT("DLSS Ray Reconstruction: not available (RR %d, ray tracing %d)"),
			UDLSSLibrary::IsDLSSRRSupported() ? 1 : 0, UDLSSLibrary::IsRayTracingAvailable() ? 1 : 0);
		return;
	}
	UDLSSLibrary::EnableDLSSRR(true);
	if (IConsoleVariable* Bilateral = IConsoleManager::Get().FindConsoleVariable(TEXT("r.Lumen.Reflections.BilateralFilter")))
	{
		Bilateral->Set(0, ECVF_SetByGameSetting);
	}
	UE_LOG(LogGlpGame, Log, TEXT("DLSS Ray Reconstruction: %s"), UDLSSLibrary::IsDLSSRREnabled() ? TEXT("on") : TEXT("requested, not enabled"));
#endif
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

	// On a screen the game holds its own frame rate (ApplyFrameCap); the off-screen automation runs set theirs.
	bFrameCap = GEngine && !FParse::Param(FCommandLine::Get(), TEXT("RenderOffScreen"));
	if (bFrameCap)
	{
		FrameCapBefore = GEngine->GetMaxFPS();
		ApplyFrameCap();
	}
	EnableRayReconstruction();
	bAttract = FParse::Param(FCommandLine::Get(), TEXT("GlpAttract"));
	LoadScores();
	bTraffic = !FParse::Param(FCommandLine::Get(), TEXT("GlpNoTraffic"));
	bSpeedRoute = FParse::Param(FCommandLine::Get(), TEXT("GlpSpeedRoute"));
	FParse::Value(FCommandLine::Get(), TEXT("GlpWeather="), FixedWeather);
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

	// The tyre marks (0.76): unit planes in world space, stretched along each strip.
	Skids = NewObject<UInstancedStaticMeshComponent>(this, TEXT("Skids"));
	Skids->SetStaticMesh(LoadObject<UStaticMesh>(nullptr, TEXT("/Engine/BasicShapes/Plane.Plane")));
	if (UMaterialInterface* SkidBase = SkidMaterial.LoadSynchronous())
	{
		Skids->SetMaterial(0, SkidBase);
	}
	Skids->SetCastShadow(false);
	Skids->SetCollisionEnabled(ECollisionEnabled::NoCollision);
	// Drawn before every other translucent surface: a modulating strip drawn after the smoke or a window would darken
	// them too (the rear-view mirror's capture showed it).
	Skids->TranslucencySortPriority = -2;
	Skids->SetUsingAbsoluteLocation(true);
	Skids->SetUsingAbsoluteRotation(true);
	Skids->SetUsingAbsoluteScale(true);
	Skids->SetupAttachment(GetRootComponent());
	Skids->RegisterComponent();
	Skids->SetWorldTransform(FTransform::Identity);

	// The wheels' dust, tyre smoke and spray (0.76): the ring of puffs, all hidden until a wheel throws one. Not in the
	// ray tracing scene nor the sky's capture: a puff faces the eye and would show as a flat card in a reflection. As
	// one primitive the puffs sort by the box of all of them: they are put after the car's glass from outside (the
	// young smoke lies between the camera and the car) and before it from the seat (SyncViews).
	Puffs = NewObject<UInstancedStaticMeshComponent>(this, TEXT("Puffs"));
	Puffs->SetStaticMesh(LoadObject<UStaticMesh>(nullptr, TEXT("/Engine/BasicShapes/Plane.Plane")));
	if (UMaterialInterface* PuffBase = PuffMaterial.LoadSynchronous())
	{
		Puffs->SetMaterial(0, PuffBase);
	}
	Puffs->SetCastShadow(false);
	Puffs->SetCollisionEnabled(ECollisionEnabled::NoCollision);
	Puffs->TranslucencySortPriority = 1;
	Puffs->bVisibleInRayTracing = false;
	Puffs->bVisibleInRealTimeSkyCaptures = false;
	Puffs->bVisibleInReflectionCaptures = false;
	Puffs->bAffectDynamicIndirectLighting = false;
	Puffs->bAffectDistanceFieldLighting = false;
	Puffs->SetUsingAbsoluteLocation(true);
	Puffs->SetUsingAbsoluteRotation(true);
	Puffs->SetUsingAbsoluteScale(true);
	Puffs->SetNumCustomDataFloats(4);
	Puffs->SetupAttachment(GetRootComponent());
	Puffs->RegisterComponent();
	Puffs->SetWorldTransform(FTransform::Identity);
	{
		// Four wheels' dust and smoke, 48 puffs each over a puff's life (the Godot version's PerWheel), their spray and
		// the traffic's (at most 43 alive per emitter on a streaming road); the oldest give way when it is full.
		constexpr int32 PuffCapacity = 1536;
		PuffPool.SetNum(PuffCapacity);
		PuffTransforms.Init(FTransform(FQuat::Identity, FVector::ZeroVector, FVector::ZeroVector), PuffCapacity);
		PuffData.Init(0.f, PuffCapacity * 4);
		Puffs->AddInstances(PuffTransforms, false, true);
		PuffRandom.Initialize(1989);
	}

	LoadSettings();
	// -GlpCar=<catalogue index> drives that car (automation: the off-screen shots of each car, 0.74).
	FParse::Value(FCommandLine::Get(), TEXT("GlpCar="), CarIndex);
	if (!Cars.IsValidIndex(CarIndex) || !Cars[CarIndex] || !IsDrivable(CarIndex))
	{
		CarIndex = 0;
	}
	NewSession();
}

void AGlpGame::ApplyWeather()
{
	// What the credit's weather does to the level (0.13), as WorldBuilder.ApplyWeather does in Godot: under any
	// weather but a clear day the sun drops to a wide soft source behind the cloud, the clouds close over, the air
	// thickens and the camera opens up; a wet road darkens and shines through MPC_Weather (the ground materials,
	// Scripts/build_scene.py); rain is streaks falling past the camera (FallRain).
	UWorld* World = GetWorld();
	if (!World)
	{
		return;
	}
	const int32 Weather = Current.weather;
	const bool bOvercast = Weather != GLP_WEATHER_DRY;
	const bool bRaining = Weather == GLP_WEATHER_RAIN;
	if (UMaterialParameterCollection* Collection = LoadObject<UMaterialParameterCollection>(nullptr, TEXT("/Game/GrudgeLap/Materials/MPC_Weather.MPC_Weather")))
	{
		UKismetMaterialLibrary::SetScalarParameterValue(World, Collection, TEXT("Wetness"), Current.wetness);
		UKismetMaterialLibrary::SetScalarParameterValue(World, Collection, TEXT("Rain"), bRaining ? 1.f : 0.f);
	}
	for (ADirectionalLight* Sun : TActorRange<ADirectionalLight>(World))
	{
		UDirectionalLightComponent* Light = Cast<UDirectionalLightComponent>(Sun->GetLightComponent());
		if (!Light)
		{
			continue;
		}
		if (SunLux < 0.f)
		{
			SunLux = Light->Intensity;
		}
		Light->SetIntensity(bOvercast ? SunLux * 0.12f : SunLux);
		Light->SetLightColor(bOvercast ? FLinearColor(0.86f, 0.89f, 0.95f) : FLinearColor::White);
		Light->SetLightSourceAngle(bOvercast ? 12.f : 0.5357f);
	}
	for (AExponentialHeightFog* FogActor : TActorRange<AExponentialHeightFog>(World))
	{
		UExponentialHeightFogComponent* Fog = FogActor->GetComponent();
		if (!Fog)
		{
			continue;
		}
		if (FogDensity < 0.f)
		{
			FogDensity = Fog->FogDensity;
			FogColour = Fog->FogInscatteringLuminance;
		}
		Fog->SetFogDensity(bOvercast ? FogDensity * (bRaining ? 4.5f : 2.5f) : FogDensity);
		Fog->SetFogInscatteringColor(bOvercast ? FLinearColor(0.42f, 0.44f, 0.48f) : FogColour);
	}
	for (AVolumetricCloud* CloudActor : TActorRange<AVolumetricCloud>(World))
	{
		UVolumetricCloudComponent* Cloud = CloudActor->GetComponentByClass<UVolumetricCloudComponent>();
		if (!Cloud)
		{
			continue;
		}
		if (!CloudMaterial)
		{
			CloudMaterial = Cloud->Material.LoadSynchronous();
		}
		if (!CloudMaterial)
		{
			continue;
		}
		if (bOvercast)
		{
			// The template's cloud material is closed over through whatever coverage parameter it has.
			UMaterialInstanceDynamic* Overcast = UMaterialInstanceDynamic::Create(CloudMaterial, this);
			TArray<FMaterialParameterInfo> Infos;
			TArray<FGuid> Ids;
			CloudMaterial->GetAllScalarParameterInfo(Infos, Ids);
			for (const FMaterialParameterInfo& Info : Infos)
			{
				const FString Name = Info.Name.ToString();
				if (Name.Contains(TEXT("overage")))
				{
					Overcast->SetScalarParameterValue(Info.Name, 0.95f);
				}
				else if (Name.Contains(TEXT("xtinction")) || Name.Contains(TEXT("ensity")))
				{
					float Value = 0.f;
					if (CloudMaterial->GetScalarParameterValue(Info, Value))
					{
						Overcast->SetScalarParameterValue(Info.Name, Value * 3.f);
					}
				}
				UE_LOG(LogGlpGame, Log, TEXT("cloud parameter %s"), *Name);
			}
			Cloud->SetMaterial(Overcast);
			Cloud->SetLayerBottomAltitude(1.2f);
			Cloud->SetLayerHeight(6.f);
		}
		else
		{
			Cloud->SetMaterial(CloudMaterial);
		}
	}
	for (APostProcessVolume* Volume : TActorRange<APostProcessVolume>(World))
	{
		if (!Volume->bUnbound)
		{
			continue;
		}
		if (ExposureEv < 0.f)
		{
			ExposureEv = Volume->Settings.AutoExposureMinBrightness;
		}
		// A grey day is two or three stops dimmer than a sunny one: the eye, and the camera, open up (the sun above is
		// down to an eighth; at 1.6 stops the first wet runs came out as dusk).
		const float Ev = bOvercast ? ExposureEv - (bRaining ? 2.8f : 2.4f) : ExposureEv;
		Volume->Settings.AutoExposureMinBrightness = Ev;
		Volume->Settings.AutoExposureMaxBrightness = Ev;
	}
	if (!Rain)
	{
		// 3600 streaks, as the Godot version's: each a unit plane scaled to 1.4 cm by 42 cm, its long side upright.
		Rain = NewObject<UInstancedStaticMeshComponent>(this, TEXT("Rain"));
		Rain->SetStaticMesh(LoadObject<UStaticMesh>(nullptr, TEXT("/Engine/BasicShapes/Plane.Plane")));
		if (UMaterialInterface* Streak = RainMaterial.IsNull() ? LoadObject<UMaterialInterface>(nullptr, TEXT("/Game/GrudgeLap/Materials/M_Rain.M_Rain")) : RainMaterial.LoadSynchronous())
		{
			Rain->SetMaterial(0, Streak);
		}
		Rain->SetCastShadow(false);
		Rain->SetCollisionEnabled(ECollisionEnabled::NoCollision);
		Rain->TranslucencySortPriority = InCockpit() ? -1 : 1;
		Rain->bVisibleInRayTracing = false;
		Rain->bVisibleInRealTimeSkyCaptures = false;
		Rain->bVisibleInReflectionCaptures = false;
		Rain->bAffectDynamicIndirectLighting = false;
		Rain->bAffectDistanceFieldLighting = false;
		Rain->SetUsingAbsoluteLocation(true);
		Rain->SetUsingAbsoluteRotation(true);
		Rain->SetUsingAbsoluteScale(true);
		Rain->SetupAttachment(GetRootComponent());
		Rain->RegisterComponent();
		Rain->SetWorldTransform(FTransform::Identity);
		Drops.SetNum(3600);
		DropTransforms.Init(FTransform(FQuat::Identity, FVector::ZeroVector, FVector::ZeroVector), Drops.Num());
		Rain->AddInstances(DropTransforms, false, true);
		DropRandom.Initialize(1965);
	}
	if (!bRaining)
	{
		bDropsSeeded = false;
		bRainEyeKnown = false;
	}
	Rain->SetVisibility(bRaining);
	if (WeatherShown != Weather)
	{
		WeatherShown = Weather;
		UE_LOG(LogGlpGame, Log, TEXT("weather %d (wetness %.2f)"), Weather, Current.wetness);
	}
}

void AGlpGame::EndPlay(const EEndPlayReason::Type EndPlayReason)
{
	// The world is going away with every actor in it: only the simulation's session needs closing.
	CloseSession(false);
	if (bFrameCap && GEngine)
	{
		// Play in the editor: the editor gets its own frame rate back.
		GEngine->SetMaxFPS(FrameCapBefore);
	}
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

void AGlpGame::ClearSkidMarks()
{
	if (Skids)
	{
		Skids->ClearInstances();
	}
	SkidNext = 0;
	for (TOptional<FVector>& Last : SkidLast)
	{
		Last.Reset();
	}
}

void AGlpGame::FallRain(float DeltaSeconds, const FVector& Eye, const FQuat& View)
{
	// The Godot version's rain (CameraRig.SetRain and LeadRain, 0.12, 0.53, 0.65): drops let go in a box 32 m across and
	// a metre deep, 10 m over the eye and 5 m ahead, at 9 to 11 m/s within 1.5 degrees of straight down, a little wind
	// pushing them on (0.7 m/s2 to Godot's +x) as they gain speed, each living 1.7 s. The box is led by where the eye
	// will be when the drops reach it: the camera's velocity, smoothed over a fifth of a second (a jump of the view is
	// not taken for one), times the 1.2 s a drop takes to fall below the eye; ahead of a car at 30 m/s there was
	// otherwise only fresh rain high up and the air before it looked dry. Each streak turns to the eye about its own
	// upright axis; the material fades it out within 1.2 to 4.5 m of the eye.
	if (!Rain || !Rain->IsVisible() || DeltaSeconds <= 0.f || Drops.IsEmpty())
	{
		return;
	}
	if (bRainEyeKnown)
	{
		const FVector Velocity = (Eye - RainEye) / DeltaSeconds;
		if (Velocity.SizeSquared() < FMath::Square(12000.0))
		{
			RainVelocity = FMath::Lerp(RainVelocity, Velocity, FMath::Min(DeltaSeconds / 0.2f, 1.f));
		}
	}
	RainEye = Eye;
	bRainEyeKnown = true;
	FVector Ahead = View.GetForwardVector();
	Ahead.Z = 0.0;
	Ahead = Ahead.GetSafeNormal(UE_SMALL_NUMBER, FVector::ForwardVector);
	const FVector Box = Eye + FVector(0.0, 0.0, 1000.0) + Ahead * 500.0 + RainVelocity * 1.2;
	static const FVector Push = GlpSpace::GodotLocalToUnreal(0.7, -1.5, 0.0);
	constexpr float Life = 1.7f;
	auto LetGo = [this, &Box](FDrop& Drop, float Age)
	{
		Drop.Position = Box + FVector(DropRandom.FRandRange(-1600.f, 1600.f), DropRandom.FRandRange(-1600.f, 1600.f), DropRandom.FRandRange(-50.f, 50.f));
		Drop.Velocity = DropRandom.VRandCone(-FVector::UpVector, FMath::DegreesToRadians(1.5f)) * DropRandom.FRandRange(900.f, 1100.f);
		Drop.Age = Age;
	};
	if (!bDropsSeeded)
	{
		// Start as if it had been raining: every drop somewhere in its fall.
		bDropsSeeded = true;
		for (FDrop& Drop : Drops)
		{
			const float Age = DropRandom.FRandRange(0.f, Life);
			LetGo(Drop, Age);
			Drop.Position += Drop.Velocity * Age + 0.5 * Push * Age * Age;
			Drop.Velocity += Push * Age;
		}
	}
	for (int32 i = 0; i < Drops.Num(); i++)
	{
		FDrop& Drop = Drops[i];
		Drop.Age += DeltaSeconds;
		if (Drop.Age >= Life)
		{
			LetGo(Drop, FMath::Fmod(Drop.Age, Life));
		}
		Drop.Velocity += Push * DeltaSeconds;
		Drop.Position += Drop.Velocity * DeltaSeconds;
		// Upright, its face turned to the eye about the vertical (the plane's +Z towards the eye, its Y up).
		FVector ToEye = Eye - Drop.Position;
		ToEye.Z = 0.0;
		const FQuat Facing = FRotationMatrix::MakeFromYZ(FVector::UpVector, ToEye.GetSafeNormal(UE_SMALL_NUMBER, FVector::ForwardVector)).ToQuat();
		DropTransforms[i] = FTransform(Facing, Drop.Position, FVector(0.014, 0.42, 1.0));
	}
	Rain->BatchUpdateInstancesTransforms(0, DropTransforms, true, false, true);
}

void AGlpGame::ClearPuffs()
{
	for (FPuff& Puff : PuffPool)
	{
		Puff.Life = 0.f;
	}
	for (int32 w = 0; w < 4; w++)
	{
		PuffLast[w].Reset();
		PuffDue[w][0] = PuffDue[w][1] = PuffDue[w][2] = 0.f;
	}
	TrafficSprayDue.Reset();
	TrafficSprayLast.Reset();
}

void AGlpGame::ThrowPuffs(float DeltaSeconds, const FVector& Eye)
{
	// As the Godot version's WheelDust: dust off the verge and the plain while a wheel runs there faster than 5 m/s, full
	// at 30; the pale smoke of rubber while a tyre slides hard on the road (its slip intensity, VehicleSim.SlipIntensity,
	// from 0.55, full at 0.95, less when the car barely moves); on a wet road a fine mist of spray from 8 m/s, full at
	// 33, by the road's wetness, behind the player's wheels and the traffic's rear wheels (0.62; the core does not
	// export the puddles' depth, so the spray is not thickened through a puddle as in Godot). A kind's puffs per
	// emitter over a puff's life at full; each rising at 0.4 to 1.6 m/s within 70 degrees of up, slowed by 1.5 to
	// 3 m/s2 and carried downwind, growing and thinning out to nothing. None in the replay. -GlpPuffTest throws all
	// three from every wheel.
	struct FKind
	{
		float Life, Start, End, Lift, Opacity, PerEmitter;
		FLinearColor Colour;
	};
	static const FKind Dust{ 1.6f, 0.6f, 2.2f, 0.f, 0.9f, 48.f, FLinearColor(FColor(148, 135, 117)) };     // sRGB 0.58, 0.53, 0.46
	static const FKind Smoke{ 2.2f, 0.8f, 3.0f, 15.f, 0.9f, 48.f, FLinearColor(FColor(219, 219, 222)) };   // sRGB 0.86, 0.86, 0.87
	static const FKind Spray{ 0.8f, 0.35f, 1.3f, 10.f, 0.2f, 96.f, FLinearColor(FColor(184, 191, 199)) };  // sRGB 0.72, 0.75, 0.78
	static const bool bTest = FParse::Param(FCommandLine::Get(), TEXT("GlpPuffTest"));
	// Owed puffs of a kind for an emitter that moved from From to To this frame, thrown spread along that path: at
	// speed a wheel covers half a metre between frames, and in one spot the puffs came out in clumps.
	auto Throw = [this, DeltaSeconds](const FKind& Kind, const FVector& From, const FVector& To, float Amount, float& Due)
	{
		if (Amount <= 0.02f)
		{
			Due = 0.f;
			return;
		}
		for (Due += Amount * Kind.PerEmitter / Kind.Life * DeltaSeconds; Due >= 1.f; Due -= 1.f)
		{
			FPuff& Puff = PuffPool[PuffNext];
			PuffNext = (PuffNext + 1) % PuffPool.Num();
			Puff.Position = FMath::Lerp(From, To, PuffRandom.FRand()) + FVector(0.0, 0.0, Kind.Lift);
			Puff.Velocity = PuffRandom.VRandCone(FVector::UpVector, FMath::DegreesToRadians(70.f)) * PuffRandom.FRandRange(40.f, 160.f);
			Puff.Age = 0.f;
			Puff.Life = Kind.Life;
			Puff.StartSize = Kind.Start;
			Puff.EndSize = Kind.End;
			Puff.Damping = PuffRandom.FRandRange(150.f, 300.f);
			Puff.Opacity = Kind.Opacity;
			Puff.Colour = Kind.Colour;
		}
	};
	FGlpCarView* Car = PlayerCar();
	const float Wet = FMath::Clamp(Current.wetness, 0.f, 1.f);
	if (Puffs && Car && Car->Root() && Current.phase == GLP_PHASE_DRIVING && DeltaSeconds > 0.f)
	{
		const float Speed = FVector(Current.velocity[0], Current.velocity[1], Current.velocity[2]).Size();
		const FVector Up = Car->Root()->GetActorUpVector();
		for (int32 w = 0; w < 4; w++)
		{
			const FVector Contact = Car->WheelHub(w) - Up * (CarSpec.wheel_radius * 100.f);
			const FVector From = PuffLast[w].Get(Contact);
			PuffLast[w] = Contact;
			const float Slip = FMath::Clamp(FMath::Max(FMath::Abs(Current.wheel_slip_ratio[w]) * 2.5f, FMath::Abs(Current.wheel_slip_angle[w]) * 4.f) - 0.25f, 0.f, 1.f);
			const bool bRoad = Current.wheel_contact[w] != 0 && Current.wheel_surface[w] == GLP_SURFACE_ROAD;
			const bool bGrass = Current.wheel_contact[w] != 0 && Current.wheel_surface[w] == GLP_SURFACE_GRASS;
			Throw(Dust, From, Contact, bTest ? 1.f : bGrass ? FMath::Clamp((Speed - 5.f) / 25.f, 0.f, 1.f) : 0.f, PuffDue[w][0]);
			Throw(Smoke, From, Contact, bTest ? 1.f : bRoad ? FMath::Clamp((Slip - 0.55f) / 0.4f, 0.f, 1.f) * FMath::Clamp(Speed / 6.f + 0.3f, 0.f, 1.f) : 0.f, PuffDue[w][1]);
			Throw(Spray, From, Contact, bTest ? 0.45f : bRoad ? Wet * FMath::Clamp((Speed - 8.f) / 25.f, 0.f, 1.f) * 0.45f : 0.f, PuffDue[w][2]);
		}
		// The traffic's spray: behind each drone's rear wheels, 0.75 m out from its centre, 1.3 m back, at the road (its
		// Godot frame); the drones have no wheels in the simulation.
		const int32 Drones = FMath::Min(TrafficCars.Num(), Bodies.Num());
		TrafficSprayDue.SetNum(Drones * 2);
		TrafficSprayLast.SetNum(Drones * 2);
		for (int32 i = 0; i < Drones; i++)
		{
			const AActor* Drone = TrafficCars[i] ? TrafficCars[i]->Root() : nullptr;
			const float Amount = Drone && (Wet > 0.f || bTest) ? FMath::Max(Wet, bTest ? 1.f : 0.f) * FMath::Clamp((FMath::Abs(Bodies[i].speed) - 8.f) / 25.f, 0.f, 1.f) * 0.45f : 0.f;
			for (int32 Side = 0; Side < 2; Side++)
			{
				const int32 e = i * 2 + Side;
				if (Amount <= 0.02f)
				{
					TrafficSprayLast[e].Reset();
					TrafficSprayDue[e] = 0.f;
					continue;
				}
				const FVector At = Drone->GetActorTransform().TransformPosition(GlpSpace::GodotLocalToUnreal(Side ? 0.75 : -0.75, -0.5, 1.3));
				const FVector From = TrafficSprayLast[e].Get(At);
				TrafficSprayLast[e] = At;
				Throw(Spray, From, At, Amount, TrafficSprayDue[e]);
			}
		}
	}
	else
	{
		for (int32 w = 0; w < 4; w++)
		{
			PuffLast[w].Reset();
		}
		for (TOptional<FVector>& Last : TrafficSprayLast)
		{
			Last.Reset();
		}
	}
	if (!Puffs)
	{
		return;
	}
	// The Godot version's particle gravity, (-3.776, 0.25, -1.32) m/s2 in its world frame, downwind (to its -x and -z)
	// and a little up; Godot's (x, y, z) is Unreal's (x, z, y), not the simulation's mapping (SimToUnreal negates z).
	static const FVector Drift = GlpSpace::GodotLocalToUnreal(-3.776, 0.25, -1.32);
	bool bAny = false;
	for (int32 i = 0; i < PuffPool.Num(); i++)
	{
		FPuff& Puff = PuffPool[i];
		float* Data = &PuffData[i * 4];
		if (Puff.Life > 0.f)
		{
			Puff.Age += DeltaSeconds;
			if (Puff.Age >= Puff.Life)
			{
				Puff.Life = 0.f;
			}
		}
		if (Puff.Life <= 0.f)
		{
			PuffTransforms[i] = FTransform(FQuat::Identity, Eye, FVector::ZeroVector);
			Data[0] = 0.f;
			continue;
		}
		bAny = true;
		Puff.Velocity += Drift * DeltaSeconds;
		const double Speed = Puff.Velocity.Size();
		if (Speed > 0.0)
		{
			Puff.Velocity *= FMath::Max(Speed - Puff.Damping * DeltaSeconds, 0.0) / Speed;
		}
		Puff.Position += Puff.Velocity * DeltaSeconds;
		const float T = Puff.Age / Puff.Life;
		// The plane is a metre across; it faces the eye (its +Z towards it).
		const FVector ToEye = (Eye - Puff.Position).GetSafeNormal(UE_SMALL_NUMBER, FVector::UpVector);
		PuffTransforms[i] = FTransform(FRotationMatrix::MakeFromZ(ToEye).ToQuat(), Puff.Position, FVector(FMath::Lerp(Puff.StartSize, Puff.EndSize, T)));
		Data[0] = Puff.Opacity * (1.f - T);
		Data[1] = Puff.Colour.R;
		Data[2] = Puff.Colour.G;
		Data[3] = Puff.Colour.B;
	}
	if (!bAny && !bPuffsShown)
	{
		return;
	}
	bPuffsShown = bAny;
	// The instance data manager sends the changed instances and their bounds at the end of the frame; marking the
	// render state dirty as well rebuilt the whole scene proxy every frame.
	Puffs->BatchUpdateInstancesTransforms(0, PuffTransforms, true, false, true);
	Puffs->SetCustomData(0, PuffPool.Num() - 1, PuffData, false);
}

void AGlpGame::LaySkidMarks()
{
	// As the Godot version's SkidMarks: a wheel on the road marks it from 0.32 of its slip intensity (the squeal's measure,
	// VehicleSim.SlipIntensity), so a tyre at the edge of its grip squeals before it marks; a strip from its last contact
	// to the present one, 1.5 cm over the road, 21 cm wide; 6000 strips, the oldest giving way.
	constexpr int32 Capacity = 6000;
	FGlpCarView* Car = PlayerCar();
	if (!Skids || !Car || !Car->Root() || Current.phase != GLP_PHASE_DRIVING)
	{
		for (TOptional<FVector>& Last : SkidLast)
		{
			Last.Reset();
		}
		return;
	}
	const FVector Up = Car->Root()->GetActorUpVector();
	for (int32 w = 0; w < 4; w++)
	{
		const float Intensity = FMath::Clamp(FMath::Max(FMath::Abs(Current.wheel_slip_ratio[w]) * 2.5f, FMath::Abs(Current.wheel_slip_angle[w]) * 4.f) - 0.25f, 0.f, 1.f);
		if (!Current.wheel_contact[w] || Current.wheel_surface[w] != GLP_SURFACE_ROAD || Intensity < 0.32f)
		{
			SkidLast[w].Reset();
			continue;
		}
		const FVector Contact = Car->WheelHub(w) - Up * (CarSpec.wheel_radius * 100.f - 1.5f);
		if (!SkidLast[w].IsSet())
		{
			SkidLast[w] = Contact;
			continue;
		}
		const FVector From = SkidLast[w].GetValue();
		const FVector Along = Contact - From;
		const double Length = Along.Size();
		if (Length < 20.0)
		{
			continue;
		}
		if (Length < 300.0)
		{
			const FTransform Strip(FRotationMatrix::MakeFromXZ(Along, Up).ToQuat(), (From + Contact) * 0.5, FVector(Length / 100.0, 0.21, 1.0));
			if (Skids->GetInstanceCount() < Capacity)
			{
				Skids->AddInstance(Strip, true);
			}
			else
			{
				// Teleported, and through the instance data manager rather than a rebuild of the 6000-strip proxy.
				Skids->UpdateInstanceTransform(SkidNext, Strip, true, false, true);
			}
			SkidNext = (SkidNext + 1) % Capacity;
		}
		SkidLast[w] = Contact;
	}
}

void AGlpGame::NewSession()
{
	Scores.Dismiss();
	bScoreOffered = false;
	CloseSession(true);
	// The credit's weather: -GlpWeather for an automated run, otherwise the player's choice (clear by default; 4 has
	// the core draw one for every credit, as the Godot version's RANDOM does).
	const int32 Choice = FixedWeather >= 0 ? FixedWeather : WeatherChoice;
	const int32 WeatherFlags = Choice == 4 ? GLP_RANDOM_WEATHER : (FMath::Clamp(Choice, 0, 3) << GLP_WEATHER_SHIFT);
	Session = Library->Create(CarIndex, (bTraffic ? GLP_TRAFFIC : 0) | (bSpeedRoute ? GLP_SPEED_ROUTE : 0) | WeatherFlags);
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
	ClearSkidMarks();
	ClearPuffs();
	const FEyePoint& Eye = EyePoints[FMath::Clamp(CarSpec.style, 0, (int32)UE_ARRAY_COUNT(EyePoints) - 1)];
	Rig.Eye = FVector3f(-0.38f, Eye.Height - CarSpec.cg_height, Eye.Back);
	// A prepared model's own eye (GLP.Eye, build_scene.py), as CarView takes the model's Eye node (0.74): the cockpit
	// is then the model's own cabin seen from its driver's seat. The table above is the lofted cars'.
	if (const FGlpCarView* Car = PlayerCar(); Car && Car->Root())
	{
		if (const AActor* EyeActor = Car->Find(TEXT("GLP.Eye")))
		{
			// The car's Unreal frame in centimetres; Unreal's (x, y, z) there is Godot's (x, z, y) (GlpSpace).
			const FVector Local = Car->Root()->GetActorTransform().InverseTransformPosition(EyeActor->GetActorLocation()) / 100.0;
			Rig.Eye = FVector3f(static_cast<float>(Local.X), static_cast<float>(Local.Z), static_cast<float>(Local.Y));
		}
	}
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

	// The drones (0.74), as the Godot version's since 0.70 (Bootstrap.TrafficCars, CarView.TrafficPaints): every car with
	// a model in turn, the everyday ones between the sports cars, each in the next of the road's twelve colours, counting
	// on from the last credit's, so no two drones of a credit look alike; the Ferraris (the flat-twelve and the 458)
	// keep their own red. Until 0.73 only the three everyday saloons drove here, in the original's three paints.
	static const int32 Traffic[] = { 5, 2, 6, 8, 7, 13, 12, 4, 3, 0, 1 };
	static const FColor TrafficPaints[] = { FColor(224, 224, 217), FColor(15, 28, 77), FColor(158, 15, 15), FColor(158, 156, 158),
		FColor(18, 56, 33), FColor(194, 173, 133), FColor(9, 9, 10), FColor(107, 148, 189), FColor(84, 13, 18), FColor(204, 153, 41),
		FColor(84, 87, 89), FColor(71, 46, 28) };
	const int32 Drones = Library->Traffic(Session, nullptr, 0);
	Bodies.SetNum(FMath::Max(Drones, 1));
	Library->Traffic(Session, Bodies.GetData(), Bodies.Num());
	for (int32 i = 0; i < Drones; i++)
	{
		const int32 Serial = DroneSerial++ % (UE_ARRAY_COUNT(Traffic) * UE_ARRAY_COUNT(TrafficPaints));
		const int32 Index = Traffic[Serial % UE_ARRAY_COUNT(Traffic)];
		FGlpCarView* Source = Cars.IsValidIndex(Index) && Cars[Index] ? Cars[Index].Get() : Cars[0].Get();
		const FLinearColor Colour(TrafficPaints[Serial % UE_ARRAY_COUNT(TrafficPaints)]);
		const bool bOwnPaint = Index == 0 || Index == 8;
		if (TUniquePtr<FGlpCarView> Copy = Source->Clone(GetWorld(), FString::Printf(TEXT("Drone%d"), i), bOwnPaint ? nullptr : &Colour, nullptr))
		{
			Copy->Root()->Tags.Add(TEXT("GLP.Copy"));
			TrafficCars.Add(MoveTemp(Copy));
		}
	}
	Library->GetState(Session, &Current);
	ApplyWeather();
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
	if (bFrameCap && Time >= FrameCapCheck)
	{
		// The window may have lost the focus or moved to a screen with another refresh rate.
		FrameCapCheck = Time + 1.f;
		ApplyFrameCap();
	}
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
	if ((Input.Pressed(EKeys::SpaceBar) || Input.Pressed(EKeys::Gamepad_FaceButton_Right)) && (Current.phase == GLP_PHASE_REPLAY || (Current.phase == GLP_PHASE_DRIVING && Current.off_road)))
	{
		Library->Abort(Session);
	}
	if (Library->Step(Session, &Controls, DeltaSeconds) != 0)
	{
		UE_LOG(LogGlpGame, Error, TEXT("step failed: %s"), *Library->LastError());
		return;
	}
	// The player's brake lamps follow the pedal (0.76), as the Godot version's CarView.Braking.
	if (FGlpCarView* Car = PlayerCar())
	{
		Car->SetBraking(Controls.brake > 0.05f ? 1.f : 0.f);
	}
	Library->GetState(Session, &Current);
	MessageText = Library->Message(Session);
	if (Current.phase == GLP_PHASE_FINISHED)
	{
		if (!bScoreOffered)
		{
			// The credit is over: a score that makes the table asks for initials (the attract mode's does not).
			bScoreOffered = true;
			if (!bAttract)
			{
				Scores.Offer(Current.score, Current.credit_best_lap, Current.credit_best_route, CarNameText);
			}
		}
		const int32 Direction = (Input.Down(EKeys::Up) || Input.Down(EKeys::W) || Input.Down(EKeys::Gamepad_DPad_Up) ? 1 : 0)
			- (Input.Down(EKeys::Down) || Input.Down(EKeys::S) || Input.Down(EKeys::Gamepad_DPad_Down) ? 1 : 0);
		const bool bLeft = Input.Pressed(EKeys::Left) || Input.Pressed(EKeys::A) || Input.Pressed(EKeys::Gamepad_DPad_Left) || Input.Pressed(EKeys::Gamepad_LeftStick_Left);
		const bool bRight = Input.Pressed(EKeys::Right) || Input.Pressed(EKeys::D) || Input.Pressed(EKeys::Gamepad_DPad_Right) || Input.Pressed(EKeys::Gamepad_LeftStick_Right);
		const bool bConfirm = Input.Pressed(EKeys::Enter) || Input.Pressed(EKeys::SpaceBar) || Input.Pressed(EKeys::Gamepad_FaceButton_Bottom);
		if (Scores.Update(DeltaSeconds, Direction, bLeft, bRight, bConfirm))
		{
			SaveScores();
		}
	}

	if (Event(Current, GLP_EVENT_NEW_BEST_LAP) && (!bAttract || bSaveBest))
	{
		SaveBestLap();
		BuildGhost();
	}
	if (Event(Current, GLP_EVENT_KNOCKED))
	{
		// The head is thrown towards what the car struck (the game's ShowKnock: Jolt(-push), push = -ToGodot(normal));
		// knock_normal is in the simulation's car frame (z forward), the rig's in Godot's (z back).
		Rig.AddJolt(FVector3f(Current.knock_normal[0], Current.knock_normal[1], -Current.knock_normal[2]), Current.knock_speed);
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
		Audio->Update(Current, TrainDistance, InCockpit(), DeltaSeconds);
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
	const bool bLeft = Input.Pressed(EKeys::Left) || Input.Pressed(EKeys::A) || Input.Pressed(EKeys::Gamepad_DPad_Left) || Input.Pressed(EKeys::Gamepad_LeftStick_Left);
	if (bLeft || Input.Pressed(EKeys::Right) || Input.Pressed(EKeys::D) || Input.Pressed(EKeys::Gamepad_DPad_Right) || Input.Pressed(EKeys::Gamepad_LeftStick_Right))
	{
		for (int32 i = 1; i < Cars.Num(); i++)
		{
			const int32 Candidate = (CarIndex + (bLeft ? -i : i) + Cars.Num() * i) % Cars.Num();
			if (Cars[Candidate] && IsDrivable(Candidate))
			{
				CarIndex = Candidate;
				break;
			}
		}
		SaveSettings();
		NewSession();
		if (Audio)
		{
			Audio->Chime();
		}
		return false;
	}
	// H steps the weather on the title screen (0.15): clear, overcast, wet road, rain, random; the session is rebuilt in it.
	if (Input.Pressed(EKeys::H))
	{
		WeatherChoice = (WeatherChoice + 1) % 5;
		SaveSettings();
		NewSession();
		if (Audio)
		{
			Audio->Chime();
		}
		return false;
	}
	// M opens the folder for the player's own replay music (replay.wav, read when a car is chosen), made if missing.
	if (Input.Pressed(EKeys::M))
	{
		const FString Folder = UGlpAudio::MusicFolder();
		IFileManager::Get().MakeDirectory(*Folder, true);
		FPlatformProcess::ExploreFolder(*Folder);
		return false;
	}
	// The cabinet chimed at every choice.
	if (Input.Pressed(EKeys::One))
	{
		Transmission = GLP_AUTOMATIC;
		SaveSettings();
		if (Audio)
		{
			Audio->Chime();
		}
	}
	else if (Input.Pressed(EKeys::Two))
	{
		Transmission = GLP_MANUAL_AUTO_CLUTCH;
		SaveSettings();
		if (Audio)
		{
			Audio->Chime();
		}
	}
	else if (Input.Pressed(EKeys::Three))
	{
		Transmission = GLP_MANUAL_CLUTCH;
		SaveSettings();
		if (Audio)
		{
			Audio->Chime();
		}
	}
	else if (Input.Pressed(EKeys::E) || Input.Pressed(EKeys::PageUp) || Input.Pressed(EKeys::Gamepad_RightShoulder) || Input.Pressed(EKeys::Q) || Input.Pressed(EKeys::PageDown) || Input.Pressed(EKeys::Gamepad_LeftShoulder))
	{
		// The shoulders (or E/Q) step through the gearboxes: a pad has no number keys.
		const bool bForward = Input.Pressed(EKeys::E) || Input.Pressed(EKeys::PageUp) || Input.Pressed(EKeys::Gamepad_RightShoulder);
		Transmission = (Transmission + (bForward ? 1 : 2)) % 3;
		SaveSettings();
		if (Audio)
		{
			Audio->Chime();
		}
	}
	// One press goes from the title to a running engine: the choice and the key on the same frame.
	if (!Input.Pressed(EKeys::Enter) && !Input.Pressed(EKeys::SpaceBar) && !Input.Pressed(EKeys::Gamepad_FaceButton_Bottom) && !Input.Pressed(EKeys::Gamepad_Special_Right))
	{
		return false;
	}
	Library->ChooseTransmission(Session, Transmission);
	return true;
}

void AGlpGame::HandleGlobalKeys()
{
	if (Input.Pressed(EKeys::C) || Input.Pressed(EKeys::Gamepad_FaceButton_Top))
	{
		Rig.bCockpit = !Rig.bCockpit;
		Rig.Reset();
	}
	if (Input.Pressed(EKeys::U))
	{
		bKmh = !bKmh;
		SaveSettings();
	}
	if (Input.Pressed(EKeys::F5) || Input.Pressed(EKeys::Gamepad_Special_Left))
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
	// Esc (0.74): the Unreal version had no way out but Alt+F4. Until it has the Godot version's menu, Esc on the title
	// screen quits and during a credit goes back to the title screen.
	if (Input.Pressed(EKeys::Escape))
	{
		if (Current.phase == GLP_PHASE_SELECT_TRANSMISSION)
		{
			SaveSettings();
			FPlatformMisc::RequestExit(false);
		}
		else
		{
			NewSession();
		}
	}
	if (Input.Pressed(EKeys::F12))
	{
		const FString File = FPaths::Combine(FPaths::ProjectSavedDir(), TEXT("Screenshots"), FString::Printf(TEXT("grudgelap-%s.png"), *FDateTime::Now().ToString(TEXT("%Y%m%d-%H%M%S"))));
		FScreenshotRequest::RequestScreenshot(File, true, false);
	}
}

void AGlpGame::ApplyFrameCap()
{
	// The Godot version's Bootstrap.ApplyFrameCap: an even fraction of the screen's refresh rate, at most 90 (72 on a
	// 144 Hz panel), and 30 while the window is in the background. Uncapped, the development laptop's 144 Hz panel
	// kept its GPU (an RTX 5060 drawing for an Intel-driven panel) flat out, and the driver timed out within a
	// minute: a black screen and a lost device (28 October 2026). Vsync alone does not hold a hybrid laptop's
	// discrete GPU back; the engine's limiter does. The refresh rate is the primary display's.
	const float Refresh = static_cast<float>(FPlatformMisc::GetMaxRefreshRate());
	const bool bFocused = FApp::HasFocus();
	const int32 Cap = bFocused ? FMath::Max(30, FMath::RoundToInt(Refresh / FMath::CeilToFloat(Refresh / 90.f))) : 30;
	if (Cap == FrameCap)
	{
		return;
	}
	FrameCap = Cap;
	GEngine->SetMaxFPS(static_cast<float>(Cap));
	// One line per change: a crash report then says what the game was asking of the GPU.
	UE_LOG(LogGlpGame, Log, TEXT("frame cap: %d fps on a %.0f Hz screen%s"), Cap, Refresh, bFocused ? TEXT("") : TEXT(" (in the background)"));
}

void AGlpGame::SyncViews(float DeltaSeconds)
{
	FGlpCarView* Car = PlayerCar();
	const bool bInside = InCockpit();
	if (bInside != bInsideShown)
	{
		Car->SetInside(bInside);
		if (Puffs)
		{
			// From the seat every puff is beyond the glass; from outside the young smoke is between the eye and the car.
			Puffs->SetTranslucentSortPriority(bInside ? -1 : 1);
		}
		if (Rain)
		{
			// The rain too: beyond the windscreen from the seat, between the eye and the car from outside.
			Rain->SetTranslucentSortPriority(bInside ? -1 : 1);
		}
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
	LaySkidMarks();
	ThrowPuffs(DeltaSeconds, Shot.Location);
	FallRain(DeltaSeconds, Shot.Location, Shot.Rotation);
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
		// A drone's brake lamps while it slows faster than 1.2 m/s^2, held 0.4 s (the Godot version's 0.69 rule).
		DroneSpeeds.SetNum(Bodies.Num());
		DroneBraking.SetNum(Bodies.Num());
		if (bMoving && DeltaSeconds > 0.f)
		{
			const float Slowing = (DroneSpeeds[i] - Bodies[i].speed) / DeltaSeconds;
			DroneBraking[i] = Slowing > 1.2f ? 0.4f : FMath::Max(DroneBraking[i] - DeltaSeconds, 0.f);
		}
		DroneSpeeds[i] = Bodies[i].speed;
		TrafficCars[i]->SetBraking(DroneBraking[i] > 0.f ? 1.f : 0.f);
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
		else if (Key == TEXT("weather"))
		{
			WeatherChoice = FMath::Clamp(FCString::Atoi(*Value), 0, 4);
		}
		else if (Key == TEXT("units"))
		{
			// Metric by default since 0.14: the old "kmh" key is left unread, so a copy that never chose gets km/h once.
			bKmh = !Value.Equals(TEXT("mph"), ESearchCase::IgnoreCase);
		}
	}
}

void AGlpGame::SaveSettings() const
{
	const FString Text = FString::Printf(TEXT("car=%d\ntransmission=%d\nunits=%s\nweather=%d\n"), CarIndex, Transmission, bKmh ? TEXT("kmh") : TEXT("mph"), WeatherChoice);
	FFileHelper::SaveStringToFile(Text, *SavedFile(TEXT("settings.ini")));
}

void AGlpGame::LoadScores()
{
	FString Text;
	if (FFileHelper::LoadFileToString(Text, *SavedFile(TEXT("scores.txt"))) && !Scores.Parse(Text))
	{
		UE_LOG(LogGlpGame, Warning, TEXT("high scores could not be read; the factory table stands"));
	}
}

void AGlpGame::SaveScores() const
{
	FFileHelper::SaveStringToFile(Scores.Serialize(), *SavedFile(TEXT("scores.txt")));
}

FString AGlpGame::BestLapFile() const
{
	// Each car its own best lap, as the Godot version keeps them since 0.73 (bestlap-<style>.bin): one file for every
	// car had the ghost of one car's lap race in the shape of another. The old best_lap.bin, of an unknown car, is left.
	return FString::Printf(TEXT("best_lap_%d.bin"), CarIndex);
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
	if (FFileHelper::SaveArrayToFile(Bytes, *SavedFile(*BestLapFile())))
	{
		UE_LOG(LogGlpGame, Log, TEXT("best lap saved: %.2f s"), Current.best_lap_time);
	}
}

bool AGlpGame::LoadBestLap()
{
	TArray<uint8> Bytes;
	if (!FFileHelper::LoadFileToArray(Bytes, *SavedFile(*BestLapFile()), FILEREAD_Silent))
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

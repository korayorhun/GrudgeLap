#include "GlpCarDriver.h"

#include "Camera/CameraComponent.h"
#include "EngineUtils.h"
#include "GameFramework/PlayerController.h"
#include "GlpLibrary.h"
#include "HAL/FileManager.h"
#include "Misc/CommandLine.h"
#include "Misc/Paths.h"
#include "UnrealClient.h"

DEFINE_LOG_CATEGORY_STATIC(LogGlpCar, Log, All);

namespace
{
	/**
	 * Simulation space (metres; +X right, +Y up, +Z forward) to Unreal's (centimetres). Godot shows the simulation
	 * as (x, y, -z) and the glTF import maps Godot's (x, y, z) to Unreal's (x, z, y), so the car lands exactly where
	 * the imported scene put it.
	 */
	FVector ToUnreal(const FVector& V, float Scale = 100.f)
	{
		return FVector(V.X, -V.Z, V.Y) * Scale;
	}

	// The game's camera rig, in the car's Unreal frame (cm): the driver's eye of the flat-12 (CarView.Eye: 0.38 m
	// left, 0.37 m over the centre of gravity, 0.15 m behind it), and the chase camera 6.3 m behind and 1.29 m over
	// it, looking 2 m ahead of the car. The imported car faces its local -Y.
	const FVector CockpitEye(-38.f, 15.f, 37.f);
	const FVector ChaseOffset(0.f, 630.f, 129.f);
	const FVector ChaseFocus(0.f, -200.f, 9.f);
}

AGlpCarDriver::AGlpCarDriver()
{
	PrimaryActorTick.bCanEverTick = true;
	PrimaryActorTick.TickGroup = TG_PrePhysics;
	Camera = CreateDefaultSubobject<UCameraComponent>(TEXT("Camera"));
	RootComponent = Camera;
	// 62 degrees vertical on 16:9, as the game.
	Camera->SetFieldOfView(94.f);
	Camera->SetConstraintAspectRatio(true);
	Camera->SetAspectRatio(16.f / 9.f);
}

void AGlpCarDriver::BeginPlay()
{
	Super::BeginPlay();

	bAttract |= FParse::Param(FCommandLine::Get(), TEXT("GlpAttract"));
	FString View;
	if (FParse::Value(FCommandLine::Get(), TEXT("GlpView="), View))
	{
		bChaseView = View.Equals(TEXT("chase"), ESearchCase::IgnoreCase);
	}
	FString Shots;
	// Not stopping at commas: FParse::Value treats them as separators by default.
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

	Wheels.SetNum(4);
	Spinners.SetNum(4);
	WheelRest.SetNum(4);
	SpinnerRest.SetNum(4);
	for (TActorIterator<AActor> It(GetWorld()); It; ++It)
	{
		for (const FName& Tag : It->Tags)
		{
			const FString Name = Tag.ToString();
			if (Name == TEXT("GLP.Car"))
			{
				Body = *It;
			}
			else if (Name.StartsWith(TEXT("GLP.Wheel")) || Name.StartsWith(TEXT("GLP.Spin")))
			{
				const int32 Index = FCString::Atoi(*Name.Right(1));
				if (Index >= 0 && Index < 4)
				{
					(Name.StartsWith(TEXT("GLP.Wheel")) ? Wheels : Spinners)[Index] = *It;
				}
			}
		}
	}
	if (!Body)
	{
		UE_LOG(LogGlpCar, Error, TEXT("no actor tagged GLP.Car: the level was not built by Scripts/build_scene.py"));
		return;
	}
	for (int32 i = 0; i < 4; i++)
	{
		if (Wheels[i])
		{
			WheelRest[i] = Wheels[i]->GetRootComponent()->GetRelativeTransform();
		}
		if (Spinners[i])
		{
			SpinnerRest[i] = Spinners[i]->GetRootComponent()->GetRelativeTransform();
		}
	}

	const FGlpLibrary* Library = FGlpLibrary::Get();
	if (!Library)
	{
		return;
	}
	Session = Library->Create(Car);
	if (!Session)
	{
		UE_LOG(LogGlpCar, Error, TEXT("no session: %s"), *Library->LastError());
		return;
	}
	Library->ChooseTransmission(Session, GLP_AUTOMATIC);
	UE_LOG(LogGlpCar, Log, TEXT("session started: car %d, %s view, %s"), Car, bChaseView ? TEXT("chase") : TEXT("cockpit"), bAttract ? TEXT("scripted driver") : TEXT("keyboard"));
}

void AGlpCarDriver::EndPlay(const EEndPlayReason::Type EndPlayReason)
{
	if (Session)
	{
		FGlpLibrary::Get()->Destroy(Session);
		Session = 0;
	}
	Super::EndPlay(EndPlayReason);
}

glp_inputs AGlpCarDriver::ReadKeyboard(float DeltaSeconds)
{
	glp_inputs Input{};
	Input.gear = GLP_NO_GEAR_CHANGE;
	const APlayerController* Player = GetWorld()->GetFirstPlayerController();
	if (!Player)
	{
		return Input;
	}
	auto Down = [Player](const FKey& A, const FKey& B) { return Player->IsInputKeyDown(A) || Player->IsInputKeyDown(B); };
	// The wheel turns at a rate rather than jumping, as in the game's keyboard driving.
	const float Target = (Down(EKeys::Right, EKeys::D) ? 1.f : 0.f) - (Down(EKeys::Left, EKeys::A) ? 1.f : 0.f);
	Steer = FMath::FInterpConstantTo(Steer, Target, DeltaSeconds, Target == 0.f ? 4.f : 2.5f);
	Input.steer = Steer;
	Input.throttle = Down(EKeys::Up, EKeys::W) ? 1.f : 0.f;
	Input.brake = Down(EKeys::Down, EKeys::S) ? 1.f : 0.f;
	// Turning the key: Enter, or simply the throttle.
	Input.ignition = (Down(EKeys::Enter, EKeys::SpaceBar) || Input.throttle > 0.f) ? 1 : 0;
	return Input;
}

void AGlpCarDriver::Tick(float DeltaSeconds)
{
	Super::Tick(DeltaSeconds);
	const FGlpLibrary* Library = FGlpLibrary::Get();
	if (!Session || !Library || !Body)
	{
		return;
	}
	if (!bViewSet)
	{
		// After every BeginPlay: a camera actor that activates itself for the player would otherwise win.
		if (APlayerController* Player = GetWorld()->GetFirstPlayerController())
		{
			Player->SetViewTarget(this);
			bViewSet = true;
		}
	}

	glp_inputs Input{};
	if (bAttract)
	{
		Library->ScriptedInputs(Session, &Input);
	}
	else
	{
		Input = ReadKeyboard(DeltaSeconds);
	}
	if (Library->Step(Session, &Input, DeltaSeconds) != 0)
	{
		UE_LOG(LogGlpCar, Error, TEXT("step failed: %s"), *Library->LastError());
		return;
	}
	glp_state State;
	Library->GetState(Session, &State);
	ApplyState(State, DeltaSeconds);
	Automate(State, DeltaSeconds);
}

void AGlpCarDriver::ApplyState(const glp_state& State, float DeltaSeconds)
{
	// The orientation is a simulation-space quaternion; it only ever rotates simulation vectors here, and the car's
	// axes are mapped across. The imported car mesh has its right along local +X, its roof along local +Z and its
	// nose along local -Y, so those are the columns its rotation needs.
	const FQuat Orientation(State.orientation[0], State.orientation[1], State.orientation[2], State.orientation[3]);
	const FVector Right = ToUnreal(Orientation.RotateVector(FVector::XAxisVector), 1.f);
	const FVector Up = ToUnreal(Orientation.RotateVector(FVector::YAxisVector), 1.f);
	const FVector Forward = ToUnreal(Orientation.RotateVector(FVector::ZAxisVector), 1.f);
	const FVector Location = ToUnreal(FVector(State.position[0], State.position[1], State.position[2]));
	const FQuat Rotation = FMatrix(Right, -Forward, Up, FVector::ZeroVector).ToQuat();
	Body->SetActorLocationAndRotation(Location, Rotation, false, nullptr, ETeleportType::TeleportPhysics);

	for (int32 i = 0; i < 4; i++)
	{
		if (Wheels[i])
		{
			// The hub hangs below its rest plane by the unused suspension travel (CarView.SetWheels); the front
			// wheels steer about the vertical. Godot's turns about its Y and X axes come out with the opposite sign
			// once the import has swapped two axes.
			FVector Hub = WheelRest[i].GetLocation();
			Hub.Z = (State.hub_height - (State.suspension_rest - State.wheel_compression[i])) * 100.f;
			const FQuat Steering = i < 2 ? FQuat(FVector::ZAxisVector, State.steer_angle) : FQuat::Identity;
			Wheels[i]->GetRootComponent()->SetRelativeLocationAndRotation(Hub, Steering * WheelRest[i].GetRotation());
		}
		if (Spinners[i])
		{
			Spinners[i]->GetRootComponent()->SetRelativeRotation(FQuat(FVector::XAxisVector, State.wheel_spin[i]) * SpinnerRest[i].GetRotation());
		}
	}

	const FTransform CarTransform(Rotation, Location);
	if (bChaseView)
	{
		const FVector Target = CarTransform.TransformPosition(ChaseOffset);
		ChaseEye = bChaseEyeSet ? FMath::Lerp(ChaseEye, Target, FMath::Clamp(DeltaSeconds * 6.f, 0.f, 1.f)) : Target;
		bChaseEyeSet = true;
		const FVector Focus = CarTransform.TransformPosition(ChaseFocus);
		Camera->SetWorldLocationAndRotation(ChaseEye, FRotationMatrix::MakeFromXZ(Focus - ChaseEye, FVector::UpVector).ToQuat());
	}
	else
	{
		// Along the nose, 2.9 degrees down: the rig looks at a point 0.7 m below the eye 14 m ahead.
		const FQuat Look = FRotator(-2.9f, -90.f, 0.f).Quaternion();
		Camera->SetWorldLocationAndRotation(CarTransform.TransformPosition(CockpitEye), Rotation * Look);
	}
}

void AGlpCarDriver::Automate(const glp_state& State, float DeltaSeconds)
{
	if (State.phase == GLP_PHASE_DRIVING || DriveTime > 0.f)
	{
		DriveTime += DeltaSeconds;
	}
	LogTimer += DeltaSeconds;
	if (LogTimer >= 1.f)
	{
		LogTimer = 0.f;
		UE_LOG(LogGlpCar, Log, TEXT("t %.1f s, phase %d, %.0f km/h, gear %d, %.0f rpm, at (%.1f, %.1f, %.1f), lap %d, %s"),
			DriveTime, State.phase, State.speed * 3.6f, State.gear, State.rpm, State.position[0], State.position[1], State.position[2], State.lap,
			State.on_road ? TEXT("on road") : TEXT("off road"));
	}
	if (NextShot < ShotTimes.Num() && DriveTime >= ShotTimes[NextShot])
	{
		const FString Directory = FPaths::Combine(FPaths::ProjectSavedDir(), TEXT("Screenshots"), TEXT("Glp"));
		IFileManager::Get().MakeDirectory(*Directory, true);
		const FString File = FPaths::Combine(Directory, FString::Printf(TEXT("%s_%02d.png"), bChaseView ? TEXT("chase") : TEXT("cockpit"), FMath::RoundToInt(ShotTimes[NextShot])));
		FScreenshotRequest::RequestScreenshot(File, false, false);
		UE_LOG(LogGlpCar, Log, TEXT("screenshot requested: %s"), *File);
		NextShot++;
	}
	if (QuitAfter > 0.f && DriveTime >= QuitAfter)
	{
		UE_LOG(LogGlpCar, Log, TEXT("quitting after %.1f s of driving"), DriveTime);
		FPlatformMisc::RequestExit(false);
		QuitAfter = 0.f;
	}
}

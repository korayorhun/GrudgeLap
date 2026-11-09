#include "GlpCarView.h"

#include "Components/SpotLightComponent.h"
#include "Components/StaticMeshComponent.h"
#include "Engine/World.h"
#include "EngineUtils.h"
#include "GameFramework/Actor.h"
#include "GlpSpace.h"
#include "Materials/MaterialInstanceDynamic.h"

namespace
{
	// Turns of the steering wheel per turn of the road wheels (CarView.SteeringRatio).
	constexpr float SteeringRatio = 12.f;

	// The lamp sockets' tags (Scripts/build_scene.py rig_car), in FGlpCarView::LampRest's order.
	const TCHAR* LampTags[] = { TEXT("GLP.LampHeadL"), TEXT("GLP.LampHeadR"), TEXT("GLP.LampTailL"), TEXT("GLP.LampTailR") };

	// The tail lamps (0.77): the Godot version's TailRed, sRGB (1, 0.06, 0.03), linear; a 6 cd tail lamp through a 5 cm
	// lens, six times that braking (ECE R7: a stop lamp at least five times the tail lamp).
	const FLinearColor TailRed(1.f, 0.005f, 0.0023f);
	constexpr float TailCandela = 6.f;
	constexpr float BrakeCandela = 36.f;
	constexpr float TailLensCm = 5.f;
	constexpr float HeadLensCm = 7.f;

	bool HasTag(const AActor* Actor, const FString& Tag)
	{
		for (const FName& Name : Actor->Tags)
		{
			if (Name.ToString() == Tag)
			{
				return true;
			}
		}
		return false;
	}

	/** The actor or the nearest of its ancestors that satisfies Test, up to (not past) Stop. */
	const AActor* Ancestor(const AActor* Actor, const AActor* Stop, TFunctionRef<bool(const AActor*)> Test)
	{
		for (const AActor* A = Actor; A && A != Stop; A = A->GetAttachParentActor())
		{
			if (Test(A))
			{
				return A;
			}
		}
		return nullptr;
	}
}

TUniquePtr<FGlpCarView> FGlpCarView::FromLevel(UWorld* World, FName RootTag)
{
	AActor* Root = nullptr;
	for (TActorIterator<AActor> It(World); It && !Root; ++It)
	{
		if (It->ActorHasTag(RootTag))
		{
			Root = *It;
		}
	}
	if (!Root)
	{
		return nullptr;
	}
	TUniquePtr<FGlpCarView> View = MakeUnique<FGlpCarView>();
	View->RootActor = Root;
	View->Wheels.SetNum(4);
	View->Spinners.SetNum(4);
	View->WheelRest.SetNum(4);
	View->SpinnerRest.SetNum(4);
	View->Owned.Add(Root);
	TArray<AActor*> Parts;
	Root->GetAttachedActors(Parts, true, true);
	for (AActor* Part : Parts)
	{
		View->Owned.Add(Part);
		for (int32 i = 0; i < 4; i++)
		{
			if (HasTag(Part, FString::Printf(TEXT("GLP.Wheel%d"), i)))
			{
				View->Wheels[i] = Part->GetRootComponent();
				View->WheelRest[i] = Part->GetRootComponent()->GetRelativeTransform();
			}
			if (HasTag(Part, FString::Printf(TEXT("GLP.Spin%d"), i)))
			{
				View->Spinners[i] = Part->GetRootComponent();
				View->SpinnerRest[i] = Part->GetRootComponent()->GetRelativeTransform();
			}
		}
		if (HasTag(Part, TEXT("GLP.Steering")))
		{
			View->Steering = Part->GetRootComponent();
			View->SteeringRest = Part->GetRootComponent()->GetRelativeTransform();
		}
		TArray<UStaticMeshComponent*> PartMeshes;
		Part->GetComponents<UStaticMeshComponent>(PartMeshes);
		for (UStaticMeshComponent* Mesh : PartMeshes)
		{
			View->TakeBrakeLamps(Mesh);
		}
	}
	View->MeasureLamps();
	for (const AActor* Part : Parts)
	{
		const bool bOutside = Ancestor(Part, Root, [](const AActor* A)
		{
			if (HasTag(A, TEXT("GLP.OutsideOnly")))
			{
				return true;
			}
			for (int32 i = 0; i < 4; i++)
			{
				if (HasTag(A, FString::Printf(TEXT("GLP.Wheel%d"), i)))
				{
					return true;
				}
			}
			return false;
		}) != nullptr;
		if (bOutside)
		{
			TArray<UStaticMeshComponent*> Meshes;
			Part->GetComponents<UStaticMeshComponent>(Meshes);
			View->OutsideOnly.Append(Meshes);
		}
	}
	return View;
}

TUniquePtr<FGlpCarView> FGlpCarView::Clone(UWorld* World, const FString& Name, const FLinearColor* Paint, UMaterialInterface* Override) const
{
	const AActor* Source = RootActor.Get();
	if (!Source)
	{
		return nullptr;
	}
	FActorSpawnParameters Params;
	Params.Name = MakeUniqueObjectName(World->GetCurrentLevel(), AActor::StaticClass(), FName(*Name));
	AActor* Copy = World->SpawnActor<AActor>(AActor::StaticClass(), FTransform::Identity, Params);
	USceneComponent* CopyRoot = NewObject<USceneComponent>(Copy, TEXT("Body"));
	CopyRoot->SetMobility(EComponentMobility::Movable);
	Copy->SetRootComponent(CopyRoot);
	CopyRoot->RegisterComponent();

	TUniquePtr<FGlpCarView> View = MakeUnique<FGlpCarView>();
	View->RootActor = Copy;
	View->Owned.Add(Copy);
	View->WheelRest = WheelRest;
	View->SpinnerRest = SpinnerRest;
	// The lamps' places as the level's car measured them (the copy's frame is its root's).
	for (int32 i = 0; i < 4; i++)
	{
		View->LampRest[i] = LampRest[i];
	}
	View->Body = Body;
	View->Ground = Ground;
	View->Wheels.SetNum(4);
	View->Spinners.SetNum(4);
	auto Frame = [Copy](const FString& FrameName, USceneComponent* Parent, const FTransform& Relative)
	{
		USceneComponent* Component = NewObject<USceneComponent>(Copy, FName(*FrameName));
		Component->SetMobility(EComponentMobility::Movable);
		Component->SetupAttachment(Parent);
		Component->SetRelativeTransform(Relative);
		Component->RegisterComponent();
		return Component;
	};
	for (int32 i = 0; i < 4; i++)
	{
		View->Wheels[i] = Frame(FString::Printf(TEXT("Wheel%d"), i), CopyRoot, WheelRest[i]);
		View->Spinners[i] = Frame(FString::Printf(TEXT("Spin%d"), i), View->Wheels[i].Get(), SpinnerRest[i]);
	}

	// The copy keeps the exterior and the wheels; the cabin is not seen through traffic or the ghost.
	const FTransform SourceRoot = Source->GetActorTransform();
	TArray<AActor*> Parts;
	Source->GetAttachedActors(Parts, true, true);
	int32 Count = 0;
	for (const AActor* Part : Parts)
	{
		int32 WheelIndex = INDEX_NONE;
		int32 SpinIndex = INDEX_NONE;
		for (int32 i = 0; i < 4; i++)
		{
			if (Ancestor(Part, Source, [i](const AActor* A) { return HasTag(A, FString::Printf(TEXT("GLP.Spin%d"), i)); }))
			{
				SpinIndex = i;
			}
			else if (Ancestor(Part, Source, [i](const AActor* A) { return HasTag(A, FString::Printf(TEXT("GLP.Wheel%d"), i)); }))
			{
				WheelIndex = i;
			}
		}
		const bool bExterior = Ancestor(Part, Source, [](const AActor* A) { return HasTag(A, TEXT("GLP.Exterior")); }) != nullptr;
		if (SpinIndex == INDEX_NONE && WheelIndex == INDEX_NONE && !bExterior)
		{
			continue;
		}
		USceneComponent* Parent = SpinIndex != INDEX_NONE ? View->Spinners[SpinIndex].Get() : WheelIndex != INDEX_NONE ? View->Wheels[WheelIndex].Get() : CopyRoot;
		// Relative to the frame it moves with, measured in the imported car's rest pose.
		const FTransform ParentRest = SpinIndex != INDEX_NONE
			? SpinnerRest[SpinIndex] * WheelRest[SpinIndex] * SourceRoot
			: WheelIndex != INDEX_NONE ? WheelRest[WheelIndex] * SourceRoot : SourceRoot;
		TArray<UStaticMeshComponent*> Meshes;
		Part->GetComponents<UStaticMeshComponent>(Meshes);
		for (const UStaticMeshComponent* Mesh : Meshes)
		{
			UStaticMeshComponent* CopyMesh = NewObject<UStaticMeshComponent>(Copy);
			CopyMesh->SetMobility(EComponentMobility::Movable);
			CopyMesh->SetStaticMesh(Mesh->GetStaticMesh());
			CopyMesh->SetupAttachment(Parent);
			CopyMesh->SetRelativeTransform(Mesh->GetComponentTransform().GetRelativeTransform(ParentRest));
			CopyMesh->SetCollisionEnabled(ECollisionEnabled::NoCollision);
			for (int32 Slot = 0; Slot < Mesh->GetNumMaterials(); Slot++)
			{
				UMaterialInterface* Material = Mesh->GetMaterial(Slot);
				if (Override)
				{
					Material = Override;
				}
				else if (FLinearColor Current; Paint && Material && Material->GetVectorParameterValue(FHashedMaterialParameterInfo(TEXT("Paint")), Current))
				{
					UMaterialInstanceDynamic* Painted = UMaterialInstanceDynamic::Create(Material, CopyMesh);
					Painted->SetVectorParameterValue(TEXT("Paint"), *Paint);
					Material = Painted;
				}
				CopyMesh->SetMaterial(Slot, Material);
			}
			CopyMesh->SetCastShadow(Override == nullptr);
			if (!Override)
			{
				View->TakeBrakeLamps(CopyMesh);
			}
			CopyMesh->RegisterComponent();
			Count++;
		}
	}
	UE_LOG(LogTemp, Log, TEXT("car copy %s: %d meshes"), *Name, Count);
	return View;
}

void FGlpCarView::TakeBrakeLamps(UStaticMeshComponent* Mesh)
{
	for (int32 Slot = 0; Slot < Mesh->GetNumMaterials(); Slot++)
	{
		UMaterialInterface* Material = Mesh->GetMaterial(Slot);
		float Glow = 0.f;
		if (!Material || !Material->GetScalarParameterValue(FHashedMaterialParameterInfo(TEXT("Glow")), Glow))
		{
			continue;
		}
		// A lens that never lights (0.77): a front marker the model made of its brake lamps' material (the V8's, the
		// five's front indicators), which Scripts/prepare_model_car.py brake_parts sets apart.
		float Stop = 1.f;
		float Tail = 1.f;
		Material->GetScalarParameterValue(FHashedMaterialParameterInfo(TEXT("StopLamp")), Stop);
		Material->GetScalarParameterValue(FHashedMaterialParameterInfo(TEXT("TailLamp")), Tail);
		if (Stop <= 0.f && Tail <= 0.f)
		{
			continue;
		}
		// A copy's slot may hold the source's dynamic instance: each car gets its own, so one car braking lights no other.
		UMaterialInterface* Parent = Material;
		if (const UMaterialInstanceDynamic* Dynamic = Cast<UMaterialInstanceDynamic>(Material))
		{
			Parent = Dynamic->Parent;
		}
		UMaterialInstanceDynamic* Lamp = UMaterialInstanceDynamic::Create(Parent, Mesh);
		Mesh->SetMaterial(Slot, Lamp);
		BrakeLamps.Add(Lamp);
	}
}

FVector FGlpCarView::WheelHub(int32 Index) const
{
	const USceneComponent* Wheel = Wheels.IsValidIndex(Index) ? Wheels[Index].Get() : nullptr;
	return Wheel ? Wheel->GetComponentLocation() : FVector::ZeroVector;
}

void FGlpCarView::SetBraking(float Glow)
{
	const bool bNow = Glow > 0.5f;
	if (FMath::IsNearlyEqual(Glow, BrakeGlow) && bNow == bBraking)
	{
		return;
	}
	BrakeGlow = Glow;
	bBraking = bNow;
	ApplyTail();
}

void FGlpCarView::ApplyTail()
{
	// The lenses in the screen's light units (M_BrakeLamp): about a third of the screen's white braking (Glow), times the
	// lens's own red, bright in daylight and not a blown-out disc; while the lamps are lit a tail lamp's glow (Tail, 0.77),
	// at any exposure. The material takes each by its part: a tail lamp both, a centre high-mounted stop lamp the brake
	// alone (the Clio's lit red all night as if it braked), and on a textured lens only its red.
	const float Brake = bBraking ? 3.f : 0.f;
	const float Tail = Lamps.bCards ? 0.6f : 0.f;
	for (const TWeakObjectPtr<UMaterialInstanceDynamic>& Lamp : BrakeLamps)
	{
		if (UMaterialInstanceDynamic* L = Lamp.Get())
		{
			L->SetScalarParameterValue(TEXT("Glow"), Brake);
			L->SetScalarParameterValue(TEXT("Tail"), Tail);
		}
	}
	const float Candela = bBraking ? BrakeCandela : TailCandela;
	for (const TWeakObjectPtr<UStaticMeshComponent>& Glow : TailGlows)
	{
		GlpLamps::SetGlowLuminance(Glow.Get(), TailRed, GlpLamps::DiscLuminance(Candela, TailLensCm));
	}
	if (USpotLightComponent* Pool = TailPool.Get())
	{
		// The tail lamps' red on the road behind, as the Godot version's TailGlow spot (0.66): 3 cd, four times that
		// braking. At 10 cd, the first value, the road behind came out a saturated red three times the Godot version's.
		Pool->SetIntensity(bBraking ? 12.f : 3.f);
	}
}

void FGlpCarView::MeasureLamps()
{
	// The body's box and the ground under the wheels in the root's frame (exterior and wheels), and the lamp sockets
	// where the level has them; the Godot version's box rule (CarView.BuildLamps) for any it lacks.
	const AActor* Root = RootActor.Get();
	if (!Root)
	{
		return;
	}
	const FTransform RootTransform = Root->GetActorTransform();
	TArray<AActor*> Parts;
	Root->GetAttachedActors(Parts, true, true);
	bool bFound[4] = {};
	float Lowest = TNumericLimits<float>::Max();
	for (const AActor* Part : Parts)
	{
		for (int32 i = 0; i < 4; i++)
		{
			if (HasTag(Part, LampTags[i]))
			{
				LampRest[i] = RootTransform.InverseTransformPosition(Part->GetActorLocation());
				bFound[i] = true;
			}
		}
		const bool bExterior = Ancestor(Part, Root, [](const AActor* A) { return HasTag(A, TEXT("GLP.Exterior")); }) != nullptr;
		const bool bWheel = Ancestor(Part, Root, [](const AActor* A)
		{
			for (int32 i = 0; i < 4; i++)
			{
				if (HasTag(A, FString::Printf(TEXT("GLP.Wheel%d"), i)))
				{
					return true;
				}
			}
			return false;
		}) != nullptr;
		if (!bExterior && !bWheel)
		{
			continue;
		}
		TArray<UStaticMeshComponent*> Meshes;
		Part->GetComponents<UStaticMeshComponent>(Meshes);
		for (UStaticMeshComponent* Mesh : Meshes)
		{
			if (!Mesh->GetStaticMesh())
			{
				continue;
			}
			const FBox Box = Mesh->CalcBounds(Mesh->GetComponentTransform().GetRelativeTransform(RootTransform)).GetBox();
			if (bExterior)
			{
				Body += Box;
			}
			else
			{
				Lowest = FMath::Min(Lowest, static_cast<float>(Box.Min.Z));
			}
		}
	}
	Ground = Lowest < TNumericLimits<float>::Max() ? Lowest : Body.IsValid ? static_cast<float>(Body.Min.Z) : -60.f;
	if (!Body.IsValid)
	{
		Body = FBox(FVector(-85.0, -220.0, Ground), FVector(85.0, 220.0, Ground + 140.0));
	}
	// The rule in the root's frame: a quarter of a metre in from the sides, 0.65 m up 2 cm ahead of the nose, 0.82 m up
	// 5 cm in from the tail, the tails a little wider.
	const double Side = FMath::Max(Body.GetExtent().X - 25.0, 30.0);
	const double Middle = Body.GetCenter().X;
	const FVector Rule[4] = { FVector(Middle - Side, Body.Min.Y - 2.0, Ground + 65.0), FVector(Middle + Side, Body.Min.Y - 2.0, Ground + 65.0),
		FVector(Middle - Side * 1.05, Body.Max.Y - 5.0, Ground + 82.0), FVector(Middle + Side * 1.05, Body.Max.Y - 5.0, Ground + 82.0) };
	for (int32 i = 0; i < 4; i++)
	{
		if (!bFound[i])
		{
			LampRest[i] = Rule[i];
		}
	}
}

void FGlpCarView::BuildLamps(const FGlpLampAssets& Assets, bool bPlayer, const FLinearColor& BeamColour)
{
	AActor* Root = RootActor.Get();
	USceneComponent* Frame = Root ? Root->GetRootComponent() : nullptr;
	if (!Frame)
	{
		return;
	}
	bLampsBuilt = true;
	// The nose is the root's -Y: a lamp facing ahead has its X there, its Y (right, the verge side of the low beam's
	// profile) along the root's +X and its Z up; a tail lamp faces +Y.
	const FQuat Ahead = FRotator(0.f, -90.f, 0.f).Quaternion();
	const FQuat Behind = FRotator(0.f, 90.f, 0.f).Quaternion();
	for (int32 i = 0; i < 2; i++)
	{
		// The glow 3 cm ahead of the lens, clear of the glass and the lamp's bowl.
		HeadGlows.Add(GlpLamps::Glow(Root, Frame, FTransform(Ahead, LampRest[i] + FVector(0.0, -3.0, 0.0)), Assets, BeamColour,
			GlpLamps::DiscLuminance(GlpLamps::LowBeamCandela, HeadLensCm), HeadLensCm, GlpLamps::EGlow::LowBeam));
	}
	for (int32 i = 2; i < 4; i++)
	{
		TailGlows.Add(GlpLamps::Glow(Root, Frame, FTransform(Behind, LampRest[i] + FVector(0.0, 3.0, 0.0)), Assets, TailRed,
			GlpLamps::DiscLuminance(TailCandela, TailLensCm), TailLensCm, GlpLamps::EGlow::Tail));
	}
	// The beams 8 cm ahead of the body's box, outside the bumper: no light on the bonnet and no shadow of the car's own
	// nose (the Godot version masks the car's layer out of its beams' shadows instead). Flat cones wide enough for the
	// profile: the IES pattern shapes the beam, and a cone falling off from the axis took 82 % off at 30 degrees.
	const double Front = Body.Min.Y - 8.0;
	if (bPlayer)
	{
		for (int32 i = 0; i < 2; i++)
		{
			// Not in the translucency lighting volume: its cells are a metre or two across, and a 20 kcd lamp in one lit the rain's
			// streaks and the wheels' spray white all round the car, behind it too. M_Rain lights each streak from these two
			// beams itself (MPC_Night, Scripts/build_scene.py RAIN_BEAMS_HLSL), and the night rain's volumetric fog shows them
			// in the air.
			USpotLightComponent* Beam = GlpLamps::Spot(Root, Frame, FTransform(Ahead, FVector(LampRest[i].X, Front, LampRest[i].Z)),
				GlpLamps::LowBeamCandela, BeamColour, 38.f, 42.f, 15000.f, true, Assets.LowBeam, false);
			if (Beam)
			{
				Beam->SetSourceRadius(6.f);
				Beam->SetVolumetricScatteringIntensity(1.5f);   // the Godot version's LightVolumetricFogEnergy
				// Through MegaLights the beams' shadows come from the virtual shadow maps, rasterised from the Nanite
				// geometry: traced against the ray tracing scene's coarser stand-in of the car's own nose, every shadow ray
				// past the screen traces' reach was blocked and the road went dark 7 m out.
				Beam->MegaLightsShadowMethod = EMegaLightsShadowMethod::VirtualShadowMap;
				Beam->MarkRenderStateDirty();
				Beams.Add(Beam);
			}
		}
		// The tail lamps' red on the road behind (the Godot version's TailGlow, 0.66): a spot from the rear centre 0.75 m up,
		// 25 cm behind the body, aimed back and 20 degrees down; an omni light lit the cabin red through the rear window.
		const FVector Pool(0.5 * (LampRest[2].X + LampRest[3].X), Body.Max.Y + 25.0, Ground + 75.0);
		// A soft-edged cone: from 30 degrees out it fades to its 70 degree rim, where a 60 degree inner cone drew a hard oval.
		TailPool = GlpLamps::Spot(Root, Frame, FTransform(FRotator(-20.f, 90.f, 0.f), Pool), 3.f, FLinearColor::FromSRGBColor(FColor(255, 20, 10)),
			30.f, 70.f, 600.f, false, nullptr);
		if (USpotLightComponent* Light = TailPool.Get())
		{
			Light->SetVolumetricScatteringIntensity(0.5f);
		}
	}
	else
	{
		// A drone's single beam between its lamps (the Godot version's Headlamps): both lamps' light, gone past 300 m, never
		// with shadows, under MegaLights too, as in Godot (traced against the ray tracing scene's stand-in of its own nose, it
		// lit only the first metres).
		const FVector At(0.5 * (LampRest[0].X + LampRest[1].X), Front, 0.5 * (LampRest[0].Z + LampRest[1].Z));
		if (USpotLightComponent* Beam = GlpLamps::Spot(Root, Frame, FTransform(Ahead, At), 2.f * GlpLamps::LowBeamCandela, BeamColour,
			38.f, 42.f, 12000.f, false, Assets.LowBeam, false))
		{
			Beam->SetSourceRadius(10.f);
			Beam->SetMaxDrawDistance(30000.f);
			Beam->SetMaxDistanceFadeRange(5000.f);
			Beams.Add(Beam);
		}
	}
	UE_LOG(LogTemp, Log, TEXT("lamps made on %s: %d beam(s), %d glows, the right head lamp at (%.0f, %.0f, %.0f) cm, the body from %.0f to %.0f cm"),
		*Root->GetName(), Beams.Num(), HeadGlows.Num() + TailGlows.Num(), LampRest[1].X, LampRest[1].Y, LampRest[1].Z, Body.Min.Y, Body.Max.Y);
}

void FGlpCarView::ShowLamps()
{
	const bool bBeams = bShown && Lamps.bBeams;
	const bool bCards = bShown && Lamps.bCards;
	for (const TWeakObjectPtr<USpotLightComponent>& Beam : Beams)
	{
		if (USpotLightComponent* Light = Beam.Get(); Light && Light->IsVisible() != bBeams)
		{
			Light->SetVisibility(bBeams);
		}
	}
	if (USpotLightComponent* Pool = TailPool.Get(); Pool && Pool->IsVisible() != bBeams)
	{
		Pool->SetVisibility(bBeams);
	}
	for (const TArray<TWeakObjectPtr<UStaticMeshComponent>>* Glows : { &HeadGlows, &TailGlows })
	{
		for (const TWeakObjectPtr<UStaticMeshComponent>& Glow : *Glows)
		{
			if (UStaticMeshComponent* Card = Glow.Get(); Card && Card->IsVisible() != bCards)
			{
				Card->SetVisibility(bCards);
			}
		}
	}
}

void FGlpCarView::SetLamps(const FGlpLampState& State, const FGlpLampAssets& Assets)
{
	bool bChanged = State.bCards != Lamps.bCards || State.bBeams != Lamps.bBeams;
	if ((State.bCards || State.bBeams) && !bLampsBuilt)
	{
		// Made whole, then each part shown or not below (the beams stay dark under a lit rain's glows by day).
		BuildLamps(Assets, State.bPlayer, State.BeamColour);
		bChanged = true;
	}
	Lamps = State;
	if (bChanged)
	{
		ShowLamps();
		ApplyTail();
	}
}

const USpotLightComponent* FGlpCarView::BeamLight(int32 Index) const
{
	if (!bShown || !Lamps.bBeams || !Lamps.bPlayer || !Beams.IsValidIndex(Index))
	{
		return nullptr;
	}
	return Beams[Index].Get();
}

void FGlpCarView::SetPose(const float Position[3], const float Orientation[4])
{
	if (AActor* Root = RootActor.Get())
	{
		Root->SetActorLocationAndRotation(GlpSpace::SimToUnreal(Position), GlpSpace::SimRotation(Orientation), false, nullptr, ETeleportType::TeleportPhysics);
	}
}

void FGlpCarView::SetWheels(const glp_state& State)
{
	for (int32 i = 0; i < 4; i++)
	{
		if (USceneComponent* Wheel = Wheels[i].Get())
		{
			// The hub hangs below its rest plane by the unused suspension travel (CarView.SetWheels); the front
			// wheels steer about the vertical. Godot's turns about its Y and X axes come out with the opposite sign
			// once the import has swapped two axes.
			FVector Hub = WheelRest[i].GetLocation();
			Hub.Z = (State.hub_height - (State.suspension_rest - State.wheel_compression[i])) * 100.f;
			const FQuat Steer = i < 2 ? FQuat(FVector::ZAxisVector, State.steer_angle) : FQuat::Identity;
			Wheel->SetRelativeLocationAndRotation(Hub, Steer * WheelRest[i].GetRotation());
		}
		if (USceneComponent* Spinner = Spinners[i].Get())
		{
			Spinner->SetRelativeRotation(FQuat(FVector::XAxisVector, State.wheel_spin[i]) * SpinnerRest[i].GetRotation());
		}
	}
	TurnSteeringWheel(State.steer_angle);
}

void FGlpCarView::AdvanceWheels(float Distance, float SteerAngle, float WheelRadius)
{
	RollAngle += Distance / FMath::Max(WheelRadius, 0.05f);
	for (int32 i = 0; i < 4; i++)
	{
		if (USceneComponent* Spinner = Spinners[i].Get())
		{
			Spinner->SetRelativeRotation(FQuat(FVector::XAxisVector, RollAngle) * SpinnerRest[i].GetRotation());
		}
		if (USceneComponent* Wheel = Wheels[i].Get())
		{
			const FQuat Steer = i < 2 ? FQuat(FVector::ZAxisVector, SteerAngle) : FQuat::Identity;
			Wheel->SetRelativeRotation(Steer * WheelRest[i].GetRotation());
		}
	}
}

void FGlpCarView::TurnSteeringWheel(float SteerAngle)
{
	// About the column, clockwise for the driver on a right turn (CarView.TurnSteeringWheel); the sign flips
	// with the import's axis swap.
	if (USceneComponent* Wheel = Steering.Get())
	{
		Wheel->SetRelativeRotation(FQuat(FVector::ZAxisVector, SteerAngle * SteeringRatio) * SteeringRest.GetRotation());
	}
}

void FGlpCarView::SetVisible(bool bVisible)
{
	for (const TWeakObjectPtr<AActor>& Actor : Owned)
	{
		if (AActor* A = Actor.Get())
		{
			A->SetActorHiddenInGame(!bVisible);
		}
	}
	// A hidden car's lamps go out with it (0.77): a light is told so itself.
	bShown = bVisible;
	ShowLamps();
}

void FGlpCarView::SetInside(bool bInside)
{
	for (const TWeakObjectPtr<UPrimitiveComponent>& Component : OutsideOnly)
	{
		// Shadows still come from the main-pass-less proxy: Nanite meshes fall back to their fallback mesh for it.
		if (UPrimitiveComponent* Primitive = Component.Get(); Primitive && Primitive->bRenderInMainPass == bInside)
		{
			Primitive->SetRenderInMainPass(!bInside);
		}
	}
}

AActor* FGlpCarView::Find(const TCHAR* Tag) const
{
	for (const TWeakObjectPtr<AActor>& Actor : Owned)
	{
		if (AActor* A = Actor.Get(); A && HasTag(A, Tag))
		{
			return A;
		}
	}
	return nullptr;
}

TArray<AActor*> FGlpCarView::FindAll(const TCHAR* Tag) const
{
	TArray<AActor*> Found;
	for (const TWeakObjectPtr<AActor>& Actor : Owned)
	{
		if (AActor* A = Actor.Get(); A && HasTag(A, Tag))
		{
			Found.Add(A);
		}
	}
	return Found;
}

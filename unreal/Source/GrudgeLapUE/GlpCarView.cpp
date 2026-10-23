#include "GlpCarView.h"

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
	}
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

TUniquePtr<FGlpCarView> FGlpCarView::Clone(UWorld* World, const FString& Name, UMaterialInterface* PaintMaterial, const FLinearColor* Paint, UMaterialInterface* Override) const
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
				else if (Paint && PaintMaterial && Material && Material->GetBaseMaterial() == PaintMaterial->GetBaseMaterial())
				{
					UMaterialInstanceDynamic* Painted = UMaterialInstanceDynamic::Create(PaintMaterial, CopyMesh);
					Painted->SetVectorParameterValue(TEXT("Paint"), *Paint);
					Material = Painted;
				}
				CopyMesh->SetMaterial(Slot, Material);
			}
			CopyMesh->SetCastShadow(Override == nullptr);
			CopyMesh->RegisterComponent();
			Count++;
		}
	}
	UE_LOG(LogTemp, Log, TEXT("car copy %s: %d meshes"), *Name, Count);
	return View;
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

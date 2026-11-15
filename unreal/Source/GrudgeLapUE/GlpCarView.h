#pragma once

#include "CoreMinimal.h"
#include "GlpLamps.h"
#include "grudgelap_physics.h"

class AActor;
class UActorComponent;
struct FGlpCockpit;
struct FGlpPaint;
class UMaterialInterface;
class UPrimitiveComponent;
class USceneComponent;
class USpotLightComponent;
class UStaticMeshComponent;
class UWorld;

/** What a car's lamps show (0.77, FGlpCarView::SetLamps). */
struct FGlpLampState
{
	/** The glows and the tail lamps (dusk to dawn, and in the rain: the Godot version's CarLampsLit). */
	bool bCards = false;
	/** The low beams' light (dusk to dawn): by day in the rain it would not show, and MegaLights stays off by day. */
	bool bBeams = false;
	/**
	 * The player's car: two shadowed beams and the tail lamps' red on the road; a drone one beam between its lamps and
	 * without shadows, under MegaLights too (FGlpCarView::BuildLamps).
	 */
	bool bPlayer = false;
	/** The beams' linear colour by the car's era: halogen, xenon, LED. */
	FLinearColor BeamColour = FLinearColor::White;
};

/**
 * A car on screen: its body, the four wheel pivots (the suspension and the steering), the parts that roll, and
 * the steering wheel in the cabin. The player's car is the imported scene's own actors, found by the tags
 * Scripts/build_scene.py sets (GLP.Car, GLP.Wheel0..3, GLP.Spin0..3, GLP.Steering, GLP.Exterior, GLP.OutsideOnly). Traffic and the
 * ghost are copies of its exterior built into one actor each (Clone): the same meshes, another paint or the
 * ghost's see-through material, and no cabin.
 */
class FGlpCarView
{
public:
	/** The imported car tagged RootTag, or null when the level has none. */
	static TUniquePtr<FGlpCarView> FromLevel(UWorld* World, FName RootTag);

	/**
	 * A copy of this car's exterior and wheels as a single new actor. Paint replaces the colour of every material
	 * with a "Paint" parameter (the body's M_CarPaint, the strakes' and trims' M_CarPaintSolid); Override, when
	 * given, replaces every material. A copy starts from the level's paint, not from one the player chose for this
	 * car (Repaint, 0.79): the traffic's Ferraris stay red whatever the player's wears.
	 */
	TUniquePtr<FGlpCarView> Clone(UWorld* World, const FString& Name, const FLinearColor* Paint, UMaterialInterface* Override) const;

	/**
	 * The car in a paint (0.79, the showroom's), or with null back in its own: every slot that wears the level's car
	 * paint (MI_Paint_<car>_<n>, an instance of M_CarPaintSolid or M_CarPaintTextured) gets an instance of this car's
	 * own on top of it with the paint's colour, base coat and pearl. The level's instance stays under it, so its cabin's
	 * lining and its texture are kept; a paint in a texture is drawn in the new colour through its mask.
	 */
	void Repaint(const FGlpPaint* Paint);

	/**
	 * The car's own colour, sRGB, for the showroom's ORIGINAL swatch: its first paint's colour, or for a paint in a
	 * texture the key its mask was made by (the level's Own parameter). False for a car without a paint slot.
	 */
	bool OwnColour(FLinearColor& OutSrgb) const;

	/**
	 * The wheels as the car stands on its own weight: the showroom shows cars that no session moves. The hubs hang
	 * where the springs settle (the car's centre of gravity cg_height over the road), unsteered.
	 */
	void SetWheelsAtRest(const glp_car_spec& Spec);

	/** The body's box in the root's frame (cm: x right, the nose at -y, z up) and the ground under the wheels there. */
	const FBox& BodyBox() const { return Body; }
	float GroundZ() const { return Ground; }

	/** Places the body: the simulation pose of its centre of gravity. */
	void SetPose(const float Position[3], const float Orientation[4]);

	/** The player's wheels from the simulation: suspension travel, steering, rolling. */
	void SetWheels(const glp_state& State);

	/** Wheels of a car the simulation only moves as a whole (traffic, the ghost): they roll by the distance it went. */
	void AdvanceWheels(float Distance, float SteerAngle, float WheelRadius);

	/** The steering wheel in the cabin follows the road wheels at the game's ratio. */
	void TurnSteeringWheel(float SteerAngle);

	void SetVisible(bool bVisible);

	/**
	 * The brake lamps' glow (0 off, 1 lit): every material of this car with a Glow parameter (M_BrakeLamp, 0.76), and at
	 * night the tail lamps' glows and the red on the road behind (0.77). Each lens lights as its part of the car says
	 * (M_BrakeLamp's StopLamp and TailLamp, 0.77): a centre stop lamp with the brake only, a front marker cut from the same
	 * material never.
	 */
	void SetBraking(float Glow);

	/**
	 * The lamps (0.77), as the Godot version's CarView.Headlights: made the first time they are lit, at the lamp sockets
	 * Scripts/prepare_model_car.py measured (tags GLP.LampHeadL, GLP.LampHeadR, GLP.LampTailL, GLP.LampTailR; the Godot
	 * version's box rule where a level lacks them). Only a change touches the components.
	 */
	void SetLamps(const FGlpLampState& State, const FGlpLampAssets& Assets);

	/** The player's beam I (0 left, 1 right) while lit, for the retroreflectors (MPC_Night); null otherwise. */
	const USpotLightComponent* BeamLight(int32 Index) const;

	/**
	 * From the driver's seat the body shell, the outside details and the wheels are not drawn, only their shadows
	 * (CarView.SetViewpoint): seen from inside, the single-sided shell showed its paint along the door tops. With
	 * InsideGlass (0.78) the car's windows wear it from the seat and their own glass from outside: the level's glass,
	 * right for a car seen from the road, took half the road's light on its way in through a raked windscreen and laid
	 * the dashboard's reflection over what was left (AGlpGame::InsideGlassMaterial). Leaving the seat takes the glass's
	 * marks off (MarkGlass).
	 */
	void SetInside(bool bInside, UMaterialInterface* InsideGlass = nullptr);

	/**
	 * Tells the glass's picture where this car's glass is, seen from its seat (0.80, M_Windscreen: the rain's drops and
	 * the wipers): with Pane (AGlpGame::GlassPaneMaterial) everything of the car the seat sees writes the custom depth
	 * with stencil 1, and a second, unseen copy of each mesh that carries a window writes its panes alone with stencil
	 * 2, in Pane (its other slots keep a translucent glass, which that pass leaves out). Where the pass holds a 2 the
	 * nearest thing of the car is a pane: the view goes through glass there, and the pass's depth says how far off the
	 * pane is. The game has that pass drawn without the upscaler's jitter (AGlpGame::BeginPlay), so the glass's edge
	 * stands still. Without Pane the marks are taken off: the game asks for them only while the glass has something to
	 * show (AGlpGame::SyncWindscreen), since the pass draws the cabin a second time. Call it after SetInside: what the
	 * seat sees is what is drawn in the main pass then.
	 *
	 * A part that Nanite draws (the steering wheel, the mirror's housing) is not marked itself: one Nanite part in that
	 * pass has the engine run a second Nanite rasteriser for it, 0.30 ms a frame on the laptop this was measured on,
	 * where the pass without any costs 0.01 ms. An unseen copy of it, drawn without Nanite (its fallback mesh), stands
	 * in for it.
	 */
	void MarkGlass(UMaterialInterface* Pane);

	/**
	 * True for a component MarkGlass made: an unseen copy of one of the car's meshes, which hangs on the car's own actor
	 * beside the mesh it copies. Whoever walks a car's meshes for what the car is made of (a copy of the car for the
	 * traffic or the ghost, the dials' or a mirror's material) leaves these out.
	 */
	static bool IsMark(const UActorComponent* Component);

	/** The driver's seat as the game corrects it (0.78): the dials' face and the steering wheel moved and scaled. */
	void SetCockpit(const FGlpCockpit& Cockpit);

	AActor* Root() const { return RootActor.Get(); }

	/** A wheel's hub (its pivot) in the world, for the tyre marks (0.76). */
	FVector WheelHub(int32 Index) const;

	/** The actor of this car tagged Tag (the binnacle's dials, the mirror glass), or null. */
	AActor* Find(const TCHAR* Tag) const;

	/** Every actor of this car tagged Tag (the outside mirrors). */
	TArray<AActor*> FindAll(const TCHAR* Tag) const;

private:
	TWeakObjectPtr<AActor> RootActor;
	TArray<TWeakObjectPtr<USceneComponent>> Wheels;
	TArray<TWeakObjectPtr<USceneComponent>> Spinners;
	TWeakObjectPtr<USceneComponent> Steering;
	TWeakObjectPtr<USceneComponent> Dials;
	TArray<FTransform> WheelRest;
	TArray<FTransform> SpinnerRest;
	FTransform SteeringRest;
	FTransform DialsRest;
	/** A window or a lens of the car: the slot that wears the level's glass (M_Glass), and that glass. */
	struct FGlassSlot
	{
		TWeakObjectPtr<UStaticMeshComponent> Mesh;
		int32 Slot = 0;
		TWeakObjectPtr<UMaterialInterface> Outside;
	};
	TArray<FGlassSlot> Glass;
	bool bInsideGlass = false;
	/** The unseen copies that write the panes into the custom depth (MarkGlass), made the first time they are wanted. */
	TArray<TWeakObjectPtr<UStaticMeshComponent>> Panes;
	/** The unseen copies that stand in that pass for the parts Nanite draws, by the part (MarkGlass). */
	TMap<TWeakObjectPtr<UStaticMeshComponent>, TWeakObjectPtr<UStaticMeshComponent>> Stands;
	bool bPanesMade = false;
	bool bGlassMarked = false;
	/** What the custom depth's stencil holds from the seat: the car's own parts, and its panes before them. */
	static constexpr int32 CabinStencil = 1;
	static constexpr int32 PaneStencil = 2;
	/** A slot that wears the car's paint: the level's material, and this car's own instance of it while it is repainted. */
	struct FPaintSlot
	{
		TWeakObjectPtr<UStaticMeshComponent> Mesh;
		int32 Slot = 0;
		TWeakObjectPtr<UMaterialInterface> Factory;
		TWeakObjectPtr<class UMaterialInstanceDynamic> Painted;
	};
	TArray<FPaintSlot> PaintSlots;
	/** The level's material of a slot that wears paint, whatever the player has chosen for it; null for any other slot. */
	UMaterialInterface* FactoryPaint(const UStaticMeshComponent* Mesh, int32 Slot) const;
	TArray<TWeakObjectPtr<AActor>> Owned;   // actors whose visibility SetVisible switches
	TArray<TWeakObjectPtr<UPrimitiveComponent>> OutsideOnly;   // drawn only from outside the car (SetInside)
	TArray<TWeakObjectPtr<class UMaterialInstanceDynamic>> BrakeLamps;   // the lamps SetBraking lights
	float BrakeGlow = -1.f;
	/** Gives a mesh's brake-lamp slots dynamic instances of their own and keeps them in BrakeLamps. */
	void TakeBrakeLamps(class UStaticMeshComponent* Mesh);
	float RollAngle = 0.f;

	// The lamps (0.77). Their places in the root's frame (cm: x right, y back, the nose at -y, z up), the body's box there
	// and the ground under the wheels, measured once on the level's car and handed to its copies.
	FVector LampRest[4] = {};   // head left, head right, tail left, tail right
	FBox Body = FBox(ForceInit);
	float Ground = -60.f;
	void MeasureLamps();
	void BuildLamps(const FGlpLampAssets& Assets, bool bPlayer, const FLinearColor& BeamColour);
	void ShowLamps();
	void ApplyTail();
	TArray<TWeakObjectPtr<USpotLightComponent>> Beams;      // the player's two, or a drone's one
	TWeakObjectPtr<USpotLightComponent> TailPool;           // the player's red on the road behind
	TArray<TWeakObjectPtr<UStaticMeshComponent>> HeadGlows;
	TArray<TWeakObjectPtr<UStaticMeshComponent>> TailGlows;
	FGlpLampState Lamps;
	bool bLampsBuilt = false;
	bool bShown = true;
	bool bBraking = false;
};

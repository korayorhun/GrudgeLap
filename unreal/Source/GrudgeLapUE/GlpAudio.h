#pragma once

#include "CoreMinimal.h"
#include "Components/SynthComponent.h"
#include "grudgelap_physics.h"
#include "GlpAudio.generated.h"

/**
 * The car's sound, as the Godot version makes it (EngineAudio, SampledEngine, TyreAudio): engine loops recorded
 * at known speeds, pitched to the simulated rpm and cross-faded, through a low-pass that opens with the throttle;
 * tyre squeal, howl, skid and gravel loops faded from the wheels' slip; the V8's overrun pops; and the procedural
 * layers on top (road roar, wind, bump thumps, crash, bleeps, the moo, the sign, the train, the title tune).
 *
 * The recordings are the repository's game/assets/audio, staged beside the binaries by the build (see
 * GrudgeLapUE.Build.cs). Everything is mixed here, mono at the 22.05 kHz the recordings and the synthesis use;
 * the engine's mixer resamples to the device. Update runs on the game thread and hands the frame's levels to the
 * audio thread, which owns all the mixing state.
 *
 * For automated checks: -GlpAudioDump=<file.wav> records the mix, -GlpMute keeps the speakers silent meanwhile.
 */
UCLASS()
class UGlpAudio : public USynthComponent
{
	GENERATED_BODY()

public:
	UGlpAudio(const FObjectInitializer& ObjectInitializer);

	/** Selects the engine recordings of a body style (glp_car_spec.style). */
	void SetCar(int32 Style);

	/**
	 * Takes the frame's simulation state. TrainDistance is the distance in metres from the car to the nearest
	 * car of the running train (a large value when it does not run); bMusic plays the title tune.
	 */
	void Update(const glp_state& State, float TrainDistance, bool bMusic, float DeltaSeconds);

protected:
	virtual bool Init(int32& SampleRate) override;
	virtual int32 OnGenerateAudio(float* OutAudio, int32 NumSamples) override;
	virtual void EndPlay(const EEndPlayReason::Type Reason) override;

private:
	struct FClip
	{
		TArray<float> Samples;
		float Rate = 22050.f;
	};

	/** The recordings in use, shared read-only with the audio thread. */
	struct FClips
	{
		TArray<FClip> Engine;        // the power loops by rpm, then the coast loop if the set has one
		TArray<float> EngineRpm;
		int32 CoastLoop = INDEX_NONE;
		TArray<FClip> Tyres;         // squeal, howl, skid, gravel
		TArray<FClip> Pops;
	};

	/** What the audio thread needs from one game frame. */
	struct FFrame
	{
		float EngineGain[4] = {};
		float EnginePitch[4] = { 1.f, 1.f, 1.f, 1.f };
		float Cutoff = 4000.f;
		float TyreGain[4] = {};
		float TyrePitch[4] = { 1.f, 1.f, 1.f, 1.f };
		float RoadRoar = 0.f;
		float Wind = 0.f;
		float TrainLevel = 0.f;
		float Thump = 0.f;
		bool bMusic = false;
		bool bCrash = false;
		bool bBleep = false;
		bool bRumble = false;
		bool bMoo = false;
		bool bClang = false;
		float MooPitch = 1.f;
		int32 Pop = INDEX_NONE;   // a pop to fire now: clip index
		float PopPitch = 1.f;
		float PopGain = 1.f;
	};

	struct FVoice
	{
		double Position = 0.0;
		float Gain = 0.f;
	};

	struct FOneShot
	{
		int32 Clip = INDEX_NONE;
		double Position = 0.0;
		float Pitch = 1.f;
		float Gain = 1.f;
	};

	static bool LoadClip(const FString& File, FClip& Clip);
	static FString AudioDirectory();
	static float EngineWeight(const TArray<float>& Rpms, int32 Count, int32 Index, float Rpm);

	// Audio thread.
	void Apply(const FFrame& Frame);
	float Synth();
	float Music();
	float Road(float Noise);
	float Moo();
	float Clang(float Noise);
	float TrainClack(float Noise);
	static float Loop(const FClip& Clip, double& Position, float Step);

	// Game thread.
	TSharedPtr<const FClips, ESPMode::ThreadSafe> GameClips;
	int32 LoadedStyle = INDEX_NONE;
	FRandomStream GameRandom{ 7 };
	float Rpm = 950.f;
	float Load = 0.f;
	float LastLoad = 0.f;
	float RoadRoar = 0.f;
	float Wind = 0.f;
	float TrainLevel = 0.f;
	float TyreLateral = 0.f;
	float TyreLongitudinal = 0.f;
	float TyreGrass = 0.f;
	float LastCompression[4] = {};
	TArray<float> PopsPending;   // seconds until each scheduled pop
	int32 LastPop = INDEX_NONE;

	// Audio thread.
	TSharedPtr<const FClips, ESPMode::ThreadSafe> Clips;
	FFrame Current;
	FVoice EngineVoices[4];
	FVoice TyreVoices[4];
	TArray<FOneShot> OneShots;
	FRandomStream Random{ 7 };
	float FilterB0 = 1.f, FilterB1 = 0.f, FilterB2 = 0.f, FilterA1 = 0.f, FilterA2 = 0.f;
	float FilterX1 = 0.f, FilterX2 = 0.f, FilterY1 = 0.f, FilterY2 = 0.f;
	float CurrentCutoff = -1.f;
	float Crash = 0.f, Rumble = 0.f, Bleep = 0.f, BleepPhase = 0.f;
	float MusicLevel = 0.f, MusicPhaseLead = 0.f, MusicPhaseBass = 0.f;
	double MusicTime = 0.0;
	float MooLevel = 0.f, MooPhase = 0.f, MooPitch = 1.f;
	float ClangLevel = 0.f, ClangPhase = 0.f, ClangPhase2 = 0.f;
	float TrainClock = 0.f;
	float RoarLp = 0.f, RoarLp2 = 0.f, WindLp = 0.f, Thump = 0.f, ThumpPhase = 0.f;

	// Automated checks (see the class comment); the recording is shared between the threads under DumpLock.
	FString DumpFile;
	bool bMute = false;
	FCriticalSection DumpLock;
	TArray<int16> Dump;
};

#pragma once

#include "CoreMinimal.h"
#include "Components/SynthComponent.h"
#include "grudgelap_physics.h"
#include "GlpAudio.generated.h"

/**
 * The car's sound, as the Godot version makes it (EngineAudio, SampledEngine, TyreAudio): the engine rebuilt
 * firing pulse by firing pulse from recordings by the simulation core's DLL (glp_engine_*, the repository's
 * audio/GranularEngine, the same voice Godot plays); the tyres' rolling roar, squeal, howl, skid and gravel loops
 * faded from the speed and the wheels' slip; the V8's overrun pops; and the procedural layers on top (wind, bump
 * thumps, crash, bleeps, the moo, the sign, the train, the cabinet's chime, starter and jingles). Like the
 * original it drives without music.
 *
 * The recordings are the repository's game/assets/audio, staged beside the binaries by the build (see
 * GrudgeLapUE.Build.cs). Everything is mixed here, mono at the engine recordings' 44.1 kHz (the 22.05 kHz loops
 * are stepped through at their own rate); the engine's mixer resamples to the device. Update runs on the game
 * thread and hands the frame's levels to the audio thread, which owns all the mixing state and the engine voice.
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

	/** The cabinet's chime for a choice made on the title screen. */
	void Chime();

	/**
	 * Takes the frame's simulation state. TrainDistance is the distance in metres from the car to the nearest
	 * car of the running train (a large value when it does not run); bInterior hears the engine from the driver's seat.
	 */
	void Update(const glp_state& State, float TrainDistance, bool bInterior, float DeltaSeconds);

protected:
	virtual bool Init(int32& SampleRate) override;
	virtual int32 OnGenerateAudio(float* OutAudio, int32 NumSamples) override;
	virtual void EndPlay(const EEndPlayReason::Type Reason) override;

private:
	static constexpr int32 TyreLoops = 5;   // road, squeal, howl, skid, gravel

	struct FClip
	{
		TArray<float> Samples;
		float Rate = 22050.f;
	};

	/**
	 * The recordings and the engine voice of a car, shared with the audio thread. The clips are read-only; the
	 * voice (a handle into the DLL, destroyed with the last reference) is rendered by the audio thread alone.
	 */
	struct FClips
	{
		glp_engine Engine = 0;
		TArray<FClip> Tyres;
		TArray<FClip> Pops;
		~FClips();
	};

	/** The jingles of the cabinet, as EngineAudio.Jingle: a fanfare for a record lap, a tune for the end of the credit. */
	enum class EJingle : int32
	{
		None = -1,
		Fanfare = 0,
		GameOver = 1,
	};

	/** One note of a jingle: when it starts (seconds), how long it sounds, its MIDI pitch, its voice and level (EngineAudio.Note). */
	struct FJingleNote
	{
		float Start;
		float Length;
		int32 Midi;
		int32 Voice;
		float Level;
	};

	/** A sounding note: its oscillator phases and age. */
	struct FJingleVoice
	{
		FJingleNote Note;
		double Phase = 0.0;
		double ModPhase = 0.0;
		float Age = 0.f;
		float Lowpass = 0.f;
	};

	/** What the audio thread needs from one game frame. */
	struct FFrame
	{
		float Rpm = 950.f;
		float Load = 0.f;
		bool bRunning = false;
		bool bInterior = false;
		float TyreGain[TyreLoops] = {};
		float TyrePitch[TyreLoops] = { 1.f, 1.f, 1.f, 1.f, 1.f };
		float Wind = 0.f;
		float TrainLevel = 0.f;
		float Thump = 0.f;
		bool bCrash = false;
		bool bBleep = false;
		bool bStarter = false;
		bool bChime = false;
		bool bMoo = false;
		bool bClang = false;
		float MooPitch = 1.f;
		EJingle Jingle = EJingle::None;
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
	static FJingleNote JingleNote(float Beat, float Beats, int32 Midi, int32 Voice, float Level, float Bpm);
	static const TArray<FJingleNote>& FanfareSong();
	static const TArray<FJingleNote>& GameOverSong();

	// Audio thread.
	void Apply(const FFrame& Frame);
	float Synth();
	float Song();
	float ChimeSample();
	float StarterSample(float Noise);
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
	float Wind = 0.f;
	float TrainLevel = 0.f;
	float TyreRolling = 0.f;
	float TyreLateral = 0.f;
	float TyreLongitudinal = 0.f;
	float TyreGrass = 0.f;
	float LastCompression[4] = {};
	TArray<float> PopsPending;   // seconds until each scheduled pop
	int32 LastPop = INDEX_NONE;
	int32 LastPhase = GLP_PHASE_SELECT_TRANSMISSION;
	bool bChimePending = false;

	// Audio thread.
	TSharedPtr<const FClips, ESPMode::ThreadSafe> Clips;
	FFrame Current;
	TArray<float> EngineBuffer;
	FVoice TyreVoices[TyreLoops];
	TArray<FOneShot> OneShots;
	FRandomStream Random{ 7 };
	float Limiter = 0.f;
	float Crash = 0.f, Bleep = 0.f, BleepPhase = 0.f;
	const TArray<FJingleNote>* SongNotes = nullptr;
	double SongTime = 0.0;
	int32 SongNext = 0;
	TArray<FJingleVoice> SongVoices;
	float ChimeLevel = 0.f, ChimePhase = 0.f;
	float Starter = 0.f, StarterPhase = 0.f, StarterLp = 0.f;
	float MooLevel = 0.f, MooPhase = 0.f, MooPitch = 1.f;
	float ClangLevel = 0.f, ClangPhase = 0.f, ClangPhase2 = 0.f;
	float TrainClock = 0.f;
	float WindLp = 0.f, Thump = 0.f, ThumpPhase = 0.f;

	// Automated checks (see the class comment); the recording is shared between the threads under DumpLock.
	FString DumpFile;
	bool bMute = false;
	FCriticalSection DumpLock;
	TArray<int16> Dump;
};

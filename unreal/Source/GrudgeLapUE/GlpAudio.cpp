#include "GlpAudio.h"

#include "HAL/FileManager.h"
#include "Misc/CommandLine.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"

DEFINE_LOG_CATEGORY_STATIC(LogGlpAudio, Log, All);

namespace
{
	constexpr int32 MixRate = 22050;

	// SampledEngine.Sets: the loops of each body style and the rpm each was recorded at.
	struct FLoopFile
	{
		const TCHAR* File;
		float Rpm;
	};
	const FLoopFile FlatTwelveLoops[] = { { TEXT("flat12/low.wav"), 3000.f }, { TEXT("flat12/mid.wav"), 4750.f }, { TEXT("flat12/high.wav"), 6650.f } };
	const FLoopFile V8Loops[] = { { TEXT("v8/low.wav"), 2700.f }, { TEXT("v8/mid.wav"), 5800.f }, { TEXT("v8/high.wav"), 8400.f } };
	const FLoopFile V8Coast = { TEXT("v8/coast.wav"), 6300.f };
	const TCHAR* TyreFiles[] = { TEXT("tyres/squeal.wav"), TEXT("tyres/squeal_hard.wav"), TEXT("tyres/skid.wav"), TEXT("tyres/gravel.wav") };
	const TCHAR* PopFiles[] = { TEXT("v8/pop1.wav"), TEXT("v8/pop2.wav"), TEXT("v8/pop3.wav"), TEXT("v8/pop4.wav"), TEXT("v8/pop5.wav"), TEXT("v8/crackle.wav") };

	// Levels of the Godot mix: loops at 0.9 (SampledEngine.Volume, TyreAudio.Volume), the pops' player at -8 dB,
	// the synthesis at 0.8 into a player at -6 dB.
	constexpr float LoopVolume = 0.9f;
	constexpr float PopLevel = 0.398f;
	constexpr float SynthVolume = 0.8f;
	constexpr float SynthLevel = 0.501f;

	// The title tune (EngineAudio.Chords): A minor, F, C, G as MIDI notes, the bass root first.
	const int32 Chords[4][5] = { { 45, 57, 60, 64, 69 }, { 41, 53, 57, 60, 65 }, { 48, 60, 64, 67, 72 }, { 43, 55, 59, 62, 67 } };
	constexpr float Bpm = 118.f;

	float Clamp01(float Value)
	{
		return FMath::Clamp(Value, 0.f, 1.f);
	}

	/** Fast attack, slower release (TyreAudio.Follow). */
	void Follow(float& Value, float Target, float DeltaSeconds, float Attack, float Release)
	{
		const float Rate = Target > Value ? Attack : Release;
		Value += (Target - Value) * Clamp01(DeltaSeconds * Rate);
	}

	bool Event(const glp_state& State, int32 Event)
	{
		return (State.events & (1u << Event)) != 0;
	}
}

UGlpAudio::UGlpAudio(const FObjectInitializer& ObjectInitializer)
	: Super(ObjectInitializer)
{
	bAutoActivate = true;
	bAllowSpatialization = false;
	bIsUISound = false;
	NumChannels = 1;
}

bool UGlpAudio::Init(int32& SampleRate)
{
	// The recordings and the synthesis are all 22.05 kHz; the mixer resamples the result to the device's rate.
	SampleRate = MixRate;
	NumChannels = 1;
	FParse::Value(FCommandLine::Get(), TEXT("GlpAudioDump="), DumpFile);
	bMute = FParse::Param(FCommandLine::Get(), TEXT("GlpMute"));
	return true;
}

void UGlpAudio::EndPlay(const EEndPlayReason::Type Reason)
{
	Super::EndPlay(Reason);
	if (DumpFile.IsEmpty())
	{
		return;
	}
	// A plain RIFF file: 16-bit mono PCM at the mix rate.
	FScopeLock Lock(&DumpLock);
	TArray<uint8> Bytes;
	auto Put32 = [&Bytes](uint32 Value) { Bytes.Append(reinterpret_cast<const uint8*>(&Value), 4); };
	auto Put16 = [&Bytes](uint16 Value) { Bytes.Append(reinterpret_cast<const uint8*>(&Value), 2); };
	const uint32 DataBytes = Dump.Num() * 2;
	Bytes.Append(reinterpret_cast<const uint8*>("RIFF"), 4);
	Put32(36 + DataBytes);
	Bytes.Append(reinterpret_cast<const uint8*>("WAVEfmt "), 8);
	Put32(16);
	Put16(1);
	Put16(1);
	Put32(MixRate);
	Put32(MixRate * 2);
	Put16(2);
	Put16(16);
	Bytes.Append(reinterpret_cast<const uint8*>("data"), 4);
	Put32(DataBytes);
	Bytes.Append(reinterpret_cast<const uint8*>(Dump.GetData()), DataBytes);
	if (FFileHelper::SaveArrayToFile(Bytes, *DumpFile))
	{
		UE_LOG(LogGlpAudio, Log, TEXT("mix recorded: %s (%.1f s)"), *DumpFile, Dump.Num() / static_cast<float>(MixRate));
	}
}

FString UGlpAudio::AudioDirectory()
{
	// Staged beside the binaries by the build; straight from the repository as a fallback.
	const FString Staged = FPaths::Combine(FPaths::ProjectDir(), TEXT("Binaries"), FPlatformProcess::GetBinariesSubdirectory(), TEXT("GrudgeLapAudio"));
	if (IFileManager::Get().DirectoryExists(*Staged))
	{
		return Staged;
	}
	return FPaths::ConvertRelativePathToFull(FPaths::Combine(FPaths::ProjectDir(), TEXT(".."), TEXT("game"), TEXT("assets"), TEXT("audio")));
}

bool UGlpAudio::LoadClip(const FString& File, FClip& Clip)
{
	// A minimal RIFF reader for the repository's recordings: PCM 16-bit or float 32-bit, the first channel.
	TArray<uint8> Bytes;
	if (!FFileHelper::LoadFileToArray(Bytes, *File, FILEREAD_Silent) || Bytes.Num() < 12 || FMemory::Memcmp(Bytes.GetData(), "RIFF", 4) != 0 || FMemory::Memcmp(Bytes.GetData() + 8, "WAVE", 4) != 0)
	{
		UE_LOG(LogGlpAudio, Warning, TEXT("recording missing or not a WAV file: %s"), *File);
		return false;
	}
	auto U16 = [&Bytes](int32 At) { return static_cast<uint32>(Bytes[At]) | static_cast<uint32>(Bytes[At + 1]) << 8; };
	auto U32 = [&](int32 At) { return U16(At) | U16(At + 2) << 16; };
	int32 Format = 0, Channels = 1, Bits = 16;
	for (int32 At = 12; At + 8 <= Bytes.Num();)
	{
		const int32 Size = static_cast<int32>(U32(At + 4));
		const int32 Body = At + 8;
		if (Size < 0 || Body + Size > Bytes.Num())
		{
			break;
		}
		if (FMemory::Memcmp(Bytes.GetData() + At, "fmt ", 4) == 0 && Size >= 16)
		{
			Format = U16(Body);
			Channels = FMath::Max<int32>(U16(Body + 2), 1);
			Clip.Rate = static_cast<float>(U32(Body + 4));
			Bits = U16(Body + 14);
		}
		else if (FMemory::Memcmp(Bytes.GetData() + At, "data", 4) == 0)
		{
			const int32 Stride = Channels * Bits / 8;
			const int32 Frames = Stride > 0 ? Size / Stride : 0;
			Clip.Samples.SetNumUninitialized(Frames);
			for (int32 i = 0; i < Frames; i++)
			{
				const int32 Sample = Body + i * Stride;
				if (Format == 1 && Bits == 16)
				{
					Clip.Samples[i] = static_cast<int16>(U16(Sample)) / 32768.f;
				}
				else if (Format == 3 && Bits == 32)
				{
					uint32 Raw = U32(Sample);
					float Value;
					FMemory::Memcpy(&Value, &Raw, sizeof(Value));
					Clip.Samples[i] = Value;
				}
				else
				{
					UE_LOG(LogGlpAudio, Warning, TEXT("unsupported WAV format %d (%d bits): %s"), Format, Bits, *File);
					Clip.Samples.Reset();
					return false;
				}
			}
			return Clip.Samples.Num() > 1;
		}
		At = Body + Size + (Size & 1);
	}
	UE_LOG(LogGlpAudio, Warning, TEXT("no audio data in %s"), *File);
	return false;
}

void UGlpAudio::SetCar(int32 Style)
{
	if (Style == LoadedStyle && GameClips)
	{
		return;
	}
	LoadedStyle = Style;
	const FString Directory = AudioDirectory();
	TSharedPtr<FClips, ESPMode::ThreadSafe> Loaded = MakeShared<FClips, ESPMode::ThreadSafe>();
	TArrayView<const FLoopFile> Loops = Style == 1 ? TArrayView<const FLoopFile>(V8Loops) : TArrayView<const FLoopFile>(FlatTwelveLoops);
	for (const FLoopFile& Loop : Loops)
	{
		FClip Clip;
		if (LoadClip(FPaths::Combine(Directory, Loop.File), Clip))
		{
			Loaded->Engine.Add(MoveTemp(Clip));
			Loaded->EngineRpm.Add(Loop.Rpm);
		}
	}
	if (Style == 1)
	{
		FClip Clip;
		if (LoadClip(FPaths::Combine(Directory, V8Coast.File), Clip))
		{
			Loaded->CoastLoop = Loaded->Engine.Add(MoveTemp(Clip));
			Loaded->EngineRpm.Add(V8Coast.Rpm);
		}
	}
	for (const TCHAR* File : TyreFiles)
	{
		LoadClip(FPaths::Combine(Directory, File), Loaded->Tyres.AddDefaulted_GetRef());
	}
	if (Style == 1)
	{
		for (const TCHAR* File : PopFiles)
		{
			FClip Clip;
			if (LoadClip(FPaths::Combine(Directory, File), Clip))
			{
				Loaded->Pops.Add(MoveTemp(Clip));
			}
		}
	}
	UE_LOG(LogGlpAudio, Log, TEXT("recordings from %s: %d engine loops, %d tyre loops, %d pops"), *Directory, Loaded->Engine.Num(), Loaded->Tyres.Num(), Loaded->Pops.Num());
	GameClips = Loaded;
	PopsPending.Reset();
	SynthCommand([this, Loaded]()
	{
		Clips = Loaded;
		OneShots.Reset();
	});
}

float UGlpAudio::EngineWeight(const TArray<float>& Rpms, int32 Count, int32 Index, float EngineRpm)
{
	// Equal-power cross-fade between neighbouring loops on a logarithmic rpm scale (SampledEngine.Weight).
	const float R = Rpms[Index];
	if (Count == 1 || (Index == 0 && EngineRpm <= R) || (Index == Count - 1 && EngineRpm >= R))
	{
		return 1.f;
	}
	if (Index > 0 && EngineRpm < R)
	{
		const float Lower = Rpms[Index - 1];
		if (EngineRpm <= Lower)
		{
			return 0.f;
		}
		const float T = FMath::Loge(EngineRpm / Lower) / FMath::Loge(R / Lower);
		return FMath::Sin(T * UE_HALF_PI);
	}
	if (Index < Count - 1 && EngineRpm >= R)
	{
		const float Upper = Rpms[Index + 1];
		if (EngineRpm >= Upper)
		{
			return 0.f;
		}
		const float T = FMath::Loge(EngineRpm / R) / FMath::Loge(Upper / R);
		return FMath::Cos(T * UE_HALF_PI);
	}
	return 0.f;
}

void UGlpAudio::Update(const glp_state& State, float TrainDistance, bool bMusic, float DeltaSeconds)
{
	if (!GameClips || DeltaSeconds <= 0.f)
	{
		return;
	}
	FFrame Frame;
	Frame.bMusic = bMusic;

	// EngineAudio.Update: the smoothed engine state the loops follow.
	const bool bRunning = State.engine_running != 0;
	Rpm += (FMath::Max(State.rpm, 0.f) - Rpm) * Clamp01(DeltaSeconds * 12.f);
	const float NewLoad = Clamp01(State.engine_torque / 500.f);
	Load += (NewLoad - Load) * Clamp01(DeltaSeconds * 8.f);

	Frame.bCrash = Event(State, GLP_EVENT_CRASHED);
	Frame.bBleep = Event(State, GLP_EVENT_CHECKPOINT_PASSED) || Event(State, GLP_EVENT_LAP_COMPLETED);
	Frame.bRumble = Event(State, GLP_EVENT_ENGINE_STARTED);
	Frame.bClang = Event(State, GLP_EVENT_SIGN_HIT);
	if (Event(State, GLP_EVENT_COW_HIT))
	{
		Frame.bMoo = true;
		Frame.MooPitch = 0.9f + 0.2f * GameRandom.FRand();
	}
	// The train is heard when it runs within a couple of hundred metres.
	TrainLevel += (Clamp01(1.f - TrainDistance / 260.f) - TrainLevel) * Clamp01(DeltaSeconds * 3.f);
	Frame.TrainLevel = TrainLevel;

	// Road roar and wind grow with speed; the roar is duller on grass, the wind only matters above town speeds.
	const float Speed = FMath::Abs(State.speed);
	bool bOnRoad = false;
	for (int32 i = 0; i < 4; i++)
	{
		bOnRoad |= State.wheel_contact[i] && State.wheel_surface[i] == GLP_SURFACE_ROAD;
	}
	RoadRoar += (FMath::Pow(Clamp01((Speed - 1.f) / 45.f), 1.3f) * (bOnRoad ? 1.f : 0.7f) - RoadRoar) * Clamp01(DeltaSeconds * 4.f);
	Wind += (FMath::Pow(Clamp01((Speed - 12.f) / 70.f), 2.f) - Wind) * Clamp01(DeltaSeconds * 3.f);
	Frame.RoadRoar = RoadRoar;
	Frame.Wind = Wind;
	// A sudden change of suspension travel is a bump: a low thump, louder for a harder hit.
	float Jolt = 0.f;
	for (int32 i = 0; i < 4; i++)
	{
		const float Compression = State.wheel_contact[i] ? State.wheel_compression[i] : 0.f;
		Jolt = FMath::Max(Jolt, FMath::Abs(Compression - LastCompression[i]) / DeltaSeconds);
		LastCompression[i] = Compression;
	}
	if (Jolt > 0.8f)
	{
		Frame.Thump = Clamp01((Jolt - 0.8f) / 2.5f);
	}

	// Sport exhaust: lifting off at high revs sets off two to four pops over the next half second.
	const FClips& Set = *GameClips;
	if (!Set.Pops.IsEmpty() && bRunning && LastLoad > 0.5f && NewLoad < 0.15f && Rpm > 4800.f && PopsPending.IsEmpty())
	{
		const int32 Count = 2 + GameRandom.RandRange(0, 2);
		float At = 0.04f;
		for (int32 i = 0; i < Count; i++)
		{
			PopsPending.Add(At);
			At += 0.08f + 0.16f * GameRandom.FRand();
		}
	}
	for (int32 i = PopsPending.Num() - 1; i >= 0; i--)
	{
		PopsPending[i] -= DeltaSeconds;
		if (PopsPending[i] > 0.f || Frame.Pop != INDEX_NONE)
		{
			continue;
		}
		PopsPending.RemoveAt(i);
		// Drawn at random without repeating the last one, with a spread of pitch (x/÷ 1.12) and level (+-3 dB),
		// as the Godot version's AudioStreamRandomizer plays them.
		int32 Pick = GameRandom.RandRange(0, Set.Pops.Num() - 1);
		if (Set.Pops.Num() > 1 && Pick == LastPop)
		{
			Pick = (Pick + 1 + GameRandom.RandRange(0, Set.Pops.Num() - 2)) % Set.Pops.Num();
		}
		LastPop = Pick;
		Frame.Pop = Pick;
		Frame.PopPitch = FMath::Exp(GameRandom.FRandRange(-1.f, 1.f) * FMath::Loge(1.12f));
		Frame.PopGain = FMath::Pow(10.f, GameRandom.FRandRange(-3.f, 3.f) / 20.f);
	}
	LastLoad = NewLoad;

	// SampledEngine.Update: each loop pitched by rpm over its own speed, faded in near it; the coast loop takes
	// over off the throttle.
	Frame.Cutoff = 900.f + 9000.f * Load;
	const float Gain = bRunning ? LoopVolume * (0.5f + 0.5f * Load) : 0.f;
	const int32 Power = Set.CoastLoop == INDEX_NONE ? Set.Engine.Num() : Set.CoastLoop;
	float CoastWeight = 0.f;
	if (Set.CoastLoop != INDEX_NONE && Set.CoastLoop < 4)
	{
		const float Band = 1.f - Clamp01(FMath::Abs(Rpm - Set.EngineRpm[Set.CoastLoop]) / 2600.f);
		CoastWeight = (1.f - Load) * Band * (bRunning ? 1.f : 0.f);
		Frame.EngineGain[Set.CoastLoop] = LoopVolume * CoastWeight * 0.9f;
		Frame.EnginePitch[Set.CoastLoop] = FMath::Clamp(FMath::Max(Rpm, 300.f) / Set.EngineRpm[Set.CoastLoop], 0.25f, 4.f);
	}
	for (int32 i = 0; i < Power && i < 4; i++)
	{
		Frame.EngineGain[i] = Gain * EngineWeight(Set.EngineRpm, Power, i, Rpm) * (1.f - 0.55f * CoastWeight);
		Frame.EnginePitch[i] = FMath::Clamp(FMath::Max(Rpm, 300.f) / Set.EngineRpm[i], 0.25f, 4.f);
	}

	// TyreAudio.Update: slip angles beyond about four degrees and slip ratios beyond fifteen percent are audible.
	float Lateral = 0.f, Longitudinal = 0.f, OffRoad = 0.f;
	for (int32 i = 0; i < 4; i++)
	{
		if (!State.wheel_contact[i])
		{
			continue;
		}
		if (State.wheel_surface[i] == GLP_SURFACE_ROAD)
		{
			Lateral = FMath::Max(Lateral, Clamp01((FMath::Abs(State.wheel_slip_angle[i]) - 0.07f) / 0.22f));
			Longitudinal = FMath::Max(Longitudinal, Clamp01((FMath::Abs(State.wheel_slip_ratio[i]) - 0.15f) / 0.6f));
		}
		else
		{
			OffRoad = 1.f;
		}
	}
	const float SpeedFactor = Clamp01((Speed - 2.5f) / 8.f);
	Follow(TyreLateral, Lateral * SpeedFactor, DeltaSeconds, 14.f, 5.f);
	Follow(TyreLongitudinal, Longitudinal * SpeedFactor, DeltaSeconds, 20.f, 6.f);
	Follow(TyreGrass, OffRoad * Clamp01(Speed / 6.f), DeltaSeconds, 6.f, 4.f);
	// The cornering loop, pitched up as the slide deepens; the howl joins at big angles; the screech follows the
	// speed of the rubber over the road; gravel off the asphalt.
	Frame.TyreGain[0] = LoopVolume * TyreLateral * (1.f - 0.5f * TyreLongitudinal);
	Frame.TyrePitch[0] = 0.88f + 0.30f * TyreLateral;
	Frame.TyreGain[1] = LoopVolume * Clamp01((TyreLateral - 0.45f) / 0.55f) * 0.9f;
	Frame.TyrePitch[1] = 0.9f + 0.2f * TyreLateral;
	Frame.TyreGain[2] = LoopVolume * TyreLongitudinal * 0.9f;
	Frame.TyrePitch[2] = 0.8f + 0.5f * Clamp01(Speed / 40.f);
	Frame.TyreGain[3] = LoopVolume * TyreGrass * (0.5f + 0.5f * Clamp01(Speed / 30.f));
	Frame.TyrePitch[3] = 0.7f + 0.6f * Clamp01(Speed / 30.f);

	// Without an audio device (-nosound) the synth never starts and queued commands would only pile up.
	if (IsPlaying())
	{
		SynthCommand([this, Frame]() { Apply(Frame); });
	}
}

void UGlpAudio::Apply(const FFrame& Frame)
{
	// Levels and pitches glide to the new frame over the next buffer (OnGenerateAudio); triggers fire once.
	Current.Cutoff = Frame.Cutoff;
	FMemory::Memcpy(Current.EngineGain, Frame.EngineGain, sizeof(Frame.EngineGain));
	FMemory::Memcpy(Current.EnginePitch, Frame.EnginePitch, sizeof(Frame.EnginePitch));
	FMemory::Memcpy(Current.TyreGain, Frame.TyreGain, sizeof(Frame.TyreGain));
	FMemory::Memcpy(Current.TyrePitch, Frame.TyrePitch, sizeof(Frame.TyrePitch));
	Current.RoadRoar = Frame.RoadRoar;
	Current.Wind = Frame.Wind;
	Current.TrainLevel = Frame.TrainLevel;
	Current.bMusic = Frame.bMusic;
	Thump = FMath::Max(Thump, Frame.Thump);
	if (Frame.bCrash)
	{
		Crash = 1.f;
	}
	if (Frame.bBleep)
	{
		Bleep = 0.35f;
	}
	if (Frame.bRumble)
	{
		Rumble = 0.6f;
	}
	if (Frame.bClang)
	{
		ClangLevel = 1.f;
	}
	if (Frame.bMoo)
	{
		MooLevel = 1.f;
		MooPhase = 0.f;
		MooPitch = Frame.MooPitch;
	}
	if (Frame.Pop != INDEX_NONE)
	{
		// Up to four ring at once; a fifth cuts the oldest short.
		if (OneShots.Num() >= 4)
		{
			OneShots.RemoveAt(0);
		}
		OneShots.Add({ Frame.Pop, 0.0, Frame.PopPitch, Frame.PopGain });
	}
}

float UGlpAudio::Loop(const FClip& Clip, double& Position, float Step)
{
	const int32 Count = Clip.Samples.Num();
	if (Count < 2)
	{
		return 0.f;
	}
	const int32 Index = static_cast<int32>(Position);
	const float Fraction = static_cast<float>(Position - Index);
	const float Value = FMath::Lerp(Clip.Samples[Index % Count], Clip.Samples[(Index + 1) % Count], Fraction);
	Position += Step;
	if (Position >= Count)
	{
		Position = FMath::Fmod(Position, static_cast<double>(Count));
	}
	return Value;
}

int32 UGlpAudio::OnGenerateAudio(float* OutAudio, int32 NumSamples)
{
	const FClips* Set = Clips.Get();
	if (!Set)
	{
		FMemory::Memzero(OutAudio, NumSamples * sizeof(float));
		return NumSamples;
	}

	// The engine's low-pass (Godot's AudioEffectLowPassFilter at resonance 0.4): a biquad, set once per buffer.
	if (!FMath::IsNearlyEqual(CurrentCutoff, Current.Cutoff, 1.f))
	{
		CurrentCutoff = Current.Cutoff;
		const float W0 = 2.f * UE_PI * FMath::Min(CurrentCutoff, MixRate * 0.45f) / MixRate;
		const float Alpha = FMath::Sin(W0) / (2.f * 0.4f);
		const float CosW = FMath::Cos(W0);
		const float A0 = 1.f + Alpha;
		FilterB0 = (1.f - CosW) * 0.5f / A0;
		FilterB1 = (1.f - CosW) / A0;
		FilterB2 = FilterB0;
		FilterA1 = -2.f * CosW / A0;
		FilterA2 = (1.f - Alpha) / A0;
	}

	float EngineFrom[4], TyreFrom[4];
	for (int32 v = 0; v < 4; v++)
	{
		EngineFrom[v] = EngineVoices[v].Gain;
		TyreFrom[v] = TyreVoices[v].Gain;
	}
	for (int32 i = 0; i < NumSamples; i++)
	{
		const float T = static_cast<float>(i + 1) / NumSamples;
		float Engine = 0.f;
		for (int32 v = 0; v < Set->Engine.Num() && v < 4; v++)
		{
			const float Gain = FMath::Lerp(EngineFrom[v], Current.EngineGain[v], T);
			const FClip& Clip = Set->Engine[v];
			const float Sample = Loop(Clip, EngineVoices[v].Position, Current.EnginePitch[v] * Clip.Rate / MixRate);
			Engine += Sample * Gain;
		}
		const float Filtered = FilterB0 * Engine + FilterB1 * FilterX1 + FilterB2 * FilterX2 - FilterA1 * FilterY1 - FilterA2 * FilterY2;
		FilterX2 = FilterX1;
		FilterX1 = Engine;
		FilterY2 = FilterY1;
		FilterY1 = Filtered;

		float Tyres = 0.f;
		for (int32 v = 0; v < Set->Tyres.Num() && v < 4; v++)
		{
			const float Gain = FMath::Lerp(TyreFrom[v], Current.TyreGain[v], T);
			const FClip& Clip = Set->Tyres[v];
			Tyres += Loop(Clip, TyreVoices[v].Position, Current.TyrePitch[v] * Clip.Rate / MixRate) * Gain;
		}

		float Pops = 0.f;
		for (int32 p = OneShots.Num() - 1; p >= 0; p--)
		{
			FOneShot& Shot = OneShots[p];
			const int32 Index = static_cast<int32>(Shot.Position);
			if (!Set->Pops.IsValidIndex(Shot.Clip) || Index + 1 >= Set->Pops[Shot.Clip].Samples.Num())
			{
				OneShots.RemoveAt(p);
				continue;
			}
			const FClip& Clip = Set->Pops[Shot.Clip];
			const float Fraction = static_cast<float>(Shot.Position - Index);
			Pops += FMath::Lerp(Clip.Samples[Index], Clip.Samples[Index + 1], Fraction) * Shot.Gain;
			Shot.Position += Shot.Pitch * Clip.Rate / MixRate;
		}

		const float Mix = FMath::Clamp(Filtered + Tyres + Pops * PopLevel + Synth() * SynthLevel, -1.f, 1.f);
		OutAudio[i] = Mix;
	}
	if (!DumpFile.IsEmpty())
	{
		FScopeLock Lock(&DumpLock);
		for (int32 i = 0; i < NumSamples; i++)
		{
			Dump.Add(static_cast<int16>(OutAudio[i] * 32767.f));
		}
	}
	if (bMute)
	{
		FMemory::Memzero(OutAudio, NumSamples * sizeof(float));
	}
	for (int32 v = 0; v < 4; v++)
	{
		EngineVoices[v].Gain = Current.EngineGain[v];
		TyreVoices[v].Gain = Current.TyreGain[v];
	}
	return NumSamples;
}

float UGlpAudio::Synth()
{
	// EngineAudio.Fill without the 1989-mode voices: one sample of the procedural layers.
	const float Noise = Random.FRandRange(-1.f, 1.f);
	float Sum = 0.f;
	if (Crash > 0.f)
	{
		Sum += Noise * Crash * 0.9f;
		Crash -= 1.4f / MixRate;
	}
	if (Rumble > 0.f)
	{
		Sum += Noise * Rumble * 0.25f;
		Rumble -= 1.2f / MixRate;
	}
	if (Bleep > 0.f)
	{
		BleepPhase += 880.f / MixRate;
		Sum += FMath::Sin(BleepPhase * UE_TWO_PI) * 0.25f * FMath::Min(1.f, Bleep * 6.f);
		Bleep -= 1.f / MixRate;
	}
	Sum += Music() + Moo() + Clang(Noise) + TrainClack(Noise) + Road(Noise);
	return FMath::Tanh(Sum * SynthVolume);
}

float UGlpAudio::Road(float Noise)
{
	// Road roar (two-pole low-passed noise), wind (one-pole) and the bump thump (a 48 Hz burst).
	RoarLp += (Noise - RoarLp) * 0.045f;
	RoarLp2 += (RoarLp - RoarLp2) * 0.045f;
	const float Roar = RoarLp2 * 7.f * Current.RoadRoar * 0.26f;
	WindLp += (Noise - WindLp) * 0.12f;
	const float WindNoise = WindLp * 2.6f * Current.Wind * 0.22f;
	float Bump = 0.f;
	if (Thump > 0.f)
	{
		ThumpPhase += 48.f / MixRate;
		if (ThumpPhase >= 1.f)
		{
			ThumpPhase -= 1.f;
		}
		Bump = FMath::Sin(ThumpPhase * UE_TWO_PI) * Thump * 0.55f + Noise * Thump * 0.08f;
		Thump -= 1.f / (MixRate * 0.16f);
	}
	return Roar + WindNoise + Bump;
}

float UGlpAudio::Moo()
{
	// A cow: two formants on a pitch that sags, over about a second.
	if (MooLevel <= 0.f)
	{
		return 0.f;
	}
	const float T = 1.f - MooLevel;
	MooPhase += (140.f - 50.f * T) * MooPitch / MixRate;
	if (MooPhase >= 1.f)
	{
		MooPhase -= 1.f;
	}
	const float P = MooPhase * UE_TWO_PI;
	const float Envelope = FMath::Min(T * 8.f, 1.f) * FMath::Min(MooLevel * 3.f, 1.f);
	const float Tone = FMath::Sin(P) * 0.6f + FMath::Sin(P * 2.f) * 0.35f + FMath::Sin(P * 3.f) * 0.2f + FMath::Sin(P * 5.f) * 0.1f;
	MooLevel -= 1.f / (MixRate * 1.1f);
	return FMath::Tanh(Tone * 1.4f) * 0.35f * Envelope;
}

float UGlpAudio::Clang(float Noise)
{
	// A road sign going over: a short bright ring from two detuned partials.
	if (ClangLevel <= 0.f)
	{
		return 0.f;
	}
	ClangPhase += 2350.f / MixRate;
	ClangPhase2 += 3170.f / MixRate;
	if (ClangPhase >= 1.f)
	{
		ClangPhase -= 1.f;
	}
	if (ClangPhase2 >= 1.f)
	{
		ClangPhase2 -= 1.f;
	}
	const float Ring = FMath::Sin(ClangPhase * UE_TWO_PI) * 0.6f + FMath::Sin(ClangPhase2 * UE_TWO_PI) * 0.4f + Noise * 0.3f * ClangLevel;
	const float Envelope = ClangLevel * ClangLevel;
	ClangLevel -= 1.f / (MixRate * 0.7f);
	return Ring * 0.35f * Envelope;
}

float UGlpAudio::TrainClack(float Noise)
{
	// Wheels over rail joints: noise bursts twice a second, as loud as the train is near.
	if (Current.TrainLevel < 0.01f)
	{
		return 0.f;
	}
	TrainClock += 1.f / MixRate;
	const float Phase = FMath::Fmod(TrainClock, 0.42f);
	const float Burst = FMath::Exp(-Phase * 60.f) + (Phase > 0.11f ? 0.5f * FMath::Exp(-(Phase - 0.11f) * 60.f) : 0.f);
	return Noise * Burst * 0.5f * Current.TrainLevel + Noise * 0.04f * Current.TrainLevel;
}

float UGlpAudio::Music()
{
	// A 16th-note arpeggio on a square wave over a sine bass, 118 bpm, four bars, faded in and out.
	MusicLevel += ((Current.bMusic ? 1.f : 0.f) - MusicLevel) * (2.5f / MixRate);
	if (MusicLevel < 0.002f)
	{
		return 0.f;
	}
	static const int32 Pattern[16] = { 1, 2, 3, 4, 3, 2, 1, 2, 3, 4, 3, 4, 3, 2, 1, 0 };
	const double Beat = MusicTime * Bpm / 60.0;
	const int32* Chord = Chords[static_cast<int32>(Beat / 4.0) % 4];
	const int32 Sixteenth = static_cast<int32>(Beat * 4.0);
	const int32 Step = Pattern[Sixteenth % 16];
	const float LeadFrequency = Step == 0 ? 0.f : 440.f * FMath::Pow(2.f, (Chord[Step] - 69) / 12.f);
	const float BassFrequency = 440.f * FMath::Pow(2.f, (Chord[0] - 69) / 12.f);
	MusicPhaseLead += LeadFrequency / MixRate;
	MusicPhaseBass += BassFrequency / MixRate;
	if (MusicPhaseLead >= 1.f)
	{
		MusicPhaseLead -= 1.f;
	}
	if (MusicPhaseBass >= 1.f)
	{
		MusicPhaseBass -= 1.f;
	}
	// Short percussive envelope per sixteenth for the lead; the bass holds for the bar.
	const float Within = static_cast<float>(Beat * 4.0 - Sixteenth);
	const float LeadEnvelope = LeadFrequency > 0.f ? FMath::Exp(-Within * 5.f) : 0.f;
	const float Lead = (MusicPhaseLead < 0.5f ? 1.f : -1.f) * 0.08f * LeadEnvelope;
	const float Bass = FMath::Sin(MusicPhaseBass * UE_TWO_PI) * 0.10f;
	MusicTime += 1.0 / MixRate;
	return (Lead + Bass) * MusicLevel;
}

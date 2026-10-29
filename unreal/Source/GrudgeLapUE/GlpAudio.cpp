#include "GlpAudio.h"

#include "GlpLibrary.h"
#include "HAL/FileManager.h"
#include "Misc/CommandLine.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"

DEFINE_LOG_CATEGORY_STATIC(LogGlpAudio, Log, All);

namespace
{
	// The engine voice renders at its recordings' rate; the loops and the synthesis (22.05 kHz in Godot) follow.
	constexpr int32 MixRate = 44100;

	// TyreAudio's loops, in the order of FFrame.TyreGain.
	const TCHAR* TyreFiles[] = { TEXT("tyres/road.wav"), TEXT("tyres/squeal.wav"), TEXT("tyres/squeal_hard.wav"), TEXT("tyres/skid.wav"), TEXT("tyres/gravel.wav") };
	const TCHAR* PopFiles[] = { TEXT("v8/pop1.wav"), TEXT("v8/pop2.wav"), TEXT("v8/pop3.wav"), TEXT("v8/pop4.wav"), TEXT("v8/pop5.wav"), TEXT("v8/crackle.wav") };
	const TCHAR* CrashFiles[] = { TEXT("crash/crash1.wav"), TEXT("crash/crash2.wav"), TEXT("crash/crash3.wav") };
	const TCHAR* SteamFile = TEXT("train/steam.wav");

	// Levels of the Godot mix: engine and loops at 0.9 (SampledEngine.Volume, TyreAudio.Volume), the pops' player
	// at -8 dB, the synthesis at 0.8 into a player at -6 dB, and a limiter on the master bus at -0.5 dB.
	constexpr float LoopVolume = 0.9f;
	constexpr float PopLevel = 0.398f;
	constexpr float CrashLevel = 0.891f;   // the crash takes' player at -1 dB
	constexpr float SteamLevel = 0.5f;     // the steam loop when the train is on top of the car
	constexpr float SynthVolume = 0.8f;
	constexpr float SynthLevel = 0.501f;
	constexpr float Ceiling = 0.944f;

	// The wind's one-pole low-pass at the 450 Hz of Godot's (0.12 per sample at 22.05 kHz). The noise spreads over
	// twice Godot's band here, so the filtered wind comes out 3 dB quieter: its level makes up for that.
	const float WindFollow = 1.f - FMath::Exp(-UE_TWO_PI * 450.f / MixRate);
	// The clunk's click: the one-pole at 1.5 kHz of Godot's (0.35 per sample at 22.05 kHz).
	const float ClunkFollow = 1.f - FMath::Exp(-UE_TWO_PI * 1510.f / MixRate);
	const float WindLevel = 2.6f * UE_SQRT_2;

	// The voices of the jingles (EngineAudio.Brass and the rest).
	constexpr int32 Brass = 0, Lead = 1, Bass = 2, Hat = 3, Snare = 4;

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
	// The engine's recordings are 44.1 kHz; the mixer resamples the result to the device's rate.
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

UGlpAudio::FClips::~FClips()
{
	if (Engine != 0)
	{
		if (const FGlpLibrary* Library = FGlpLibrary::Get())
		{
			Library->EngineDestroy(Engine);
		}
	}
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
	// SampledEngine.Load: the grain banks of the car's engine, by its body style (CarStyle: the twelve, the V8, the
	// hot hatch's four, the five, the turbo hatch's four), with the cylinders that turn revs into firing pulses.
	struct FEngineBank { const TCHAR* Folder; int32 Cylinders; };
	static const FEngineBank EngineBanks[] = { { TEXT("flat12"), 12 }, { TEXT("v8"), 8 }, { TEXT("hothatch"), 4 }, { TEXT("five"), 5 }, { TEXT("turbohatch"), 4 } };
	const FEngineBank& Bank = EngineBanks[FMath::Clamp(Style, 0, (int32)UE_ARRAY_COUNT(EngineBanks) - 1)];
	if (const FGlpLibrary* Library = FGlpLibrary::Get())
	{
		const FString Banks = FPaths::Combine(Directory, Bank.Folder);
		Loaded->Engine = Library->EngineCreate(TCHAR_TO_UTF8(*Banks), Bank.Cylinders);
		if (Loaded->Engine == 0)
		{
			UE_LOG(LogGlpAudio, Warning, TEXT("engine sound unavailable: %s"), *Library->LastError());
		}
		else if (Library->EngineSampleRate(Loaded->Engine) != MixRate)
		{
			UE_LOG(LogGlpAudio, Warning, TEXT("engine recordings in %s are not at %d Hz; the engine stays silent"), *Banks, MixRate);
			Library->EngineDestroy(Loaded->Engine);
			Loaded->Engine = 0;
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
	for (const TCHAR* File : CrashFiles)
	{
		FClip Clip;
		if (LoadClip(FPaths::Combine(Directory, File), Clip))
		{
			Loaded->Crashes.Add(MoveTemp(Clip));
		}
	}
	LoadClip(FPaths::Combine(Directory, SteamFile), Loaded->Steam);
	UE_LOG(LogGlpAudio, Log, TEXT("recordings from %s: engine %s, %d tyre loops, %d pops, %d crashes, steam %s"), *Directory, Loaded->Engine != 0 ? TEXT("loaded") : TEXT("missing"), Loaded->Tyres.Num(), Loaded->Pops.Num(), Loaded->Crashes.Num(), Loaded->Steam.Samples.Num() > 1 ? TEXT("loaded") : TEXT("missing"));
	GameClips = Loaded;
	PopsPending.Reset();
	SynthCommand([this, Loaded]()
	{
		Clips = Loaded;
		OneShots.Reset();
	});
}

void UGlpAudio::Chime()
{
	bChimePending = true;
}

void UGlpAudio::Update(const glp_state& State, float TrainDistance, bool bInterior, float DeltaSeconds)
{
	if (!GameClips || DeltaSeconds <= 0.f)
	{
		return;
	}
	FFrame Frame;
	Frame.bChime = bChimePending;
	bChimePending = false;
	// A record lap sings the fanfare; the end of the credit the tune of the original's high-score screen, once.
	if (Event(State, GLP_EVENT_NEW_BEST_LAP))
	{
		Frame.Jingle = EJingle::Fanfare;
	}
	else if (State.phase == GLP_PHASE_FINISHED && LastPhase != GLP_PHASE_FINISHED)
	{
		Frame.Jingle = EJingle::GameOver;
	}
	LastPhase = State.phase;

	// EngineAudio.Update: the smoothed engine state the voice follows.
	// The instant replay has no engine (EngineAudio): the simulation holds its last revs through it.
	const bool bRunning = State.engine_running != 0 && State.phase != GLP_PHASE_REPLAY;
	Rpm += (FMath::Max(State.rpm, 0.f) - Rpm) * Clamp01(DeltaSeconds * 12.f);
	const float NewLoad = Clamp01(State.engine_torque / 500.f);
	Load += (NewLoad - Load) * Clamp01(DeltaSeconds * 8.f);
	Frame.Rpm = Rpm;
	Frame.Load = Load;
	Frame.bRunning = bRunning;
	Frame.bInterior = bInterior;

	Frame.bBleep = Event(State, GLP_EVENT_CHECKPOINT_PASSED) || Event(State, GLP_EVENT_LAP_COMPLETED);
	Frame.bStarter = Event(State, GLP_EVENT_ENGINE_STARTED);
	Frame.bClang = Event(State, GLP_EVENT_SIGN_HIT);
	if (Event(State, GLP_EVENT_COW_HIT))
	{
		Frame.bMoo = true;
		Frame.MooPitch = 0.9f + 0.2f * GameRandom.FRand();
	}
	// The train is heard when it runs within a couple of hundred metres.
	TrainLevel += (Clamp01(1.f - TrainDistance / 260.f) - TrainLevel) * Clamp01(DeltaSeconds * 3.f);
	Frame.TrainLevel = TrainLevel;

	// Wind grows with speed and only matters above town speeds (the tyres' roar is a recording, below).
	const float Speed = FMath::Abs(State.speed);
	Wind += (FMath::Pow(Clamp01((Speed - 12.f) / 70.f), 2.f) - Wind) * Clamp01(DeltaSeconds * 3.f);
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

	// The crash: one of the recordings, drawn at random with a spread of pitch (x/÷ 1.08) and level (+-2 dB) as the
	// Godot version's AudioStreamRandomizer plays them, with the weight of a thump under it; the burst of noise when
	// none is loaded.
	if (Event(State, GLP_EVENT_CRASHED))
	{
		if (Set.Crashes.IsEmpty())
		{
			Frame.bCrash = true;
		}
		else
		{
			Frame.CrashTake = GameRandom.RandRange(0, Set.Crashes.Num() - 1);
			Frame.CrashPitch = FMath::Exp(GameRandom.FRandRange(-1.f, 1.f) * FMath::Loge(1.08f));
			Frame.CrashGain = FMath::Pow(10.f, GameRandom.FRandRange(-2.f, 2.f) / 20.f);
		}
		Frame.Thump = 1.f;
	}
	// The shifter: a clunk on every change of gear while the car is driven (the automatic's too).
	if (State.gear != LastGear && State.phase == GLP_PHASE_DRIVING && bRunning)
	{
		Frame.bClunk = true;
	}
	LastGear = State.gear;

	// TyreAudio.Update: slip angles beyond about four degrees and slip ratios beyond fifteen percent are audible.
	float Lateral = 0.f, Longitudinal = 0.f, OffRoad = 0.f;
	int32 OnRoad = 0;
	for (int32 i = 0; i < 4; i++)
	{
		if (!State.wheel_contact[i])
		{
			continue;
		}
		if (State.wheel_surface[i] == GLP_SURFACE_ROAD)
		{
			OnRoad++;
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
	Follow(TyreRolling, FMath::Pow(Clamp01((Speed - 1.f) / 45.f), 1.1f) * OnRoad / 4.f, DeltaSeconds, 4.f, 4.f);
	// The tread's roar, its pitch rising with the speed (recorded at about motorway speed); the cornering loop,
	// pitched up as the slide deepens; the howl joins at big angles; the screech follows the speed of the rubber
	// over the road; gravel off the asphalt.
	Frame.TyreGain[0] = LoopVolume * TyreRolling * 0.55f;
	Frame.TyrePitch[0] = 0.75f + 0.5f * Clamp01(Speed / 50.f);
	Frame.TyreGain[1] = LoopVolume * TyreLateral * (1.f - 0.5f * TyreLongitudinal);
	Frame.TyrePitch[1] = 0.88f + 0.30f * TyreLateral;
	Frame.TyreGain[2] = LoopVolume * Clamp01((TyreLateral - 0.45f) / 0.55f) * 0.9f;
	Frame.TyrePitch[2] = 0.9f + 0.2f * TyreLateral;
	Frame.TyreGain[3] = LoopVolume * TyreLongitudinal * 0.9f;
	Frame.TyrePitch[3] = 0.8f + 0.5f * Clamp01(Speed / 40.f);
	Frame.TyreGain[4] = LoopVolume * TyreGrass * (0.5f + 0.5f * Clamp01(Speed / 30.f));
	Frame.TyrePitch[4] = 0.7f + 0.6f * Clamp01(Speed / 30.f);

	// Without an audio device (-nosound) the synth never starts and queued commands would only pile up.
	if (IsPlaying())
	{
		SynthCommand([this, Frame]() { Apply(Frame); });
	}
}

void UGlpAudio::Apply(const FFrame& Frame)
{
	// Levels, pitches and the revs glide to the new frame over the next buffer (OnGenerateAudio); triggers fire once.
	Current.Rpm = Frame.Rpm;
	Current.Load = Frame.Load;
	Current.bRunning = Frame.bRunning;
	Current.bInterior = Frame.bInterior;
	FMemory::Memcpy(Current.TyreGain, Frame.TyreGain, sizeof(Frame.TyreGain));
	FMemory::Memcpy(Current.TyrePitch, Frame.TyrePitch, sizeof(Frame.TyrePitch));
	Current.Wind = Frame.Wind;
	Current.TrainLevel = Frame.TrainLevel;
	if (Frame.Jingle != EJingle::None)
	{
		SongNotes = Frame.Jingle == EJingle::Fanfare ? &FanfareSong() : &GameOverSong();
		SongTime = 0.0;
		SongNext = 0;
		SongVoices.Reset();
	}
	if (Frame.bChime)
	{
		ChimeLevel = 1.f;
		ChimePhase = 0.f;
	}
	Thump = FMath::Max(Thump, Frame.Thump);
	if (Frame.bCrash)
	{
		Crash = 1.f;
	}
	if (Frame.bClunk)
	{
		ClunkLevel = 1.f;
		ClunkPhase = 0.f;
	}
	if (Frame.CrashTake != INDEX_NONE)
	{
		OneShots.Add({ Frame.CrashTake, 0.0, Frame.CrashPitch, Frame.CrashGain, 1 });
	}
	if (Frame.bBleep)
	{
		Bleep = 0.35f;
	}
	if (Frame.bStarter)
	{
		Starter = 1.f;
		StarterPhase = 0.f;
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

	// SampledEngine.Update: the DLL's voice renders the whole buffer, gliding from the previous buffer's revs.
	EngineBuffer.SetNumUninitialized(NumSamples, EAllowShrinking::No);
	const FGlpLibrary* Library = FGlpLibrary::Get();
	if (Set->Engine == 0 || !Library || Library->EngineRender(Set->Engine, EngineBuffer.GetData(), NumSamples, Current.Rpm, Current.Load, Current.bRunning ? 1 : 0, Current.bInterior ? 1 : 0, LoopVolume) != 0)
	{
		FMemory::Memzero(EngineBuffer.GetData(), NumSamples * sizeof(float));
	}

	float TyreFrom[TyreLoops];
	for (int32 v = 0; v < TyreLoops; v++)
	{
		TyreFrom[v] = TyreVoices[v].Gain;
	}
	bSteam = Set->Steam.Samples.Num() > 1;
	const float SteamFrom = SteamVoice.Gain;
	const float SteamTarget = FMath::Pow(Current.TrainLevel, 1.5f) * SteamLevel;
	// The limiter's release: a peak above the ceiling is held down and let go over about 0.15 s.
	const float Release = 1.f - FMath::Exp(-1.f / (0.15f * MixRate));
	for (int32 i = 0; i < NumSamples; i++)
	{
		const float T = static_cast<float>(i + 1) / NumSamples;
		float Tyres = 0.f;
		for (int32 v = 0; v < Set->Tyres.Num() && v < TyreLoops; v++)
		{
			const float Gain = FMath::Lerp(TyreFrom[v], Current.TyreGain[v], T);
			const FClip& Clip = Set->Tyres[v];
			Tyres += Loop(Clip, TyreVoices[v].Position, Current.TyrePitch[v] * Clip.Rate / MixRate) * Gain;
		}

		float Shots = 0.f;
		for (int32 p = OneShots.Num() - 1; p >= 0; p--)
		{
			FOneShot& Shot = OneShots[p];
			const TArray<FClip>& Bank = Shot.Bank == 1 ? Set->Crashes : Set->Pops;
			const int32 Index = static_cast<int32>(Shot.Position);
			if (!Bank.IsValidIndex(Shot.Clip) || Index + 1 >= Bank[Shot.Clip].Samples.Num())
			{
				OneShots.RemoveAt(p);
				continue;
			}
			const FClip& Clip = Bank[Shot.Clip];
			const float Fraction = static_cast<float>(Shot.Position - Index);
			Shots += FMath::Lerp(Clip.Samples[Index], Clip.Samples[Index + 1], Fraction) * Shot.Gain * (Shot.Bank == 1 ? CrashLevel : PopLevel);
			Shot.Position += Shot.Pitch * Clip.Rate / MixRate;
		}
		// The train's steam loop, as loud as the train is near.
		float Steam = 0.f;
		if (bSteam)
		{
			Steam = Loop(Set->Steam, SteamVoice.Position, Set->Steam.Rate / MixRate) * FMath::Lerp(SteamFrom, SteamTarget, T);
		}

		// Godot's master-bus limiter: instant attack, so nothing passes the ceiling, instead of clipping.
		const float Mix = EngineBuffer[i] + Tyres + Shots + Steam + Synth() * SynthLevel;
		const float Peak = FMath::Abs(Mix);
		Limiter = Peak > Limiter ? Peak : Limiter + (Peak - Limiter) * Release;
		OutAudio[i] = Limiter > Ceiling ? Mix * Ceiling / Limiter : Mix;
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
	for (int32 v = 0; v < TyreLoops; v++)
	{
		TyreVoices[v].Gain = Current.TyreGain[v];
	}
	SteamVoice.Gain = SteamTarget;
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
	if (Bleep > 0.f)
	{
		BleepPhase += 880.f / MixRate;
		Sum += FMath::Sin(BleepPhase * UE_TWO_PI) * 0.25f * FMath::Min(1.f, Bleep * 6.f);
		Bleep -= 1.f / MixRate;
	}
	Sum += Song() + ChimeSample() + StarterSample(Noise) + Moo() + Clang(Noise) + Clunk(Noise) + (bSteam ? 0.f : TrainClack(Noise)) + Road(Noise);
	return FMath::Tanh(Sum * SynthVolume);
}

float UGlpAudio::Road(float Noise)
{
	// Wind (one-pole low-passed noise) and the bump thump (a 48 Hz burst).
	WindLp += (Noise - WindLp) * WindFollow;
	const float WindNoise = WindLp * WindLevel * Current.Wind * 0.22f;
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
	return WindNoise + Bump;
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

float UGlpAudio::Clunk(float Noise)
{
	// The shifter going into a gear (EngineAudio.Clunk): a low thud of ninety milliseconds with the click of the linkage on top.
	if (ClunkLevel <= 0.f)
	{
		return 0.f;
	}
	const float T = 1.f - ClunkLevel;
	ClunkPhase += 110.f / MixRate;
	if (ClunkPhase >= 1.f)
	{
		ClunkPhase -= 1.f;
	}
	ClunkLp += (Noise - ClunkLp) * ClunkFollow;
	const float Thud = FMath::Sin(ClunkPhase * UE_TWO_PI) * FMath::Exp(-T * 28.f);
	const float Click = ClunkLp * FMath::Exp(-T * 90.f) * 0.8f;
	ClunkLevel -= 1.f / (MixRate * 0.09f);
	return (Thud + Click) * 0.28f;
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

UGlpAudio::FJingleNote UGlpAudio::JingleNote(float Beat, float Beats, int32 Midi, int32 Voice, float Level, float Bpm)
{
	// EngineAudio.N: a note at a beat, lasting beats, of a voice at a level, in a song of bpm.
	return { Beat * 60.f / Bpm, Beats * 60.f / Bpm, Midi, Voice, Level };
}

const TArray<UGlpAudio::FJingleNote>& UGlpAudio::FanfareSong()
{
	// Six seconds of fanfare in B flat (EngineAudio.BuildFanfare): the call, its answer up the triad and the held
	// chord, over a bass that marks the bars.
	static const TArray<FJingleNote> Notes = []()
	{
		constexpr float Bpm = 132.f;
		TArray<FJingleNote> List;
		const int32 Call[] = { 65, 65, 65, 70 };
		const float CallBeats[] = { 0.f, 0.5f, 1.f, 1.5f };
		const float CallLength[] = { 0.45f, 0.45f, 0.45f, 2.4f };
		for (int32 i = 0; i < 4; i++)
		{
			List.Add(JingleNote(CallBeats[i], CallLength[i], Call[i], Brass, 0.55f, Bpm));
		}
		const int32 Answer[] = { 74, 72, 70, 74, 77 };
		const float AnswerBeats[] = { 4.f, 4.5f, 5.f, 5.5f, 6.f };
		const float AnswerLength[] = { 0.45f, 0.45f, 0.45f, 0.45f, 2.f };
		for (int32 i = 0; i < 5; i++)
		{
			List.Add(JingleNote(AnswerBeats[i], AnswerLength[i], Answer[i], Brass, 0.55f, Bpm));
		}
		for (const int32 Midi : { 70, 74, 77, 82 })
		{
			List.Add(JingleNote(8.f, 4.f, Midi, Brass, 0.42f, Bpm));
		}
		for (const float Beat : { 0.f, 2.f, 4.f, 6.f, 8.f, 10.f })
		{
			List.Add(JingleNote(Beat, 1.5f, 46, Bass, 0.5f, Bpm));
		}
		return List;
	}();
	return Notes;
}

const TArray<UGlpAudio::FJingleNote>& UGlpAudio::GameOverSong()
{
	// Fourteen seconds for the end of a credit (EngineAudio.BuildGameOver): an FM lead over a walking bass in
	// A minor, hats on the eighths and a snare on two and four.
	static const TArray<FJingleNote> Notes = []()
	{
		constexpr float Bpm = 124.f;
		TArray<FJingleNote> List;
		struct FMelodyNote { float Beat; float Length; int32 Midi; };
		const FMelodyNote Melody[] =
		{
			{ 0.f, 0.5f, 69 }, { 0.5f, 0.5f, 72 }, { 1.f, 1.f, 76 }, { 2.f, 0.5f, 74 }, { 2.5f, 0.5f, 72 }, { 3.f, 1.f, 74 },
			{ 4.f, 0.5f, 72 }, { 4.5f, 0.5f, 69 }, { 5.f, 1.f, 67 }, { 6.f, 2.f, 69 },
			{ 8.f, 0.5f, 69 }, { 8.5f, 0.5f, 72 }, { 9.f, 1.f, 76 }, { 10.f, 0.5f, 77 }, { 10.5f, 0.5f, 76 }, { 11.f, 1.f, 74 },
			{ 12.f, 0.5f, 72 }, { 12.5f, 0.5f, 74 }, { 13.f, 0.5f, 72 }, { 13.5f, 0.5f, 71 }, { 14.f, 2.f, 69 },
			{ 16.f, 0.5f, 76 }, { 16.5f, 0.5f, 77 }, { 17.f, 1.f, 79 }, { 18.f, 0.5f, 77 }, { 18.5f, 0.5f, 76 }, { 19.f, 1.f, 74 },
			{ 20.f, 0.5f, 72 }, { 20.5f, 0.5f, 74 }, { 21.f, 1.f, 76 }, { 22.f, 2.f, 71 },
			{ 24.f, 0.5f, 72 }, { 24.5f, 0.5f, 74 }, { 25.f, 1.f, 76 }, { 26.f, 0.5f, 74 }, { 26.5f, 0.5f, 72 }, { 27.f, 1.f, 71 },
			{ 28.f, 1.f, 72 }, { 29.f, 1.f, 71 }, { 30.f, 2.f, 69 },
		};
		for (const FMelodyNote& Note : Melody)
		{
			List.Add(JingleNote(Note.Beat, Note.Length, Note.Midi, Lead, 0.5f, Bpm));
		}
		const int32 Roots[] = { 45, 41, 48, 43, 45, 41, 48, 43 };
		for (int32 Bar = 0; Bar < 8; Bar++)
		{
			const int32 Root = Bar == 7 ? 43 : Roots[Bar];
			List.Add(JingleNote(Bar * 4.f, 0.9f, Root, Bass, 0.6f, Bpm));
			List.Add(JingleNote(Bar * 4.f + 1.5f, 0.4f, Root + 7, Bass, 0.45f, Bpm));
			List.Add(JingleNote(Bar * 4.f + 2.f, 0.9f, Root, Bass, 0.6f, Bpm));
			List.Add(JingleNote(Bar * 4.f + 3.f, 0.4f, Root + 12, Bass, 0.4f, Bpm));
		}
		List.Add(JingleNote(32.f, 2.f, 45, Bass, 0.6f, Bpm));
		for (int32 Eighth = 0; Eighth < 64; Eighth++)
		{
			List.Add(JingleNote(Eighth * 0.5f, 0.1f, 0, Hat, Eighth % 2 == 0 ? 0.18f : 0.12f, Bpm));
		}
		for (int32 Bar = 0; Bar < 8; Bar++)
		{
			List.Add(JingleNote(Bar * 4.f + 1.f, 0.2f, 0, Snare, 0.35f, Bpm));
			List.Add(JingleNote(Bar * 4.f + 3.f, 0.2f, 0, Snare, 0.35f, Bpm));
		}
		return List;
	}();
	return Notes;
}

float UGlpAudio::Song()
{
	// EngineAudio.Song: notes start on their beat and sound through their voice's envelope.
	if (!SongNotes)
	{
		return 0.f;
	}
	const TArray<FJingleNote>& Notes = *SongNotes;
	SongTime += 1.0 / MixRate;
	while (SongNext < Notes.Num() && Notes[SongNext].Start <= SongTime)
	{
		FJingleVoice& Voice = SongVoices.AddDefaulted_GetRef();
		Voice.Note = Notes[SongNext];
		SongNext++;
	}
	float Sum = 0.f;
	for (int32 i = SongVoices.Num() - 1; i >= 0; i--)
	{
		FJingleVoice& V = SongVoices[i];
		const FJingleNote& Note = V.Note;
		V.Age += 1.f / MixRate;
		if (V.Age > Note.Length + 0.4f)
		{
			SongVoices.RemoveAtSwap(i);
			continue;
		}
		const float Frequency = 440.f * FMath::Pow(2.f, (Note.Midi - 69) / 12.f);
		const float Release = V.Age > Note.Length ? FMath::Exp(-(V.Age - Note.Length) * 14.f) : 1.f;
		float Value;
		switch (Note.Voice)
		{
		case Brass:
		{
			// A sawtooth through a low-pass that opens with a quick attack: the brass patch of the FM chips.
			V.Phase += Frequency / MixRate;
			if (V.Phase >= 1.0)
			{
				V.Phase -= 1.0;
			}
			const float Saw = static_cast<float>(V.Phase * 2.0 - 1.0);
			const float Open = FMath::Min(V.Age / 0.05f, 1.f) * (0.55f + 0.45f * FMath::Exp(-V.Age * 2.5f));
			V.Lowpass += (Saw - V.Lowpass) * (0.12f + 0.55f * Open);
			Value = V.Lowpass * FMath::Min(V.Age / 0.02f, 1.f) * Release;
			break;
		}
		case Lead:
		{
			// Two-operator FM, modulator at three times the pitch, its index falling away: a bright electric piano.
			V.ModPhase += Frequency * 3.f / MixRate;
			V.Phase += Frequency / MixRate;
			const float Index = 2.2f * FMath::Exp(-V.Age * 5.f) + 0.2f;
			Value = FMath::Sin(static_cast<float>(V.Phase * UE_TWO_PI) + Index * FMath::Sin(static_cast<float>(V.ModPhase * UE_TWO_PI)));
			Value *= FMath::Min(V.Age / 0.006f, 1.f) * FMath::Exp(-V.Age * 1.6f) * Release;
			break;
		}
		case Bass:
		{
			V.ModPhase += Frequency / MixRate;
			V.Phase += Frequency / MixRate;
			const float Index = 1.4f * FMath::Exp(-V.Age * 7.f) + 0.3f;
			Value = FMath::Sin(static_cast<float>(V.Phase * UE_TWO_PI) + Index * FMath::Sin(static_cast<float>(V.ModPhase * UE_TWO_PI)));
			Value *= FMath::Min(V.Age / 0.005f, 1.f) * FMath::Exp(-V.Age * 2.2f) * Release;
			break;
		}
		case Hat:
			Value = Random.FRandRange(-1.f, 1.f) * FMath::Exp(-V.Age * 60.f);
			break;
		default:
		{
			V.Phase += 185.f / MixRate;
			Value = Random.FRandRange(-1.f, 1.f) * FMath::Exp(-V.Age * 18.f) + FMath::Sin(static_cast<float>(V.Phase * UE_TWO_PI)) * FMath::Exp(-V.Age * 30.f) * 0.8f;
			break;
		}
		}
		Sum += Value * Note.Level;
	}
	if (SongNext >= Notes.Num() && SongVoices.Num() == 0)
	{
		SongNotes = nullptr;
	}
	return Sum * 0.35f;
}

float UGlpAudio::ChimeSample()
{
	// The title screen's chime: a bell of two partials that rings for half a second.
	if (ChimeLevel <= 0.f)
	{
		return 0.f;
	}
	ChimePhase += 1046.5f / MixRate;
	const float T = 1.f - ChimeLevel;
	const float Bell = FMath::Sin(ChimePhase * UE_TWO_PI) * 0.6f + FMath::Sin(ChimePhase * UE_TWO_PI * 2.76f) * 0.3f * FMath::Exp(-T * 9.f);
	ChimeLevel -= 1.f / (MixRate * 0.55f);
	return Bell * 0.3f * FMath::Exp(-T * 5.f);
}

float UGlpAudio::StarterSample(float Noise)
{
	// The starter motor at the turn of the key: a chugging pulse of low-passed noise with a whine on it for most of
	// a second, the engine catching under it as the voice comes in.
	if (Starter <= 0.f)
	{
		return 0.f;
	}
	const float T = 1.f - Starter;
	StarterPhase += (11.f + 4.f * T) / MixRate;
	if (StarterPhase >= 1.f)
	{
		StarterPhase -= 1.f;
	}
	const float Chug = 0.35f + 0.65f * FMath::Square(FMath::Max(FMath::Sin(StarterPhase * UE_TWO_PI), 0.f));
	StarterLp += (Noise - StarterLp) * 0.09f;
	const float Whine = FMath::Sin(StarterPhase * UE_TWO_PI * 18.f) * 0.25f;
	Starter -= 1.f / (MixRate * 0.85f);
	return (StarterLp * 1.8f + Whine) * Chug * 0.35f * FMath::Min(Starter * 4.f, 1.f);
}

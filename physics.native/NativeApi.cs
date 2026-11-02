using System.Numerics;
using System.Text;
using GrudgeLap.Audio;
using GrudgeLap.Physics.Race;
using GrudgeLap.Physics.Track;
using GrudgeLap.Physics.Vehicle;

namespace GrudgeLap.Physics.Native;

/// <summary>One race session behind a C handle, with the attract-mode driver that may drive it.</summary>
internal sealed class NativeSession(RaceSession session, int car)
{
    public RaceSession Session { get; } = session;
    public ScriptedDriver Driver { get; } = new();
    /// <summary>The catalogue index the session was created with.</summary>
    public int Car { get; } = car;
}

/// <summary>
/// The managed side of the C interface: everything Exports does, without the native calling convention, so the
/// tests can drive it. Each function maps one glp_* function of include/grudgelap_physics.h.
/// </summary>
internal static class NativeApi
{
    public const int AbiVersion = 8;
    public const int TrafficFlag = 1;
    public const int SpeedRouteFlag = 2;
    /// <summary>Bits 4-5 of the flags: the credit's weather (0 dry, 1 overcast, 2 wet, 3 rain); <see cref="RandomWeatherFlag"/> draws one instead.</summary>
    public const int WeatherShift = 4;
    public const int WeatherMask = 0x30;
    public const int RandomWeatherFlag = 0x40;

    public static int CarCount => VehicleSpec.Catalogue.Length;

    /// <summary>
    /// The engine voice of a car from the grain banks in <paramref name="folder"/>: engine_drive.wav/.json, and
    /// engine_idle and engine_overrun .wav/.json when the folder has them (tools/make-engine-bank.py writes the pairs).
    /// </summary>
    /// <remarks>
    /// glp_engine_create names no car, so the car is found by the folder's name (its <see cref="EngineSpec.SoundFolder"/>)
    /// and the voice is made as the Godot version's SampledEngine makes it: voiced for that car's body and trimmed over
    /// its redline. Without that the flat-twelve and the V8 were voiced as front-engined hatchbacks (a 2.2 kHz cabin
    /// booming at 75 Hz where theirs is 4 kHz at 132 Hz) and every car trimmed over its recording's revs (0.20 review). A
    /// folder no car uses gets a front-engined hatchback's voicing, trimmed over its recording.
    /// </remarks>
    public static GranularEngine CreateEngine(string folder, int cylinders)
    {
        GrainBank? Bank(string name)
        {
            var wav = Path.Combine(folder, name + ".wav");
            var marks = Path.Combine(folder, name + ".json");
            return File.Exists(wav) && File.Exists(marks) ? GrainBank.FromFiles(wav, marks) : null;
        }
        var drive = Bank("engine_drive") ?? throw new FileNotFoundException($"no engine_drive.wav and .json in {folder}");
        var car = CarOfSoundFolder(folder);
        var voicing = car is null ? EngineVoicing.FrontEngined : VoicingOf(car);
        return new GranularEngine(Bank("engine_idle"), drive, cylinders, voicing, car?.Engine.RedlineRpm ?? 0f, overrun: Bank("engine_overrun"));
    }

    /// <summary>
    /// The engine voice of the catalogue's car <paramref name="car"/> from the grain banks in <paramref name="folder"/>
    /// (glp_engine_create_for_car, cars13): voiced for that car's body and trimmed over its redline, with its cylinders.
    /// <see cref="CreateEngine"/> finds the car by the folder's name, which names one car only while every car has its
    /// own banks; the five cars of cars13 play the nearest existing banks until theirs land (the Bravo HGT's five and
    /// the Bravo 1.6's four the hot hatch's, the 458's V8 the V8's, the Leon Cupra's turbo four the rally hatch's, the
    /// BRZ's boxer the V6 hatch's), and by the folder alone they would be voiced and trimmed as the first car of that
    /// folder.
    /// </summary>
    public static GranularEngine CreateEngineForCar(string folder, int car)
    {
        var spec = Spec(car);
        GrainBank? Bank(string name)
        {
            var wav = Path.Combine(folder, name + ".wav");
            var marks = Path.Combine(folder, name + ".json");
            return File.Exists(wav) && File.Exists(marks) ? GrainBank.FromFiles(wav, marks) : null;
        }
        var drive = Bank("engine_drive") ?? throw new FileNotFoundException($"no engine_drive.wav and .json in {folder}");
        return new GranularEngine(Bank("engine_idle"), drive, spec.Engine.Cylinders, VoicingOf(spec), spec.Engine.RedlineRpm, overrun: Bank("engine_overrun"));
    }

    /// <summary>The catalogue's car whose engine sounds from <paramref name="folder"/> (by its last name), if any.</summary>
    internal static VehicleSpec? CarOfSoundFolder(string folder)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
        return VehicleSpec.Catalogue.FirstOrDefault(c => string.Equals(c.Engine.SoundFolder, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// How a car's body passes its engine's sound: the same as the Godot version's SampledEngine.VoicingOf (keep the two
    /// alike; the audio library does not know the cars, and the game does not reference this library).
    /// </summary>
    internal static EngineVoicing VoicingOf(VehicleSpec spec) => spec.Style switch
    {
        CarStyle.FlatTwelve or CarStyle.V8 or CarStyle.FlatPlaneV8 => new EngineVoicing(EnginePlacement.Mid, 1.3f, TailpipeRadius: 0.035f),
        CarStyle.Saloon or CarStyle.Fleet => new EngineVoicing(EnginePlacement.Front, 1.9f),
        CarStyle.Estate => new EngineVoicing(EnginePlacement.Front, 2.8f),
        _ => new EngineVoicing(EnginePlacement.Front, 2.3f),
    };

    public static string CarName(int car) => Spec(car).Name;

    /// <remarks>cg_height is where the game puts the body: the springs' rest length less the static sag (the
    /// car's weight on four springs), the wheel radius, and the hub's height under the centre of gravity.</remarks>
    public static NativeCarSpec CarSpec(int car)
    {
        var spec = Spec(car);
        var sag = spec.MassKg * MathUtil.Gravity / 4f / spec.SuspensionStiffness;
        return new NativeCarSpec
        {
            Style = (int)spec.Style,
            ForwardGears = spec.ForwardGearCount,
            SpeedoMaxMph = spec.SpeedoMaxMph,
            RedlineRpm = spec.Engine.RedlineRpm,
            WheelRadius = spec.WheelRadius,
            HalfTrack = spec.HalfTrack,
            FrontAxle = spec.FrontAxle,
            RearAxle = spec.RearAxle,
            HubHeight = spec.HubHeight,
            SuspensionRest = spec.SuspensionRest,
            CgHeight = spec.SuspensionRest + spec.WheelRadius - sag - spec.HubHeight,
        };
    }

    /// <param name="flags">TrafficFlag for the drones of the original; without it the road is the player's.
    /// SpeedRouteFlag sends the attract-mode driver down the speed route (past the level crossing) instead of the stunt route.
    /// Bits 4-5 name the weather (WeatherShift), or RandomWeatherFlag has the core draw one as the Godot version does.</param>
    public static NativeSession Create(int car, int flags)
    {
        var weather = (flags & RandomWeatherFlag) != 0 ? WeatherModel.Draw(Random.Shared) : (Weather)((flags & WeatherMask) >> WeatherShift);
        if (!Enum.IsDefined(weather)) throw new ArgumentOutOfRangeException(nameof(flags), flags, "unknown weather");
        var options = ((flags & TrafficFlag) != 0 ? RaceOptions.Medium : RaceOptions.MediumNoTraffic) with { Weather = weather };
        var native = new NativeSession(new RaceSession(OriginalInspiredTrack.Build(), Spec(car), options), car);
        native.Driver.PreferStunt = (flags & SpeedRouteFlag) == 0;
        return native;
    }

    public static void ChooseTransmission(NativeSession native, int mode)
    {
        if (!Enum.IsDefined((TransmissionMode)mode)) throw new ArgumentOutOfRangeException(nameof(mode), mode, "unknown transmission mode");
        native.Session.ChooseTransmission((TransmissionMode)mode);
    }

    public static void Abort(NativeSession native) => native.Session.Abort();

    public static NativeInputs ScriptedInputs(NativeSession native) => ToNative(native.Driver.Next(native.Session));

    public static void Step(NativeSession native, in NativeInputs inputs, float dt) => native.Session.Step(FromNative(inputs), dt);

    public static unsafe NativeState State(NativeSession native)
    {
        var session = native.Session;
        var car = session.Car;
        var engine = car.Drivetrain.Engine;
        var state = new NativeState
        {
            Speed = car.Speed,
            SteerAngle = car.SteerAngle,
            Rpm = engine.Rpm,
            Gear = car.Drivetrain.Gear,
            Phase = (int)session.Phase,
            Lap = session.Lap,
            OnRoad = car.OnRoad ? 1 : 0,
            Airborne = car.Airborne ? 1 : 0,
            TimeRemaining = session.TimeRemaining,
            LapTime = session.LapTime,
            BestLapTime = session.BestLapTime,
            Score = session.Score,
            HubHeight = car.Spec.HubHeight,
            SuspensionRest = car.Spec.SuspensionRest,
            Car = native.Car,
            ForwardGears = car.Spec.ForwardGearCount,
            Transmission = (int)car.Drivetrain.Mode,
            EngineRunning = engine.Running ? 1 : 0,
            EngineTorque = engine.LastTorque,
            LateralAcceleration = car.LateralAcceleration,
            LongitudinalAcceleration = car.LongitudinalAcceleration,
            Route = (int)session.Route,
            OffRoad = session.OffRoad ? 1 : 0,
            OffRoadTimer = session.OffRoadTimer,
            WrongWay = session.WrongWay ? 1 : 0,
            CrashCount = session.CrashCount,
            LastCrash = (int)session.LastCrash,
            LastSoftHit = session.LastSoftHit,
            GrudgeMatch = session.GrudgeMatch ? 1 : 0,
            GhostVisible = session.GhostVisible ? 1 : 0,
            GhostSteer = session.GhostSteer,
            ReplayTime = session.ReplayTime,
            ReplaySeconds = session.Options.ReplaySeconds,
            Elapsed = session.Elapsed,
            TrainRunning = session.Track.Train is { Running: true } ? 1 : 0,
            CreditBestLap = session.CreditBestLap,
            CreditBestRoute = (int)session.CreditBestRoute,
            Damage = session.Damage,
            KnockKind = (int)session.LastKnock.Kind,
            KnockSpeed = session.LastKnock.Speed,
            KnockIndex = session.LastKnock.Index,
            CowMooed = session.Herd.Mooed,
            Weather = (int)session.Weather,
            Wetness = WeatherModel.Wetness(session.Weather),
        };
        Write(state.KnockPoint, session.LastKnock.Point);
        Write(state.KnockNormal, session.LastKnock.Normal);
        foreach (var e in session.Events) state.Events |= 1u << (int)e;
        Write(state.Position, car.Position);
        Write(state.Orientation, car.Orientation);
        Write(state.Velocity, car.Velocity);
        Write(state.GhostPosition, session.GhostPosition);
        Write(state.GhostOrientation, session.GhostOrientation);
        // The instant replay scrubs through the recorded poses over its length, as the game shows it.
        var replay = session.Replay;
        var pose = replay.Count > 1
            ? replay[(int)(Math.Clamp(session.ReplayTime / session.Options.ReplaySeconds, 0f, 1f) * (replay.Count - 1))]
            : new PoseSample(0f, car.Position, car.Orientation, car.SteerAngle);
        Write(state.ReplayPosition, pose.Position);
        Write(state.ReplayOrientation, pose.Orientation);
        state.ReplaySteer = pose.SteerAngle;
        for (var i = 0; i < 4; i++)
        {
            var wheel = car.Wheels[i];
            state.WheelSpin[i] = wheel.SpinAngle;
            state.WheelCompression[i] = wheel.InContact ? wheel.Compression : 0f;
            state.WheelContact[i] = wheel.InContact ? 1 : 0;
            state.WheelSurface[i] = wheel.InContact ? (int)wheel.Surface : (int)SurfaceType.None;
            state.WheelSlipAngle[i] = wheel.SlipAngle;
            state.WheelSlipRatio[i] = wheel.SlipRatio;
        }
        return state;
    }

    /// <summary>Copies up to output.Length traffic cars; returns how many there are.</summary>
    public static unsafe int Traffic(NativeSession native, Span<NativeBody> output)
    {
        var cars = native.Session.Traffic?.Cars;
        if (cars is null) return 0;
        for (var i = 0; i < cars.Count && i < output.Length; i++)
        {
            fixed (NativeBody* body = &output[i])
            {
                Write(body->Position, cars[i].Position);
                Write(body->Orientation, cars[i].Orientation);
                body->Speed = cars[i].Speed;
                body->Colour = cars[i].Colour;
            }
        }
        return cars.Count;
    }

    /// <summary>
    /// The cars of the train, standing on the ground (the terrain height at each, as the game draws them) and
    /// facing along the line; they stay where they are while the train waits off the circuit.
    /// </summary>
    public static unsafe int Train(NativeSession native, Span<NativeBody> output)
    {
        var track = native.Session.Track;
        if (track.Train is not { } train) return 0;
        var direction = train.Direction;
        var facing = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.Atan2(direction.X, direction.Z));
        for (var i = 0; i < train.Cars.Length && i < output.Length; i++)
        {
            var c = train.Cars[i];
            fixed (NativeBody* body = &output[i])
            {
                Write(body->Position, new Vector3(c.X, track.Terrain.Height(c.X, c.Z), c.Z));
                Write(body->Orientation, facing);
                body->Speed = train.Running ? train.Speed : 0f;
                body->Colour = i == 0 ? 0 : 1;
            }
        }
        return train.Cars.Length;
    }

    /// <summary>The cows as they stand now: on the ground, turned by their yaw (the head along +x), the prop index as the colour.</summary>
    public static unsafe int Cows(NativeSession native, Span<NativeBody> output)
    {
        var cows = native.Session.Herd.Cows;
        for (var i = 0; i < cows.Count && i < output.Length; i++)
        {
            var cow = cows[i];
            fixed (NativeBody* body = &output[i])
            {
                Write(body->Position, cow.Position);
                Write(body->Orientation, Quaternion.CreateFromAxisAngle(Vector3.UnitY, cow.YawDeg * MathUtil.Deg2Rad));
                body->Speed = cow.Velocity.Length();
                body->Colour = cow.Prop;
            }
        }
        return cows.Count;
    }

    public static unsafe int Props(NativeSession native, Span<NativeProp> output)
    {
        var session = native.Session;
        var props = session.Track.Props;
        for (var i = 0; i < props.Count && i < output.Length; i++)
        {
            var prop = props[i];
            fixed (NativeProp* p = &output[i])
            {
                p->Kind = (int)prop.Kind;
                Write(p->Position, prop.Position);
                p->YawDegrees = prop.YawDeg;
                Write(p->Size, prop.Size);
                p->Knocked = session.KnockedSigns.Contains(i) ? 1 : 0;
            }
        }
        return props.Count;
    }

    public static byte[] BestLap(NativeSession native) => native.Session.BestLap?.Serialize() ?? [];

    public static void LoadBestLap(NativeSession native, ReadOnlySpan<byte> data)
    {
        var recording = LapRecording.Deserialize(data.ToArray()) ?? throw new ArgumentException("not a best-lap recording", nameof(data));
        native.Session.LoadBestLap(recording);
    }

    /// <summary>Writes text as UTF-8 into a C buffer: cut to capacity - 1 bytes on a whole character, always
    /// terminated. Returns the text's full length in bytes so a caller can retry with a larger buffer.</summary>
    public static unsafe int CopyText(string text, byte* buffer, int capacity)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        if (buffer == null || capacity <= 0) return bytes.Length;
        var count = Math.Min(bytes.Length, capacity - 1);
        while (count > 0 && count < bytes.Length && (bytes[count] & 0xC0) == 0x80) count--;  // not inside a character
        bytes.AsSpan(0, count).CopyTo(new Span<byte>(buffer, capacity));
        buffer[count] = 0;
        return bytes.Length;
    }

    private static VehicleSpec Spec(int car)
    {
        var catalogue = VehicleSpec.Catalogue;
        if (car < 0 || car >= catalogue.Length) throw new ArgumentOutOfRangeException(nameof(car), car, $"there are {catalogue.Length} cars");
        return catalogue[car];
    }

    private static unsafe void Write(float* target, Vector3 v)
    {
        target[0] = v.X;
        target[1] = v.Y;
        target[2] = v.Z;
    }

    private static unsafe void Write(float* target, Quaternion q)
    {
        target[0] = q.X;
        target[1] = q.Y;
        target[2] = q.Z;
        target[3] = q.W;
    }

    private static DriverInputs FromNative(in NativeInputs n) => new()
    {
        Steer = n.Steer,
        Throttle = n.Throttle,
        Brake = n.Brake,
        Clutch = n.Clutch,
        Gear = n.Gear,
        Ignition = n.Ignition != 0,
        ShiftUp = n.ShiftUp != 0,
        ShiftDown = n.ShiftDown != 0,
    };

    private static NativeInputs ToNative(in DriverInputs d) => new()
    {
        Steer = d.Steer,
        Throttle = d.Throttle,
        Brake = d.Brake,
        Clutch = d.Clutch,
        Gear = d.Gear,
        Ignition = d.Ignition ? 1 : 0,
        ShiftUp = d.ShiftUp ? 1 : 0,
        ShiftDown = d.ShiftDown ? 1 : 0,
    };
}

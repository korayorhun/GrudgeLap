using System.Runtime.InteropServices;
using System.Text;
using GrudgeLap.Physics.Native;
using GrudgeLap.Physics.Race;
using GrudgeLap.Physics.Track;
using GrudgeLap.Physics.Vehicle;
using Xunit;
using Xunit.Abstractions;

namespace GrudgeLap.Physics.Tests;

/// <summary>The C interface of physics.native: its managed side, and the published NativeAOT library when built.</summary>
public sealed unsafe class NativeApiTests
{
    private const float Dt = 1f / 60f;
    private readonly ITestOutputHelper _out;

    public NativeApiTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void ScriptedDriverStartsTheEngineAndDrivesOff()
    {
        var native = NativeApi.Create(0, 0);
        NativeApi.ChooseTransmission(native, (int)TransmissionMode.Automatic);
        var start = NativeApi.State(native);
        var events = 0u;
        for (var i = 0; i < 60 * 12; i++)
        {
            NativeApi.Step(native, NativeApi.ScriptedInputs(native), Dt);
            events |= NativeApi.State(native).Events;
        }
        var state = NativeApi.State(native);
        var dx = state.Position[0] - start.Position[0];
        var dz = state.Position[2] - start.Position[2];
        var travelled = MathF.Sqrt(dx * dx + dz * dz);
        _out.WriteLine($"after 12 s: phase {(SessionPhase)state.Phase}, {state.Speed * 3.6f:F0} km/h, gear {state.Gear}, {state.Rpm:F0} rpm, {travelled:F0} m");
        Assert.Equal((int)SessionPhase.Driving, state.Phase);
        Assert.True(travelled > 100f, $"travelled only {travelled} m");
        Assert.True(state.Gear >= 2);
        Assert.Equal(1, state.OnRoad);
        Assert.Equal(1, state.EngineRunning);
        Assert.Equal((int)TransmissionMode.Automatic, state.Transmission);
        Assert.Equal(VehicleSpec.Catalogue[0].ForwardGearCount, state.ForwardGears);
        Assert.Equal(VehicleSpec.Catalogue[0].HubHeight, state.HubHeight);
        Assert.Equal((int)SurfaceType.Road, state.WheelSurface[0]);
        Assert.NotEqual(0u, events & (1u << (int)RaceEvent.EngineStarted));
        Assert.True(state.Elapsed > 11f);
    }

    [Fact]
    public void TrafficAndTrainAndPropsAreReported()
    {
        var quiet = NativeApi.Create(0, 0);
        Assert.Equal(0, NativeApi.Traffic(quiet, []));
        var busy = NativeApi.Create(1, NativeApi.TrafficFlag);
        var count = NativeApi.Traffic(busy, []);
        Assert.Equal(new RaceOptions().TrafficSameDirection + new RaceOptions().TrafficOncoming, count);
        var cars = new NativeBody[count];
        Assert.Equal(count, NativeApi.Traffic(busy, cars));
        Assert.Contains(cars, c => c.Position[0] != 0f || c.Position[2] != 0f);

        var train = new NativeBody[8];
        var wagons = NativeApi.Train(busy, train);
        Assert.True(wagons > 0);
        Assert.Equal(0, train[0].Colour);
        Assert.Equal(1, train[wagons - 1].Colour);

        var track = OriginalInspiredTrack.Build();
        var props = new NativeProp[NativeApi.Props(busy, [])];
        Assert.Equal(track.Props.Count, NativeApi.Props(busy, props));
        Assert.Equal((int)track.Props[0].Kind, props[0].Kind);
        Assert.Equal(track.Props[0].Position.X, props[0].Position[0]);
        Assert.All(props, p => Assert.Equal(0, p.Knocked));
    }

    [Fact]
    public void BestLapRoundTripsAsBytes()
    {
        var native = NativeApi.Create(0, 0);
        Assert.Empty(NativeApi.BestLap(native));
        var samples = Enumerable.Range(0, 10)
            .Select(i => new PoseSample(i * 0.5f, new System.Numerics.Vector3(i, 0.6f, i * 2f), System.Numerics.Quaternion.Identity, 0f))
            .ToList();
        var recording = new LapRecording(RouteKind.Stunt, 61.5f, samples);
        var other = NativeApi.Create(0, 0);
        NativeApi.LoadBestLap(other, recording.Serialize());
        Assert.Equal(1, NativeApi.State(other).GrudgeMatch);
        Assert.Equal(61.5f, NativeApi.State(other).BestLapTime);
        Assert.Equal(recording.Serialize(), NativeApi.BestLap(other));
        Assert.Throws<ArgumentException>(() => NativeApi.LoadBestLap(other, [1, 2, 3]));
    }

    [Fact]
    public void RejectsAnUnknownCarAndTransmission()
    {
        Assert.Equal(VehicleSpec.Catalogue.Length, NativeApi.CarCount);
        Assert.Equal(VehicleSpec.Catalogue[1].Name, NativeApi.CarName(1));
        var v8 = NativeApi.CarSpec(1);
        Assert.Equal((int)CarStyle.V8, v8.Style);
        Assert.Equal(VehicleSpec.Catalogue[1].Engine.RedlineRpm, v8.RedlineRpm);
        // The flat-12 rests with its centre of gravity about 0.61 m over the road, as the game's camera rig assumes.
        Assert.InRange(NativeApi.CarSpec(0).CgHeight, 0.58f, 0.64f);
        Assert.Throws<ArgumentOutOfRangeException>(() => NativeApi.Create(VehicleSpec.Catalogue.Length, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => NativeApi.ChooseTransmission(NativeApi.Create(0, 0), 7));
    }

    [Fact]
    public void TextIsCutOnAWholeCharacterAndTerminated()
    {
        var buffer = stackalloc byte[6];
        // "ŞŞŞ" is six bytes; five fit, so the cut falls back to four (two whole characters).
        Assert.Equal(6, NativeApi.CopyText("ŞŞŞ", buffer, 6));
        Assert.Equal("ŞŞ", Encoding.UTF8.GetString(buffer, 4));
        Assert.Equal(0, buffer[4]);
        Assert.Equal(11, NativeApi.CopyText("TURN KEY ON", null, 0));
    }

    /// <summary>
    /// The published library (artifacts/physics-native, `dotnet publish physics.native -c Release -r win-x64
    /// -o artifacts/physics-native`) driven through its C entry points must step exactly like the managed side:
    /// same inputs, same bytes of state and traffic. That also proves the C structs' layouts. Skipped (with a
    /// note) where the library has not been published, as on the CI runner.
    /// </summary>
    [Fact]
    public void PublishedLibraryStepsLikeTheManagedSide()
    {
        var path = FindPublishedLibrary();
        if (path is null)
        {
            _out.WriteLine("skipped: artifacts/physics-native/GrudgeLap.Physics.Native.dll is not published");
            return;
        }
        var library = NativeLibrary.Load(path);
        try
        {
            var abi = (delegate* unmanaged[Cdecl]<int>)NativeLibrary.GetExport(library, "glp_abi_version");
            Assert.Equal(NativeApi.AbiVersion, abi());
            var create = (delegate* unmanaged[Cdecl]<int, int, nint>)NativeLibrary.GetExport(library, "glp_create");
            var destroy = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(library, "glp_destroy");
            var choose = (delegate* unmanaged[Cdecl]<nint, int, int>)NativeLibrary.GetExport(library, "glp_choose_transmission");
            var scripted = (delegate* unmanaged[Cdecl]<nint, NativeInputs*, int>)NativeLibrary.GetExport(library, "glp_scripted_inputs");
            var step = (delegate* unmanaged[Cdecl]<nint, NativeInputs*, float, int>)NativeLibrary.GetExport(library, "glp_step");
            var getState = (delegate* unmanaged[Cdecl]<nint, NativeState*, int>)NativeLibrary.GetExport(library, "glp_get_state");
            var message = (delegate* unmanaged[Cdecl]<nint, byte*, int, int>)NativeLibrary.GetExport(library, "glp_message");
            var traffic = (delegate* unmanaged[Cdecl]<nint, NativeBody*, int, int>)NativeLibrary.GetExport(library, "glp_traffic");
            var carName = (delegate* unmanaged[Cdecl]<int, byte*, int, int>)NativeLibrary.GetExport(library, "glp_car_name");

            Assert.Equal(0, create(99, 0));
            var name = stackalloc byte[64];
            carName(1, name, 64);
            Assert.Equal(VehicleSpec.Catalogue[1].Name, Marshal.PtrToStringUTF8((nint)name));
            var session = create(0, NativeApi.TrafficFlag);
            Assert.NotEqual(0, session);
            var managed = NativeApi.Create(0, NativeApi.TrafficFlag);
            try
            {
                Assert.Equal(0, choose(session, (int)TransmissionMode.Automatic));
                NativeApi.ChooseTransmission(managed, (int)TransmissionMode.Automatic);
                var buffer = stackalloc byte[64];
                message(session, buffer, 64);
                Assert.Equal(managed.Session.Message, Marshal.PtrToStringUTF8((nint)buffer));
                for (var i = 0; i < 60 * 6; i++)
                {
                    NativeInputs inputs;
                    Assert.Equal(0, scripted(session, &inputs));
                    Assert.Equal(0, step(session, &inputs, Dt));
                    NativeApi.Step(managed, NativeApi.ScriptedInputs(managed), Dt);
                }
                NativeState state;
                Assert.Equal(0, getState(session, &state));
                var expected = NativeApi.State(managed);
                Assert.Equal(352, sizeof(NativeState));
                Assert.Equal(36, sizeof(NativeBody));
                Assert.Equal(36, sizeof(NativeProp));
                Assert.True(new ReadOnlySpan<byte>(&state, sizeof(NativeState)).SequenceEqual(new ReadOnlySpan<byte>(&expected, sizeof(NativeState))),
                    $"native at ({state.Position[0]}, {state.Position[2]}), managed at ({expected.Position[0]}, {expected.Position[2]})");
                var cars = stackalloc NativeBody[16];
                var count = traffic(session, cars, 16);
                var expectedCars = new NativeBody[16];
                Assert.Equal(NativeApi.Traffic(managed, expectedCars), count);
                fixed (NativeBody* e = expectedCars)
                {
                    Assert.True(new ReadOnlySpan<byte>(cars, count * sizeof(NativeBody)).SequenceEqual(new ReadOnlySpan<byte>(e, count * sizeof(NativeBody))));
                }
                _out.WriteLine($"native and managed agree after 6 s: {state.Speed * 3.6f:F0} km/h, score {state.Score}, {count} traffic cars");
            }
            finally
            {
                destroy(session);
            }
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    private static string? FindPublishedLibrary()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "artifacts", "physics-native", "GrudgeLap.Physics.Native.dll");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}

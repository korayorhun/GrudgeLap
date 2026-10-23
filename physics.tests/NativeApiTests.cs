using System.Runtime.InteropServices;
using System.Text;
using GrudgeLap.Physics.Native;
using GrudgeLap.Physics.Race;
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
        var native = NativeApi.Create(0);
        NativeApi.ChooseTransmission(native, (int)TransmissionMode.Automatic);
        var start = NativeApi.State(native);
        for (var i = 0; i < 60 * 12; i++)
        {
            NativeApi.Step(native, NativeApi.ScriptedInputs(native), Dt);
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
        Assert.Equal(VehicleSpec.Catalogue[0].HubHeight, state.HubHeight);
    }

    [Fact]
    public void RejectsAnUnknownCarAndTransmission()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NativeApi.Create(VehicleSpec.Catalogue.Length));
        Assert.Throws<ArgumentOutOfRangeException>(() => NativeApi.ChooseTransmission(NativeApi.Create(0), 7));
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
    /// same inputs, same floats. That also proves glp_state's layout matches NativeState. Skipped (with a note)
    /// where the library has not been published, as on the CI runner.
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
            var create = (delegate* unmanaged[Cdecl]<int, nint>)NativeLibrary.GetExport(library, "glp_create");
            var destroy = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(library, "glp_destroy");
            var choose = (delegate* unmanaged[Cdecl]<nint, int, int>)NativeLibrary.GetExport(library, "glp_choose_transmission");
            var scripted = (delegate* unmanaged[Cdecl]<nint, NativeInputs*, int>)NativeLibrary.GetExport(library, "glp_scripted_inputs");
            var step = (delegate* unmanaged[Cdecl]<nint, NativeInputs*, float, int>)NativeLibrary.GetExport(library, "glp_step");
            var getState = (delegate* unmanaged[Cdecl]<nint, NativeState*, int>)NativeLibrary.GetExport(library, "glp_get_state");
            var message = (delegate* unmanaged[Cdecl]<nint, byte*, int, int>)NativeLibrary.GetExport(library, "glp_message");

            Assert.Equal(NativeApi.AbiVersion, abi());
            Assert.Equal(0, create(99));
            var session = create(0);
            Assert.NotEqual(0, session);
            var managed = NativeApi.Create(0);
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
                Assert.Equal(sizeof(NativeState), 152);
                Assert.True(new ReadOnlySpan<byte>(&state, sizeof(NativeState)).SequenceEqual(new ReadOnlySpan<byte>(&expected, sizeof(NativeState))),
                    $"native at ({state.Position[0]}, {state.Position[2]}), managed at ({expected.Position[0]}, {expected.Position[2]})");
                _out.WriteLine($"native and managed agree after 6 s: {state.Speed * 3.6f:F0} km/h, score {state.Score}");
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

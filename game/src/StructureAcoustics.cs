using Godot;
using GrudgeLap.Physics;
using GrudgeLap.Physics.Race;
using GrudgeLap.Physics.Track;
using GrudgeLap.Physics.Vehicle;
using SN = System.Numerics;

namespace GrudgeLap.Game;

/// <summary>
/// The sound of the structures (0.40): the echo off a deck the car passes under (an overpass, a ramp of the drawbridge)
/// and the knock of the joints where a deck begins and ends. Under a bridge a car is heard twice, the second time off
/// the deck's underside some milliseconds later, with a short ring in the space between; and every bridge has an
/// expansion joint at each end, which the wheels cross with a double knock, the front axle and then the rear. Until 0.40
/// the engine sounded under a bridge as it did in the open, and a deck began without a sound.
/// </summary>
/// <remarks>
/// The car's sounds (the engine's recordings, and the tyres' bus) play on a bus of their own, <see cref="BusName"/>, made
/// before the tyres' so they can send to it (a bus sends only to one before it): a delay with two taps and a small
/// reverb, both silent in the open. Under a deck the first tap's time is the sound's way up to the underside and back,
/// the second the way round once more, and their levels and the reverb's wet rise and fall over a tenth of a second.
/// The decks are the structures' frames, looked through every frame after a quick reject (a few thousand).
/// </remarks>
internal sealed class StructureAcoustics
{
    public const string BusName = "Car";
    /// <summary>The slab under a deck's road surface (WorldBuilder's DeckDepth) and the engine's height over the road.</summary>
    private const float Underside = 1.2f + 0.6f;
    private const float SoundSpeed = 343f;
    private const float Wheelbase = 2.5f;

    private readonly List<RoadFrame> _decks = [];
    private readonly Dictionary<TrackPath, List<float>> _joints = [];
    private float _amount;
    private float _height = 6f;
    private TrackPath? _lastPath;
    private float _lastDistance;
    private float _rearIn = -1f;
    private float _rearLevel;
    private bool _wasUnder;

    /// <summary>Automation (--pose-log): the decks entered and left and the joints knocked, in the log.</summary>
    public static bool Log { get; set; }

    public StructureAcoustics(TrackGeometry track)
    {
        foreach (var path in track.Paths)
        {
            var joints = new List<float>();
            for (var i = 0; i < path.Frames.Count; i++)
            {
                var f = path.Frames[i];
                if (IsDeck(f) && f.Surface == SurfaceType.Road) _decks.Add(f);
                if (i > 0 && IsDeck(path.Frames[i - 1]) != IsDeck(f) && f.Surface == SurfaceType.Road && path.Frames[i - 1].Surface == SurfaceType.Road)
                {
                    joints.Add((path.Frames[i - 1].Distance + f.Distance) * 0.5f);
                }
            }
            _joints[path] = joints;
        }
    }

    /// <summary>A deck: a structure's road off the ground (the loop counts; the drawbridge's open gap has no road).</summary>
    private static bool IsDeck(RoadFrame f) => f.Kind is SegmentKind.Overpass or SegmentKind.RampUp or SegmentKind.RampDown or SegmentKind.Loop;

    /// <summary>Makes the car's bus with its delay and reverb, once (the buses outlive a session), and returns its name.</summary>
    public static string EnsureBus()
    {
        if (AudioServer.GetBusIndex(BusName) >= 0) return BusName;
        AudioServer.AddBus();
        var index = AudioServer.BusCount - 1;
        AudioServer.SetBusName(index, BusName);
        AudioServer.SetBusSend(index, "Master");
        AudioServer.AddBusEffect(index, new AudioEffectDelay
        {
            Dry = 1f,
            Tap1Active = true, Tap1DelayMs = 30f, Tap1LevelDb = -80f, Tap1Pan = -0.1f,
            Tap2Active = true, Tap2DelayMs = 60f, Tap2LevelDb = -80f, Tap2Pan = 0.1f,
            FeedbackActive = false,
        });
        AudioServer.AddBusEffect(index, new AudioEffectReverb
        {
            RoomSize = 0.22f, Damping = 0.55f, Spread = 0.7f, Hipass = 0.12f, PredelayMsec = 8f, PredelayFeedback = 0.2f,
            Dry = 1f, Wet = 0f,
        });
        return BusName;
    }

    /// <summary>
    /// Once a frame: the echo's time and strength for the deck over the car, if any; returns how hard a joint the
    /// wheels have just crossed knocks (0 for none), the front axle now and the rear a wheelbase later.
    /// </summary>
    public float Update(RaceSession session, float dt, bool silent)
    {
        var car = session.Car;
        var position = car.Position;
        var gap = float.MaxValue;
        foreach (var deck in _decks)
        {
            var rel = position - deck.Origin;
            if (MathF.Abs(rel.X) > 20f || MathF.Abs(rel.Z) > 20f) continue;
            if (MathF.Abs(SN.Vector3.Dot(rel, deck.Tangent)) > 1.5f) continue;
            if (MathF.Abs(SN.Vector3.Dot(rel, deck.Lateral)) > deck.HalfWidth + 1.5f) continue;
            var over = deck.Origin.Y - position.Y;
            if (over > 2.5f && over < 30f) gap = MathF.Min(gap, over);
        }
        var target = !silent && gap < 30f ? 1f : 0f;
        if (Log && (target > 0f) != _wasUnder) GD.Print(target > 0f ? $"acoustics: under a deck {gap:0.0} m up, echo {MathF.Max(gap - Underside, 0.8f) * 2f / SoundSpeed * 1000f:0} ms" : "acoustics: out from under the deck");
        _wasUnder = target > 0f;
        _amount += (target - _amount) * MathUtil.Clamp01(dt * 10f);
        if (gap < 30f) _height = gap;
        var index = AudioServer.GetBusIndex(BusName);
        if (index >= 0 && AudioServer.GetBusEffectCount(index) >= 2
            && AudioServer.GetBusEffect(index, 0) is AudioEffectDelay delay && AudioServer.GetBusEffect(index, 1) is AudioEffectReverb reverb)
        {
            var way = MathF.Max(_height - Underside, 0.8f) * 2f / SoundSpeed * 1000f;
            delay.Tap1DelayMs = Math.Clamp(way, 4f, 170f);
            delay.Tap2DelayMs = Math.Clamp(way * 2f, 8f, 340f);
            // The reflection spreads over its way up and back: about -12 dB under a cutting's 5 m deck, -26 dB from
            // the top of the loop 25 m up; the ring of the space between falls off the same way.
            var near = Math.Clamp(2.5f / (2f * _height), 0.05f, 0.4f);
            var level = MathF.Max(_amount, 0.0001f);
            delay.Tap1LevelDb = Mathf.LinearToDb(level * near);
            delay.Tap2LevelDb = Mathf.LinearToDb(level * near * 0.4f);
            reverb.Wet = 0.22f * _amount * Math.Clamp(6f / _height, 0.2f, 1f);
        }

        // The joints: crossed by the car's place along its path since the last frame, on the same path.
        var knock = 0f;
        var path = car.Cursor.Path;
        var distance = car.Cursor.Distance;
        var speed = MathF.Abs(car.Speed);
        if (!silent && ReferenceEquals(path, _lastPath) && speed > 2f && _joints.TryGetValue(path, out var joints))
        {
            var (from, to) = distance >= _lastDistance ? (_lastDistance, distance) : (distance, _lastDistance);
            foreach (var joint in joints)
            {
                if (joint <= from || joint > to) continue;
                knock = MathUtil.Clamp01(0.25f + speed / 40f) * 0.7f;
                if (Log) GD.Print($"acoustics: joint at {joint:0} m, knock {knock:0.00}");
                _rearIn = Wheelbase / speed;
                _rearLevel = knock * 0.85f;
            }
        }
        if (_rearIn >= 0f)
        {
            _rearIn -= dt;
            if (_rearIn < 0f) knock = MathF.Max(knock, _rearLevel);
        }
        _lastPath = path;
        _lastDistance = distance;
        return knock;
    }
}

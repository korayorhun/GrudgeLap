using Godot;
using GrudgeLap.Physics.Race;
using GrudgeLap.Physics.Track;
using SN = System.Numerics;

namespace GrudgeLap.Game;

/// <summary>
/// The instant replay of a crash as drawn. The simulation stops the car where it struck and starts the replay; this
/// plays the recorded poses of the seconds before, then the crash itself, which the simulation never plays out: the
/// body dents where it hit and the parts there break off, flying with bits of trim, paint and glass; sparks burst,
/// a holed radiator steams, the glass near the blow cracks; and the car rebounds off what it hit and spins to a
/// stop, the first second in slow motion. When the replay is over the car is repaired and the debris cleared.
/// </summary>
/// <remarks>
/// The rebound is a level slide with a yaw spin from the blow's impulse acting off the centre of gravity, and a
/// pitch that rocks and settles; enough to read as an impact without a second, view-side simulation of the car.
/// In 1989 mode the replay is the cabinet's: the recorded poses only, no damage.
/// </remarks>
internal sealed class CrashScene
{
    /// <summary>Replay seconds given to the crash and what follows it, at the end of the replay.</summary>
    public const float AftermathSeconds = 2.6f;
    private const float SlowSeconds = 1.0f;        // replay seconds of slow motion right after the blow
    private const float SlowRate = 0.35f;
    private const float DentGrowth = 0.08f;        // aftermath seconds over which the dent is pressed in
    private const float SampleRate = 30f;          // RaceSession records the poses at 30 Hz

    private sealed class Piece
    {
        public required Node3D Node;
        public Vector3 Velocity;
        public Vector3 SpinAxis = Vector3.Up;
        public float SpinRate;
        public float Radius;
        public bool Resting;
    }

    private readonly CarView _car;
    private readonly RaceSession _session;
    private readonly bool _effects;
    private readonly Node3D _debris;
    private readonly Transform3D _crashPose;
    private readonly Vector3 _velocity;
    private readonly Vector3 _normal;              // level, from the car into what it hit; zero for a crash on the ground
    private readonly Vector3? _surface;            // the nearest point of what it hit, when it is known
    private readonly float _terrainAtCrash;
    private readonly float _road;                  // the road under the car's origin, in its frame
    private readonly List<Piece> _pieces = [];
    private readonly List<CpuParticles3D> _particles = [];
    private readonly Random _random = new(1984);

    private Vector3 _position;
    private Vector3 _slide;
    private float _yaw;
    private float _yawRate;
    private float _pitchAmplitude;
    private float _aftermath = -1f;
    private float _contactAt = -1f;                // aftermath seconds at which the car touches what it hit
    private bool _contact;
    private Vector3 _point;                        // the blow, in the car's frame
    private Vector3 _push;
    private float _depth;
    private float _radius;
    private float _closing;
    private int _dent = -1;
    private int _crumple = -1;
    private int _buckle = -1;

    /// <summary>True on the frame the replay reaches the blow, for the crash to be heard again.</summary>
    public bool StruckThisFrame { get; private set; }
    /// <summary>How far the replay's camera has closed in on the crash: 0 the crane's high shot, 1 close by.</summary>
    public float CloseUp { get; private set; }
    /// <summary>
    /// The way the replay's camera looks: the car's heading, and over the last second before the blow round to a
    /// side-on view of it (square to the way the car hit, on the side the crane was), held while the car spins.
    /// </summary>
    public Vector3 ViewForward { get; private set; } = Vector3.Forward;

    public CrashScene(Node3D world, CarView car, RaceSession session, bool effects)
    {
        _car = car;
        _session = session;
        _effects = effects;
        _debris = new Node3D { Name = "CrashDebris" };
        world.AddChild(_debris);
        var sim = session.Car;
        _crashPose = Convert.ToGodot(sim.Position, sim.Orientation);
        _velocity = Convert.ToGodot(sim.Velocity);
        (_normal, _surface) = Obstacle(session);
        _terrainAtCrash = session.Track.Terrain.Height(sim.Position.X, sim.Position.Z);
        _road = car.Extent.Road;
        _position = _crashPose.Origin;
    }

    /// <summary>
    /// Which way the thing struck lies from the car, level, and its nearest point: of the solid prop's box, of the
    /// drone's side or the train car's; the way the car was going, with no point, when it cannot be found; nothing
    /// for a crash on the ground (rolled over, landed hard, fell).
    /// </summary>
    private static (Vector3 Normal, Vector3? Surface) Obstacle(RaceSession session)
    {
        var car = session.Car;
        var p = car.Position;
        SN.Vector3? target = null;
        var targetRadius = 0f;
        var best = 100f;
        void Consider(SN.Vector3 point, float radius)
        {
            var d = point - p;
            d.Y = 0f;
            if (d.LengthSquared() < best)
            {
                best = d.LengthSquared();
                target = point;
                targetRadius = radius;
            }
        }
        switch (session.LastCrash)
        {
            case CrashReason.HitTree:
            case CrashReason.HitBuilding:
                foreach (var prop in session.Track.Props)
                {
                    if (!prop.Solid) continue;
                    // The nearest point of the prop's box, in its own yawed frame, back in the world.
                    var yaw = prop.YawDeg * MathF.PI / 180f;
                    var axisX = new SN.Vector3(MathF.Cos(yaw), 0f, -MathF.Sin(yaw));
                    var axisZ = new SN.Vector3(MathF.Sin(yaw), 0f, MathF.Cos(yaw));
                    var d = p - prop.Position;
                    var lx = Math.Clamp(SN.Vector3.Dot(d, axisX), -prop.Size.X * 0.5f, prop.Size.X * 0.5f);
                    var lz = Math.Clamp(SN.Vector3.Dot(d, axisZ), -prop.Size.Z * 0.5f, prop.Size.Z * 0.5f);
                    // A tree's box is wider than its trunk: the bark is where the car stops.
                    Consider(prop.Position + axisX * lx + axisZ * lz, prop.Kind == PropKind.Tree ? -0.3f : 0f);
                }
                break;
            case CrashReason.HitTraffic when session.Traffic is not null:
                foreach (var drone in session.Traffic.Cars) Consider(drone.Position, 0.95f);
                break;
            case CrashReason.HitTrain when session.Track.Train is not null:
                foreach (var wagon in session.Track.Train.Cars) Consider(wagon, Train.CarHalfWidth);
                break;
            default:
                return (Vector3.Zero, null);
        }
        var level = target is { } t ? Convert.ToGodot(t - p) : Convert.ToGodot(car.Velocity);
        level.Y = 0f;
        if (level.LengthSquared() < 1e-6f) return (Vector3.Zero, null);
        var normal = level.Normalized();
        return (normal, target is { } at ? Convert.ToGodot(at) - normal * targetRadius : null);
    }

    /// <summary>
    /// The car's pose at <paramref name="replayTime"/>: the recorded approach, then the aftermath, in which the car
    /// runs on until it touches what it hit (the simulation called the crash when it came within reach) and the
    /// blow is set off. The debris and the particles move on with the aftermath's clock, slow at first.
    /// </summary>
    public Transform3D Pose(float replayTime, float replaySeconds)
    {
        StruckThisFrame = false;
        var samples = _session.Replay;
        if (!_effects)
        {
            // The cabinet's replay: the whole recording spread over the replay, from the crane.
            var t = Math.Clamp(replayTime / replaySeconds, 0f, 1f);
            var cabinet = Sample(samples, t * (samples.Count - 1));
            ViewForward = -cabinet.Basis.Z;
            return cabinet;
        }
        var approach = replaySeconds - AftermathSeconds;
        if (replayTime < approach)
        {
            // The last seconds before the crash at their own pace, the camera closing in over the last one; the last
            // sample is the crash.
            var back = (approach - replayTime) * SampleRate;
            var pose = Sample(samples, MathF.Max(samples.Count - 1 - back, 0f));
            var t = Math.Clamp((replayTime - (approach - 1.2f)) / 1.2f, 0f, 1f);
            CloseUp = t * t * (3f - 2f * t);
            ViewForward = (-pose.Basis.Z).Slerp(SideOn(), CloseUp);
            return pose;
        }
        CloseUp = 1f;
        ViewForward = SideOn();
        var into = replayTime - approach;
        var aftermath = into < SlowSeconds ? into * SlowRate : SlowSeconds * SlowRate + (into - SlowSeconds);
        if (_aftermath < 0f)
        {
            _aftermath = 0f;
            Prepare();
        }
        var da = MathF.Max(aftermath - _aftermath, 0f);
        var rate = into < SlowSeconds ? SlowRate : 1f;
        foreach (var particles in _particles) particles.SpeedScale = rate;
        Advance(aftermath, da);
        return CarPose();
    }

    /// <summary>
    /// The camera's heading for the crash: 35 degrees off square to the way the car hit, the camera on the side of
    /// the blow, so the crumpled corner, the obstacle and the bits flying off are all in view; of the two sides, the
    /// one nearer the crane's (behind and to the left).
    /// </summary>
    private Vector3 SideOn()
    {
        var heading = -_crashPose.Basis.Z;
        var hit = _normal != Vector3.Zero ? _normal : (heading with { Y = 0f }).Normalized();
        var square = new Vector3(-hit.Z, 0f, hit.X);
        if (square.Dot(heading) < 0f) square = -square;
        // The camera stands behind its heading: turning the heading away from the blow brings the camera round towards it.
        return (square * MathF.Cos(0.61f) - hit * MathF.Sin(0.61f)).Normalized();
    }

    private static Transform3D Sample(IReadOnlyList<PoseSample> samples, float index)
    {
        var i = Math.Clamp((int)index, 0, samples.Count - 1);
        var j = Math.Min(i + 1, samples.Count - 1);
        var f = Math.Clamp(index - i, 0f, 1f);
        var a = samples[i];
        var b = samples[j];
        return Convert.ToGodot(SN.Vector3.Lerp(a.Position, b.Position, f), SN.Quaternion.Slerp(a.Orientation, b.Orientation, f));
    }

    private Transform3D CarPose()
    {
        var since = _contact ? _aftermath - _contactAt : 0f;
        var pitch = -_pitchAmplitude * MathF.Exp(-4f * since) * MathF.Sin(9f * since);
        var basis = new Basis(Vector3.Up, _yaw) * _crashPose.Basis * new Basis(Vector3.Right, pitch);
        return new Transform3D(basis, _position);
    }

    /// <summary>The ground under a point near the crash: the road's height there, following the terrain's rise and fall.</summary>
    private float Ground(Vector3 point)
    {
        var rise = _session.Track.Terrain.Height(point.X, -point.Z) - _terrainAtCrash;
        return _crashPose.Origin.Y + _road + Math.Clamp(rise, -0.6f, 0.6f);
    }

    /// <summary>
    /// Where the blow lands on the car, how deep and how wide, from what it hit and how fast; and how long the car
    /// runs on before it touches it (the gap between the car and what it hit, at the speed it was closing).
    /// </summary>
    private void Prepare()
    {
        var level = new Vector3(_velocity.X, 0f, _velocity.Z);
        _closing = _normal == Vector3.Zero ? level.Length() : MathF.Max(level.Dot(_normal), 0f);
        var (nose, tail, halfWidth, road) = _car.Extent;
        var middle = (nose + tail) * 0.5f;
        var reason = _session.LastCrash;
        if (_normal != Vector3.Zero)
        {
            var d = _crashPose.Basis.Inverse() * _normal;
            d.Y = 0f;
            d = d.Normalized();
            // The ray from the body's middle, at bonnet height, to the side of its box it leaves through.
            var tx = MathF.Abs(d.X) > 1e-4f ? halfWidth / MathF.Abs(d.X) : float.MaxValue;
            var tz = d.Z < -1e-4f ? (nose - middle) / d.Z : d.Z > 1e-4f ? (tail - middle) / d.Z : float.MaxValue;
            _point = new Vector3(0f, road + 0.58f, middle) + d * MathF.Min(tx, tz) + d * 0.04f;
            _push = -d;
            (_radius, _depth) = reason switch
            {
                CrashReason.HitTree => (0.40f, Math.Clamp(_closing * 0.020f, 0.06f, 0.55f)),
                CrashReason.HitTrain => (0.95f, 0.45f),
                CrashReason.HitTraffic => (0.65f, Math.Clamp(_closing * 0.016f, 0.05f, 0.42f)),
                _ => (0.75f, Math.Clamp(_closing * 0.015f, 0.05f, 0.42f)),
            };
        }
        else if (reason == CrashReason.RolledOver)
        {
            _point = new Vector3(0f, road + 1.38f, middle - 0.2f);
            _push = Vector3.Down;
            (_radius, _depth) = (0.95f, 0.18f);
        }
        else
        {
            // Hard landing or a fall: the nose takes it, low.
            _point = new Vector3(0f, road + 0.30f, nose + 0.05f);
            _push = new Vector3(0f, 0.45f, 1f).Normalized();
            (_radius, _depth) = (0.75f, Math.Clamp(level.Length() * 0.012f, 0.08f, 0.35f));
        }
        _slide = level;
        var gap = _surface is { } surface && _normal != Vector3.Zero ? MathF.Max((surface - _crashPose * _point).Dot(_normal), 0f) : 0f;
        _contactAt = MathF.Min(gap / MathF.Max(_closing, 2f), 0.25f);
    }

    /// <summary>
    /// The blow: the closing speed reversed at a fifth, the sliding speed halved, a spin from the impulse acting at
    /// the point struck (per unit mass, over a yaw inertia of 1.6 m^2 per kilogram) and a pitch that rocks the car;
    /// the dent, the parts and bits thrown, sparks, steam, cracked glass.
    /// </summary>
    private void Contact()
    {
        _contact = true;
        StruckThisFrame = true;
        var level = new Vector3(_velocity.X, 0f, _velocity.Z);
        var (nose, tail, _, road) = _car.Extent;
        var middle = (nose + tail) * 0.5f;
        if (_normal != Vector3.Zero)
        {
            _slide = (level - _normal * _closing) * 0.45f - _normal * (_closing * 0.22f);
            var arm = (_crashPose.Basis * _point) with { Y = 0f };
            var impulse = -_normal * (1.22f * _closing);
            _yawRate = Math.Clamp(arm.Cross(impulse).Y / 1.6f, -3.5f, 3.5f);
            var frontal = _point.Z < middle ? 1f : -1f;
            _pitchAmplitude = frontal * Math.Clamp(_closing * 0.006f, 0f, 0.14f);
        }
        else
        {
            _slide = level * 0.35f;
        }
        if (!_effects) return;

        var pose = CarPose();
        // The blow's own dent, deep and as wide as what hit; round it the panel's crumple zone gives a little over
        // a wider patch; and a frontal blow buckles the bonnet up a short way back from the nose.
        _dent = _car.Dent(_point, _push, _depth * 0.15f, _radius);
        _crumple = _car.Dent(_point, _push, 0f, _radius + 0.55f);
        if (_point.Z < middle && _normal != Vector3.Zero) _buckle = _car.Dent(new Vector3(_point.X * 0.5f, road + 0.82f, nose + 0.45f), new Vector3(0f, 1f, 0.35f), 0f, 0.45f);
        var world = pose * _point;
        var thrown = _velocity * 0.55f;
        foreach (var part in _car.BreakOff(_point, _radius * 1.1f + 0.1f, _debris)) Throw(part, thrown, 2.5f, 0.12f);
        if (_depth > 0.1f)
        {
            _car.CrackGlass(_point, _radius * 1.7f);
            // A hard frontal blow folds the scuttle and cracks the screen too.
            if (_point.Z < middle && _closing > 13f) _car.CrackGlass(new Vector3(0f, road + 0.95f, nose + (middle - nose) * 0.55f), 0.8f);
        }
        Fragments(world, thrown, Math.Clamp((int)(8f + _closing * 1.6f), 8, 54));
        // Sparks fly back off what was hit, along the push into the car.
        Sparks(world, pose.Basis * _push, _closing);
        // From under the bonnet's front edge, where the radiator sits.
        if (_point.Z < middle && _closing > 7f) Steam(new Vector3(_point.X * 0.5f, road + 0.72f, nose + 0.4f));
    }

    /// <summary>
    /// Runs the car on to the blow, presses the dent in over its first moments, then lets the car slide, spin and
    /// rock to rest, and moves the debris.
    /// </summary>
    private void Advance(float aftermath, float da)
    {
        if (da <= 0f) return;
        if (!_contact)
        {
            // Running on into what it hit, at the speed it had; the blow when it touches.
            var run = MathF.Min(aftermath, _contactAt) - _aftermath;
            _position += _slide * MathF.Max(run, 0f);
            _aftermath = aftermath;
            if (aftermath >= _contactAt) Contact();
            _position.Y = Ground(_position) - _road;
            return;
        }
        _aftermath = aftermath;
        var since = aftermath - _contactAt;
        if (_dent >= 0 && since <= DentGrowth + da)
        {
            var grown = Math.Clamp(since / DentGrowth, 0.15f, 1f);
            var ease = 1f - (1f - grown) * (1f - grown);
            _car.SetDentDepths((_dent, _depth * ease), (_crumple, _depth * 0.4f * ease), (_buckle, _depth * 0.35f * ease));
        }
        var speed = _slide.Length();
        if (speed > 1e-3f)
        {
            // Sliding on its tyres and the wreck's corners: about 0.75 g.
            var after = MathF.Max(speed - 7.5f * da, 0f);
            _position += _slide / speed * ((speed + after) * 0.5f * da);
            _slide *= after / speed;
        }
        _yaw += _yawRate * da;
        _yawRate *= MathF.Exp(-1.6f * da);
        _position.Y = Ground(_position) - _road;

        foreach (var piece in _pieces)
        {
            if (piece.Resting) continue;
            piece.Velocity += Vector3.Down * (9.81f * da);
            var p = piece.Node.Position + piece.Velocity * da;
            piece.Node.Basis = piece.Node.Basis.Rotated(piece.SpinAxis, piece.SpinRate * da);
            var ground = Ground(p) + piece.Radius;
            if (p.Y < ground)
            {
                p.Y = ground;
                if (piece.Velocity.Y < -1.2f)
                {
                    piece.Velocity = new Vector3(piece.Velocity.X * 0.55f, -piece.Velocity.Y * 0.3f, piece.Velocity.Z * 0.55f);
                    piece.SpinRate *= 0.5f;
                }
                else
                {
                    piece.Resting = true;
                }
            }
            piece.Node.Position = p;
        }
    }

    /// <summary>Sends a piece on its way: the car's momentum, a spread to the sides and upwards, a tumble.</summary>
    private void Throw(Node3D node, Vector3 thrown, float spread, float radius)
    {
        var scatter = new Vector3(Rand(-spread, spread), Rand(1.5f, 4.5f), Rand(-spread, spread));
        var axis = new Vector3(Rand(-1f, 1f), Rand(-1f, 1f), Rand(-1f, 1f));
        _pieces.Add(new Piece
        {
            Node = node,
            Velocity = thrown * Rand(0.5f, 1.1f) + scatter,
            SpinAxis = axis.LengthSquared() > 1e-4f ? axis.Normalized() : Vector3.Up,
            SpinRate = Rand(4f, 14f),
            Radius = radius,
        });
    }

    private float Rand(float from, float to) => from + (to - from) * (float)_random.NextDouble();

    /// <summary>Bits of the car: paint off the panels, black plastic of the bumpers and trim, glass.</summary>
    private void Fragments(Vector3 at, Vector3 thrown, int count)
    {
        var paint = new StandardMaterial3D { AlbedoColor = _car.Paint, Metallic = 0.2f, Roughness = 0.3f };
        var plastic = new StandardMaterial3D { AlbedoColor = new Color(0.05f, 0.05f, 0.055f), Roughness = 0.7f };
        var glass = new StandardMaterial3D { AlbedoColor = new Color(0.8f, 0.88f, 0.95f, 0.55f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Metallic = 0.5f, Roughness = 0.05f };
        for (var i = 0; i < count; i++)
        {
            var kind = i % 5;
            var material = kind < 2 ? paint : kind < 4 ? plastic : glass;
            var size = kind == 4
                ? new Vector3(Rand(0.015f, 0.05f), 0.004f, Rand(0.015f, 0.05f))
                : new Vector3(Rand(0.03f, 0.13f), Rand(0.006f, 0.02f), Rand(0.03f, 0.11f));
            var node = new MeshInstance3D { Mesh = new BoxMesh { Size = size, Material = material }, Name = "Bit" };
            _debris.AddChild(node);
            node.GlobalPosition = at + new Vector3(Rand(-0.25f, 0.25f), Rand(-0.15f, 0.25f), Rand(-0.25f, 0.25f));
            node.Basis = new Basis(new Vector3(Rand(-1f, 1f), Rand(-1f, 1f), Rand(-1f, 1f)).Normalized(), Rand(0f, MathF.Tau));
            Throw(node, thrown, 3.2f, size.Y * 0.5f + 0.005f);
        }
    }

    /// <summary>A burst of sparks from the blow, fanned out about <paramref name="away"/> and upwards.</summary>
    private void Sparks(Vector3 at, Vector3 away, float closing)
    {
        var spark = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(1f, 0.72f, 0.35f),
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
        };
        var particles = new CpuParticles3D
        {
            Name = "Sparks",
            Amount = Math.Clamp((int)(30f + closing * 3f), 30, 110),
            Lifetime = 0.45f,
            OneShot = true,
            Explosiveness = 0.92f,
            Mesh = new QuadMesh { Size = new Vector2(0.03f, 0.03f), Material = spark },
            Direction = (away + Vector3.Up * 0.6f).Normalized(),
            Spread = 70f,
            InitialVelocityMin = 3f,
            InitialVelocityMax = 4f + closing * 0.4f,
            Gravity = new Vector3(0f, -9.8f, 0f),
            ScaleAmountMin = 0.5f,
            ScaleAmountMax = 1.3f,
        };
        _debris.AddChild(particles);
        particles.GlobalPosition = at;
        particles.Emitting = true;
        _particles.Add(particles);
    }

    /// <summary>Steam from a holed radiator, rising from the front of the car and following it as it slides.</summary>
    private void Steam(Vector3 local)
    {
        // A soft round puff: white at the middle fading to nothing at the rim (a plain quad read as grey squares).
        var fade = new Gradient();
        fade.SetColor(0, new Color(1f, 1f, 1f, 1f));
        fade.SetColor(1, new Color(1f, 1f, 1f, 0f));
        var puff = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(0.94f, 0.95f, 0.97f, 0.16f),
            AlbedoTexture = new GradientTexture2D { Gradient = fade, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(0.5f, 0f), Width = 64, Height = 64 },
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
        };
        var scale = new Curve();
        scale.AddPoint(new Vector2(0f, 0.3f));
        scale.AddPoint(new Vector2(1f, 1.6f));
        var particles = new CpuParticles3D
        {
            Name = "Steam",
            Amount = 26,
            Lifetime = 1.6f,
            Mesh = new QuadMesh { Size = new Vector2(0.45f, 0.45f), Material = puff },
            Direction = Vector3.Up,
            Spread = 25f,
            InitialVelocityMin = 0.6f,
            InitialVelocityMax = 1.5f,
            Gravity = new Vector3(0f, 0.6f, 0f),
            ScaleAmountCurve = scale,
            Position = local,
        };
        _car.AddChild(particles);
        particles.Emitting = true;
        _particles.Add(particles);
    }

    /// <summary>The replay is over: the car is repaired and set down by the session, the debris goes.</summary>
    public void End()
    {
        _car.Repair();
        foreach (var particles in _particles) particles.QueueFree();
        _particles.Clear();
        _debris.QueueFree();
        _pieces.Clear();
    }
}

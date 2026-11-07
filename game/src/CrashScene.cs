using Godot;
using GrudgeLap.Physics.Race;
using GrudgeLap.Physics.Track;
using SN = System.Numerics;

namespace GrudgeLap.Game;

/// <summary>
/// The instant replay of a crash as drawn. The simulation carries the wreck on through the crash's own moment (0.35:
/// it used to stop the car where it struck and play a scripted slide): the car rebounds, tumbles or slides to rest
/// on its body, and it is all recorded. The replay plays the recorded poses of the seconds before, the crash itself
/// (the first second of it in slow motion) and the wreck coming to rest; at the blow the body dents where it hit and
/// the parts there break off, flying with bits of trim, paint and glass, sparks burst, a holed radiator steams, the
/// glass near the blow cracks, and the car explodes into fire that burns through the replay, as the cabinet's
/// fireball did. Before the replay the crash is shown for a moment from where the player sits (<see cref="Impact"/>).
/// When the replay is over the car is repaired and the debris cleared.
/// </summary>
/// <remarks>
/// In 1989 mode the replay is the cabinet's: the recorded poses only, no damage and no fire.
/// </remarks>
internal sealed class CrashScene
{
    private const float DentGrowth = 0.08f;        // seconds of the crash over which the dent is pressed in

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
    private readonly List<Piece> _pieces = [];
    private readonly List<CpuParticles3D> _particles = [];
    private readonly List<OmniLight3D> _glows = [];
    private readonly List<Node> _impactNodes = [];     // the fire and sparks of the moment of the crash, gone when the replay starts
    private readonly Random _random = new(1984);
    private PathCursor _cursor;                        // the track probe's cursor, kept near the wreck

    private float _lastGameTime = float.NaN;
    private bool _contact;
    private bool _prepared;
    private Vector3 _point;                        // the blow, in the car's frame
    private Vector3 _push;
    private float _depth;
    private float _radius;
    private float _closing;
    private int _dent = -1;
    private int _crumple = -1;
    private int _buckle = -1;

    /// <summary>True on the frame the replay reaches the blow.</summary>
    public bool StruckThisFrame { get; private set; }
    /// <summary>How far the replay's camera has closed in on the crash: 0 the crane's high shot, 1 close by.</summary>
    public float CloseUp { get; private set; }
    /// <summary>
    /// The way the replay's camera looks: the car's heading, and over the last second before the blow round to a
    /// side-on view of it (square to the way the car hit, on the side the crane was), held while the wreck settles.
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
        _cursor = sim.Cursor;
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
            case CrashReason.HitCow:
                // The cow's side, about half its width out from its middle, a metre off the ground.
                foreach (var cow in session.Herd.Cows) Consider(cow.Position + new SN.Vector3(0f, 1f, 0f), Herd.Width * 0.5f);
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
    /// The crash as it happens, before the replay (0.35): the explosion, the fire and the sparks at the blow on the
    /// car as it strikes, which the cabinet showed as a fireball over the cracked windscreen. Cleared when the replay
    /// starts (<see cref="EndImpact"/>), whose approach shows the car whole.
    /// </summary>
    public void Impact()
    {
        if (!_effects) return;
        Prepare();
        var particles = _particles.Count;
        var glows = _glows.Count;
        var debris = _debris.GetChildCount();
        Explode(FireSeat(), MathF.Max(_closing, 4f), _crashPose);
        Sparks(_crashPose * _point, _crashPose.Basis * _push, _closing);
        for (var i = particles; i < _particles.Count; i++) _impactNodes.Add(_particles[i]);
        for (var i = glows; i < _glows.Count; i++) _impactNodes.Add(_glows[i]);
        for (var i = debris; i < _debris.GetChildCount(); i++) _impactNodes.Add(_debris.GetChild(i));
    }

    /// <summary>The replay begins: the impact's fire and sparks go (the replay lights its own at the blow).</summary>
    public void EndImpact()
    {
        foreach (var node in _impactNodes)
        {
            if (node is CpuParticles3D particles) _particles.Remove(particles);
            if (node is OmniLight3D glow) _glows.Remove(glow);
            node.QueueFree();
        }
        _impactNodes.Clear();
    }

    /// <summary>
    /// The car's pose at <paramref name="replayTime"/> seconds into the playback: the recorded approach at its own
    /// pace, the first second after the blow at the slow rate, then the wreck coming to rest as recorded. The blow's
    /// effects are set off when the playback reaches the crash; the debris moves on with the playback's clock.
    /// </summary>
    public Transform3D Pose(float replayTime)
    {
        StruckThisFrame = false;
        var samples = _session.Replay;
        if (!_effects)
        {
            // The cabinet's replay: the whole recording spread over the replay, from the crane.
            var t = Math.Clamp(replayTime / _session.Options.ReplaySeconds, 0f, 1f);
            var cabinet = Sample(samples, t * (samples.Count - 1));
            ViewForward = -cabinet.Basis.Z;
            return cabinet;
        }
        var crashTime = _session.CrashTime;
        var approach = _session.Options.ReplaySeconds - _session.Options.ImpactSeconds;
        var slowSpan = RaceSession.ReplaySlowSeconds / RaceSession.ReplaySlowRate;
        float gameTime;
        if (replayTime < approach) gameTime = crashTime - approach + replayTime;
        else
        {
            var into = replayTime - approach;
            gameTime = into < slowSpan ? crashTime + into * RaceSession.ReplaySlowRate : crashTime + RaceSession.ReplaySlowSeconds + (into - slowSpan);
        }
        var pose = SampleAt(samples, gameTime);
        // The camera closes in over the last second before the blow and holds its side-on view after.
        var closing = Math.Clamp((replayTime - (approach - 1.2f)) / 1.2f, 0f, 1f);
        CloseUp = closing * closing * (3f - 2f * closing);
        ViewForward = (-pose.Basis.Z).Slerp(SideOn(), CloseUp);
        if (gameTime >= crashTime && !_contact)
        {
            _contact = true;
            StruckThisFrame = true;
            Contact(pose);
        }
        var da = float.IsNaN(_lastGameTime) ? 0f : MathF.Max(gameTime - _lastGameTime, 0f);
        _lastGameTime = gameTime;
        if (_contact)
        {
            var since = gameTime - crashTime;
            if (_dent >= 0 && since <= DentGrowth + da)
            {
                var grown = Math.Clamp(since / DentGrowth, 0.15f, 1f);
                var ease = 1f - (1f - grown) * (1f - grown);
                _car.SetDentDepths((_dent, _depth * ease), (_crumple, _depth * 0.4f * ease), (_buckle, _depth * 0.35f * ease));
            }
            foreach (var particles in _particles) particles.SpeedScale = replayTime - approach < slowSpan ? RaceSession.ReplaySlowRate : 1f;
            foreach (var glow in _glows) glow.LightEnergy = 2.6f + 1.2f * MathF.Sin(gameTime * 23f) * MathF.Sin(gameTime * 7.3f);
            AdvancePieces(da);
        }
        return pose;
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

    /// <summary>The recorded pose at a game time: the samples are a thirtieth of a second apart (RaceSession.RecordReplay).</summary>
    private static Transform3D SampleAt(IReadOnlyList<PoseSample> samples, float gameTime)
    {
        if (samples.Count == 0) return Transform3D.Identity;
        var span = samples[^1].Time - samples[0].Time;
        var index = span > 1e-4f ? (gameTime - samples[0].Time) / span * (samples.Count - 1) : 0f;
        return Sample(samples, Math.Clamp(index, 0f, samples.Count - 1));
    }

    /// <summary>The ground under a point near the crash: the road's surface where there is one, the terrain elsewhere.</summary>
    private float Ground(Vector3 point)
    {
        var cursor = _cursor;
        var hit = _session.Track.Probe(Convert.ToSim(point), ref cursor, true);
        return hit.HasSurface ? hit.SurfacePoint.Y : _session.Track.Terrain.Height(point.X, -point.Z);
    }

    /// <summary>
    /// Where the blow lands on the car, how deep and how wide, from what it hit and how fast.
    /// </summary>
    private void Prepare()
    {
        if (_prepared) return;
        _prepared = true;
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
                // A cow is soft and heavy: a wide, shallow dent in the front, and the glass above it.
                CrashReason.HitCow => (0.85f, Math.Clamp(_closing * 0.010f, 0.05f, 0.28f)),
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
    }

    /// <summary>Where the fire starts, in the car's frame: over the crumpled end, at bonnet height.</summary>
    private Vector3 FireSeat()
    {
        var (nose, tail, _, road) = _car.Extent;
        var frontal = _point.Z < (nose + tail) * 0.5f || _normal == Vector3.Zero;
        return new Vector3(_point.X * 0.6f, road + 0.8f, frontal ? nose + 0.2f : tail - 0.2f);
    }

    /// <summary>
    /// The blow as the replay reaches it: the dent, the parts and bits thrown, sparks, steam, cracked glass, and the
    /// explosion and fire of the cabinet's crash.
    /// </summary>
    private void Contact(in Transform3D pose)
    {
        Prepare();
        var (nose, tail, _, road) = _car.Extent;
        var middle = (nose + tail) * 0.5f;
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
        Explode(FireSeat(), MathF.Max(_closing, 4f), pose);
    }

    /// <summary>
    /// The crash's explosion (0.35, the owner's "an explosion when the car hits, as in the original"): a burst of big
    /// flame tongues thrown out from the crumpled end with a flash of light that dies in a third of a second, and the
    /// fire that goes on burning after it (<see cref="Fire"/>).
    /// </summary>
    private void Explode(Vector3 local, float strength, in Transform3D pose)
    {
        var (disc, life) = FlameLook();
        var flame = FlameMaterial(disc);
        var grow = new Curve();
        grow.AddPoint(new Vector2(0f, 0.5f));
        grow.AddPoint(new Vector2(0.3f, 1f));
        grow.AddPoint(new Vector2(1f, 0.2f));
        var burst = new CpuParticles3D
        {
            Name = "Explosion",
            Amount = Math.Clamp((int)(36f + strength * 2f), 36, 80),
            Lifetime = 0.65f,
            OneShot = true,
            Explosiveness = 1f,
            Mesh = new QuadMesh { Size = new Vector2(1.4f, 1.4f), Material = flame },
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.4f,
            Direction = Vector3.Up,
            Spread = 180f,
            InitialVelocityMin = 4f,
            InitialVelocityMax = 9f + strength * 0.2f,
            Gravity = new Vector3(0f, 3f, 0f),
            ScaleAmountCurve = grow,
            ColorRamp = life,
        };
        _debris.AddChild(burst);
        burst.GlobalPosition = pose * local;
        burst.Emitting = true;
        _particles.Add(burst);
        var flash = new OmniLight3D
        {
            Name = "Flash",
            LightColor = new Color(1f, 0.7f, 0.35f),
            LightEnergy = 14f,
            OmniRange = 16f,
            ShadowEnabled = false,
        };
        _debris.AddChild(flash);
        flash.GlobalPosition = pose * local + Vector3.Up * 1.2f;
        flash.CreateTween().TweenProperty(flash, "light_energy", 0f, 0.35f);
        Fire(local, strength);
    }

    /// <summary>The flame billboard's disc and its colour over a tongue's life: white-yellow at birth, orange, deep red, gone.</summary>
    private static (GradientTexture2D Disc, Gradient Life) FlameLook()
    {
        var fade = new Gradient();
        fade.SetColor(0, new Color(1f, 1f, 1f, 1f));
        fade.SetColor(1, new Color(1f, 1f, 1f, 0f));
        var disc = new GradientTexture2D { Gradient = fade, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(0.5f, 0f), Width = 64, Height = 64 };
        var life = new Gradient();
        life.SetColor(0, new Color(1f, 0.95f, 0.65f, 1f));
        life.AddPoint(0.3f, new Color(1f, 0.55f, 0.12f, 0.95f));
        life.AddPoint(0.7f, new Color(0.55f, 0.1f, 0.02f, 0.5f));
        life.SetColor(life.GetPointCount() - 1, new Color(0.1f, 0.02f, 0f, 0f));
        return (disc, life);
    }

    private static StandardMaterial3D FlameMaterial(GradientTexture2D disc) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        AlbedoColor = new Color(1f, 0.7f, 0.3f),
        AlbedoTexture = disc,
        VertexColorUseAsAlbedo = true,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
    };

    /// <summary>
    /// Fire from the blow: flames that rise from the crumpled end with black smoke over them, following the wreck,
    /// and a flickering orange light on the ground and the body. The car burns through the replay and the fire goes
    /// out with it (<see cref="End"/>).
    /// </summary>
    private void Fire(Vector3 local, float strength)
    {
        var (disc, life) = FlameLook();
        var grow = new Curve();
        grow.AddPoint(new Vector2(0f, 0.35f));
        grow.AddPoint(new Vector2(0.4f, 1f));
        grow.AddPoint(new Vector2(1f, 0.3f));
        var flames = new CpuParticles3D
        {
            Name = "Fire",
            Amount = Math.Clamp((int)(30f + strength * 3f), 30, 96),
            Lifetime = 0.75f,
            Mesh = new QuadMesh { Size = new Vector2(0.55f, 0.55f), Material = FlameMaterial(disc) },
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.35f,
            Direction = Vector3.Up,
            Spread = 35f,
            InitialVelocityMin = 1.2f,
            InitialVelocityMax = 3.2f,
            Gravity = new Vector3(0f, 2.8f, 0f),
            ScaleAmountCurve = grow,
            ColorRamp = life,
            Position = local,
        };
        _car.AddChild(flames);
        flames.Emitting = true;
        _particles.Add(flames);

        var soot = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(0.12f, 0.11f, 0.1f, 0.32f),
            AlbedoTexture = disc,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
        };
        var swell = new Curve();
        swell.AddPoint(new Vector2(0f, 0.5f));
        swell.AddPoint(new Vector2(1f, 3f));
        var smoke = new CpuParticles3D
        {
            Name = "Smoke",
            Amount = 26,
            Lifetime = 2.4f,
            Mesh = new QuadMesh { Size = new Vector2(0.8f, 0.8f), Material = soot },
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.3f,
            Direction = Vector3.Up,
            Spread = 20f,
            InitialVelocityMin = 1.0f,
            InitialVelocityMax = 2.0f,
            Gravity = new Vector3(0.4f, 1.2f, 0f),
            ScaleAmountCurve = swell,
            Position = local + Vector3.Up * 0.5f,
        };
        _car.AddChild(smoke);
        smoke.Emitting = true;
        _particles.Add(smoke);

        var glow = new OmniLight3D
        {
            Name = "FireGlow",
            LightColor = new Color(1f, 0.55f, 0.2f),
            LightEnergy = 3f,
            OmniRange = 8f,
            Position = local + Vector3.Up * 0.6f,
            ShadowEnabled = false,
        };
        _car.AddChild(glow);
        _glows.Add(glow);
    }

    /// <summary>The debris flies on, falls and comes to rest on the ground, by the playback's clock.</summary>
    private void AdvancePieces(float da)
    {
        if (da <= 0f) return;
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
        foreach (var glow in _glows) glow.QueueFree();
        _glows.Clear();
        _impactNodes.Clear();
        _debris.QueueFree();
        _pieces.Clear();
    }
}

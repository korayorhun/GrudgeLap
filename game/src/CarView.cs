using Godot;
using GrudgeLap.Physics;
using GrudgeLap.Physics.Vehicle;
using SN = System.Numerics;

namespace GrudgeLap.Game;

/// <summary>
/// Visual stand-in for a car: a loft through cross-sections built from a <see cref="BodyStyle"/>'s profile
/// curves, wheel arches cut into the loft, period details (lamp lids, strakes, louvres and slatted tail for
/// the 1984 flat-twelve; slim lamps, side scoops, glass engine cover, round lamps and four exhaust tips for
/// the 2012 V8) and four star-rimmed wheels that steer and spin from the simulation. The node's origin is
/// the centre of gravity, as in the simulation, so the body sits lower by the static ride height. The cabin
/// is furnished and lined (dashboard, instrument binnacle, steering wheel that turns, seats, door trim, roof
/// lining, pillars) and seen through tinted glass; from the driver's seat the shell and the glass are not
/// drawn but the shell still casts its shadow over the cabin (<see cref="SetViewpoint"/>).
/// </summary>
internal sealed partial class CarView : Node3D
{
    private const float DriverX = -0.38f;                  // the driver sits on the left
    private const float LiningInset = 0.025f;              // the cabin lining sits this far inside the shell
    private const int HalfPoints = 11;                     // P0..P10 of the half profile, floor centre to roof centre
    private const int RingSize = HalfPoints * 2 - 2;       // points and strips of the design outline; both halves share the floor and roof centre points
    private const int TopFirst = 5;                        // first strip of the top skin (bonnet edge / greenhouse base)
    private const int TopLast = RingSize - 6;              // last strip of the top skin, mirrored side
    private const int FilletSteps = 4;                     // sub-strips across each rounded corner of the outline
    private const float ArchRadius = 0.44f;                // wheel radius plus the gap to the wing lip
    private const float SmoothingCos = 0.5f;               // faces meeting at more than 60 degrees keep a hard edge
    private const float SeamWidth = 0.014f;                // dark band of a panel shut line
    private const float SteeringRatio = 12f;               // turns of the wheel per turn of the road wheels

    public static readonly Color DefaultPaint = new(0.76f, 0.03f, 0.05f);
    /// <summary>Paints for the drone cars, by their colour index.</summary>
    public static readonly Color[] DronePaints = [new(0.12f, 0.25f, 0.70f), new(0.88f, 0.88f, 0.84f), new(0.90f, 0.72f, 0.10f)];
    private static readonly Color Trim = new(0.06f, 0.06f, 0.065f);
    private static readonly Color Amber = new(1f, 0.55f, 0.08f);
    /// <summary>Tint that turns the brown leather photo into the black leather of the dashboard.</summary>
    private static readonly Color BlackLeather = new(0.18f, 0.25f, 0.32f);

    private readonly Node3D[] _wheelPivots = new Node3D[4];
    private readonly Node3D[] _wheelSpinners = new Node3D[4];
    private readonly List<Node3D> _outsideOnly = [];      // not drawn from the driver's seat, but still casting shadows
    private readonly List<Node3D> _cockpitOnly = [];
    private MeshInstance3D? _glass;                       // hidden from the seat; glass casts no shadow at all
    private MeshInstance3D? _gaugeFace;
    private Node3D? _body;
    private bool? _insideCar;
    private BodyStyle _style = BodyStyle.FlatTwelve;
    private Color _paint = DefaultPaint;
    private float _cgHeight;
    private float _wheelRadius;
    private float _wheelAngle;
    private Node3D? _steeringSpin;

    /// <summary>The driver's eyes in this node's space (origin at the centre of gravity), for the cockpit camera.</summary>
    public Vector3 Eye => new(DriverX, _style.EyeH - _cgHeight, _style.EyeZ);

    public static CarView Create(VehicleSpec spec, Color? paint = null)
    {
        var view = new CarView
        {
            Name = "Car",
            _paint = paint ?? DefaultPaint,
            _wheelRadius = spec.WheelRadius,
            _style = spec.Style == CarStyle.V8 ? BodyStyle.V8 : BodyStyle.FlatTwelve,
        };
        var sag = spec.MassKg * MathUtil.Gravity / 4f / spec.SuspensionStiffness;
        view._cgHeight = spec.SuspensionRest + spec.WheelRadius - sag - spec.HubHeight;
        view.BuildBody(spec);
        view.BuildWheels(spec);
        return view;
    }

    /// <summary>
    /// From the driver's seat the shell, the wheels and the outside details are not drawn, so the view is the
    /// cabin and the bonnet beyond it; they keep casting their shadows, so the roof still shades the dashboard.
    /// Called every frame; only a change of viewpoint touches the meshes.
    /// </summary>
    public void SetViewpoint(bool insideCar)
    {
        if (_insideCar == insideCar) return;
        _insideCar = insideCar;
        var cast = insideCar ? GeometryInstance3D.ShadowCastingSetting.ShadowsOnly : GeometryInstance3D.ShadowCastingSetting.On;
        foreach (var node in _outsideOnly)
        {
            if (node is GeometryInstance3D own) own.CastShadow = cast;
            foreach (var mesh in Descendants(node).OfType<GeometryInstance3D>()) mesh.CastShadow = cast;
        }
        if (_glass is not null) _glass.Visible = !insideCar;
        foreach (var node in _cockpitOnly) node.Visible = insideCar;
    }

    /// <summary>Puts the instrument panel (<see cref="InstrumentCluster"/>) into the binnacle: lit dials under the hood.</summary>
    public void MountInstruments(Texture2D panel)
    {
        if (_gaugeFace is null) return;
        _gaugeFace.MaterialOverride = new StandardMaterial3D
        {
            AlbedoTexture = panel,
            EmissionEnabled = true,
            EmissionTexture = panel,
            EmissionEnergyMultiplier = 0.45f,
            Roughness = 0.3f,
        };
    }

    /// <summary>Hangs the rear-view mirror under the roof at the top of the windscreen, angled towards the driver.</summary>
    public void MountMirror(RearMirror mirror)
    {
        if (_body is null) return;
        // On the screen, 12 cm ahead of its top edge: under the roof it hung so close to the eyes that it filled a
        // third of the view.
        var roofZ = _style.Keys.First(k => k > _style.CabinFrom);
        mirror.Position = new Vector3(-0.16f, _style.Sample(_style.CentreH, roofZ) - 0.10f, roofZ - 0.12f);
        // Half the angle between the driver and the rear window, so the glass shows the driver the road behind.
        mirror.Rotation = new Vector3(0f, -0.30f, 0f);
        _body.AddChild(mirror);
    }

    /// <summary>Turns this car into the translucent ghost of the best lap: one glowing material, no shadow.</summary>
    public void MakeGhost()
    {
        SetViewpoint(false);
        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.55f, 0.85f, 1.0f, 0.30f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            EmissionEnabled = true,
            Emission = new Color(0.25f, 0.55f, 0.85f),
            EmissionEnergyMultiplier = 0.9f,
            Roughness = 0.4f,
        };
        foreach (var mesh in Descendants(this).OfType<MeshInstance3D>())
        {
            mesh.MaterialOverride = material;
            mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }
        foreach (var node in _cockpitOnly) node.Visible = false;
    }

    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            yield return child;
            foreach (var grandchild in Descendants(child)) yield return grandchild;
        }
    }

    /// <summary>Spins the wheels for a car driven kinematically (drones, the ghost): distance rolled and steering angle.</summary>
    public void AdvanceWheels(float distance, float steerAngle)
    {
        _wheelAngle += distance / _wheelRadius;
        for (var i = 0; i < 4; i++)
        {
            _wheelSpinners[i].Rotation = new Vector3(-_wheelAngle, 0f, 0f);
            if (i < 2) _wheelPivots[i].Rotation = new Vector3(0f, -steerAngle, 0f);
        }
        TurnSteeringWheel(steerAngle);
    }

    /// <summary>Right turn = clockwise for the driver: the column's axis points at the driver, so that is a negative angle.</summary>
    private void TurnSteeringWheel(float steerAngle)
    {
        if (_steeringSpin is not null) _steeringSpin.Rotation = new Vector3(0f, -steerAngle * SteeringRatio, 0f);
    }

    // ---- body profile ----------------------------------------------------------------------------------
    // The body is a loft through cross-sections sampled along z (Godot z, nose negative). Each section is
    // built from the style's curves, keyed at the design stations; metres above the road at rest.

    /// <summary>Profile curves and details of one car body; values keyed at <see cref="Keys"/>.</summary>
    internal sealed class BodyStyle
    {
        public required float[] Keys;        // Godot z of the design stations, nose negative
        public required float[] FloorH;
        public required float[] HalfWidth;
        public required float[] WaistH;      // height of the widest point
        public required float[] ShoulderH;   // wing top / beltline
        public required float[] EdgeW;       // bonnet edge / greenhouse base
        public required float[] EdgeH;
        public required float[] CrestW;      // wing crest / roof edge
        public required float[] CrestH;
        public required float[] CentreH;
        public float CowlZ = -0.72f;         // windscreen base; everything ahead of it is the bonnet
        public float CabinFrom = -0.5f;      // stations between these are the greenhouse
        public float CabinTo = 0.6f;
        public float FrontArchTop = 0.64f;
        public float RearArchTop = 0.80f;
        public float FrontArchRadius = ArchRadius;   // horizontal radius of the arch cut about the hub
        public float RearArchRadius = ArchRadius;
        public float ScreenFrom = float.NaN;  // z range whose top skin is all glass: the windscreen and the side glass beside it
        public float ScreenTo = float.NaN;
        public float RearGlassFrom = float.NaN;   // z range whose skin between the crests is glass: a rear window between buttresses
        public float RearGlassTo = float.NaN;
        public float IntakeFrom = float.NaN;  // z range of a recess low on the flank (the strakes' intake)
        public float IntakeTo = float.NaN;
        public float IntakeBottom = 0.28f;    // its lower edge, metres above the road
        public float IntakeTopFront = 0.55f;  // its upper edge at the front, once the front taper is done
        public float IntakeTopBack = 0.72f;   // and at the back, ahead of the rear arch
        public float IntakeDepth = 0.07f;
        public float[] StrakeHeights = [];    // heights of the strakes across the intake, flush with the skin
        public float StrakesFrom = float.NaN; // where the strakes start (they stop at the intake's back)
        public float LouvreFrom = 0.83f;      // z range of the louvred engine cover
        public float LouvreTo = 1.75f;
        public float NoseTrimBelow = 0.42f;   // the nose face is black below this height
        public float TailTrimBelow = 0.50f;   // and the tail face
        public float FrontTrack = float.NaN;  // half tracks of the wheels as drawn (the simulation's HalfTrack otherwise)
        public float RearTrack = float.NaN;
        public float FrontTyreWidth = 0.27f;
        public float RearTyreWidth = 0.27f;
        public float RimRadius = 0.228f;      // outer radius of the rim lip: 0.228 reads as 18 in, 0.203 is a 16 in wheel
        public bool LampLids;                // pop-up headlamp lids on the wing crests
        public bool Strakes;                 // slatted flank intakes
        public bool Louvres;                 // louvred engine cover (otherwise a glass cover)
        public float GlassDeckFrom = 99f;    // z range of the glass engine cover
        public float GlassDeckTo = -99f;
        public bool SlattedTail;             // full-width slatted tail with light bars (otherwise round lamps)
        public int ExhaustTips;
        public float[] TopSeams = [];        // z of the shut lines across the top skin (bonnet rear edge, engine cover)
        public float[] FlankSeams = [];      // z of the door shut lines on the flanks
        public float RearBulkheadZ = 0.68f;  // back wall of the cabin, behind the seats; the lining ends here
        public float EyeZ = 0.15f;           // driver's eyes: over the seat, well behind the screen header
        public float EyeH = 0.98f;           // and their height above the road at rest
        public Color LeatherTint = Colors.White;                  // multiplies the brown leather photo: seats, door trim, wheel rim
        public Color CarpetTint = new(0.29f, 0.23f, 0.22f);       // multiplies the beige carpet photo
        public Color HeadlinerTint = new(0.66f, 0.62f, 0.56f);    // multiplies the grey fabric photo of the roof lining

        /// <summary>
        /// The 1984 flat-twelve: a long low wedge, 1.57 m wide just behind the nose and 1.82 m at the doors, flaring
        /// to 1.98 m ahead of the rear wheels; a long raked screen from a cowl a third of the way back; a short narrow
        /// roof whose buttresses run down to a tail only 0.77 m high; the intake low on the door with five strakes
        /// across it; pop-up lamp lids at the outer corners of the bonnet; tight arches over 16 in wheels.
        /// </summary>
        /// <remarks>
        /// Keyed on a dimensioned four-view drawing of the 1984 coupe (getoutlines.com, CC BY 4.0: 4485 x 1976 x 1130
        /// mm, wheelbase 2550, overhangs 1085 / 845, tracks 1520 / 1677), its side and plan views each calibrated on
        /// their own dimension lines, so the heights and widths here are measured rather than eyeballed. The loft
        /// before it had a near constant width, the cabin forward of the middle, the strakes up at the shoulder, the
        /// belt line 0.1 m high and the tail 0.13 m high. Keys are Godot z with the nose at -2.355 (1085 mm ahead of
        /// the front axle at -1.27).
        /// </remarks>
        public static readonly BodyStyle FlatTwelve = new()
        {
            //         nose      -        -        -    f.axle    cowl   screen  header   roof   r.end    deck  r.axle      -        -     tail
            Keys = [-2.355f, -2.235f, -2.055f, -1.755f, -1.27f, -1.035f, -0.615f, -0.195f, 0.295f, 0.745f, 0.895f, 1.28f, 1.645f, 1.895f, 2.13f],
            FloorH = [0.20f, 0.19f, 0.18f, 0.17f, 0.16f, 0.155f, 0.155f, 0.155f, 0.155f, 0.155f, 0.155f, 0.16f, 0.18f, 0.23f, 0.28f],
            HalfWidth = [0.58f, 0.72f, 0.81f, 0.864f, 0.90f, 0.91f, 0.91f, 0.924f, 0.95f, 0.984f, 0.988f, 0.975f, 0.962f, 0.945f, 0.885f],
            WaistH = [0.40f, 0.41f, 0.42f, 0.44f, 0.46f, 0.47f, 0.48f, 0.49f, 0.51f, 0.53f, 0.54f, 0.55f, 0.54f, 0.52f, 0.50f],
            ShoulderH = [0.50f, 0.53f, 0.585f, 0.655f, 0.74f, 0.765f, 0.755f, 0.745f, 0.765f, 0.795f, 0.81f, 0.82f, 0.80f, 0.785f, 0.76f],
            EdgeW = [0.55f, 0.68f, 0.77f, 0.83f, 0.865f, 0.74f, 0.735f, 0.735f, 0.72f, 0.70f, 0.72f, 0.76f, 0.82f, 0.85f, 0.83f],
            EdgeH = [0.52f, 0.56f, 0.615f, 0.685f, 0.765f, 0.785f, 0.775f, 0.765f, 0.785f, 0.815f, 0.83f, 0.835f, 0.815f, 0.79f, 0.765f],
            CrestW = [0.44f, 0.60f, 0.70f, 0.72f, 0.73f, 0.72f, 0.63f, 0.54f, 0.545f, 0.56f, 0.62f, 0.66f, 0.68f, 0.72f, 0.70f],
            CrestH = [0.535f, 0.575f, 0.635f, 0.707f, 0.79f, 0.815f, 0.95f, 1.08f, 1.115f, 1.07f, 1.035f, 0.93f, 0.835f, 0.78f, 0.765f],
            CentreH = [0.525f, 0.56f, 0.615f, 0.685f, 0.76f, 0.805f, 0.975f, 1.095f, 1.134f, 1.085f, 0.87f, 0.86f, 0.835f, 0.785f, 0.77f],
            CowlZ = -1.035f,
            CabinFrom = -0.20f,
            CabinTo = 0.75f,
            ScreenFrom = -1.035f,
            ScreenTo = -0.195f,
            RearGlassFrom = 0.745f,
            RearGlassTo = 0.895f,
            FrontArchTop = 0.69f,
            RearArchTop = 0.70f,
            FrontArchRadius = 0.36f,
            RearArchRadius = 0.40f,
            IntakeFrom = -0.735f,
            IntakeTo = 0.645f,
            IntakeBottom = 0.28f,
            IntakeTopFront = 0.535f,
            IntakeTopBack = 0.725f,
            IntakeDepth = 0.07f,
            StrakeHeights = [0.338f, 0.394f, 0.444f, 0.497f, 0.547f],
            StrakesFrom = -0.705f,
            LouvreFrom = 0.93f,
            LouvreTo = 1.84f,
            NoseTrimBelow = 0.31f,
            TailTrimBelow = 0.39f,
            FrontTrack = 0.76f,
            RearTrack = 0.8385f,
            FrontTyreWidth = 0.225f,
            RearTyreWidth = 0.255f,
            RimRadius = 0.203f,
            LampLids = true,
            Strakes = true,
            Louvres = true,
            SlattedTail = true,
            ExhaustTips = 4,
            TopSeams = [-1.265f, 0.92f, 1.86f],
            FlankSeams = [-0.805f, 0.515f],
            RearBulkheadZ = 0.70f,
            LeatherTint = new(0.80f, 0.85f, 0.75f),
        };

        /// <summary>
        /// Depth of the flank intake at <paramref name="z"/> (0 outside it) and the height of its upper edge. The
        /// recess opens over a short taper at the front, where its upper edge also rises from near the sill, and
        /// closes over a few centimetres at the back, its rounded rear edge ahead of the rear arch.
        /// </summary>
        public (float Depth, float Top) Intake(float z)
        {
            if (float.IsNaN(IntakeFrom) || z < IntakeFrom || z > IntakeTo) return (0f, 0f);
            const float taper = 0.12f, closure = 0.06f;
            var along = (z - IntakeFrom - taper) / (IntakeTo - IntakeFrom - taper);
            var line = IntakeTopFront + (IntakeTopBack - IntakeTopFront) * Math.Clamp(along, 0f, 1f);
            var open = Math.Clamp((z - IntakeFrom) / taper, 0f, 1f);
            var shut = Math.Clamp((IntakeTo - z) / closure, 0f, 1f);
            var top = IntakeBottom + 0.08f + (line - IntakeBottom - 0.08f) * Smooth(open);
            return (IntakeDepth * Smooth(MathF.Min(open, shut)), top);

            static float Smooth(float t) => t * t * (3f - 2f * t);
        }

        /// <summary>The 2012 V8: low wide nose, long screen, teardrop cabin over a glass engine cover, strong rear haunches, round lamps.</summary>
        public static readonly BodyStyle V8 = new()
        {
            Keys = [-2.27f, -2.05f, -1.74f, -1.32f, -0.88f, -0.72f, -0.25f, 0.35f, 0.80f, 1.33f, 1.77f, 2.26f],
            FloorH = [0.20f, 0.16f, 0.14f, 0.14f, 0.14f, 0.14f, 0.14f, 0.14f, 0.14f, 0.14f, 0.16f, 0.22f],
            HalfWidth = [0.84f, 0.90f, 0.95f, 0.97f, 0.96f, 0.96f, 0.96f, 0.97f, 0.97f, 0.97f, 0.95f, 0.90f],
            WaistH = [0.42f, 0.44f, 0.46f, 0.47f, 0.47f, 0.47f, 0.48f, 0.49f, 0.50f, 0.52f, 0.50f, 0.48f],
            ShoulderH = [0.50f, 0.60f, 0.72f, 0.78f, 0.78f, 0.80f, 0.84f, 0.88f, 0.92f, 0.96f, 0.92f, 0.86f],
            EdgeW = [0.80f, 0.84f, 0.86f, 0.84f, 0.80f, 0.70f, 0.66f, 0.68f, 0.72f, 0.84f, 0.86f, 0.84f],
            EdgeH = [0.52f, 0.62f, 0.74f, 0.80f, 0.80f, 0.82f, 0.90f, 0.92f, 0.95f, 0.98f, 0.94f, 0.88f],
            CrestW = [0.62f, 0.62f, 0.62f, 0.60f, 0.58f, 0.56f, 0.52f, 0.54f, 0.58f, 0.62f, 0.64f, 0.66f],
            CrestH = [0.54f, 0.64f, 0.76f, 0.82f, 0.84f, 0.85f, 1.14f, 1.18f, 1.05f, 1.00f, 0.96f, 0.90f],
            CentreH = [0.52f, 0.62f, 0.74f, 0.80f, 0.83f, 0.86f, 1.19f, 1.21f, 1.07f, 1.02f, 0.97f, 0.90f],
            CowlZ = -0.72f,
            CabinFrom = -0.55f,
            CabinTo = 0.6f,
            FrontArchTop = 0.70f,
            RearArchTop = 0.84f,
            GlassDeckFrom = 0.78f,
            GlassDeckTo = 1.80f,
            ExhaustTips = 4,
            TopSeams = [-0.75f, 0.75f, 1.82f],
            FlankSeams = [-0.65f, 0.70f],
            RearBulkheadZ = 0.70f,
            EyeZ = 0.12f,
            EyeH = 1.02f,
            LeatherTint = BlackLeather,
            CarpetTint = new(0.15f, 0.16f, 0.20f),
            HeadlinerTint = new(0.45f, 0.45f, 0.48f),
        };

        /// <summary>
        /// Monotone cubic interpolation between the keys (Fritsch-Carlson slopes): smooth along the body, and it
        /// never overshoots a key, so the cowl cannot bulge above the windscreen base.
        /// </summary>
        public float Sample(float[] values, float z)
        {
            if (z <= Keys[0]) return values[0];
            if (z >= Keys[^1]) return values[^1];
            var i = 1;
            while (Keys[i] < z) i++;
            var z0 = Keys[i - 1];
            var z1 = Keys[i];
            var h = z1 - z0;
            var t = (z - z0) / h;
            var m0 = Slope(values, i - 1);
            var m1 = Slope(values, i);
            var t2 = t * t;
            var t3 = t2 * t;
            return (2f * t3 - 3f * t2 + 1f) * values[i - 1] + (t3 - 2f * t2 + t) * h * m0
                 + (-2f * t3 + 3f * t2) * values[i] + (t3 - t2) * h * m1;
        }

        private float Slope(float[] values, int i)
        {
            var before = i > 0 ? (values[i] - values[i - 1]) / (Keys[i] - Keys[i - 1]) : float.NaN;
            var after = i < values.Length - 1 ? (values[i + 1] - values[i]) / (Keys[i + 1] - Keys[i]) : float.NaN;
            if (float.IsNaN(before)) return after;
            if (float.IsNaN(after)) return before;
            if (before * after <= 0f) return 0f;
            var m = (before + after) * 0.5f;
            var limit = 3f * MathF.Min(MathF.Abs(before), MathF.Abs(after));
            return MathF.CopySign(MathF.Min(MathF.Abs(m), limit), m);
        }
    }

    private readonly record struct Station(float Z, float ArchLift, bool Cabin, float IntakeDepth);

    /// <summary>Where the stations through a wheel arch sit, as fractions of the arch radius either side of the hub.</summary>
    private static readonly float[] ArchSamples = [-1.045f, -1f, -0.909f, -0.682f, -0.341f, 0f, 0.341f, 0.682f, 0.909f, 1f, 1.045f];

    /// <summary>
    /// Outline points that are rounded off like pressed sheet metal, both sides: the shoulder, the bonnet edge /
    /// greenhouse base and the crest, with their radius in metres. Left sharp, these three character lines made
    /// the body read as folded card; a fillet keeps the line but lets the highlight roll over it.
    /// </summary>
    private static readonly (int Point, float Radius)[] Fillets =
        [(4, 0.05f), (5, 0.035f), (6, 0.05f), (RingSize - 6, 0.05f), (RingSize - 5, 0.035f), (RingSize - 4, 0.05f)];

    /// <summary>
    /// For each strip of a built ring (point j to j + 1), the strip of the design outline it belongs to. Glass,
    /// trim, seams and louvres are decided on the outline strips (<see cref="TopFirst"/> and the like), so the
    /// rounded corners do not move them; the first half of a fillet continues the strip before the corner.
    /// </summary>
    private static readonly int[] StripOrigin = BuildStripOrigin();

    private static float FilletRadius(int point)
    {
        foreach (var (p, radius) in Fillets)
        {
            if (p == point) return radius;
        }
        return 0f;
    }

    private static int[] BuildStripOrigin()
    {
        var origin = new List<int>();
        for (var k = 0; k < RingSize; k++)
        {
            if (FilletRadius(k) > 0f)
            {
                for (var s = 0; s < FilletSteps; s++) origin.Add(s < FilletSteps / 2 ? (k + RingSize - 1) % RingSize : k);
            }
            origin.Add(k);
        }
        return [.. origin];
    }

    /// <summary>Design stations, a dense run along the bonnet (it is what the driver looks at) and samples through each wheel arch.</summary>
    private Station[] Stations(VehicleSpec spec)
    {
        var style = _style;
        var axles = new[] { -spec.FrontAxle, spec.RearAxle };
        var zs = new List<float>(style.Keys);
        for (var z = style.Keys[0] + 0.06f; z < style.CowlZ; z += 0.10f) zs.Add(z);
        foreach (var axle in axles)
        {
            var radius = axle < 0f ? style.FrontArchRadius : style.RearArchRadius;
            foreach (var fraction in ArchSamples) zs.Add(axle + fraction * radius);
        }
        if (!float.IsNaN(style.IntakeFrom))
        {
            // Every 10 cm along the intake, so its rising edge and its depth ramps stay smooth; a station just outside
            // each end keeps the recess's dark wall from smearing into the flank beyond it.
            for (var z = style.IntakeFrom; z <= style.IntakeTo + 1e-4f; z += 0.10f) zs.Add(z);
            zs.AddRange([style.IntakeFrom - 0.012f, style.IntakeFrom + 0.04f, style.IntakeTo - 0.03f, style.IntakeTo, style.IntakeTo + 0.012f]);
        }
        // A station pair at every shut line gives the seam its own thin strip of quads.
        foreach (var seam in style.TopSeams.Concat(style.FlankSeams))
        {
            zs.Add(seam);
            zs.Add(seam + SeamWidth);
        }
        zs.Sort();
        var stations = new List<Station>();
        foreach (var z in zs)
        {
            // Keys and arch samples can land on the same station (give or take float rounding); a zero-length
            // quad between them would only spoil the normal smoothing.
            if (stations.Count > 0 && z - stations[^1].Z < 1e-3f) continue;
            var lift = 0f;
            foreach (var axle in axles)
            {
                var d = MathF.Abs(z - axle);
                var radius = axle < 0f ? style.FrontArchRadius : style.RearArchRadius;
                if (d <= radius + 1e-4f)
                {
                    // Round arch over the hub, ending at hub height, flattened under the wing where the body is low.
                    var arch = spec.WheelRadius + MathF.Sqrt(MathF.Max(radius * radius - d * d, 0f));
                    lift = MathF.Max(lift, MathF.Min(arch, axle < 0f ? style.FrontArchTop : style.RearArchTop));
                }
            }
            stations.Add(new Station(z, lift, z > style.CabinFrom && z < style.CabinTo, style.Intake(z).Depth));
        }
        return [.. stations];
    }

    /// <summary>
    /// Closed ring of a cross-section: floor centre, up the right side to the roof centre, down the left, with
    /// the corners in <see cref="Fillets"/> rounded off; every ring has the same number of points.
    /// </summary>
    private Vector3[] Ring(Station s)
    {
        var style = _style;
        var z = s.Z;
        var floor = style.Sample(style.FloorH, z);
        var w = style.Sample(style.HalfWidth, z);
        var crestW = style.Sample(style.CrestW, z);
        var crestH = style.Sample(style.CrestH, z);
        var centreH = style.Sample(style.CentreH, z);
        // Between the crests the skin is a parabola: the bonnet dips to the centre, the roof and the engine cover dome.
        var half = new Vector2[HalfPoints]
        {
            new(0f, floor),
            new(w - 0.08f, floor),
            new(w - 0.005f, 0.30f),
            new(w, style.Sample(style.WaistH, z)),
            new(w - 0.01f, style.Sample(style.ShoulderH, z)),
            new(style.Sample(style.EdgeW, z), style.Sample(style.EdgeH, z)),
            new(crestW, crestH),
            new(crestW * 0.70f, Mathf.Lerp(centreH, crestH, 0.49f)),
            new(crestW * 0.40f, Mathf.Lerp(centreH, crestH, 0.16f)),
            new(crestW * 0.15f, Mathf.Lerp(centreH, crestH, 0.0225f)),
            new(0f, centreH),
        };
        var (intakeDepth, intakeTop) = style.Intake(z);
        if (intakeTop > 0f)
        {
            // The recess: the flank from the sill slopes in to its deepest just under the upper edge, a short ceiling
            // runs back out to the skin there, and the door above stays upright. Deep under the edge, as on the car,
            // so the edge casts its shadow into the slot.
            half[2] = new Vector2(w - intakeDepth, intakeTop - 0.035f);
            half[3] = new Vector2(w, intakeTop);
        }
        if (s.ArchLift > 0f)
        {
            // The sill and the lower flank are cut up to the arch; the floor centre stays down so the underside
            // becomes a dark slope into the well instead of a floor rising through the bonnet dip.
            for (var k = 1; k <= 3; k++) half[k].Y = MathF.Max(half[k].Y, s.ArchLift);
        }
        var outline = new Vector2[RingSize];
        for (var k = 0; k < HalfPoints; k++) outline[k] = half[k];
        for (var k = 1; k < HalfPoints - 1; k++)
        {
            var mirrored = half[HalfPoints - 1 - k];
            outline[HalfPoints - 1 + k] = new Vector2(-mirrored.X, mirrored.Y);
        }
        var ring = new List<Vector3>(StripOrigin.Length);
        for (var k = 0; k < RingSize; k++)
        {
            var p = outline[k];
            var radius = FilletRadius(k);
            if (radius <= 0f)
            {
                ring.Add(new Vector3(p.X, p.Y, z));
                continue;
            }
            // A quadratic Bezier with the corner as its control point. The radius shrinks where a neighbouring
            // strip is short (at the nose, over the arches) so that two fillets never overlap.
            var prev = outline[(k + RingSize - 1) % RingSize];
            var next = outline[(k + 1) % RingSize];
            var r = MathF.Min(radius, 0.35f * MathF.Min(p.DistanceTo(prev), p.DistanceTo(next)));
            var a = p + (prev - p).Normalized() * r;
            var b = p + (next - p).Normalized() * r;
            for (var step = 0; step <= FilletSteps; step++)
            {
                var t = (float)step / FilletSteps;
                var q = (1f - t) * (1f - t) * a + 2f * t * (1f - t) * p + t * t * b;
                ring.Add(new Vector3(q.X, q.Y, z));
            }
        }
        return [.. ring];
    }

    private void BuildBody(VehicleSpec spec)
    {
        var style = _style;
        var body = new Node3D { Name = "Body", Position = new Vector3(0f, -_cgHeight, 0f) };
        AddChild(body);
        _body = body;

        var stations = Stations(spec);
        var rings = new Vector3[stations.Length][];
        for (var i = 0; i < stations.Length; i++) rings[i] = Ring(stations[i]);

        // Outward unit normal of every quad (station i to i+1, ring point k to k+1). The ring runs
        // counter-clockwise in the x-y plane (floor centre, up the right side, over the roof, down the left),
        // so the outward side of a strip is its tangent turned clockwise; a centroid test would misjudge the
        // strips that lean towards the centre line, such as the bonnet dip beside the wing crests.
        var n = rings[0].Length;
        var faces = new Vector3[stations.Length - 1][];
        for (var i = 0; i < faces.Length; i++)
        {
            faces[i] = new Vector3[n];
            var centroid = (Centroid(rings[i]) + Centroid(rings[i + 1])) * 0.5f;
            for (var j = 0; j < n; j++)
            {
                var j2 = (j + 1) % n;
                var a = rings[i][j];
                var b = rings[i][j2];
                var c = rings[i + 1][j];
                var d = rings[i + 1][j2];
                var normal = (d - a).Cross(b - c);
                if (normal.LengthSquared() < 1e-10f) normal = Vector3.Up;
                normal = normal.Normalized();
                var tangent = (b - a) + (d - c);
                var hint = new Vector3(tangent.Y, -tangent.X, 0f);
                var side = normal.Dot(hint);
                if (MathF.Abs(side) < 0.05f * hint.Length()) side = normal.Dot((a + b + c + d) * 0.25f - centroid);
                if (side < 0f) normal = -normal;
                faces[i][j] = normal;
            }
        }

        var bonnet = new MeshBuilder();
        var shell = new MeshBuilder();
        var glass = new MeshBuilder();

        // Windscreen and rear window span a cabin and a non-cabin station; between cabin stations only the flank
        // strips are side windows, the roof stays painted. A glass engine cover is glass on top.
        bool IsGlass(Station front, Station back, int k)
        {
            var top = k >= TopFirst && k <= TopLast;
            if (!float.IsNaN(style.ScreenFrom))
            {
                // A long raked screen spans several stations (the front arch's among them): all of its top skin is glass,
                // and behind the roof only the skin between the buttresses is the rear window.
                if (top && front.Z >= style.ScreenFrom - 1e-3f && back.Z <= style.ScreenTo + 1e-3f) return true;
                if (k > TopFirst + 1 && k < TopLast - 1 && front.Z >= style.RearGlassFrom - 1e-3f && back.Z <= style.RearGlassTo + 1e-3f) return true;
                return front.Cabin && back.Cabin && (k == TopFirst || k == TopLast);
            }
            if (front.Cabin != back.Cabin) return top;
            if (front.Cabin && (k == TopFirst || k == TopLast)) return true;
            return k > TopFirst && k < TopLast && front.Z >= style.GlassDeckFrom - 0.01f && back.Z <= style.GlassDeckTo + 0.01f;
        }

        for (var i = 0; i < faces.Length; i++)
        {
            var front = stations[i];
            var back = stations[i + 1];
            var isBonnet = back.Z <= style.CowlZ + 0.001f;
            for (var j = 0; j < n; j++)
            {
                var j2 = (j + 1) % n;
                var k = StripOrigin[j];
                var isGlass = IsGlass(front, back, k);
                var target = isGlass ? glass : isBonnet ? bonnet : shell;
                var colour = QuadColour(front, back, k);
                var faceNormal = faces[i][j];
                var a = rings[i][j];
                var b = rings[i][j2];
                var c = rings[i + 1][j];
                var d = rings[i + 1][j2];
                var na = VertexNormal(faces, i, j, faceNormal);
                var nb = VertexNormal(faces, i, j2, faceNormal);
                var nc = VertexNormal(faces, i + 1, j, faceNormal);
                var nd = VertexNormal(faces, i + 1, j2, faceNormal);
                Tri(target, a, na, c, nc, b, nb, colour, faceNormal);
                Tri(target, b, nb, c, nc, d, nd, colour, faceNormal);
            }
        }
        Cap(bonnet, rings[0], new Vector3(0f, 0f, -1f), style.NoseTrimBelow, _paint);
        Cap(shell, rings[^1], new Vector3(0f, 0f, 1f), style.TailTrimBelow, _paint);
        // Bulkhead at the cowl: from the driver's seat the bonnet must not read as an open tube with the wheels inside.
        var cowl = Array.FindIndex(stations, s => MathF.Abs(s.Z - style.CowlZ) < 1e-3f);
        Cap(bonnet, rings[cowl], new Vector3(0f, 0f, 1f), float.MaxValue, _paint);

        // The inside of the cabin, from the windscreen base to the bulkhead behind the seats: every opaque strip
        // of the upper body again, pushed in by the lining's thickness and facing inwards, as door trim and roof
        // lining. From the seat the shell is not drawn, and without it the road showed through the doors and the
        // sky through the roof.
        var lining = new MeshBuilder();
        var headliner = new MeshBuilder();
        for (var i = 0; i < faces.Length; i++)
        {
            var front = stations[i];
            var back = stations[i + 1];
            if (front.Z < style.CowlZ - 0.001f || back.Z > style.RearBulkheadZ + 0.001f) continue;
            for (var j = 0; j < n; j++)
            {
                var k = StripOrigin[j];
                // The sills and the floor are below the cabin floor, and the windows stay open.
                if (k < 3 || k > RingSize - 4 || IsGlass(front, back, k)) continue;
                var roof = front.Cabin && back.Cabin && k > TopFirst && k < TopLast;
                var j2 = (j + 1) % n;
                var faceNormal = faces[i][j];
                var na = VertexNormal(faces, i, j, faceNormal);
                var nb = VertexNormal(faces, i, j2, faceNormal);
                var nc = VertexNormal(faces, i + 1, j, faceNormal);
                var nd = VertexNormal(faces, i + 1, j2, faceNormal);
                var a = rings[i][j] - na * LiningInset;
                var b = rings[i][j2] - nb * LiningInset;
                var c = rings[i + 1][j] - nc * LiningInset;
                var d = rings[i + 1][j2] - nd * LiningInset;
                var target = roof ? headliner : lining;
                Tri(target, a, -na, c, -nc, b, -nb, Colors.White, -faceNormal);
                Tri(target, b, -nb, c, -nc, d, -nd, Colors.White, -faceNormal);
            }
        }
        var bulkhead = Array.FindIndex(stations, s => MathF.Abs(s.Z - style.RearBulkheadZ) < 1e-3f);
        if (bulkhead >= 0) Cap(lining, rings[bulkhead], new Vector3(0f, 0f, -1f), float.MaxValue, Colors.White);
        var lined = new[] { (lining, WorldBuilder.PropMaterial("leather", style.LeatherTint, 4f), "Lining"), (headliner, WorldBuilder.PropMaterial("fabric", style.HeadlinerTint, 3f), "Headliner") };
        foreach (var (tool, material, name) in lined)
        {
            // The shell casts the cabin's shadow; the lining just inside it would only add shadow acne.
            body.AddChild(new MeshInstance3D { Mesh = tool.Commit(), MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Name = name });
        }

        // Little metallic: a metallic albedo reflects the sky and turns the red pink; the gloss comes from the clearcoat.
        var paint = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            Metallic = 0.18f,
            Roughness = 0.26f,
            ClearcoatEnabled = true,
            Clearcoat = 0.9f,
            ClearcoatRoughness = 0.08f,
        };
        // Dark tinted glass: the cabin shows through it faintly and the sky and the road reflect in it. Lighter, the
        // long screen and the tall side glass of the flat-twelve made the car read as an open targa.
        var tint = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.05f, 0.08f, 0.11f, 0.80f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Metallic = 0.9f,
            Roughness = 0.05f,
            CullMode = BaseMaterial3D.CullModeEnum.Back,
        };
        var bonnetMesh = new MeshInstance3D { Mesh = bonnet.Commit(), MaterialOverride = paint, Name = "Bonnet" };
        var shellMesh = new MeshInstance3D { Mesh = shell.Commit(), MaterialOverride = paint, Name = "Shell" };
        var glassMesh = new MeshInstance3D { Mesh = glass.Commit(), MaterialOverride = tint, Name = "Glass" };
        body.AddChild(bonnetMesh);
        body.AddChild(shellMesh);
        body.AddChild(glassMesh);
        _outsideOnly.Add(shellMesh);
        // Tinted glass lets the sun through: no shadow, and from the seat it is not drawn at all.
        glassMesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        _glass = glassMesh;

        BuildDetails(body, spec);
    }

    /// <summary>Vertex colour of a loft quad: black sills, bumpers, arch walls and engine louvres; paint elsewhere.</summary>
    private Color QuadColour(Station front, Station back, int k)
    {
        var lowerSide = k <= 1 || k >= RingSize - 2;
        var archWall = k == 2 || k == RingSize - 3;
        if (lowerSide) return Trim;
        if (archWall && (front.ArchLift > 0f || back.ArchLift > 0f)) return Trim;
        if (_style.Louvres && front.Z >= _style.LouvreFrom && back.Z <= _style.LouvreTo && k > TopFirst + 1 && k < TopLast - 1) return Trim;
        if ((k == 2 || k == RingSize - 3) && front.IntakeDepth > 0f && back.IntakeDepth > 0f) return Trim;
        // Shut lines: the 14 mm strip at each seam station goes dark, across the top skin or down the flank.
        var top = k > TopFirst && k < TopLast;
        var flank = k is 3 or 4 || k == RingSize - 4 || k == RingSize - 5;
        foreach (var seam in _style.TopSeams)
        {
            if (top && front.Z >= seam - 1e-3f && back.Z <= seam + SeamWidth + 1e-3f) return Trim;
        }
        foreach (var seam in _style.FlankSeams)
        {
            if (flank && front.Z >= seam - 1e-3f && back.Z <= seam + SeamWidth + 1e-3f) return Trim;
        }
        return _paint;
    }

    /// <summary>
    /// Normal for a quad corner at ring point (i, j): the faces around the point that meet the quad at less
    /// than the smoothing angle are averaged, so the skin shades smoothly while creases stay sharp.
    /// </summary>
    private static Vector3 VertexNormal(Vector3[][] faces, int i, int j, Vector3 faceNormal)
    {
        var sum = Vector3.Zero;
        for (var di = -1; di <= 0; di++)
        {
            var fi = i + di;
            if (fi < 0 || fi >= faces.Length) continue;
            var size = faces[fi].Length;
            for (var dj = -1; dj <= 0; dj++)
            {
                var fj = (j + dj + size) % size;
                var n = faces[fi][fj];
                if (n.Dot(faceNormal) >= SmoothingCos) sum += n;
            }
        }
        return sum.LengthSquared() < 1e-8f ? faceNormal : sum.Normalized();
    }

    private static Vector3 Centroid(Vector3[] ring)
    {
        var c = Vector3.Zero;
        foreach (var p in ring) c += p;
        return c / ring.Length;
    }

    /// <summary>Adds a triangle wound so that its front face looks along <paramref name="outward"/> (Godot front faces wind clockwise).</summary>
    private static void Tri(MeshBuilder st, Vector3 a, Vector3 na, Vector3 b, Vector3 nb, Vector3 c, Vector3 nc, Color colour, Vector3 outward)
    {
        if ((b - a).Cross(c - a).Dot(outward) > 0f)
        {
            (b, c) = (c, b);
            (nb, nc) = (nc, nb);
        }
        st.SetColor(colour);
        st.SetNormal(na);
        st.AddVertex(a);
        st.SetNormal(nb);
        st.AddVertex(b);
        st.SetNormal(nc);
        st.AddVertex(c);
    }

    private static void Cap(MeshBuilder st, Vector3[] ring, Vector3 normal, float trimBelow, Color paint)
    {
        var centre = Centroid(ring);
        for (var j = 0; j < ring.Length; j++)
        {
            var j2 = (j + 1) % ring.Length;
            var colour = (ring[j].Y + ring[j2].Y) * 0.5f < trimBelow ? Trim : paint;
            Tri(st, centre, normal, ring[j], normal, ring[j2], normal, colour, normal);
        }
    }

    // ---- details -------------------------------------------------------------------------------------

    private void BuildDetails(Node3D body, VehicleSpec spec)
    {
        var style = _style;
        var solidPaint = new StandardMaterial3D { AlbedoColor = _paint, Metallic = 0.18f, Roughness = 0.26f, ClearcoatEnabled = true, Clearcoat = 0.9f, ClearcoatRoughness = 0.08f };
        var trim = new StandardMaterial3D { AlbedoColor = Trim, Roughness = 0.75f };
        var lamp = new StandardMaterial3D { AlbedoColor = new Color(0.9f, 0.10f, 0.06f), EmissionEnabled = true, Emission = new Color(0.9f, 0.08f, 0.04f), EmissionEnergyMultiplier = 1.4f };
        var chrome = new StandardMaterial3D { AlbedoColor = new Color(0.8f, 0.8f, 0.82f), Metallic = 0.95f, Roughness = 0.2f };

        BuildInterior(body, trim);

        if (style.LampLids)
        {
            // Pop-up lamp lids at the outer front corners of the bonnet, lying on its slope and outlined by their shut
            // line; visible from the driver's seat too. Standing proud they read as two blocks on the bonnet.
            var lidZ = style.Keys[0] + 0.33f;
            var lidY = style.Sample(style.CrestH, lidZ) - 0.004f;
            var rise = (style.Sample(style.CrestH, lidZ + 0.1f) - style.Sample(style.CrestH, lidZ - 0.1f)) / 0.2f;
            foreach (var side in new[] { -1f, 1f })
            {
                // A negative pitch lifts the lid's rear edge (+z) to follow the bonnet up towards the screen.
                var lid = new Node3D { Position = new Vector3(side * 0.555f, lidY, lidZ), Rotation = new Vector3(-MathF.Atan(rise), 0f, 0f) };
                body.AddChild(lid);
                Box(lid, new Vector3(0.33f, 0.022f, 0.21f), Vector3.Zero, solidPaint);
                Box(lid, new Vector3(0.346f, 0.004f, 0.008f), new Vector3(0f, 0.0095f, -0.109f), trim);
                Box(lid, new Vector3(0.346f, 0.004f, 0.008f), new Vector3(0f, 0.0095f, 0.109f), trim);
                Box(lid, new Vector3(0.008f, 0.004f, 0.226f), new Vector3(-0.169f, 0.0095f, 0f), trim);
                Box(lid, new Vector3(0.008f, 0.004f, 0.226f), new Vector3(0.169f, 0.0095f, 0f), trim);
            }
        }
        else
        {
            // Slim lamps wrapped into the nose corners, visible from the seat as two thin strips.
            var lampMat = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.9f, 1f), EmissionEnabled = true, Emission = new Color(0.8f, 0.85f, 1f), EmissionEnergyMultiplier = 0.6f };
            var z = style.Keys[0] + 0.30f;
            var y = style.Sample(style.ShoulderH, z) - 0.02f;
            foreach (var side in new[] { -1f, 1f })
            {
                var strip = Box(body, new Vector3(0.20f, 0.025f, 0.40f), new Vector3(side * 0.70f, y, z), lampMat);
                strip.Rotation = new Vector3(0.18f, side * 0.35f, 0f);
            }
        }

        var outside = new Node3D { Name = "Outside" };
        body.AddChild(outside);
        _outsideOnly.Add(outside);

        // Inner walls of the wheel wells, just inboard of the tyres and below the skin, so nothing shows through the arches.
        foreach (var axle in new[] { -spec.FrontAxle, spec.RearAxle })
        {
            var inboard = HalfTrack(spec, axle < 0f) - 0.26f;
            var top = (axle < 0f ? style.FrontArchTop : style.RearArchTop) - 0.02f;
            var size = new Vector3(0.16f, top - 0.16f, 2f * (axle < 0f ? style.FrontArchRadius : style.RearArchRadius) + 0.04f);
            Box(outside, size, new Vector3(-inboard, (top + 0.16f) * 0.5f, axle), trim);
            Box(outside, size, new Vector3(inboard, (top + 0.16f) * 0.5f, axle), trim);
        }

        if (style.Strakes)
        {
            // The strakes across the flank intake, flush with the skin, from the door's front to the rounded back of the
            // intake ahead of the rear arch: the signature of the 1984 car (the rules of its day frowned on large open
            // intakes). The loft cuts the recess behind them (BodyStyle.Intake). Each strake is laid in short pieces that
            // follow the flank as it flares towards the rear wing, a few millimetres inside the skin so that where the
            // recess has not opened yet the skin hides it instead of fighting it.
            var strakesTo = style.IntakeTo - 0.05f;
            const int pieces = 4;
            foreach (var side in new[] { -1f, 1f })
            {
                foreach (var height in style.StrakeHeights)
                {
                    for (var p = 0; p < pieces; p++)
                    {
                        var z0 = style.StrakesFrom + (strakesTo - style.StrakesFrom) * p / pieces;
                        var z1 = style.StrakesFrom + (strakesTo - style.StrakesFrom) * (p + 1) / pieces;
                        var x0 = style.Sample(style.HalfWidth, z0) - 0.016f;
                        var x1 = style.Sample(style.HalfWidth, z1) - 0.016f;
                        var length = MathF.Sqrt((z1 - z0) * (z1 - z0) + (x1 - x0) * (x1 - x0)) + 0.004f;
                        var piece = Box(outside, new Vector3(0.024f, 0.022f, length), new Vector3(side * (x0 + x1) * 0.5f, height, (z0 + z1) * 0.5f), solidPaint);
                        // A yaw of atan2(dx, dz) turns the piece's length (+z) along the flank.
                        piece.Rotation = new Vector3(0f, side * MathF.Atan2(x1 - x0, z1 - z0), 0f);
                    }
                }
            }
        }
        else
        {
            // Big side scoops ahead of the rear wheels and a lip on the sills.
            foreach (var side in new[] { -1f, 1f })
            {
                var scoop = Box(outside, new Vector3(0.12f, 0.30f, 0.62f), new Vector3(side * 0.95f, 0.60f, spec.RearAxle - 0.80f), trim);
                scoop.Rotation = new Vector3(0f, side * 0.12f, 0f);
                Box(outside, new Vector3(0.06f, 0.05f, 1.6f), new Vector3(side * 0.97f, 0.22f, 0.2f), trim);
            }
        }

        if (style.Louvres)
        {
            // Engine cover louvres across the dark panel of the loft, between the buttresses, following the deck down;
            // raised a little over the panel's centre, where the deck dips between the buttresses.
            const int slats = 8;
            for (var i = 0; i < slats; i++)
            {
                var z = style.LouvreFrom + 0.06f + i * (style.LouvreTo - style.LouvreFrom - 0.12f) / (slats - 1);
                var crestW = style.Sample(style.CrestW, z);
                var centreH = style.Sample(style.CentreH, z);
                var y = centreH + 0.3f * (style.Sample(style.CrestH, z) - centreH) + 0.004f;
                Box(outside, new Vector3(2f * 0.62f * crestW, 0.02f, 0.05f), new Vector3(0f, y, z), solidPaint);
            }
        }
        else
        {
            // A small lip spoiler across the tail.
            var lipZ = style.Keys[^1] - 0.10f;
            Box(outside, new Vector3(1.6f, 0.04f, 0.22f), new Vector3(0f, style.Sample(style.CentreH, lipZ) - 0.005f, lipZ), solidPaint);
        }

        var tailZ = style.Keys[^1];
        if (style.SlattedTail)
        {
            // The full-width slatted grille with the lamps glowing through it, over a painted panel that carries the
            // plate in a recess; the black lower bumper below (the tail face's trim line) holds the tips.
            var grilleW = 2f * style.Sample(style.HalfWidth, tailZ) - 0.06f;
            Box(outside, new Vector3(grilleW, 0.19f, 0.05f), new Vector3(0f, 0.635f, tailZ), trim);
            Box(outside, new Vector3(0.60f, 0.13f, 0.02f), new Vector3(-0.54f, 0.635f, tailZ + 0.032f), lamp);
            Box(outside, new Vector3(0.60f, 0.13f, 0.02f), new Vector3(0.54f, 0.635f, tailZ + 0.032f), lamp);
            for (var i = 0; i < 6; i++)
            {
                Box(outside, new Vector3(grilleW, 0.014f, 0.08f), new Vector3(0f, 0.555f + i * 0.032f, tailZ + 0.035f), trim);
            }
            Box(outside, new Vector3(0.58f, 0.13f, 0.01f), new Vector3(0f, 0.465f, tailZ + 0.004f), trim);
        }
        else
        {
            // Two round lamps, a black diffuser and the exhaust tips.
            foreach (var side in new[] { -1f, 1f })
            {
                var round = new MeshInstance3D
                {
                    Mesh = new CylinderMesh { TopRadius = 0.11f, BottomRadius = 0.11f, Height = 0.06f, Material = lamp, RadialSegments = 24 },
                    Position = new Vector3(side * 0.62f, style.Sample(style.ShoulderH, tailZ) - 0.08f, tailZ + 0.01f),
                    RotationDegrees = new Vector3(90f, 0f, 0f),
                };
                outside.AddChild(round);
                var ring = new MeshInstance3D
                {
                    Mesh = new TorusMesh { InnerRadius = 0.11f, OuterRadius = 0.135f, Material = trim, RingSegments = 24, Rings = 6 },
                    Position = new Vector3(side * 0.62f, style.Sample(style.ShoulderH, tailZ) - 0.08f, tailZ + 0.01f),
                    RotationDegrees = new Vector3(90f, 0f, 0f),
                };
                outside.AddChild(ring);
            }
            Box(outside, new Vector3(1.7f, 0.26f, 0.10f), new Vector3(0f, 0.34f, tailZ - 0.02f), trim);
        }
        for (var i = 0; i < style.ExhaustTips; i++)
        {
            var x = (i < 2 ? -1f : 1f) * (0.36f + (i % 2) * 0.13f);
            var tip = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.045f, BottomRadius = 0.045f, Height = 0.16f, Material = chrome, RadialSegments = 16 },
                Position = new Vector3(x, style.SlattedTail ? 0.33f : 0.36f, tailZ + 0.03f),
                RotationDegrees = new Vector3(90f, 0f, 0f),
            };
            outside.AddChild(tip);
        }

        // Number plate, front indicators and door handles; a nose slot on the flat-twelve.
        var plateMat = new StandardMaterial3D { AlbedoColor = new Color(0.92f, 0.92f, 0.9f), Roughness = 0.5f };
        Box(outside, new Vector3(0.52f, 0.11f, 0.012f), new Vector3(0f, style.SlattedTail ? 0.465f : 0.56f, tailZ + (style.SlattedTail ? 0.016f : 0.03f)), plateMat);
        var amber = new StandardMaterial3D { AlbedoColor = Amber, EmissionEnabled = true, Emission = Amber, EmissionEnergyMultiplier = 0.35f };
        var noseZ = style.Keys[0] + 0.10f;
        foreach (var side in new[] { -1f, 1f })
        {
            if (style.LampLids)
            {
                // Indicators at the ends of the grille band, where the bumper turns back round the corner, and the
                // handle at the top of the door just ahead of its rear shut line.
                var cornerZ = style.Keys[0] + 0.11f;
                var indicator = Box(outside, new Vector3(0.12f, 0.06f, 0.03f), new Vector3(side * (style.Sample(style.HalfWidth, cornerZ) - 0.035f), 0.40f, cornerZ), amber);
                indicator.Rotation = new Vector3(0f, side * 0.75f, 0f);
                var doorZ = style.FlankSeams[^1] - 0.09f;
                Box(outside, new Vector3(0.025f, 0.03f, 0.13f), new Vector3(side * (style.Sample(style.HalfWidth, doorZ) - 0.005f), style.Sample(style.ShoulderH, doorZ) - 0.05f, doorZ), trim);
                continue;
            }
            var y = style.Sample(style.ShoulderH, noseZ) - 0.07f;
            var oldIndicator = Box(outside, new Vector3(0.16f, 0.045f, 0.03f), new Vector3(side * 0.74f, y, noseZ), amber);
            oldIndicator.Rotation = new Vector3(0f, side * 0.45f, 0f);
            var handleZ = style.CabinTo - 0.05f;
            Box(outside, new Vector3(0.025f, 0.03f, 0.13f), new Vector3(side * (style.Sample(style.HalfWidth, handleZ) - 0.005f), style.Sample(style.ShoulderH, handleZ) - 0.12f, handleZ), trim);
        }
        if (style.LampLids)
        {
            // The nose: a black egg-crate grille across the painted bumper, the driving lamps behind its outer ends;
            // only the lip under it is black (the nose face's trim line).
            var faceZ = style.Keys[0];
            var grilleW = 2f * style.HalfWidth[0] - 0.10f;
            Box(outside, new Vector3(grilleW, 0.125f, 0.02f), new Vector3(0f, 0.397f, faceZ - 0.005f), trim);
            var driving = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.86f, 0.82f), Metallic = 0.6f, Roughness = 0.2f };
            foreach (var side in new[] { -1f, 1f }) Box(outside, new Vector3(0.17f, 0.075f, 0.01f), new Vector3(side * (grilleW * 0.5f - 0.13f), 0.397f, faceZ - 0.019f), driving);
            var crate = new StandardMaterial3D { AlbedoColor = new Color(0.16f, 0.16f, 0.17f), Roughness = 0.6f };
            for (var i = 0; i <= 16; i++) Box(outside, new Vector3(0.008f, 0.12f, 0.012f), new Vector3(-grilleW * 0.5f + grilleW * i / 16f, 0.397f, faceZ - 0.027f), crate);
            foreach (var barY in new[] { 0.36f, 0.397f, 0.434f }) Box(outside, new Vector3(grilleW, 0.007f, 0.012f), new Vector3(0f, barY, faceZ - 0.027f), crate);
        }

        // The outside mirrors hang on the body, not in "Outside": the driver sees them through the side windows.
        // Each is a housing wide across the car with its glass on the rear face, towards the driver.
        var mirrorGlass = new StandardMaterial3D { AlbedoColor = new Color(0.75f, 0.77f, 0.8f), Metallic = 1f, Roughness = 0.04f };
        if (style.LampLids)
        {
            // The single mirror of the early cars, high on the driver's A-pillar: a stalk from the side glass out past
            // the wing, the head in body colour at about the height of the driver's eyes, seen through the side window.
            var stalk = Box(body, new Vector3(0.22f, 0.018f, 0.018f), new Vector3(-0.83f, 0.91f, -0.555f), trim);
            stalk.Rotation = new Vector3(0f, 0f, -MathF.Atan2(0.06f, 0.21f));
            Box(body, new Vector3(0.17f, 0.10f, 0.07f), new Vector3(-1.01f, 0.95f, -0.555f), solidPaint);
            Box(body, new Vector3(0.15f, 0.08f, 0.004f), new Vector3(-1.01f, 0.95f, -0.518f), mirrorGlass);
        }
        else
        {
            // Door mirrors on short stalks from the beltline.
            foreach (var side in new[] { -1f, 1f })
            {
                var y = style.Sample(style.ShoulderH, -0.30f);
                Box(body, new Vector3(0.14f, 0.03f, 0.03f), new Vector3(side * 0.96f, y + 0.02f, -0.30f), trim);
                Box(body, new Vector3(0.15f, 0.09f, 0.07f), new Vector3(side * 1.07f, y + 0.04f, -0.30f), solidPaint);
                Box(body, new Vector3(0.13f, 0.07f, 0.004f), new Vector3(side * 1.07f, y + 0.04f, -0.264f), mirrorGlass);
            }
        }
    }

    /// <summary>
    /// The cabin: a carpeted floor, a leather dashboard swept across the car, a hooded binnacle whose face shows
    /// the instrument panel (<see cref="MountInstruments"/>), a three-spoke wheel on a column leaning back 22
    /// degrees that turns with the steering, two seats, the A-pillars and the wipers. The wheel and the binnacle
    /// are placed from the driver's eyes (<see cref="BodyStyle.EyeZ"/>): from the seat the dials sit inside the
    /// upper half of the wheel, and the hood stays below the line of sight to the road.
    /// </summary>
    private void BuildInterior(Node3D body, Material trim)
    {
        var style = _style;
        var leather = WorldBuilder.PropMaterial("leather", style.LeatherTint, 4f);
        var dash = WorldBuilder.PropMaterial("leather", BlackLeather, 5f, matte: true);
        var carpet = WorldBuilder.PropMaterial("carpet", style.CarpetTint, 3f);
        var satin = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.55f, 0.57f), Metallic = 0.85f, Roughness = 0.38f };
        var dark = new StandardMaterial3D { AlbedoColor = new Color(0.035f, 0.035f, 0.04f), Roughness = 0.85f };
        var cowlH = style.Sample(style.CentreH, style.CowlZ);
        var eye = new Vector3(DriverX, style.EyeH, style.EyeZ);

        if (float.IsNaN(style.ScreenFrom))
        {
            Box(body, new Vector3(1.70f, 0.04f, 1.30f), new Vector3(0f, 0.36f, 0.05f), carpet);   // floor
        }
        else
        {
            // A screen set far forward leaves a long footwell: the floor runs from under the dash to the bulkhead, and
            // stays clear of the intake's recess in the flanks.
            var floorFrom = style.CowlZ + 0.15f;
            Box(body, new Vector3(1.60f, 0.04f, style.RearBulkheadZ - floorFrom), new Vector3(0f, 0.36f, (floorFrom + style.RearBulkheadZ) * 0.5f), carpet);
        }
        // Dashboard: a profile swept across the cabin, (metres behind the windscreen base, metres below it): along
        // the top, over a rolled lip 0.24 m back and down the fascia to the knees. The wheel's hub sits well
        // behind the fascia, as in a car, not inside it.
        Vector2[] dashProfile =
        [
            new(-0.03f, -0.004f), new(0.08f, -0.010f), new(0.16f, -0.016f), new(0.20f, -0.026f), new(0.225f, -0.045f),
            new(0.237f, -0.070f), new(0.230f, -0.100f), new(0.212f, -0.160f), new(0.200f, -0.280f), new(0.190f, -0.400f),
            new(0.100f, -0.430f),
        ];
        // A long screen leaves a deep dashboard: its top runs on towards the driver until the lip is half a metre from
        // the eyes, and it stays inside the base of the side glass (wider, its ends showed through the doors' tops).
        var longScreen = !float.IsNaN(style.ScreenFrom);
        var stretch = longScreen ? MathF.Max(0f, style.EyeZ - 0.50f - style.CowlZ - 0.237f) : 0f;
        var dashHalf = longScreen ? MathF.Min(0.88f, style.Sample(style.EdgeW, style.CowlZ) - 0.03f) : 0.88f;
        var swept = dashProfile.Select((p, i) => new Vector2(style.CowlZ + p.X + (i >= 2 ? stretch : 0f), cowlH + p.Y)).ToArray();
        body.AddChild(new MeshInstance3D { Mesh = SweepAcross(swept, dashHalf), MaterialOverride = dash, Name = "Dashboard" });
        if (!float.IsNaN(style.IntakeFrom))
        {
            // Below the door trim the intake's recess is not lined: a leather panel inside it keeps the road from
            // showing through the lower door. Outside, it sits behind the recess's dark wall.
            var from = MathF.Max(style.IntakeFrom, style.CowlZ);
            var to = MathF.Min(style.IntakeTo, style.RearBulkheadZ);
            var x = style.Sample(style.HalfWidth, from) - style.IntakeDepth - 0.05f;
            foreach (var side in new[] { -1f, 1f }) Box(body, new Vector3(0.02f, 0.36f, to - from), new Vector3(side * x, 0.54f, (from + to) * 0.5f), leather);
        }

        // Binnacle: its face 0.62 m ahead of the eyes and 17 degrees below them, turned to face them, in a pod
        // under a curved visor whose top stays 9 degrees below the eyes, under the line of sight to the road.
        var binnacle = new Node3D { Name = "Binnacle", Position = eye + new Vector3(0f, -0.19f, -0.62f), Rotation = new Vector3(-MathF.Atan2(0.19f, 0.62f), 0f, 0f) };
        body.AddChild(binnacle);
        Box(binnacle, new Vector3(0.42f, 0.16f, 0.12f), new Vector3(0f, -0.01f, -0.065f), dash);
        // (z towards the driver, y up) in the binnacle's frame, from behind the pod over the top to the lip.
        Vector2[] visor =
        [
            new(-0.13f, 0.0f), new(-0.13f, 0.06f), new(-0.10f, 0.095f), new(-0.04f, 0.11f), new(0.03f, 0.105f),
            new(0.08f, 0.092f), new(0.10f, 0.078f), new(0.098f, 0.064f),
        ];
        binnacle.AddChild(new MeshInstance3D { Mesh = SweepAcross(visor, 0.215f), MaterialOverride = dash, Name = "Visor" });
        var faceSize = new Vector2(0.36f, 0.36f * InstrumentCluster.PanelSize.Y / InstrumentCluster.PanelSize.X);
        _gaugeFace = new MeshInstance3D { Mesh = new QuadMesh { Size = faceSize }, MaterialOverride = dark, Position = new Vector3(0f, 0f, 0.002f), Name = "Dials" };
        binnacle.AddChild(_gaugeFace);

        // Steering wheel: its hub 0.50 m ahead of the eyes and 26 degrees below them, so its rim passes just above
        // the dials, with its column running down into the fascia; the spin node turns with the steering.
        var hub = eye + new Vector3(0f, -0.50f * MathF.Tan(Mathf.DegToRad(26f)), -0.50f);
        var column = new Node3D { Name = "Column", Position = hub, Rotation = new Vector3(Mathf.DegToRad(68f), 0f, 0f) };
        body.AddChild(column);
        column.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.032f, BottomRadius = 0.038f, Height = 0.30f, Material = dark, RadialSegments = 16 }, Position = new Vector3(0f, -0.17f, 0f) });
        var spin = new Node3D { Name = "Wheel" };
        column.AddChild(spin);
        _steeringSpin = spin;
        spin.AddChild(new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.158f, OuterRadius = 0.194f, Material = dash, Rings = 48, RingSegments = 12 } });
        spin.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.045f, BottomRadius = 0.05f, Height = 0.035f, Material = dark, RadialSegments = 24 } });
        foreach (var angle in new[] { 0f, 90f, -90f })
        {
            // Local +Z of the tilted column points down, so the spoke at 0 degrees is the bottom one.
            var a = Mathf.DegToRad(angle);
            spin.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.026f, 0.007f, 0.13f), Material = satin },
                Position = new Vector3(MathF.Sin(a) * 0.105f, 0.004f, MathF.Cos(a) * 0.105f),
                Rotation = new Vector3(0f, a, 0f),
            });
        }

        foreach (var x in new[] { DriverX, -DriverX })
        {
            Box(body, new Vector3(0.50f, 0.14f, 0.52f), new Vector3(x, 0.47f, 0.22f), leather);
            var backrest = Box(body, new Vector3(0.50f, 0.52f, 0.12f), new Vector3(x, 0.72f, 0.50f), leather);
            // A positive pitch tips the top towards +z, the tail: the backrest reclines from the cushion onto the headrest.
            backrest.Rotation = new Vector3(0.22f, 0f, 0f);
            Box(body, new Vector3(0.26f, 0.12f, 0.10f), new Vector3(x, 0.99f, 0.54f), leather);
        }

        // A-pillars from the cowl corners to the roof corners above the screen. The eyes are twice as far from the
        // foot of the left one as from its top, so a pillar of even section looks twice as wide where it meets
        // the roof (a 45 mm square bar read as a black band across the view); it narrows towards the roof
        // instead and stays a slim, even line framing the screen. From outside it is seen through the tinted
        // glass, where the taper does not show.
        var roofZ = style.Keys.First(k => k > style.CabinFrom);
        foreach (var side in new[] { -1f, 1f })
        {
            var a = new Vector3(side * style.Sample(style.CrestW, style.CowlZ), style.Sample(style.CrestH, style.CowlZ) - 0.015f, style.CowlZ);
            var b = new Vector3(side * style.Sample(style.CrestW, roofZ), style.Sample(style.CrestH, roofZ) - 0.015f, roofZ);
            Pillar(body, a, b, 0.020f, 0.013f, dash);
        }

        // Wipers parked across the base of the screen.
        foreach (var x in new[] { -0.40f, 0.22f })
        {
            var blade = Box(body, new Vector3(0.52f, 0.012f, 0.03f), new Vector3(x, cowlH + 0.012f, style.CowlZ - 0.05f), trim);
            blade.RotationDegrees = new Vector3(0f, 12f, 0f);
        }
    }

    /// <summary>
    /// A profile in the (z, y) plane swept across the car from -<paramref name="half"/> to +<paramref name="half"/>
    /// in x, its ends closed: the dashboard, the binnacle's visor. Smooth normals along the profile let a rolled
    /// edge catch a highlight. The profile's outside is on its left when walked in order: up when it runs
    /// towards +z, towards the driver (+z) when it runs down.
    /// </summary>
    private static ArrayMesh SweepAcross(Vector2[] profile, float half)
    {
        Vector3 At(int i, float x) => new(x, profile[i].Y, profile[i].X);
        // Along the profile (z, y) turned a quarter counter-clockwise points out of the shape.
        var normals = new Vector3[profile.Length];
        for (var i = 0; i < profile.Length; i++)
        {
            var t = profile[Math.Min(i + 1, profile.Length - 1)] - profile[Math.Max(i - 1, 0)];
            normals[i] = new Vector3(0f, t.X, -t.Y).Normalized();
        }
        var st = new MeshBuilder();
        for (var i = 0; i < profile.Length - 1; i++)
        {
            var outward = (normals[i] + normals[i + 1]).Normalized();
            Tri(st, At(i, -half), normals[i], At(i + 1, -half), normals[i + 1], At(i, half), normals[i], Colors.White, outward);
            Tri(st, At(i, half), normals[i], At(i + 1, -half), normals[i + 1], At(i + 1, half), normals[i + 1], Colors.White, outward);
        }
        foreach (var side in new[] { -1f, 1f })
        {
            var normal = new Vector3(side, 0f, 0f);
            var centre = Vector3.Zero;
            for (var i = 0; i < profile.Length; i++) centre += At(i, side * half);
            centre /= profile.Length;
            for (var i = 0; i < profile.Length; i++)
            {
                Tri(st, centre, normal, At(i, side * half), normal, At((i + 1) % profile.Length, side * half), normal, Colors.White, normal);
            }
        }
        return st.Commit();
    }

    /// <summary>A round bar from <paramref name="a"/> to <paramref name="b"/> that narrows from <paramref name="radiusA"/> to <paramref name="radiusB"/>.</summary>
    private static void Pillar(Node3D parent, Vector3 a, Vector3 b, float radiusA, float radiusB, Material material)
    {
        // A cylinder runs along local +Y with its top radius there: turned so +Y becomes -Z, then looking from a to b, its top lands on b.
        var mesh = new CylinderMesh { BottomRadius = radiusA, TopRadius = radiusB, Height = a.DistanceTo(b), RadialSegments = 12, Rings = 1, Material = material };
        var basis = Basis.LookingAt(b - a, Vector3.Up) * new Basis(Vector3.Right, -Mathf.Pi / 2f);
        parent.AddChild(new MeshInstance3D { Mesh = mesh, Transform = new Transform3D(basis, (a + b) * 0.5f) });
    }

    // ---- wheels --------------------------------------------------------------------------------------

    private void BuildWheels(VehicleSpec spec)
    {
        var rubber = new StandardMaterial3D { AlbedoColor = new Color(0.05f, 0.05f, 0.05f), Roughness = 0.92f };
        // Cellular noise as a normal map gives the rubber a tread-like grain (triplanar: the lathe has no UVs).
        var tread = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.Cellular, Frequency = 0.08f, Seed = 9 };
        rubber.NormalEnabled = true;
        rubber.NormalTexture = new NoiseTexture2D { Noise = tread, Width = 256, Height = 256, Seamless = true, AsNormalMap = true, BumpStrength = 4f };
        rubber.NormalScale = 0.6f;
        rubber.Uv1Triplanar = true;
        rubber.Uv1Scale = new Vector3(14f, 14f, 14f);
        var steel = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.45f, 0.47f), Metallic = 0.85f, Roughness = 0.4f };
        var caliperMat = new StandardMaterial3D { AlbedoColor = new Color(0.75f, 0.08f, 0.05f), Roughness = 0.5f };
        var alloy = new StandardMaterial3D { AlbedoColor = new Color(0.76f, 0.76f, 0.78f), Metallic = 0.9f, Roughness = 0.28f };
        var dish = new StandardMaterial3D { AlbedoColor = new Color(0.16f, 0.16f, 0.17f), Metallic = 0.6f, Roughness = 0.5f };
        var style = _style;
        var tyres = new[] { Tyre(spec.WheelRadius, style.FrontTyreWidth * 0.5f, style.RimRadius, rubber), Tyre(spec.WheelRadius, style.RearTyreWidth * 0.5f, style.RimRadius, rubber) };
        var front = HalfTrack(spec, true);
        var rear = HalfTrack(spec, false);
        var hubs = new[]
        {
            new SN.Vector3(-front, spec.HubHeight, spec.FrontAxle),
            new SN.Vector3(front, spec.HubHeight, spec.FrontAxle),
            new SN.Vector3(-rear, spec.HubHeight, -spec.RearAxle),
            new SN.Vector3(rear, spec.HubHeight, -spec.RearAxle),
        };
        for (var i = 0; i < 4; i++)
        {
            var pivot = new Node3D { Name = $"Wheel{i}", Position = Convert.ToGodot(hubs[i]) };
            AddChild(pivot);
            _outsideOnly.Add(pivot);
            var spinner = new Node3D { Name = "Spin" };
            pivot.AddChild(spinner);
            var isFront = i < 2;
            spinner.AddChild(new MeshInstance3D { Mesh = tyres[isFront ? 0 : 1] });
            // The rim sits inside the tyre's sidewall: its faces move in with a narrower tyre.
            var inset = (isFront ? style.FrontTyreWidth : style.RearTyreWidth) / 0.27f;
            var rim = style.RimRadius;

            // Five-spoke star rim on the outer face of the wheel.
            var side = MathF.Sign(hubs[i].X);
            // Brake disc on the spinner and a red caliper on the pivot (it steers but does not spin), behind the spokes.
            spinner.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.15f, BottomRadius = 0.15f, Height = 0.025f, Material = steel, RadialSegments = 32 },
                Position = new Vector3(side * 0.03f, 0f, 0f),
                RotationDegrees = new Vector3(0f, 0f, 90f),
            });
            pivot.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.05f, 0.12f, 0.07f), Material = caliperMat },
                Position = new Vector3(side * 0.03f, 0.10f, -0.06f),
            });
            spinner.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = rim - 0.013f, BottomRadius = rim - 0.013f, Height = 0.02f, Material = dish, RadialSegments = 24 },
                Position = new Vector3(side * 0.07f * inset, 0f, 0f),
                RotationDegrees = new Vector3(0f, 0f, 90f),
            });
            spinner.AddChild(new MeshInstance3D
            {
                Mesh = new TorusMesh { InnerRadius = rim - 0.023f, OuterRadius = rim, Material = alloy, RingSegments = 32, Rings = 8 },
                Position = new Vector3(side * 0.12f * inset, 0f, 0f),
                RotationDegrees = new Vector3(0f, 0f, 90f),
            });
            spinner.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.055f, BottomRadius = 0.055f, Height = 0.04f, Material = alloy, RadialSegments = 16 },
                Position = new Vector3(side * 0.115f * inset, 0f, 0f),
                RotationDegrees = new Vector3(0f, 0f, 90f),
            });
            for (var s = 0; s < 5; s++)
            {
                var angle = s * Mathf.Tau / 5f;
                spinner.AddChild(new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(0.025f, rim - 0.028f, 0.045f), Material = alloy },
                    Position = new Vector3(side * 0.11f * inset, ((rim - 0.028f) * 0.5f + 0.01f) * Mathf.Cos(angle), ((rim - 0.028f) * 0.5f + 0.01f) * Mathf.Sin(angle)),
                    Rotation = new Vector3(angle, 0f, 0f),
                });
            }
            _wheelPivots[i] = pivot;
            _wheelSpinners[i] = spinner;
        }
    }

    /// <summary>Tyre as a lathe around the x axis: flat tread, rounded shoulders, open bore for a rim of radius <paramref name="rim"/>.</summary>
    private static ArrayMesh Tyre(float radius, float halfWidth, float rim, Material material)
    {
        var bead = rim - 0.003f;
        var sidewall = rim + 0.072f;
        var profile = new Vector2[]
        {
            new(bead, -halfWidth + 0.005f), new(sidewall, -halfWidth), new(radius - 0.006f, -halfWidth + 0.025f), new(radius, 0f),
            new(radius - 0.006f, halfWidth - 0.025f), new(sidewall, halfWidth), new(bead, halfWidth - 0.005f),
        };
        const int segments = 44;
        var st = new MeshBuilder();
        for (var p = 0; p < profile.Length; p++)
        {
            var p2 = (p + 1) % profile.Length;
            // Outward 2D normal of this profile edge in the (radius, x) plane.
            var edge = profile[p2] - profile[p];
            var edgeNormal = new Vector2(edge.Y, -edge.X).Normalized();
            for (var s = 0; s < segments; s++)
            {
                var a0 = s * Mathf.Tau / segments;
                var a1 = (s + 1) * Mathf.Tau / segments;
                var v00 = Revolve(profile[p], a0);
                var v01 = Revolve(profile[p], a1);
                var v10 = Revolve(profile[p2], a0);
                var v11 = Revolve(profile[p2], a1);
                var n0 = RevolveNormal(edgeNormal, a0);
                var n1 = RevolveNormal(edgeNormal, a1);
                Tri(st, v00, n0, v10, n0, v01, n1, Colors.White, n0 + n1);
                Tri(st, v01, n1, v10, n0, v11, n1, Colors.White, n0 + n1);
            }
        }
        var mesh = st.Commit();
        mesh.SurfaceSetMaterial(0, material);
        return mesh;

        static Vector3 Revolve(Vector2 rx, float angle) => new(rx.Y, rx.X * Mathf.Cos(angle), rx.X * Mathf.Sin(angle));
        static Vector3 RevolveNormal(Vector2 n, float angle) => new Vector3(n.Y, n.X * Mathf.Cos(angle), n.X * Mathf.Sin(angle)).Normalized();
    }

    /// <summary>Half track of an axle as drawn: the style's (the period car's front and rear differ), or the simulation's.</summary>
    private float HalfTrack(VehicleSpec spec, bool front)
    {
        var drawn = front ? _style.FrontTrack : _style.RearTrack;
        return float.IsNaN(drawn) ? spec.HalfTrack : drawn;
    }

    public void SetPose(SN.Vector3 position, SN.Quaternion orientation) => Transform = Convert.ToGodot(position, orientation);

    public void SetWheels(WheelState[] wheels, float steerAngle, VehicleSpec spec)
    {
        for (var i = 0; i < 4; i++)
        {
            var pivot = _wheelPivots[i];
            var w = wheels[i];
            // The wheel centre hangs below the hub by the suspension extension: full droop in the air, less on the ground.
            var compression = w.InContact ? w.Compression : 0f;
            var hubY = spec.HubHeight - (spec.SuspensionRest - compression);
            var local = pivot.Position;
            pivot.Position = new Vector3(local.X, hubY, local.Z);
            var steer = w.IsFront ? -steerAngle : 0f;
            pivot.Rotation = new Vector3(0f, steer, 0f);
            _wheelSpinners[i].Rotation = new Vector3(-w.SpinAngle, 0f, 0f);
        }
        TurnSteeringWheel(steerAngle);
    }

    private static MeshInstance3D Box(Node3D parent, Vector3 size, Vector3 position, Material material)
    {
        var box = new MeshInstance3D { Mesh = new BoxMesh { Size = size, Material = material }, Position = position };
        parent.AddChild(box);
        return box;
    }
}

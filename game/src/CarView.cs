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
/// the centre of gravity, as in the simulation, so the body sits lower by the static ride height. From the
/// driver's seat only the bonnet, the lamps and a dashboard top are shown (<see cref="SetViewpoint"/>).
/// </summary>
internal sealed partial class CarView : Node3D
{
    private const int HalfPoints = 11;                     // P0..P10 of the half profile, floor centre to roof centre
    private const int RingSize = HalfPoints * 2 - 2;       // both halves share the floor and roof centre points
    private const int TopFirst = 5;                        // first strip of the top skin (bonnet edge / greenhouse base)
    private const int TopLast = RingSize - 6;              // last strip of the top skin, mirrored side
    private const float ArchRadius = 0.44f;                // wheel radius plus the gap to the wing lip
    private const float SmoothingCos = 0.5f;               // faces meeting at more than 60 degrees keep a hard edge

    public static readonly Color DefaultPaint = new(0.76f, 0.03f, 0.05f);
    /// <summary>Paints for the drone cars, by their colour index.</summary>
    public static readonly Color[] DronePaints = [new(0.12f, 0.25f, 0.70f), new(0.88f, 0.88f, 0.84f), new(0.90f, 0.72f, 0.10f)];
    private static readonly Color Trim = new(0.06f, 0.06f, 0.065f);

    private readonly Node3D[] _wheelPivots = new Node3D[4];
    private readonly Node3D[] _wheelSpinners = new Node3D[4];
    private readonly List<Node3D> _outsideOnly = [];
    private readonly List<Node3D> _cockpitOnly = [];
    private BodyStyle _style = BodyStyle.FlatTwelve;
    private Color _paint = DefaultPaint;
    private float _cgHeight;
    private float _wheelRadius;
    private float _wheelAngle;

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

    /// <summary>Shows what the driver sees from the seat (bonnet, lamp lids, dashboard top) or the whole car.</summary>
    public void SetViewpoint(bool insideCar)
    {
        foreach (var node in _outsideOnly) node.Visible = !insideCar;
        foreach (var node in _cockpitOnly) node.Visible = insideCar;
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
        public bool LampLids;                // pop-up headlamp lids on the wing crests
        public bool Strakes;                 // slatted flank intakes
        public bool Louvres;                 // louvred engine cover (otherwise a glass cover)
        public float GlassDeckFrom = 99f;    // z range of the glass engine cover
        public float GlassDeckTo = -99f;
        public bool SlattedTail;             // full-width slatted tail with light bars (otherwise round lamps)
        public int ExhaustTips;

        /// <summary>The 1984 flat-twelve: blade nose, lamp lids, dipped bonnet, small cabin, widening rear, strakes, louvres, slatted tail.</summary>
        public static readonly BodyStyle FlatTwelve = new()
        {
            Keys = [-2.26f, -2.05f, -1.71f, -1.27f, -0.83f, -0.72f, -0.20f, 0.40f, 0.84f, 1.28f, 1.72f, 2.20f],
            FloorH = [0.22f, 0.17f, 0.15f, 0.15f, 0.15f, 0.15f, 0.15f, 0.15f, 0.15f, 0.15f, 0.17f, 0.21f],
            HalfWidth = [0.86f, 0.90f, 0.93f, 0.94f, 0.95f, 0.95f, 0.955f, 0.965f, 0.975f, 0.99f, 0.985f, 0.95f],
            WaistH = [0.40f, 0.43f, 0.46f, 0.46f, 0.46f, 0.46f, 0.46f, 0.47f, 0.48f, 0.50f, 0.49f, 0.48f],
            ShoulderH = [0.50f, 0.56f, 0.63f, 0.71f, 0.75f, 0.78f, 0.82f, 0.84f, 0.86f, 0.87f, 0.86f, 0.84f],
            EdgeW = [0.80f, 0.84f, 0.86f, 0.88f, 0.88f, 0.74f, 0.71f, 0.73f, 0.80f, 0.84f, 0.86f, 0.86f],
            EdgeH = [0.52f, 0.585f, 0.65f, 0.73f, 0.77f, 0.80f, 0.86f, 0.87f, 0.92f, 0.93f, 0.925f, 0.89f],
            CrestW = [0.62f, 0.62f, 0.62f, 0.62f, 0.62f, 0.62f, 0.60f, 0.61f, 0.64f, 0.64f, 0.65f, 0.66f],
            CrestH = [0.53f, 0.60f, 0.665f, 0.745f, 0.785f, 0.81f, 1.10f, 1.10f, 0.94f, 0.95f, 0.94f, 0.905f],
            CentreH = [0.50f, 0.545f, 0.59f, 0.67f, 0.735f, 0.80f, 1.13f, 1.13f, 0.95f, 0.955f, 0.945f, 0.905f],
            LampLids = true,
            Strakes = true,
            Louvres = true,
            SlattedTail = true,
            ExhaustTips = 0,
        };

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

    private readonly record struct Station(float Z, float ArchLift, bool Cabin);

    /// <summary>Design stations, a dense run along the bonnet (it is what the driver looks at) and samples through each wheel arch.</summary>
    private Station[] Stations(VehicleSpec spec)
    {
        var style = _style;
        var axles = new[] { -spec.FrontAxle, spec.RearAxle };
        var zs = new List<float>(style.Keys);
        for (var z = style.Keys[0] + 0.06f; z < style.CowlZ; z += 0.10f) zs.Add(z);
        foreach (var axle in axles)
        {
            foreach (var offset in new[] { -0.46f, -0.44f, -0.40f, -0.30f, -0.15f, 0f, 0.15f, 0.30f, 0.40f, 0.44f, 0.46f }) zs.Add(axle + offset);
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
                if (d <= ArchRadius + 1e-4f)
                {
                    // Round arch over the hub, ending at hub height, flattened under the wing where the body is low.
                    var arch = spec.WheelRadius + MathF.Sqrt(MathF.Max(ArchRadius * ArchRadius - d * d, 0f));
                    lift = MathF.Max(lift, MathF.Min(arch, axle < 0f ? style.FrontArchTop : style.RearArchTop));
                }
            }
            stations.Add(new Station(z, lift, z > style.CabinFrom && z < style.CabinTo));
        }
        return [.. stations];
    }

    /// <summary>Closed outline of a cross-section: floor centre, up the right side to the roof centre, down the left.</summary>
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
        if (s.ArchLift > 0f)
        {
            // The sill and the lower flank are cut up to the arch; the floor centre stays down so the underside
            // becomes a dark slope into the well instead of a floor rising through the bonnet dip.
            for (var k = 1; k <= 3; k++) half[k].Y = MathF.Max(half[k].Y, s.ArchLift);
        }
        var ring = new Vector3[RingSize];
        for (var k = 0; k < HalfPoints; k++) ring[k] = new Vector3(half[k].X, half[k].Y, z);
        for (var k = 1; k < HalfPoints - 1; k++)
        {
            var mirrored = half[HalfPoints - 1 - k];
            ring[HalfPoints - 1 + k] = new Vector3(-mirrored.X, mirrored.Y, z);
        }
        return ring;
    }

    private void BuildBody(VehicleSpec spec)
    {
        var style = _style;
        var body = new Node3D { Name = "Body", Position = new Vector3(0f, -_cgHeight, 0f) };
        AddChild(body);

        var stations = Stations(spec);
        var rings = new Vector3[stations.Length][];
        for (var i = 0; i < stations.Length; i++) rings[i] = Ring(stations[i]);

        // Outward unit normal of every quad (station i to i+1, ring point k to k+1). The ring runs
        // counter-clockwise in the x-y plane (floor centre, up the right side, over the roof, down the left),
        // so the outward side of a strip is its tangent turned clockwise; a centroid test would misjudge the
        // strips that lean towards the centre line, such as the bonnet dip beside the wing crests.
        var faces = new Vector3[stations.Length - 1][];
        for (var i = 0; i < faces.Length; i++)
        {
            faces[i] = new Vector3[RingSize];
            var centroid = (Centroid(rings[i]) + Centroid(rings[i + 1])) * 0.5f;
            for (var k = 0; k < RingSize; k++)
            {
                var k2 = (k + 1) % RingSize;
                var a = rings[i][k];
                var b = rings[i][k2];
                var c = rings[i + 1][k];
                var d = rings[i + 1][k2];
                var n = (d - a).Cross(b - c);
                if (n.LengthSquared() < 1e-10f) n = Vector3.Up;
                n = n.Normalized();
                var tangent = (b - a) + (d - c);
                var hint = new Vector3(tangent.Y, -tangent.X, 0f);
                var side = n.Dot(hint);
                if (MathF.Abs(side) < 0.05f * hint.Length()) side = n.Dot((a + b + c + d) * 0.25f - centroid);
                if (side < 0f) n = -n;
                faces[i][k] = n;
            }
        }

        var bonnet = new SurfaceTool();
        var shell = new SurfaceTool();
        var glass = new SurfaceTool();
        bonnet.Begin(Mesh.PrimitiveType.Triangles);
        shell.Begin(Mesh.PrimitiveType.Triangles);
        glass.Begin(Mesh.PrimitiveType.Triangles);

        for (var i = 0; i < faces.Length; i++)
        {
            var front = stations[i];
            var back = stations[i + 1];
            var isBonnet = back.Z <= style.CowlZ + 0.001f;
            for (var k = 0; k < RingSize; k++)
            {
                var k2 = (k + 1) % RingSize;
                // Windscreen and rear window span a cabin and a non-cabin station; between cabin stations only
                // the flank strips are side windows, the roof stays painted. A glass engine cover is glass on top.
                var top = k >= TopFirst && k <= TopLast;
                var isGlass = front.Cabin != back.Cabin ? top : front.Cabin && (k == TopFirst || k == TopLast);
                if (!isGlass && k > TopFirst && k < TopLast && front.Z >= style.GlassDeckFrom - 0.01f && back.Z <= style.GlassDeckTo + 0.01f) isGlass = true;
                var target = isGlass ? glass : isBonnet ? bonnet : shell;
                var colour = QuadColour(front, back, k);
                var faceNormal = faces[i][k];
                var a = rings[i][k];
                var b = rings[i][k2];
                var c = rings[i + 1][k];
                var d = rings[i + 1][k2];
                var na = VertexNormal(faces, i, k, faceNormal);
                var nb = VertexNormal(faces, i, k2, faceNormal);
                var nc = VertexNormal(faces, i + 1, k, faceNormal);
                var nd = VertexNormal(faces, i + 1, k2, faceNormal);
                Tri(target, a, na, c, nc, b, nb, colour, faceNormal);
                Tri(target, b, nb, c, nc, d, nd, colour, faceNormal);
            }
        }
        Cap(bonnet, rings[0], new Vector3(0f, 0f, -1f), 0.42f, _paint);
        Cap(shell, rings[^1], new Vector3(0f, 0f, 1f), 0.50f, _paint);
        // Bulkhead at the cowl: from the driver's seat the bonnet must not read as an open tube with the wheels inside.
        var cowl = Array.FindIndex(stations, s => MathF.Abs(s.Z - style.CowlZ) < 1e-3f);
        Cap(bonnet, rings[cowl], new Vector3(0f, 0f, 1f), float.MaxValue, _paint);

        // Little metallic: a metallic albedo reflects the sky and turns the red pink; the gloss comes from the clearcoat.
        var paint = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            Metallic = 0.12f,
            Roughness = 0.30f,
            ClearcoatEnabled = true,
            Clearcoat = 0.7f,
            ClearcoatRoughness = 0.15f,
        };
        var tint = new StandardMaterial3D { AlbedoColor = new Color(0.10f, 0.13f, 0.17f), Metallic = 0.9f, Roughness = 0.06f };
        var bonnetMesh = new MeshInstance3D { Mesh = bonnet.Commit(), MaterialOverride = paint, Name = "Bonnet" };
        var shellMesh = new MeshInstance3D { Mesh = shell.Commit(), MaterialOverride = paint, Name = "Shell" };
        var glassMesh = new MeshInstance3D { Mesh = glass.Commit(), MaterialOverride = tint, Name = "Glass" };
        body.AddChild(bonnetMesh);
        body.AddChild(shellMesh);
        body.AddChild(glassMesh);
        _outsideOnly.Add(shellMesh);
        _outsideOnly.Add(glassMesh);

        BuildDetails(body, spec);
    }

    /// <summary>Vertex colour of a loft quad: black sills, bumpers, arch walls and engine louvres; paint elsewhere.</summary>
    private Color QuadColour(Station front, Station back, int k)
    {
        var lowerSide = k <= 1 || k >= RingSize - 2;
        var archWall = k == 2 || k == RingSize - 3;
        if (lowerSide) return Trim;
        if (archWall && (front.ArchLift > 0f || back.ArchLift > 0f)) return Trim;
        if (_style.Louvres && front.Z >= 0.83f && back.Z <= 1.75f && k > TopFirst && k < TopLast) return Trim;
        return _paint;
    }

    /// <summary>
    /// Normal for a quad corner at ring point (i, k): the faces around the point that meet the quad at less
    /// than the smoothing angle are averaged, so the skin shades smoothly while creases stay sharp.
    /// </summary>
    private static Vector3 VertexNormal(Vector3[][] faces, int i, int k, Vector3 faceNormal)
    {
        var sum = Vector3.Zero;
        for (var di = -1; di <= 0; di++)
        {
            var fi = i + di;
            if (fi < 0 || fi >= faces.Length) continue;
            for (var dk = -1; dk <= 0; dk++)
            {
                var fk = (k + dk + RingSize) % RingSize;
                var n = faces[fi][fk];
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
    private static void Tri(SurfaceTool st, Vector3 a, Vector3 na, Vector3 b, Vector3 nb, Vector3 c, Vector3 nc, Color colour, Vector3 outward)
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

    private static void Cap(SurfaceTool st, Vector3[] ring, Vector3 normal, float trimBelow, Color paint)
    {
        var centre = Centroid(ring);
        for (var k = 0; k < RingSize; k++)
        {
            var k2 = (k + 1) % RingSize;
            var colour = (ring[k].Y + ring[k2].Y) * 0.5f < trimBelow ? Trim : paint;
            Tri(st, centre, normal, ring[k], normal, ring[k2], normal, colour, normal);
        }
    }

    // ---- details -------------------------------------------------------------------------------------

    private void BuildDetails(Node3D body, VehicleSpec spec)
    {
        var style = _style;
        var solidPaint = new StandardMaterial3D { AlbedoColor = _paint, Metallic = 0.12f, Roughness = 0.30f, ClearcoatEnabled = true, Clearcoat = 0.7f, ClearcoatRoughness = 0.15f };
        var trim = new StandardMaterial3D { AlbedoColor = Trim, Roughness = 0.75f };
        var lamp = new StandardMaterial3D { AlbedoColor = new Color(0.9f, 0.10f, 0.06f), EmissionEnabled = true, Emission = new Color(0.9f, 0.08f, 0.04f), EmissionEnergyMultiplier = 1.4f };
        var chrome = new StandardMaterial3D { AlbedoColor = new Color(0.8f, 0.8f, 0.82f), Metallic = 0.95f, Roughness = 0.2f };

        // Dashboard top: fills the view below the cowl from the driver's seat.
        var dash = Box(body, new Vector3(2.6f, 0.10f, 0.80f), new Vector3(0f, style.Sample(style.CentreH, style.CowlZ) - 0.06f, style.CowlZ + 0.42f), trim);
        _cockpitOnly.Add(dash);

        if (style.LampLids)
        {
            // Pop-up lamp lids on the wing crests, visible from the driver's seat too.
            var lidZ = style.Keys[0] + 0.42f;
            var lidY = style.Sample(style.CrestH, lidZ) - 0.02f;
            Box(body, new Vector3(0.44f, 0.06f, 0.36f), new Vector3(-0.58f, lidY, lidZ), solidPaint);
            Box(body, new Vector3(0.44f, 0.06f, 0.36f), new Vector3(0.58f, lidY, lidZ), solidPaint);
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
            var inboard = spec.HalfTrack - 0.26f;
            var top = (axle < 0f ? style.FrontArchTop : style.RearArchTop) - 0.02f;
            var size = new Vector3(0.16f, top - 0.16f, 0.92f);
            Box(outside, size, new Vector3(-inboard, (top + 0.16f) * 0.5f, axle), trim);
            Box(outside, size, new Vector3(inboard, (top + 0.16f) * 0.5f, axle), trim);
        }

        if (style.Strakes)
        {
            // Flank intakes: a dark recess with five horizontal strakes, running into the rear arches.
            foreach (var side in new[] { -1f, 1f })
            {
                var strakes = new Node3D { Position = new Vector3(side * 0.95f, 0.63f, 0.30f), Rotation = new Vector3(0f, side * 0.019f, 0f) };
                outside.AddChild(strakes);
                Box(strakes, new Vector3(0.05f, 0.34f, 1.20f), Vector3.Zero, trim);
                for (var i = 0; i < 5; i++)
                {
                    Box(strakes, new Vector3(0.07f, 0.022f, 1.18f), new Vector3(side * 0.035f, -0.13f + i * 0.065f, 0f), solidPaint);
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
            // Engine cover louvres over the dark panel of the loft.
            for (var i = 0; i < 7; i++)
            {
                Box(outside, new Vector3(1.26f, 0.02f, 0.05f), new Vector3(0f, 0.96f, 1.00f + i * 0.14f), solidPaint);
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
            // Full-width black grille, lamps behind it, five slats across.
            Box(outside, new Vector3(1.90f, 0.32f, 0.05f), new Vector3(0f, 0.68f, tailZ), trim);
            Box(outside, new Vector3(0.64f, 0.16f, 0.02f), new Vector3(-0.56f, 0.69f, tailZ + 0.032f), lamp);
            Box(outside, new Vector3(0.64f, 0.16f, 0.02f), new Vector3(0.56f, 0.69f, tailZ + 0.032f), lamp);
            for (var i = 0; i < 5; i++)
            {
                Box(outside, new Vector3(1.90f, 0.018f, 0.09f), new Vector3(0f, 0.56f + i * 0.06f, tailZ + 0.035f), trim);
            }
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
                Position = new Vector3(x, 0.36f, tailZ + 0.03f),
                RotationDegrees = new Vector3(90f, 0f, 0f),
            };
            outside.AddChild(tip);
        }

        if (style.LampLids)
        {
            // Single high-mounted mirror on the driver's A-pillar, as on the early cars.
            Box(outside, new Vector3(0.22f, 0.03f, 0.03f), new Vector3(-0.81f, 0.975f, -0.42f), trim);
            Box(outside, new Vector3(0.08f, 0.11f, 0.17f), new Vector3(-0.94f, 0.98f, -0.42f), trim);
        }
        else
        {
            // Door mirrors on short stalks from the beltline.
            foreach (var side in new[] { -1f, 1f })
            {
                var y = style.Sample(style.ShoulderH, -0.30f);
                Box(outside, new Vector3(0.14f, 0.03f, 0.03f), new Vector3(side * 0.96f, y + 0.02f, -0.30f), trim);
                Box(outside, new Vector3(0.07f, 0.09f, 0.15f), new Vector3(side * 1.04f, y + 0.04f, -0.30f), solidPaint);
            }
        }
    }

    // ---- wheels --------------------------------------------------------------------------------------

    private void BuildWheels(VehicleSpec spec)
    {
        var rubber = new StandardMaterial3D { AlbedoColor = new Color(0.05f, 0.05f, 0.05f), Roughness = 0.92f };
        var alloy = new StandardMaterial3D { AlbedoColor = new Color(0.76f, 0.76f, 0.78f), Metallic = 0.9f, Roughness = 0.28f };
        var dish = new StandardMaterial3D { AlbedoColor = new Color(0.16f, 0.16f, 0.17f), Metallic = 0.6f, Roughness = 0.5f };
        var tyreMesh = Tyre(spec.WheelRadius, rubber);
        var hubs = new[]
        {
            new SN.Vector3(-spec.HalfTrack, spec.HubHeight, spec.FrontAxle),
            new SN.Vector3(spec.HalfTrack, spec.HubHeight, spec.FrontAxle),
            new SN.Vector3(-spec.HalfTrack, spec.HubHeight, -spec.RearAxle),
            new SN.Vector3(spec.HalfTrack, spec.HubHeight, -spec.RearAxle),
        };
        for (var i = 0; i < 4; i++)
        {
            var pivot = new Node3D { Name = $"Wheel{i}", Position = Convert.ToGodot(hubs[i]) };
            AddChild(pivot);
            _outsideOnly.Add(pivot);
            var spinner = new Node3D { Name = "Spin" };
            pivot.AddChild(spinner);
            spinner.AddChild(new MeshInstance3D { Mesh = tyreMesh });

            // Five-spoke star rim on the outer face of the wheel.
            var side = MathF.Sign(hubs[i].X);
            spinner.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.215f, BottomRadius = 0.215f, Height = 0.02f, Material = dish, RadialSegments = 24 },
                Position = new Vector3(side * 0.07f, 0f, 0f),
                RotationDegrees = new Vector3(0f, 0f, 90f),
            });
            spinner.AddChild(new MeshInstance3D
            {
                Mesh = new TorusMesh { InnerRadius = 0.205f, OuterRadius = 0.228f, Material = alloy, RingSegments = 32, Rings = 8 },
                Position = new Vector3(side * 0.12f, 0f, 0f),
                RotationDegrees = new Vector3(0f, 0f, 90f),
            });
            spinner.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.055f, BottomRadius = 0.055f, Height = 0.04f, Material = alloy, RadialSegments = 16 },
                Position = new Vector3(side * 0.115f, 0f, 0f),
                RotationDegrees = new Vector3(0f, 0f, 90f),
            });
            for (var s = 0; s < 5; s++)
            {
                var angle = s * Mathf.Tau / 5f;
                spinner.AddChild(new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(0.025f, 0.20f, 0.045f), Material = alloy },
                    Position = new Vector3(side * 0.11f, 0.11f * Mathf.Cos(angle), 0.11f * Mathf.Sin(angle)),
                    Rotation = new Vector3(angle, 0f, 0f),
                });
            }
            _wheelPivots[i] = pivot;
            _wheelSpinners[i] = spinner;
        }
    }

    /// <summary>Tyre as a lathe around the x axis: flat tread, rounded shoulders, open bore for the rim.</summary>
    private static ArrayMesh Tyre(float radius, Material material)
    {
        var profile = new Vector2[]
        {
            new(0.225f, -0.13f), new(0.30f, -0.135f), new(radius - 0.006f, -0.11f), new(radius, 0f),
            new(radius - 0.006f, 0.11f), new(0.30f, 0.135f), new(0.225f, 0.13f),
        };
        const int segments = 28;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
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
    }

    private static MeshInstance3D Box(Node3D parent, Vector3 size, Vector3 position, Material material)
    {
        var box = new MeshInstance3D { Mesh = new BoxMesh { Size = size, Material = material }, Position = position };
        parent.AddChild(box);
        return box;
    }
}

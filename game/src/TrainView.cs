using Godot;
using GrudgeLap.Physics.Track;
using SN = System.Numerics;

namespace GrudgeLap.Game;

/// <summary>
/// The train of the level crossing: a dark red locomotive and silver passenger coaches with a red band, riding the
/// simulation's train along its railway (<see cref="BuildRailway"/>; the 1989 game had a long pale train with a
/// red stripe cross on no rails).
/// </summary>
internal sealed partial class TrainView : Node3D
{
    private readonly List<Node3D> _cars = new();
    private Train _train = null!;
    private TerrainField _terrain = null!;

    /// <summary>Height of the rails' top over the ground: the wheels stand on it.</summary>
    private const float RailTop = 0.14f;
    private const float Gauge = 1.435f;

    /// <summary>The locomotive's node, where the train's sounds are placed.</summary>
    public Node3D Locomotive => _cars[0];

    public static TrainView Create(Train train, TerrainField terrain)
    {
        var view = new TrainView { Name = "Train", _train = train, _terrain = terrain };
        var body = WorldBuilder.PropMaterial("metal", new Color(0.55f, 0.12f, 0.10f), 0.5f);
        var coach = WorldBuilder.PropMaterial("metal", new Color(0.84f, 0.85f, 0.87f), 0.5f);
        var band = new StandardMaterial3D { AlbedoColor = new Color(0.70f, 0.08f, 0.07f), Roughness = 0.5f };
        var windows = new StandardMaterial3D { AlbedoColor = new Color(0.10f, 0.12f, 0.15f), Roughness = 0.2f, Metallic = 0.6f };
        var roof = new StandardMaterial3D { AlbedoColor = new Color(0.42f, 0.42f, 0.44f), Roughness = 0.7f };
        var dark = new StandardMaterial3D { AlbedoColor = new Color(0.08f, 0.08f, 0.09f), Roughness = 0.8f, Metallic = 0.3f };
        for (var i = 0; i < train.Cars.Length; i++)
        {
            var car = new Node3D { Name = i == 0 ? "Locomotive" : $"Wagon{i}" };
            if (i == 0)
            {
                // Boiler, cab, chimney, cowcatcher.
                Add(car, new CylinderMesh { TopRadius = 1.3f, BottomRadius = 1.3f, Height = 8.5f, Material = body }, new Vector3(0f, 2.4f, 1.5f), new Vector3(Mathf.Pi / 2f, 0f, 0f));
                Add(car, new BoxMesh { Size = new Vector3(3.0f, 3.2f, 3.6f), Material = body }, new Vector3(0f, 2.6f, -4.6f));
                Add(car, new BoxMesh { Size = new Vector3(3.2f, 0.3f, 4.2f), Material = dark }, new Vector3(0f, 4.3f, -4.6f));
                Add(car, new CylinderMesh { TopRadius = 0.45f, BottomRadius = 0.35f, Height = 1.6f, Material = dark }, new Vector3(0f, 4.3f, 4.2f));
                Add(car, new PrismMesh { Size = new Vector3(3.2f, 1.4f, 1.6f), Material = dark }, new Vector3(0f, 0.9f, 6.6f), new Vector3(Mathf.Pi / 2f, 0f, 0f));
                Add(car, new BoxMesh { Size = new Vector3(3.0f, 1.2f, 13.5f), Material = dark }, new Vector3(0f, 1.1f, 0f));
            }
            else
            {
                // A passenger coach: silver sides, the red band under a row of windows, a grey roof, the dark underframe.
                Add(car, new BoxMesh { Size = new Vector3(3.0f, 2.8f, 13.5f), Material = coach }, new Vector3(0f, 2.6f, 0f));
                Add(car, new BoxMesh { Size = new Vector3(3.04f, 0.35f, 13.3f), Material = band }, new Vector3(0f, 2.05f, 0f));
                Add(car, new BoxMesh { Size = new Vector3(3.04f, 0.6f, 12.6f), Material = windows }, new Vector3(0f, 2.95f, 0f));
                Add(car, new BoxMesh { Size = new Vector3(2.9f, 0.25f, 13.4f), Material = roof }, new Vector3(0f, 4.1f, 0f));
                Add(car, new BoxMesh { Size = new Vector3(3.0f, 0.9f, 13.5f), Material = dark }, new Vector3(0f, 0.95f, 0f));
            }
            foreach (var z in new[] { -5f, -3.4f, 3.4f, 5f })
            {
                foreach (var x in new[] { -1.35f, 1.35f })
                {
                    Add(car, new CylinderMesh { TopRadius = 0.55f, BottomRadius = 0.55f, Height = 0.3f, Material = dark, RadialSegments = 14 }, new Vector3(x, 0.55f, z), new Vector3(0f, 0f, Mathf.Pi / 2f));
                }
            }
            view.AddChild(car);
            view._cars.Add(car);
        }
        view.Sync();
        return view;
    }

    /// <summary>
    /// The railway along the train's line: a gravel bed, sleepers every 0.65 m and two rails at standard gauge, laid
    /// low on the ground (the rails' top 14 cm over it, so the road's level crossing needs no ramp); where the line
    /// crosses a road the bed and the sleepers stop and the rails lie in the asphalt, their top flush with it. Without
    /// it, as in 1989, the train looked like a lorry driving across the fields and the road.
    /// </summary>
    public static Node3D BuildRailway(Train train, TrackGeometry track)
    {
        var root = new Node3D { Name = "Railway" };
        var terrain = track.Terrain;
        var direction = train.Direction;
        var right = new SN.Vector3(-direction.Z, 0f, direction.X);
        const float step = 2f;
        var count = (int)MathF.Ceiling((train.LineTo - train.LineFrom) / step) + 1;
        var points = new SN.Vector3[count];
        var paved = new bool[count];
        for (var i = 0; i < count; i++)
        {
            var p = train.Crossing + direction * (train.LineFrom + i * step);
            paved[i] = OnRoad(track, p);
            points[i] = new SN.Vector3(p.X, terrain.Height(p.X, p.Z), p.Z);
        }

        // The bed: a strip of gravel a little proud of the ground, 3 m wide, its edges down in the grass.
        var bed = new MeshBuilder();
        for (var i = 0; i < count - 1; i++)
        {
            if (paved[i] || paved[i + 1]) continue;
            var a = points[i];
            var b = points[i + 1];
            void Strip(float x0, float y0, float x1, float y1)
            {
                var a0 = Convert.ToGodot(a + right * x0 + SN.Vector3.UnitY * y0);
                var a1 = Convert.ToGodot(a + right * x1 + SN.Vector3.UnitY * y1);
                var b0 = Convert.ToGodot(b + right * x0 + SN.Vector3.UnitY * y0);
                var b1 = Convert.ToGodot(b + right * x1 + SN.Vector3.UnitY * y1);
                var normal = (b0 - a0).Cross(a1 - a0).Normalized();
                if (normal.Y < 0f) normal = -normal;
                foreach (var v in new[] { a0, b0, a1, a1, b0, b1 })
                {
                    bed.SetNormal(normal);
                    bed.AddVertex(v);
                }
            }
            Strip(-1.6f, -0.08f, -1.15f, 0.04f);
            Strip(-1.15f, 0.04f, 1.15f, 0.04f);
            Strip(1.15f, 0.04f, 1.6f, -0.08f);
        }
        var gravel = WorldBuilder.PropMaterial("dirt", new Color(0.62f, 0.60f, 0.57f), 1.5f);
        gravel.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        root.AddChild(new MeshInstance3D { Mesh = bed.Commit(), MaterialOverride = gravel, Name = "Ballast" });

        // Sleepers on the bed, merged into one mesh: as a multimesh the world export took them for grass tufts.
        var sleepers = new MeshBuilder();
        var across = Convert.ToGodot(right).Normalized();
        var ahead = Convert.ToGodot(direction).Normalized();
        for (var s = 0f; s < (count - 1) * step; s += 0.65f)
        {
            var i = (int)(s / step);
            if (paved[i] || paved[Math.Min(i + 1, count - 1)]) continue;
            var p = Convert.ToGodot(SN.Vector3.Lerp(points[i], points[Math.Min(i + 1, count - 1)], s / step - i));
            Block(sleepers, p + Vector3.Up * 0.06f, across * 1.25f, Vector3.Up * 0.035f, ahead * 0.12f);
        }
        root.AddChild(new MeshInstance3D { Mesh = sleepers.Commit(), MaterialOverride = WorldBuilder.PropMaterial("bark", new Color(0.42f, 0.36f, 0.30f), 1f), Name = "Sleepers" });

        // The rails: their top 14 cm over the ground, or flush with the asphalt across a road.
        var rails = new MeshBuilder();
        foreach (var side in new[] { -0.5f * Gauge, 0.5f * Gauge })
        {
            for (var i = 0; i < count - 1; i++)
            {
                var topA = paved[i] ? 0.01f : RailTop;
                var topB = paved[i + 1] ? 0.01f : RailTop;
                var a = points[i] + right * side;
                var b = points[i + 1] + right * side;
                foreach (var (x0, x1, top) in new[] { (-0.036f, 0.036f, true), (-0.036f, -0.036f, false), (0.036f, 0.036f, false) })
                {
                    var a0 = Convert.ToGodot(a + right * x0 + SN.Vector3.UnitY * (top ? topA : topA - 0.12f));
                    var a1 = Convert.ToGodot(a + right * x1 + SN.Vector3.UnitY * topA);
                    var b0 = Convert.ToGodot(b + right * x0 + SN.Vector3.UnitY * (top ? topB : topB - 0.12f));
                    var b1 = Convert.ToGodot(b + right * x1 + SN.Vector3.UnitY * topB);
                    var normal = top ? Vector3.Up : Convert.ToGodot(right * MathF.Sign(x0)).Normalized();
                    foreach (var v in new[] { a0, b0, a1, a1, b0, b1 })
                    {
                        rails.SetNormal(normal);
                        rails.AddVertex(v);
                    }
                }
            }
        }
        var steel = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.53f, 0.50f), Metallic = 0.9f, Roughness = 0.35f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        root.AddChild(new MeshInstance3D { Mesh = rails.Commit(), MaterialOverride = steel, Name = "Rails" });
        return root;
    }

    /// <summary>A box from its centre and three half-extent vectors (square to each other), its faces wound outwards.</summary>
    private static void Block(MeshBuilder st, Vector3 centre, Vector3 x, Vector3 y, Vector3 z)
    {
        foreach (var (normal, u, v) in new[] { (x, y, z), (-x, z, y), (y, z, x), (-y, x, z), (z, x, y), (-z, y, x) })
        {
            var c = centre + normal;
            var n = normal.Normalized();
            Vector3[] corners = [c - u - v, c + u - v, c + u + v, c - u + v];
            foreach (var k in new[] { 0, 1, 2, 0, 2, 3 })
            {
                st.SetNormal(n);
                st.AddVertex(corners[k]);
            }
        }
    }

    /// <summary>True when the point is on a road or its shoulders (within 1.5 m of the edge of a frame beside it).</summary>
    private static bool OnRoad(TrackGeometry track, SN.Vector3 point)
    {
        foreach (var path in track.Paths)
        {
            foreach (var f in path.Frames)
            {
                var d = point - f.Origin;
                d.Y = 0f;
                if (d.LengthSquared() > 400f) continue;
                var tangent = SN.Vector3.Normalize(f.Tangent with { Y = 0f });
                if (MathF.Abs(SN.Vector3.Dot(d, tangent)) > 1.2f) continue;
                var lateral = SN.Vector3.Normalize(f.Lateral with { Y = 0f });
                if (MathF.Abs(SN.Vector3.Dot(d, lateral)) < f.HalfWidth + 1.5f) return true;
            }
        }
        return false;
    }

    private static void Add(Node3D parent, Mesh mesh, Vector3 position, Vector3? rotation = null)
    {
        parent.AddChild(new MeshInstance3D { Mesh = mesh, Position = position, Rotation = rotation ?? Vector3.Zero });
    }

    /// <summary>Puts every car at the simulation's position, on the ground, facing along the line of travel.</summary>
    public void Sync()
    {
        Visible = _train.Running;
        if (!_train.Running) return;
        var forward = _train.Direction;
        var yaw = -MathF.Atan2(forward.X, forward.Z);
        for (var i = 0; i < _cars.Count; i++)
        {
            var c = _train.Cars[i];
            var y = _terrain.Height(c.X, c.Z) + RailTop;
            _cars[i].Position = Convert.ToGodot(new SN.Vector3(c.X, y, c.Z));
            _cars[i].Rotation = new Vector3(0f, yaw, 0f);
        }
    }
}

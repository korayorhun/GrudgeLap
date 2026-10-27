using Godot;
using GrudgeLap.Physics.Track;
using SN = System.Numerics;

namespace GrudgeLap.Game;

/// <summary>
/// The train of the level crossing: a dark red locomotive and three wagons that ride the simulation's
/// train on the terrain, on no rails (as in 1989).
/// </summary>
internal sealed partial class TrainView : Node3D
{
    private readonly List<Node3D> _cars = new();
    private Train _train = null!;
    private TerrainField _terrain = null!;

    /// <summary>The locomotive's node, where the train's sounds are placed.</summary>
    public Node3D Locomotive => _cars[0];

    public static TrainView Create(Train train, TerrainField terrain)
    {
        var view = new TrainView { Name = "Train", _train = train, _terrain = terrain };
        var body = WorldBuilder.PropMaterial("metal", new Color(0.55f, 0.12f, 0.10f), 0.5f);
        var wagon = WorldBuilder.PropMaterial("metal", new Color(0.45f, 0.42f, 0.40f), 0.5f);
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
                Add(car, new BoxMesh { Size = new Vector3(3.0f, 2.8f, 13.5f), Material = wagon }, new Vector3(0f, 2.6f, 0f));
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
            var y = _terrain.Height(c.X, c.Z);
            _cars[i].Position = Convert.ToGodot(new SN.Vector3(c.X, y, c.Z));
            _cars[i].Rotation = new Vector3(0f, yaw, 0f);
        }
    }
}

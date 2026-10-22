using Godot;
using GrudgeLap.Physics.Track;

namespace GrudgeLap.Game;

/// <summary>Primitive-built roadside props: enough to read the scene until proper models arrive.</summary>
internal static class PropBuilder
{
    public static Node3D Build(TrackProp prop)
    {
        var node = new Node3D { Name = $"{prop.Kind}-{prop.Label}" };
        node.Position = Convert.ToGodot(prop.Position);
        node.RotationDegrees = new Vector3(0f, -prop.YawDeg, 0f);

        switch (prop.Kind)
        {
            case PropKind.Barn:
                Box(node, prop.Size, new Color(0.62f, 0.14f, 0.10f), y: prop.Size.Y * 0.5f);
                Roof(node, prop.Size, new Color(0.35f, 0.33f, 0.30f));
                break;
            case PropKind.House:
                Box(node, prop.Size, new Color(0.85f, 0.78f, 0.62f), y: prop.Size.Y * 0.5f);
                Roof(node, prop.Size, new Color(0.45f, 0.22f, 0.15f));
                break;
            case PropKind.Shop:
                Box(node, prop.Size, new Color(0.80f, 0.74f, 0.60f), y: prop.Size.Y * 0.5f);
                Box(node, new System.Numerics.Vector3(prop.Size.X + 2f, 0.4f, prop.Size.Z + 2f), new Color(0.3f, 0.3f, 0.32f), y: prop.Size.Y + 0.2f);
                break;
            case PropKind.Hut:
                Box(node, prop.Size, new Color(0.36f, 0.42f, 0.28f), y: prop.Size.Y * 0.5f);
                Label(node, "THE HUT", new Vector3(0f, prop.Size.Y + 0.6f, prop.Size.Z * 0.5f + 0.05f), 48);
                break;
            case PropKind.Tree:
                Cylinder(node, 0.35f, 3.2f, new Color(0.38f, 0.26f, 0.14f), y: 1.6f);
                Sphere(node, prop.Size.X * 0.55f, new Color(0.14f, 0.38f, 0.12f), y: 3.2f + prop.Size.X * 0.4f);
                break;
            case PropKind.Cow:
                Box(node, new System.Numerics.Vector3(prop.Size.X, 0.8f, prop.Size.Z), new Color(0.95f, 0.95f, 0.92f), y: 1.0f);
                Box(node, new System.Numerics.Vector3(0.7f, 0.6f, 0.6f), new Color(0.2f, 0.2f, 0.2f), y: 1.25f, z: prop.Size.X * 0.5f + 0.2f);
                foreach (var (x, z) in new[] { (-0.5f, 0.5f), (0.5f, 0.5f), (-0.5f, -0.5f), (0.5f, -0.5f) })
                {
                    Box(node, new System.Numerics.Vector3(0.2f, 0.6f, 0.2f), new Color(0.3f, 0.3f, 0.3f), y: 0.3f, x: x * prop.Size.Z, z: z * prop.Size.X * 0.8f);
                }
                break;
            case PropKind.Sign:
                Cylinder(node, 0.06f, 2.2f, new Color(0.5f, 0.5f, 0.5f), y: 1.1f);
                Box(node, new System.Numerics.Vector3(1.1f, 1.1f, 0.06f), prop.Label == "DIP" || prop.Label == "HILL" ? new Color(0.95f, 0.78f, 0.1f) : new Color(0.95f, 0.95f, 0.95f), y: 2.7f);
                Label(node, prop.Label, new Vector3(0f, 2.7f, 0.05f), 40, new Color(0.05f, 0.05f, 0.05f));
                break;
            case PropKind.Signpost:
                var span = MathF.Max(prop.Size.X, 13f);
                Cylinder(node, 0.18f, 5.5f, new Color(0.25f, 0.5f, 0.25f), y: 2.75f, x: -span * 0.5f);
                Cylinder(node, 0.18f, 5.5f, new Color(0.25f, 0.5f, 0.25f), y: 2.75f, x: span * 0.5f);
                Box(node, new System.Numerics.Vector3(span, 1.1f, 0.2f), new Color(0.25f, 0.5f, 0.25f), y: 5.2f);
                // On the crest of the hill: speed carries straight on, stunt drops off to the right.
                Label(node, prop.Label == "SPEED/STUNT" ? "SPEED  ^     |     STUNT  >" : prop.Label, new Vector3(0f, 5.2f, 0.12f), 64);
                break;
            case PropKind.Tower:
                Cylinder(node, 1.2f, prop.Size.Y, new Color(0.7f, 0.7f, 0.72f), y: prop.Size.Y * 0.5f);
                break;
            case PropKind.Barrier:
                Box(node, prop.Size, new Color(0.95f, 0.85f, 0.2f), y: prop.Size.Y * 0.5f);
                break;
            case PropKind.Flowers:
                for (var i = 0; i < 14; i++)
                {
                    var angle = i * 2.4f;
                    var r = 0.6f + (i % 5) * 0.7f;
                    Box(node, new System.Numerics.Vector3(0.25f, 0.25f, 0.25f), i % 2 == 0 ? new Color(0.9f, 0.3f, 0.5f) : new Color(0.95f, 0.85f, 0.2f),
                        y: 0.35f, x: MathF.Cos(angle) * r, z: MathF.Sin(angle) * r);
                }
                break;
            default:
                Box(node, prop.Size, new Color(0.6f, 0.6f, 0.6f), y: prop.Size.Y * 0.5f);
                break;
        }
        return node;
    }

    private static void Box(Node3D parent, System.Numerics.Vector3 size, Color colour, float y, float x = 0f, float z = 0f)
    {
        var mi = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(size.X, size.Y, size.Z), Material = Mat(colour) },
            Position = new Vector3(x, y, z),
        };
        parent.AddChild(mi);
    }

    private static void Roof(Node3D parent, System.Numerics.Vector3 size, Color colour)
    {
        var mi = new MeshInstance3D
        {
            Mesh = new PrismMesh { Size = new Vector3(size.X + 0.6f, size.Y * 0.45f, size.Z + 0.6f), Material = Mat(colour) },
            Position = new Vector3(0f, size.Y + size.Y * 0.225f, 0f),
        };
        parent.AddChild(mi);
    }

    private static void Cylinder(Node3D parent, float radius, float height, Color colour, float y, float x = 0f)
    {
        var mi = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = height, Material = Mat(colour) },
            Position = new Vector3(x, y, 0f),
        };
        parent.AddChild(mi);
    }

    private static void Sphere(Node3D parent, float radius, Color colour, float y)
    {
        var mi = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = radius, Height = radius * 2f, Material = Mat(colour) },
            Position = new Vector3(0f, y, 0f),
        };
        parent.AddChild(mi);
    }

    private static void Label(Node3D parent, string text, Vector3 position, int size, Color? colour = null)
    {
        var label = new Label3D
        {
            Text = text,
            FontSize = size,
            PixelSize = 0.01f,
            Position = position,
            Modulate = colour ?? new Color(1f, 1f, 1f),
            OutlineSize = 6,
            DoubleSided = true,
        };
        parent.AddChild(label);
    }

    private static StandardMaterial3D Mat(Color colour) => new() { AlbedoColor = colour, Roughness = 0.8f };
}

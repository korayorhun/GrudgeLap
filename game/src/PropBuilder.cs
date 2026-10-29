using Godot;
using GrudgeLap.Physics;
using GrudgeLap.Physics.Track;
using SN = System.Numerics;

namespace GrudgeLap.Game;

/// <summary>
/// Roadside props built from primitives and the CC0 photo materials: the red barn with its "33" door at
/// the back, wood-sided houses, the shop with its canopy and pumps, the camouflaged rubble hut, cows with
/// patches, yellow and white road signs, arrow signs, the level-crossing crossbuck, the green start gantry,
/// sawhorse barriers, the skid-pad column and flower beds. Trees and fields are built by
/// <see cref="FoliageBuilder"/>.
/// </summary>
internal static class PropBuilder
{
    private static readonly Random Rng = new(5);

    public static Node3D Build(TrackProp prop, TerrainField terrain)
    {
        if (prop.Kind == PropKind.Tree) return FoliageBuilder.BuildTree(prop, Rng).With(prop);
        if (prop.Kind == PropKind.Field) return FoliageBuilder.BuildField(prop, terrain);

        var node = new Node3D { Name = $"{prop.Kind}-{prop.Label}" };
        node.Position = Convert.ToGodot(prop.Position);
        node.RotationDegrees = new Vector3(0f, -prop.YawDeg, 0f);
        var size = prop.Size;

        switch (prop.Kind)
        {
            case PropKind.Barn:
                Box(node, size, WorldBuilder.PropMaterial("siding", new Color(0.70f, 0.14f, 0.10f), 0.45f), y: size.Y * 0.5f);
                Roof(node, size, WorldBuilder.PropMaterial("roof", new Color(0.55f, 0.55f, 0.58f), 0.6f));
                Box(node, new SN.Vector3(size.X * 0.35f, size.Y * 0.62f, 0.15f), Plain(new Color(0.16f, 0.45f, 0.18f)), y: size.Y * 0.31f, z: -size.Z * 0.5f - 0.05f);
                Label(node, prop.Label == "33" ? "33" : "", new Vector3(0f, size.Y * 0.4f, -size.Z * 0.5f - 0.15f), 96, new Color(0.95f, 0.95f, 0.9f), flip: true);
                // Doors at the front and a hay-loft window.
                Box(node, new SN.Vector3(size.X * 0.3f, size.Y * 0.55f, 0.12f), Plain(new Color(0.35f, 0.22f, 0.14f)), y: size.Y * 0.275f, z: size.Z * 0.5f + 0.04f);
                Box(node, new SN.Vector3(1.4f, 1.1f, 0.1f), Plain(new Color(0.08f, 0.08f, 0.1f)), y: size.Y * 0.82f, z: size.Z * 0.5f + 0.04f);
                break;

            case PropKind.House:
                var wall = prop.Label == "orange" ? new Color(0.92f, 0.56f, 0.22f) : new Color(0.96f, 0.95f, 0.88f);
                Box(node, size, WorldBuilder.PropMaterial("siding", wall, 0.5f), y: size.Y * 0.5f);
                Roof(node, size, WorldBuilder.PropMaterial("roof", new Color(0.5f, 0.28f, 0.2f), 0.6f));
                Box(node, new SN.Vector3(1.1f, 2.2f, 0.1f), Plain(new Color(0.3f, 0.18f, 0.1f)), y: 1.1f, z: size.Z * 0.5f + 0.04f);
                foreach (var x in new[] { -size.X * 0.3f, size.X * 0.3f })
                {
                    Box(node, new SN.Vector3(1.3f, 1.3f, 0.1f), Glass(), y: size.Y * 0.55f, x: x, z: size.Z * 0.5f + 0.04f);
                    Box(node, new SN.Vector3(1.3f, 1.3f, 0.1f), Glass(), y: size.Y * 0.55f, x: x, z: -size.Z * 0.5f - 0.04f);
                }
                // Chimney.
                Box(node, new SN.Vector3(0.9f, 2.2f, 0.9f), WorldBuilder.PropMaterial("bricks", new Color(0.8f, 0.7f, 0.65f), 1f), y: size.Y + size.Y * 0.35f, x: size.X * 0.3f);
                break;

            case PropKind.Shop:
                Box(node, size, WorldBuilder.PropMaterial("concrete", new Color(0.85f, 0.83f, 0.78f), 0.5f), y: size.Y * 0.5f);
                Box(node, new SN.Vector3(size.X + 1f, 0.5f, size.Z + 1f), WorldBuilder.PropMaterial("metal", new Color(0.4f, 0.4f, 0.42f), 0.5f), y: size.Y + 0.25f);
                // Big window along the front, the canopy over the pumps, the GAS sign on a pole.
                Box(node, new SN.Vector3(size.X * 0.7f, size.Y * 0.45f, 0.1f), Glass(), y: size.Y * 0.5f, z: size.Z * 0.5f + 0.04f);
                Box(node, new SN.Vector3(size.X * 0.8f, 0.4f, 7f), WorldBuilder.PropMaterial("metal", new Color(0.85f, 0.85f, 0.88f), 0.5f), y: size.Y - 0.2f, z: size.Z * 0.5f + 4.5f);
                foreach (var x in new[] { -size.X * 0.32f, size.X * 0.32f })
                {
                    Cylinder(node, 0.18f, size.Y - 0.4f, Plain(new Color(0.75f, 0.75f, 0.78f)), y: (size.Y - 0.4f) * 0.5f, x: x, z: size.Z * 0.5f + 7.5f);
                }
                Cylinder(node, 0.14f, 7f, Plain(new Color(0.5f, 0.5f, 0.52f)), y: 3.5f, x: size.X * 0.5f + 1.5f, z: size.Z * 0.5f + 2f);
                Box(node, new SN.Vector3(2.6f, 1.2f, 0.15f), Plain(new Color(0.92f, 0.12f, 0.12f)), y: 7.2f, x: size.X * 0.5f + 1.5f, z: size.Z * 0.5f + 2f);
                Label(node, "GAS", new Vector3(size.X * 0.5f + 1.5f, 7.2f, size.Z * 0.5f + 2.1f), 72, new Color(1f, 1f, 1f));
                Label(node, "GAS", new Vector3(size.X * 0.5f + 1.5f, 7.2f, size.Z * 0.5f + 1.9f), 72, new Color(1f, 1f, 1f), flip: true);
                break;

            case PropKind.Pump:
                Box(node, size, Plain(new Color(0.85f, 0.15f, 0.12f)), y: size.Y * 0.5f);
                Box(node, new SN.Vector3(size.X * 0.8f, size.Y * 0.3f, 0.1f), Plain(new Color(0.1f, 0.1f, 0.12f)), y: size.Y * 0.65f, z: size.Z * 0.5f + 0.04f);
                break;

            case PropKind.Hut:
                // Rubble walls in camouflage patches; the sign reads from the back only, as in the original.
                Box(node, size, WorldBuilder.PropMaterial("bricks", new Color(0.55f, 0.6f, 0.4f), 0.8f), y: size.Y * 0.5f);
                foreach (var (dx, dy, dz, sx, sy) in new[] { (-0.3f, 0.3f, 1f, 0.35f, 0.3f), (0.25f, 0.6f, 1f, 0.3f, 0.25f), (0.1f, 0.25f, -1f, 0.4f, 0.3f) })
                {
                    Box(node, new SN.Vector3(size.X * sx, size.Y * sy, 0.06f), Plain(new Color(0.28f, 0.22f, 0.14f)), y: size.Y * dy, x: size.X * dx, z: dz * (size.Z * 0.5f + 0.03f));
                }
                Roof(node, size, Plain(new Color(0.22f, 0.3f, 0.18f)));
                Label(node, "THE HUT", new Vector3(0f, size.Y + 0.6f, -size.Z * 0.5f - 0.1f), 48, flip: true);
                break;

            case PropKind.Cow:
                var hide = Plain(new Color(0.95f, 0.95f, 0.92f));
                var patch = Plain(new Color(0.12f, 0.1f, 0.1f));
                Box(node, new SN.Vector3(size.X, 0.9f, size.Z), hide, y: 1.05f);
                Box(node, new SN.Vector3(size.X * 0.35f, 0.5f, size.Z + 0.04f), patch, y: 1.2f, x: -size.X * 0.2f);
                Box(node, new SN.Vector3(size.X * 0.25f, 0.6f, size.Z + 0.04f), patch, y: 1.0f, x: size.X * 0.28f);
                Box(node, new SN.Vector3(0.7f, 0.6f, 0.6f), hide, y: 1.3f, x: size.X * 0.5f + 0.25f);
                Box(node, new SN.Vector3(0.3f, 0.3f, 0.62f), patch, y: 1.2f, x: size.X * 0.5f + 0.5f);
                foreach (var (x, z) in new[] { (-0.55f, 0.55f), (0.55f, 0.55f), (-0.55f, -0.55f), (0.55f, -0.55f) })
                {
                    Box(node, new SN.Vector3(0.2f, 0.65f, 0.2f), Plain(new Color(0.3f, 0.28f, 0.26f)), y: 0.32f, x: x * size.X * 0.8f, z: z * size.Z * 0.7f);
                }
                break;

            case PropKind.Sign:
            {
                // The original's signs: the speed on a yellow square, with a yellow diamond and its arrow above it where
                // the road bends ("90>" turns right), yellow diamonds for DIP and the chevrons, white squares for the rest.
                var label = prop.Label;
                var bend = label.Length > 1 && label[^1] is '>' or '<' && !label.StartsWith('-') && !label.StartsWith('<');
                var number = bend ? label[..^1] : label;
                var signYellow = Plain(new Color(0.95f, 0.78f, 0.1f));
                var pole = Plain(new Color(0.5f, 0.5f, 0.5f));
                if (label is "DIP" or "HILL" or "->" or "<-")
                {
                    Cylinder(node, 0.06f, 2.2f, pole, y: 1.1f);
                    var diamond = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.95f, 0.95f, 0.06f), Material = signYellow }, Position = new Vector3(0f, 2.7f, 0f), Rotation = new Vector3(0f, 0f, Mathf.Pi / 4f) };
                    node.AddChild(diamond);
                    if (label is "->" or "<-") Arrow(node, label == "->", new Vector3(0f, 2.7f, 0.05f));
                    else Label(node, label, new Vector3(0f, 2.7f, 0.05f), 40, new Color(0.05f, 0.05f, 0.05f));
                }
                else if (int.TryParse(number, out _))
                {
                    Cylinder(node, 0.06f, bend ? 3.4f : 2.2f, pole, y: bend ? 1.7f : 1.1f);
                    Box(node, new SN.Vector3(1.0f, 1.0f, 0.06f), signYellow, y: 2.45f);
                    Label(node, number, new Vector3(0f, 2.45f, 0.05f), 40, new Color(0.05f, 0.05f, 0.05f));
                    if (bend)
                    {
                        var diamond = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.8f, 0.8f, 0.06f), Material = signYellow }, Position = new Vector3(0f, 3.55f, 0f), Rotation = new Vector3(0f, 0f, Mathf.Pi / 4f) };
                        node.AddChild(diamond);
                        Arrow(node, label[^1] == '>', new Vector3(0f, 3.55f, 0.05f));
                    }
                }
                else
                {
                    Cylinder(node, 0.06f, 2.2f, pole, y: 1.1f);
                    Box(node, new SN.Vector3(1.1f, 1.1f, 0.06f), Plain(new Color(0.95f, 0.95f, 0.95f)), y: 2.7f);
                    Label(node, label, new Vector3(0f, 2.7f, 0.05f), label.Length > 3 ? 28 : 40, new Color(0.05f, 0.05f, 0.05f));
                }
                break;
            }

            case PropKind.Crossbuck:
                Cylinder(node, 0.08f, 3.2f, Plain(new Color(0.9f, 0.9f, 0.9f)), y: 1.6f);
                foreach (var roll in new[] { 0.6f, -0.6f })
                {
                    var board = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.9f, 0.26f, 0.05f), Material = Plain(new Color(0.95f, 0.95f, 0.95f)) }, Position = new Vector3(0f, 2.9f, 0f), Rotation = new Vector3(0f, 0f, roll) };
                    node.AddChild(board);
                }
                Label(node, "RAIL ROAD", new Vector3(0f, 3.25f, 0.04f), 18, new Color(0.05f, 0.05f, 0.05f));
                Label(node, "CROSSING", new Vector3(0f, 2.55f, 0.04f), 18, new Color(0.05f, 0.05f, 0.05f));
                break;

            case PropKind.Signpost:
                // The gantries of the original: a red checkpoint banner on timber posts, and the green fork sign on
                // the crest with the traffic light hanging under it (speed carries straight on, stunt drops off to the right).
                var span = MathF.Max(size.X, 13f);
                var checkpoint = prop.Label == "CHECKPOINT";
                var post = Plain(checkpoint ? new Color(0.42f, 0.28f, 0.16f) : new Color(0.22f, 0.45f, 0.22f));
                var banner = Plain(checkpoint ? new Color(0.70f, 0.09f, 0.07f) : new Color(0.22f, 0.45f, 0.22f));
                Cylinder(node, 0.18f, 5.5f, post, y: 2.75f, x: -span * 0.5f);
                Cylinder(node, 0.18f, 5.5f, post, y: 2.75f, x: span * 0.5f);
                Box(node, new SN.Vector3(span, 1.1f, 0.2f), banner, y: 5.2f);
                if (checkpoint)
                {
                    Label(node, prop.Label, new Vector3(0f, 5.2f, 0.12f), 64);
                }
                else
                {
                    Label(node, "SPEED TRACK  ^", new Vector3(-span * 0.22f, 5.2f, 0.12f), 44);
                    Label(node, "STUNT TRACK  >", new Vector3(span * 0.22f, 5.2f, 0.12f), 44);
                    Box(node, new SN.Vector3(0.42f, 1.15f, 0.36f), Plain(new Color(0.08f, 0.08f, 0.09f)), y: 4.05f);
                    foreach (var (dy, colour, lit) in new[] { (0.36f, new Color(0.35f, 0.04f, 0.03f), false), (0f, new Color(0.35f, 0.25f, 0.03f), false), (-0.36f, new Color(0.10f, 0.95f, 0.25f), true) })
                    {
                        var lampMaterial = new StandardMaterial3D { AlbedoColor = colour, EmissionEnabled = lit, Emission = colour, EmissionEnergyMultiplier = 1.5f };
                        node.AddChild(new MeshInstance3D
                        {
                            Mesh = new CylinderMesh { TopRadius = 0.13f, BottomRadius = 0.13f, Height = 0.05f, Material = lampMaterial, RadialSegments = 16 },
                            Position = new Vector3(0f, 4.05f + dy, 0.19f),
                            RotationDegrees = new Vector3(90f, 0f, 0f),
                        });
                    }
                }
                break;

            case PropKind.Gate:
                var green = Plain(new Color(0.12f, 0.5f, 0.2f));
                Cylinder(node, 0.22f, size.Y, green, y: size.Y * 0.5f, x: -size.X * 0.5f);
                Cylinder(node, 0.22f, size.Y, green, y: size.Y * 0.5f, x: size.X * 0.5f);
                Box(node, new SN.Vector3(size.X + 0.4f, 1.4f, 0.3f), green, y: size.Y - 0.7f);
                Label(node, prop.Label, new Vector3(0f, size.Y - 0.7f, 0.17f), 80, new Color(1f, 1f, 1f));
                Label(node, prop.Label, new Vector3(0f, size.Y - 0.7f, -0.17f), 80, new Color(1f, 1f, 1f), flip: true);
                break;

            case PropKind.Tower:
                Cylinder(node, size.X * 0.5f, size.Y, WorldBuilder.PropMaterial("concrete", new Color(0.8f, 0.78f, 0.74f), 0.5f), y: size.Y * 0.5f);
                Cylinder(node, size.X * 0.7f, 0.6f, WorldBuilder.PropMaterial("concrete", new Color(0.7f, 0.68f, 0.64f), 0.5f), y: size.Y + 0.3f);
                break;

            case PropKind.Barrier:
                // Sawhorse: two A-frame leg pairs under a striped top board and a plain lower rail.
                var yellow = Plain(new Color(0.95f, 0.78f, 0.12f));
                var black = Plain(new Color(0.08f, 0.08f, 0.08f));
                var wood = Plain(new Color(0.72f, 0.66f, 0.52f));
                var top = MathF.Max(size.Y, 1.0f);
                const int stripes = 8;
                for (var s = 0; s < stripes; s++)
                {
                    var w = size.X / stripes;
                    Box(node, new SN.Vector3(w + 0.01f, 0.3f, 0.06f), s % 2 == 0 ? yellow : black, y: top, x: -size.X * 0.5f + w * (s + 0.5f));
                }
                Box(node, new SN.Vector3(size.X * 0.92f, 0.12f, 0.05f), wood, y: top * 0.45f);
                foreach (var x in new[] { -size.X * 0.42f, size.X * 0.42f })
                {
                    foreach (var z in new[] { -1f, 1f })
                    {
                        var leg = new MeshInstance3D
                        {
                            Mesh = new BoxMesh { Size = new Vector3(0.08f, top + 0.25f, 0.08f), Material = wood },
                            Position = new Vector3(x, (top + 0.15f) * 0.5f, z * 0.22f),
                            Rotation = new Vector3(z * 0.42f, 0f, 0f),
                        };
                        node.AddChild(leg);
                    }
                }
                break;

            case PropKind.Flowers:
                var stem = Plain(new Color(0.2f, 0.45f, 0.15f));
                for (var i = 0; i < 18; i++)
                {
                    var angle = i * 2.4f;
                    var r = 0.6f + (i % 5) * 0.7f;
                    var colour = (i % 4) switch { 0 => new Color(0.9f, 0.25f, 0.45f), 1 => new Color(0.95f, 0.85f, 0.2f), 2 => new Color(0.95f, 0.95f, 0.95f), _ => new Color(0.55f, 0.3f, 0.8f) };
                    Box(node, new SN.Vector3(0.06f, 0.5f, 0.06f), stem, y: 0.25f, x: MathF.Cos(angle) * r, z: MathF.Sin(angle) * r);
                    Box(node, new SN.Vector3(0.28f, 0.14f, 0.28f), Plain(colour), y: 0.55f, x: MathF.Cos(angle) * r, z: MathF.Sin(angle) * r);
                }
                break;

            case PropKind.Pier:
                // A concrete pier under a deck, sunk 0.3 m into the ground so no gap shows on a slope; a tower's footprint is
                // drawn with the tower (WorldBuilder.BuildTowers).
                if (prop.Label != "tower") Box(node, new SN.Vector3(size.X, size.Y + 0.3f, size.Z), WorldBuilder.PropMaterial("concrete", new Color(0.78f, 0.77f, 0.74f), 0.5f), y: (size.Y - 0.3f) * 0.5f);
                break;

            case PropKind.Train:
                Box(node, size, WorldBuilder.PropMaterial("metal", new Color(0.5f, 0.12f, 0.1f), 0.5f), y: size.Y * 0.5f);
                break;

            default:
                Box(node, size, Plain(new Color(0.6f, 0.6f, 0.6f)), y: size.Y * 0.5f);
                break;
        }
        return node;
    }

    private static Node3D With(this Node3D node, TrackProp prop)
    {
        node.Position = Convert.ToGodot(prop.Position);
        node.RotationDegrees = new Vector3(0f, -prop.YawDeg, 0f);
        return node;
    }

    private static void Box(Node3D parent, SN.Vector3 size, Material material, float y, float x = 0f, float z = 0f)
    {
        parent.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(size.X, size.Y, size.Z), Material = material },
            Position = new Vector3(x, y, z),
        });
    }

    private static void Roof(Node3D parent, SN.Vector3 size, Material material)
    {
        parent.AddChild(new MeshInstance3D
        {
            Mesh = new PrismMesh { Size = new Vector3(size.X + 0.8f, size.Y * 0.45f, size.Z + 0.8f), Material = material },
            Position = new Vector3(0f, size.Y + size.Y * 0.225f, 0f),
        });
    }

    private static void Cylinder(Node3D parent, float radius, float height, Material material, float y, float x = 0f, float z = 0f)
    {
        parent.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = height, Material = material },
            Position = new Vector3(x, y, z),
        });
    }

    /// <summary>A flat black arrow on a sign face, pointing right or left.</summary>
    private static void Arrow(Node3D parent, bool right, Vector3 position)
    {
        var st = new MeshBuilder();
        var s = right ? 1f : -1f;
        st.SetNormal(Vector3.Back);
        // Shaft.
        Tri(st, new Vector3(-0.32f * s, -0.08f, 0f), new Vector3(0.08f * s, -0.08f, 0f), new Vector3(0.08f * s, 0.08f, 0f));
        Tri(st, new Vector3(-0.32f * s, -0.08f, 0f), new Vector3(0.08f * s, 0.08f, 0f), new Vector3(-0.32f * s, 0.08f, 0f));
        // Head.
        Tri(st, new Vector3(0.04f * s, -0.26f, 0f), new Vector3(0.36f * s, 0f, 0f), new Vector3(0.04f * s, 0.26f, 0f));
        var mesh = st.Commit();
        parent.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.05f, 0.05f, 0.05f), CullMode = BaseMaterial3D.CullModeEnum.Disabled, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded }, Position = position });
    }

    private static void Tri(MeshBuilder st, Vector3 a, Vector3 b, Vector3 c)
    {
        st.AddVertex(a);
        st.AddVertex(b);
        st.AddVertex(c);
    }

    private static void Label(Node3D parent, string text, Vector3 position, int size, Color? colour = null, bool flip = false)
    {
        if (string.IsNullOrEmpty(text)) return;
        var label = new Label3D
        {
            Text = text,
            FontSize = size,
            PixelSize = 0.01f,
            Position = position,
            Modulate = colour ?? new Color(1f, 1f, 1f),
            OutlineSize = 6,
            DoubleSided = false,
            Rotation = new Vector3(0f, flip ? Mathf.Pi : 0f, 0f),
        };
        parent.AddChild(label);
    }

    private static readonly Dictionary<Color, StandardMaterial3D> Plains = new();

    private static StandardMaterial3D Plain(Color colour)
    {
        if (!Plains.TryGetValue(colour, out var material))
        {
            material = new StandardMaterial3D { AlbedoColor = colour, Roughness = 0.8f };
            Plains[colour] = material;
        }
        return material;
    }

    private static StandardMaterial3D Glass() => Plain(new Color(0.12f, 0.16f, 0.22f));

    public static void ReleaseShared() => Plains.Clear();
}

using Godot;
using GrudgeLap.Physics.Track;

namespace GrudgeLap.Game;

/// <summary>
/// The houses and the barns as they are built: a farmhouse of a storey and a half on a concrete plinth, sided, with
/// white corner boards, a shingled gable roof of real thickness overhanging the walls, framed windows with their
/// glazing bars, sills and shutters, a front porch on posts with its own roof and steps, a brick chimney with its
/// cap, and gutters; and the red barn with its gambrel roof, white trim, the big doors with their white cross
/// braces, the hay-loft door and the cupola on the ridge. Until 0.9.1 both were a box under a prism.
/// </summary>
/// <remarks>
/// Built in the prop's frame: x along the ridge, +z the front (where the door is), y up from the ground, sized to the
/// prop's box (its footprint is what the car knocks into).
/// </remarks>
internal static class BuildingBuilder
{
    private static readonly Color Trim = new(0.93f, 0.92f, 0.88f);

    public static void House(Node3D node, TrackProp prop)
    {
        var size = prop.Size;
        var width = size.X;
        var depth = size.Z;
        var orange = prop.Label == "orange";
        var wallTint = orange ? new Color(0.90f, 0.58f, 0.30f) : new Color(0.95f, 0.94f, 0.88f);
        var siding = WorldBuilder.PropMaterial("siding", wallTint, 0.5f);
        var trim = Plain(Trim, 0.6f);
        var roofing = WorldBuilder.PropMaterial("roof", orange ? new Color(0.36f, 0.22f, 0.17f) : new Color(0.30f, 0.31f, 0.33f), 0.6f);
        var concrete = WorldBuilder.PropMaterial("concrete", new Color(0.68f, 0.67f, 0.64f), 0.5f);
        var shutters = Plain(orange ? new Color(0.22f, 0.30f, 0.20f) : new Color(0.13f, 0.24f, 0.18f), 0.6f);

        // A storey and a half: the walls to the eaves, the roof at 35 degrees over the depth to the ridge.
        const float plinth = 0.55f;
        var roofRise = depth * 0.5f * MathF.Tan(Mathf.DegToRad(35f));
        var eaves = MathF.Max(size.Y - roofRise, 3.6f);
        Box(node, new Vector3(width + 0.25f, plinth, depth + 0.25f), concrete, new Vector3(0f, plinth * 0.5f, 0f));
        Box(node, new Vector3(width, eaves - plinth, depth), siding, new Vector3(0f, (eaves + plinth) * 0.5f, 0f));
        // The gable ends: the attic walls under the roof.
        node.AddChild(new MeshInstance3D
        {
            Mesh = new PrismMesh { Size = new Vector3(depth, roofRise, width), Material = siding },
            Transform = new Transform3D(new Basis(Vector3.Up, Mathf.Pi / 2f), new Vector3(0f, eaves + roofRise * 0.5f, 0f)),
        });
        // Corner boards.
        foreach (var (x, z) in new[] { (1f, 1f), (1f, -1f), (-1f, 1f), (-1f, -1f) })
        {
            Box(node, new Vector3(0.16f, eaves - plinth, 0.16f), trim, new Vector3(x * (width * 0.5f + 0.01f), (eaves + plinth) * 0.5f, z * (depth * 0.5f + 0.01f)));
        }
        GableRoof(node, width, depth, eaves, roofRise, roofing, trim);

        // The front: the door in the middle under the porch, a window either side; the back three windows; a window
        // in each end wall and one in each gable.
        var front = depth * 0.5f;
        Door(node, new Vector3(0f, plinth, front), trim);
        Porch(node, width * 0.62f, front, plinth, trim, concrete, roofing);
        var sill = plinth + 0.85f;
        foreach (var x in new[] { -width * 0.31f, width * 0.31f })
        {
            Window(node, new Vector3(x, sill, front), 0f, 1.1f, 1.45f, trim, shutters);
            Window(node, new Vector3(x, sill, -front), Mathf.Pi, 1.1f, 1.45f, trim, shutters);
        }
        Window(node, new Vector3(0f, sill, -front), Mathf.Pi, 1.1f, 1.45f, trim, shutters);
        foreach (var side in new[] { -1f, 1f })
        {
            var turn = side > 0f ? Mathf.Pi / 2f : -Mathf.Pi / 2f;
            Window(node, new Vector3(side * width * 0.5f, sill, -depth * 0.15f), turn, 1.0f, 1.4f, trim, null);
            Window(node, new Vector3(side * width * 0.5f, eaves + 0.25f, 0f), turn, 0.8f, 1.0f, trim, null);
        }

        // The chimney up the outside of one end wall, through the eaves, with its cap.
        var bricks = WorldBuilder.PropMaterial("bricks", new Color(0.72f, 0.42f, 0.34f), 1f);
        var chimneyX = width * 0.5f + 0.45f;
        var chimneyTop = eaves + roofRise + 0.9f;
        Box(node, new Vector3(0.85f, chimneyTop, 1.0f), bricks, new Vector3(chimneyX, chimneyTop * 0.5f, depth * 0.18f));
        Box(node, new Vector3(1.0f, 0.12f, 1.15f), concrete, new Vector3(chimneyX, chimneyTop + 0.06f, depth * 0.18f));
        Box(node, new Vector3(0.36f, 0.3f, 0.36f), Plain(new Color(0.12f, 0.1f, 0.1f), 0.9f), new Vector3(chimneyX, chimneyTop + 0.2f, depth * 0.18f));
    }

    /// <summary>The 1989 barn, red with white trim, on a concrete plinth: a gambrel roof, the big doors at the front, the hay-loft door over them, a cupola.</summary>
    public static void Barn(Node3D node, TrackProp prop, Action<Node3D, string, Vector3, int, Color?, bool> label)
    {
        var size = prop.Size;
        var width = size.X;
        var depth = size.Z;
        var red = WorldBuilder.PropMaterial("siding", new Color(0.62f, 0.12f, 0.09f), 0.45f);
        var trim = Plain(Trim, 0.6f);
        var roofing = WorldBuilder.PropMaterial("roof", new Color(0.38f, 0.39f, 0.41f), 0.6f);
        var concrete = WorldBuilder.PropMaterial("concrete", new Color(0.66f, 0.65f, 0.62f), 0.5f);
        var dark = Plain(new Color(0.08f, 0.07f, 0.07f), 0.9f);

        // The ridge runs front to back (z) on a barn: the gambrel's two slopes a side are across x.
        const float plinth = 0.4f;
        var eaves = size.Y * 0.5f;
        Box(node, new Vector3(width + 0.2f, plinth, depth + 0.2f), concrete, new Vector3(0f, plinth * 0.5f, 0f));
        Box(node, new Vector3(width, eaves - plinth, depth), red, new Vector3(0f, (eaves + plinth) * 0.5f, 0f));
        // The gambrel: from the eaves steeply up to the knee (60 degrees), then shallowly to the ridge (25 degrees).
        var half = width * 0.5f;
        var kneeIn = half * 0.38f;
        var kneeRise = kneeIn * MathF.Tan(Mathf.DegToRad(60f));
        var topRise = (half - kneeIn) * MathF.Tan(Mathf.DegToRad(25f));
        Vector2[] outline = [new(-half, eaves), new(-half + kneeIn, eaves + kneeRise), new(0f, eaves + kneeRise + topRise), new(half - kneeIn, eaves + kneeRise), new(half, eaves)];
        // The gable walls under it, front and back, and the roof's four slopes over them.
        foreach (var z in new[] { depth * 0.5f, -depth * 0.5f }) GableWall(node, outline, z, red);
        for (var i = 0; i < outline.Length - 1; i++)
        {
            RoofSlope(node, outline[i], outline[i + 1], depth + 0.6f, roofing, trim, i == 0 || i == outline.Length - 2);
        }
        // A cap along the ridge over the top slopes' joint.
        Box(node, new Vector3(0.4f, 0.12f, depth + 0.65f), roofing, new Vector3(0f, eaves + kneeRise + topRise + 0.12f, 0f));
        // Trim: the corners, the fascia along the gambrel's edges at both ends.
        foreach (var (x, z) in new[] { (1f, 1f), (1f, -1f), (-1f, 1f), (-1f, -1f) })
        {
            Box(node, new Vector3(0.2f, eaves - plinth, 0.2f), trim, new Vector3(x * (half + 0.01f), (eaves + plinth) * 0.5f, z * (depth * 0.5f + 0.01f)));
        }
        foreach (var z in new[] { depth * 0.5f + 0.32f, -depth * 0.5f - 0.32f })
        {
            for (var i = 0; i < outline.Length - 1; i++) Rafter(node, outline[i], outline[i + 1], z, trim);
        }

        // The big doors at the front, framed and braced in white; the hay-loft door over them under a little hood.
        var front = depth * 0.5f + 0.06f;
        var doorWidth = width * 0.36f;
        var doorHeight = MathF.Min(eaves * 0.86f, 4.2f);
        BracedDoor(node, new Vector3(0f, plinth, front), doorWidth, doorHeight, Plain(new Color(0.55f, 0.11f, 0.08f), 0.7f), trim);
        var loft = new Vector3(0f, eaves + kneeRise * 0.25f, front);
        Box(node, new Vector3(1.6f, 1.5f, 0.1f), dark, loft + new Vector3(0f, 0.75f, 0f));
        Frame(node, loft + new Vector3(0f, 0.75f, 0.03f), 1.6f, 1.5f, 0.14f, trim);
        Box(node, new Vector3(2.0f, 0.1f, 0.7f), roofing, loft + new Vector3(0f, 1.62f, 0.3f));
        // Small windows down the long sides.
        foreach (var side in new[] { -1f, 1f })
        {
            foreach (var z in new[] { -depth * 0.25f, depth * 0.25f })
            {
                var at = new Vector3(side * (half + 0.03f), eaves * 0.55f, z);
                Box(node, new Vector3(0.06f, 0.8f, 1.0f), dark, at);
                Box(node, new Vector3(0.1f, 0.12f, 1.2f), trim, at + new Vector3(0f, 0.46f, 0f));
                Box(node, new Vector3(0.1f, 0.12f, 1.2f), trim, at - new Vector3(0f, 0.46f, 0f));
            }
        }
        // The cupola astride the ridge: a little louvred box with a pyramid roof.
        var ridge = eaves + kneeRise + topRise;
        Box(node, new Vector3(1.3f, 1.1f, 1.3f), Plain(Trim, 0.6f), new Vector3(0f, ridge + 0.4f, 0f));
        Box(node, new Vector3(1.0f, 0.55f, 1.32f), dark, new Vector3(0f, ridge + 0.55f, 0f));
        Box(node, new Vector3(1.32f, 0.55f, 1.0f), dark, new Vector3(0f, ridge + 0.55f, 0f));
        node.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.02f, BottomRadius = 1.05f, Height = 0.8f, RadialSegments = 4, Rings = 1, Material = roofing },
            Transform = new Transform3D(new Basis(Vector3.Up, Mathf.Pi / 4f), new Vector3(0f, ridge + 1.35f, 0f)),
        });
        // The original's "33" on the back, white on the red.
        label(node, prop.Label == "33" ? "33" : "", new Vector3(0f, eaves * 0.62f, -depth * 0.5f - 0.15f), 96, new Color(0.95f, 0.95f, 0.9f), true);
    }

    /// <summary>
    /// The filling station: a flat-roofed shop of painted block on a plinth, a red band round its top, a shopfront of
    /// glass in aluminium frames; the canopy over the pumps on four square columns, white with a red band and lights
    /// under it; and the price sign on its pole by the road.
    /// </summary>
    public static void Shop(Node3D node, TrackProp prop, Action<Node3D, string, Vector3, int, Color?, bool> label)
    {
        var size = prop.Size;
        var width = size.X;
        var depth = size.Z;
        var height = size.Y;
        var walls = WorldBuilder.PropMaterial("concrete", new Color(0.90f, 0.88f, 0.84f), 0.5f);
        var red = Plain(new Color(0.78f, 0.10f, 0.08f), 0.45f);
        var white = Plain(new Color(0.94f, 0.94f, 0.93f), 0.45f);
        var aluminium = Plain(new Color(0.70f, 0.71f, 0.73f), 0.35f);
        var concrete = WorldBuilder.PropMaterial("concrete", new Color(0.62f, 0.62f, 0.60f), 0.5f);
        const float plinth = 0.25f;
        Box(node, new Vector3(width + 0.2f, plinth, depth + 0.2f), concrete, new Vector3(0f, plinth * 0.5f, 0f));
        Box(node, new Vector3(width, height - plinth, depth), walls, new Vector3(0f, (height + plinth) * 0.5f, 0f));
        // The red band round the top, the parapet's coping over it.
        Box(node, new Vector3(width + 0.08f, 0.7f, depth + 0.08f), red, new Vector3(0f, height - 0.35f, 0f));
        Box(node, new Vector3(width + 0.2f, 0.1f, depth + 0.2f), white, new Vector3(0f, height + 0.05f, 0f));
        // The shopfront: four tall panes with a transom, in aluminium.
        var front = depth * 0.5f;
        var glassWidth = width * 0.72f;
        var glassHeight = height - 1.6f;
        var glassMiddle = new Vector3(-width * 0.08f, plinth + 0.35f + glassHeight * 0.5f, front + 0.02f);
        Box(node, new Vector3(glassWidth, glassHeight, 0.06f), Glass(), glassMiddle);
        Frame(node, glassMiddle + new Vector3(0f, 0f, 0.04f), glassWidth, glassHeight, 0.1f, aluminium);
        for (var i = 1; i < 4; i++)
        {
            Box(node, new Vector3(0.08f, glassHeight, 0.08f), aluminium, glassMiddle + new Vector3(-glassWidth * 0.5f + glassWidth * i / 4f, 0f, 0.04f));
        }
        Box(node, new Vector3(glassWidth, 0.08f, 0.08f), aluminium, glassMiddle + new Vector3(0f, glassHeight * 0.5f - 0.5f, 0.04f));
        // An ice chest by the door and a bench.
        Box(node, new Vector3(1.0f, 0.85f, 0.6f), white, new Vector3(width * 0.36f, plinth + 0.43f, front + 0.4f));
        Box(node, new Vector3(1.6f, 0.08f, 0.4f), Plain(new Color(0.45f, 0.30f, 0.2f), 0.7f), new Vector3(-width * 0.36f, plinth + 0.45f, front + 0.35f));

        // The canopy over the pumps, towards the road.
        const float reach = 7.5f;
        var canopyWidth = width * 0.85f;
        var canopyHeight = MathF.Max(height - 0.3f, 4.3f);
        var canopyCentre = new Vector3(0f, canopyHeight + 0.35f, front + reach * 0.5f + 0.4f);
        Box(node, new Vector3(canopyWidth, 0.7f, reach), white, canopyCentre);
        foreach (var z in new[] { 1f, -1f })
        {
            Box(node, new Vector3(canopyWidth + 0.04f, 0.26f, 0.04f), red, canopyCentre + new Vector3(0f, -0.05f, z * (reach * 0.5f + 0.01f)));
        }
        foreach (var x in new[] { 1f, -1f })
        {
            Box(node, new Vector3(0.04f, 0.26f, reach + 0.04f), red, canopyCentre + new Vector3(x * (canopyWidth * 0.5f + 0.01f), -0.05f, 0f));
        }
        // Lights in the canopy's underside.
        _lamp ??= new StandardMaterial3D { AlbedoColor = new Color(1f, 1f, 0.96f), EmissionEnabled = true, Emission = new Color(1f, 0.98f, 0.92f), EmissionEnergyMultiplier = 1.2f };
        foreach (var x in new[] { -canopyWidth * 0.3f, 0f, canopyWidth * 0.3f })
        {
            foreach (var z in new[] { -reach * 0.25f, reach * 0.25f })
            {
                Box(node, new Vector3(1.0f, 0.03f, 0.5f), _lamp, canopyCentre + new Vector3(x, -0.36f, z));
            }
        }
        foreach (var x in new[] { -canopyWidth * 0.32f, canopyWidth * 0.32f })
        {
            foreach (var z in new[] { front + 2.2f, front + reach - 1.4f })
            {
                Box(node, new Vector3(0.35f, canopyHeight, 0.35f), white, new Vector3(x, canopyHeight * 0.5f, z));
                Box(node, new Vector3(0.45f, 0.25f, 0.45f), concrete, new Vector3(x, 0.125f, z));
            }
        }

        // The price sign by the road, on its pole: the name board and the prices under it.
        var pole = new Vector3(width * 0.5f + 1.5f, 0f, front + reach + 1.5f);
        Box(node, new Vector3(0.3f, 7.4f, 0.3f), aluminium, pole + new Vector3(0f, 3.7f, 0f));
        Box(node, new Vector3(2.8f, 1.3f, 0.3f), red, pole + new Vector3(0f, 7.2f, 0f));
        Box(node, new Vector3(2.4f, 1.3f, 0.26f), white, pole + new Vector3(0f, 5.85f, 0f));
        foreach (var flip in new[] { false, true })
        {
            var z = flip ? -0.17f : 0.17f;
            label(node, prop.Label == "" ? "GAS" : prop.Label, pole + new Vector3(0f, 7.2f, z), 72, new Color(1f, 1f, 1f), flip);
            label(node, "1.09", pole + new Vector3(0f, 6.05f, z), 48, new Color(0.1f, 0.1f, 0.12f), flip);
            label(node, "DIESEL 0.99", pole + new Vector3(0f, 5.6f, z), 26, new Color(0.1f, 0.1f, 0.12f), flip);
        }
    }

    /// <summary>
    /// A petrol pump of the 1980s on its island: a concrete kerb, the cabinet in white over a red base with the dial
    /// window on both faces, the hose looped at its side, and the lit topper.
    /// </summary>
    public static void Pump(Node3D node, TrackProp prop)
    {
        var size = prop.Size;
        var red = Plain(new Color(0.80f, 0.11f, 0.09f), 0.45f);
        var white = Plain(new Color(0.94f, 0.94f, 0.93f), 0.45f);
        var black = Plain(new Color(0.05f, 0.05f, 0.055f), 0.6f);
        var concrete = WorldBuilder.PropMaterial("concrete", new Color(0.66f, 0.66f, 0.64f), 0.5f);
        Box(node, new Vector3(size.X + 0.6f, 0.22f, size.Z + 1.6f), concrete, new Vector3(0f, 0.11f, 0f));
        const float top = 0.22f;
        Box(node, new Vector3(size.X, 0.5f, size.Z), red, new Vector3(0f, top + 0.25f, 0f));
        Box(node, new Vector3(size.X, size.Y - 0.5f, size.Z), white, new Vector3(0f, top + 0.5f + (size.Y - 0.5f) * 0.5f, 0f));
        foreach (var z in new[] { 1f, -1f })
        {
            var face = z * (size.Z * 0.5f + 0.01f);
            Box(node, new Vector3(size.X * 0.75f, 0.45f, 0.02f), black, new Vector3(0f, top + size.Y - 0.45f, face));
            Box(node, new Vector3(size.X * 0.6f, 0.12f, 0.025f), Plain(new Color(0.85f, 0.75f, 0.2f), 0.4f), new Vector3(0f, top + size.Y - 0.38f, face));
        }
        Box(node, new Vector3(0.12f, 0.25f, 0.18f), black, new Vector3(size.X * 0.5f + 0.06f, top + 1.0f, 0f));
        node.AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.30f, OuterRadius = 0.34f, Rings = 16, RingSegments = 6, Material = black },
            Transform = new Transform3D(new Basis(Vector3.Back, Mathf.Pi / 2f), new Vector3(size.X * 0.5f + 0.05f, top + 0.75f, 0f)),
        });
        _topper ??= new StandardMaterial3D { AlbedoColor = new Color(0.95f, 0.95f, 0.92f), EmissionEnabled = true, Emission = new Color(0.9f, 0.88f, 0.8f), EmissionEnergyMultiplier = 0.5f };
        Box(node, new Vector3(size.X * 0.9f, 0.3f, size.Z * 0.9f), _topper, new Vector3(0f, top + size.Y + 0.15f, 0f));
        Box(node, new Vector3(size.X * 0.92f, 0.08f, size.Z * 0.92f), red, new Vector3(0f, top + size.Y + 0.15f, 0f));
    }

    private static StandardMaterial3D? _lamp;
    private static StandardMaterial3D? _topper;

    /// <summary>A gable roof along x: two slabs of shingles 18 cm thick from the ridge out past the eaves, a ridge cap, the fascia and the gutters.</summary>
    private static void GableRoof(Node3D node, float width, float depth, float eaves, float rise, Material roofing, Material trim)
    {
        const float overhang = 0.5f;
        const float thickness = 0.18f;
        var half = depth * 0.5f;
        var angle = MathF.Atan2(rise, half);
        var run = (half + overhang) / MathF.Cos(angle);
        foreach (var side in new[] { -1f, 1f })
        {
            // The slab's top runs from the ridge down to past the eaves; its middle is half way, a half-thickness in.
            var down = new Vector3(0f, -MathF.Sin(angle), side * MathF.Cos(angle));
            var normal = new Vector3(0f, MathF.Cos(angle), side * MathF.Sin(angle));
            var ridge = new Vector3(0f, eaves + rise + thickness / MathF.Cos(angle) * 0.5f, 0f);
            // The ridge point is on the slab's mid-thickness line: the slab sits on the walls and the gables.
            var centre = ridge + down * (run * 0.5f);
            Oriented(node, centre, Vector3.Right, normal, new Vector3(width + 0.7f, thickness, run), roofing);
            // The fascia board along the eave, and a gutter under it.
            var edge = ridge + down * run;
            Oriented(node, edge - normal * 0.1f, Vector3.Right, Vector3.Up, new Vector3(width + 0.72f, 0.24f, 0.04f), trim);
            Box(node, new Vector3(width + 0.6f, 0.12f, 0.14f), trim, edge + new Vector3(0f, -0.26f, side * 0.06f));
        }
        Box(node, new Vector3(width + 0.75f, 0.12f, 0.36f), roofing, new Vector3(0f, eaves + rise + thickness * 1.1f, 0f));
    }

    /// <summary>A door with its frame, a step's worth above the plinth, in the wall facing +z.</summary>
    private static void Door(Node3D node, Vector3 at, Material trim)
    {
        var panel = Plain(new Color(0.34f, 0.12f, 0.08f), 0.55f);
        Box(node, new Vector3(1.0f, 2.1f, 0.08f), panel, at + new Vector3(0f, 1.05f, 0.04f));
        Frame(node, at + new Vector3(0f, 1.05f, 0.06f), 1.0f, 2.1f, 0.14f, trim);
        // Panels on the door, a glazed light at the top, and the knob.
        Box(node, new Vector3(0.6f, 0.4f, 0.04f), Glass(), at + new Vector3(0f, 1.75f, 0.09f));
        Box(node, new Vector3(0.32f, 0.55f, 0.03f), Plain(new Color(0.29f, 0.10f, 0.07f), 0.6f), at + new Vector3(-0.2f, 0.75f, 0.09f));
        Box(node, new Vector3(0.32f, 0.55f, 0.03f), Plain(new Color(0.29f, 0.10f, 0.07f), 0.6f), at + new Vector3(0.2f, 0.75f, 0.09f));
        node.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.035f, Height = 0.07f, Material = Plain(new Color(0.75f, 0.62f, 0.3f), 0.3f) }, Position = at + new Vector3(0.38f, 1.0f, 0.13f) });
    }

    /// <summary>The front porch: a timber deck over the plinth with steps, four posts and a lean-to roof against the wall.</summary>
    private static void Porch(Node3D node, float width, float front, float plinth, Material trim, Material concrete, Material roofing)
    {
        const float deep = 2.4f;
        var deck = WorldBuilder.PropMaterial("bark", new Color(0.58f, 0.47f, 0.36f), 1.2f);
        Box(node, new Vector3(width, plinth, deep), deck, new Vector3(0f, plinth * 0.5f, front + deep * 0.5f));
        for (var i = 0; i < 2; i++)
        {
            var h = plinth * (2 - i) / 3f;
            Box(node, new Vector3(1.6f, h, 0.32f), concrete, new Vector3(0f, h * 0.5f, front + deep + 0.16f + 0.32f * i));
        }
        foreach (var x in new[] { -width * 0.5f + 0.15f, -width * 0.18f, width * 0.18f, width * 0.5f - 0.15f })
        {
            if (MathF.Abs(x) < 0.9f) continue;
            Box(node, new Vector3(0.16f, 2.5f, 0.16f), trim, new Vector3(x, plinth + 1.25f, front + deep - 0.15f));
        }
        // A railing either side of the steps.
        foreach (var side in new[] { -1f, 1f })
        {
            var span = width * 0.5f - 1.0f;
            Box(node, new Vector3(span, 0.08f, 0.08f), trim, new Vector3(side * (1.0f + span * 0.5f), plinth + 0.9f, front + deep - 0.15f));
        }
        // The porch roof slopes from the wall down over the posts.
        var top = plinth + 2.5f;
        var angle = MathF.Atan2(0.5f, deep);
        Oriented(node, new Vector3(0f, top + 0.3f, front + deep * 0.5f + 0.1f), Vector3.Right,
            new Vector3(0f, MathF.Cos(angle), MathF.Sin(angle)), new Vector3(width + 0.4f, 0.14f, deep + 0.6f), roofing);
        Box(node, new Vector3(width + 0.2f, 0.22f, 0.12f), trim, new Vector3(0f, top + 0.02f, front + deep - 0.1f));
    }

    /// <summary>
    /// A window in a wall: its sill at <paramref name="at"/> (bottom middle, on the wall's face), facing out along
    /// +z turned by <paramref name="turn"/>: the glass, a white frame and glazing bars (two over two), the sill under
    /// it, and shutters either side when given.
    /// </summary>
    private static void Window(Node3D node, Vector3 at, float turn, float width, float height, Material trim, Material? shutters)
    {
        var holder = new Node3D { Position = at, Rotation = new Vector3(0f, turn, 0f) };
        node.AddChild(holder);
        var middle = new Vector3(0f, height * 0.5f, 0f);
        Box(holder, new Vector3(width, height, 0.06f), Glass(), middle + new Vector3(0f, 0f, 0.01f));
        Frame(holder, middle + new Vector3(0f, 0f, 0.05f), width, height, 0.1f, trim);
        Box(holder, new Vector3(0.05f, height, 0.05f), trim, middle + new Vector3(0f, 0f, 0.05f));
        Box(holder, new Vector3(width, 0.05f, 0.05f), trim, middle + new Vector3(0f, 0f, 0.05f));
        Box(holder, new Vector3(width + 0.3f, 0.07f, 0.2f), trim, new Vector3(0f, -0.06f, 0.1f));
        if (shutters is null) return;
        foreach (var side in new[] { -1f, 1f })
        {
            Box(holder, new Vector3(width * 0.42f, height + 0.1f, 0.05f), shutters, new Vector3(side * (width * 0.5f + 0.12f + width * 0.21f), height * 0.5f, 0.04f));
        }
    }

    /// <summary>A rectangular frame of boards <paramref name="board"/> wide round an opening centred at <paramref name="centre"/>, in the xy plane.</summary>
    private static void Frame(Node3D node, Vector3 centre, float width, float height, float board, Material material)
    {
        Box(node, new Vector3(width + 2f * board, board, 0.06f), material, centre + new Vector3(0f, height * 0.5f + board * 0.5f, 0f));
        Box(node, new Vector3(width + 2f * board, board, 0.06f), material, centre - new Vector3(0f, height * 0.5f + board * 0.5f, 0f));
        Box(node, new Vector3(board, height, 0.06f), material, centre + new Vector3(width * 0.5f + board * 0.5f, 0f, 0f));
        Box(node, new Vector3(board, height, 0.06f), material, centre - new Vector3(width * 0.5f + board * 0.5f, 0f, 0f));
    }

    /// <summary>A barn's pair of doors, framed in white with a white cross brace on each leaf.</summary>
    private static void BracedDoor(Node3D node, Vector3 at, float width, float height, Material leaf, Material trim)
    {
        var middle = at + new Vector3(0f, height * 0.5f, 0f);
        Box(node, new Vector3(width, height, 0.08f), leaf, middle);
        Frame(node, middle + new Vector3(0f, 0f, 0.05f), width, height, 0.2f, trim);
        Box(node, new Vector3(0.14f, height, 0.06f), trim, middle + new Vector3(0f, 0f, 0.07f));
        foreach (var side in new[] { -1f, 1f })
        {
            var centre = middle + new Vector3(side * width * 0.25f, 0f, 0.08f);
            var leafWidth = width * 0.5f;
            var diagonal = MathF.Sqrt(leafWidth * leafWidth + height * height);
            var angle = MathF.Atan2(height, leafWidth);
            foreach (var sign in new[] { -1f, 1f })
            {
                node.AddChild(new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(diagonal - 0.1f, 0.14f, 0.05f), Material = trim },
                    Transform = new Transform3D(new Basis(Vector3.Back, sign * angle), centre),
                });
            }
            // The rail across the middle of the leaf.
            Box(node, new Vector3(leafWidth, 0.14f, 0.05f), trim, centre + new Vector3(0f, 0f, 0.01f));
        }
        // The track the doors slide on.
        Box(node, new Vector3(width * 2.1f, 0.12f, 0.12f), Plain(new Color(0.15f, 0.15f, 0.16f), 0.5f), at + new Vector3(0f, height + 0.3f, 0.06f));
    }

    /// <summary>The gable wall of the gambrel at <paramref name="z"/>: the five-sided panel above the eaves, as a fan of triangles.</summary>
    private static void GableWall(Node3D node, Vector2[] outline, float z, Material material)
    {
        var mb = new MeshBuilder();
        var facing = z > 0f ? Vector3.Back : Vector3.Forward;
        mb.SetNormal(facing);
        var baseLine = outline[0].Y;
        var centre = new Vector3(0f, baseLine, z);
        for (var i = 0; i < outline.Length - 1; i++)
        {
            var a = new Vector3(outline[i].X, outline[i].Y, z);
            var b = new Vector3(outline[i + 1].X, outline[i + 1].Y, z);
            // Wound clockwise as seen from outside.
            if (z > 0f) { mb.AddVertex(centre); mb.AddVertex(a); mb.AddVertex(b); }
            else { mb.AddVertex(centre); mb.AddVertex(b); mb.AddVertex(a); }
        }
        node.AddChild(new MeshInstance3D { Mesh = mb.Commit(), MaterialOverride = material });
    }

    /// <summary>One slope of the gambrel between two outline points, as long as the barn plus its overhang, 15 cm thick.</summary>
    private static void RoofSlope(Node3D node, Vector2 from, Vector2 to, float length, Material roofing, Material trim, bool eave)
    {
        var along = new Vector3(to.X - from.X, to.Y - from.Y, 0f);
        var span = along.Length();
        var down = along / span;
        // The slab's outward normal: up and away from the middle.
        var normal = new Vector3(-down.Y, down.X, 0f);
        if (normal.Y < 0f) normal = -normal;
        var overhang = eave ? 0.4f : 0f;
        var start = new Vector3(from.X, from.Y, 0f) - (eave && from.Y < to.Y ? down * overhang : Vector3.Zero);
        var end = new Vector3(to.X, to.Y, 0f) + (eave && to.Y < from.Y ? down * overhang : Vector3.Zero);
        var middle = (start + end) * 0.5f + normal * 0.075f;
        Oriented(node, middle, down, normal, new Vector3((end - start).Length() + 0.05f, 0.15f, length), roofing);
        _ = trim;
    }

    /// <summary>A white board along the gambrel's edge at a gable end (the fascia, the "rake").</summary>
    private static void Rafter(Node3D node, Vector2 from, Vector2 to, float z, Material trim)
    {
        var a = new Vector3(from.X, from.Y, z);
        var b = new Vector3(to.X, to.Y, z);
        var along = (b - a).Normalized();
        var normal = new Vector3(-along.Y, along.X, 0f);
        if (normal.Y < 0f) normal = -normal;
        Oriented(node, (a + b) * 0.5f + normal * 0.06f, along, normal, new Vector3((b - a).Length() + 0.1f, 0.22f, 0.06f), trim);
    }

    /// <summary>
    /// A box with its x and y axes along the given (orthonormal) directions; z is x cross y, so the basis is never a
    /// mirror (a mirrored box shows its inside faces).
    /// </summary>
    private static void Oriented(Node3D node, Vector3 centre, Vector3 x, Vector3 y, Vector3 size, Material material)
    {
        node.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size, Material = material },
            Transform = new Transform3D(new Basis(x, y, x.Cross(y).Normalized()), centre),
        });
    }

    private static void Box(Node3D node, Vector3 size, Material material, Vector3 centre)
    {
        node.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size, Material = material }, Position = centre });
    }

    private static readonly Dictionary<(Color, float), StandardMaterial3D> Plains = new();

    private static StandardMaterial3D Plain(Color colour, float roughness)
    {
        if (!Plains.TryGetValue((colour, roughness), out var material))
        {
            material = new StandardMaterial3D { AlbedoColor = colour, Roughness = roughness };
            Plains[(colour, roughness)] = material;
        }
        return material;
    }

    /// <summary>Window glass: dark, smooth and a little metallic, so the sky shows in it.</summary>
    private static StandardMaterial3D Glass()
    {
        _glass ??= new StandardMaterial3D { AlbedoColor = new Color(0.10f, 0.13f, 0.17f), Roughness = 0.08f, Metallic = 0.55f };
        return _glass;
    }

    private static StandardMaterial3D? _glass;

    public static void ReleaseShared()
    {
        Plains.Clear();
        _glass = null;
        _lamp = null;
        _topper = null;
    }
}

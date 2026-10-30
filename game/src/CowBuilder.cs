using Godot;
using GrudgeLap.Physics.Race;

namespace GrudgeLap.Game;

/// <summary>
/// A Holstein cow to the measure of a real one (1.45 m at the withers, 2.45 m from the nose to the pin bones):
/// a lofted barrel of a body with the hips and the shoulders standing out, a deep neck with its dewlap, the long
/// head with its muzzle, ears and eyes, four jointed legs on their hooves, the udder and a tail with its switch.
/// Every cow has black and white patches of its own: a texture painted from 3-D noise over the body as built, so a
/// patch runs on from the barrel down a leg; the legs are white below the knees and the face has a white blaze.
/// The neck, the head, the legs and the tail hang from pivots that <see cref="CowRig"/> moves.
/// </summary>
/// <remarks>
/// Built in the cow's own frame: +x towards the head, +y up, z across; the origin on the ground under the middle of
/// the cow (the herd's plan is centred there). Until 0.9.1 a cow was a box with two black boxes for patches.
/// </remarks>
internal static class CowBuilder
{
    private const int Segments = 28;
    /// <summary>The atlas the hide is painted into: the body and the neck, the head, the legs, the tail.</summary>
    private const int AtlasWidth = 768;
    private const int AtlasHeight = 384;
    /// <summary>The model is built nose at +1.42 and pin bones at -1.03; this shifts it to centre on the origin.</summary>
    private const float Centre = 0.2f;

    private static readonly Color White = new(0.90f, 0.89f, 0.86f);
    private static readonly Color Black = new(0.035f, 0.033f, 0.035f);
    private static readonly Color Hoof = new(0.16f, 0.14f, 0.13f);

    private enum Part { Body, Neck, Head, Leg, Tail }

    /// <summary>A cross-section of a tube: a superellipse about <paramref name="Centre"/>, rounder below than above where the two halves differ.</summary>
    private readonly record struct Ring(Vector3 Centre, Vector3 Side, Vector3 Up, float HalfWidth, float Top, float Bottom);

    /// <summary>A tube as built, kept for painting its patch of the atlas.</summary>
    private sealed record Tube(Ring[] Rings, Rect2 Uv, Part Part, Vector3 Offset);

    public static CowRig Build(int seed)
    {
        var random = new Random(seed * 7919 + 11);
        var rig = new CowRig { Name = "Cow" };
        var model = new Node3D { Name = "Model", Position = new Vector3(-Centre, 0f, 0f) };
        rig.AddChild(model);
        rig.Model = model;
        var tubes = new List<Tube>();
        var hide = new StandardMaterial3D { Roughness = 0.82f, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic };

        // ---- the body: rings from the pin bones to the brisket, the back nearly level, the belly lowest at the barrel.
        (float X, float Top, float Bottom, float HalfWidth)[] body =
        [
            (-1.03f, 1.30f, 1.10f, 0.06f),
            (-0.98f, 1.40f, 0.88f, 0.17f),
            (-0.89f, 1.45f, 0.75f, 0.23f),
            (-0.74f, 1.47f, 0.69f, 0.275f),
            (-0.52f, 1.45f, 0.64f, 0.295f),
            (-0.26f, 1.43f, 0.595f, 0.33f),
            (0.04f, 1.43f, 0.60f, 0.335f),
            (0.30f, 1.45f, 0.66f, 0.30f),
            (0.50f, 1.48f, 0.74f, 0.255f),
            (0.66f, 1.45f, 0.82f, 0.205f),
            (0.78f, 1.32f, 0.89f, 0.15f),
            (0.85f, 1.18f, 0.97f, 0.075f),
        ];
        var rings = body.Select(r =>
        {
            var middle = (r.Top + r.Bottom) * 0.5f;
            return new Ring(new Vector3(r.X, middle, 0f), Vector3.Back, Vector3.Up, r.HalfWidth, r.Top - middle, middle - r.Bottom);
        }).ToArray();
        tubes.Add(AddTube(model, rings, new Rect2(0f, 0f, 384f, 384f), Part.Body, Vector3.Zero, hide, capStart: true, capEnd: true, "Body"));

        // ---- the neck, from a pivot inside the shoulders to the poll, deep with the dewlap under it; the head hangs
        // from the poll on a pivot of its own.
        var neckPivot = new Vector3(0.55f, 1.12f, 0f);
        var neck = new Node3D { Name = "Neck", Position = neckPivot };
        model.AddChild(neck);
        rig.Neck = neck;
        var poll = new Vector3(1.12f, 1.31f, 0f) - neckPivot;
        var neckRings = new[]
        {
            NeckRing(Vector3.Zero, poll, 0.17f, 0.20f, 0.30f),
            NeckRing(poll * 0.35f, poll, 0.155f, 0.17f, 0.28f),
            NeckRing(poll * 0.70f, poll, 0.13f, 0.14f, 0.19f),
            NeckRing(poll * 0.95f, poll, 0.115f, 0.115f, 0.13f),
        };
        tubes.Add(AddTube(neck, neckRings, new Rect2(384f, 0f, 192f, 192f), Part.Neck, neckPivot, hide, capStart: false, capEnd: true, "NeckMesh"));

        var head = new Node3D { Name = "Head", Position = poll };
        neck.AddChild(head);
        rig.Head = head;
        var nose = new Vector3(0.30f, -0.44f, 0f);
        var along = nose.Normalized();
        var faceUp = new Vector3(-along.Y, along.X, 0f);   // the forehead's side of the head
        (float T, float HalfWidth, float Top, float Bottom)[] headShape =
        [
            (-0.02f, 0.085f, 0.07f, 0.08f),
            (0.06f, 0.115f, 0.085f, 0.095f),
            (0.16f, 0.12f, 0.08f, 0.09f),
            (0.30f, 0.085f, 0.065f, 0.075f),
            (0.44f, 0.088f, 0.06f, 0.07f),
            (0.53f, 0.08f, 0.05f, 0.06f),
        ];
        var headRings = headShape.Select(h => new Ring(along * h.T, Vector3.Back, faceUp, h.HalfWidth, h.Top, h.Bottom)).ToArray();
        tubes.Add(AddTube(head, headRings, new Rect2(576f, 0f, 192f, 160f), Part.Head, neckPivot + poll, hide, capStart: true, capEnd: false, "HeadMesh"));
        // The muzzle, the nostrils' dark nose pad at the end of it.
        var muzzle = Plain(new Color(0.16f, 0.13f, 0.13f), 0.45f);
        Ellipsoid(head, along * 0.535f, new Vector3(0.075f, 0.06f, 0.088f), muzzle, Basis.LookingAt(-along, faceUp));
        // The eyes, set wide on the brow, and the ears flat out to the sides behind them.
        var eye = Plain(new Color(0.02f, 0.015f, 0.012f), 0.15f);
        foreach (var side in new[] { -1f, 1f })
        {
            Ellipsoid(head, along * 0.10f + faceUp * 0.03f + new Vector3(0f, 0f, side * 0.105f), new Vector3(0.024f, 0.02f, 0.02f), eye, Basis.Identity);
            var ear = new Vector3(-0.02f, 0.03f, side * 0.16f) + along * 0.03f;
            Ellipsoid(head, ear, new Vector3(0.05f, 0.022f, 0.095f), Plain(Black, 0.8f), new Basis(Vector3.Right, side * 0.35f));
        }

        // ---- the legs: tubes down a jointed line, each from a pivot inside the body; white below the knee, on hooves.
        (Vector3 Pivot, (float X, float Y, float Half)[] Line)[] legs =
        [
            (new Vector3(0.55f, 1.0f, 0.17f), FrontLeg),
            (new Vector3(0.55f, 1.0f, -0.17f), FrontLeg),
            (new Vector3(-0.70f, 1.1f, 0.19f), HindLeg),
            (new Vector3(-0.70f, 1.1f, -0.19f), HindLeg),
        ];
        rig.Legs = new Node3D[4];
        for (var i = 0; i < legs.Length; i++)
        {
            var (pivot, line) = legs[i];
            var leg = new Node3D { Name = $"Leg{i}", Position = pivot };
            model.AddChild(leg);
            rig.Legs[i] = leg;
            var points = line.Select(p => new Vector3(p.X - pivot.X, p.Y - pivot.Y, 0f)).ToArray();
            var legRings = new Ring[points.Length];
            for (var k = 0; k < points.Length; k++)
            {
                var direction = (points[Math.Min(k + 1, points.Length - 1)] - points[Math.Max(k - 1, 0)]).Normalized();
                // The ring lies across the leg: its "up" is the leg's forward side, square to the line in the side plane.
                var forward = new Vector3(-direction.Y, direction.X, 0f);
                if (forward.X < 0f) forward = -forward;
                legRings[k] = new Ring(points[k], Vector3.Back, forward, line[k].Half * 0.82f, line[k].Half, line[k].Half);
            }
            tubes.Add(AddTube(leg, legRings, new Rect2(384f + 96f * i, 192f, 96f, 192f), Part.Leg, pivot, hide, capStart: true, capEnd: true, "LegMesh"));
        }

        // ---- the udder between the hind legs, with its four teats.
        var udder = Plain(new Color(0.84f, 0.62f, 0.58f), 0.6f);
        Ellipsoid(model, new Vector3(-0.47f, 0.63f, 0f), new Vector3(0.21f, 0.12f, 0.16f), udder, Basis.Identity);
        foreach (var (x, z) in new[] { (-0.40f, 0.07f), (-0.40f, -0.07f), (-0.55f, 0.07f), (-0.55f, -0.07f) })
        {
            Ellipsoid(model, new Vector3(x, 0.50f, z), new Vector3(0.016f, 0.04f, 0.016f), udder, Basis.Identity);
        }

        // ---- the tail from the tail head, hanging to the hocks, with the switch at its end.
        var tailPivot = new Vector3(-1.0f, 1.40f, 0f);
        var tail = new Node3D { Name = "Tail", Position = tailPivot };
        model.AddChild(tail);
        rig.Tail = tail;
        (float X, float Y, float Half)[] tailLine = [(-1.0f, 1.42f, 0.04f), (-1.045f, 1.36f, 0.035f), (-1.06f, 1.10f, 0.025f), (-1.05f, 0.80f, 0.02f), (-1.04f, 0.62f, 0.018f), (-1.035f, 0.50f, 0.03f), (-1.03f, 0.36f, 0.035f), (-1.03f, 0.30f, 0.01f)];
        var tailRings = tailLine.Select((p, k) =>
        {
            var at = new Vector3(p.X, p.Y, 0f) - tailPivot;
            return new Ring(at, Vector3.Back, Vector3.Right, p.Half, p.Half, p.Half);
        }).ToArray();
        tubes.Add(AddTube(tail, tailRings, new Rect2(576f, 160f, 192f, 32f), Part.Tail, tailPivot, hide, capStart: true, capEnd: true, "TailMesh"));

        hide.AlbedoTexture = Paint(tubes, random);
        rig.Phase = (float)random.NextDouble() * 20f;
        return rig;
    }

    // The legs' lines in the cow's frame (x along, y up) and their half thickness, front to back: the forearm, the
    // knee, the cannon, the fetlock and the hoof; the hind leg's thigh runs forward to the stifle and back to the hock.
    private static readonly (float X, float Y, float Half)[] FrontLeg =
        [(0.55f, 1.0f, 0.10f), (0.565f, 0.66f, 0.075f), (0.575f, 0.45f, 0.056f), (0.58f, 0.24f, 0.042f), (0.595f, 0.12f, 0.046f), (0.62f, 0.05f, 0.052f), (0.645f, 0.0f, 0.058f)];
    private static readonly (float X, float Y, float Half)[] HindLeg =
        [(-0.70f, 1.1f, 0.15f), (-0.63f, 0.86f, 0.115f), (-0.74f, 0.64f, 0.08f), (-0.83f, 0.48f, 0.06f), (-0.80f, 0.24f, 0.042f), (-0.785f, 0.12f, 0.046f), (-0.76f, 0.05f, 0.052f), (-0.735f, 0.0f, 0.058f)];

    private static Ring NeckRing(Vector3 at, Vector3 axis, float halfWidth, float top, float bottom)
    {
        var along = axis.Normalized();
        return new Ring(at, Vector3.Back, new Vector3(-along.Y, along.X, 0f), halfWidth, top, bottom);
    }

    /// <summary>The point of a ring at angle <paramref name="angle"/> (0 at the side +z, a quarter turn at the top).</summary>
    private static Vector3 OnRing(in Ring ring, float angle)
    {
        var c = MathF.Cos(angle);
        var s = MathF.Sin(angle);
        // A superellipse of exponent 2.4: fuller than an ellipse, the barrel of a cow rather than a sausage.
        const float power = 2f / 2.4f;
        var x = MathF.Sign(c) * MathF.Pow(MathF.Abs(c), power);
        var y = MathF.Sign(s) * MathF.Pow(MathF.Abs(s), power);
        return ring.Centre + ring.Side * (ring.HalfWidth * x) + ring.Up * (y * (y >= 0f ? ring.Top : ring.Bottom));
    }

    private static Ring Lerp(in Ring a, in Ring b, float t) => new(
        a.Centre.Lerp(b.Centre, t), a.Side.Lerp(b.Side, t).Normalized(), a.Up.Lerp(b.Up, t).Normalized(),
        Mathf.Lerp(a.HalfWidth, b.HalfWidth, t), Mathf.Lerp(a.Top, b.Top, t), Mathf.Lerp(a.Bottom, b.Bottom, t));

    /// <summary>A smooth tube through the rings, with its UVs in <paramref name="uv"/> of the atlas (pixels).</summary>
    private static Tube AddTube(Node3D parent, Ring[] rings, Rect2 uv, Part part, Vector3 offset, Material material, bool capStart, bool capEnd, string name)
    {
        var columns = Segments + 1;
        var grid = new Vector3[rings.Length, columns];
        for (var i = 0; i < rings.Length; i++)
        {
            for (var j = 0; j < columns; j++) grid[i, j] = OnRing(rings[i], MathF.Tau * j / Segments);
        }
        var mb = new MeshBuilder();
        var normals = new Vector3[rings.Length, columns];
        for (var i = 0; i < rings.Length; i++)
        {
            for (var j = 0; j < columns; j++)
            {
                var around = grid[i, (j + 1) % Segments] - grid[i, (j - 1 + Segments) % Segments];
                var lengthwise = grid[Math.Min(i + 1, rings.Length - 1), j] - grid[Math.Max(i - 1, 0), j];
                var n = lengthwise.Cross(around);
                normals[i, j] = n.LengthSquared() > 1e-10f ? n.Normalized() : (grid[i, j] - rings[i].Centre).Normalized();
            }
        }
        var size = new Vector2(AtlasWidth, AtlasHeight);
        for (var i = 0; i < rings.Length; i++)
        {
            for (var j = 0; j < columns; j++)
            {
                mb.SetNormal(normals[i, j]);
                mb.SetUV(new Vector2(uv.Position.X + uv.Size.X * j / Segments, uv.Position.Y + uv.Size.Y * i / (rings.Length - 1)) / size);
                mb.AddVertex(grid[i, j]);
            }
        }
        for (var i = 0; i < rings.Length - 1; i++)
        {
            for (var j = 0; j < Segments; j++)
            {
                var a = i * columns + j;
                var b = a + columns;
                // Wound so the faces look outwards (Godot's front faces are clockwise seen from outside).
                mb.AddIndex(a); mb.AddIndex(a + 1); mb.AddIndex(b);
                mb.AddIndex(a + 1); mb.AddIndex(b + 1); mb.AddIndex(b);
            }
        }
        void Cap(int ring, bool start)
        {
            // Out of the tube's end, along its last step.
            var outward = (rings[ring].Centre - rings[ring + (start ? 1 : -1)].Centre).Normalized();
            var first = mb.VertexCount;
            mb.SetNormal(outward);
            mb.SetUV(new Vector2(uv.Position.X + uv.Size.X * 0.5f, uv.Position.Y + (start ? 0f : uv.Size.Y)) / size);
            mb.AddVertex(rings[ring].Centre + outward * 0.01f);
            for (var j = 0; j < Segments; j++)
            {
                var a = ring * columns + j;
                if (start) { mb.AddIndex(first); mb.AddIndex(a + 1); mb.AddIndex(a); }
                else { mb.AddIndex(first); mb.AddIndex(a); mb.AddIndex(a + 1); }
            }
        }
        if (capStart) Cap(0, true);
        if (capEnd) Cap(rings.Length - 1, false);
        parent.AddChild(new MeshInstance3D { Name = name, Mesh = mb.Commit(), MaterialOverride = material });
        return new Tube(rings, uv, part, offset);
    }

    /// <summary>
    /// Paints the hide: every texel of a tube's patch of the atlas is the point of the body it lands on, coloured by
    /// the cow's patches there (3-D noise over the body as it stands, so a patch is seamless round the barrel and runs
    /// on across the parts), white on the legs below the knees and the switch of the tail, a white blaze down a
    /// black face, and the white a little grubby low down.
    /// </summary>
    private static ImageTexture Paint(List<Tube> tubes, Random random)
    {
        var data = new byte[AtlasWidth * AtlasHeight * 3];
        var shift = new Vector3((float)random.NextDouble() * 50f, (float)random.NextDouble() * 50f, (float)random.NextDouble() * 50f);
        // Some cows are mostly white, some mostly black.
        var threshold = 0.46f + 0.14f * (float)random.NextDouble();
        foreach (var tube in tubes)
        {
            var x0 = (int)tube.Uv.Position.X;
            var y0 = (int)tube.Uv.Position.Y;
            var w = Math.Max((int)tube.Uv.Size.X, 1);
            var h = Math.Max((int)tube.Uv.Size.Y, 1);
            for (var py = 0; py < h; py++)
            {
                var t = (tube.Rings.Length - 1) * (py + 0.5f) / h;
                var k = Math.Min((int)t, tube.Rings.Length - 2);
                var ring = Lerp(tube.Rings[k], tube.Rings[k + 1], t - k);
                for (var px = 0; px < w; px++)
                {
                    var angle = MathF.Tau * (px + 0.5f) / w;
                    var local = OnRing(ring, angle);
                    var colour = HideAt(tube.Part, local + tube.Offset, local, ring, shift, threshold);
                    var index = ((y0 + py) * AtlasWidth + x0 + px) * 3;
                    data[index] = (byte)(Mathf.Clamp(colour.R, 0f, 1f) * 255f);
                    data[index + 1] = (byte)(Mathf.Clamp(colour.G, 0f, 1f) * 255f);
                    data[index + 2] = (byte)(Mathf.Clamp(colour.B, 0f, 1f) * 255f);
                }
            }
        }
        var image = Image.CreateFromData(AtlasWidth, AtlasHeight, false, Image.Format.Rgb8, data);
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    private static Color HideAt(Part part, Vector3 p, Vector3 local, in Ring ring, Vector3 shift, float threshold)
    {
        // Blobs a metre or so across with ragged edges: two scales of noise, the finer one warping the coarse one.
        var q = p * 1.7f + shift;
        var n = Fbm(q + new Vector3(Fbm(q * 2.1f) - 0.5f, 0f, Fbm(q * 2.1f + new Vector3(5.2f, 1.3f, 7.7f)) - 0.5f) * 0.55f);
        var black = Mathf.SmoothStep(threshold - 0.012f, threshold + 0.012f, n);
        switch (part)
        {
            case Part.Leg:
                // White socks below the knees and hocks; the hooves dark.
                if (p.Y < 0.055f) return Hoof;
                black *= Mathf.SmoothStep(0.40f, 0.52f, p.Y);
                break;
            case Part.Head:
                // A black face with a white blaze down the forehead and the nose.
                var across = MathF.Abs(local.Z);
                var forehead = (local - ring.Centre).Dot(ring.Up) > 0f;
                var blaze = forehead && across < 0.035f + 0.02f * MathF.Sin(p.Y * 30f) ? 1f : 0f;
                black = MathF.Max(black, 0.85f) * (1f - blaze);
                break;
            case Part.Tail:
                if (p.Y < 0.55f) black = 0f;
                break;
        }
        // A belly and legs that have lain in the field: the white goes grey-brown low down.
        var grime = Mathf.Clamp((0.75f - p.Y) / 0.6f, 0f, 1f) * 0.22f;
        var white = White.Lerp(new Color(0.55f, 0.48f, 0.40f), grime);
        // A little variation hair to hair.
        var speck = 0.96f + 0.08f * Hash((int)(p.X * 400f), (int)(p.Y * 400f), (int)(p.Z * 400f));
        var c = white.Lerp(Black, black);
        return new Color(c.R * speck, c.G * speck, c.B * speck);
    }

    private static float Fbm(Vector3 p) => 0.55f * Noise(p) + 0.30f * Noise(p * 2.03f + new Vector3(17.1f, 3.3f, 9.9f)) + 0.15f * Noise(p * 4.1f + new Vector3(2.7f, 11.4f, 5.1f));

    /// <summary>Value noise in 3-D, 0..1, smooth between the lattice points.</summary>
    private static float Noise(Vector3 p)
    {
        var x0 = (int)MathF.Floor(p.X);
        var y0 = (int)MathF.Floor(p.Y);
        var z0 = (int)MathF.Floor(p.Z);
        var fx = Smooth(p.X - x0);
        var fy = Smooth(p.Y - y0);
        var fz = Smooth(p.Z - z0);
        float L(int dx, int dy, int dz) => Hash(x0 + dx, y0 + dy, z0 + dz);
        var x00 = Mathf.Lerp(L(0, 0, 0), L(1, 0, 0), fx);
        var x10 = Mathf.Lerp(L(0, 1, 0), L(1, 1, 0), fx);
        var x01 = Mathf.Lerp(L(0, 0, 1), L(1, 0, 1), fx);
        var x11 = Mathf.Lerp(L(0, 1, 1), L(1, 1, 1), fx);
        return Mathf.Lerp(Mathf.Lerp(x00, x10, fy), Mathf.Lerp(x01, x11, fy), fz);
    }

    private static float Smooth(float t) => t * t * (3f - 2f * t);

    private static float Hash(int x, int y, int z)
    {
        unchecked
        {
            var h = (uint)(x * 374761393 + y * 668265263 + z * 1274126177);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777215f;
        }
    }

    private static void Ellipsoid(Node3D parent, Vector3 position, Vector3 radii, Material material, Basis turn)
    {
        parent.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 16, Rings = 8, Material = material },
            Transform = new Transform3D(turn * Basis.FromScale(radii), position),
        });
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
}

/// <summary>
/// A cow's joints and what moves them (<see cref="Pose"/>): it grazes with its head down and looks up now and then,
/// walks or trots on diagonal pairs of legs as it moves, staggers when the car knocks it, and swishes its tail.
/// </summary>
internal sealed partial class CowRig : Node3D
{
    public Node3D Model = null!;
    public Node3D Neck = null!;
    public Node3D Head = null!;
    public Node3D[] Legs = [];
    public Node3D Tail = null!;
    /// <summary>A cow's own offset in the grazing cycle and the tail's swish, so the herd does not move as one.</summary>
    public float Phase;
    private float _gait;
    private float _neck;
    private float _head;

    /// <summary>Sets the cow where the herd has it and moves its joints for what it is doing.</summary>
    public void Pose(Cow cow, float time, float dt)
    {
        Position = Convert.ToGodot(cow.Position);
        Rotation = new Vector3(0f, -cow.YawDeg * Mathf.Pi / 180f, 0f);
        var speed = cow.Velocity.Length();
        var moving = Mathf.Clamp(speed / 2.6f, 0f, 1f);

        // A stride of about 1.6 m: the legs swing in diagonal pairs, further the faster it goes.
        _gait += dt * speed / 1.6f * Mathf.Tau;
        var swing = 0.38f * moving;
        Legs[0].Rotation = new Vector3(0f, 0f, swing * MathF.Sin(_gait));
        Legs[3].Rotation = new Vector3(0f, 0f, swing * MathF.Sin(_gait));
        Legs[1].Rotation = new Vector3(0f, 0f, swing * MathF.Sin(_gait + Mathf.Pi));
        Legs[2].Rotation = new Vector3(0f, 0f, swing * MathF.Sin(_gait + Mathf.Pi));

        // Grazing: the neck down and the head turned up on it to bring the muzzle to the grass, eleven seconds in
        // fourteen; then up, chewing, looking about. Startled, shoved or on the move, the head comes up.
        var cycle = (time + Phase) % 14f;
        var grazing = cow.Startled <= 0f && cow.Knocked <= 0f && speed < 0.3f && cycle < 11f;
        var neckTarget = grazing ? -1.12f : 0.08f;
        var headTarget = grazing ? 0.62f : 0f;
        var rate = Mathf.Clamp(dt * (grazing ? 1.2f : 3f), 0f, 1f);
        _neck += (neckTarget - _neck) * rate;
        _head += (headTarget - _head) * rate;
        Neck.Rotation = new Vector3(0f, 0f, _neck);
        Head.Rotation = new Vector3(0f, 0f, _head + (grazing ? 0f : 0.05f * MathF.Sin(time * 1.3f + Phase)));

        // The tail swishes at the flies, harder once the cow is upset.
        var upset = cow.Startled > 0f || cow.Knocked > 0f ? 1f : 0f;
        Tail.Rotation = new Vector3((0.18f + 0.3f * upset) * MathF.Sin(time * (1.7f + upset) + Phase), 0f, 0.1f * upset);

        // A stagger after a knock: the cow rocks on its legs and sinks a little, dying away over a second and a half.
        var knocked = cow.Knocked;
        var rock = knocked > 0f ? 0.2f * MathF.Sin(knocked * 9f) * MathF.Exp(-knocked * 2.2f) : 0f;
        Model.Rotation = new Vector3(rock, 0f, 0f);
        Model.Position = new Vector3(Model.Position.X, -MathF.Abs(rock) * 0.3f, 0f);
    }
}

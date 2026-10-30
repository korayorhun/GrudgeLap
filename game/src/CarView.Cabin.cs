using Godot;

namespace GrudgeLap.Game;

/// <summary>
/// The cabin as the driver sees it (0.9.1): the daylight that comes in through the glass and bounces round the cabin,
/// the A-pillars as wide trimmed posts framing the screen, a dashboard in two tones with its vents, glovebox and trim
/// strip, door trims with their armrests, pulls, handles and speakers, shaped seats with bolsters and headrests on
/// posts, and a steering wheel with a padded hub. Until 0.9.1 the pillars were thin black rods, the headliner and the
/// fascia came out black (lit by the sky's ambient alone under the roof's shadow) and the seats were boxes.
/// </summary>
internal sealed partial class CarView
{
    internal sealed partial class BodyStyle
    {
        /// <summary>The dashboard's top pad and its lower fascia (they multiply the grained leather photo).</summary>
        public Color DashTint = new(0.30f, 0.31f, 0.33f);
        public Color FasciaTint = new(0.42f, 0.43f, 0.45f);
        /// <summary>The trim strip across the fascia: satin on the cars of the 2000s and later, dark plastic before.</summary>
        public bool SatinStrip;
        /// <summary>Cloth seats (the fabric photo); leather on the sports cars.</summary>
        public bool ClothSeats;
    }

    /// <summary>
    /// Daylight in the cabin: the shell shades it from the sun and the sky's ambient alone left everything that faces
    /// down or sideways black. Two soft lights without shadows stand in for the light that comes in through the glass
    /// and bounces off the seats and the dashboard: one low between the seats that lifts the headliner, the visors and
    /// the pillars, one over the passenger's footwell for the fascia and the door. Seen from the driver's seat only.
    /// </summary>
    private void BuildCabinLight(Node3D body)
    {
        var style = _style;
        var warm = new Color(1f, 0.97f, 0.92f);
        var bounce = new OmniLight3D
        {
            Name = "CabinBounce",
            Position = new Vector3(0f, style.EyeH - 0.62f, style.EyeZ + 0.05f),
            OmniRange = 1.6f,
            OmniAttenuation = 1.1f,
            LightEnergy = 0.9f,
            LightColor = warm,
            LightSpecular = 0.15f,
            ShadowEnabled = false,
        };
        var fascia = new OmniLight3D
        {
            Name = "CabinFascia",
            Position = new Vector3(-DriverX * 0.6f, style.EyeH - 0.30f, style.EyeZ - 0.15f),
            OmniRange = 1.3f,
            OmniAttenuation = 1.3f,
            LightEnergy = 0.6f,
            LightColor = warm,
            LightSpecular = 0.1f,
            ShadowEnabled = false,
        };
        foreach (var light in new[] { bounce, fascia })
        {
            body.AddChild(light);
            _cockpitOnly.Add(light);
        }
    }

    /// <summary>
    /// The A-pillars: trimmed posts from the screen's foot to the header, 8 cm wide at the foot and 6 at the top,
    /// their face turned to the driver's eyes so they read as the panels they are, not as rods.
    /// </summary>
    private void BuildPillarTrim(Node3D body, Material trim, Vector3 eye, float roofZ)
    {
        var style = _style;
        foreach (var side in new[] { -1f, 1f })
        {
            var a = new Vector3(side * (style.Sample(style.CrestW, style.CowlZ) - 0.02f), style.Sample(style.CrestH, style.CowlZ) - 0.03f, style.CowlZ + 0.02f);
            var b = new Vector3(side * (style.Sample(style.CrestW, roofZ) - 0.03f), style.Sample(style.CrestH, roofZ) - 0.03f, roofZ);
            const int pieces = 6;
            for (var p = 0; p < pieces; p++)
            {
                var p0 = a.Lerp(b, (float)p / pieces);
                var p1 = a.Lerp(b, (float)(p + 1) / pieces);
                var mid = (p0 + p1) * 0.5f;
                var along = (p1 - p0).Normalized();
                // The face turned towards the eyes, square to the pillar's line.
                var toEye = (eye - mid) - along * (eye - mid).Dot(along);
                var facing = toEye.Normalized();
                var across = facing.Cross(along).Normalized();
                var width = Mathf.Lerp(0.08f, 0.045f, (p + 0.5f) / pieces);
                body.AddChild(new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(width, 0.022f, p0.DistanceTo(p1) + 0.01f), Material = trim },
                    Transform = new Transform3D(new Basis(across, facing, along), mid + facing * 0.012f),
                });
            }
        }
    }

    /// <summary>
    /// The dashboard's furniture: vents at its ends, the glovebox in the passenger's side of the fascia with its shut
    /// line, and the trim strip across the fascia under the top pad.
    /// </summary>
    private void BuildDashDetails(Node3D body, Material fascia, Material dark, Material satin, float stretch, float dashHalf)
    {
        var style = _style;
        var cowlH = style.Sample(style.CentreH, style.CowlZ);
        var faceZ = style.CowlZ + 0.212f + stretch;
        var slat = new StandardMaterial3D { AlbedoColor = new Color(0.07f, 0.07f, 0.08f), Metallic = 0.2f, Roughness = 0.6f };
        foreach (var side in new[] { -1f, 1f })
        {
            var x = side * (dashHalf - 0.11f);
            var vent = new Vector3(x, cowlH - 0.105f, faceZ + 0.006f);
            Box(body, new Vector3(0.13f, 0.075f, 0.02f), vent, dark);
            for (var i = 0; i < 4; i++) Box(body, new Vector3(0.115f, 0.007f, 0.016f), vent + new Vector3(0f, -0.026f + i * 0.017f, 0.006f), slat);
        }
        // The trim strip across the fascia, broken by the binnacle and the centre stack.
        var stripY = cowlH - 0.155f;
        var stripMaterial = style.SatinStrip ? satin : dark;
        Box(body, new Vector3(dashHalf - 0.17f, 0.012f, 0.012f), new Vector3((dashHalf + 0.17f) * 0.5f, stripY, faceZ + 0.004f), stripMaterial);
        // The glovebox: a lid a few millimetres proud of the fascia, its shut line round it.
        var lid = new Vector3(-DriverX + 0.02f, cowlH - 0.27f, faceZ + 0.004f);
        Box(body, new Vector3(0.40f, 0.15f, 0.012f), lid, fascia);
        Box(body, new Vector3(0.41f, 0.004f, 0.014f), lid + new Vector3(0f, 0.077f, 0f), dark);
        Box(body, new Vector3(0.41f, 0.004f, 0.014f), lid + new Vector3(0f, -0.077f, 0f), dark);
        Box(body, new Vector3(0.004f, 0.15f, 0.014f), lid + new Vector3(0.203f, 0f, 0f), dark);
        Box(body, new Vector3(0.004f, 0.15f, 0.014f), lid + new Vector3(-0.203f, 0f, 0f), dark);
        Box(body, new Vector3(0.08f, 0.018f, 0.016f), lid + new Vector3(0f, 0.045f, 0.004f), dark);
    }

    /// <summary>
    /// The front doors' trims as seen beside the seats: the sill capping under the side glass, an armrest with its
    /// pull, the opening handle ahead of it and a speaker grille low in the door; on both doors.
    /// </summary>
    private void BuildDoorTrim(Node3D body, Material trim, Material dark, Material satin)
    {
        var style = _style;
        var front = MathF.Max(style.CowlZ + 0.10f, style.CabinFrom - 0.40f);
        var back = style.EyeZ + 0.28f;
        foreach (var side in new[] { -1f, 1f })
        {
            float Inner(float z, float y) => side * (MathF.Min(style.Sample(style.HalfWidth, z), style.Sample(style.EdgeW, z) + 0.05f) - 0.075f);
            // The capping along the top of the door, just under the glass.
            var belt = style.Sample(style.EdgeH, (front + back) * 0.5f) - 0.015f;
            var capFrom = MathF.Max(front, style.CabinFrom - 0.05f);
            var cap = Box(body, new Vector3(0.05f, 0.03f, back - capFrom), new Vector3(side * (style.Sample(style.EdgeW, (capFrom + back) * 0.5f) - 0.035f), belt, (capFrom + back) * 0.5f), dark);
            cap.Rotation = Vector3.Zero;
            // The armrest at elbow height with the pull moulded into it, and the handle ahead of it.
            var armZ = style.EyeZ - 0.18f;
            var armY = style.EyeH - 0.46f;
            Box(body, new Vector3(0.075f, 0.05f, 0.34f), new Vector3(Inner(armZ, armY), armY, armZ), trim);
            Box(body, new Vector3(0.03f, 0.08f, 0.10f), new Vector3(Inner(armZ, armY) - side * 0.03f, armY + 0.06f, armZ - 0.08f), trim);
            var handleZ = style.EyeZ - 0.48f;
            Box(body, new Vector3(0.02f, 0.025f, 0.11f), new Vector3(Inner(handleZ, armY + 0.12f) + side * 0.008f, armY + 0.12f, handleZ), satin);
            // The speaker grille in the door's lower front.
            var speakerZ = style.EyeZ - 0.62f;
            var speakerY = armY - 0.24f;
            body.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.075f, BottomRadius = 0.075f, Height = 0.012f, Material = dark, RadialSegments = 24 },
                Position = new Vector3(Inner(speakerZ, speakerY) + side * 0.004f, speakerY, speakerZ),
                RotationDegrees = new Vector3(0f, 0f, 90f),
            });
        }
    }

    /// <summary>
    /// A seat at <paramref name="x"/>: a cushion between two rolled bolsters, a backrest reclined 13 degrees with its
    /// side wings, a headrest on two posts; on a base above the floor in the upright cars.
    /// </summary>
    private void BuildSeat(Node3D body, float x, float seatY, float seatZ, Material cover, Material dark, bool raised)
    {
        Box(body, new Vector3(0.44f, 0.11f, 0.50f), new Vector3(x, seatY - 0.01f, seatZ), cover);
        foreach (var s in new[] { -1f, 1f })
        {
            body.AddChild(new MeshInstance3D
            {
                Mesh = new CapsuleMesh { Radius = 0.045f, Height = 0.48f, Material = cover, RadialSegments = 12, Rings = 4 },
                Position = new Vector3(x + s * 0.215f, seatY + 0.035f, seatZ + 0.01f),
                RotationDegrees = new Vector3(90f, 0f, 0f),
            });
        }
        // A positive pitch tips the top towards +z, the tail: the backrest reclines from the cushion onto the headrest.
        var back = new Node3D { Position = new Vector3(x, seatY + 0.04f, seatZ + 0.25f), Rotation = new Vector3(0.22f, 0f, 0f) };
        body.AddChild(back);
        Box(back, new Vector3(0.42f, 0.58f, 0.10f), new Vector3(0f, 0.29f, 0f), cover);
        foreach (var s in new[] { -1f, 1f })
        {
            back.AddChild(new MeshInstance3D
            {
                Mesh = new CapsuleMesh { Radius = 0.055f, Height = 0.54f, Material = cover, RadialSegments = 12, Rings = 4 },
                Position = new Vector3(s * 0.205f, 0.29f, -0.03f),
            });
            Box(back, new Vector3(0.012f, 0.08f, 0.012f), new Vector3(s * 0.07f, 0.62f, 0.0f), dark);
        }
        back.AddChild(new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = 0.065f, Height = 0.26f, Material = cover, RadialSegments = 12, Rings = 4 },
            Position = new Vector3(0f, 0.70f, 0.0f),
            RotationDegrees = new Vector3(0f, 0f, 90f),
            Scale = new Vector3(1f, 1f, 0.75f),
        });
        if (raised) Box(body, new Vector3(0.40f, seatY - 0.45f, 0.40f), new Vector3(x, (seatY + 0.31f) * 0.5f, seatZ), dark);
    }
}

using Godot;
using GrudgeLap.Physics.Vehicle;

namespace GrudgeLap.Game;

/// <summary>
/// The three front-wheel-drive hatchbacks: their bodies, keyed on dimensioned blueprints, and the details a
/// hatchback is known by, laid on the skin rather than stuck on as boxes. Headlamps, grilles and intakes are drawn
/// as outlines seen from the front and carried back along z onto the nose; tail lamps are seen from behind and
/// carried onto the tailgate; the rubbing strip and the pillars over the side glass are carried in from the side.
/// Each lies a few millimetres off the paint along the skin's normal, so it follows the curve of the nose or the
/// corner of the tailgate as the real part does. Inside: upright seats, a rear bench and a parcel shelf.
/// </summary>
internal sealed partial class CarView
{
    private const float SkinStep = 0.01f;     // spacing of the cached sections the details are laid on

    private Vector3[][]? _skin;
    private Vector2[]? _section;

    internal sealed partial class BodyStyle
    {
        public bool Hatchback;                // tailgate, rear bench, lamps and grilles laid on the skin
        public float RunStep = float.NaN;     // stations every this many metres from the cowl to the tail as well
        public float[]? ShoulderW;            // x of the shoulder point; the flank tucks in to it from the waist (w - 0.01 otherwise)
        public float RearSeatZ = float.NaN;   // centre of the rear bench's cushion
        public Vector2[] Headlamp = [];       // the right-hand headlamp seen from the front: x out from the centre line, y up
        public Vector2[] Grille = [];         // across the nose seen from the front, both halves
        public Vector2[] LowerIntake = [];    // the right-hand half of the bumper's intake, mirrored to the left
        public Vector2[] TailLamp = [];       // the right-hand tail lamp seen from behind
        public float FogLampX = float.NaN;    // round fog lamps in the front bumper
        public float FogLampY;
        public float FrontPlateY = 0.40f;     // centre heights of the number plates
        public float RearPlateY = 0.70f;
        public float[] ExhaustX = [];         // the tailpipes under the rear bumper
        public float ExhaustY = 0.27f;
        public float SpoilerLength;           // roof spoiler over the rear window; 0 for none
        public float RubStripY = float.NaN;   // centre height of the rubbing strip along the doors
        public float[] PillarsZ = [];         // black pillars over the side glass: the B-pillar of a five-door and the like
        public float HandleZ;                 // the front door's handle
        public float HandleY;
        public float MirrorZ;                 // where the door mirror stands on the beltline
        public bool PaintedMirrors;           // mirror housings in body colour; black plastic otherwise

        /// <summary>
        /// The hot hatch of 2001, a three-door on the lines of the second-generation Clio: 3.77 m long and 1.66 m
        /// wide over the sport model's arches, a short bonnet rounding down to a nose with big teardrop lamps at its
        /// corners, a raked screen and a roof that runs on to a tailgate set nearly upright, with the lamps standing
        /// up the corners beside it.
        /// </summary>
        /// <remarks>
        /// Keyed on a dimensioned drawing of the 2000 five-door (getoutlines.com, CC BY 4.0: 3773 mm long, wheelbase
        /// 2472, overhangs 714 / 587, height 1417, tracks 1418 / 1392), its side view measured every 5 cm for the
        /// roofline and at every 5 cm of height for the nose and the tail, its front and rear views for the
        /// tumblehome; the door is the three-door's, longer, and the tracks the sport model's. The nose is at z =
        /// -1.664 (714 mm ahead of the front axle at -0.95).
        /// </remarks>
        public static readonly BodyStyle HotHatch = new()
        {
            //         nose       -        -        -    f.axle   cowl   screen  header    roof      -     r.end   glass   g.base  tailgate   -     bumper   tail
            Keys = [-1.664f, -1.584f, -1.414f, -1.164f, -0.95f, -0.694f, -0.334f, -0.004f, 0.636f, 1.336f, 1.636f, 1.786f, 1.896f, 1.976f, 2.056f, 2.081f, 2.109f],
            FloorH = [0.21f, 0.19f, 0.17f, 0.17f, 0.17f, 0.17f, 0.17f, 0.17f, 0.17f, 0.18f, 0.20f, 0.22f, 0.24f, 0.25f, 0.26f, 0.26f, 0.26f],
            HalfWidth = [0.60f, 0.70f, 0.775f, 0.81f, 0.825f, 0.83f, 0.83f, 0.83f, 0.83f, 0.828f, 0.82f, 0.81f, 0.80f, 0.79f, 0.78f, 0.775f, 0.76f],
            WaistH = [0.34f, 0.40f, 0.50f, 0.56f, 0.58f, 0.60f, 0.61f, 0.61f, 0.62f, 0.63f, 0.64f, 0.64f, 0.64f, 0.62f, 0.55f, 0.46f, 0.42f],
            ShoulderH = [0.44f, 0.555f, 0.66f, 0.74f, 0.79f, 0.84f, 0.875f, 0.885f, 0.905f, 0.935f, 0.94f, 0.94f, 0.93f, 0.85f, 0.74f, 0.60f, 0.55f],
            ShoulderW = [0.56f, 0.66f, 0.74f, 0.78f, 0.79f, 0.79f, 0.785f, 0.78f, 0.78f, 0.78f, 0.775f, 0.765f, 0.755f, 0.75f, 0.75f, 0.75f, 0.72f],
            EdgeW = [0.52f, 0.62f, 0.69f, 0.72f, 0.73f, 0.745f, 0.745f, 0.745f, 0.745f, 0.74f, 0.735f, 0.72f, 0.71f, 0.70f, 0.70f, 0.70f, 0.66f],
            EdgeH = [0.455f, 0.575f, 0.695f, 0.775f, 0.82f, 0.855f, 0.89f, 0.90f, 0.92f, 0.95f, 0.955f, 0.96f, 0.955f, 0.865f, 0.755f, 0.61f, 0.56f],
            CrestW = [0.42f, 0.52f, 0.58f, 0.62f, 0.65f, 0.70f, 0.635f, 0.565f, 0.555f, 0.56f, 0.57f, 0.585f, 0.60f, 0.62f, 0.64f, 0.65f, 0.62f],
            CrestH = [0.465f, 0.59f, 0.715f, 0.80f, 0.85f, 0.88f, 1.125f, 1.315f, 1.375f, 1.345f, 1.305f, 1.215f, 1.06f, 0.88f, 0.77f, 0.615f, 0.565f],
            CentreH = [0.47f, 0.60f, 0.73f, 0.82f, 0.873f, 0.92f, 1.167f, 1.35f, 1.415f, 1.381f, 1.338f, 1.246f, 1.09f, 0.89f, 0.78f, 0.62f, 0.57f],
            CowlZ = -0.694f,
            CabinFrom = -0.01f,
            CabinTo = 1.39f,
            ScreenFrom = -0.694f,
            ScreenTo = -0.004f,
            RearGlassFrom = 1.636f,
            RearGlassTo = 1.896f,
            FrontArchTop = 0.665f,
            RearArchTop = 0.665f,
            FrontArchRadius = 0.37f,
            RearArchRadius = 0.37f,
            NoseTrimBelow = 0.33f,
            TailTrimBelow = 0.31f,
            FrontTrack = 0.73f,
            RearTrack = 0.715f,
            FrontTyreWidth = 0.205f,
            RearTyreWidth = 0.205f,
            RimRadius = 0.203f,
            TopSeams = [-0.72f, 1.62f],
            FlankSeams = [-0.56f, 0.786f],
            RearBulkheadZ = 1.896f,
            EyeZ = 0.42f,
            EyeH = 1.14f,
            LeatherTint = new(0.30f, 0.32f, 0.40f),
            CarpetTint = new(0.12f, 0.12f, 0.13f),
            HeadlinerTint = new(0.66f, 0.66f, 0.68f),
            Hatchback = true,
            RunStep = 0.10f,
            RearSeatZ = 1.0f,
            Headlamp = [new(0.43f, 0.640f), new(0.47f, 0.615f), new(0.56f, 0.600f), new(0.66f, 0.605f), new(0.72f, 0.625f), new(0.745f, 0.660f), new(0.735f, 0.695f), new(0.69f, 0.715f), new(0.60f, 0.722f), new(0.50f, 0.715f), new(0.445f, 0.690f)],
            Grille = [new(-0.37f, 0.592f), new(-0.20f, 0.585f), new(0f, 0.583f), new(0.20f, 0.585f), new(0.37f, 0.592f), new(0.35f, 0.612f), new(0.20f, 0.608f), new(0f, 0.606f), new(-0.20f, 0.608f), new(-0.35f, 0.612f)],
            TailLamp = [new(0.56f, 0.64f), new(0.70f, 0.625f), new(0.755f, 0.645f), new(0.765f, 0.75f), new(0.75f, 0.86f), new(0.71f, 0.93f), new(0.64f, 0.945f), new(0.585f, 0.915f), new(0.56f, 0.80f)],
            FogLampX = 0.56f,
            FogLampY = 0.265f,
            FrontPlateY = 0.40f,
            RearPlateY = 0.70f,
            ExhaustX = [-0.45f],
            ExhaustY = 0.26f,
            SpoilerLength = 0.10f,
            RubStripY = 0.46f,
            HandleZ = 0.70f,
            HandleY = 0.855f,
            MirrorZ = -0.40f,
            ClothSeats = true,
        };

        /// <summary>
        /// The five-cylinder hatch of 1997, a three-door on the lines of the first Bravo: 4.03 m long and 1.75 m wide,
        /// a long low bonnet over slim lamps, a very raked screen, a beltline that rises to a thick rear pillar and a
        /// steep rear window over the big rounded lamps in the tailgate's corners.
        /// </summary>
        /// <remarks>
        /// Keyed on a three-view drawing of the 1995 three-door (getoutlines.com, CC BY 4.0), calibrated on the
        /// published 4025 mm length and 2540 mm wheelbase (overhangs 875 / 614), measured like the hot hatch. The
        /// nose is at z = -1.825.
        /// </remarks>
        public static readonly BodyStyle FiveHatch = new()
        {
            //         nose       -        -        -    f.axle   cowl   screen  header    roof      -     r.end   glass   g.base  tailgate   -     bumper   tail
            Keys = [-1.825f, -1.745f, -1.575f, -1.275f, -0.95f, -0.655f, -0.275f, 0.145f, 0.725f, 1.375f, 1.795f, 1.945f, 2.075f, 2.12f, 2.145f, 2.17f, 2.204f],
            FloorH = [0.22f, 0.20f, 0.19f, 0.18f, 0.18f, 0.18f, 0.18f, 0.18f, 0.18f, 0.19f, 0.21f, 0.23f, 0.25f, 0.26f, 0.26f, 0.27f, 0.27f],
            HalfWidth = [0.62f, 0.73f, 0.81f, 0.85f, 0.865f, 0.873f, 0.875f, 0.875f, 0.875f, 0.872f, 0.86f, 0.85f, 0.835f, 0.82f, 0.81f, 0.80f, 0.78f],
            WaistH = [0.36f, 0.42f, 0.50f, 0.58f, 0.62f, 0.64f, 0.65f, 0.65f, 0.66f, 0.67f, 0.68f, 0.68f, 0.66f, 0.58f, 0.50f, 0.46f, 0.42f],
            ShoulderH = [0.46f, 0.60f, 0.68f, 0.77f, 0.82f, 0.86f, 0.88f, 0.885f, 0.90f, 0.955f, 0.97f, 0.96f, 0.93f, 0.82f, 0.66f, 0.58f, 0.53f],
            ShoulderW = [0.58f, 0.69f, 0.77f, 0.81f, 0.825f, 0.83f, 0.83f, 0.83f, 0.83f, 0.825f, 0.815f, 0.80f, 0.79f, 0.785f, 0.78f, 0.77f, 0.74f],
            EdgeW = [0.54f, 0.64f, 0.71f, 0.74f, 0.75f, 0.765f, 0.765f, 0.765f, 0.765f, 0.76f, 0.745f, 0.73f, 0.72f, 0.72f, 0.72f, 0.72f, 0.70f],
            EdgeH = [0.475f, 0.62f, 0.705f, 0.795f, 0.845f, 0.88f, 0.895f, 0.90f, 0.915f, 0.97f, 0.985f, 0.98f, 0.95f, 0.83f, 0.67f, 0.59f, 0.54f],
            CrestW = [0.44f, 0.54f, 0.60f, 0.64f, 0.67f, 0.72f, 0.65f, 0.575f, 0.56f, 0.565f, 0.58f, 0.60f, 0.62f, 0.64f, 0.66f, 0.66f, 0.64f],
            CrestH = [0.49f, 0.645f, 0.745f, 0.83f, 0.895f, 0.91f, 1.14f, 1.325f, 1.375f, 1.355f, 1.30f, 1.13f, 0.985f, 0.835f, 0.675f, 0.595f, 0.545f],
            CentreH = [0.50f, 0.655f, 0.76f, 0.85f, 0.915f, 0.975f, 1.19f, 1.36f, 1.419f, 1.396f, 1.335f, 1.155f, 1.00f, 0.84f, 0.68f, 0.60f, 0.55f],
            CowlZ = -0.655f,
            CabinFrom = 0.14f,
            CabinTo = 1.56f,
            ScreenFrom = -0.655f,
            ScreenTo = 0.145f,
            RearGlassFrom = 1.795f,
            RearGlassTo = 2.075f,
            FrontArchTop = 0.655f,
            RearArchTop = 0.655f,
            FrontArchRadius = 0.36f,
            RearArchRadius = 0.36f,
            NoseTrimBelow = 0.30f,
            TailTrimBelow = 0.30f,
            FrontTrack = 0.727f,
            RearTrack = 0.713f,
            FrontTyreWidth = 0.195f,
            RearTyreWidth = 0.195f,
            RimRadius = 0.1905f,
            TopSeams = [-0.68f, 1.78f],
            FlankSeams = [-0.495f, 0.735f],
            RearBulkheadZ = 2.075f,
            EyeZ = 0.55f,
            EyeH = 1.13f,
            LeatherTint = new(0.40f, 0.40f, 0.42f),
            CarpetTint = new(0.14f, 0.14f, 0.15f),
            HeadlinerTint = new(0.60f, 0.58f, 0.55f),
            Hatchback = true,
            RunStep = 0.10f,
            RearSeatZ = 1.15f,
            Headlamp = [new(0.32f, 0.555f), new(0.50f, 0.545f), new(0.66f, 0.555f), new(0.72f, 0.585f), new(0.70f, 0.615f), new(0.56f, 0.615f), new(0.40f, 0.60f), new(0.33f, 0.58f)],
            Grille = [new(-0.27f, 0.555f), new(0f, 0.552f), new(0.27f, 0.555f), new(0.29f, 0.582f), new(0f, 0.592f), new(-0.29f, 0.582f)],
            LowerIntake = [new(0.30f, 0.33f), new(0.56f, 0.33f), new(0.58f, 0.37f), new(0.56f, 0.41f), new(0.30f, 0.41f)],
            TailLamp = [new(0.58f, 0.60f), new(0.70f, 0.59f), new(0.775f, 0.63f), new(0.785f, 0.74f), new(0.77f, 0.84f), new(0.72f, 0.88f), new(0.64f, 0.86f), new(0.59f, 0.78f), new(0.575f, 0.68f)],
            FogLampX = 0.66f,
            FogLampY = 0.37f,
            FrontPlateY = 0.37f,
            RearPlateY = 0.40f,
            ExhaustX = [-0.50f],
            ExhaustY = 0.26f,
            SpoilerLength = 0.08f,
            RubStripY = 0.46f,
            HandleZ = 0.62f,
            HandleY = 0.86f,
            MirrorZ = -0.38f,
            ClothSeats = true,
        };

        /// <summary>
        /// The turbo hatch of 2008, a five-door on the lines of the second Leon: 4.32 m long and 1.77 m wide, a nose
        /// with swept lamps over a wide intake, a screen raked like a coupe's, a strong crease along the flanks under
        /// a beltline that climbs to a short rear side window, the rear door handles hidden by the glass, a roof
        /// spoiler over a sloping rear window and lamps wrapped round the tailgate's shoulders.
        /// </summary>
        /// <remarks>
        /// Keyed on a three-view drawing of the 2007 car (getoutlines.com, CC BY 4.0), calibrated on the published
        /// 4315 mm length and 2578 mm wheelbase (overhangs 940 / 795), measured like the hot hatch. The nose is at
        /// z = -1.92.
        /// </remarks>
        public static readonly BodyStyle TurboHatch = new()
        {
            //         nose       -        -        -    f.axle   cowl   screen  header    roof      -     r.end   glass   g.base  tailgate   -     bumper   tail
            Keys = [-1.92f, -1.84f, -1.67f, -1.37f, -0.98f, -0.75f, -0.34f, 0.10f, 0.78f, 1.43f, 1.86f, 2.01f, 2.15f, 2.25f, 2.29f, 2.34f, 2.395f],
            FloorH = [0.22f, 0.20f, 0.19f, 0.19f, 0.19f, 0.19f, 0.19f, 0.19f, 0.19f, 0.20f, 0.22f, 0.24f, 0.26f, 0.27f, 0.27f, 0.27f, 0.27f],
            HalfWidth = [0.66f, 0.76f, 0.835f, 0.865f, 0.88f, 0.884f, 0.884f, 0.884f, 0.884f, 0.884f, 0.875f, 0.86f, 0.845f, 0.835f, 0.83f, 0.82f, 0.80f],
            WaistH = [0.40f, 0.48f, 0.58f, 0.66f, 0.70f, 0.71f, 0.72f, 0.72f, 0.73f, 0.74f, 0.75f, 0.75f, 0.74f, 0.66f, 0.56f, 0.50f, 0.46f],
            ShoulderH = [0.53f, 0.62f, 0.75f, 0.86f, 0.92f, 0.96f, 1.0f, 1.015f, 1.04f, 1.075f, 1.08f, 1.07f, 1.04f, 0.96f, 0.74f, 0.66f, 0.60f],
            ShoulderW = [0.62f, 0.72f, 0.80f, 0.83f, 0.84f, 0.84f, 0.835f, 0.83f, 0.83f, 0.83f, 0.825f, 0.815f, 0.805f, 0.80f, 0.80f, 0.79f, 0.76f],
            EdgeW = [0.58f, 0.67f, 0.75f, 0.77f, 0.78f, 0.785f, 0.78f, 0.775f, 0.775f, 0.77f, 0.76f, 0.745f, 0.735f, 0.735f, 0.735f, 0.735f, 0.70f],
            EdgeH = [0.545f, 0.635f, 0.78f, 0.89f, 0.945f, 0.985f, 1.02f, 1.035f, 1.06f, 1.095f, 1.10f, 1.09f, 1.06f, 0.97f, 0.745f, 0.67f, 0.61f],
            CrestW = [0.46f, 0.57f, 0.64f, 0.68f, 0.71f, 0.74f, 0.67f, 0.585f, 0.565f, 0.57f, 0.585f, 0.61f, 0.63f, 0.66f, 0.67f, 0.68f, 0.65f],
            CrestH = [0.555f, 0.645f, 0.80f, 0.915f, 0.965f, 1.005f, 1.20f, 1.39f, 1.41f, 1.40f, 1.355f, 1.195f, 1.10f, 0.975f, 0.748f, 0.675f, 0.615f],
            CentreH = [0.56f, 0.65f, 0.81f, 0.93f, 0.98f, 1.04f, 1.235f, 1.432f, 1.458f, 1.445f, 1.39f, 1.22f, 1.11f, 0.98f, 0.75f, 0.68f, 0.62f],
            CowlZ = -0.75f,
            CabinFrom = 0.095f,
            CabinTo = 1.62f,
            ScreenFrom = -0.75f,
            ScreenTo = 0.10f,
            RearGlassFrom = 1.86f,
            RearGlassTo = 2.15f,
            FrontArchTop = 0.71f,
            RearArchTop = 0.71f,
            FrontArchRadius = 0.39f,
            RearArchRadius = 0.39f,
            NoseTrimBelow = 0.28f,
            TailTrimBelow = 0.30f,
            FrontTrack = 0.767f,
            RearTrack = 0.753f,
            FrontTyreWidth = 0.225f,
            RearTyreWidth = 0.225f,
            RimRadius = 0.228f,
            TopSeams = [-0.775f, 1.845f],
            FlankSeams = [-0.50f, 0.555f, 1.41f],
            RearBulkheadZ = 2.15f,
            EyeZ = 0.45f,
            EyeH = 1.17f,
            LeatherTint = BlackLeather,
            CarpetTint = new(0.10f, 0.10f, 0.11f),
            HeadlinerTint = new(0.36f, 0.36f, 0.38f),
            Hatchback = true,
            RunStep = 0.10f,
            RearSeatZ = 1.18f,
            Headlamp = [new(0.36f, 0.66f), new(0.48f, 0.665f), new(0.62f, 0.69f), new(0.74f, 0.73f), new(0.79f, 0.77f), new(0.77f, 0.80f), new(0.66f, 0.79f), new(0.52f, 0.76f), new(0.40f, 0.72f), new(0.35f, 0.69f)],
            Grille = [new(-0.23f, 0.56f), new(0f, 0.555f), new(0.23f, 0.56f), new(0.29f, 0.70f), new(0f, 0.715f), new(-0.29f, 0.70f)],
            LowerIntake = [new(0f, 0.26f), new(0.50f, 0.26f), new(0.58f, 0.32f), new(0.55f, 0.42f), new(0.30f, 0.43f), new(0f, 0.43f)],
            TailLamp = [new(0.48f, 0.90f), new(0.60f, 0.86f), new(0.75f, 0.85f), new(0.815f, 0.88f), new(0.825f, 0.94f), new(0.78f, 0.99f), new(0.62f, 1.0f), new(0.50f, 0.97f)],
            FogLampX = 0.63f,
            FogLampY = 0.33f,
            FrontPlateY = 0.495f,
            RearPlateY = 0.52f,
            ExhaustX = [-0.36f, -0.48f],
            ExhaustY = 0.27f,
            SpoilerLength = 0.18f,
            PillarsZ = [0.555f, 1.41f],
            HandleZ = 0.40f,
            HandleY = 0.93f,
            MirrorZ = -0.45f,
            PaintedMirrors = true,
            SatinStrip = true,
            ClothSeats = true,
        };
    }

    // ---- the skin, for laying details on it -----------------------------------------------------------

    /// <summary>Sections every centimetre from the nose to the tail, built once per car for the details.</summary>
    private Vector3[][] Skin(VehicleSpec spec)
    {
        if (_skin is not null) return _skin;
        var style = _style;
        var count = (int)MathF.Ceiling((style.Keys[^1] - style.Keys[0]) / SkinStep) + 1;
        _skin = new Vector3[count][];
        for (var i = 0; i < count; i++) _skin[i] = Ring(StationAt(spec, MathF.Min(style.Keys[0] + i * SkinStep, style.Keys[^1])));
        _section = new Vector2[_skin[0].Length];
        return _skin;
    }

    /// <summary>
    /// The section at <paramref name="z"/> in the x-y plane, interpolated point by point between the cached ones
    /// (every ring has the same points). The result is a shared buffer, valid until the next call.
    /// </summary>
    private Vector2[] SectionAt(VehicleSpec spec, float z)
    {
        var skin = Skin(spec);
        var f = Math.Clamp((z - _style.Keys[0]) / SkinStep, 0f, skin.Length - 1.001f);
        var i = (int)f;
        var t = f - i;
        var a = skin[i];
        var b = skin[i + 1];
        var section = _section!;
        for (var k = 0; k < section.Length; k++) section[k] = new Vector2(Mathf.Lerp(a[k].X, b[k].X, t), Mathf.Lerp(a[k].Y, b[k].Y, t));
        return section;
    }

    private bool InsideSection(VehicleSpec spec, float z, float x, float y)
    {
        // Even-odd rule over the section's polygon.
        var section = SectionAt(spec, z);
        var inside = false;
        for (int i = 0, j = section.Length - 1; i < section.Length; j = i++)
        {
            var a = section[i];
            var b = section[j];
            if ((a.Y > y) != (b.Y > y) && x < (b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }

    /// <summary>
    /// Where the line along z through (x, y) first meets the skin, coming from the nose or from the tail. The
    /// sections grow from the nose and shrink to the tail, so the point lies outside them up to one z and inside
    /// after it; a point inside the end section is on the end face itself.
    /// </summary>
    private float SkinZ(VehicleSpec spec, float x, float y, bool fromNose)
    {
        var end = fromNose ? _style.Keys[0] : _style.Keys[^1];
        if (InsideSection(spec, end, x, y)) return end;
        var step = fromNose ? 0.02f : -0.02f;
        var outside = end;
        for (var i = 1; i <= 70; i++)
        {
            var inside = end + step * i;
            if (!InsideSection(spec, inside, x, y))
            {
                outside = inside;
                continue;
            }
            for (var k = 0; k < 14; k++)
            {
                var mid = (outside + inside) * 0.5f;
                if (InsideSection(spec, mid, x, y)) inside = mid;
                else outside = mid;
            }
            return (outside + inside) * 0.5f;
        }
        return end;
    }

    /// <summary>The skin seen from the right at <paramref name="z"/>: the outermost x of the section at height <paramref name="y"/>.</summary>
    private float SkinX(VehicleSpec spec, float z, float y)
    {
        var section = SectionAt(spec, z);
        var best = 0f;
        for (int i = 0, j = section.Length - 1; i < section.Length; j = i++)
        {
            var a = section[i];
            var b = section[j];
            if ((a.Y > y) == (b.Y > y)) continue;
            best = MathF.Max(best, a.X + (b.X - a.X) * (y - a.Y) / (b.Y - a.Y));
        }
        return best;
    }

    /// <summary>
    /// The points of an outline seen from an end, filled with rings shrinking to its centre (the outline is drawn
    /// star-shaped about it) and carried along z onto the skin: grid[ring, point], ring 0 the rim. Each edge is cut
    /// in three so the rim follows the skin's curvature.
    /// </summary>
    private Vector3[,] EndGrid(VehicleSpec spec, Vector2[] outline, bool fromNose, float scale = 1f)
    {
        const int rings = 3;
        const int cuts = 3;
        var points = new Vector2[outline.Length * cuts];
        for (var i = 0; i < outline.Length; i++)
        {
            for (var c = 0; c < cuts; c++) points[i * cuts + c] = outline[i].Lerp(outline[(i + 1) % outline.Length], (float)c / cuts);
        }
        var centre = Vector2.Zero;
        foreach (var p in points) centre += p;
        centre /= points.Length;
        var grid = new Vector3[rings + 1, points.Length];
        for (var r = 0; r <= rings; r++)
        {
            // The innermost ring is a small copy, not a point, so every quad keeps its area.
            var t = scale * MathF.Max(1f - (float)r / rings, 0.08f);
            for (var i = 0; i < points.Length; i++)
            {
                var q = centre + (points[i] - centre) * t;
                grid[r, i] = new Vector3(q.X, q.Y, SkinZ(spec, MathF.Abs(q.X), q.Y, fromNose));
            }
        }
        return grid;
    }

    /// <summary>
    /// A mesh over a grid of skin points (rows of points closed into loops when <paramref name="closed"/>, else
    /// open strips), lifted off the skin by <paramref name="lift"/> along its normal; <paramref name="facing"/> is
    /// the side the patch faces (-z for the nose, +z for the tail, +x for the right flank), used to orient the
    /// normals. <paramref name="mirror"/> draws it on the left. A centre cap closes a loop grid's innermost ring.
    /// </summary>
    private static ArrayMesh SkinMesh(Vector3[,] grid, bool closed, Vector3 facing, float lift, bool mirror)
    {
        var rows = grid.GetLength(0);
        var cols = grid.GetLength(1);
        var flip = new Vector3(mirror ? -1f : 1f, 1f, 1f);
        var view = facing * flip;
        var points = new Vector3[rows, cols];
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < cols; c++) points[r, c] = grid[r, c] * flip;
        }
        // Vertex normals from the faces around each point, turned to face the viewer's side.
        var normals = new Vector3[rows, cols];
        var quads = closed ? cols : cols - 1;
        for (var r = 0; r < rows - 1; r++)
        {
            for (var c = 0; c < quads; c++)
            {
                var c2 = (c + 1) % cols;
                var n = (points[r + 1, c2] - points[r, c]).Cross(points[r + 1, c] - points[r, c2]);
                if (n.Dot(view) < 0f) n = -n;
                normals[r, c] += n;
                normals[r, c2] += n;
                normals[r + 1, c] += n;
                normals[r + 1, c2] += n;
            }
        }
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < cols; c++)
            {
                var n = normals[r, c];
                n = n.LengthSquared() < 1e-12f ? view : n.Normalized();
                normals[r, c] = n;
                points[r, c] += n * lift;
            }
        }
        var st = new MeshBuilder();
        for (var r = 0; r < rows - 1; r++)
        {
            for (var c = 0; c < quads; c++)
            {
                var c2 = (c + 1) % cols;
                var outward = normals[r, c] + normals[r, c2] + normals[r + 1, c] + normals[r + 1, c2];
                Tri(st, points[r, c], normals[r, c], points[r + 1, c], normals[r + 1, c], points[r, c2], normals[r, c2], Colors.White, outward);
                Tri(st, points[r, c2], normals[r, c2], points[r + 1, c], normals[r + 1, c], points[r + 1, c2], normals[r + 1, c2], Colors.White, outward);
            }
        }
        if (closed)
        {
            var centre = Vector3.Zero;
            var normal = Vector3.Zero;
            for (var c = 0; c < cols; c++)
            {
                centre += points[rows - 1, c];
                normal += normals[rows - 1, c];
            }
            centre /= cols;
            normal = normal.Normalized();
            for (var c = 0; c < cols; c++)
            {
                var c2 = (c + 1) % cols;
                Tri(st, centre, normal, points[rows - 1, c], normals[rows - 1, c], points[rows - 1, c2], normals[rows - 1, c2], Colors.White, normal);
            }
        }
        return st.Commit();
    }

    /// <summary>
    /// A band on the right flank from <paramref name="z0"/> to <paramref name="z1"/> between the heights the two
    /// functions give, carried in from the side onto the skin: the rubbing strip, the pillars over the side glass.
    /// </summary>
    private Vector3[,] SideGrid(VehicleSpec spec, float z0, float z1, Func<float, float> bottom, Func<float, float> top)
    {
        const int across = 3;
        var steps = Math.Max(2, (int)MathF.Ceiling((z1 - z0) / 0.05f));
        var grid = new Vector3[across + 1, steps + 1];
        for (var s = 0; s <= steps; s++)
        {
            var z = z0 + (z1 - z0) * s / steps;
            for (var a = 0; a <= across; a++)
            {
                var y = Mathf.Lerp(bottom(z), top(z), (float)a / across);
                grid[a, s] = new Vector3(SkinX(spec, z, y), y, z);
            }
        }
        return grid;
    }

    // ---- details ---------------------------------------------------------------------------------------

    /// <summary>
    /// A hatchback's outside: lamps, grille and intake laid on the nose, tail lamps on the tailgate, plates, fog
    /// lamps, tailpipes, roof spoiler, rear wiper, rubbing strip, pillars over the side glass, door handles and
    /// the door mirrors (on the body: the driver sees them through the side windows).
    /// </summary>
    private void BuildHatchDetails(Node3D body, VehicleSpec spec, Material paint, Material trim, Material chrome)
    {
        var style = _style;
        var outside = new Node3D { Name = "Outside" };
        body.AddChild(outside);
        _outsideOnly.Add(outside);
        BuildWheelWells(outside, spec, trim);

        var reflector = new StandardMaterial3D { AlbedoColor = new Color(0.80f, 0.81f, 0.83f), Metallic = 1f, Roughness = 0.12f };
        // Clear glass over the reflector: mostly the sky's reflection, the chrome showing through it.
        var lens = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.86f, 0.90f, 0.95f, 0.22f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Metallic = 0.4f,
            Roughness = 0.03f,
        };
        var bulb = new StandardMaterial3D { AlbedoColor = new Color(1f, 1f, 0.96f), EmissionEnabled = true, Emission = new Color(0.92f, 0.94f, 1f), EmissionEnergyMultiplier = 0.7f };
        var red = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.03f, 0.03f), Metallic = 0.3f, Roughness = 0.08f, EmissionEnabled = true, Emission = new Color(0.85f, 0.05f, 0.03f), EmissionEnergyMultiplier = 0.8f };
        var brake = new StandardMaterial3D { AlbedoColor = new Color(0.75f, 0.06f, 0.04f), Roughness = 0.1f, EmissionEnabled = true, Emission = new Color(1f, 0.08f, 0.04f), EmissionEnergyMultiplier = 1.5f };
        var gloss = new StandardMaterial3D { AlbedoColor = new Color(0.025f, 0.025f, 0.03f), Metallic = 0.2f, Roughness = 0.18f };
        var plate = new StandardMaterial3D { AlbedoColor = new Color(0.92f, 0.92f, 0.9f), Roughness = 0.5f };

        void Add(Node3D parent, ArrayMesh mesh, Material material, string name) =>
            parent.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = material, Name = name });

        // Headlamps: the chrome reflector on the skin and the lens 8 mm proud of it, two round lamps behind the lens.
        if (style.Headlamp.Length > 0)
        {
            var grid = EndGrid(spec, style.Headlamp, fromNose: true);
            var minX = style.Headlamp.Min(p => p.X);
            var maxX = style.Headlamp.Max(p => p.X);
            var midY = (style.Headlamp.Min(p => p.Y) + style.Headlamp.Max(p => p.Y)) * 0.5f;
            var radius = float.IsNaN(style.HeadlampBulbRadius) ? MathF.Min(0.045f, (style.Headlamp.Max(p => p.Y) - style.Headlamp.Min(p => p.Y)) * 0.32f) : style.HeadlampBulbRadius;
            foreach (var mirror in new[] { false, true })
            {
                Add(outside, SkinMesh(grid, true, Vector3.Forward, 0.002f, mirror), reflector, "Headlamp");
                Add(outside, SkinMesh(grid, true, Vector3.Forward, 0.008f, mirror), lens, "HeadlampLens");
                for (var lampIndex = 0; lampIndex < style.HeadlampBulbs; lampIndex++)
                {
                    var along = style.HeadlampBulbs == 1 ? 0.5f : 0.3f + 0.4f * lampIndex / (style.HeadlampBulbs - 1);
                    var x = Mathf.Lerp(minX, maxX, along);
                    var z = SkinZ(spec, x, midY, true);
                    outside.AddChild(new MeshInstance3D
                    {
                        Mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = 0.012f, Material = bulb, RadialSegments = 20 },
                        Position = new Vector3(mirror ? -x : x, midY, z - 0.002f),
                        RotationDegrees = new Vector3(90f, 0f, 0f),
                    });
                }
            }
        }
        if (style.Grille.Length > 0) Add(outside, SkinMesh(EndGrid(spec, style.Grille, true), true, Vector3.Forward, 0.003f, false), gloss, "Grille");
        if (style.LowerIntake.Length > 0)
        {
            var intake = EndGrid(spec, style.LowerIntake, true);
            foreach (var mirror in new[] { false, true }) Add(outside, SkinMesh(intake, true, Vector3.Forward, 0.003f, mirror), gloss, "Intake");
        }
        if (!float.IsNaN(style.FogLampX))
        {
            foreach (var side in new[] { -1f, 1f })
            {
                var z = SkinZ(spec, style.FogLampX, style.FogLampY, true);
                var position = new Vector3(side * style.FogLampX, style.FogLampY, z - 0.004f);
                outside.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.05f, Height = 0.012f, Material = gloss, RadialSegments = 20 }, Position = position, RotationDegrees = new Vector3(90f, 0f, 0f) });
                outside.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.038f, BottomRadius = 0.038f, Height = 0.012f, Material = bulb, RadialSegments = 20 }, Position = position + new Vector3(0f, 0f, -0.004f), RotationDegrees = new Vector3(90f, 0f, 0f) });
            }
        }
        var noseZ = SkinZ(spec, 0f, style.FrontPlateY, true);
        Box(outside, new Vector3(0.52f, 0.11f, 0.008f), new Vector3(0f, style.FrontPlateY, noseZ - 0.006f), plate);

        // Tail lamps: the red lens on the tailgate's corners, a brighter core inside it.
        if (style.TailLamp.Length > 0)
        {
            var grid = EndGrid(spec, style.TailLamp, fromNose: false);
            var core = EndGrid(spec, style.TailLamp, fromNose: false, scale: 0.55f);
            foreach (var mirror in new[] { false, true })
            {
                Add(outside, SkinMesh(grid, true, Vector3.Back, 0.003f, mirror), red, "TailLamp");
                Add(outside, SkinMesh(core, true, Vector3.Back, 0.005f, mirror), brake, "BrakeLamp");
            }
        }
        var tailZ = SkinZ(spec, 0f, style.RearPlateY, false);
        Box(outside, new Vector3(0.52f, 0.11f, 0.008f), new Vector3(0f, style.RearPlateY, tailZ + 0.006f), plate);
        var dark = new StandardMaterial3D { AlbedoColor = new Color(0.02f, 0.02f, 0.02f), Roughness = 0.9f };
        foreach (var x in style.ExhaustX)
        {
            var end = style.Keys[^1];
            outside.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.042f, BottomRadius = 0.042f, Height = 0.16f, Material = chrome, RadialSegments = 16 }, Position = new Vector3(x, style.ExhaustY, end - 0.04f), RotationDegrees = new Vector3(90f, 0f, 0f) });
            outside.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.034f, BottomRadius = 0.034f, Height = 0.005f, Material = dark, RadialSegments = 16 }, Position = new Vector3(x, style.ExhaustY, end + 0.041f), RotationDegrees = new Vector3(90f, 0f, 0f) });
        }

        // Roof spoiler over the rear window, continuing the roof a little flatter than the glass falls away.
        if (style.SpoilerLength > 0f)
        {
            var from = style.RearGlassFrom;
            var width = 2f * style.Sample(style.CrestW, from) + 0.04f;
            var spoiler = Box(outside, new Vector3(width, 0.025f, style.SpoilerLength), new Vector3(0f, style.Sample(style.CentreH, from) + 0.006f, from + style.SpoilerLength * 0.5f - 0.03f), paint);
            spoiler.Rotation = new Vector3(0.12f, 0f, 0f);
        }
        // Rear wiper parked along the foot of the rear window (not on a saloon's).
        if (style.RearWiper)
        {
            var z = style.RearGlassTo - 0.05f;
            var fall = (style.Sample(style.CentreH, z - 0.05f) - style.Sample(style.CentreH, z + 0.05f)) / 0.1f;
            var blade = Box(outside, new Vector3(0.36f, 0.012f, 0.02f), new Vector3(-0.10f, style.Sample(style.CentreH, z) + 0.012f, z), trim);
            blade.Rotation = new Vector3(MathF.Atan(fall), 0f, 0f);
        }

        // Roof rails along the roof's edges on the estate: a bar on a foot at each end and one in the middle.
        if (style.RoofRails)
        {
            var from = style.CabinFrom + 0.20f;
            var to = style.RearGlassFrom - 0.10f;
            var satinRail = new StandardMaterial3D { AlbedoColor = new Color(0.62f, 0.63f, 0.65f), Metallic = 0.8f, Roughness = 0.35f };
            foreach (var side in new[] { -1f, 1f })
            {
                const int pieces = 8;
                for (var p = 0; p < pieces; p++)
                {
                    var z0 = from + (to - from) * p / pieces;
                    var z1 = from + (to - from) * (p + 1) / pieces;
                    var y0 = style.Sample(style.CrestH, z0) + 0.055f;
                    var y1 = style.Sample(style.CrestH, z1) + 0.055f;
                    var x = side * (style.Sample(style.CrestW, (z0 + z1) * 0.5f) - 0.04f);
                    var rail = Box(outside, new Vector3(0.03f, 0.025f, MathF.Sqrt((z1 - z0) * (z1 - z0) + (y1 - y0) * (y1 - y0)) + 0.002f), new Vector3(x, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f), satinRail);
                    rail.Rotation = new Vector3(-MathF.Atan2(y1 - y0, z1 - z0), 0f, 0f);
                }
                foreach (var z in new[] { from + 0.02f, (from + to) * 0.5f, to - 0.02f })
                {
                    Box(outside, new Vector3(0.04f, 0.06f, 0.07f), new Vector3(side * (style.Sample(style.CrestW, z) - 0.04f), style.Sample(style.CrestH, z) + 0.028f, z), trim);
                }
            }
        }

        // Along the flanks: the rubbing strip between the arches, the black pillars over the side glass, the handles.
        if (!float.IsNaN(style.RubStripY))
        {
            var strip = SideGrid(spec, -spec.FrontAxle + style.FrontArchRadius + 0.06f, spec.RearAxle - style.RearArchRadius - 0.06f, _ => style.RubStripY - 0.022f, _ => style.RubStripY + 0.022f);
            foreach (var mirror in new[] { false, true }) Add(outside, SkinMesh(strip, false, Vector3.Right, 0.004f, mirror), trim, "RubStrip");
        }
        foreach (var pillarZ in style.PillarsZ)
        {
            var pillar = SideGrid(spec, pillarZ - 0.045f, pillarZ + 0.045f, z => style.Sample(style.EdgeH, z) + 0.004f, z => style.Sample(style.CrestH, z) - 0.004f);
            foreach (var mirror in new[] { false, true }) Add(outside, SkinMesh(pillar, false, Vector3.Right, 0.004f, mirror), gloss, "Pillar");
        }
        foreach (var side in new[] { -1f, 1f })
        {
            var x = SkinX(spec, style.HandleZ, style.HandleY);
            Box(outside, new Vector3(0.03f, 0.032f, 0.15f), new Vector3(side * (x + 0.008f), style.HandleY, style.HandleZ), trim);
        }

        // Door mirrors at the front corner of the side glass: a black foot on the door's top, a rounded housing (black
        // plastic on the older cars, paint on the turbo hatch) and the glass on its rear face, towards the driver.
        // About 0.97 m out from the centre line, as wide as the published widths with mirrors. From the driver's seat
        // the housing's back is what shows through the side glass: in paint, with a highlight, it read as a red ball.
        var mirrorGlass = new StandardMaterial3D { AlbedoColor = new Color(0.75f, 0.77f, 0.8f), Metallic = 1f, Roughness = 0.04f };
        var housingMaterial = style.PaintedMirrors ? paint : new StandardMaterial3D { AlbedoColor = new Color(0.05f, 0.05f, 0.055f), Roughness = 0.6f };
        foreach (var side in new[] { -1f, 1f })
        {
            var z = style.MirrorZ;
            var belt = style.Sample(style.EdgeH, z);
            var glassX = style.Sample(style.EdgeW, z);
            _mirrorParts.Add(Box(body, new Vector3(0.07f, 0.05f, 0.09f), new Vector3(side * (glassX + 0.03f), belt + 0.025f, z), trim));
            var housing = new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 0.5f, Height = 1f, Material = housingMaterial, RadialSegments = 20, Rings = 10 },
                Position = new Vector3(side * (glassX + 0.125f), belt + 0.065f, z),
                Scale = new Vector3(0.19f, 0.115f, 0.075f),
            };
            body.AddChild(housing);
            var face = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 0.004f, Material = mirrorGlass, RadialSegments = 20 },
                Position = new Vector3(side * (glassX + 0.125f), belt + 0.065f, z + 0.04f),
                Rotation = new Vector3(Mathf.Pi / 2f, 0f, 0f),
                Scale = new Vector3(0.165f, 1f, 0.09f),
            };
            body.AddChild(face);
            _mirrorParts.Add(housing);
            _mirrorParts.Add(face);
        }
    }

    /// <summary>
    /// A hatchback's rear bench and its parcel shelf, which spans from the backrest to the tailgate at the foot of
    /// the rear window: from behind, through the glass, it closes the boot.
    /// </summary>
    private void BuildRearSeats(Node3D body, Material leather, Material carpet)
    {
        var style = _style;
        var z = style.RearSeatZ;
        Box(body, new Vector3(1.24f, 0.16f, 0.46f), new Vector3(0f, 0.47f, z), leather);
        var backrest = Box(body, new Vector3(1.24f, 0.56f, 0.10f), new Vector3(0f, 0.77f, z + 0.29f), leather);
        backrest.Rotation = new Vector3(0.25f, 0f, 0f);
        var shelfFrom = z + 0.38f;
        var shelfTo = style.RearGlassTo - 0.02f;
        var shelfY = style.Sample(style.CentreH, style.RearGlassTo) - 0.05f;
        var shelfHalf = style.Sample(style.EdgeW, shelfTo) - 0.03f;
        Box(body, new Vector3(2f * shelfHalf, 0.02f, shelfTo - shelfFrom), new Vector3(0f, shelfY, (shelfFrom + shelfTo) * 0.5f), carpet);
    }
}

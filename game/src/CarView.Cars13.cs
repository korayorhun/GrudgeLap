using Godot;
using GrudgeLap.Physics.Vehicle;

namespace GrudgeLap.Game;

/// <summary>
/// The lofted bodies of the cars13 cars that no CC-licensed model of was found (cars13 bodies): the first Fiat Bravo
/// (type 182, the three-door of 1995-2001) as the HGT and the 1.6 16V, and the third SEAT Leon (5F) in its 2017
/// facelift as the five-door Cupra. They are built like the hatchbacks (CarView.Hatchbacks.cs) from their published
/// dimensions and CC BY dimensioned drawings, with what tells each from the hatch it stood in for until now: the shape of
/// its side glass (laid over the loft's glass strip as painted or black covers), its lamps, intakes, diffuser and
/// wheels, and inside the Leon's high centre screen, flat-bottomed wheel and bucket seats.
/// </summary>
internal sealed partial class CarView
{
    internal sealed partial class BodyStyle
    {
        /// <summary>
        /// Side-view polygons (z, y) laid over the side glass in paint: the glass strip runs from the belt to the roof's
        /// edge between the cabin's ends, and a real car's window is another shape (the Bravo's quarter window comes to
        /// a point at the back; the Leon's window line kicks up into its C-pillar). Each is drawn between its lowest and
        /// highest point at every z, held to the glass strip, and lined inside so the driver does not see through it.
        /// </summary>
        public Vector2[][] PaintedCovers = [];
        /// <summary>The same in gloss black: the sail panels at the foot of the A-pillars.</summary>
        public Vector2[][] BlackCovers = [];
        /// <summary>The radius the shoulder's fillet rolls the crease over: a sharp crease reads as the Leon's.</summary>
        public float ShoulderFillet = 0.05f;
        /// <summary>The right-hand corner intake of the front bumper seen from the front (mirrored to the left).</summary>
        public Vector2[] CornerIntake = [];
        /// <summary>The right half of the rear diffuser seen from behind (mirrored), in gloss black.</summary>
        public Vector2[] RearDiffuser = [];
        /// <summary>The LED running light along the headlamp, seen from the front (right-hand lamp).</summary>
        public Vector2[] HeadlampLed = [];
        /// <summary>How brightly the tail lamps glow by day (1: the hatchbacks' lit lamps; the cars13 lofts' are unlit, dark red).</summary>
        public float TailLampGlow = 1f;
        /// <summary>A clear lens under the tail lamp (the Bravo's reversing lamp and indicator).</summary>
        public Vector2[] TailLampLower = [];
        /// <summary>The tailgate's handle across its middle, seen from behind, in satin (the Bravo's "smile").</summary>
        public Vector2[] TailHandle = [];
        /// <summary>Bands across the nose and the tail seen from the ends (the Bravo's rubbing strips round its bumpers), in black.</summary>
        public Vector2[] FrontStrip = [];
        public Vector2[] RearStrip = [];
        /// <summary>Body-colour side skirts along the sills and a lip under the front bumper (the Bravo HGT's kit).</summary>
        public bool SideSkirts;
        public bool FrontLip;
        /// <summary>The rear door's handle on a five-door; NaN for a three-door.</summary>
        public float RearHandleZ = float.NaN;
        /// <summary>The alloy wheel's spokes, single or in pairs, and the alloy's colour.</summary>
        public int Spokes = 5;
        public bool TwinSpokes;
        public Color AlloyTint = new(0.76f, 0.76f, 0.78f);
        /// <summary>The brake calipers' colour behind the alloys.</summary>
        public Color CaliperTint = new(0.75f, 0.08f, 0.05f);
        /// <summary>A touch screen standing on top of the centre stack, angled to the driver (the Leon's).</summary>
        public bool CentreScreen;
        /// <summary>A steering wheel flattened at the bottom (the Cupra's).</summary>
        public bool FlatBottomWheel;
        /// <summary>Bucket seats: deep bolsters and a high backrest with the headrest moulded in.</summary>
        public bool SportSeats;

        /// <summary>
        /// The Fiat Bravo of 1995-2001 (type 182), the three-door: 4.03 m long and 1.755 m wide, a smooth nose with slim
        /// lamps either side of a narrow grille, a very raked screen, a wedge waistline climbing to a thick C-pillar and a
        /// long quarter window that comes to a point at its back, a steep rear window over big oval lamps in the corners
        /// of the tailgate and the "smile" handle between them. <paramref name="hgt"/>: the HGT's body-colour side skirts
        /// and front lip and its 15-inch alloys; the 1.6 16V on 14-inch steel wheels under covers.
        /// </summary>
        /// <remarks>
        /// Keyed on the 1995 three-door's dimensioned drawing (getoutlines.com, CC BY 4.0; the same as the five-cylinder
        /// hatch's) on the axles that hatch was drawn round, and fitted to the Bravo's (FittedTo). Published: 4025 mm long,
        /// 1755 wide, 1410 (HGT) and 1420 (1.6) high, wheelbase 2540, tracks 1471 / 1430 and 205/50 R15 on the HGT
        /// (auto-data.net), 1439 / 1441 and 185/65 R14 on the 1.6 (cars-data, as VehicleSpec.HatchSixteen: auto-data.net's
        /// 1461 / 1463 drew its wheels 2 cm wider than the simulation's, the cars13 review). Against the five-cylinder hatch: the belt rises from 0.915 m at the door's
        /// front to 1.025 m at the C-pillar (the drawing's door and quarter window; 0.895 to 0.985 before), the lamps are
        /// the drawing's (slim headlamps from the grille to the wing's corner, oval tail lamps at 0.66-0.92 m), and the
        /// side glass takes the drawing's shape: a black sail ahead of the door glass, a black B-pillar, the quarter
        /// window's top falling to a point 1.07 m up at the back. The three-view of 1999 in the logs is the five-door Brava.
        /// </remarks>
        private static BodyStyle Bravo(bool hgt) => new()
        {
            //         nose       -        -        -    f.axle   cowl   screen  header    roof      -     r.end   glass   g.base  tailgate   -     bumper   tail
            Keys = [-1.825f, -1.745f, -1.575f, -1.275f, -0.95f, -0.60f, -0.275f, 0.145f, 0.725f, 1.375f, 1.795f, 1.945f, 2.075f, 2.12f, 2.145f, 2.17f, 2.204f],
            FloorH = [0.22f, 0.20f, 0.19f, 0.18f, 0.18f, 0.18f, 0.18f, 0.18f, 0.18f, 0.19f, 0.21f, 0.23f, 0.25f, 0.26f, 0.26f, 0.27f, 0.27f],
            HalfWidth = [0.62f, 0.73f, 0.81f, 0.85f, 0.865f, 0.873f, 0.8775f, 0.8775f, 0.8775f, 0.874f, 0.86f, 0.85f, 0.835f, 0.82f, 0.81f, 0.80f, 0.78f],
            WaistH = [0.36f, 0.42f, 0.50f, 0.58f, 0.62f, 0.64f, 0.65f, 0.65f, 0.66f, 0.67f, 0.68f, 0.68f, 0.66f, 0.58f, 0.50f, 0.46f, 0.42f],
            ShoulderH = [0.46f, 0.60f, 0.68f, 0.77f, 0.82f, 0.86f, 0.885f, 0.895f, 0.915f, 0.975f, 0.99f, 0.97f, 0.93f, 0.82f, 0.66f, 0.58f, 0.53f],
            ShoulderW = [0.58f, 0.69f, 0.77f, 0.81f, 0.825f, 0.83f, 0.83f, 0.83f, 0.83f, 0.825f, 0.815f, 0.80f, 0.79f, 0.785f, 0.78f, 0.77f, 0.74f],
            EdgeW = [0.54f, 0.64f, 0.71f, 0.74f, 0.75f, 0.765f, 0.765f, 0.765f, 0.765f, 0.76f, 0.745f, 0.73f, 0.72f, 0.72f, 0.72f, 0.72f, 0.70f],
            EdgeH = [0.475f, 0.62f, 0.705f, 0.795f, 0.845f, 0.885f, 0.915f, 0.935f, 0.965f, 1.005f, 1.025f, 1.0f, 0.95f, 0.83f, 0.67f, 0.59f, 0.54f],
            CrestW = [0.44f, 0.54f, 0.60f, 0.64f, 0.67f, 0.72f, 0.65f, 0.575f, 0.56f, 0.565f, 0.58f, 0.60f, 0.62f, 0.64f, 0.66f, 0.66f, 0.64f],
            CrestH = [0.49f, 0.645f, 0.745f, 0.83f, 0.895f, 0.91f, 1.14f, 1.325f, 1.375f, 1.355f, 1.30f, 1.13f, 0.985f, 0.835f, 0.675f, 0.595f, 0.545f],
            CentreH = hgt
                ? [0.50f, 0.655f, 0.76f, 0.85f, 0.915f, 0.975f, 1.185f, 1.35f, 1.41f, 1.387f, 1.327f, 1.15f, 1.00f, 0.84f, 0.68f, 0.60f, 0.55f]
                : [0.50f, 0.655f, 0.76f, 0.85f, 0.915f, 0.975f, 1.19f, 1.36f, 1.419f, 1.396f, 1.335f, 1.155f, 1.00f, 0.84f, 0.68f, 0.60f, 0.55f],
            CowlZ = -0.60f,
            CabinFrom = 0.14f,
            CabinTo = 1.56f,
            ScreenFrom = -0.60f,
            ScreenTo = 0.145f,
            RearGlassFrom = 1.795f,
            RearGlassTo = 2.075f,
            FrontArchTop = 0.655f,
            RearArchTop = 0.655f,
            FrontArchRadius = 0.36f,
            RearArchRadius = 0.36f,
            NoseTrimBelow = hgt ? 0.24f : 0.30f,
            TailTrimBelow = 0.30f,
            KeyedFrontAxle = 0.95f,
            KeyedRearAxle = 1.59f,
            FrontTrack = hgt ? 0.7355f : 0.7195f,
            RearTrack = hgt ? 0.715f : 0.7205f,
            FrontTyreWidth = hgt ? 0.205f : 0.185f,
            RearTyreWidth = hgt ? 0.205f : 0.185f,
            // 15 in alloys on the HGT, 14 in steel wheels under covers on the 1.6 (the SX had the alloys as an option).
            RimRadius = hgt ? 0.1905f : 0.1778f,
            Hubcap = hgt ? null : "vented",
            Spokes = 5,
            TopSeams = [-0.625f, 1.78f],
            FlankSeams = [-0.495f, 0.735f],
            RearBulkheadZ = 2.075f,
            EyeZ = 0.55f,
            EyeH = hgt ? 1.12f : 1.13f,
            LeatherTint = hgt ? new(0.30f, 0.33f, 0.42f) : new(0.40f, 0.40f, 0.42f),
            CarpetTint = new(0.14f, 0.14f, 0.15f),
            HeadlinerTint = new(0.60f, 0.58f, 0.55f),
            DashTint = new(0.22f, 0.22f, 0.23f),
            FasciaTint = new(0.30f, 0.30f, 0.31f),
            Hatchback = true,
            RunStep = 0.10f,
            RearSeatZ = 1.15f,
            // Slim lamps from the grille's ends to the corners of the wings, a narrow grille between them carrying the badge.
            Headlamp = [new(0.185f, 0.56f), new(0.35f, 0.552f), new(0.52f, 0.556f), new(0.64f, 0.578f), new(0.685f, 0.607f), new(0.655f, 0.64f), new(0.52f, 0.655f), new(0.36f, 0.647f), new(0.24f, 0.62f), new(0.185f, 0.592f)],
            Grille = [new(-0.165f, 0.567f), new(0f, 0.565f), new(0.165f, 0.567f), new(0.175f, 0.607f), new(0f, 0.613f), new(-0.175f, 0.607f)],
            LowerIntake = hgt
                ? [new(0f, 0.25f), new(0.46f, 0.25f), new(0.50f, 0.29f), new(0.46f, 0.35f), new(0f, 0.35f)]
                : [new(0.27f, 0.355f), new(0.45f, 0.355f), new(0.46f, 0.39f), new(0.45f, 0.43f), new(0.27f, 0.43f)],
            HeadlampBulbs = 2,
            TailLampGlow = 0.2f,
            // The big oval lamps in the tailgate's corners at the foot of the rear window, the lower lens under each.
            TailLamp = [new(0.49f, 0.70f), new(0.56f, 0.655f), new(0.65f, 0.648f), new(0.725f, 0.69f), new(0.768f, 0.77f), new(0.778f, 0.86f), new(0.75f, 0.925f), new(0.69f, 0.95f), new(0.62f, 0.935f), new(0.55f, 0.88f), new(0.505f, 0.795f)],
            TailLampLower = [new(0.53f, 0.598f), new(0.66f, 0.598f), new(0.665f, 0.638f), new(0.53f, 0.638f)],
            TailHandle = [new(-0.30f, 0.70f), new(0f, 0.69f), new(0.30f, 0.70f), new(0.32f, 0.735f), new(0.26f, 0.755f), new(0f, 0.75f), new(-0.26f, 0.755f), new(-0.32f, 0.735f)],
            FogLampX = 0.60f,
            FogLampY = 0.38f,
            // The dark rubbing strip round the bumpers, at the height of the doors' strip.
            FrontStrip = [new(-0.80f, 0.475f), new(0f, 0.475f), new(0.80f, 0.475f), new(0.80f, 0.515f), new(0f, 0.515f), new(-0.80f, 0.515f)],
            RearStrip = [new(-0.78f, 0.555f), new(0f, 0.555f), new(0.78f, 0.555f), new(0.78f, 0.592f), new(0f, 0.592f), new(-0.78f, 0.592f)],
            FrontPlateY = 0.43f,
            RearPlateY = 0.39f,
            ExhaustX = [-0.50f],
            ExhaustY = 0.26f,
            SpoilerLength = 0.08f,
            RubStripY = 0.46f,
            PillarsZ = [0.705f],
            HandleZ = 0.62f,
            HandleY = 0.86f,
            MirrorZ = -0.38f,
            ClothSeats = true,
            SportSeats = hgt,
            SideSkirts = hgt,
            FrontLip = hgt,
            // Keyed z (FittedTo moves them with the axles): the sail ahead of the door glass, and the C-pillar over the
            // quarter window's back, whose top falls to the point the window ends in.
            BlackCovers = [[new(-0.61f, 0.80f), new(-0.61f, 1.25f), new(-0.33f, 1.25f), new(-0.33f, 0.80f)]],
            PaintedCovers = [[new(1.10f, 1.45f), new(1.10f, 1.372f), new(1.30f, 1.335f), new(1.42f, 1.27f), new(1.50f, 1.18f), new(1.545f, 1.08f), new(1.575f, 1.0f), new(1.60f, 1.0f), new(1.60f, 1.45f)]],
        };

        public static readonly BodyStyle BravoHgt = Bravo(hgt: true);
        public static readonly BodyStyle BravoSixteen = Bravo(hgt: false);

        /// <summary>
        /// The SEAT Leon Cupra of 2017-2020 (5F facelift), the five-door: 4.27 m long, 1.816 m wide and 1.435 m high, a
        /// sharp crease along the flanks from the headlamps to the tail lamps, full-LED headlamps swept back to a point
        /// over the hexagonal grille, the Cupra's bumper with its big intakes, a five-door glasshouse whose window line
        /// kicks up into the C-pillar to a small quarter window, a roof spoiler over the rear window, slim lamps across the
        /// tailgate's shoulders, a diffuser with twin round tailpipes at each side and 19-inch wheels.
        /// </summary>
        /// <remarks>
        /// The side is keyed on getoutlines.com's free side view of the 2013 Leon five-door (blueprint 28850, CC BY 4.0;
        /// the 2017 facelift kept the shell), on the car's own axles (its wheel centres 2631 mm apart, the published
        /// wheelbase): the roofline, the beltline and the window's edges, the shoulder crease, the sills, the arches and
        /// the overhangs read every 5 cm (the drawing gives 4278 mm against the published 4271), and the whole body
        /// lowered 24 mm from the drawing's 1459 mm to the Cupra's published 1435 (its sport suspension; the wheels stay).
        /// Nothing comes from a drawing that is not free (the cars13 review: the loft of cars/bodies was traced from the
        /// preview of a paid one). The widths, the lamps, the grille and the intakes are estimates against the published
        /// 1816 mm and tracks of 1535 / 1510 (auto-data.net) from Wikimedia Commons photographs of 2017-20 Cupras, looked
        /// at for proportions only (a head-on view of a 2019 Cupra 290 by MrWalkr, CC BY-SA 4.0), at the heights the
        /// side view gives; 235/35 R19. The nose is at z = -1.907.
        /// </remarks>
        public static readonly BodyStyle LeonCupra = new()
        {
            //         nose       -        -        -    f.axle   cowl   screen  header    roof      -     r.end   spoiler g.base  tailgate   -     bumper   tail
            Keys = [-1.907f, -1.875f, -1.80f, -1.50f, -1.052f, -0.76f, -0.40f, 0.02f, 0.45f, 1.00f, 1.45f, 1.80f, 2.15f, 2.24f, 2.29f, 2.33f, 2.368f],
            FloorH = [0.239f, 0.209f, 0.195f, 0.18f, 0.177f, 0.177f, 0.177f, 0.177f, 0.177f, 0.177f, 0.18f, 0.19f, 0.233f, 0.24f, 0.26f, 0.30f, 0.36f],
            HalfWidth = [0.60f, 0.73f, 0.83f, 0.893f, 0.905f, 0.908f, 0.908f, 0.908f, 0.908f, 0.908f, 0.906f, 0.90f, 0.89f, 0.88f, 0.87f, 0.855f, 0.81f],
            WaistH = [0.45f, 0.48f, 0.52f, 0.60f, 0.64f, 0.65f, 0.66f, 0.66f, 0.66f, 0.66f, 0.67f, 0.67f, 0.65f, 0.62f, 0.56f, 0.50f, 0.46f],
            ShoulderH = [0.536f, 0.576f, 0.636f, 0.757f, 0.813f, 0.828f, 0.831f, 0.832f, 0.828f, 0.856f, 0.907f, 0.924f, 0.922f, 0.88f, 0.76f, 0.62f, 0.526f],
            ShoulderW = [0.57f, 0.69f, 0.80f, 0.87f, 0.885f, 0.885f, 0.885f, 0.885f, 0.885f, 0.885f, 0.88f, 0.865f, 0.85f, 0.84f, 0.83f, 0.815f, 0.78f],
            EdgeW = [0.52f, 0.63f, 0.74f, 0.79f, 0.80f, 0.79f, 0.785f, 0.78f, 0.78f, 0.775f, 0.765f, 0.745f, 0.73f, 0.725f, 0.72f, 0.71f, 0.69f],
            EdgeH = [0.545f, 0.59f, 0.705f, 0.784f, 0.881f, 0.908f, 0.926f, 0.945f, 0.967f, 0.99f, 1.016f, 1.046f, 0.976f, 0.906f, 0.78f, 0.64f, 0.545f],
            CrestW = [0.42f, 0.53f, 0.62f, 0.69f, 0.72f, 0.74f, 0.69f, 0.62f, 0.61f, 0.61f, 0.60f, 0.60f, 0.63f, 0.65f, 0.67f, 0.68f, 0.66f],
            CrestH = [0.552f, 0.60f, 0.725f, 0.83f, 0.925f, 0.955f, 1.123f, 1.266f, 1.345f, 1.327f, 1.273f, 1.266f, 1.02f, 0.93f, 0.82f, 0.67f, 0.556f],
            CentreH = [0.556f, 0.606f, 0.74f, 0.867f, 0.947f, 0.976f, 1.174f, 1.356f, 1.426f, 1.426f, 1.386f, 1.345f, 1.046f, 0.968f, 0.863f, 0.716f, 0.576f],
            CowlZ = -0.76f,
            CabinFrom = 0.015f,
            CabinTo = 1.60f,
            ScreenFrom = -0.76f,
            ScreenTo = 0.02f,
            RearGlassFrom = 1.82f,
            RearGlassTo = 2.17f,
            FrontArchTop = 0.673f,
            RearArchTop = 0.658f,
            FrontArchRadius = 0.39f,
            RearArchRadius = 0.40f,
            NoseTrimBelow = 0.24f,
            TailTrimBelow = 0.28f,
            FrontTrack = 0.7675f,
            RearTrack = 0.755f,
            FrontTyreWidth = 0.235f,
            RearTyreWidth = 0.235f,
            RimRadius = 0.2413f,
            // The Cupra's 19 in wheels: ten thin spokes in pairs, dark, behind them the red calipers.
            Spokes = 5,
            TwinSpokes = true,
            AlloyTint = new(0.20f, 0.20f, 0.22f),
            ShoulderFillet = 0.008f,
            TopSeams = [-0.775f, 1.84f],
            FlankSeams = [-0.60f, 0.48f, 1.33f],
            RearBulkheadZ = 2.17f,
            EyeZ = 0.42f,
            EyeH = 1.15f,
            LeatherTint = BlackLeather,
            CarpetTint = new(0.10f, 0.10f, 0.11f),
            HeadlinerTint = new(0.20f, 0.20f, 0.21f),
            DashTint = new(0.16f, 0.16f, 0.17f),
            FasciaTint = new(0.22f, 0.22f, 0.23f),
            Hatchback = true,
            RunStep = 0.10f,
            RearSeatZ = 1.15f,
            // The full-LED lamp: a point low by the grille's top corner, swept out and up to the wing, its LED light
            // along its lower edge (the side view's 0.59-0.76 m, the head-on photograph's 0.37-0.83 m out).
            Headlamp = [new(0.37f, 0.635f), new(0.46f, 0.622f), new(0.60f, 0.637f), new(0.72f, 0.667f), new(0.80f, 0.705f), new(0.83f, 0.745f), new(0.80f, 0.768f), new(0.70f, 0.765f), new(0.58f, 0.742f), new(0.46f, 0.705f), new(0.385f, 0.668f)],
            HeadlampLed = [new(0.44f, 0.630f), new(0.60f, 0.645f), new(0.71f, 0.672f), new(0.79f, 0.708f), new(0.795f, 0.722f), new(0.71f, 0.688f), new(0.60f, 0.660f), new(0.44f, 0.645f)],
            HeadlampBulbs = 2,
            HeadlampBulbRadius = 0.026f,
            TailLampGlow = 0.25f,
            // The grille, wide at the top under the bonnet's edge, the plate under it, and under the plate the Cupra's
            // wide lower intake; the intakes at the bumper's corners under the running lights.
            Grille = [new(-0.26f, 0.540f), new(0f, 0.535f), new(0.26f, 0.540f), new(0.35f, 0.675f), new(0f, 0.682f), new(-0.35f, 0.675f)],
            LowerIntake = [new(0f, 0.245f), new(0.47f, 0.245f), new(0.50f, 0.29f), new(0.45f, 0.385f), new(0.30f, 0.39f), new(0f, 0.39f)],
            CornerIntake = [new(0.56f, 0.27f), new(0.80f, 0.285f), new(0.83f, 0.38f), new(0.75f, 0.40f), new(0.60f, 0.39f)],
            // The tail lamps across the body's corners and onto the tailgate, pointed at their inner ends; the diffuser
            // under the bumper with the tailpipes, two at each side.
            TailLamp = [new(0.38f, 0.84f), new(0.48f, 0.815f), new(0.60f, 0.80f), new(0.72f, 0.795f), new(0.81f, 0.80f), new(0.84f, 0.83f), new(0.83f, 0.88f), new(0.78f, 0.905f), new(0.64f, 0.905f), new(0.50f, 0.89f), new(0.41f, 0.865f)],
            RearDiffuser = [new(0f, 0.26f), new(0.80f, 0.26f), new(0.80f, 0.30f), new(0.66f, 0.33f), new(0.50f, 0.40f), new(0.38f, 0.44f), new(0f, 0.44f)],
            FrontPlateY = 0.445f,
            RearPlateY = 0.48f,
            ExhaustX = [-0.66f, -0.555f, 0.555f, 0.66f],
            ExhaustY = 0.30f,
            SpoilerLength = 0.22f,
            PillarsZ = [0.53f, 1.30f],
            HandleZ = 0.35f,
            HandleY = 0.784f,
            RearHandleZ = 1.24f,
            MirrorZ = -0.31f,
            PaintedMirrors = true,
            SatinStrip = true,
            SportSeats = true,
            CentreScreen = true,
            FlatBottomWheel = true,
            Airbag = true,
            // The black sail ahead of the front door glass (the side view's triangle from the windscreen's foot to the door
            // frame); the window line kicking up into the C-pillar and the window's top falling to meet it, as the side
            // view draws them (its heights lowered with the body).
            BlackCovers = [[new(-0.76f, 0.88f), new(-0.76f, 1.10f), new(-0.54f, 1.056f), new(-0.47f, 0.926f), new(-0.47f, 0.88f)]],
            PaintedCovers =
            [
                [new(1.20f, 0.93f), new(1.20f, 1.003f), new(1.25f, 1.010f), new(1.30f, 1.025f), new(1.35f, 1.047f), new(1.40f, 1.071f), new(1.45f, 1.106f), new(1.50f, 1.140f), new(1.55f, 1.178f), new(1.60f, 1.216f), new(1.62f, 1.24f), new(1.62f, 0.93f)],
                [new(1.40f, 1.45f), new(1.40f, 1.281f), new(1.50f, 1.264f), new(1.55f, 1.253f), new(1.60f, 1.24f), new(1.62f, 1.24f), new(1.62f, 1.45f)],
            ],
        };
    }

    /// <summary>
    /// The covers laid over the side glass (<see cref="BodyStyle.PaintedCovers"/>, <see cref="BodyStyle.BlackCovers"/>):
    /// each polygon's span at every z, held between the belt and the roof's edge, carried in onto the skin from the side,
    /// in paint (or gloss black) outside and the door trim's leather inside.
    /// </summary>
    private void BuildGlassCovers(Node3D outside, Node3D body, VehicleSpec spec, Material paint, Material black, Material lining)
    {
        var style = _style;
        foreach (var (covers, material) in new[] { (style.PaintedCovers, paint), (style.BlackCovers, black) })
        {
            foreach (var polygon in covers)
            {
                var from = MathF.Max(polygon.Min(p => p.X), style.CowlZ);
                var to = MathF.Min(polygon.Max(p => p.X), style.CabinTo + 0.02f);
                if (to - from < 0.01f) continue;
                float Edge(float z) => style.Sample(style.EdgeH, z) + 0.003f;
                float Crest(float z) => style.Sample(style.CrestH, z) - 0.003f;
                float Low(float z) => Math.Clamp(Span(polygon, z).Low, Edge(z), Crest(z));
                float High(float z) => Math.Clamp(Span(polygon, z).High, Edge(z), Crest(z));
                var grid = SideGrid(spec, from, to, Low, High);
                foreach (var mirror in new[] { false, true })
                {
                    outside.AddChild(new MeshInstance3D { Mesh = SkinMesh(grid, false, Vector3.Right, 0.004f, mirror), MaterialOverride = material, Name = "GlassCover" });
                    // Inside, the lining's leather a lining's thickness in from the glass: from the seat the shell and the
                    // glass are not drawn, and the road showed through the "pillar".
                    var inner = new MeshInstance3D { Mesh = SkinMesh(grid, false, Vector3.Left, LiningInset, mirror), MaterialOverride = lining, Name = "GlassCoverLining", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                    body.AddChild(inner);
                }
            }
        }
    }

    /// <summary>The lowest and highest y of a polygon's edges at <paramref name="z"/> (its outline in (z, y)).</summary>
    private static (float Low, float High) Span(Vector2[] polygon, float z)
    {
        var low = float.MaxValue;
        var high = float.MinValue;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            var a = polygon[j];
            var b = polygon[i];
            if (z < MathF.Min(a.X, b.X) || z > MathF.Max(a.X, b.X)) continue;
            if (MathF.Abs(b.X - a.X) < 1e-6f)
            {
                // An upright edge: all of it at this z.
                low = MathF.Min(low, MathF.Min(a.Y, b.Y));
                high = MathF.Max(high, MathF.Max(a.Y, b.Y));
                continue;
            }
            var y = a.Y + (b.Y - a.Y) * (z - a.X) / (b.X - a.X);
            low = MathF.Min(low, y);
            high = MathF.Max(high, y);
        }
        return low <= high ? (low, high) : (0f, 0f);
    }

    /// <summary>
    /// The cars13 lofts' own details (cars13 bodies): the corner intakes, the LED running lights, the diffuser, the
    /// Bravo's lower tail lenses and tailgate handle, the HGT's skirts and lip, the rear door handles.
    /// </summary>
    private void BuildCars13Details(Node3D outside, VehicleSpec spec, Material paint, Material trim, Material chrome, Material gloss)
    {
        var style = _style;
        void Add(ArrayMesh mesh, Material material, string name) => outside.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = material, Name = name });
        if (style.CornerIntake.Length > 0)
        {
            var grid = EndGrid(spec, style.CornerIntake, true);
            foreach (var mirror in new[] { false, true }) Add(SkinMesh(grid, true, Vector3.Forward, 0.003f, mirror), gloss, "CornerIntake");
        }
        if (style.HeadlampLed.Length > 0)
        {
            var led = new StandardMaterial3D { AlbedoColor = new Color(0.95f, 0.97f, 1f), EmissionEnabled = true, Emission = new Color(0.90f, 0.94f, 1f), EmissionEnergyMultiplier = 1.6f };
            var grid = EndGrid(spec, style.HeadlampLed, true);
            foreach (var mirror in new[] { false, true }) Add(SkinMesh(grid, true, Vector3.Forward, 0.011f, mirror), led, "HeadlampLed");
        }
        if (style.RearDiffuser.Length > 0)
        {
            var grid = EndGrid(spec, style.RearDiffuser, false);
            foreach (var mirror in new[] { false, true }) Add(SkinMesh(grid, true, Vector3.Back, 0.003f, mirror), gloss, "Diffuser");
        }
        if (style.TailLampLower.Length > 0)
        {
            var clear = new StandardMaterial3D { AlbedoColor = new Color(0.62f, 0.60f, 0.58f), Metallic = 0.5f, Roughness = 0.1f };
            var grid = EndGrid(spec, style.TailLampLower, false);
            foreach (var mirror in new[] { false, true }) Add(SkinMesh(grid, true, Vector3.Back, 0.003f, mirror), clear, "TailLampLower");
        }
        if (style.FrontStrip.Length > 0) Add(SkinMesh(EndGrid(spec, style.FrontStrip, true), true, Vector3.Forward, 0.004f, false), trim, "FrontStrip");
        if (style.RearStrip.Length > 0) Add(SkinMesh(EndGrid(spec, style.RearStrip, false), true, Vector3.Back, 0.004f, false), trim, "RearStrip");
        if (style.TailHandle.Length > 0) Add(SkinMesh(EndGrid(spec, style.TailHandle, false), true, Vector3.Back, 0.004f, false), chrome, "TailHandle");
        if (style.SideSkirts)
        {
            // The HGT's skirts: a band in body colour along the sills between the arches, standing a centimetre proud.
            var skirt = SideGrid(spec, -spec.FrontAxle + style.FrontArchRadius + 0.02f, spec.RearAxle - style.RearArchRadius - 0.02f, _ => style.Sample(style.FloorH, 0f) + 0.035f, _ => style.Sample(style.FloorH, 0f) + 0.13f);
            foreach (var mirror in new[] { false, true }) Add(SkinMesh(skirt, false, Vector3.Right, 0.012f, mirror), paint, "SideSkirt");
        }
        if (style.FrontLip)
        {
            // The HGT's lip under the bumper, across the nose's foot.
            var noseZ = style.Keys[0];
            var half = style.Sample(style.HalfWidth, noseZ + 0.10f) - 0.08f;
            Box(outside, new Vector3(2f * half, 0.035f, 0.10f), new Vector3(0f, style.Sample(style.FloorH, noseZ) + 0.005f, noseZ + 0.06f), trim);
        }
        if (!float.IsNaN(style.RearHandleZ))
        {
            foreach (var side in new[] { -1f, 1f })
            {
                var x = SkinX(spec, style.RearHandleZ, style.HandleY);
                Box(outside, new Vector3(0.03f, 0.032f, 0.15f), new Vector3(side * (x + 0.008f), style.HandleY, style.RearHandleZ), style.PaintedMirrors ? paint : trim);
            }
        }
    }
}

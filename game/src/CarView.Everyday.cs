using Godot;

namespace GrudgeLap.Game;

/// <summary>
/// The everyday cars of Turkey's roads (0.9.1): the 1.6 saloon of the 1990s on the lines of the Tofas Sahin, the 1.4
/// estate on the lines of the Renault 12 Toros, and the 1.4 fleet saloon of today on the lines of the Fiat Egea. They
/// are built like the hatchbacks (CarView.Hatchbacks.cs): a loft keyed on the profile, with the lamps, the grilles,
/// the strips and the plates laid on the skin; a saloon's boot is the tailgate's place.
/// </summary>
internal sealed partial class CarView
{
    internal sealed partial class BodyStyle
    {
        /// <summary>A wiper on the rear glass (the hatchbacks and the estate; not a saloon).</summary>
        public bool RearWiper = true;
        /// <summary>Roof rails along the roof's edges (the estate).</summary>
        public bool RoofRails;
        /// <summary>An airbag in the steering wheel's hub; the cars before the mid-1990s have a horn pad.</summary>
        public bool? Airbag;
        /// <summary>The radius of the round headlamps behind the lens; NaN keeps the small projector lamps of the 2000s.</summary>
        public float HeadlampBulbRadius = float.NaN;
        /// <summary>How many round lamps each headlamp holds (two on the estate's twin round lamps).</summary>
        public int HeadlampBulbs = 2;
        /// <summary>A steel wheel under a plastic cover, "plain" or "vented"; null for the alloys.</summary>
        public string? Hubcap;

        /// <summary>
        /// The 1.6 saloon of 1991, on the lines of the Tofas Sahin (the Fiat 131 body built in Bursa, 1985-2002):
        /// 4.32 m long and 1.64 m wide, a long flat bonnet over rectangular lamps either side of a black grille band,
        /// a tall upright greenhouse with thin pillars, the rear window over a short level boot, the black plastic
        /// bumpers wrapping the corners and a black rubbing strip down the doors.
        /// </summary>
        /// <remarks>
        /// Keyed on a dimensioned three-view drawing of the 1980 Fiat 131 Mirafiori saloon (getoutlines.com, CC BY
        /// 4.0), calibrated on the Sahin's 4316 mm and 1437 mm (the drawing's own proportions agree within 1.5 %):
        /// overhangs 720 / 1106 mm, wheelbase 2490, the side view measured every 10 cm for the roofline and the
        /// beltline. The nose is at z = -1.89.
        /// </remarks>
        public static readonly BodyStyle Saloon = new()
        {
            //         nose      face   bonnet      -    f.axle   cowl   screen  header   roof   r.end   r.axle  g.base  deck      -     tail-   tail
            Keys = [-1.89f, -1.83f, -1.75f, -1.50f, -1.17f, -0.83f, -0.54f, -0.25f, 0.35f, 0.98f, 1.32f, 1.70f, 2.00f, 2.31f, 2.38f, 2.426f],
            FloorH = [0.29f, 0.28f, 0.27f, 0.26f, 0.26f, 0.25f, 0.24f, 0.24f, 0.24f, 0.24f, 0.25f, 0.26f, 0.27f, 0.28f, 0.28f, 0.29f],
            HalfWidth = [0.70f, 0.76f, 0.785f, 0.80f, 0.815f, 0.821f, 0.821f, 0.821f, 0.821f, 0.821f, 0.818f, 0.81f, 0.80f, 0.79f, 0.78f, 0.74f],
            WaistH = [0.42f, 0.62f, 0.66f, 0.69f, 0.70f, 0.70f, 0.70f, 0.70f, 0.70f, 0.70f, 0.70f, 0.70f, 0.69f, 0.66f, 0.62f, 0.46f],
            ShoulderH = [0.48f, 0.80f, 0.83f, 0.855f, 0.875f, 0.90f, 0.915f, 0.92f, 0.925f, 0.93f, 0.93f, 0.925f, 0.895f, 0.855f, 0.81f, 0.52f],
            ShoulderW = [0.685f, 0.745f, 0.77f, 0.785f, 0.80f, 0.806f, 0.806f, 0.806f, 0.806f, 0.806f, 0.803f, 0.795f, 0.785f, 0.775f, 0.765f, 0.725f],
            EdgeW = [0.66f, 0.73f, 0.76f, 0.77f, 0.775f, 0.775f, 0.77f, 0.77f, 0.77f, 0.77f, 0.775f, 0.78f, 0.775f, 0.765f, 0.755f, 0.715f],
            EdgeH = [0.50f, 0.84f, 0.86f, 0.88f, 0.905f, 0.93f, 0.945f, 0.95f, 0.955f, 0.96f, 0.96f, 0.955f, 0.92f, 0.88f, 0.84f, 0.54f],
            CrestW = [0.62f, 0.66f, 0.69f, 0.70f, 0.71f, 0.71f, 0.66f, 0.62f, 0.62f, 0.625f, 0.66f, 0.70f, 0.71f, 0.71f, 0.70f, 0.66f],
            CrestH = [0.51f, 0.855f, 0.88f, 0.90f, 0.925f, 0.955f, 1.15f, 1.365f, 1.40f, 1.39f, 1.215f, 0.975f, 0.935f, 0.895f, 0.855f, 0.555f],
            CentreH = [0.52f, 0.86f, 0.885f, 0.905f, 0.935f, 0.965f, 1.18f, 1.40f, 1.437f, 1.43f, 1.25f, 0.985f, 0.94f, 0.90f, 0.86f, 0.56f],
            CowlZ = -0.83f,
            CabinFrom = -0.255f,
            CabinTo = 1.10f,
            ScreenFrom = -0.83f,
            ScreenTo = -0.25f,
            RearGlassFrom = 0.98f,
            RearGlassTo = 1.70f,
            FrontArchTop = 0.64f,
            RearArchTop = 0.64f,
            FrontArchRadius = 0.355f,
            RearArchRadius = 0.355f,
            NoseTrimBelow = 0.555f,
            TailTrimBelow = 0.555f,
            FrontTrack = 0.693f,
            RearTrack = 0.664f,
            FrontTyreWidth = 0.175f,
            RearTyreWidth = 0.175f,
            RimRadius = 0.165f,
            TopSeams = [-0.86f, 1.73f],
            FlankSeams = [-0.67f, 0.36f, 1.30f],
            RearBulkheadZ = 1.70f,
            EyeZ = 0.22f,
            EyeH = 1.18f,
            LeatherTint = new(0.46f, 0.40f, 0.34f),
            CarpetTint = new(0.20f, 0.18f, 0.16f),
            HeadlinerTint = new(0.74f, 0.71f, 0.64f),
            Hatchback = true,
            RunStep = 0.10f,
            RearSeatZ = 1.02f,
            Headlamp = [new(0.40f, 0.645f), new(0.62f, 0.645f), new(0.735f, 0.652f), new(0.762f, 0.682f), new(0.758f, 0.765f), new(0.70f, 0.785f), new(0.40f, 0.785f)],
            Grille = [new(-0.385f, 0.648f), new(0f, 0.648f), new(0.385f, 0.648f), new(0.385f, 0.782f), new(0f, 0.782f), new(-0.385f, 0.782f)],
            TailLamp = [new(0.30f, 0.62f), new(0.60f, 0.62f), new(0.765f, 0.625f), new(0.775f, 0.66f), new(0.77f, 0.795f), new(0.60f, 0.80f), new(0.30f, 0.80f)],
            FrontPlateY = 0.42f,
            RearPlateY = 0.71f,
            ExhaustX = [-0.45f],
            ExhaustY = 0.26f,
            RubStripY = 0.52f,
            PillarsZ = [0.36f],
            HandleZ = 0.24f,
            HandleY = 0.85f,
            MirrorZ = -0.60f,
            RearWiper = false,
            Airbag = false,
            HeadlampBulbRadius = 0.045f,
            HeadlampBulbs = 1,
            Hubcap = "plain",
            DashTint = new(0.26f, 0.25f, 0.24f),
            FasciaTint = new(0.40f, 0.38f, 0.36f),
            ClothSeats = true,
        };

        /// <summary>
        /// The 1.4 estate of 1993, on the lines of the Renault 12 Toros (Oyak-Renault's R12 of 1989-2000): 4.38 m long
        /// and 1.62 m wide, the engine ahead of the front axle under a long bonnet, two round lamps a side in a black
        /// panel with the grille's bars, a low flat beltline under a long glasshouse of three side windows, a near
        /// upright tailgate with slim upright lamps at the foot of its pillars, roof rails, black bumpers and strips.
        /// </summary>
        /// <remarks>
        /// Keyed on a dimensioned three-view drawing of the 1969 Dacia 1300 Break (getoutlines.com, CC BY 4.0), the
        /// Romanian Renault 12 estate of the same body, calibrated on the Toros's 4380 mm and 1450 mm: overhangs
        /// 850 / 1090 mm, wheelbase 2441. The nose is at z = -1.85.
        /// </remarks>
        public static readonly BodyStyle Estate = new()
        {
            //         nose      face   bonnet      -    f.axle   cowl   screen  header   roof      -        -    r.end   gate    gate-   bumper  tail
            Keys = [-1.85f, -1.79f, -1.70f, -1.40f, -1.00f, -0.62f, -0.30f, 0.02f, 0.60f, 1.20f, 1.80f, 2.27f, 2.36f, 2.42f, 2.49f, 2.53f],
            FloorH = [0.29f, 0.28f, 0.27f, 0.26f, 0.25f, 0.24f, 0.23f, 0.23f, 0.23f, 0.23f, 0.24f, 0.26f, 0.27f, 0.28f, 0.28f, 0.29f],
            HalfWidth = [0.70f, 0.76f, 0.78f, 0.80f, 0.808f, 0.81f, 0.81f, 0.81f, 0.81f, 0.81f, 0.805f, 0.795f, 0.79f, 0.785f, 0.78f, 0.74f],
            WaistH = [0.42f, 0.60f, 0.64f, 0.66f, 0.66f, 0.66f, 0.66f, 0.66f, 0.66f, 0.66f, 0.66f, 0.66f, 0.65f, 0.63f, 0.58f, 0.46f],
            ShoulderH = [0.48f, 0.73f, 0.76f, 0.81f, 0.85f, 0.88f, 0.89f, 0.895f, 0.90f, 0.90f, 0.90f, 0.895f, 0.88f, 0.78f, 0.645f, 0.52f],
            ShoulderW = [0.685f, 0.745f, 0.765f, 0.785f, 0.793f, 0.795f, 0.795f, 0.795f, 0.795f, 0.795f, 0.79f, 0.78f, 0.775f, 0.77f, 0.765f, 0.725f],
            EdgeW = [0.66f, 0.72f, 0.745f, 0.76f, 0.765f, 0.765f, 0.76f, 0.755f, 0.755f, 0.755f, 0.755f, 0.75f, 0.75f, 0.75f, 0.745f, 0.70f],
            EdgeH = [0.50f, 0.745f, 0.765f, 0.815f, 0.87f, 0.905f, 0.915f, 0.92f, 0.925f, 0.925f, 0.925f, 0.92f, 0.905f, 0.80f, 0.665f, 0.54f],
            CrestW = [0.62f, 0.66f, 0.68f, 0.70f, 0.71f, 0.70f, 0.65f, 0.60f, 0.60f, 0.60f, 0.60f, 0.60f, 0.66f, 0.70f, 0.71f, 0.66f],
            CrestH = [0.51f, 0.765f, 0.785f, 0.835f, 0.89f, 0.935f, 1.17f, 1.375f, 1.41f, 1.42f, 1.405f, 1.345f, 0.935f, 0.815f, 0.675f, 0.555f],
            CentreH = [0.52f, 0.77f, 0.79f, 0.84f, 0.90f, 0.95f, 1.20f, 1.39f, 1.43f, 1.44f, 1.42f, 1.36f, 0.95f, 0.82f, 0.68f, 0.56f],
            CowlZ = -0.62f,
            CabinFrom = 0.015f,
            CabinTo = 2.20f,
            ScreenFrom = -0.62f,
            ScreenTo = 0.02f,
            RearGlassFrom = 2.27f,
            RearGlassTo = 2.36f,
            FrontArchTop = 0.64f,
            RearArchTop = 0.64f,
            FrontArchRadius = 0.35f,
            RearArchRadius = 0.35f,
            NoseTrimBelow = 0.555f,
            TailTrimBelow = 0.555f,
            FrontTrack = 0.656f,
            RearTrack = 0.657f,
            FrontTyreWidth = 0.155f,
            RearTyreWidth = 0.155f,
            RimRadius = 0.165f,
            TopSeams = [-0.65f, 2.25f],
            FlankSeams = [-0.52f, 0.48f, 1.42f],
            RearBulkheadZ = 2.36f,
            EyeZ = 0.50f,
            EyeH = 1.16f,
            LeatherTint = new(0.50f, 0.38f, 0.28f),
            CarpetTint = new(0.20f, 0.17f, 0.14f),
            HeadlinerTint = new(0.72f, 0.68f, 0.60f),
            Hatchback = true,
            RunStep = 0.10f,
            RearSeatZ = 0.98f,
            Headlamp = [new(0.42f, 0.600f), new(0.73f, 0.600f), new(0.755f, 0.628f), new(0.755f, 0.692f), new(0.73f, 0.720f), new(0.42f, 0.720f), new(0.40f, 0.692f), new(0.40f, 0.628f)],
            Grille = [new(-0.40f, 0.602f), new(0f, 0.602f), new(0.40f, 0.602f), new(0.40f, 0.718f), new(0f, 0.718f), new(-0.40f, 0.718f)],
            TailLamp = [new(0.665f, 0.62f), new(0.745f, 0.62f), new(0.755f, 0.66f), new(0.752f, 0.84f), new(0.74f, 0.86f), new(0.67f, 0.86f), new(0.66f, 0.84f)],
            FrontPlateY = 0.42f,
            RearPlateY = 0.68f,
            ExhaustX = [-0.45f],
            ExhaustY = 0.25f,
            RubStripY = 0.46f,
            PillarsZ = [0.48f],
            HandleZ = 0.40f,
            HandleY = 0.82f,
            MirrorZ = -0.42f,
            RoofRails = true,
            Airbag = false,
            HeadlampBulbRadius = 0.062f,
            Hubcap = "vented",
            DashTint = new(0.24f, 0.24f, 0.24f),
            FasciaTint = new(0.37f, 0.36f, 0.35f),
            ClothSeats = true,
        };

        /// <summary>
        /// The 1.4 fleet saloon of 2017, on the lines of the Fiat Egea (the Tipo saloon of Bursa, 2015-): 4.53 m long
        /// and 1.79 m wide, a cab-forward saloon with a short bonnet, slim swept lamps either side of a trapezoid
        /// black grille, fog lamps in the bumper's corners, a raked screen, a high beltline under a roof that runs on
        /// into a fastback-like pillar, a short boot with its lip, and tail lamps wrapped from the wings onto the lid.
        /// </summary>
        /// <remarks>
        /// Proportioned on the published figures (4532 x 1792 x 1497 mm, wheelbase 2636, overhangs 893 / 1003) and
        /// on photographs of the car (Wikimedia Commons, credited in THIRD_PARTY.md): no free dimensioned drawing was
        /// found. The nose is at z = -1.921.
        /// </remarks>
        public static readonly BodyStyle Fleet = new()
        {
            //         nose      face   bonnet      -    f.axle   cowl   screen  header   roof   r.roof  r.glass g.base  deck    lip    tail-   tail
            Keys = [-1.921f, -1.87f, -1.76f, -1.45f, -1.028f, -0.72f, -0.33f, 0.07f, 0.55f, 1.10f, 1.42f, 1.86f, 2.20f, 2.50f, 2.58f, 2.611f],
            FloorH = [0.27f, 0.25f, 0.23f, 0.22f, 0.22f, 0.21f, 0.21f, 0.21f, 0.21f, 0.21f, 0.22f, 0.24f, 0.26f, 0.27f, 0.28f, 0.28f],
            HalfWidth = [0.74f, 0.80f, 0.845f, 0.875f, 0.89f, 0.896f, 0.896f, 0.896f, 0.896f, 0.896f, 0.893f, 0.885f, 0.875f, 0.86f, 0.85f, 0.81f],
            WaistH = [0.40f, 0.52f, 0.60f, 0.66f, 0.70f, 0.72f, 0.72f, 0.72f, 0.72f, 0.73f, 0.74f, 0.75f, 0.75f, 0.73f, 0.66f, 0.46f],
            ShoulderH = [0.48f, 0.68f, 0.77f, 0.84f, 0.89f, 0.96f, 0.975f, 0.985f, 0.995f, 1.005f, 1.02f, 1.035f, 1.03f, 1.02f, 0.93f, 0.52f],
            ShoulderW = [0.72f, 0.78f, 0.825f, 0.855f, 0.868f, 0.873f, 0.873f, 0.873f, 0.873f, 0.873f, 0.87f, 0.862f, 0.852f, 0.84f, 0.83f, 0.79f],
            EdgeW = [0.68f, 0.74f, 0.78f, 0.80f, 0.81f, 0.81f, 0.80f, 0.795f, 0.795f, 0.795f, 0.80f, 0.81f, 0.81f, 0.80f, 0.79f, 0.75f],
            EdgeH = [0.50f, 0.70f, 0.79f, 0.855f, 0.91f, 0.99f, 1.005f, 1.015f, 1.025f, 1.035f, 1.05f, 1.065f, 1.055f, 1.045f, 0.95f, 0.54f],
            CrestW = [0.62f, 0.68f, 0.72f, 0.74f, 0.76f, 0.76f, 0.69f, 0.625f, 0.615f, 0.625f, 0.66f, 0.72f, 0.74f, 0.74f, 0.73f, 0.70f],
            CrestH = [0.51f, 0.715f, 0.80f, 0.87f, 0.925f, 0.995f, 1.21f, 1.415f, 1.455f, 1.43f, 1.325f, 1.085f, 1.065f, 1.06f, 0.955f, 0.555f],
            CentreH = [0.52f, 0.72f, 0.81f, 0.885f, 0.945f, 1.01f, 1.24f, 1.455f, 1.497f, 1.47f, 1.36f, 1.10f, 1.075f, 1.07f, 0.96f, 0.56f],
            CowlZ = -0.72f,
            CabinFrom = 0.065f,
            CabinTo = 1.55f,
            ScreenFrom = -0.72f,
            ScreenTo = 0.07f,
            RearGlassFrom = 1.10f,
            RearGlassTo = 1.86f,
            FrontArchTop = 0.70f,
            RearArchTop = 0.70f,
            FrontArchRadius = 0.38f,
            RearArchRadius = 0.38f,
            NoseTrimBelow = 0.30f,
            TailTrimBelow = 0.36f,
            FrontTrack = 0.771f,
            RearTrack = 0.772f,
            FrontTyreWidth = 0.195f,
            RearTyreWidth = 0.195f,
            RimRadius = 0.1905f,
            TopSeams = [-0.75f, 1.89f],
            FlankSeams = [-0.55f, 0.62f, 1.62f],
            RearBulkheadZ = 1.86f,
            EyeZ = 0.48f,
            EyeH = 1.17f,
            LeatherTint = new(0.24f, 0.24f, 0.26f),
            CarpetTint = new(0.10f, 0.10f, 0.11f),
            HeadlinerTint = new(0.62f, 0.62f, 0.62f),
            Hatchback = true,
            RunStep = 0.10f,
            RearSeatZ = 1.12f,
            Headlamp = [new(0.40f, 0.70f), new(0.56f, 0.69f), new(0.70f, 0.70f), new(0.80f, 0.73f), new(0.83f, 0.77f), new(0.78f, 0.785f), new(0.62f, 0.78f), new(0.48f, 0.76f), new(0.40f, 0.735f)],
            Grille = [new(-0.36f, 0.625f), new(0f, 0.62f), new(0.36f, 0.625f), new(0.40f, 0.735f), new(0f, 0.74f), new(-0.40f, 0.735f)],
            LowerIntake = [new(0f, 0.34f), new(0.40f, 0.34f), new(0.44f, 0.38f), new(0.42f, 0.44f), new(0f, 0.44f)],
            TailLamp = [new(0.45f, 0.92f), new(0.62f, 0.90f), new(0.79f, 0.915f), new(0.84f, 0.95f), new(0.835f, 1.01f), new(0.70f, 1.035f), new(0.52f, 1.03f), new(0.44f, 0.98f)],
            FogLampX = 0.62f,
            FogLampY = 0.40f,
            FrontPlateY = 0.47f,
            RearPlateY = 0.80f,
            ExhaustX = [-0.45f],
            ExhaustY = 0.27f,
            PillarsZ = [0.62f],
            HandleZ = 0.50f,
            HandleY = 0.93f,
            MirrorZ = -0.48f,
            PaintedMirrors = true,
            RearWiper = false,
            Airbag = true,
            Hubcap = "vented",
            DashTint = new(0.28f, 0.29f, 0.31f),
            FasciaTint = new(0.40f, 0.41f, 0.43f),
            SatinStrip = true,
            ClothSeats = true,
        };
    }
}

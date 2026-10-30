namespace GrudgeLap.Physics.Vehicle;

/// <summary>
/// Tunable description of a car. The default is a late-80s rear-wheel-drive flat-twelve sports car with the
/// original game's four-speed gearbox; <see cref="V8Evo"/> is a 2010s mid-engined V8 with a seven-speed box; three
/// front-wheel-drive hatchbacks follow the hot hatches of 1997-2008 (<see cref="HotHatch"/>, <see cref="FiveHatch"/>,
/// <see cref="TurboHatch"/>); and three everyday cars of Turkey's roads (0.9.1): the 1.6 rear-wheel-drive saloon of the
/// 1990s (<see cref="Saloon"/>), the 1.4 estate of the same years (<see cref="Estate"/>) and the 1.4 fleet saloon of
/// today (<see cref="Fleet"/>). The numbers are game numbers, tuned for feel from the published figures of the cars
/// they follow (power, torque, mass, wheelbase, track, tyres, gearing), not a manufacturer datasheet.
/// </summary>
public sealed record VehicleSpec
{
    /// <summary>Name shown on the car-select screen; descriptive, no make or model trademarks.</summary>
    public string Name { get; init; } = "FLAT-12 GT '84";
    /// <summary>Which body the view builds and which engine recordings it plays.</summary>
    public CarStyle Style { get; init; } = CarStyle.FlatTwelve;
    public EngineSpec Engine { get; init; } = EngineSpec.FlatTwelve;
    /// <summary>Full scale of the speedometer, mph.</summary>
    public float SpeedoMaxMph { get; init; } = 160f;

    // Body
    public float MassKg { get; init; } = 1580f;              // car + driver + fuel
    public float InertiaRoll { get; init; } = 520f;          // kg m^2 about the forward axis
    public float InertiaYaw { get; init; } = 2300f;          // about the up axis
    public float InertiaPitch { get; init; } = 2100f;        // about the lateral axis
    public float HalfTrack { get; init; } = 0.78f;           // m, hub to centre line
    public float FrontAxle { get; init; } = 1.27f;           // m, CG to front hubs
    public float RearAxle { get; init; } = 1.28f;            // m, CG to rear hubs
    public float HubHeight { get; init; } = -0.12f;          // m, hub plane below the CG
    /// <summary>
    /// The body's footprint for knocks against obstacles: centre of gravity to the front and the rear bumper and half
    /// the widest width (m), as the view draws the body (its nose and tail keys and widest half width).
    /// </summary>
    public float BodyFront { get; init; } = 2.355f;
    public float BodyRear { get; init; } = 2.13f;
    public float BodyHalfWidth { get; init; } = 0.99f;
    public float DragCoefficient { get; init; } = 0.36f;
    public float FrontalArea { get; init; } = 1.95f;         // m^2
    public float RollingResistance { get; init; } = 0.015f;
    /// <summary>Multiplier on the surface friction: modern tyres grip harder than 1984's.</summary>
    public float TireGrip { get; init; } = 1.0f;
    /// <summary>Electronic traction control: the throttle is eased when the driven wheels spin up. Period-correct only on the modern cars.</summary>
    public bool TractionControl { get; init; }
    /// <summary>The engine drives the front wheels (a transverse engine in a hatchback) instead of the rear.</summary>
    public bool FrontWheelDrive { get; init; }

    // Suspension (raycast model per wheel)
    public float WheelRadius { get; init; } = 0.33f;
    public float SuspensionRest { get; init; } = 0.26f;      // m of travel at full droop
    public float SuspensionStiffness { get; init; } = 38000f; // N/m
    public float SuspensionDamping { get; init; } = 3800f;   // N s/m
    public float BumpStopStiffness { get; init; } = 260000f; // N/m beyond MaxCompression
    public float MaxCompression { get; init; } = 0.17f;      // m

    // Steering
    public float MaxSteerLowSpeedDeg { get; init; } = 32f;
    public float MaxSteerHighSpeedDeg { get; init; } = 7f;
    public float SteerSpeedBlendMps { get; init; } = 55f;    // speed at which the high-speed limit applies

    // Brakes
    public float FrontBrakeTorque { get; init; } = 2700f;    // N m per wheel at full pedal
    public float RearBrakeTorque { get; init; } = 1700f;

    // Drivetrain
    public float[] GearRatios { get; init; } = [-3.0f, 0f, 3.14f, 2.11f, 1.46f, 1.0f]; // index = gear + 1 (R, N, 1..4)
    public float FinalDrive { get; init; } = 3.21f;
    public float DrivelineEfficiency { get; init; } = 0.9f;
    public float ClutchMaxTorque { get; init; } = 900f;       // N m
    public float EngineInertia { get; init; } = 0.28f;        // kg m^2 (flywheel + clutch)
    public float WheelInertia { get; init; } = 1.3f;          // kg m^2 per wheel + half shaft

    public int ForwardGearCount => GearRatios.Length - 2;
    public float RatioForGear(int gear) => GearRatios[gear + 1];

    public static VehicleSpec FlatTwelve => new();

    /// <summary>Mid-engined V8 of the 2010s with a sports exhaust: lighter, stiffer, 570 hp at 8000, seven gears.</summary>
    public static VehicleSpec V8Evo => new()
    {
        Name = "V8 GTB '12 SPORT EXHAUST",
        Style = CarStyle.V8,
        Engine = EngineSpec.V8,
        SpeedoMaxMph = 220f,
        MassKg = 1485f,
        InertiaRoll = 480f,
        InertiaYaw = 2150f,
        InertiaPitch = 1950f,
        HalfTrack = 0.80f,
        FrontAxle = 1.32f,
        RearAxle = 1.33f,
        BodyFront = 2.27f,
        BodyRear = 2.26f,
        BodyHalfWidth = 0.97f,
        DragCoefficient = 0.33f,
        FrontalArea = 1.98f,
        TireGrip = 1.25f,
        TractionControl = true,
        SuspensionStiffness = 46000f,
        SuspensionDamping = 4200f,
        MaxSteerLowSpeedDeg = 30f,
        FrontBrakeTorque = 3200f,
        RearBrakeTorque = 2000f,
        GearRatios = [-3.1f, 0f, 3.08f, 2.19f, 1.63f, 1.29f, 1.03f, 0.84f, 0.69f],
        FinalDrive = 4.9f,
        ClutchMaxTorque = 1200f,
        EngineInertia = 0.20f,
    };

    /// <summary>
    /// A hot hatch of 2001 (after the 2.0-litre Clio of the time): 172 hp at 6250, 1140 kg with the driver, five
    /// gears, 205/45 R16 tyres, the front axle carrying 62 % of the weight, no traction control. 0-60 in about 7 s.
    /// </summary>
    public static VehicleSpec HotHatch => new()
    {
        Name = "HOT HATCH 2.0 16V '01",
        Style = CarStyle.HotHatch,
        Engine = EngineSpec.HotHatch,
        SpeedoMaxMph = 150f,
        MassKg = 1140f,
        InertiaRoll = 390f,
        InertiaYaw = 1500f,
        InertiaPitch = 1450f,
        HalfTrack = 0.73f,
        FrontAxle = 0.95f,
        RearAxle = 1.52f,
        BodyFront = 1.664f,
        BodyRear = 2.109f,
        BodyHalfWidth = 0.83f,
        HubHeight = -0.20f,
        DragCoefficient = 0.34f,
        FrontalArea = 1.95f,
        TireGrip = 1.08f,
        WheelRadius = 0.296f,
        SuspensionRest = 0.22f,
        SuspensionStiffness = 30000f,
        SuspensionDamping = 2900f,
        MaxCompression = 0.13f,
        MaxSteerLowSpeedDeg = 34f,
        MaxSteerHighSpeedDeg = 7.5f,
        FrontBrakeTorque = 2100f,
        RearBrakeTorque = 800f,
        GearRatios = [-3.55f, 0f, 3.09f, 1.84f, 1.32f, 1.03f, 0.86f],
        FinalDrive = 4.21f,
        ClutchMaxTorque = 360f,
        EngineInertia = 0.12f,
        WheelInertia = 0.9f,
        FrontWheelDrive = true,
    };

    /// <summary>
    /// A five-cylinder hatch of 1997 (after the 2.0 20-valve Bravo): 147 hp at 6100, the five's warble, 1310 kg,
    /// five gears, 195/55 R15 tyres. Slower than the others, and the one that sounds like nothing else.
    /// </summary>
    public static VehicleSpec FiveHatch => new()
    {
        Name = "FIVE 2.0 20V HATCH '97",
        Style = CarStyle.FiveHatch,
        Engine = EngineSpec.FiveCylinder,
        SpeedoMaxMph = 150f,
        MassKg = 1310f,
        InertiaRoll = 430f,
        InertiaYaw = 1750f,
        InertiaPitch = 1700f,
        HalfTrack = 0.73f,
        FrontAxle = 0.95f,
        RearAxle = 1.59f,
        BodyFront = 1.825f,
        BodyRear = 2.204f,
        BodyHalfWidth = 0.875f,
        HubHeight = -0.20f,
        DragCoefficient = 0.32f,
        FrontalArea = 2.0f,
        TireGrip = 1.0f,
        WheelRadius = 0.298f,
        SuspensionRest = 0.23f,
        SuspensionStiffness = 30000f,
        SuspensionDamping = 3000f,
        MaxCompression = 0.14f,
        MaxSteerLowSpeedDeg = 33f,
        MaxSteerHighSpeedDeg = 7f,
        FrontBrakeTorque = 2100f,
        RearBrakeTorque = 850f,
        GearRatios = [-3.71f, 0f, 3.35f, 2.05f, 1.41f, 1.03f, 0.82f],
        FinalDrive = 4.07f,
        ClutchMaxTorque = 330f,
        EngineInertia = 0.14f,
        WheelInertia = 0.9f,
        FrontWheelDrive = true,
    };

    /// <summary>
    /// A turbo hatch of 2008 (after the 2.0 TFSI Leon of the time): 240 hp, 300 N m from 2200 to 5500 once the
    /// turbo has spooled, 1480 kg, six gears, 225/40 R18 tyres and traction control. 0-60 in about 6 s.
    /// </summary>
    public static VehicleSpec TurboHatch => new()
    {
        Name = "TURBO HATCH 2.0T '08",
        Style = CarStyle.TurboHatch,
        Engine = EngineSpec.TurboFour,
        SpeedoMaxMph = 170f,
        MassKg = 1480f,
        InertiaRoll = 470f,
        InertiaYaw = 2000f,
        InertiaPitch = 1950f,
        HalfTrack = 0.765f,
        FrontAxle = 0.98f,
        RearAxle = 1.60f,
        BodyFront = 1.92f,
        BodyRear = 2.395f,
        BodyHalfWidth = 0.884f,
        HubHeight = -0.20f,
        DragCoefficient = 0.33f,
        FrontalArea = 2.15f,
        TireGrip = 1.18f,
        TractionControl = true,
        WheelRadius = 0.319f,
        SuspensionRest = 0.22f,
        SuspensionStiffness = 38000f,
        SuspensionDamping = 3700f,
        MaxCompression = 0.13f,
        MaxSteerLowSpeedDeg = 33f,
        MaxSteerHighSpeedDeg = 7f,
        FrontBrakeTorque = 3000f,
        RearBrakeTorque = 1300f,
        GearRatios = [-3.18f, 0f, 3.36f, 2.09f, 1.47f, 1.10f, 0.87f, 0.73f],
        FinalDrive = 3.65f,
        ClutchMaxTorque = 520f,
        EngineInertia = 0.13f,
        WheelInertia = 1.0f,
        FrontWheelDrive = true,
    };

    /// <summary>
    /// The 1.6 saloon of 1991, after the Tofas Sahin (the Turkish-built Fiat 131 of 1985-2002): the 131's 1,585 cc
    /// pushrod four with its twin-choke carburettor, 75 PS DIN at 5400 and 123 N m at 3000 (FIA form 5608), 960 kg
    /// at the kerb, the five-speed box of 1990 (3.667 / 2.100 / 1.361 / 1.000 / 0.881, final drive 4.10) to a live rear
    /// axle, 175/70 R13 tyres, discs in front and drums behind, no servo-free luxuries. 160 km/h, 0-100 in 13.9 s.
    /// </summary>
    public static VehicleSpec Saloon => new()
    {
        Name = "SALOON 1.6 '91",
        Style = CarStyle.Saloon,
        Engine = EngineSpec.SaloonFour,
        SpeedoMaxMph = 112f,
        MassKg = 1050f,
        InertiaRoll = 380f,
        InertiaYaw = 1650f,
        InertiaPitch = 1600f,
        HalfTrack = 0.678f,
        FrontAxle = 1.17f,
        RearAxle = 1.32f,
        BodyFront = 1.89f,
        BodyRear = 2.426f,
        BodyHalfWidth = 0.821f,
        HubHeight = -0.20f,
        DragCoefficient = 0.43f,
        FrontalArea = 1.90f,
        TireGrip = 0.86f,
        WheelRadius = 0.2875f,
        SuspensionRest = 0.25f,
        SuspensionStiffness = 24000f,
        SuspensionDamping = 2300f,
        MaxCompression = 0.15f,
        MaxSteerLowSpeedDeg = 33f,
        MaxSteerHighSpeedDeg = 7f,
        FrontBrakeTorque = 1500f,
        RearBrakeTorque = 700f,
        GearRatios = [-3.526f, 0f, 3.667f, 2.100f, 1.361f, 1.000f, 0.881f],
        FinalDrive = 4.10f,
        ClutchMaxTorque = 220f,
        EngineInertia = 0.13f,
        WheelInertia = 0.75f,
    };

    /// <summary>
    /// The 1.4 estate of 1993, after the Renault 12 Toros (Oyak-Renault's R12 of 1989-2000): the Cleon-Fonte pushrod
    /// four of 1,397 cc with one single-choke carburettor, 61.5 PS DIN and about 102 N m at 3000, set ahead of the
    /// front axle (FIA form 5396), driving the front wheels through the five-speed box from the Renault 18 (3.818 /
    /// 2.235 / 1.478 / 1.036 / 0.852, final drive 3.777); 950 kg, 155 R13 tyres, soft springs. About 150 km/h.
    /// </summary>
    public static VehicleSpec Estate => new()
    {
        Name = "ESTATE 1.4 '93",
        Style = CarStyle.Estate,
        Engine = EngineSpec.EstateFour,
        SpeedoMaxMph = 112f,
        MassKg = 1045f,
        InertiaRoll = 370f,
        InertiaYaw = 1700f,
        InertiaPitch = 1650f,
        HalfTrack = 0.656f,
        FrontAxle = 1.00f,
        RearAxle = 1.441f,
        BodyFront = 1.85f,
        BodyRear = 2.53f,
        BodyHalfWidth = 0.81f,
        HubHeight = -0.20f,
        DragCoefficient = 0.46f,
        FrontalArea = 1.95f,
        TireGrip = 0.82f,
        WheelRadius = 0.289f,
        SuspensionRest = 0.26f,
        SuspensionStiffness = 22000f,
        SuspensionDamping = 2100f,
        MaxCompression = 0.16f,
        MaxSteerLowSpeedDeg = 32f,
        MaxSteerHighSpeedDeg = 7f,
        FrontBrakeTorque = 1450f,
        RearBrakeTorque = 600f,
        GearRatios = [-3.083f, 0f, 3.818f, 2.235f, 1.478f, 1.036f, 0.852f],
        FinalDrive = 3.777f,
        ClutchMaxTorque = 190f,
        EngineInertia = 0.12f,
        WheelInertia = 0.7f,
        FrontWheelDrive = true,
    };

    /// <summary>
    /// The 1.4 fleet saloon of 2017, after the Fiat Egea (the Tipo saloon built in Bursa): the sixteen-valve 1,368 cc
    /// Fire, 95 PS at 6000 and 127 N m at 4500 (Fiat's handbook), 1,150 kg, six gears (3.545 / 2.158 / 1.480 / 1.121 /
    /// 0.921 / 0.766, final drive 4.071), 195/65 R15 tyres on steel wheels, ESC. Slippery (Cd 0.29): 185 km/h, 0-100
    /// in 11.5 s.
    /// </summary>
    public static VehicleSpec Fleet => new()
    {
        Name = "SALOON 1.4 16V '17",
        Style = CarStyle.Fleet,
        Engine = EngineSpec.FleetFour,
        SpeedoMaxMph = 143f,
        // The 1,150 kg is the EU kerb weight, which counts a 75 kg driver and most of a tank already.
        MassKg = 1170f,
        InertiaRoll = 450f,
        InertiaYaw = 2050f,
        InertiaPitch = 2000f,
        HalfTrack = 0.771f,
        FrontAxle = 1.028f,
        RearAxle = 1.608f,
        BodyFront = 1.921f,
        BodyRear = 2.611f,
        BodyHalfWidth = 0.896f,
        HubHeight = -0.20f,
        DragCoefficient = 0.30f,
        FrontalArea = 2.20f,
        TireGrip = 1.0f,
        TractionControl = true,
        WheelRadius = 0.317f,
        SuspensionRest = 0.24f,
        SuspensionStiffness = 30000f,
        SuspensionDamping = 2900f,
        MaxCompression = 0.14f,
        MaxSteerLowSpeedDeg = 34f,
        MaxSteerHighSpeedDeg = 7f,
        FrontBrakeTorque = 2100f,
        RearBrakeTorque = 750f,
        GearRatios = [-3.818f, 0f, 3.545f, 2.158f, 1.480f, 1.121f, 0.921f, 0.766f],
        FinalDrive = 4.071f,
        ClutchMaxTorque = 260f,
        EngineInertia = 0.11f,
        WheelInertia = 0.9f,
        FrontWheelDrive = true,
    };

    /// <summary>The cars the player can choose from, in menu order.</summary>
    public static VehicleSpec[] Catalogue => [FlatTwelve, V8Evo, HotHatch, FiveHatch, TurboHatch, Saloon, Estate, Fleet];
}

public enum CarStyle
{
    FlatTwelve,
    V8,
    HotHatch,
    FiveHatch,
    TurboHatch,
    /// <summary>The 1.6 rear-wheel-drive saloon of the 1990s (after the Tofas Sahin).</summary>
    Saloon,
    /// <summary>The 1.4 front-wheel-drive estate of the 1990s (after the Renault 12 Toros).</summary>
    Estate,
    /// <summary>The 1.4 front-wheel-drive fleet saloon of today (after the Fiat Egea).</summary>
    Fleet,
}

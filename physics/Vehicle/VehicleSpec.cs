namespace GrudgeLap.Physics.Vehicle;

/// <summary>
/// Tunable description of a car. The default is a late-80s rear-wheel-drive flat-twelve sports car with the
/// original game's four-speed gearbox; <see cref="V8Evo"/> is a 2010s mid-engined V8 with a seven-speed box; three
/// front-wheel-drive hatchbacks follow the hot hatches of 1997-2008 (<see cref="HotHatch"/>, <see cref="FiveHatch"/>,
/// <see cref="TurboHatch"/>). The numbers are game numbers, tuned for feel from the published figures of the cars
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

    /// <summary>The cars the player can choose from, in menu order.</summary>
    public static VehicleSpec[] Catalogue => [FlatTwelve, V8Evo, HotHatch, FiveHatch, TurboHatch];
}

public enum CarStyle
{
    FlatTwelve,
    V8,
    HotHatch,
    FiveHatch,
    TurboHatch,
}

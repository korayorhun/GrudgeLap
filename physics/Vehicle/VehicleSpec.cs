namespace GrudgeLap.Physics.Vehicle;

/// <summary>
/// Tunable description of a car. The default is a late-80s rear-wheel-drive flat-twelve sports car with the
/// original game's four-speed gearbox; <see cref="V8Evo"/> is a 2010s mid-engined V8 with a seven-speed box.
/// The numbers are game numbers, tuned for feel, not a manufacturer datasheet.
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
    /// <summary>Electronic traction control: the throttle is eased when the driven wheels spin up. Period-correct only on the modern car.</summary>
    public bool TractionControl { get; init; }

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

    /// <summary>The cars the player can choose from, in menu order.</summary>
    public static VehicleSpec[] Catalogue => [FlatTwelve, V8Evo];
}

public enum CarStyle
{
    FlatTwelve,
    V8,
}

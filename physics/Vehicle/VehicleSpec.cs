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
    /// <summary>
    /// Where the mass is centred, from the body's reference point (m; x right, y up, z forward). The reference point is
    /// the one the hubs, the body and the view are measured from, 0.6 m up (midway between the axles on the flat-twelve,
    /// FrontAxle/RearAxle from it on every car); the real cars
    /// carry their weight lower, and the mid-engined ones towards the back (40/60 for the flat-twelve, 42/58 for the V8),
    /// which is what makes a car squat, roll and turn the way it does.
    /// </summary>
    public System.Numerics.Vector3 CentreOfMassOffset { get; init; } = new(0f, -0.14f, -0.26f);
    public float DragCoefficient { get; init; } = 0.36f;
    public float FrontalArea { get; init; } = 1.95f;         // m^2
    /// <summary>
    /// Aerodynamic lift at each axle as lift coefficient times area, m^2 (positive lifts, negative presses down): the
    /// force is that times the dynamic pressure, applied over the axle. Road cars of the 1980s and 1990s lift a
    /// little (whole-car lift coefficients of 0.1-0.3, mostly at the front; Hucho, Aerodynamics of Road Vehicles,
    /// ch. 4); the V8 of the 2010s is shaped to press itself down a little, more at the back.
    /// </summary>
    public float FrontLiftArea { get; init; } = 0.12f;
    public float RearLiftArea { get; init; } = 0.06f;
    public float RollingResistance { get; init; } = 0.015f;
    /// <summary>Multiplier on the surface friction: modern tyres grip harder than 1984's.</summary>
    public float TireGrip { get; init; } = 1.0f;
    /// <summary>Electronic traction control: the throttle is eased when the driven wheels spin up. Period-correct only on the modern cars.</summary>
    public bool TractionControl { get; init; }
    /// <summary>
    /// Launch control (0.19): on a standing start the engine is held at its launch revs and the clutch passes all but a
    /// few per cent of what the tyres can take, reading the wheel speeds as it goes; without it the assisted clutch makes
    /// the start a good driver makes, revs up and slips, a little short of the tyres' peak (VehicleSim.LaunchTraction).
    /// Only the V8 of the 2010s has one.
    /// </summary>
    public bool LaunchControl { get; init; }
    /// <summary>The engine drives the front wheels (a transverse engine in a hatchback) instead of the rear; on a four-wheel-drive car, see <see cref="FourWheelDrive"/>.</summary>
    public bool FrontWheelDrive { get; init; }
    /// <summary>
    /// Four-wheel drive: how the gearbox's torque reaches both axles and each axle's differential; null on a car that
    /// drives one axle, which <see cref="FrontWheelDrive"/> names. <see cref="LimitedSlipLocking"/> and
    /// <see cref="LimitedSlipPreload"/> then do not apply: each axle has its own.
    /// </summary>
    public FourWheelDrive? FourWheelDrive { get; init; }
    /// <summary>Whether the engine drives the wheels of the front axle (<paramref name="front"/>) or of the rear one.</summary>
    public bool Drives(bool front) => FourWheelDrive is not null || front == FrontWheelDrive;
    /// <summary>Tyre sizes as published, front and rear: they set each axle's nominal load and cornering stiffness (<see cref="TireSize"/>).</summary>
    public TireSize FrontTire { get; init; } = new(225f, 50f, 16f);
    public TireSize RearTire { get; init; } = new(255f, 50f, 16f);

    // Suspension (raycast model per wheel)
    public float WheelRadius { get; init; } = 0.33f;
    public float SuspensionRest { get; init; } = 0.26f;      // m of travel at full droop
    public float SuspensionStiffness { get; init; } = 38000f; // N/m
    public float SuspensionDamping { get; init; } = 3800f;   // N s/m
    public float BumpStopStiffness { get; init; } = 260000f; // N/m beyond MaxCompression
    public float MaxCompression { get; init; } = 0.17f;      // m
    /// <summary>
    /// How much harder the dampers resist extension (rebound) than compression (bump), as a ratio. Road dampers rebound
    /// two to three times harder than they bump (Milliken, ch. 21; Dixon, The Shock Absorber Handbook): a bump is
    /// let through softly and the spring's return is checked. <see cref="SuspensionDamping"/> is the mean of the two.
    /// </summary>
    public float DamperReboundRatio { get; init; } = 2.2f;
    /// <summary>
    /// Anti-roll bars as a wheel rate, N/m: the force pushing the more compressed wheel of an axle down (and its partner
    /// up) per metre of compression difference, so an axle's roll stiffness from its bar is this times the track
    /// squared. The front-to-rear split of the roll stiffness is the main tuning of a car's balance: the stiffer end
    /// takes more of the load transfer in a bend and, its tyres using load less well, gives up grip first.
    /// </summary>
    public float FrontAntiRoll { get; init; } = 27400f;
    public float RearAntiRoll { get; init; } = 12000f;

    // Steering
    /// <summary>
    /// Share of Ackermann geometry in the steering linkage (0.19): 0 steers both front wheels by the same angle (parallel
    /// steer, which until 0.18 every car had), 1 turns the inner wheel more, so that both wheels' axes meet on the line
    /// of the rear axle and neither scrubs sideways at parking speed. Road cars are built close to it (Gillespie,
    /// Fundamentals of Vehicle Dynamics, ch. 8); at speed the difference is a fraction of a degree.
    /// </summary>
    public float AckermannShare { get; init; } = 1f;
    /// <summary>Caster (mechanical) trail, m: the steering axis meets the road this far ahead of the contact patch; 15-30 mm on road cars.</summary>
    public float CasterTrail { get; init; } = 0.02f;
    /// <summary>
    /// Compliance of the steering at each front road wheel, rad per N m of aligning torque (0.19): the rack, the column,
    /// the mounts and the tie-rod bushes give, so the aligning torque (side force times caster and pneumatic trail) steers
    /// the wheel out of the bend a little. It is part of every road car's understeer, K_str = W_f (p + t) / K_ss in
    /// Gillespie's ch. 6, and the share that falls off with the pneumatic trail at the limit; 1 / 14000 adds about half a
    /// degree per g on the front-heavy hatchbacks and a little less on the mid-engined cars.
    /// </summary>
    public float SteeringCompliance { get; init; } = 1f / 14000f;
    public float MaxSteerLowSpeedDeg { get; init; } = 32f;
    public float MaxSteerHighSpeedDeg { get; init; } = 7f;
    public float SteerSpeedBlendMps { get; init; } = 55f;    // speed at which the high-speed limit applies

    // Brakes
    public float FrontBrakeTorque { get; init; } = 2700f;    // N m per wheel at full pedal
    /// <summary>
    /// Rear brake torque per wheel at full pedal, after the proportioning valve: set so that the front wheels lock first
    /// at the car's grip limit (the front's share of the braking a little above its share of the load in a stop at that
    /// limit, static share + h / L x mu), as brake systems are designed; a car whose rears lock first spins in a hard stop.
    /// For the front-heavy hatchbacks that leaves the rear little: in a 1.1 g stop their rear axle carries a seventh of
    /// the weight.
    /// </summary>
    public float RearBrakeTorque { get; init; } = 1700f;
    /// <summary>Anti-lock brakes: each wheel's pressure is eased when it slips past the tyre's peak, so the car stops short and still steers.</summary>
    public bool AntiLockBrakes { get; init; }

    // Drivetrain
    public float[] GearRatios { get; init; } = [-3.0f, 0f, 3.14f, 2.11f, 1.46f, 1.0f]; // index = gear + 1 (R, N, 1..4)
    public float FinalDrive { get; init; } = 3.21f;
    public float DrivelineEfficiency { get; init; } = 0.9f;
    public float ClutchMaxTorque { get; init; } = 900f;       // N m
    public float EngineInertia { get; init; } = 0.28f;        // kg m^2 (flywheel + clutch)
    /// <summary>
    /// Seconds a gear change takes in the assisted modes, the drive interrupted: about a quarter of a second for a
    /// quick hand on a manual gate, a tenth for a dual-clutch box that hands the drive from one clutch to the other.
    /// </summary>
    public float ShiftSeconds { get; init; } = 0.28f;
    public float WheelInertia { get; init; } = 1.3f;          // kg m^2 per wheel + half shaft
    /// <summary>
    /// Limited-slip differential: the share of the torque into the differential that its clutches can pass from the
    /// faster driven wheel to the slower one (0 = open differential, which always splits the torque evenly and so lets
    /// an unloaded inside wheel spin away the drive). The flat-twelve's is a 40 % clutch-type unit.
    /// </summary>
    public float LimitedSlipLocking { get; init; } = 0.4f;
    /// <summary>Torque (N m) the limited-slip clutches pass with no drive at all, from their spring preload.</summary>
    public float LimitedSlipPreload { get; init; } = 60f;

    public int ForwardGearCount => GearRatios.Length - 2;
    public float RatioForGear(int gear) => GearRatios[gear + 1];

    /// <summary>
    /// The flat-twelve keeps the defaults but for its gear change: an open metal gate and a heavy single-plate clutch,
    /// slower across the gate than a modern manual (0.19: at the default quarter second the car ran
    /// 0-100 km/h in 4.9 s, half a second under every published figure).
    /// </summary>
    public static VehicleSpec FlatTwelve => new() { ShiftSeconds = 0.45f };

    /// <summary>Mid-engined V8 of the 2010s with a sports exhaust: lighter, stiffer, 570 hp at 8000, seven gears.</summary>
    public static VehicleSpec V8Evo => new()
    {
        Name = "V8 GTB '12 SPORT EXHAUST",
        Style = CarStyle.V8,
        // The 570S's seven-speed dual-clutch box: the next gear is engaged on its own clutch and taken up as the other
        // lets go, so a shift costs a fraction of the drive a manual's does.
        ShiftSeconds = 0.03f,
        // Its launch control: brake, full throttle, release (McLaren's "Launch" mode of the Sports Series).
        LaunchControl = true,
        FrontLiftArea = -0.06f,
        RearLiftArea = -0.12f,
        // 225/35 R19 front, 285/35 R20 rear; an open differential (the real car brakes a spinning inside wheel instead).
        FrontTire = new(225f, 35f, 19f),
        RearTire = new(285f, 35f, 20f),
        FrontAntiRoll = 33300f,
        RearAntiRoll = 16300f,
        CentreOfMassOffset = new(0f, -0.19f, -0.22f),
        AntiLockBrakes = true,
        LimitedSlipLocking = 0f,
        LimitedSlipPreload = 0f,
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
        // Car and Driver's 570S held 1.05 g on its 300 ft skidpad (2016).
        TireGrip = 1.15f,
        TractionControl = true,
        SuspensionStiffness = 46000f,
        SuspensionDamping = 4200f,
        MaxSteerLowSpeedDeg = 30f,
        FrontBrakeTorque = 3200f,
        RearBrakeTorque = 1750f,
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
        FrontTire = new(205f, 45f, 16f),
        RearTire = new(205f, 45f, 16f),
        FrontAntiRoll = 16000f,
        RearAntiRoll = 9700f,
        CentreOfMassOffset = new(0f, -0.10f, 0f),
        LimitedSlipLocking = 0f,
        LimitedSlipPreload = 0f,
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
        FrontLiftArea = 0.10f,
        RearLiftArea = 0.07f,
        TireGrip = 1.08f,
        WheelRadius = 0.296f,
        SuspensionRest = 0.22f,
        SuspensionStiffness = 30000f,
        SuspensionDamping = 2900f,
        MaxCompression = 0.13f,
        MaxSteerLowSpeedDeg = 34f,
        MaxSteerHighSpeedDeg = 7.5f,
        FrontBrakeTorque = 2100f,
        RearBrakeTorque = 290f,
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
        FrontTire = new(195f, 55f, 15f),
        RearTire = new(195f, 55f, 15f),
        FrontAntiRoll = 19000f,
        RearAntiRoll = 8200f,
        CentreOfMassOffset = new(0f, -0.09f, 0f),
        LimitedSlipLocking = 0f,
        LimitedSlipPreload = 0f,
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
        FrontLiftArea = 0.10f,
        RearLiftArea = 0.07f,
        TireGrip = 1.0f,
        WheelRadius = 0.298f,
        SuspensionRest = 0.23f,
        SuspensionStiffness = 30000f,
        SuspensionDamping = 3000f,
        MaxCompression = 0.14f,
        MaxSteerLowSpeedDeg = 33f,
        MaxSteerHighSpeedDeg = 7f,
        FrontBrakeTorque = 2100f,
        RearBrakeTorque = 310f,
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
        FrontTire = new(225f, 40f, 18f),
        RearTire = new(225f, 40f, 18f),
        FrontAntiRoll = 25000f,
        RearAntiRoll = 15300f,
        CentreOfMassOffset = new(0f, -0.09f, 0f),
        AntiLockBrakes = true,
        LimitedSlipLocking = 0f,
        LimitedSlipPreload = 0f,
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
        FrontLiftArea = 0.08f,
        RearLiftArea = 0.05f,
        TireGrip = 1.08f,
        TractionControl = true,
        WheelRadius = 0.319f,
        SuspensionRest = 0.22f,
        SuspensionStiffness = 38000f,
        SuspensionDamping = 3700f,
        MaxCompression = 0.13f,
        MaxSteerLowSpeedDeg = 33f,
        MaxSteerHighSpeedDeg = 7f,
        FrontBrakeTorque = 3000f,
        RearBrakeTorque = 370f,
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
        FrontTire = new(175f, 70f, 13f),
        RearTire = new(175f, 70f, 13f),
        FrontAntiRoll = 7000f,
        RearAntiRoll = 3000f,
        CentreOfMassOffset = new(0f, -0.10f, 0f),
        LimitedSlipLocking = 0f,
        LimitedSlipPreload = 0f,
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
        FrontLiftArea = 0.16f,
        RearLiftArea = 0.10f,
        TireGrip = 0.86f,
        WheelRadius = 0.2875f,
        SuspensionRest = 0.25f,
        SuspensionStiffness = 24000f,
        SuspensionDamping = 2300f,
        MaxCompression = 0.15f,
        MaxSteerLowSpeedDeg = 33f,
        MaxSteerHighSpeedDeg = 7f,
        FrontBrakeTorque = 1500f,
        RearBrakeTorque = 500f,
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
        // 155 R13: an 80-series radial.
        FrontTire = new(155f, 80f, 13f),
        RearTire = new(155f, 80f, 13f),
        FrontAntiRoll = 7000f,
        RearAntiRoll = 2250f,
        CentreOfMassOffset = new(0f, -0.08f, 0f),
        LimitedSlipLocking = 0f,
        LimitedSlipPreload = 0f,
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
        FrontLiftArea = 0.16f,
        RearLiftArea = 0.12f,
        TireGrip = 0.82f,
        WheelRadius = 0.289f,
        SuspensionRest = 0.26f,
        SuspensionStiffness = 22000f,
        SuspensionDamping = 2100f,
        MaxCompression = 0.16f,
        MaxSteerLowSpeedDeg = 32f,
        MaxSteerHighSpeedDeg = 7f,
        FrontBrakeTorque = 1450f,
        RearBrakeTorque = 340f,
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
        FrontTire = new(195f, 65f, 15f),
        RearTire = new(195f, 65f, 15f),
        FrontAntiRoll = 7800f,
        RearAntiRoll = 3000f,
        CentreOfMassOffset = new(0f, -0.10f, 0f),
        // ESC brings anti-lock brakes with it.
        AntiLockBrakes = true,
        LimitedSlipLocking = 0f,
        LimitedSlipPreload = 0f,
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
        FrontLiftArea = 0.08f,
        RearLiftArea = 0.05f,
        TireGrip = 0.94f,
        TractionControl = true,
        WheelRadius = 0.317f,
        SuspensionRest = 0.24f,
        SuspensionStiffness = 30000f,
        SuspensionDamping = 2900f,
        MaxCompression = 0.14f,
        MaxSteerLowSpeedDeg = 34f,
        MaxSteerHighSpeedDeg = 7f,
        FrontBrakeTorque = 2100f,
        RearBrakeTorque = 400f,
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

/// <summary>
/// A four-wheel-drive car's driveline: its centre, which shares the gearbox's torque between the axles and couples their
/// speeds, and the differential of each axle. Two layouts of the time are modelled. A permanent four-wheel drive (the
/// Delta Integrale's) splits the torque in a fixed ratio through an epicyclic centre differential, and a viscous coupling
/// across it passes torque from the faster axle to the slower one, the more the further their speeds part, so an axle
/// that spins hands its drive to the one that grips. An on-demand one (the Golf R32's 4MOTION) drives the front axle
/// and engages the rear through a multi-plate coupling (a Haldex) as the front wheels outrun the rear ones.
/// </summary>
/// <remarks>
/// Torques are N m at the wheels (both wheels of an axle together) and speeds each axle's mean wheel speed, rad/s: the
/// propshaft, the final drives and the couplings' own ratios are folded into these figures.
/// </remarks>
public sealed record FourWheelDrive
{
    /// <summary>
    /// Share of the gearbox's torque the centre differential sends to the front axle while neither axle slips (an
    /// epicyclic's fixed split); 1 where the gearbox drives the front axle and only the coupling feeds the rear.
    /// </summary>
    public float FrontShare { get; init; } = 0.5f;
    public CentreCoupling Coupling { get; init; } = CentreCoupling.Viscous;
    /// <summary>Torque (N m) the coupling passes from the faster axle to the slower one per rad/s between their speeds.</summary>
    public float CouplingRate { get; init; }
    /// <summary>
    /// The most the coupling passes, as a share of the gearbox's torque (an on-demand coupling's clutch and its control);
    /// a viscous coupling has no such limit short of its hump, which is not modelled.
    /// </summary>
    public float CouplingShare { get; init; } = float.PositiveInfinity;
    /// <summary>The front axle's differential: locking and preload as <see cref="VehicleSpec.LimitedSlipLocking"/> has them, of the front axle's torque.</summary>
    public float FrontLocking { get; init; }
    public float FrontPreload { get; init; }
    /// <summary>The rear axle's differential, as the front's.</summary>
    public float RearLocking { get; init; }
    public float RearPreload { get; init; }
}

public enum CentreCoupling
{
    /// <summary>A viscous coupling across an epicyclic centre differential (Ferguson's): its torque grows with the speed difference, either way, brakes on or off.</summary>
    Viscous,
    /// <summary>
    /// An on-demand multi-plate coupling to the rear axle (Haldex's first generation): it closes as the front wheels
    /// outrun the rear ones, up to <see cref="FourWheelDrive.CouplingShare"/> of the drive, and its control opens it while
    /// the brakes are on, so the anti-lock brakes see each axle on its own.
    /// </summary>
    Haldex,
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

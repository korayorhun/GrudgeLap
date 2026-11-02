namespace GrudgeLap.Physics.Vehicle;

/// <summary>
/// Tunable description of a car. The default is a late-80s rear-wheel-drive flat-twelve sports car with the
/// original game's four-speed gearbox; <see cref="V8Evo"/> is a 2010s mid-engined V8 with a seven-speed box; three
/// hatchbacks follow the cars the game draws for them: the front-wheel-drive 205 GTi 1.9 of 1988 (<see cref="HotHatch"/>),
/// the four-wheel-drive Delta HF Integrale Evoluzione of 1992 (<see cref="FiveHatch"/>) and Golf R32 of 2003
/// (<see cref="TurboHatch"/>); and three everyday cars of Turkey's roads (0.9.1): the 1.6 rear-wheel-drive saloon of the
/// 1990s (<see cref="Saloon"/>), the 1.4 estate of the same years (<see cref="Estate"/>) and the 2.1 diesel fleet
/// saloon of the 2010s (<see cref="Fleet"/>, the E-Class the game draws). The numbers are game numbers, tuned for feel
/// from the published figures of the cars they follow (power, torque, mass, wheelbase, track, tyres, gearing), not a
/// manufacturer datasheet.
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
    /// The body's footprint for knocks against obstacles: the reference point to the front and the rear bumper and half
    /// the widest width without the mirrors (m). On the cars that follow a real one since dims6 (the three hatchbacks and
    /// the fleet saloon) the published length, split between the overhangs as the drawn model has them, and the published
    /// width; on the others as the view draws the lofted body (its nose and tail keys and widest half width).
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
    /// <summary>
    /// A brake-based differential function (dyn5): the stability system's brakes hold a driven wheel that spins ahead of
    /// its partner on the same axle, so that the open differential's torque goes on driving the wheel that grips. McLaren's
    /// brake steer on the V8, Volkswagen's EDS on the V6 hatch and Mercedes-Benz's ETS on the fleet saloon; null on a car
    /// that had none (the 205 GTi, the Delta Integrale with its viscous centre and Torsen, the flat-twelve with its
    /// clutch-type limited-slip unit, the saloon and the estate of the 1990s).
    /// </summary>
    public ElectronicDifferential? ElectronicDifferential { get; init; }
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
        // 225/35 R19 front, 285/35 R20 rear; an open differential with brake steer in place of a limited-slip unit.
        FrontTire = new(225f, 35f, 19f),
        RearTire = new(285f, 35f, 20f),
        FrontAntiRoll = 33300f,
        RearAntiRoll = 16300f,
        CentreOfMassOffset = new(0f, -0.19f, -0.22f),
        AntiLockBrakes = true,
        LimitedSlipLocking = 0f,
        LimitedSlipPreload = 0f,
        // McLaren's brake steer: the Sports Series has an open rear differential, and the stability system brakes the
        // inside rear wheel as it spins up out of a bend, so the outside one, carrying the load, takes the drive; McLaren
        // chose it over a limited-slip unit for its weight (Jalopnik, "What Is Brake Steer", 2017; McLaren's Ian Howshall
        // on the 720S, automobilefanatics.com). Its working figures are not published: the inside wheel is let slip half
        // a metre a second more than the outside one before the brake holds it (a game figure, under the 1.3 m/s the
        // flat-twelve's 40 % limited-slip unit lets through), at any speed.
        ElectronicDifferential = new ElectronicDifferential { SlipLead = 0.5f },
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
    /// The hot hatch of 1988, after the Peugeot 205 GTi 1.9 (1987-92) the game draws: the eight-valve XU9JA, 130 PS at
    /// 6000 and 161 N m at 4750 (auto-data.net's 205 GTI 1.9 page), 910 kg at the kerb (listings run from 855,
    /// auto-data.net, to 940, a Japanese catalogue), the BE1/5 box (2.92 / 1.85 / 1.36 / 1.07 / 0.86, final drive 3.69,
    /// reverse 3.33: auto motor und sport's data of a test car), 185/55 R15 tyres, discs all round, no anti-lock brakes,
    /// about 63/37. Peugeot's figures: 0-100 km/h in 7.8 s and 205 km/h.
    /// </summary>
    /// <remarks>
    /// Its size is the 205 GTi 1.9's since dims6, from auto motor und sport's data of its test car: 3705 x 1572 x 1355 mm,
    /// 2420 mm between the axles, tracks of 1382 mm in front and 1339 behind. The simulation has one track for both axles:
    /// the mean, 1.36 m (the front is 3 % wider). Until dims6 the wheelbase, the track and the footprint were the ones the
    /// drawn model had been prepared to (2.47 m, 1.46 m; tools/car_models.py scales the model to the simulation's
    /// wheelbase), which drew the car 2 % too big. The centre of mass keeps its share on the front axle (62.8 %), so it
    /// moved with the axles; the yaw and pitch inertias keep their ratio to the mass times the axles' distances from it
    /// (they fall with the square of the wheelbase), the roll inertia its ratio to the mass times the width and the height
    /// squared. The narrower track rolls the car more on the same bars (3.24 deg/g on the skid pad against 2.81), as the
    /// roll stiffness of a bar or a spring goes with the track squared. The springs follow the mass, so the car sits at the
    /// same height on them; the mass, its place, the tyres, the gearing, the engine, the brakes and the bars are the 205's.
    /// </remarks>
    public static VehicleSpec HotHatch => new()
    {
        Name = "HOT HATCH 1.9 8V '88",
        Style = CarStyle.HotHatch,
        FrontTire = new(185f, 55f, 15f),
        RearTire = new(185f, 55f, 15f),
        // The 205's rear axle carries a bar of its own on trailing arms and torsion bars: a livelier balance than the
        // later hatches, set towards the rear.
        FrontAntiRoll = 12500f,
        RearAntiRoll = 9500f,
        CentreOfMassOffset = new(0f, -0.10f, 0.03f),
        LimitedSlipLocking = 0f,
        LimitedSlipPreload = 0f,
        Engine = EngineSpec.HotHatch,
        SpeedoMaxMph = 140f,
        // 910 kg with a driver and a tank.
        MassKg = 985f,
        InertiaRoll = 315f,
        InertiaYaw = 1250f,
        InertiaPitch = 1200f,
        HalfTrack = 0.68f,
        FrontAxle = 0.931f,
        RearAxle = 1.489f,
        BodyFront = 1.587f,
        BodyRear = 2.118f,
        BodyHalfWidth = 0.786f,
        HubHeight = -0.20f,
        // Cd 0.35 (auto-data.net); 1.57 m wide and 1.36 m tall (auto motor und sport).
        DragCoefficient = 0.35f,
        FrontalArea = 1.75f,
        FrontLiftArea = 0.10f,
        RearLiftArea = 0.07f,
        // The Michelin MXV of the late 1980s: the flat-twelve's grip, short of the 2000s cars'.
        TireGrip = 1.0f,
        WheelRadius = 0.292f,
        SuspensionRest = 0.22f,
        // 1140 kg rode on 30,000 N/m before: the same sag for the model's ride height (see the remarks).
        SuspensionStiffness = 25920f,
        SuspensionDamping = 2500f,
        MaxCompression = 0.13f,
        MaxSteerLowSpeedDeg = 34f,
        MaxSteerHighSpeedDeg = 7.5f,
        FrontBrakeTorque = 1800f,
        RearBrakeTorque = 250f,
        GearRatios = [-3.33f, 0f, 2.92f, 1.85f, 1.36f, 1.07f, 0.86f],
        FinalDrive = 3.69f,
        ClutchMaxTorque = 290f,
        EngineInertia = 0.13f,
        WheelInertia = 0.8f,
        FrontWheelDrive = true,
    };

    /// <summary>
    /// The rally hatch of 1992, after the Lancia Delta HF Integrale Evoluzione (1991-93) the game draws: the sixteen-valve
    /// two-litre with a Garrett T3, 210 PS at 5750 and 304 N m at 3500 on 1 bar of boost, the five-speed box (3.500 /
    /// 2.176 / 1.523 / 1.156 / 0.916, final drive 3.111), 205/50 ZR15 tyres, Bosch anti-lock brakes and permanent
    /// four-wheel drive: an epicyclic centre differential sending 47 % to the front and 53 % to the rear with a Ferguson
    /// viscous coupling across it, an open front differential and a Torsen at the rear (rallycars.com's spec of the
    /// street Evoluzione I). 1340 kg at the kerb (1300 DIN, ultimatespecs.com). Lancia's figures: 0-100 km/h in 5.7 s,
    /// 220 km/h.
    /// </summary>
    /// <remarks>
    /// Its size is the Evoluzione's since dims6 (supercars.net's specification of the 1991 car, which auto-data.net's
    /// agrees with): 3900 x 1770 x 1365 mm, 2480 mm between the axles, tracks of 1502 mm in front and 1500 behind
    /// (1506 / 1504 for the Evoluzione II of 1993). Until dims6 it had the drawn model's 2.54 m and 1.46 m; the centre of
    /// mass, the inertias and the footprint follow as the hot hatch's remarks say. The wider track rolls it less (2.91
    /// deg/g against 3.07).
    /// </remarks>
    public static VehicleSpec FiveHatch => new()
    {
        Name = "RALLY HATCH 2.0 TURBO 4WD '92",
        Style = CarStyle.FiveHatch,
        FrontTire = new(205f, 50f, 15f),
        RearTire = new(205f, 50f, 15f),
        FrontAntiRoll = 20000f,
        RearAntiRoll = 10500f,
        // About 60/40: no measured axle loads were found; the transverse engine hangs ahead of the front axle, the
        // propshaft and the rear differential sit behind, so a little less in front than the front-wheel-drive hatches.
        CentreOfMassOffset = new(0f, -0.09f, -0.048f),
        AntiLockBrakes = true,
        FourWheelDrive = new FourWheelDrive
        {
            FrontShare = 0.47f,
            Coupling = CentreCoupling.Viscous,
            // A game figure, as no published one was found: an axle spinning a metre a second faster than the other
            // (3.4 rad/s) hands about 600 N m to the one that grips.
            CouplingRate = 180f,
            // A Torsen biasing about 2.3 to 1 (70/30), the share given to the slower wheel: (2.3 - 1) / (2 x 3.3).
            RearLocking = 0.2f,
        },
        Engine = EngineSpec.RallyTurboFour,
        SpeedoMaxMph = 160f,
        // 1300 kg DIN with a driver.
        MassKg = 1375f,
        InertiaRoll = 455f,
        InertiaYaw = 1750f,
        InertiaPitch = 1700f,
        HalfTrack = 0.75f,
        FrontAxle = 0.928f,
        RearAxle = 1.552f,
        BodyFront = 1.719f,
        BodyRear = 2.181f,
        BodyHalfWidth = 0.885f,
        HubHeight = -0.20f,
        // No published drag figure was found: the square body of 1979 with its flared arches, 1.77 m by 1.365.
        DragCoefficient = 0.38f,
        FrontalArea = 1.95f,
        FrontLiftArea = 0.08f,
        RearLiftArea = 0.04f,
        TireGrip = 1.0f,
        WheelRadius = 0.293f,
        SuspensionRest = 0.23f,
        // 1310 kg rode on 30,000 N/m before: the same sag (see the hot hatch's remarks).
        SuspensionStiffness = 31490f,
        SuspensionDamping = 3200f,
        MaxCompression = 0.14f,
        MaxSteerLowSpeedDeg = 33f,
        MaxSteerHighSpeedDeg = 7f,
        // 281 mm ventilated discs in front with four-pot callipers, 251 mm discs behind.
        FrontBrakeTorque = 2400f,
        RearBrakeTorque = 450f,
        GearRatios = [-3.545f, 0f, 3.5f, 2.176f, 1.523f, 1.156f, 0.916f],
        FinalDrive = 3.111f,
        // The transfer gears and the rear final drive cost a little more than a front-wheel-drive box.
        DrivelineEfficiency = 0.89f,
        ClutchMaxTorque = 480f,
        EngineInertia = 0.12f,
        WheelInertia = 0.9f,
    };

    /// <summary>
    /// The V6 hatch of 2003, after the Golf R32 (2002-04) the game draws: the 3.2-litre VR6, 241 PS at 6250 and 320 N m
    /// from 2800 to 3200, the six-speed 02M (code FMP: 3.357 / 2.087 / 1.469 / 1.098 on a 4.235 final drive, 1.108 /
    /// 0.927 on 3.273; VW's workshop manual), 225/40 R18 tyres, ESP with anti-lock brakes and traction control, and
    /// 4MOTION: a Haldex coupling engages the rear axle as the front wheels slip, the car otherwise driving its fronts.
    /// 1477 kg as VW quoted it (3409 lb, 1546 kg, at the kerb for the US car of 2004, Edmunds). VW's figures: 0-100
    /// km/h in 6.6 s, 247 km/h.
    /// </summary>
    /// <remarks>
    /// Its size is the R32's since dims6 (auto-data.net's and CarExpert's listings of the 2003 car, Ultimatecarpage's
    /// alike): 4149 x 1735 x 1444 mm, 2518 mm between the axles, tracks of 1513 mm in front and 1487 behind, one mean
    /// track of 1.50 m here. Until dims6 it had the drawn model's 2.58 m and 1.53 m; the centre of mass, the inertias and
    /// the footprint follow as the hot hatch's remarks say.
    /// </remarks>
    public static VehicleSpec TurboHatch => new()
    {
        Name = "V6 HATCH 3.2 4WD '03",
        Style = CarStyle.TurboHatch,
        FrontTire = new(225f, 40f, 18f),
        RearTire = new(225f, 40f, 18f),
        FrontAntiRoll = 26200f,
        RearAntiRoll = 16000f,
        // About 61/39: no measured axle loads were found; the VR6 is heavy and sits ahead of the axle, the Haldex unit
        // and the rear differential behind.
        CentreOfMassOffset = new(0f, -0.09f, -0.03f),
        AntiLockBrakes = true,
        FourWheelDrive = new FourWheelDrive
        {
            FrontShare = 1f,
            Coupling = CentreCoupling.Haldex,
            // Game figures, as none are published: past a lead of 3 % (the fronts' slip under a part-throttle pull stays
            // below it) the first Haldex closes within a fraction of a turn between its shafts, a fifth of a metre a
            // second more (0.6 rad/s) passing its fill. Its clutch pack holds 2500 N m at the rear wheels, about what
            // the rear tyres put down on a dry launch, so once closed it ties the axles together (a first-generation
            // Haldex can lock to an even split, jcr-leeds.com's Gen 1 guide) and the tyres' grip, not the coupling,
            // shares the drive.
            CouplingRate = 4000f,
            CouplingCapacity = 2500f,
            CouplingLead = 0.03f,
        },
        LimitedSlipLocking = 0f,
        LimitedSlipPreload = 0f,
        // EDS (EDL), the electronic differential lock of the Bosch ABS/ESP of the time: a driven wheel turning about
        // 100 rpm faster than the other of its axle is braked, at road speeds up to 40 km/h (the Golf IV's handbook, as
        // vwmanual.ru gives it; the Audi A6 C5's handbook says the same); on a four-wheel-drive car it acts on all four
        // wheels and, once in, lets go only at 80 km/h (the Audi A4 B5's workshop description of its EDL; a Golf with
        // 4MOTION has the same ABS control unit "with EDL/TCS/ESP", Volkswagen's SSP 206). 100 rpm on the 0.319 m tyre
        // is 3.3 m/s.
        ElectronicDifferential = new ElectronicDifferential { SlipLead = 3.3f, EngageBelowKmh = 40f, CutOutKmh = 80f },
        Engine = EngineSpec.VrSix,
        SpeedoMaxMph = 160f,
        // With a driver and a tank: between VW's 1477 kg and the US car's 1546.
        MassKg = 1550f,
        InertiaRoll = 480f,
        InertiaYaw = 2000f,
        InertiaPitch = 1945f,
        HalfTrack = 0.75f,
        FrontAxle = 0.956f,
        RearAxle = 1.562f,
        BodyFront = 1.811f,
        BodyRear = 2.338f,
        BodyHalfWidth = 0.868f,
        HubHeight = -0.20f,
        // Cd 0.32 on 2.22 m^2 for the R32 of 2005 (kfz-tech.de); the Mk4's body is a little lower.
        DragCoefficient = 0.32f,
        FrontalArea = 2.15f,
        FrontLiftArea = 0.08f,
        RearLiftArea = 0.05f,
        TireGrip = 1.08f,
        TractionControl = true,
        WheelRadius = 0.319f,
        SuspensionRest = 0.22f,
        // 1480 kg rode on 38,000 N/m before: the same sag (see the hot hatch's remarks).
        SuspensionStiffness = 39800f,
        SuspensionDamping = 3870f,
        MaxCompression = 0.13f,
        MaxSteerLowSpeedDeg = 33f,
        MaxSteerHighSpeedDeg = 7f,
        FrontBrakeTorque = 3100f,
        RearBrakeTorque = 420f,
        // The 02M drives gears 1-4 and 5-6 through two final drives; 5th and 6th are given on the first one (1.108 x
        // 3.273 / 4.235, 0.927 x 3.273 / 4.235), as is reverse (3.990).
        GearRatios = [-3.084f, 0f, 3.357f, 2.087f, 1.469f, 1.098f, 0.856f, 0.716f],
        FinalDrive = 4.235f,
        // The Haldex unit, the propshaft and the rear final drive cost a little more than a front-wheel-drive box.
        DrivelineEfficiency = 0.89f,
        ClutchMaxTorque = 520f,
        // The VR6's dual-mass flywheel.
        EngineInertia = 0.17f,
        WheelInertia = 1.1f,
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
    /// The fleet saloon of 2014, after the Mercedes-Benz E-Class W212 the game draws, as one of the OM 651 diesels of the
    /// taxi ranks and company fleets of Europe and Turkey: the E 250 CDI of the facelift (2013-15), the 2.1-litre diesel
    /// four with a two-stage turbocharger, 204 PS at 3800 and 500 N m from 1600 to 1800, rear-wheel drive through the
    /// six-speed manual that was its standard gearbox, 205/60 R16 tyres, ESP with anti-lock brakes and ETS, 1,765 kg,
    /// 0-100 km/h in 7.7 s and 240 km/h. The sources: thecarspec's listing of the W212 facelift E 250 CDI (the power and
    /// torque, the tyres, the size, 1,765 kg, 7.7 s and 240 km/h), cars-data's of the 2013 saloon with the manual (7.7 s,
    /// 240 km/h; 7.5 s and 242 km/h with the automatic), auto motor und sport's technical data of the E 250 CDI 4MATIC (the
    /// rated speed 3800, and a final drive of 2.47 behind its 7G-TRONIC) and of its test of the C 250 CDI (W204, 03/2011:
    /// the same OM 651 at 204 PS behind the six-speed manual, 5.10 / 2.78 / 1.75 / 1.25 / 1.00 / 0.81, reverse 4.62, final
    /// drive 2.47). No gearing was found published for the manual E-Class: the C's gearbox is taken as its, and the final
    /// drive the C and the automatic E share is assumed for it.
    /// </summary>
    /// <remarks>
    /// The seven-speed 7G-TRONIC PLUS was the option most of these cars were bought with (0-100 km/h in 7.5 s, 242 km/h);
    /// the game's driveline is a clutch and a gearbox on every car, its automatic mode shifting them for the driver, and
    /// a torque converter is not modelled, so the car has the manual it was listed with. Its size is the W212 saloon's
    /// since dims6 (thecarspec's and cars-data's listings of the facelift E 250 CDI): 4879 x 1854 x 1474 mm, 2874 mm
    /// between the axles, tracks of 1598 mm in front and 1604 behind (cars-data; 1614 at thecarspec), one track of 1.60 m
    /// here. Until dims6 the wheelbase, the track and the footprint were the Egea's it followed before (2.636 m, 1.542 m),
    /// to which the drawn model had been prepared, so the E-Class was drawn at 92 % of its size; the centre of mass, the
    /// inertias and the footprint follow as the hot hatch's remarks say. The longer wheelbase moves less weight in a stop
    /// and a launch (75 % on the front wheels at 1.1 g, 77 before; 7.92 s to 100 km/h against 7.87) and the wider track
    /// rolls it less (3.29 deg/g against 3.54). The springs follow the mass so the car sits at the same height on them;
    /// the mass, its place, the tyres, the gearing, the engine, the brakes and the bars are the E 250 CDI's.
    /// </remarks>
    public static VehicleSpec Fleet => new()
    {
        Name = "SALOON 2.1 DIESEL '14",
        Style = CarStyle.Fleet,
        // 205/60 R16 on the E 250 CDI of 2013 (the 225/55 R16 of 2009 gave way to them in 2011).
        FrontTire = new(205f, 60f, 16f),
        RearTire = new(205f, 60f, 16f),
        // Torsion-bar stabilisers on both multi-link axles; the balance set towards the front, as on a comfortable saloon
        // that understeers at the limit.
        FrontAntiRoll = 15000f,
        RearAntiRoll = 7000f,
        // About 53/47: auto motor und sport weighed the E 250 CGI Coupe of the same platform at 53.4/46.6 (its test of
        // 03/2010); no axle loads were found for the diesel saloon.
        CentreOfMassOffset = new(0f, -0.10f, -0.23f),
        // ESP brings anti-lock brakes and traction control with it.
        AntiLockBrakes = true,
        LimitedSlipLocking = 0f,
        LimitedSlipPreload = 0f,
        // ETS, the traction control within ESP: it "brakes the drive wheels individually if they spin ... for example if
        // the road surface is slippery on one side. In addition, more drive torque is transferred to the wheel or wheels
        // with traction", and it stays on with ESP switched off (the W212's operator's manual, Mercedes-Benz USA's of the
        // 2016 E-Class saloon and estate, p. 81). It is the brake function the Egea's BLD was. Mercedes gives no threshold
        // or speed limit: a rear wheel is let slip 0.8 m/s more than the other before the brake holds it, at any speed (a
        // game figure; at the BLD's 1.0 the brake held the inside rear out of a tight bend to 1.16 m/s ahead, barely
        // under the 1.36 the traction control alone allows, where the handbook has it hand the drive across).
        ElectronicDifferential = new ElectronicDifferential { SlipLead = 0.8f },
        Engine = EngineSpec.DieselFour,
        // Its speedometer reads to 260 km/h.
        SpeedoMaxMph = 162f,
        // thecarspec's 1,765 kg, which is auto-data's kerb weight of the manual saloon (1,690 kg, without a driver) with the
        // EU's 75 kg for the driver and luggage, the tank 90 % full; rounded up to 1,770. cars-data lists 1,665-1,675 kg
        // without a driver for other trims.
        MassKg = 1770f,
        InertiaRoll = 710f,
        InertiaYaw = 3685f,
        InertiaPitch = 3600f,
        HalfTrack = 0.80f,
        FrontAxle = 1.121f,
        RearAxle = 1.753f,
        BodyFront = 1.984f,
        BodyRear = 2.895f,
        BodyHalfWidth = 0.927f,
        HubHeight = -0.20f,
        // The W212 saloon is given as Cd 0.25 in its most economical trims (auto-data's facelift E 200 CDI; the saloon's
        // launch figure of 2009); no figure was found for the E 250 CDI, set a hundredth higher (a game figure). 1.85 m wide
        // and 1.47 m tall (thecarspec).
        DragCoefficient = 0.26f,
        FrontalArea = 2.30f,
        FrontLiftArea = 0.06f,
        RearLiftArea = 0.04f,
        // Touring tyres of the 2010s: a little more grip than the Egea's economy tyres had (0.94) and less than the R32's
        // 18 in sports tyres (1.08); it stops from 100 km/h in 37.7 m (see HandlingTests).
        TireGrip = 1.04f,
        TractionControl = true,
        WheelRadius = 0.326f,
        SuspensionRest = 0.24f,
        // 1170 kg rode on 30,000 N/m before: the same sag (see the remarks).
        SuspensionStiffness = 45385f,
        SuspensionDamping = 4400f,
        MaxCompression = 0.14f,
        MaxSteerLowSpeedDeg = 34f,
        MaxSteerHighSpeedDeg = 7f,
        // Ventilated discs in front, solid discs behind.
        FrontBrakeTorque = 3000f,
        RearBrakeTorque = 850f,
        GearRatios = [-4.62f, 0f, 5.10f, 2.78f, 1.75f, 1.25f, 1.00f, 0.81f],
        FinalDrive = 2.47f,
        ClutchMaxTorque = 800f,
        // The OM 651's dual-mass flywheel.
        EngineInertia = 0.17f,
        WheelInertia = 1.1f,
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
    /// The most torque (N m) the coupling passes, whatever the speed difference: an on-demand coupling's clutch pack at
    /// its full pressure. A viscous coupling has no such limit short of its hump, which is not modelled.
    /// </summary>
    public float CouplingCapacity { get; init; } = float.PositiveInfinity;
    /// <summary>
    /// How far the front axle may run ahead of the rear (or behind it), as a share of the rear's speed, before an
    /// on-demand coupling closes: its control leaves the tyres' slip of a part-throttle pull and the fronts' longer path
    /// through a bend alone, so the car drives its fronts until they really slip. Zero for a viscous coupling.
    /// </summary>
    public float CouplingLead { get; init; }
    /// <summary>The front axle's differential: locking and preload as <see cref="VehicleSpec.LimitedSlipLocking"/> has them, of the front axle's torque.</summary>
    public float FrontLocking { get; init; }
    public float FrontPreload { get; init; }
    /// <summary>The rear axle's differential, as the front's.</summary>
    public float RearLocking { get; init; }
    public float RearPreload { get; init; }
}

/// <summary>
/// A brake-based differential function: the stability system's modulator brakes a driven wheel that spins ahead of its
/// partner on the same axle. An open differential sends both wheels the same torque, so one that spins away on less grip
/// takes the engine to its limiter (or its traction control to a cut) and leaves the other with little; held by its brake,
/// it gives the differential something to push against and the wheel that grips keeps its share. It acts on every
/// driven axle and stands aside while the driver brakes.
/// </summary>
/// <remarks>
/// The brake is a closed loop on the difference between the two wheels' slip speeds (each tyre's speed over the road
/// less the road's under it, so a bend's longer outside path is not taken for spin), as a system with wheel-speed and
/// yaw-rate sensors reckons it: the brake builds while one wheel leads by more than <see cref="SlipLead"/> and holds it
/// there, up to that wheel's brake torque at full pedal, as long as the other wheel grips: once both spin, cutting the
/// engine is the traction control's job.
/// </remarks>
public sealed record ElectronicDifferential
{
    /// <summary>How far a driven wheel's slip speed may run ahead of its partner's before the brake holds it, m/s.</summary>
    public float SlipLead { get; init; }
    /// <summary>Road speed (km/h) up to which the function comes in; infinite where it works at any speed.</summary>
    public float EngageBelowKmh { get; init; } = float.PositiveInfinity;
    /// <summary>Road speed (km/h) at which a function that came in lets go.</summary>
    public float CutOutKmh { get; init; } = float.PositiveInfinity;
}

public enum CentreCoupling
{
    /// <summary>A viscous coupling across an epicyclic centre differential (Ferguson's): its torque grows with the speed difference, either way, brakes on or off.</summary>
    Viscous,
    /// <summary>
    /// An on-demand multi-plate coupling to the rear axle (Haldex's first generation): it closes as the front wheels
    /// outrun the rear ones, up to its clutch pack's <see cref="FourWheelDrive.CouplingCapacity"/>, and its control opens it while
    /// the brakes are on, so the anti-lock brakes see each axle on its own.
    /// </summary>
    Haldex,
}

public enum CarStyle
{
    FlatTwelve,
    V8,
    /// <summary>The front-wheel-drive hot hatch of the late 1980s (after the Peugeot 205 GTi 1.9).</summary>
    HotHatch,
    /// <summary>The four-wheel-drive rally hatch of the early 1990s (after the Lancia Delta HF Integrale Evoluzione); the key keeps the name of the five-cylinder car it once was.</summary>
    FiveHatch,
    /// <summary>The four-wheel-drive V6 hatch of the 2000s (after the Golf R32); the key keeps the name of the turbo car it once was.</summary>
    TurboHatch,
    /// <summary>The 1.6 rear-wheel-drive saloon of the 1990s (after the Tofas Sahin).</summary>
    Saloon,
    /// <summary>The 1.4 front-wheel-drive estate of the 1990s (after the Renault 12 Toros).</summary>
    Estate,
    /// <summary>
    /// The 2.1 diesel rear-wheel-drive fleet saloon of the 2010s (after the Mercedes-Benz E 250 CDI, W212, the model the
    /// game draws; a front-wheel-drive 1.4 after the Fiat Egea before).
    /// </summary>
    Fleet,
}

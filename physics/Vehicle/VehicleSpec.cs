namespace GrudgeLap.Physics.Vehicle;

/// <summary>
/// Tunable description of a car. The default is a late-80s rear-wheel-drive flat-twelve sports car with the
/// original game's four-speed gearbox; <see cref="V8Evo"/> is a 2010s mid-engined V8 with a seven-speed box; three
/// hatchbacks follow the cars the game draws for them: the front-wheel-drive 205 GTi 1.9 of 1988 (<see cref="HotHatch"/>),
/// the four-wheel-drive Delta HF Integrale Evoluzione of 1992 (<see cref="FiveHatch"/>) and Golf R32 of 2003
/// (<see cref="TurboHatch"/>); and three everyday cars of Turkey's roads (0.9.1): the 1.6 rear-wheel-drive saloon of the
/// 1990s (<see cref="Saloon"/>), the 1.4 estate of the same years (<see cref="Estate"/>) and the 2.1 diesel fleet
/// saloon of the 2010s (<see cref="Fleet"/>, the E-Class the game draws); and five cars of cars13, appended after them
/// so saved choices keep their index: the flat-plane V8 of 2010 (<see cref="FlatPlaneV8"/>, after the Ferrari 458
/// Italia), the five-cylinder and the 1.6 hatchbacks of 2001 (<see cref="HgtFive"/> and <see cref="HatchSixteen"/>,
/// after the Fiat Bravo HGT 2.0 20V and 1.6 16V), the hot hatch of 2018 (<see cref="CupraTsi"/>, after the SEAT Leon
/// Cupra with the 245 PS tune of its platform) and the coupe of 2013 (<see cref="Boxer"/>, after the Subaru BRZ). The
/// numbers are game numbers, tuned
/// for feel from the published figures of the cars they follow (power, torque, mass, wheelbase, track, tyres, gearing),
/// not a manufacturer datasheet.
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
    /// <summary>The roof's height over the road at rest (m): the top of the body box the ground holds a wreck up by (VehicleSim.BodyContacts, 0.35).</summary>
    public float RoofHeight { get; init; } = 1.3f;
    /// <summary>
    /// The body's footprint for knocks against obstacles: the reference point to the front and the rear bumper and half
    /// the widest width without the mirrors (m). On the cars that follow a real one since dims6 (the three hatchbacks and
    /// the fleet saloon) the published length, split between the overhangs as the drawn model has them, and the published
    /// width, or the drawn body's where the model is wider than the car (the 205's and the E-Class's: a knock comes when
    /// the drawn side meets the wall, not 2-3 cm into it); on the others as the view draws the lofted body (its nose and
    /// tail keys and widest half width).
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
    /// <summary>
    /// The side's area times its cross-flow coefficient, m^2 (0.61): what a crosswind pushes on, with half the air's
    /// density times the air's sideways speed past the car times its whole speed. A saloon's side is some 5 m^2 and a
    /// bluff body's coefficient across the flow near 0.8 (Hucho, ch. 4): a 10 m/s crosswind at 100 km/h pushes a
    /// ton of car with some 700 N.
    /// </summary>
    public float SideForceArea { get; init; } = 4f;
    /// <summary>
    /// Where the side force acts, metres ahead of the centre of mass (0.61): a car's centre of pressure sideways lies
    /// forward of its centre of mass, so a gust turns its nose away from the wind as it pushes it across the road.
    /// </summary>
    public float SideForceAhead { get; init; } = 0.35f;
    public float RollingResistance { get; init; } = 0.015f;
    /// <summary>
    /// The road load's growth with speed beyond the air's drag (<see cref="DragCoefficient"/> times <see cref="FrontalArea"/>)
    /// and the constant <see cref="RollingResistance"/> (0.76): N per m/s, and N per (m/s)^2. A coast-down's road load
    /// is A + B v + C v^2, and the tyres' losses, which rise with speed, and the driveline's churning behind the gearbox
    /// sit in its B and in the part of its C the published drag coefficient does not explain. Zero on a car whose road load
    /// is only its drag and its rolling resistance (every car but the BRZ, whose road load is the EPA's coast-down of it).
    /// The force is shared between the tyres by their load, as the rolling resistance is.
    /// </summary>
    public float RoadLoadPerSpeed { get; init; }
    public float RoadLoadPerSpeedSquared { get; init; }
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
    /// <summary>
    /// Wheel rates (N/m at the wheel) of each axle's springs where they are set apart (0.76), so that each end rides at
    /// its own frequency; NaN on a car whose axles are sprung from <see cref="SuspensionStiffness"/> by their share of
    /// the weight (every car but the BRZ), which gives both ends one frequency. The springs are preloaded so the car
    /// still sits at the height <see cref="SuspensionStiffness"/> gives it (the drawn models and the model preparations'
    /// copies of the geometry are set to that height), as a strut's spring sits preloaded on its seat at full droop.
    /// </summary>
    public float FrontWheelRate { get; init; } = float.NaN;
    public float RearWheelRate { get; init; } = float.NaN;

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
    /// <summary>
    /// Seconds the brakes' pressure takes to rise to what the pedal asks (0.76): the booster, the lines and the callipers'
    /// fill between the foot and the full stop, which every published stopping distance includes, as it is timed from the
    /// pedal. Zero on a car whose brakes act at once (every car but the BRZ: their grip was set against their stops with
    /// it so); the pressure falls with the pedal at once.
    /// </summary>
    public float BrakeBuildUpSeconds { get; init; }

    /// <summary>
    /// The speed limiter (cars13): the road speed (km/h) past which the engine management takes the throttle away and
    /// holds the car, as the German makers' agreement holds most of their cars of the 2000s and 2010s to 250 km/h and
    /// Volkswagen's group cars with them; infinite on a car without one (every car but the Leon Cupra, whose published
    /// 250 km/h is the limiter's, not the car's). The ease is smooth, over the last 3 km/h under the limit
    /// (Drivetrain.Step), the way the real limiters fade the fuel in: no rev limiter's cut, nothing for the sound to
    /// stutter on. Managed only: the native ABI does not carry it.
    /// </summary>
    public float SpeedLimiterKmh { get; init; } = float.PositiveInfinity;

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

    /// <summary>
    /// The car's own automatic gearbox, a torque converter and its own gear set (0.76), which it drives through in
    /// <see cref="TransmissionMode.Automatic"/>; null on a car whose automatic mode shifts its manual gearbox and works its
    /// clutch for the driver (every car but the BRZ). The ratios above stay the manual gearbox's, which the manual modes use.
    /// </summary>
    public AutomaticGearbox? Automatic { get; init; }

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
        // The 205's 1572 mm would be 0.786; its drawn model is 1.64 m across the body under the mirrors (its rear tyres
        // reach 0.855 out).
        BodyHalfWidth = 0.82f,
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
    /// The seven-speed 7G-TRONIC PLUS was the option most of these cars were bought with (0-100 km/h in 7.5 s, 242
    /// km/h); the game's driveline is a clutch and a gearbox on every car, its automatic mode shifting them for the
    /// driver, and a torque converter is not modelled, so the car has the manual it was listed with. Its size is the
    /// W212 saloon's since dims6 (thecarspec's and cars-data's listings of the facelift E 250 CDI): 4879 x 1854 x 1474
    /// mm, 2874 mm between the axles, tracks of 1598 mm in front and 1614 behind (auto-data.net, thecarspec and
    /// cararac; 1604 at cars-data), one track of 1.60 m here (the mean, 1.606, within 0.4 %). Until dims6 the
    /// wheelbase, the track and the footprint were the Egea's it followed before (2.636 m, 1.542 m), to which the drawn
    /// model had been prepared, so the E-Class was drawn at 92 % of its size; the centre of mass, the inertias and the
    /// footprint follow as the hot hatch's remarks say. The longer wheelbase moves less weight in a stop and a launch
    /// (75 % on the front wheels at 1.1 g, 77 before; 7.92 s to 100 km/h against 7.87) and the wider track rolls it
    /// less (3.29 deg/g against 3.54). The springs follow the mass so the car sits at the same height on them; the
    /// mass, its place, the tyres, the gearing, the engine, the brakes and the bars are the E 250 CDI's.
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
        // The W212's 1854 mm would be 0.927; its drawn model is 1.89 m across the body under the mirrors.
        BodyHalfWidth = 0.945f,
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

    /// <summary>
    /// The mid-engined V8 of 2010, after the Ferrari 458 Italia (2009-15): the 4.5-litre flat-plane V8, 570 CV at 9000
    /// and 540 N m at 6000, the seven-speed Getrag dual-clutch gearbox (3.08 / 2.19 / 1.63 / 1.29 / 1.03 / 0.84 / 0.69 on
    /// a 5.14 final drive: carspecs.us's 2010 listing and Wikipedia's 458 page, which has 2.18 for second), the E-Diff
    /// with F1-Trac, 235/35 ZR20 front and 295/35 ZR20 rear tyres, carbon-ceramic brakes (398 mm in front, 360 behind)
    /// with ABS, 1380 kg dry, 1485 kg at the kerb, 42/58 (Ferrari's press release of 2009, as conceptcarz.com carries it;
    /// autozine.org's archive page on the 458). Ferrari's figures: 0-100 km/h in under 3.4 s, over 325 km/h, 100-0 km/h
    /// in 32.5 m, Cd 0.33 with 140 kg of downforce at 200 km/h (360 kg at top speed, split 41/59 front to rear).
    /// </summary>
    /// <remarks>
    /// Its size is the 458's: 4527 x 1937 x 1213 mm, 2650 mm between the axles, tracks of 1672 mm in front and 1606
    /// behind (Ferrari's technical sheet), one track of 1.64 m here (the mean, as on every car). The overhangs are split
    /// as the real car's are (about 1030 mm in front, 847 behind). The car is drawn by its own model with the 570S's
    /// cabin since cars13 stage 2; a build without the models draws the V8's loft, fitted to these axles
    /// (CarStyle.FlatPlaneV8). The E-Diff: a multi-plate clutch across the rear differential pressed
    /// by a hydraulic actuator the ECU commands from open to locked, with the traction control (F1-Trac) in the same ECU
    /// deciding both from the car's state (Ferrari's description of the 458's E-Diff 3; the semi-active differential
    /// Ferrari, Magna Steyr and the Politecnico di Milano developed for the F430, IFAC World Congress 2005). The game's
    /// differential is the clutch-type unit the flat-twelve has: the E-Diff's power-on strategy is taken as a locking
    /// share of 35 % of the drive torque (a game figure, since Ferrari publishes no lock figure; a little under the
    /// flat-twelve's 40 % plate unit, because the real strategy opens the clutch on turn-in, which the model does not)
    /// and no preload, as the clutch is open without pressure. F1-Trac is the traction control, and the launch control
    /// the 458 had (the dual-clutch gearbox's launch mode) is <see cref="LaunchControl"/>.
    /// </remarks>
    public static VehicleSpec FlatPlaneV8 => new()
    {
        Name = "V8 4.5 '10",
        Style = CarStyle.FlatPlaneV8,
        // Getrag's seven-speed dual-clutch box: a shift costs what the 570S's does (the same make of gearbox).
        ShiftSeconds = 0.03f,
        LaunchControl = true,
        // 140 kg at 200 km/h, 41 % of it over the front axle: lift areas of -0.30 and -0.44 m^2 together make 1373 N at
        // 55.6 m/s (0.5 x 1.2 x 55.6^2 x 0.74), and 370 kg at 325 km/h, Ferrari's 360.
        FrontLiftArea = -0.30f,
        RearLiftArea = -0.44f,
        FrontTire = new(235f, 35f, 20f),
        RearTire = new(295f, 35f, 20f),
        // A little stiffer than the 570S's bars, for 75 kg more and a car Ferrari set up for the track.
        FrontAntiRoll = 34000f,
        RearAntiRoll = 17000f,
        // 42/58: the centre of mass 0.212 m behind the middle of the 2.65 m wheelbase, 0.19 m under the reference point
        // as the V8's (a mid-engined car of 1.21 m).
        CentreOfMassOffset = new(0f, -0.19f, -0.212f),
        AntiLockBrakes = true,
        // The E-Diff (see the remarks).
        LimitedSlipLocking = 0.35f,
        LimitedSlipPreload = 0f,
        Engine = EngineSpec.FlatPlaneV8,
        SpeedoMaxMph = 220f,
        // 1485 kg at the kerb with a driver.
        MassKg = 1560f,
        // The V8's inertias at 1560 kg, the same wheelbase.
        InertiaRoll = 505f,
        InertiaYaw = 2260f,
        InertiaPitch = 2050f,
        HalfTrack = 0.82f,
        FrontAxle = 1.325f,
        RearAxle = 1.325f,
        BodyFront = 2.355f,
        BodyRear = 2.172f,
        BodyHalfWidth = 0.9685f,
        HubHeight = -0.12f,
        DragCoefficient = 0.33f,
        // 1.94 m wide and 1.21 m tall.
        FrontalArea = 2.05f,
        // Automobile Magazine's 458 held 1.04-1.08 g on its cornering test (2010); the 570S's 1.05 g is 1.15 here.
        TireGrip = 1.17f,
        TractionControl = true,
        SuspensionStiffness = 48000f,
        SuspensionDamping = 4300f,
        MaxSteerLowSpeedDeg = 30f,
        // Carbon-ceramic discs of 398 mm in front and 360 behind, six- and four-piston callipers: Ferrari's 32.5 m from
        // 100 km/h.
        FrontBrakeTorque = 3400f,
        RearBrakeTorque = 1900f,
        // Reverse is not published: a game figure.
        GearRatios = [-3.0f, 0f, 3.08f, 2.19f, 1.63f, 1.29f, 1.03f, 0.84f, 0.69f],
        FinalDrive = 5.14f,
        ClutchMaxTorque = 1200f,
        // A flat-plane crank and a light flywheel behind a dual-clutch box.
        EngineInertia = 0.18f,
        // 295/35 R20 behind (0.357 m), 235/35 R20 in front (0.336): one radius here, between the two, so a model drawn
        // with both sits on the road (tools/car_models.py sets the hubs at their mean). It is also about the driven tyre's
        // rolling radius under load (a tyre rolls on about 97 % of its free radius: 0.346), which sets the speed of each
        // gear: 330 km/h at 9000 rpm in seventh, so the real car's "over 325" is within a few per cent of its cut too.
        // Here it tops out at 323 km/h on the cut, the model's road load not growing with speed (design audit item 23);
        // on the free 0.357 it ran to 335 km/h, still on the cut (cars13 review).
        WheelRadius = 0.345f,
        // A rear corner's rotating parts: a 295/35 ZR20 tyre of about 12.5 kg turning at a radius of gyration of about
        // 0.32 m (1.3 kg m^2), a forged 20 x 10.5 in wheel of about 11 kg (0.45), the 360 mm carbon-ceramic disc, the hub
        // and the half shaft (0.1): about 1.8 kg m^2, a game estimate (Ferrari publishes no inertias; the front corners,
        // on 235/35 tyres, are nearer 1.5, but the model has one figure and the driven wheels' sets the drive). At the
        // default 1.3 the rear wheels' speeds alternated against each other at the step rate for 31 steps launching out
        // of a tight turn (launch control, 60 % steer; the 570S's 5: cars13 review); at 1.8, for 1.
        WheelInertia = 1.8f,
    };

    /// <summary>
    /// The five-cylinder hatch of 2001, after the Fiat Bravo HGT 2.0 20V of the 1998-2001 facelift (the 155 PS car): the
    /// 1,998 cc twenty-valve five, 155 PS at 6500 and 186 N m at 3750, the five-speed C.510 box (3.55 / 2.24 / 1.52 /
    /// 1.16 / 0.95: cars-data.com's listing of the 1998 car; final drive 3.56: carfolio.com) to an open front
    /// differential, 205/50 R15 tyres (auto-data.net; cars-data lists 195/55 R15), ventilated discs in front and discs
    /// behind with ABS, 1165 kg at the kerb (auto-data.net and cars-data.com agree). Fiat's figures: 0-100 km/h in 8.5 s
    /// (8.0 in cars-data's table), 213 km/h, which the gearing puts at 6,530 rpm in fifth, on the power peak.
    /// </summary>
    /// <remarks>
    /// Its size is the Bravo's: 4025 x 1755 x 1410 mm, 2540 mm between the axles, tracks of 1471 mm in front and 1430
    /// behind (auto-data.net, cars-data.com), one track of 1.45 m here (the mean). The overhangs are the ones the
    /// three-door Bravo's loft was keyed on (875 / 614 mm, CarView.Hatchbacks.cs); since cars/bodies the car is drawn
    /// by a loft of its own on the Bravo's lines (CarView.Cars13.cs, CarStyle.HgtFive), no CC-licensed model of it having
    /// been found. About 62/38: no measured axle loads were found, and a
    /// transverse five with its gearbox hangs ahead of the front axle as the 205's four does (62.8 %). Cd: no figure was
    /// found, 0.32 for the smooth three-door body of 1995 (a game figure). The springs give the hot hatch's sag at this
    /// mass (see HotHatch's remarks), the bars sit between the 205's and the R32's; reverse is not published.
    /// </remarks>
    public static VehicleSpec HgtFive => new()
    {
        Name = "HATCH 2.0 20V '01",
        Style = CarStyle.HgtFive,
        FrontTire = new(205f, 50f, 15f),
        RearTire = new(205f, 50f, 15f),
        FrontAntiRoll = 17000f,
        RearAntiRoll = 9000f,
        CentreOfMassOffset = new(0f, -0.09f, 0.01f),
        AntiLockBrakes = true,
        LimitedSlipLocking = 0f,
        LimitedSlipPreload = 0f,
        Engine = EngineSpec.HgtFive,
        // Its speedometer reads to 240 km/h.
        SpeedoMaxMph = 150f,
        // 1165 kg at the kerb with a driver.
        MassKg = 1240f,
        // The R32's inertias at 1240 kg and the Bravo's wheelbase, width and height (see HotHatch's remarks).
        InertiaRoll = 375f,
        InertiaYaw = 1630f,
        InertiaPitch = 1585f,
        HalfTrack = 0.725f,
        FrontAxle = 0.975f,
        RearAxle = 1.565f,
        BodyFront = 1.85f,
        BodyRear = 2.179f,
        BodyHalfWidth = 0.8775f,
        HubHeight = -0.20f,
        DragCoefficient = 0.32f,
        // 1.755 m wide and 1.41 m tall.
        FrontalArea = 2.0f,
        FrontLiftArea = 0.08f,
        RearLiftArea = 0.05f,
        // Performance tyres of the late 1990s on 15 in rims: between the 205's (1.0) and the R32's (1.08).
        TireGrip = 1.03f,
        WheelRadius = 0.293f,
        SuspensionRest = 0.22f,
        // The hot hatch's sag (985 kg on 25,920 N/m) at 1240 kg.
        SuspensionStiffness = 32630f,
        SuspensionDamping = 3100f,
        MaxCompression = 0.13f,
        MaxSteerLowSpeedDeg = 33f,
        MaxSteerHighSpeedDeg = 7f,
        // Ventilated discs in front, solid discs behind (cars-data), proportioned so the fronts lock first on a 62/38 car.
        FrontBrakeTorque = 2300f,
        RearBrakeTorque = 450f,
        GearRatios = [-3.55f, 0f, 3.55f, 2.24f, 1.52f, 1.16f, 0.95f],
        FinalDrive = 3.56f,
        ClutchMaxTorque = 350f,
        EngineInertia = 0.14f,
        WheelInertia = 0.9f,
        FrontWheelDrive = true,
    };

    /// <summary>
    /// The 1.6 hatch of 2001, after the Fiat Bravo 1.6 16V of the same body: the 1,581 cc sixteen-valve four, 103 PS at
    /// 5750 and 144 N m at 4000, five speeds to an open front differential, 185/65 R14 tyres, discs in front and drums
    /// behind, ABS (standard on the GT trim and optional below it: the car has it), 1060 kg at the kerb for the SX
    /// (auto-data.net; 1025 for the base three-door, 1065 for the five-door Brava). Its figures: 0-100 km/h in 11.0 s
    /// and 184 km/h, 2,900 rpm at 100 km/h in fifth (cars-data.com's listing of the 1.6 16V GT).
    /// </summary>
    /// <remarks>
    /// The HGT's body: 4025 x 1755 x 1414 mm, 2540 mm between the axles, tracks of 1439 / 1441 mm on its 14 in wheels
    /// (cars-data), one track of 1.44 m here, the same overhangs and the same loft (CarStyle.HatchSixteen). No gearing
    /// was found published for the 1.6 16V's five-speed (Fiat's C.514 family): the ratios are the C.514's as a Fiat
    /// forum gives them for the Punto 1.2 16V (3.909 / 2.158 / 1.480 / 1.121 / 0.921, reverse 3.818) on a 3.56 final
    /// drive, which gives cars-data's 2,900 rpm at 100 km/h (2,918 here) and puts 184 km/h at 5,370 rpm in fifth, near
    /// the power peak. About 62/38 as the HGT; the sag the hot hatch's; Cd as the HGT's; the inertias the HGT's at this
    /// mass.
    /// </remarks>
    public static VehicleSpec HatchSixteen => new()
    {
        Name = "HATCH 1.6 16V '01",
        Style = CarStyle.HatchSixteen,
        FrontTire = new(185f, 65f, 14f),
        RearTire = new(185f, 65f, 14f),
        // Softer than the HGT's: a family hatch's bars.
        FrontAntiRoll = 14000f,
        RearAntiRoll = 6500f,
        CentreOfMassOffset = new(0f, -0.09f, 0f),
        AntiLockBrakes = true,
        LimitedSlipLocking = 0f,
        LimitedSlipPreload = 0f,
        Engine = EngineSpec.HatchSixteen,
        // Its speedometer reads to 220 km/h.
        SpeedoMaxMph = 137f,
        // 1060 kg at the kerb with a driver.
        MassKg = 1135f,
        InertiaRoll = 345f,
        InertiaYaw = 1500f,
        InertiaPitch = 1460f,
        HalfTrack = 0.72f,
        FrontAxle = 0.975f,
        RearAxle = 1.565f,
        BodyFront = 1.85f,
        BodyRear = 2.179f,
        BodyHalfWidth = 0.8775f,
        HubHeight = -0.20f,
        DragCoefficient = 0.32f,
        FrontalArea = 2.0f,
        FrontLiftArea = 0.10f,
        RearLiftArea = 0.06f,
        // Economy tyres of the 2000s, the Egea's 0.94.
        TireGrip = 0.94f,
        WheelRadius = 0.298f,
        SuspensionRest = 0.22f,
        // The hot hatch's sag at 1135 kg.
        SuspensionStiffness = 29870f,
        SuspensionDamping = 2800f,
        MaxCompression = 0.13f,
        MaxSteerLowSpeedDeg = 33f,
        MaxSteerHighSpeedDeg = 7f,
        // Discs in front, drums behind.
        FrontBrakeTorque = 1800f,
        RearBrakeTorque = 350f,
        GearRatios = [-3.818f, 0f, 3.909f, 2.158f, 1.480f, 1.121f, 0.921f],
        FinalDrive = 3.56f,
        ClutchMaxTorque = 260f,
        EngineInertia = 0.12f,
        WheelInertia = 0.8f,
        FrontWheelDrive = true,
    };

    /// <summary>
    /// The hot hatch of 2018, after the SEAT Leon Cupra five-door (the Mk3 facelift, type 5F) with the 245 PS tune of
    /// the 2.0 TSI EA888 that its MQB platform carried in 2017-20 (cars/phys2): 245 PS from 5000 to 6700 and 370 N m
    /// from 1600 to 4300 (EngineSpec.TsiFour), the six-speed manual of the Octavia RS 245 (3.77 / 2.09 / 1.48 / 1.09 on
    /// a 3.45 final drive, 1.10 / 0.91 on 2.76, reverse 4.55: Skoda's technical data of the Octavia Combi RS 245,
    /// 5 June 2017, cdn.skoda-storyboard.com), front-wheel drive with the VAQ the 245 PS Golf and Octavia also have,
    /// and the 2018 Leon's body and chassis: 235/35 R19 tyres (225/40 R18 on the 18 in wheels), 340 x 30 mm ventilated
    /// discs in front and 310 x 22 behind, ESC with ABS and traction control, 1395 kg in running order with a driver
    /// (SEAT's technical sheet of the Leon SC CUPRA 300 of 02/2017: 1375 kg for the three-door, the five-door listed
    /// 20 kg heavier).
    /// <para>
    /// Why this car. The user asked for a "Seat Leon Cupra 2018 240HP 2.0TSI", and no 2018 Leon Cupra had 240 hp: SEAT
    /// sold it at 300 PS until the petrol particulate filter of autumn 2018 brought it to 290 PS with its 380 N m kept
    /// (ADAC's listings of the Cupra 290 of 09/2019: 213 kW, 380 N m); the 240 PS Cupra was the Mk2 of 2006-12. The
    /// 2018 body and chassis are kept and given the 245 PS (180 kW, 241.6 hp) tune the same platform carried in the
    /// Volkswagen Golf GTI Performance (Mk7.5) and the Skoda Octavia RS 245, the nearest real engine to the request: an
    /// assumption the user can reverse (docs/design.md item 23 says how; the 300 PS car's figures are kept in comments
    /// here, in EngineSpec's TsiFourCurve and in the tests' rows).
    /// </para>
    /// <para>
    /// Why the manual. Skoda's sheet gives both gearboxes' ratios: the manual's above and the seven-speed DSG's (DQ381:
    /// 3.40 / 2.75 / 1.77 / 0.93 / 0.71 / 0.76 / 0.64, reverse 2.90, on 4.17 / 3.13). The one independent road test
    /// of the platform's 245 PS cars found drove a manual: Auto Express timed a six-speed Octavia vRS 245 hatch at 6.8 s
    /// to 60 mph, 5.5 s from 30 to 70 mph through the gears and 4.1 s from 30 to 50 in fourth ("Skoda Octavia vRS 245 vs
    /// Ford Focus ST", 16 August 2017; the same figures for the vRS Challenge of 18 June 2019, 1,365 kg at the kerb). No
    /// magazine's timing of a DSG car of this generation was found, only the makers' figures (the DSG timed with the tune
    /// is the next generation's Octavia RS of 2021, below), and the manual carries the 250 km/h both makers publish
    /// (Volkswagen's DSG Golf 248). The Leon Cupra 300 of SEAT's sheet, whose body this is, was a manual too,
    /// on a box with the same ratios from second gear up and its own first gear and final drives (below).
    /// </para>
    /// <para>
    /// Published figures. Volkswagen: 6.2 s to 100 km/h for the Golf GTI Performance with either gearbox and 250 km/h
    /// with the manual (its newsroom, "Golf GTI Performance (245 PS): icon for the ages", 13 April 2018); ADAC's
    /// catalogue lists the five-door manual of 03/2017-08/2018 at 1387 kg with a driver, 2626 mm between the axles,
    /// 6.2 s and 250 km/h. Skoda: 6.6 s for the heavier Octavia RS 245 hatch (6.7 the Combi, 1467 kg with a driver) and
    /// 250 km/h, electronically limited (Skoda's sheet; its launch release). The Golf, the Leon's twin in size and
    /// mass, sets the targets (a tenth either side of 6.2 s; the tests' rows say how), a window the Octavia's 6.6 s
    /// falls in too; Auto Express's through-the-gears time checks the car (its 0-60 mph, slower than Skoda's own
    /// 0-62 mph, is not taken as the launch's). The quarter mile: no time was found for the platform's 245 PS cars of
    /// 2017-20; the one found for the tune is Wheels' for the next generation's Octavia RS of 2021, a larger car with the
    /// same 180 kW and 370 N m on the seven-speed DSG, 14.47 s at 163.08 km/h (101.3 mph) with a DriftBox, 6.15 s to
    /// 97 km/h and 6.41 s to 100 (Scott Newman, "2021 Skoda Octavia RS review: performance tested", whichcar.com.au,
    /// 24 September 2021), which the tests hold the car to a tenth either side of, the trap speed too.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Its size is the five-door's: 4281 x 1816 x 1443 mm, 2631 mm between the axles, tracks of 1536 / 1510 mm on the
    /// 19 in wheels (SEAT's sheet: 1534 / 1511 on 18 in), one track of 1.523 m here; the overhangs about 880 / 770 mm
    /// (the SC's 4246 and 2596 give the same 1650 together). Since cars/bodies the car is drawn by a loft of its own on
    /// the 2017-20 five-door's lines (CarView.Cars13.cs, CarStyle.CupraTsi), no CC-licensed model of it having been
    /// found. The VAQ (Vorderachsquersperre): a multi-plate
    /// clutch across the front differential pressed by an electro-hydraulic actuator built on Haldex's fifth-generation
    /// coupling, locking up to 1600 N m as the ESC's controller reads the steering angle, the wheel speeds and the yaw
    /// rate (Volkswagen's description of the Golf GTI Performance's unit; PistonHeads, "VW's VAQ diff explained"; a VAG
    /// service guide, jcr-leeds.com). It is modelled as the game's clutch-type unit with a locking share of 40 % of the
    /// drive torque under power (a game figure: the controller's law is not published; the 1600 N m is more than the
    /// axle sees above second gear) and no preload (open without pressure), which hands the drive to the outside wheel out
    /// of a bend as the unit is meant to; the brake function of its ESC (XDS) is left out, the VAQ doing its work. The
    /// 250 km/h is the limiter's (<see cref="SpeedLimiterKmh"/>): the car has the power for more. About 60/40 (a
    /// transverse four and its gearbox ahead of the axle; no axle loads were found); Cd 0.32 (none published for the
    /// Cupra; the Leon 5F is given as 0.30-0.31 in its economy trims, the Octavia RS 245 0.319: a game figure); the springs
    /// give the R32's sag.
    /// </remarks>
    public static VehicleSpec CupraTsi => new()
    {
        Name = "HOT HATCH 2.0 TSI '18",
        Style = CarStyle.CupraTsi,
        FrontTire = new(235f, 35f, 19f),
        RearTire = new(235f, 35f, 19f),
        // A hot hatch of 2018 with its balance set by a stiff rear, so that it turns in rather than ploughs on.
        FrontAntiRoll = 28000f,
        RearAntiRoll = 18000f,
        CentreOfMassOffset = new(0f, -0.09f, 0f),
        AntiLockBrakes = true,
        // The VAQ (see the remarks).
        LimitedSlipLocking = 0.4f,
        LimitedSlipPreload = 0f,
        Engine = EngineSpec.TsiFour,
        // Its speedometer reads to 280 km/h.
        SpeedoMaxMph = 175f,
        SpeedLimiterKmh = 250f,
        // SEAT's 1375 kg for the three-door with a driver; the five-door's 1395, kept: the 245 PS tune is the same EA888 with a
        // manual of the same family (ADAC's 245 PS Golf five-door manual: 1387 kg; a DSG adds 20 kg on Skoda's sheet).
        MassKg = 1395f,
        // The R32's inertias at 1395 kg and the Leon's wheelbase, width and height (see HotHatch's remarks).
        InertiaRoll = 475f,
        InertiaYaw = 1965f,
        InertiaPitch = 1910f,
        HalfTrack = 0.7615f,
        FrontAxle = 1.052f,
        RearAxle = 1.579f,
        BodyFront = 1.932f,
        BodyRear = 2.349f,
        BodyHalfWidth = 0.908f,
        HubHeight = -0.20f,
        DragCoefficient = 0.32f,
        // 1.816 m wide and 1.443 m tall.
        FrontalArea = 2.15f,
        FrontLiftArea = 0.06f,
        RearLiftArea = 0.03f,
        // 235/35 R19 performance tyres of 2018: between the R32's 18 in sports tyres (1.08) and the 570S's (1.15).
        TireGrip = 1.12f,
        TractionControl = true,
        WheelRadius = 0.324f,
        SuspensionRest = 0.22f,
        // The R32's sag (1550 kg on 39,800 N/m) at 1395 kg.
        SuspensionStiffness = 35830f,
        SuspensionDamping = 3700f,
        MaxCompression = 0.13f,
        MaxSteerLowSpeedDeg = 32f,
        MaxSteerHighSpeedDeg = 7f,
        // 340 x 30 mm ventilated discs in front, 310 x 22 behind, proportioned so the fronts lock first on a 60/40 car.
        FrontBrakeTorque = 3300f,
        RearBrakeTorque = 720f,
        // The Octavia RS 245's six-speed manual (Skoda's sheet). The box drives gears 1-4 and 5-6 through two output shafts
        // with their own final drives, 3.45 and 2.76, as the R32's 02M does: the only order that keeps the overall ratios
        // falling (5th on 3.45 would be lower than 4th). 5th and 6th are given on the first one (1.10 x 2.76 / 3.45 = 0.880,
        // 0.91 x 2.76 / 3.45 = 0.728); the sheet does not say which carries reverse, taken on the first. That gives 13.0,
        // 7.21, 5.11, 3.76, 3.04 and 2.51 overall: 100 km/h in second, 250 km/h at about 5,100 rpm in sixth. It is a
        // manual's gear change (ShiftSeconds' default), so the showroom reads a six-speed manual.
        // The 300 PS alternative (SEAT's sheet of the Leon SC CUPRA 300, 02/2017; the gearing until cars/phys2): its box's
        // 3.357 / 2.087 / 1.481 / 1.088 on 3.944 and 1.097 / 0.912 on 3.087, reverse 3.990, that is
        // GearRatios = [-3.99, 0, 3.357, 2.087, 1.481, 1.088, 0.8587, 0.7138] and FinalDrive = 3.944.
        GearRatios = [-4.55f, 0f, 3.77f, 2.09f, 1.48f, 1.09f, 0.88f, 0.728f],
        FinalDrive = 3.45f,
        ClutchMaxTorque = 600f,
        // The EA888's dual-mass flywheel.
        EngineInertia = 0.17f,
        WheelInertia = 1.1f,
        FrontWheelDrive = true,
    };

    /// <summary>
    /// The coupe of 2013, after the Subaru BRZ (ZC6, 2012-16, before the facelift of 2017): the two-litre FA20 boxer four,
    /// 200 PS at 7000 and 205 N m at 6400-6600, the six-speed manual (3.626 / 2.188 / 1.541 / 1.213 / 1.000 / 0.767,
    /// reverse 3.437, final drive 4.10 until 2016 and 4.30 from 2017: Subaru's 2013 and 2017 specification sheets) to a
    /// Torsen limited-slip rear differential, 215/45 R17 tyres, ventilated discs of 294 mm (11.6 in) in front and 290 mm
    /// (11.4 in) behind with ABS, VSC with traction control, 53/47; 2762 lb (1253 kg) at the kerb for the US manual car,
    /// 1240 kg as listed in Europe. Its figures: 0-100 km/h in 7.6 s and 226 km/h (Subaru Europe); Edmunds' Inside Line
    /// ran a manual BRZ of 2012 to 60 mph in 7.3 s and over the quarter mile in 15.3 s at 92.1 mph (autoblog.com's report
    /// of the test), which takes third gear: second runs out at the limiter's 7,400 rpm at 97 km/h on the free tyre
    /// radius, about 95 on the loaded one, just short of 60 mph. Its 215/45 R17 Michelin Primacy HP touring tyres held
    /// about 0.9 g in the road tests of 2012-13 (Car and Driver's and Motor Trend's BRZ and FR-S). Since 0.76 it is also the
    /// owner's car, a European BRZ of 2017-20 with the six-speed automatic, which it drives through in the automatic mode
    /// (<see cref="BoxerAutomatic"/>); the manual modes keep the manual gearbox.
    /// </summary>
    /// <remarks>
    /// Its size is the BRZ's: 4240 x 1775 x 1285 mm, 2570 mm between the axles, tracks of 1520 mm in front and 1540
    /// behind (Subaru's sheet: 59.8 / 60.6 in), one track of 1.53 m here; the overhangs about 920 / 750 mm. Cd 0.29
    /// (Subaru Canada's sheet, without the rear spoiler; 0.28 with it). The Torsen's bias ratio is not published: a
    /// Type-2 unit's 2.5:1 is taken, a locking share of (2.5 - 1) / (2 x 3.5) = 0.21 as the Delta's rear is reckoned
    /// (FourWheelDrive.RearLocking), with no preload. The law passes that share of the drive torque from the faster
    /// wheel whatever the slower one can take, so with a wheel in the air it still sends a fifth of the axle's torque to
    /// the grounded one, where a real Torsen, biasing torque against the other wheel's, sends almost none: the Delta's
    /// rear works the same way, and a law with the bias's own limit is left to a pass on both. Since cars/bodies the car
    /// is drawn by a model of its twin, a first-generation Toyota 86 (the 2017 facelift's nose) with the W212's cabin
    /// (tools/car_models.json "brz"); a build without the models draws the V8's loft, fitted to these axles
    /// (CarStyle.Boxer), the nearest in height and proportion though its glass engine deck belongs to a mid-engined car.
    /// The inertias follow the sports cars' (the dynamic index about 0.9).
    /// <para>
    /// The facelift of 2017-20, compared (cars/phys2, 0.76): the drawn model is a facelifted first-generation twin (a
    /// Toyota 86), still the ZC6. In Europe the facelift kept the engine and the figures this spec follows with either
    /// gearbox: 200 PS at 7000, 205 N m at 6400-6600, 215/45 R17 87W on 7J x 17, Cd 0.28; 1243 kg at the kerb with the
    /// manual and 1266 kg with the automatic (both without a driver), 0-100 km/h in 7.6 s and 226 km/h with the manual,
    /// 8.2 s and 210 km/h with the automatic (Subaru Deutschland's price list and technical data of the 2017 model year;
    /// motor1.com's German test of the manual, 19 January 2017). Its revised springs and dampers and larger rear anti-roll
    /// bar are published without rates (Subaru of America's release of 24 October 2016), so the 2013-16 car's measured
    /// rates stand (the fields' comments). The American and Japanese manual took 205 hp at 7000, 156 lb ft at 6400 and a
    /// 4.30 final drive in place of the 4.10 (Subaru of America, 24 October 2016; their automatic kept 200 hp, 151 lb ft
    /// and 4.10), and Car and Driver timed its twin, a 2017 Toyota 86 manual on the same tyres (2,754 lb), at 6.2 s to
    /// 60 mph, 16.8 s to 100 mph, the quarter mile in 14.8 s at 95 mph and 134 mph flat out (Car and Driver, 13 October
    /// 2016, as Yahoo Autos carries it). The BRZ stays the European car; moving to the American manual takes the curve
    /// through 205 hp at 7000 and 212 N m at 6400 and FinalDrive = 4.30.
    /// </para>
    /// <para>
    /// 0.76 made it the owner's car (his "2021 model" European BRZ with the automatic: "its cabin, its sound and its
    /// driving physics are nothing like it") and took up the 0.70 review's findings for every gearbox mode: the automatic
    /// (<see cref="BoxerAutomatic"/>), the FA20's dyno shape (EngineSpec.BoxerFour), the EPA's road load, the springs at
    /// the BRZ's ride frequencies and the bars at its load transfer, the bump travel and the brakes' build-up. The 0.76
    /// review then gave the D range its own reading of the pedal, a drive at the wheels
    /// (AutomaticGearbox.PedalPowerExponent), and the FA20 its launch revs above its dip in the manual modes
    /// (EngineSpec.BoxerFour). Measured then: with the manual 0-100 km/h in 7.77 s
    /// on the road-test launch (7.6 published), 229 km/h (226) and 38.6 m from 100 km/h (39); with the automatic 8.21 s
    /// (8.2), 212 km/h in fifth (210) and 38.8 m.
    /// </para>
    /// </remarks>
    public static VehicleSpec Boxer => new()
    {
        Name = "COUPE 2.0 BOXER '13",
        Style = CarStyle.Boxer,
        FrontTire = new(215f, 45f, 17f),
        RearTire = new(215f, 45f, 17f),
        // The bars (0.76) give the real car's balance and roll: 61 % of the roll stiffness in front, its lateral load
        // transfer (Spectral Performance's model of the BRZ: "roll resistance on front 61%"), and about 2.3 deg/g ("max
        // roll angle +/-2.3 deg"). The real car's roll centres (53 mm in front, 97 mm behind on the same page) carry part
        // of its load transfer through the links; the model's are at the ground, so its bars carry that part too and are
        // stiffer than the real bars (18 / 14 mm, 23.0 / 6.8 kN/m in these units). Until 0.76 the bars were 22,000 /
        // 14,000 on stiffer springs: 57 % in front, the car a little loose against the real one.
        FrontAntiRoll = 26500f,
        RearAntiRoll = 12700f,
        // 53/47 with the reference point 1.21 / 1.36 m from the axles; 0.14 m under it: the boxer sits low and far back.
        CentreOfMassOffset = new(0f, -0.14f, 0f),
        AntiLockBrakes = true,
        // The Torsen (see the remarks).
        LimitedSlipLocking = 0.21f,
        LimitedSlipPreload = 0f,
        Engine = EngineSpec.BoxerFour,
        // Its speedometer reads to 260 km/h.
        SpeedoMaxMph = 162f,
        // No speed limiter: its published top speeds (226 km/h with the manual, 210 with the automatic: Subaru
        // Deutschland's technical data of 2017) are the drag's; Subaru's specification sheets give no limited speed, and
        // an owner's run to an indicated 237 km/h (Carscoops, 2013) ended on the drag. Until 0.76 the road load was the
        // drag of Cd 0.29 on an estimated 2.0 m^2 and a constant rolling resistance, and the car ran to 246 km/h, 8 % over;
        // since then it is the EPA's coast-down of the car (RollingResistance, RoadLoadPerSpeed below).
        // 1240 kg at the kerb with a driver: the manual's (1243 kg in Subaru Deutschland's 2017 data, without the driver).
        // The automatic's 1266 kg is not taken: one mass serves every gearbox mode, and the model preparations' copy of
        // the geometry carries it (tools/car_models.py SPECS).
        MassKg = 1315f,
        InertiaRoll = 420f,
        InertiaYaw = 2000f,
        InertiaPitch = 1900f,
        HalfTrack = 0.765f,
        FrontAxle = 1.21f,
        RearAxle = 1.36f,
        BodyFront = 2.13f,
        BodyRear = 2.11f,
        BodyHalfWidth = 0.8875f,
        HubHeight = -0.16f,
        DragCoefficient = 0.29f,
        // 1.775 m wide and 1.285 m tall.
        FrontalArea = 2.0f,
        FrontLiftArea = 0.08f,
        RearLiftArea = 0.04f,
        // The road load (0.76) is the EPA's coast-down of the 2017-19 BRZ with the automatic (its test-car data, test
        // weight 3,125 lb): A 32.33 lbf, B 0.1765 lbf/mph, C 0.0177 lbf/mph^2, that is 143.8 N, 1.756 N per m/s and
        // 0.394 N per (m/s)^2. A as a rolling resistance at that weight; B as it is; of C, what the drag above (0.348 at
        // the model's air density) leaves. The manual's of the same years is within 3 % of it at 200 km/h.
        RollingResistance = 0.0103f,
        RoadLoadPerSpeed = 1.756f,
        RoadLoadPerSpeedSquared = 0.046f,
        // Touring tyres, chosen for the car's balance rather than its grip: the flat-twelve's 1.0 (about 0.9 g).
        TireGrip = 1.0f,
        TractionControl = true,
        WheelRadius = 0.313f,
        SuspensionRest = 0.24f,
        // The ride height's springs (the sag of a quarter of the mass on 36,000 N/m, 90 mm, which the drawn model and
        // the model preparations' SPECS are set to); the wheel rates are the axles' own below.
        SuspensionStiffness = 36000f,
        // The springs (0.76): the BRZ's ride frequencies, 1.43 Hz in front and 1.47 Hz behind (Spectral Performance's
        // suspension model), on each corner's share of the car's mass, which the model's springs carry whole (the real
        // car's carry the body; its wheels, some 38 kg a corner, ride on the tyres). Until 0.76 the axles shared 36,000
        // N/m by their loads, 38.1 / 33.9 kN/m, 1.67 Hz at both ends: 1.36 and 1.28 times too stiff.
        FrontWheelRate = 28100f,
        RearWheelRate = 26400f,
        SuspensionDamping = 3600f,
        // 64 mm of bump travel from the ride height (2.5 in, Spectral Performance), the 90 mm of rebound the sag gives.
        MaxCompression = 0.154f,
        // A 13.1:1 rack: quick.
        MaxSteerLowSpeedDeg = 32f,
        MaxSteerHighSpeedDeg = 7f,
        // Ventilated discs of 294 mm (11.6 in) in front and 290 mm (11.4 in) behind, two-piston callipers in front,
        // proportioned for a 53/47 car.
        FrontBrakeTorque = 2600f,
        RearBrakeTorque = 1100f,
        // The brakes' response and build-up from the pedal (0.76): a car's hydraulic brake reaches its full pressure some
        // 0.1-0.3 s after the foot (accident reconstruction's response and build-up times; a game figure at the top of
        // that range). Every published stop is timed from the pedal, the build-up in it; without it the car stopped from
        // 100 km/h in 36.5 m, 7 % short of the tests: ADAC's "just under 39 m" (its test of December 2014), Motor
        // Trend's 120 ft from 60 mph and Car and Driver's 165 ft from 70 mph (39.2 and 39.6 m from 100 km/h).
        BrakeBuildUpSeconds = 0.3f,
        GearRatios = [-3.437f, 0f, 3.626f, 2.188f, 1.541f, 1.213f, 1.000f, 0.767f],
        FinalDrive = 4.10f,
        ClutchMaxTorque = 400f,
        EngineInertia = 0.14f,
        WheelInertia = 1.0f,
        Automatic = BoxerAutomatic,
    };

    /// <summary>
    /// The BRZ's six-speed automatic (0.76), the owner's car: Aisin's A960E as the 2017-20 facelift had it in Europe (its
    /// manual mode, paddles and downshift blips are not modelled: the game's automatic mode is its D range). Its ratios,
    /// 3.538 / 2.060 / 1.404 / 1.000 / 0.713 / 0.582 with reverse 3.168, on a 4.100 final drive (Subaru of America's 2013
    /// and 2017 specification sheets: the manual's final drive went to 4.300 for 2017, the automatic's stayed; the EPA's
    /// test-car data of 2017-19 list the automatic on a 4.1 axle). The converter stalls at 2,540 +/- 150 rpm (SSP
    /// Performance's converter listing, quoting the factory's figure) with a stall torque ratio of 1.95 (Toyota's
    /// description of the A960E's three-element, one-stage, two-phase converter, as an excerpt of it quotes it: the
    /// original was not seen). Subaru Deutschland's figures for it: 0-100 km/h in 8.2 s and 210 km/h, against 7.6 s and
    /// 226 km/h with the manual; MotorWeek's automatic BRZ took 7.7 s to 60 mph.
    /// </summary>
    /// <remarks>
    /// The lock-up clutch closes in fourth to sixth (Toyota's flex lock-up; its speed and capacity, the shift timing and
    /// the D range's shift map are game figures in the manner of a six-speed automatic of its time, Aisin's being
    /// unpublished; see <see cref="AutomaticGearbox"/>'s defaults). The manual's 226 km/h is reached in sixth at about
    /// 6,000 rpm; the automatic's 210 in fifth at about 5,300, since its sixth (0.582) is an overdrive that pulls no
    /// more than about 180 km/h on the road load: flat out the gearbox changes up at 7,000 rpm, which fifth never reaches.
    /// </remarks>
    private static AutomaticGearbox BoxerAutomatic => new()
    {
        GearRatios = [-3.168f, 0f, 3.538f, 2.060f, 1.404f, 1.000f, 0.713f, 0.582f],
        FinalDrive = 4.100f,
        StallRpm = 2540f,
        StallTorqueRatio = 1.95f,
        // The D range's upshift line (a game figure, Aisin's map not being published): the earliest a change up comes,
        // about 1,500 rpm on a whisper of pedal, which puts sixth in at a steady 70 km/h, and at full pedal 7,000 rpm, a
        // little short of the 7,400 redline so the change is made before the limiter. Since the 0.76 review a change up
        // also waits until the gear above can give the pedal's drive (AutomaticGearbox.UpshiftLoadLimit), so a pull
        // from rest changes up at 1,800-2,200 rpm at 0.3 pedal, 2,100-2,900 at 0.4 and 3,300-3,700 at 0.5, and from about
        // 0.7 near the full pedal's line.
        UpshiftRpm = [(0f, 1500f), (0.3f, 1800f), (0.45f, 2050f), (0.6f, 2700f), (0.75f, 3800f), (0.85f, 5000f), (0.95f, 6500f), (1f, 7000f)],
    };

    /// <summary>
    /// The everyday hatchback of 2001, after the Renault Clio II phase 2 five-door 1.6 16V (A1: the owner asked for "renault
    /// clio" among the standard cars of Turkey, 28 October 2026; 0.33.0): Renault's K4M 1,598 cc sixteen-valve four, 107 PS
    /// (79 kW) at 5750 and 148 N m at 3750 (auto-data.net's listing of the Clio II phase II 2001 1.6 16V, 107 hp:
    /// 193 km/h, 0-100 km/h in 9.6 s, 1020 kg at the kerb for the three-door, 185/55 R15 on 6J x 15 rims, 3773 mm long,
    /// 2472 mm between the axles, tracks 1406 / 1385 mm), front-wheel drive through Renault's JB3 five-speed (3.364 /
    /// 1.864 / 1.321 / 1.029 / 0.821 on a 4.067 final drive, reverse 3.545: the JB3-953/958 suffixes as a ClioSport
    /// forum's table of Renault gearboxes gives them, the box owners match to the 1.6 16V; no Renault table was found),
    /// which puts 193 km/h at 5,850 rpm in fifth, by the power peak. Discs in front, drums behind, ABS (standard on the
    /// 1.6 16V). 1045 kg at the kerb for the five-door, 1120 with a driver; about 62/38 as the hatchbacks; the springs,
    /// bars, inertias and sag scaled from the hot hatch's to this mass (game figures). Drawn by Nardeol's model of the
    /// phase 2 five-door with its own cabin since 0.75 (tools/car_models.json "clio"; the SAAB 9-5's before); a build without the models draws
    /// the Bravo 1.6's loft, fitted to these axles. Its voice is the Bravo 1.6's "four16v" banks, a sixteen-valve 1.6
    /// four's (an MX-5's and a Golf's), until a recording of the K4M is found (docs/ISTERLER.md item 10.4).
    /// </summary>
    public static VehicleSpec ClioSixteen => new()
    {
        Name = "CLIO 1.6 16V '01",
        Style = CarStyle.ClioSixteen,
        FrontTire = new(185f, 55f, 15f),
        RearTire = new(185f, 55f, 15f),
        // A family hatch's bars, the Bravo 1.6's a little softer for the lighter car.
        FrontAntiRoll = 13000f,
        RearAntiRoll = 6000f,
        CentreOfMassOffset = new(0f, -0.09f, 0f),
        AntiLockBrakes = true,
        LimitedSlipLocking = 0f,
        LimitedSlipPreload = 0f,
        Engine = EngineSpec.ClioSixteen,
        // Its speedometer reads to 220 km/h.
        SpeedoMaxMph = 137f,
        // 1045 kg at the kerb with a driver.
        MassKg = 1120f,
        InertiaRoll = 335f,
        InertiaYaw = 1400f,
        InertiaPitch = 1360f,
        // Tracks 1406 / 1385 mm: one of 1.40 m here.
        HalfTrack = 0.70f,
        // 62/38 with the reference point 0.94 / 1.532 m from the axles.
        FrontAxle = 0.94f,
        RearAxle = 1.532f,
        // The model's overhangs at the Clio's wheelbase (0.804 m ahead of the front axle, 0.582 m behind the rear).
        BodyFront = 1.744f,
        BodyRear = 2.114f,
        BodyHalfWidth = 0.82f,
        HubHeight = -0.20f,
        DragCoefficient = 0.33f,
        // 1.639 m wide and 1.417 m tall.
        FrontalArea = 1.95f,
        FrontLiftArea = 0.10f,
        RearLiftArea = 0.06f,
        // Economy tyres of the 2000s, as the Bravo 1.6's.
        TireGrip = 0.94f,
        // 185/55 R15.
        WheelRadius = 0.292f,
        SuspensionRest = 0.22f,
        // The hot hatch's sag at 1120 kg.
        SuspensionStiffness = 29470f,
        SuspensionDamping = 2750f,
        MaxCompression = 0.13f,
        MaxSteerLowSpeedDeg = 34f,
        MaxSteerHighSpeedDeg = 7f,
        // Discs in front, drums behind.
        FrontBrakeTorque = 1750f,
        RearBrakeTorque = 350f,
        GearRatios = [-3.545f, 0f, 3.364f, 1.864f, 1.321f, 1.029f, 0.821f],
        FinalDrive = 4.067f,
        ClutchMaxTorque = 250f,
        EngineInertia = 0.12f,
        WheelInertia = 0.75f,
        FrontWheelDrive = true,
    };

    /// <summary>
    /// The cars the player can choose from, in menu order; the five of cars13 are appended after the eight of 0.24.0, so
    /// a saved choice (its index) still names the car it did.
    /// </summary>
    public static VehicleSpec[] Catalogue => [FlatTwelve, V8Evo, HotHatch, FiveHatch, TurboHatch, Saloon, Estate, Fleet, FlatPlaneV8, HgtFive, HatchSixteen, CupraTsi, Boxer, ClioSixteen];
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
    /// <summary>
    /// The mid-engined flat-plane V8 of 2010 (after the Ferrari 458 Italia; cars13). Model key "458" (drawn by
    /// its model since cars13 stage 2), sound folder "v8flat".
    /// </summary>
    FlatPlaneV8,
    /// <summary>
    /// The front-wheel-drive five-cylinder hatch of 2001 (after the Fiat Bravo HGT 2.0 20V; cars13). Model key
    /// "bravohgt", sound folder "inline5"; drawn by a loft of its own on the 1995 three-door Bravo's lines
    /// (CarView.Cars13.cs) since cars/bodies, as no CC-licensed model of it was found.
    /// </summary>
    HgtFive,
    /// <summary>
    /// The front-wheel-drive 1.6 hatch of 2001 (after the Fiat Bravo 1.6 16V; cars13). Model key "bravo16", sound
    /// folder "four16v"; drawn by the same Bravo loft, on its own wheels and trim.
    /// </summary>
    HatchSixteen,
    /// <summary>
    /// The front-wheel-drive turbo hot hatch of 2018 (after the SEAT Leon Cupra, with its platform's 245 PS tune since
    /// cars/phys2; cars13). Model key "leoncupra",
    /// sound folder "tsi4"; drawn by a loft of its own on the 2017-20 five-door's lines (CarView.Cars13.cs) since
    /// cars/bodies, as no CC-licensed model of it was found.
    /// </summary>
    CupraTsi,
    /// <summary>
    /// The rear-wheel-drive boxer coupe of 2013 (after the Subaru BRZ; cars13). Model key "brz", sound folder "boxer4";
    /// drawn by a model of its twin, a first-generation Toyota 86, since cars/bodies (the V8's loft without the models).
    /// </summary>
    Boxer,
    /// <summary>
    /// The front-wheel-drive 1.6 everyday hatchback of 2001 (after the Renault Clio II phase 2 five-door 1.6 16V; A1,
    /// 0.33.0). Model key "clio", sound folder "four16v" (the Bravo 1.6's banks, until a K4M recording is found); drawn by
    /// Nardeol's model with its own cabin since 0.75 (the Bravo 1.6's loft without the models).
    /// </summary>
    ClioSixteen,
}

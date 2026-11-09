# Design notes

## Principle

The feel is the product: physics, the steering loop and the sound come first, graphics last. The simulation
is a plain .NET library with no engine dependency, stepped at a fixed 240 Hz; Godot only renders the state
and feeds it input. `physics.native/` publishes the same code as a native DLL with a C interface (NativeAOT)
for engines that do not host .NET, the Unreal Engine version first; a test steps the published library and
the managed side together and requires the same bytes, which also pins the C structs' layout. That split lets every behaviour be a headless test and keeps the door open for a
force-feedback wheel later (the FFB loop of the 1989 cabinet ran at 4 ms, the car model at 25 ms).

## Simulation (`physics/`)

- **Track** — a graph of four paths (common → speed | stunt → final → common), each a list of road frames
  about one metre apart with origin, tangent, normal and lateral axes. The builder is turtle-style:
  straights, arcs with eased banking, cosine hills and dips, a helical loop (normal points to the loop
  centre, so it points *down* at the top), an open drawbridge with a surface-less gap, overpasses, and
  Hermite splines that close the circuit without hand-solved geometry. Lengths are given in 88 ft slabs,
  the road unit of the original.
- **Ground** — `TerrainField`: value-noise rolling ground (half a metre on the default circuit, the flat green
  plain of the original; it was 2.2 m) pulled to the edge height of every road built on
  earth (straights, curves, hills, dips, banked curves, splines), so hills get embankments and dips get
  cuttings; bridges, ramps and the loop are structures and leave the ground alone: where a run of earthwork
  meets one, the run's last frame pulls the ground no further than three quarters of a metre past itself
  along the road (0.9.1; before, its pull ran on along the tangent and left a shelf of embankment under the
  low bridge over the speed route's cutting, up to 0.7 m high). The ends of a path, where it joins another
  at the fork and the merge, keep projecting: the fork hill is the three roads' pull together. The road, its shoulders
  and a 2 m verge form one plane across the ground (tilted on a banked curve), and the ground never rises
  over a road it runs beside, then climbs no steeper than 1 in 1: where two routes run close at different
  heights, blending alone had lifted it 2.6 m through the lower road. Skid pads are flat
  discs blended in; beyond the outermost road a ring of hills rises over half a kilometre and fades before
  the far plane. The same field answers the physics probe off the road, places the props and builds the
  terrain mesh; `Height(x, z, out roadWeight)` also says how close to a road edge a point is. The mountain
  range on the horizon is the view's alone (`WorldBuilder.BuildMountains`, 3.2 to 6.4 km out, 240 to 560 m
  high): nothing drives there.
- **Layout** — `OriginalInspiredTrack` follows the plan of the 1989 course. It was traced from the "Original
  Track" map of the Hard Drivin' II track editor (stitched by the weltenschule analysis; 48 map pixels to the
  88 ft slab, which also gives the original's laps: about 127 slabs for the speed route and 92 for the stunt
  route) and checked against an attract-mode video of the arcade: the road's centreline was found as a
  shortest path along the map's road pixels, straights and arcs were fitted to its heading, and the builder's
  circuit was laid over the map until the two coincided. The start sits just past a low bridge that carries
  the start straight over the speed route's diagonal, at the foot of the bare hill whose crest holds the fork
  8.5 slabs on. Both branches drop the 10 m off the crest at the same rate, over the 102 m of the stunt route's
  curve, so the road they share for their first metres is one surface; with a gentler descent of its own the
  speed road stood a metre above the stunt road where they overlap, and a car taking the stunt route dropped
  off its edge. The speed route carries on over the crest, sweeps round the east side in one long right-hander
  (190 degrees), passes under the open drawbridge (the routes meet there: on the arcade's speed lap the car
  drives between the bridge's towers), runs the diagonal over a crest and through the cutting under the start
  straight, crosses the railway and circles the farm to the north-west, counter-clockwise, with the checkpoint
  on its top: 3.38 km, 126 slabs. The stunt route drops off the hill to the right past the barn, over a hump
  to the drawbridge, which jumps the speed route, bends right round the gas station, runs the loop on its
  westbound straight and the checkpoint nine slabs later, dives through the cutting under its own return leg
  into the left-handed banked 180 (R 62 m, 32 degrees) and climbs back north over that cutting to the merge:
  2.51 km, 93 slabs. The skid pad lies off the start straight to the left (a hard left at the line, as in the
  original), without a sign.
- **Decor** — in the original's places: the start gantry, the green fork gantry on the crest, yellow chevrons on
  the outside of the stunt route's curve off the hill, the barn with "33" on its back door and its cows at the
  road's edge at its foot, sawhorse barriers on both sides of the drawbridge's approach, before the loop and on
  its exit hump (kept clear of the loop's helix, whose descending half sits behind its entry), the gas station
  with its pumps inside the bend after the bridge and the hut across the road, the red checkpoint banners,
  crossbucks at the level crossing, the speed route's farm (orange house, barn, cows) outside the first corner
  of the east sweep, the second barn and its cows after the speed checkpoint and the houses outside the
  north-west loop, fields where the original's plan has them (plough, crop, hay) slid outward until they clear
  every road, hedgerow trees on their far side, a few roadside trees (none within 16 m of another road),
  flowers and the skid-pad column. Signs as in the original: the speed on a yellow square with a yellow arrow
  diamond above it before a bend ("90>"), yellow diamonds for DIP and the chevrons. Props are *solid* (crash)
  or *soft*: a cow moos and a sign tips over, once, with no penalty. The piers under the jump's ramps and the
  drawbridge towers are solid props as well (`TrackGeometry.AddBridgePiers`, 0.9: the view drew them alone before
  and the car drove through them); a tower is stood further out, or left out, where its legs would land on the
  speed route that runs under the jump, so each lip of the gap keeps one tower, on opposite sides.
- **Train** — `Train`: a kinematic locomotive and five passenger coaches that cross the speed route's
  diagonal after its cutting on a schedule (run, pause, run), on a railway the view lays along its line since
  0.9 (`TrainView.BuildRailway`: a gravel bed, sleepers and rails 14 cm proud of the ground, the rails set
  flush in the asphalt across the road; 1989 had none and the train read as a lorry crossing the fields); its
  far end passes within
  sight of the stunt route's last metres before the merge. The session treats a car under it as a crash; the scripted driver
  stops short of the crossing when a run is due.
- **Surface probe** — the one question the car asks: what is under this point? Nearest frame search in a
  window around a cursor, interpolation to the neighbour, lateral/height from the frame; the skid pad and
  then the ground field for anything off the road. Branches overlap for their first metres, so the probe
  also searches sibling branches near a fork, and where a point is over two roads there (or at a join) it rests
  on the road surface nearest below it, the one drawn on top: by the nearest frame alone, a car in the right
  lane just past the fork followed the stunt branch's steeper descent under the speed road and fell through it.
  The road the cursor has just left is not a candidate there: on the crest a few metres past the fork its last
  frame was the higher surface, the cursor hopped back onto it and the rules replayed its start line.
- **Vehicle** — rigid body on four raycast wheels (spring/damper/bump stop, anti-roll bars, each axle sprung for
  its load) with its mass where the real car carries it (`CentreOfMassOffset`), Magic Formula tyres with combined
  slip along the normalised slip vector, a fall-off past the peak, load-sensitive friction and cornering stiffness
  from the published tyre sizes and lateral relaxation, wheel spin solved implicitly against the tyre, a per-car grip
  multiplier, rolling resistance as a
  body force (much higher on grass: grip 0.66 and resistance 0.11 of the load since 0.9, from 0.55 and 0.16,
  which made the verge a trap), aerodynamic drag, speed-sensitive steering lock, Ackermann steering and the
  steering's give under the tyres' aligning torque (caster and a pneumatic trail that vanishes at the limit, 0.19),
  optional traction
  control that cuts the engine's torque as the driven wheels spin up, optional anti-lock brakes, brakes
  proportioned to lock the fronts first. Rear- or front-wheel drive
  (`VehicleSpec.FrontWheelDrive`) through an open differential or, on the flat-twelve, a limited-slip one, or
  four-wheel drive (`VehicleSpec.FourWheelDrive`, cars3: a centre differential with a fixed split and a viscous
  coupling, or the gearbox driving the front axle with an on-demand coupling to the rear, and a differential per
  axle; audit item 18); a turbo engine's torque builds with its boost
  (`EngineModel.Boost`: half a second of lag above a threshold rpm, falling at a third of that rate so a
  shift does not lose it), and the view whistles and vents it.
- **Cars** — `VehicleSpec` presets: the 1984 flat-twelve (1580 kg, four speeds, 6900 rpm), the 2012 V8
  (1485 kg, seven speeds, 9000 rpm, stickier tyres, traction control) and three hatchbacks that, since cars3,
  follow the cars the game draws (audit item 19): the front-wheel-drive Peugeot 205 GTi 1.9 of 1988 (985 kg with a
  driver, 130 PS, five speeds, no ABS), the four-wheel-drive Lancia Delta HF Integrale Evoluzione of 1992 (1375 kg,
  210 PS with a turbo's lag, five speeds, a 47/53 centre differential with a viscous coupling, a Torsen behind, ABS)
  and the four-wheel-drive Golf R32 of 2003 (1550 kg, a 241 PS VR6, six speeds, a Haldex coupling, ABS and traction
  control); `VehicleTests.Hatchbacks_match_their_published_figures` holds their 0-60 and top speed. Then the three
  everyday cars of 0.9.1 (the 1.6 saloon, the 1.4 estate and the fleet saloon, since fleet the E 250 CDI) and, since
  cars13 (audit item 23), five more: the flat-plane V8 of 2010 after the Ferrari 458 Italia (1560 kg, 570 CV at 9000,
  seven-speed dual clutch, the E-Diff, launch control), the Fiat Bravo HGT 2.0 20V five and the Bravo 1.6 16V of 2001
  (1240 and 1135 kg, front-wheel drive, five speeds, ABS), the SEAT Leon Cupra of 2018 (1395 kg, since phys2 the
  245 PS turbo four its platform carried in the Golf GTI Performance and the Octavia RS 245, six speeds, the VAQ front
  differential, a 250 km/h limiter; the 300 PS car kept as a commented alternative the user can switch back to, audit
  item 23) and
  the Subaru BRZ of 2013 (1315 kg, a 200 PS boxer four, six speeds, a Torsen);
  `VehicleTests.The_cars_of_cars13_match_their_published_figures` holds their 0-60, launched as a road test, and top
  speed. Each
  carries an `EngineSpec` (idle, redline, full-throttle curve, engine braking, cylinders, the recordings'
  folder, boost), a body style and the recordings to play.
- **Drivetrain** — torque-curve engine with idle governor, rev limiter and stall; clutch as a lock/slip
  state machine with the slip torque limited to what synchronises the two inertias in one step (the axle
  side includes the car's mass through the tyres; otherwise the flywheel hammers the wheels and a stall is
  impossible); a launch assist in the assisted modes (0.19) that revs the engine to its launch speed (95 % of peak
  torque, full boost on the turbo) and slips the clutch with the driven tyres just short of their peak, a pre-rev on
  brake and throttle at a standstill, launch control on the V8, gear take-up after a shift limited to the tyres' grip
  with the engine eased down to the shaft's speed; gearboxes with shift points relative to the redline and a
  kick-down in the automatic; three transmission modes.
- **Traffic** — drones that ride the road frames of the start straight and the speed route in both
  directions in their own lane, never the stunt route; kinematic, solid to the player, moved away from a
  respawn point.
- **Rules** — timers from the cabinet's option table (90 s, +45 per lap, +45 at the checkpoint on Medium),
  route detection at the fork (announced 45 m in, where the branches have parted), checkpoints and return
  points as path markers (none between the gas-station bend and the loop since 0.19: set down 26 m short of it after
  a fall, no car had the run-up to make it; counted on road that follows on; a hop between the overlapping branches or back
  across the fork carries the distance over, so nothing is scored or crossed twice), off-road ten-second rule,
  wrong-way detection (suspended on the skid pad), crash detection (hard landing, roll-over, falling into
  the gap or hitting the deck edge, solid props, drones), a 10 s instant replay buffer, the key-turn restart
  after every crash, messages with a shelf life (among them, for a car without the pull for the loop, a warning on the
  climb to the fork and on the stunt route, `LoopRunUp`, 0.19), and the best lap recorded at 20 Hz as the ghost of the
  grudge match (serialisable, loaded into the next credit).
- **Scripted driver** — pure-pursuit steering along its lane plus a curvature-aware speed plan with
  braking distance, slower over a crest (the road falling away takes weight off the tyres:
  v² = g R k / (1 + κ R k)); it changes down a gear early when there is speed to gather and the gear below pulls
  harder, never changes up in the air (0.19), and takes the speed route in a car that cannot make the loop;
  it catches a slide with opposite lock once the body slip passes 5° and stays off
  the pedals past 8°, keeps the wheel still in the air, and times its lane changes by the session clock, so a
  lap looks the same at 20 fps as at 144 (the game asks it once a frame); traffic sense (follow a slower
  drone, pass with 480 m of empty opposite lane, hurry back when one comes within 250 m, abort a pass when
  something comes, and if it is already alongside the drone then, brake behind it in the passing lane before
  moving back over: until 0.9.1 it dropped straight back into the drone's side); shared by the tests and the game's attract mode.

- Road wear (`RoadWear`): a deterministic function of the place on the road, shared by the probe and the view
  so that what is felt is also seen. Settled earthworks undulate by a centimetre over a dozen metres; potholes
  (at most one per seven-metre cell and lane, bowls 1.8 to 5 cm deep and under a metre across) sit in the band
  the wheels run in; the structures carry only the millimetres of their joints. Patches of other asphalt are
  visual only. A finer roughness (4 mm at 1.1 m) was tried and dropped: the rigid probe wheel hammered the
  suspension with it and the drag strip lost twenty miles an hour of top speed.
- High scores (`HighScoreTable`): ten lines in order, a factory table to beat, three initials of a 27-letter
  alphabet, one text form shared with the Unreal version. The session keeps the credit's own best lap
  (`CreditBestLap`) apart from the loaded ghost's time for it.

### Vehicle dynamics audit (0.18)

The car model was held against established vehicle-dynamics practice (Pacejka, *Tyre and Vehicle Dynamics*;
Milliken, *Race Car Vehicle Dynamics*; Gillespie, *Fundamentals of Vehicle Dynamics*) and against the published
figures of the cars the catalogue follows. Findings, ranked by how much each changes how the cars feel and how real
they are; the first eleven were fixed in 0.18, each pinned by a test in `HandlingTests` (proving-ground instruments in
`physics.tests/HandlingRig.cs`: a 160 m asphalt pad, a constant-radius circle driver after SAE J266, straight stops
and standing sprints).

1. **Tyre curves never fell past their peak** (fixed). The Magic Formula shapes (lateral C 1.35, E 0.95;
   longitudinal C 1.6, E 0.97) only saturated: a locked wheel braked with 99 % of the peak friction, a wheel
   spinning at three times the road speed drove as hard as one at the ideal slip, and the side force at 20 degrees
   of slip was still rising. Real road tyres on dry asphalt peak near 10-12 % slip and 5-8 degrees, then fall to
   about 0.75 (locked) and 0.9 (sliding sideways) of the peak. Now: normalised curves with slope 1 and the peak at
   u = 2 (`TireModel`), so ABS has something to gain (locked stops are 22-26 % longer than ABS ones,
   `Locked_wheels_stop_longer_than_anti_lock_brakes`), wheelspin costs drive and a slide past the limit lets go.
2. **Combined slip clipped the two pure forces** (fixed). The friction circle scaled the longitudinal and lateral
   forces only when their sum exceeded the peak, so a locked front wheel at 7 degrees still gave two thirds of its
   side force: a car steered with its wheels locked. Now the slips are normalised by stiffness and friction and
   combined as one vector, the force lying along it (a friction ellipse that also points the right way): with the
   fronts locked the car slides straight on (`Locked_front_wheels_do_not_steer_and_anti_lock_brakes_do`: about 1.1 m
   sideways from 80 km/h with the wheel turned, against 13.4 m with ABS).
3. **Cornering stiffness proportional to the load** (fixed). With K = B C D and D = mu Fz every tyre used its load
   equally well, which makes any car steer exactly neutral whatever its weight distribution (the old pad figures:
   0.5-0.7 deg/g for every car, front-heavy hatches included). Now K follows Pacejka's load dependence
   (c Fz0 sin(2 atan(Fz / 2 Fz0))), with the nominal load from the tyre's width and the stiffness per load from its
   aspect ratio (`TireSize`, the published sizes on every spec, staggered 225/255 and 225/285 on the mid-engined
   cars), and it no longer scales with the road's friction (a wet tyre is as stiff, it just lets go sooner).
4. **Centre of mass 0.6 m up and halfway between the axles** (fixed). The body's reference point (hubs, footprint,
   view, models) stands 0.6 m above the road (midway between the axles on the flat-twelve, nearer the front axle on
   the front-heavy cars), and the mass was there too: SUV-high load transfer, and 50/50 on the two mid-engined cars
   that are 40/60 (the Testarossa) and 42/58 (the 570S). `VehicleSpec.CentreOfMassOffset` now
   puts the mass where the real cars carry it (0.45-0.57 m, rearward on the mid-engined cars) without moving the
   reference point the view and the models are fitted to; each axle is sprung for its load so the cars sit level
   (`Static_weight_distribution_matches_the_car`). Braking load transfer agrees with statics to a per cent
   (`Weight_moves_forward_under_braking_as_statics_says`).
5. **No anti-roll bars** (fixed). Roll came from the corner springs alone, 4-10 deg/g (a 1970s saloon's lean on a
   sports car), and the front-to-rear split of the roll stiffness, the main tuning of a car's balance, was always
   the springs' 50/50. `FrontAntiRoll` / `RearAntiRoll` (wheel rates, N/m) now give 1.5 deg/g (V8), 2.2 (flat-twelve),
   2.5-3.2 (hatchbacks), 3.8 (fleet saloon) and 5-6 (the 1990s saloon and estate), with 55 % of the roll stiffness
   in front on the mid-engined cars and 58-66 % on the others. On the pad (`Understeer_and_roll_on_the_skid_pad`) the
   front-heavy cars now understeer (0.8-2.5 deg/g) and the mid-engined ones stay near neutral (0.2-0.4); lifting off
   at 92 % of the limit tightens their line by 15-20 % (`Lifting_off_at_the_limit_tucks_the_tail_of_a_mid_engined_car`).
6. **Brakes** (fixed). No anti-lock brakes anywhere: `AntiLockBrakes` now on the three cars whose specs carry
   traction control or ESC (the V8, the turbo hatch and the fleet saloon), a per-wheel release-and-reapply
   modulator at the tyre's peak slip. The rear brakes of the front-heavy cars locked first once the mass moved
   down to where it belongs; they are now proportioned so the fronts lock first at each car's limit
   (`The_front_wheels_lock_before_the_rears`). Stops from 100 km/h: 31.5 m (V8; Car and Driver's 570S, 144 ft from
   70 mph, is about 35 m), 36-40 m (turbo hatch, fleet saloon), 44-49 m for the 1980s-2000s cars without ABS on
   locked wheels, 53-56 m for the 1990s saloon and estate; rain adds about 45 %
   (`Sprints_and_stops_within_the_real_cars_ranges`).
7. **Wheel spin integrated explicitly against a stiff tyre** (fixed). The slip ratio was relaxed over 0.3 m only to
   keep the wheel stable; with tyres that fall past their peak that lag let the wheel ring against the tyre and the
   slipping clutch at the step rate (a V8 sat at the limiter in first gear with its clutch hammering). The wheel's
   speed is now solved implicitly each step (the speed at which inertia, drive, brake and the tyre force of that
   speed balance, by a bracketed regula falsi), the clutch is told the axle's true one-step inertia and the road's
   load torque, and only the slip angle keeps its 0.3 m relaxation length. The step costs about 20 us at 240 Hz, of
   which 12 us are the five surface probes it always had.
8. **Differential and traction control** (fixed). Every car had an open differential; the flat-twelve now has its
   40 % clutch-type limited-slip unit with a 60 N m preload (`Limited_slip_differential_holds_the_inside_wheel`: the
   inside rear runs 1.3 m/s ahead of the outside one out of a tight bend, 9.2 with an open differential). Traction
   control eased the driver's pedal by the slip ratio, which near standstill opened the assisted clutch on every cut;
   it now cuts the engine's torque (`Drivetrain.TorqueCut`) by the slip speed, and not while the clutch slips.
   Since wheelspin now costs drive, the scripted driver and the keys feather the throttle against it and the brake
   off a locking wheel the way a driver's foot does (`ScriptedDriver.FeatherThrottle` / `FeatherBrake`; a pad's
   triggers are passed as they are).

9. **Aerodynamic lift and downforce** (fixed). Drag only: the V8 at 200 mph carried no downforce and the 1980s
   cars no lift. `FrontLiftArea` / `RearLiftArea` (lift coefficient times area per axle) now lift the older cars a
   little, mostly at the front (the flat-twelve's tyres carry 97 % of its weight at 250 km/h), and press the V8 down,
   more at the back (104 %; `At_speed_the_V8_presses_down_and_the_eighties_car_lifts`).
10. **Shifts** (fixed in part). Every gearbox took 0.28 s per change with the drive cut; the V8's dual-clutch box now
    takes 0.1 s (`ShiftSeconds`), which brought its 0-100 km/h from 4.5 s to 3.9 (the real car, with launch
    control, 3.2).
11. **Dampers** (fixed). Linear and symmetric; they now rebound 2.2 times harder than they bump
    (`DamperReboundRatio`, the mean unchanged), as road dampers do: a bump is let through softly and the spring's
    return is checked, so a car settles after a crest instead of bouncing.

Items 12, 13 and 16 were taken up in 0.19, 15 was decided against, 14 was taken up in dyn5:

12. **Launches** (fixed in 0.19). The assisted clutch bit from about 2,000 rpm whatever the car: the revs collapsed
    into a bog, the tyres spun up to a slip of 1.5 and the driver had to feather the throttle back to a fifth (V8
    3.9 s to 100 km/h against the real 3.2, the turbo hatch 7.5 against 6.4). Now a standing start in the assisted
    modes is made as a good driver makes it (`Drivetrain.Launching`): the engine is held at its launch revs
    (`EngineSpec.LaunchRpm`: the lowest giving 95 % of peak torque, and full boost on the turbo) by a governor on the
    throttle, and the clutch takes the engine's torque in proportion to how far the revs stand above a band below
    that speed, never more than the driven tyres can put down (`VehicleSim.LaunchTraction`: each tyre's peak force at
    its load, 92 % of it for the assist, 99.5 % for the V8's launch control, cut back while a wheel spins past its
    peak, plus what the wheels take to spin up); it locks once the shaft has caught the engine up. Brake and throttle
    together at a standstill hold the revs there with the clutch open (`PreRevving`: the two-footed start, the
    launch control's brake-throttle-release). After a shift the clutch takes up the gear with no more than the tyres
    can take and the engine eased down to the shaft's speed (`Synchronising`; it bit with its whole capacity, which
    spun the V8's tyres past their peak in second), and the V8's dual-clutch box hands the drive over in 0.03 s.
    The rig times the sprint as a road test does, from the release of the brake (it timed the shift into first and
    the revs rising from idle too). 0-100 km/h now: flat-twelve 5.05 s (Ferrari quoted 5.8, Car and Driver timed
    5.0 to 60 mph in 1986; its gated gearbox takes 0.45 s a change, the default quarter second gave 4.9), V8 3.4 (3.2; traction-limited at 0.89 g throughout), hot hatch 7.5 (7.2), five 9.1
    (8.5), turbo hatch 7.0 (6.4), 1.6 saloon 13.5 (13.9), estate 17.1 (16-17), fleet saloon 12.3 (11.5); the ranges
    in `Sprints_and_stops_within_the_real_cars_ranges` came in to about a tenth either side of those figures, and
    `Standing_starts_hold_the_launch_revs_and_the_tyres_short_of_their_peak` and
    `Flooring_it_from_rest_launches_without_a_bog` pin the launch itself. Still a little quick on the flat-twelve:
    its magazine figures include a test driver on a heavy single-plate clutch. The ranges are about a tenth of the
    figure either side, not a tenth of a second: the five, the turbo hatch and the fleet saloon run 0.6-0.8 s slower
    than quoted.
13. **Steering** (fixed in 0.19). Parallel steer, no trail, no compliance. Each front wheel now has its own angle
    (`WheelState.SteerAngle`, drawn by the view; the cockpit's wheel shows the command): Ackermann geometry
    (`AckermannShare`, full on every car) turns the inner wheel more, so on full lock at walking pace the two front
    tyres roll round one centre with under a degree of slip between them instead of 8 degrees of scrub
    (`Ackermann_lets_both_front_wheels_roll_round_at_parking_speed`). The tyres' aligning torque, side force times
    caster trail (2 cm) and a pneumatic trail of 3 cm at rest that shrinks to nothing just past the side force's peak
    (`TireModel.PneumaticTrail`, the brush model's a/3), turns each wheel out of the bend through the steering's
    compliance (1/14000 rad per N m, Gillespie's K_str = W_f (p + t) / K_ss): every car gains about half a degree per
    g of understeer, which brings the hot hatch from 0.8 to 1.4 deg/g, inside the 1-4 of its class, and leaves the
    mid-engined cars mildly understeering (0.8-1.0), as they are tuned. The aligning torque per g at the limit falls to half
    or 60 % of what it is at 0.3 g (`The_steering_goes_light_before_the_front_tyres_let_go`): the lightening of
    the rim a driver feels, and here the steering's give fading with it. `VehicleSim.AligningTorque` is there for a
    force-feedback wheel. The keyboard is unchanged: its centring rate is the stand-in for the aligning torque, and
    slowing it as the trail falls away would leave lock on in an understeering slide, making the keys harder to
    drive, which the brief ruled out; the speed-sensitive lock stays the keys' stand-in for a three-turn wheel.
    The hot hatch keeps its catalogue identity (the 2.0 16V of 2001: 172 hp, 1140 kg, 205/45 R16, 62 % in front)
    although the model drawn is a 205 GTi 1.9 (130 PS, 960 kg, 63/37, 185/55 R15): its dimensions are the
    simulation's (`tools/car_models.py` scales the model to them), the name, the power and the recordings are the
    car the player picks, and its low understeer was the missing steering compliance, not the spec (the 205's softer
    tyres and lighter body give the same 0.5 deg/g from the tyres alone). Should the drawn car stay a 205, the spec
    and `car_models.py` can follow it together in one change. (cars3: the spec followed it, item 19; the geometry
    `car_models.py` scales the model to stayed.)
14. **Brake-based differentials** (fixed in dyn5). The 570S has an open rear differential and its stability system
    brakes a spinning inside rear wheel instead (McLaren's brake steer, chosen over a limited-slip unit for its weight);
    the Golf R32's ESP brings EDS, which brakes a driven wheel turning about 100 rpm faster than the other of its axle,
    coming in below 40 km/h and, on a four-wheel-drive car, acting on all four wheels and letting go at 80 (the Golf IV's
    handbook, the Audi A4 B5's EDL description, SSP 206); the Egea's ESC has BLD, which brakes a drive wheel that slips
    on its own (the Tipo saloon's handbook of 2016). None was modelled: their differentials were plainly open. The 205,
    the Delta (viscous centre, Torsen rear), the flat-twelve (clutch-type limited-slip unit), the two 1990s saloons and
    the estate had nothing of the kind and have nothing. `VehicleSpec.ElectronicDifferential` now gives the three cars a
    closed loop on each driven axle (`VehicleSim.UpdateElectronicDifferential`): a wheel whose slip speed (its speed
    over the road less the road's, so a bend's longer outside path is not taken for spin) runs more than the car's lead
    ahead of its partner's is braked. The loop is proportional-integral with gains scaled by the inertia the wheel turns
    with (critically damped at 25 rad/s behind a 0.02 s pressure lag, reading the lead averaged over two steps), holds up
    to the wheel's brake torque at full pedal, only while the partner grips (its reach fades as the partner passes its
    own peak: then the engine has to be cut, not a wheel braked), and never with the driver's foot on the brake. Leads:
    3.3 m/s on the R32 (100 rpm), 0.5 m/s on the V8 and 1.0 m/s on the fleet saloon (game figures: neither maker
    publishes one). The traction control shares the work as the systems do: once the function has engaged, a single
    spinning wheel is left to the brake, up to its lead, and the engine is cut when both wheels of an axle spin or one
    runs past what the brake holds. The launch assist counts on the brake on split grip (twice the better tyre's force,
    no more than the lesser one's plus the brake's, at a driver's 92 % of the peak; counted as the sum, the clutch
    pulsed at 30 Hz as the brake caught the spinning wheel and let it go). On even grip (both tyres on the ground, their
    surfaces' grip within 5 %) it counts the sum, as without the function. The review of dyn5 found the first version
    counting the brake there too, and the traction control leaving a lone spinning wheel to a brake that was not yet
    on: at the V8's asphalt launch a one-step blip of slip on one rear wheel had the assist ask for twice the better
    tyre's force before any brake was on, the wheel ran 3.0 m/s ahead of its partner and was braked at 619 N m (15.47
    m/s after 2 s against 15.69 without the function; in the rain 9.81 against 9.86, 412 N m), 0-60 mph took 3.48 s
    (3.47), and the R32 in the rain was credited for torque EDS never gave (its laps 0.06 and 0.07 s slower). Both are
    fixed: the even-grip launch is the one without the function to the thousandth, dry and wet, with no brake on either
    wheel (`On_even_grip_the_launch_is_the_one_without_the_brake`), and the V8's 0-60 is 3.467 s with or without it. The
    loop reads each wheel's lead with the last step's contact, as it reads the wheel and road speeds, so a wheel's
    first step back on the ground is not reckoned at its take-off's road speed.

    `ElectronicDifferentialTests`: from rest with the left wheels on the grass beside the pad, after 2.5 s the V8 is at
    12.02 m/s against 3.12 with its differential open (the grass wheel 0.44 m/s ahead of its partner on average against
    9.92), the R32 at 13.42 against 12.23 (its Haldex already moves most of the drive) and the fleet saloon at 6.31
    against 4.16. Out of a 22 m bend on full throttle the V8's inside rear is held to 0.67 m/s ahead of the outside one
    (1.41 under its traction control alone) and the fleet's inside front to 1.08 (1.46), the fleet gaining 3.15 m/s in
    1.2 s against 2.76; the V8 gains 5.44 against 5.57, its outside rear tyre at 0.9 g having no grip to spare sideways
    for the drive the brake hands it, as with the flat-twelve's limited-slip unit. (Reading the partner's grip as its
    combined slip, sideways as well as along, was tried after the review: the brake then lets go in the bend, the inside
    wheel runs 1.4 to 2.1 m/s ahead, as far as without the function, and the 5.65 to 5.69 m/s the car gains comes from
    the traction control no longer cutting the engine for the spinning wheel, not from the brake; it was left as it is.
    McLaren's own brake steer works mostly on the way into a bend, braking the inside rear to turn the car in, which
    the model leaves out.) The R32's EDS brakes a front wheel held 5 m/s ahead at 30 km/h (156 N m) and not at 45, and from rest last brakes at 18 km/h; no function brakes under
    the driver's foot. Stepped at 240 Hz, the brake never moves up and down on alternate steps and the held wheel does
    so no more than with the differential open. Laps: the V8 86.1 and 101.1 s (86.2 and 101.1), the R32 92.8 and
    104.8 s (unchanged to the hundredth, dry and wet, with and without traffic: once the clutch is in, its traction
    control acts before EDS's 100 rpm), the
    fleet 102.0 and 112.7 s (101.9 and 112.7: on the stunt route it brakes a front wheel spinning in the air); every car
    laps both routes and the loop verdicts stand. The V8's lift-off test runs at 25.0 m/s (24.5) and the fleet's
    steering goes light at 0.95 g (0.91).
    The five other cars are unchanged bit for bit: every figure the tests print, and a hash of their whole state over
    both routes dry and wet, the 40 m pad, a sprint, a stop and a bend exit, match 0.22.0's. The native ABI is unchanged
    (the spec record and `WheelState.DifferentialBrake` are managed only).

    Left as they are: brake steer's other half, the inside rear braked on the way into a bend to cut understeer (a yaw
    control, not a differential); the disc temperature at which EDS stands down. Found on the way, not fixed: on split
    grip at a standing start the assisted clutch locks and slips on alternate steps, and the wheel on the grass hunts with
    it at the step rate (the fleet saloon with its differential open: 49 reversals in 2.5 s; the R32 93, 88 with EDS;
    the V8 and the fleet with the brake: none). It is the clutch's lock/slip machine, shared by every car, and changing
    it moves every car's figures. And the open differential gives each wheel half the engine's inertia, so the speed
    difference between the two wheels carries half the engine where a real differential's carries none; the two wheels'
    common speed is right, and through it the brake gains what the real ones do (the engine kept off its limiter, the
    traction control's cut, the launch).

15. **Camber and tyre temperature** (decided against in 0.19). The raycast wheel has no suspension geometry, so camber
    from roll and bump would need a camber gain per car that no source gives for these cars, and its effect on the
    balance would trade one for one against the roll-stiffness split that `Understeer_and_roll_on_the_skid_pad`
    already pins to the cars; camber thrust would only move the same balance by other means. Road tyres also work
    across a wide temperature window: a thermal model would mostly shave a few per cent of grip off the first corner
    of a 90-second credit, at the price of results that depend on the history of a run (the lap and sprint tests
    are made from cold every time). Neither is cheap to make or to test for what it would add here.
16. **The stunt route for the slower cars** (fixed in 0.19). The 1.6 saloon ran the loop's run-up in fifth at
    2,900 rpm (the scripted driver changed up over the drawbridge with its wheels in the air) and fell off; it now
    changes down for the run-up (`Drivetrain.KickDownPullsHarder`) and makes the loop. The 1.4 estate cannot (61 PS
    and Cd 0.46: 1.2 m/s^2 at 50 mph in its best gear against the 1.35 the run-up asks, `LoopRunUp`), which is real:
    the game now says so on the climb to the fork ("CAN'T MAKE THE LOOP - TAKE SPEED TRACK") and on choosing the
    stunt route, and the scripted driver takes the speed route with it. Worse, the return point 26 m short of the
    loop left no car the run-up after a fall (the V8 reached 44 mph there): it is gone, and a fall now sets the car
    down after the drawbridge, from where every car that makes the loop on a flying lap makes it from rest
    (`A_car_set_down_after_falling_off_the_loop_has_the_run_up_to_make_it`). The track's geometry is unchanged.
17. **A broadside slide** (fixed in 0.19, found in review). The tyre faded its slip angle by the speed along the
    wheel alone (to keep a parked car still), so a wheel moving straight sideways had no slip angle and no side force:
    a car spun across the road slid on as if on ice (0.6 g of scrub at 60 degrees, nothing at 90). The fade now follows
    the contact patch's whole speed; a broadside slide scrubs 0.86 g on the flat-twelve and 0.70 g on the estate
    (`A_car_sliding_broadside_scrubs_its_speed_off`). Below 0.3 m/s the slip angle still fades out (it would make a
    parked car jitter), and there a standing grip holds the patch sideways: a share of its sideways speed taken off
    each step, capped by the grip, as the rolling resistance already did along the wheel. Before it, a parked car
    nudged sideways at 1.2 m/s slid on at 1.2 m/s; it now stops in 0.12-0.18 s and stays put
    (`A_parked_car_nudged_sideways_settles`).

Items 18 and 19 came with cars3, when the three hatchbacks were made to drive like the cars the game draws:

18. **Four-wheel drive** (added in cars3). A car drove its front or its rear axle; the Delta and the R32 the game draws
    drive all four wheels. `VehicleSpec.FourWheelDrive` now gives a car a centre differential with a fixed front share
    (an epicyclic's: 0.47 on the Delta) or the gearbox driving the front axle (a share of 1), a coupling between the
    axles (`CentreCoupling.Viscous`, Ferguson's, whose torque grows with the speed difference either way and is never
    open; or `Haldex`, a multi-plate coupling that closes once the fronts run more than 3 % ahead of the rears, up to
    its clutch pack's capacity (2500 N m at the rear wheels, about what the rear tyres put down on a dry launch, so once
    closed it ties the axles together and the tyres share the drive), and opens while the brakes are on, as the first
    generation's control did for the anti-lock brakes) and a differential per axle (the existing clutch-type model; a Torsen of bias ratio b is its locking
    (b - 1) / (2 (b + 1)) without preload: 0.2 for the Delta's rear). The clutch sees the centre differential's input:
    s w_f + (1 - s) w_r, the two axles as one inertia 1 / (s^2 / I_f + (1 - s)^2 / I_r) with their tyres' stiffness,
    and their loads in the same proportion; with the clutch locked the engine's inertia is shared as the torque is. The
    coupling's law is taken at the step's end, with each axle's drive, road load and tyre-stiffened inertia, so it
    never passes more than ties the axles: taken at the step's start it lagged the clutch by a step, the clutch spun the
    fronts up and the coupling, a step late, hammered them back (the Golf's fronts alternated between 6 % and 73 %
    slip at the step rate). The launch assist counts what all four tyres can take as the centre shares it, with what
    the coupling can move from the axle at its limit to the other: the least of the four tyres' whole, of the front's
    limit plus the coupling over s and of the rear's plus the coupling over 1 - s. A viscous coupling is counted at its
    rate times the slip the launch allows (one axle at its tyres' peak), a Haldex at its capacity, so the fronts run
    ahead to close it as a first Haldex's did; traction control, the scripted driver's feathering and its "driven
    wheels down" read every driven wheel. `FourWheelDriveTests`: at a steady pull the Delta sends 51 % to the
    front (the epicyclic's 47 % plus a few per cent the coupling hands back from the rear, which creeps a little faster
    on its larger share), the R32 100 %; with the fronts on grass and the rears on the pad and the same torque sent in
    with or without the coupling (the clutch held part-way, slipping under an engine on its limiter, so that the rears
    stay short of their peak), the Delta's coupling gives the rear 59 % (an open centre: 53 %), holds the fronts to
    0.26 m/s ahead of the rears (8.0) and gains 2.50 m/s where the open centre gains 2.26; the R32's gives the rear 59 %
    (none without it), holds the fronts to 0.35 m/s ahead (14.3) and gains 2.55 m/s against 0.99; on the same split
    with the launch assist and the throttle floored the R32 is at 3.38 m/s after 0.7 s, 1.36 without its coupling
    (`The_launch_assist_counts_on_the_Haldex`; the Delta's launch from idle is its engine's either way); under braking
    the Haldex passes nothing. The two-wheel-drive cars go through the same arithmetic as before: every figure the tests
    print for them (sprints, stops, skid pad, launches, laps) was compared line by line with 0.19's and is unchanged
    (`Two_wheel_drive_cars_drive_one_axle` pins where their torque goes). The native ABI is unchanged: the new spec
    fields are managed only. Not modelled then: the R32's electronic differential lock (EDS, braking a spinning wheel) and
    the 570S's brake steer (both since dyn5, item 14); a viscous coupling's hump; the Haldex's own temperature and pump lag.

    Review follow-ups (cars3): the first fronts-on-grass test let the clutch in at the limiter, which spun the rears on
    the pad as hard as the fronts on the grass (41 rad/s at 2-8 rad/s of ground speed), so it showed the coupling
    shuffling torque between two spinning axles, not handing it to one that grips; the Haldex passed at most half the
    drive, a share rather than a clutch pack's capacity, so with the clutch dumped the fronts kept half and stayed
    12.5 m/s ahead; and the launch assist counted the Haldex only for what it already passed, which it never let the
    fronts close it for: with its fronts on grass the R32 pulled away at 0.3 g with or without the coupling, and on a
    dry road only 18.5 % of the drive reached the rear in the first 2 s (33 % now) and 0-100 km/h took 6.89 s (6.62
    now). Over a scripted lap at 20, 60 and 144 fps the coupling reverses 7-23 times a lap and reaches its capacity on
    one frame in 15,000.
19. **The three hatchbacks follow the cars the game draws** (fixed in cars3). The models drawn since 0.16 are a Peugeot
    205 GTi, a Lancia Delta HF Integrale and a Golf R32; the specs were a 2001 Clio 2.0 16V, a 1997 Bravo 2.0 20V five
    and a 2008 Leon 2.0 TFSI, all front-wheel drive. Now, from published figures (sources in the specs' comments):

    | | hot hatch | rally hatch | V6 hatch |
    |---|---|---|---|
    | follows (before) | 2001 Clio 2.0 16V | 1997 Bravo 2.0 20V | 2008 Leon 2.0 TFSI |
    | follows (cars3) | 1988 205 GTi 1.9 | 1992 Delta HF Integrale Evoluzione | 2003 Golf R32 |
    | name | HOT HATCH 1.9 8V '88 | RALLY HATCH 2.0 TURBO 4WD '92 | V6 HATCH 3.2 4WD '03 |
    | engine | 172 hp 2.0 16V → 130 PS at 6000, 161 N m at 4750, 8V four | 147 hp 2.0 five → 210 PS at 5750, 304 N m at 3500, turbo four (T3, half its torque off boost, 0.7 s of lag) | 240 hp 2.0 turbo four → 241 PS at 6250, 320 N m at 2800-3200, VR6 |
    | mass (with driver) | 1140 → 985 kg | 1310 → 1375 kg | 1480 → 1550 kg |
    | weight on the front axle | 61.5 → 62.8 % | 62.6 → 60.6 % | 62.0 → 60.9 % |
    | gearbox | 5 speeds, 4.21 → BE1/5 2.92-0.86, 3.69 | 5 speeds, 4.07 → 3.5-0.916, 3.111 | 6 speeds, 3.65 → 02M 3.357-0.927 on 4.235 / 3.273 |
    | tyres | 205/45 R16 → 185/55 R15 | 195/55 R15 → 205/50 R15 | 225/40 R18 (kept) |
    | driven wheels | front | front → all four (47/53, viscous, Torsen rear) | front → all four (Haldex, closing as the fronts slip) |
    | ABS / traction control | no / no | no → ABS / no | ABS / traction control (kept) |
    | 0-100 km/h, published | 7.2 → 7.8 s | 8.5 → 5.7 s | 6.4 → 6.6 s |
    | 0-100 km/h, here | 7.52 → 8.36 s | 9.10 → 6.27 s | 7.00 → 6.62 s |
    | 100-0 km/h dry, here | 44.0 → 46.6 m (locked) | 49.2 (locked) → 39.2 m | 36.4 → 36.5 m |
    | top speed, published | 220 → 205 km/h | 215 → 220 km/h | 247 km/h |
    | top speed, here | 219 → 212 km/h | 216 → 233 km/h | 246 → 249 km/h |
    | understeer, roll (40 m circle) | 1.46 → 1.44 deg/g, 2.82 → 2.81 deg/g | 2.26 → 2.04, 3.20 → 3.07 | 1.82 → 1.86, 2.51 → 2.51 |
    | loop run-up (needs 1.35 m/s^2) | → 3.09 | → 3.86 | → 4.22 |

    The sprint and stop ranges in `Sprints_and_stops_within_the_real_cars_ranges` are now a tenth of the published
    figure either side, no tighter: the Delta sits at the very top of its range (6.27 s against 5.7; the bound is 6.3 s,
    the tenth over rounded up, so anything that slows it takes it out), held back by the T3's lag and a first gear the
    engine, not the four tyres, limits (0.69 g), and from idle it takes 6.48 s to 60 mph against that test's 6.5; the
    205 is 0.56 s slow on its modest torque; the R32's Haldex waits for the fronts to slip before the rear joins in, as
    the first generation did, and the launch assist lets them. The skid-pad
    ranges were narrowed round each car within its class (no understeer gradient is published for any of the three).
    All three lap both routes without a crash and make the loop (`Every_car_laps_both_routes`, `LoopRunUp`). The
    wheelbase, track, ride height and body footprint stayed those `tools/car_models.py` prepared the drawn models to
    (2.47, 2.54 and 2.58 m between the axles, against the real 2.42, 2.48 and 2.52) until dims6 moved them to the real
    cars' (item 21); the springs were scaled with the masses so the cars sit at the height the models are set to. Where no figure was found the spec says so: the Delta's and the R32's axle loads, the Delta's
    drag, the couplings' rates and the Haldex's lead are game figures.

Item 20 came with fleet, when the fleet saloon was made to drive like the car the game draws:

20. **The fleet saloon follows the E-Class it is drawn as** (fixed in fleet). The model drawn since 0.16 is a
    Mercedes-Benz E-Class W212 of the 2013 facelift (its one-piece headlamps); the spec was a 2017 Fiat Egea 1.4 16V,
    front-wheel drive. It is now the E 250 CDI of 2013-15: the OM 651 2.1-litre diesel four with its two-stage
    turbocharger, 204 PS at 3800 and 500 N m from 1600 to 1800, rear-wheel drive, the six-speed manual listed as its
    standard gearbox (5.10 / 2.78 / 1.75 / 1.25 / 1.00 / 0.81, reverse 4.62, final drive 2.47), 205/60 R16 tyres,
    1,765 kg, 0-100 km/h in 7.7 s and 240 km/h. The sources, as the fleet review traced them: thecarspec's listing of
    the W212 facelift E 250 CDI (the power and torque, the tyres, the size, 1,765 kg, 7.7 s, 240 km/h); cars-data's of
    the 2013 saloon with the manual (7.7 s, 240 km/h) and with the automatic (7.5 s, 242 km/h); auto motor und sport's
    technical data of the E 250 CDI 4MATIC (3800 rpm, and a final drive of 2.47 behind its 7G-TRONIC); and the same
    magazine's technical data of its test of the C 250 CDI (W204, 03/2011), the same OM 651 at 204 PS behind the
    six-speed manual: the ratios above are the C's (which it rates at 4200 rpm). No gearing was found published for the
    manual E-Class, so the C's gearbox is taken as its and the final drive the C and the automatic E share is assumed
    for it. The mass: 1,765 kg is auto-data's kerb weight of the manual saloon (1,690 kg, without a driver) with the
    EU's 75 kg for the driver and luggage, the tank 90 % full (auto-data lists 1,700 kg for the 7G-TRONIC, cars-data
    1,665-1,675 kg for other trims); the car carries 1,770. The first cut of this item named a Mercedes-Benz data sheet
    of 11/2012 and the Mercedes-Benz Classic archive as its sources; neither could be found again, and these replace
    them (the figures are the same). Cd 0.26: the W212 saloon is given as 0.25 in its most economical trims (auto-data's
    facelift E 200 CDI; the saloon's launch figure of 2009); none was found for the E 250 CDI, set a hundredth higher
    (a game figure).

    Why this one: the W212s of the taxi ranks and company fleets of Europe and Turkey were the OM 651 diesels (the
    E 200 CDI, E 220 CDI and E 250 CDI share the engine in three tunes, the 2.1 the name gives); the game follows the
    strongest of the three, the E 250 CDI, which Mercedes sold in Turkey as in Europe. The gearbox: most were bought
    with the 7G-TRONIC PLUS (0-100 km/h in 7.5 s, 242 km/h), but the game's driveline is a clutch and a gearbox on every
    car (its automatic mode shifts them for the driver through the same clutch) and a torque converter is not modelled,
    so the car has the manual Mercedes listed as standard: a real gearbox of the real car rather than the automatic's
    ratios behind a clutch it never had. A converter (its multiplication at stall, the lock-up clutch, the automatic's
    quick shifts under load) would be its own piece of work, for this car alone. The tyres: 205/60 R16 is what both
    sheets give the E 250 CDI of 2013 (the 225/55 R16 of 2009 gave way to them in 07/2011; 245/45 R17 are the 4MATIC's),
    and all three have the same radius to a millimetre. Traction: ESP with ETS, which the W212's handbook describes as
    braking "the drive wheels individually if they spin ... for example if the road surface is slippery on one side. In
    addition, more drive torque is transferred to the wheel or wheels with traction", on even with ESP switched off
    (Mercedes-Benz USA's operator's manual of the 2016 E-Class saloon and estate, p. 81): the brake function the Egea's
    BLD was (item 14), now on the rear axle. Mercedes gives no threshold: the lead is a game figure, 0.8 m/s (at the
    BLD's 1.0 the brake held the inside rear out of a tight bend to 1.16 m/s ahead against the 1.36 the traction control
    alone allows, hardly the drive handed across that the handbook describes). The weight: no axle loads were found for
    the diesel saloon; auto motor und sport weighed the E 250 CGI Coupe of the same platform at 53.4/46.6 (03/2010), and
    the car is set at 53/47. The engine: the curve is the published one at full boost (500 N m from 1600 to 1800, 377 N m
    at 3800), falling past the rated speed to a governor at 4800 (Mercedes publishes the rated speed, not the
    governor's: a game figure); off boost it makes two fifths of it (a two-litre diesel breathing on its own; the 150 kW
    OM 651 runs just under 3 bar absolute), the small high-pressure stage on full boost by 1400 rpm in about half a
    second (game figures). Idle 750 rpm. The governor (`EngineSpec.Governor`, added in the fleet review): a diesel has
    no ignition to cut, and its governor takes the injected quantity back steadily from the breakaway speed to none at
    the maximum no-load speed (Bosch, Diesel-Engine Management, the governor's breakaway and high-idle characteristics).
    Here the redline is that no-load speed and the injection falls over the last 200 rpm below it
    (`EngineModel.GovernorSpanRpm`, a game figure), so the engine settles on it, free in neutral at 4755 rpm on 22 % of
    its fuel, and `EngineModel.LimiterCut` never reports a cut. Until the review it had the petrol engines' limiter, a
    60 ms cut whenever the revs passed 4800, which the sound plays as the crisp stutter of an ignition cut: the review
    saw the scripted laps reach it on three of four runs, and the bend exit below cut on 32 of its 288 steps without the
    brake function.

    | | before | after |
    |---|---|---|
    | follows | 2017 Fiat Egea 1.4 16V | 2014 Mercedes-Benz E 250 CDI (W212 facelift) |
    | name | SALOON 1.4 16V '17 | SALOON 2.1 DIESEL '14 |
    | engine | 95 PS at 6000, 127 N m at 4500, petrol four | 204 PS at 3800, 500 N m at 1600-1800, two-stage turbodiesel four |
    | mass (with driver) | 1170 kg | 1770 kg |
    | weight on the front axle | 61.0 % | 53.0 % |
    | gearbox | 6 speeds, 3.545-0.766, 4.071 | 6 speeds, 5.10-0.81, 2.47 |
    | tyres | 195/65 R15 | 205/60 R16 |
    | driven wheels | front | rear |
    | brake function, lead | BLD, 1.0 m/s | ETS, 0.8 m/s |
    | 0-100 km/h, published | 11.5 s | 7.7 s |
    | 0-100 km/h, here (revved on the brake; floored from idle) | 12.27 s | 7.87 s (7.97) |
    | 100-0 km/h dry, here (ABS; rain) | 39.7 m (56.1) | 37.7 m (53.1) |
    | top speed, published | 185 km/h | 240 km/h |
    | top speed, here | 185 km/h (115 mph) in 5th | 245 km/h (152 mph) in 6th at 4040 rpm |
    | understeer, roll (40 m circle) | 2.04 deg/g, 3.80 deg/g | 1.97, 3.54 |
    | loop run-up (needs 1.35 m/s^2) | 2.09 | 2.76 |
    | rev limit | a petrol limiter's cut at 6500 | a diesel's governor: the fuel falls from 4600 to none at 4800 |
    | scripted laps, stunt and speed route, dry (rain) | 102.0 (a crash), 112.7 (123.8) s | 94.9 (105.2), 106.9 (115.8) s |

    The sprint is traction-limited in first (0.62-0.65 g on the rear tyres of a 53/47 car, the launch held at 1875 rpm
    with the driven tyres at 61 % of their peak slip) and 0.17 s slower than Mercedes's figure; the stop is 37.7 m against
    auto motor und sport's 36.4 m for the Coupe on wider 17 in tyres, at a grip of 1.04 (the Egea's economy tyres had
    0.94, the R32's sports tyres 1.08); 0-60 mph from idle takes 7.83 s; the top speed is 2 % over Mercedes's, held by the
    drag a little past the rated speed. The ranges in `Sprints_and_stops_within_the_real_cars_ranges` are a tenth of the
    published sprint and of the Coupe's stop either side (6.9-8.5 s, 33-40 m); in
    `Everyday_cars_match_their_published_figures` 6.6-8.1 s to 60 mph (a tenth either side of the 7.3 s that 7.7 s to
    100 km/h makes) and 144-154 mph; the skid-pad ranges were narrowed round the car within its class (1.4-2.6 deg/g,
    3.0-4.2 deg/g: no figure is published, so they are centred on the model's own 1.97 and 3.54 and guard against a
    change, not against a measurement), and the static weight check now covers it (51-55 %). The launch tests take it
    too: it holds its launch revs with the tyres short of their peak, and floored from idle it is 0.10 s behind the
    road-test start, its turbo spooling on the clutch. It laps both routes dry and wet without a crash (the Egea's
    scripted stunt lap in the rain ended in one) and makes the loop, from rest at the last return point too (64 mph at
    its foot). The brake-function tests changed with the driven axle. On split grip it reaches 6.74 m/s after 2.5 s
    against 5.19 with its differential open (the Egea 6.31 against 4.16): a rear-driven car's wheel on the grass carries
    more of the load as the car squats, so the open differential does better and the brake adds less: 1.30 times, just
    short of the Egea's bound of 1.3, which was loosened to fit (to 1.2 in the first cut, 1.25 since the review, a
    margin like the R32's). Out of a 22 m bend it holds the inside rear to 0.93 m/s ahead (1.36 with the traction
    control alone) and gains 3.37 m/s against 3.27, 3 % where the front-driven Egea gained 14 %: like the V8's, its
    outside rear tyre carries the bend as well as the drive the brake hands it. The first cut had 3.38 against 3.23 and
    asked 2 %, loosened from the Egea's 5 % to fit; the governor took the limiter's cuts out of the open car's run (it
    reaches 4680 rpm) and the test asks 2.5 % now. So the commit that brought this item said its tests were tightened,
    and of the published figures they were, but these two bounds and the skid pad's were set to the model's results.
    On split grip the braked wheel reverses on alternate steps 18 times in 2.5 s (the Egea's none), fewer than with its
    differential open (50, as the Egea's 49): the clutch's lock/slip machine of item 14's last paragraph.

    Worth knowing, none of it a fault (the fleet review): the everyday saloon is now among the quick cars, as 204 PS and
    500 N m in 1,770 kg are (its scripted stunt lap without traffic, 94.9 s, is the Golf R32's 94.8 and quicker than the
    205 GTi's 97.0; the review timed it level with the Delta's in traffic); in the
    automatic mode it takes 8.27 s to 100 km/h against 7.87 with the test's shifts, as the automatic changes up at 0.885
    of the redline (4250 rpm, against 4460) and holds each gear 0.9 s, and a torque converter's multiplication, which
    the 7G-TRONIC's 7.5 s has, is not modelled; and floored in sixth from 45 km/h it lugs at 730 rpm, below its idle (the
    gearing gives that: 2.0 overall on a 0.326 m tyre), without stalling, and gains 16 km/h in 20 s on its off-boost
    torque, where the Egea's short petrol gearing pulled to 80 km/h.

    The axles, the track, the ride height and the body's footprint stayed those `tools/car_models.py` prepared the model
    to (2.636 m between the axles, the Egea's, where the W212 has 2.874: the model was drawn at 92 % of the car's size)
    until dims6 moved them to the real car's (item 21), and the springs were scaled with the mass (45,385 N/m) so it
    sits at the same height; the model rig's mass, rate and wheel radius were changed with them (`tools/car_models.py`,
    `unreal/Scripts/prepare_car_model.py`). Its tyres are 0.9 cm larger in
    radius, so the drawn car, and the drones drawn as it (`CarView.SetTrafficPose` lifts each by its own static ride
    height), stand that much higher. The traffic's own motion does not read the spec (the drones are scripted on
    `Traffic`), and its tests pass unchanged. `--car=fleet` (and 7) still picks it; its alias is now `w212`, not `egea`.

    The other seven cars are unchanged bit for bit: a hash of their whole state over both routes dry and wet, the 40 m
    pad, a sprint, a stop and a bend exit matches the branch point's (after dyn5's review) line for line. The native ABI
    is unchanged.

    The sound: until diesel6 the fleet bank (the Punto's and the Bravo's petrol fours) played this engine's revs,
    750-4800, with the turbo's whine and a petrol car's vent synthesised for it, as for any engine with boost
    (`EngineSpec.BoostShare` 0.6). On the governor the sound hears the fuel fall away, not a limiter's stutter. Since
    diesel6 it is a diesel four: its banks (`SoundFolder` "diesel4") are cut from a CC0 recording of a TDI's engine bay,
    the only legitimately licensed one found of a car's turbodiesel four with an idle, a loaded run-up and an overrun
    (none of an OM 651), with the combustion's knock in them (the hard, broadband rattle of a compression-ignition
    pressure rise, loudest at idle and light load), and its turbo spools down when the driver lifts, neither venting nor
    surging (`EngineSpec.LiftOff`, `TurboLiftOff.SpoolsDown`, in place of the bool `BlowOffValve`: no throttle shuts on a
    diesel's boost, the fuel is cut and the boost runs down through the engine). See *Sound, diesel6* below.

Item 21 came with dims6, when the cars respecified to the real cars the game draws were given those cars' size:

21. **The drawn cars at their real size** (fixed in dims6). Items 19 and 20 gave the 205 GTi, the Delta, the R32 and the
    E-Class their engines, masses, tyres and gearing but kept the wheelbase, the track and the body's footprint of the
    cars they had followed before, and the preparation scales each model to the simulation's wheelbase
    (`tools/car_models.py`, `tools/prepare_car_models.py`; `CarView.Model.cs` hangs the wheels at the model's hubs):
    the E-Class was drawn at 92 % of its size (2.636 m between the axles, the Egea's, against the W212's 2.874), the
    205 at 102 %, the Delta at 102 %, the R32 at 102.5 %. Now each has its real car's wheelbase and track, from
    published figures (the specs' remarks cite them): the 205 GTi 1.9 from auto motor und sport's data of its test car
    (3705 x 1572 x 1355 mm, wheelbase 2420, tracks 1382 / 1339), the Delta Integrale Evoluzione from supercars.net's
    specification of the 1991 car (3900 x 1770 x 1365, 2480, 1502 / 1500), the R32 from auto-data.net's and CarExpert's
    listings (4149 x 1735 x 1444, 2518, 1513 / 1487) and the E 250 CDI from thecarspec's and cars-data's (4879 x 1854 x
    1474, 2874, 1598 / 1614, as auto-data.net and cararac have it too; cars-data has 1604 behind).

    | | 205 GTi 1.9 | Delta Evoluzione | Golf R32 | E 250 CDI |
    |---|---|---|---|---|
    | wheelbase, m | 2.47 → 2.42 | 2.54 → 2.48 | 2.58 → 2.518 | 2.636 → 2.874 |
    | track, m (one for both axles: the published mean) | 1.46 → 1.36 | 1.46 → 1.50 | 1.53 → 1.50 | 1.542 → 1.60 |
    | reference point to the front / rear axle, m | 0.95 / 1.52 → 0.931 / 1.489 | 0.95 / 1.59 → 0.928 / 1.552 | 0.98 / 1.60 → 0.956 / 1.562 | 1.028 / 1.608 → 1.121 / 1.753 |
    | centre of mass ahead of it, m; weight on the front axle | 0.03 (kept); 62.8 % | -0.05 → -0.048; 60.6 % | -0.03 (kept); 60.9 → 60.8 % | -0.211 → -0.23; 53.0 % |
    | inertia roll / yaw / pitch, kg m^2 | 335 / 1300 / 1250 → 315 / 1250 / 1200 | 450 / 1835 / 1785 → 455 / 1750 / 1700 | 490 / 2095 / 2040 → 480 / 2000 / 1945 | 680 / 3100 / 3030 → 710 / 3685 / 3600 |
    | footprint: nose / tail from the reference point, half width, m | 1.664 / 2.109, 0.83 → 1.587 / 2.118, 0.82 | 1.825 / 2.204, 0.875 → 1.719 / 2.181, 0.885 | 1.92 / 2.395, 0.884 → 1.811 / 2.338, 0.868 | 1.921 / 2.611, 0.896 → 1.984 / 2.895, 0.945 |
    | model scale; drawn length, against the real car's | 1.8463 → 1.8089; 3.69 → 3.62 m, 3.705 | 1.0253 → 1.0011; 4.03 → 3.93, 3.90 | 0.0113 → 0.0110; 4.29 → 4.18, 4.149 | 0.9201 → 1.0032; 4.48 → 4.89, 4.879 |
    | understeer, roll (40 m circle), deg/g | 1.44 → 1.50, 2.81 → 3.24 | 2.04 → 2.02, 3.07 → 2.91 | 1.86 → 1.89, 2.51 → 2.61 | 1.97 → 1.88, 3.54 → 3.29 |
    | 0-100 km/h; 100-0 km/h dry | 8.36 s; 46.6 → 46.4 m | 6.27 s; 39.2 → 39.0 m | 6.62 s; 36.5 → 36.3 m | 7.87 → 7.92 s; 37.7 → 37.8 m |

    What moved with the axles, and why. The simulation has one track for both axles, so each car has the mean of its
    published front and rear tracks: the 205's differ most, its front 3 % wider than its rear, the others' by 2 % or
    less; a track per axle would change every car's split of the roll stiffness and is left for when it is needed. The
    centre of mass keeps each spec's share on the front axle (the published or estimated one of items 19 and 20; no axle
    loads were found for the Delta, the R32 or the diesel saloon), so it moved with the axles. Its height and the ride
    height are unchanged: the hub plane, the springs' travel and their sag are the same, so the reference point stands
    0.62-0.67 m over the road as before and the drones stand where they did. The yaw and pitch inertias keep their ratio
    to the mass times the axles' distances from the centre of mass (the dynamic index, 0.85-1.0 here), so they go with
    the square of the wheelbase; the roll inertia keeps its ratio to the mass times the published width and height
    squared. The footprint the knocks use is the published length, split between the overhangs as the drawn model has
    them, and the published width without the mirrors, or the drawn body's where the model is wider than the car: the
    205's model is 1.64 m across under its mirrors against the car's 1.572, the E-Class's 1.89 against 1.854, so their
    half widths are 0.82 and 0.945 (0.786 and 0.927 by the figures), and a wall is met when the drawn side meets it, not
    2-3 cm into it (the 205's rear tyres still reach 3.5 cm past its body). Where the model is smaller (the 205's, 9 cm
    short; the Delta's and the R32's narrower) the car's own size stays.

    Kept: the masses, the springs and their sag, the bars, the brakes, the tyres, the engines and the gearing, and every
    published-figure range in the tests. The figures moved only as the geometry makes them move. The bars are wheel
    rates, so an axle's roll stiffness goes with its track squared: the 205's narrower track rolls it 15 % more (3.24
    deg/g, within its 2.2-3.4), the Delta's and the E-Class's wider ones less. The longer wheelbase moves less weight
    under braking and on a launch (h / L): the E-Class's front axle carries 75 % at 1.1 g against 77, its driven rear
    tyres less in first, so it takes 7.92 s to 100 km/h against 7.87 (within 6.9-8.5; Mercedes's 7.7); its split-grip
    and bend-exit runs with ETS reach 6.87 against 5.00 m/s (1.37 times; the bound 1.25) and 3.37 against 3.27 (the bound
    2.5 %). Every car still laps both routes dry and wet without a crash and makes the loop (2.76-4.22 m/s^2 at 50 mph,
    unchanged); the scripted laps moved by hundredths of a second. The other four cars are unchanged bit for bit: a hash
    of the whole state over both routes dry and wet, the 40 m pad, a sprint, a stop and a bend exit matches the branch
    point's. The flat-twelve (2.55 m, the Testarossa's), the saloon (2.49, the Sahin's; the drawn VAZ-2106, 2.424 m
    between its own axles, is set to the Sahin's), the estate (2.441, the Renault 12's) and the V8 (2.65 against the
    570S's 2.67, 0.75 % short) were within a hundredth already and were left alone.

    The drawn models at the real wheelbase: the E-Class is 4.89 m long against 4.879, the Delta 3.93 against 3.90 and
    the R32 4.18 against 4.149 (the prepared models' vertices in the car's frame, which is what is drawn); the 205's
    model is 3.62 m against 3.705, 2.4 % short: its height (1.36 m) and its wheelbase are the 205's, the model's own
    overhangs are short. The Delta's model is 3 % narrower than the car (1.71 m), its tracks with it (1.40 m). Side
    shots show the wheels in their arches (scaled to the simulation's tyres by 0.983-1.060 now, 0.960-1.077 before;
    the 205's model has small tyres of its own) and the hubs where the simulation has them, to a few pixels; the tyres
    stand on the road, the 205's and the E-Class's to the millimetre and the Delta's 1 mm over it, but not the R32's,
    2.2 cm over it as before dims6 (see the review follow-ups below). One fix in the preparation came with it: the eyes are set 13 cm under whatever is over them, and at the 205's real size the top of its driver's seat, a
    mesh named "defaultMaterial" like every other of the model, came 4 cm over them and had the eyes lowered 10 cm; a
    mesh shaped as a seat (no wider than 0.7 m nor deeper than 0.9, rising from 0.4 m under the eyes) is passed over now,
    and the four unchanged models prepare to the same bytes. The lofted bodies, drawn without the models (`--lofted`, or
    a checkout without them), are keyed round the axles they were drawn for: they are fitted to the spec's
    (`BodyStyle.FittedTo`: the overhangs move with their axles, the stations between are stretched), so their arches
    stay over the wheels; the other four lofts are the same objects as before.

    Tests: `Every_car_has_the_size_of_the_car_it_follows` pins each car's wheelbase within a hundredth of the published
    one and the four respecified cars' tracks within a hundredth of the published mean;
    `The_model_preparations_have_the_simulations_geometry` reads both preparations' SPECS (`tools/car_models.py`,
    `unreal/Scripts/prepare_car_model.py`) and asks for the simulation's axles, track, hub height, travel, mass, rate and
    tyre radius, as a spec changed without them draws the car at its old size and the game only logs it.

    Review follow-ups (dims6):
    - The E-Class's rear-view mirror glass hung 7.5 cm under its housing in the cockpit view, cut by the screen's edge.
      The glass is placed on a box found by hand in the car's frame (`mirror_box` in `tools/car_models.json`), read at
      the Egea's wheelbase, and the model is now scaled 1.0903 times about its front hubs, moved from 1.028 m to 1.121
      ahead of the reference point. The Delta's eyes (`eye_at`, read at its old 2.54 m) were 2 cm high on its model by
      the same slip. Points given by hand in the car frame now name the geometry they were read in (`frame`: the front
      and rear axles and the rest hub height), and the preparation carries them to the present one as the model moves
      (`in_present_frame`): the glass is at (-0.026, +0.564, +0.135), 0.240 x 0.071 m, in its housing again; the Delta's
      eyes at (-0.351, +0.500, +0.292), 15.8 cm under the roof (16.2 at the old scale, 13.8 with the slip); the estate's
      frame is its present geometry, so it prepares to the same bytes, and the other five models too. Test
      `Hand_given_car_frame_points_name_the_geometry_they_were_read_in`.
    - The drones' knock box (`Traffic.HalfLength`, 2.15 m both ways) is centred on the traffic's position, while each
      drawn car hung from its own reference point, nearer the nose than the tail: the E-Class's tail stood 0.75 m out of
      the box behind (0.51 at the Egea's size), and the player's car sank that far into it before a knock.
      `CarView.SetTrafficPose` moves each drawn car forward by half its overhangs' difference (`BodyRear - BodyFront`),
      its middle on the box's: the E-Class now overhangs it by 0.29 m at either end, the saloon, the estate and the R32
      sit within 3 cm of it, the 205 0.34 m inside. A box per drawn car would take the car each drone is drawn as into
      the traffic, which is the view's choice (and the Unreal level's another), so it is left.
    - The footprints of the 205 and the E-Class take their drawn bodies' widths where the models are wider than the
      cars (above). The W212's rear track is 1614 mm at auto-data.net, thecarspec and cararac and 1604 only at
      cars-data: the test's mean is 1.606 m, the simulation's 1.60 within 0.4 %. The Testarossa's tracks are usually
      published as 1520 / 1660 mm (the drawing the flat-twelve's keys come from has 1677 behind), only a comment.
    - Left: the R32's tyres stand 2.2 cm over the road, as they did before dims6. Its model's tyres are drawn loaded,
      flattened at the bottom with the sidewalls bulging beside the flat, and the preparation takes a wheel's pivot at
      its tyre's box centre and the radius at its farthest vertex: the front pivots sit 1.1 cm over the tread's true
      centre (the tyres turn off-centre) and the bulge is taken for the radius, so the round tread is drawn 0.307 m
      against the simulation's 0.319. Fitting the tread's circle away from the flat would cure both, but it changes
      `car_models.rig`, which both preparations share, for every model, and wants its own pass.

22. **The 570S's driving position** (fixed in v8seat). From the McLaren's seat the road was a strip a pixel high
    between the wheel and the header, the dashboard and the wheel filled the lower half of the frame and the right
    half was a grey wash. The preparation sets the eyes 13 cm under whatever lies over them and passes over a seat
    by its shape (item 21: no wider than 0.7 m), since a backrest stands at head height just behind the eyes; the
    570S model's two bucket seats are one mesh 1.34 m across, which the rule let through. Their box reaches 1.04 m
    over the road, and their backs, 0.975 m up where they rise behind the eyes, had the eyes set 13 cm under them,
    at 0.845 m (the preparation's default put them at 0.936; the real car's roof is 1.202 m high and its driver's
    eyes near a metre up). The rule now passes over a mesh up to 1.4 m wide, a seat pair, that is no deeper than a
    seat and rises from 0.4 m under the eyes, which nothing over the head does. On top of that the entry corrects
    the 570S's eyes 5 cm up and 3 cm back on the default (28 cm over and 52 cm behind the wheel's centre), to 0.33 m
    over and 0.55 m behind it, as a reclined supercar seat has them: the eyes at (-0.358, +0.355, -0.014), 0.986 m
    over the road, 0.67 m over the model's cushion (0.30-0.32 m up, measured along the driver's line) and 0.155 m
    under its roof skin, the head's top 3 cm under it. The regulations' stand-in for a driver's eyes, the V points
    of UN Regulation 125 (Directive 77/649/EEC before it), sit 589-665 mm over the seat's R point and 68 mm behind it
    at a 25 degree seat-back; the R point is the design hip pivot, which sits over the cushion, so the 0.67 m over
    the cushion is not the same measure and says only that the eyes are no higher over the hip than that. No R point
    of the 570S is published. The model's seat back is no guide to the eyes: at their height its face is 5 cm
    behind them (z +0.035 across the bucket), where a head, a man's some 20 cm long with its back about 18 cm behind
    the eyes, would stand 13 cm into it; read off that seat the eyes would be 0.42 m behind the wheel's centre at
    most, closer than a driver sits. The model's buckets sit forward of where its wheel puts the driver, and the
    eyes follow the wheel, which the driver sees; the seat behind them shows only to a look across the cabin (the
    passenger's head restraint level with the eyes, in the right part of a look 60 degrees right). The mirror hangs
    at (0, +0.411, -0.506), 5.6 cm over the eyes and 36 degrees right. At the frame's centre column the dash top is
    now at 58 % of the height in the parked seat shot with 83 rows of road over it (was 1), where the other cars'
    seats have it (flat-twelve 63, Delta 60, estate 58, E-Class 56 %); over the 40 s attract lap its median is 54 %
    (main 49 %) and 9 of 80 frames show almost no road (main 16).

    The grey wash was neither the cabin probe nor the glass: the model's dashboard, scuttle and door tops are gloss
    black (roughness 0.18-0.22) and mirrored the sky at the low eye's grazing angle, where the real car's are leather
    and, on the dash top, Alcantara, which has almost no sheen. The entry corrects their finish (`materials` in
    `tools/car_models.json`, applied by the preparation): plastic.black and McLaren.black at roughness 0.88, as the
    game's own dark cabin trim (0.85) and rubber (0.86). At 0.6 and 0.55, as first set, the sun ahead still spread
    over the right half as a flat grey sheen. Measured over the same deterministic 40 s cockpit lap (`--write-movie`,
    30 fps, 80 frames at 2 fps), the right half's dash band (rows 560-760, x 1000-1550 of 1600 x 900) went over 65
    of 255 in 12 frames at 0.6/0.55 (peak 84.6, when the sun is ahead), in none with the model's gloss (peak 58.4)
    and in none at 0.88 (peak 52.8); the dash top's band averages 88.5 with the gloss, 66.0 at 0.6/0.55 and 58.1 at
    0.88. Against 0.6/0.55 the orbit views change only on the black trim and the cabin seen through the glass
    (0.7-1.1 % of the pixels), the chase view by 0.4 % over a 20 s movie, as much as main differs from it there.

    The dials: with the eyes 14 cm higher the line of sight comes down steeper over the rim, and the panel as first
    placed (on the binnacle's face, 0.39 m wide) lost the tachometer from 3.75 to 6.75 thousand rpm under the
    binnacle's hood lip, half the gear readout under the rim and the speedometer at 200 km/h. Projected from the eyes
    onto the panel, the cabin and the wheel between them leave a window under the hood and over the rim; the entry
    sets the panel 0.31 m wide, 1 cm right and 4 mm down on the binnacle's face (dials width and offset) and moves
    the gear readout up the panel to where that window is (dials gear_top: its box from 44 px down the 1024 x 400
    cluster instead of 120; the preparation hangs a child Gear under Dials, CarView hands it to InstrumentCluster;
    every other car keeps the readout where it was). From the eyes at rest the speed reads from 30 to 270 km/h and
    the rpm from 3,000 to 8,250, with the gear readout wholly in view; leaning forward at speed (the cockpit camera's
    6 cm) to 8,500, and the readout stays in view for the head's sway of 4.5 cm either side and 3 cm back. The
    automatic box changes up at 7,965 rpm (0.885 of the 9,000 redline), inside that; the needle's last 500 rpm
    before the red sector and the tachometer's hub pass behind the rim, which crosses the panel's lower half.

    Where the preparation put each car's eyes, dials and mirror now goes to `tools/car_cockpits.json`, rewritten with
    every car it prepares (the models are build output and not in the repository), so that the tests read what it
    did rather than its source text: the 570S's eyes 0.986 m over the road and 0.155 m under the lining with Object_16,
    1.337 m wide, passed over; every car's eyes between 0.95 and 1.30 m over the road (the 570S 0.986, the flat-twelve
    1.042, the saloon 1.227) and none closer than 13 cm under what is over them. The other seven models prepare to the
    same bytes. Test `A_seat_pair_in_one_mesh_does_not_lower_the_eyes`.

Item 23 came with cars13, when five more cars were added to the catalogue, specified like the respecified ones so that
they drive like the real cars before their models and sounds land:

23. **Five more cars** (cars13: the physics track; their models and their own engine banks follow on parallel
    tracks). Appended after the fleet saloon (catalogue indices 8-12, so a saved choice keeps its car): the flat-plane
    V8 of 2010 (`VehicleSpec.FlatPlaneV8`, after the Ferrari 458 Italia of 2009-15), the five-cylinder and the 1.6
    hatchbacks of 2001 (`HgtFive` and `HatchSixteen`, after the Fiat Bravo HGT 2.0 20V of the 1998-2001 facelift and the
    Bravo 1.6 16V), the hot hatch of 2018 (`CupraTsi`, after the SEAT Leon Cupra 300, the Mk3 facelift; since phys2
    the same car with its platform's 245 PS tune, below, so the table's Cupra column is the 300 PS car of cars13) and the
    coupe of 2013 (`Boxer`, after the Subaru BRZ ZC6 of 2012-16). Each is set from published figures as items 19-21 set the
    others (the sources in the specs' comments; a figure none publishes is said to be a game figure there), the mass
    with a 75 kg driver as every spec carries it, the size the real car's (one track, the published mean), the engine's
    curve through the published torque and power points.

    | | 458 Italia | Bravo HGT 2.0 20V | Bravo 1.6 16V | Leon Cupra 300 | BRZ |
    |---|---|---|---|---|---|
    | name; `--car=` | V8 4.5 '10; `458`, `flatv8`, 8 | HATCH 2.0 20V '01; `bravohgt`, 9 | HATCH 1.6 16V '01; `bravo16`, 10 | HOT HATCH 2.0 TSI '18; `leoncupra`, `cupra`, 11 | COUPE 2.0 BOXER '13; `brz`, 12 |
    | engine | 4.5 flat-plane V8, 570 CV at 9000, 540 N m at 6000, redline 9000 | 2.0 twenty-valve five, 155 PS at 6500, 186 N m at 3750 | 1.6 sixteen-valve four, 103 PS at 5750, 144 N m at 4000 | 2.0 TSI four, 300 PS at 5500-6200, 380 N m at 1800-5500; its turbo makes 55 % of it, spooling in 0.35 s from 1200 rpm | 2.0 boxer four, 200 PS at 7000, 205 N m at 6400-6600, redline 7400, the trough at 4000 |
    | mass with a driver (published kerb) | 1560 kg (1485) | 1240 (1165) | 1135 (1060, the SX; 1025 base) | 1395 (SEAT: 1375 with a driver for the three-door) | 1315 (1240 in Europe; 1253 US) |
    | weight on the front axle | 42 % (Ferrari) | 62 % (a game figure) | 62 % (a game figure) | 60 % (a game figure) | 53 % (Subaru) |
    | gearbox | Getrag 7-speed dual clutch, 3.08-0.69 on 5.14, 0.03 s a shift | 5 speeds, 3.55-0.95 on 3.56 | 5 speeds, 3.909-0.921 (the C.514's) on 3.56, set to cars-data's 2,900 rpm at 100 km/h | 6 speeds, 3.357-1.088 on 3.944 and 1.097 / 0.912 on 3.087 (SEAT's sheet) | 6 speeds, 3.626-0.767 on 4.10 (Subaru's 2013 sheet) |
    | driven wheels; differential | rear; the E-Diff as a clutch-type unit locking 35 % under power, no preload | front; open | front; open | front; the VAQ as a clutch-type unit locking 40 %, no preload | rear; Torsen, 2.5:1 taken, locking 0.21 |
    | ABS / traction control / launch control | yes / F1-Trac / yes | yes / no / no | yes (the GT's) / no / no | yes / ESC / no | yes / VSC / no |
    | speed limiter (`SpeedLimiterKmh`) | none | none | none | 250 km/h | none (its top speed is the drag's) |
    | tyres | 235/35 R20, 295/35 R20 | 205/50 R15 | 185/65 R14 | 235/35 R19 | 215/45 R17 |
    | length x width x height; wheelbase; tracks, mm | 4527 x 1937 x 1213; 2650; 1672 / 1606 | 4025 x 1755 x 1410; 2540; 1471 / 1430 | 4025 x 1755 x 1414; 2540; 1439 / 1441 | 4281 x 1816 x 1443; 2631; 1536 / 1510 | 4240 x 1775 x 1285; 2570; 1520 / 1540 |
    | Cd; lift | 0.33; 140 kg of downforce at 200 km/h, 41/59 (370 kg at 325) | 0.32 (a game figure) | 0.32 (a game figure) | 0.32 (a game figure) | 0.29 (Subaru Canada, no spoiler) |
    | 0-100 km/h, published | under 3.4 s | 8.5 s (cars-data's table: 8.0) | 11.0 s | 5.8 s, the five-door's (5.7 the three-door's) | 7.6 s (Edmunds: 0-60 mph in 7.3 s, the quarter mile in 15.3 s at 92.1 mph) |
    | 0-100 km/h, here (revved on the brake; floored from idle) | 3.42 s (3.58) | 8.77 (8.94) | 10.97 (11.17) | 6.07 (6.22) | 8.07 (8.45) |
    | 0-60 mph launched as a road test (revved on the brake, each gear to the limiter or until the next pulls harder), here; from idle with the shifts at 0.93 of the limiter, as until the review | 3.23 s (3.45) | 8.02 (8.72) | 10.13 (10.78) | 5.77 (6.25) | 7.20, the quarter mile in 15.4 s at 94 mph (8.28) |
    | 100-0 km/h dry (rain), here; published | 30.8 m (44.0); Ferrari's 32.5 | 36.4 (51.9); none | 39.8 (56.3); none | 33.8 (47.7); none | 36.5 (52.6); Car and Driver's 165 ft from 70 mph, about 39.6 |
    | top speed, published | over 325 km/h | 213 km/h | 184 km/h | 250 km/h, limited | 226 km/h (230 in cars-data's listing) |
    | top speed, here | 323 km/h (201 mph) in 7th at 9,000 rpm, on the rev limiter | 219 km/h (136 mph) in 5th at 6,800 rpm | 191 km/h (119 mph) in 5th at 5,650 rpm | 247 km/h (154 mph) in 6th at 5,790 rpm, on the speed limiter | 246 km/h (153 mph) in 6th at 6,670 rpm |
    | understeer, roll (40 m circle) | 1.10, 1.50 deg/g | 1.78, 2.99 | 2.16, 3.26 | 1.73, 2.31 | 1.25, 2.12 |
    | loop run-up (needs 1.35 m/s^2) | 9.74 | 3.13 | 2.32 | 5.93 | 3.71 |
    | scripted laps, stunt / speed route, dry, no traffic | 85.8 / 100.9 s | 96.6 / 107.4 | 100.5 / 111.4 | 91.0 / 103.9 | 96.5 / 107.1 |
    | drawn as, until its model lands | the V8's loft at its axles | the loft keyed on the 1995 Bravo | the same | the loft keyed on the 2007 Leon | the V8's loft (a stand-in: its glass deck is a mid-engined car's) |
    | sounds from, until its banks land | "v8" (whose run-up and overrun were cut from a 458) | "hothatch", laid at a five's firing | "hothatch" | "turbohatch" (the Pulsar's turbo four, with the synthesised whistle and vent) | "v6", laid at a four's firing (see below) |

    The choices. The 458's E-Diff is a multi-plate clutch across the rear differential pressed by a hydraulic actuator
    under the same ECU as F1-Trac, from open to locked (Ferrari's description; the semi-active differential Ferrari,
    Magna Steyr and the Politecnico di Milano developed for the F430, IFAC 2005): the game's clutch-type differential
    model takes it as a locking share of 35 % of the drive torque under power and no preload (open without pressure),
    a game figure, since Ferrari publishes no lock figure, set a little under the flat-twelve's 40 % plate unit because
    the real strategy opens the clutch on turn-in, which the model's law does not; F1-Trac is `TractionControl` and the
    gearbox's launch mode `LaunchControl`. The Cupra's VAQ is a clutch pack across the front differential on Haldex's
    fifth-generation hardware, locking up to 1600 N m as the ESC reads the steering angle, the wheel speeds and the yaw
    rate (Volkswagen's description of the Golf GTI Performance's unit; PistonHeads, "VW's VAQ diff explained"): a
    locking share of 40 % under power, no preload, which hands the drive to the outside wheel out of a bend as the unit
    is meant to; its ESC's brake function (XDS) is left out, the VAQ doing its work, so the brake-based differential
    stays on the three cars that had nothing else (`ElectronicDifferentialTests.Only_the_cars_that_had_one_brake_a_spinning_wheel`
    is unchanged). The BRZ's Torsen is the existing Torsen path (a bias ratio of 2.5:1 taken as a Type-2 unit's, since
    none is published: (2.5 - 1) / (2 x 3.5) = 0.21, no preload) on the rear axle through `LimitedSlipLocking`. The
    Cupra has a speed limiter, which the model did not have: `VehicleSpec.SpeedLimiterKmh` (infinite on every other car;
    managed only) has `Drivetrain.Step` take the throttle back over the last 3 km/h under the limit and hold the car there,
    the smooth hold of the real limiters rather than the rev limiter's cut (the sound hears a lift, not a stutter); SEAT
    quotes its 250 km/h as limited. The BRZ has none, though its 226 km/h is sometimes given as electronically limited:
    nothing bears that out (Subaru's specification sheets give no limited speed, the 2026 BRZ's none at all; cars-data
    lists the 2013 Sport at 230 km/h; an owner's run to an indicated 237 km/h, Carscoops 2013, ended on the drag). Its
    226 km/h is the drag's, in sixth at about 6,000 rpm, and here, on the
    published curve, Cd 0.29 and a frontal area of 2.0 m^2 estimated from its size, it runs to 246 km/h at 6,670 rpm,
    8 % over (within the tenth either side the other cars are held to; the HGT and the Bravo 1.6 are 3-4 % over theirs):
    the model's rolling and driveline losses do not grow with speed as a real car's do, which a later pass on the drag
    of every car could take up.
    The Cupra: the user asked for a 240 HP car; the 2018 Leon Cupra was sold at 300 PS until the petrol particulate
    filter of autumn 2018 brought it to 290 PS (its 380 N m kept), and the game follows the 300 PS car of SEAT's sheet,
    the tune published in full with its gearing; 290 can be set later by lowering the top of the curve (phys2, below,
    gave it its platform's 245 PS tune instead, as an assumption the user can reverse). The Bravo 1.6's
    gearing: no ratio table was found for its five-speed, so the C.514's ratios as a Fiat forum gives them for the Punto
    1.2 16V stand in on a final drive that gives cars-data's 2,900 rpm at 100 km/h; its ABS is the GT trim's (standard
    there, optional below). The HGT's final drive is carfolio's 3.56 under cars-data's ratios, which put its 213 km/h at
    6,530 rpm in fifth, on the power peak. The 458's gearing (3.08-0.69 on 5.14) is what the V8 of 2012 already carried,
    which was the 458's table by another name; the 570S's own gearbox was never the game's.

    Where they land against the published figures (ranges a tenth either side in `HandlingTests` and `VehicleTests`, as
    items 19-20 set them): the 458 at 3.42 s to 100 km/h against Ferrari's "under 3.4" (traction-limited in first at
    0.87 of its tyres' peak by its launch control, like the 570S; 0.88 before the review gave it heavier wheels), the
    HGT 8.77 against 8.5, the Bravo 1.6 10.97 against 11.0, the Cupra 6.07 against the five-door's 5.8 (a
    front-wheel-drive car of 300 PS is traction-limited; the launch assist holds its front tyres at 65 % of their peak
    slip as a driver's foot does, where SEAT's figure has the VAQ and a launch procedure), the BRZ 8.07 against 7.6 (a
    rear-drive 53/47 car on touring tyres, launched at 5,400 rpm where 95 % of its peaky curve's torque is). Stops: the
    458's 30.8 m against Ferrari's 32.5 on its carbon-ceramic brakes at a grip of 1.17 (Automobile Magazine's 458 held
    1.04-1.08 g); the others have no published stop and take the class's range (34-44 m for the HGT, the Delta's and
    R32's class; 38-47 for the 1.6 on its drums; 31-38 for the Cupra; the BRZ's 35-44 a tenth either side of Car and
    Driver's). Top speeds: the HGT's 219 km/h against 213 and the 1.6's 191 against 184 are 3-4 % over, the drag letting
    them past the power peak in fifth (the estimated Cd of 0.32), and the BRZ's 246 against 226 8 % over (above); the
    Cupra sits on its limiter. The skid-pad ranges are the class's narrowed round each car's own figure (0.6 deg/g
    either side), as the fleet saloon's were: no understeer gradient or roll rate is published for any of the five, so
    they guard against a change, not against a measurement. All five hold their launch revs with the driven tyres short
    of their peak, launch without a bog floored from idle, lap both routes dry without a crash and make the loop
    (2.32-9.74 m/s^2 at 50 mph); the 458 tucks its tail on a lift at the limit as the other mid-engined cars do, and its
    weight and the Cupra's move forward under braking as statics says.

    Every place that enumerates the catalogue was extended: the `CarStyle` enum and `Catalogue`; `Bootstrap.CarIndex`'s
    ids and aliases (`--car=458|flatv8`, `bravohgt`, `bravo16`, `leoncupra|cupra`, `brz`, or 8-12); `CarView.Model.cs`'s
    model keys (`458`, `bravohgt`, `bravo16`, `leoncupra`, `brz`: the files the model track delivers to
    `game/assets/models`, drawn as soon as they are there) and the lofts above (`BodyStyle.FittedTo` stretches each to
    the car's axles, so the arches sit over the wheels); the voicing of the mid-engined cars (`SampledEngine.VoicingOf`,
    `NativeApi.VoicingOf`) and the V8's overrun pops, which the 458 shares; the two model preparations' SPECS
    (`tools/car_models.py`, `unreal/Scripts/prepare_car_model.py`; `The_model_preparations_have_the_simulations_geometry`
    reads them for every car); `unreal/Scripts/build.cmd`'s export loop (since the review) and `build_scene.py`'s car
    list (GLP.Car8-12, each filled from its export with the lofted stand-in, empty while the export is missing) and
    `GlpAudio.cpp`'s bank table and its pops, the 458's too (neither built nor run here: the Unreal tree is kept
    consistent). The `SoundFolder` of
    each new engine points at the nearest existing bank for now, as the table says, with the cylinders of the real
    engine (the HGT's five is laid on the hot hatch's bank at a five's firing, with the five's exhaust beat); stage 2
    swaps the folders for their own banks. The BRZ was to play the hot hatch's bank too, the nearest four in character,
    but its idle was recorded at 1,140 rpm and the FA20 idles at 650 +/- 100 (Subaru's service manual; 700 here): laid
    at 61 % of its firing the gaps between the grains fell 24-28 dB for 10 ms at a time and the level stepped 1.2 LU at
    1,400 rpm (`EngineVoiceTests`: the idle, the sweep and the level over the revs failed for it alone; the Bravo 1.6,
    at 75 %, passes with 17 dB). On the estate's idle (810 rpm) the level still sagged 2.6 LU over the revs, on the
    saloon's (810) it stepped 2.2 LU at 800 rpm; the VR6's, recorded at 590-735 rpm of a six (30-37 Hz against the
    boxer's 23 Hz), passes every voice test, and its run-up, held to the period past its top, carries the boxer to
    7,400 rpm as it carries the VR6 to 6,600. The diesel's knock test compares the diesel with every petrol four's
    voice, the Bravo 1.6 and the Cupra on their borrowed banks too, and the BRZ as a named exception while it plays the
    V6 hatch's (`DieselCharacterTests.BorrowedSixBanks`, keyed by its style and the folder, so it lapses when "boxer4"
    takes the folder's place): the VR6's recording, which no four played before, carries -13.7 dB of its sound between
    1 and 3 kHz idling at the BRZ's 700 rpm against the diesel's -9.3, a six's brighter exhaust, where the test asks
    the diesel to be 6 dB over the fours'; the BRZ is held 3.5 dB under it idling (measured 4.4) and to the others'
    3.5 dB at 1,500 rpm (measured 9.4), and its banks, a six's recordings (the run-up swings 0.32 on its marks against
    the diesel's 0.24), are left out of the comparison on their marks. Until the review the test dropped it silently,
    filtering out every four on another engine's banks; a player hears its idle nearly as bright as the diesel's
    until its banks land. The audio library was left as it is (the gaps under a recording stay quiet by design,
    `GrainRing.Take`); the boxer's own bank, recorded at its idle, ends it and puts the BRZ back among the fours. The native
    library grew one function for the shared folders: `glp_engine_create` finds a car by its folder's name, which names
    one car only while every car has its own banks, so the banks the new cars borrow would have voiced and trimmed them
    as the cars that own them (the two Bravos as the 205, the BRZ as the R32); `glp_engine_create_for_car(folder, car)` makes
    the voice for the catalogue's car (its cylinders, voicing and redline) from whatever folder, `GlpAudio.cpp` calls it
    with the style (the catalogue's index), and `glp_engine_create` keeps its meaning (`NativeApiTests`: the trims of
    every car's voice equal the game's, the folder path's equal the first car's of the folder, the published library
    renders the Bravo HGT on the hot hatch's banks as the managed side does). The rest of the ABI is unchanged; the new
    function and the catalogue's five more cars (`glp_car_count`, `glp_car_spec`'s styles 8-12) make it ABI 8 (since
    the review: the Unreal loader refused a library without the symbol already, and now the version says so too).

    The eight cars of 0.24.0 are unchanged bit for bit: a hash of their whole state over both routes dry and wet, the
    40 m pad, a sprint, a stop and a bend exit (as the reviews of dyn5, fleet and dims6 made it) matches 0.24.0's line
    for line, and every figure their tests print is the same. The speed limiter's code is entered only by a car that
    has one.

    Left to the other tracks and to later: the five models (`tools/car_models.json` entries, their licences in
    THIRD_PARTY.md and `unreal/Import/Models/LICENSE.txt`) and the five banks (`v8flat`, `five`, `four16v`, `tsi4`,
    `boxer4`, in `game/assets/audio/SOURCES.md`), whose folders the specs then take; the Unreal exports of the five
    cars; an understeer or stopping figure for any of them if a road test turns up; the E-Diff's and the VAQ's control
    laws (lock with the steering angle and the yaw rate, open on turn-in), which the clutch-type law does not have; the
    BRZ's voicing as a coupe with a boot (1.9 m, the saloons') once it has its own bank (the audio tests voice a folder
    as the first car that plays it, the R32 for "v6").

    The review of cars13 (merged clean into 0.24.0, the eight cars bit for bit) and what came of it. The 0-60 test
    (`VehicleTests.The_cars_of_cars13_match_their_published_figures`) had driven the five as the hatchbacks are driven,
    floored from idle with the shifts at 0.93 of the limiter, a tenth under the published figure to a fifth over, the
    fifth over meant for a turbo waiting for its boost: the BRZ took 8.28 s against 7.22 (Subaru's 7.6 s to 100 km/h
    less a twentieth) and passed on that allowance. The car was right and the method was not: from idle the shift into
    first and the revs rising cost the cars about half a second before they move (a fifth on the 458 with its launch
    control), and at 0.93 of its limiter the BRZ gave up second at 87 km/h for a third gear landing in the FA20's
    trough, where its second runs on to the limiter just short of 60 mph (7,400 rpm is 97 km/h on the free tyre radius,
    less the tyres' slip). Launched as Edmunds launched one, revved on the brake with each gear held to the limiter or
    until the next pulls harder (`RoadTestZeroToSixty`), it takes 7.20 s against their 7.3 and runs the quarter mile in
    15.4 s at 94 mph against their 15.3 s at 92.1: the trap speed, which the launch does not reach, says its power, mass
    and drag are right, so the curve was left as it is (a fuller curve or less drag, which the review suggested, would
    have made it quicker than the road test). All five are now held to a tenth either side of the published figure less
    a twentieth, the turbo Cupra too: 3.23 / 8.02 / 10.13 / 5.77 / 7.20 s against 3.23 / 8.08 / 10.45 / 5.51 / 7.22. The
    hatchbacks of cars3 keep their own test and its fifth. The BRZ's top speed stays 8 % over (above); its window is
    unchanged.

    The 458's rear wheels hunted at the step rate launching out of a tight turn (launch control, 60 % steer: the two
    rear wheels' speed difference changed sign every 240 Hz step for 31 steps, 13 in the rain; the 570S 5, the 458
    without its launch control none): its wheels carried the default 1.3 kg m^2, which is a 16 in wheel's. A rear
    corner of the 458 turns about 1.8 (a 295/35 ZR20 tyre of about 12.5 kg at a radius of gyration of about 0.32 m,
    a forged 20 x 10.5 in wheel of about 11 kg, the 360 mm carbon-ceramic disc, the hub and the half shaft: a game
    estimate, as Ferrari publishes no inertias), and at 1.8 the run is 1 step, none in the rain
    (`HandlingTests.A_launch_out_of_a_tight_turn_does_not_set_the_driven_wheels_hunting` holds both cars with launch
    control to 8 at most). At the start of a scripted lap the longest run falls from 39 steps to 3 (19 to 1 in the
    rain), the sign changes on the road from 0.47 a second to 0.035. At 80 % steer it falls from 41 steps to 13 and
    at full lock it is 21 (16 before), the 570S's 13 and 19 there: the launch governor's own alternation at full lock
    is shared with the 570S and left to a pass on the governor, which would change the eight cars. Its 0-100 km/h is
    3.42 s (3.40), its stop 30.8 m (30.7), its stunt lap 85.8 s (85.7), its skid-pad figures as before. Its one wheel
    radius stays 0.345 m, the mean of the front and rear tyres' free radii, so a model drawn with both sits on the road
    (the preparation sets the hubs at their mean), and about the driven tyre's rolling radius under load (97 % of
    0.357): it gives 330 km/h at 9,000 rpm in seventh, so the real car's "over 325" lies within a few per cent of its
    cut too. Here it tops out at 323 km/h on the cut, as the 570S does, the model's road load not growing with speed;
    on the rear tyre's free radius it ran to 335 km/h, still on the cut, and the drawn tyres would stand a centimetre
    off the road.

    The rest of the review: the Unreal build loops over the five's exports (`build.cmd`, see above); ABI 8 (above);
    the diesel's knock test names its exception (above); the BRZ's rear discs are 290 mm (11.4 in), its fronts 294;
    the Cupra is the five-door throughout, its sprint held round SEAT's 5.8 s for it (5.2-6.4 s), not the
    three-door's 5.7. The Torsen's law here, the share of the drive torque passed from the faster wheel whatever the
    slower one can take, still sends a fifth of the axle's torque to the grounded wheel with the other in the air,
    where a real Torsen, biasing torque against the other wheel's, sends almost none; the Delta's rear works the same
    way, and a law with the bias's own limit is left to a pass on both. The E-Diff and the VAQ stay first
    approximations as said above (no lock with the steering or the yaw, none on the overrun, the VAQ's light-throttle
    opening not modelled). Open for the user: the Cupra. The user asked for a "Seat Leon Cupra 2018 240HP 2.0TSI";
    no 2018 Leon Cupra had 240 HP. The 240 PS Cupra is the Mk2 (2006-12, sold as the "2.0 TSI Cupra" from 2009:
    300 N m from 2200 rpm, 6.4 s to 100 km/h, 247 km/h), and the nearest later one the Cupra Leon VZ 245 (2020 on).
    The 300 PS Mk3 of SEAT's 2017 sheet stays until the user chooses among the three. (Phys2, below, took the
    nearest to the request as an assumption the user can reverse: the 2018 car with the 245 PS tune of its platform.)

    Stage 2 (cars/stage2: phys13, models13 and sound13 merged onto 0.25.0, the McLaren's cockpit fix and the
    showroom's paint in it). The table's last two rows are history now: the 458 is drawn by its model (models13, with
    the 570S's cabin; its cockpit and paint below, under Real car models) and the other four by their lofts until a
    model of each is found; every engine names its own bank (`SoundFolder`: the 458 `v8flat`, the HGT `inline5`, the
    Bravo 1.6 `four16v`, the Cupra `tsi4`, the BRZ `boxer4`; `GlpAudio.cpp`'s table alike; under Sound, cars13 stage 2).
    The BRZ is voiced as the coupe with a boot it is, the saloons' 1.9 m cabin, as this item left for its own bank,
    and the diesel's knock test holds it to the same margins as every petrol four. Each car's ORIGINAL paint is its
    real car's launch or press colour (under Each car's paint). No physics value changed: the eight cars' and the
    five's figures are this item's.

    Bodies (cars/bodies, under Real car models): the BRZ is drawn by a model, a first-generation Toyota 86 (its twin,
    the same shell) with the W212's cabin; the two Bravos and the Leon Cupra by lofts of their own
    (`BodyStyle.BravoHgt`, `BravoSixteen`, `LeonCupra`), since no credible model of either exists.

    Phys2 (cars/phys2, on cars/stage2): two spec decisions, the Cupra's engine and the BRZ's generation, in
    `VehicleSpec.cs` and `EngineModel.cs` with their tests. Every other car is bit for bit as it was: the whole-state
    hash of the cars13 review (both routes dry and wet, with and without traffic, the assisted manual and the automatic,
    three laps each, the 40 m pad, a sprint to top speed and the stops dry and wet) matches cars/stage2's line for line
    for cars 0-10, and cars 0-7 match 0.25.0's as they did. The prepared models are byte for byte stage 2's (no geometry
    changed).

    The Cupra: the 245 PS tune of its platform, an assumption the user can reverse. The user asked for a "Seat Leon
    Cupra 2018 240HP 2.0TSI", and no 2018 Leon Cupra had 240 HP (above). The game keeps the 2018 Mk3 five-door's body
    and chassis (its size, 1395 kg with a driver, 235/35 R19 tyres, brakes, the VAQ, the 250 km/h limiter, its own
    loft since cars/bodies) and gives it the 245 PS (180 kW, 241.6 hp) tune of the EA888 generation-3 two-litre that
    the same MQB platform carried in 2017-20 in the Volkswagen Golf GTI Performance (Mk7.5) and the Skoda Octavia RS
    245: 245 PS from 5000 to 6700 rpm and 370 N m from 1600 to 4300 at 9.6:1 (Skoda's technical data of the Octavia
    Combi RS 245, 5 June 2017; Volkswagen's newsroom, 13 April 2018; ADAC's catalogue gives the Golf's band as
    4,700-6,200), the full boost from 1600 rpm with the turbo's threshold 600 under it (1000 rpm; the off-boost share,
    45 %, and the 6,800 rpm limiter are the 300's), in `EngineSpec.TsiFour` and `TsiFourCurve`. The gearbox is the
    six-speed manual of the 245 PS cars (3.77 / 2.09 / 1.48 / 1.09 on a 3.45 final drive, 1.10 / 0.91 on 2.76, reverse
    4.55: Skoda's sheet), not the seven-speed DSG (DQ381: 3.40 / 2.75 / 1.77 / 0.93 / 0.71 / 0.76 / 0.64, reverse 2.90,
    on 4.17 / 3.13, the same sheet). Both sets of ratios are published there; the one independent road test of the
    platform's 245 PS cars found drove a manual (Auto Express's six-speed Octavia vRS 245 hatch, 16 August 2017: 6.8 s
    to 60 mph, 5.5 s from 30 to 70 mph through the gears, 4.1 s from 30 to 50 in fourth; the same figures for the vRS
    Challenge of 18 June 2019), no magazine's timing of a DSG car of that generation turned up (the one DSG timed with
    the tune is the next generation's Octavia RS of 2021, below), and the manual carries the 250 km/h both makers
    publish (the DSG Golf's is 248). The showroom's facts line says 6-SPEED MANUAL and is right: the two final drives
    the spec carried, which the stage-2 report took for a DSG's, were those SEAT's sheet gives the Cupra 300's manual
    (two output shafts, as on the R32's 02M), and the 245 PS manual has two as well; only the order 1-4 on the first and
    5-6 on the second keeps its overall ratios falling. The mass stays the 300's (the same engine with a manual of the
    same family; ADAC lists the 245 PS Golf five-door manual at 1387 kg with a driver). The targets: Volkswagen's 6.2 s
    to 100 km/h for the Golf GTI Performance with either gearbox, the Leon's twin in size and mass, a tenth either side
    for the sprint (5.6-6.8 s, `HandlingTests`) and, less a twentieth, for the 0-60 mph road-test launch (5.3-6.5 s,
    `VehicleTests.The_cars_of_cars13_match_their_published_figures`), a window Skoda's 6.6 s for the heavier Octavia
    falls in too; 250 km/h, limited (150-158 mph); and Auto Express's 30-70 mph through the gears, a tenth either side
    (4.95-6.05 s, the new `VehicleTests.The_Leon_Cupra_runs_30_to_70_mph_through_the_gears_as_its_platforms_road_test`,
    on `RoadTestTimes`, the marks of the 0-60 run). The magazine's 0-60 mph, 6.8 s, slower than Skoda's own 6.6 s to 62
    mph for the car, is not taken as the launch's. The quarter mile (added in a second phys2 pass): no time was found
    for the platform's 245 PS cars of 2017-20, and the one found for the tune is Wheels' for the next generation's
    Octavia RS, a larger car with the same 180 kW and 370 N m on the seven-speed DSG: 14.47 s at 163.08 km/h (101.3 mph)
    with a DriftBox, its best run, 6.15 s to 97 km/h and 6.41 s to 100 (Scott Newman, "2021 Skoda Octavia RS review:
    performance tested", whichcar.com.au, 24 September 2021). It is held a tenth either side, the time and the trap
    speed alike (13.0-15.9 s, 91.2-111.5 mph, the new
    `VehicleTests.The_Leon_Cupra_runs_the_quarter_mile_as_its_tune_was_timed`, on `RoadTestQuarterMile`, the 0-60 run
    carried on to 402.3 m): a check of the tune's pace and speed over the distance, not the Leon's own figure. The car
    runs it in 14.38 s at 101.7 mph (163.6 km/h).

    | Leon Cupra | 300 PS (cars13 to stage 2) | 245 PS (phys2) | published, the 245 PS cars |
    |---|---|---|---|
    | engine | 300 PS at 5500-6200, 380 N m at 1800-5500, full boost from 1800 | 245 PS at 5000-6700, 370 N m at 1600-4300, full boost from 1600 | Skoda, Volkswagen |
    | gearbox | 6-speed manual, 3.357-1.088 on 3.944, 1.097 / 0.912 on 3.087 | 6-speed manual, 3.77-1.09 on 3.45, 1.10 / 0.91 on 2.76 | Skoda's sheet |
    | showroom's figures line | 300 HP AT 6200 RPM, 380 NM AT 1800 RPM | 245 HP AT 5000 RPM, 370 NM AT 1600 RPM | 180 kW, 370 N m |
    | 0-100 km/h, revved on the brake (floored from idle) | 6.07 s (6.22) | 6.13 (6.27) | 6.2 (Golf), 6.6 (Octavia) |
    | 0-60 mph, road-test launch | 5.77 s | 5.83 | 6.8 (Auto Express, Octavia) |
    | 30-70 mph through the gears | 4.47 s | 5.05 | 5.5 (Auto Express, Octavia) |
    | 30-50 mph in fourth (a scratch rig, not a test) | 3.23 s | 3.80 | 4.1 (Auto Express, Octavia) |
    | 0-100 mph; the quarter mile | 12.2 s; 14.0 s at 108.2 mph | 14.0; 14.38 at 101.7 | none; 14.47 at 101.3 (Wheels, the 2021 Octavia RS, DSG) |
    | top speed | 247 km/h in 6th at 5,790 rpm, on the speed limiter | 247 in 5th at 6,240 rpm, on the speed limiter | 250 km/h, limited |
    | 100-0 km/h dry (rain) | 33.8 m (47.7) | 33.8 (47.7) | none |
    | understeer, roll (40 m circle) | 1.73, 2.31 deg/g | 1.72, 2.31 | none |
    | loop run-up (needs 1.35 m/s^2); launch revs | 5.93; 2,000 rpm | 4.78; 2,000 | |
    | scripted laps, stunt / speed route, dry, no traffic | 91.0 / 103.9 s | 91.7 / 104.4 | |

    How to switch back to the 300 PS car (every place is marked "The 300 PS alternative" in its comments): in
    `EngineModel.cs` put the points in `TsiFourCurve`'s remarks back as the curve and `TsiFour`'s `BoostThresholdRpm`
    back to 1200; in `VehicleSpec.CupraTsi` set `GearRatios = [-3.99f, 0f, 3.357f, 2.087f, 1.481f, 1.088f, 0.8587f,
    0.7138f]` and `FinalDrive = 3.944f` (the comment over them) and restore its summary's figures (SEAT's 5.8 s for the
    five-door, 250 km/h limited); in the tests put `HandlingTests`' sprint row for car 11 back to 5.2-6.4 s and
    `VehicleTests`' cars13 row to 5.0-6.05 s (both rows' comments give them), and drop the 30-70 mph and quarter-mile
    tests, which hold the 245 PS tune to the road tests of its platform and of its next generation (the 300 runs them in
    4.47 s and in 13.98 s at 108.2 mph, the quarter mile inside the 245's window; no 300's figure was sourced for
    either); then the docs (this item, the feel checklist's cars13 rows, both READMEs' "245 PS"). The 290 PS car of
    autumn 2018 would be the 300's curve with its top taken down to 213 kW.

    The BRZ: it stays the 2013-16 car. The drawn model is a facelifted first-generation twin (a Toyota 86), still the
    ZC6. The facelift of 2017-20 compared: in Europe it kept the engine and every figure the spec follows (200 PS,
    205 N m at 6400-6600, 7.6 s to 100 km/h, 226 km/h, 215/45 R17; 1243 kg at the kerb against 1240: motor1.com's German
    test of the facelift, 19 January 2017), with its revised springs and dampers and larger rear anti-roll bar published
    without rates (Subaru of America's release of 24 October 2016; motor1.com) and no final drive published. The
    American and Japanese manual took 205 hp at 7000, 156 lb ft at 6400 and a 4.30 final drive in place of the 4.10
    (Subaru of America, 24 October 2016), and Car and Driver timed its twin, a 2017 Toyota 86 manual on the same
    Michelin Primacy HP 215/45 R17 tyres (2,754 lb), at 6.2 s to 60 mph, 16.8 s to 100 mph, the quarter mile in 14.8 s
    at 95 mph, 134 mph flat out (drag-limited), 161 ft from 70 mph and 0.89 g (13 October 2016, as Yahoo Autos carries
    it). Those figures are well sourced, and set to them (the curve through 205 hp at 7000 and 212 N m at 6400-6800,
    `FinalDrive = 4.30`, the rest as it is) the car here runs 7.13 s to 60 mph launched as a road test (7.20 as it is),
    16.5 s to 100 mph and the quarter mile in 15.3 s at 95.8 mph: as far off that test's 0-60 mph as the 2013 car is off
    Car and Driver's 2013 figures (6.3-6.4 s; 0-60specs.com's list of the magazine's tests), its trap speed the test's.
    But flat out it runs to 253 km/h at 7,200 rpm in sixth, 12 % over Subaru's 226 and 17 % over Car and Driver's
    134 mph, out of the tenth either side every car's top speed is held to (the 4.10 car's 246 is 8 % over already): the
    model's road load does not grow with speed (above). The facelift's targets could not be kept with its figures, so
    the BRZ stays the 2013-16 car, which in Europe is also the facelift's engine and published figures; its targets are
    unchanged (Subaru's 7.6 s and 226 km/h, Edmunds' 7.3 s to 60 mph and 15.3 s at 92.1 mph over the quarter mile). One
    correction, the stage-2 report's open point: the curve's redline point is 189 N m (190 before), so the power peaks
    at 7000 rpm as Subaru publishes it and the showroom reads 200 HP AT 7000 RPM (7400 before, 0.1 % over the peak). It
    changes only the race runs' state, where the engine passes 7000 rpm, not their lap times; the pad, the sprint, the
    stops and every figure the tests print are as they were (7.20 s to 60 mph, 246 km/h, 96.5 / 107.1 s a lap); the
    quarter mile, run on past second gear's limiter, is 15.43 s at 94.1 mph (15.42 at 94.0). To move to the American
    facelift later: that curve and final drive, the name's year '17, and the top speed's window settled first, by a pass
    on every car's road load or a published top speed of the 4.30 car (Subaru gives none; Car and Driver's 134 mph is
    the 86's).

Kept, as earlier decisions: the fixed 240 Hz step with semi-implicit Euler for the body, the raycast wheels, the
grass's grip and rolling resistance (0.9), the weather's grip figures (0.12), the speed-sensitive steering lock,
the clutch's lock/slip machine, and the low-speed fades of the slip angle (no jitter at rest).

## View (`game/`)

- World is generated from the track at start-up (`WorldBuilder`): terrain mesh sampled from the ground
  field (embankments, cuttings, dips, rolling fields, the hill ring) every 14 m and every 2 m in the cells a
  road's earthwork reaches, 4 cm under the asphalt, with vertex-colour variation and a trodden tint along
  the road edges (the 14 m grid alone floated the road over dips and buried it where it levels out), and
  round it a flat frame of grass to the horizon with the terrain's ground cut out of it. Until 0.8 the
  terrain's triangles faced down and were culled from above: a whole flat plane just under the terrain stood in
  for the ground, the hills showed the undersides of their far slopes, and once the cuttings went 4.5 m down
  the plane lay across them and their roads; the
  photo-textured road (CC0 ambientCG) as a grid of 16 stations every half metre that follows `RoadWear`
  (sunk by its depth, normals from the slope), with the alternating slab shades of the original, darker
  wheel tracks, patches of paler and darker asphalt, potholes darkened in their bowls, a grain per vertex, and
  over it all a wear sheet (`tools/make-road-wear.py`: tar snakes, patch seams, oil spots) multiplied in on a
  second UV set that drifts across the road so its repeat never reads; the first sheet, with 26 near-black
  snakes, looked like a spider's web from the car and was cut to nine faint ones; 2.2 m orange shoulders all
  the way round, as on the 1989 circuit, with a tone that wanders every two metres, one dashed yellow centre line
  that skips a dash here and there and no edge lines (the orange marks the edges, as in the original); bare
  earth (the dirt photo, red-brown) wherever the ground has been pulled more than about two metres off the plain
  to meet a road, the fork hill and the walls of the cuttings, with the grass browning towards it; bridge decks with steel guardrails and
  pillars down to the ground, the loop's steelwork (a ring of girders outside the deck on both sides, struts,
  cross-beams and braces under it, legs to the ground where it is low), the drawbridge's white A-frame
  towers joined by a portal beam over the road with three cables back to each ramp, the skid pad; a sheet of
  fair-weather clouds (`tools/make-clouds.py`, periodic fractal noise so it tiles) 1.5 km up, unlit, drifting
  at five metres a second; where the two branches share the road at the fork and at the merge only
  the outer edges keep their line and shoulder and the stunt ribbon sits a centimetre lower (`SiblingSide`:
  the lines crossed and the ribbons flickered); a ring of mountains round the horizon (ridged noise, forest,
  rock and snow by height and slope) and the hills beyond the roads tinted as woods; clear-sky HDRI with the
  sun aimed where the picture has it (the partly cloudy sky before it washed the plain to grey), cascaded
  shadows, SSAO, SSIL, screen-space reflections, a trace of volumetric haze, light fog with little aerial
  perspective so the range stays crisp, AgX tone mapping and TAA; the ambient light is 80 % sky and 20 % a warm
  tone for the light the sunlit ground throws back (it stands in for SDFGI, see the presets).
- Foliage (`FoliageBuilder`): grass tufts as crossed alpha-scissor cards in multimeshes, dense along the
  roads and sparse over the ground, olive-tinted to the terrain and faded out beyond 150 m; trees as a
  bark trunk with branches under a canopy of leaf-cluster cards; the wood beyond the fields (`BuildForest`:
  the dark tree line of the original's horizon) as multimeshes of a cheaper tree in three variants, placed on
  a jittered 25 m grid from 140 m out to the edge of the terrain, thinned into clearings by a slow noise,
  kept off the roads, pads, fields and farms by a 20 m cell mask, the band within 450 m of a road casting
  shadows and the rest not (about 500 trees near, some 20 000 beyond); fields as terrain-hugging patches
  (ploughed dirt, crop rows, hay with bales). Both atlases come from `tools/make-foliage.py`.
- Props (`PropBuilder`): textured primitives for the barn (siding, tiled roof, the green "33" door at the
  back), houses (orange or white siding, windows, chimney), the shop with canopy, pumps and a GAS sign,
  the camouflaged rubble hut, cows with patches, yellow diamond and white number signs, arrow signs, the
  crossbuck, the start gantry, striped sawhorses, the pad column and flower beds. A sign that is hit leans
  over about its base, away from the car. `TrainView` rides the simulation's train on the terrain.
- Keyboard driver with ramped pedals and speed-dependent steering rates; gears 1-7, left/right pick the car
  on the title screen. A gamepad (`InputSetup`, the console-era racing layout: left stick, triggers,
  shoulders, A/B/X/Y, d-pad fallbacks) is read as positions: the stick past a 0.12 dead zone through a 1.6
  power curve, the triggers straight, whichever the player is moving winning over the keys.
- Cars: a loft through cross-sections built from a `BodyStyle`'s profile curves keyed along the body,
  monotone-cubic between keys; wheel arches cut into the loft; star-rimmed wheels that steer and spin.
  Quads are wound by the ring direction (tangent turned clockwise = outward), normals are smoothed below a
  60 degree crease angle. The three character lines of each section (shoulder, bonnet edge / greenhouse
  base, crest) are rounded with 35-50 mm fillets in four sub-strips, so highlights roll over them like
  pressed steel; glass, trim and seams are still decided on the twenty-point design outline. Two styles. The flat-twelve is keyed on a dimensioned four-view drawing of
  the 1984 car (4485 x 1976 x 1130 mm, wheelbase 2550, overhangs 1085 / 845, tracks 1520 / 1677), its side and
  plan views calibrated on their own dimension lines: a long low wedge, 1.57 m wide just behind the nose, 1.82 m
  at the doors and 1.98 m ahead of the rear wheels; pop-up lamp lids at the outer corners of the bonnet, lying on
  its slope; an egg-crate grille with driving lamps in a painted bumper, black only at the lip; a long raked screen
  from a cowl a third of the way back (the windscreen is a glass range of stations, `ScreenFrom`/`ScreenTo`), a
  short narrow roof whose buttresses run down to a tail 0.77 m high with a short rear window between them; the
  intake low on the door, a recess cut into the loft (`Intake`: the flank sloping in to its deepest under the
  rising upper edge) with five strakes flush across it, and a leather panel inside so the road does not show
  through the lower door; tight arches over 16 in wheels, the rear track and tyres wider than the front; the
  single mirror half-way up the A-pillar in body colour; louvres between the buttresses; the slatted full-width
  tail over a painted panel with the plate in a recess. The loft before it had a near constant width, the cabin
  forward of the middle, the strakes up at the shoulder, the belt line 0.1 m high and the tail 0.13 m high. The
  glass is darker than it was: lighter, the long screen made the car read as an open targa. The V8 has a low
  wide nose, a long screen, a glass engine cover, side scoops, round lamps, four tips and a lip. Panel shut lines are dark strips of the loft itself (a
  pair of stations 14 mm apart at each seam); a plate, indicators, door handles, brake discs with red
  calipers and a cellular tread grain on the tyres finish the outside. The cabin is lined and furnished,
  in CC0 leather, carpet and fabric: door trim and roof lining are the loft's opaque upper strips again,
  25 mm further in and facing inwards, closed by a bulkhead behind the seats; a dashboard swept across the
  car with a rolled lip; a binnacle under a curved visor whose face is the live instrument panel; a
  three-spoke wheel on a column leaning back 22 degrees that turns twelve times the road wheels' angle;
  seats, tapered A-pillars, wipers, outside mirrors with glass, and on the player's car a rear-view mirror
  (below). The wheel and the binnacle are placed from the driver's eyes, so the dials sit inside the upper
  half of the wheel and the visor stays 8 degrees under the line of sight to the road. In front of the driver
  the dashboard's top drops into a pocket, and the binnacle sits as low as keeps the whole of the dials over
  the line of sight across its lip: on the flat top of 0.8.0 the lip hid their lower halves. From the driver's seat the
  shell, the wheels and the outside details are not drawn but still cast shadows (`ShadowsOnly`), so the
  roof shades the cabin; the glass is not drawn and never casts a shadow. Drones reuse the loft in other
  paints; the ghost is the same loft in one translucent glowing material without shadows.
- Cockpit camera at the driver's eyes over the seat (`CarView.Eye`: on the flat-twelve 0.98 m above the
  road and 0.87 m behind the windscreen base), 62 degrees of vertical field of view opening to 68 at
  speed, a few millimetres of jitter that grow with speed and on grass, and the head lagging the car's
  accelerations by a few centimetres (it sways out of a corner and nods under braking, the cabin moving
  around the eyes the way the 1989 cabinet slid its painted dashboard); chase camera; crane shot for the
  replay; `--orbit` fixes a camera on the car for pictures of it.
- Rear-view mirror (`RearMirror`): a camera of its own renders a 512x144 picture, flipped left to right,
  starting 2.7 m out past the car's tail and with a lighter environment (no SDFGI, SSR, SSIL, SSAO, glow
  or volumetric fog); it only runs in the cockpit view, and renders every other frame without multisampling
  (it costs 2.5-3.5 ms of GPU when it does).
- Instruments (`InstrumentCluster`), drawn into a viewport of their own: speedometer with odometer,
  tachometer with its red zone, gear readout, oil and charge lamps that light while the engine is off.
  The same picture is the binnacle's face in the cockpit and the panel at the foot of the screen in the
  other views, where it scrolls with lateral acceleration like the original's moving dashboard. HUD:
  time, score, lap, route, best lap and ghost indicator, updater notice, messages, the frame rate on F6. Title
  screen on a dark panel: car, gearbox (the last choice is remembered, automatic at first; the shoulders or
  E/Q step through them), and one press of Enter, Space, A or a click both chooses and turns the key. The
  end of the credit (`ScoreBoard`): the score, the entry of three initials when it made the top ten (up and
  down turn the letter and repeat while held, left and right move along, Enter or A takes the letter and on
  the last one the line), then the table with the new line picked out; `user://scores.txt`.
- Audio: the engine rebuilt firing pulse by firing pulse from recordings (`audio/GranularEngine`, played by
  `SampledEngine` and, through `physics.native`, by the Unreal version): per car an idle recording and a run
  up through a gear are cut into their firing pulses (`tools/make-engine-bank.py`), and every firing period
  of the simulated revs one pulse recorded at the nearest revs is laid down and overlap-added — the
  recording's own sequence while the revs hold, nothing looped or pitch-shifted, so the exhaust keeps its
  character across the rev range; the two banks cross-fade above idle, a low-pass opens with the throttle,
  and from the cockpit the engine comes through the bulkhead (duller, with a cabin boom); the load is the
  engine's throttle (0.19). Tyres and road
  from recordings (`TyreAudio`, levels from `audio/TyreMix`: the tread's roar recorded in a cabin, growing as
  v^1.5 and pitched up with the speed; a squeal as a tyre nears the peak of its force curve, the howl of a tyre
  sliding sideways past it and the scrub of a locked or spinning wheel, gravel while a wheel is off the asphalt;
  see the sound audit below); two to four exhaust pops when the V8 lifts off above 4800 rpm,
  one-shots from a CC0 recording drawn at random with a spread of pitch and level; synthesised beside the tyres
  (`audio/BodyNoise`) the wind (v^2, crossing the roar at 110 km/h in the cabin), a wet road's spray, the gears'
  and the final drive's whine and the anti-lock pump; a thump when the suspension is jolted; the shifter's clunk at every change of gear (a
  110 Hz thud with the click of the linkage); a crash as one of three CC0 recordings of metal and glass with
  a thump under it; the pulse-train engine, a filtered-noise squeal, a noise-burst crash and the clack of
  the train's wheels kept for 1989 mode; the sounds of the world placed in it (`WorldAudio`): every drone's
  engine on the drone (the flat-twelve voice at cruise, each car a touch off the others), the train's steam
  loop and whistle on the locomotive (two blasts for the crossing, nine and five seconds out), attenuated
  by inverse distance and Doppler-shifted from the simulation's velocities against the camera (the engine's
  own tracking measures against the wall clock and gave nothing in a fixed-rate run; a parked car hears a
  drone at 23 m/s drop from 508 to 444 Hz as it passes, the 0.874 the formula gives), through a 1.5 kHz
  low-pass and 7 dB of glass from the driver's seat; the engine silent through the instant replay (the
  simulation holds its last revs through it, which played as a frozen note); checkpoint bleep, a cow's moo
  (formants on a sagging pitch), the clang of a sign going over, and the cabinet's own moments: a chime at every choice on the title screen, the starter at the turn of the key, a brass fanfare
  when a lap beats the record, a driving tune under the instant replay (the score of the replay music below, played
  by this synth in 1989 mode: a quarter-duty pulse lead, FM bass, saw brass, a clav, kick, snare, toms, hats and
  cymbals; 9 dB over the other jingles because the engine is silent then, faded out in a third of a second when the
  replay is over or skipped) and an FM tune when the credit is over. The notes are ours in the timbres of the FM
  and pulse chips: the original's replay music is Atari's and is not copied. The arcade had no music while driving, so neither
  does this. The overrun recordings take the
  run-up's place as the throttle closes. A limiter on the master bus. Loops imported compressed (QOA) must end
  at `GetLength() × MixRate` frames, not at the data's byte count: that once cut every loop to its first
  fifth, with a click at each turn.
- The menu (`PauseMenu`, Esc or the pad's Back): carry on, new game, the car (its showroom, 0.25), the car's paint,
  camera, graphics, weather, 1989 mode, units, full screen, replay music and quit, with the keys, the d-pad or the
  mouse. While it is open the session does not step, no input reaches the car and the master bus is muted. Until
  0.8.1 the game could only be left by closing its window, which a full-screen window does not offer. Twelve lines
  since the CAR line, at a 44 px pitch so the panel keeps about the height eleven lines at 48 gave it (660 px against
  690). Not for want of room: the canvas is 1600 x 900 stretched with `expand`, never under 900 lines high whatever
  the window, so a taller panel would have fitted too (until the showroom's review this said the 48 px pitch would
  not clear a 720-line window, which the canvas never is).
- The showroom (0.25, `Showroom`): the player asked for a menu where the cars turn round so a car is chosen by
  seeing it; until then a car was a name on the title screen's line. It opens from the menu's CAR line (Enter, or
  left / right, which step straight on to the next car) and from the title screen (down, the key or the d-pad, not
  the brake trigger a resting foot holds, or a click on the car's name). The car is drawn with its real model on a
  turntable that turns a full circle in about 24 s (0.26 rad/s, slow enough to look at a panel as it passes), first
  seen from the front three-quarter on the driver's side; left / right (A / D, the chevrons) step through
  `VehicleSpec.Catalogue` with a 0.32 s slide (the car leaves sideways, the next comes in from the other side, its
  name fading out and in, the title screen's chime), up / down (W / S) through the paints, C or the pad's Y opens
  the colour picker; a drag turns the table (letting go keeps a little of the spin, dying away in about a second:
  the spin is the frame's whole drag over the frame's time, eased over about 50 ms, so a mouse reporting 1000 times
  a second and one reporting 125 let go the same, and a drag held still lets go still; until the showroom's review
  it was the last motion event's alone), the wheel brings the camera closer or further (0.55 to 1.8 of its
  framing), and the chevrons, the swatches and the buttons take clicks. Enter or A take the car and the paints
  chosen: another car starts a new game, as the title screen's left / right always did, the same car is repainted
  where it stands; Esc, B or Back go back with nothing changed. Space does what it does on the screen the showroom
  was opened over: over the title screen it takes the car, as it starts the game there, and over the menu it goes
  back, as the menu's footer says (until the showroom's review it took the car there too, and a Space pressed as the
  menu had taught ended the race in progress with a new game). Either way the player is where the showroom was
  opened, the title screen or the menu. The car's name is in the bar over the stage, fading out and in with the
  slide.
  - The stage is a world of its own (a `SubViewport` with `OwnWorld3D`), so the game's sun, weather and fog do not
    reach it, and the main viewport draws no 3D while it is open (`Disable3D`): a paused frame of the track costs
    what a driven one does, and would be drawn under the stage unseen. The light is a photographer's: a grey dome
    with the studio's lamps in it (`StudioSky`, a sky shader: three long strips overhead across the car and three tall
    softboxes round it at the height of its flanks, behind the camera and to either side, all out of the lens's view),
    a soft key from the front quarter with the shadows that set the car on the floor, a cooler fill from the other
    side, a rim from behind and above, a dark glossy floor that fades into the dome by a thin fog so the stage has no
    edge, screen-space reflections on the floor, AgX as on the road so a paint reads the same colour in both. The lamps
    are in the sky because a car's clearcoat mirrors the sky alone: as meshes in the room, caught by a reflection probe,
    they reached the paint only through the base coat's rough reflection, and every flank read as satin. None of the
    three lights has a highlight of its own; the highlights are the sky's lamps'. At full strength the key and the
    rim, small sources, drew hard white spots with a halo of glow on the bonnet and the roof (seen from the front, the
    bonnet mirrors the space above and behind the car, where the rim stands), where a studio's lamps are softboxes
    whose reflections are broad soft patches; the key held down to 0.35 still burnt the side glass and the panel under
    it to white with a halo twice a turn, wherever their normal halved the angle between the key and the lens (at 100
    degrees 15,800 clipped pixels on the 205 and 14,900 on the E-Class, none without it). The showroom's review put it
    on the softbox behind the camera, but the sky drawn without its lamps clipped the same.
  - The camera: a 32 degree lens (vertical) from 11 degrees above, at 2.9 times the radius the car sweeps on the
    table (half its length and width together, or 1.25 times its height, the larger), further where its nearest
    corner, turned towards the lens, would come within 16 px of the panel (`FloorFitDistance`, solved from the lens:
    the short, tall cars, the 205 and the Delta, whose aerial also raises its middle), and aimed 0.098 of its distance
    under the car's middle, so the car stands between the bar and the panel through the whole turn. Framed by half the
    length and aimed at 0.072 until the showroom's review, the 205's front wheel went under the panel at the first view
    (145 degrees), the short cars' rear wheel as a rear corner came round, and the tall cars' roofs into the name,
    which was drawn on the stage then. With the picker open the camera slides the car into the stage left of the
    picker's panel and steps back until its sweep fits that frame's half-width with a tenth to spare; it only slid
    before, and a long car's nose went under the left chevron (at 4:3 its tail under the picker too).
  - The ORIGINAL swatch is the car's own colour, the model's paint that covers the most of it, now by area: counted by
    its surfaces' indices (since 0.21, for the crash's debris), the McLaren's finely cut maroon parts outnumbered its
    orange panels and the orange car's swatch was maroon; the debris takes the orange too. The area is worked out once
    a run for a model with more than one paint.
  - The facts under the car come from its spec alone, so a car added to the catalogue appears with them and its
    own paint key and nothing else to write: the year from the '84 that ends the name; where the engine sits, read
    from the weight on each axle (`RearAxle`, `CentreOfMassOffset`: more than half on the rear is an engine behind
    the seats, the flat-twelve's 40 / 60 and the V8's 42 / 58); the cylinders and the induction (a governor is a
    diesel's, a boost share a turbo's); the driven wheels; the gears and the gearbox (a change under 0.125 s is a
    dual-clutch box handing over); the front / rear split; peak power (metric horsepower, torque times revs over
    7023, as the title screen prints it) and peak torque, each with its revs, from the full-throttle torque curve;
    the mass, the power to weight and the tyres. The panel's foot says they are the simulation's, the mass with a
    driver and fuel: the showroom's review read them against the drawn cars' published figures, which they are not
    (the V8's curve peaks at 585 hp at 8,600 rpm, where its spec's own comment says 570 at 8,000 and the 570S drawn
    has 570 PS at 7,400-7,500 rpm and 600 N m, from 2015 where the name says '12; the flat-twelve has the arcade car's
    four gears where the Testarossa had five, and 395 hp where it had 390 PS; the 205's 985 kg carries a driver and a
    tank against its 875 kg DIN). The figures are the car the player drives, so they stay the spec's.
  - The neighbours' models load on a thread while the player looks at a car (`ResourceLoader.LoadThreadedRequest`),
    so a step does not wait on the disk: a prepared model is 20 to 70 MB, and read on the main thread it held the
    picture for most of a second. Only the two neighbours are kept: every car's textures at once would be hundreds of
    megabytes of video memory. The graphics preset reaches the stage (Low: three quarters of the pixels, FXAA, no
    SSAO or reflections; 1989 mode a third of the pixels and no glow).
  - The showroom lets go of its cars when it closes. The scenes it kept are disposed (a model it no longer wants is
    disposed as it steps on), a neighbour still loading is collected when its load ends and let go
    (`Showroom.CollectAbandoned`), and the frame after the close collects .NET's wrappers until a round frees nothing
    (three or four collections, 17 to 22 ms). Until the showroom's review none of this happened: a model still loading
    stayed in the loader for the session (a step and a close 0.15 s later kept the Delta's 27 textures, 140 MB of video
    memory), and the cars' meshes, materials and textures waited on the collector, since a wrapper holds its Godot
    object while Godot refers to it too and, on a heap of about 20 MB, the collector ran a minute and a half later (310
    resources and 200 MB of video memory over the baseline until then); one collection still left 69 MB, a material
    let go by one leaving its textures' wrappers to the next. The model materials already corrected
    (`CorrectModelMaterial`) are marked on themselves, no longer in a static set that held every model the game had
    drawn for the session (68 MB after a visit to every car). Measured with a scripted run in a scratch copy with its
    own user folder, over the V8's credit: a visit to all eight cars, and a step with a close 0.15 s or 3 s after it,
    each back within a second of the close at the driving baseline, 1,721 MB of video memory and 1,383 resources, where
    two forced collections later leave them.
  - Frame time, measured with `--perf-log` and the showroom opened by `--showroom=v8` (the title's and the menu's ways
    in open the same stage; the review measured those through a scripted run, 3.2 to 10.6 ms of GPU against 6.6 to 13.4 driving, on
    a shared machine), on the development laptop (RTX 5060 Laptop), hidden runs at the game's
    frame cap (32 frames a second on the hidden window's 32 Hz screen), five-second averages once the first car is
    in (the first ones carry its loading): the showroom on the V8 held the cap with 4.3 to 5.9 ms of GPU and 8 to 11
    ms of CPU a frame, 4.6 to 5.1 and 9 to 14 with the colour picker open (its square rebuilt only when the hue moves),
    where driving the same car took 6.5 to 8.6 and 13 to 16; other runs shared the machine (cars/menu).
- Each car's paint (0.25, `PaintBook`, `PaintChoice`, `ColourPicker`): the player asked to choose the colour in the
  showroom, every car starting from its own, the model's factory colour (ORIGINAL, as since 0.22), and to mix one of
  their own. A car's paint is its own colour, a preset (`CarView.Paints`) or a mixed colour in a finish, kept per car
  in the settings as text, `paint.<car>` in the game section, the car named by its style (`paint.flattwelve`,
  `paint.hothatch`..., so a car added to the catalogue has its own key with nothing to write): `ORIGINAL`, a
  preset's name (`DARK BLUE`), or the colour as hex and its finish (`#1E4FA0 METALLIC`), readable in the file and
  untouched if the presets are reordered. Until 0.24 one paint (`paint`, an index into the presets) served whatever
  car was driven: the first start of 0.25 gives every car that paint, so a choice is not lost, and drops the old
  key, so the migration happens once. The menu's PAINT line steps through the presets and the colour the car was
  last mixed in, for the car of the credit; `--paint=` takes a preset or `#RRGGBB[:solid|metallic|pearl]` for the
  run's car, this run only. An automated run (`--invisible`, `--screenshot`, `--quit-after`) reads the paints but
  never writes the player's file, which the test runs share; `--paint-book-test` writes, reads back and migrates
  them in a file of its own (`user://paint-book-test.cfg`) and quits with 0 when every check holds. The drones keep
  the traffic's palette and the ghost its glow. Paints chosen in the showroom are edits until a car is taken, then
  every car's is kept; Esc forgets them.
  - The picker (C or Y, or a click on the last swatch, which stands on a strip of every hue): a square of
    saturation (across) and lightness (down) beside a bar of hue, HSL rather than HSV so the pastels and the dark
    shades of one hue lie on one line from white through the full colour to black, and under them the hue,
    saturation, lightness and finish as rows: up / down choose a row, left / right change it (a tap a degree or a
    per cent, held faster and faster from a quarter of a second, 30 to 150 steps a second), the mouse clicks or
    drags the square and the bar. The turning car is repainted as the colour moves, the texture-painted cars too (the
    205's and the VAZ's atlases, the Delta's white: the mask path of 0.21), and the camera slides the car left and
    steps back, clear of the picker's panel and the left chevron. Enter, Space, A, C or Y keep the colour; Esc, B or
    Back put back the paint the car had, and its mixed swatch. TAKE IT and BACK wait for the picker, as Enter and Esc
    do, and are drawn dimmed while it is open: until the showroom's review a click on them was kept and fired when the
    picker closed, so DONE left the showroom without the colour and Esc took the car. A car's last mixed colour stays
    its swatch while the game runs, and after a restart when the car is still in it. A colour starts from the car's
    last mixed one, or from its own colour as a solid; a grey, which has no hue of its own, opens at the hue the bar
    had when the picker last closed on that car (at 0, red, before the review). Every swatch has a faint edge, so the
    black one shows on the dark panel.
  - The finishes: solid as the classic paints (metallic 0.05, roughness 0.38 under the clearcoat), metallic as the
    silver (0.6, 0.3), and pearl (0.3, 0.32 and a flop). A pearl's mica is coated thinly enough to colour the light it
    returns by interference, so its colour shifts with the angle it is seen at; the models' paint shaders blend
    towards a tint as a panel turns edge-on (the two-tone blend on the view angle that real-time car paints use for
    it, 0.55 times the cube of one minus the cosine, so a panel seen at 60 degrees keeps nearly all its colour), the
    tint the colour's hue moved on by a twelfth of the circle and 12 % darker, or for a white, silver or black, which
    have no hue to move, the warm gold of the mica. The lofted bodies' standard material cannot, and draw a pearl as
    its metallic base. A mixed colour is held inside what a paint reflects, 0.03 to 0.92 a channel (sRGB, about the
    black's 0.035 and the white's 0.90), so the picker's pure white does not glow in the sun and its pure black is not
    a hole in the picture.
  - A lofted body's ORIGINAL (cars13 stage 2, `CarView.LoftPaint`): the default red, but for the five cars of cars13,
    which wear their real cars' launch or press colours: the 458 Rosso Corsa (#E4002B, Pantone 185 C, its model's
    paint set to it as well), the Bravo HGT Giallo Ginestra (Fiat 258, a Bravo-only colour of the 1998 facelift, solid,
    #F0C000), the Bravo 1.6 Verde Reflex (Fiat 391, Bravo-only, metallic, #2E6B4F), the Leon Cupra Desire Red (the
    metallic SEAT launched the Cupra 300 with, #A61427), the BRZ WR Blue Pearl (#1A4FD6, PaintScratch's reading of
    02C, a pearl). The sources are in the code; no press-car colour of either Bravo was found, so both are colours only
    the Bravo was sold in, their shades estimated. The showroom's swatches read these values. The lofted bodies drew
    them paler on the turntable than the swatches until cars/bodies, solids as much as metallics: not the paint but
    the loft's material, which carries its colours on the vertices as the sRGB values the paints are written in, and
    Godot took them as linear (`VertexColorIsSrgb` was off): Desire Red's red channel 0.65 drew as 0.83, while the
    loft's own painted details (mirrors, spoiler, `AlbedoColor`, converted) came out darker than the body they sit on.
    The loft's paint now declares its vertex colours sRGB; its black trim (0.06) is black now too, not a 0.27 grey. On
    the turntable at 100 degrees the body's median colour went from (128, 33, 48) to (95, 0, 3) for the Cupra's
    #A61427, from (190, 168, 45) to (183, 146, 19) for the HGT's #F0C000, from (62, 97, 80) to (15, 49, 31) for the
    1.6's #2E6B4F: as saturated as the swatch, darker where the side is in shade.
- A fault in the sound update is logged once and the frame carries on (`Bootstrap._Process`). In 0.8.0 the
  world sound kept the players of the last credit's drones, freed with their views, and threw on every frame of
  the next credit: the view stopped following the car, which stood at the line while the engine revved and the
  clock ran. `WorldAudio.DetachDrones` now forgets them with the views.
- Graphics presets (F7, remembered): High is everything; Medium drops SSIL and screen-space reflections; Low
  also drops SSAO, glow, the volumetric haze, the sun's shadows and the far wood, renders at three quarters of
  the window with FXAA instead of MSAA and TAA (`WorldBuilder.SetQuality`, `Bootstrap.ApplyGraphics`);
  `--quality=` sets one for a run. SDFGI was part of High until 0.8: measured on the development laptop it was
  a third of the main view's GPU time (12.9 ms a frame with it, 9.2 without), doubled the worst frames (38 ms
  against 18) and held 550 MB of video memory, because its cascades re-voxelise the world as a camera moving
  at 35 m/s drags them along; every other effect measured under half a millisecond.
- Frame rate (`Bootstrap.ApplyFrameCap`): the game holds itself to an even fraction of the screen's refresh
  rate, at most 90 (72 on a 144 Hz panel), 30 while it is in the background and 20 in 1989 mode, and logs
  each change. Uncapped, the development laptop's 144 Hz panel asked its GPU (an RTX 5060 drawing for an
  Intel-driven panel) for more frames than High could draw; every session ended within a minute in a driver
  timeout, a black screen and a lost Vulkan device (28 October 2026). Vsync alone does not hold a hybrid
  laptop's discrete GPU back; the engine's own limiter does. On a window taller than 1200 lines the 3D view is
  rendered at 1080 and scaled up with FSR, so a large monitor cannot undo that. A hidden window is throttled
  by the system to about 17 fps whatever the game asks, so automated runs measure the GPU time a frame takes
  rather than the frame rate.
- "1989 mode" (F8): a third of the render resolution, 20 fps, textures and normal maps off, nearest
  filtering, no shadows, global illumination, ambient occlusion, reflections, glow, fog or TAA, a flat sky,
  orange shoulders everywhere, no clouds or wear sheet, the synthesised engine and squeal instead of the
  recordings, the placed sounds muted.
- The hatchbacks (`CarView.Hatchbacks.cs`, 0.9): three `BodyStyle`s keyed on dimensioned blueprints
  (getoutlines.com, CC BY 4.0: the 2000 Clio, the 1995 Bravo three-door and the 2007 Leon), their side views
  measured every 5 cm for the roofline and at every 5 cm of height for the nose and the tail, the end views for
  the tumblehome. Stations run every 10 cm from the cowl to the tail (`RunStep`) so the curved roof and the
  tailgate follow the profile, the shoulder has its own width (`ShoulderW`, the flank tucks in above the
  waist) and the rear window spans the whole width between the crests. Their details are not boxes: the
  headlamps, grille and intake are outlines seen from the front, filled with shrinking rings and carried back
  along z onto the skin (`EndGrid`, `SkinZ` on sections cached every centimetre), the tail lamps the same from
  behind, the rubbing strip and the pillars over the side glass carried in from the side; each lies a few
  millimetres off the paint along its normal, so it follows the curve of the nose or the corner of the
  tailgate. A hatchback's cabin has upright seats on bases, a rear bench and a parcel shelf, and a modern
  airbag hub; every car gained a centre stack (vents, radio, heater knobs), a console with the gear lever and
  the handbrake and sun visors. The end caps of the loft are cut along the trim line (the black bumper's edge
  ran as a V down from the centre of the fan before). The title screen prints the car's power (from the torque
  curve), mass, driven wheels and gears, and left/right step through the cars both ways.
- Crash damage (`CrashScene`, `CarView.Damage.cs`, 0.9): the simulation stops the car when it comes within
  reach of what it hit and starts the replay; the view plays the recorded approach at its own pace with the
  crane coming down to a few metres off the car and swinging round to a front three-quarter view of the blow,
  runs the car on until it touches the obstacle (the nearest point of the prop's box, the drone or the wagon),
  then plays out the crash the simulation never did: a dent pressed into the shell, the glass and the lining
  over a few frames (vertices moved along the push with a creased falloff, each moved face taking its own
  normal so the metal reads crumpled), a wider shallow crumple round it and a buckled bonnet on a frontal
  blow; the outside parts within reach (lamps, grille, plate, mirrors, strip) unparented into a debris node
  and thrown with bits of paint, plastic and glass that tumble, bounce and come to rest on the road; sparks
  fanned back off the obstacle; a steam plume from the radiator; the glass near the blow (and the screen on a
  hard frontal one) laid again with a cracked texture (cellular noise's cell edges on a colour ramp); and the
  car rebounding, spinning from the impulse acting off the centre of gravity, rocking in pitch and sliding
  to rest, the first second in slow motion. When the replay is over (`Repair`) the original meshes are put
  back, the parts re-parented and the debris freed, and the session sets the car down. 1989 mode keeps the
  cabinet's replay: the recording from the crane, no damage.
- Materials: the road and the shoulders are drawn from both faces, because on the bridges, the ramps and the
  loop the ribbon is the whole deck and is seen from below; the centre line and the far ground only from above.
  The hot hatch's and the five's mirror housings are black plastic, the turbo hatch's in paint: from the seat
  the housing's back shows through the side glass, and in red paint it read as a ball beside the screen.
- 0.17: the flat-twelve is the Testarossa again, the car the game is about (the Ferrari models were taken down from
  Sketchfab, which is why 0.16 used the Esprit; the CC BY copy in the Objaverse mirror stands): li_osh's shell,
  which has no cabin, with the Esprit's cabin borrowed (`cabin_from`: a donor entry, its parts rigged for this car
  and moved by an offset). The hatchbacks are three cars: the hot hatch a Peugeot 205 GTi, the five a Lancia
  Delta Integrale (its cabin is one black mesh, so its wheel does not turn; `eye_at` gives the eyes), the turbo
  hatch a Golf R32 Mk4 with the SAAB 9-5's cabin moved 15 cm forward. Models lying along x turn about y first
  (`forward: -x`); untextured pure black is lifted to 0.035, as nothing real is black as 0.
- 0.18: the 1.4 estate is a Renault 12 (boitaloran's saloon: the Toros shares everything ahead of the B-pillar; 1.7
  million triangles, its wheels in five shared meshes split by corner, its steering wheel part of the dashboard). The
  paint's lining on a panel's back face is now only inside the cabin (a box from the eyes, in the car's frame, which
  `CarView._Process` hands the shader every frame): the Renault's boot lid and a door are wound the wrong way and
  showed black from outside. Split pieces are measured by the vertices their own triangles use. Brake lamps: the
  rear lenses named in a model's `brake` entry glow red while the driver brakes (`CarView.Braking`, a copy of the
  lens per car); `keep` leaves a material alone though its name says glass (the Testarossa's red tail lenses had
  turned into dark window glass). The Unreal level's model script now rigs with the same `tools/car_models.py`,
  so its flat-twelve is the Testarossa too. Switch: `--hold-brake`.
- 0.20 look (the cars and the cabin, judged on A/B screenshots of the orbit, chase and seat views):
  the models' own materials are corrected where the exporters left the glTF defaults (`CarView.CorrectModelMaterial`):
  metallic 1 and roughness 1 with no texture to drive them (the McLaren's cabin plastic and carbon, the Renault's
  dials, the Delta's black) becomes a matt dielectric, a wing mirror's glass left that way (a white blot from the
  Testarossa's seat) becomes silvered glass, tyres become matt rubber (they were glossy, 0.15 on the Testarossa and a
  third metallic on the Renault: wet plastic toys' tyres), a grey dielectric wheel a painted alloy, and a rear lamp unit
  exported glowing white (the McLaren's) a red tail lamp. The Esprit's door cards in the Testarossa stood out over its
  doors as a cream bar; `cabin_from.inside_only` names donor parts that poke through a shell, prepared as `Inside_...`
  nodes the game draws from the seat only. From the seat a reflection probe the size of the cabin
  (`CarView.BuildCabinShade`, captures nothing, so it costs an empty capture as it moves with the car, about 0.3 ms)
  gives the cabin a dim constant ambient light instead of the open sky's: the dashboard is in shadow where the roof
  shades it and lit where the sun comes through the glass, and the dark dashboards lost the white streaks of a sky they
  cannot see. Under cloud the windows and the lamp lenses take no highlight from the sun, which the overcast weather
  widens to ten degrees (the V8's rear window glared white in the rain), and the rear-view mirror's glass is silvered
  glass from outside rather than its frozen picture. GPU time unchanged within the noise (chase about 6.3 ms, seat
  7.2, orbit 6.7 on the development laptop, before and after). Tried and left out: a disc of blurred spokes over each
  wheel at speed (on the Testarossa's star wheel it read as a photograph's blur, on the Mercedes as a smoked cover and on
  the McLaren's concave rims it floated off the spokes), the paint's base layer smoother or duller under the
  clearcoat (no visible difference: the base's 4 % reflection is lost under the sunlit red), and the cabin shade on
  the cabin's own meshes from outside as well (the seats through the glass barely changed).
- 0.21 paint (the cars that looked alike, judged on orbit, chase and seat screenshots of every car in its own
  colour and in red, white, dark blue and yellow): two models carry their paint in a texture atlas with everything
  else, so they had no paint material and the player's colour did nothing on them, and every VAZ in the traffic was
  the same beige: the 205 (`uv_1`: a navy with the black trim, the lamps, the GTi badges and the red pinstripe) and
  the VAZ-2106 (`Steel`: a beige with its baked shading, grime, the chrome strips and the grille). Their entries name
  those materials (`paint_atlas`, with the key colour measured from the texture, which the preparation refines to
  the median of the matching texels the paint's triangles cover), and every paint drawn from a texture (the Delta's
  too: a white with its Martini lettering, vents and grime, which the flat paint had thrown away) gets a paint mask
  (`prepare_car_models.paint_mask`): red the paint's weight, the texels whose chromaticity (r, g and b over their
  sum, in linear light, which shading leaves as it is) is the key's and which are neither too dark to have one nor a
  white badge; green the texel's lightness against the key, halved, smoothed a little where the key is dark (a
  JPEG's noise in the navy). The grime on the paint counts as paint (`fill_specks`): the small patches that are not
  paint (at most 128 texels of a 1024 x 1024 texture), wholly inside it and no lighter than it (half again the key's
  luminance at most) nor near black. The 205's navy has a hundred and sixty dirt specks and smudges, greyer than the
  paint, and the VAZ's beige hundreds; kept as texels, they were invisible in the model's own colour but navy dots
  along the sills and a blotchy front valance in a white, a yellow or a silver; painted, their lightness darkens the
  new colour instead. Badges, chrome and lamps are lighter, the black trim and vents larger, and the Delta's Martini
  letters black. The mask rides in the material's emissive slot with a black emission (Godot's importer extracts
  and mipmaps it with the model's textures; nothing glows if the material is drawn as it is) and the key in its
  extras, which is how the game tells it from a glow of the model's own (a material that glows already is left
  unmasked, with a message). The game (`CarView.TexturePaint`) draws paint times lightness where the mask says
  paint, with the paint's finish under the flat paint's clearcoat, and the model's own texture, roughness, metallic
  and normal maps everywhere else, so the trim, the lamps, the chrome, the badges and the lettering are untouched in
  any colour; the lining of a panel's back face in the cabin is the flat paint's, and the faces the preparation lines
  inside the glasshouse are only those whose texture is paint. A paint is a colour and a finish (`CarView.CarPaint`):
  the solid colours the coated dielectric (metallic 0.05, roughness 0.38), the silver a metallic (0.6, 0.3), which
  as a solid was a flat light grey hardly told from the white. The menu has a PAINT line (`CarView.Paints`,
  remembered; `--paint=` for a run): the car's own colour by default (`ORIGINAL`: the model's flat paint colours, or
  the paint texture as it is with the model's own roughness and coat, so the 205's bonnet dust and the VAZ's matt
  beige stay, and its own metallic dropped for the paint's, since the Delta's is glTF's default of 1; a near-white
  texture lifted to the menu's white, as the Delta's sRGB 185 read as a dirty mid grey), or red, white, silver,
  black, dark blue, green, yellow; a model is repainted in place (`CarView.Repaint`), a lofted body built again. A
  model with two paints (the McLaren's orange and maroon) gives the crash's debris the one covering the most of it.
  The traffic is five everyday cars, the saloon, the estate, the fleet saloon, the 205 (the GTi its model is, red
  wheels, bucket seats and pinstripe with it) and the Golf (not the Testarossa, the McLaren or the Delta), each drone
  the next car and the next of twelve road colours of the 1970s to the 2000s (`CarView.TrafficPaints`, neighbours
  differing in hue or lightness; the silver and a gunmetal grey metallics), counting on from the last credit's
  drones: a credit has eight, no two alike and others than the last credit's, and a pair comes back after sixty
  drones (seven and a half credits). The drones move as before; only the look changed. Their cabins
  (`CarView.TrimCabinForTraffic`: the meshes inside, not the paint, glass or lenses) cast no shadow and are drawn
  within 25 m only: the 205 and the Golf brought 35 to 110 draw calls more than the three-car traffic (the shadow
  cascades draw a mesh again), and trimmed the eight drones draw about 140 fewer. Measured with the traffic on
  (`--perf-log` with the frame's draw calls added for the test, the development laptop, the same build with the old
  three cars, the five, and the five trimmed): 3417 / 3452 / 3268 draw calls on the attract lap, 3476 / 3589 / 3338
  on the speed route; the render thread's CPU 1.3-1.8 ms a frame in all; GPU 7.0-7.9 ms in all, within the runs'
  noise. The CPU figure `--perf-log` prints (render plus the frame's process time, 10-14 ms here) varies by 1-2 ms
  from run to run on this machine and is dominated by the process time, which the drawing does not touch (the
  review's 0.3-1.1 ms over main is within that, against a main that also differs in its audio, the sound review's
  fixes), and the first figures written here (8.91 / 8.63 ms) were two runs' noise, not a saving. 5.6 ms of GPU
  with the camera at a 205 drone; loading the two more models adds about 6 ms to the first five seconds' average
  CPU. Switch: `--look=drone:<n>[:distance[:bearing[:height]]]` (pictures of the traffic's cars; the height, over the
  road and 1.8 m by default, low for where the tyres meet it). Each drawn drone stands at its own car's static ride
  height (dyn5, `CarView.SetTrafficPose`): the traffic carries every drone 0.61 m over the road (`Traffic.RideHeight`,
  about the sports car's), and drawn there the five cars sat into it by the difference, the 205 0.9 cm, the saloon
  2.0, the estate 2.3, the Golf 3.4 and the Mercedes 5.1; they are lifted by it along their own up, the traffic's
  positions, gaps and knocks unchanged (shots at `--look=drone:<n>:3.5:90:0.25`, before and after). Since dims6 each
  is also moved forward by half its overhangs' difference, centred on the traffic's knock box (dynamics audit item 21's
  follow-ups).
- models13 (the models for the five cars of cars13, drawn once its catalogue entries are in: `CarView.ModelKey`
  names them 458, bravohgt, bravo16, leoncupra and brz, and a key without a prepared file keeps its loft). The
  flat-plane V8 is a Ferrari 458 Italia (Antonis_zks's, CC BY as recorded in the Objaverse mirror: Sketchfab has
  taken down every Ferrari model of that period, the Testarossa's with them), a shell without a cabin that borrows the
  McLaren 570S's (its interior tub, dashboard, seats and steering wheel, rigged at the 458's 2.65 m wheelbase with no
  offset or scale: the two mid-engined cabins sit alike). Three additions to the preparation, each idle for a model
  whose entry does not ask for it, so the eight cars before it prepare to the same bytes (SHA-256 against main's):
  `glass` names the materials that are windows though their names do not say so: the 458's whole glasshouse
  (windscreen, side windows, rear window and the engine cover's glass) is one opaque black Material.048, and from the
  seat the review saw black with a sliver of road; the mirror's glass finder counts it as well, so the hung mirror
  sits 6 cm under the windscreen. A part its accessors make bigger than its tyre is measured again by the vertices
  its triangles use (`car_models.rig`, given the binary chunk): the 458's rims share a vertex list whose bounds make
  them 0.51 file units across against the tyre's 0.46 (their own 0.40), so they had stayed in the body, standing
  still inside the rolling, steering tyres (the turning discs behind them hid it); the Unreal preparation rigs without
  the chunk, as before. `cabin_from.seats` names a donor's parts that are seats though their names do not say so,
  prepared as `Seat_...` for the eyes to pass over: the 570S's two buckets are one mesh 1.34 m across, whose tops the
  headroom check took for a roof, setting the 458's eyes 13 cm under them, 0.86 m over the road, where the binnacle
  hid the road at the frame's centre column; now 0.95 m (0.52 m behind and 0.28 m over the wheel's centre, 21 cm under
  the roof skin), the road in rows 462-509 of 900 at the centre column over a dash top at 57 % of the frame.
  The 458's paint is the material named "Material", given as "==Material" since the name is part of every
  Material.0NN; the lens over its round tail lamps lights when braking; its calipers stay with the body. Its tyres
  are alike in the model where the real car's are staggered (235/35 R20 in front, 0.672 m; 295/35 R20 behind,
  0.715 m), which the game's one radius a car would not show anyway. The boxer coupe kept its loft here: the only
  BRZ, 86 or FR-S of a credible origin then found, RoninVR's Scion FR-S 2015, is unfinished at the tail the chase
  camera looks into (no lower bumper, ragged rear quarters, arches open at the back), and was not fetched (cars/bodies
  found the BRZ a model, below, and dropped that record). No CC-licensed model of the first-generation Fiat Bravo or of the 2018 Leon Cupra exists outside a login
  (the Objaverse catalogue's 80 Fiat records, Sketchfab with and without the downloadable filter, archive.org's
  Thingiverse clips), so the Bravos and the Cupra keep theirs too. `CarModelSourceTests` pins the 458's entry and ties
  the four places a model is named: every file `fetch_models.py` fetches has its LICENSE.txt (with its Sketchfab uid) and is drawn by the game
  or the Unreal level, and THIRD_PARTY.md credits exactly the models the game draws (this branch first credited two
  models no entry drew and fetched their 52 MB on every fresh build). The 570S cabin's gloss-black dashboard
  mirrored the sky in the 458 as it did in the V8 until stage 2 gave the 458's entry the V8's `materials` (next
  item). Left: with the Testarossa the game draws two Ferrari bodies, whose names and designs are Ferrari's
  (THIRD_PARTY.md), a maker that has had its models taken down from Sketchfab.
- cars13 stage 2, the 458's cockpit and paint (cars/stage2, the McLaren's cockpit fix and models13 met). The 458
  borrows the 570S's whole cabin, so its entry takes what 0.25.0 gave the V8's: the dashboard, scuttle and door tops
  matt (`materials`: plastic.black and McLaren.black at roughness 0.88), the dials 0.31 m wide, 1 cm right and 4 mm
  down on the binnacle with the gear readout from 44 px down the panel, and the eyes 5 cm up and 3 cm back on the
  default (`eye`), 0.33 m over and 0.55 m behind the wheel's centre as in the 570S. The eyes were not asked for and
  were added for the dials: the dials projected from the default eyes (`dialmask`, the cabin and the wheel between
  the eyes and the panel, at rest and for the head's sway: 4.5 cm either side, 3 cm back, 3 cm forward and right, 6 cm
  forward at speed), 5 cm lower and 3 cm nearer the wheel than the McLaren's, put the rim higher across the panel:
  the rev counter read only 4,000-7,000 rpm at every head position, the speedometer 70-230 km/h, the gear readout 43-80
  % in view (the V8: 3,000-7,750, 40-260 km/h, 100 %). Raising the panel to the window between the binnacle's hood and
  the rim needed 12 mm or more, and from there it stood out of the binnacle over the hood in the seat view (shots at
  12, 20 and 24 mm); a smaller panel gains nothing. On the McLaren's eyes over the same wheel the projection is the
  V8's to the figure, and the seat view frames the road as the V8's does: eyes 1.001 m over the road, 0.155 m under
  the lining (the default found nothing over them, which a sparse roof mesh lets through), the hung mirror 8 cm over
  the eyes (no glass over its new place: the windscreen's top lies ahead of it). Its paint, the material named
  "Material", is a pure #F70000 in the model; the entry sets it to Rosso Corsa, #E4002B (`materials`' new `colour`,
  sRGB, kept linear as glTF has it), the 458's launch colour (`CarView.LoftPaint` cites the sources), so the ORIGINAL
  swatch, the paint covering the most of the car, reads #E4002B in the showroom. The other eight models prepare to
  the same bytes as main's (SHA-256 against a preparation of main's tree), `tools/car_cockpits.json` takes the 458,
  and `A_seat_pair_in_one_mesh_does_not_lower_the_eyes` holds its eyes, dials and dashboard to the 570S's against the
  same wheel.
- cars13 bodies (cars/bodies): the BRZ, the two Bravos and the Leon Cupra as much like their real cars as the game
  can draw them, from the chase view, the showroom and the seat.
  - The BRZ is drawn by "ToYoTa FR" by Ethan (Sketchfab, CC BY 4.0; `toyota-86-ethanchew.LICENSE.txt`): a
    first-generation Toyota 86 (ZN6) in the facelift's styling, the BRZ ZC6's twin on the same shell, the only credible
    model of the car found (1,105 Sketchfab models of the BRZ, 86, GT86, FR-S and GR86 checked: game rips, SketchUp
    conversions, non-commercial licences, race cars). Uploaded after the Objaverse snapshot, it is fetched from the
    project's own copy, a release asset (`models-2026-11`), checked against the SHA-256 of the file its LICENSE.txt
    describes (`fetch_models.py`'s `RELEASED`, held to the LICENSE.txt by `CarModelSourceTests`). At the BRZ's 2570 mm
    wheelbase it is 4.24 m long (Subaru: 4240), 1.795 m wide (1775) and 1.263 m high (1285), tracks 1.511 m
    (1520 / 1540). The entry (`tools/car_models.json` "brz"): forward +z; the four tyres with their rims rolling; the
    discs and calipers, one mesh each across the four corners, fixed to the body; the paint "==material" (a part of
    "black_car_material"); the windows' near-black "Glass" drawn as the game's glass and the headlamps' covers, which
    share it, as clear lenses (`lens`: a copy of the material named for the lens pass); the lamps' strength-10
    emission, a night render's, drawn unlit by day with the headlamps faintly lit (`materials`' new `emissive` and
    `emissive_strength`), the tail lamps lit when braking. Dropped: the two seats, which the author says are not his
    and so are outside the licence, the Toyota badges and the caps' logos (`The_BRZ_leaves_out_what_its_licence_does_not_cover`).
  - The BRZ's cabin: the model has a plain dashboard top and door cards and nothing else, so it borrows the W212's
    (`cabin_from` "fleet"), placed as the preparation places a donor, and cut where a saloon's cabin shows through a
    1.26 m coupe (`crop`, new: the triangles of the named parts whose centres lie in a car-frame box): the donor's
    dashboard and gauges higher than 0.36 m in the car's frame (8.5 cm under the eyes; the saloon's dash top stood over
    the coupe's scuttle), its panels from 12 cm ahead of the eyes back, the rear bench, the front doors' tops over
    0.2 m (through the coupe's lower window line), the wheel's Mercedes star and a gauge sliver the rim cut across. Eyes 1.069 m over the road for left-hand drive (asked: about 1.06), 0.13 m under
    the lining, 0.52 m behind and 0.28 m over the wheel's centre; the road at the centre column from about row 390 to
    the bonnet at 535 of 900, parked. The dials 0.24 m wide, 1 cm up (`dials`), projected from the eyes as the McLaren's
    were (`dialmask`, five head positions: at rest, 4.5 cm either side, 6 cm forward, 3 cm back): 20-240 km/h and
    750-8,250 rpm readable at every one, the gear readout in full. The mirror is hung by the game. Frame time against
    the V8 (`--perf-log`, chase view, 25 s, 72 fps cap): the BRZ 5.6 ms of GPU and 6.9 of CPU a frame, the V8 5.6 and
    7.6; from the seat 7.0 and 10.1 against 6.7 and 10.8. The other nine models prepare to the same bytes as before.
  - The Bravos and the Leon keep lofts: no credible model of either exists (cars13 hunt: the only Brava is a GTA San
    Andreas mod, the Marea a BeamNG one; no 5F Leon that is not a rip), and the user chose the game's own bodies. Each
    is now a style of its own (`CarView.Cars13.cs`), keyed on the published dimensions (auto-data.net: the Bravo
    4025 x 1755 x 1410 / 1420 mm, wheelbase 2540, tracks 1471 / 1430 for the HGT and 1461 / 1463 for the 1.6,
    205/50 R15 and 185/60 R14; the Leon Cupra five-door 4271 x 1816 x 1435, wheelbase 2631, overhangs 855 / 785, tracks
    1535 / 1510, 235/35 R19) and on drawings: the 1995 Bravo three-door's (getoutlines, CC BY 4.0, the one the
    five-cylinder hatch was keyed on; the 1999 drawing beside it in the logs is the five-door Brava) and, until the
    cars13 review found it is a paid drawing ("Download for $24", a Basic License, not CC BY), the 2015 Leon Cupra
    five-door's preview (getoutlines); the Leon is keyed since on the free side view of the 2013 five-door (below,
    "cars13 review fixes"). Only the measurements come from them; no drawing or photograph is shipped (THIRD_PARTY.md), and the reference photos
    (Wikimedia Commons, for comparison) stay in the logs.
  - The Bravo (three-door 182): the five-cylinder hatch's keys with the belt raised into the wedge the drawing has
    (0.915 m at the door's front, 1.025 m at the C-pillar; 0.895 to 0.985 before), the screen's foot 5.5 cm further
    back (the drawing's), slim headlamps from the narrow grille to the wings' corners, the big oval tail lamps in the
    tailgate's corners at the foot of the rear window (0.65-0.95 m) with the clear lens under each, the "smile" handle
    between them, a dark rubbing strip round both bumpers at the doors' strip's height, and the side glass's shape laid
    over the loft's glass strip (`BlackCovers`, `PaintedCovers`, new: side-view polygons in black or paint, lined
    inside): a black sail ahead of the door glass, a black B-pillar, and the C-pillar over the quarter window's back,
    whose top falls to the point the window ends in, 1.07 m up. The HGT: body-colour side skirts, a lip under the
    bumper, a wide lower intake, 15 in five-spoke alloys on 205s and 1.41 m; the 1.6: 14 in steel wheels under
    vented covers on 185s, the plain bumper's two intakes, 1.42 m. The task's brief spoke of tall thin vertical tail
    lamps; the drawing and every photograph of the 182 show the big ovals, which the loft draws.
  - The Leon Cupra (5F facelift five-door): keyed new: a near-upright nose to the grille's top at 0.72 m, the sharp
    crease along the flanks (`ShoulderFillet` 8 mm where the others roll it over 5 cm), the full-LED headlamps swept
    back to a point with the running light along their foot (`HeadlampLed`), the hexagonal grille, the Cupra bumper's
    lower intake under the plate and its corner intakes (`CornerIntake`), the window line kicking up into the C-pillar
    to a small quarter window (covers again) behind black B- and C-pillars, the roof spoiler, the slim tail lamps
    across the shoulders, the diffuser (`RearDiffuser`) with two round tailpipes at each end, the rear doors' handles,
    and 19 in twin-spoke wheels in dark alloy (`Spokes`, `TwinSpokes`, `AlloyTint`) on 235s. By day the lofts' tail
    lamps are unlit red lenses that light when the driver brakes (`TailLampGlow`, the models' `Braking`).
  - Inside: lofted cabins laid out like the real cars' rather than a model's cabin borrowed into the loft. The lofted
    shell and its lining are analytic (the covers above, the door trims, the pillars), so a lofted cabin fits it with
    nothing through the doors or the roof, where a borrowed one would be cut to an analytic shape it was not made for
    (as the W212's had to be for the BRZ, a model whose shell at least is a car's), and the SAAB's and the W212's would
    have shown their own makers' wheels and stacks in a Fiat and a SEAT. The Leon: the Cupra's flat-bottomed wheel
    (`FlatBottomWheel`), the touch screen standing over the centre stack (`CentreScreen`), bucket seats with moulded
    headrests (`SportSeats`), a black headlining; the HGT bucket seats too; both Bravos a dark grey dashboard.
  - Not done then (liners and a plate since 0.32.0, see below): the BRZ's arches are open behind the tyres in the model (no wheel-well liners), so from the side, at
    the wheels' height, the road beyond shows through the front arch; the plate recess has no plate; the Unreal level
    keeps the BRZ's lofted stand-in (its preparation rigs the model, but would keep the V8 loft's cabin, whose door
    linings stand about 4 cm outside the 86's doors: a cabin for it there is another step).
- cars13 review fixes (cars/stage2 after the merge of bodies, audio2 and phys2):
  - The Leon Cupra's loft re-keyed: the review found its drawing, getoutlines' 2015 Leon Cupra (vector-drawings/9730),
    sold for $24 under a Basic License, which the licence rule does not allow. The side is now keyed on getoutlines'
    free side view of the 2013 five-door (blueprints/28850, CC BY 4.0, the facelift kept the shell), read every 5 cm on
    the car's own axles (its wheel centres 2631 mm apart): roofline, beltline, the window's top and its kick into the
    C-pillar, the shoulder crease, sills, arches and overhangs (4278 mm against the published 4271); the body lowered
    24 mm from the drawing's 1459 mm to the Cupra's 1435. The widths, the lamps, the grille and the intakes are
    estimates against the published 1816 mm and tracks, from Commons photographs of 2017-20 Cupras looked at for
    proportions only (a head-on 2019 Cupra 290 by MrWalkr, CC BY-SA 4.0), at the side view's heights. No end view is
    free, so the ends are the least certain part; the rear reads bulkier than the car's.
  - The lofted wheels' rim lip was an octagon: Godot's `TorusMesh.Rings` is the count round the ring and
    `RingSegments` the tube's sides, and the lip had them the wrong way round (8 and 32; now 48 and 8). The round
    tail lamps' trim ring had the same fault (6 sides; now 24). It showed only with `--lofted` before cars13, and on the
    Bravos and the Leon by default since; with it every lofted body changes, as `VertexColorIsSrgb` changed their
    colours in cars/bodies: cars 0-7 are unchanged where their models draw them.
  - The showroom lets go of the cabin materials its lofted cars made (`WorldBuilder.ReleaseUnusedPropMaterials`, after
    the collection that frees its cars: a material only the cache still holds has one reference). Visiting all 13
    cars and closing now ends at the baseline's 1383 resources and 1390 MB of textures (before: 1390 and 1415, 33 MB
    over in all); the total video memory ends 8-9 MB over (1729 against 1720-1721), the pipelines compiled for the
    lofted bodies' shaders, which stay compiled for the session by design and do not grow with more visits. The check's
    target is therefore: resources and texture memory back to the baseline, the total within 10 MB. Closing while a car
    still loads, and a lofted player's car (the HGT, whose own materials are kept), end the same way.
  - The 458's mirror hangs under the windscreen's header (`mirror_offset` 4 cm down and 5 cm forward: the glass at
    0.395 m, 0.559 m ahead of the reference, 6 cm under the glass's top), where the eye correction had left it 8 cm
    over the eyes under the roof. Its left A-pillar is wide from the seat because the model's pillar is: from the
    windscreen's edge to the door glass's front edge its skin is 13-14 cm along the car at mid-height (11.5 degrees of
    view from the eyes at 400 of 900 rows, the 570S's 6.3), with nothing behind it (a ray from the eyes leaves the
    car after it), so a `crop` would open a hole in the body; turning the band's rear faces into glass left saw-teeth
    along the model's 3-5 cm faces (a prototype in the logs). Left for a step that reshapes the pillar.
  - The Cupra's limiter holds 250 km/h: its throttle ramp ends 3 km/h past the limit instead of at it, and settled
    where the drag met the throttle it left (247.1 km/h at 0.95, the review's probe).
  - The knock test compares every petrol run-up on its marks again but the Bravo 1.6's (the MX-5 with its
    aftermarket exhaust), so the 0.25.0 fours are held as before audio2.
  - Credits: Freesound #191194 (the BRZ's idle) with its licence link and changes; the MX-5's recording listed CC BY
    3.0 as its Commons page tags it, with the video's CC BY 4.0 beside it. Docs and comments: the Cupra's 245 PS in the
    overview, each cars13 car's stand-in body named in VehicleSpec, the Toyota 86 in the README's model list and its
    car paragraph, the recordings named as stand-ins, the Bravo 1.6's drawn tracks set to the spec's 1439 / 1441 mm.
  - Before this merges to main the BRZ model's release asset must exist: `models-2026-11` with
    `toyota-86-ethanchew.glb` (SHA-256 d7ff3342...), created with `--latest=false`, because the game's updater reads
    `releases/latest` and would fail to parse a `models-` tag as a version.
- Real car models (0.16, `tools/prepare_car_models.py`, `CarView.Model.cs`). The lofted bodies and the boxes of
  their cabins read as toys, from the seat most of all, so the catalogue's cars are drawn as CC BY models of real
  cars with cabins of their own (THIRD_PARTY.md): the flat-twelve a Lotus Esprit Turbo 1983 (right-hand drive,
  mirrored to the left), the V8 a McLaren 570S, the three hatchbacks a Peugeot 205 GTi, the 1990s saloon a
  VAZ-2106 (a 1.6 rear-driven Fiat-derived saloon, the nearest to the Sahin there is), the fleet saloon a
  Mercedes-Benz E-Class W212; the 1.4 estate keeps its loft (the only Renault 12 is a saloon of 1.7 million
  triangles). The search kept only models whose authors made them: an Aston Martin DB11 that looked right was
  very probably ripped from a commercial game and was dropped. The preparation, at build time from the files
  `unreal/Scripts/fetch_models.py` fetches (the outputs are not in the repository), puts each model in the car
  node's frame (faced to -Z, scaled to the simulation's wheelbase, hubs at the rest height), splits meshes that
  hold an axle's or all four wheels (`split`), scales each wheel to the simulation's tyre radius (measured from
  the tyre's vertices, as a wheel stored turned has a swollen box), finds the steering wheel's column axis
  (its thinnest direction), and places the eyes (half a metre behind the wheel's centre and 28 cm over it, plus the
  entry's `eye` correction, 13 cm under the headlining; a seat or a seat pair under them is passed over, item 22), the
  dials (on the named instrument meshes, or where the line of sight over the hub meets the dashboard; the entry may
  size and move them and move the gear readout up the panel, `gear_top`, item 22) and the mirror (the model's own
  glass, sized to it, or hung by the game under the windscreen 36 degrees right of straight ahead when the model's is
  out of view, as in the Esprit), and writes where they went to `tools/car_cockpits.json` for the tests. An entry
  may also correct a material's finish where the model is wrong about the real car (`materials`). Paint is found by
  whole words of a material's name ("lack" inside "Black" had painted every black part) and drawn in the car's colour
  with a dark lining on the back of a panel; painted faces inside the glasshouse that face the cabin take a dark
  trim (`GLP_Lining`), and painted parts that face into the car (a mirrored door) are turned out. The game hangs
  the wheels on its own pivots, turns the steering wheel, mounts the live instrument panel and the mirror, and
  dents the model's paint in a crash as it dents the loft. A checkout without the prepared models, `--lofted`,
  and the world export for the Unreal level draw the lofts. From the seat the models cost what the lofts cost
  (6.2 ms of GPU from the seat, 8.4 in the chase view, at 32 fps on the development laptop).
- The look of 0.15: the player found the track less real after 0.11-0.14 than in 0.10, and the light and the road
  of 0.10 came back, compared frame by frame against 0.10.0 at the same moments of the attract lap: the grey photo
  asphalt (0.52 with the slab shade 0.88, the grain 5 % and the normal map at 0.5), sun 1.45, ambient 1.15, exposure
  0.9, SSAO 1.5, fog 0.00004 with aerial perspective 0.35, contrast 1.05 and saturation 1.12. What made the dry road
  of 0.10 look wet stays gone: no roughness map, roughness 0.8 all over, a broad sheen towards the horizon that
  mirrors neither the car nor the sky. The grass's macro variation, the car's paint, probe and contact shadow of
  0.11 stay.
- The look of 0.11 (a step towards a photograph, after the plain photo textures of 0.10): the asphalt darkened to the
  albedo of aged asphalt (0.33) and made matte all over (its photo roughness map, mean 0.52, had the road mirroring
  the car and the sky under the screen-space reflections as if it had rained), the gravel darkened and matte, the
  grass a little less saturated with a 90 m macro-variation noise multiplied in on the terrain's second UV set
  (dry-straw patches, so the 4.3 m tile does not read as a repeat across the plain), less ambient and more sun
  (0.95 / 1.75) with blended shadow splits, saturation 1.02 and contrast 1.08, more aerial perspective; the car paint
  a coated dielectric (metallic 0.05, roughness 0.38, clearcoat 1 at 0.04); a reflection probe travelling with the
  player's car (`WorldBuilder.BuildCarProbe`: rendered again at each move, every half second or twelve metres; the
  car sits on visual layer 2 and is left out of the capture) and a dark radial decal under the body as its contact
  shadow (`CarView.MarkPlayer`); the chase camera a longer lens (54 degrees from 7.4 m back) so the car is not drawn
  in a fish-eye; leaf and grass cards with alpha-to-coverage and backlight. Off (probe) or unchanged on Low and in
  1989 mode.
- Off the road (0.9): the grass tufts are 0.45 m tall (0.75 before, a metre with the scale: a car on the verge
  drove under the grass) and start 4.5 m past the shoulder, a mown verge. The drones' and the ghost's wheels
  are drawn at rest, one radius over the road: set at the hub mounts they hung 16 cm up and the cars floated.
- Updater (rewritten after the 8 November investigation of the failures at home; until then Godot's HttpRequest
  downloaded all or nothing). The network work is engine-free and runs on the thread pool; `Updater` (a node)
  starts it, polls it every frame and acts on the main thread.
  - Finding the release (`UpdateFeed`): first the release's `update.json` through
    `github.com/…/releases/latest/download/update.json` (exact package names, sizes, SHA-256; outside the REST API's
    60 calls an hour per address, which a carrier-shared address can find used up), then the API's latest release
    (sizes, GitHub's own `digest`; the rate-limit headers logged), then the API's release list (a latest release that
    is not a plain version tag, e.g. `models-2026-11`, is passed over; drafts and pre-releases too). Out of API calls,
    the tag github.com's own redirect gave away still names the packages (`GrudgeLap-Setup-<v>.exe`,
    `GrudgeLap-Portable-<v>.zip`, checksums from their `.sha256`). Exact names only: the Unreal preview zip and any
    other asset are never taken. Versions of two to four numbers compare padded (0.77.0 = 0.77.0.0).
  - Downloading (`UpdateClient`, .NET's HttpClient: SChannel TLS that fetches a missing root as a browser does, the
    system proxy with the user's sign-in, where Godot trusted only the roots in the store and ignored the proxy):
    the bytes go to `<name>.part`; every attempt asks the github.com URL again (redirects followed by hand: the signed
    release-assets URL expires within minutes) with a `Range` for the rest; 45 s without a byte ends an attempt (a
    half-open connection never ends by itself), six attempts in a row that add nothing end the download, waits of
    1-3-10-30-60 s between them, Retry-After honoured up to 2 minutes; a 200 to a range request rewrites from the
    start; size and SHA-256 are checked on the worker, a mismatch is fetched once more from the start, and only then
    is the file renamed to its real name, so a file under that name is always a verified package and is reused
    without a download. Free space is checked first (the portable copy keeps room for unpacking). Progress is read
    from Interlocked counters; quitting cancels everything at once (cancelling HttpRequest on a dead connection had
    blocked the main thread for good).
  - When: at every start unless a check ran within 2 minutes (a last check in the future counts as due); a failed
    automatic check or download is tried again after 30 s and 3 minutes (the .part keeps what came); F10 asks at
    once. The browser is never opened by a start, only by F10.
  - Handing over: on the title screen, or once a credit is over and no initials are being typed. An installed copy
    starts the new installer itself, by its full path, with `/SILENT /NORESTART /SP- /NOCANCEL /UPDATE=1 /LOG=…`
    (until 0.76 a hidden PowerShell from %TEMP% ran it after the game had gone, what antivirus software watches
    most, and nobody heard when it failed); a portable copy starts its unpack script by Windows PowerShell's full
    path. Both through ShellExecute, so that the child inherits none of the game's handles. A start that fails
    (an antivirus blocking the file) or a child that ends within 1.5 s leaves the game running with the reason on
    screen; otherwise the game quits through `Bootstrap.Shutdown`, which lets go of the one-copy lock.
    `GrudgeLap.iss` `PrepareToInstall`, when the game started it (`/UPDATE=1`, or `2` for no restart afterwards),
    waits up to a minute for that lock to go and the exe to open exclusively before any `taskkill` (tried for real
    on a sandboxed build: it waited the 27 s a helper held them, then installed).
  - The next start reads `[update] applying` (written before the hand-over) against its own version: an update that
    took empties the download folder; one that did not is counted with its reason (the installer's log: the last
    message box's words, or its last failure line; the script's `FAILED:` line), shown, and after two the game stops
    applying that version and F10 opens its release page.
  - `user://update.log` (outside Godot's rotated logs/, about 256 KB then `update.old.log`): versions, paths, free
    space, proxy, antivirus products and states (Security Center's records), every route, attempt, verification,
    hand-over and the installer log's last lines. The HUD's notice line and the title panel show the status and every
    failure's reason, also while the menu or the showroom is open.
  - The download folder holds one package at most: other versions' packages and parts go when a download starts,
    packages of the running version or older at every start, everything after a confirmed update (the development
    laptop had 1.45 GB of applied installers in it).
  - The exported exe carries the engine's version (`application/file_version` and `product_version` 4.7.2.0 in
    `export_presets.cfg`) instead of the game's: the exe is the stock Godot template with the game's name and icon,
    and with the game's version it differed in 4 bytes from release to release, so antivirus software met a new
    unsigned exe twice an hour at night. Exports of 0.76.0 and 0.77.0 of a test project are now byte-identical.
    Nothing reads the exe's version: the game's comes from the .pck, Inno copies with `ignoreversion`. Bump it with
    the engine.
  - Checks without a player: `--update-only[=apply]` (the updater alone, no world, exit code the result),
    `GRUDGELAP_UPDATE_DRYRUN=1` (find the release, download nothing), `GRUDGELAP_UPDATE_FEED=<url>` (a stand-in for
    GitHub). `update.tests` runs the feed and the client against an in-process HttpListener that drops at 40 %, falls
    silent, ignores ranges, expires its signed redirects, runs out of API calls, offers a non-version latest tag and
    extra assets, serves a bad checksum, trickles, and against a nearly full disk; and the portable unpack script
    for real on a folder of its own.
- One copy per computer (`SingleInstance`): the game holds a Global named mutex; a second launch signals a
  named event, which the running game polls every frame to restore and raise its window, and quits before
  building the world. `--invisible` runs neither take nor check the lock.
- Command-line switches for automation: `--attract`, `--chase`, `--speed-route`,
  `--car=flat12|v8|hothatch|five|turbohatch|saloon|estate|fleet|458|bravohgt|bravo16|leoncupra|brz` (or the car's
  index, 0-12, or a short name: `peugeot205`, `delta`, `r32`, `sahin`, `toros`, `w212`, `flatv8`, `cupra`),
  `--ram=tree|building|traffic|test` (the
  scripted driver runs the course until a thing of that kind is near and drives into it, or into a tree planted
  on the start straight: the crash replay on film),
  `--no-traffic`, `--weather=dry|overcast|wet|rain` (the credit's weather; otherwise every credit draws one),
  `--lofted` (the lofted bodies instead of the real models), `--paint=<original|red|white|silver|black|dark-blue|green|yellow|#RRGGBB[:solid|metallic|pearl]>`
  (the run's car in that paint, this run only), `--showroom[=<car>]` (the showroom open from the start, on that car,
  over the title screen), `--showroom-entry=menu` (reached through the menu's CAR line instead), `--turn=<degrees>`
  (the turntable held at that angle; a drag still turns it), `--showroom-paint=<paint>` (the showroom's car in that
  paint, a mixed colour mixed through the picker), `--picker` (the colour picker open), `--paint-book-test` (the
  per-car paints' settings round trip and migration, then quit: 0 when every check holds),
  `--look=drone:<n>[:distance[:bearing[:height]]]` (the camera on the n-th drone), `--seat-look=<yaw>,<pitch>` (the cockpit view turned,
  degrees, left and up positive: pictures of a cabin), `--dent-test` (a deep dent in the nose two seconds in),
  `--perf-log` (the measured GPU and CPU time a frame, every five seconds, under the frame cap),
  `--drop=<file>` (a file dropped on the window two seconds in: the replay music's path),
  `--no-ghost` (the saved best lap is not raced, for pictures of the car), `--no-hud` (no
  overlay over the 3D scene, for comparing it with another engine's picture), `--retro`,
  `--screenshot=<file>`, `--at=<s>`, `--quit-after=<s>`, `--park` (the car waits at the start with its
  engine off while the traffic and the train go by: checks of the placed sounds), `--time=<s>` (a credit
  of that length), `--try-table` (the attract driver's credit is offered to the high-score table: pictures
  of the entry screen), `--new-game-after=<s>` (a second credit, as F5 starts one: the check for the car that
  stood at the line in 0.8.0), `--menu-after=<s>` (the menu opened, for a picture of it), `--jingle=<fanfare|replay|gameover>`
  (a jingle a second in, to check the synth with `--park`), `--quality=low|medium|high`,
  `--orbit=<yaw>,<pitch>,<distance>` (degrees and metres, 0 = from behind), `--show-all` (whole car from
  the driver's seat, for checking the body), `--save-best` (an attract lap may become the saved best lap),
  `--export-world=<file.glb>` (writes the generated terrain, roads, bridges, props and the player's car to
  glTF for another engine, and beside it `<file>.json` with every grass tuft, every tree of the wood
  (position, yaw, scale, variant; one mesh of each variant travels in the file as `ForestVariant-<n>`), the
  props by kind, the car's pose and the cockpit camera; no lights, sky, clouds, other cars or grass meshes;
  then quits),
  `--invisible` (the window is parked off-screen, made unfocusable and hidden, so an automated run never
  shows on the desktop or takes the keyboard; pair it with Godot's own `--position -32000,-32000` so the
  window is created off-screen in the first place). Rendering continues for a hidden window, only a
  minimised one stops it, so `--screenshot` still works. After a GPU device loss (a Vulkan crash in a long
  invisible run) such runs can hang before their first frame; recording the run with Godot's own
  `--write-movie <file.avi> --fixed-fps 30` and taking the frame with ffmpeg has not, and its audio track
  is the sound check too.

- Start-up: the world is generated, not loaded. Meshes go to the engine through `MeshBuilder` (managed
  arrays, one engine call per mesh; tangents of the terrain grid and the road strips come from their UV
  directions instead of the general solver) and the terrain is sampled in parallel, about 0.8 s on an
  8-core laptop; the engine adds about 1.3 s on the first frame. The boot splash (`game/splash.png`, drawn
  from a cockpit screenshot by `tools/make-splash.py`) shows the game rather than an empty window meanwhile.
  The game data ships beside the exe (`GrudgeLap.pck`), not inside it: antivirus software can hold a new
  unsigned executable back on its first launch (15-35 s measured with Acronis on the development laptop,
  4 s afterwards), and the bare exe changes only with the engine or the version stamp. Signing it is the
  real fix, planned for 1.0.

### Sound audit (0.19)

The car's sound was held against what a microphone hears in and around a real car (in-cabin and pass-by recordings,
and the literature of vehicle acoustics: Sandberg and Ejsmont, *Tyre/Road Noise Reference Book*; Harrison, *Vehicle
Refinement*) and measured offline instead of by ear: the voices are rendered headless in `audio.tests` and their
levels, spectra and envelopes compared (an FFT helper, `audio.tests/Spectrum.cs`), and the tyre sounds are driven by
the simulation's own stops. Findings ranked by how much each kept the car from sounding real; the first seven were
fixed in 0.19, each pinned by a test (`TyreMixTests`, `BodyNoiseTests`, `GranularEngineTests`,
`physics.tests/SoundReadoutTests`).

1. **The engine's load was the torque over 500 N m** (fixed). A V12 reaches that; a 1.4 four never comes near. At full
   throttle and 4000 rpm the estate read 0.19, so the voice played 77 % of its overrun recording, low-passed as a shut
   throttle and 11 dB under its full-throttle level: driven flat out it sounded as if coasting. The saloon read 0.24,
   the fleet saloon 0.25, the hot hatch 0.39, the five 0.37, the turbo hatch 0.6 on full boost; only the V12 and the
   V8 got their bark. Now the load is the throttle the engine runs at (`EngineModel.Throttle`: the driver's, the idle
   governor's, shut by the limiter's cut and through assisted shifts), followed in 1/16 s instead of 1/8.
2. **The tyres ignored the tyre model** (fixed). The squeal grew with the slip angle from a fixed 4 degrees to 17
   whatever the tyre, its load or its grip, and only grew; a locked wheel and a spinning one were the same screech
   set by the slip ratio alone; a stop with anti-lock brakes sounded like a locked one; the loudest wheel stood for
   all four; the skid pad was taken for gravel and crunched; a wet road squealed like a dry one. Now `TyreMix` reads
   where each tyre is on its force curve (`TireModel.SlipToPeak`, 1 at the peak): quiet below 60 % of it, the squeal
   from there to the peak, past it the howl of a sideways slide or the scrub of a locked or spinning wheel, pitched
   by the sliding speed; the wheels add as independent sources (3 dB per doubling), heavier tyres are louder, water
   all but silences the squeal and softens the scrub. On the simulation's own stops from 100 km/h the estate's locked
   wheels scrub (1.2) with the squeal at a fifth of that; the V8's anti-lock brakes hold its tyres at the peak, so it
   squeals (0.76) with no scrub, chattering between 0.4 and 1.2 at the valves' seven cycles a second (the simulation's
   modulator settles too finely to be heard, so the chatter is laid on while it holds pressure back).
3. **The wind was missing** (fixed). Silent below 43 km/h and grown as ((v - 12) / 70)^2, it was 26 dB under the
   tyres' roar at 110 km/h and still 17 dB under at 216 km/h, where real cabins are dominated by it. Now
   (`BodyNoise`) it grows as v^2 (12 dB a doubling) up to 200 km/h, its band moves up with the speed (vortex
   shedding) with gusts of buffeting, and it is calibrated against the roar's recording to cross it at 110 km/h in the
   cabin (`The_wind_takes_over_from_the_tyres_roar_near_110_kmh`); from outside it is 3 dB lower.
4. **The roar grew almost linearly with the speed** (fixed): 22 dB a decade, where measured tyre/road noise grows by
   about 30 (v^1.5). Its level at 110 km/h is unchanged; like the wind it grows no more past about 216 km/h.
5. **No gear whine** (fixed). Gearboxes sing at their meshing frequencies (a shaft's revolutions a second times its
   teeth): now the selected pair and the constant-mesh pair of an assumed countershaft box, loudest in first, a
   direct top gear nearly silent, a straight-cut reverse three times as loud, and the hypoid final drive's tone rising
   with road speed, all loaded by the torque through the closed clutch and modulated by the shafts' runout; louder in
   the cabin (`Gears_whine_at_their_meshing_frequencies_and_loudest_in_the_low_gears`).
6. **No anti-lock pump** (fixed). The modulator's return pump growls at about 45 Hz while it works, pulsing with the
   valves, heard in the cabin and little outside.
7. **No spray on a wet road** (fixed). The rain's hiss did not care whether the car moved; now the tread throws water
   with a hiss that grows as v^1.5 with the wetness, on top of the rain.
8. **Overrun against throttle** (checked, kept). On every car the overrun at the same revs is quieter overall (6-15
   dB), at the firing frequency (2.7-20 dB) and above 2 kHz (3-23 dB): pinned by
   `Overrun_is_quieter_than_the_throttle_at_the_same_revs_in_every_band`. It is not always darker: the V12's, the
   V8's and the saloon's overrun recordings carry more rasp than their run-ups, which is what such exhausts do.
9. **Timbre against revs** comes from the recordings and rises with the revs, but not evenly where a bank joins
   segments recorded apart (the turbo hatch is brighter at 3350 rpm than at 4700). Fixed in 0.20, see below
   (*Banks evened across their joins*).
10. **The cabin is one filter for every car**: a 2.6 kHz low-pass and a 130 Hz boom, the same for a front engine
    heard through the bulkhead and a mid engine behind the driver's head; the intake's roar (front) against the
    exhaust (rear) is not modelled. Fixed in 0.20 without new recordings, see below (*Voicing per car*).
11. **The turbo** (minor): its whistle is one sine about 25 dB under the engine, without the compressor's surge or the
    flutter of a shut throttle on boost; the blow-off valve is fine. Fixed in 0.20, see below (*The turbo*).
12. **The rev limiter**: the cut is 60 ms, which the voice's smoothing blurs into a dip rather than the hard
    stutter of a real limiter. Fixed in 0.20, see below (*The limiter*).
13. **The other cars** (kept): every drone plays the same recording, attenuated by inverse distance, Doppler-shifted
    from the simulation and low-passed with distance by the engine (air absorption); they are a background.
14. **The Unreal version** (open): GlpAudio still takes the torque over 500 N m as the load and keeps the old tyre
    mapping. The throttle has to be passed through (its inputs are known there) and `TyreMix` and `BodyNoise` exposed
    through `physics.native` as new functions, which keeps its ABI.
15. **A slide near broadside went quiet** (fixed in review). The sideways slide was rebuilt as the speed along the
    wheel times the tangent of the slip angle, which collapses as a slide nears 90 degrees: the howl fell from 1.2 at
    80 degrees to nothing at 88. The wheel now reports the ground's speed across it (`WheelState.LateralSpeed`), and
    the howl, the tyres' travel and the roar take the contact patch's own speeds (`A_slide_near_broadside_keeps_its_howl`).
16. **The hatchbacks' voices** (changed in cars3, with the cars). Each engine names its bank (`EngineSpec.SoundFolder`)
    and its cylinders, which the Unreal version's table follows. The 205 keeps the Civic Type R's four; the Delta, a
    turbo four now, plays the Pulsar's turbo four (`turbohatch/`) with the synthesised whistle and blow-off, which come
    with any engine that has boost; the R32 plays a six (`v6/`, cut from the CC0 archive item's supercharged V6 of a
    Jaguar F-type S: no CC0 or CC BY VR6 recording was found, and a VR6 fires every 120 degrees like any even six), at
    rpm x 6 / 120 (`GranularEngineTests` hold its firing frequency at 2500 and 5500 rpm and at idle). Its run-up bank
    is cut from blips of a standing car, unloaded: above its top the voice spaces its top pulses closer. The
    five-cylinder banks left the build with the car (`GranularEngine.FivePattern` stays, unused). Open: the archive
    item's own Delta track is a drive-by with no idle, and a Commons Integrale recording awaits its licence review; a
    real VR6 recording would replace the V6's.
17. **The hatchbacks' voices after the merge** (soundfix, after cars3 and sound3 met). Six audio tests failed and the
    R32 clicked: (a) the V6's run-up was cut through the crackle of its overloaded recording at the top of every blip
    (steps of 0.69 between samples at 3,500-3,600 rpm) and through the stretch at the standing car's limiter (cut every
    150 ms, 10 dB duller), and its segments began with the MP3 decoder's silence, which the tool's seek left there;
    re-cut to the crackle (up to about 3,600 rpm) with the tool seeking exactly and warning of crackles (SOURCES.md),
    its overrun from the limiter (3,800 rpm, was 3,100). (b) Past the top of its recording (3,600 rpm, its redline
    6,600) the voice laid the run-up's pulses closer and divided their overlap out, the pulses taking each other down:
    the R32 fell 8 dB from half its redline to the redline (-16.0 LUFS pulling hard; 4.9 LU under the roar and the wind
    at top speed in the cabin). A first fix kept the whole grains and counted their overlap only for its share: the
    level came back, but several grains sounded at every sample, each a pick at random among the 32 highest pulses from
    four blips, and their sum was a noise between the harmonics (review: pulling at 90 % of the redline the harmonics
    held a sixth of the sound, -7.7 dB, against 0.6, -2.2 dB, on 0.20's banks, the bed at a third, half and two thirds
    of the firing frequency 2.7 dB under it, against 11.3, and some 50 peaks within 14 dB of the firing tone, against
    one). Now a run-up grain laid past the top has its window held to the period it is laid at, so the windows sum to
    one and each pulse plays at its recorded strength alone, and it is picked among the 16 highest pulses
    (`GranularEngine.TopSpread`): at 90 % of the redline -3.3 dB of harmonics, the bed 13.8 dB under, two peaks (at 100
    %: -3.5 dB, 12.2 dB, two; 0.20 -3.3, 7.4, one). This changes every car whose redline lies past its run-up's top (the
    saloon 1.38 times, the estate 1.24, the Delta 1.12, the fleet saloon 1.04, the V8 1.03), only up there;
    `Pulls_as_a_tone_up_to_the_redline` holds the harmonics to a third of the sound on every car (measured: at least
    -3.7 dB, the hot hatch's own recording at 60 and 80 %; the first fix failed it, -5.0 dB at 80 %). (c) The Delta's
    run-up held a compressor's flutter (22.2-23.1 s of the Pulsar track, pulses up to 30 dB brighter), cut out: its
    timbre's spread about its trend fell from 2.3 to 1.0 dB; its turbo's whine, tuned against a voice the flutter's
    peaks had held 1.5 LU down, went from 0.085 to 0.1 to stay level with the engine in its band. (d) Every car's level
    across its revs (review): within each recording a run-up's level drifts (the car draws away, a blip fades at its
    top: the Delta's last segment 4 dB over the bank's trend at 4,000 rpm and 4 dB under it at 5,500, the V6's highest
    pulses half as loud as those twenty before), and held at a given rpm the voice played that stretch's level. Held at
    full throttle the Delta sagged 3 to 5 LU between 5,000 and 5,600 rpm, the R32 dipped to -17.9 LUFS at 2,700 rpm, the
    estate moved 7 LU from 45 to 50 % of its redline. Every grain of the run-up is now levelled onto the run-up's trend
    (`GrainBank.LevelGains`: the median level of its segment's dozen pulses around it, so the cylinders' beat stays and
    a few pulses of decoder silence do not lift their neighbours), and every grain of the overrun onto the same trend.
    Held at full throttle every 100 rpm the Delta now rises from -20.1 LUFS at 2,000 rpm to -12.5 at the redline, never
    more than 0.5 LU down from one step to the next, the R32 from -16.0 to -13.6 at 3,900 and level to the redline
    (-13.9); within 40 rpm of 4,080 the Delta's holds a rev apart range over 0.8 LU (3.3 before). The overrun levelled
    onto the run-up replaces the cut's match by the recordings' overall loudness: what a shut throttle or a cut takes
    away is the level law, the throttle's filter and the overrun's own sound. (Levelling the run-up alone left the
    Delta's overrun at 4,500 rpm 4.6 dB under its full throttle and the V8's at 5,600 2.2 dB, against the 5 dB
    `Overrun_is_quieter_than_the_throttle_at_the_same_revs_in_every_band` asks; now 9.0 and 6.6.) The cut is therefore
    no longer "as deep on every car", and was not before: held on the limiter, cut 60 ms in 100, the level swings 7.2 dB
    on the estate, whose overrun recording reaches its redline (the level law's 7.5 dB), and 11 to 19 dB on the cars
    whose overrun is laid past its top there and averaged, as it is with the throttle shut (the flat-twelve 17.4, the V8
    12.9, the hot hatch 15.6, the Delta 19.0, the R32 15.9, the saloon 14.6, the fleet saloon 11.3; in 0.20 6.6 to 15.8
    dB, matched by loudness). In the game (hidden, silent 40 s attract runs, dry, no traffic, cabin and chase views,
    against 0.20): the R32 -14.1 and -14.2 LUFS (0.20 -15.5 and -16.3), its loudness range 5.0 and 5.6 LU (4.1 and 4.6;
    the first fix 6.5 and 7.0), its sharp transients 0 and 5 (15 and 68), its largest sample step 0.22 (0.46); the Delta
    -14.0 and -14.1 (-14.1, -14.5); the other cars within 0.8 LU of 0.20 (the hot hatch 0.8 and 0.6 quieter, its trim
    taken at levelled revs), their loudness range narrower on the cars whose recordings drifted most (the estate 3.6 to
    1.4 LU in the cabin, the fleet saloon 9.2 to 5.9) and up to 1.4 LU wider on the hot hatch and the Delta, the fleet
    saloon's chase transients 60 to 16; every peak at -0.6 dBFS per channel or under. (e) Tests that named cars by
    catalogue index (the turbo four as index 4, the R32's now) look them up by sound folder or by boost; the
    full-throttle test compares the loads at the follower's revs, and holds the old load (the torque over 500 N m) to
    the level law's own figure on every car instead of 3 dB under a cut-off (which held the Delta, 0.55, to more than
    its law's 2.6 dB). Two tests passed or failed by the grains a run happened to pick, and any change to the picks
    moved them: the limiter's, averaged over twenty cycles (the Delta's crisp cut fell in 6 to 26 ms over eight seeds,
    the V8 of 0.20 swung 6.6 dB over twenty and 5.3 over a hundred), now over a hundred; and the stopped engine's tail,
    held to an absolute 0.01 (the estate's 0.0085 or 0.0102), now to 25 dB under the idle it stopped from (measured 28
    to 34 dB). Left: past the top of its recording the R32's run-up is its 16 highest pulses (3,560-3,620 rpm, eleven of
    them from one blip) spaced up to 1.8 times closer, a tone but no recording of those revs; lifting off from the
    redline it plays its overrun (recorded up to 3,800 rpm) past its top as well, averaged, with 28 sharp transients in
    five seconds outside and in the cabin (0.20 9 and 16; its overrun bank itself has two in its 5.7 s); a VR6 recorded
    through its revs would replace both.
18. **Every voice clean across its revs** (banks, after 0.22). Measured bank by bank and rendered from idle to redline:
    (a) the banks cut before the V6's re-cut began their segments with the MP3 decoder's silence, 50 grains 12 dB or more
    under their neighbours, most 40 to 160 dB under (the V8's run-up 15 and its overrun 7, the flat-twelve's run-up 8
    and its overrun 7, the Delta's overrun 5, the 205's run-up 4 and its overrun 3, the estate's overrun 1), each a
    drop-out of a firing period wherever it was picked; rendered flat out from idle to the redline the flat-twelve fell
    32 dB for 10 ms and the Delta 23. Every bank is re-cut with the exact seek from the same recordings; where that
    exposed what the silence had hidden the start points moved (SOURCES.md): the V8's run-up past its recording's
    fade-in, the Delta's idle past the engine catching and its overrun past the end of a blip (both crackled at their
    documented starts), the 205's overrun past an overloaded blip, the saloon's idle and the fleet saloon's Bravo
    segments around their ticks, the V6's overrun past the limiter's last bangs. The tool now also fills the pulses it
    lost where two expected pulses moved onto one peak (12 to 17 % of some run-ups' grains spanned over 1.3 periods,
    and played quieter laid at the revs: the fleet saloon stepped 1 LU past the top of its run-up), since the review
    only gaps of 1.75 periods or more (f). No bank has a grain more than 9.8 dB under its neighbours or a crackle by the
    tool's measure (`Every_bank_is_cut_without_drop_outs_or_crackles`).
    (b) Between the idle's recording and the run-up's the voice sagged at full throttle, 3 to 8 LU under both ends:
    the Delta at 30 % of its redline (2,000 rpm, below its run-up's bottom at 2,430) 4 LU under its run-up 400 rpm up,
    the V8 at 1,900 rpm 7 LU under its idle and its run-up. The idle's pulses, laid closer than recorded, averaged one
    another down; two recordings mixed by their shares in amplitude lost up to 3 dB halfway (they do not add in phase);
    and the idle kept its idling level whatever the throttle. Now (`GranularEngine`) the idle's grains past the top of
    its recording are held to their period like the run-up's, each stream (the idle, the run-up with the overrun) is
    normalised apart and keeps the square root of its share, and pulling, the idle's grains are levelled onto the
    run-up's trend and brought to the loudness of the run-up's pulses (`LoadedIdleGain`, since the review across the
    crossfade, (f); by RMS alone an idle recorded with its energy under 100 Hz stayed 5 to 9 LU under in loudness).
    Levelling the grains by their K-weighted loudness instead of RMS was tried and dropped: the run-ups' joins then
    stepped up to 2.3 LU. Held
    flat out every 100 rpm from idle to the redline (each the mean of five two-second holds within 50 rpm), no car now
    steps more than 1.1 LU from one to the next, outside or in the cabin (before 1.4 to 2.8; the V8 also 2.7 LU at
    2,300 rpm from its run-up's fade-in and 1.9 at 6,700 from a 9 dB dip in its recording, both cut out):
    `The_level_follows_the_revs_evenly_from_the_idle_to_the_redline`. Idle, flat out to the redline in eight seconds and
    off the throttle, on five seeds since the review, no step between samples is over 2.2 times the RMS of the 5 ms
    around it nor any 10 ms more than 16.9 dB under the 210 ms around it
    (`Sweeps_from_idle_to_the_redline_and_off_without_clicks_or_drop_outs`). (c)
    The R32: no better recording was found (SOURCES.md lists the CC0 and CC BY sixes looked at on Freesound, Wikimedia
    Commons and in the archive item: a Supra on a dyno and a Lexus IS300 pulling through their gears both overload,
    the rest are standing, in-cabin or talked over), so its run-up still stops at 3,600 rpm and its overrun now at
    3,650; lifting off from the redline its sharp transients fell from 28 in five seconds (43 once the tool filled its
    marks) to 1 to 3, the overrun's first tenth of a second after each limiter, which held the ignition's last bangs,
    cut away. (d) The limiter test compared the crisp cut's fall with the blurred one's; on the estate's re-cut banks
    the blurred cut swings 0.7 dB, a third of a decibel between its quarter and three quarters, and its fall timed at 6
    or 24 ms as the grains fell: the test now also takes a blurred swing of a quarter of the crisp one or less as not
    crisp (`The_limiter_cuts_crisply`; the estate's crisp cut 6.5 dB in 6 ms). In the game (hidden, silent 40 s attract
    runs, dry, no traffic, cabin and chase, against 0.22): every car within 0.3 LU of before (the fleet saloon 0.3 louder,
    the flat-twelve 0.2-0.3), loudness range within 0.6 LU, sharp transients from 4 to 2 behind the R32, 1 to 0 in the
    flat-twelve's cabin, 16 to 8 behind the fleet saloon and 4 to 3 in its cabin, 0 elsewhere before and after, no 10
    ms drop-out in any run, peaks at -0.6 dBFS per channel or under. Left: the R32's run-up past 3,600 rpm is still its
    16 highest pulses laid closer; the drive and overrun recordings, mixed by the load at part throttle, still sum by
    amplitude (up to 3 dB down halfway, around a quarter of the throttle), which the idle's mix no longer does; the
    fleet saloon's and the 205's run-ups have sharp onsets at low revs that the in-game transient count still finds
    (their recordings', 11-15 dB over a grain's top end).
    (f) The review of the banks (independent, before the merge) found the mark-filler cutting holes, the idle's throttle
    flattened and the tests too narrow. The tool rounded a gap of 1.5 periods to two and filled it with a pulse half a
    period from either side, and kept two peaks half a period apart: the 205's idle, whose tracker read 38 Hz over an
    uneven stretch of 34 pulses a second, gained pulses with sides of 585 to 656 samples against a period of 1,160, and
    idling at 900 rpm two such sides left a gap no grain covered (five frames 20 dB or more under the idle around them
    in two minutes over six seeds, 6 ms at -120 dB). It now fills only gaps of 1.75 periods or more, three quarters of a
    period from either side, and keeps the stronger of two peaks closer than 0.6 of a period; every bank is re-cut with
    it from the same segments (only the marks move), and none reaches under 0.6 of its period on a side
    (`Every_grain_reaches_most_of_a_period_either_side`; the 205 idling now at most 17.4 dB under,
    `Idles_without_drop_outs`, five seeds at the governor's throttle and at full throttle). The idle slid by its gain
    from its own level onto the run-up's trend between 0.2 and 0.6 of the throttle, against the level law wherever the
    trend lay under its own level (at the idle's revs, on most cars): the V8 at 900 rpm rose 1.7 LU from the governor's
    throttle to full and fell 1.9 from 0.3 to 0.6. The idle's level now goes from its own at a fifth of the throttle to
    the pulling one at full in even decibels with the level law (`IdleLevel`), and the pulling one is matched stream
    against stream, the idle alone against the run-up alone at the same revs a quarter, half and three quarters across
    the crossfade, outside and in the cabin (matched at the idle's top revs alone, against the run-up laid there far
    under its recording, the pulling idle came out 1 to 4.5 dB under its own level on five cars; matched against the
    run-up at its own bottom revs, as tried, the idle was the louder across the crossfade, its pulses held to the period
    growing louder with the revs, and the voice sagged where the run-up took over: the 205 1.8 LU at 2,000 rpm, the
    fleet saloon's cabin 2.2 at 1,700). Opening the throttle at the idle's revs now rises 0.4 LU or more a step and 3.0
    (estate) to 12.2 LU (saloon) in all, the V8 3.8 (`Opening_the_throttle_at_the_idle_revs_raises_the_level`; 0.22 rose
    6.4 to 7.7 LU on most cars but sagged 3 to 8 LU into the run-up). Held up to its own level instead, as the review
    proposed, the idle at full throttle lay over the run-up's first revs on the estate, the Delta and the V6 hatch, and
    the voice fell 1.7 to 3.0 LU into the run-up and rose again (and stepped 1.7 to 2.0 LU on the estate); that was
    measured and dropped. Matched this way the saloon's idle (a Niva's, quiet and under 100 Hz) was lifted 18 dB at full
    throttle and stepped by 1.0 between samples: each pulse is now held, like the idle's own level, to a step of 0.45
    (`LoadedIdleLevels`); it still rounds off its peaks at full throttle at the idle's revs, as in 0.22 (1,300 samples
    in ten seconds against 900, its light-throttle idle already at the ceiling). The tests now run the sweep and the
    idle on five seeds (seed 4 of the Delta dropped 20.3 dB idling), hold the idle, add a sag limit to the level across
    the revs (no point 2.1 LU under the lower of the loudest within 600 rpm either side: 1.5 or less but the fleet
    saloon's cabin 2.0 at 1,700 rpm, its run-up alone sagging 1.4 there laid under its recording, and the estate 1.7 at
    1,200 rpm, where its quieter idle recording plays; the 205's, 1.5 before, is 1.3), and count the ticks lifting off
    from the redline (`Lifts_off_from_the_redline_without_a_run_of_ticks`: at most 20 in five seconds; the V6 hatch 14
    in its cabin, 4 outside, against 28 to 44 with the limiter's last bangs in its overrun, which the crackle test
    passed at 3.3 times; the 205's overloaded blip 35 to 76). SOURCES.md no longer says Commons has no Volkswagen (it
    has at least eight; the Touareg's VR6 is revved standing and clipped) and names the archive item's Supra 950 and two
    GT3s (blips, no run-up). Making a voice takes 65-135 ms (50-120 before), on a worker. In the game (hidden, silent 40
    s attract runs, dry, no traffic, cabin and chase, against 0.22): every car within 0.4 LU (the fleet saloon 0.3-0.4
    louder), loudness range within 0.5 LU, no step over 3 times the RMS around it and no 10 ms drop-out in any run;
    peaks -0.7 to -3.3 dBFS, the flat-twelve's chase back to -1.9 and the V8's cabin to -3.0 (-0.5 and -1.0 before this
    review) and the saloon's chase -2.2, but three runs touched -1 dBFS for an instant (the Delta's chase -0.7 at one
    moment, 3 samples; the estate's cabin -0.9 and chase -1.0), where the engine alone stays under -1 dBFS
    (`Full_throttle_peaks_under_minus_one_dbfs`). Left: the estate's two idle recordings differ by about 1.5 LU pulling,
    and the fleet saloon's run-up laid under its recording sags in the cabin, both within the 2.1 LU limit and over 1.5;
    full throttle at the idle's revs is held to what the crossfade allows, 3.0 to 3.8 LU over the governor's throttle on
    the estate, the Delta and the V8.

### Sound, 0.20: the audit's open findings and the balance between cars

Measured the same way (renders in `audio.tests/EngineVoiceTests` and `TurboNoiseTests`) and in the game itself:
hidden, silent `--write-movie` runs of 40 s of attract mode per car and view at 30 fps, their audio measured with
ffmpeg's `ebur128` (integrated loudness, mono, everything in the mix: engine, tyres, wind, turbo, traffic).

- **Voicing per car** (finding 10, `audio/EngineVoicing.cs`). One recording per engine carries intake and exhaust
  together; the voicing reshapes it per body and viewpoint from what is known of cabin transfer, not from new
  recordings. The panels between engine and driver lose more the higher the frequency (the mass law, about 6 dB an
  octave, more where absorbers line them; Beranek and Vér, *Noise and Vibration Control Engineering*; Harrison,
  *Vehicle Refinement*): a front engine is heard through the dash panel, the most heavily treated of a car, low-passed
  at 2.2 kHz, its exhaust under the floor mostly as boom; a mid engine (the flat-twelve and the V8, behind the seats)
  sits a metre from the driver's head behind a single, often glazed bulkhead with its intake plenum right there,
  low-passed at 4 kHz with its induction roar (a band about 380 Hz, the firing harmonics and the intake's runner and
  box resonances) up to 5 dB at full throttle, 1.5 dB for a front engine. The boom is the cabin's first longitudinal
  mode, c / 2L (Nefske, Wolf and Howell, *J. Sound Vib.* 80, 1982): 132 Hz in the 1.3 m two-seat cells (0.19's one
  filter), 75 Hz in the hatchbacks (2.3 m, open to the load space), 90 Hz in the saloons (1.9 m), 61 Hz in the
  estate (2.8 m), under a four's firing frequency at speed instead of on it. From behind the car (the chase view) the
  tailpipe beams its top along its axis above ka = 1 (Levine and Schwinger, *Phys. Rev.* 73, 1948): +3 dB above
  1.8 kHz for a 30 mm pipe, 1.6 kHz for the mid-engined cars' 35 mm ones, with half the mid engine's intake roar.
  `SampledEngine.VoicingOf` keys it on `CarStyle`: the flat-twelve and the V8 are mid-engined, the saloons and the
  estate have their own cabins, and any other style (the hatchbacks, and new ones) is a front-engined hatchback.
  Measured on the estate's recording at 4000 rpm: the energy above 2.5 kHz against below is -27.0 dB in a front
  engine's cabin, -23.3 dB in a mid engine's and -13.4 dB behind the car.
- **The turbo** (finding 11, `audio/TurboNoise.cs`, any engine with `BoostShare > 0`). The shaft turns as the square
  root of the compressor's work (Euler), the most boost taken from the engine's own figures, a pressure ratio of 1 /
  (1 - BoostShare) (1.8 for the turbo four) at 150,000 rpm, and off boost the exhaust still turns it to a third of
  that; it spools behind the boost and runs on for a second. It whistles at its six blades' passing frequency (Raitor
  and Neise, *J. Sound Vib.* 314, 2008), 5.2 kHz at a third of its speed, climbing out of the mix as it comes on full
  boost, and at the shaft's own frequency (the unbalance's whine, 2.5 kHz on full boost), over the rush of the air.
  Shutting the throttle on boost vents the charge through the diverter valve (`EngineSpec.LiftOff`, `Vents` by
  default, as production petrol turbo cars have one; a diesel's spools down, diesel6); without a valve the compressor
  surges (Greitzer, *ASME J. Eng. Power* 98, 1976; Galindo et al., *Exp. Therm. Fluid Sci.* 32, 2008): chuffs of air
  with the shaft stumbling, about 14 a second on full boost falling to 7 as the stored air bleeds away (measured: a
  15 dB beat at 9.8 Hz). The whine is the tone heard on boost (review): the turbo four is on full boost within half a
  second of flooring it, its blade tone then at 15 kHz, past the game's 22.05 kHz mix, and with the whine at a quarter of its present gain the whole turbo
  was under the engine in every third-octave band (the whine 11 dB under in its own). Now the whine is level with the
  engine in its band, +1.9 dB outside and +0.8 dB in the cabin at a shaft gain of 0.1 (`The_turbo_is_heard_on_boost`,
  through `EngineModel`'s boost, on the Delta's run-up; soundfix), and both tones fade to 0.3 with the throttle shut,
  as they leave mostly through the compressor's inlet with the air it draws. The turbo sits in the engine bay, so the
  cabin hears it through the same panels as the engine: low-passed at the body's corner (`TurboState.CabinCutoff`, 2.2
  kHz for the front-engined turbo hatch) at 0.6 of its level, crossfaded over 0.15 s (heard unfiltered, its rush stood
  11 dB over the cabin's engine at 6.4 kHz). Subtle still: on full boost the whole turbo is 12.8 LU under the engine,
  the vent's and the surge's loudest 100-250 ms 13 LU under, each standing out over the steady turbo in its own band
  (the vent +9 dB above 3 kHz, the surge +18 dB under 2 kHz). The turbo runs down with the engine in the crash replay
  (the follower holds its last revs there).
- **The limiter** (finding 12). `EngineModel.LimiterCut` says when the ignition is cut; `EngineFollower` holds the load
  through it and `GranularEngine.Cut` makes every pulse laid while it lasts an overrun pulse at the closed throttle's
  level (the overrun's grains levelled onto the run-up's trend since soundfix, item 17 (d) of the 0.19 list: the cut
  takes the level law's 7.5 dB, more where the overrun is laid past the top of its recording). Averaged over a
  hundred 60-in-100 ms cuts (twenty before soundfix, too few: the grains a run picked moved the figures), the level
  swings 18.5 dB on the turbo hatch, 6.5 on the estate and 10.8 on the V8 and falls in 2-6 ms; through the load, as in
  0.19, it swings 0.7-1.4 dB and takes 24-54 ms. The V8's pops are set off by the
  driver lifting, not by the cut. (The cut is heard as late as the voice follows the revs, its latency: a grain's
  lead, up to 45 ms for the turbo hatch's 22 Hz idle grains.) The cut is latched in `EngineModel.Torque` with the
  throttle it shuts (review): read from its countdown, which `Tick` runs after `Torque`, the step that ended a cut
  reported no cut with the throttle still shut, and the follower's held load sagged to 0.4-0.7 every few frames, a dip
  of about 2 dB with the throttle's filter closing from 12 to about 7 kHz, 0.19's blurred dip again. Driven through
  the simulated engine at 240 Hz and read at 60 frames a second, in neutral and with a car's inertia, the load now holds
  at 1.0 (`The_simulated_limiter_holds_the_load_through_its_cuts`). The 1989 mode's synthesised engine has no pulses
  to cut: it takes the throttle unheld, so it dips on the limiter as it did in 0.19.
- **Banks evened across their joins** (finding 9, `GrainBank`). Each grain's brightness (its energy above 1 kHz
  against below) and level are fitted against the firing frequency with a slope common to the bank and an intercept
  per segment (so a step between recordings cannot pass for a slope), the slope held to 0-6 dB an octave for
  brightness and 0-3 for level; each segment is then tilted (a shelf about 1 kHz, at most ±6 dB) and set (at most
  ±9 dB) onto the trend. The rendered timbre's spread about its trend across the revs falls from 3.7 to 3.0 dB on the
  turbo hatch and from 3.0 to 2.0 dB on the fleet saloon (`The_timbre_follows_the_revs_more_evenly`); it moves by a
  tenth of a dB or less on the others (the V8's spread is its recordings' own). A mean-square frequency was tried
  first as the brightness: ruled by the recordings' hiss, evening it made the turbo hatch duller still at 4700 rpm.
  Levels evened with a slope up to 6 dB an octave pushed the flat-twelve's low run-up down until pulling at 3000 rpm it
  was barely louder than its idle; at 3 they lift the fleet saloon's Bravo segment (recorded 4 dB under the Punto's)
  so that it is no longer buried by the wind at the top of its revs.
- **Loudness between cars**. After 0.19 the estate in attract was 5.5 LU louder than the flat-twelve in the cockpit
  and the V8 2.8 LU quieter still: the recordings were made at their microphones' levels, and the one cabin filter's
  130 Hz boom sat on a four's firing frequency. Each voice now measures itself when it is made, pulling at full
  throttle at 45, 60, 75 and 90 % of its redline (BS.1770, ungated), and trims its outside and its cabin path to
  -14 LUFS at full volume (`GranularEngine.ReferenceLufs`: at the 0.19 voices' mean of -15 the game came out 2.4 LU
  under 0.19's level; higher, the turbo hatch's recording starts to click); the idle is brought to 10 LU
  under that (`IdleLufs`; the saloon's idle, a Niva's recorded quietly, was 25 LU under its pulling), as far as its
  recording's sharpest edge allows without a click. Rendered in a full-throttle run-up from 40 to 95 % of the redline
  (not the trim's own holds), the eight cars sit within 1.2 LU of their mean outside and in the cabin, the V8 the
  loudest (`Every_engine_is_about_as_loud_pulling_hard`); a big engine differs in its sound (the
  V12's firing at 600 Hz, the mid engine's brighter cabin and intake), not its level. In the game (attract, 40 s,
  integrated, the whole mix):

  | Car | 0.19 cockpit | 0.19 chase | 0.20 cockpit | 0.20 chase |
  |---|---|---|---|---|
  | flat-twelve | -16.9 | -17.1 | -18.5 | -18.3 |
  | V8 | -19.5 | -19.9 | -18.7 | -18.4 |
  | hot hatch | -15.9 | -16.6 | -17.3 | -17.2 |
  | five | -15.3 | -17.2 | -16.8 | -16.7 |
  | turbo hatch | -16.0 | -18.8 | -16.1 | -16.1 |
  | saloon | -14.2 | -17.3 | -16.3 | -16.2 |
  | estate | -11.4 | -14.9 | -14.4 | -14.0 |
  | fleet saloon | -14.4 | -17.7 | -17.3 | -17.2 |

  The spread in the cockpit fell from 8.1 to 4.3 LU, from behind from 5.0 to 4.4, and the two views now agree
  within 0.4 LU on every car. What is left is the driving, not the voices: the attract driver holds the estate flat out
  92 % of the time (mean throttle 0.94), the V8 16 % (0.31; it has the power to spare), and a closed throttle plays at
  CutLevel, 7.5 dB under an open one (the engine alone, tyres, wind and traffic muted in a local run: V8 -21.0, estate
  -15.7 LUFS at -15 LUFS reference). A 1.4 driven flat out is loud and a V8 cruising is not, as on the road; pulling
  equally hard they are within 1.2 LU.
- **The load, pinned through the sound** (review). `EngineFollower` (audio library) takes the engine's throttle and
  limiter cut and smooths them as the voice wants; `EngineAudio` only passes them on, so the mapping is tested headless:
  every engine held at full throttle at 60 % of its redline gives the voice a load of 1 and its full-throttle sound,
  where the torque over 500 N m would have played the estate 7.2 LU down, the saloon 10.5
  (`Full_throttle_reaches_the_voice_as_full_load_on_every_engine`).
- **The idle** (review). The engine alone in neutral settles with its governor's throttle at 0.10-0.16, the voice's load
  at idle; the idle is 1.1-1.9 dB louder than at 0.18's load of a few hundredths and 6-13 LU under the engine pulling at
  60 % of its redline (`The_idle_governors_throttle_keeps_a_quiet_idle`).
- **Headroom at top speed** (review). Pulling at 90 % of its redline at 250 km/h, where the roar and the wind have
  stopped growing, every engine is at most 2.2 LU under roar and wind together (the fleet saloon's cabin; up to 3.7 over
  behind the flat-twelve), and in the third-octave band where it stands out most it is 5.3 dB (the hot hatch's cabin) to
  16.9 dB over them, well over the masked threshold of a tone in noise (Zwicker and Fastl, *Psychoacoustics*)
  (`The_engine_stays_audible_over_the_roar_and_the_wind_at_top_speed`). The hot hatch's cabin is the closest: its
  recording is broadband at the top of its revs, without a strong firing order.
- **Peaks** (review). Trimmed to the reference alone, three cars peaked over full scale at the game's volume of 0.9
  (holds at full throttle from 30 % of the redline to the redline: the fleet saloon at +2.8 dBFS, the V8 at +1.2 and
  the turbo hatch at +1.1, where 0.19's worst was -0.2), and the master `AudioEffectHardLimiter` (-0.5 dB) pulled the
  whole mix down with them, tyres and wind included, by up to 3.3 dB. Three things hold them now. Evening a bank no
  longer lifts a segment's peaks past the raw recording's, but for its rarest samples (`PeakMeter`: the level one sample
  in 10,000 passes; the fleet saloon's run-up reaches 0.99 with a single click where one in 10,000 passes 0.64, and a
  gain held to the click would have undone the Bravo segment's lift and left it buried by the wind again). The trims
  are held so that the voice's peaks in the trim's own renders, taken on every 5 % from 30 % of the redline to the
  redline, stay under -1 dBFS at full volume (`GranularEngine.PeakCeiling`), also but for the rarest samples. And what
  those miss (a click, a grain with a sharper edge than the renders met) is rounded off above -1.9 dBFS
  (`PeakKnee`, the trimmed peaks at the game's volume) by a tanh towards -1 dBFS, so the voice never passes it. At the
  game's volume every car now peaks at -1.0 dBFS or under, outside and in the cabin, the rounding touching at most 5
  samples in 10,000 (the turbo hatch's cabin) and none on four of the cars (`Full_throttle_peaks_under_minus_one_dbfs`);
  the loudness between cars is unchanged. The bank's peaks are pinned in `Segments_are_evened_across_the_revs`.
- **Making a voice** (review). Evening a car's banks and trimming its voice (two trim renders and the idle's) takes
  40-130 ms in a release build, 100-290 in a debug one, longer than the engine generator's 50 ms buffer: made on the
  main thread at a change of car, it ran the generator dry. `SampledEngine.Load` now reads the recordings through
  Godot's loader and makes the banks and the voice on a worker; the previous voice plays until the new one is ready.
  The fixed filters (the cabin's low-pass and boom, the tailpipe's shelf) are set once when the voice is made, only
  the throttle's filter and the intake's roar follow the load every 32 samples.
- **The Unreal version** keeps its ABI (no new export). `glp_engine_create` names no car, so `NativeApi.CreateEngine`
  finds it by the folder's name (`EngineSpec.SoundFolder`) and makes the voice as `SampledEngine` does: voiced for that
  car's body (`NativeApi.VoicingOf`, kept alike with `SampledEngine.VoicingOf`) and trimmed over its redline, with the
  evened banks and the peaks held as above (`EngineVoiceIsVoicedAndTrimmedForItsCar`; a folder no car uses keeps the
  front-engined default, trimmed over its recording). Without that the flat-twelve and the V8 were voiced as
  front-engined hatchbacks, a 2.2 kHz cabin booming at 75 Hz where theirs is 4 kHz at 132 Hz. The Unreal side still
  makes the voice on its game thread when a car is chosen (its audio thread plays the previous set until the new one
  is swapped in, so it does not run dry). To have the rest it would need new exports: the limiter's cut and the
  turbo's state in `glp_state` with a `TurboNoise` voice, and `EngineFollower` in place of its own smoothing; with
  finding 14.

### Sound, diesel6: the fleet saloon's diesel

The fleet saloon has been the E 250 CDI since fleet (dynamics audit, item 20), its OM 651 a 2.1-litre turbodiesel four
idling at 750 rpm and governed at 4,800, but it still played a petrol four (the Punto's and the Bravo's 1.4 T-Jet) and
its turbo still vented like a petrol car's blow-off valve when the driver lifted. Both are a diesel's now.

- **The recording** (`game/assets/audio/diesel4`, SOURCES.md). No CC0 or CC BY recording of an OM 651, or of any W212
  or W204 CDI, was found: the Internet Archive's CC0 `car-engines` holds no diesel; Wikimedia Commons' diesels are four
  edited YouTube tracks with their licence review pending (a Passat B5 1.9 TDI, an Audi A3, a Seat Toledo, a Chevrolet
  Cruze), none with a steady loaded run-up near the engine; Freesound's are starts and blips standing (a Mercedes
  Sprinter, an Astra CDTi, a Jumpy HDi, a 320d, two Mercedes diesels of unknown model), cabin recordings where the road
  and the wind cover the firing (a Passat, an i40 CRDi, a Master dCi), phone clips, and fastson's two drives of a TDI
  with the microphone in the engine compartment (#243773 and #243783, CC0): idling, pulling away, accelerating through
  the low gears, coasting. Those are cut. The car is not named; its idle reads as a four's (a comb of 6.5 Hz, the cam's
  turn, with its firing at 26 Hz, 780 rpm, and its second harmonic the strongest lines), and whether it is a
  common-rail or a unit-injector TDI is not said.
- **Its hiss** (`tools/clean-recording.py`). The drives were recorded at -55 to -60 dBFS, and above about 4 kHz they are
  the recorder's floor (those bands move by under 3 dB between idle and run-up). The bank tool brings every segment to
  one loudness, the floor 40 dB up with it: cut raw, the grains' sharpest steps stood 2 to 3.6 times their RMS (every
  other bank's 0.1 to 0.7), and the audio tests found clicks idling at full throttle (3.2 times the RMS around them) and
  a step of 2.7 LU where the idle hands over to the run-up in the cabin. The floor is now measured in every bin as its
  10th percentile over the file and taken away from 700 Hz up (under that the engine is always far over it), with a
  low-pass at 6 kHz, before the banks are cut; nothing moves in time, so the tool's marks are unchanged by it. The
  grains' sharpest steps are 0.3 to 0.9 times their RMS after it. The idle is #243773's, whose 1-3 kHz beats 0.54 of
  its mean at the firing rate (the knock); #243783's first idle beats 0.27 there, its band above 3 kHz hiss, and is left
  out, its second idle (about 1,000 rpm) kept.
- **The banks.** The idle at 780 and about 1,000 rpm; the run-up under load from 1,300 to 3,425 rpm in five stretches
  of the two drives; the overrun from 2,000 down to 1,150 rpm (the revs falling slowly after a change up, the fuel
  cut). The tool's tracker cannot follow a firing frequency under about 45 Hz (its 12 % search band is then narrower than
  a bin and may hold none, so the estimate sticks), so the run-up starts at 1,300 rpm and the voice crossfades the idle
  into it; one stretch is split where the tracker jumped to a partial for a few pulses. The idles are followed on their
  second harmonic (`make-engine-bank.py`'s optional harmonic field; review): followed on the firing, 150 of #243783's
  352 idle pulses were labelled 28.63 Hz, 7 to 12 % under the 32.6 its spectrum and the rest of its pulses give, and 104
  of the bank's grains spanned over 1.4 periods on a side where a pulse was missed; now its labels' median is 32.84 Hz
  and 45 grains do. Past 3,425 rpm the voice lays
  the run-up's highest pulses closer up to the governor's 4,800 (1.4 times), and lifting from there plays the overrun's
  highest pulses closer still, as on the V6 hatch. Every bank test passes (no grain over 3.3 dB under its neighbours,
  the sharpest 2.9 times its segment's median, every side 0.60 of its period or more; sweeps and idles on five seeds
  with steps at most 1.4 times the RMS around them; no ticks lifting off; steps across the revs 0.9 LU, sags 1.1; peaks
  -1.4 dBFS outside and -1.1 in the cabin, 128 samples in 952,560 rounded there; pulling as a tone; the throttle at
  idle revs 5.5 LU from the governor's throttle to full). The voicing is the saloons' (`SampledEngine.VoicingOf`: front
  engine, a 1.9 m cabin booming at 90 Hz).
- **The hum** (`The_diesel_idles_without_a_steady_hum`; review). Both drives carry a steady tone that does not follow
  the revs, 162.7 Hz idling at 780 rpm and 161.3 at about 980 (an auxiliary of the car's), the strongest line of either
  idle; cut into grains and laid at the firing rate it was chopped into a buzz, and the voice idled with 160-167 Hz 7.1
  dB over its firing line outside and 8.9 in the cabin, where the petrol cars' sit well under theirs. The idle is cut
  from copies notched at 159-166 Hz (`clean-recording.py`'s optional notch, a zero-phase second-order band-stop): 10.4
  dB under the firing line outside, 8.6 in the cabin. The run-up and the overrun keep the tone (un-notched files):
  every firing harmonic passes the band through them, and a notch would cut each as it went by; there the tone is no
  louder than the harmonics around it.
- **The knock** (`The_diesel_knocks_where_the_petrol_fours_do_not`). A diesel's combustion starts with a steep rise of
  pressure as the fuel injected into hot air ignites at once, which rings the block and the head (the clatter, strongest
  at idle and light load, where the ignition delay is longest against the little fuel burnt; Heywood, *Internal
  Combustion Engine Fundamentals*, ch. 10). In the voice: idling,
  its 1-3 kHz is -9.3 dB of the sound against the petrol fours' -17.5 (the saloon) to -29.3 (the hot hatch), and at
  1,500 rpm on a light throttle -10.8 against -15.1 to -27.6; the Punto/Bravo bank gave -29.6 and -26.5. That band
  follows its firing (its envelope folded on the firing period swings 0.97 and 0.60 of its mean). The review found
  neither figure tells a knock from hiss: white noise cut on the diesel's marks and played the same way scores -5.6 and
  -6.3 dB and swings 0.40 and 0.46, the grains' windows laying the firing's rhythm on anything. The test now also wants
  the voice 0.1 over that control and measures the knock where no grain window can make it, on the banks' own
  recordings folded on their marks: the diesel's idle swings 0.67 and its run-up 0.24, white noise on the same marks
  0.04 and 0.03, the petrol fours' idles 0.19 to 0.41 and run-ups 0.04 to 0.09 (the review: the source's idle has a 26
  Hz line 35 dB over the median of its 1-3 kHz envelope; above 6 kHz it is mostly hiss). The diesel's top is its governor's 4,800 rpm
  and its recording's 3,425 laid closer, without a petrol engine's rasp at 6,000 and over.
- **The turbo on lift-off** (`EngineSpec.LiftOff`, `TurboLiftOff`; `TurboState.Lift`, `TurboLift`). A diesel has no
  throttle plate: its load is the fuel injected, so lifting shuts nothing on the boost, the air goes on through the
  engine and the turbine, the charge falls with the exhaust's energy, and diesel turbochargers are fitted without a
  blow-off or diverter valve. The bool `BlowOffValve` (true vents, false surges) is now a three-way choice: `Vents` (the
  default, every petrol turbo, the Delta among them), `Surges`, and `SpoolsDown` (the diesel): no vent and no surge, the
  shaft runs down over its second of run-on and the whine with it. The air through the compressor, which carries its
  tones out of the inlet and makes its rush, follows the falling boost on a diesel rather than the pedal (review: with
  the pedal setting it, the whine fell 10.5 dB in the first 50 ms after the lift while its pitch had barely moved, a step
  down where the spool-down should be heard; held, the boost never exceeds the throttle, so nothing else changes and the
  petrol turbos are untouched). Driven through `EngineModel`: never venting nor surging, nothing over the steady turbo
  above 3 kHz (0.4 dB under it: the rush goes on as the boost falls) or under 2 kHz (1.5 under), a beat of 0.5 dB
  against the vent's 3.1 and the surge's 14.5, the whine's band 0.3 dB down in the first 100 ms (at most 3 allowed)
  and 10.5 a second after, its pitch from 2,499 Hz to 2,384 half a second after the lift and 2,086 a second after;
  `A_diesels_turbo_spools_down_on_lift_off_without_a_vent_or_a_surge`, `Every_turbo_engine_lifts_off_as_its_kind_does`).
- **The turbo's tones** (`TurboNoise.TonalGain`). Against the diesel's knock, loud around 2.5 kHz, its turbo's whine was
  1.7 dB under the engine in its band outside and 3.8 in the cabin, under the masked threshold there
  (`The_turbo_is_heard_on_boost`). The tones now follow the pressure ratio the turbo works at: the work done on the air
  goes as the tip speed squared (Euler), a fan's or a compressor's sound power as the fifth power of its tip speed (the
  fan laws' sound-power term, 50 log10 of the speed ratio at a fixed size, as AMCA Publication 301 applies it), so the
  tones' pressure as the work to the 1.25, against the rally hatch's turbo, on which the gains were set
  (`ReferenceBoostShare` 0.48: a gain of exactly 1, its sound unchanged sample for sample). The pressure ratios are the
  game's own, from each engine's torque without boost against with it: the diesel's 2.5 bar absolute against the
  Delta's 1.9 gives 4.1 dB, and its whine is 2.3 dB over the engine in its band outside, 0.2 in the cabin. (The OM
  651's 150 kW tune is described as running just under 3 bar, which the same law would make 6.3 dB; the 2.5 is kept so
  the sound's boost is the one the torque model makes.) (A faster shaft for the diesel's smaller high-pressure
  stage, its whine at 3.7 kHz, was tried first: the knock is as loud there, and the whine fell 1.4 and 4.5 dB under.)
- **In the game** (hidden, silent 40 s `--write-movie` attract runs at 30 fps, dry, cabin and chase, ffmpeg's `ebur128`
  over the whole mix, transients counted as the lift-off test counts its ticks): the fleet saloon -13.3 LUFS in its cabin
  and -13.4 from behind after the review (-13.4 and -13.3 before it, -13.3 and -13.3 on its petrol banks), the other
  cars -11.7 (the estate) to -16.0 (the V8), the voice alone trimmed to the reference as every car's (-14.1 LUFS pulling
  outside, -13.8 in the cabin, among the others' -13.5 to -14.3; `Every_engine_is_about_as_loud_pulling_hard`). Its
  loudness range is wider, 6.9 and 7.5 LU against 5.6 and 5.7 (the voice held flat out runs from -18.4 LUFS at 800 rpm,
  -19.6 before the review's re-cut idle, to -12.7 at 4,800; the review measured it 5.7 LU under its top held flat out
  at 1,440 rpm, where the petrol fleet banks were 3.2 under and the estate, the hot hatch and the flat-twelve 6.3 to 7.1, within the
  tests' limits but worth a listen: a diesel is usually strong near its 1,600-1,800 rpm torque peak); peaks -0.5 dBFS
  in the cabin and from behind (the whole mix's; -0.5 and -0.7 before the review); sharp transients none over 12 times
  the level around them, 20 ms drop-outs none. At top speed its cabin is 0.3 LU over the roar and the wind, 2.2 under
  on the petrol banks. The rally hatch, the only other turbo car, renders sample for sample as before in its cabin.
- **The Unreal version** reads the "diesel4" folder in its bank table (`GlpAudio.cpp`); it has no turbo voice yet.
- Left: the recording's car is not named and is not an OM 651; its run-up ends at 3,425 rpm and its overrun at 2,000,
  so the top of the diesel's revs is its highest pulses laid closer; the drives' steady 161-163 Hz tone (an auxiliary
  of the car's) is notched out of the idle only and stays in the run-up and the overrun; the tool's tracker cannot follow
  a firing under about 45 Hz on the firing itself (the idles are followed on their second harmonic). A recording of an
  OM 651 pulling to its governed speed under load would serve better (roadmap).

### Sound, cars13 stage 1: the five new engines' banks

Five cars join the catalogue in cars13 (8-12: the 458 Italia, the Bravo HGT 2.0 20V, the Bravo 1.6 16V, the 2018 Leon
Cupra, the first BRZ). Their engines' banks were cut first, in their own folders, before the catalogue entries that
name them exist (stage 2 sets each engine's `SoundFolder`): `v8flat`, `five`, `four16v`, `tsi4` and `boxer4`. Sources,
segments, rebuild commands and every recording checked and passed over are in `game/assets/audio/SOURCES.md`.

- **What each plays.** The 458: its own run-up (`3 Ferrari 458` of the CC0 item, to 8,830 rpm, the stretch the 570S's
  `v8` has borrowed since 0.9 and two more) and a Ferrari 360's flat-plane V8 at idle (Freesound #241083, CC0): a
  flat-plane V8 fires evenly every 90 degrees like two inline fours, its strongest lines the firing and half of it, as
  the 360's idle shows. The HGT: the five-cylinder banks of 0.20.0 restored from the history and re-cut (a Volvo five's
  idle, CC BY 4.0, low-passed at 1.5 kHz; the S2000's run-up laid at a five's firing, `GranularEngine.FivePattern`); the
  S2000's segments end before the crackles the tool now warns of. The 1.6 16V: the CC0 item's MX-5, a mild sixteen-valve
  four of the Bravo's kind (no Fiat 1.6 16V was found), its loaded run-up only (its blips standing left out). The Cupra:
  a Golf R's lap of Kyalami with the microphone at the exhaust (#268622, CC0), the same EA888 2.0 TSI; the run-up from
  its full-throttle stretches. The BRZ: an FR-S on a DynoJet (#183654, CC0) and a GT 86 idling on the same author's
  dynamometer (#191194, CC BY 4.0), the FA20 of all three. The FA20's exhaust headers are of equal length: its idle
  carries the firing's harmonics only, not the EJ boxers' beat at the cycle rate (an EJ Forester's idle has a comb every
  8.6 Hz), so the voice plays it as an even four, which it is.
- **Labels checked against the spectrograms.** Every segment's firing labels were laid over its spectrogram: the cut
  first made had labels on other lines in four places (the FR-S's first six seconds stuck at 57 Hz while the engine ran
  from 68 to 95 Hz, and from 16 to 19 s 15 to 20 % under its firing; two of the Golf R's overrun stretches on the
  crank's line at half the firing, labelled 1,060-1,560 rpm where the revs fell from 6,250 and 5,600; a stretch of the
  360's idle wavering between 860 and 1,300 rpm). The FR-S's firing line runs among lines at 1.5 and 2.5 times it and a
  steady tone of the room; it was found from the top of the run (250 Hz at the 7,500 rpm limiter) and anchored every few
  seconds down to 2,000 rpm. Cut in six stretches searched over bands up to 50 % wide, it was still mis-tracked in
  places (the cars13 review, against those anchors: 8 % of its 2,866 labels more than 5 % off, 3.9 % more than
  15 %, at worst 45 %; 24 and 28 jumps of over 3 % from one pulse to the next in two stretches, 0 to 5 in every
  other run-up): it is re-cut in eighteen stretches of one to one and a half seconds, each band about 7 % either side
  of the anchors, the room's 188 Hz tone kept out of them and 5.0-5.3 s, where the firing line is 7 to 20 dB under that
  tone, left out. 0.5 % of its 2,830 labels lie more than 5 % off, none more than 8 %, with 8 such jumps in
  all. It plays as it did: the bank-level figures 5.7 dB, 2.1 times and a side of 0.601 at worst; the tone's share held
  every 100 rpm from 1,900 to 7,400 a mean -2.1 dB (-2.0 before), at least -3.4; the level across the revs stepping 1.0
  LU at most outside (2,100 rpm, as before) and 0.8 in the cabin (3,900, 0.6 before; a re-cut with a join at 6.0 s
  stepped 1.13 LU at 2,500 rpm outside, the joins at 6.5 s 1.01).
- **The boxer's run-up levelled** (`tools/level-recording.py`, new). The dynamometer run rises 9 to 13 dB an octave (8.8
  within the six stretches first cut, 12.8 by one-second RMS over the run), and the voice levels grains onto a trend
  whose slope is the one within the recordings' stretches, held between 0 and 3 dB an octave (`GrainBank.LevelTrend`):
  the boxer's took the 3 in full. So do the flat-twelve's, the estate's and the diesel's (5.6, 11.4 and 6.0 dB an octave
  within their stretches; the other run-ups' are 0 or nearly, the hot hatch's 0.3), but the boxer's idle lies the
  furthest under its run-up of the four (2.75 octaves under the run-up's median firing, against 2.40, 1.72 and 1.01).
  Cut as it was, the trend fell 6 dB from the run-up to the idle's revs, the idle pulling was held 6 dB down (the most
  `LevelGains` moves a grain), and full throttle at the idle revs was 0.9 LU over the governor's (the tests' floor is
  2.5, the other cars 3 to 12). Levelled over one-second windows, the run's pulses against their neighbours untouched,
  its slope within the stretches is 0 (-0.4 dB an octave, held to 0) and the rise is 3.9 LU (4.1 before the re-cut
  above).
- **The HGT's idle low-passed.** Recorded under the bonnet, the Volvo's idle ticks (steps 3 to 5 times the RMS around
  them), and it was 29 dB brighter than the S2000's run-up where it hands over: the voice clicked idling at full
  throttle (3.0 times the RMS) and stepped 2.0 LU at 1,100 rpm outside, 1.6 at 1,400 once the ticks were gone at 6 kHz
  (3 LU over the run-up outside, 3 under it in the cabin). Low-passed at 1.5 kHz it steps 0.7 LU at most.
- **Measured** with the bank-level and voice-level checks of `EngineVoiceTests`, run on the banks by folder with each
  car's cylinders, redline and idle (the 458 eight, 9,000, 1,000 rpm; the HGT five, 7,000, 850; the 1.6 four, 6,500 and
  6,800, 850; the Cupra four, 6,800, 800; the BRZ four, 7,400, 700), its voicing the game's (mid-engined for the 458,
  front for the others): no grain more than 7.6 dB under its neighbours, the sharpest 3.9 times its segment's median,
  every side 0.60 of a period or more (`Every_bank_is_cut_without_drop_outs_or_crackles` and
  `Every_grain_reaches_most_of_a_period_either_side` now take every bank folder on disk, wired to a car or not); the
  sweep from idle to the redline and off, five seeds, outside and in the cabin, and ten seconds idling at the governor's
  and at full throttle, without a click or a drop-out; the level held every 100 rpm from idle to redline stepping 1.1 LU
  at most (the Cupra's cabin at 3,000 rpm, where its run-up's recording begins) and sagging 1.1 LU at most (the 458's
  cabin at 6,700 rpm); opening the throttle at the idle revs 3.9 (the 458 and the BRZ) to 8.0 LU (the HGT) louder, each
  step 0.5 LU or more; pulling a tone (the harmonics' share at least -4.0 dB, the 458 at its 9,000 rpm redline and the
  BRZ at 6,660 rpm); peaks -1.3 dBFS at worst (the Cupra's cabin, 280 of 952,560 samples rounded off; 3 outside), 5 in
  the BRZ's cabin, none on the others; lifting off from the redline, at most 8 ticks in five seconds (the HGT's cabin 13
  to 19 before its overrun gained 6.0-6.6 s, now 0 to 1); -13.9 to -14.6 LUFS pulling hard (the reference -14); the idle
  8.3 LU or more under pulling at 60 % of the redline.
- Left: three of the five lean on the CC0 Internet Archive item `car-engines` (the 458's run-up and overrun, the HGT's
  run-up and overrun, all three of the 1.6's banks), whose CC0 is only its uploader's word: it names no recordist, its
  tracks carry a compilation's track numbers, one is a video's soundtrack, the S2000's and the MX-5's are mastered
  (peaks over full scale, the MX-5 idling within 1 dB of its pull), and the 458's has the form of the ringtones Ferrari
  gave away in 2009 (SOURCES.md lists the evidence). No further bank is cut from it; the 1.6's, which has no other
  source, is the first to replace, and the search after the review found no described recording of a sixteen-valve four
  pulling through its revs (a Mazda 323 under the bonnet, CC BY 4.0, pulls only to 3,500 rpm). The 458 and the 570S
  share the 458's run-up recording (the 570S's own M838T is in the same item's McLaren tracks, so not used); the Cupra's
  lap never pulls hard under 2,900 rpm, so under its run-up the voice crossfades from the idle; the 1.6 is an MX-5's
  four, not Fiat's; the HGT's run-up is still a four laid at a five's firing; the BRZ's idle is 807 rpm (the car idles
  at about 700); `--car=five` names the Delta, whose model files carry the key of the five-cylinder hatch it replaced,
  not the HGT whose banks are in `five/`: stage 2 gives the HGT a short name of its own (or renames the folder) when it
  wires it. Wikimedia Commons' YouTube recordings of a 458 Speciale, an F430, a Cupra León, a BRZ and a Golf GTI (CC BY
  3.0, their licence review pending) are a later pass's. Stage 2 wires the folders to the cars, where the catalogue's
  voice tests pin these figures.

### Sound, cars13 stage 2: the banks wired

The five engines name their banks (`EngineSpec.SoundFolder`): the 458 `v8flat`, the Bravo HGT `inline5`, the Bravo 1.6
`four16v`, the Leon Cupra `tsi4`, the BRZ `boxer4`, and `GlpAudio.cpp`'s bank table the same. The HGT's bank was cut as
`five/` and is renamed `inline5/` (the .wav, .json and .import moved; the imports' paths regenerated, their uids kept):
`--car=five` is the Delta's short name, its model files carrying the key of the five-cylinder hatch it replaced, and
keeps selecting the Delta. Every car has a bank of its own again, so `glp_engine_create`'s folder names one car each;
`glp_engine_create_for_car` stays the host's call.

- **Voicing.** The 458 is mid-engined as the V8 is (`VoicingOf`, here and in `NativeApi`; `EngineVoiceTests` voices
  `v8flat` so too, where it took the front-engined default while no car played it); the BRZ, a coupe with a boot, takes
  the saloons' 1.9 m cabin, which item 23 left until its own bank landed (the audio tests voiced `v6` as the R32 it
  first belongs to, a hatchback).
- **The diesel's knock.** The BRZ's exception (`BorrowedSixBanks`) is gone: on `boxer4` it is compared like every petrol
  four, 6.6 dB under the diesel's 1-3 kHz idling (6 needed) and 5.1 at 1,500 rpm (3.5). On the banks' own recordings
  the Cupra's idle swung 0.90 on its marks against the diesel's 0.67, but its 1-3 kHz is 44.8 dB of its sound under
  the rest (the diesel's 9.0): a lavalier at a Golf R's exhaust records almost nothing there idling, and a ripple 36 dB
  under the diesel's band is no knock anyone hears. The comparison on the marks now leaves out a bank 30 dB or more
  under the diesel's band; every other petrol bank is compared (the Bravo 1.6's idle, 25 dB under, the furthest), and
  every petrol voice is held to the share test whatever its bank (the Cupra's -45.7 and -32.3 dB). The Cupra's run-up
  swings 0.22 on its marks against the diesel's 0.24, the nearest of the petrol run-ups.
- The v8flat, inline5 and four16v banks are wired as they are; they are to be cut again from recordings whose recordists
  describe them (stage 1's provenance notes; done in audio2, below).

### Sound, cars13 audio2: three engines from described recordings

The 458's run-up and overrun, the Bravo HGT's three banks and the Bravo 1.6's three were the Internet Archive item
`car-engines`' (`3 Ferrari 458`, `honda s2000` laid at a five's firing, `mazda MX 5`), a likely compilation whose CC0 is
only its uploader's word (stage 1's notes; `game/assets/audio/SOURCES.md`). They are cut again from recordings whose
makers describe the car, the engine and the recording, so no bank of the five cars13 engines uses the item. Sources,
licences, credits, segments, rebuild lines and the candidates passed over are in `SOURCES.md` and `THIRD_PARTY.md`.

- **What each plays.** The 458: an F430 Scuderia's F136 4.3 flat-plane V8 (the 458's engine family) pulling through a
  gear from 2,020 to 8,770 rpm on a chassis dynamometer and coasting down after it (FiExhaust's video, its soundtrack on
  Wikimedia Commons, CC BY 3.0); the 360's idle (CC0) stays. The HGT: a real five, an Audi TT RS's 2.5 turbocharged five
  recorded with a microphone on its rear number plate (Speedster Turbo Berlin, Commons, CC BY 3.0): its idle, two pulls (1,560
  to 6,590 rpm) and three lift-offs. The 1.6: an MX-5's stock B6-ZE 1.6 sixteen-valve four with a Fujitsubo header and a
  custom exhaust, its idle and four full-throttle pulls through three gears (Ride in Japan, Commons, CC BY 4.0: published
  after YouTube's Creative Commons option became 4.0 on 1 August 2025, where Commons tags it 3.0), and a Golf V 1.6 FSI's
  revs falling after five run-ups for its overrun (Soundholder's recording library on Freesound, CC BY 3.0; 1,460 to 5,990
  rpm, where the MX-5's one lift-off spans 3,630 to 5,620). Every file was fetched from its page with a descriptive
  User-Agent and matched the research's copy by SHA-256; the segments start from the research's plans (half-second
  stretches searched about 5 to 7 % either side of the line).
- **Firing lines confirmed** by the spacing of the lines around them and on the cut banks by their cycle's orders (each
  segment resampled on its own labels, the firing at order k = cylinders; a four-stroke engine's sound repeats once a
  cycle, so labels at twice the firing would put its lines on the half orders and at half the firing would empty the odd
  orders): whole orders 12.4 (the 458's run-up), 15.5 (the HGT's) and 22.3 dB (the 1.6's) over the half orders, the
  strongest at the firing and its harmonics, and the revs where an engine's are (8,770 rpm at the top of the F136 E's
  pull, 6,590 under the TT RS's limiter, the MX-5 idling at 830 rpm and changing from second to third at the B6
  gearbox's ratio).
- **Levelled where the checks failed.** The F430's pull and the TT RS's two pulls rise within their half-second
  stretches, and the voice's trend took 1.9 and 3 dB an octave (`GrainBank.LevelTrend`): full throttle at the idle revs
  came out 0.5 LU under the governor's throttle on the 458 and 2.3 over on the HGT (2.5 asked), and the 458 stepped 1.2
  LU at 2,100 rpm. Levelled over half-second windows (`tools/level-recording.py`, as the BRZ's run is over one second;
  one-second windows barely moved half-second stretches), and the 458's run-up started at 2.5 s past the first half
  second of the throttle opening (its darkest, 3.2 dB under the bank's trend of brightness after the largest tilt):
  the slopes 0.2 and 0 dB an octave, full throttle at the idle revs 3.4 LU over the governor's on the 458 (3.9 on its
  old banks) and 7.9 on the HGT (8.0), the 458 stepping 1.0 LU at most.
- **The 1.6's idle cleaned.** Its 1-3 kHz is steady (it swings 0.12 on its marks, white noise 0.09) and 13 dB of its
  sound under the rest; cut into grains a steady band is laid at every firing like a knock, and the diesel's test held
  the 1.6's idle 4.8 dB under the diesel's band (6 asked). The file's floor taken out from 700 Hz up
  (`tools/clean-recording.py` with its defaults, the idle's copy only): 21.9 dB under, and nearer its run-up in timbre.
- **The diesel's knock test compares the run-ups no more on their marks.** The MX-5's run-up rasps at every firing
  under full load (its 1-3 kHz swings 0.70 on its marks; the diesel's run-up 0.24, the other petrol fours' 0.04 to 0.22):
  a free exhaust's rasp, which a knock is not; a knock is the combustion's, plainest at an idle and a light throttle,
  where the test still holds every petrol four's voice under the diesel's (the 1.6's 21.9 dB idling and 4.6 at 1,500 rpm
  on a light throttle) and every petrol idle's swing on its marks under the diesel's. The Golf's run-ups, the one
  alternative, swing 0.05 but played 1.5 dB under the diesel at 1,500 rpm (3.5 asked).
- **The HGT's idle is its own.** The TT RS's (71.5-76.0 s, settled at 1,090 rpm after a coast-down) against the Volvo's
  of 0.9-stage 2: both pass, the TT RS's stepping and sagging less in the cabin (0.5 and 0.8 LU against 0.7 and 1.1), and
  it is one engine with its run-up (its grains' third-octave spectrum 5.2 dB RMS from the run-up's lowest revs, the
  Volvo's, low-passed at 1.5 kHz, 21.5). The Volvo is not shipped.
- **FivePattern removed.** The five's weights per cylinder (`GranularEngine.FivePattern`, 1.00, 0.82, 0.94, 0.78 and
  0.90, for the S2000's four laid at a five's firing) did nothing audible on the TT RS's banks: the ring divides the
  shares of overlapping grains back out (`GrainRing.Take`), so they only tilted the crossfade between neighbours, and the
  voice with them differed from the voice without by 27 to 32 dB under its level. The five's beat is the recording's:
  held every 500 rpm from 2,500 to 6,000 the voice holds the cycle's orders that are not the firing's 6.2 dB over the
  half orders, the recording 7.8 (`EngineVoiceTests.The_five_beats_as_its_recording_does`, within 2.5 dB). A
  recording of another engine laid at a five's firing would need a gain per pulse outside the ring.
- **Measured** (`EngineVoiceTests`, `DieselCharacterTests`): the three engines' banks hold no grain more than 6.3 dB
  under its neighbours (the 458's overrun), none sharper than 2.1 times its segment's median, every side 0.60 of a period
  or more, and the tool warns of no crackle; all fifteen banks of the five cars13 engines rebuild from `SOURCES.md`'s
  lines to the byte. Held every 100 rpm at full throttle the level steps 1.0 LU at most (the 458's cabin at 1,100 rpm, as
  before) and sags 1.5 at most (the 458's cabin at 6,900; 1.1 before; the 1.6's 1.2 outside at 1,700, 0.9 before); full
  throttle at the idle revs 3.4 (the 458), 7.9 (the HGT) and 4.8 LU (the 1.6; 6.8 before) over the governor's, each step
  0.4 LU or more; peaks -1.2 dBFS at worst (the 1.6 outside, 73 of 952,560 samples rounded off; the HGT 13 in its
  cabin, the 458 none); pulling hard -14.0 and -13.7 LUFS (the 458 outside and in the cabin), -14.0 and -13.9 (the HGT),
  -15.1 and -14.3 (the 1.6, its peaky run-up holding its trim down, within 1 LU of the cars' mean); the sweep and the
  idles over five seeds without a click or a drop-out (steps at most 1.8 times the RMS around them, the deepest 10 ms
  11.2 dB under); lifting off from the redline one tick at most in five seconds (the 1.6 outside 2 to 8 before); the
  tone's share at least -3.5 dB (the 458 at its redline); the diesel's band 21.9 dB over the 1.6's idling and 4.6 at
  1,500 rpm. In the game, hidden and silent 40 s movies
  (`--write-movie`, 30 fps, the attract lap, cabin and chase) of the three cars against the same runs on cars/stage2,
  measured with ffmpeg's ebur128 and scanned for clicks, drop-outs and ticks: -15.5 and -16.0 LUFS (the 458 in its
  cabin and from behind; -16.1 and -15.5 before), -13.6 and -13.7 (the HGT; -13.7 and -13.6), -13.8 and -14.8 (the
  1.6; -13.7 and -13.9); true peaks -1.3 dBFS at worst (the 1.6 from behind; -2.1 before); no click (no step 3 times
  the RMS of the 5 ms around it: the sharpest 1.6 times, a step of 0.49 in the 458's launch from behind, its loudest
  passage) and no drop-out (the deepest 10 ms 6.6 dB under the 210 ms around it); ticks 0 or 1 but the 1.6 from
  behind, 7 in 38 s (none before: the MX-5's exhaust rasps at every firing; four at the launch, 2.5-2.8 s, and
  three later),
  where the HGT from behind ticked 51 times on stage 2's S2000 banks and does not now.
- **Caveats.** The F430 track is held by a limiter (242 decoded samples past full scale, the coast-down within 3 dB of the
  pull) and the car wears an aftermarket valved exhaust on a dynamometer: louder and rawer than a stock 458. The TT RS is
  a 2.5 turbocharged five standing in for the HGT's 2.0 naturally aspirated five: the same firing, a deeper sound and a
  turbo's hush; its idle, 4.5 s long, idles 22 % over the HGT's 850 rpm. The MX-5 stands in for Fiat's 1.6 16V, with a
  sports header and exhaust and a processed soundtrack (its driving held within 2.4 dB); its overrun is another engine,
  a Golf's. Commons' licence reviews of the three tracks are still pending (their YouTube licences were read on the
  videos' pages, 2 November 2026). The shipped banks of the V8 (`v8`), the flat-twelve, the hot hatch, the rally hatch
  and the V6 hatch still relied on the archive item then; they moved in banks2 (below).

### Sound, banks2: the five first cars' engines from described recordings (0.27.0)

The V8's run-up and overrun, the flat-twelve's, and the hot hatch's, the rally hatch's and the V6 hatch's three
banks each were the Internet Archive item `car-engines`' (`3 Ferrari 458`, `15 Lamborghini Countach`, `honda civic type
r`, `25 Nissan Pulsar`, `Jaguar f-type s`: a likely compilation whose CC0 is only its uploader's word, stage 1's notes),
the last banks of the game cut from it after cars13 audio2. They are cut again from recordings whose makers describe the
car, the engine and the recording, so no bank uses the item, and the drones' loop, which was the flat-twelve voice on
the Countach bank, is rendered again on the new one. Sources, licences, credits, segments, rebuild lines and the
candidates passed over are in `game/assets/audio/SOURCES.md` and `THIRD_PARTY.md`.

- **What each plays.** The V8, drawn as a McLaren 570S: the 570S's own M838TE pulling twice to its 8,500 rpm limiter
  on a catless Fi Exhaust system (FiExhaust's video, Wikimedia Commons, CC BY 3.0), a 458 Speciale held against its
  9,000 rpm limiter at a show for the top the game's 9,000 rpm redline asks for (TvdL Films, Commons, CC BY 3.0), and
  the 540C's revs falling after its blips for the overrun (FiExhaust, Commons); the Maserati's idle (CC0) stays. The
  flat-twelve: an F12tdf's 6.3 V12 accelerating hard on the road, 2,350 to 5,690 rpm, and lifting off (Supercars1989,
  Commons, CC BY 3.0); its Lamborghini idle (CC0) stays. The hot hatch: a 1982 Alfa Romeo Spider 2000's two-litre
  eight-valve twin cam revved standing at its exhaust, two blips for the run-up and their falls for the overrun
  (Luciano's Classics, Commons, CC BY 4.0 at source), and a 1990 Volvo 240's 2.3 eight-valve four idling outside with the
  bonnet closed (j_soundeffects, Freesound, CC BY 4.0). The rally hatch: an Audi S1's 2.0 TFSI through first, third and
  fourth gears of a 0-240 km/h run (Calvin MotorSport, Commons, CC BY 3.0), and a Fiat Coupé 2.0 16v Turbo, the
  Integrale's engine family, idling and lifting off at the end of its 0-200 run (Witu Tech, Commons, CC BY 3.0). The V6
  hatch: a 3.2 V6, a Honda J32 driven fast with a lavalier above its exhaust, for the pull (1,570-5,160 rpm), the coast
  after it and the idle (EvanBoyerman, Freesound, CC BY 4.0). Every file was fetched from its page with a descriptive
  User-Agent (the Commons files' SHA-1 checked against their pages) and its video's licence read from YouTube's metadata.
- **Firing lines.** In the S1's and the Coupé's tracks the strongest line is twice the firing (the fourth crank order),
  told from the pairs of lines and the revs they give in each gear; the S1's run-up is tracked on it with the bank
  tool's harmonic field at 2 (the field now takes a fraction too, for a half order; unused). Quiet idles whose firing
  line is weak (the Volvo's, the Coupé's) are tracked on their second harmonic over a band wider than a frame's bin:
  over a narrow band the tracker stuck at its seed, every label 27.0 Hz, and the voice took the Volvo idle's top for 810
  rpm, under the hot hatch's governed idle of 930, where it was already across into the run-up's pulses, 5 to 6 LU
  louder than every other idle (the voice's idle, governor and throttle tests).
- **Choices the tests made.** The Acura's run-up starts at 2.3 s (1,570 rpm), past the throttle's dull opening, and is
  levelled over half-second windows (from 0.8 s, or unlevelled, the level at the idle revs did not rise with the
  throttle); its gear-change lifts are out of the overrun (14 to 28 ticks in five seconds lifting off from the
  redline; none on the coast alone); its idle is 3 s of its steadiest stretch (the wider 6 s stepped the level 1.2 LU
  at 1,400 rpm). The Spider's blips start at 2,620 and 2,920 rpm (from their first soft pulses the level fell with the
  throttle at the idle revs) and the hold at 3,540-4,460 rpm is out (a step of 2.6 LU at 3,500 rpm); the Spider's own
  idle is out for the Volvo's: at the exhaust it is the brightest idle of the catalogue (1-3 kHz -5.0 dB of its sound,
  where the diesel test asks every petrol four's idle 6 dB under the diesel's -9.3). The S1's fifth gear and its second
  run are out (the evening did not even the timbre, the limiter's cut fell in 12 ms, a step of 1.8 LU at 4,600 rpm and
  an overrun as bright as the run-up); the Coupé's lift-off is low-passed at 4 kHz (at 6 kHz it was 2.9 dB under the
  run-up above 2 kHz, 3 asked). The 570S's labels past its limiter followed the half order and are cut off; cut to
  8,560 rpm alone the voice held -6.0 dB of the sound as a tone at 9,000 (-4.77 asked), with the Speciale's ten
  quarter-second stretches it passes.
- **Measured** (the audio tests, every one green on the final banks): the bank-level checks of every folder (no grain
  12 dB under its neighbours, none four times as sharp as its segment's, every side 0.6 of a period or more), the
  catalogue's voice checks on the five cars (full load reaching the voice, a quiet governed idle, the tone to the
  redline, the limiter's crisp cut, the level following the revs within 1.2 LU a step and 2.1 LU a sag, the throttle
  raising the level at the idle revs, lifting off without a run of ticks, the overrun quieter than the throttle in every
  band at the same revs, the firing frequency at idle and pulling, the diesel's knock over every petrol four's).
- **Caveats.** The flat-twelve is a 2016 V12, not a flat-twelve (no usable recording of one was found: the Commons
  Testarossa is a phone at a distance, the Countach an idle and a departure); the hot hatch's idle is another eight-valve
  four's than its run-up; the rally hatch's run-up is a modern turbo four, its idle and lift-off the Integrale's own
  family; the V6 hatch's run-up ends at 5,160 rpm and the voice lays its top pulses closer to the R32's 6,600 (1.3
  times; the E46 M3's S54 on Freesound, #415276, is the candidate if that is ever raised); the V8's top is a flat-plane
  458 Speciale's beside a cross-plane 570S's pulls. Commons' licence reviews of the seven tracks are pending (their
  YouTube licences were read on 3 November 2026). The Unreal version's bank table is unchanged (the folders are).

### Cabin daylight for the model cabins (0.28.0)

The owner reported some cockpits too dark to see (3 November 2026). Measured on hidden screenshots of every car's seat
view six seconds into the attract lap (the share of pixels under 16 of 255 in the lower 55 % of the frame): the lofted
cabins (the two Bravos, the Leon) 11-15 %, every car drawn with a model's cabin 28-55 % (the W212 55, the R12 51, the
205 48, the BRZ 46, the Delta 43, the VAZ 40, the 458 35, the SAAB 30, the 570S 28, the Esprit 21). The cause: the two
soft lights that stand in for the daylight bouncing round the cabin (`BuildCabinLight`, 0.9.1) were built only by the
lofted interior; a model with a cabin of its own (an Eye in the prepared scene) got none, and was lit by the sky's
ambient alone under its roof, exactly what 0.9.1 had fixed for the lofted cabin. The lights are now placed from the
eye point, the lofted style's or the model's (`CarView.Model.cs`, in the body's frame at the road's height), and every
model cabin has them: 5-22 % after (the R12 45: its dashboard is the model's black plastic, lit now but dark by
colour; the 458 11, its header still low over the screen, open item 1). The lofted cabins are unchanged (within 0.5 %).

### Start-up time (U3, 0.29.0)

The owner's "the game opens very late" (22 October 2026), measured on the shipped 0.28.0 build, hidden and silent, to
its first frame: 9.4 s warm, 58 s the first time after an install (the engine compiling its shaders;
not the game's to shorten). `--perf-log` now stamps every stage of `Bootstrap._Ready`, the props by kind, the world
builder's stages and the session's parts (`StartMark`, `WorldBuilder.Stage`). In the editor build the start took 13.6 s:
the seven cows 7.2 s (a second each: `CowBuilder.Paint` paints a 768 x 384 hide from three-octave value noise a pixel at
a time, on one core), the drones 2.2 s (`BuildDroneViews` loads the five everyday cars' models one after another,
0.4 s each), the world 2.1 s (the terrain 1.0, the roads 0.5), the track 0.3, everything else under 0.15.

- The hide's rows are painted in parallel (`Parallel.For` over the rows of each tube; every helper is a pure function
  of its arguments and each row writes its own stretch of the atlas, so the hides are the same to the byte): the seven
  cows 0.8 s.
- The models of the player's car and of the traffic's five everyday cars are asked of Godot's loader thread at the top
  of `_Ready` (`CarView.RequestModels`), before the world is built on the main thread, and taken from it when a car is
  built (`LoadModelScene`: a path the thread has, or is still on, comes from `LoadThreadedGet`, which waits; any other
  is loaded as before). The drones then take 40 ms. The showroom's own threaded preload is untouched.
- Editor build 13.6 s to 7.1 s; the shipped build 9.4 s to 8.2, its first frame at 4.8 s warm.
- Left: the terrain's refinement and mesh (1.0 s; its heights are already sampled in parallel), the roads (0.5 s),
  and the first start's shader compilation.

### Dials in the wheel's opening (0.30.0)

Open item 9: the live instrument panel partly behind the steering wheel in four model cabins, seen on the hidden
seat-view shots taken for 0.28.0 (the R12's speedometer behind the rim's left side, the W212's tachometer behind its
right, the Delta's gear readout and the top of its cluster under the rim, the Golf's cluster small with its top
clipped). The preparation puts the panel on the named instrument mesh's face, or where the line of sight over the hub
meets the dashboard, and on these models that is not where the wheel's opening is: the R12's instruments sit 8.5 cm
right of its wheel's axis; the W212's are recessed in tubes whose bezels occlude as well; the Delta's wheel is part of
the dashboard mesh, modelled turned and to the right of the eyes, so its opening lies low and right of the model's own
instrument recess; the Golf's is simply small from 0.68 m. Fixed with the entries' `dials` corrections alone
(`tools/car_models.json`: offset in the car frame and width), each read off a shot and checked on the next:

- R12 (estate): 4.5 cm left, 1.2 cm up and 3 cm nearer the eyes (in front of its bezels), 25.5 cm wide.
- W212 (fleet): 1.2 cm left and 2.5 cm nearer, 24 cm wide (2 to 3 cm higher put it under the binnacle's cowl).
- Delta (five): 5 cm right, 5.5 cm down and 4 cm nearer, 24 cm wide.
- Golf R32 (turbohatch): 1.3 cm down, 30 cm wide.

Both dials and the gear readout are in the opening on all four now (shots six seconds into the attract lap, the wheel
straight); the Delta's odometer line is under the dashboard's lip. The record `tools/car_cockpits.json` moved with
them; the other cars' models are untouched.

### Start-up time, the world and the cows (U3, 0.31.0)

The shipped 0.30.0 build, hidden, to its first frame: 4.4 s, of which the world 2.1 s (the terrain 1.3 s), the props
0.8 s (the seven cows 0.5 s). Measured on the Release export: the editor's Debug assemblies are unoptimised and put
the terrain's cost in the wrong places. Stage marks inside `BuildTerrain` (`--perf-log`) showed the coarse grid's
sampling 316 ms, the point index 152 ms, the vertices' heights and normals 162 ms, the colours 56 ms.

- `TerrainField.Height` looked in 25 cells round every point; the 180,000 coarse points lie mostly far from every
  road, where all 25 are empty. A set of the cells within reach of a frame is asked first: 316 ms to 30 ms, the
  same heights.
- A fine vertex's normal sampled the field four more times at its lattice neighbours, which are mostly fine vertices
  themselves or coarse corners already in the grid: the raw heights are sampled once per point and shared, the ring
  of neighbours outside every refined cell sampled into a side table (380,000 field reads to about 100,000). The
  normals differ from before only by float rounding of the neighbours' coordinates.
- The point index was one dictionary for every point, grown by doubling; the coarse corners are a regular grid and
  are indexed by an array now, the dictionary holds the 76,000 fine points: 152 ms to 45 ms.
- The colour pass's field reads (`Base`) run in parallel; the noise object and the mesh builder stay on the main
  thread.
- The cows are built before the world (their bodies 20 ms each) and their hides paint on the thread pool while the
  world is built; `CowBuilder.FinishHide` makes the texture when the cow joins the tree, in the props' order. The
  random draws keep their order, so every cow is as it was: the cows 501 ms to 148 ms on the main thread.
- The road rows are listed first and the wear under every station (five field reads a station, 136,000 stations)
  is read side by side before the builder is fed: the three roads 193 ms to 151 ms.

The terrain 1289 ms to about 320 ms; the first frame 4.4 s to 3.0 s (three runs: 3.05, 2.98, 3.05 s). A seat-view
frame of the 570S differs from the 0.30.0 build's by no more than two runs of 0.30.0 differ from each other. Left:
the engine's own start before `_Ready` (1.1 s), the sky, far ground and mountains (240 ms), the roads' remaining
serial part, the shoulders (80 ms), the grass and the forest (170 ms), and the first start's shader compilation.

### The BRZ's wheel-arch liners and rear plate (open item 2, 0.32.0)

The 86 model's arches are open behind the tyres and its plate recess is empty (cars13). Two more things the prepared
scene can name, for any model that lacks them (`tools/car_models.json` "liners": true, "plate": {centre, size}):
`Liner0..3` at the hubs, scaled to the liner's outboard reach, radius and inboard reach, and `Plate` at the plate's
centre facing back, scaled to its size. `CarView.BuildModelLiners` builds a dark part-tube over each hub, from 20
degrees behind it over the top to 20 degrees ahead (clear of the bumpers), from 5 mm past the tyre's outer face to
10 cm past its inner one, with a sector closing the inboard end so the road no longer shows through the car at the
wheels' height; `BuildModelPlate` a 52 x 11 cm plate. The liner's radius is measured in the preparation: 1.2 cm
inside the wing's arch lip over that span (the 86's lips sit only 3.6 cm over the front tyres and 0.4 cm over the
rear, so a liner 5 cm over the tyre, the first try, showed through the wings), never more than 5 cm over the tyre
and never under 5 mm. The plate's centre was read off the prepared model's rear surface (the recess between the lamps,
its centre 10 cm over the centre of gravity, 2.076 m behind it, the plate 6 mm proud). Checked on hidden orbit shots
from the side, the rear quarter and the front quarter. Left on the BRZ: the 86's nose and the W212's cabin.

### The Clio (A1, 0.33.0)

The owner's "renault clio" among the standard cars of Turkey (28 October 2026), open item 5. The catalogue's
fourteenth car, `VehicleSpec.ClioSixteen` ("CLIO 1.6 16V '01", `--car=clio`): a Renault Clio II phase 2 five-door
1.6 16V on auto-data.net's figures (107 PS at 5750, 148 N m at 3750, 1020 kg at the kerb for the three-door, 1045
taken for the five-door, 185/55 R15, 2472 mm between the axles, tracks 1406 / 1385, 193 km/h, 0-100 km/h in 9.6 s),
Renault's JB3 five-speed as a ClioSport forum's gearbox table gives the suffixes owners match to the 1.6 16V (3.364 /
1.864 / 1.321 / 1.029 / 0.821 on 4.067, which puts 193 km/h at 5,850 rpm in fifth, by the power peak; no Renault
table was found), discs and drums, ABS; the springs, bars and inertias scaled from the hot hatch's (game figures).
Its engine's curve (`EngineSpec.ClioSixteen`) runs through the two published points; its voice is the Bravo 1.6's
`four16v` banks, a sixteen-valve 1.6 four's, with that bank's idle, until a K4M recording is found (open item 4). The
lofted stand-in without the models is the Bravo 1.6's.

The model: Sketchfab was searched for the Clio on 2026-11-03 (33 queries, downloadable and not, 115 models; the
earlier Objaverse sweep had no Clio). Rejected: the AI-generated ones (Hunyuan), the re-uploaders of paid and game
models found in the cars13 hunts, mod packs, a photogrammetry shell, and azul88007's hand-made "Clio 2 Phase 2
template", whose cabin, engine and dashboard carry BeamNG's vehicle material names (vivace, bolide) and so are that
game's parts. Taken: "Clio 2" by Nardeol (CC BY 4.0, 337,228 triangles, 12 untextured materials, a Rennes hobbyist's
Blender work among 24 small pieces with French names), a shell without a cabin; its LICENSE.txt records the
evidence, the file is the project's copy on the `models-2026-11` release, checked by SHA-256 (`fetch_models.py`
RELEASED, `CarModelSourceTests`). The entry (`tools/car_models.json` "clio"): the tyres, wheels and hubcaps are one
mesh each across the corners, under named empties whose meshes are "Object_N" (the selectors are "#40", "#46", "#14";
the split pieces "Object_40_FL" and so on), the paint "Tle_rouge", the lamp covers lenses, "Phare.001" the brake
lamps, the SAAB 9-5's cabin as the Golf R32 has it (without the sun visors, 4 % smaller, 3 cm up and 8 cm back), its
materials corrected (Rouge Vif #B41F2C, silver hubcaps, dark steel wheels). The preparation scales it to the
wheelbase: 3.86 m long (Renault: 3773), tracks 1.366 m. Checked on hidden orbit shots from the front and rear
quarters and the side, and from the seat (the dials in the wheel's opening, the dashboard against the shell).

### The track's structures and the ground guard (0.34.0)

The owner's play-through of 7 November 2026 (ISTERLER P9-P11, F6) found four faults in the course's geometry and
one in the simulation. Each was photographed from a fixed camera before and after (`--camera=`, below) and is pinned
by a test.

- **The loop's exit ran into the entry road.** `TrackBuilder.Loop` is a helix: the deck shifts sideways by a given
  distance over the revolution so the exit clears the entry. The circuit's 11 m was one road width, so the descending
  half came down over the entry road's right shoulder (its deck's edge a hand's width over the asphalt, the ring's
  legs standing in the gravel) and the exit road's shoulder lay on the entry road. The helix now shifts 17.5 m: two
  road widths, both gravel shoulders, the ring 0.8 m outside the deck and half a metre between
  (`TrackTests.Loop_exit_clears_the_entry_road`: the descending deck's edge 1.5 m outside the entry's shoulder at the
  closest). The loop's first and last metres, where the deck lies level on the ground, now count as earthwork
  (`TerrainField.IsEarthwork`), so the ground meets the deck's foot instead of falling away from it in a step.
- **The bridges had no thickness.** The decks were the road ribbon alone, a sheet with no side or underside, and
  the gravel shoulder of the earth roads ran up the loop and along the drawbridge. `WorldBuilder.DeckBox` now builds
  the body of every deck between two frames: a kerb along each edge (0.45 m wide, 0.28 m tall; its top and its face
  to the road), the deck's side from the kerb's outer edge down to the soffit, and the soffit closing the box. The
  overpasses and the drawbridge ramps are 0.9 m deep concrete slabs with the railings on the kerbs; the loop's deck is
  a 0.5 m steel box with a 0.3 m edge plate, so from outside the loop has a body and from inside no floor is
  see-through. Decks have no gravel shoulders (`IsDeck`).
- **The banked 180's exit could not be controlled.** The bank eased out over the last 25 m of the arc while the
  curvature stayed that of the 62 m radius: at 100 km/h the bend asks 12.6 m/s^2 across the car, the 32-degree bank
  gives 5.2 of it, and when the bank went the tyres alone were asked for 1.3 g on a flat 62 m radius, more than they
  have, so the car understeered off the exit; the roll itself was 40 degrees a second. `TrackBuilder.Curve` takes a
  `transition`: the curve begins and ends with a spiral of that many metres over which the curvature and the bank
  rise from nothing to the arc's and fall back together, as a road's transition spiral and its superelevation runoff
  do (both linear along the spiral, so the lateral acceleration the bend asks and the lean that meets it stay in
  proportion). The heading still turns the whole angle: each spiral turns half of what an arc of its length would and
  the arc between them turns the rest, so the curve is `transition` metres longer than the plain arc. The circuit's
  180 takes 75 m at each end: a 269 m bend, the roll 13 degrees a second at 110 km/h, the exit a two-and-a-half-second
  taper (`TrackTests.Banked_180_tapers_its_bank_with_its_curvature`: the bank's share and the curvature's share agree
  to 0.00, the tapers 67 and 68 m). The return leg still bridges its own cutting
  (`Return_leg_bridges_its_own_cutting`: 5 m from the cutting's bottom, 52 m into the 89 m overpass, 5.5 m of
  headroom). Without a transition the geometry is bit for bit what every curve had before.
- **The car fell under the grass and the asphalt.** A wheel's contact was found by a ray from its hub down the car's
  axis to the surface the probe gave; a hub under its surface made the ray negative and the wheel was taken to be in
  the air, so once a hard landing, a bank steeper than the wheel could follow or a knock had pushed a hub through the
  road, nothing held the car up: it sank through the world until 6 m under the ground called a "fall off a bridge"
  and the replay showed the car under the road. The ground guard (`VehicleSim.SubStep`) now puts such a hub on its
  bump stop and, at the end of the step, lifts the body out along the surface's normal to where the hub's spring sits
  on the stop, taking away the speed into the surface; the centre of gravity itself under a surface by more than a
  decimetre is brought back to nine tenths of its ride height. `VehicleTests.A_car_under_the_road_is_lifted_back_onto_it`
  sinks a car half a metre at 70 km/h: one lift, and a second later it is at its old height on four wheels. The guard
  lifts only a hub a little under (0.6 m) a surface the car is the right way up on: a car arriving under the drawbridge's
  landing deck still hits its edge or falls into the gap, and a car fallen outside the loop is not pulled onto its deck. The lifts are
  counted (`GroundGuardLifts`, in the pose log) so a run that needs the guard can be found and its cause fixed.
- **The decks' edges.** On a bridge or a ramp a car that reaches the deck's edge is now held by the kerb and the rail:
  a knock off a wall running with the road (`RaceSession.Knocks`, a dent and sparks; a crash only at the crash speed),
  never a drop off the side (`A_ramps_rail_holds_the_car_on_the_deck`). The loop has no rail; its deck holds the car
  by itself.

**Playing the game by hand, hidden.** The owner asked that the game be played and crashed to find such faults. Five
switches make a hidden run drivable and readable (`Bootstrap.ParseArgs`): `--at=4,8,12` takes a picture at each time
(named `<stem>-<t>.png`); `--pose-log` prints the car's place twice a second (path, distance, position, speed, height
over its surface, in the air or not, damage, the guard's lifts) and every event on its frame; `--camera=x,y,z,tx,ty,tz[,fov]`
is a fixed camera in simulation axes that stays through a replay; `--hold=path:from:to:speed` makes the scripted driver
hold a speed over a stretch (the drawbridge at 12 m/s, the loop at 14, the banked 180 at 65); `--ram=` takes any
solid prop kind by name (pier, barrier, pump, tower). `--ssao=`, `--sun-shadow=` and `--shadow-filter=` force one
effect for a picture. A lap chart of the stunt route from the pose log (default car, attract speed): the drawbridge at
28-32 s, the loop at 43.6-48 s, the banked 180 at 60-66 s, the merge at 101 s.

### The crash carried on by the simulation (0.35.0)

Until 0.35 the simulation stopped the car where it struck and the view played a scripted slide (`CrashScene.Advance`:
a level slide with a yaw from the blow, a pitch that rocked, the car's height pinned to the crash height with the
terrain's rise clamped to 0.6 m). That is what put a wreck under the road: a car crashed nose-down in the air kept its
nose-down pose at the crash height, a car on a bank sank into the road, and nothing ever checked the body's lowest point
against the ground. The owner asked for the car not to freeze at the crash, for an explosion as in the original, and
for the other cars to move in the replay. All three come from one change: the crash is a blow the simulation plays out.

- **The blow** (`RaceSession.Knocks.Knock`): a knock over the crash speed used to return before any impulse. Now the
  car is pushed out of what it hit, takes the blow through its centre of mass (a tenth of the closing speed back; the
  crush zone spreads a head-on's load across the front, so a tree a hand's breadth off the centre line does not spin
  the car: applied at the touch, 9 cm off centre gave 1.6 rad/s) with friction at the touch on what slides, the
  engine dies (`EngineModel.Stop`) and the wheels lock. The car's origin sits below its centre of mass
  (`VehicleSpec.CentreOfMassOffset`): a blow at the origin's height pitched the car up on its tail, so the knock's
  point and the blow are at the centre of mass's height.
- **The wreck window** (`RaceSession.Step`, the replay phase's first `RaceOptions.ImpactSeconds` = 2 s): the car is
  stepped on, out of gear with the brakes on, the knocks looked for after every piece (it bounces off what it meets;
  a second blow is only a knock), the drones brake for it as for the car, the train and the cows go on, and the
  replay keeps recording. The wreck skids to a stop where the blow threw it; off the drawbridge it falls to the ground
  under the bridge and lands; off the loop it falls and comes to rest on its roof.
- **The body on the ground** (`VehicleSim.BodyContacts`): eight corners of the body box (the sills at the axles, the
  roof inset for the cabin) against the surface each is over, looked at when a wheel is off the ground or the car is
  tilted. A corner under its surface by up to 20 cm has its speed into the surface taken away at once (a tenth of it
  back: a body does not bounce like a tyre), friction on its slide, and is moved out by a quarter of its depth a step
  (a spring stored the blow and threw the car 20 m up; the overhangs' corners touched first in a nose-down landing and
  threw the tail down). A corner deeper than that is under something it never touched (the roof of a wreck sliding
  under the landing ramp was pushed up through the deck). A tilted car carried by its corners counts as rolled over
  (`BodyGrounded`), which the loop test needs: a fallen car now rests on its roof beside the loop's foot instead of
  sinking out of the world. `VehicleTests.A_car_on_its_roof_comes_to_rest_on_it`: 0.65 m over the road, up.y -1.00.
- **The probe under a deck** (`TrackGeometry.Probe`): a point more than a metre under a bridge, a ramp or the loop
  is over the ground below, not on that road (the wreck under the landing ramp was given the deck over its head as its
  surface and fell through the ground); over the drawbridge's gap the chasm holds to 1.5 m below the gap line
  (`FellOffBridge` is called a metre below it).
- **The replay** (`CrashScene.Pose`): the recorded poses by game time, the `ReplaySeconds` before the end of the
  wreck window, the first second after the blow at `RaceSession.ReplaySlowRate` (0.35); the blow's effects when the
  playback reaches `RaceSession.CrashTime` (the dent, the parts and bits thrown, sparks, steam, cracked glass, the
  explosion and the fire); the drones from their recorded poses by the same clock (`Bootstrap.SyncTrafficReplay`,
  `RaceSession.ReplayTraffic`). The scripted slide is gone. The replay camera keeps low for a car fallen under a bridge
  (`CameraRig.Replay`, `low`), and the chase camera takes the car's up and moves in towards the loop's centre as the
  road tilts (it sat inside the deck for the whole climb).
- **The crash from the seat** (`CrashScene.Impact`, `Hud.DrawGlassCracks`): through the wreck window the view stays
  where the player sits: the explosion (a burst of big flame tongues with a flash that dies in a third of a second,
  `CrashScene.Explode`), the fire and the smoke that follow the wreck, and the cabinet's white cracks over the
  windscreen; the replay's caption and tune come with the playback. The crash's thump sounds once, as it happens; the
  engine note dies with the engine and the wreck slides in silence; in the replay only the tune plays.
- **What is a crash** (`RaceSession.CrashNow`): looked for after every sixtieth of a second of the car's motion (a
  long frame put a tree deep inside the car's plan before the first look, and the car went through it); a nose-first
  touchdown at speed (pitched more than 20 degrees down at over 60 km/h) is a hard landing whatever its vertical speed;
  a side-swipe at 2.5 times the crash speed (an oncoming drone at 200 km/h relative) is a crash whatever the overlap's
  axis; a sawhorse is soft (`TrackProp.Soft`): it tips over like a sign for a scuff of damage; the wood's trunks near
  the roads are solid (`FoliageBuilder.NearTrunks`, registered after the props are drawn).
- **The banked 180's inner edge** (`TrackBuilder.Curve`): with transition spirals the centre line climbs with the bank
  by the half width's rise, so the inner edge stays at grade and the outer edge is the embankment (banked about a
  centre line at ground level, the inside sat 2.9 m down in a ditch).

### The ground, the verge and the far view (0.36.0)

The owner's list after 0.35: the far road lighter and darkening as the car came near (ISTERLER P13), the verge's
stones not orange and the same everywhere (P12), the grass far from real (P14), the cabin's shadows trembling (G8),
and the drawbridge's deck "a millimetre thin" at the jump (P10).

- **No mipmaps** (P13's main cause): every world texture was imported lossless and without mipmaps. Godot's importer
  turns both on only when it sees the texture used in a 3D material (`detect_3d`), and the materials are made in
  code, so it never saw one. Far off, each pixel took one texel of the photo: the asphalt's dark grains and pale chips
  aliased into a light, shimmering grey that settled into the true, darker grey only as the car came near; the grass
  and the gravel did the same. Every `*.import` under `assets/textures` now asks for VRAM compression (BC7 for
  colour, roughness and the alpha atlases, BC5 for the normal maps) and mipmaps; the sky panorama keeps its own. A
  texture added later needs the same parameters and `godot --headless --path game --import` run twice (the first run
  writes the defaults).
- **The car's reflection probe** lit everything inside its 90 m box with the car's surroundings, and the box moves
  with the car: the road ahead changed colour as it came into the box. `ReflectionMask = 2`: the probe lights only
  the player's car (`CarView.MarkPlayer` puts it on layers 1 and 2). `--probe-all` gives the old behaviour for
  comparison, `--probe=0` takes the probe away.
- **The asphalt's sheen**: dry roughness 0.8 to 0.9, specular 0.5 to 0.3; at a grazing angle the road far ahead
  mirrored the pale sky.
- **The wear sheet** (`tools/make-road-wear.py`, `road_wear.png`): a tar snake crossing the sheet's edge was drawn
  from one wrapped end to the other, a ruled line across the whole sheet and so down the road; the patches were
  outlines, frames lying on the road. Now a wrapping segment is skipped, the patches are filled ragged shapes with
  soft seams, and the wheel tracks are bands.
- **The ground** (`assets/shaders/terrain.gdshader`, `WorldBuilder.GroundMaterial`): two grass photos (ambientCG
  Grass004, a short lawn, and Grass001, a meadow with clover, both 2K) mixed in patches of thirty to sixty metres, and
  a soil photo (Ground067). Each is sampled with its tile broken up (Quilez's third technique: two lookups at offsets
  picked by a smooth noise index, blended where the index steps, the seam following the picture's own detail), so no
  pattern repeats; a coarser second lookup takes over between 20 and 90 m. The colour varies at three scales: dry,
  straw-coloured stretches over a hundred metres and more, lusher hollows over a few, clumps over half a metre. Bare
  soil shows on the trodden strip by the road (the terrain's road weight, UV2.y), in scattered bald patches and on the
  cuttings' faces (UV2.x, so the terrain no longer splits its earth into a second mesh), with a ragged edge that opens
  between the darkest blades first. A little relief (molehills, ruts) tilts the normal.
- **The verge** (`assets/shaders/shoulder.gdshader`, `WorldBuilder.ShoulderMaterial`): a photographed bed of stones
  (ambientCG Gravel020: colour, normal and height, 2K) painted the cabinet's burnt orange. The height tells the
  stones' tops from the joints, which are dark soil; the paint keeps the stone's own shading under it, covers the
  stones by the asphalt and thins outwards (UV.x runs from 0 at the asphalt to 1 at the band's outer edge, so
  `BuildShoulders` reverses the left side's u), is faded unevenly along the road and chipped in flecks; unpainted
  stones take the earth's colour; the outer edge frays into the grass (alpha scissor on a noise). The normal map bends
  the world-space normal, so the stones' relief shows in the low sun. The colour was matched to the arcade video's
  verge (155, 85, 20 measured; ours 165, 93, 32 in the same light): the first painted version was a pinkish orange,
  partly from the stones' sheen taking the sky's blue (specular 0.2 now). The instanced roadside rocks of the first
  version are gone: meshes of a few dozen triangles read as fake beside the photo.
- **The tufts** (`assets/shaders/tuft.gdshader`): the blade atlas tinted to the ground's own green and to the
  ground's dry and lush patches (by the instance's origin), darker at the root, the light through the blades kept
  small (they were brighter than the ground and lit from behind like lamps), dithered out between 70 and 120 m, where
  the ground's texture carries the plain. Small tufts where the verge frays (`FoliageBuilder`: 2.1 to 4.5 m past the
  asphalt's edge, at 55 % of the road's frames). The trees' leaves let less light through.
- **The cabin's shadows** (G8): the camera's vibration was white noise drawn every frame (`CameraRig`), so the cabin
  and its shadows jumped to a new place every frame: the trembling. It is smooth noise now (`FastNoiseLite` at
  11 Hz), growing with speed and the road's roughness (`--shake=0` turns it off). The sun is a point (at 0.5 degrees
  the contact-hardening filter's noise crawled over the dashboard as the car moved); bias 0.05 and normal bias 1.5
  (0.02 and 1.0 put a speckle of self-shadow on the grazing-lit dashboard), blur 1.3; the cascades end at 350 m
  (500) with the nearest 10 m deep (25) for finer texels in the cabin. SSAO's radius is the cabin's scale (1.5 m to
  1.0 m) at full resolution (`environment/ssao/half_size=false`, quality 3: at half size it shimmered on the dash).
- **The air**: the clear day's fog 0.00004 to 0.00009 and a deeper blue (0.66, 0.74, 0.86), aerial perspective 0.35
  to 0.55: nothing faded with distance, so the plain two kilometres out had the verge's contrast, a flat painting. The
  far woods are darker and bluer.
- **The drawbridge's ends** (P10): the slabs are 1.2 m deep (0.9), and their ends at the gap are closed
  (`WorldBuilder.DeckEnd`: the deck's and both kerbs' end faces). The box was open there: from the take-off only the
  asphalt's edge showed, the owner's millimetre.
- **Measured and left off**: SDFGI (`--sdfgi=1`) added 4.6 ms a frame for no visible change on the open plain; FSR 2
  (`--fsr2=<scale>`) gave nothing at full resolution. Godot has no DLSS: that, with Lumen and Nanite, is for the
  Unreal version (ISTERLER H5, H6). The other switches of the diagnosis: `--ssr=`, `--volfog=`, `--fog=`,
  `--probe=`, `--probe-all`.
- **The export** (`--export-world`, for the Unreal version): glTF cannot carry Godot's shaders, so
  `WorldBuilder.ExportMode` builds the ground and the verge with the standard materials, as before.

### The real land (0.37.0)

The Godot/Unreal comparison of 7 November showed the mountains as the weakest thing in both versions: in Godot a ring
of noise with vertex colours that under the haze read as a bank of cloud. 0.37 puts a real place round the plain.

- **The place**: Jackson Hole, Wyoming, centred on the sagebrush flats east of Moose (43.70 N, 110.67 W): the Teton
  Range to the west (its front 6 to 8 km out, the Grand Teton 13 km, 2.1 km over the valley floor), the Gros Ventre to
  the east, the valley running north to Jackson Lake. A flat floor 10 km wide fits the game's own terrain (the roads and
  2.4 km round them), and the range rises where the old ring stood. The game's +X looks west (`--heading 270`): the
  start straight runs at the Tetons, and the game's sun (from 144 degrees, 43 degrees up) lights their face from the
  south-east as the morning sun does.
- **The data** (`tools/make-terrain.py`): the USGS 3DEP 1 arc-second elevation model (tile n44w111, public domain) and
  a Sentinel-2 L2A true-colour image (S2B_12TWP_20240705: 5 July 2024, 0.4 % cloud, snow on the high peaks only; the
  13 June image had the range white to its foot, the 23 June one half the swath missing). A 48 km square round the
  circuit's centre, in a transverse Mercator frame about it: a 30 m grid of heights over the valley floor (2019 m),
  the game's sun's visibility (marched towards the sun to 12 km: the mountains' own shadows, which the shadow maps,
  ending at 350 m, cannot give) and the sky's (twelve horizons to 3 km: the gullies darker), zlib'd into
  `assets/terrain/jackson.bin` (5.5 MB); and a 5120 px colour texture (9.4 m a texel). The image's own sunlight
  (65 degrees up, from 143) is mostly divided out with the elevation's normals, so the game's lower sun shades the
  slopes again without doubling it; the product's 0..0.2 reflectance scale is taken back towards reflectance (dark
  ground near its old sRGB value, bright ground held down), the saturated snow set white, and the valley floor
  graded from the pinkish tan the satellite sees to the olive grey the sagebrush shows from the ground.
- **The mesh** (`RealLand`): a polar grid round the circuit's centre, since the land is only ever seen from within a
  kilometre of it: 1024 bearings, rings 0.6 % of their radius apart (12 m at 2 km, 60 m at 10 km, 140 m at 24 km),
  439 000 vertices in 32 wedges the camera can leave out. Inside the game's terrain the land lies flat at the old far
  ground's level and the triangles wholly inside it less 60 m are left out (the cuttings go below that level); the real
  heights come in over 1.2 km from the terrain's edge. The Earth's curvature with the air's refraction drops the land
  7 m at 10 km and 39 m at 24 km; a skirt carries the rim out to 60 km. The wedges are worked out in parallel and
  committed in series: 0.3 s of the start-up (the colour texture, imported lossless at first, took 0.7 s to load by
  itself: a texture added later needs its `.import` set to VRAM compression with mipmaps and its `.md5` under
  `.godot/imported` deleted before the next import; run headless, the importer notices a changed source, not changed
  settings).
- **The material** (`assets/shaders/land.gdshader`): the image's colour, the plain's own green by the terrain's edge
  (`WorldBuilder.PlainFar`, over 900 m), a little grain where the 9 m texels are magnified; Lambert light with the
  baked sun's visibility on the directional light and the sky's share on the ambient only; no specular. The GPU time
  did not move (13.4 to 14.1 ms with the land, 13.5 to 14.8 ms with the old far ground and ring: `--land=0`).
- **The clouds** (`assets/shaders/clouds.gdshader`): the 14 km sheet at 1.5 km would have cut through the peaks; the
  layer is now 60 km at 2.8 km, the sheet sampled at two scales and angles (it repeated as a wallpaper at one),
  thinned by a slow noise into clear and cloudy stretches, faded out from 9 to 28 km so its edge is never seen, drifted
  by the game in metres (`WorldBuilder.Tick`).
- **The air**: a clear day's fog 0.00009 to 0.00003 (about 130 km of visibility, the Rockies' dry air: the Tetons a
  quarter hazed at 10 km); overcast and rain keep their denser air, the range grey through it. The cameras see 65 km.
- **The 1989 look** keeps the old far ground and ring (`ShowLand`); the export (`--export-world`) carries the land
  with its colour on a standard material and leaves the hidden ring behind.

### The camera's motion blur and the road's patches (0.38.0)

- **Motion blur** (`MotionBlur`, `assets/shaders/motion_blur.glsl`): a compositor effect on the main camera, after the
  transparent pass, in linear light before glow and tone mapping. Each pixel is smeared along Godot's motion vectors
  scaled by a fixed exposure over the frame's time (1/100 s: a camera's shutter on a bright day), so the smear is the
  same at 30 and 90 frames a second, held to 3.5 % of the width, with 12 jittered samples; a sample counts only as far
  as it moves at least as fast as the pixel, so the cabin, the mirror and the car the chase camera follows stay sharp
  and do not bleed into the world behind them. The colour is blurred into a copy and written back. A colour resolved
  from MSAA has neither storage nor copy-to usage, so multisampling is off while the blur is on
  (`Bootstrap.ApplyGraphics`): TAA smooths the edges on its own, and the GPU time fell from 14.6 to 12.4 ms. On at
  medium and high quality, off in the 1989 look and with `--motion-blur=0`. The sky gets no motion vectors and does
  not smear as the camera turns.
- **The road's patches** (`tools/make-road-wear.py`): cut and filled as road crews do, rectangles square to the road
  with straight edges and a thin sealed joint, most darker (newer asphalt), a few lighter (old ones gone grey), a lane
  wide, over a wheel track or a trench across the road. 0.36's rounded, jittered patches read as dark puddles of oil.

### The scanned trees (0.39.0)

The wood (37 000 trees round the plain) and the roadside trees were crossed cards of a leaf-cluster photo: blobs at a
distance, lollipops up close. 0.39 draws them from Poly Haven's photoscanned trees (CC0): three firs (`fir_tree_01`'s
a, b and c, 19, 14 and 14.5 m), a large broadleaf (`jacaranda_tree`, 19.5 m, green in its scan) and a slender one
(`tree_small_02`, 4.6 m). They run to 0.4 to 7 million triangles each, so they are drawn as impostors.

- **The bake** (`tools/bake-impostors.py`, in Blender 5.2, portable): an orthographic camera 4 degrees over the
  horizon at twelve bearings round the tree, the frame square (the crown's reach either side of the trunk's foot, or
  the height), the foot at the bottom centre; every material redrawn as emission, once its base colour (sRGB) and once
  its normal in the camera's frame (raw; Blender's camera frame has z along the view, so a face turned to the camera
  comes out with z at -1), its alpha kept. `tools/pack-impostors.py` scales the 1024 px renders to 768 and packs each
  pass into a 4 x 3 atlas (`assets/foliage`, BC7 with mipmaps) with a JSON of the frame's size.
- **The card** (`assets/shaders/impostor.gdshader`): a 1 m quad turned to the camera about the vertical and sized to
  the frame (its bounds set to the frame, or the culling dropped trees still in view); the camera's bearing in the
  tree's own frame picks the two nearest views, dithered into each other per pixel (TAA smooths the seam). The atlas is
  sampled with the card's own derivatives (`textureGrad`): derivatives taken across the per-pixel jump between cells
  picked the blurriest mip and drew a cloud of speckles round every tree. The alpha is raised with the mip level (1 +
  0.45 a level), since a mip averages a leaf with the gaps round it and the cut at one half thinned the far trees to
  dots. The baked normal is turned back into the tree's frame for the view and blended half and half with a round
  crown's, so a crown is lit on the sun's side and shaded on the other (the leaves' own normals alone scattered the
  light and some trees read pale); the colour darkens low in the crown, where an unlit bake has no shadow.
- **The wood** (`FoliageBuilder.WoodOfImpostors`): the card trees' places, 18 m apart instead of 25 (a fir is
  narrower than a card crown), each given a tree by weight (firs 68 %, the broadleaf 26 %, the slender 6 %) and
  scaled to 10 to 15 m with the old scatter; one multimesh per tree and band, the near band casting shadows (the card's
  silhouette turned to the light). The roadside trees are the broadleaf or now and then a fir at the track's heights.
  The GPU time fell to 11.5 ms (12.4 with the card trees); the world takes 0.15 s longer to plant the wood.
- **The 1989 look** draws the cards in a flat green; the export keeps the card trees for the Unreal version.

### The structures' sound and the scanned grass (0.40.0)

- **The echo under a deck** (`StructureAcoustics`): a car under a bridge is heard twice, the second time off the deck's
  underside, with a short ring in the space between; until 0.40 the engine sounded the same there as in the open. The
  engine's recordings and the tyres' bus now play on a bus of their own, `Car`, made before the tyres' bus (a bus sends
  only to one before it) with a two-tap delay and a small reverb, both silent in the open. Every frame the decks (the
  frames of the overpasses, ramps and loop, a few hundred after a quick reject) are looked through for one over the car
  within its width and 1.5 m along it, 2.5 to 30 m up; the first tap's time is the way up to the underside (the slab
  and the engine's height taken off) and back, the second twice that, their levels fall with the way as a reflection
  spreads (2.5 / 2h: about -12 dB under a cutting's 5 m deck, -26 dB from the loop's top 25 m up), the reverb's wet the
  same, and all of it rises and falls over a tenth of a second. The attract lap logs (`--pose-log`) the cutting's deck
  4.9 m up with an 18 ms echo and the loop's top with 138 ms. None in the 1989 look or in the replay.
- **The joints**: where a path's road passes onto a deck or off it, the expansion joint knocks under the front axle and
  again a wheelbase later under the rear (the bump synthesiser's 48 Hz thump, stronger with speed).
- **The audio dump** (`--audio-dump=<file.wav>`): an `AudioEffectRecord` at the end of the master bus from the first
  frame, written at the quit: the sound checked by measurement on a machine that plays it silently.
- **The grass clumps** (`tools/bake-grass.py`): the tufts' 4 x 2 atlas baked in Blender from Poly Haven's Grass Medium
  01 and 02 (CC0), four to six clumps to a cell set in a row across the card's 1.2 m, turned and scaled at random so the
  tallest blades stand 0.25 to 0.42 m, drawn from the side unlit with their alpha (the drawn blades of
  `tools/make-foliage.py` until 0.40); the tuft shader takes the scans' own colours in dry weather (the tint over the dry
  one otherwise). The verge's fringe on both sides and the band beyond it about twice as dense: 20 000 tufts.

### Weathered buildings and the tufts' coverage (0.41.0)

- **Weathered** (`WorldBuilder.Weathered`, `assets/shaders/building.gdshader`): the barn, the houses and the shop were
  clean computer models, the barn one even, saturated red, every wall meeting the grass like a model on a table. Their
  walls, roofs, trim and plinths now take a shader that projects the photo on the three axes as the standard material's
  triplanar did, turns the normal map into the world with the whiteout blend, and adds the weather: grime and damp
  splashed up from the ground below a ragged line about knee-high (browner, darker, rougher), the rain's streaks down
  the vertical faces from the eaves, the paint faded in broad patches and a little greyed; walls take all of it, roofs and
  trim less. The ground's height comes from the building as an instance uniform on each part
  (`BuildingBuilder.SetFoot`): the parts are boxes placed at their own centres. Barn red is now 0.50, 0.13, 0.10 (0.62,
  0.12, 0.09). The 1989 look turns them flat, the weather wets them, the export takes the standard materials.
- **The tufts' coverage** (`tuft.gdshader`): the scanned clumps' blades are thin, and a mip level averages a blade
  with the air round it, so past a few metres the alpha cut took most of each clump: the plain read as a lawn with dark
  specks. The alpha is raised with the mip level (1 + 0.7 a level), as the trees' is.
- **Tried and taken out**: a dense meadow of the clumps' cards along every road (200 000 cards in chunks drawn within
  70 m): from the seat it read as dark specks on the lawn, and it cost 0.6 s at the start. A meadow needs blades in the
  thousands a square metre and their own lighting; left for later.

### Skid marks, dust and tyre smoke (0.42.0)

The simulation's tyres have slid, locked and spun since 0.18, and the road kept no trace of it; a car crossed the plain
at 150 km/h without raising a grain.

- **Skid marks** (`SkidMarks`): each wheel on the road whose slip intensity (the squeal's measure: the larger of 2.5
  times the slip ratio and 4 times the slip angle, less 0.25) passes 0.32 lays a strip from its last contact to its
  present one, 0.9 of its tyre's section wide, its alpha from 0.12 to 0.87 with the slide; a jump of more than 4 m
  starts the strip again. One multimesh of unit quads in a ring of 6000 strips, 1.5 cm over the road, alpha blended,
  a little glossy; cleared for a new session; laid through the wreck's slide after a crash as well (the replay itself
  moves no wheel). The attract driver stays within its tyres' grip and leaves none; checked with the threshold at nought
  (two dark tracks in the car's lane) and put back.
- **Dust and tyre smoke** (`WheelDust`): a GPU particle system a wheel for each, in the world's space, its emitter at
  the wheel's contact every frame and its amount turned with `AmountRatio`: dust off the grass and the verge from 5 m/s
  (full at 30 m/s), pale tyre smoke on the road from a slip intensity of 0.55; soft round puffs, lit, that grow and thin
  out over 1.6 and 2.2 s. Checked driving across the grass into the barn (`--ram=Barn`).
- `--pose-log` logs every fiftieth strip. None of it in the 1989 look.

### Volumetric clouds (0.43.0)

The clouds were a flat sheet at 2.8 km (0.37) with a painted grey underside: from the seat they read as a pattern on a
ceiling. 0.43 marches real ones through the sky (`assets/shaders/volumetric_sky.gdshader`, `WorldBuilder.VolumetricSky`).

- **The sky**: a sky shader in place of the panorama material, showing the same HDRI by `SKY_COORDS` (the sun's place
  and the light unchanged) and compositing over it the clouds of a quarter-resolution pass (`use_quarter_res_pass`).
  The radiance map (the scene's ambient light and reflections) is the photo alone (`AT_CUBEMAP_PASS`), and the sky is
  processed incrementally, so the drift does not cost a full radiance render.
- **The clouds**: a slab from 1.7 to 3.2 km over the valley floor, entered and left along the eye's ray (to 40 km), 36
  jittered steps; the density is a tiling fractal Perlin (`NoiseTexture3D`, 96 x 48 x 96, a 5.2 km tile) under a
  cumulus's profile (a flat, sharp base, rounded and thinning towards a top that rises with the coverage), less the
  coverage, eroded at its edges by a cellular noise (1.1 km), with clear and cloudy stretches from a 2D noise (26 km).
  Coverage 0.41.
- **The light**: the sun's through the cloud above each point (four steps of 160 m towards it, Beer's law, a little of the
  powder effect at the edges), a two-lobed Henyey-Greenstein phase (forward 0.55, back -0.2: the bright rim towards the
  sun), the sky's from above (blue on the tops, grey under the bases); the far clouds fade into the sky's own colour.
- **Wind**: the drift moves every two seconds (a change of the sky's parameters re-renders its radiance; two seconds'
  drift is a few metres kilometres away). Cost about 0.4 ms of GPU time. `--volumetric-clouds=0` shows the old sheet;
  the export keeps the panorama; overcast and the 1989 look are as before.

### Cloud shadows and rounder clouds (0.44.0)

The clouds of 0.43 drifted over a land they never shaded, and seen across the plain they stood as columns with flat
walls, grained with blotches far off.

- **The shadows** (`CloudShadows`, `assets/shaders/cloud_shadow.gdshaderinc`): the sun's share at a point through the
  sky's own clouds, by the same noises and density (without the billowed edges: a shadow is soft anyway), four steps
  towards the sun through the layer, Beer's law, 0.12 under a thick cloud for the light it scatters on. The land (per
  vertex: soft shadows a kilometre across, the vertices tens of metres apart out there, into its own `light()`), the
  plain's ground (per pixel, out of 500 m) and the trees' impostors (per tree, at its foot) take it; the road, the cars
  and the props light by Godot's own lighting, so the sun itself carries the camera's share: the same function runs on
  the CPU at the camera every frame (the shape noise and the weather map read back from their textures once generated,
  sampled as the GPU samples them) and dims the sun's energy, and each shader lights a point by its own share over the
  camera's, blended in from 500 m to 1.5 km. Driving into a cloud's shadow the road, the cabin and the car go into the
  shade together, as they do under a real one a kilometre across; the error is near the shadow's edge a few hundred
  metres off. The shadows take the sky's wind every frame (the sky every two seconds).
- **The first view in the sun**: the start line lay under a cloud; once the noises are read back the clouds are moved on
  along the wind, 400 m at a time, until the sun at the camera is full.
- **Rounder clouds**: the shape noise was sampled on a 5.2 km tile up as across, but its texture is half as tall, so it
  changed over twice the height and the clouds stood as columns with flat walls; now 2.6 km up. The steps' offsets are
  interleaved gradient noise (an even, fine grain) in place of a random hash, which blotched the far clouds, and 48
  steps instead of 36.
- GPU time within the measurement's noise (11.6-12.0 ms against 11.4-11.9 ms over an attract lap). `--cloud-shadows=0`
  turns them off; none in overcast weather or the 1989 look; `--pose-log` logs the camera's share as it changes.

### The wind (0.45.0)

The clouds drifted and their shadows ran over the plain while every blade and leaf stood as if under glass.

- **One wind** (`assets/shaders/wind.gdshaderinc`): the clouds' own direction (the sky's clouds move to Godot's -x and
  -z), 4.5 m/s at the grass; gusts from two octaves of value noise stretched across the wind (fronts tens of metres
  wide, a few deep), running downwind at 1.3 times the breeze; `wind_lean` gives a plant's lean per metre of height: a
  steady 0.06, up to 0.28 in a gust, with a quick flutter (6.3 rad/s) over it.
- **The grass** (`tuft.gdshader`): each tuft's card sheared downwind by its height squared (the card is 0.45 m), the
  whole tuft at once; in a gust a tip moves some 13 cm.
- **The trees** (`impostor.gdshader`): the card's top swayed the same way, nine times stiffer (a 13 m tree's crown
  moves 10 to 40 cm); the foot stays put; not in the 1989 look.
- **The meadow** (`terrain.gdshader`): where a gust runs, the grass within 500 m is up to an eighth paler, the blades'
  undersides turned up, so the gusts are seen running over the plain.
- **The dust** (`WheelDust`): the particles' gravity carries them downwind at 4 m/s² against their damping, so a car's
  dust and smoke drift off to one side.
- **The black specks**: half the grass tufts drew unlit, dark specks over the whole plain (the dense meadow of 0.41 was
  taken out for them): the cards' normal is up, and on a double-sided material Godot turns it down on the side seen
  from behind. The tuft shader now sets the world's up as the normal on both sides, so a tuft is lit as the ground
  under it is.
- **The clouds' streaks**: the steps' offsets varied from pixel to pixel showed as a pattern on the clouds (blotches from
  a random hash until 0.44, diagonal streaks from interleaved gradient noise), and a per-frame offset did not help: the
  view's temporal anti-aliasing does not reach the sky's quarter-resolution pass. The steps now sit at the middles of
  equal parts for every pixel, 64 of them (48 in 0.44), and the bands this leaves are faint; GPU time unchanged within
  the measurement's noise.

### The windscreen (0.46.0)

The glass was not drawn from the seat (CarView hides it), so the rain fell in the air before a windscreen that stayed
dry, and the sun ahead lit nothing between the driver and the road.

- **One quad over the view** (`Windscreen`, `assets/shaders/windscreen.gdshader`), the camera's child, drawn after
  everything else with its depth test off; the cabin (nearer than 1.2 m: the dashboard, the pillars, the mirror) is
  left alone by the depth, the glass is wherever the view reaches past it, side windows included. Premultiplied
  alpha: the drops replace what is behind them, the haze adds to it.
- **Drops**: two sizes on grids of cells across the glass, each cell's drop landing at a time drawn from the rain's rate
  after the last sweep over it (an exponential arrival), so the glass fills as the wipers' blade moves on; a drop is a
  lens that shows the scene behind it upside down and drawn in, darker at its rim. At speed the air drives the landed
  drops up the glass and stretches them. Between the sweeps a film of water softens the view.
- **The wipers**: two blades on pivots below the glass's foot, parked pointing right, sweeping up and back with a
  crank's eased motion; for every point the shader works out when a blade last passed it from the wipers' clock, so
  the drops are wiped where the blade has been. Steady in rain; on a wet day without rain a few drops and no wipers.
- **The dust in the sun**: on a dry day with the sun ahead the glass's dust and the wipers' fine arcs round their
  pivots light up into a haze round the sun, faint further off; it goes when a cloud covers the sun (CloudShadows).
- **The motion blur** (`MotionBlur`) runs before the transparent pass now (after it until 0.46): what is drawn on the
  glass stays sharp as the road streams by behind it; the particles are soft anyway.
- The cockpit only: hidden outside the car, in the replay and the inspection views, and in the 1989 look;
  `--windscreen=0` for comparisons. One wipe time a pixel serves every drop round it (a drop the blade is crossing
  shows cut for that frame, as the blade cuts it; a time a drop cost 36 arc functions a pixel), and the dust's noises
  are not worked out more than 50 degrees off the sun: about 0.4 ms of GPU time in the rain, none measurable dry.
- **The sun through a cloud**: a cloud the sky's march gave up in (3 % of the light through) is opaque now; the photo's
  sun, thousands of times the sky's brightness, shone through a thick cumulus as a white point.

### The roadside going by (0.47.0)

The car passed a post every 50 m, signs, trees and the barn's wall within a metre or two, and heard none of them.

- **The reflectors** (`RoadsideEchoes`): the marker posts (the view places them, so `WorldBuilder.BuildMarkerPosts`
  hands over their feet), the signs, the trees by the road, the piers, towers, pumps, gantries and sawhorses, the
  buildings; each a circle on the ground with a size for how much it throws back (a post 0.5, a tree 0.8, a wall 1.3),
  in a grid of 20 m cells. Once a frame the strongest within 14 m of the car: strongest abeam, over a window along the
  road the object's length and a bit, falling as the gap's 1.3th power (gentler than a point scatterer's square: the
  posts stand 4 to 5 m off a car in its lane, and at the square their whoosh was lost under the wind).
- **The echo** (`BodyNoise`, `BodyState.Passing` and `PassingGap`): the direct roar comes from the recordings; this adds
  its reflection: band-passed noise of the roar's band (700 Hz) combed with itself delayed by the way to the object and
  back (2 x gap / 343 m/s, 6 to 30 ms, glided across the block through a 4096-sample line), so the comb's teeth sweep as
  the gap opens and closes: the whoosh of a post at speed, the long roar of a wall. Its level grows as the square of the
  speed, like the roar's (`PassGain` 0.08 RMS at the roll's reference speed, at most 0.2), a little less in the cabin; the echo draws
  its noise from a stream of its own, so the wind's gusts do not change when something passes. Silent in the
  1989 look; `--pose-log` logs each object as the car passes it.
- **No sun shadows under an overcast sky**: behind cloud the sun is a wide source (10 degrees) and its shadows are all
  but gone, but the soft filter for so wide a source cost 7 ms a frame: in the rain the attract lap ran at 17-19 ms
  (47-51 fps on the development laptop) against 10-12 ms without them, and the cockpit and the road looked the same.
  The sun casts shadows under a clear sky only (`WorldBuilder.SunShadowsOn`; `--sun-shadow=` still forces them).

### The meadow by the roads (0.48.0)

From the seat the ground by the road read as a mown lawn: the lawn's photo with a scatter of tufts. 0.41 laid a meadow
of the scanned clumps along every road and took it out again for the dark specks it scattered; they were the cards
drawn unlit from behind (0.45), and the meadow is back (`MeadowBuilder`, `assets/shaders/meadow.gdshader`).

- **Where**: from the verge's fray out to 24 m on either side of every road, the lateral distance drawn towards the
  verge as the 2.2th power (densest near it, thinning out; at 14 m the meadow ended in a visible edge), about 28 cards a
  metre of road, some 83 000 in all; not on the trodden strip by the road (the terrain's road weight over 0.9: a mown
  strip a few metres wide), on a road or its stones, in a field or on a pad; not by the loop, the ramps, the jump's gap
  or the overpasses.
- **How**: crossed cards 0.9 m wide and 0.4 m tall, each instance's atlas cell and shade in its custom data; chunks of
  60 m of road, each a multimesh drawn within 70 m of the camera and dithered out from 45 m, so a few thousand cards are
  drawn; built path by path side by side on the CPU. The shader lights them as the ground (the world's up as the normal
  on both sides), raises the thin blades' alpha with the mip level, tints them by the ground's dry and lush patches and
  bends them in the shared wind (`wind.gdshaderinc`).
- Medium and high quality; not in the 1989 look nor the export; `--meadow=0` for comparisons.

### The brakes' heat and fade (0.49.0)

The model kept no brake temperature anywhere: the tenth hard stop in a row stopped as short as the first.

- **The disc** (`BrakeThermal`, `WheelState.BrakeTemperature`): a lump of cast iron for each wheel, 8 kg at the front and
  5 kg at the rear for a brake of 2700 N m and in proportion for others (460 J/(kg K)); heated by the brake's work while
  the wheel turns against it (the brake's torque times the wheel's speed; a wheel it holds still heats its tyre, not the
  disc), cooled by convection growing with the speed (a time constant of some eight minutes standing, a minute and a
  quarter at 30 m/s), integrated exactly over each step for its constant power.
- **The fade**: the pads hold the disc fully to 400 °C, lose a quarter of their friction at 700 °C and two fifths at
  1000 °C and no more (`BrakeThermal.Fade`), the driver's brake and the electronic differential's alike, as the organic
  and semi-metallic pads of road cars of the time did. A stop from 200 km/h puts some 170 °C into a hot hatch's front
  disc; four in a row with nothing but the launch back up between them take the fronts past 400 °C and the fourth stop is
  longer than the first (`HandlingTests.Hard_stops_in_a_row_heat_the_brakes_until_they_fade`); one stop from 100 km/h
  leaves them under 200 °C, so every other stopping figure is a cool brake's.

### Buck-and-rail fences (0.49.0)

Jackson Hole's ranch roads run between buck-and-rail fences of weathered lodgepole; the plain here ran unfenced from the
verge to the woods.

- **The bay** (`FenceBuilder`): one mesh, a buck of two six-sided poles 12 cm across from feet 1.2 m apart across the line,
  crossing about a metre up, the top rail in their crotch and two rails on the outer face of one leg at 0.70 and 0.38 m,
  each rail 3.4 m so it overlaps the next bay; Poly Haven's Brown Planks 03 mapped along each pole within one plank's
  grain (nine across the photo), a shade greyer.
- **The lines**: both sides of every open road (not the loop, the ramps, the jump's gap or the overpasses) 32 m from the
  centre line for a road 11 m wide, a bay every 3 m of road, each bay's instance sheared from its start to its end on the
  ground so the legs stay upright on a slope; left out where another road's ground comes near (the terrain's road weight),
  within 12 m and half its size of a building, a tower, a pier, a pump, the gantry or the train, in a field or on a pad,
  over a step of more than 1.2 m, and two bays for a gate every 250 to 650 m. Chunks of 200 m of road drawn within 700 m.
- Visual only: the simulation does not know them. Not in the 1989 look nor the export.

### The power line (0.50.0)

- **The poles** (`PowerLineBuilder`): one mesh, a post tapered from 30 to 22 cm, 10 m over the ground and 1.5 m in it, a
  crossarm 2.4 m long 9.4 m up across the line and three insulators, in the fences' weathered wood darkened to creosote;
  every 55 m on one side of each road (alternating from road to road) 25 m from the centre line for a road 11 m wide,
  left out where the fences are (other roads, buildings, trees, fields, pads) and wherever the road is the loop, a ramp,
  the jump's gap or an overpass.
- **The wires** (`assets/shaders/wire.gdshader`): between consecutive poles, each of the three a parabola of 0.9 m sag in
  16 segments, all of them one mesh; drawn as ribbons turned about the wire to face the camera, their width the wire's
  own 2 cm or a pixel and a half at that distance, whichever is wider, and their opacity the wire's share of that width:
  a 2 cm cylinder 300 m off is a twentieth of a pixel, and drawn as geometry it shows as a dotted line that crawls as
  the car moves; as a hairline of the right weight it holds still.
- Visual only; not in the 1989 look nor the export.

### The scanned cows (0.51.0)

The herd's cows were built in code (0.9.1: a lofted barrel, a neck, a head, four jointed legs, a painted hide): at
5-50 m from the road they read as toys.

- **The model**: "Cow frame test" by Nyilonelycompany (Sketchfab, CC BY 4.0, via the Objaverse mirror; credited in
  THIRD_PARTY.md): a horned Holstein of 29 000 triangles with its hide in one texture, on a quadruped's metarig of 191
  bones, fetched with the car models into `unreal/Import/Models` (`fetch_models.py`, its LICENSE.txt beside it) and
  copied as it comes to `game/assets/models/cow_holstein.glb` by `tools/build.ps1` (build output, as the cars are);
  sized at run time by its hips' height over its hooves, 1.42 m, a grown Holstein's. Found by a search of Sketchfab's and other free scans: no
  CC0 or CC BY photoscan of a Holstein exists; a red-pied photoscan whose page is gone and a lighter static red-pied cow
  were the others considered.
- **Into the herd's frame** (`CowBuilder.BuildScanned`): the scene turned a quarter round (its head at +z to the herd's
  +x) and set down by its toes' bones (the hooves' soles 5 cm under them), the middle of the four over the rig's origin:
  a skinned mesh's bounds are in its bind frame, which the skeleton's own scale and turn move, and set down by them the
  cow stood a metre and a half under the ground. Every other cow mirrored and each 0.94-1.06 times the size, so the
  seven do not stand there as copies.
- **Posed as before** (`CowRig.Pose`): the herd's grazing cycle bends the neck's root and its two segments and the head
  about their x axes (0.15, 0.5, 0.5 and 0.25 rad at full graze, the muzzle in the grass), a slow look about turns the
  neck about z while the head is up, the thighs swing in diagonal pairs about x as the cow walks or trots, the tail's two
  segments swish about z, harder once it is upset, and the stagger of a knock rocks the whole model as it did the built
  one. The clip that comes with the model (a mild idle) is not used: it never brings the muzzle down to graze.
- The export keeps the built cow (the other engine dresses its own); so does a build without the model.

### The prairie's sound (0.52.0)

The world was silent but for the cars, the train and the cows: standing at the start, the engine idling, nothing else.

- **The recording**: dsebeste's PrairieSummer_ambience (Freesound #351609, CC0), a warm day on the Canadian prairie,
  birds, insects and the wind in the long grass; `tools/make-ambience.py` makes it a 47-second loop from the HQ preview:
  44.1 kHz stereo, high-passed at 80 Hz, the last 3 s cross-faded (equal power) into the first, the recorder's ticks
  (broadband clicks a few milliseconds long, found where the band over 6 kHz jumps over 4.5 times its running level, and
  two by hand) patched with the sound a tenth of a second before, set to -30 dBFS RMS (the recording is -58).
- **The mix** (`WorldAudio`): one player on the world's bus, everywhere at one level, so the glass takes its top off and
  a few decibels from the seat as it does the rest of the world; full on a clear day, 0.6 overcast, 0.45 wet, 0.2 in the
  rain; silent in the replay (which plays its tune alone) and the 1989 look. At speed the engine, the tyres and the wind
  cover it as they would; standing, it is the sound of the place.

### The overcast's deck of cloud and the wet road's spray (0.53.0)

- **The deck** (`WorldBuilder.OvercastDeck`): an overcast, wet or rainy credit showed the procedural grey of 0.12, a flat
  gradient. It now has the clear sky's volumetric clouds (`assets/shaders/volumetric_sky.gdshader`, the same noises and
  wind) as a low deck over a grey dome in place of the photo (`use_photo` off: lighter towards the horizon, darker
  below it), so the grey has a deck's texture, thicker and darker patches and thinner lighter ones drifting by; the
  dome is the radiance map too, so the light is the old overcast's grey. The deck sits lower and closes as the weather
  worsens: 1.2-2.6 km and 85 % cover overcast, 0.9-2.4 km and 93 % wet, 0.7-2.2 km and whole in the rain, its underside
  darker; the sun behind it at 1.6 instead of 3.6. Without the volumetric sky (the export, `--volumetric-clouds=0`) the
  procedural grey stays.
- **The air under it**: the deck fades into the dome's grey towards the horizon over the fog's own distance
  (`haze_distance`: 20 km overcast, 9 km wet, 3.5 km in the rain; the clear sky keeps its 28 km), and the fog is the
  dome's grey at the horizon (0.60, 0.62, 0.66; 0.9 of it wet, 0.78 in the rain; 0.70-0.76 on every grey day until
  0.53), so the far land and the far deck meet in one grey. With the clear sky's haze the rain's deck stayed dark down
  to the horizon, and the fogged Tetons stood against it as pale cut-outs. The radiance map stays the dome alone (the
  march in the cubemap pass would have cost three times the quarter-resolution pass's pixels). The deck costs about
  0.3 ms a frame in the rain (10.7 against 10.4 ms with `--volumetric-clouds=0`, an attract lap at 1080p).
- **The spray** (`WheelDust`): on a wet road each tyre throws up a fine mist, a third particle system a wheel beside
  the dust and the smoke, from 30 km/h and full at 120, scaled by the road's wetness (0.85 wet, 1 in the rain): thin
  (a third of the dust's opacity), quick (0.8 s) and drifting downwind with the rest. All three now fade where they
  meet the road or the ground (proximity fade over 0.6 m) instead of cutting into it in hard bands.
- **The rain by the eye** (`CameraRig.SetRain`): the streaks fade in from 1.2 m and are whole from 4.5 m. A drop half
  a metre away was a streak thirty pixels wide, which no eye or camera sees in focus, and from the seat the drops fell
  inside the cabin.

### The rain's sounds (0.54.0)

The rain fell without a sound: on the glass the drops and the wipers moved in silence, and outside there was only the
tyres' spray.

- **The recordings** (`tools/make-rain.py`, all CC0 on Freesound): the drumming on a car's roof from inside (ondrosik
  #332856, a Zoom H1 in the cabin), medium rain on concrete, grass and leaves for outside (klangfabrik #160699, wide),
  and a car's wipers from inside (BeeProductive #395608, a cycle every 1.53 s). The two beds are 42-second loops
  (high-passed, the last 3 s cross-faded into the first, -24 and -26 dBFS RMS).
- **The wipers**: the recording's blades reverse every 0.765 s, a thump each time, the rubber's swish loudest between;
  the game's sweep up and back takes 1.1 s (`Windscreen.WiperSweep`), so each stroke is shortened to 0.55 s by cutting
  out its middle, the swish, cross-faded over 60 ms: the thumps stay as they were, and so does the pitch (resampling
  would have raised the motor six semitones). Four cycles make the loop. `WorldAudio` keeps it on the blades from the
  windscreen's own clock (`Windscreen.Wipers`), the park's thump 0.06 s into each sweep and the output's latency ahead.
  Far off (the wipers just started, a hitch, a pause) the loop jumps to its place; nearer it is drawn in by playing up
  to 3 % fast or slow, the steady difference between the game's clock and the sound card's learnt (1.5 % on the
  development laptop), so it stays within 10 ms of the blades. Jumping whenever it drifted 50 ms clicked every second
  or two.
- **The mix** (`WorldAudio.UpdateRain`): outside, the rain on the world's bus, so from the seat it comes through the glass
  like the rest of the world, and at 0.55 under a bridge's deck. From the seat, the roof's drumming and the wipers on the
  master bus, the cabin's own: the drumming a few decibels over the idling engine standing, half as loud again at
  110 km/h as the drops strike the glass faster, and gone under a deck (`StructureAcoustics.Covered`, the deck that also
  gives the echo). None in the replay, which plays its tune alone, or in the 1989 look.

### The sunrise (0.55.0)

Every credit ran under one sun, 43 degrees up, the light of a summer's late morning.

- **The sky** (`tools/make-sunrise-sky.py`): Poly Haven's "Syferfontein 6d Clear (Pure Sky)" (Greg Zaal, sky edits Jarod
  Guest, CC0), a clear sky with the sun 5.85 degrees up, its columns rolled (nothing resampled) so that its sun stands at
  compass bearing 85 in the game's panorama (u = 0.5 + atan2(-x, -z) / 2 pi, +X west and +Z north): over the Gros
  Ventre, whose ridge stands 3.5 to 4 degrees high from the circuit that way (measured from the land's heights), and
  behind a driver on the start straight, so the low light falls on the Tetons' faces ahead. `WorldBuilder.SunriseDirection`
  is that sun; the directional light turns to it, its colour about 3500 K (1, 0.66, 0.38), 2.1 (horizontal ground takes a
  tenth of it), the sky's light 0.8 of the day's so the shadows' blue stands against the lit gold. Under the grey deck
  the light stays high whatever the hour (a sunrise's 6 degrees lit the spray and the rain facing it over a road it
  barely touched), and the morning is a dimmer grey (0.75 of the light).
- **The land's shadows** (`tools/make-terrain.py`, grid version 2): a sun 6 degrees up throws the ranges' shadows across
  the far land, half of which lies in them; the grid carries a second visibility, cast for the sunrise's sun the way the
  day's is (`--from-bin` recasts both from a written grid without the rasters); `RealLand` puts it in the vertices' alpha
  and `land.gdshader` takes the hour's. A version 1 grid lights the sunrise with the day's.
- **The air and the clouds**: the haze takes the low sun's gold towards it (the fog's sun scatter, 0.35) over a paler
  morning air, exposure 1.0; the volumetric clouds take the sun's colour and thin to 0.22 cover (fair-weather cumulus
  grow with the day's heat, and through 15 km of their layer towards a sun 6 degrees up their shadows would have kept the
  plain in shade, so the clouds' shadows are off at sunrise).
- **The grass** (`tuft.gdshader`, `meadow.gdshader`, `stand`): by day the cards are lit as the ground under them (the
  normal straight up); a sun 6 degrees up lights flat ground at a tenth of its strength, and blades stand in it, so at
  sunrise the cards' normal leans half way towards the eye: bright looking away from the sun, backlit looking into it.
- **The menu and the credit**: LIGHT (day, sunrise, or one of them drawn for every credit), kept in the settings like the
  weather (the panel 704 px high for its thirteen lines); `--light=day|sunrise` for automated runs, `--menu` opens the
  menu a second in for pictures of it.
- **The spray** (`WheelDust`): twice the puffs, 1.3 m and a fifth opaque where 0.53's were 1.8 m and three tenths, so the
  wet road's mist trails from each wheel instead of showing as soft blocks over a dark road.
- **Cost**: 12.0 ms a frame at sunrise against 11.6 by day (the longer shadows; an attract lap at 1080p).

### Aquaplaning (0.56.0)

The puddles were the view's alone: a mirror-flat patch on the asphalt, under the tyres the same wet road as everywhere.

- **The water** (`PuddleMap`, `WorldBuilder.MakePuddles`): the puddles' tile is made at once from the noise 0.12's
  NoiseTexture2D made (simplex, two octaves, 512 square, seamless), giving the view its roughness ramp as before (the
  picture is the same) and the simulation a depth: the water standing in the puddles' cores, none where the noise is
  over 0.36 and deepest under 0.20 (the mirror-flat cores are 15 % of the asphalt; with their rims counted two fifths of
  the road floated a tyre and some wheel was half afloat 41 % of a rainy lap). Laid as the asphalt lays it, two tiles
  across the road and one every 5.56 m along, from each wheel's place on the road (`SurfaceHit.Lateral`, the frame's
  distance). `WeatherModel.WaterDepth`: in the rain a 0.5 mm film and 5 mm in a puddle, once it has stopped 0.2 and 2.8.
- **The tyre** (`TireModel.Aquaplaning`, `TireModel.WaterDrag`): NASA's dynamic aquaplaning speed (Horne, 6.36 sqrt(p)
  km/h: 96 km/h at 230 kPa); where the water stands deeper than the tread drains (from 1.5 mm, wholly at 4) the patch
  lifts from 70 % of that speed and floats by 115 %, keeping 15 % of its grip; a film shallower is the wet grip alone.
  The water it pushes aside drags it, half the water's density times the tread's width by the depth times the speed
  squared (Cd 0.6, half of it once afloat): a puddle under one side pulls the car towards it, and a flooded road slows a
  coasting car. Without a map (the tests' default) a wet road has no standing water.
- **Measured** (`AquaplaningTests`, the hatchback in the rain on a road flooded from edge to edge): a stop from 120 km/h
  takes 168 m against 78, from 90 47 against 45, from 50 the same; three seconds' coasting from 100 km/h loses 4 km/h
  more. The scripted driver's rainy lap: no crash, 84.5 km/h on average against 85.3 without the puddles.
- **Heard and seen**: the spray grows to more than twice through a puddle (`WheelDust`), and the tyres' hiss half again
  as loud for each tyre in one (`TyreContact.WaterMm`, `TyreMix`).
- **The windscreen** (`windscreen.gdshader`, `Windscreen.Layer`): the wipers' arcs, a line every 3 px since 0.46, aliased
  into loops like a map's contours over the glass against a low sun; now one every 12 px, each of its own depth (most
  faint), a pixel or two wide at any resolution and only in the blades' sweep, faded over its last tenth of a radian.
  The glass's quad covers the view of whichever camera draws it, so the rear-view mirror showed the drops, the blades
  and the arcs: it is on a layer of its own, which the mirror's camera and the car's probe leave out.
- `--puddles=0` leaves the water out for comparisons; the pose log prints `aqua=` when a tyre floats.

### The night (0.57.0)

- **The hour** (`WorldBuilder.Hour`, `SetHour`): the day, the sunrise (0.55) and the night; the menu's LIGHT steps
  through day, sunrise, night and random (a draw of the three), its stored numbers keeping 0.55's (random 2, night 3).
- **The sky and the moon**: Poly Haven's Kloppenheim 02 pure sky (Greg Zaal, sky edits Jarod Guest, CC0), a clear
  night with the moon 17 degrees up and its stars, used as photographed: its moon stands at u 0.599, where the day's sun
  stands, so the moon is the day sun's bearing (144) and the land's baked shadows are the day's (a little short for a
  moon this low, which no one sees by its light). Poly Haven exposes its skies for a picture, this one only 1.75 times
  darker than the day's: it is drawn at 0.035 of its level (`panorama_energy`), the volumetric clouds' sky light comes
  down with it (or they glowed as by day) and take the moon's colour, a third of the sky covered. The moon's light is
  blue-white (0.70, 0.80, 1) at 0.18, the air a deep blue, the camera's exposure 1.35; no clouds' shadows and no glare
  on the glass by its light.
- **What lit the night as dusk**: the ambient's fifth that is not the sky's is a constant warm ground bounce, a day's
  (`GroundBounce`); the closed cabin's probe has a constant ambient (`CabinAmbient`) and two shadowless lights stand in
  for the daylight through the glass (`BuildCabinLight`). All three now come down with the night sky
  (`CarView.CabinLight`), leaving the cabin to its instruments.
- **The lamps** (`CarView.Headlights`): two spot lights at the body's front corners, 0.65 m up, aimed 1.5 degrees down
  through a low beam's pattern drawn into a projector texture (`LowBeam`: a flat cut-off on the oncoming side that steps
  up 15 degrees over the verge, the hot spot just under it, a broad fill), 28 at 120 m and 32 degrees, their shadows
  leaving the car's own layer out (`ShadowCasterMask`, or the bumper and the lamp glass blacked them out) and lighting
  the volumetric fog (beams in the rain); a red omni light behind, 3.5 m, for the tail lamps' glow on the road.
- **The reflectors** (`assets/shaders/retroreflector.gdshader`): the posts' reflectors send a lamp's light back the way
  it came, a lobe a few degrees wide round the light's own direction at 400 times, so a post a hundred metres ahead
  flares in the faint light over the cut-off; the sun is left out of the lobe (with it behind the driver every post lit
  up by day).
- **Cost**: 13.5 ms a frame at night against 11.7 by day (the two lamps' shadows), an attract lap at 1080p.
- Still to come: the traffic's and the train's lamps, the farms' yard lights; a few cars' light trim (the estate's dash
  panel, the flat-twelve's console) stays lighter than the rest at night.

### The lamps of the night (0.58.0)

- **A lamp's glow** (`NightLamps.Glow`, `assets/shaders/lamp_glow.gdshader`): a card turned to the camera, added over
  the scene past the glow's threshold so it blooms, tested against the depth (the body hides the far lamp); it shines
  the way its node's -Z faces and fades seen from the other side (a headlamp from behind, a tail lamp from ahead), or
  all round for a yard lamp's refractor. One material for each colour and strength (forty-odd lamps share a handful: a
  material each had cost a couple of milliseconds of the CPU's frame). The night's glow intensity is 0.5 against the
  day's 0.2, so an oncoming car's lamps are haloed points as an eye sees them in the dark.
- **The cars** (`CarView.Headlights`, `MeasureLamps`): every car's lamps from its shell's or its model's box, two white
  glows at the nose and two red at the tail; the player's two shadowed beams and the red glow on the road (0.57), a
  drone's one beam between its lamps without shadows (eight drones' shadows cost too much for what they show).
- **The train** (`TrainView.Night`): a headlamp on the smokebox, a glow and a beam 160 m down the line, and the coaches'
  windows lit warm from inside.
- **The farms** (`NightLamps.YardLamp`): by each barn and house a wooden pole 7 m high with a lamp on an arm, standing by
  day too; at night an orange sodium lamp (a 1980s farm's dusk-to-dawn lamp, about 2000 K) lights the yard and the
  building's face 30 m round, without shadows. The world's export keeps the poles and leaves the lamps out (glTF carries
  no shader: the glows went out as white cards).
- **Cost**: 13.2 ms a frame at night, the CPU's share as by day.

### The night's sound, the paint's beads, the low beam's reach (0.59.0)

- **The sound** (`WorldAudio`, `ambience/night.wav`, `tools/make-night.py`): the birds sang all night; at night the day's
  bed is silent and the prairie's night plays instead: Danjocross's "Kansas Night" (Freesound #503207, CC0), frogs and
  toads, the insects and now and then a whip-poor-will, no cars; 60 s from 30 s in, high-passed at 90 Hz, the last 4 s
  cross-faded into the first (a 56 s loop), -30 dBFS RMS as the day's. Full on a clear night and after rain (the frogs
  call), 0.9 overcast, 0.35 under the rain; silent in the replay. Measured standing: the bed above 6 kHz 17 dB lower than
  the day's (no birds), the band from 2 to 6 kHz up.
- **The paint's beads** (`assets/shaders/road_paint_retro.gdshader`): at night a pass added over the centre and edge
  lines (the materials' `NextPass`, set in `ApplyHour`) sends the lamps' light back towards them, a lobe a degree or two
  wide at 6 times, so the lines shine down the road past the beams' pool; directional light (the moon) and the paint's
  own lighting are left to the lines' material. A wider lobe lit the near lines like neon.
- **The low beam's reach** (`CarView.BuildLamps`, `LowBeam`): aimed 1.5 degrees down with the cut-off under the axis, the
  pool ended 15 m out and the road beyond was above the cut-off; aimed 0.6 degrees down with the cut-off just under the
  axis it reaches 50 m, as a low beam's does, and the lamps came down to 20 (the drones' to 30).
- **The brake lamps** (`CarView.Braking`): at night the tail lamps' glow flares from 3.5 to 9 with the brake.

### Dusk (0.60.0)

- **The sky and the sun** (`tools/make-sunrise-sky.py ... sky_dusk.hdr 285`): the sunrise's photo turned to compass bearing
  285, the sun 5.85 degrees up behind the Tetons, whose ridge stands 7.5 to 9 degrees high from the circuit that way, so
  the land hides the sun and its glow spreads over the ridge (`WorldBuilder.DuskDirection`).
- **The land** (`tools/make-terrain.py`, grid version 3): a third sun's visibility, the dusk's: the circuit and half the
  far land lie in the range's shadow (it reaches some 19 km at 6 degrees). `RealLand` puts it in the vertices' second UV;
  `land.gdshader` takes it at dusk and lights the faces the sun still reaches six times the valley's weak light
  (`sun_gain`).
- **The light**: the directional light is the west's glow over a valley in shadow, deep orange (1, 0.55, 0.32) at 0.3;
  the clouds, kilometres up, still have the sun: their sun strength is six times, pink and orange underneath; the haze's
  sun scatter 0.3, exposure 0.95, the sky's light 0.85 and the ground's bounce 0.6 of the day's, the cabin 0.45; the
  glass's dust and arcs take a quarter of the glow (the sun is behind the range).
- **Lamps and sound**: dusk-to-dawn, the lamps are lit as at night (`WorldBuilder.LampsLit`: the cars', the train's, the
  yards', the paint's beads); the day's birds at 0.45 over the night's frogs and insects at 0.55.
- **The menu**: LIGHT steps day, sunrise, dusk, night, random (a draw of the four); the dusk is stored as 4.
- **Cost**: 12.5 ms a frame (an attract lap at 1080p).

### The wind on the car (0.61.0)

- **The air** (`VehicleSim.Wind`): the drag is now the car's velocity through the air, so a headwind adds to it and a
  tailwind takes from it; a crosswind pushes on the side, half the air's density times the side's area and cross-flow
  coefficient (`VehicleSpec.SideForceArea`, 4 m^2: a saloon's 5 m^2 side at about 0.8) times the air's sideways speed past
  the car times its whole speed, at the centre of pressure 0.35 m ahead of the centre of mass (`SideForceAhead`), so the
  nose turns downwind as the car is pushed across the road. Still air by default (the tests as they were).
- **The prairie's wind** (`Bootstrap.PrairieWind`): the way the grass leans (`wind.gdshaderinc`), 6 m/s on average (a
  Wyoming day's), gusting from a third of that to half again over seconds (three slow waves); still in the 1989 look.
- **Measured** (`CrosswindTests`, the hatchback coasting from 100 km/h, the wheel held): a 10 m/s crosswind moves it
  2.8 m across in 3 s and turns its nose 4 degrees downwind (6 m/s: 1.6 m); after 5 s a 10 m/s headwind has it at 90.0
  km/h, a tailwind at 95.8, still air at 93.3. The scripted laps, dry and wet, run as before without a crash.
- **Heard** (`BodyState.AirSpeed`): the wind's roar follows the air's speed past the car, not the road's: a headwind
  raises it and the gusts swell it; the roadside's echo keeps the car's speed.

### The traffic in the rain (0.62.0)

- **Spray** (`WheelDust.UpdateTraffic`): the drones have no wheels in the simulation, so their views' frames place two
  spray emitters behind each one's rear wheels (0.75 m out, 1.3 m back, at the road), driven as the player's from
  30 km/h to full at 120 by the road's wetness; made as the drones first need them.
- **Hiss** (`WorldAudio`): each drone carries the rain's hiss (`ambience/rain-outside.wav`) placed on it a third of an
  octave up, as loud as its speed throws water, shifted by the Doppler effect as it passes; silent on a dry road, in the
  replay and the 1989 look.
- **Lamps** (`WorldBuilder.CarLampsLit`): the cars' lamps are on in the rain whatever the hour, as drivers have them in
  the wet; the yards' and the train's keep to dusk and night.

### The thunderstorm (0.63.0)

- **Strikes** (`Thunderstorm`): while it rains, every 18 to 53 seconds a strike somewhere round the plain, 0.9 to 8 km off
  (900 + 7100 u^1.5 m: more often far than near); the flash at once, three strokes in a third of a second (bright, a dark
  gap, fainter, again, fainter still) through the colour adjustment's brightness (`WorldBuilder.SetFlash`), lifting the
  image by the strike's nearness times the hour's gain (`FlashGain`: 1.2 at night, 0.8 at dusk, 0.5 at sunrise, 0.35
  by day, when the night's whole turned the grey sky white); the thunder a second for every 343 metres later.
- **Thunder** (`assets/audio/thunder`, `tools/make-thunder.py`): near under 2 km (BlueDelta's heavy strike, #446753),
  middling under 4.5 km (BlueDelta's lightning and rolling thunder, #367702), far beyond (richwise's distant thunder,
  #361401), all CC0, played 6 dB quieter for each doubling of the distance past a kilometre on the world's bus, so from
  the seat they come through the glass. Never in the replay or the 1989 look; a clap under way when the rain stops plays
  out.
- **The drops at night** (`windscreen.gdshader` `glint`, `WorldBuilder.GlassGlint`): each drop's glint of the sky was a
  day's at every hour, and at night the drops stood on the glass like snowflakes; it is the hour's now (0.06 at night,
  half at dusk).

### The lightning (0.64.0)

- **The bolt** (`Thunderstorm.DrawBolt`, `assets/shaders/lightning.gdshader`): for a strike under 6 km a jagged line from
  the deck's base (700 m) to 30 m under the plain at the strike, split six times by midpoint displacement (each midpoint
  pushed sideways by up to a fifth of its segment), with two or three branches off its upper half; ribbons turned to the
  eye and added over the scene unfogged, sized in pixels of the 3D render at the strike's distance (the active camera's
  field of view, the window's height times the 3D scale; the stretched view's rect is its 1600 x 900 base): a
  flat-topped core three pixels wide, a branch's two and a half, halos four to six times as wide at a twentieth of the
  energy. Under two pixels the temporal anti-aliasing broke a line into dots. Its brightness follows the strokes (1,
  0.1, 0.7, 0.05, 0.4), a dim channel in the gaps. A far strike shows no bolt (the last near one's mesh is dropped).
- **The lit deck** (`volumetric_sky.gdshader` `flash`, `flash_at`; `WorldBuilder.SetFlash(stroke, strength, at)`): the
  stroke lights the deck from inside, 1/(1 + (r/1.2 km)^2) round the channel's top, and the haze towards the strike (a
  lobe of the bearing to the sixth power), by the stroke times the square root of the strike's strength times the hour's
  gain times 0.4 (in proportion a strike a kilometre off turned the sky white from edge to edge while one at five lit a
  patch); the colour adjustment's lift is the strength times the gain times 0.35 (0.63: times 1), so the land stands dark
  against lit cloud. Not in the sky's radiance map, whose incremental update would light the scene frames after the
  stroke; the car's reflection probe (`FollowCar`) is not moved while a flash is lit and is captured again once it is
  over, or a capture taken mid-stroke kept the lit sky in the paint for up to half a second. Under the pause menu the
  storm is checked with its clock stopped, so a change of weather or the 1989 look puts the flash and the bolt out.
- **The night under the deck** (`WorldBuilder.DeckDawn`, `dome_ground`): the night sky under the deck rendered black
  over pale mountains. The deck's and the fog's colours are sRGB (Godot converts both to light), so the night's factor
  of 0.05 left the deck's horizon about a hundredth of the day's light: they take 0.2 at night now, about a twentieth.
  The pale mountains came from the dome under the horizon, which was never set and kept the day's grey at every hour,
  at night many times the sky above: through the radiance map it lit the land from below, and with the fog's aerial
  perspective (0.8) it gave the far land four fifths of its colour, whatever the fog's own colour (set to black, the
  mountains did not change). It takes the hour's factor now; the day is unchanged, and at dusk and sunrise the land
  under the deck is a little darker against the sky, as it is at night. Measured on a fixed night view: the sky from
  0.5 to about 10 of 255, the mountains from 31 to about 11, a shade under the sky above them.

### Rain at night (0.65.0)

- **The drops lit** (`assets/shaders/rain.gdshader`, `CameraRig.SetRain`): the streaks' StandardMaterial3D (unshaded) is a
  spatial shader with the same fixed-Y billboard (BaseMaterial3D's code for it, the particle's scale kept) and the same
  fade from 1.2 to 4.5 m, lit: the sky's light by day as before (the streaks measured the same on a fixed view, 113 of
  255 against 114), at night only what reaches them. Its light(): lambert, light through from behind (0.85), and for a
  lamp (not the sun) a retroreflection lobe, 5 times pow(L.V, 12): a drop is a small round lens and sends a lamp's light
  back the way it came, as the paint's glass beads do, so the rain in the car's own beams shines. Unlit, it stood white
  in the dark.
- **The rain ahead of the car** (`CameraRig.LeadRain`): the drops fall in the world, so at speed those down at the eye's
  height and the beams' had been let go a second earlier where the car had since left; ahead of a car at 30 m/s there
  was only the fresh rain high up (from the seat the air looked dry by day, and at night the beams lit nothing). The
  emitter leads the camera by its velocity (smoothed over 0.2 s, a jump of the view ignored) times 1.2 s, the time a drop
  takes from the box to below the eye.
- **The beams in the air** (`WorldBuilder.RainNightAir`): in the rain at night the volumetric fog is 0.0012 per metre
  (0.00035 otherwise under the deck), so the lamps' cones show; and its sky affect is 1 there (0.1 otherwise): with the
  tenth, the beams' lit air veiled the far land but not the sky above it, and the mountains stood pale against the sky
  again. At 0.0025 a far strike's glow lost half its brightness through the veil and the cockpit looked foggy.
- **The strike's light** (`Thunderstorm`, `WorldBuilder.FlashLight`): an OmniLight3D 500 m up the channel while the
  strokes last, no shadow, range 16 km, attenuation 2 (Godot's inverse square), its energy the light a kilometre off times
  a million; the plain round a near strike, its fences and the wet road light up, the mountains behind stay dark (a
  directional light first tried lit them as brightly as the road; at 330 m up the ground under it was a hot spot).
- Night rain on an attract lap: 11.1 to 11.6 ms of GPU time a frame, 60 frames a second.

### Night from the seat (0.66.0)

- **The mirror graded once** (`WorldBuilder.MirrorEnvironment`): the rear-view mirror's picture is drawn in the main view,
  which tone-maps, exposes and colours it again; its environment was a copy of the main one with AgX, the night's
  exposure of 1.35 and the colour adjustment, so the picture was graded twice, its darks lifted twice, and at night the
  mirror was the brightest thing in view. Its environment is linear now, exposure 1, no colour adjustment, and the
  weather's exposure, the 1989 look's tone mapper and the lightning's lift leave it alone (the main view's grading lifts
  the mirror's picture with everything else). By day the mirror reads about the same (60 against 58 of 255 on the same
  frame); on a clear night its sky reads 6 to 11 against 8 to 17 through the windscreen. Highlights clip at white in its
  8-bit picture.
- **The tail lamps' red** (`CarView.BuildLamps`): a spot aimed back and down (70 degrees, 4 m, energy 0.6) instead of
  an omni light 3.5 m round, which lit the cabin red through the rear window, the seats and the pillars glowing at
  night; the road behind keeps its red pool.
- **The wiper blades** (`windscreen.gdshader` `cabin_light`, `WorldBuilder.CabinLight`): their unlit day grey scaled by
  the cabin's light, so at night they are black against the sky and not light-grey bars lifted by the night's exposure.
- **Review fixes for 0.65** (a two-lens review workflow with adversarial checks): the lightning's light set off the
  marker posts' retroreflector lobe (gain 400, power 100, made for lamps beside the eye), and every post opposite a
  strike flared like a lamp out to the horizon; the reflectors are on a layer of their own (`WorldBuilder.ReflectorLayer`)
  that the lightning's light leaves out. A strike could start on the pause menu's stopped clock (the clock runs on
  through dry credits, so one was overdue the moment WEATHER went to rain) and hold its first stroke and its light at
  full behind the menu; no strike starts with a dt of 0 now, and the first of a rain comes 6 to 16 s into it. The rain
  shader's billboard and lobe passed.
- The design review that picked these (a four-angle proposal workflow and a judge reading the code) also ranked, for later:
  lamps blooming on the wet glass, road paint that reads as paint, a true-size mirror and live door mirrors, every lamp
  streaking in the wet road, rev-matched downshifts, grounded trees beyond the shadows, per-car traffic engines, and
  range-land colour on the far plain.

### Road paint (0.67.0)

- **The paint's shader** (`assets/shaders/road_paint.gdshader`, `WorldBuilder.PaintShader`): the centre and edge lines were
  StandardMaterial3Ds of their vertex colour, flat quads with ruler-straight edges, the decal look. Two shared shader
  materials now (the centre line dashed, 0.16 m; the edge lines 0.15 m, greyer towards the verge), when the ground's
  shaders are on and the asphalt's photos are there (the export keeps the flat paint): the asphalt photo on its 5.5 m
  tile in world space gives the paint the stones' relief (0.85 to 1.04 of its colour by a texel's brightness against the
  tile's mean, its last mip) and wears it through to the tar (the photo times the road's 0.52) in the hollows, a tenth to
  a fifth of it by stretch; a sprayed line's edges, ragged by up to 6 mm by alpha scissor, smoothed away where a pixel
  spans more than that; the centre dashes cut to exactly 3 m every 9 m (the mesh's ends fall on its frames) with ends
  ragged by up to 2 cm; the asphalt's normal map at 0.7; roughness 0.62 dry to 0.25 in the rain (only the edge lines
  followed the wetness before). The night's glass beads stay a next pass over them; the 1989 look keeps the flat paint.
- The edge lines' left side now runs u from the road's side (0) to the verge's (1) as the right side does
  (`Quad(..., uLeft: 1, uRight: 0)`), for the greying.

### Rev-matched downshifts (0.68.0)

- **The blip** (`physics/Vehicle/Drivetrain.cs`, `RevMatch`): in the assisted modes (the automatic and the assisted
  clutch) a shift lifted the throttle to zero, and on a change down nothing raised the revs: once the shift's open-clutch
  time was over, the clutch took the engine up to the lower gear's shaft speed through the driven tyres, which were
  already braking. Now, while the shift's clutch is open, a change down blips the throttle when the lower gear's shaft
  is more than 150 rpm above the engine, with a proportional governor aimed just under the shaft's speed (full 310 rpm
  short, closed 60 short, capped at 0.98 of the redline), and the take-up keeps blipping while the engine is still below
  the shaft (a dual-clutch change ends before the blip has done its work). A change up still lifts; the manual clutch is
  untouched. The engine's throttle carries the blip to the voice and the rev counter.
- Measured (the 205 at 100 km/h under half braking, third to second): before, the axle's torque peaked at 2892 N m and
  the clutch had not locked after 1.2 s; now 197 N m, locked after 0.28 s with the engine 27 rpm from the shaft. The
  Testarossa at 120 km/h, third to second: the lower gear is past the redline, and it is unchanged (2394 N m).
  `physics.tests/DownshiftTests.cs` (the change down blips and locks close to the shaft; it fails without the blip;
  a change up still lifts).

### The road paint's review (0.68.0)

- The centre line's dashes are built to exactly 9k and 9k + 3 m (`RoadFrame.Lerp` at the dash's ends): the mesh began
  each dash at the first frame inside it, up to a metre late, and 0.67's shader cut its end at 3 m, so the dashes ran 2
  to 3 m, square at the start and ragged at the end. The fray now fades by the pixel's footprint across the line (6 mm)
  and along it (the 4 cm cells), not at a tenth of the line's width across it only (the edges fizzed on Low, without
  TAA); the worn specks take the wet road's 0.15 in the rain, not the dry 0.52; the fray noise's input wraps every 164 m
  (a sine hash fed thousands of units turns periodic). The night beads still draw the whole quads: their few-millimetre
  slivers past the fray were left.

### The traffic's brake lamps (0.69.0)

- **Brake lamps** (`Bootstrap.SyncTraffic`): the drones slow when the player or a slower drone is ahead in their lane
  (`Traffic.Step`, braking at up to 7 m/s^2), but only the player's car ever lit its brake lamps. A drone's view now
  brakes while its speed falls faster than 1.2 m/s^2 between frames, held 0.4 s so that the lamps do not flicker
  (CarView.Braking: the lamps' emission, and at night the tail glows' flare). On an attract minute the drones kept their
  cruising speeds and none braked; it shows when a faster drone closes on a slower one or on the player.
- **The paint's review, second pass**: the worn specks take the road's own albedo colour as an sRGB uniform
  (`tar_tint`, set with the wetness), where 0.67's 0.52 and 0.68's mix of 0.52 and 0.15 were treated as light and left
  them about twice the road's brightness dry and up to eight times wet; the edge fray's noise is steadied to its mean
  (a constant 3 mm inset) where a pixel spans more than half of its 4 cm cells along the line, instead of 0.68's fade,
  which took the fray away from a few metres out; the dash ends' fray is not faded at all (one value per dash, steady).

### The replay tune in the 1989 look, matte tyre marks, the line-up (0.70.0)

- **The replay tune** (`EngineAudio.PlayJingle`): a tune in the user folder (0.11, `user://music/replay.*`) now plays in the
  1989 look too; until 0.70 that look always played the synthesised score, and a tune of the player's own was never heard
  there. `_replayMusicIsOwn` tells the player's tune from the game's recording, which in the 1989 look still gives way to
  the synthesised score. The owner's tune (the arcade's own) is only in the user folder of the owner's machine, never in
  the repository or a release (ISTERLER S3). Checked by recording the master bus (`--audio-dump`) through a crash's
  replay (`--ram=test`): the recording's envelope (10 ms RMS frames, normalised cross-correlation) matches the tune's at
  0.98 and the game's own tune's at 0.30; in the 1989 look (`--retro --jingle=Replay`) 0.74 against 0.20, with the
  pulse-train engine under it.
- **Matte tyre marks** (`SkidMarks`): roughness 1 and no specular at all (`MetallicSpecular` 0, `SpecularMode.Disabled`).
  At 0.42's roughness of 0.55 and the default specular the strips reflected the sky at a grazing angle more than the
  rougher road round them and read as white streaks from the seat; Godot's f90 follows f0, so with f0 = 0 neither the
  lobe nor the Fresnel rise is left.
- **The line-up** (`Lineup`): the title and the showroom step through ten cars. The saloon (the VAZ-2106 model) and the
  three lofted cars13 bodies are withdrawn, as the owner asked, but stay in the catalogue, whose indices the physics
  tests, the saved settings and the paint book use; a saved choice of a withdrawn car moves on to the next. The traffic
  draws every car with a model (eleven, the Ferraris in their own red, the others in the road's twelve colours), all
  requested from the loader thread at the start as before.

### The far clouds steady, the menu without the replay music (0.71.0)

- **Mip chains for the clouds' noises** (`CloudNoise`, `volumetric_sky.gdshader`): NoiseTexture3D makes only the full-size
  level, so the cloud march sampled the finest noise at every distance. Towards the horizon a quarter-resolution pixel
  spans some 150 m and a step of the march up to 230 m, against texels of 54 m (shape) and 23 m (detail), and as the
  clouds drifted their billows slipped in and out of the samples: the far clouds flickered. `CloudNoise` makes the same
  seamless noises (FastNoiseLite's own GetSeamlessImage3D, blend skirt 0.1, normalised) on a worker thread and box-averages
  every level down to one texel, in the order `ImageTexture3D.Create` takes them (each level's slices in turn); the
  texture object is handed out at once and filled when the worker is done. The shader reads the shape, the detail and
  the weather map with `textureLod` at the footprint of each sample: the larger of its pixel across the view
  (`t` times the pixel's angle, from the derivatives of the eye direction taken before any branch) and its step along
  the ray; the sun's four light steps read at least 80 m. The cloud shade on the CPU (`CloudShadows.Capture`) takes the
  full-size level from `CloudNoise.Voxels`; read back from the texture it would now get every level's slices.
- Measured with a fixed camera on the horizon (ten consecutive frames, mean absolute second difference in time): rows
  400-560 of a 1080p view from 0.47 to 0.30 grey levels, the sky above from 0.09 to 0.08; the near clouds look the same
  and the farthest a little softer. The horizontal bands a fixed-step march leaves inside far clouds are unchanged.
- **The menu** (`PauseMenu`): the REPLAY MUSIC line is gone at the owner's wish; the tune in the user folder plays (0.70),
  and a music file dropped on the window still becomes it, without opening the menu.

### A steel loop you see through, no stuck sounds, the 0.70 review (0.72.0)

- **The loop's grating** (`WorldBuilder.BuildLoopDeck`, `loop_grating.gdshader`, the owner's P16): the loop's frames off
  the ground (`IsGrating`: a Loop frame whose up is more than 16 degrees off the vertical, the earthworks' rule) get a
  strip of their own, UV in metres (across, along from the grating's start), drawn by a shader that computes a hot-dip
  galvanised bar grating per pixel: bearing bars along the road 10 mm thick, 76 mm deep every 60 mm, cross bars 6 mm
  thick, 10 mm deep every 102 mm, 78 % open from above. Each set of bars is a pulse train whose coverage of the pixel's
  footprint is an exact box filter of its running integral (the footprint widened 1.6 times against moire); the bars'
  depth is taken into account (the ray's sideways walk inside them), so the deck closes at a grazing angle, stays open
  along the slots and shows the bars' side walls darker. The opacity is blended (the transparent pass): alpha hash,
  tried first, left the far deck as white specks the temporal anti-aliasing did not settle; alpha to coverage needs
  MSAA, off by default with the motion blur. Blended, the grating casts no shadow (it would throw a solid one); the
  steel under it does. The asphalt ribbon, the centre line and the tyre marks stop on the grating; its two ends on the
  earthworks stay asphalt, untinted. Under it the deck box keeps its kerb plates and side girders, 0.8 m deep, without
  the soffit, and gains two stringers under the wheel tracks and a floor beam at every frame. 1989 look: flat navy with
  a one-pixel column dither, opaque, as the arcade's footage shows. The physics is unchanged. Method after the 8 November
  research (loop-grating.md).
- **The car heard only while it is driven** (`EngineAudio.Update`, `TyreAudio`): the replay shows a frozen wreck, a
  finished credit's session is not stepped (`RaceSession.Step` returns in `Finished`) and the showroom over a paused
  credit does not step it, but the engine kept its last revs, the tyres their last slip and the wind its last speed:
  a credit that ran out at 91 km/h left -14 dB of roar to the end of the recording, and the replay began with the
  wreck's squeal. Now `live` is `StartEngine` or `Driving` and not paused; otherwise the engine is off, the structure
  echoes silent and the tyres and the body noise muted, the tyres eased over a tenth of a second (`_level`). Measured
  with `--audio-dump`: after the credit's end -27 to -31 dB (the game-over tune and the world), the replay's first
  second -38 dB instead of -22. `EndMusic` on every new session fades a jingle still playing (the game-over tune ran on
  into the next credit past the menu's NEW GAME) and the replay's tune.
- **The 0.70 review's fixes**: `AdoptReplayMusic` decodes the dropped file before anything in the folder is touched,
  copies it in under a temporary name and leaves the folder's own file alone when it is dropped back (both deleted the
  owner's tune before); a tune taken while the replay's music plays is put in when that music ends (it was dropped);
  the traffic's McLaren takes the road's colours (only the Ferraris keep their red); `Showroom.Create` keeps any car
  automation names, and the line-up steps from a withdrawn car to its neighbours.

### A clear ghost, the banked 180's gentle ends, nothing on the low bridge (0.73.0)

- **The ghost** (`CarView.MakeGhost`, `ghost_depth.gdshader`, `ghost.gdshader`, `Bootstrap`): each car keeps its own best
  lap (`user://bestlap-<style>.bin`; the old shared `bestlap.bin`, of an unknown car, is left for the versions before and
  no longer raced), so the ghost is the lap set in the car it is drawn as. A `Label3D` over it says what it is ("BEST LAP
  1:26.8  STUNT"), hidden within 6 m. Its look: the cabin's own parts (`CabinMeshes`, the classification the traffic's
  cabin trim used), the lamps' lights and any decal or particles go; every mesh gets a depth-only pass (render priority
  -10, drawn 3 mm inside the surface) whose next pass is the visible skin (priority +10): a cool translucent shell with a
  Fresnel outline, faded within 2.5-8 m of the eye. So each pixel shows only the ghost's front-most surface; before, one
  translucent material over every part showed the seats, the cabin and the wheels' insides through each other. The
  ghost's depth hides blended objects behind it (glass, particles): accepted.
- **The banked 180** (`TrackBuilder.TransitionShare`, `OriginalInspiredTrack`): the spirals' share is a cosine-ramped
  trapezoid of the roll rate (half-cosine ramps over the first and last quarter, point-symmetric, so each spiral still turns
  half of an arc of its length), and the spirals are 100 m. Per frame interval the roll rate changes by at most 0.027
  degrees a metre (0.426 with 0.34's linear share, at four places) and the outer edge's grade by at most 0.51 % (8.18 %);
  the peak roll rate is 0.34's, 12.8 degrees a second at 108 km/h. The bend is 294 m (269), its exit 0.46 m from where
  it was, so only the distances after it grow by 25 m. Measured in the research's probe (the game's physics, a held 108
  km/h): 9-11 m/s^2 felt at the ends instead of 20-26, 1-2.6 degrees a second of roll instead of 15-20. Test:
  `Banked_180_rolls_in_and_out_without_a_step`. The Unreal export (`--export-world`) has to be run again for its level.
- **Decks in the terrain** (`TerrainField.DeckHeight`, the dressing builders): the frames of the bridges, the drawbridge's
  ramps and the loop are indexed by cell; `Height` keeps the ground 0.12 m under the lowest deck over a point (the
  stunt route's low return-leg bridge, 0-0.84 m over the plain for 60 of its 90 m, had the ground up through its asphalt
  by 1.8 cm), and the meadow, the tufts, the fences (2 m margin) and the power poles (1 m) skip any point under a deck
  less than 2.5 m over the ground: 5 fence bays, 19 meadow cards and 3 tufts had stood on that bridge. They also keep off
  the railway's ballast (`Train.OffsetFromLine`; 4 fence bays crossed the line, 66 cards and 25 tufts stood on it). The
  flower bed after the bend, 6.7 m from the cutting's centre line since 0.34, is 17.9 m clear with the longer bend; a test
  holds every bed 12.5 m clear. Tests: `Ground_stays_under_every_deck`, `Flower_beds_stay_clear_of_every_road`. Method
  and the placement replay: the 8 November track research.
- **Licences**: at the owner's word of 8 November 2026 BlendSwap models marked Fan Art (no commercial use) and CC BY-SA
  ones may be used, the restriction written in THIRD_PARTY.md (DEVAM.md, ISTERLER A8).

### The Godot cars in the Unreal level, ray tracing, the smoke and the ghost's clock (0.74.0)

- **The owner's decision (8 November 2026):** photorealism comes first, so Unreal Engine 5.8 becomes the main engine, step
  by step; the C# simulation stays as it is (the NativeAOT DLL), Godot keeps generating and exporting the world and keeps
  being released. The evaluation behind it: C:\temp\log\grudgelap\v74\engine\ (Unreal 5.8 has hardware Lumen, DLSS
  RR and FG, MegaLights, Substrate; Godot 4.7/4.8 has no DLSS or hardware ray-traced effects; Unity HDRP gets no new
  features).
- **The Unreal cars** (`unreal/Scripts/prepare_model_car.py`): the Godot game's prepared model replaces the export's
  lofted car whole, cabin included (`ModelBody`, `Wheel0..3/Spin`, `Steering/Spin`, `Eye`, `Dials`, `RearMirror`); the
  meshes of each group are baked into one per material (a 205 from 160 actors to 7: the render thread went from 19.3 to
  16.5 ms a frame with eight drones), unreachable nodes are pruned (left in, their names made the importer suffix the
  model's), the binary chunk is compacted. The cockpit camera starts at the model's eye; the paint is a clear coat over the
  model's colour or texture. Details: `unreal/README.md`.
- **Hardware ray tracing and DLSS Ray Reconstruction** (Unreal): Lumen traces the geometry and lights reflections at the hit
  point; on an RTX card the game turns on DLSS-RR through the plugin's `UDLSSLibrary` (`-GlpNoRR` to compare); the grass and
  the bushes stay out of the ray-tracing scene. Measured at 1080p on the RTX 5060 Laptop: 16.5-17 ms a frame (DLSS with RR
  3.1 ms, Lumen reflections 2.6 ms); the render thread is the limit (5-7 ms waiting on visibility: 230 000 grass instances).
- **The Unreal verge** (`M_Shoulder`): the Godot verge's painted stones (shoulder.gdshader, P12) in one Custom node; its
  colours are the shader's sRGB constants made linear (taken as linear they read pale yellow).
- **The smoke out of the cabin** (`WheelDust`): the puffs fade within 1.5-3.5 m of the eye (PixelAlpha distance fade); a
  sliding rear tyre's 3 m puffs had reached into the cabin.
- **The ghost on the lap clock** (`LapRecording.TryPose`, `RaceSession.RecordLap/OpenLap/RecordedLap`): samples are found
  by their stored time (binary search), the recorder keeps its leftover time, a lap opens with a sample at 0 s and closes
  with one at its own time; the file format is unchanged, so old best laps replay at their own times. `KeepsBestLaps`:
  attract laps without `--save-best` and laps under `--no-ghost` are no record and never overwrite a file;
  `FormatLapTime` carries a rounded 60.0; `FitGhostLabel` keeps the label at least 10 pixels of the 3D picture (the 1989
  look). Tests: `physics.tests/GhostTests.cs` (312 pass).

### The cars made truer, and the Unreal loop's grating (0.75.0)

- **The model fixes** (`tools/prepare_car_models.py`, `tools/car_models.json`; the investigation and its renders:
  C:\temp\log\grudgelap\v73\fix\ and v74\models\): `own_material` (parts drawn in an untextured copy of their material,
  for a colour a texture atlas has wrong: the 205's rims, a flat red in the atlas its tyres share, now the GTi 1.9's
  silver-grey, its calipers dull iron); `turn` with `turn_pieces` (the Renault 12's lower door panels, modelled inside
  out on mirrored nodes, turned outwards: the black band on its doors was the lining the paint shader draws on a back
  face inside the cabin box); the spec-gloss corrections (an untextured specular-glossiness material the entry corrects
  is rewritten as metallic-roughness: the Esprit cabin in black leather); `steering_part` (the Clio's own cabin with its
  wheel cut out to turn); the Golf's tail lamps as the brake lamp's red lens with the R32's smoked band; RainerS's 458
  converted by `tools/convert_rainers_458.py` (BlendSwap #5151, CC BY 3.0; `fetch_models.py` and `CarModelSourceTests`
  accept its BlendSwap page).
- **The Unreal cars' materials** (`prepare_model_car.py`): `CarView.CorrectModelMaterial`'s rules ported (an untextured
  material at the exporter's metallic and roughness of 1 is a matt dielectric, rubber, wing-mirror glass, white rear-lamp
  blocks, grey painted wheels); specular-glossiness read as Godot's importer reads its factors (diffuse, glossiness,
  metallic 0); dark dielectrics given a grained surface (roughness 0.8; under Lumen the dashboards mirrored the sky).
  The paint is two-sided, its back faces inside the cabin box (CarView.SetPaintCabin's, from the model's eye) the dark
  lining. Known: the dashboards still read lighter than in the Godot game.
- **The Unreal loop** (`M_LoopGrating`): the Godot grating's per-pixel bars in one Custom node, the coverage dithered in
  time (InterleavedGradientNoise) and resolved by DLSS or TSR, masked, so the deck casts its ladder of shadows. The
  cuttings' earth is Poly Haven's dirt_floor, grey-brown, instead of a mud scan tinted orange.

### The BRZ's automatic, the ghost's depth, and the Unreal roadside, glass, dust, rain and wind (0.76.0)

- **The BRZ as the owner's European automatic** (`physics/Vehicle/AutomaticGearbox.cs`, `Drivetrain.Converter.cs`): a
  car's own automatic (`VehicleSpec.Automatic`, only the BRZ's so far) drives through a torque converter (stall 2,540 rpm,
  torque ratio 1.95 at stall falling to 1 at a speed ratio of 0.85; the stall speed from an aftermarket listing quoting the
  factory figure, the ratio from a Toyota A960E description seen only in a search summary) and a lock-up clutch (4th-6th
  above 45 km/h), the A960E's ratios 3.538 / 2.060 / 1.404 / 1.000 / 0.713 / 0.582 on a 4.100 final drive (Subaru's
  sheets; EPA test data). In D the pedal asks for a drive at the wheels (pedal^1.75 of the engine's peak power, a force
  below 36 km/h; the FA20's computer maps the pedal to a torque demand) and the box changes up only into a gear that can
  give it, down when the gear in cannot; kickdown at 95 % pedal; a car rolling back holds first. The research's
  corrections in every mode: per-axle wheel rates for 1.43 / 1.47 Hz, bars 26,500 / 12,700, the EPA coast-down's road
  load, a 0.3 s brake build-up, the torque curve's dip at 4,000 rpm, the manual's launch at 4,500 rpm
  (`EngineSpec.LaunchRpmOverride`). 0-100 km/h 8.21 s (Subaru 8.2), 212 km/h (210), 100-0 km/h 38.6 m. Every other car's
  test output is unchanged.
- **The ghost** (`CarView.MakeGhost`, `ghost.gdshader`, `ghost_depth.gdshader`): its depth pass at render priority 9, just
  before its skin (10), after every ordinary blended surface, so the grating, the rain, the glass and the tyre marks behind
  it show through it (at -10 it hid them, and the faded ghost near the eye still cut its hole in the rain); both passes draw
  both faces and push the depth 3 mm away from the eye along the line of sight, so inward-wound and mirrored parts (the
  estate's glasshouse and doors, the Testarossa's tail lamp, the BRZ's liners) are no longer holes. The 0.73 note that
  the ghost's depth hides blended objects behind it no longer holds. In the 1989 look the loop's deck still covers the
  ghost's label at the top of the loop (both blended at priority 0; the deck drawn opaque in that look is the fix, next).
- **The Unreal glass** (`prepare_model_car.py`, `Merger.drop_inner_panes`): a window modelled as two or three parallel
  layers keeps its outermost; Unreal's thin-translucent glass stands for a whole pane, and its path length is 1/cos of the
  incidence (not of the refracted ray), so through the Testarossa's windscreen, 72 degrees to the eye's line, two layers
  passed a ninth of the light.
- **The Unreal effects** (`AGlpGame`): the wheels' dust, smoke and wet-road spray as `WheelDust`'s three kinds (and the
  traffic's spray), a CPU ring of camera-facing instances with per-instance opacity and colour (`M_Puff`: lit, a distance
  fade near the eye and a soft edge at the ground, both smoothsteps as Godot's); the rain as `CameraRig`'s 3600 lit streaks
  in a box led ahead of the camera (`M_Rain`, now soft-referenced so the package cooks it: the 0.75 package had no rain);
  the tyre marks drawn before every other translucent surface, the puffs and the rain after the car's glass from outside
  and before it from the seat.
- **The Unreal wind** (`build_scene.py`, `WIND_HLSL`): `wind.gdshaderinc` as a world position offset on windy copies of
  the scanned plants' glTF materials, each vertex leaning by the square of its height over the instance's foot (stiffness
  1 for the grass, 3 for the bushes, 9 for the trees); off beyond 30 / 50 / 150 m and not invalidating the virtual shadow
  maps. Measured 16.45 ms a frame against 16.2 before it.
- **The Unreal camera** (`GlpCamera`): `CameraRig`'s 11 Hz seat vibration (smooth noise; it was a fresh random offset every
  frame), the knock's jolt spring, the chase eye rising towards the loop's centre with the car's tilt.
- **Tried and dropped:** the volumetric clouds' shadows on the ground (the sun's `cast_cloud_shadows`): no measurable change
  with the template's clouds.

## Unreal Engine version (`unreal/`)

- Started on 23 October 2026, after a feasibility test on the start straight. With the same viewpoints and
  sun as the game, Unreal 5.8 with CC0 scans was clearly closer to a photograph: Nanite grass and trees,
  Lumen, a physical sky with volumetric clouds. It ran at 70 fps at 1920×1080 on the development laptop
  (RTX 5060 Laptop, cockpit view).
- **The simulation stays in C#.** `physics.native` publishes it with NativeAOT as
  `GrudgeLap.Physics.Native.dll`, a C interface of `glp_*` functions (`include/grudgelap_physics.h`). The
  `GrudgeLapUE` module loads it and steps a session every frame.
- **Coordinates.** The game's glTF export brings the track and the car across; the simulation's (x, y, z)
  in metres is Unreal's (x, −z, y) in centimetres.
- **The level is generated, not hand-edited.** `Scripts/build_scene.py` builds it from the export and the
  downloaded scans, so `Content/` is not versioned. What the export needed is recorded there and in
  [unreal/README.md](../unreal/README.md): hidden nodes, rewound primitives, vertex colours, foliage
  cut-outs and Nanite area preservation.
- **Towards a photograph (0.13, after the user's verdict that the game was "far from real life"):** the road no
  longer repeats (a second rotated sampling of the scan blended by a slow noise, a 57 m brightness wobble, the
  160 m wear sheet) and carries 300-odd scattered decals of cracks, patches, oil, skid marks and potholes along
  the exported centrelines; the weather of the credit reaches the level through `MPC_Weather` (a wet road
  darkens and mirrors, with puddles from a world-aligned mask) and `AGlpGame::ApplyWeather` (sun, clouds, fog,
  exposure, a rain sheet round the camera); a photographic finish (grain, fringing, flare, bloom, motion blur);
  the chase camera's longer lens; and real CC BY car models in place of the lofted bodies
  (`prepare_car_model.py`: turned, scaled to the simulation's wheelbase, hubs at rest height, wheels re-centred on
  the game's own pivots; the game's cabin kept). The traffic is the everyday cars when the level has them.
- **The game runs in Unreal** (`AGlpGame`): the title screen with the car and gearbox choice (both cars:
  the V8 is taken from a second export with `--car=v8`), the keyboard with the manual gearbox, traffic, the
  ghost of the best lap, the train, signs that tip over, the HUD and the instrument panel (drawn on the
  binnacle and at the foot of the screen), the rear-view and outside mirrors, the gamepad with the Godot
  version's layout, the high-score table and its entry of initials (`GlpScores`, the same text form), and
  the Godot version's sound mixed from the same recordings: the crash recordings, the shifter's clunk and
  the train's steam loop, mixed in by its distance (the drones' placed engines and the Doppler shift are not
  ported; the mono mix has no listener). Settings, the best lap and the high scores live in
  `unreal/Saved/GrudgeLap`; the best-lap file is the simulation's own format, the same as the Godot
  version's. ABI 5 of the core added the credit's own best lap to the state for the table. On a screen the
  game holds the Godot version's frame cap (`AGlpGame::ApplyFrameCap`: an even fraction of the primary
  display's refresh rate, at most 90, 30 without the focus) and hands the editor its own back at the end of
  a Play; the off-screen automation runs keep theirs.
- **Scenery.** Scanned trees stand where the game's trees stand all round the circuit and, as the wood
  beyond the fields, where the game's forest placements are (an instanced component per scan, drawn to
  2.5 km); scanned grass fills the meadow at the start and the 40 m beside every road (about 250 000 tufts,
  drawn to 300 m). The loop's steelwork and the drawbridge's towers come across in the export as meshes.

## Conventions

Simulation space is right-handed, +Y up, forward +Z, right +X, heading positive to the right; Godot's
forward is -Z, so the view flips Z and nothing else. Units are SI; speeds are shown in mph by default.
Settings and the best lap live under `user://` (`%APPDATA%\Godot\app_userdata\GrudgeLap`).

## Release

`tools/release.ps1 -Version x.y.z` stamps the version into `project.godot`, the csproj and the Inno script,
runs the tests, exports, builds the installer and the portable zip with their SHA-256 files, commits, tags,
pushes, creates the GitHub release, updates the Scoop bucket and submits the winget manifest. CI
(`.github/workflows/build.yml`) repeats tests, export and installer on every push. Since 0.6.0 every
version goes out as a GitHub release again (`-NoStores`), because the game updates itself from there at
start-up; the installer script is written UTF-8 with a byte-order mark (`release.ps1` stamps the version that
way), because Inno Setup reads a script without one in the system's ANSI code page and the Turkish screens of
the installers from 0.6.0 to 0.9.0 showed their letters double-encoded. Every release also carries `update.json`
(version, tag, and each package's exact name, size and SHA-256), which the updater reads first; the release is
created with `--latest` said outright, and the script then reads `releases/latest/download/update.json` back until it
names the new version (and stops with an error otherwise). Games up to 0.76 read the API and ignore that asset. The
updater keeps one package at most in its download folder (see the Updater notes). Scoop and winget wait for the
stable 1.0, which will be the first winget submission (the earlier new-package requests were withdrawn).

## Roadmap

1. Force-feedback wheel support through DirectInput (torque = model force + damping + friction + centring,
   the recipe of the expired 1991 patent); until then, keyboard only.
2. A cleaner twelve-cylinder recording for the 1984 car if one with a usable licence turns up; for the fleet saloon, a
   recording of an OM 651 (or any diesel four) pulling to its governed speed under load and lifting from there, under
   CC0 or CC BY (its bank's run-up ends at 3,425 rpm and its overrun at 2,000).
3. A longer overrun recording for the twelve (its bank is one second long); a steam locomotive recorded
   closer than the Welsh one (binaural from a carriage window) if one turns up under CC0.
4. More circuits built with the same turtle API; the hidden skid pad could grow cones and a timer.
   The hatchbacks in the Unreal version: an export of each (`--car=`) and their entries in `build_scene.py`.
5. Scanned models instead of lofts and primitives: a car body with real curvature (the loft has twenty
   design points per section, three of them filleted) and buildings with real geometry would be the next
   step past textured primitives. A reflection probe riding with the car was tried in 0.5 and left out:
   with SDFGI and screen-space reflections already on the paint the difference was hard to see, while a
   real-time probe renders the scene six more times a frame.

## 0.10.0: knocks, cows, everyday cars, roads, buildings, the cabin

- Knocks (`RaceSession.Knocks.cs`): the car's plan (a rectangle from `VehicleSpec.BodyFront`/`BodyRear`/`BodyHalfWidth`)
  is tested against the solid props', the drones' and the cows' with the separating-axis test (`Footprint`). Below
  the crash speed (15 m/s closing, 54 km/h; 20 m/s for a cow) the car is pushed out of the overlap and bounced off
  with an impulse (restitution 0.3 to 0.08, friction 0.35 along the face) that also turns it; the damage grows with
  the square of the closing speed (0.4 at the crash speed) and past a third of the way costs up to 30 % of the
  engine's torque; at 1 the knock is a crash. Landings between 6.5 and 10 m/s dent the car instead of wrecking it.
  The view dents the car where it was struck, sparks off hard knocks, steams past 60 % and flashes the screen's
  edges; the HUD shows a damage gauge. A crash repairs the car after its replay.
- Drones keep their distance along the road (measured as distance along the traffic loop, so a bend does not hide a
  car): no faster than they can stop from at 4 m/s^2 before a 3 m gap, settling to a 1.2 s time gap; they brake at up
  to 7 m/s^2. A knock changes a drone's speed by its share of the impulse along its way.
- Cows (`Herd`): 650 kg bodies 2.45 by 0.75 m; shoved, they skid at 4.5 m/s^2 and turn, then trot off at 2.6 m/s for
  3.5 s; a car closing faster than 8 m/s within 14 m makes one shy aside. Each moos every 12-35 s while the car is
  within 70 m. The model (`CowBuilder`) is lofted to a Holstein's measure with patches painted from 3-D noise, and
  grazes, walks, staggers and swishes its tail (`CowRig`).
- Everyday cars: `VehicleSpec.Saloon` (after the Tofas Sahin: FIA form 5608 for the engine, the Tofas five-speed
  ratios), `Estate` (after the Renault 12 Toros: FIA form 5396) and `Fleet` (after the Fiat Egea: Fiat's handbook;
  since fleet the Mercedes-Benz E 250 CDI of 2013-15 the model shows, the dynamics audit's item 20); 0-60 mph 13.1 /
  16.2 / 12.1 s and 99 / 89 / 115 mph in the tests (the E 250 CDI: 7.83 s and 152 mph). Bodies in
  `CarView.Everyday.cs`, steel wheels under plastic covers; the traffic is these three in turn, and since 0.21 the 205
  and the Golf too, in twelve road colours
  (their cabins drawn only near).
- Roads: grey-brown gravel shoulders (ambientCG Ground110) and brown soil on the earthworks, white edge lines and
  marker posts every 50 m; 1989 mode restores the orange and hides the lines and posts.
- Buildings (`BuildingBuilder`): the farmhouse (plinth, corner boards, shingled gable roof of real thickness, framed
  windows with shutters, porch, chimney, gutters), the gambrel-roofed barn with its braced doors and cupola, the filling
  station with its canopy and price sign, and the pumps.
- The cabin (`CarView.Cabin.cs`): two shadowless fill lights stand in for the light bouncing round a cabin (seen from
  the seat only), the A-pillars are trimmed panels, the dashboard has a top pad and a lighter fascia with vents,
  glovebox and trim strip, the doors have their trims, the seats bolsters and headrests, and the airbag cars a padded hub.
- Weather (0.12, `physics/Race/Weather.cs`): clear by default since 0.15; the menu's WEATHER line chooses clear,
  overcast, wet road, rain or random (a draw for every credit: dry 55 %, overcast 20 %, wet 12 %, rain 13 %), kept
  under `weather` in the settings and applied at once (`RaceSession.SetWeather`); `--weather=` fixes it for
  automation, the native API's flags for the Unreal version (its title screen: H). The simulation
  scales every surface's peak friction by `WeatherModel.Grip` (wet asphalt 0.74, rain 0.68; grass 0.80 / 0.75), and
  the scripted driver plans its corners and braking with the same figures, so the attract lap and the tests hold in
  the rain. The view (`WorldBuilder.ApplyWeather`): a grey procedural sky in place of the photo one, the sun a wide
  soft source behind the cloud, sky-only ambient, thicker air, a dark mirror-like road with puddles (a steep noise as
  its roughness map), dark gravel and grass with a sheen, shining line paint; rain as a box of streaks travelling with
  the camera (`CameraRig.SetRain`) and a hiss in the mix (`EngineAudio.RainHiss`). The HUD names any weather but a
  clear day. 1989 mode keeps its flat day whatever the weather; the grip is the session's either way.
- The arcade's open plain (0.13, from its attract-mode map and frames): the wood keeps 480 m back from every road
  and thins over the next 400 m (140 m until 0.12, when it hid every landmark behind the next bend), so the loop,
  the drawbridge towers, the barn and the checkpoint gantry stand in view across the fields; the shoulders are the
  red-brown earth of the original again, 3.2 m wide (the compacted gravel of 0.10 read as a highway); no edge
  lines or marker posts (`WorldBuilder.RoadFurniture`); the centre dashes 4.5 m every 12 m and 20 cm wide; the
  mountains taller and nearer (360-800 m at 3.9 km); and the road rolls over two more gentle crests (the speed
  route after the drawbridge, the stunt route's run to the merge). The plan itself was already the original's: its
  attract map and ours agree on every feature and its order. 0.15 took the arcade's dressing back where it had
  read as less real than 0.12's road: the gravel shoulders (2.6 m), the white edge lines and the marker posts
  returned, the centre dashes went back to 3 m every 9 m, the mountains to the low chain at 4.4 km, the wood to
  260 m from the roads, and the plain's swell eased to 2 m. The plain itself rolls like farmland (0.13:
  `rollingGround` 2.6 m with a 620 m swell under the 230/95/38 m octaves, half a metre and no swell until then); the
  roads keep their profile and the ground field grades the embankments and cuttings, the buildings stand on levelled
  pads (`TerrainField.AddFlat`), and the terrain mesh paints bare earth by the relief the roads cut into the plain,
  not by the height itself.
- Units: km/h by default since 0.14 (mph until then, as the cabinet's dial); U or the menu switches, and the choice is
  kept under `units` in the settings (the Unreal version the same, under a new key so an old copy gets metric once).
- Instant replay music: our own tune, written for the game in the manner of the late-eighties arcade and not a
  transcription of the original's (it replaced eleven seconds of a CC0 FM tune). `tools/make-replay-music.py`
  holds the score and renders `music/replay.ogg` and `replay.wav` from it, and writes the same notes to
  `game/src/ReplayScore.cs` for the 1989 synth, so both modes play one tune. Ten seconds (the replay's length; the
  last chord lands at 8.8 s and rings out before the fade) in A mixolydian at 136 bpm, sixteenths swung to 56 %: the
  band's intro hit and a tom fill, a four-bar hook (a syncopated lead over slap bass, horn stabs pushed a sixteenth
  ahead of the bar, a clav comp on the off-sixteenths; A, G to D/F#, A, then F and G as band hits) and a button on A
  with the brass falling off. The sound is the cabinet's as far as it is documented: its sound board had no FM chip
  but a TMS32010 DSP mixing sampled instruments into a 12-bit DAC at 20 kHz, mono (MAME's `harddriv_a.cpp`), and the
  samples of 1989 were of FM synths, drum machines with gated reverb and orchestra hits. So the instruments are
  four-operator FM in the YM2151's layout (feedback on the first operator, exponential attacks, decays linear in
  decibels: brass whose modulators open after the carriers, a sawtooth-and-square lead with bends and a delayed
  vibrato, DX-style slap bass with thumb, pop and dead notes, a clav), drums from oscillators and noise (an 808's six
  squares for the metal) and an orchestra hit, and the mix goes through the board: band-limited to 20 kHz, glued and
  limited, truncated to 12 bits, held between samples and smoothed by the amplifier's low-pass. Peak -1 dBFS and
  -17 LUFS, as the file before it. A file of the player's
  own under `user://music/replay.ogg|.mp3|.wav` takes its place (0.11, `EngineAudio.CustomReplayMusic`): nothing
  copyrighted ships with the game, but a copy can play what its owner remembers. The menu's REPLAY MUSIC line shows
  which plays and opens the folder (0.15); a file dropped in while the game runs is picked up when the menu opens,
  and a file dragged onto the game's window is copied in and taken at once (`EngineAudio.AdoptReplayMusic`; the
  name it had is kept in `replay-name.txt` for the menu). The Unreal version plays the same tune (GlpAudio: its own
  `Saved/GrudgeLap/music/replay.wav`, the Godot folder's `replay.wav`, else the built-in recording), from the start
  of the replay, fading when it ends.


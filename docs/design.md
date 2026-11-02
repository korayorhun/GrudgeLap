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
  control); `VehicleTests.Hatchbacks_match_their_published_figures` holds their 0-60 and top speed. Each
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
- The menu (`PauseMenu`, Esc or the pad's Back): carry on, new game, camera, graphics, 1989 mode, units, full
  screen and quit, with the keys, the d-pad or the mouse. While it is open the session does not step, no input
  reaches the car and the master bus is muted. Until 0.8.1 the game could only be left by closing its window,
  which a full-screen window does not offer.
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
  (its thinnest direction), and places the eyes (half a metre behind the wheel's centre and 28 cm over it, 13 cm
  under the headlining), the dials (on the named instrument meshes, or where the line of sight over the hub meets
  the dashboard) and the mirror (the model's own glass, sized to it, or hung by the game under the windscreen 36
  degrees right of straight ahead when the model's is out of view, as in the Esprit). Paint is found by whole
  words of a material's name ("lack" inside "Black" had painted every black part) and drawn in the car's colour
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
- Updater: once a day the latest GitHub release is fetched; F10 downloads the matching package (installer
  for an installed copy, zip for a portable one), verifies the published SHA-256 and applies it (silent
  installer with `/UPDATE=1`, or a PowerShell script that waits for the game to exit, unpacks over the
  folder and relaunches).
- One copy per computer (`SingleInstance`): the game holds a Global named mutex; a second launch signals a
  named event, which the running game polls every frame to restore and raise its window, and quits before
  building the world. `--invisible` runs neither take nor check the lock.
- Command-line switches for automation: `--attract`, `--chase`, `--speed-route`,
  `--car=flat12|v8|hothatch|five|turbohatch` (or the car's index, or the name of the car it follows: `peugeot205`,
  `delta`, `r32`, `sahin`, `toros`, `w212`), `--ram=tree|building|traffic|test` (the
  scripted driver runs the course until a thing of that kind is near and drives into it, or into a tree planted
  on the start straight: the crash replay on film),
  `--no-traffic`, `--weather=dry|overcast|wet|rain` (the credit's weather; otherwise every credit draws one),
  `--lofted` (the lofted bodies instead of the real models), `--paint=<original|red|white|silver|black|dark-blue|green|yellow>`
  (the player's paint for the run), `--look=drone:<n>[:distance[:bearing[:height]]]` (the camera on the n-th drone), `--seat-look=<yaw>,<pitch>` (the cockpit view turned,
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
the installers from 0.6.0 to 0.9.0 showed their letters double-encoded. The updater clears packages more than a
day old from its download folder before it fetches a new one (an update that was never applied left its 80 MB
behind). Scoop and winget wait for the stable 1.0, which will be the first winget submission (the earlier
new-package requests were withdrawn).

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


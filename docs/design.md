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
  (`VehicleSpec.FrontWheelDrive`) through an open differential or, on the flat-twelve, a limited-slip one; a turbo engine's torque builds with its boost
  (`EngineModel.Boost`: half a second of lag above a threshold rpm, falling at a third of that rate so a
  shift does not lose it), and the view whistles and vents it.
- **Cars** — `VehicleSpec` presets: the 1984 flat-twelve (1580 kg, four speeds, 6900 rpm), the 2012 V8
  (1485 kg, seven speeds, 9000 rpm, stickier tyres, traction control) and, since 0.9, three front-wheel-drive
  hatchbacks tuned to the published figures of the cars they follow: a 2001 hot hatch (1140 kg, 172 hp, five
  speeds, 7200 rpm, no traction control), a 1997 five-cylinder (1310 kg, 147 hp, five speeds, the five's
  warble) and a 2008 turbo (1480 kg, 240 hp with 300 N m from 2200 rpm on boost, six speeds, traction
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

Items 12, 13 and 16 were taken up in 0.19, 15 was decided against, 14 stays open:

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
    the revs rising from idle too). 0-100 km/h now: flat-twelve 4.9 s (Ferrari quoted 5.8, Car and Driver timed
    5.0 to 60 mph in 1986), V8 3.4 (3.2; traction-limited at 0.89 g throughout), hot hatch 7.5 (7.2), five 9.1
    (8.5), turbo hatch 7.0 (6.4), 1.6 saloon 13.5 (13.9), estate 17.1 (16-17), fleet saloon 12.3 (11.5); the ranges
    in `Sprints_and_stops_within_the_real_cars_ranges` came in to about a tenth either side of those figures, and
    `Standing_starts_hold_the_launch_revs_and_the_tyres_short_of_their_peak` and
    `Flooring_it_from_rest_launches_without_a_bog` pin the launch itself. Still quick on the flat-twelve: its
    magazine figures include a test driver on a heavy single-plate clutch.
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
    and `car_models.py` can follow it together in one change.
14. **The 570S's brake steer** (it brakes a spinning inside wheel instead of locking the axle) and the hatchbacks'
    electronic equivalents are not modelled; their differentials are open. Still open.
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
  and from the cockpit the engine comes through the bulkhead (duller, with a cabin boom). Tyres and road
  from recordings (`TyreAudio`: the tread's roar recorded in a cabin, pitched up with the speed; a cornering
  squeal from about four degrees of slip angle, a harder howl that joins it at big angles, a screech for
  wheelspin and locked wheels beyond 15 % slip, gravel while a wheel is off the asphalt, each faded and
  pitched from the slip of the four wheels); two to four exhaust pops when the V8 lifts off above 4800 rpm,
  one-shots from a CC0 recording drawn at random with a spread of pitch and level; synthesised wind that
  grows with speed and a thump when the suspension is jolted; the shifter's clunk at every change of gear (a
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
  `--car=flat12|v8|hothatch|five|turbohatch` (or the car's index), `--ram=tree|building|traffic|test` (the
  scripted driver runs the course until a thing of that kind is near and drives into it, or into a tree planted
  on the start straight: the crash replay on film),
  `--no-traffic`, `--weather=dry|overcast|wet|rain` (the credit's weather; otherwise every credit draws one),
  `--lofted` (the lofted bodies instead of the real models), `--seat-look=<yaw>,<pitch>` (the cockpit view turned,
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
2. A cleaner twelve-cylinder recording for the 1984 car if one with a usable licence turns up.
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
  ratios), `Estate` (after the Renault 12 Toros: FIA form 5396) and `Fleet` (after the Fiat Egea: Fiat's handbook);
  0-60 mph 13.1 / 16.2 / 12.1 s and 99 / 89 / 115 mph in the tests. Bodies in `CarView.Everyday.cs`, steel wheels under
  plastic covers; the traffic is these three in turn.
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


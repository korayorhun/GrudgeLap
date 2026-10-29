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
  cuttings; bridges, ramps and the loop are structures and leave the ground alone. The road, its shoulders
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
- **Vehicle** — rigid body on four raycast wheels (spring/damper/bump stop), Pacejka-style tyres with a
  friction circle, load sensitivity and relaxation, a per-car grip multiplier, rolling resistance as a
  body force (much higher on grass: grip 0.66 and resistance 0.11 of the load since 0.9, from 0.55 and 0.16,
  which made the verge a trap), aerodynamic drag, speed-sensitive steering lock, optional traction
  control that eases the throttle as the driven wheels spin up. Rear- or front-wheel drive
  (`VehicleSpec.FrontWheelDrive`) through an open differential; a turbo engine's torque builds with its boost
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
  impossible); a launch assist in the assisted modes that lets the engine hold its revs while the car
  starts rolling; gearboxes with shift points relative to the redline; three transmission modes.
- **Traffic** — drones that ride the road frames of the start straight and the speed route in both
  directions in their own lane, never the stunt route; kinematic, solid to the player, moved away from a
  respawn point.
- **Rules** — timers from the cabinet's option table (90 s, +45 per lap, +45 at the checkpoint on Medium),
  route detection at the fork (announced 45 m in, where the branches have parted), checkpoints and return
  points as path markers (counted on road that follows on; a hop between the overlapping branches or back
  across the fork carries the distance over, so nothing is scored or crossed twice), off-road ten-second rule,
  wrong-way detection (suspended on the skid pad), crash detection (hard landing, roll-over, falling into
  the gap or hitting the deck edge, solid props, drones), a 10 s instant replay buffer, the key-turn restart
  after every crash, messages with a shelf life, and the best lap recorded at 20 Hz as the ghost of the
  grudge match (serialisable, loaded into the next credit).
- **Scripted driver** — pure-pursuit steering along its lane plus a curvature-aware speed plan with
  braking distance, slower over a crest (the road falling away takes weight off the tyres:
  v² = g R k / (1 + κ R k)); it catches a slide with opposite lock once the body slip passes 5° and stays off
  the pedals past 8°, keeps the wheel still in the air, and times its lane changes by the session clock, so a
  lap looks the same at 20 fps as at 144 (the game asks it once a frame); traffic sense (follow a slower
  drone, pass with 480 m of empty opposite lane, hurry back when one comes within 250 m, abort a pass when
  something comes); shared by the tests and the game's attract mode.

- Road wear (`RoadWear`): a deterministic function of the place on the road, shared by the probe and the view
  so that what is felt is also seen. Settled earthworks undulate by a centimetre over a dozen metres; potholes
  (at most one per seven-metre cell and lane, bowls 1.8 to 5 cm deep and under a metre across) sit in the band
  the wheels run in; the structures carry only the millimetres of their joints. Patches of other asphalt are
  visual only. A finer roughness (4 mm at 1.1 m) was tried and dropped: the rigid probe wheel hammered the
  suspension with it and the drag strip lost twenty miles an hour of top speed.
- High scores (`HighScoreTable`): ten lines in order, a factory table to beat, three initials of a 27-letter
  alphabet, one text form shared with the Unreal version. The session keeps the credit's own best lap
  (`CreditBestLap`) apart from the loaded ghost's time for it.

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
  when a lap beats the record, a driving tune under the instant replay (eleven seconds at 168 bpm in D major:
  a quarter-duty pulse lead, a bass on the eighths in octaves, brass stabs, kick, snare and hats; 9 dB over the
  other jingles because the engine is silent then, faded out in a third of a second when the replay is over or
  skipped) and an FM tune when the credit is over. The notes are ours in the timbres of the FM and pulse chips:
  the original's replay music is Atari's and is not copied. The arcade had no music while driving, so neither
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
  `--no-traffic`, `--no-ghost` (the saved best lap is not raced, for pictures of the car), `--no-hud` (no
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
start-up; Scoop and winget wait for the stable 1.0, which will be the first winget submission (the earlier
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

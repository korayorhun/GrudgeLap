# Design notes

## Principle

The feel is the product: physics, the steering loop and the sound come first, graphics last. The simulation
is a plain .NET library with no engine dependency, stepped at a fixed 240 Hz; Godot only renders the state
and feeds it input. That split lets every behaviour be a headless test and keeps the door open for a
force-feedback wheel later (the FFB loop of the 1989 cabinet ran at 4 ms, the car model at 25 ms).

## Simulation (`physics/`)

- **Track** — a graph of four paths (common → speed | stunt → final → common), each a list of road frames
  about one metre apart with origin, tangent, normal and lateral axes. The builder is turtle-style:
  straights, arcs with eased banking, cosine hills and dips, a helical loop (normal points to the loop
  centre, so it points *down* at the top), an open drawbridge with a surface-less gap, overpasses, and
  Hermite splines that close the circuit without hand-solved geometry. Lengths are given in 88 ft slabs,
  the road unit of the original.
- **Ground** — `TerrainField`: value-noise rolling ground pulled to the edge height of every road built on
  earth (straights, curves, hills, dips, banked curves, splines), so hills get embankments and dips get
  cuttings; bridges, ramps and the loop are structures and leave the ground alone. Skid pads are flat
  discs blended in; beyond the outermost road a ring of hills rises over half a kilometre and fades before
  the far plane. The same field answers the physics probe off the road, places the props and builds the
  terrain mesh; `Height(x, z, out roadWeight)` also says how close to a road edge a point is.
- **Decor** — `OriginalInspiredTrack` places the props the way the 1989 circuit had them: the start gantry,
  crossbucks at the level crossing, the barn with "33" on its back door, cows, an orange and a white
  farmhouse, the gas station with its pumps, the hut, flowers, sawhorse barriers before the stunts, fields
  (plough, crop, hay) slid outward until they clear every road, hedgerow trees on their far side, the
  skid-pad column and a hint sign. Props are *solid* (crash) or *soft*: a cow moos and a sign tips over,
  once, with no penalty.
- **Train** — `Train`: a kinematic locomotive and wagons that cross the speed route on a schedule (run,
  pause, run), on no rails as in 1989. The session treats a car under it as a crash; the scripted driver
  stops short of the crossing when a run is due.
- **Surface probe** — the one question the car asks: what is under this point? Nearest frame search in a
  window around a cursor, interpolation to the neighbour, lateral/height from the frame; the skid pad and
  then the ground field for anything off the road. Branches overlap for their first metres, so the probe
  also searches sibling branches near a fork.
- **Vehicle** — rigid body on four raycast wheels (spring/damper/bump stop), Pacejka-style tyres with a
  friction circle, load sensitivity and relaxation, a per-car grip multiplier, rolling resistance as a
  body force (much higher on grass), aerodynamic drag, speed-sensitive steering lock, optional traction
  control that eases the throttle as the driven wheels spin up. Rear-wheel drive through an open
  differential.
- **Cars** — `VehicleSpec` presets: the 1984 flat-twelve (1580 kg, four speeds, 6900 rpm) and the 2012 V8
  (1485 kg, seven speeds, 9000 rpm, stickier tyres, traction control). Each carries an `EngineSpec`
  (idle, redline, full-throttle curve, engine braking), a body style and the recordings to play.
- **Drivetrain** — torque-curve engine with idle governor, rev limiter and stall; clutch as a lock/slip
  state machine with the slip torque limited to what synchronises the two inertias in one step (the axle
  side includes the car's mass through the tyres; otherwise the flywheel hammers the wheels and a stall is
  impossible); a launch assist in the assisted modes that lets the engine hold its revs while the car
  starts rolling; gearboxes with shift points relative to the redline; three transmission modes.
- **Traffic** — drones that ride the road frames of the start straight and the speed route in both
  directions in their own lane, never the stunt route; kinematic, solid to the player, moved away from a
  respawn point.
- **Rules** — timers from the cabinet's option table (90 s, +45 per lap, +45 at the checkpoint on Medium),
  route detection at the fork, checkpoints and return points as path markers, off-road ten-second rule,
  wrong-way detection (suspended on the skid pad), crash detection (hard landing, roll-over, falling into
  the gap or hitting the deck edge, solid props, drones), a 10 s instant replay buffer, the key-turn restart
  after every crash, messages with a shelf life, and the best lap recorded at 20 Hz as the ghost of the
  grudge match (serialisable, loaded into the next credit).
- **Scripted driver** — pure-pursuit steering along its lane plus a curvature-aware speed plan with
  braking distance; traffic sense (follow a slower drone, pass when the opposite lane is clear for long
  enough, abort a pass when something comes); shared by the tests and the game's attract mode.

## View (`game/`)

- World is generated from the track at start-up (`WorldBuilder`): terrain mesh sampled from the ground
  field (embankments, cuttings, dips, rolling fields, the hill ring) with vertex-colour variation and a
  trodden tint along the road edges; photo-textured road ribbons (CC0 ambientCG) in six strips with the
  alternating slab shades of the original and darker wheel tracks, gravel shoulders (orange on the stunt
  structures, everywhere in 1989 mode), dashed centre line, bridge decks with steel guardrails and pillars
  down to the ground, the skid pad; HDRI sky with the sun aimed where the picture has it, cascaded shadows,
  SDFGI, SSAO, SSIL, screen-space reflections, a thin volumetric haze, AgX tone mapping and TAA.
- Foliage (`FoliageBuilder`): grass tufts as crossed alpha-scissor cards in multimeshes, dense along the
  roads and sparse over the ground, olive-tinted to the terrain and faded out beyond 150 m; trees as a
  bark trunk with branches under a canopy of leaf-cluster cards; fields as terrain-hugging patches
  (ploughed dirt, crop rows, hay with bales). Both atlases come from `tools/make-foliage.py`.
- Props (`PropBuilder`): textured primitives for the barn (siding, tiled roof, the green "33" door at the
  back), houses (orange or white siding, windows, chimney), the shop with canopy, pumps and a GAS sign,
  the camouflaged rubble hut, cows with patches, yellow diamond and white number signs, arrow signs, the
  crossbuck, the start gantry, striped sawhorses, the pad column and flower beds. A sign that is hit leans
  over about its base, away from the car. `TrainView` rides the simulation's train on the terrain.
- Keyboard driver with ramped pedals and speed-dependent steering rates; gears 1-7, left/right pick the car
  on the title screen.
- Cars: a loft through cross-sections built from a `BodyStyle`'s profile curves keyed along the body,
  monotone-cubic between keys; wheel arches cut into the loft; star-rimmed wheels that steer and spin.
  Quads are wound by the ring direction (tangent turned clockwise = outward), normals are smoothed below a
  60 degree crease angle. Two styles: the flat-twelve (blade nose, lamp lids, dipped bonnet, small cabin,
  strakes, louvres, slatted tail, A-pillar mirror) and the V8 (low wide nose, long screen, glass engine
  cover, side scoops, round lamps, four tips, lip). Panel shut lines are dark strips of the loft itself (a
  pair of stations 14 mm apart at each seam); a plate, indicators, door handles, brake discs with red
  calipers and a cellular tread grain on the tyres finish the outside. The cabin is furnished and seen
  through tinted glass: floor, dashboard and binnacle, a steering wheel on a column leaning back 22 degrees
  that turns twelve times the road wheels' angle, two seats, the A-pillars and the wipers. From the
  driver's seat the shell and the glass are hidden, so the bonnet, its lamps and the cabin remain; the
  A-pillars narrow towards the roof, because the eye is under 0.3 m from their tops and a pillar of even
  section would read as a band across the view. Drones reuse the loft in other paints; the ghost is the
  same loft in one translucent glowing material without shadows.
- Cockpit camera at the driver's eye (left seat, 1.00 m above the road, 0.67 m behind the windscreen base,
  62 degrees of vertical field of view opening to 68 at speed, a few millimetres of jitter that grow with
  speed and on grass; rolls with the car) and chase camera; crane shot for the replay; `--orbit` fixes a
  camera on the car for pictures of it.
- HUD: speedometer and tachometer scaled to the car, gear, transmission mode, time, score, lap, route, best
  lap and ghost indicator, updater notice, messages; the cluster scrolls with lateral acceleration like the
  original's moving dashboard. Title screen: car, transmission, version and the F-keys.
- Audio: engine from recordings (`SampledEngine`: loops at known speeds pitched to the rpm, cross-faded on
  a log scale, through a low-pass that opens with the throttle, an off-throttle loop for the V8); tyres
  from recordings (`TyreAudio`: a cornering squeal from about four degrees of slip angle, a harder howl
  that joins it at big angles, a screech for wheelspin and locked wheels beyond 15 % slip, gravel while a
  wheel is off the asphalt, each faded and pitched from the slip of the four wheels); synthesised road
  roar and wind that grow with speed, a thump when the suspension is jolted, and two to four exhaust pops
  when the V8 lifts off above 4800 rpm; the pulse-train engine and a filtered-noise squeal kept for 1989
  mode; crash burst, checkpoint bleep, a cow's moo (formants on a sagging pitch), the clang of a sign going
  over, the clack of the train's wheels when it runs nearby, and a four-bar synth tune on the title screen
  and in replays.
- "1989 mode" (F8): a third of the render resolution, 20 fps, textures and normal maps off, nearest
  filtering, no shadows, global illumination, ambient occlusion, reflections, glow, fog or TAA, a flat sky,
  orange shoulders everywhere, the synthesised engine and squeal instead of the recordings.
- Updater: once a day the latest GitHub release is fetched; F10 downloads the matching package (installer
  for an installed copy, zip for a portable one), verifies the published SHA-256 and applies it (silent
  installer with `/UPDATE=1`, or a PowerShell script that waits for the game to exit, unpacks over the
  folder and relaunches).
- Command-line switches for automation: `--attract`, `--chase`, `--speed-route`, `--car=v8|flat12`,
  `--no-traffic`, `--no-ghost` (the saved best lap is not raced, for pictures of the car), `--retro`,
  `--screenshot=<file>`, `--at=<s>`, `--quit-after=<s>`,
  `--orbit=<yaw>,<pitch>,<distance>` (degrees and metres, 0 = from behind), `--show-all` (whole car from
  the driver's seat, for checking the body), `--save-best` (an attract lap may become the saved best lap),
  `--invisible` (the window is parked off-screen, made unfocusable and hidden, so an automated run never
  shows on the desktop or takes the keyboard; pair it with Godot's own `--position -32000,-32000` so the
  window is created off-screen in the first place). Rendering continues for a hidden window, only a
  minimised one stops it, so `--screenshot` still works.

## Conventions

Simulation space is right-handed, +Y up, forward +Z, right +X, heading positive to the right; Godot's
forward is -Z, so the view flips Z and nothing else. Units are SI; speeds are shown in mph by default.
Settings and the best lap live under `user://` (`%APPDATA%\Godot\app_userdata\GrudgeLap`).

## Release

`tools/release.ps1 -Version x.y.z` stamps the version into `project.godot`, the csproj and the Inno script,
runs the tests, exports, builds the installer and the portable zip with their SHA-256 files, commits, tags,
pushes, creates the GitHub release, updates the Scoop bucket and submits the winget manifest. CI
(`.github/workflows/build.yml`) repeats tests, export and installer on every push.

## Roadmap

1. Force-feedback wheel support through DirectInput (torque = model force + damping + friction + centring,
   the recipe of the expired 1991 patent); until then, keyboard only.
2. A cleaner twelve-cylinder recording for the 1984 car if one with a usable licence turns up.
3. Crash and exhaust-pop samples instead of the synthesised ones (the tyres are recorded since 0.4); a
   longer tune.
4. More circuits built with the same turtle API; the hidden skid pad could grow cones and a timer.
5. Scanned models instead of lofts and primitives: a car body with real curvature (the loft has twenty
   points per section) and buildings with real geometry would be the next step past textured primitives;
   a reflection probe on the car is worth measuring for the paint.

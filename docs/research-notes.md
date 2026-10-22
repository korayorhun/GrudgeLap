# Research notes

Public sources that shaped the design. Nothing here is copied into the game; these are the facts and
links we worked from.

## The 1989 cabinet in short

- Atari Games' Applied Research group (Rick Moncrief, Max Behensky, Stephanie Mott, Jed Margolin, Erik
  Durfey) set out in 1984 to build an affordable driving-training simulator; it became an arcade game in
  February 1989. The car model was developed with Doug Milliken, co-author of *Race Car Vehicle Dynamics*.
- Four processors: a 68010 for the game, a TMS34010 for polygons, a second TMS34010 for the car model
  (fixed point, so the model was a "two-wheel" one with pitch only; the 1990 sequel moved to a floating-point
  DSP and a four-wheel model), an ADSP-2100 for 3D maths. 512x384 at 60 Hz; the polygon rate was far lower.
- Controls: a motor-driven steering wheel with three turns lock to lock and continuous force feedback, a
  strain-gauge brake pedal that measures force rather than travel, a clutch, an H-pattern four-speed shifter
  with electromagnetically controlled resistance, an ignition key, an adjustable seat whose position scales
  the steering force.
- Force feedback recipe (US 5,044,956, expired): torque = model force + damping + friction (spring-block)
  + a servo term toward a commanded position; 4 ms control loop, model values every 25 ms, low-pass filtered,
  with watchdogs and dual ADC checks for safety.
- Rules: timers from the operator's manual (Medium: 90 s initial, 45 s extensions), a fork into a speed
  route and a stunt route (drawbridge jump, vertical loop, banked 180), checkpoints, ten seconds off road,
  a ten-second aerial instant replay after every crash, the key turned again after each crash, and a
  "championship lap" against the recorded best driver (the ghost-car patents 5,269,687 / 5,354,202 /
  5,577,913, all expired).
- The home ports' track data used 88 ft slabs; the loop needed at least 60 mph, the bridge roughly 60-80.

## Links

- Jed Margolin's hardware pages: https://www.jmargolin.com/schem/schems.htm
- Operator's manual scans: https://archive.org/details/arcade-game-manuals/
- Patents: https://patents.google.com/patent/US5044956A/en (steering force feedback),
  https://patents.google.com/patent/US5005148A/en (moving dashboard),
  https://patents.google.com/patent/US5354202A/en (ghost car)
- Technical analysis of the series: http://weltenschule.de/vgames/HardDrivin/HardDrivin_analysis.html
- Wikipedia: https://en.wikipedia.org/wiki/Hard_Drivin%27

## Rights

The original game's name, assets and track belong to its current rights holder (Warner Bros.
Entertainment, via Midway Games West). This project uses none of them; the expired patents and the public
record describe the mechanics it pays homage to.

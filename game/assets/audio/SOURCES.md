# Recordings

## Engines (`flat12/engine_*`, `v8/engine_*`, `hothatch/engine_*`, `five/engine_*`, `turbohatch/engine_*`)

Each car has two grain banks: a recording of the engine at idle and one running up through a gear, cut into
its firing pulses by `tools/make-engine-bank.py` (a 16-bit mono WAV at 44.1 kHz and, beside it, a JSON file
with the position and the firing frequency of every pulse). The game's engine voice (`audio/GranularEngine.cs`,
played by Godot's `SampledEngine` and by the Unreal version through the simulation core's DLL) lays one pulse
of a bank per firing period of the simulated revs, taken from the pulses recorded at the nearest revs, and
overlap-adds them; nothing loops and nothing is pitch-shifted.

| Bank | Source | Segments (start@end s @ firing Hz at start @ search band Hz) |
|---|---|---|
| `v8/engine_idle` | Freesound #585215 "Car Engine: Maserati GranTurismo Switch to Sport Mode and Launch" by myeclecticself, https://freesound.org/s/585215/ (CC0 1.0) | 0.5@4.8@56@40@80 |
| `v8/engine_drive` | `3 Ferrari 458.mp3` from the Internet Archive item "Car Engine Sounds" (below) | 0.15@3.95@150@120@700, 4.85@6.95@400@330@560, 7.3@9.5@370@300@500 |
| `flat12/engine_idle` | Freesound #112075 "Lambo idle and rev.mp3" by cheesepuff, https://freesound.org/s/112075/ (CC0 1.0) | 4.0@16.3@121@98@128 |
| `flat12/engine_drive` | `15 Lamborghini Countach.mp3` from the Internet Archive item "Car Engine Sounds" (below) | 0.08@2.75@262@220@800, 3.3@5.6@556@450@820 |
| `v8/engine_overrun` | `3 Ferrari 458.mp3` (the stretches with the throttle shut and the revs falling) | 15.0@16.3@436@380@480, 19.0@20.0@377@300@420 |
| `flat12/engine_overrun` | `15 Lamborghini Countach.mp3` (the throttle shut at the end of the run) | 8.65@9.65@290@200@320 |
| `hothatch/engine_idle` | `honda civic type r.mp3` from the Internet Archive item "Car Engine Sounds" (below) | 2.7@3.4@42@30@46, 10.3@11.9@35@27@42 |
| `hothatch/engine_drive` | `honda civic type r.mp3` | 14.2@16.3@60@45@235, 17.4@19.6@150@125@240 |
| `hothatch/engine_overrun` | `honda civic type r.mp3` (the throttle shut, revs falling) | 0.6@2.2@112@45@130, 4.5@6.2@110@40@125, 8.5@10.0@91@35@100 |
| `five/engine_idle` | Freesound #95838 "RG Volvo Engine.wav" by cmusounddesign (a 2.4-litre five under the bonnet, at idle), https://freesound.org/s/95838/ (CC BY 4.0) | 6.0@23.0@42.5@36@50 |
| `five/engine_drive` | `honda s2000.mp3` from the Internet Archive item "Car Engine Sounds" (below), played as a five (see the note) | 12.0@15.6@57@45@215, 2.9@5.6@205@180@300 |
| `five/engine_overrun` | `honda s2000.mp3` (the throttle shut, revs falling) | 6.0@8.6@224@60@260 |
| `turbohatch/engine_idle` | `25 Nissan Pulsar.mp3` from the Internet Archive item "Car Engine Sounds" (below) | 0.4@2.4@33@22@48 |
| `turbohatch/engine_drive` | `25 Nissan Pulsar.mp3` | 6.0@8.7@94@80@175, 21.3@24.5@109@90@210, 25.0@27.9@127@110@200 |
| `turbohatch/engine_overrun` | `25 Nissan Pulsar.mp3` (the throttle shut, revs falling) | 13.0@15.3@130@60@140, 16.5@19.2@94@28@100 |

The overrun banks play in place of the run-up as the throttle closes (`GranularEngine`): off the throttle a real
engine stops firing and what is left is the exhaust's rasp, which no filter on the run-up gave.

The Internet Archive item **"Car Engine Sounds"** (identifier `car-engines`, uploaded 2015-09-01, dated
2011-02-22) was published by its uploader under **CC0 1.0 Universal** (`licenseurl` in the item's metadata:
http://creativecommons.org/publicdomain/zero/1.0/). Item page: https://archive.org/details/car-engines

Rebuild a bank (the cylinder count turns firing frequency into rpm: rpm = Hz x 120 / cylinders):

    python tools/make-engine-bank.py game/assets/audio/v8/engine_drive 8 "3 Ferrari 458.mp3@0.15@3.95@150@120@700" ...

The banks are imported uncompressed (`compress/mode=0` in their `.import` files) because the voice reads their
samples; a compressed import would not give them back.

No recording of a flat-twelve with a usable licence was found; the twelve-cylinder banks of the 1984 car are a
Lamborghini V12 at idle and the Countach track, the closest in era and sound. Swap the files (same names, made
with the script) to use other recordings.

The five-cylinder hatch (0.9) idles on a real five (the CC BY Volvo recording above; credit cmusounddesign) but
no run-up of a five under load with a usable licence was found (the Freesound ramps of a Volvo S60 by el-bee are
CC BY-NC and were not used): its drive and overrun banks are cut from the S2000 track, a high-revving four,
laid down at the five's firing frequency (rpm x 5 / 120), and `GranularEngine` weights every fifth pulse in a
fixed pattern (`FivePattern`) to give the five's warble back. The hot hatch and the turbo hatch are cut from
the Civic Type R and the Pulsar tracks of the same CC0 item; the turbo's whistle and blow-off valve are
synthesised (`EngineAudio.Turbo`).

## Tyres and road (`tyres/`)

| Loop | Source | Slice | License |
|---|---|---|---|
| `tyres/road.wav` | Freesound #383476 "Car interior driving 02.wav" by Daphne_in_Wonderland, https://freesound.org/s/383476/ | 40.0 s + 8.0 s | CC0 1.0 |
| `tyres/squeal.wav` | Freesound #850041 "tire-squeal" by JeffPearson, https://freesound.org/s/850041/ | 18.6 s + 3.0 s | CC0 1.0 |
| `tyres/squeal_hard.wav` | Internet Archive item `Sound_Effects_2` ("Sounds Effects 2", Wayzata Technology, 1995), file "squealing tires", https://archive.org/details/Sound_Effects_2 | 2.0 s + 4.0 s | CC0 1.0 (item `licenseurl`) |
| `tyres/skid.wav` | BigSoundBank #2370 "Screeching tires #3", https://bigsoundbank.com/detail-2370-screeching-tires-3.html | 0.35 s + 1.2 s | CC0 1.0 |
| `tyres/gravel.wav` | Freesound #341069 "gravel road" by seth-m, https://freesound.org/s/341069/ | 1.0 s + 4.0 s | CC0 1.0 |

Cut with `tools/make-loop.py` (mono 22.05 kHz, the tail cross-faded into the head so the loop is seamless,
peak-normalised; the road loop with a 0.5 s cross-fade). The game pitches and fades them from the speed and the
slip of the four wheels (`TyreAudio`); the road loop is the tread's roar as heard in a cabin, recorded at about
motorway speed. Wind and bumps are synthesised.

## Traffic (`traffic/drone.wav`)

The drones' engine: 2.4 s of the flat-twelve voice itself (the Countach run-up bank played by
`GranularEngine` at cruise, recorded from the game) looped with a 0.4 s cross-fade by `tools/make-loop.py`;
no new source material. `WorldAudio` places it on every drone, each a touch off the others in pitch, with
inverse-distance attenuation and the Doppler shift computed from the simulation.

## Train (`train/steam.wav`, `train/whistle.wav`)

| Clip | Source | Slice | License |
|---|---|---|---|
| `train/steam.wav` | Freesound #209972 "Wales steam train.flac" by jrosin (binaural, from the window of a running steam train), https://freesound.org/s/209972/ | 52.0 s + 6.0 s, 0.5 s cross-fade (`make-loop.py`) | CC0 1.0 |
| `train/whistle.wav` | Freesound #407394 "Steam Whistle.wav" by mike_stranks (a vintage traction engine's whistle), https://freesound.org/s/407394/ | 3.7 s + 2.8 s, 0.5 s fade-out (`make-oneshot.py`) | CC0 1.0 |

Both are placed on the locomotive by `WorldAudio`: the loop while the train runs, the whistle twice as it
approaches the crossing (nine and five seconds out).

## Crash (`crash/crash1.wav` … `crash/crash3.wav`)

| Clip | Source | Slice | License |
|---|---|---|---|
| `crash/crash1.wav` | Freesound #592388 "Car Crash (with Glass)" by magnuswaker, https://freesound.org/s/592388/ | 0.0 s + 2.3 s, 0.5 s fade-out | CC0 1.0 |
| `crash/crash2.wav` | Freesound #807438 "car crash short 1" by Logicogonist (made from #237375 below), https://freesound.org/s/807438/ | 0.0 s + 0.6 s, 0.15 s fade-out | CC0 1.0 |
| `crash/crash3.wav` | Freesound #237375 "Car Crash" by squareal, https://freesound.org/s/237375/ | 0.0 s + 1.6 s, 0.4 s fade-out | CC0 1.0 |

Cut with `tools/make-oneshot.py` (peak 0.9) from the high-quality previews. `EngineAudio` plays one at random
per crash with a spread of pitch (x/÷ 1.08) and level (±2 dB) and a thump under it; 1989 mode keeps the
cabinet's burst of noise.

## Exhaust pops (`v8/pop1.wav` … `v8/pop5.wav`, `v8/crackle.wav`)

One-shots cut from Freesound #797835 "Car Antilag (No cleanup, 32float)" by modusmogulus,
https://freesound.org/s/797835/ , licensed **CC0 1.0** (taken from its high-quality preview).

| One-shot | Slice | Fade-out |
|---|---|---|
| `v8/pop1.wav` | 1.400 s + 0.13 s | 0.08 s |
| `v8/pop2.wav` | 1.555 s + 0.13 s | 0.08 s |
| `v8/pop3.wav` | 1.755 s + 0.13 s (a double crack) | 0.08 s |
| `v8/pop4.wav` | 1.960 s + 0.13 s | 0.08 s |
| `v8/pop5.wav` | 2.165 s + 0.13 s | 0.08 s |
| `v8/crackle.wav` | 0.305 s + 0.33 s (a burst of crackles) | 0.10 s |

Cut with `tools/make-oneshot.py` (mono 22.05 kHz, 2 ms ramp in, cosine fade-out so the recording's
background dies with the bang, peak-normalised to 0.8). The V8 fires two to four of them, drawn at random
with a spread of pitch and level, when the driver lifts off above 4800 rpm.

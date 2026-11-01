# Recordings

## Engines (`flat12/engine_*`, `v8/engine_*`, `hothatch/engine_*`, `turbohatch/engine_*`, `v6/engine_*`)

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
| `turbohatch/engine_idle` | `25 Nissan Pulsar.mp3` from the Internet Archive item "Car Engine Sounds" (below) | 0.4@2.4@33@22@48 |
| `turbohatch/engine_drive` | `25 Nissan Pulsar.mp3` | 6.0@8.7@94@80@175, 21.3@24.5@109@90@210, 25.0@27.9@127@110@200 |
| `turbohatch/engine_overrun` | `25 Nissan Pulsar.mp3` (the throttle shut, revs falling) | 13.0@15.3@130@60@140, 16.5@19.2@94@28@100 |
| `v6/engine_idle` | `Jaguar f-type s.mp3` from the Internet Archive item "Car Engine Sounds" (below) | 13.0@14.4@32@25@40, 30.5@31.05@32@25@40 |
| `v6/engine_drive` | `Jaguar f-type s.mp3` (the blips of the throttle, rising) | 14.77@15.1@59@45@110, 4.44@4.8@59@45@110, 1.09@1.85@43@36@130, 18.12@18.72@80@65@200, 20.49@21.17@75@60@230, 22.69@23.27@108@90@230, 25.34@26.06@70@55@170, 26.27@27.17@82@60@230 |
| `v6/engine_overrun` | `Jaguar f-type s.mp3` (the revs falling after each blip, the throttle shut) | 21.80@22.66@156@95@175, 24.0@25.32@156@65@175, 27.8@29.9@156@44@175 |

The overrun banks play in place of the run-up as the throttle closes (`GranularEngine`): off the throttle a real
engine stops firing and what is left is the exhaust's rasp, which no filter on the run-up gave.

The files are used as cut. Since 0.20 the voice evens a bank when it loads it (`GrainBank`: each segment tilted and
levelled onto the bank's trend across the revs, so the joins between recordings do not step) and trims each car to
one loudness (`GranularEngine`, BS.1770); nothing is written back.

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

Which bank a car plays is its engine's `EngineSpec.SoundFolder` (physics/Vehicle/EngineModel.cs), with its
`Cylinders` turning revs into firing pulses; the Unreal version's table in `GlpAudio.cpp` follows it.

The three hatchbacks follow the cars the game draws (cars3). The hot hatch (the 205 GTi's eight-valve 1.9) plays the
Civic Type R's four of the same CC0 item. The rally hatch (the Delta Integrale's two-litre turbo four) plays the
turbo four of the `turbohatch` banks, the Pulsar track of the same item, with the turbo synthesised for any engine
with boost (`audio/TurboNoise.cs`, 0.20: the compressor's whistle from the shaft's speed, the vent of a blow-off
valve or the flutter of a surging compressor). The same item holds a `Lancia Delta HF
Integrale.mp3`; it is a 15-second drive-by, Doppler-shifted, with no idle and no steady run-up, and the voice could
not be cut from it (its spectrogram and pitch track, 1 November 2026). A Commons recording of an Integrale ("Lancia
Delta Integrale - engine and exhaust sound", Grzegorz Kalinecki, CC BY 3.0, its licence review pending) is left for a
later pass.
The V6 hatch (the Golf R32's 3.2 VR6) plays a six: no CC0 or CC BY recording of a VR6 was found (Freesound's and
Wikimedia Commons' searches turned up none; the VR6 recordings on offer are commercial libraries), so its banks are
cut from the same CC0 item's `Jaguar f-type s.mp3`, the supercharged three-litre V6 of the F-type S. A VR6 fires
its six cylinders every 120 degrees of crank like any even-firing six, so the voice fires at rpm x 6 / 120 as
the real engine does and the recording's pulses are those of a six; the V6's supercharger whine and its sharper
note are the difference. The recording is the car revved at a standstill, so the run-up bank is cut from the
throttle's blips (unloaded, up to about 4,200 rpm) and the overrun from the revs falling after them; above that the
voice spaces the highest pulses closer, as every bank does past its top.

The five-cylinder hatch of 0.9-0.19 (a Volvo five at idle, credit cmusounddesign, CC BY 4.0, and the S2000 track
laid at a five's firing frequency, `GranularEngine.FivePattern`) left the game with the car it voiced; its banks are
no longer shipped.

## The everyday cars (`saloon/`, `estate/`, `fleet/`, 0.10.0)

| Bank | Source | Segments (start@end s @ firing Hz at start @ search band Hz) |
|---|---|---|
| `saloon/engine_idle` | Freesound #187029 "car Niva.wav" by dobrymolodec (the Lada Niva's 1.6, of the Fiat-124 family the 1.6 saloon's pushrod four belongs to), https://freesound.org/s/187029/ (CC0 1.0) | 0.3@6.4@27@20@36, 19.6@25.0@27@20@36 |
| `saloon/engine_drive` | Freesound #255122 "A car pulling away on asphalt, Lada 1500 Combi 1981" by YleArkisto, https://freesound.org/s/255122/ (CC BY 4.0) | 11.2@14.8@66@55@140, 17.4@19.2@118@100@150 |
| `saloon/engine_overrun` | Freesound #255126 "A car approaching, stopping, shutting down, Lada 1500 Combi" by YleArkisto, https://freesound.org/s/255126/ (CC BY 4.0) | 10.8@18.0@130@65@150 |
| `estate/engine_idle` | Freesound #141240 "Renault 19 underhood acceleration" and #141245 "Reault 19 driving 2" by lovretta (the carburetted Cleon 1.4 of the same family as the estate's), https://freesound.org/s/141240/ (CC0 1.0) | 141240: 0.6@2.9@27@20@36; 141245: 3.0@7.2@36@28@44 |
| `estate/engine_drive` | Freesound #141245, as above | 10.8@16.2@50@40@105, 25.5@33.3@92@80@160, 121.2@126.8@91@80@170 |
| `estate/engine_overrun` | Freesound #141245, as above | 133.2@139.4@194@95@215 |
| `fleet/engine_idle` | Wikimedia Commons "Fiat Grande Punto 1.4 T-Jet - engine and exhaust sound (YouTube -1zYK0ORqWg)" by Adri Drives (the 1.4 Fire family), CC BY 3.0 | 19.2@27.0@27@20@36 |
| `fleet/engine_drive` | the same, and "Fiat Bravo 1.4 T-Jet - engine and exhaust sound (YouTube TYdDRfp-LdY)" by michael maurice, CC BY 3.0 | Punto 64.0@70.2@64@55@115, 72.4@77.4@91@80@130; Bravo 10.0@14.4@156@140@220 |
| `fleet/engine_overrun` | the Bravo recording | 57.6@61.6@167@130@190 |

No CC0 or CC BY recording of the cars themselves was found (a phone clip of a Renault 12 was too short); the banks
are their engines' closest siblings: the Lada's Fiat-124 four for the saloon's Fiat 131 pushrod four, the Renault 19's
carburetted Cleon 1.4 for the estate's, the Punto's and the Bravo's 1.4 Fire for the fleet saloon's (turbocharged
there, which shows little through the pulses the voice uses). The two Commons files come from YouTube uploads marked
with YouTube's Creative Commons Attribution licence; their licence review was pending on Commons on 29 October 2026.

## Cows (`cow/moo1.wav` ... `cow/moo5.wav`)

Five moos of one cow recorded by Joseph Sardin (BigSoundBank #2382-#2386 "Cow moos #2"-"#6",
https://bigsoundbank.com/cow-moos-2-s2382.html, CC0 1.0), cut from the FLAC originals with `tools/make-oneshot.py`
(0.35 s fade-out, peak 0.85): 2382 at 0.55 s + 2.0 s, 2383 at 0.45 + 2.3, 2384 at 0.55 + 2.1, 2385 at 0.95 + 2.4, 2386
at 0.35 + 2.1. `WorldAudio` places them on the cows: loud when the car shoves one, softer now and then from one grazing
within 70 m. 1989 mode keeps the cabinet's synthesised moo.

## Instant replay music (`music/replay.ogg`, `music/replay.wav`)

An original composition of this project, written for it in the style of late-1980s arcade music; it is not a
transcription, an arrangement or a sample of any existing piece (the original game's replay music is Atari's and is
neither copied nor used). Nothing in it is recorded: `tools/make-replay-music.py` composes and renders it from its
score and writes both files (and `game/src/ReplayScore.cs`, the same score for the 1989 mode's synthesiser), so it is
under the project's own licence like the code.

Ten seconds in A mixolydian at 136 bpm with swung sixteenths: an intro hit of the band and a tom fill, a four-bar hook
(a syncopated FM lead over slap bass, horn stabs and a clav comp: A, G to D/F#, A, then F and G as band hits) and a
button on A with the brass falling off. The palette follows what is documented of the 1989 cabinet's sound board (a
68000 and a TMS32010 DSP mixing sampled instruments at 20 kHz into a 12-bit AM6012 DAC, mono; MAME's
`atari/harddriv_a.cpp`) and the studio sounds such samples were taken from: four-operator FM in the YM2151's layout for
the brass, the lead, the slap bass and the clav, drum-machine drums rebuilt from oscillators and noise with gated
reverb, an orchestra hit. The mix is then passed through the board as the script models it (20 kHz, truncated to 12
bits, held between samples, the amplifier's low-pass): peak -1 dBFS, -17 LUFS integrated (mono). `replay.ogg` is
Vorbis 44.1 kHz with two identical channels, `replay.wav` mono 22.05 kHz 16-bit for the Unreal version's reader (the
Godot export leaves it out). Rebuild:

    python -I tools/make-replay-music.py [--stems <folder>]

It replaced eleven seconds of the CC0 "Horizon Surfer" by congusbongus (OpenGameArt). Since 0.11 a player
can put a `replay.ogg`, `replay.mp3` or `replay.wav` of their own into the user folder (`user://music/`), which that
copy plays instead; it is never part of the game.

## References (not shipped)

Photographs looked at for the everyday cars' proportions and details, from Wikimedia Commons: "Tofaş Şahin.jpg"
(public domain), "Tofaş Şahin (late), Firüzağa.jpg" (Mr.choppers, CC BY-SA 3.0), "Renault 12TSW Toros front.jpg"
(CC BY-SA 3.0), "Renault 12 Break rear Turkey.JPG" (CC BY-SA 3.0), "2019 Fiat Tipo berlina 4-porte.jpg" (CC BY-SA 4.0),
"2017 Fiat Tipo Sedan on Mt. Tabor, Israel.jpg" (CC BY-SA 4.0), "2018 Fiat Tipo SW S-Design interior.jpg" (CC BY-SA 4.0).

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

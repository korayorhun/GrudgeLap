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

Eleven seconds of "Horizon Surfer" by congusbongus (OpenGameArt, https://opengameart.org/content/horizon-surfer,
CC0 1.0; FM, made in BambooTracker on the YM2608 chip), from 14.2 s (where the full arrangement comes in) with a
20 ms fade-in and a 1.4 s fade-out, loudness-normalised (-14 LUFS); `replay.wav` is the same, mono 22.05 kHz, for the
Unreal version's reader (the Godot export leaves it out). The original game's replay music is Atari's and is not
used; 1989 mode keeps our synthesised tune.

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

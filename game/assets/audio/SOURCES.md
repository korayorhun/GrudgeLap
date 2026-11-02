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
| `v8/engine_drive` | `3 Ferrari 458.mp3` from the Internet Archive item "Car Engine Sounds" (below) | 0.3@3.95@164@120@700, 4.85@5.6@400@330@560, 5.95@6.95@460@330@560, 7.3@9.5@370@300@500 |
| `flat12/engine_idle` | Freesound #112075 "Lambo idle and rev.mp3" by cheesepuff, https://freesound.org/s/112075/ (CC0 1.0) | 4.0@16.3@121@98@128 |
| `flat12/engine_drive` | `15 Lamborghini Countach.mp3` from the Internet Archive item "Car Engine Sounds" (below) | 0.08@2.75@262@220@800, 3.3@5.6@556@450@820 |
| `v8/engine_overrun` | `3 Ferrari 458.mp3` (the stretches with the throttle shut and the revs falling) | 15.0@16.3@436@380@480, 19.0@20.0@377@300@420 |
| `flat12/engine_overrun` | `15 Lamborghini Countach.mp3` (the throttle shut at the end of the run) | 8.65@9.65@290@200@320 |
| `hothatch/engine_idle` | `honda civic type r.mp3` from the Internet Archive item "Car Engine Sounds" (below) | 3.05@3.45@38@30@46, 10.3@11.9@35@27@42 |
| `hothatch/engine_drive` | `honda civic type r.mp3` | 14.2@16.3@60@45@235, 17.4@19.6@150@125@240 |
| `hothatch/engine_overrun` | `honda civic type r.mp3` (the throttle shut, revs falling) | 0.85@2.2@109@45@130, 4.5@6.2@110@40@125, 8.5@9.75@91@35@100 |
| `turbohatch/engine_idle` | `25 Nissan Pulsar.mp3` from the Internet Archive item "Car Engine Sounds" (below) | 0.55@2.4@33@22@48 |
| `turbohatch/engine_drive` | `25 Nissan Pulsar.mp3` | 6.0@8.7@94@80@175, 21.3@22.2@109@90@210, 23.1@24.5@129@90@210, 25.0@27.9@127@110@200 |
| `turbohatch/engine_overrun` | `25 Nissan Pulsar.mp3` (the throttle shut, revs falling) | 13.0@15.3@130@60@140, 16.65@19.2@92@28@100 |
| `v6/engine_idle` | `Jaguar f-type s.mp3` from the Internet Archive item "Car Engine Sounds" (below) | 13.0@14.4@32@25@40, 30.5@31.05@32@25@40 |
| `v6/engine_drive` | `Jaguar f-type s.mp3` (the blips of the throttle, rising, each up to where its recording overloads) | 14.80@15.1@60@45@110, 4.46@4.8@60@45@110, 1.12@1.85@44@36@130, 18.15@18.70@82@65@200, 20.53@20.875@86@60@200, 22.73@23.005@106@90@200, 25.40@25.97@72@55@170, 26.57@26.815@124@60@200 |
| `v6/engine_overrun` | `Jaguar f-type s.mp3` (the revs falling from the limiter after each blip, the throttle shut) | 21.55@22.66@180@95@210, 23.62@25.32@181@65@210, 27.40@29.9@180@44@210 |

The overrun banks play in place of the run-up as the throttle closes (`GranularEngine`): off the throttle a real
engine stops firing and what is left is the exhaust's rasp, which no filter on the run-up gave.

The files are used as cut. Since 0.20 the voice evens a bank when it loads it (`GrainBank`: each segment tilted and
levelled onto the bank's trend across the revs, so the joins between recordings do not step) and trims each car to
one loudness (`GranularEngine`, BS.1770); since soundfix it also levels every grain of the run-up and the overrun onto
the run-up's trend, against the drift of level within a recording (`GrainBank.LevelGains`), and since banks the idle's
too when the engine pulls, so that from the idle's revs to the run-up's the voice follows one trend. Nothing is written back.

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
throttle's blips (unloaded) and the overrun from the revs falling after them. Each blip runs up to the car's limiter at
a standstill, about 4,000 rpm, and its recording overloads on the way, from about 3,600 rpm: the microphone crackles
for 40 ms (steps of nearly full scale between samples, which the voice replayed as clicks wherever it picked those
pulses), and the stretch at the limiter is the ignition cut every 150 ms with the throttle partly shut, 10 dB duller
than the run-up. The run-up is cut from each blip's start to just before its crackle (up to about 3,600 rpm), the
overrun from where the revs leave the limiter (from about 3,800 rpm; 1 November 2026), and since the banks pass
from 0.1 to 0.15 s later (from about 3,650 rpm, 2 November 2026): the first tenth of a second after each limiter holds the
ignition's last bangs, ticks 7 to 9 dB over the rest of a grain's top end, which the voice laid past the overrun's top as
the driver lifted from the redline (28 sharp transients in five seconds, 43 once the tool filled its missing marks). Past 3,600 rpm the voice lays
the run-up's 16 highest pulses closer, up to 1.8 times at the R32's 6,600 rpm, each held to the period it is laid at
so it plays at its recorded strength (`GranularEngine`); a recording of those revs would serve better.
None was found on 2 November 2026 either. Freesound (searched for VR6, R32, Golf R32, Corrado, six cylinder, inline and
straight six, V6, and the six-cylinder BMWs, Nissans, Porsches and Toyotas, CC0 and CC BY only, high-quality previews):
a Toyota Supra on a chassis dyno (#496171, CC0) pulls through its gears but its recording distorts from about 4,700 rpm
(steps four to six times sharper than below, as its uploader warns); a Chevrolet Traverse's 3.6 V6 accelerating (#351962, CC BY 4.0) is recorded
in its cabin through an automatic's shifts, the firing buried under the body's boom; a BMW M3 (#343369, CC BY 3.0) is 20
seconds of driving that does not run up past about 4,500 rpm; a Suzuki Vitara's V6 (#249720), a DeLorean's PRV V6
(#623436) and a Porsche 911 (#772802) are revved standing, like the Jaguar. Wikimedia Commons (1,400 engine and exhaust
recordings, most of them YouTube audio under CC BY 3.0) has at least eight Volkswagens ("Volkswagen ... - engine and
exhaust sound (YouTube ...)"), which the search of 2 November missed; the nearest, a Touareg's 3.6 VR6 (the R32's engine
family, CC BY 3.0), is revved standing and clipped (99 s with 270,894 samples at full scale, its peaks decoding 3.5 dB
past it; banks review). Of Commons' other sixes a Lexus IS300 (the 2JZ-GE straight six) is the one pulling through its
gears, from 1,100 to 5,200 rpm, but clipped from 1,800 rpm on (its peaks decode past full scale, 79,000 samples in ten
seconds); the others are a cold start (BMW Z4 M Coupé) and exhaust videos with talking (Nissan 350Z, 300ZX), and its
"Audi A3" and "BMW 330i" are a 2.0 TDI and a four. The CC0 archive item's other sixes, passed over for the Jaguar in
cars3, show in their pitch tracks no run-up of one engine through the revs the Jaguar lacks: the GT-R's V6, the Cayman
S's flat six and the Ghibli's V6, the "2 Toyota Supra 950" (9 s, its revs jumping between 3,000 and 8,300 rpm), and the
"18 Porsche 911 GT3" and "19 Porsche 997 GT3 RS" (4 and 5 s of blips, fetched for the banks review). The "alfa romeo
giulia" track does not say which Giulia it is, a six or a four. The Jaguar stays.

The rally hatch's run-up leaves out 22.2-23.1 s of the Pulsar track: a compressor's flutter, a band at 1.3 kHz beating
ten times a second with bursts of hiss, whose pulses were up to 30 dB brighter than their neighbours at 3,300-3,500
rpm (the "brighter at 3350 rpm" of the 0.19 audit, finding 9, which the evening could not take out); the voice
synthesises the turbo's own sounds (`audio/TurboNoise.cs`).

Every bank was re-cut on 2 November 2026 (banks) from the same recordings with the tool seeking exactly. Those cut
before the V6's re-cut began their segments with the MP3 decoder's silence: 50 grains 12 dB or more under their
neighbours, most 40 to 160 dB under (the V8's run-up 15 and its overrun 7, the flat-twelve's run-up 8 and its overrun
7, the rally hatch's overrun 5, the hot hatch's run-up 4 and its overrun 3, the estate's overrun 1), each a drop-out of a
firing period wherever the voice picked it. Cut exactly, some start points fell on what the decoder's silence had hidden
or the tool had not warned of, and moved:

- the V8's run-up from 0.15 to 0.3 s: its first 30 pulses are the recording fading in from silence, up to 20 dB under the
  rest (the voice stepped 2.7 LU at 2,300 rpm); its second segment is split around 5.6-5.95 s, where the recording dips
  9 dB (it stepped 1.9 LU at 6,700 rpm);
- the hot hatch's overrun from 0.6 to 0.85 s and to 9.75 s from 10.0: the blip before 0.8 s overloads the recording
  (samples past full scale) and its end crackles; its idle from 2.7 to 3.05 s, past a waver up to 1,370 rpm that set
  the top of the idle 200 rpm too high;
- the rally hatch's idle from 0.4 to 0.55 s and its overrun's second segment from 16.5 to 16.65 s: the Pulsar track
  starts with the engine catching, 13 dB quieter and twice as sharp, and the overrun's segment with the end of a blip;
- the V6's overrun from 0.13 to 0.15 s later (above);
- the saloon's idle to 19.75-21.2 s of its second stretch (19.6-25.0 s): around it three bursts of a tick 10 times as
  sharp as the recording (the Niva's idle is otherwise smooth), and after them 22.55-25.0 s is 12.5 dB duller than the
  rest, more than the evening's tilt of 6 dB could take back;
- the fleet saloon's Bravo run-up and overrun without the instants of its ticks (10.74, 10.85, 12.21 and 12.75 s; 57.97,
  58.58, 60.63, 60.88 and 61.2 s), the Punto's run-up without a tick at 74.98 s.

The tool now also fills the pulses it lost: where two expected pulses moved onto the same peak, one was dropped and the
grains on either side spanned two periods (12 % of the fleet saloon's run-up, 17 % of the flat-twelve's spanned over 1.3
periods), which laid at the revs played quieter than the rest. It fills only a gap of 1.75 periods or more, with pulses
three quarters of a period or more from either side, and of two peaks closer than 0.6 of a period it keeps the stronger
(banks review): filling from 1.5 periods with half a period either side, and keeping peaks half a period apart, left
grains reaching half a period on a side, and laid under their recorded revs two such sides left gaps (the hot hatch's
idle, 6 ms of silence at 900 rpm). Every bank was re-cut with it from the segments above (the recordings and the
segments' samples are unchanged, only the marks moved). No bank now has a grain more than 9.8 dB under its neighbours,
a crackle by the tool's measure or a grain reaching under 0.6 of its period on a side
(`EngineVoiceTests.Every_bank_is_cut_without_drop_outs_or_crackles`, `Every_grain_reaches_most_of_a_period_either_side`).

The five-cylinder hatch of 0.9-0.19 (a Volvo five at idle, credit cmusounddesign, CC BY 4.0, and the S2000 track
laid at a five's firing frequency, `GranularEngine.FivePattern`) left the game with the car it voiced; its banks are
no longer shipped.

## The everyday cars (`saloon/`, `estate/`, 0.10.0)

| Bank | Source | Segments (start@end s @ firing Hz at start @ search band Hz) |
|---|---|---|
| `saloon/engine_idle` | Freesound #187029 "car Niva.wav" by dobrymolodec (the Lada Niva's 1.6, of the Fiat-124 family the 1.6 saloon's pushrod four belongs to), https://freesound.org/s/187029/ (CC0 1.0) | 0.3@6.4@27@20@36, 19.75@21.2@27@20@36 |
| `saloon/engine_drive` | Freesound #255122 "A car pulling away on asphalt, Lada 1500 Combi 1981" by YleArkisto, https://freesound.org/s/255122/ (CC BY 4.0) | 11.2@14.8@66@55@140, 17.4@19.2@118@100@150 |
| `saloon/engine_overrun` | Freesound #255126 "A car approaching, stopping, shutting down, Lada 1500 Combi" by YleArkisto, https://freesound.org/s/255126/ (CC BY 4.0) | 10.8@18.0@130@65@150 |
| `estate/engine_idle` | Freesound #141240 "Renault 19 underhood acceleration" and #141245 "Reault 19 driving 2" by lovretta (the carburetted Cleon 1.4 of the same family as the estate's), https://freesound.org/s/141240/ (CC0 1.0) | 141240: 0.6@2.9@27@20@36; 141245: 3.0@7.2@36@28@44 |
| `estate/engine_drive` | Freesound #141245, as above | 10.8@16.2@50@40@105, 25.5@33.3@92@80@160, 121.2@126.8@91@80@170 |
| `estate/engine_overrun` | Freesound #141245, as above | 133.2@139.4@194@95@215 |

No CC0 or CC BY recording of the cars themselves was found (a phone clip of a Renault 12 was too short); the banks
are their engines' closest siblings: the Lada's Fiat-124 four for the saloon's Fiat 131 pushrod four and the Renault
19's carburetted Cleon 1.4 for the estate's.

The fleet saloon played the Punto's and the Bravo's 1.4 T-Jet petrol fours (`fleet/`, Wikimedia Commons, CC BY 3.0, by
Adri Drives and michael maurice) from 0.10 until it became the E 250 CDI, a diesel, and its own banks were cut (below);
those banks are no longer shipped.

## The fleet saloon's diesel (`diesel4/`, diesel6)

| Bank | Source | Segments (start@end s @ firing Hz at start @ search band Hz) |
|---|---|---|
| `diesel4/engine_idle` | Freesound #243773 "Diesel-drive2.wav" and #243783 "Diesel-drive.wav" by fastson, https://freesound.org/s/243773/ and https://freesound.org/s/243783/ (CC0 1.0), notched at 159-166 Hz | #243773 0.5@8.8@26@20@32@2; #243783 28.0@40.0@33.5@28@38@2 (the firing followed on its second harmonic) |
| `diesel4/engine_drive` | the same | #243773 94.4@98.2@45@38@75, 98.45@100.2@61@50@85; #243783 90.3@93.6@54@45@100; #243773 27.4@29.0@58@50@100, 29.0@29.88@91@85@130 |
| `diesel4/engine_overrun` | #243773 (the revs falling slowly after a change up, the level down: the fuel cut) | 32.3@42.0@62@38@70 |

Both recordings are a short drive of one car with a TDI engine (the uploader's tag), recorded with a Sony PCM-M10 and a
Shure SM-58 in the engine compartment (the uploader's description): idling, pulling away, accelerating through the
low gears to about 50 km/h, coasting. The make and model are not given; the firing reads as a four's: at idle the
spectrum is a comb of 6.5 Hz (the camshaft's turn, half the crank's), its strongest lines at 26 and 52 Hz, a four's
firing at 780 rpm and its second harmonic (a three or a five firing at that rate would turn its crank and cam at other
rates than the comb's), and that idle is where the TDIs' sit. Whether its injection is common-rail or by unit injectors
is not said. It is the only legitimately licensed recording found of a car's four-cylinder turbodiesel that holds an
idle, a run-up under load and an overrun, from close to the engine; none of an OM 651 (or of any W212 or W204 CDI) was
found.

The recordings were made at -55 to -60 dBFS, and above about 4 kHz they are the recorder's floor (those bands move by
under 3 dB between the idle and the run-up): cut as they were, the bank tool lifted the floor 40 dB with the engine,
and the grains' sharpest steps stood 2 to 3.6 times their RMS where every other bank's stand at 0.1 to 0.7: hiss at
every firing pulse. Each is first cleaned with `tools/clean-recording.py` (the floor measured in every frequency bin as
its 10th percentile over the file and taken away from 700 Hz up, then a low-pass at 6 kHz; nothing is moved in time),
and the banks are cut from the cleaned files. After it the grains' sharpest steps are 0.3 to 0.9 times their RMS, and
the knock at idle is the recording's: its 1-3 kHz follows the firing (the band's envelope beats 0.54 of its mean at the
firing rate in #243773's idle; in #243783's first idle, 5-21 s, 0.27, with the band above 3 kHz hiss, so that stretch
is not used). The high-quality previews were taken (MP3s of about 190 kbit/s; the WAV originals need a login).
Rebuild:

    python tools/clean-recording.py 243773.mp3 diesel-drive2.wav
    python tools/clean-recording.py 243783.mp3 diesel-drive.wav
    python tools/clean-recording.py 243773.mp3 diesel-drive2-idle.wav 700 6000 159-166
    python tools/clean-recording.py 243783.mp3 diesel-drive-idle.wav 700 6000 159-166
    python tools/make-engine-bank.py game/assets/audio/diesel4/engine_idle 4 "diesel-drive2-idle.wav@0.5@8.8@26@20@32@2" "diesel-drive-idle.wav@28.0@40.0@33.5@28@38@2"
    python tools/make-engine-bank.py game/assets/audio/diesel4/engine_drive 4 "diesel-drive2.wav@94.4@98.2@45@38@75" "diesel-drive2.wav@98.45@100.2@61@50@85" "diesel-drive.wav@90.3@93.6@54@45@100" "diesel-drive2.wav@27.4@29.0@58@50@100" "diesel-drive2.wav@29.0@29.88@91@85@130"
    python tools/make-engine-bank.py game/assets/audio/diesel4/engine_overrun 4 "diesel-drive2.wav@32.3@42.0@62@38@70"

What the recordings give and what they do not: the idle at 780 and about 1,000 rpm (the game's diesel idles at 750),
the run-up under load from 1,300 to 3,425 rpm in five stretches (#243773's run-up is split at 98.2-98.45 s, where the
firing tracker jumped to a partial for a few pulses, and at 29.0 s, past which it follows the firing only within a
higher band), and the overrun from 2,000 down to 1,150 rpm. The fleet saloon's governor is at 4,800 rpm: past 3,425 the
voice lays the run-up's highest pulses closer, as for the V6 hatch, and lifting from high revs plays the overrun's
highest pulses closer still. A recording of the same kind of engine revving higher under load would serve better.

Both recordings carry a steady tone that does not follow the revs (an auxiliary of the car's): 162.7 Hz idling at 780
rpm in #243773, 161.3 at about 980 in #243783, the strongest line of either idle, level with the second firing
harmonic. Cut into grains and laid at the firing rate it was chopped into a buzz: the voice idled with 160-167 Hz 7.1 dB
over its firing line outside and 8.9 in the cabin, where the petrol cars' sit well under theirs (diesel6 review). The
idle is cut from copies notched at 159-166 Hz (the last argument above: a zero-phase second-order band-stop), and the
voice now idles with that band 10.4 dB under its firing line outside and 8.6 in the cabin
(`The_diesel_idles_without_a_steady_hum`). #243783's fifth firing harmonic (about 163 Hz) goes with it; #243773's
sixth (156 Hz) is kept. The run-up and the overrun are cut from the un-notched files: through them every firing harmonic
passes the band in turn, and a notch would cut each as it went by; the tone is no louder than the harmonics around it
there.

The bank tool's tracker cannot follow a firing frequency under about 45 Hz (1,350 rpm for a four): its search band of
12 % either side is then narrower than a bin of its 4,096-sample frames, often holds none, and the estimate stays where it
was. The run-up starts at 1,300 rpm for that reason, and the voice crossfades the idle into it. The idles are followed
on their second harmonic (the segment's last field, 2: a band twice as wide in hertz), since the steady idle did not
save them from it (diesel6 review): followed on the firing, #243773's idle was labelled 26.00 Hz on every pulse and
#243783's 28.63 Hz on 150 of its 352 (its pulses' spacing gives 30.9 there and 32.6 where the tracker followed, its
spectrum 32.5 to 33), 7 to 12 % slow, with 104 of the bank's grains spanning over 1.4 periods on a side where a pulse was
missed. On the second harmonic the labels' medians are 26.07 and 32.84 Hz, as the spectra and the spacing give (26.0 and
32.5-33; 25.6 and 32.8), and 45 grains span over 1.4 periods.

What was searched (2 November 2026), CC0 and CC BY only:

- the Internet Archive's CC0 item `car-engines` (above): 70 tracks, all petrol sports and muscle cars, no diesel;
- Wikimedia Commons (its search for diesel, TDI, CDI, dCi, HDi, CRDi, W212, W204, E-Class and "engine and exhaust
  sound"; its category "Sounds of automobiles", 456 files): four YouTube "engine and exhaust sound" tracks of diesels or
  likely ones, the "Volkswagen Passat B5 1.9 TDI (remapped, 185 hp)" (tomeko0o3769, CC BY 3.0), an "Audi A3" (Importex
  Cartuning & Sound), a "Seat Toledo" and a "Chevrolet Cruze", all with their licence review pending and edited from
  videos (cuts, drive-bys, blips standing, music over the A3's first half); none holds a steady loaded run-up near the
  engine, and none is an OM 651;
- Freesound (searches for diesel car, TDI, CDI, CDTi, CRDi, dCi, HDi, JTD, Multijet, TDCi, 320d, Mercedes diesel,
  Sprinter, C220, E220, diesel engine bay, diesel clatter, diesel rev): besides fastson's, a Mercedes diesel starting
  outside (#213729, PITFACTORY, CC0; model not given, start and idle), a Mercedes diesel's idle and blips (#399773,
  Iridiuss, CC0), a 2026 Mercedes Sprinter revved standing up to about 2,700 rpm (#873236, Robo9418, CC0; its OM 654 is
  the OM 651's successor, but nothing under load), an Opel Astra 1.9 CDTi started, idled and revved in a garage
  (#28641, digifishmusic, CC BY 4.0), a Citroen Jumpy 2.0 HDi likewise (#500193, chlund, CC0), a BMW 320d passing two
  microphones and revved in neutral (#33459-33461, pempi, CC BY 4.0), recordings in the cabin of a 2007 Passat diesel
  (#89948-89951, Loffy, CC BY 3.0), a Hyundai i40 1.7 CRDi on the motorway (#569683, RadioOAF, CC0), a 1.9 diesel in
  Belgium (#521768 and #521769, gecop, CC0) and a Renault Master dCi 135 (#425851, Soundholder, CC BY 3.0), where the
  road and the wind cover the firing; and phone clips of a Skoda Octavia TDI idling ten metres away (#827862,
  Mihacappy, CC0) and a New Beetle TDI (#448217, Evilmikie, CC0).

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

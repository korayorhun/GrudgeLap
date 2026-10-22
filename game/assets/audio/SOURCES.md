# Recordings

## Engines (`flat12/`, `v8/`)

The loops in this folder are short slices cut from recordings in the Internet Archive item
**"Car Engine Sounds"** (identifier `car-engines`, uploaded 2015-09-01, dated 2011-02-22), which its uploader
published under **CC0 1.0 Universal** (`licenseurl` in the item's metadata:
http://creativecommons.org/publicdomain/zero/1.0/). Item page: https://archive.org/details/car-engines

| Loop | Source track | Slice | Nominal rpm |
|---|---|---|---|
| `v8/low.wav` | `3 Ferrari 458.mp3` | 0.32 s + 0.36 s | 2700 |
| `v8/mid.wav` | `3 Ferrari 458.mp3` | 7.35 s + 0.75 s | 5800 |
| `v8/high.wav` | `3 Ferrari 458.mp3` | 3.45 s + 0.55 s | 8400 |
| `v8/coast.wav` | `3 Ferrari 458.mp3` | 15.3 s + 1.0 s (off throttle) | 6300 |
| `flat12/low.wav` | `15 Lamborghini Countach.mp3` | 0.12 s + 0.40 s | 3000 |
| `flat12/mid.wav` | `15 Lamborghini Countach.mp3` | 7.0 s + 1.0 s | 4750 |
| `flat12/high.wav` | `15 Lamborghini Countach.mp3` | 4.3 s + 0.45 s | 6650 |

Processing: decoded to mono 22.05 kHz, the tail of each slice folded into its head with an equal-power
cross-fade so the loop is seamless, DC removed, peak-normalised. The nominal rpm is the firing-frequency
estimate of the slice (harmonic product spectrum; rpm = f0 x 120 / cylinders), which only needs to be
consistent between the loops of one set: the game pitches each loop by rpm / nominal.

## Tyres (`tyres/`)

| Loop | Source | Slice | License |
|---|---|---|---|
| `tyres/squeal.wav` | Freesound #850041 "tire-squeal" by JeffPearson, https://freesound.org/s/850041/ | 18.6 s + 3.0 s | CC0 1.0 |
| `tyres/squeal_hard.wav` | Internet Archive item `Sound_Effects_2` ("Sounds Effects 2", Wayzata Technology, 1995), file "squealing tires", https://archive.org/details/Sound_Effects_2 | 2.0 s + 4.0 s | CC0 1.0 (item `licenseurl`) |
| `tyres/skid.wav` | BigSoundBank #2370 "Screeching tires #3", https://bigsoundbank.com/detail-2370-screeching-tires-3.html | 0.35 s + 1.2 s | CC0 1.0 |
| `tyres/gravel.wav` | Freesound #341069 "gravel road" by seth-m, https://freesound.org/s/341069/ | 1.0 s + 4.0 s | CC0 1.0 |

Cut with `tools/make-loop.py` like the engine loops (mono 22.05 kHz, cross-faded tail, peak-normalised). The
game pitches and fades them from the slip of the four wheels (`TyreAudio`). Road roar, wind and bumps are
synthesised.

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

No recording of a flat-twelve with a usable licence was found; the twelve-cylinder loops of the 1984 car
are from the Countach track, the closest in era and layout. Swap the files (same names, same nominal rpm
order) to use another recording.

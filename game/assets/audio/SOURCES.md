# Engine recordings

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

No recording of a flat-twelve with a usable licence was found; the twelve-cylinder loops of the 1984 car
are from the Countach track, the closest in era and layout. Swap the files (same names, same nominal rpm
order) to use another recording.

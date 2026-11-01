"""Cuts an engine grain bank out of recordings, for the granular engine voice (audio/GranularEngine.cs).

Usage: python make-engine-bank.py <out prefix> <cylinders> <segment> [<segment> ...]
       segment = <recording>@<start s>@<end s>@<firing Hz at start>@<lowest Hz>@<highest Hz>

Writes <out prefix>.wav (mono 16-bit PCM at 44.1 kHz: the segments one after another) and <out prefix>.json
(the grain marks: a sample position and the firing frequency there, for every firing pulse).

A segment should hold one engine at steady or smoothly changing revs (a run up through a gear, an idle). The
firing frequency (rpm x cylinders / 120 for a four-stroke) is followed frame by frame from the given start,
never jumping more than 12 % between frames, so a harmonic cannot take over. Marks are laid one firing period
apart by integrating it, and each is moved onto the nearest peak of the firing component (the recording low-
passed just above the firing frequency): every grain then starts its pulse at the same point, so neighbouring
grains overlap in phase however they are picked later. Each segment is normalised to the same loudness.
"""
import json
import site
import subprocess
import sys
import wave

sys.path.append(site.getusersitepackages())
import numpy as np

SR = 44100


def load(path, start, end):
    # Seeking after the input decodes the file from its start and drops what comes before `start`: exact to the sample.
    # Seeking before it started every MP3 segment with the decoder's silence, a few grains near -60 dB or below that the
    # voice played as drop-outs wherever it picked them.
    raw = subprocess.run(["ffmpeg", "-v", "error", "-i", path, "-ss", str(start), "-t", str(end - start),
                          "-ac", "1", "-ar", str(SR), "-f", "f32le", "-"], capture_output=True, check=True).stdout
    return np.frombuffer(raw, dtype=np.float32).astype(np.float64)


TRACK_PAD = 2048


def track(x, f_start, lo, hi, hop=220, win=4096):
    """The firing frequency at every sample, following the strongest peak within 12 % of the last estimate."""
    w = np.hanning(win)
    freqs = np.fft.rfftfreq(win, 1 / SR)
    estimates, prev = [], f_start
    for i in range(0, len(x) - win, hop):
        s = np.abs(np.fft.rfft(x[i:i + win] * w))
        band = (freqs > prev * 0.88) & (freqs < prev * 1.12) & (freqs > lo) & (freqs < hi)
        if band.any():
            k = int(np.argmax(np.where(band, s, 0)))
            a, b, c = np.log(s[k - 1:k + 2] + 1e-12)
            curvature = a - 2 * b + c
            f = (k + (0.5 * (a - c) / curvature if curvature != 0 else 0.0)) * SR / win
            # Held to the search band: a nearly flat top (curvature near zero) threw the parabola's vertex hundreds of
            # hertz off, out of the band, and the estimate never came back (the Pulsar's run-up re-cut from its first
            # samples ran off to 898 Hz). The vertex is not held to its bin: at low firing frequencies the band is
            # narrower than a bin, and the vertex is all the tracker climbs by.
            f = float(np.clip(f, lo, hi))
            prev = 0.7 * prev + 0.3 * f
        estimates.append(prev)
    centres = np.arange(len(estimates)) * hop + win / 2
    return np.interp(np.arange(len(x)), centres, estimates)


def lowpass(x, cutoff):
    """A zero-phase pair of one-pole low-passes: the firing component, without shifting its peaks."""
    a = np.exp(-2 * np.pi * cutoff / SR)
    y = np.empty_like(x)
    acc = 0.0
    for i, v in enumerate(x):
        acc = (1 - a) * v + a * acc
        y[i] = acc
    acc = 0.0
    for i in range(len(y) - 1, -1, -1):
        acc = (1 - a) * y[i] + a * acc
        y[i] = acc
    return y


def marks(x, f):
    smooth = lowpass(x, float(np.median(f)) * 1.3)
    phase = np.cumsum(f / SR)
    out = []
    for m in np.searchsorted(phase, np.arange(1, int(phase[-1]))):
        period = SR / f[m]
        lo, hi = int(m - period / 3), int(m + period / 3)
        if lo >= 0 and hi < len(x):
            out.append(lo + int(np.argmax(smooth[lo:hi])))
    out = sorted(set(out))
    kept = [out[0]]
    for m in out[1:]:
        if m - kept[-1] > 0.5 * SR / f[m]:   # a peak found twice
            kept.append(m)
    return np.array(kept)


def crackles(x, m, factor=4.0):
    """
    The stretches (first and last sample) of grains holding a step between samples far sharper than the segment's grains
    have (more than `factor` times their median sharpest step against their level): the crackle of an overloaded
    microphone or of a clipped recording, which the voice would replay as a click wherever it picks that grain (the V6
    hatch's run-up, cut at first through the crackle at the top of every blip of the throttle).
    """
    sharp = []
    for i in range(1, len(m) - 1):
        g = x[m[i - 1]:m[i + 1]]
        sharp.append(np.abs(np.diff(g)).max() / max(np.sqrt(np.mean(g * g)), 1e-9))
    if not sharp:
        return []
    typical = float(np.median(sharp))
    spans = []
    for i, s in enumerate(sharp):
        if s <= factor * typical:
            continue
        # Grains overlapping one crackle (or crackles a period apart) are reported as one stretch of the recording.
        if spans and m[i] <= spans[-1][1]:
            spans[-1][1] = int(m[i + 2])
        else:
            spans.append([int(m[i]), int(m[i + 2])])
    return spans


def main():
    prefix, cylinders = sys.argv[1], int(sys.argv[2])
    pieces, all_marks, all_firing, offset = [], [], [], 0
    for spec in sys.argv[3:]:
        path, start, end, f0, lo, hi = spec.split("@")
        # The tracker's frames are 4096 samples long and centred on their positions: tracked over the segment alone, its
        # first and last 46 ms held the frequency of the frames at either end, and on a fast run-up (the V6's blips of
        # the throttle, which gain 1000 rpm in a tenth of a second) the last pulses were laid at revs long passed.
        # Tracked with half a frame of the recording on either side, the first frame is centred on the segment's start.
        lead = min(TRACK_PAD / SR, float(start))
        around = load(path, float(start) - lead, float(end) + TRACK_PAD / SR)
        first = int(round(lead * SR))
        x = around[first:first + int(round((float(end) - float(start)) * SR))].copy()
        x -= x.mean()
        x *= 0.25 / max(np.sqrt(np.mean(x * x)), 1e-9)
        f = track(around - around.mean(), float(f0), float(lo), float(hi))[first:first + len(x)]
        m = marks(x, f)
        # Grains need a whole period on each side; the first and last marks of a segment only bound others.
        all_marks.extend((m + offset).tolist())
        all_firing.extend(np.round(f[m], 2).tolist())
        print(f"{path} {start}-{end} s: {len(m)} pulses, {f[m].min():.0f}..{f[m].max():.0f} Hz "
              f"({f[m].min() * 120 / cylinders:.0f}..{f[m].max() * 120 / cylinders:.0f} rpm)")
        for first, last in crackles(x, m):
            print(f"  warning: crackles at {float(start) + first / SR:.3f}-{float(start) + last / SR:.3f} s (an overloaded "
                  f"or clipped recording: end the segment before them, or the voice clicks there)")
        pieces.append(x)
        offset += len(x)
    y = np.concatenate(pieces)
    peak = np.max(np.abs(y))
    if peak > 0.99:
        y *= 0.99 / peak
    with wave.open(prefix + ".wav", "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes((y * 32767).astype(np.int16).tobytes())
    # Segment boundaries split grains: a mark whose neighbour lies in another segment cannot be used.
    bounds = np.cumsum([len(p) for p in pieces])
    segment = np.searchsorted(bounds, np.array(all_marks), side="right")
    json.dump({"rate": SR, "cylinders": cylinders, "marks": all_marks, "firing": all_firing,
               "segment": segment.tolist()}, open(prefix + ".json", "w"), separators=(",", ":"))
    print(f"{prefix}: {len(y) / SR:.1f} s, {len(all_marks)} grains")


main()

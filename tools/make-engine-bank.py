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
    raw = subprocess.run(["ffmpeg", "-v", "error", "-ss", str(start), "-t", str(end - start), "-i", path,
                          "-ac", "1", "-ar", str(SR), "-f", "f32le", "-"], capture_output=True, check=True).stdout
    return np.frombuffer(raw, dtype=np.float32).astype(np.float64)


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


def main():
    prefix, cylinders = sys.argv[1], int(sys.argv[2])
    pieces, all_marks, all_firing, offset = [], [], [], 0
    for spec in sys.argv[3:]:
        path, start, end, f0, lo, hi = spec.split("@")
        x = load(path, float(start), float(end))
        x -= x.mean()
        x *= 0.25 / max(np.sqrt(np.mean(x * x)), 1e-9)
        f = track(x, float(f0), float(lo), float(hi))
        m = marks(x, f)
        # Grains need a whole period on each side; the first and last marks of a segment only bound others.
        all_marks.extend((m + offset).tolist())
        all_firing.extend(np.round(f[m], 2).tolist())
        print(f"{path} {start}-{end} s: {len(m)} pulses, {f[m].min():.0f}..{f[m].max():.0f} Hz "
              f"({f[m].min() * 120 / cylinders:.0f}..{f[m].max() * 120 / cylinders:.0f} rpm)")
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

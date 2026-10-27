"""Draws the cloud sheet, game/assets/textures/clouds.png: a tileable RGBA square of fair-weather cumulus, white with
a soft grey underside, on a transparent ground. The world hangs it as one wide plane high over the circuit and
drifts it (WorldBuilder.BuildClouds). Deterministic; no source material.
Usage: python make-clouds.py [out.png]
"""
import site
import sys

sys.path.append(site.getusersitepackages())
import numpy as np
from PIL import Image

SIZE = 1024
out = sys.argv[1] if len(sys.argv) > 1 else "game/assets/textures/clouds.png"
rng = np.random.default_rng(1989)


def fractal(beta, seed_offset=0):
    """Periodic fractal noise: white noise shaped in the frequency domain, so the sheet tiles without a seam."""
    white = np.random.default_rng(1989 + seed_offset).standard_normal((SIZE, SIZE))
    spectrum = np.fft.fft2(white)
    fy = np.fft.fftfreq(SIZE)[:, None]
    fx = np.fft.fftfreq(SIZE)[None, :]
    f = np.sqrt(fx * fx + fy * fy)
    f[0, 0] = 1.0
    shaped = spectrum / f ** beta
    shaped[0, 0] = 0.0
    field = np.real(np.fft.ifft2(shaped))
    return (field - field.mean()) / field.std()


# Large soft masses with finer detail on their edges; the detail is sharpened so the edges look puffy.
mass = fractal(2.0)
detail = fractal(1.4, 7)
field = mass + 0.22 * detail


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


# Coverage about a third of the sky; the edge softens over a narrow band so the cards do not read as cut-outs.
alpha = smoothstep(0.55, 1.35, field)
# Shading: the cloud's thickness darkens its underside a little (the sheet is seen from below).
thickness = smoothstep(0.45, 2.2, field)
grey = 1.0 - 0.14 * thickness
rgb = np.stack([grey, grey, grey * 1.01], axis=-1)
img = np.concatenate([np.clip(rgb, 0, 1), alpha[..., None]], axis=-1)
Image.fromarray((img * 255).astype(np.uint8), "RGBA").save(out, optimize=True)
print(f"{out}: {SIZE}x{SIZE}, cover {alpha.mean():.2f}")

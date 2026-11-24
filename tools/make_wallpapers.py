"""Draws RemSox's default desktop wallpaper and lock screen and writes them as QOI images.

The kernel ships the results (RemSox.Kernel/Resources/Wallpapers/desktop.qoi and lock.qoi) and
copies them to /system/wallpapers at boot; this script is only how they were made, so they can
be redrawn:

    python tools/make_wallpapers.py RemSox.Kernel/Resources/Wallpapers [--preview DIR]

Both are layered hills under a sky, dusk for the desktop and night for the lock screen. The
colours change mostly from top to bottom, so rows repeat and the QOI files stay small: they go
to remote desktop viewers too. Needs numpy (and Pillow for --preview).
"""

import argparse
import os
import struct

import numpy as np

WIDTH, HEIGHT = 1920, 1080


def lerp_colors(stops, t):
    """Colour at t (0..1, any shape) along [(position, (r, g, b)), ...]."""
    positions = np.array([p for p, _ in stops])
    colors = np.array([c for _, c in stops], dtype=float)
    return np.stack([np.interp(t, positions, colors[:, i]) for i in range(3)], axis=-1)


def ridge(x, base, waves):
    """The top edge of a hill layer: base height plus a few sines, in pixels from the top."""
    y = np.full_like(x, base, dtype=float)
    for amplitude, period, phase in waves:
        y += amplitude * np.sin(2 * np.pi * x / period + phase)
    return y


def draw(sky, layers, moon=None):
    ys = np.arange(HEIGHT, dtype=float)[:, None]
    xs = np.arange(WIDTH, dtype=float)[None, :]
    image = np.broadcast_to(lerp_colors(sky, ys / HEIGHT), (HEIGHT, WIDTH, 3)).copy()

    if moon is not None:
        (cx, cy, radius, color, glow) = moon
        distance = np.sqrt((xs - cx) ** 2 + (ys - cy) ** 2)
        halo = np.clip(1 - (distance - radius) / glow, 0, 1) ** 2 * 0.35
        image += (np.array(color) - image) * halo[..., None]
        disc = np.clip(radius + 0.5 - distance, 0, 1)
        image += (np.array(color) - image) * disc[..., None]

    for base, waves, top_color, bottom_color in layers:
        edge = ridge(xs[0], base, waves)[None, :]
        # How much of each pixel lies below the ridge: anti-aliases the edge.
        coverage = np.clip(ys + 1 - edge, 0, 1)
        depth = np.clip((ys - edge) / (HEIGHT - edge.min()), 0, 1)
        fill = lerp_colors([(0, top_color), (1, bottom_color)], depth)
        image = image * (1 - coverage[..., None]) + fill * coverage[..., None]

    return np.clip(np.rint(image), 0, 255).astype(np.uint8)


def desktop():
    sky = [(0.0, (22, 24, 48)), (0.45, (58, 44, 92)), (0.72, (150, 86, 112)), (0.85, (214, 140, 120))]
    layers = [
        (640, [(38, 1400, 0.4), (16, 520, 1.3)], (92, 60, 104), (70, 46, 88)),
        (735, [(30, 1100, 2.1), (12, 430, 0.2)], (60, 40, 84), (44, 30, 68)),
        (830, [(26, 1700, 4.0), (10, 610, 2.6)], (38, 28, 62), (28, 22, 50)),
        (930, [(22, 1300, 1.0), (8, 380, 5.1)], (22, 18, 40), (16, 14, 30)),
    ]
    return draw(sky, layers)


def lock():
    sky = [(0.0, (8, 10, 24)), (0.55, (20, 30, 62)), (0.85, (44, 62, 104))]
    layers = [
        (700, [(34, 1500, 2.4), (14, 560, 0.7)], (34, 46, 82), (26, 36, 66)),
        (800, [(28, 1150, 0.9), (11, 470, 3.3)], (24, 32, 62), (18, 26, 50)),
        (900, [(24, 1650, 3.5), (9, 590, 1.9)], (14, 20, 42), (10, 15, 32)),
    ]
    moon = (1420, 250, 46, (236, 232, 214), 150)
    return draw(sky, layers, moon)


def to_qoi(pixels):
    """QOI, 3 channels (the images are opaque), as https://qoiformat.org describes."""
    height, width, _ = pixels.shape
    out = bytearray(b"qoif" + struct.pack(">IIBB", width, height, 3, 0))
    index = [None] * 64
    previous = (0, 0, 0)
    run = 0
    flat = [tuple(p) for p in pixels.reshape(-1, 3).tolist()]
    for i, pixel in enumerate(flat):
        if pixel == previous:
            run += 1
            if run == 62 or i == len(flat) - 1:
                out.append(0xC0 | (run - 1))
                run = 0
            continue
        if run:
            out.append(0xC0 | (run - 1))
            run = 0
        r, g, b = pixel
        h = (r * 3 + g * 5 + b * 7 + 255 * 11) % 64
        if index[h] == pixel:
            out.append(h)
        else:
            index[h] = pixel
            dr = (r - previous[0] + 128) % 256 - 128
            dg = (g - previous[1] + 128) % 256 - 128
            db = (b - previous[2] + 128) % 256 - 128
            if -2 <= dr <= 1 and -2 <= dg <= 1 and -2 <= db <= 1:
                out.append(0x40 | (dr + 2) << 4 | (dg + 2) << 2 | (db + 2))
            elif -32 <= dg <= 31 and -8 <= dr - dg <= 7 and -8 <= db - dg <= 7:
                out += bytes((0x80 | (dg + 32), (dr - dg + 8) << 4 | (db - dg + 8)))
            else:
                out += bytes((0xFE, r, g, b))
        previous = pixel
    out += bytes((0, 0, 0, 0, 0, 0, 0, 1))
    return bytes(out)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("out", help="where to write desktop.qoi and lock.qoi")
    parser.add_argument("--preview", help="also write PNG previews here")
    args = parser.parse_args()

    os.makedirs(args.out, exist_ok=True)
    for name, image in (("desktop", desktop()), ("lock", lock())):
        data = to_qoi(image)
        with open(os.path.join(args.out, name + ".qoi"), "wb") as f:
            f.write(data)
        print(f"{name}.qoi  {len(data) // 1024} KB")
        if args.preview:
            from PIL import Image

            os.makedirs(args.preview, exist_ok=True)
            Image.fromarray(image).save(os.path.join(args.preview, name + ".png"))


if __name__ == "__main__":
    main()

"""Builds RemSox's cursor set from Bibata Modern Ice and writes it as Windows .cur files.

The kernel ships the result (RemSox.Kernel/Resources/Cursors/*.cur) and copies it to
/system/cursors at boot; this script is only how those files were made, so they can be made
again (another size, another Bibata variant):

    python tools/make_cursors.py BITMAPS_DIR OUT_DIR [--size 22]

BITMAPS_DIR is one variant's folder from Bibata's bitmaps.zip (a GitHub release of
https://github.com/ful1e5/Bibata_Cursor, e.g. bitmaps/Bibata-Modern-Ice): 256x256 PNGs.
Each cursor is scaled to SIZE pixels and written as a 32-bit image with alpha, never a PNG
inside the .cur (RemSox's reader takes only uncompressed images). Animated cursors (busy,
working) keep their first frame. Needs Pillow.

Bibata is by Abdulkaiz Khatri (ful1e5), GPL-3.0; see Resources/Cursors/Bibata-LICENSE.txt.
"""

import argparse
import os
import struct

from PIL import Image

SOURCE_SIZE = 256

# RemSox's name (a Windows pointer role), Bibata's image, and its hotspot at 256x256, from
# Bibata's configs/normal/win_rg.build.toml (cursors it gives no hotspot point from the middle).
CURSORS = [
    ("arrow", "left_ptr.png", (55, 17)),               # Normal select
    ("help", "question_arrow.png", (42, 86)),          # Help select
    ("working", "left_ptr_watch-01.png", (55, 17)),    # Working in background
    ("busy", "wait-01.png", (128, 128)),               # Busy
    ("crosshair", "crosshair.png", (128, 128)),        # Precision select
    ("text", "xterm.png", (128, 128)),                 # Text select
    ("pen", "pencil.png", (46, 211)),                  # Handwriting
    ("unavailable", "circle.png", (55, 17)),           # Unavailable
    ("resize-ns", "sb_v_double_arrow.png", (128, 128)),    # Vertical resize
    ("resize-ew", "sb_h_double_arrow.png", (128, 128)),    # Horizontal resize
    ("resize-nwse", "bd_double_arrow.png", (128, 128)),    # Diagonal resize 1
    ("resize-nesw", "fd_double_arrow.png", (128, 128)),    # Diagonal resize 2
    ("move", "move.png", (128, 128)),                  # Move
    ("alternate", "right_ptr.png", (204, 17)),         # Alternate select
    ("link", "hand2.png", (114, 18)),                  # Link select
    ("grab", "hand1.png", (144, 79)),                  # Pan
    ("grabbing", "grabbing.png", (128, 66)),           # Grabbing
    ("zoom-in", "zoom-in.png", (116, 116)),
    ("zoom-out", "zoom-out.png", (116, 116)),
    ("person", "person.png", (55, 17)),                # Person select
    ("pin", "pin.png", (55, 17)),                      # Location select
]


def to_cur(image, hotspot):
    """A .cur file holding one 32-bit image (BGRA rows bottom-up, then an all-zero AND mask)."""
    width, height = image.size
    pixels = image.load()
    xor = bytearray()
    for y in reversed(range(height)):
        for x in range(width):
            r, g, b, a = pixels[x, y]
            xor += bytes((b, g, r, a))
    mask = bytes(((width + 31) // 32) * 4 * height)
    header = struct.pack("<IiiHHIIiiII", 40, width, height * 2, 1, 32, 0, len(xor) + len(mask), 0, 0, 0, 0)
    data = header + xor + mask
    directory = struct.pack("<HHH", 0, 2, 1)
    entry = struct.pack("<BBBBHHII", width % 256, height % 256, 0, 0, hotspot[0], hotspot[1], len(data), 6 + 16)
    return directory + entry + data


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("bitmaps", help="a Bibata variant's folder of 256x256 PNGs")
    parser.add_argument("out", help="where to write the .cur files")
    parser.add_argument("--size", type=int, default=22, help="cursor size in pixels (default 22, like Bibata's Windows build)")
    args = parser.parse_args()

    os.makedirs(args.out, exist_ok=True)
    scale = args.size / SOURCE_SIZE
    for name, png, (hx, hy) in CURSORS:
        source = Image.open(os.path.join(args.bitmaps, png)).convert("RGBA")
        if source.size != (SOURCE_SIZE, SOURCE_SIZE):
            raise SystemExit(f"{png} is {source.size}, not {SOURCE_SIZE}x{SOURCE_SIZE}")
        image = source.resize((args.size, args.size), Image.LANCZOS)
        hotspot = (min(args.size - 1, round(hx * scale)), min(args.size - 1, round(hy * scale)))
        with open(os.path.join(args.out, name + ".cur"), "wb") as f:
            f.write(to_cur(image, hotspot))
        print(f"{name}.cur  hotspot {hotspot[0]},{hotspot[1]}")


if __name__ == "__main__":
    main()

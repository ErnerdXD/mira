#!/usr/bin/env python3
"""
Generate the app tile and logo assets for Mira.

Pure standard library -- no Pillow, no ImageMagick, no external tools. Just
zlib and struct, so this runs anywhere Python does and the assets stay
reproducible from source.

Shapes are defined as signed distance fields and rendered with 4x supersampling,
which gives clean anti-aliased edges without a drawing library.

Usage:
    python scripts/generate-assets.py [output_dir]

Default output_dir is src/MirrorReceiver/Assets.
"""

from __future__ import annotations

import math
import os
import struct
import sys
import zlib

# --------------------------------------------------------------------------- #
# Supersampling factor. 4 gives good edges; raise for even smoother output.
# --------------------------------------------------------------------------- #
SS = 4

# Brand gradient: violet (top-left) -> deep indigo (bottom-right).
GRAD_A = (124, 58, 237)    # #7C3AED
GRAD_B = (49, 46, 129)     # #312E81
MARK = (255, 255, 255)


# --------------------------------------------------------------------------- #
# PNG writer
# --------------------------------------------------------------------------- #
def encode_png(width: int, height: int, pixels: bytes) -> bytes:
    """pixels: raw RGBA bytes, length == width * height * 4. Returns PNG bytes."""

    def chunk(tag: bytes, data: bytes) -> bytes:
        return (
            struct.pack(">I", len(data))
            + tag
            + data
            + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
        )

    raw = bytearray()
    stride = width * 4
    for y in range(height):
        raw.append(0)  # filter type 0 (None)
        raw += pixels[y * stride:(y + 1) * stride]

    out = b"\x89PNG\r\n\x1a\n"
    out += chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
    out += chunk(b"IDAT", zlib.compress(bytes(raw), 9))
    out += chunk(b"IEND", b"")
    return out


def write_png(path: str, width: int, height: int, pixels: bytes) -> None:
    with open(path, "wb") as fh:
        fh.write(encode_png(width, height, pixels))


def write_ico(path: str, images: list[tuple[int, int, bytes]]) -> None:
    """
    Write a multi-resolution .ico using PNG-compressed entries.

    PNG entries inside ICO are supported by Windows Vista and later, and using
    them means one encoder for both outputs. Width/height of 0 means 256.
    """
    count = len(images)
    header = struct.pack("<HHH", 0, 1, count)

    entries = b""
    payload = b""
    offset = 6 + 16 * count

    for width, height, pixels in images:
        data = encode_png(width, height, pixels)
        entries += struct.pack(
            "<BBBBHHII",
            0 if width >= 256 else width,
            0 if height >= 256 else height,
            0,                       # palette colours
            0,                       # reserved
            1,                       # colour planes
            32,                      # bits per pixel
            len(data),
            offset,
        )
        payload += data
        offset += len(data)

    with open(path, "wb") as fh:
        fh.write(header + entries + payload)


# --------------------------------------------------------------------------- #
# Signed distance fields (negative == inside)
# --------------------------------------------------------------------------- #
def sd_rounded_rect(px, py, cx, cy, hw, hh, r) -> float:
    r = min(r, hw, hh)
    dx = abs(px - cx) - hw + r
    dy = abs(py - cy) - hh + r
    ax, ay = max(dx, 0.0), max(dy, 0.0)
    return math.hypot(ax, ay) + min(max(dx, dy), 0.0) - r


def coverage(d: float, feather: float) -> float:
    """1 inside, 0 outside, smooth across a band of width `feather`."""
    return min(max(0.5 - d / feather, 0.0), 1.0)


# --------------------------------------------------------------------------- #
# The mark: a screen outline with the classic "cast" waves in its lower-left.
# All geometry is expressed in a 0..1 unit box so it scales to any tile.
# --------------------------------------------------------------------------- #
SCREEN_CX, SCREEN_CY = 0.50, 0.42
SCREEN_HW, SCREEN_HH = 0.44, 0.28
SCREEN_R = 0.07
SCREEN_STROKE = 0.085

WAVE_OX, WAVE_OY = 0.20, 0.70       # wave origin (lower-left, inside screen)
WAVE_DOT_R = 0.055
WAVE_RADII = (0.15, 0.25)
WAVE_STROKE = 0.055


def mark_alpha(u: float, v: float) -> float:
    """
    Coverage of the mark at unit coords (u, v), both in 0..1.
    Returns 0..1.
    """
    a = 0.0

    # --- screen outline -------------------------------------------------- #
    d = sd_rounded_rect(u, v, SCREEN_CX, SCREEN_CY, SCREEN_HW, SCREEN_HH, SCREEN_R)
    a = max(a, coverage(abs(d) - SCREEN_STROKE * 0.5, 0.012))

    # --- cast waves: only the quadrant up-and-right of the origin -------- #
    dx = u - WAVE_OX
    dy = v - WAVE_OY
    if dx > -0.02 and dy < 0.02:
        dist = math.hypot(dx, dy)

        # centre dot
        a = max(a, coverage(dist - WAVE_DOT_R, 0.012))

        # concentric arcs
        for radius in WAVE_RADII:
            a = max(a, coverage(abs(dist - radius) - WAVE_STROKE * 0.5, 0.012))

    return min(a, 1.0)


# --------------------------------------------------------------------------- #
# Tile renderers
# --------------------------------------------------------------------------- #
def gradient(u: float, v: float) -> tuple[int, int, int]:
    t = min(max((u + v) * 0.5, 0.0), 1.0)
    return tuple(int(round(GRAD_A[i] + (GRAD_B[i] - GRAD_A[i]) * t)) for i in range(3))


def render_square(size: int, bg: bool = True, radius_frac: float = 0.22,
                  mark_scale: float = 0.66) -> bytes:
    """
    Square tile. `bg=False` produces a transparent-background "unplated" logo
    for taskbar/alt-form use.
    """
    hi = size * SS
    buf = [[(0, 0, 0, 0)] * hi for _ in range(hi)]

    for y in range(hi):
        v = (y + 0.5) / hi
        for x in range(hi):
            u = (x + 0.5) / hi

            if bg:
                d_tile = sd_rounded_rect(u, v, 0.5, 0.5, 0.5, 0.5, radius_frac)
                tile_a = coverage(d_tile, 1.0 / hi)
                if tile_a <= 0.0:
                    continue
                r, g, b = gradient(u, v)
            else:
                tile_a = 0.0
                r = g = b = 0

            # mark, scaled and centred inside the unit box
            mu = (u - 0.5) / mark_scale + 0.5
            mv = (v - 0.5) / mark_scale + 0.5
            ma = mark_alpha(mu, mv) if (0.0 <= mu <= 1.0 and 0.0 <= mv <= 1.0) else 0.0

            if bg:
                r = int(r * (1 - ma) + MARK[0] * ma)
                g = int(g * (1 - ma) + MARK[1] * ma)
                b = int(b * (1 - ma) + MARK[2] * ma)
                buf[y][x] = (r, g, b, int(tile_a * 255))
            else:
                buf[y][x] = (255, 255, 255, int(ma * 255))

    return downsample(buf, hi, hi, out_w=size, out_h=size)


def render_rect(width: int, height: int, mark_frac: float) -> bytes:
    """Full-bleed gradient background with the mark centred."""
    hi_w, hi_h = width * SS, height * SS
    buf = [[(0, 0, 0, 0)] * hi_w for _ in range(hi_h)]

    # The mark is drawn in a centred square of side `mark_frac` of the height.
    box = mark_frac * height

    for y in range(hi_h):
        v = (y + 0.5) / hi_h
        for x in range(hi_w):
            u = (x + 0.5) / hi_w
            r, g, b = gradient(u, v)

            # map to the centred mark box (in pixels, then to unit coords)
            px = (x + 0.5) / SS
            py = (y + 0.5) / SS
            mu = (px - (width - box) * 0.5) / box
            mv = (py - (height - box) * 0.5) / box
            ma = mark_alpha(mu, mv) if (0.0 <= mu <= 1.0 and 0.0 <= mv <= 1.0) else 0.0

            r = int(r * (1 - ma) + MARK[0] * ma)
            g = int(g * (1 - ma) + MARK[1] * ma)
            b = int(b * (1 - ma) + MARK[2] * ma)
            buf[y][x] = (r, g, b, 255)

    return downsample(buf, hi_w, hi_h, out_w=width, out_h=height)


def downsample(buf, hi_w, hi_h, out_w=None, out_h=None) -> bytes:
    """Box-filter the supersampled buffer down to final size."""
    out_w = out_w or (hi_w // SS)
    out_h = out_h or (hi_h // SS)
    out = bytearray()
    for oy in range(out_h):
        for ox in range(out_w):
            rs = gs = bs = as_ = 0
            for sy in range(SS):
                row = buf[oy * SS + sy]
                for sx in range(SS):
                    r, g, b, a = row[ox * SS + sx]
                    rs += r * a; gs += g * a; bs += b * a; as_ += a
            n = SS * SS
            if as_ == 0:
                out += bytes((0, 0, 0, 0))
            else:
                out += bytes((rs // as_, gs // as_, bs // as_, as_ // n))
    return bytes(out)


# --------------------------------------------------------------------------- #
def main() -> int:
    here = os.path.dirname(os.path.abspath(__file__))
    out_dir = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
        here, "..", "src", "MirrorReceiver", "Assets"
    )
    out_dir = os.path.abspath(out_dir)
    os.makedirs(out_dir, exist_ok=True)

    # (filename, width, height, renderer)
    jobs = [
        ("StoreLogo.png", 50, 50,
         lambda: render_square(50, mark_scale=0.70)),
        ("Square44x44Logo.png", 44, 44,
         lambda: render_square(44, mark_scale=0.72)),
        ("Square150x150Logo.png", 150, 150,
         lambda: render_square(150, mark_scale=0.66)),
        ("Wide310x150Logo.png", 310, 150,
         lambda: render_rect(310, 150, 0.62)),
        ("SplashScreen.png", 620, 300,
         lambda: render_rect(620, 300, 0.72)),
        ("Square44x44Logo.targetsize-24_altform-unplated.png", 24, 24,
         lambda: render_square(24, bg=False, mark_scale=0.94)),
    ]

    for name, width, height, fn in jobs:
        data = fn()
        expected = width * height * 4
        if len(data) != expected:
            raise RuntimeError(
                f"{name}: renderer produced {len(data)} bytes, expected {expected}"
            )
        write_png(os.path.join(out_dir, name), width, height, data)
        print(f"  {name:56} {width:>3}x{height:<3}  {len(data):>7,} B")

    print(f"\nWrote {len(jobs)} assets to {out_dir}")

    # --- multi-resolution .ico for Mira.exe ------------------------------- #
    ico_dir = os.path.abspath(os.path.join(here, "..", "app"))
    os.makedirs(ico_dir, exist_ok=True)
    ico_path = os.path.join(ico_dir, "mira.ico")

    # Windows picks the closest size; supply the common shell sizes.
    ico_sizes = [16, 24, 32, 48, 64, 128, 256]
    images = [(s, s, render_square(s, mark_scale=0.78)) for s in ico_sizes]
    write_ico(ico_path, images)
    print(f"Wrote icon to {ico_path}  ({os.path.getsize(ico_path):,} bytes, "
          f"{len(ico_sizes)} sizes)")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())

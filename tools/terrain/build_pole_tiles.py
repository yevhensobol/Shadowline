import argparse
import gzip
import json
from pathlib import Path

import numpy as np
import rasterio

UNIT_M = 0.5


def to_units(heights):
    return np.clip(np.round(heights / UNIT_M), -32767, 32767).astype(np.int32)


def delta(units):
    d = units.copy()
    d[:, 1:] -= units[:, :-1]
    d[1:, :] -= units[:-1, :]
    d[1:, 1:] += units[:-1, :-1]
    return d.astype("<i2")


def outside(r, c, tile, spacing, half):
    x0 = -half + c * tile * spacing
    x1 = min(x0 + tile * spacing, half)
    y1 = half - r * tile * spacing
    y0 = max(y1 - tile * spacing, -half)
    nx = 0.0 if x0 <= 0 <= x1 else min(abs(x0), abs(x1))
    ny = 0.0 if y0 <= 0 <= y1 else min(abs(y0), abs(y1))
    return nx * nx + ny * ny > half * half


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dem", required=True)
    parser.add_argument("--out-dir", required=True)
    parser.add_argument("--tile", type=int, default=512)
    parser.add_argument("--coarse-factor", type=int, default=4)
    args = parser.parse_args()

    with rasterio.open(args.dem) as dataset:
        heights = dataset.read(1).astype(np.float64)
        transform = dataset.transform
    size = heights.shape[0]
    if heights.shape[0] != heights.shape[1] or abs(transform.a + transform.e) > 1e-9:
        raise SystemExit("expected a square grid with square pixels")
    spacing = transform.a
    half = -transform.c
    if abs(transform.f - half) > 1e-6 or abs(size * spacing - 2 * half) > 1e-6:
        raise SystemExit("expected a grid centred on the pole")

    out = Path(args.out_dir)
    (out / "80").mkdir(parents=True, exist_ok=True)
    tiles = (size + args.tile - 1) // args.tile
    padded = np.pad(heights, ((0, tiles * args.tile - size), (0, tiles * args.tile - size)), mode="edge")
    units = to_units(padded)
    total = 0
    present = []
    for r in range(tiles):
        for c in range(tiles):
            if outside(r, c, args.tile, spacing, half):
                continue
            block = units[r * args.tile:(r + 1) * args.tile, c * args.tile:(c + 1) * args.tile]
            data = gzip.compress(delta(block).tobytes(), compresslevel=9, mtime=0)
            (out / "80" / f"r{r}c{c}.bin.gz").write_bytes(data)
            present.append([r, c])
            total += len(data)

    f = args.coarse_factor
    coarse_size = size // f
    coarse = heights[:coarse_size * f, :coarse_size * f].reshape(coarse_size, f, coarse_size, f).mean(axis=(1, 3))
    header = np.array([coarse_size], "<i4").tobytes() + np.array([coarse_size * spacing * f / 2, UNIT_M], "<f4").tobytes()
    coarse_data = gzip.compress(header + delta(to_units(coarse)).tobytes(), compresslevel=9, mtime=0)
    (out / "coarse.bin.gz").write_bytes(coarse_data)

    manifest = {
        "formatVersion": 1,
        "source": "LOLA LDEM_80S_80MPP_ADJ, south polar stereographic, 1737.4 km sphere",
        "sizePx": size,
        "spacingM": spacing,
        "halfExtentM": half,
        "tilePx": args.tile,
        "tiles": tiles,
        "unitM": UNIT_M,
        "coarseSpacingM": spacing * f,
        "encoding": "int16 second difference along rows and columns, wrapping, gzip",
        "present": present,
    }
    (out / "pole.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(f"{len(present)} of {tiles * tiles} tiles, {total / 1e6:.1f} MB; coarse {coarse_size} px at {spacing * f:.0f} m, {len(coarse_data) / 1e6:.1f} MB")


if __name__ == "__main__":
    main()

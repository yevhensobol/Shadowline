import argparse
import gzip
import json
from pathlib import Path

import numpy as np

SAMPLES = 256
BORDER = 1
LEVELS = 4
UNIT_M = 2.0


def delta(units):
    d = units.astype(np.int32)
    out = d.copy()
    out[:, 1:] -= d[:, :-1]
    out[1:, :] -= d[:-1, :]
    out[1:, 1:] += d[:-1, :-1]
    return out.astype("<i2")


def halve(grid):
    h, w = grid.shape
    out = np.empty((h // 2, w // 2), np.float32)
    step = 512
    for r in range(0, h // 2, step):
        block = grid[2 * r:2 * (r + step)].astype(np.float32)
        out[r:r + step] = block.reshape(-1, 2, w // 2, 2).mean(axis=(1, 3))
    return out


def level_shape(lines, samples, level):
    h, w = lines >> level, samples >> level
    return h, w, -(-h // SAMPLES), -(-w // SAMPLES)


def write_level(grid, level, out, first, last):
    h, w = grid.shape
    rows = -(-h // SAMPLES)
    cols = -(-w // SAMPLES)
    folder = out / str(level)
    folder.mkdir(parents=True, exist_ok=True)
    total = 0
    for r in range(first, min(last, rows)):
        ri = np.clip(np.arange(r * SAMPLES - BORDER, (r + 1) * SAMPLES + BORDER), 0, h - 1)
        band = grid[ri]
        for c in range(cols):
            ci = np.arange(c * SAMPLES - BORDER, (c + 1) * SAMPLES + BORDER) % w
            units = np.clip(np.round(band[:, ci] / UNIT_M), -32767, 32767)
            data = gzip.compress(delta(units).tobytes(), compresslevel=9, mtime=0)
            (folder / f"r{r}c{c}.bin.gz").write_bytes(data)
            total += len(data)
    return rows, cols, total


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--img", required=True)
    parser.add_argument("--out-dir", required=True)
    parser.add_argument("--lines", type=int, default=11520)
    parser.add_argument("--samples", type=int, default=23040)
    parser.add_argument("--scale", type=float, default=0.5)
    parser.add_argument("--level", type=int)
    parser.add_argument("--rows", default="0:9999")
    args = parser.parse_args()

    ppd = args.samples / 360.0
    out = Path(args.out_dir)
    if args.level is not None:
        raw = np.memmap(args.img, dtype="<i2", mode="r", shape=(args.lines, args.samples))
        first, last = (int(v) for v in args.rows.split(":"))
        if args.level == 0:
            lo = max(0, first * SAMPLES - BORDER)
            hi = min(args.lines, last * SAMPLES + BORDER)
            grid = np.zeros(raw.shape, np.float32)
            grid[lo:hi] = raw[lo:hi].astype(np.float32) * args.scale
            if lo > 0:
                grid[:lo] = grid[lo]
            if hi < args.lines:
                grid[hi:] = grid[hi - 1]
        else:
            grid = np.empty(raw.shape, np.float32)
            for r in range(0, args.lines, 1024):
                grid[r:r + 1024] = raw[r:r + 1024].astype(np.float32) * args.scale
            for _ in range(args.level):
                grid = halve(grid)
        rows, cols, total = write_level(grid, args.level, out, first, last)
        print(args.level, rows, cols, total, flush=True)
        return
    levels = []
    for level in range(LEVELS):
        h, w, rows, cols = level_shape(args.lines, args.samples, level)
        total = sum(f.stat().st_size for f in (out / str(level)).glob("*.bin.gz"))
        levels.append({"level": level, "tileDeg": SAMPLES / ppd * 2 ** level, "ppd": ppd / 2 ** level, "rows": rows, "cols": cols, "bytes": total})
    manifest = {
        "formatVersion": 1,
        "source": "LOLA LDEM_64 (GDR, 64 px/deg, 473.8 m/px), simple cylindrical, heights above 1737.4 km",
        "samples": SAMPLES,
        "border": BORDER,
        "unitM": UNIT_M,
        "origin": "lat 90 N, lon 0 E, rows southward, columns eastward, pixel-registered",
        "encoding": "int16 second difference along rows and columns, gzip",
        "levels": levels,
    }
    (out / "relief.json").write_text(json.dumps(manifest, indent=2) + "\n")


if __name__ == "__main__":
    main()

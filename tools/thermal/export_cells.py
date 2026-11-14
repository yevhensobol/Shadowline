import argparse
import math
import struct
from pathlib import Path

import numpy as np

MOON_RADIUS_M = 1737400.0


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--hz", required=True, help="hz.npz cache from tools/light/build_pole_light.py")
    parser.add_argument("--out", required=True)
    parser.add_argument("--count", type=int, default=4000)
    parser.add_argument("--half", type=float, default=304000.0)
    parser.add_argument("--seed", type=int, default=7)
    parser.add_argument("--observer", type=float, default=2.0)
    args = parser.parse_args()
    cache = np.load(args.hz)
    hz = cache["hz"]
    base = cache["base"]
    azimuths, n, _ = hz.shape
    spacing = 2 * args.half / n
    rng = np.random.default_rng(args.seed)
    rows = rng.integers(0, n, size=args.count * 3)
    cols = rng.integers(0, n, size=args.count * 3)
    x = -args.half + (cols + 0.5) * spacing
    y = args.half - (rows + 0.5) * spacing
    rho = np.hypot(x, y)
    lat = -90 + np.degrees(2 * np.arctan(rho / (2 * MOON_RADIUS_M)))
    keep = lat <= -80.0
    rows, cols, x, y, lat = rows[keep][: args.count], cols[keep][: args.count], x[keep][: args.count], y[keep][: args.count], lat[keep][: args.count]
    lon = np.degrees(np.arctan2(x, y))
    az = np.arange(720) * 0.5
    with open(args.out, "wb") as handle:
        handle.write(struct.pack("<i", len(rows)))
        for r, c, la, lo in zip(rows, cols, lat, lon):
            phi = np.mod(90.0 - lo - az, 360.0) * azimuths / 360.0
            k0 = np.floor(phi).astype(int) % azimuths
            w = phi - np.floor(phi)
            h = hz[k0, r, c].astype(np.float64) * (1 - w) + hz[(k0 + 1) % azimuths, r, c].astype(np.float64) * w
            handle.write(struct.pack("<fff", la, lo, base[r, c] - args.observer))
            handle.write(h.astype("<f4").tobytes())
    np.savez(Path(args.out).with_suffix(".idx.npz"), rows=rows, cols=cols)
    print(len(rows), "cells")


if __name__ == "__main__":
    main()

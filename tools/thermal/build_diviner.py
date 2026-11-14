import argparse
import math
import struct
from pathlib import Path

import numpy as np

MOON_RADIUS_M = 1737400.0


def read_prp(path):
    raw = np.fromfile(path, dtype=np.uint8)
    width = 210
    rows = raw.size // width
    table = raw[: rows * width].reshape(rows, width)[1:]

    def column(start, length):
        text = table[:, start:start + length].copy().view(f"S{length}").ravel()
        return text.astype(np.float64)

    return {
        "lon": column(126, 15),
        "lat": column(143, 15),
        "avg": column(177, 8),
        "max": column(187, 8),
        "ice": column(197, 11),
    }


def grid(values, lat, lon, n, half):
    rho = 2 * MOON_RADIUS_M * np.tan(np.radians(90 + lat) / 2)
    x = rho * np.sin(np.radians(lon))
    y = rho * np.cos(np.radians(lon))
    spacing = 2 * half / n
    col = np.floor((x + half) / spacing).astype(np.int64)
    row = np.floor((half - y) / spacing).astype(np.int64)
    inside = (col >= 0) & (col < n) & (row >= 0) & (row < n)
    index = row[inside] * n + col[inside]
    out = {}
    count = np.bincount(index, minlength=n * n)
    for name, v in values.items():
        v = v[inside]
        if name == "ice":
            v = np.where(v == -1, 0.0, v)
            valid = v >= 0
            best = np.full(n * n, np.inf)
            np.minimum.at(best, index[valid], v[valid])
            out[name] = best.reshape(n, n)
        else:
            out[name] = (np.bincount(index, weights=v, minlength=n * n) / np.maximum(count, 1)).reshape(n, n)
    out["count"] = count.reshape(n, n)
    return out


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--prp", required=True, help="DLRE_PRP_SOUTH.TAB")
    parser.add_argument("--data-dir", required=True)
    parser.add_argument("--cache", required=True, help="npz with the full-precision 1 km grid")
    parser.add_argument("--n", type=int, default=608)
    parser.add_argument("--half", type=float, default=304000.0)
    args = parser.parse_args()
    prp = read_prp(args.prp)
    print(len(prp["lat"]), "triangles", prp["lat"].min(), prp["lat"].max())
    g = grid({k: prp[k] for k in ("avg", "max", "ice")}, prp["lat"], prp["lon"], args.n, args.half)
    np.savez(args.cache, **g)
    empty = g["count"] == 0
    avg = np.where(empty, 0, np.clip(np.round(g["avg"] / 2), 1, 254)).astype(np.uint8)
    mx = np.where(empty, 0, np.clip(np.round(g["max"] / 2), 1, 254)).astype(np.uint8)
    ice = np.where(np.isfinite(g["ice"]) & ~empty, np.clip(np.round(g["ice"] * 50), 0, 254), 255).astype(np.uint8)
    out = Path(args.data_dir) / "pole_diviner.bin"
    with open(out, "wb") as handle:
        handle.write(struct.pack("<iff", args.n, args.half, 2.0))
        handle.write(avg.tobytes())
        handle.write(mx.tobytes())
        handle.write(ice.tobytes())
    print("cells with data", int((~empty).sum()), "ice-stable cells", int(np.isfinite(g["ice"]).sum()))


if __name__ == "__main__":
    main()

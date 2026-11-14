import argparse
import math

import numpy as np


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dem", required=True, help="LDEM_80S_80MPP_ADJ.tiff")
    parser.add_argument("--index", required=True, help="cells.idx.npz from export_cells.py")
    parser.add_argument("--out", required=True)
    parser.add_argument("--n", type=int, default=608)
    parser.add_argument("--half", type=float, default=304000.0)
    parser.add_argument("--baseline", type=float, default=480.0)
    args = parser.parse_args()
    import rasterio

    with rasterio.open(args.dem) as ds:
        dem = ds.read(1).astype(np.float32)
        pixel = ds.transform.a
        half_dem = -ds.transform.c
    if np.nanmax(np.abs(dem)) < 50:
        dem *= 1000
    idx = np.load(args.index)
    rows, cols = idx["rows"], idx["cols"]
    spacing = 2 * args.half / args.n
    x = -args.half + (cols + 0.5) * spacing
    y = args.half - (rows + 0.5) * spacing
    step = max(1, int(round(args.baseline / pixel / 2)))
    r = ((half_dem - y) / pixel - 0.5).round().astype(int)
    c = ((x + half_dem) / pixel - 0.5).round().astype(int)
    r = np.clip(r, step, dem.shape[0] - step - 1)
    c = np.clip(c, step, dem.shape[1] - step - 1)
    gx = (dem[r, c + step] - dem[r, c - step]) / (2 * step * pixel)
    gy = (dem[r - step, c] - dem[r + step, c]) / (2 * step * pixel)
    slope = np.degrees(np.arctan(np.hypot(gx, gy)))
    lon = np.arctan2(x, y)
    north = np.stack([np.sin(lon), np.cos(lon)], axis=-1)
    east = np.stack([np.cos(lon), -np.sin(lon)], axis=-1)
    down = np.stack([-gx, -gy], axis=-1)
    aspect = np.degrees(np.arctan2((down * east).sum(-1), (down * north).sum(-1))) % 360
    np.savez(args.out, slope=slope.astype(np.float32), aspect=aspect.astype(np.float32))
    print("slope mean", float(slope.mean()), "max", float(slope.max()))


if __name__ == "__main__":
    main()

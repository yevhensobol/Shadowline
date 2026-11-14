import argparse
import json
import math
from pathlib import Path

import numpy as np
import rasterio
from pyproj import CRS, Transformer
from rasterio.windows import Window

import terrain


def bilinear(dataset_array, rows, cols):
    height, width = dataset_array.shape
    r0 = np.floor(rows).astype(int)
    c0 = np.floor(cols).astype(int)
    r0 = np.clip(r0, 0, height - 2)
    c0 = np.clip(c0, 0, width - 2)
    fr = np.clip(rows - r0, 0.0, 1.0)
    fc = np.clip(cols - c0, 0.0, 1.0)
    top = dataset_array[r0, c0] * (1 - fc) + dataset_array[r0, c0 + 1] * fc
    bottom = dataset_array[r0 + 1, c0] * (1 - fc) + dataset_array[r0 + 1, c0 + 1] * fc
    return top * (1 - fr) + bottom * fr


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dem", required=True)
    parser.add_argument("--catalog", required=True)
    parser.add_argument("--out-dir", required=True)
    parser.add_argument("--sites")
    parser.add_argument("--size", type=int, default=401)
    parser.add_argument("--spacing-m", type=float, default=100.0)
    args = parser.parse_args()

    catalog = json.loads(Path(args.catalog).read_text(encoding="utf-8"))
    wanted = set(args.sites.split(",")) if args.sites else None
    out_dir = Path(args.out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    half_extent_m = (args.size - 1) / 2.0 * args.spacing_m * math.sqrt(2.0)

    with rasterio.open(args.dem) as dataset:
        pixel_m = abs(dataset.transform.a)
        inverse = ~dataset.transform
        projected = CRS.from_wkt(dataset.crs.to_wkt())
        transformer = Transformer.from_crs(projected.geodetic_crs, projected, always_xy=True)
        margin_px = int(half_extent_m / pixel_m) + 4

        for site in catalog["sites"]:
            if wanted and site["id"] not in wanted:
                continue
            lat = site["latitudeDeg"]
            lon = (site["longitudeEastDeg"] + 180.0) % 360.0 - 180.0
            x, y = transformer.transform(lon, lat)
            col, row = inverse * (x, y)
            if not (margin_px <= row < dataset.height - margin_px and margin_px <= col < dataset.width - margin_px):
                print(f"skip {site['id']}: the DEM does not cover the terrain patch")
                continue

            row0 = int(row) - margin_px
            col0 = int(col) - margin_px
            size = 2 * margin_px + 2
            data = dataset.read(1, window=Window(col0, row0, size, size), masked=True).astype(np.float64)
            data = np.ma.filled(data, np.nan)

            def sample(lat_array, lon_array):
                gx, gy = transformer.transform(lon_array, lat_array)
                gc, gr = inverse * (gx, gy)
                return bilinear(data, gr - row0 - 0.5, gc - col0 - 0.5)

            grid = terrain.build_grid(lat, lon, sample, args.size, args.spacing_m)
            (out_dir / f"{site['id']}.bin").write_bytes(terrain.encode(grid, args.spacing_m))
            print(f"{site['id']}: min {np.nanmin(grid):.0f} m, max {np.nanmax(grid):.0f} m")


if __name__ == "__main__":
    main()

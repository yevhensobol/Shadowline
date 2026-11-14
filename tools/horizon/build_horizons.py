import argparse
import json
import math
import sys
from pathlib import Path

import numpy as np
import rasterio
from pyproj import CRS, Transformer
from rasterio.windows import Window

from horizon import MOON_RADIUS_M, _bilinear, horizon_profile, write_profile

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "terrain"))
from build_global import equirect_sampler, raw_grid  # noqa: E402


def wrap_longitude(lon_east_deg):
    wrapped = (lon_east_deg + 180.0) % 360.0 - 180.0
    return wrapped


def site_pixel(transformer, inverse_affine, lat, lon):
    x, y = transformer.transform(wrap_longitude(lon), lat)
    col, row = inverse_affine * (x, y)
    return x, y, row - 0.5, col - 0.5


def grid_north_deg(transformer, inverse_affine, lat, lon):
    _, _, row0, col0 = site_pixel(transformer, inverse_affine, lat, lon)
    _, _, row1, col1 = site_pixel(transformer, inverse_affine, min(lat + 0.001, 89.999), lon)
    return math.degrees(math.atan2(col1 - col0, -(row1 - row0))) % 360.0


def read_window(dataset, row, col, radius_px):
    row0 = max(0, int(math.floor(row)) - radius_px)
    col0 = max(0, int(math.floor(col)) - radius_px)
    row1 = min(dataset.height, int(math.floor(row)) + radius_px + 2)
    col1 = min(dataset.width, int(math.floor(col)) + radius_px + 2)
    window = Window(col0, row0, col1 - col0, row1 - row0)
    data = dataset.read(1, window=window, masked=True).astype(np.float64)
    return np.ma.filled(data, np.nan), row0, col0


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dem", required=True)
    parser.add_argument("--catalog", required=True)
    parser.add_argument("--out-dir", required=True)
    parser.add_argument("--sites")
    parser.add_argument("--max-range-km", type=float, default=100.0)
    parser.add_argument("--observer-height-m", type=float, default=2.0)
    parser.add_argument("--far-ldem")
    parser.add_argument("--far-range-km", type=float, default=300.0)
    parser.add_argument("--far-spacing-m", type=float, default=1000.0)
    args = parser.parse_args()

    far_sample = None
    if args.far_ldem:
        with rasterio.open(args.far_ldem) as far:
            far_sample = equirect_sampler(far.read(1))

    catalog = json.loads(Path(args.catalog).read_text(encoding="utf-8"))
    wanted = set(args.sites.split(",")) if args.sites else None
    out_dir = Path(args.out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)

    with rasterio.open(args.dem) as dataset:
        pixel_m = abs(dataset.transform.a)
        inverse = ~dataset.transform
        projected = CRS.from_wkt(dataset.crs.to_wkt())
        transformer = Transformer.from_crs(projected.geodetic_crs, projected, always_xy=True)
        radius_px = int(args.max_range_km * 1000.0 / pixel_m)

        for site in catalog["sites"]:
            if wanted and site["id"] not in wanted:
                continue
            lat = site["latitudeDeg"]
            lon = site["longitudeEastDeg"]
            _, _, row, col = site_pixel(transformer, inverse, lat, lon)
            if not (0 <= row < dataset.height - 1 and 0 <= col < dataset.width - 1):
                print(f"skip {site['id']}: outside the DEM")
                continue

            clipped = not (radius_px <= row < dataset.height - radius_px and radius_px <= col < dataset.width - radius_px)
            if clipped and far_sample is None:
                print(f"skip {site['id']}: the DEM does not cover the full {args.max_range_km:g} km range")
                continue

            window, row0, col0 = read_window(dataset, row, col, radius_px)
            profile = horizon_profile(
                window,
                pixel_m,
                row - row0,
                col - col0,
                grid_north_deg=grid_north_deg(transformer, inverse, lat, lon),
                observer_height_m=args.observer_height_m,
                moon_radius_m=MOON_RADIUS_M,
                max_range_m=args.max_range_km * 1000.0,
            )
            note = ""
            if far_sample is not None:
                n = int(2 * args.far_range_km * 1000.0 / args.far_spacing_m) + 1
                heights = raw_grid(lat, lon, far_sample, n, args.far_spacing_m)
                centre = (n - 1) / 2.0
                near_base = float(_bilinear(window, np.array([row - row0]), np.array([col - col0]))[0])
                far_profile = horizon_profile(
                    heights,
                    args.far_spacing_m,
                    centre,
                    centre,
                    observer_height_m=near_base + args.observer_height_m - float(heights[int(centre), int(centre)]),
                    max_range_m=args.far_range_km * 1000.0,
                    min_range_m=0.0 if clipped else args.max_range_km * 1000.0,
                )
                raised = int((far_profile > profile).sum())
                profile = np.maximum(profile, far_profile).astype(np.float32)
                note = f", far field raises {raised} of {profile.size} azimuths" + (", near field clipped at the DEM edge" if clipped else "")
            write_profile(out_dir / f"{site['id']}.bin", profile)
            print(f"{site['id']}: max {profile.max():.2f} deg, min {profile.min():.2f} deg{note}")


if __name__ == "__main__":
    main()

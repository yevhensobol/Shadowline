import argparse
import json
import sys
from pathlib import Path

import numpy as np
import rasterio

import terrain

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "horizon"))
from horizon import horizon_profile, write_profile  # noqa: E402


def equirect_sampler(km):
    height, width = km.shape
    meters = km.astype(np.float64) * 1000.0

    def sample(lat, lon):
        cols = np.mod((np.asarray(lon) + 180.0) / 360.0 * width - 0.5, width)
        rows = np.clip((90.0 - np.asarray(lat)) / 180.0 * height - 0.5, 0, height - 1.000001)
        c0 = np.floor(cols).astype(int)
        r0 = np.floor(rows).astype(int)
        c1 = (c0 + 1) % width
        r1 = np.minimum(r0 + 1, height - 1)
        fc = cols - c0
        fr = rows - r0
        top = meters[r0, c0] * (1 - fc) + meters[r0, c1] * fc
        bottom = meters[r1, c0] * (1 - fc) + meters[r1, c1] * fc
        return top * (1 - fr) + bottom * fr

    return sample


def raw_grid(lat, lon, sample, n, spacing_m):
    half = (n - 1) / 2.0
    cols, rows = np.meshgrid(np.arange(n), np.arange(n))
    east = (cols - half) * spacing_m
    north = (half - rows) * spacing_m
    distance = np.hypot(east, north)
    bearing = np.arctan2(east, north)
    glat, glon = terrain.destination(lat, lon, bearing, distance)
    glat = np.where(distance == 0, lat, glat)
    glon = np.where(distance == 0, lon, glon)
    return sample(glat, glon)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--ldem", required=True)
    parser.add_argument("--catalog", required=True)
    parser.add_argument("--data-dir", required=True)
    parser.add_argument("--tile-size", type=int, default=301)
    parser.add_argument("--tile-spacing-m", type=float, default=1000.0)
    parser.add_argument("--horizon-range-m", type=float, default=300000.0)
    parser.add_argument("--observer-m", type=float, default=2.0)
    parser.add_argument("--force", action="store_true")
    args = parser.parse_args()

    catalog_path = Path(args.catalog)
    catalog = json.loads(catalog_path.read_text(encoding="utf-8"))
    data_dir = Path(args.data_dir)
    (data_dir / "terrain").mkdir(parents=True, exist_ok=True)
    (data_dir / "horizon").mkdir(parents=True, exist_ok=True)
    with rasterio.open(args.ldem) as dataset:
        sample = equirect_sampler(dataset.read(1))

    horizon_n = int(2 * args.horizon_range_m / args.tile_spacing_m) + 1
    for site in catalog["sites"]:
        if site["id"].startswith("syn-"):
            continue
        lat = site["latitudeDeg"]
        lon = site["longitudeEastDeg"]
        site["elevationM"] = round(float(sample(np.array([lat]), np.array([lon]))[0]), 1)
        terrain_path = data_dir / "terrain" / f"{site['id']}.bin"
        horizon_path = data_dir / "horizon" / f"{site['id']}.bin"
        if args.force or not terrain_path.exists():
            grid = terrain.build_grid(lat, lon, sample, args.tile_size, args.tile_spacing_m)
            terrain_path.write_bytes(terrain.encode(grid, args.tile_spacing_m))
        if args.force or not horizon_path.exists():
            heights = raw_grid(lat, lon, sample, horizon_n, args.tile_spacing_m)
            centre = (horizon_n - 1) / 2.0
            profile = horizon_profile(heights, args.tile_spacing_m, centre, centre, 0.0, args.observer_m, max_range_m=args.horizon_range_m)
            write_profile(horizon_path, profile)
            print(f"{site['id']}: elevation {site['elevationM']:.0f} m, horizon max {profile.max():.2f} deg, mean {profile.mean():.2f} deg")

    catalog_path.write_text(json.dumps(catalog, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()

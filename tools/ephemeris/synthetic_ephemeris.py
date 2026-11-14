import argparse
import json
import math
from pathlib import Path

import numpy as np

SUN_DISTANCE_KM = 149_597_870.7
EARTH_MEAN_DISTANCE_KM = 384_400.0
SYNODIC_DAYS = 29.530589
DRACONIC_YEAR_DAYS = 346.62
ANOMALISTIC_DAYS = 27.554550
LIBRATION_LON_DEG = 6.3
LIBRATION_LAT_DEG = 6.7
SUN_DECLINATION_DEG = 1.54


def direction(lon_deg, lat_deg):
    lon = np.radians(lon_deg)
    lat = np.radians(lat_deg)
    return np.stack([np.cos(lat) * np.cos(lon), np.cos(lat) * np.sin(lon), np.sin(lat)], axis=-1)


def build(count, step_minutes):
    days = np.arange(count) * step_minutes / 1440.0
    sun_lon = 180.0 - 360.0 * days / SYNODIC_DAYS
    sun_lat = SUN_DECLINATION_DEG * np.sin(2 * math.pi * days / DRACONIC_YEAR_DAYS)
    earth_lon = LIBRATION_LON_DEG * np.sin(2 * math.pi * days / ANOMALISTIC_DAYS)
    earth_lat = LIBRATION_LAT_DEG * np.sin(2 * math.pi * days / 27.212221)
    earth_range = EARTH_MEAN_DISTANCE_KM * (1 + 0.055 * np.cos(2 * math.pi * days / ANOMALISTIC_DAYS))
    sun = direction(sun_lon, sun_lat) * SUN_DISTANCE_KM
    earth = direction(earth_lon, earth_lat) * earth_range[:, None]
    return np.concatenate([sun, earth], axis=1).astype("<f4")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--manifest", required=True)
    parser.add_argument("--out", required=True)
    args = parser.parse_args()

    manifest = json.loads(Path(args.manifest).read_text(encoding="utf-8"))
    rows = build(manifest["gridCount"], manifest["gridStepMinutes"])
    Path(args.out).write_bytes(rows.tobytes())
    print(f"wrote {rows.shape[0]} rows, {rows.nbytes} bytes")


if __name__ == "__main__":
    main()

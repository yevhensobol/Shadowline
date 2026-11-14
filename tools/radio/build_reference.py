import argparse
import csv
import json

import numpy as np
import rasterio

from viewshed import Dem, fractions, los_area_km2, viewshed


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dem", required=True)
    parser.add_argument("--table", required=True)
    parser.add_argument("--ids", required=True)
    parser.add_argument("--out", required=True)
    args = parser.parse_args()

    with rasterio.open(args.dem) as dataset:
        heights = dataset.read(1).astype(np.float64)
        transform = dataset.transform
    dem = Dem(heights, transform.a, -transform.c)
    wanted = {int(v) for v in args.ids.split(",")}
    out = []
    for row in csv.DictReader(open(args.table, newline="")):
        if int(row["cand_id"]) not in wanted:
            continue
        x, y = float(row["x_m"]), float(row["y_m"])
        d, visible = viewshed(dem, x, y)
        f2, f10, _ = fractions(d, visible)
        out.append({
            "id": int(row["cand_id"]),
            "x": x,
            "y": y,
            "los2": round(f2, 4),
            "los10": round(f10, 4),
            "areaKm2": round(los_area_km2(d, visible), 2),
            "lunarrfLos2": float(row["los_2km"]),
            "lunarrfLos10": float(row["los_10km"]),
            "lunarrfAreaKm2": float(row["los_area_km2"]),
        })
    json.dump(out, open(args.out, "w"), indent=1)


if __name__ == "__main__":
    main()

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
    parser.add_argument("--out", required=True)
    args = parser.parse_args()

    with rasterio.open(args.dem) as dataset:
        heights = dataset.read(1).astype(np.float64)
        transform = dataset.transform
    dem = Dem(heights, transform.a, -transform.c)

    rows = list(csv.DictReader(open(args.table, newline="")))
    out = []
    for row in rows:
        x, y = float(row["x_m"]), float(row["y_m"])
        d, visible = viewshed(dem, x, y)
        f2, f10, f22 = fractions(d, visible)
        out.append({
            "id": int(row["cand_id"]),
            "cls": row["cls"],
            "lat": float(row["lat"]),
            "z_lunarrf": float(row["z_m"]),
            "z_ours": float(dem.sample(np.array([x]), np.array([y]))[0]),
            "los2_lunarrf": float(row["los_2km"]),
            "los10_lunarrf": float(row["los_10km"]),
            "area_lunarrf": float(row["los_area_km2"]),
            "cov2_lunarrf": float(row["cov_of_all_2km_1.800GHz"]),
            "los2": f2,
            "los10": f10,
            "area": los_area_km2(d, visible),
        })

    json.dump(out, open(args.out, "w"), indent=0)
    for cls in ("landing_site", "ridge"):
        sub = [o for o in out if o["cls"] == cls]
        for key, ref in (("los2", "los2_lunarrf"), ("los10", "los10_lunarrf"), ("area", "area_lunarrf")):
            a = np.array([o[key] for o in sub])
            b = np.array([o[ref] for o in sub])
            r = np.corrcoef(a, b)[0, 1]
            print(f"{cls:13s} {key:6s} n={len(sub)} ours median {np.median(a):.3f} lunarrf {np.median(b):.3f} "
                  f"r {r:.3f} mae {np.mean(np.abs(a - b)):.3f} bias {np.mean(a - b):+.3f}")


if __name__ == "__main__":
    main()

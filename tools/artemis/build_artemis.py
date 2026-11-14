import argparse
import json
import math
from collections import defaultdict
from pathlib import Path

import shapefile

REGIONS = {
    "PCB": ("Peak near Cabeus B", True),
    "HW": ("Haworth", True),
    "MMA": ("Malapert Massif", True),
    "MMP": ("Mons Mouton Plateau", True),
    "MMO": ("Mons Mouton", True),
    "NR1": ("Nobile Rim 1", True),
    "NR2": ("Nobile Rim 2", True),
    "dGR2": ("de Gerlache Rim 2", True),
    "SP": ("Slater Plain", True),
    "dGR1": ("de Gerlache Rim 1", False),
    "dGKM": ("de Gerlache-Kocher Massif", False),
    "CR": ("Connecting Ridge", False),
    "CRE": ("Connecting Ridge Extension", False),
    "PNS": ("Peak near Shackleton", False),
    "FRA": ("Faustini Rim A", False),
    "AR": ("Amundsen Rim", False),
}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("shapefile")
    parser.add_argument("out")
    args = parser.parse_args()

    reader = shapefile.Reader(args.shapefile)
    grouped = defaultdict(list)
    for record in reader.records():
        code = record["Landing_Re"]
        if code not in REGIONS:
            raise SystemExit(f"unknown region code {code}")
        grouped[code].append({
            "name": str(record["Name"]),
            "lat": round(float(record["Latitude"]), 5),
            "lon": round(float(record["Longitude"]), 5),
        })

    regions = []
    for code, (name, current) in REGIONS.items():
        sites = grouped.get(code, [])
        if not sites:
            continue
        x = sum(math.cos(math.radians(s["lat"])) * math.cos(math.radians(s["lon"])) for s in sites)
        y = sum(math.cos(math.radians(s["lat"])) * math.sin(math.radians(s["lon"])) for s in sites)
        z = sum(math.sin(math.radians(s["lat"])) for s in sites)
        lat = math.degrees(math.atan2(z, math.hypot(x, y)))
        lon = math.degrees(math.atan2(y, x))
        regions.append({"code": code, "name": name, "artemis3": current, "lat": round(lat, 4), "lon": round(lon, 4), "sites": sites})

    document = {
        "formatVersion": 1,
        "source": "https://doi.org/10.5281/zenodo.17084058",
        "announcement": "https://www.nasa.gov/news-release/nasa-provides-update-on-artemis-iii-moon-landing-regions/",
        "regions": regions,
    }
    Path(args.out).write_text(json.dumps(document, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(len(regions), "regions,", sum(len(r["sites"]) for r in regions), "sites")


if __name__ == "__main__":
    main()

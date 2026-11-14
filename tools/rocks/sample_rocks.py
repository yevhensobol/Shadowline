import argparse
import json
import math
import struct
import urllib.request

URL = "https://pds-geosciences.wustl.edu/lro/urn-nasa-pds-lro_diviner_derived1/data_derived_ghrm/img/dghrm_ra_sam_70s70n_img.img"
SAMPLES = 46080
PPD = 128
NORTH = 70.0


def cell(lat, lon):
    return math.floor((NORTH - lat) * PPD), math.floor((lon % 360.0) * PPD)


def read(row, col, count):
    start = (row * SAMPLES + col) * 4
    request = urllib.request.Request(URL, headers={"Range": f"bytes={start}-{start + 4 * count - 1}"})
    with urllib.request.urlopen(request, timeout=60) as response:
        data = response.read()
    return struct.unpack(f"<{count}f", data)


def sample(lat, lon):
    row, col = cell(lat, lon)
    values = [v for k in (-1, 0, 1) for v in read(row + k, col - 1, 3) if math.isfinite(v)]
    return round(sum(values) / len(values), 4) if values else None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--catalog", required=True)
    parser.add_argument("--write", action="store_true")
    args = parser.parse_args()
    catalog = json.load(open(args.catalog, encoding="utf-8"))
    for site in catalog["sites"]:
        if site["id"].startswith("syn-") or abs(site["latitudeDeg"]) >= NORTH:
            site.pop("rockAbundance", None)
            continue
        site["rockAbundance"] = sample(site["latitudeDeg"], site["longitudeEastDeg"])
        print(site["id"], site["rockAbundance"])
    if args.write:
        open(args.catalog, "w", encoding="utf-8").write(json.dumps(catalog, ensure_ascii=False, indent=2) + "\n")


if __name__ == "__main__":
    main()

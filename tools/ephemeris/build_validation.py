import argparse
import json
from datetime import datetime, timezone
from pathlib import Path

from fetch_horizons import BODIES, parse, query


def to_iso(horizons_time):
    parsed = datetime.strptime(horizons_time.strip(), "%Y-%b-%d %H:%M")
    return parsed.replace(tzinfo=timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--catalog", required=True)
    parser.add_argument("--sites", required=True)
    parser.add_argument("--start", required=True)
    parser.add_argument("--stop", required=True)
    parser.add_argument("--step", default="6 h")
    parser.add_argument("--out", required=True)
    args = parser.parse_args()

    catalog = json.loads(Path(args.catalog).read_text(encoding="utf-8"))
    by_id = {site["id"]: site for site in catalog["sites"]}
    site_ids = [site_id for site_id in by_id if not site_id.startswith("syn-")] if args.sites == "all" else args.sites.split(",")
    fixtures = []
    for number, site_id in enumerate(site_ids, 1):
        site = by_id[site_id]
        height_km = site["elevationM"] / 1000.0
        for body in BODIES:
            result = query(body, site["latitudeDeg"], site["longitudeEastDeg"], height_km, args.start, args.stop, args.step)
            for time, azimuth, elevation in parse(result):
                fixtures.append(
                    {
                        "siteId": site_id,
                        "body": body,
                        "timeUtc": to_iso(time),
                        "azimuthDeg": azimuth,
                        "elevationDeg": elevation,
                    }
                )
        print(f"{number}/{len(site_ids)} {site_id}")

    document = {"formatVersion": 1, "source": "JPL Horizons API", "fixtures": fixtures}
    Path(args.out).write_text(json.dumps(document, separators=(",", ":")) + "\n", encoding="utf-8")
    print(f"wrote {len(fixtures)} fixtures to {args.out}")


if __name__ == "__main__":
    main()

import argparse
import csv
import json
import urllib.parse
import urllib.request
from pathlib import Path

API = "https://ssd.jpl.nasa.gov/api/horizons.api"
BODIES = {"sun": "10", "earth": "399"}


def query(body, lat, lon_east, height_km, start, stop, step):
    params = {
        "format": "json",
        "COMMAND": f"'{BODIES[body]}'",
        "OBJ_DATA": "'NO'",
        "MAKE_EPHEM": "'YES'",
        "EPHEM_TYPE": "'OBSERVER'",
        "CENTER": "'coord@301'",
        "COORD_TYPE": "'GEODETIC'",
        "SITE_COORD": f"'{lon_east},{lat},{height_km}'",
        "START_TIME": f"'{start}'",
        "STOP_TIME": f"'{stop}'",
        "STEP_SIZE": f"'{step}'",
        "QUANTITIES": "'4'",
        "ANG_FORMAT": "'DEG'",
        "CSV_FORMAT": "'YES'",
        "TIME_DIGITS": "'MINUTES'",
    }
    url = API + "?" + urllib.parse.urlencode(params)
    with urllib.request.urlopen(url, timeout=60) as response:
        payload = json.loads(response.read().decode("utf-8"))
    if "result" not in payload:
        raise SystemExit(f"Horizons error: {payload}")
    return payload["result"]


def parse(result):
    start = result.index("$$SOE")
    end = result.index("$$EOE")
    rows = []
    for line in result[start + len("$$SOE"):end].strip().splitlines():
        fields = [field.strip() for field in line.split(",")]
        while fields and fields[-1] == "":
            fields.pop()
        rows.append((fields[0], float(fields[-2]), float(fields[-1])))
    return rows


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--lat", type=float, required=True)
    parser.add_argument("--lon-east", type=float, required=True)
    parser.add_argument("--height-km", type=float, default=0.0)
    parser.add_argument("--start", required=True)
    parser.add_argument("--stop", required=True)
    parser.add_argument("--step", default="30 min")
    parser.add_argument("--out", required=True)
    args = parser.parse_args()

    with Path(args.out).open("w", newline="", encoding="utf-8") as handle:
        writer = csv.writer(handle)
        writer.writerow(["body", "time", "azimuthDeg", "elevationDeg"])
        for body in BODIES:
            result = query(body, args.lat, args.lon_east, args.height_km, args.start, args.stop, args.step)
            for time, azimuth, elevation in parse(result):
                writer.writerow([body, time, azimuth, elevation])
    print(f"wrote {args.out}")


if __name__ == "__main__":
    main()

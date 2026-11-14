import argparse
import json
import math
import struct
import urllib.request

MOON_RADIUS_M = 1737400.0
NULL_LIMIT = -1e30

PRODUCTS = {
    "MALAPERTA01": {
        "url": "https://pds.mcp.nasa.gov/data/store/img/lunar_reconnaissance_orbiter/pds4/lroc/lro-l-lroc-5-rdr/LROLRC_2001/DATA/SDP/NAC_DTM/MALAPERTA01/NAC_DTM_MALAPERTA01_P803S0015.IMG",
        "header": 28400, "samples": 7100, "scale": 3.0, "line_offset": 104103.0, "sample_offset": 1174.0,
    },
    "NOBILE03": {
        "url": "https://pds.mcp.nasa.gov/data/store/img/lunar_reconnaissance_orbiter/pds4/lroc/lro-l-lroc-5-rdr/LROLRC_2001/DATA/SDP/NAC_DTM/NOBILE03/NAC_DTM_NOBILE03_P847S0291.IMG",
        "header": 4688, "samples": 1172, "scale": 4.0, "line_offset": 35678.0, "sample_offset": -18891.0,
    },
}

CASES = [
    {"id": "im1", "product": "MALAPERTA01", "lat": -80.13, "lon": 1.44, "box": (50, 9), "baselines": [1, 2, 4, 7, 14, 27]},
    {"id": "im2", "product": "NOBILE03", "lat": -84.7906, "lon": 29.1957, "radius": 6, "baselines": [1, 10]},
]


def pixel(product, lat, lon):
    r = 2 * MOON_RADIUS_M * math.tan(math.pi / 4 + math.radians(lat) / 2)
    line = product["line_offset"] - r * math.cos(math.radians(lon)) / product["scale"]
    sample = product["sample_offset"] + r * math.sin(math.radians(lon)) / product["scale"]
    return line, sample


def read_rows(product, first, last):
    width = product["samples"] * 4
    start = product["header"] + (first - 1) * width
    stop = product["header"] + last * width - 1
    request = urllib.request.Request(product["url"], headers={"Range": f"bytes={start}-{stop}"})
    with urllib.request.urlopen(request, timeout=120) as response:
        data = response.read()
    values = struct.unpack(f"<{len(data) // 4}f", data)
    return [values[i * product["samples"]:(i + 1) * product["samples"]] for i in range(last - first + 1)]


def quantiles(values):
    values = sorted(values)
    pick = lambda f: values[int(f * (len(values) - 1))]
    return {"n": len(values), "p10": round(pick(0.1), 2), "median": round(pick(0.5), 2), "p90": round(pick(0.9), 2)}


def analyse(case):
    product = PRODUCTS[case["product"]]
    line, sample = pixel(product, case["lat"], case["lon"])
    centre_line, centre_sample = round(line), round(sample)
    reach = max(case.get("box", (case.get("radius", 0), 0))[0], case.get("radius", 0)) + max(case["baselines"]) + 1
    rows = read_rows(product, centre_line - reach, centre_line + reach)
    at = lambda l, s: rows[l - (centre_line - reach)][s - 1]
    result = {"line": line, "sample": sample, "elevationM": round(at(centre_line, centre_sample), 1), "baselines": {}}
    for k in case["baselines"]:
        slopes = []
        if "box" in case:
            points = [(l, s) for l in range(centre_line - case["box"][0], centre_line + case["box"][0] + 1) for s in range(centre_sample - case["box"][1], centre_sample + case["box"][1] + 1)]
        else:
            r = case["radius"]
            points = [(l, s) for l in range(centre_line - r, centre_line + r + 1) for s in range(centre_sample - r, centre_sample + r + 1) if (l - centre_line) ** 2 + (s - centre_sample) ** 2 <= r * r]
        for l, s in points:
            z = (at(l, s + k), at(l, s - k), at(l + k, s), at(l - k, s))
            if any(v < NULL_LIMIT for v in z):
                continue
            gx = (z[0] - z[1]) / (2 * k * product["scale"])
            gy = (z[2] - z[3]) / (2 * k * product["scale"])
            slopes.append(math.degrees(math.atan(math.hypot(gx, gy))))
        result["baselines"][f"{2 * k * product['scale']:.0f}m"] = quantiles(slopes)
    return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--out")
    args = parser.parse_args()
    report = {case["id"]: analyse(case) for case in CASES}
    text = json.dumps(report, indent=2)
    if args.out:
        open(args.out, "w", encoding="utf-8").write(text + "\n")
    print(text)


if __name__ == "__main__":
    main()

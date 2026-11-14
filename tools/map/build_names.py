import argparse
import io
import json
import struct
import urllib.request
import zipfile
from datetime import date
from pathlib import Path

URL = "https://asc-planetarynames-data.s3.us-west-2.amazonaws.com/MOON_nomenclature_center_pts.zip"


def read_dbf(data, encoding):
    count, header, size = struct.unpack("<IHH", data[4:12])
    fields = []
    offset = 32
    while data[offset] != 0x0D:
        name = data[offset:offset + 11].split(b"\0")[0].decode("ascii").lower()
        fields.append((name, data[offset + 16]))
        offset += 32
    rows = []
    for index in range(count):
        start = header + index * size
        record = data[start:start + size]
        if record[:1] == b"*":
            continue
        position = 1
        row = {}
        for name, length in fields:
            row[name] = record[position:position + length].decode(encoding, errors="replace").strip()
            position += length
        rows.append(row)
    return [name for name, _ in fields], rows


def pick(fields, *candidates):
    for candidate in candidates:
        if candidate in fields:
            return candidate
    raise SystemExit(f"none of {candidates} in fields {fields}")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", required=True)
    parser.add_argument("--cache", required=True)
    args = parser.parse_args()

    cache = Path(args.cache)
    cache.mkdir(parents=True, exist_ok=True)
    archive = cache / "MOON_nomenclature_center_pts.zip"
    if not archive.exists():
        request = urllib.request.Request(URL, headers={"User-Agent": "Shadowline data pipeline"})
        with urllib.request.urlopen(request, timeout=120) as response:
            archive.write_bytes(response.read())

    with zipfile.ZipFile(archive) as bundle:
        names = bundle.namelist()
        dbf = next(name for name in names if name.lower().endswith(".dbf"))
        cpg = next((name for name in names if name.lower().endswith(".cpg")), None)
        encoding = bundle.read(cpg).decode("ascii").strip() if cpg else "utf-8"
        encoding = "utf-8" if encoding.upper() in ("UTF-8", "UTF8", "65001") else encoding
        fields, rows = read_dbf(bundle.read(dbf), encoding)

    f_name = pick(fields, "name", "clean_name")
    f_lat = pick(fields, "center_lat", "lat")
    f_lon = pick(fields, "center_lon", "lon", "long")
    f_diam = pick(fields, "diameter", "diam")
    f_type = pick(fields, "type", "feature_ty")
    f_approval = next((field for field in ("approval", "approval_s") if field in fields), None)

    types = []
    features = []
    for row in rows:
        if f_approval and "dropped" in row[f_approval].lower():
            continue
        try:
            lat = float(row[f_lat])
            lon = float(row[f_lon])
            diameter = float(row[f_diam] or 0)
        except ValueError:
            continue
        kind = row[f_type].split(",")[0].strip().lower()
        if kind not in types:
            types.append(kind)
        lon = (lon + 180) % 360 - 180
        features.append([row[f_name], round(lat, 4), round(lon, 4), round(diameter, 2), types.index(kind)])

    features.sort(key=lambda item: -item[3])
    out = Path(args.out)
    payload = {
        "formatVersion": 1,
        "source": "IAU WGPSN, USGS Gazetteer of Planetary Nomenclature, MOON_nomenclature_center_pts",
        "retrieved": date.today().isoformat(),
        "types": types,
        "features": features,
    }
    out.write_text(json.dumps(payload, ensure_ascii=False, separators=(",", ":")) + "\n", encoding="utf-8")
    print(f"fields: {', '.join(fields)}")
    print(f"{out}: {len(features)} features, {out.stat().st_size // 1024} KB")
    for kind in types:
        print(f"  {kind}: {sum(1 for item in features if types[item[4]] == kind)}")


if __name__ == "__main__":
    main()

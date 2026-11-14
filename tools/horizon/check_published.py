import argparse
import json
import math
import subprocess
import sys
import tempfile
from pathlib import Path

import numpy as np

POINTS = [
    ("glaser2014-spot1", -89.4395, 222.8066 - 360.0, 0.920),
    ("glaser2014-spot2", -89.4399, 222.8524 - 360.0, 0.923),
]


def sun_share(ephemeris, lat_deg, lon_deg, horizon):
    lat = math.radians(lat_deg)
    lon = math.radians(lon_deg)
    up = np.array([math.cos(lat) * math.cos(lon), math.cos(lat) * math.sin(lon), math.sin(lat)])
    north = np.array([-math.sin(lat) * math.cos(lon), -math.sin(lat) * math.sin(lon), math.cos(lat)])
    east = np.array([-math.sin(lon), math.cos(lon), 0.0])
    v = ephemeris[:, :3] - up * 1737.4
    distance = np.linalg.norm(v, axis=1)
    u = v / distance[:, None]
    elevation = np.degrees(np.arcsin(u @ up))
    azimuth = np.degrees(np.arctan2(u @ east, u @ north)) % 360.0
    k = azimuth / (360.0 / horizon.size)
    k0 = np.floor(k).astype(int) % horizon.size
    w = k - np.floor(k)
    terrain = horizon[k0] * (1 - w) + horizon[(k0 + 1) % horizon.size] * w
    radius = np.degrees(np.arcsin(695700.0 / distance))
    return float((elevation + radius > terrain).mean())


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dem", required=True)
    parser.add_argument("--far-ldem", required=True)
    parser.add_argument("--data-dir", required=True)
    args = parser.parse_args()

    data = Path(args.data_dir)
    manifest = json.loads((data / "manifest.json").read_text(encoding="utf-8"))
    ephemeris = np.fromfile(data / "ephemeris.bin", dtype="<f4").reshape(manifest["gridCount"], 6).astype(np.float64)
    with tempfile.TemporaryDirectory() as tmp:
        catalog = Path(tmp) / "catalog.json"
        catalog.write_text(json.dumps({"sites": [{"id": i, "latitudeDeg": la, "longitudeEastDeg": lo} for i, la, lo, _ in POINTS]}), encoding="utf-8")
        subprocess.run([sys.executable, str(Path(__file__).with_name("build_horizons.py")), "--dem", args.dem, "--far-ldem", args.far_ldem, "--catalog", str(catalog), "--out-dir", tmp], check=True)
        for point_id, lat, lon, published in POINTS:
            horizon = np.fromfile(Path(tmp) / f"{point_id}.bin", dtype="<f4").astype(np.float64)
            share = sun_share(ephemeris, lat, lon, horizon)
            print(f"{point_id}: Shadowline {share:.3f} over one year, Glaser et al. 2014 {published:.3f} over 19 years, difference {share - published:+.3f}")


if __name__ == "__main__":
    main()

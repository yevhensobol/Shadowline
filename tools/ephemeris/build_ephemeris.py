import argparse
import json
from datetime import datetime, timedelta, timezone
from pathlib import Path

import numpy as np
import spiceypy as spice

KERNEL_FILES = [
    "naif0012.tls",
    "de440s.bsp",
    "moon_pa_de440_200625.bpc",
    "moon_de440_250416.tf",
]

FRAME_CANDIDATES = ["MOON_ME_DE440_ME421", "MOON_ME"]

SOURCES = [
    {
        "name": "NAIF SPICE, DE440 planetary ephemeris and lunar frame kernels",
        "url": "https://naif.jpl.nasa.gov/naif/data.html",
        "citation": "Park, R. S. et al., The Astronomical Journal 161, 105 (2021)",
    }
]


def parse_utc(text):
    return datetime.fromisoformat(text.replace("Z", "+00:00")).astimezone(timezone.utc)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--manifest", required=True)
    parser.add_argument("--kernels", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--abcorr", default="LT+S")
    parser.add_argument("--update-manifest", action="store_true")
    args = parser.parse_args()

    manifest_path = Path(args.manifest)
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    start = parse_utc(manifest["gridStartUtc"])
    step = manifest["gridStepMinutes"]
    count = manifest["gridCount"]

    kernel_dir = Path(args.kernels)
    for name in KERNEL_FILES:
        spice.furnsh(str(kernel_dir / name))

    frame = next((name for name in FRAME_CANDIDATES if spice.namfrm(name) != 0), None)
    if frame is None:
        raise SystemExit("none of the frames " + ", ".join(FRAME_CANDIDATES) + " is defined by the loaded kernels")
    print(f"frame: {frame}")

    et0 = spice.str2et(start.strftime("%Y-%m-%d %H:%M:%S") + " UTC")
    ets = et0 + np.arange(count) * step * 60.0

    sun = np.array([spice.spkpos("SUN", float(et), frame, args.abcorr, "MOON")[0] for et in ets])
    earth = np.array([spice.spkpos("EARTH", float(et), frame, args.abcorr, "MOON")[0] for et in ets])
    rows = np.concatenate([sun, earth], axis=1).astype("<f4")

    Path(args.out).write_bytes(rows.tobytes())
    print(f"wrote {rows.shape[0]} rows, {rows.nbytes} bytes")

    if args.update_manifest:
        manifest["kind"] = "real"
        manifest["builtAtUtc"] = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
        manifest["sources"] = SOURCES
        manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
        print("manifest updated")


if __name__ == "__main__":
    main()

import argparse
import datetime as dt
import json
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "history"))

KERNEL_FILES = ["naif0012.tls", "de440s.bsp", "moon_pa_de440_200625.bpc", "moon_de440_250416.tf"]
FRAME_CANDIDATES = ["MOON_ME_DE440_ME421", "MOON_ME"]


class SpiceMoon:
    def __init__(self, kernels, abcorr):
        import spiceypy

        self.spice = spiceypy
        for name in KERNEL_FILES:
            spiceypy.furnsh(str(Path(kernels) / name))
        self.frame = next((name for name in FRAME_CANDIDATES if spiceypy.namfrm(name) != 0), None)
        if self.frame is None:
            raise SystemExit("none of the frames " + ", ".join(FRAME_CANDIDATES) + " is defined by the loaded kernels")
        self.abcorr = abcorr

    def vectors(self, times):
        ets = [self.spice.str2et(time.replace("T", " ") + " UTC") for time in times]
        sun = np.array([self.spice.spkpos("SUN", et, self.frame, self.abcorr, "MOON")[0] for et in ets])
        earth = np.array([self.spice.spkpos("EARTH", et, self.frame, self.abcorr, "MOON")[0] for et in ets])
        return sun, earth


def year_rows(moon, start, step_minutes):
    end = start.replace(year=start.year + 1)
    step = dt.timedelta(minutes=step_minutes)
    count = int((end - start) / step)
    rows = []
    chunk = 2000
    for first in range(0, count, chunk):
        times = [(start + i * step).strftime("%Y-%m-%dT%H:%M:%S") for i in range(first, min(count, first + chunk))]
        sun, earth = moon.vectors(times)
        rows.append(np.hstack([sun, earth]))
    return np.vstack(rows).astype("<f4"), count


def angle_deg(a, b):
    a = a / np.linalg.norm(a, axis=1, keepdims=True)
    b = b / np.linalg.norm(b, axis=1, keepdims=True)
    return np.degrees(np.arccos(np.clip(np.einsum("ij,ij->i", a, b), -1, 1)))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--kernels")
    parser.add_argument("--abcorr", default="LT+S")
    parser.add_argument("--bsp")
    parser.add_argument("--data-dir", required=True)
    parser.add_argument("--years", default="2027,2028,2029")
    parser.add_argument("--step-minutes", type=int, default=30)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()

    data = Path(args.data_dir)
    manifest = json.loads((data / "manifest.json").read_text(encoding="utf-8"))
    base = dt.datetime.fromisoformat(manifest["gridStartUtc"].replace("Z", "+00:00"))
    if args.kernels:
        moon = SpiceMoon(args.kernels, args.abcorr)
        frame_note = f"All years: NAIF SPICE {moon.frame}, DE440, {args.abcorr}"
    elif args.bsp:
        from moonframe import MoonCentred

        moon = MoonCentred(args.bsp)
        frame_note = "First year: SPICE MOON_ME. Later years: MOON_ME via the IAU 2015 lunar rotation model, DE440 positions with light time"
    else:
        raise SystemExit("pass --kernels (SPICE) or --bsp (IAU rotation model)")

    if args.check:
        shipped = np.fromfile(data / "ephemeris.bin", dtype="<f4").reshape(-1, 6).astype(np.float64)
        rows, _ = year_rows(moon, base, args.step_minutes)
        rows = rows.astype(np.float64)
        sun = angle_deg(shipped[:, :3], rows[:, :3])
        earth = angle_deg(shipped[:, 3:], rows[:, 3:])
        print(f"sun max {sun.max() * 3600:.3f} arcsec mean {sun.mean() * 3600:.3f}, earth max {earth.max() * 3600:.3f} arcsec mean {earth.mean() * 3600:.3f}")
        return

    out = data / "years"
    out.mkdir(parents=True, exist_ok=True)
    listing = [{"id": str(base.year), "gridStartUtc": manifest["gridStartUtc"], "gridStepMinutes": manifest["gridStepMinutes"], "gridCount": manifest["gridCount"], "file": "ephemeris.bin"}]
    for year in [int(y) for y in args.years.split(",")]:
        start = base.replace(year=year)
        rows, count = year_rows(moon, start, args.step_minutes)
        (out / f"{year}.bin").write_bytes(rows.tobytes())
        listing.append({"id": str(year), "gridStartUtc": start.strftime("%Y-%m-%dT%H:%M:%SZ"), "gridStepMinutes": args.step_minutes, "gridCount": count, "file": f"years/{year}.bin"})
        print(year, count, "rows")

    document = {"formatVersion": 1, "frame": frame_note, "years": listing}
    (data / "years.json").write_text(json.dumps(document, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()

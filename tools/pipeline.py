import argparse
import json
import subprocess
import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DATA = ROOT / "src" / "Shadowline.Web" / "wwwroot" / "data"
TOOLS = ROOT / "tools"
STEPS = ["grid", "kernels", "ephemeris", "validation", "horizons", "terrain"]


def run(command, dry_run):
    print("+ " + " ".join(str(part) for part in command))
    if not dry_run:
        subprocess.run([str(part) for part in command], check=True)


def set_grid(start, days, step_minutes, dry_run):
    path = DATA / "manifest.json"
    manifest = json.loads(path.read_text(encoding="utf-8"))
    manifest["gridStartUtc"] = start.strftime("%Y-%m-%dT%H:%M:%SZ")
    manifest["gridStepMinutes"] = step_minutes
    manifest["gridCount"] = days * 24 * 60 // step_minutes
    print(f"grid: {manifest['gridStartUtc']}, step {step_minutes} min, {manifest['gridCount']} rows")
    if not dry_run:
        path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")


def default_validation_sites():
    return "all"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--steps", default=",".join(STEPS))
    parser.add_argument("--start", default="2026-11-01")
    parser.add_argument("--days", type=int, default=365)
    parser.add_argument("--step-minutes", type=int, default=30)
    parser.add_argument("--kernels", default=str(ROOT.parent / "shadowline-kernels"))
    parser.add_argument("--dem")
    parser.add_argument("--max-range-km", type=float, default=100.0)
    parser.add_argument("--observer-height-m", type=float, default=2.0)
    parser.add_argument("--far-ldem")
    parser.add_argument("--validation-sites")
    parser.add_argument("--validation-days", type=int, default=1460)
    parser.add_argument("--validation-step", default="13 d")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    selected = [step.strip() for step in args.steps.split(",") if step.strip()]
    unknown = [step for step in selected if step not in STEPS]
    if unknown:
        raise SystemExit(f"unknown steps: {', '.join(unknown)}")
    if 1440 % args.step_minutes != 0:
        raise SystemExit("step-minutes must divide 1440")

    start = datetime.fromisoformat(args.start).replace(tzinfo=timezone.utc)
    python = sys.executable

    if "grid" in selected:
        set_grid(start, args.days, args.step_minutes, args.dry_run)

    if "kernels" in selected:
        run([python, TOOLS / "ephemeris" / "fetch_kernels.py", "--dest", args.kernels], args.dry_run)

    if "ephemeris" in selected:
        run(
            [
                python, TOOLS / "ephemeris" / "build_ephemeris.py",
                "--manifest", DATA / "manifest.json",
                "--kernels", args.kernels,
                "--out", DATA / "ephemeris.bin",
                "--update-manifest",
            ],
            args.dry_run,
        )

    if "validation" in selected:
        stop = start + timedelta(days=args.validation_days)
        run(
            [
                python, TOOLS / "ephemeris" / "build_validation.py",
                "--catalog", DATA / "catalog.json",
                "--sites", args.validation_sites or default_validation_sites(),
                "--start", start.strftime("%Y-%m-%d"),
                "--stop", stop.strftime("%Y-%m-%d"),
                "--step", args.validation_step,
                "--out", DATA / "validation.json",
            ],
            args.dry_run,
        )

    if "horizons" in selected:
        if not args.dem:
            print("horizons: skipped, pass --dem <file.tif>")
        else:
            run(
                [
                    python, TOOLS / "horizon" / "build_horizons.py",
                    "--dem", args.dem,
                    "--catalog", DATA / "catalog.json",
                    "--out-dir", DATA / "horizon",
                    "--max-range-km", args.max_range_km,
                    "--observer-height-m", args.observer_height_m,
                ] + (["--far-ldem", args.far_ldem] if args.far_ldem else []),
                args.dry_run,
            )

    if "terrain" in selected:
        if not args.dem:
            print("terrain: skipped, pass --dem <file.tif>")
        else:
            run(
                [
                    python, TOOLS / "terrain" / "build_terrain.py",
                    "--dem", args.dem,
                    "--catalog", DATA / "catalog.json",
                    "--out-dir", DATA / "terrain",
                ],
                args.dry_run,
            )


if __name__ == "__main__":
    main()

import argparse
import subprocess
import sys
import time
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
DATA = ROOT / "src" / "Shadowline.Web" / "wwwroot" / "data"
SCENE = ROOT / "src" / "Shadowline.Web" / "wwwroot" / "img" / "scene"
TOOLS = ROOT / "tools"
CHECK = TOOLS / "thermal" / "RegolithCheck"
STEPS = ["artemis", "diviner", "horizons", "cells", "slopes", "regolith", "validate"]


def run(command):
    print("+ " + " ".join(str(part) for part in command), flush=True)
    started = time.time()
    subprocess.run([str(part) for part in command], check=True)
    print(f"  {time.time() - started:.0f} s", flush=True)


def need(path, hint):
    if path is None or not Path(path).exists():
        raise SystemExit(f"missing {path}: {hint}")


def regolith(work, count, jobs):
    run(["dotnet", "build", "-c", "Release", "-v", "q", CHECK])
    size = (count + jobs - 1) // jobs
    parts = []
    procs = []
    for k in range(jobs):
        first = k * size
        if first >= count:
            break
        out = work / f"model_{k}.csv"
        out.unlink(missing_ok=True)
        parts.append(out)
        command = ["dotnet", "run", "-c", "Release", "--no-build", "--project", CHECK, "--", DATA, work / "cells.bin", out, first, min(size, count - first), work / "slopes.bin"]
        print("+ " + " ".join(str(part) for part in command), flush=True)
        procs.append(subprocess.Popen([str(part) for part in command]))
    started = time.time()
    if any(proc.wait() != 0 for proc in procs):
        raise SystemExit("RegolithCheck failed")
    with open(work / "model.csv", "w", encoding="utf-8", newline="") as merged:
        for out in parts:
            merged.write(out.read_text(encoding="utf-8"))
    print(f"  {time.time() - started:.0f} s, {count} cells", flush=True)


def main():
    parser = argparse.ArgumentParser(description="Train and check every Shadowline model")
    parser.add_argument("--steps", default=",".join(STEPS))
    parser.add_argument("--work", default=str(ROOT / "data-cache" / "ml"))
    parser.add_argument("--dem", help="LDEM_80S_80MPP_ADJ.tiff")
    parser.add_argument("--prp", help="dlre_prp_south.tab")
    parser.add_argument("--cells", type=int, default=4000)
    parser.add_argument("--jobs", type=int, default=4)
    args = parser.parse_args()
    steps = [s.strip() for s in args.steps.split(",") if s.strip()]
    unknown = [s for s in steps if s not in STEPS]
    if unknown:
        raise SystemExit(f"unknown steps: {', '.join(unknown)}")
    work = Path(args.work)
    work.mkdir(parents=True, exist_ok=True)
    py = sys.executable

    if "artemis" in steps:
        run([py, TOOLS / "ml" / "train_artemis.py", "--data-dir", DATA, "--scene-dir", SCENE])
    if "diviner" in steps:
        need(args.prp, "download dlre_prp_south.tab, see tools/ml/README.md")
        run([py, TOOLS / "thermal" / "check_prp.py", "--prp", args.prp])
        run([py, TOOLS / "thermal" / "build_diviner.py", "--prp", args.prp, "--data-dir", DATA, "--cache", work / "diviner_grid.npz"])
    if "horizons" in steps and not (work / "hz.npz").exists():
        need(args.dem, "download LDEM_80S_80MPP_ADJ.tiff, see tools/ml/README.md")
        run([py, TOOLS / "light" / "build_pole_light.py", "--dem", args.dem, "--data-dir", DATA, "--out-dir", work, "--cache", work / "hz.npz"])
    if "cells" in steps:
        need(work / "hz.npz", "run the horizons step")
        run([py, TOOLS / "thermal" / "export_cells.py", "--hz", work / "hz.npz", "--out", work / "cells.bin", "--count", args.cells])
    if "slopes" in steps:
        need(args.dem, "download LDEM_80S_80MPP_ADJ.tiff, see tools/ml/README.md")
        run([py, TOOLS / "thermal" / "cell_slopes.py", "--dem", args.dem, "--index", work / "cells.idx.npz", "--out", work / "slopes.npz"])
        s = np.load(work / "slopes.npz")
        np.stack([s["slope"], s["aspect"]], axis=-1).astype("<f4").tofile(work / "slopes.bin")
    if "regolith" in steps:
        need(work / "slopes.bin", "run the slopes step")
        regolith(work, args.cells, max(1, args.jobs))
    if "validate" in steps:
        need(work / "diviner_grid.npz", "run the diviner step")
        run([py, TOOLS / "thermal" / "validate_regolith.py", "--model", work / "model.csv", "--index", work / "cells.idx.npz", "--diviner", work / "diviner_grid.npz", "--scene-dir", SCENE, "--data-dir", DATA])


if __name__ == "__main__":
    main()

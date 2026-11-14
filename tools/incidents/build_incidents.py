import argparse
import datetime as dt
import json
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "history"))
from moonframe import MoonCentred  # noqa: E402

STEP_MINUTES = 30


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--bsp", required=True)
    parser.add_argument("--facts", default=str(Path(__file__).resolve().parent / "incidents.json"))
    parser.add_argument("--data-dir", required=True)
    args = parser.parse_args()
    facts = json.loads(Path(args.facts).read_text(encoding="utf-8"))
    out = Path(args.data_dir) / "incidents"
    out.mkdir(parents=True, exist_ok=True)
    moon = MoonCentred(args.bsp)
    step = dt.timedelta(minutes=STEP_MINUTES)
    for incident in facts["incidents"]:
        start = dt.datetime.fromisoformat(incident["gridStartUtc"].replace("Z", "+00:00"))
        count = incident.pop("gridDays") * 24 * 60 // STEP_MINUTES
        times = [(start + i * step).strftime("%Y-%m-%dT%H:%M:%S") for i in range(count)]
        sun, earth = moon.vectors(times)
        (out / f"{incident['id']}.bin").write_bytes(np.hstack([sun, earth]).astype("<f4").tobytes())
        incident["gridStepMinutes"] = STEP_MINUTES
        incident["gridCount"] = count
        print(incident["id"], count, "rows")
    facts["frame"] = "MOON_ME via the IAU 2015 lunar rotation model, DE440 positions with light time"
    (Path(args.data_dir) / "incidents.json").write_text(json.dumps(facts, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()

import argparse
import datetime as dt
import json
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
from moonframe import MoonCentred  # noqa: E402


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--bsp", required=True)
    parser.add_argument("--events", default=str(Path(__file__).resolve().parent / "events.json"))
    parser.add_argument("--data-dir", required=True)
    parser.add_argument("--days-before", type=int, default=3)
    parser.add_argument("--days-after", type=int, default=25)
    parser.add_argument("--step-minutes", type=int, default=30)
    args = parser.parse_args()

    events = json.loads(Path(args.events).read_text(encoding="utf-8"))
    out = Path(args.data_dir) / "history"
    out.mkdir(parents=True, exist_ok=True)
    moon = MoonCentred(args.bsp)
    listing = []
    for event in events:
        landing = dt.datetime.fromisoformat(event["landingUtc"].replace("Z", "+00:00"))
        step = dt.timedelta(minutes=args.step_minutes)
        start = landing.replace(minute=(landing.minute // args.step_minutes) * args.step_minutes, second=0) - dt.timedelta(days=args.days_before)
        count = int(dt.timedelta(days=args.days_before + args.days_after) / step)
        times = [(start + i * step).strftime("%Y-%m-%dT%H:%M:%S") for i in range(count)]
        sun, earth = moon.vectors(times)
        rows = np.hstack([sun, earth]).astype("<f4")
        (out / f"{event['id']}.bin").write_bytes(rows.tobytes())
        listing.append({
            "id": event["id"],
            "site": event["site"],
            "landingUtc": event["landingUtc"],
            "gridStartUtc": start.strftime("%Y-%m-%dT%H:%M:%SZ"),
            "gridStepMinutes": args.step_minutes,
            "gridCount": count,
            "source": event["source"],
            "title": event["title"],
        })
        print(event["id"], count, "rows")

    document = {"formatVersion": 1, "frame": "MOON_ME via the IAU 2015 lunar rotation model in TDB, DE440 positions with light time and stellar aberration", "events": listing}
    (Path(args.data_dir) / "history.json").write_text(json.dumps(document, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()

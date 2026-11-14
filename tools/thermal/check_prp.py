import argparse
import hashlib
from pathlib import Path

import numpy as np

WIDTH = 210
RECORDS = 2880000


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--prp", required=True)
    args = parser.parse_args()
    path = Path(args.prp)
    size = path.stat().st_size
    print("size", size, "expected", WIDTH * (RECORDS + 1), "ok" if size == WIDTH * (RECORDS + 1) else "MISMATCH")
    if size % WIDTH != 0:
        raise SystemExit("size is not a whole number of records, the download is cut")
    raw = np.fromfile(path, dtype=np.uint8)
    table = raw.reshape(-1, WIDTH)
    print("records", table.shape[0] - 1)
    ends = (table[:, -2] == 13) & (table[:, -1] == 10)
    print("records ending in CRLF", int(ends.sum()), "of", table.shape[0])
    body = table[1:]
    allowed = np.zeros(256, dtype=bool)
    allowed[np.frombuffer(b" 0123456789.-,eE+", dtype=np.uint8)] = True
    bad = 0
    for start in range(0, body.shape[0], 200000):
        bad += int((~allowed[body[start:start + 200000, :-2]]).any(axis=1).sum())
    print("records with unexpected characters", bad)
    fields = [(0, 12), (14, 12), (28, 12), (42, 12), (56, 12), (70, 12), (84, 12), (98, 12), (112, 12), (126, 15), (143, 15), (160, 15), (177, 8), (187, 8), (197, 11)]
    names = ["x1", "y1", "z1", "x2", "y2", "z2", "x3", "y3", "z3", "lon", "lat", "alt", "avg", "max", "ice"]
    values = {}
    for name, (start, length) in zip(names, fields):
        v = body[:, start:start + length].copy().view(f"S{length}").ravel().astype(np.float64)
        values[name] = v
        print(f"{name:4s} min {v.min():12.4f} max {v.max():12.4f} nan {int(np.isnan(v).sum())}")
    r = np.sqrt(values["x1"] ** 2 + values["y1"] ** 2 + values["z1"] ** 2)
    print("vertex radius km", round(float(r.min()), 2), round(float(r.max()), 2))
    print("avg <= max everywhere", bool((values["avg"] <= values["max"] + 1e-6).all()))
    md5 = hashlib.md5()
    with open(path, "rb") as handle:
        for chunk in iter(lambda: handle.read(1 << 24), b""):
            md5.update(chunk)
    print("md5", md5.hexdigest())
    if size != WIDTH * (RECORDS + 1) or bad or int(ends.sum()) != table.shape[0] or not (values["avg"] <= values["max"] + 1e-6).all():
        raise SystemExit("file check failed")


if __name__ == "__main__":
    main()

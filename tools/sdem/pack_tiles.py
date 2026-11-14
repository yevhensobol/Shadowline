import argparse
import gzip
import json
import os
import shutil
import time
from pathlib import Path

UNIT_M = 0.5


def write_gzip(raw, path):
    temp = path.with_suffix(".tmp")
    with open(temp, "wb") as handle:
        with gzip.GzipFile(fileobj=handle, mode="wb", compresslevel=9, mtime=0) as stream:
            stream.write(raw)
    os.replace(temp, path)


def pack(source, target, budget):
    start = time.monotonic()
    done = True
    manifest = json.loads((source / "manifest.json").read_text(encoding="utf-8"))
    regions = []
    files = 0
    for region in manifest["regions"]:
        meta = json.loads((source / region["id"] / "region.json").read_text(encoding="utf-8"))
        out = target / region["id"]
        out.mkdir(parents=True, exist_ok=True)
        tiles = []
        for tile in meta["tiles"]:
            heights = out / (tile["id"] + ".bin.gz")
            photo = out / (tile["id"] + ".jpg")
            if not (heights.exists() and photo.exists()):
                if budget and time.monotonic() - start > budget:
                    done = False
                    break
                raw = (source / region["id"] / (tile["id"] + ".bin")).read_bytes()
                if len(raw) != tile["cols"] * tile["rows"] * 2:
                    raise ValueError(f"{region['id']}/{tile['id']}: size {len(raw)}")
                write_gzip(raw, heights)
                shutil.copyfile(source / region["id"] / (tile["id"] + ".jpg"), photo)
            files += 2
            x0, y0, x1, y1 = (round(v, 2) for v in tile["bounds"])
            tiles.append([tile["id"], tile["level"], x0, y0, x1, y1, tile["cols"], tile["rows"]])
        if not done:
            return files, False
        regions.append({"id": region["id"], "name": region["name"], "bounds": [round(v, 2) for v in region["bounds"]], "photoM": region.get("photoNativeM", [5.0])[0], "tiles": tiles})
    index = {"version": 1, "unitM": UNIT_M, "source": "doi:10.5281/zenodo.17954508", "regions": regions}
    (target / "index.json").write_text(json.dumps(index, separators=(",", ":")) + "\n", encoding="utf-8")
    return files + 1, True


def main():
    parser = argparse.ArgumentParser(description="Pack imported SDEM tiles for the web client")
    parser.add_argument("source", type=Path)
    parser.add_argument("--out", type=Path, default=Path(__file__).resolve().parents[2] / "src" / "Shadowline.Web" / "wwwroot" / "data" / "sdem")
    parser.add_argument("--budget", type=float, default=0, help="stop after this many seconds, rerun to continue")
    args = parser.parse_args()
    files, complete = pack(args.source, args.out, args.budget)
    print(f"{files} files in {args.out}" if complete else f"partial: {files} files, rerun to continue")


if __name__ == "__main__":
    main()

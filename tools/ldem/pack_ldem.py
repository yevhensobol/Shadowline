"""Pack the LOLA 5 m south pole DEM (ldem_87s_5mpp.tif, Barker et al. 2021) into
height tiles for the 3D view: levels of 5, 20 and 80 m, 257 x 257 samples per tile,
int16 in 0.5 m steps with row deltas, gzip, appended into pack files of at most 24 MiB
that the browser reads with HTTP range requests."""
import argparse
import gzip
import json
import math
import os
import time
from pathlib import Path

import numpy as np
import tifffile

N = 256
UNIT_M = 0.5
CHUNK = 24 * 1024 * 1024
SOURCE = "LOLA 5 m/px DEM mosaic 87-90 S (ldem_87s_5mpp), Barker et al. 2021, doi:10.1016/j.pss.2020.105119"


class Packer:
    def __init__(self, out):
        self.out = out
        self.files = []
        self.handle = None
        self.size = 0

    def add(self, blob):
        if self.handle is None or self.size + len(blob) > CHUNK:
            if self.handle:
                self.handle.close()
            name = f"p{len(self.files):03d}.bin"
            self.files.append(name)
            self.handle = open(self.out / name, "wb")
            self.size = 0
        offset = self.size
        self.handle.write(blob)
        self.size += len(blob)
        return len(self.files) - 1, offset, len(blob)

    def close(self):
        if self.handle:
            self.handle.close()


def encode(tile):
    q = np.round(tile / UNIT_M).astype(np.int32)
    d = q.copy()
    d[:, 1:] = q[:, 1:] - q[:, :-1]
    d[1:, 0] = q[1:, 0] - q[:-1, 0]
    return gzip.compress(d.astype("<i2").tobytes(), compresslevel=6, mtime=0)


def tiles_of(grid, packer, level):
    rows, cols = grid.shape
    out = []
    for tr in range(max(1, math.ceil((rows - 1) / N))):
        for tc in range(max(1, math.ceil((cols - 1) / N))):
            tile = grid[tr * N:tr * N + N + 1, tc * N:tc * N + N + 1]
            if tile.shape != (N + 1, N + 1) or not np.isfinite(tile).all():
                continue
            out.append([tc, tr, *packer.add(encode(tile))])
    print(f"level {level} m: {len(out)} tiles", flush=True)
    return out


def block_mean(a, k):
    h, w = a.shape[0] // k * k, a.shape[1] // k * k
    return a[:h, :w].reshape(h // k, k, w // k, k).mean(axis=(1, 3))


class Reader:
    def __init__(self, path):
        self.tif = tifffile.TiffFile(path)
        self.page = self.tif.pages[0]
        self.height, self.width = self.page.shape[:2]
        tag = self.page.tags.get("GDAL_NODATA")
        self.nodata = float(tag.value) if tag else None
        if self.page.is_tiled:
            self.sh, self.sw = self.page.tilelength, self.page.tilewidth
        else:
            self.sh, self.sw = self.page.rowsperstrip, self.width
        self.across = math.ceil(self.width / self.sw)

    def rows(self, r0, r1):
        out = np.full((r1 - r0, self.width), np.nan, np.float32)
        fh = self.tif.filehandle
        for sr in range(r0 // self.sh, (r1 - 1) // self.sh + 1):
            for sc in range(self.across):
                index = sr * self.across + sc
                fh.seek(self.page.dataoffsets[index])
                data = fh.read(self.page.databytecounts[index])
                seg, _, shape = self.page.decode(data, index)
                seg = np.asarray(seg, np.float32).reshape(shape[1], shape[2])
                y0, x0 = sr * self.sh, sc * self.sw
                a, b = max(r0, y0), min(r1, y0 + seg.shape[0], self.height)
                w = min(seg.shape[1], self.width - x0)
                if b > a:
                    out[a - r0:b - r0, x0:x0 + w] = seg[a - y0:b - y0, :w]
        if self.nodata is not None and np.isfinite(self.nodata):
            out[out == self.nodata] = np.nan
        out[np.abs(out) > 20000] = np.nan
        return out


def check(reader, out, files, tiles, count=40):
    rng = np.random.default_rng(1)
    worst = 0.0
    for k in rng.choice(len(tiles), size=min(count, len(tiles)), replace=False):
        tc, tr, f, offset, length = tiles[k]
        with open(out / files[f], "rb") as fh:
            fh.seek(offset)
            d = np.frombuffer(gzip.decompress(fh.read(length)), "<i2").astype(np.int32).reshape(N + 1, N + 1)
        d[:, 0] = np.cumsum(d[:, 0])
        h = np.cumsum(d, axis=1) * UNIT_M
        src = reader.rows(tr * N, tr * N + N + 1)[:, tc * N:tc * N + N + 1]
        worst = max(worst, float(np.abs(h - src).max()))
    print(f"check: {min(count, len(tiles))} random tiles, max difference {worst:.3f} m", flush=True)
    if worst > UNIT_M:
        raise SystemExit("check failed")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("tif")
    parser.add_argument("out")
    parser.add_argument("--rows", type=int, default=0, help="limit 5 m tile rows (for a quick test)")
    args = parser.parse_args()
    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    reader = Reader(args.tif)
    page = reader.page
    scale = page.tags["ModelPixelScaleTag"].value
    tie = page.tags["ModelTiepointTag"].value
    keys = page.geotiff_tags or {}
    point = keys.get("GTRasterTypeGeoKey") in (2, "RasterPixelIsPoint") or str(keys.get("GTRasterTypeGeoKey")).endswith("Point")
    res = float(scale[0])
    ox = tie[3] + (0 if point else res / 2) - tie[0] * res
    oy = tie[4] - (0 if point else res / 2) + tie[1] * res
    print(f"{reader.width} x {reader.height}, {res} m, origin {ox:.1f} {oy:.1f}, tiled {page.is_tiled}, compression {page.compression!r}, predictor {page.predictor!r}, nodata {reader.nodata}", flush=True)
    packer = Packer(out)
    levels = []
    tile_rows = math.ceil((reader.height - 1) / N)
    if args.rows:
        tile_rows = min(tile_rows, args.rows)
    ds = np.full((tile_rows * N // 4, reader.width // 4), np.nan, np.float32)
    five = []
    start = time.time()
    for tr in range(tile_rows):
        band = reader.rows(tr * N, min(tr * N + N + 1, reader.height))
        if band.shape[0] < N + 1:
            band = np.vstack([band, np.full((N + 1 - band.shape[0], reader.width), np.nan, np.float32)])
        part = block_mean(band[:N], 4)
        ds[tr * N // 4:tr * N // 4 + part.shape[0], :part.shape[1]] = part
        for tc in range(math.ceil((reader.width - 1) / N)):
            tile = band[:, tc * N:tc * N + N + 1]
            if tile.shape != (N + 1, N + 1) or not np.isfinite(tile).all():
                continue
            five.append([tc, tr, *packer.add(encode(tile))])
        if tr % 10 == 0:
            print(f"row {tr + 1}/{tile_rows}: {len(five)} tiles, {time.time() - start:.0f} s", flush=True)
    print(f"level 5 m: {len(five)} tiles", flush=True)
    levels.append({"res": res, "x": ox, "y": oy, "tiles": five})
    x20, y20 = ox + 1.5 * res, oy - 1.5 * res
    levels.append({"res": res * 4, "x": x20, "y": y20, "tiles": tiles_of(ds, packer, res * 4)})
    d80 = block_mean(ds, 4)
    levels.append({"res": res * 16, "x": x20 + 6 * res, "y": y20 - 6 * res, "tiles": tiles_of(d80, packer, res * 16)})
    packer.close()
    index = {"version": 2, "unitM": UNIT_M, "n": N + 1, "delta": True, "source": SOURCE, "files": packer.files, "levels": levels}
    (out / "index.json").write_text(json.dumps(index, separators=(",", ":")), encoding="utf-8")
    check(reader, out, packer.files, five)
    total = sum((out / f).stat().st_size for f in packer.files)
    print(f"done: {len(packer.files)} pack files, {total / 2**20:.0f} MiB, {time.time() - start:.0f} s", flush=True)


if __name__ == "__main__":
    main()

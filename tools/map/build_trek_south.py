import argparse
import io
import math
import time
import urllib.error
import urllib.request
from pathlib import Path

import numpy as np
from PIL import Image

LAYER = "LRO_WAC_Mosaic_SPole60_100m_v02"
URL = "https://trek.nasa.gov/tiles/Moon/SP/{layer}/1.0.0/default/default028mm/{level}/{row}/{col}.png"
ORIGIN_M = 1095930.0
SPAN_M = 2 * ORIGIN_M
TILE = 256
RADIUS_M = 1737400.0


def resolution(level):
    return SPAN_M / TILE / 2 ** level


def fetch(level, row, col, cache):
    path = cache / str(level) / f"{row}_{col}.png"
    if path.exists():
        return Image.open(path).convert("L")
    path.parent.mkdir(parents=True, exist_ok=True)
    url = URL.format(layer=LAYER, level=level, row=row, col=col)
    request = urllib.request.Request(url, headers={"User-Agent": "Shadowline data pipeline"})
    for attempt in range(4):
        try:
            with urllib.request.urlopen(request, timeout=60) as response:
                data = response.read()
            path.write_bytes(data)
            return Image.open(io.BytesIO(data)).convert("L")
        except urllib.error.HTTPError as error:
            if error.code == 404:
                return None
            if attempt == 3:
                raise
        except (urllib.error.URLError, TimeoutError):
            if attempt == 3:
                raise
        time.sleep(2 * (attempt + 1))
    return None


def mosaic(level, half_m, cache):
    res = resolution(level)
    first = int(math.floor((ORIGIN_M - half_m) / (TILE * res)))
    last = int(math.floor((ORIGIN_M + half_m) / (TILE * res)))
    count = last - first + 1
    canvas = np.zeros((count * TILE, count * TILE), dtype=np.float32)
    for row in range(first, last + 1):
        for col in range(first, last + 1):
            tile = fetch(level, row, col, cache)
            if tile is None:
                continue
            r0 = (row - first) * TILE
            c0 = (col - first) * TILE
            canvas[r0:r0 + TILE, c0:c0 + TILE] = np.asarray(tile, dtype=np.float32)
        print(f"row {row - first + 1}/{count}")
    left_m = -ORIGIN_M + first * TILE * res
    top_m = ORIGIN_M - first * TILE * res
    return canvas, left_m, top_m, res


def bilinear(source, rows, cols):
    height, width = source.shape
    rows = np.clip(rows, 0, height - 1.001)
    cols = np.clip(cols, 0, width - 1.001)
    r0 = np.floor(rows).astype(int)
    c0 = np.floor(cols).astype(int)
    fr = rows - r0
    fc = cols - c0
    top = source[r0, c0] * (1 - fc) + source[r0, c0 + 1] * fc
    bottom = source[r0 + 1, c0] * (1 - fc) + source[r0 + 1, c0 + 1] * fc
    return top * (1 - fr) + bottom * fr


def stretch(values, inside, low_pct, high_pct):
    sample = values[inside & (values > 0)]
    low, high = np.percentile(sample, [low_pct, high_pct])
    scaled = (values - low) / max(high - low, 1e-6)
    return np.clip(scaled, 0, 1) ** (1 / 1.2) * 255


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", required=True)
    parser.add_argument("--cache", required=True)
    parser.add_argument("--size", type=int, default=2048)
    parser.add_argument("--limit", type=float, default=-60.0)
    parser.add_argument("--level", type=int, default=4)
    parser.add_argument("--supersample", type=int, default=2)
    parser.add_argument("--low", type=float, default=0.5)
    parser.add_argument("--high", type=float, default=99.7)
    args = parser.parse_args()

    half_m = 2 * RADIUS_M * math.tan(math.radians(90 + args.limit) / 2)
    canvas, left_m, top_m, res = mosaic(args.level, half_m, Path(args.cache))

    n = args.size * args.supersample
    centres = (np.arange(n) + 0.5) / n * 2 - 1
    dx, dy = np.meshgrid(centres, -centres)
    x_m = dx * half_m
    y_m = dy * half_m
    cols = (x_m - left_m) / res - 0.5
    rows = (top_m - y_m) / res - 0.5
    values = bilinear(canvas, rows, cols)
    inside = np.hypot(dx, dy) <= 1
    image = stretch(values, inside, args.low, args.high)
    image[~inside] = 0

    big = Image.fromarray(image.astype(np.uint8), mode="L")
    small = big.resize((args.size, args.size), Image.LANCZOS) if args.supersample > 1 else big
    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    small.save(out, quality=86, optimize=True, progressive=True)
    print(f"{out}: {args.size} px, {2 * half_m / args.size:.0f} m per pixel, level {args.level} at {res:.0f} m")


if __name__ == "__main__":
    main()

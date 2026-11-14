import argparse
import io
import time
import urllib.error
import urllib.request
from pathlib import Path

import numpy as np
from PIL import Image

LAYER = "LRO_WAC_Mosaic_Global_303ppd_v02"
URL = "https://trek.nasa.gov/tiles/Moon/EQ/{layer}/1.0.0/default/default028mm/{level}/{row}/{col}.jpg"
TILE = 256


def fetch(level, row, col, cache):
    path = cache / str(level) / f"{row}_{col}.jpg"
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


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", required=True)
    parser.add_argument("--cache", required=True)
    parser.add_argument("--width", type=int, default=2048)
    parser.add_argument("--level", type=int, default=3)
    parser.add_argument("--low", type=float, default=0.5)
    parser.add_argument("--high", type=float, default=99.7)
    args = parser.parse_args()

    cols = 2 * 2 ** args.level
    rows = 2 ** args.level
    canvas = np.zeros((rows * TILE, cols * TILE), dtype=np.float32)
    for row in range(rows):
        for col in range(cols):
            tile = fetch(args.level, row, col, Path(args.cache))
            if tile is not None:
                canvas[row * TILE:(row + 1) * TILE, col * TILE:(col + 1) * TILE] = np.asarray(tile, dtype=np.float32)
        print(f"row {row + 1}/{rows}")

    low, high = np.percentile(canvas[canvas > 0], [args.low, args.high])
    image = np.clip((canvas - low) / max(high - low, 1e-6), 0, 1) ** (1 / 1.2) * 255
    big = Image.fromarray(image.astype(np.uint8), mode="L")
    size = (args.width, args.width // 2)
    small = big.resize(size, Image.LANCZOS) if big.size != size else big
    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    small.save(out, quality=86, optimize=True, progressive=True)
    print(f"{out}: {size[0]} x {size[1]} px, level {args.level} at {360 / big.size[0] * 30.3:.2f} km per pixel at the equator")


if __name__ == "__main__":
    main()

import argparse
import math
from pathlib import Path

import numpy as np
from PIL import Image

Image.MAX_IMAGE_PIXELS = None


def bilinear(source, rows, cols):
    height, width = source.shape[:2]
    cols = np.mod(cols, width)
    rows = np.clip(rows, 0, height - 1)
    c0 = np.floor(cols).astype(int)
    r0 = np.floor(rows).astype(int)
    c1 = (c0 + 1) % width
    r1 = np.minimum(r0 + 1, height - 1)
    fc = (cols - c0)[..., None]
    fr = (rows - r0)[..., None]
    top = source[r0, c0] * (1 - fc) + source[r0, c1] * fc
    bottom = source[r1, c0] * (1 - fc) + source[r1, c1] * fc
    return top * (1 - fr) + bottom * fr


def polar(source, size, limit_deg, lon_offset_deg):
    height, width = source.shape[:2]
    radius = size / 2
    scale = math.tan(math.radians(90 + limit_deg) / 2)
    ys, xs = np.mgrid[0:size, 0:size].astype(np.float64)
    dx = (xs + 0.5 - radius) / radius
    dy = (radius - (ys + 0.5)) / radius
    r = np.hypot(dx, dy)
    lat = np.degrees(2 * np.arctan(r * scale)) - 90
    lon = np.degrees(np.arctan2(dx, dy))
    cols = (lon + 180 + lon_offset_deg) / 360 * width - 0.5
    rows = (90 - lat) / 180 * height - 0.5
    image = bilinear(source, rows, cols)
    image[r > 1] = 0
    return image


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("source")
    parser.add_argument("outdir")
    parser.add_argument("--lon-offset", type=float, default=0.0)
    parser.add_argument("--polar-size", type=int, default=800)
    parser.add_argument("--limit", type=float, default=-60.0)
    args = parser.parse_args()

    outdir = Path(args.outdir)
    outdir.mkdir(parents=True, exist_ok=True)

    image = Image.open(args.source).convert("RGB")
    source = np.asarray(image, dtype=np.float32)

    flat = image.resize((2048, 1024), Image.LANCZOS)
    flat.save(outdir / "equirect.jpg", quality=85, optimize=True)

    south = polar(source, args.polar_size, args.limit, args.lon_offset)
    Image.fromarray(np.clip(south, 0, 255).astype(np.uint8)).save(outdir / "south.jpg", quality=88, optimize=True)


if __name__ == "__main__":
    main()

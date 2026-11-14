import argparse
from pathlib import Path

import numpy as np
import rasterio

TILE_DEG = 15
PX_PER_DEG = 16


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("ldem")
    parser.add_argument("out_dir")
    args = parser.parse_args()
    out = Path(args.out_dir)
    out.mkdir(parents=True, exist_ok=True)
    with rasterio.open(args.ldem) as dataset:
        km = dataset.read(1)
    if km.shape != (180 * PX_PER_DEG, 360 * PX_PER_DEG):
        raise SystemExit(f"unexpected size {km.shape}")
    meters = np.clip(np.round(km.astype(np.float64) * 1000.0), -32767, 32767).astype("<i2")
    size = TILE_DEG * PX_PER_DEG
    for row in range(180 // TILE_DEG):
        for col in range(360 // TILE_DEG):
            tile = meters[row * size:(row + 1) * size, col * size:(col + 1) * size]
            (out / f"r{row}c{col}.bin").write_bytes(np.ascontiguousarray(tile).tobytes())
    print(f"{(180 // TILE_DEG) * (360 // TILE_DEG)} tiles of {size}x{size}")


if __name__ == "__main__":
    main()

import argparse
import json
import math
import time
from pathlib import Path

import cv2
import numpy as np
from PIL import Image

MOON_RADIUS_M = 1737400.0
SUN_RADIUS_KM = 695700.0
AZIMUTHS = 360


def distances_m():
    parts = [
        np.arange(250, 5000, 250),
        np.arange(5000, 20000, 500),
        np.arange(20000, 60000, 1000),
        np.arange(60000, 150000, 2500),
        np.arange(150000, 300001, 5000),
    ]
    return np.concatenate(parts).astype(np.float64)


def block_mean(array, factor):
    n = array.shape[0] // factor
    return array[: n * factor, : n * factor].reshape(n, factor, n, factor).mean(axis=(1, 3))


def load_dem(path):
    import rasterio

    with rasterio.open(path) as dataset:
        dem = dataset.read(1).astype(np.float32)
        pixel = dataset.transform.a
        half = -dataset.transform.c
    dem = np.where(np.isfinite(dem), dem, np.float32(np.nanmedian(dem)))
    if np.nanmax(np.abs(dem)) < 50:
        dem = dem * 1000.0
    return dem, pixel, half


def slope_map(dem, pixel, out_size):
    gy, gx = np.gradient(dem, pixel)
    slope = np.degrees(np.arctan(np.hypot(gx, gy))).astype(np.float32)
    del gx, gy
    return cv2.resize(slope, (out_size, out_size), interpolation=cv2.INTER_AREA)


def observer_grid(n, half):
    spacing = 2 * half / n
    j = np.arange(n)
    x = -half + (j + 0.5) * spacing
    y = half - (j + 0.5) * spacing
    xx, yy = np.meshgrid(x, y)
    return xx, yy


def horizons(dem, pixel, half, xx, yy, observer_height):
    col = ((xx + half) / pixel - 0.5).astype(np.float32)
    row = ((half - yy) / pixel - 0.5).astype(np.float32)
    base = cv2.remap(dem, col, row, cv2.INTER_LINEAR) + observer_height
    result = np.empty((AZIMUTHS,) + xx.shape, dtype=np.float16)
    dist = distances_m()
    curvature = dist * dist / (2 * MOON_RADIUS_M)
    start = time.time()
    for k in range(AZIMUTHS):
        phi = math.radians(k)
        dc = math.cos(phi) / pixel
        dr = -math.sin(phi) / pixel
        best = np.full(xx.shape, -1.0, dtype=np.float32)
        for d, c in zip(dist, curvature):
            heights = cv2.remap(dem, col + np.float32(d * dc), row + np.float32(d * dr), cv2.INTER_LINEAR,
                                borderMode=cv2.BORDER_CONSTANT, borderValue=-1e7)
            np.maximum(best, (heights - base - np.float32(c)) / np.float32(d), out=best)
        result[k] = np.degrees(np.arctan(best)).astype(np.float16)
        if k % 30 == 29:
            print(f"azimuth {k + 1}/{AZIMUTHS} {time.time() - start:.0f}s", flush=True)
    return result, base


def fractions(hz, base, xx, yy, rows):
    rho = np.hypot(xx, yy)
    lat = -math.pi / 2 + 2 * np.arctan(rho / (2 * MOON_RADIUS_M))
    lon = np.arctan2(xx, yy)
    r = (MOON_RADIUS_M + base) / 1000.0
    up = np.stack([np.cos(lat) * np.cos(lon), np.cos(lat) * np.sin(lon), np.sin(lat)], axis=-1).reshape(-1, 3)
    north = np.stack([-np.sin(lat) * np.cos(lon), -np.sin(lat) * np.sin(lon), np.cos(lat)], axis=-1).reshape(-1, 3)
    east = np.stack([-np.sin(lon), np.cos(lon), np.zeros_like(lon)], axis=-1).reshape(-1, 3)
    position = up * r.reshape(-1, 1)
    phi_north = 90.0 - np.degrees(lon).reshape(-1)
    flat = hz.reshape(AZIMUTHS, -1)
    count = flat.shape[1]
    columns = np.arange(count)
    sun_sum = np.zeros(count, dtype=np.float64)
    earth_sum = np.zeros(count, dtype=np.float64)
    sun_max = np.zeros(count, dtype=np.float32)

    def observe(target):
        v = target[None, :] - position
        v /= np.linalg.norm(v, axis=1, keepdims=True)
        el = np.degrees(np.arcsin(np.clip(np.einsum("ij,ij->i", v, up), -1, 1)))
        az = np.degrees(np.arctan2(np.einsum("ij,ij->i", v, east), np.einsum("ij,ij->i", v, north)))
        phi = np.mod(phi_north - az, 360.0)
        k0 = np.floor(phi).astype(np.int64) % AZIMUTHS
        w = (phi - np.floor(phi)).astype(np.float32)
        h = flat[k0, columns].astype(np.float32) * (1 - w) + flat[(k0 + 1) % AZIMUTHS, columns].astype(np.float32) * w
        return el - h

    start = time.time()
    for i, row in enumerate(rows):
        sun = row[:3].astype(np.float64)
        radius = np.degrees(np.arcsin(SUN_RADIUS_KM / np.linalg.norm(sun)))
        above = observe(sun)
        visible = above > -radius
        sun_sum += visible
        np.maximum(sun_max, visible.astype(np.float32), out=sun_max)
        earth_sum += observe(row[3:].astype(np.float64)) > 0
        if i % 500 == 499:
            print(f"step {i + 1}/{len(rows)} {time.time() - start:.0f}s", flush=True)
    return sun_sum, earth_sum, sun_max, len(rows)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dem", required=True)
    parser.add_argument("--data-dir", required=True)
    parser.add_argument("--out-dir", required=True)
    parser.add_argument("--grid", type=int, default=608)
    parser.add_argument("--slope-size", type=int, default=2048)
    parser.add_argument("--stride", type=int, default=4)
    parser.add_argument("--observer-height", type=float, default=2.0)
    parser.add_argument("--cache", required=True)
    parser.add_argument("--parts", type=int, default=4)
    parser.add_argument("--part", type=int)
    args = parser.parse_args()
    out = Path(args.out_dir)
    data = Path(args.data_dir)

    xx, yy = observer_grid(args.grid, 304000.0)
    cache = Path(args.cache)
    if not cache.exists():
        dem, pixel, half = load_dem(args.dem)
        slope = slope_map(dem, pixel, args.slope_size)
        Image.fromarray(np.clip(np.round(slope * 4), 0, 255).astype(np.uint8), mode="L").save(out / "pole_slope.jpg", quality=92, optimize=True)
        del slope
        coarse = block_mean(dem, 3).astype(np.float32)
        del dem
        xx, yy = observer_grid(args.grid, half)
        hz, base = horizons(coarse, pixel * 3, half, xx, yy, args.observer_height)
        np.savez(cache, hz=hz, base=base)
        return

    stored = np.load(cache)
    hz, base = stored["hz"], stored["base"]
    if args.part is not None:
        manifest = json.loads((data / "manifest.json").read_text(encoding="utf-8"))
        ephemeris = np.fromfile(data / "ephemeris.bin", dtype="<f4").reshape(manifest["gridCount"], 6)
        rows = ephemeris[:: args.stride][args.part :: args.parts]
        sun_sum, earth_sum, sun_max, count = fractions(hz, base, xx, yy, rows)
        np.savez(cache.with_name(f"part{args.part}.npz"), sun=sun_sum, earth=earth_sum, peak=sun_max, count=count)
        return

    parts = [np.load(cache.with_name(f"part{k}.npz")) for k in range(args.parts)]
    count = sum(int(part["count"]) for part in parts)
    sun = (sum(part["sun"] for part in parts) / count).reshape(xx.shape)
    earth = (sum(part["earth"] for part in parts) / count).reshape(xx.shape)
    sun_max = np.maximum.reduce([part["peak"] for part in parts]).reshape(xx.shape)
    rgb = np.zeros(xx.shape + (3,), dtype=np.uint8)
    rgb[..., 0] = np.clip(np.round(sun * 255), 0, 255)
    rgb[..., 1] = np.clip(np.round(earth * 255), 0, 255)
    rgb[..., 2] = np.where(sun_max <= 0, 255, 0)
    Image.fromarray(rgb, mode="RGB").save(out / "pole_light.png", optimize=True)
    print("no sun", float((sun_max <= 0).mean()), "mean sun", float(sun.mean()), "mean earth", float(earth.mean()))

if __name__ == "__main__":
    main()

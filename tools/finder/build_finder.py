import argparse
import gzip
import json
import math
import struct
import time
from datetime import datetime, timezone
from pathlib import Path

import cv2
import numpy as np
import tifffile
from PIL import Image
from scipy.ndimage import distance_transform_edt

MOON_RADIUS_M = 1737400.0
SUN_RADIUS_KM = 695700.0
AZIMUTHS = 360
TILE_HALF_M = 304000.0
DEM_PIXEL_M = 80.0


def distances_m():
    parts = [
        np.arange(250, 5000, 250),
        np.arange(5000, 20000, 500),
        np.arange(20000, 60000, 1000),
        np.arange(60000, 150000, 2500),
        np.arange(150000, 300001, 5000),
    ]
    return np.concatenate(parts).astype(np.float64)


def load_dem(path):
    dem = tifffile.imread(path).astype(np.float32)
    dem = np.where(np.isfinite(dem), dem, np.float32(np.nanmedian(dem)))
    if np.nanmax(np.abs(dem)) < 50:
        dem *= 1000.0
    return dem


def block_mean(array, factor):
    n = array.shape[0] // factor
    return array[: n * factor, : n * factor].reshape(n, factor, n, factor).mean(axis=(1, 3))


def slope_stats(dem, n, cell_m, half_m):
    per = int(round(cell_m / DEM_PIXEL_M))
    first = int(round((TILE_HALF_M - half_m) / DEM_PIXEL_M))
    p50 = np.zeros((n, n), dtype=np.float32)
    p90 = np.zeros((n, n), dtype=np.float32)
    for i in range(n):
        r0 = first + i * per
        strip = dem[r0 - 1 : r0 + per + 1, first - 1 : first + n * per + 1]
        gy, gx = np.gradient(strip, DEM_PIXEL_M)
        slope = np.degrees(np.arctan(np.hypot(gx, gy)))[1:-1, 1:-1]
        blocks = slope.reshape(per, n, per).transpose(1, 0, 2).reshape(n, per * per)
        p50[i] = np.percentile(blocks, 50, axis=1)
        p90[i] = np.percentile(blocks, 90, axis=1)
    return p50, p90


def observer_grid(n, half_m):
    spacing = 2 * half_m / n
    j = np.arange(n)
    x = -half_m + (j + 0.5) * spacing
    y = half_m - (j + 0.5) * spacing
    return np.meshgrid(x, y)


def horizons(dem, pixel, xx, yy, observer_height):
    col = ((xx + TILE_HALF_M) / pixel - 0.5).astype(np.float32)
    row = ((TILE_HALF_M - yy) / pixel - 0.5).astype(np.float32)
    base = cv2.remap(dem, col, row, cv2.INTER_LINEAR) + observer_height
    result = np.empty((AZIMUTHS,) + xx.shape, dtype=np.float32)
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
        result[k] = np.degrees(np.arctan(best))
        if k % 60 == 59:
            print(f"azimuth {k + 1}/{AZIMUTHS} {time.time() - start:.0f}s", flush=True)
    return result, base


def visibility(hz, base, xx, yy, rows):
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

    def above(target):
        v = target[None, :] - position
        v /= np.linalg.norm(v, axis=1, keepdims=True)
        el = np.degrees(np.arcsin(np.clip(np.einsum("ij,ij->i", v, up), -1, 1)))
        az = np.degrees(np.arctan2(np.einsum("ij,ij->i", v, east), np.einsum("ij,ij->i", v, north)))
        phi = np.mod(phi_north - az, 360.0)
        k0 = np.floor(phi).astype(np.int64) % AZIMUTHS
        w = phi - np.floor(phi)
        h = flat[k0, columns] * (1 - w) + flat[(k0 + 1) % AZIMUTHS, columns] * w
        return el - h

    sun = np.zeros((len(rows), count), dtype=bool)
    earth = np.zeros((len(rows), count), dtype=bool)
    for i, row in enumerate(rows):
        s = row[:3].astype(np.float64)
        radius = np.degrees(np.arcsin(SUN_RADIUS_KM / np.linalg.norm(s)))
        sun[i] = above(s) > -radius
        earth[i] = above(row[3:].astype(np.float64)) > 0
    return sun, earth, lat, lon


def water_distance_km(diviner_path, xx, yy, max_depth_m):
    raw = Path(diviner_path).read_bytes()
    n, half, _unit = struct.unpack_from("<iff", raw, 0)
    planes = np.frombuffer(raw, dtype=np.uint8, offset=12).reshape(3, n, n)
    depth = planes[2].astype(np.float32) * 0.02
    ice = (planes[2] != 255) & (depth <= max_depth_m)
    pixel_km = 2 * half / n / 1000.0
    dist = distance_transform_edt(~ice) * pixel_km
    col = ((xx + half) / (2 * half) * n - 0.5).astype(np.float32)
    row = ((half - yy) / (2 * half) * n - 0.5).astype(np.float32)
    return cv2.remap(dist.astype(np.float32), col, row, cv2.INTER_LINEAR, borderMode=cv2.BORDER_REPLICATE)


def hillshade(dem, half_m, size):
    first = int(round((TILE_HALF_M - half_m) / DEM_PIXEL_M))
    span = int(round(2 * half_m / DEM_PIXEL_M))
    crop = dem[first : first + span, first : first + span]
    small = cv2.resize(crop, (size, size), interpolation=cv2.INTER_AREA)
    pixel = 2 * half_m / size
    gy, gx = np.gradient(small, pixel)
    azimuth = math.radians(315.0)
    altitude = math.radians(35.0)
    slope = np.arctan(np.hypot(gx, gy) * 2.0)
    aspect = np.arctan2(-gx, gy)
    shade = np.sin(altitude) * np.cos(slope) + np.cos(altitude) * np.sin(slope) * np.cos(azimuth - aspect)
    shade = np.clip(shade, 0, 1)
    return (shade * 200 + 20).astype(np.uint8)


def pack(bits):
    return np.packbits(bits.T, axis=1, bitorder="little")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dem", required=True)
    parser.add_argument("--data-dir", required=True)
    parser.add_argument("--img-dir", required=True)
    parser.add_argument("--cache", required=True)
    parser.add_argument("--cells", type=int, default=184)
    parser.add_argument("--cell-m", type=float, default=2000.0)
    parser.add_argument("--domain-lat", type=float, default=-84.0)
    parser.add_argument("--stride", type=int, default=12)
    parser.add_argument("--observer-height", type=float, default=2.0)
    parser.add_argument("--ice-depth", type=float, default=0.0)
    args = parser.parse_args()
    data = Path(args.data_dir)
    n = args.cells
    half_m = n * args.cell_m / 2
    xx, yy = observer_grid(n, half_m)
    cache = Path(args.cache)

    if not cache.exists():
        dem = load_dem(args.dem)
        p50, p90 = slope_stats(dem, n, args.cell_m, half_m)
        shade = hillshade(dem, half_m, 736)
        coarse = block_mean(dem, 3).astype(np.float32)
        del dem
        hz, base = horizons(coarse, DEM_PIXEL_M * 3, xx, yy, args.observer_height)
        np.savez(cache, hz=hz.astype(np.float16), base=base, p50=p50, p90=p90, shade=shade)
        print("cache written", flush=True)
        return

    stored = np.load(cache)
    hz = stored["hz"].astype(np.float32)
    manifest = json.loads((data / "manifest.json").read_text(encoding="utf-8"))
    ephemeris = np.fromfile(data / "ephemeris.bin", dtype="<f4").reshape(manifest["gridCount"], 6)
    rows = ephemeris[:: args.stride]
    sun, earth, lat, _lon = visibility(hz, stored["base"], xx, yy, rows)
    steps = len(rows)
    domain = (np.degrees(lat) <= args.domain_lat).reshape(-1)
    water = water_distance_km(data / "pole_diviner.bin", xx, yy, args.ice_depth).reshape(-1)
    start = datetime.strptime(manifest["gridStartUtc"], "%Y-%m-%dT%H:%M:%SZ").replace(tzinfo=timezone.utc)
    header = b"SLFD" + struct.pack("<iiffiiqf", 1, n, args.cell_m, half_m, steps, manifest["gridStepMinutes"] * args.stride, int(start.timestamp()), args.domain_lat)
    planes = [
        domain.astype(np.uint8),
        np.clip(np.round(stored["p50"].reshape(-1) * 4), 0, 255).astype(np.uint8),
        np.clip(np.round(stored["p90"].reshape(-1) * 4), 0, 255).astype(np.uint8),
        np.clip(np.round(water), 0, 255).astype(np.uint8),
        np.clip(np.round(sun.mean(axis=0) * 255), 0, 255).astype(np.uint8),
        np.clip(np.round(earth.mean(axis=0) * 255), 0, 255).astype(np.uint8),
    ]
    body = header + b"".join(p.tobytes() for p in planes) + pack(sun).tobytes() + pack(earth).tobytes()
    (data / "finder.bin.gz").write_bytes(gzip.compress(body, 9))
    img = Path(args.img_dir)
    img.mkdir(parents=True, exist_ok=True)
    Image.fromarray(stored["shade"], mode="L").save(img / "basemap.jpg", quality=82, optimize=True)
    inside = domain.astype(bool)
    print(json.dumps({
        "cells": int(inside.sum()),
        "steps": steps,
        "bytes": len(body),
        "gz": (data / "finder.bin.gz").stat().st_size,
        "sunMean": float(sun.mean(axis=0)[inside].mean()),
        "earthMean": float(earth.mean(axis=0)[inside].mean()),
        "waterKmMedian": float(np.median(water[inside])),
        "p90Median": float(np.median(stored["p90"].reshape(-1)[inside])),
    }), flush=True)


if __name__ == "__main__":
    main()

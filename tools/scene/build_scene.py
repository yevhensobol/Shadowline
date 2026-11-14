import argparse
import math
import struct
from pathlib import Path

import numpy as np
from PIL import Image

Image.MAX_IMAGE_PIXELS = None

MOON_RADIUS_M = 1737400.0
POLE_HALF_M = 304000.0
POLE_UNIT_M = 0.5


def save_jpg(image, path, size, quality=86):
    image.resize(size, Image.LANCZOS).save(path, quality=quality, optimize=True, progressive=True)


def moon_color(source, outdir):
    image = Image.open(source).convert("RGB")
    save_jpg(image, outdir / "moon_color_8k.jpg", (8192, 4096), 84)
    save_jpg(image, outdir / "moon_color_4k.jpg", (4096, 2048), 86)


def polar_lonlat(n, half_m):
    ys, xs = np.mgrid[0:n, 0:n].astype(np.float64)
    x = (xs + 0.5) / n * 2 * half_m - half_m
    y = half_m - (ys + 0.5) / n * 2 * half_m
    rho = np.hypot(x, y)
    lat = -90 + np.degrees(2 * np.arctan(rho / (2 * MOON_RADIUS_M)))
    lon = np.degrees(np.arctan2(x, y))
    return lat, lon


def sample_equirect(source, lat, lon):
    height, width = source.shape[:2]
    cols = np.mod((lon + 180) / 360 * width - 0.5, width)
    rows = np.clip((90 - lat) / 180 * height - 0.5, 0, height - 1)
    c0 = np.floor(cols).astype(int)
    r0 = np.floor(rows).astype(int)
    c1 = (c0 + 1) % width
    r1 = np.minimum(r0 + 1, height - 1)
    fc = cols - c0
    fr = rows - r0
    if source.ndim == 3:
        fc = fc[..., None]
        fr = fr[..., None]
    top = source[r0, c0] * (1 - fc) + source[r0, c1] * fc
    bottom = source[r1, c0] * (1 - fc) + source[r1, c1] * fc
    return top * (1 - fr) + bottom * fr


def pole_tile(dem_path, color_path, outdir, n_mesh, n_tex):
    import rasterio

    with rasterio.open(dem_path) as dataset:
        dem = dataset.read(1)
    dem = np.where(np.isfinite(dem), dem, np.nanmedian(dem)).astype(np.float64)
    factor = dem.shape[0] // n_mesh
    cropped = dem[: n_mesh * factor, : n_mesh * factor]
    heights = cropped.reshape(n_mesh, factor, n_mesh, factor).mean(axis=(1, 3))
    units = np.clip(np.round(heights / POLE_UNIT_M), -32767, 32767).astype("<i2")
    with open(outdir / "pole_height.bin", "wb") as handle:
        handle.write(struct.pack("<iff", n_mesh, POLE_HALF_M, POLE_UNIT_M))
        handle.write(units.tobytes())
    color = np.asarray(Image.open(color_path).convert("RGB"), dtype=np.float32)
    lat, lon = polar_lonlat(n_tex, POLE_HALF_M)
    albedo = sample_equirect(color, lat, lon)
    Image.fromarray(np.clip(albedo, 0, 255).astype(np.uint8)).save(outdir / "pole_albedo.jpg", quality=86, optimize=True, progressive=True)


def moon_normal(ldem_path, outdir, width):
    import rasterio

    with rasterio.open(ldem_path) as dataset:
        km = dataset.read(1).astype(np.float32)
    km = np.asarray(Image.fromarray(km, mode="F").resize((width, width // 2), Image.BILINEAR), dtype=np.float64)
    rows, cols = km.shape
    lat = np.radians(90 - (np.arange(rows) + 0.5) / rows * 180)[:, None]
    dlon = 2 * math.pi / cols
    dlat = math.pi / rows
    radius = MOON_RADIUS_M / 1000.0
    dx = (np.roll(km, -1, axis=1) - np.roll(km, 1, axis=1)) / (2 * radius * np.maximum(np.cos(lat), 0.02) * dlon)
    up = np.vstack([km[:1], km[:-1]])
    down = np.vstack([km[1:], km[-1:]])
    dy = (up - down) / (2 * radius * dlat)
    nx = -dx
    ny = -dy
    nz = np.ones_like(km)
    length = np.sqrt(nx * nx + ny * ny + nz * nz)
    rgb = np.stack([nx / length, ny / length, nz / length], axis=-1) * 0.5 + 0.5
    Image.fromarray(np.clip(rgb * 255, 0, 255).astype(np.uint8)).save(outdir / "moon_normal_4k.jpg", quality=92, optimize=True)


def earth(day, night, clouds, outdir):
    if day:
        image = Image.open(day)
        image.draft("RGB", (8192, 4096))
        save_jpg(image.convert("RGB"), outdir / "earth_day_4k.jpg", (4096, 2048), 86)
    if night:
        image = Image.open(night)
        image.draft("RGB", (8192, 4096))
        save_jpg(image.convert("RGB"), outdir / "earth_night_4k.jpg", (4096, 2048), 84)
    if clouds:
        image = Image.open(clouds).convert("L")
        image.resize((2048, 1024), Image.LANCZOS).save(outdir / "earth_clouds_2k.jpg", quality=86, optimize=True)


def stars(exr_path, outdir, width):
    import OpenEXR

    with OpenEXR.File(str(exr_path)) as handle:
        linear = np.asarray(handle.parts[0].channels["RGB"].pixels, dtype=np.float32)
    srgb = np.where(linear <= 0.0031308, 12.92 * linear, 1.055 * np.power(np.maximum(linear, 0), 1 / 2.4) - 0.055)
    rgb = Image.fromarray(np.clip(srgb * 255, 0, 255).astype(np.uint8))
    if rgb.width != width:
        rgb = rgb.resize((width, width // 2), Image.LANCZOS)
    rgb.save(outdir / "stars_4k.jpg", quality=90, optimize=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("outdir")
    parser.add_argument("--color")
    parser.add_argument("--pole-dem")
    parser.add_argument("--ldem")
    parser.add_argument("--earth-day")
    parser.add_argument("--earth-night")
    parser.add_argument("--clouds")
    parser.add_argument("--stars")
    parser.add_argument("--pole-mesh", type=int, default=760)
    parser.add_argument("--pole-texture", type=int, default=2048)
    args = parser.parse_args()
    outdir = Path(args.outdir)
    outdir.mkdir(parents=True, exist_ok=True)
    if args.color:
        moon_color(args.color, outdir)
    if args.pole_dem and args.color:
        pole_tile(args.pole_dem, args.color, outdir, args.pole_mesh, args.pole_texture)
    if args.ldem:
        moon_normal(args.ldem, outdir, 4096)
    if args.earth_day or args.earth_night or args.clouds:
        earth(args.earth_day, args.earth_night, args.clouds, outdir)
    if args.stars:
        stars(args.stars, outdir, 4096)


if __name__ == "__main__":
    main()

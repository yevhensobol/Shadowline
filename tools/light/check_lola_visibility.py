import argparse
import json
import math
import re
import urllib.request
from datetime import date
from pathlib import Path

import numpy as np
from PIL import Image

BASE = "https://pds-geosciences.wustl.edu/lro/lro-l-lola-3-rdr-v1/lrolol_1xxx/extras/illumination/img/"
PRODUCTS = {
    "sun240": "avgvisib_65s_240m_201608",
    "earth240": "avgvisib_65s_240m_201608_earth",
    "sun060": "avgvisib_85s_060m_201608",
    "earth060": "avgvisib_85s_060m_201608_earth",
}
MOON_KM = 1737.4
SUN_KM = 695700.0
EARTH_KM = 6371.0


def fetch(name, cache):
    for suffix in (".lbl", ".img"):
        path = cache / (name + suffix)
        if not path.exists():
            request = urllib.request.Request(BASE + name + suffix, headers={"User-Agent": "Shadowline data pipeline"})
            with urllib.request.urlopen(request, timeout=600) as response, open(path.with_suffix(suffix + ".part"), "wb") as out:
                while True:
                    chunk = response.read(1 << 20)
                    if not chunk:
                        break
                    out.write(chunk)
            path.with_suffix(suffix + ".part").rename(path)
            print(f"downloaded {path.name}")


def number(label, key):
    match = re.search(rf"^\s*{key}\s*=\s*\"?(-?[0-9.]+)", label, re.MULTILINE)
    if not match:
        raise SystemExit(f"{key} missing in label")
    return float(match.group(1))


class Product:
    def __init__(self, name, cache):
        label = (cache / (name + ".lbl")).read_text(encoding="ascii", errors="replace")
        if "POLAR STEREOGRAPHIC" not in label or "LSB_INTEGER" not in label:
            raise SystemExit(f"{name}: unexpected projection or sample type")
        self.lines = int(number(label, "LINES"))
        self.samples = int(number(label, "LINE_SAMPLES"))
        self.scale_m = number(label, "MAP_SCALE") * 1000 if number(label, "MAP_SCALE") < 10 else number(label, "MAP_SCALE")
        self.line0 = number(label, "LINE_PROJECTION_OFFSET")
        self.sample0 = number(label, "SAMPLE_PROJECTION_OFFSET")
        self.factor = number(label, "SCALING_FACTOR")
        self.radius_km = number(label, "A_AXIS_RADIUS")
        raw = np.fromfile(cache / (name + ".img"), dtype="<i2")
        if raw.size != self.lines * self.samples:
            raise SystemExit(f"{name}: size {raw.size} differs from label")
        self.data = raw.reshape(self.lines, self.samples).astype(np.float32) * self.factor
        self.name = name

    def sample(self, x_m, y_m):
        cols = self.sample0 + x_m / self.scale_m
        rows = self.line0 - y_m / self.scale_m
        inside = (cols >= 0) & (cols <= self.samples - 1.001) & (rows >= 0) & (rows <= self.lines - 1.001)
        c = np.clip(cols, 0, self.samples - 1.001)
        r = np.clip(rows, 0, self.lines - 1.001)
        c0 = np.floor(c).astype(int)
        r0 = np.floor(r).astype(int)
        fc = c - c0
        fr = r - r0
        d = self.data
        value = (d[r0, c0] * (1 - fc) + d[r0, c0 + 1] * fc) * (1 - fr) + (d[r0 + 1, c0] * (1 - fc) + d[r0 + 1, c0 + 1] * fc) * fr
        return np.where(inside, value, np.nan)


def stereo(lat_deg, lon_deg):
    rho = 2 * MOON_KM * 1000 * math.tan(math.radians(90 + lat_deg) / 2)
    return rho * math.sin(math.radians(lon_deg)), rho * math.cos(math.radians(lon_deg))


def load_ephemeris(data):
    years = json.loads((data / "years.json").read_text(encoding="utf-8"))["years"]
    blocks = []
    for year in years:
        raw = np.fromfile(data / year["file"], dtype="<f4")
        blocks.append(raw.reshape(year["gridCount"], 6).astype(np.float64))
    return np.vstack(blocks), [year["id"] for year in years]


def shares(ephemeris, lat_deg, lon_deg, horizon):
    lat = math.radians(lat_deg)
    lon = math.radians(lon_deg)
    up = np.array([math.cos(lat) * math.cos(lon), math.cos(lat) * math.sin(lon), math.sin(lat)])
    north = np.array([-math.sin(lat) * math.cos(lon), -math.sin(lat) * math.sin(lon), math.cos(lat)])
    east = np.array([-math.sin(lon), math.cos(lon), 0.0])
    result = {}
    for key, columns, body_km in (("sun", slice(0, 3), SUN_KM), ("earth", slice(3, 6), EARTH_KM)):
        v = ephemeris[:, columns] - up * MOON_KM
        distance = np.linalg.norm(v, axis=1)
        u = v / distance[:, None]
        elevation = np.degrees(np.arcsin(np.clip(u @ up, -1, 1)))
        azimuth = np.degrees(np.arctan2(u @ east, u @ north)) % 360.0
        k = azimuth / (360.0 / horizon.size)
        k0 = np.floor(k).astype(int) % horizon.size
        w = k - np.floor(k)
        terrain = horizon[k0] * (1 - w) + horizon[(k0 + 1) % horizon.size] * w
        radius = np.degrees(np.arcsin(body_km / distance))
        result[key + "Any"] = float((elevation + radius > terrain).mean())
        result[key + "Centre"] = float((elevation > terrain).mean())
    return result


def stats(ours, lola):
    ok = np.isfinite(ours) & np.isfinite(lola)
    a = ours[ok]
    b = lola[ok]
    d = a - b
    return {
        "cells": int(ok.sum()),
        "meanDiff": round(float(d.mean()), 4),
        "medianDiff": round(float(np.median(d)), 4),
        "mae": round(float(np.abs(d).mean()), 4),
        "rmse": round(float(np.sqrt((d ** 2).mean())), 4),
        "r": round(float(np.corrcoef(a, b)[0, 1]), 4),
        "within005": round(float((np.abs(d) <= 0.05).mean()), 4),
        "within010": round(float((np.abs(d) <= 0.10).mean()), 4),
        "p05": round(float(np.percentile(d, 5)), 4),
        "p95": round(float(np.percentile(d, 95)), 4),
        "histogram": np.histogram(d, bins=np.linspace(-0.5, 0.5, 41))[0].tolist(),
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--cache", required=True)
    parser.add_argument("--data-dir", required=True)
    parser.add_argument("--scene-dir", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--diff-png")
    args = parser.parse_args()

    cache = Path(args.cache)
    cache.mkdir(parents=True, exist_ok=True)
    for name in PRODUCTS.values():
        fetch(name, cache)
    lola = {key: Product(name, cache) for key, name in PRODUCTS.items()}
    data = Path(args.data_dir)

    light = np.asarray(Image.open(Path(args.scene_dir) / "pole_light.png").convert("RGB"), dtype=np.float32)
    n = light.shape[0]
    half = 304000.0
    sub = 4
    offsets = (np.arange(sub) + 0.5) / sub
    centres = (np.arange(n)[:, None] + offsets[None, :]).ravel() / n * 2 * half - half
    xs, ys = np.meshgrid(centres, -centres)
    maps = {}
    for key in ("sun240", "earth240"):
        fine = lola[key].sample(xs, ys)
        maps[key] = fine.reshape(n, sub, n, sub).mean(axis=(1, 3))
    ours_sun = light[..., 0] / 255
    ours_earth = light[..., 1] / 255
    never = light[..., 2] > 127

    variants = {}
    for k in range(4):
        for flip in (False, True):
            image = np.rot90(maps["sun240"][:, ::-1] if flip else maps["sun240"], k)
            ok = np.isfinite(image)
            variants[f"rot{k * 90}{'+flip' if flip else ''}"] = round(float(np.corrcoef(ours_sun[ok], image[ok])[0, 1]), 4)
    best = max(variants, key=variants.get)
    print("orientation", variants)
    if best != "rot0":
        raise SystemExit(f"orientation check failed: best match {best}")

    lola_never = maps["sun240"] <= 0.001
    psr = {
        "bothShadow": int((never & lola_never).sum()),
        "oursOnly": int((never & ~lola_never).sum()),
        "lolaOnly": int((~never & lola_never).sum()),
        "bothLit": int((~never & ~lola_never).sum()),
    }

    ephemeris, years = load_ephemeris(data)
    catalog = json.loads((data / "catalog.json").read_text(encoding="utf-8"))["sites"]
    sites = []
    for site in catalog:
        lat = site["latitudeDeg"]
        lon = (site["longitudeEastDeg"] + 180) % 360 - 180
        path = data / "horizon" / f"{site['id']}.bin"
        if lat > -65 or not path.exists():
            continue
        horizon = np.fromfile(path, dtype="<f4").astype(np.float64)
        ours = shares(ephemeris, lat, lon, horizon)
        x, y = stereo(lat, lon)
        fine = lat <= -85.2
        sun_product = lola["sun060" if fine else "sun240"]
        earth_product = lola["earth060" if fine else "earth240"]
        px = np.array([x + dx * sun_product.scale_m for dx in (-1, 0, 1) for dy in (-1, 0, 1)])
        py = np.array([y + dy * sun_product.scale_m for dx in (-1, 0, 1) for dy in (-1, 0, 1)])
        sun_near = sun_product.sample(px, py)
        earth_near = earth_product.sample(px, py)
        sites.append({
            "id": site["id"],
            "latitudeDeg": lat,
            "longitudeEastDeg": lon,
            "lolaResolutionM": int(sun_product.scale_m),
            "ours": {key: round(value, 4) for key, value in ours.items()},
            "lola": {
                "sun": round(float(sun_near[4]), 4),
                "sunMin": round(float(np.nanmin(sun_near)), 4),
                "sunMax": round(float(np.nanmax(sun_near)), 4),
                "earth": round(float(earth_near[4]), 4),
                "earthMin": round(float(np.nanmin(earth_near)), 4),
                "earthMax": round(float(np.nanmax(earth_near)), 4),
            },
        })
        print(f"{site['id']}: sun ours {ours['sunAny']:.3f} lola {sun_near[4]:.3f} [{np.nanmin(sun_near):.3f}-{np.nanmax(sun_near):.3f}], earth ours centre {ours['earthCentre']:.3f} any {ours['earthAny']:.3f} lola {earth_near[4]:.3f}")

    result = {
        "formatVersion": 1,
        "retrieved": date.today().isoformat(),
        "source": {
            "products": list(PRODUCTS.values()),
            "url": BASE,
            "citation": "Mazarico, E., Neumann, G. A., Smith, D. E., Zuber, M. T., Torrence, M. H. (2011). Illumination conditions of the lunar polar regions using LOLA topography. Icarus 211, 1066-1081. doi:10.1016/j.icarus.2010.10.030",
            "definition": "Share of time steps with any part of the Sun or Earth disc visible, LOLA topography, polar stereographic, 1737.4 km sphere",
        },
        "ours": {
            "map": "img/scene/pole_light.png, 608 x 608, 1 km, one year from the manifest start",
            "sites": f"site horizons, ephemeris years {', '.join(years)}",
        },
        "orientation": variants,
        "map": {
            "sun": stats(ours_sun.ravel(), maps["sun240"].ravel()),
            "sunLit": stats(ours_sun[maps["sun240"] > 0.05], maps["sun240"][maps["sun240"] > 0.05]),
            "earthCentreVsAny": stats(ours_earth.ravel(), maps["earth240"].ravel()),
            "psrCells": psr,
        },
        "sites": sites,
    }
    Path(args.out).write_text(json.dumps(result, indent=1) + "\n", encoding="utf-8")
    print(json.dumps({key: {k: v for k, v in value.items() if k != "histogram"} if isinstance(value, dict) else value for key, value in result["map"].items()}, indent=1))

    if args.diff_png:
        diff = np.nan_to_num(ours_sun - maps["sun240"])
        rgb = np.zeros((n, n, 3), dtype=np.uint8)
        rgb[..., 0] = np.clip(diff, 0, 0.5) / 0.5 * 255
        rgb[..., 2] = np.clip(-diff, 0, 0.5) / 0.5 * 255
        rgb[..., 1] = np.clip(maps["sun240"], 0, 1) * 60
        Image.fromarray(rgb).save(args.diff_png)


if __name__ == "__main__":
    main()

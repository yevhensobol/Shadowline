import struct

import numpy as np

MOON_RADIUS_M = 1737400.0
UNIT_M = 0.5
INT16_MAX = 32767


def destination(lat_deg, lon_deg, bearing_rad, distance_m, radius_m=MOON_RADIUS_M):
    lat = np.radians(lat_deg)
    lon = np.radians(lon_deg)
    delta = distance_m / radius_m
    lat2 = np.arcsin(np.sin(lat) * np.cos(delta) + np.cos(lat) * np.sin(delta) * np.cos(bearing_rad))
    lon2 = lon + np.arctan2(
        np.sin(bearing_rad) * np.sin(delta) * np.cos(lat),
        np.cos(delta) - np.sin(lat) * np.sin(lat2),
    )
    return np.degrees(lat2), np.degrees(lon2)


def build_grid(site_lat, site_lon, sample, n, spacing_m, radius_m=MOON_RADIUS_M):
    half = (n - 1) / 2.0
    cols, rows = np.meshgrid(np.arange(n), np.arange(n))
    east = (cols - half) * spacing_m
    north = (half - rows) * spacing_m
    distance = np.hypot(east, north)
    bearing = np.arctan2(east, north)
    lat, lon = destination(site_lat, site_lon, bearing, distance, radius_m)
    lat = np.where(distance == 0, site_lat, lat)
    lon = np.where(distance == 0, site_lon, lon)
    heights = sample(lat, lon)
    centre = heights[int(half), int(half)]
    return heights - centre - distance ** 2 / (2.0 * radius_m)


def encode(grid, spacing_m):
    n = grid.shape[0]
    filled = np.where(np.isnan(grid), 0.0, grid)
    units = np.clip(np.round(filled / UNIT_M), -INT16_MAX, INT16_MAX).astype("<i2")
    return struct.pack("<iff", n, spacing_m, UNIT_M) + units.tobytes()


def decode(data):
    n, spacing_m, unit = struct.unpack_from("<iff", data, 0)
    units = np.frombuffer(data, dtype="<i2", count=n * n, offset=12).reshape(n, n)
    return units.astype(np.float64) * unit, spacing_m

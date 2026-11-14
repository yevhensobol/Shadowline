import numpy as np

SAMPLES = 720
MOON_RADIUS_M = 1_737_400.0
CHUNK_SAMPLES = 4_000_000


def _bilinear(elevation, rows, cols):
    height, width = elevation.shape
    r0 = np.floor(rows).astype(np.int64)
    c0 = np.floor(cols).astype(np.int64)
    inside = (r0 >= 0) & (c0 >= 0) & (r0 + 1 < height) & (c0 + 1 < width)
    r0c = np.clip(r0, 0, height - 2)
    c0c = np.clip(c0, 0, width - 2)
    fr = rows - r0c
    fc = cols - c0c
    top = elevation[r0c, c0c] * (1 - fc) + elevation[r0c, c0c + 1] * fc
    bottom = elevation[r0c + 1, c0c] * (1 - fc) + elevation[r0c + 1, c0c + 1] * fc
    values = top * (1 - fr) + bottom * fr
    return np.where(inside, values, np.nan)


def horizon_profile(
    elevation,
    pixel_m,
    row,
    col,
    grid_north_deg=0.0,
    observer_height_m=0.0,
    moon_radius_m=MOON_RADIUS_M,
    max_range_m=200_000.0,
    samples=SAMPLES,
    min_range_m=0.0,
):
    dem = np.asarray(elevation, dtype=np.float64)
    if dem.ndim != 2 or dem.shape[0] < 2 or dem.shape[1] < 2:
        raise ValueError("elevation must be a 2D array of at least 2x2")
    if pixel_m <= 0 or samples <= 0:
        raise ValueError("pixel_m and samples must be positive")

    base = _bilinear(dem, np.array([float(row)]), np.array([float(col)]))[0]
    if not np.isfinite(base):
        raise ValueError("observer position is outside the data or on a no-data cell")
    observer_z = base + observer_height_m

    diagonal_steps = int(np.ceil(np.hypot(*dem.shape)))
    steps = max(1, min(int(max_range_m / pixel_m), diagonal_steps))
    distances = np.arange(1, steps + 1, dtype=np.float64) * pixel_m
    curvature = distances * distances / (2.0 * moon_radius_m)

    azimuths = np.arange(samples, dtype=np.float64) * (360.0 / samples)
    angles = np.radians(azimuths + grid_north_deg)
    d_row = -np.cos(angles)
    d_col = np.sin(angles)

    result = np.zeros(samples, dtype=np.float64)
    per_chunk = max(1, CHUNK_SAMPLES // steps)
    step_index = np.arange(1, steps + 1, dtype=np.float64)

    for start in range(0, samples, per_chunk):
        stop = min(samples, start + per_chunk)
        rows = row + d_row[start:stop, None] * step_index[None, :]
        cols = col + d_col[start:stop, None] * step_index[None, :]
        heights = _bilinear(dem, rows, cols)
        angle = np.arctan2(heights - observer_z - curvature[None, :], distances[None, :])
        angle = np.where(np.isfinite(heights) & (distances[None, :] >= min_range_m), angle, -np.inf)
        best = angle.max(axis=1)
        result[start:stop] = np.where(np.isfinite(best), np.degrees(best), -90.0 if min_range_m > 0 else 0.0)

    return np.clip(result, -90.0, 90.0).astype(np.float32)


def write_profile(path, profile):
    values = np.asarray(profile, dtype="<f4")
    if values.shape != (SAMPLES,):
        raise ValueError(f"profile must have {SAMPLES} values")
    values.tofile(path)

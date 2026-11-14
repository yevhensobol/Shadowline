import numpy as np

MOON_RADIUS_M = 1737400.0
RAYS = 1440
STEP_M = 80.0
RANGE_M = 22000.0
MAST_M = 4.0
RECEIVER_M = 1.5


class Dem:
    def __init__(self, heights, spacing, half):
        self.h = heights
        self.spacing = spacing
        self.half = half
        self.size = heights.shape[0]

    def sample(self, x, y):
        col = (x + self.half) / self.spacing - 0.5
        row = (self.half - y) / self.spacing - 0.5
        c0 = np.clip(np.floor(col).astype(np.int64), 0, self.size - 2)
        r0 = np.clip(np.floor(row).astype(np.int64), 0, self.size - 2)
        fc = np.clip(col - c0, 0, 1)
        fr = np.clip(row - r0, 0, 1)
        h = self.h
        top = h[r0, c0] * (1 - fc) + h[r0, c0 + 1] * fc
        bottom = h[r0 + 1, c0] * (1 - fc) + h[r0 + 1, c0 + 1] * fc
        return top * (1 - fr) + bottom * fr


def viewshed(dem, x0, y0, mast=MAST_M, receiver=RECEIVER_M, rays=RAYS, step=STEP_M, rng=RANGE_M):
    rho = np.hypot(x0, y0)
    nx, ny = (x0 / rho, y0 / rho) if rho > 1 else (0.0, 1.0)
    ex, ey = ny, -nx
    az = np.arange(rays) * 2 * np.pi / rays
    dx = np.cos(az) * nx + np.sin(az) * ex
    dy = np.cos(az) * ny + np.sin(az) * ey
    d = np.arange(1, int(round(rng / step)) + 1) * step
    xs = x0 + dx[:, None] * d[None, :]
    ys = y0 + dy[:, None] * d[None, :]
    base = float(dem.sample(np.array([x0]), np.array([y0]))[0]) + mast
    drop = d * d / (2 * MOON_RADIUS_M)
    terrain = (dem.sample(xs, ys) - drop[None, :] - base) / d[None, :]
    target = terrain + receiver / d[None, :]
    blocking = np.maximum.accumulate(terrain, axis=1)
    prior = np.concatenate([np.full((rays, 1), -np.inf), blocking[:, :-1]], axis=1)
    return d, target >= prior


def fractions(d, visible, radii=(2000.0, 10000.0, RANGE_M)):
    weight = np.broadcast_to(d[None, :], visible.shape)
    out = []
    for r in radii:
        m = d <= r + 1e-6
        out.append(float((visible[:, m] * weight[:, m]).sum() / weight[:, m].sum()))
    return out


def los_area_km2(d, visible, rng=RANGE_M):
    m = d <= rng + 1e-6
    disc = np.pi * (rng / 1000.0) ** 2
    return fractions(d, visible, (rng,))[0] * disc

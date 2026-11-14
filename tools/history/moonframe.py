import numpy as np
from astropy.time import Time
from jplephem.spk import SPK

C_KM_S = 299792.458


def iau_moon_matrix(days):
    d = np.asarray(days, dtype=np.float64)
    t = d / 36525.0
    rad = np.radians
    e = [None,
         125.045 - 0.0529921 * d, 250.089 - 0.1059842 * d, 260.008 + 13.0120009 * d, 176.625 + 13.3407154 * d,
         357.529 + 0.9856003 * d, 311.589 + 26.4057084 * d, 134.963 + 13.0649930 * d, 276.617 + 0.3287146 * d,
         34.226 + 1.7484877 * d, 15.134 - 0.1589763 * d, 119.743 + 0.0036096 * d, 239.961 + 0.1643573 * d,
         25.053 + 12.9590088 * d]
    s = lambda i: np.sin(rad(e[i]))
    c = lambda i: np.cos(rad(e[i]))
    ra = 269.9949 + 0.0031 * t - 3.8787 * s(1) - 0.1204 * s(2) + 0.0700 * s(3) - 0.0172 * s(4) + 0.0072 * s(6) - 0.0052 * s(10) + 0.0043 * s(13)
    dec = 66.5392 + 0.0130 * t + 1.5419 * c(1) + 0.0239 * c(2) - 0.0278 * c(3) + 0.0068 * c(4) - 0.0029 * c(6) + 0.0009 * c(7) + 0.0008 * c(10) - 0.0009 * c(13)
    w = (38.3213 + 13.17635815 * d - 1.4e-12 * d * d + 3.5610 * s(1) + 0.1208 * s(2) - 0.0642 * s(3) + 0.0158 * s(4) + 0.0252 * s(5)
         - 0.0066 * s(6) - 0.0047 * s(7) - 0.0046 * s(8) + 0.0028 * s(9) + 0.0052 * s(10) + 0.0040 * s(11) + 0.0019 * s(12) - 0.0044 * s(13))

    def rz(a):
        ca, sa = np.cos(a), np.sin(a)
        z, o = np.zeros_like(a), np.ones_like(a)
        return np.stack([np.stack([ca, sa, z], -1), np.stack([-sa, ca, z], -1), np.stack([z, z, o], -1)], -2)

    def rx(a):
        ca, sa = np.cos(a), np.sin(a)
        z, o = np.zeros_like(a), np.ones_like(a)
        return np.stack([np.stack([o, z, z], -1), np.stack([z, ca, sa], -1), np.stack([z, -sa, ca], -1)], -2)

    return rz(rad(w)) @ rx(rad(90 - dec)) @ rz(rad(90 + ra))


def aberrate(vectors, velocity_km_s):
    distance = np.linalg.norm(vectors, axis=0)
    u = vectors / distance
    beta = velocity_km_s / C_KM_S
    dot = np.einsum("ij,ij->j", u, beta)
    apparent = u + beta - dot * u
    apparent /= np.linalg.norm(apparent, axis=0)
    return apparent * distance


class MoonCentred:
    def __init__(self, bsp):
        self.kernel = SPK.open(bsp)

    def _pos(self, centre, target, jd):
        return self.kernel[centre, target].compute(jd)

    def _vel(self, centre, target, jd):
        return self.kernel[centre, target].compute_and_differentiate(jd)[1] / 86400.0

    def vectors(self, utc_iso_list, light_time=True, aberration=True):
        times = Time(utc_iso_list, scale="utc")
        jd = times.tdb.jd
        emb = self._pos(0, 3, jd)
        moon = emb + self._pos(3, 301, jd)
        moon_velocity = self._vel(0, 3, jd) + self._vel(3, 301, jd)

        def body(jd_obs, kind):
            if kind == "sun":
                return self._pos(0, 10, jd_obs)
            return self._pos(0, 3, jd_obs) + self._pos(3, 399, jd_obs)

        result = {}
        for kind in ("sun", "earth"):
            target = body(jd, kind)
            if light_time:
                for _ in range(3):
                    distance = np.linalg.norm(target - moon, axis=0)
                    target = body(jd - distance / C_KM_S / 86400.0, kind)
            relative = target - moon
            if aberration:
                relative = aberrate(relative, moon_velocity)
            result[kind] = relative.T
        days = jd - 2451545.0
        matrix = iau_moon_matrix(days)
        sun = np.einsum("nij,nj->ni", matrix, result["sun"])
        earth = np.einsum("nij,nj->ni", matrix, result["earth"])
        return sun, earth

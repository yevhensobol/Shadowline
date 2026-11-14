import math
import os
import tempfile
import time
import unittest

import numpy as np

from horizon import MOON_RADIUS_M, SAMPLES, horizon_profile, write_profile


def wall_dem(side="north", height=50.0, size=201, distance_px=20, pixel_m=10.0):
    dem = np.zeros((size, size))
    centre = size // 2
    edge = centre - distance_px
    if side == "north":
        dem[: edge + 1, :] = height
    elif side == "south":
        dem[centre + distance_px :, :] = height
    elif side == "east":
        dem[:, centre + distance_px :] = height
    elif side == "west":
        dem[:, : edge + 1] = height
    return dem


def expected_angle(height, distance, radius=MOON_RADIUS_M):
    return math.degrees(math.atan2(height - distance * distance / (2 * radius), distance))


def peak_azimuth(profile):
    return int(np.argmax(profile)) * 360.0 / SAMPLES


class HorizonTests(unittest.TestCase):
    def test_flat_dem_is_zero_everywhere(self):
        profile = horizon_profile(np.zeros((101, 101)), 10.0, 50, 50)

        self.assertTrue(np.all(np.abs(profile) < 0.01))

    def test_wall_to_the_north(self):
        pixel = 10.0
        profile = horizon_profile(wall_dem("north"), pixel, 100, 100)

        self.assertAlmostEqual(float(profile[0]), expected_angle(50.0, 20 * pixel), delta=0.01)
        self.assertAlmostEqual(float(profile[360]), 0.0, delta=0.01)

    def test_grid_north_mapping(self):
        pixel = 10.0
        cases = {
            ("north", 0.0): 0.0,
            ("east", 0.0): 90.0,
            ("south", 0.0): 180.0,
            ("west", 0.0): 270.0,
            ("east", 90.0): 0.0,
            ("east", 180.0): 270.0,
            ("east", 270.0): 180.0,
            ("south", 90.0): 90.0,
        }
        for (side, grid_north), azimuth in cases.items():
            with self.subTest(side=side, grid_north=grid_north):
                profile = horizon_profile(wall_dem(side), pixel, 100, 100, grid_north_deg=grid_north)
                index = int(round(azimuth / 0.5)) % SAMPLES
                self.assertAlmostEqual(float(profile[index]), expected_angle(50.0, 20 * pixel), delta=0.01)

    def test_mountain_peak_azimuth(self):
        size = 301
        rows, cols = np.mgrid[0:size, 0:size]
        centre = size // 2
        dem = 500.0 * np.exp(-(((rows - (centre - 60)) ** 2 + (cols - (centre + 60)) ** 2) / (2 * 15.0**2)))

        profile = horizon_profile(dem, 10.0, centre, centre)

        self.assertAlmostEqual(peak_azimuth(profile), 45.0, delta=2.0)
        self.assertGreater(float(profile.max()), 10.0)
        self.assertTrue(np.all(np.isfinite(profile)))

    def test_constant_offset_does_not_change_the_profile(self):
        size = 201
        rows, cols = np.mgrid[0:size, 0:size]
        dem = 100.0 * np.sin(rows / 17.0) + 60.0 * np.cos(cols / 23.0)

        base = horizon_profile(dem, 10.0, 100, 100)
        shifted = horizon_profile(dem + 1234.0, 10.0, 100, 100)

        self.assertTrue(np.allclose(base, shifted, atol=1e-4))

    def test_curvature_on_a_flat_plateau(self):
        pixel = 25.0
        profile = horizon_profile(np.zeros((101, 101)), pixel, 50, 50)

        nearest = math.degrees(math.atan2(-pixel * pixel / (2 * MOON_RADIUS_M), pixel))
        self.assertLess(nearest, 0.0)
        self.assertTrue(np.allclose(profile, nearest, atol=1e-6))

    def test_observer_height_lowers_angles(self):
        dem = wall_dem("north")

        ground = horizon_profile(dem, 10.0, 100, 100)
        raised = horizon_profile(dem, 10.0, 100, 100, observer_height_m=10.0)

        self.assertLess(float(raised[0]), float(ground[0]))
        self.assertAlmostEqual(float(raised[0]), expected_angle(40.0, 200.0), delta=0.01)

    def test_nan_holes_are_skipped(self):
        dem = wall_dem("north")
        dem[90:95, 90:95] = np.nan

        profile = horizon_profile(dem, 10.0, 100, 100)

        self.assertTrue(np.all(np.isfinite(profile)))
        self.assertAlmostEqual(float(profile[0]), expected_angle(50.0, 200.0), delta=0.01)

    def test_ray_entirely_in_nan_returns_zero(self):
        dem = np.full((201, 201), np.nan)
        dem[100:102, 100:102] = 0.0

        profile = horizon_profile(dem, 10.0, 100, 100)

        self.assertEqual(float(profile[0]), 0.0)
        self.assertEqual(float(profile[360]), 0.0)

    def test_invalid_observer_raises(self):
        dem = np.zeros((50, 50))
        nan_dem = dem.copy()
        nan_dem[25, 25] = np.nan

        with self.assertRaises(ValueError):
            horizon_profile(dem, 10.0, -1, 10)
        with self.assertRaises(ValueError):
            horizon_profile(dem, 10.0, 10, 80)
        with self.assertRaises(ValueError):
            horizon_profile(nan_dem, 10.0, 25, 25)

    def test_output_shape_dtype_and_finiteness(self):
        profile = horizon_profile(wall_dem("east"), 10.0, 100, 100)

        self.assertEqual(profile.shape, (SAMPLES,))
        self.assertEqual(profile.dtype, np.float32)
        self.assertTrue(np.all(np.isfinite(profile)))

    def test_fractional_observer_position(self):
        size = 101
        cols = np.tile(np.arange(size, dtype=float), (size, 1))
        dem = 2.0 * cols

        profile = horizon_profile(dem, 1.0, 50.0, 10.5)

        self.assertAlmostEqual(float(profile[180]), math.degrees(math.atan(2.0)), delta=0.01)

    def test_write_profile_round_trip(self):
        profile = horizon_profile(wall_dem("north"), 10.0, 100, 100)
        with tempfile.TemporaryDirectory() as folder:
            path = os.path.join(folder, "site.bin")
            write_profile(path, profile)

            self.assertEqual(os.path.getsize(path), 2880)
            self.assertTrue(np.array_equal(np.fromfile(path, "<f4"), profile))

    def test_write_profile_rejects_wrong_length(self):
        with tempfile.TemporaryDirectory() as folder:
            with self.assertRaises(ValueError):
                write_profile(os.path.join(folder, "x.bin"), np.zeros(10))

    def test_large_dem_runs_in_reasonable_time(self):
        rng = np.random.default_rng(1)
        dem = rng.normal(0.0, 20.0, (4000, 4000)).astype(np.float32)

        started = time.perf_counter()
        horizon_profile(dem, 5.0, 2000, 2000, max_range_m=20_000.0)
        elapsed = time.perf_counter() - started

        self.assertLess(elapsed, 5.0)


if __name__ == "__main__":
    unittest.main()

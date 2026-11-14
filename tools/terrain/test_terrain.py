import unittest

import numpy as np

import terrain


class TerrainTests(unittest.TestCase):
    def test_flat_surface_curves_away_from_the_site(self):
        grid = terrain.build_grid(-85.0, 30.0, lambda lat, lon: np.zeros_like(lat), 11, 1000.0)
        self.assertAlmostEqual(grid[5, 5], 0.0)
        expected = -(5000.0 ** 2) / (2 * terrain.MOON_RADIUS_M)
        self.assertAlmostEqual(grid[5, 10], expected, places=3)
        self.assertAlmostEqual(grid[0, 5], expected, places=3)

    def test_north_is_up_and_east_is_right(self):
        site_lat, site_lon = -85.0, 30.0

        def sample(lat, lon):
            return 1000.0 * (lat - site_lat) + 10.0 * (lon - site_lon)

        grid = terrain.build_grid(site_lat, site_lon, sample, 11, 1000.0)
        self.assertGreater(grid[0, 5], grid[10, 5])
        self.assertGreater(grid[5, 10], grid[5, 0])

    def test_encode_round_trips_within_one_unit(self):
        grid = np.linspace(-500.0, 800.0, 25).reshape(5, 5)
        decoded, spacing = terrain.decode(terrain.encode(grid, 100.0))
        self.assertEqual(spacing, 100.0)
        self.assertLessEqual(np.abs(decoded - grid).max(), terrain.UNIT_M)


if __name__ == "__main__":
    unittest.main()

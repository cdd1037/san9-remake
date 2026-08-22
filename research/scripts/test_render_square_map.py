import unittest

import render_square_map as renderer


class LandResourceSelectionTests(unittest.TestCase):
    def test_terrain_six_uses_its_two_registered_resources(self) -> None:
        self.assertEqual(10523, renderer.select_land_resource(6, 1, 0, 0))
        self.assertEqual(10524, renderer.select_land_resource(6, 1, 0, 1))

    def test_out_of_range_offset_returns_terrain_base(self) -> None:
        self.assertEqual(10523, renderer.select_land_resource(6, 0, 0, 1))


if __name__ == "__main__":
    unittest.main()

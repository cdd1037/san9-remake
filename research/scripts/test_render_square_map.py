import unittest

from square_map import (
    boundaries,
    building_compositions,
    buildings,
    canvas,
    graphics,
    scenario,
    terrain,
)


class RgbCanvasTests(unittest.TestCase):
    def test_weighted_pixel_blends_in_rgb888(self) -> None:
        target = canvas.RgbCanvas(1, 1)
        target.put(0, 0, (20, 40, 60))
        target.put(0, 0, (220, 140, 40), 128)
        self.assertEqual(bytes((120, 90, 49)), target.to_bytes())

    def test_mask_edge_keeps_256_weight_formula(self) -> None:
        target = canvas.RgbCanvas(1, 1)
        target.put(0, 0, (80, 120, 160))
        target.put_mask_edge(0, 0, (200, 100, 40), 64)
        self.assertEqual(bytes((170, 105, 70)), target.to_bytes())

    def test_indexed_image_uses_palette_alpha_channel(self) -> None:
        target = canvas.RgbCanvas(1, 1)
        target.put(0, 0, (80, 120, 160))
        image = scenario.ImageRecord(1, 1, b"\x01")
        target.draw_indexed(
            image,
            [(0, 0, 0), (200, 100, 40)],
            0,
            0,
            palette_alphas=[0, 32],
        )
        self.assertEqual(bytes((104, 112, 128)), target.to_bytes())


class LandResourceSelectionTests(unittest.TestCase):
    def test_source_x_wraps_only_at_scanline_run_start(self) -> None:
        self.assertEqual(200, terrain.land_source_x(200, 0, 0, 288))
        self.assertEqual(263, terrain.land_source_x(200, 0, 63, 288))
        self.assertEqual(1, terrain.land_source_x(215, 10, 10, 288))
        self.assertEqual(54, terrain.land_source_x(215, 10, 63, 288))

    def test_terrain_six_uses_its_two_registered_resources(self) -> None:
        self.assertEqual(10523, terrain.select_land_resource(6, 1, 0, 0))
        self.assertEqual(10524, terrain.select_land_resource(6, 1, 0, 1))

    def test_out_of_range_offset_returns_terrain_base(self) -> None:
        self.assertEqual(10523, terrain.select_land_resource(6, 0, 0, 1))

    def test_original_sort_reverses_or_preserves_equal_keys_by_midpoint(self) -> None:
        square = scenario.Square(3, 0, 1, 0, 0, 0, -1, -1, -1)
        calls = [
            (10, square, 0, 0, 0, 1),
            (20, square, 0, 0, 0, 2),
            (5, square, 0, 0, 0, 3),
            (10, square, 0, 0, 0, 4),
            (10, square, 0, 0, 0, 5),
            (20, square, 0, 0, 0, 6),
        ]
        self.assertEqual(
            [(5, 3), (10, 5), (10, 4), (10, 1), (20, 2), (20, 6)],
            [(call[0], call[5]) for call in terrain.sort_land_draw_calls(calls)],
        )


class OverlaySelectionTests(unittest.TestCase):
    def test_boundary_direction_order_matches_north_east_south_west(self) -> None:
        self.assertEqual(
            (0x01, 0x02, 0x04, 0x08),
            tuple(direction.bit for direction in boundaries.BOUNDARY_DIRECTIONS),
        )

    def test_boundary_resource_uses_style_orientation_and_elevation_shape(self) -> None:
        north, east, _, _ = boundaries.BOUNDARY_DIRECTIONS
        self.assertEqual(
            (10797, False),
            boundaries.select_boundary_resource(0, 0x01, 0, north),
        )
        self.assertEqual(
            (10795, True),
            boundaries.select_boundary_resource(0x08, 0x02, 0, east),
        )
        self.assertEqual(
            (10802, False),
            boundaries.select_boundary_resource(0x02, 0, 0x02, east),
        )

    def test_city_boundary_takes_precedence_when_masks_overlap(self) -> None:
        north = boundaries.BOUNDARY_DIRECTIONS[0]
        self.assertEqual(
            (10803, False),
            boundaries.select_boundary_resource(0, 0x01, 0x01, north),
        )

    def test_bridge_uses_lndadd_first_palette(self) -> None:
        self.assertEqual("P_LndAdd.s9", scenario.BRIDGE_PALETTE_FILENAME)
        self.assertEqual(0, scenario.BRIDGE_PALETTE_INDEX)

    def test_bridge_requires_terrain_and_marker_bit(self) -> None:
        square = scenario.Square(11, 0, 0, 0, 0, 0x0200, -1, 1, -1)
        self.assertEqual(10005, terrain.select_bridge_resource(square))
        self.assertIsNone(
            terrain.select_bridge_resource(
                scenario.Square(11, 0, 0, 0, 0, 0, -1, 1, -1)
            )
        )

    def test_region_coordinates_keep_center_and_anchor_distinct(self) -> None:
        self.assertEqual((192, 48), terrain.region_square_center(2, 1, 6))
        self.assertEqual((160, 32), terrain.region_square_anchor(2, 1, 6))

    def test_global_center_preserves_original_land_texture_phase(self) -> None:
        self.assertEqual((7296, 2128), terrain.global_square_center(80, 52))

    def test_building_base_uses_center_and_square_height(self) -> None:
        self.assertEqual((192, 48), terrain.region_building_base(2, 1, 6, 1))
        self.assertEqual((192, 38), terrain.region_building_base(2, 1, 6, 2))
        self.assertEqual((192, 18), terrain.region_building_base(2, 1, 6, 4))

    def test_building_base_rejects_missing_square_offset(self) -> None:
        with self.assertRaisesRegex(ValueError, "valid \\+0x08 offset"):
            terrain.region_building_base(2, 1, 6, -1)

    def test_durability_quantizes_from_intact_to_last_frame(self) -> None:
        self.assertEqual(0, buildings.durability_frame(100, 100, 4))
        self.assertEqual(1, buildings.durability_frame(74, 100, 4))
        self.assertEqual(3, buildings.durability_frame(0, 100, 4))

    def test_city_uses_five_by_five_composition_groups(self) -> None:
        city = scenario.FixedBuilding(0, 600, 5, 5, 2)
        self.assertEqual(9, buildings.select_building_composition(city, 440, 0))
        self.assertEqual(14, buildings.select_building_composition(city, 440, 1))
        self.assertEqual(4, buildings.select_building_composition(city, 440, 2))
        self.assertEqual(10, len(building_compositions.BUILDING_COMPOSITIONS[9]))

    def test_city_development_uses_max_income_or_harvest_at_original_gates(self) -> None:
        self.assertEqual(0, buildings.city_development_frame(300, 300))
        self.assertEqual(1, buildings.city_development_frame(301, 300))
        self.assertEqual(1, buildings.city_development_frame(300, 600))
        self.assertEqual(2, buildings.city_development_frame(560, 601))

    def test_city_resource_offset_uses_development_frame(self) -> None:
        city = scenario.BuildingSnapshot(
            zone_code=15,
            fixed_building_code=0,
            force_code=0,
            city_code=15,
            durability=440,
            max_durability=800,
            x=89,
            y=64,
            is_city=True,
            city_terrain_code=0,
            current_income=560,
            current_harvest=150,
        )
        sprite = building_compositions.BUILDING_COMPOSITIONS[9][7]
        self.assertEqual(5909, buildings.building_sprite_resource_id(city, sprite))
        inner_building = building_compositions.BUILDING_COMPOSITIONS[9][8]
        self.assertEqual(5683, buildings.building_sprite_resource_id(city, inner_building))

    def test_port_uses_three_by_three_composition(self) -> None:
        port = scenario.FixedBuilding(1, 300, 3, 3, 1)
        self.assertEqual(18, buildings.select_building_composition(port, 300, 0))
        self.assertEqual((5874, 5727), tuple(
            sprite.resource_id for sprite in building_compositions.BUILDING_COMPOSITIONS[18]
        ))

    def test_hulao_pass_uses_its_own_six_sprite_composition(self) -> None:
        pass_gate = scenario.FixedBuilding(4, 1000, 1, 5, 1)
        self.assertEqual(33, buildings.select_building_composition(pass_gate, 900, 0))
        self.assertEqual(6, len(building_compositions.BUILDING_COMPOSITIONS[33]))
        self.assertEqual(5776, building_compositions.BUILDING_COMPOSITIONS[33][2].resource_id)
        self.assertEqual(-68, building_compositions.BUILDING_COMPOSITIONS[33][0].offset_x)

    def test_city_outer_wall_shadow_resources_follow_original_tables(self) -> None:
        self.assertEqual(
            (5918, 5923, 5925, 5927),
            tuple(
                building_compositions.BUILDING_COMPOSITIONS[cid][5].resource_id
                for cid in (1, 2, 3, 4)
            ),
        )
        self.assertEqual(
            (5917, 5922, 5924, 5926),
            tuple(
                building_compositions.BUILDING_COMPOSITIONS[cid][4].resource_id
                for cid in (1, 2, 3, 4)
            ),
        )
        self.assertEqual(
            ((-100, 32), (-100, 32), (-10, 32), (-10, 32)),
            tuple(
                (
                    building_compositions.BUILDING_COMPOSITIONS[cid][4].offset_x,
                    building_compositions.BUILDING_COMPOSITIONS[cid][4].offset_y,
                )
                for cid in (1, 2, 3, 4)
            ),
        )
        self.assertEqual(
            (5907, 5912, 5914, 5916),
            tuple(
                building_compositions.BUILDING_COMPOSITIONS[cid][5].resource_id
                for cid in (6, 7, 8, 9)
            ),
        )
        self.assertEqual(
            (5929, 5934, 5936, 5938),
            tuple(
                building_compositions.BUILDING_COMPOSITIONS[cid][5].resource_id
                for cid in (11, 12, 13, 14)
            ),
        )

    def test_other_fixed_building_compositions_cover_scenario_objects(self) -> None:
        for code, durability, expected in ((2, 800, 23), (3, 700, 28), (9, 600, 57)):
            building = scenario.FixedBuilding(code, 900 if code != 9 else 700, 1, 1, 1)
            self.assertEqual(
                expected,
                buildings.select_building_composition(building, durability, 0),
            )


class BuildingPaletteAlphaTests(unittest.TestCase):
    def test_shadow_palette_alpha_uses_koei_mmx_denominator(self) -> None:
        destination = (80, 120, 160)
        source = (200, 100, 40)
        self.assertEqual(
            destination,
            graphics.palette_alpha_blend(destination, source, 0),
        )
        self.assertEqual(
            (104, 112, 128),
            graphics.palette_alpha_blend(destination, source, 32),
        )
        self.assertEqual(
            (136, 104, 96),
            graphics.palette_alpha_blend(destination, source, 64),
        )
        self.assertEqual(
            (200, 96, 40),
            graphics.palette_alpha_blend(destination, source, 128),
        )

    def test_six_bit_palette_range_does_not_become_fully_opaque(self) -> None:
        destination = (0, 0, 0)
        source = (248, 248, 248)
        self.assertEqual(
            (120, 120, 120),
            graphics.palette_alpha_blend(destination, source, 64),
        )


if __name__ == "__main__":
    unittest.main()

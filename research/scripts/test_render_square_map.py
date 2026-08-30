import struct
import tempfile
import unittest
from pathlib import Path

from square_map import (
    boundaries,
    building_info,
    building_compositions,
    buildings,
    canvas,
    clouds,
    graphics,
    scenario,
    terrain,
)


class MapEdgeCloudTests(unittest.TestCase):
    def test_flat_map_edge_polylines_meet_at_diamond_vertices(self) -> None:
        square = scenario.Square(3, 0, 0, 0, 0, 0, -1, -1, -1)
        left, right = clouds.build_edge_polylines(
            [square] * (scenario.MAP_WIDTH * scenario.MAP_HEIGHT), 128
        )
        self.assertEqual(400, len(left))
        self.assertEqual(400, len(right))
        self.assertEqual((6496, 122), left[0])
        self.assertEqual(left[0], right[0])
        self.assertEqual(96, min(x for x, _ in left))
        self.assertEqual(12896, max(x for x, _ in right))

    def test_cloud_resource_groups_keep_registry_palette_partition(self) -> None:
        self.assertEqual(10783, clouds.CLOUD_FILL_RESOURCE)
        self.assertEqual(1, clouds.CLOUD_FILL_PALETTE)
        self.assertEqual(0, clouds.CLOUD_OBSTRUCTED_PALETTE)
        self.assertEqual(4, clouds.CLOUD_FOREGROUND_PALETTE)
        foreground_ids = {
            placement.resource_id
            for table in (
                clouds.NORTH_WEST_CLOUDS,
                clouds.NORTH_EAST_CLOUDS,
                clouds.SOUTH_WEST_CLOUDS,
                clouds.SOUTH_EAST_CLOUDS,
            )
            for placement in table
        }
        self.assertEqual(set(range(10788, 10794)), foreground_ids)

    def test_foreground_height_uses_edge_sample_average(self) -> None:
        samples = [clouds.EdgeSample(2, False)] * 399
        self.assertEqual(
            72,
            clouds._adjust_foreground_y(100, samples, north_side=True),
        )

    def test_building_overlap_selects_dark_cloud_pair(self) -> None:
        self.assertTrue(clouds._intersects_any(0, 0, 9, 9, [(9, 9, 20, 20)]))
        self.assertFalse(clouds._intersects_any(0, 0, 8, 8, [(9, 9, 20, 20)]))


class SquareTransitionGenerationTests(unittest.TestCase):
    @staticmethod
    def _square(terrain_code: int, offset: int = 1) -> scenario.Square:
        return scenario.Square(
            terrain_code, 0, offset, 0, 0, 0, -1, -1, -1
        )

    def test_elevation_mask_raises_each_corner_from_three_neighbors(self) -> None:
        squares = [self._square(2) for _ in range(9)]
        squares[0] = self._square(2, 2)  # north-west raises bit 0
        squares[5] = self._square(2, 2)  # east raises north-east and south-east

        self.assertEqual(
            0x0B,
            terrain.calculate_elevation_transition_mask(
                squares, 1, 1, width=3, height=3
            ),
        )

    def test_terrain_mask_uses_atlas_order_and_diagonal_strict_maximum(self) -> None:
        squares = [self._square(16) for _ in range(9)]
        squares[4] = self._square(2)
        squares[1] = self._square(0)  # north is greater than the center
        squares[5] = self._square(3)  # east is greater than the center
        squares[0] = self._square(1)  # north-west is greater than both sides
        squares[2] = self._square(5)  # north-east loses to the north square

        self.assertEqual(
            0x91,
            terrain.calculate_terrain_transition_mask(
                squares, 1, 1, width=3, height=3
            ),
        )

    def test_bridge_transition_uses_shared_atlas_order(self) -> None:
        squares = [self._square(11) for _ in range(9)]
        squares[1] = self._square(12)  # same atlas key: no north transition
        squares[5] = self._square(2)  # higher land key: east transition

        self.assertEqual(
            0x10,
            terrain.calculate_terrain_transition_mask(
                squares, 1, 1, width=3, height=3
            ),
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

    def test_water_texture_variant_selects_exact_registered_resource(self) -> None:
        self.assertEqual(
            [10315, 10316, 10317, 10318, 10317, 10316],
            [terrain.select_land_resource(9, 1, 0, variant) for variant in range(6)],
        )

    def test_edge_texture_variant_keeps_nine_resource_stride(self) -> None:
        self.assertEqual(
            [10336, 10345, 10354],
            [terrain.select_land_resource(0, 0, 1, variant) for variant in range(3)],
        )

    def test_water_texture_variant_has_confirmed_atlas_sort_remap(self) -> None:
        self.assertEqual(
            [339, 337, 338, 336, 337, 338],
            [
                terrain.calculate_land_atlas_index(
                    scenario.Square(9, 0, 1, 0, variant, 0, -1, -1, -1)
                )
                for variant in range(6)
            ],
        )

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

    def test_boundary_resource_table_covers_both_styles_and_all_shapes(self) -> None:
        for direction in boundaries.BOUNDARY_DIRECTIONS:
            orientation_offset = 0 if direction.uses_first_resource_triplet else 3
            elevation_cases = (
                (0, 0, False),
                (direction.primary_elevation_bit, 1, True),
                (direction.secondary_elevation_bit, 2, False),
                (
                    direction.primary_elevation_bit | direction.secondary_elevation_bit,
                    0,
                    True,
                ),
            )
            for elevation_mask, shape, primary_elevated in elevation_cases:
                with self.subTest(direction=direction.bit, elevation=elevation_mask):
                    self.assertEqual(
                        (10794 + orientation_offset + shape, primary_elevated),
                        boundaries.select_boundary_resource(
                            elevation_mask, direction.bit, 0, direction
                        ),
                    )
                    self.assertEqual(
                        (10800 + orientation_offset + shape, primary_elevated),
                        boundaries.select_boundary_resource(
                            elevation_mask, direction.bit, direction.bit, direction
                        ),
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
            soldiers=32000,
            permanent_merchant=True,
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


class BuildingInfoTests(unittest.TestCase):
    @staticmethod
    def building(
        *, city_code: int = 15, is_city: bool = True,
        soldiers: int = 1000, merchant: bool = False,
        zone_code: int = 99, fixed_building_code: int = 0,
    ) -> scenario.BuildingSnapshot:
        return scenario.BuildingSnapshot(
            zone_code=city_code if is_city else zone_code,
            fixed_building_code=fixed_building_code,
            force_code=0,
            city_code=city_code,
            durability=50,
            max_durability=100,
            x=10,
            y=10,
            is_city=is_city,
            city_terrain_code=0,
            current_income=None,
            current_harvest=None,
            soldiers=soldiers,
            permanent_merchant=merchant,
        )

    def test_panel_width_and_city_icon_rules(self) -> None:
        ordinary = self.building()
        merchant = self.building(merchant=True)
        training = self.building(city_code=14)
        zone = self.building(is_city=False, merchant=True)
        self.assertEqual(76, building_info.panel_width(ordinary, None))
        self.assertEqual(95, building_info.panel_width(merchant, None))
        self.assertEqual(95, building_info.panel_width(training, None))
        self.assertEqual(116, building_info.panel_width(merchant, 15))
        self.assertEqual((240, 200), building_info.panel_origin(100, 200, 128, 0))
        self.assertEqual((240, 254), building_info.panel_origin(100, 200, 128, 1))
        self.assertEqual((308, 222), building_info.panel_origin(100, 200, 128, 4))
        with self.assertRaises(ValueError):
            building_info.panel_origin(100, 200, 128, -1)
        with self.assertRaises(ValueError):
            building_info.panel_origin(100, 200, 128, 17)
        self.assertIsNone(building_info.shared_icon_resource(zone))
        self.assertFalse(building_info.building_hosts_emperor(zone, 15))

    def test_merchant_takes_precedence_and_emperor_coexists(self) -> None:
        training_merchant = self.building(city_code=14, merchant=True)
        self.assertEqual(
            building_info.MERCHANT_ICON_RESOURCE,
            building_info.shared_icon_resource(training_merchant),
        )
        self.assertTrue(
            building_info.building_hosts_emperor(training_merchant, 14)
        )

    def test_static_morale_is_zero_without_soldiers(self) -> None:
        self.assertEqual(50, building_info.displayed_morale(self.building(), 50))
        self.assertEqual(
            0,
            building_info.displayed_morale(self.building(soldiers=0), 50),
        )
        self.assertEqual(100, building_info.validate_morale(100))
        with self.assertRaisesRegex(ValueError, "between 0 and 100"):
            building_info.validate_morale(101)

    def test_soldier_count_field_ends_at_original_right_edge(self) -> None:
        self.assertEqual(
            72,
            building_info.SOLDIER_TEXT_X + building_info.SOLDIER_TEXT_WIDTH,
        )

    def test_common_and_marker_archive_counts_are_strict(self) -> None:
        building_info.validate_resource_counts(
            [None] * 67, [None] * 491, [None] * 5, [None] * 34
        )
        with self.assertRaisesRegex(ValueError, "G_Common"):
            building_info.validate_resource_counts(
                [None] * 66, [None] * 491, [None] * 5, [None] * 34
            )
        with self.assertRaisesRegex(ValueError, "G_Marker"):
            building_info.validate_resource_counts(
                [None] * 67, [None] * 491, [None] * 5, [None] * 33
            )

    def test_status_bar_width_and_threshold_resources_match_original(self) -> None:
        self.assertEqual(0, building_info.status_bar_width(0, 100))
        self.assertEqual(34, building_info.status_bar_width(50, 100))
        self.assertEqual(68, building_info.status_bar_width(100, 100))
        resources = building_info.MORALE_BAR_RESOURCES
        self.assertEqual(394, building_info.status_bar_fill_resource(19, 100, resources))
        self.assertEqual(395, building_info.status_bar_fill_resource(20, 100, resources))
        self.assertEqual(392, building_info.status_bar_fill_resource(40, 100, resources))

    def test_name_keys_distinguish_city_and_zone_catalogs(self) -> None:
        self.assertEqual(
            ("city", 15), building_info.building_name_key(self.building())
        )
        self.assertEqual(
            ("zone", 49),
            building_info.building_name_key(self.building(is_city=False)),
        )
        with self.assertRaisesRegex(ValueError, "missing visible building"):
            building_info.resolve_building_name(self.building(), {})

    def test_static_building_type_suffixes_follow_the_executable(self) -> None:
        names = {
            ("city", 15): "洛阳",
            ("zone", 49): "虎牢关",
            ("zone", 50): "孟津",
        }
        self.assertEqual(
            "洛阳", building_info.resolve_building_name(self.building(), names)
        )
        self.assertEqual(
            "虎牢关",
            building_info.resolve_building_name(
                self.building(is_city=False, fixed_building_code=4), names
            ),
        )
        self.assertEqual(
            "孟津港",
            building_info.resolve_building_name(
                self.building(
                    is_city=False, zone_code=100, fixed_building_code=1
                ),
                names,
            ),
        )

    def test_semantic_catalog_rejects_duplicate_keys(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "names.csv"
            path.write_text("kind,id,name\ncity,15,洛阳\ncity,15,雒阳\n", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "duplicate semantic key"):
                building_info.read_semantic_catalog(path)

    def test_emperor_candidates_follow_biography_order_and_status(self) -> None:
        head = bytes(82)
        persons = bytearray(
            scenario.PERSON_RECORD_COUNT * scenario.PERSON_RECORD_SIZE
        )
        for index, biography, status in (
            (675, 563, 8),
            (676, 564, 8),
            (677, 565, 9),
        ):
            offset = index * scenario.PERSON_RECORD_SIZE
            struct.pack_into("<H", persons, offset + 0x10, biography)
            struct.pack_into("<H", persons, offset + 0x18, 15)
            struct.pack_into("<H", persons, offset + 0x1C, status)
        self.assertEqual(
            15,
            scenario.resolve_emperor_city_code(head, bytes(persons), []),
        )
        disabled_head = bytearray(head)
        struct.pack_into("<I", disabled_head, 4, 1)
        self.assertIsNone(
            scenario.resolve_emperor_city_code(
                bytes(disabled_head), bytes(persons), []
            )
        )


class PaletteImageArchiveTests(unittest.TestCase):
    def test_palette_prefix_and_image_stream_are_both_consumed(self) -> None:
        palette = bytes(40) + bytes(range(256)) * 4
        pixels = b"\x00\x01"
        image_record = struct.pack("<4I", 18, 2, 2, 1) + pixels
        image_stream = struct.pack("<I", 4 + len(image_record)) + image_record
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "archive.s9"
            path.write_bytes(struct.pack("<I", 1) + palette + image_stream)
            palettes, images = graphics.parse_palette_image_archive(path)
        self.assertEqual(1, len(palettes))
        self.assertEqual((0, 1, 2), palettes[0][0])
        self.assertEqual((2, 1, pixels), (
            images[0].width, images[0].height, images[0].pixels
        ))


if __name__ == "__main__":
    unittest.main()

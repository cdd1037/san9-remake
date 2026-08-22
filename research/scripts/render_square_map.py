#!/usr/bin/env python3
"""Render a partial isometric SQUARE terrain map for visual experiments.

This is an experiment renderer, not product code. It reads the scenario's
14-byte ``squa`` records and the local indexed land/mask resources, then
renders the terrain layer and its eight-direction transition records.
Buildings, units, borders and bridges are intentionally omitted. MESH/COV is
available as an explicit diagnostic overlay because the original only draws it
for a runtime-selected square/object list, not for every bare-map square.

The land resource selector is transcribed from the confirmed IDA data flow at
0x5184E0. Palette selection follows the static resource-to-palette registry at
0x65E7D0. The G_MASK/P_MASK groups and target position tables are transcribed
from 0x518F60, 0x610C20 and 0x6110A0; the renderer remains an experiment and
does not recreate the original target-texture object system.
"""

from __future__ import annotations

import argparse
import binascii
import collections
import struct
import zlib
from dataclasses import dataclass
from pathlib import Path


MAP_WIDTH = 200
MAP_HEIGHT = 200
SQUARE_RECORD_SIZE = 14
SQUA_OFFSET = 0x1406
SQUA_SIZE = MAP_WIDTH * MAP_HEIGHT * SQUARE_RECORD_SIZE
SCENARIO_DATE_OFFSET = 0x2C
LAND_RESOURCE_BASE = 10006
MASK_RESOURCE_BASE = 10806
MASK_EDGE_FLAG1_BASE = 10950
MASK_EDGE_FLAG2_BASE = 10959
MESH_RESOURCE_BASE = 10972
MESH_RESOURCE_COUNT = 96
MESH_STATE_BASES = (10972, 11004, 11036, 11020, 10988, 11052)
# create_square_mesh_visual() stores (alpha << 8) | 2 at resource +0x14.
# The low byte selects alpha-blend mode 2; the high byte is the source alpha.
MESH_STATE_ALPHA = (50, 25, 20, 22, 20, 20)


@dataclass(frozen=True)
class Square:
    terrain: int
    edge_mask: int
    offset: int
    elevation_mask: int
    texture_variant: int


@dataclass(frozen=True)
class ImageRecord:
    width: int
    height: int
    pixels: bytes


def png_chunk(kind: bytes, payload: bytes) -> bytes:
    return (
        struct.pack(">I", len(payload))
        + kind
        + payload
        + struct.pack(">I", binascii.crc32(kind + payload) & 0xFFFFFFFF)
    )


def encode_rgb_png(width: int, height: int, rgb: bytes) -> bytes:
    rows = b"".join(
        b"\x00" + rgb[y * width * 3 : (y + 1) * width * 3]
        for y in range(height)
    )
    return (
        b"\x89PNG\r\n\x1a\n"
        + png_chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
        + png_chunk(b"IDAT", zlib.compress(rows, 9))
        + png_chunk(b"IEND", b"")
    )


def parse_image_stream(path: Path) -> list[ImageRecord]:
    data = path.read_bytes()
    total = struct.unpack_from("<I", data, 0)[0]
    if total != len(data):
        raise ValueError(f"{path}: stream size {total} != file size {len(data)}")

    records: list[ImageRecord] = []
    position = 4
    while position < total:
        if position + 16 > total:
            raise ValueError(f"{path}: truncated image header at {position}")
        record_size, row_stride, width, height = struct.unpack_from(
            "<4I", data, position
        )
        if record_size < 16 or position + record_size > total:
            raise ValueError(f"{path}: invalid image record at {position}")
        pixels = data[position + 16 : position + record_size]
        if row_stride != width or len(pixels) != row_stride * height:
            raise ValueError(f"{path}: inconsistent {width}x{height} image record")
        records.append(ImageRecord(width, height, pixels))
        position += record_size
    return records


def parse_palettes(path: Path) -> list[list[tuple[int, int, int]]]:
    data = path.read_bytes()
    count = struct.unpack_from("<I", data, 0)[0]
    record_size = 40 + 256 * 4
    expected_size = 4 + count * record_size
    if len(data) != expected_size:
        raise ValueError(f"{path}: palette file size {len(data)} != {expected_size}")

    palettes: list[list[tuple[int, int, int]]] = []
    for index in range(count):
        start = 4 + index * record_size + 40
        palette_bytes = data[start : start + 256 * 4]
        palettes.append(
            [
                tuple(palette_bytes[offset : offset + 3])
                for offset in range(0, len(palette_bytes), 4)
            ]
        )
    return palettes


def select_land_palette_name(scenario: Path, requested: str) -> str:
    """Select the P_Land palette used by the original seasonal loader."""

    if requested != "auto":
        return requested
    data = scenario.read_bytes()
    if len(data) < SCENARIO_DATE_OFFSET + 2:
        raise ValueError(f"{scenario}: missing scenario date field")
    encoded_date = struct.unpack_from("<H", data, SCENARIO_DATE_OFFSET)[0]
    month = (encoded_date // 30) % 12 + 1
    return ("S", "N", "A", "W")[(month - 1) // 3]


def read_squares(path: Path) -> list[Square]:
    data = path.read_bytes()
    if len(data) < SQUA_OFFSET + SQUA_SIZE:
        raise ValueError(f"{path}: file is too short for the SQUARE chunk")

    records: list[Square] = []
    position = SQUA_OFFSET
    for _ in range(MAP_WIDTH * MAP_HEIGHT):
        serialized_id, flags, zone = struct.unpack_from("<3H", data, position)
        del serialized_id, flags, zone
        aux, edge_mask, offset, terrain, elevation_mask, texture_variant, bridge, boundary = (
            data[position + 6 : position + 14]
        )
        del aux, bridge, boundary
        records.append(
            Square(
                terrain=-1 if terrain == 0xFF else terrain,
                edge_mask=0 if edge_mask == 0xFF else edge_mask,
                offset=-1 if offset == 0xFF else offset,
                elevation_mask=-1 if elevation_mask == 0xFF else elevation_mask,
                texture_variant=-1
                if texture_variant == 0xFF
                else texture_variant,
            )
        )
        position += SQUARE_RECORD_SIZE
    return records


def terrain_family(terrain: int) -> int:
    if terrain == 0:
        return 0
    if terrain in (1, 13, 14):
        return 6
    if terrain in (2, 4, 7):
        return 1
    if terrain == 6:
        return 3
    if terrain in (9, 12):
        return 7
    if terrain in (10, 15, 16):
        return 4
    if terrain == 11:
        return 5
    return 2


def select_land_resource(
    terrain: int, offset: int, elevation_mask: int, variant: int
) -> int | None:
    """Return the G_LLand resource ID from the confirmed selector tables."""

    if not 0 <= terrain < 17:
        return None

    variant_count = [3, 3, 3, 3, 3, 3, 2, 3, 3, 4, 4, 1, 1, 3, 3, 4, 4]
    offset_min = [0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 0, 0, 1, 1]
    offset_max = [7, 7, 7, 2, 2, 2, 1, 1, 1, 1, 1, 1, 1, 7, 7, 1, 1]
    resource_base = [
        10331,
        10006,
        10525,
        10198,
        10717,
        10255,
        10523,
        10774,
        10312,
        10315,
        10319,
        10315,
        10315,
        10006,
        10006,
        10323,
        10327,
    ]
    edge_transition_terrain = {0, 1, 2, 3, 4, 5, 13, 14}
    edge_index = [0, 5, 8, 0, 7, 2, 4, 7, 6, 1, 3, 6, 0, 5, 8, 0]
    invalid_edge_value = 0xFF

    low = offset_min[terrain]
    high = offset_max[terrain]
    if offset < low or offset > high:
        return resource_base[terrain]

    variant_offset = {1: 1, 2: 2, 3: 3, 4: 2, 5: 1}.get(variant, 0)
    edge_value = 0
    if terrain in edge_transition_terrain and offset != high:
        edge_value = edge_index[elevation_mask & 0x0F]
        if edge_value == invalid_edge_value:
            edge_value = 0

    step = 9 * variant_count[terrain] if terrain in edge_transition_terrain else variant_count[terrain]
    variant_step = variant_offset if offset == high else 9 * variant_offset
    resource_id = resource_base[terrain] + edge_value + variant_step + step * (offset - low)
    if not LAND_RESOURCE_BASE <= resource_id < LAND_RESOURCE_BASE + 771:
        return None
    return resource_id


LAND_PALETTE_RANGES = (
    (10006, 10196, 0),
    (10198, 10253, 1),
    (10255, 10310, 2),
    (10312, 10313, 3),
    (10315, 10315, 4),
    (10316, 10316, 5),
    (10317, 10317, 6),
    (10318, 10318, 7),
    (10319, 10319, 4),
    (10320, 10320, 5),
    (10321, 10321, 6),
    (10322, 10322, 7),
    (10323, 10323, 4),
    (10324, 10324, 5),
    (10325, 10325, 6),
    (10326, 10326, 7),
    (10327, 10327, 4),
    (10328, 10328, 5),
    (10329, 10329, 6),
    (10330, 10330, 7),
    (10331, 10521, 8),
    (10523, 10524, 9),
    (10525, 10715, 10),
    (10717, 10772, 11),
    (10774, 10775, 12),
)


def palette_index_for_resource(resource_id: int) -> int | None:
    """Return the P_Land_N record for a registered G_LLand resource.

    The ranges are the land-image part of the static resource registry at
    IDA 0x65E7D0.  The registry binds resource IDs to global palette slots
    91..103; P_Land_N record zero corresponds to slot 91.
    """

    for first, last, palette_index in LAND_PALETTE_RANGES:
        if first <= resource_id <= last:
            return palette_index
    return None


def select_mask_resource(elevation_mask: int, record_type: int, mask_flags: int) -> int:
    """Return the G_MASK resource selected by select_land_tile_subresource().

    For a non-zero ``squa +0x0A``, the original selects one of the sixteen
    nine-image elevation groups. For zero, it selects one of two additional
    groups based on the edge-record flags before falling back to the first
    nine-image group.
    """

    record_type = max(0, min(8, record_type))
    if 1 <= elevation_mask < 16:
        return MASK_RESOURCE_BASE + 9 * elevation_mask + record_type
    if mask_flags & 1:
        return MASK_EDGE_FLAG1_BASE + record_type
    if mask_flags & 2:
        return MASK_EDGE_FLAG2_BASE + record_type
    return MASK_RESOURCE_BASE + record_type


def select_edge_mask_flags(current: Square, neighbour: Square) -> int:
    """Reproduce sub_520ED0's two special e0 edge-mask classifications."""

    # sub_520ED0 keeps the current terrain in EDX across sub_51CFE0(); the
    # following 0x0B/0x0C comparisons therefore also test the current square,
    # not the neighbour record stored in edi.
    if current.terrain in (9, 10, 11, 12, 15, 16):
        return 1
    if neighbour.offset == 1 and neighbour.terrain in (0, 5):
        return 2
    return 0


def terrain_fallback_color(terrain: int) -> tuple[int, int, int]:
    return {
        0: (74, 102, 48),
        1: (112, 101, 82),
        2: (124, 111, 62),
        3: (95, 130, 54),
        4: (119, 112, 68),
        5: (75, 108, 56),
        6: (110, 124, 104),
        7: (120, 114, 69),
        8: (92, 130, 82),
        9: (96, 143, 133),
        10: (59, 112, 157),
        11: (125, 89, 63),
        12: (87, 132, 144),
        13: (105, 112, 78),
        14: (115, 107, 72),
        15: (64, 120, 163),
        16: (69, 128, 171),
    }.get(terrain, (90, 90, 90))


def blend_color(color: tuple[int, int, int], factor: float) -> tuple[int, int, int]:
    return tuple(max(0, min(255, int(channel * factor))) for channel in color)


# dword_610C20/dword_610C24, indexed by elevation_mask * 9 + record_type.
# record_type 0 is the current square; 1..8 are the eight edge records.
MASK_POSITION_TABLE = (
    (32, 48), (0, 64), (64, 64), (0, 32), (64, 32), (32, 80), (0, 48), (95, 48), (32, 23),
    (32, 36), (0, 64), (64, 64), (4, 15), (64, 15), (32, 80), (0, 48), (96, 48), (32, 10),
    (32, 47), (0, 64), (64, 64), (0, 32), (64, 32), (32, 66), (0, 48), (96, 48), (32, 23),
    (32, 36), (0, 64), (64, 64), (4, 15), (64, 15), (32, 66), (0, 48), (96, 48), (32, 10),
    (32, 48), (4, 53), (64, 64), (0, 30), (64, 32), (32, 80), (0, 38), (96, 48), (32, 27),
    (32, 37), (4, 53), (64, 64), (0, 22), (64, 15), (32, 80), (0, 38), (96, 48), (32, 13),
    (32, 38), (0, 54), (64, 64), (0, 30), (64, 32), (32, 66), (0, 38), (96, 48), (32, 23),
    (32, 37), (0, 53), (64, 63), (0, 21), (64, 16), (32, 66), (0, 37), (96, 47), (32, 13),
    (32, 48), (0, 64), (64, 53), (0, 32), (64, 30), (32, 80), (0, 48), (96, 38), (32, 27),
    (32, 37), (0, 64), (64, 53), (4, 15), (64, 22), (32, 80), (0, 48), (96, 38), (32, 13),
    (32, 38), (0, 64), (64, 54), (0, 32), (64, 30), (32, 66), (0, 48), (96, 38), (32, 23),
    (32, 37), (0, 63), (64, 53), (4, 16), (64, 21), (32, 66), (0, 47), (96, 37), (32, 13),
    (32, 45), (4, 54), (64, 54), (0, 29), (64, 29), (32, 79), (0, 37), (96, 37), (32, 27),
    (32, 37), (4, 54), (64, 54), (0, 21), (64, 21), (32, 79), (0, 37), (96, 37), (32, 13),
    (32, 56), (0, 63), (64, 63), (0, 39), (64, 39), (32, 80), (0, 47), (96, 47), (32, 37),
    (32, 38), (0, 54), (64, 54), (0, 22), (64, 22), (32, 70), (0, 38), (96, 38), (32, 13),
)
MASK_VERTICAL_ADJUST = (0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 10, 0)


EDGE_RECORDS = (
    (0x10, 3, 1, 0),
    (0x20, 2, -1, 0),
    (0x40, 4, 0, 1),
    (0x80, 1, 0, -1),
    (0x01, 5, -1, -1),
    (0x02, 6, 1, -1),
    (0x04, 8, 1, 1),
    (0x08, 7, -1, 1),
)


ATLAS_BASE_BY_TERRAIN = (
    12, 14, 7, 9, 8, 11, 5, 6, 10, 4, 3, 4, 4, 15, 13, 2, 1
)
ATLAS_EDGE_VARIANT_TERRAINS = {0, 1, 2, 3, 4, 5, 13, 14}


def calculate_land_atlas_index(square: Square) -> int:
    """Transcribe calculate_land_tile_atlas_index() for record sorting."""

    terrain = square.terrain
    if not 0 <= terrain < len(ATLAS_BASE_BY_TERRAIN):
        return 0
    offset = square.offset if square.offset >= 0 else 0
    elevation_mask = square.elevation_mask if square.elevation_mask >= 0 else 0
    variant = square.texture_variant if square.texture_variant >= 0 else 0
    edge_variant = 0
    if terrain in ATLAS_EDGE_VARIANT_TERRAINS:
        bit_count = elevation_mask.bit_count()
        if bit_count in (1, 3):
            edge_variant = 1
        elif bit_count == 2:
            edge_variant = 3 if elevation_mask in (5, 9) else 2
        edge_variant *= 4
    if terrain in (9, 10, 15, 16):
        variant = {0: 3, 3: 0, 4: 1, 5: 2}.get(variant, variant)
    return (ATLAS_BASE_BY_TERRAIN[terrain] << 4) + 272 * offset + variant + edge_variant


def render_region(
    squares: list[Square],
    land_images: list[ImageRecord],
    land_palettes: list[list[tuple[int, int, int]]],
    mask_images: list[ImageRecord],
    mask_palette: list[tuple[int, int, int]],
    mesh_images: list[ImageRecord],
    mesh_palette: list[tuple[int, int, int]],
    x0: int,
    y0: int,
    width: int,
    height: int,
    mesh_state: int | None = None,
    mesh_elevated_only: bool = False,
) -> tuple[int, int, bytes, collections.Counter[int], collections.Counter[int]]:
    margin = 128
    output_width = (width + height) * 32 + margin * 2
    output_height = (width + height) * 16 + margin * 2
    canvas = bytearray(output_width * output_height * 3)
    counters: collections.Counter[int] = collections.Counter()
    mesh_counters: collections.Counter[int] = collections.Counter()

    def put_pixel(x: int, y: int, color: tuple[int, int, int], weight: int = 255) -> None:
        if not (0 <= x < output_width and 0 <= y < output_height):
            return
        offset = (y * output_width + x) * 3
        if weight >= 255:
            canvas[offset : offset + 3] = bytes(color)
            return
        if weight <= 0:
            return
        old = canvas[offset : offset + 3]
        canvas[offset : offset + 3] = bytes(
            (weight * channel + (255 - weight) * old[index]) // 255
            for index, channel in enumerate(color)
        )

    def put_edge_pixel(
        x: int, y: int, color: tuple[int, int, int], target_weight: int
    ) -> None:
        if not (0 <= x < output_width and 0 <= y < output_height):
            return
        if target_weight >= 256:
            return
        offset = (y * output_width + x) * 3
        old = canvas[offset : offset + 3]
        source_weight = 256 - target_weight
        canvas[offset : offset + 3] = bytes(
            (source_weight * channel + target_weight * old[index]) >> 8
            for index, channel in enumerate(color)
        )

    def draw_record(
        square: Square,
        map_x: int,
        map_y: int,
        record_type: int,
        mask_flags: int = 0,
    ) -> None:
        terrain = square.terrain
        elevation_mask = square.elevation_mask if 0 <= square.elevation_mask < 16 else 0
        offset_code = square.offset if square.offset >= 0 else 0
        resource_id = select_land_resource(
            terrain,
            square.offset,
            square.elevation_mask,
            square.texture_variant,
        )
        if resource_id is None:
            counters[-1] += 1
            image = None
            palette = land_palettes[0]
        else:
            counters[resource_id] += 1
            image = land_images[resource_id - LAND_RESOURCE_BASE]
            palette_index = palette_index_for_resource(resource_id)
            palette = (
                land_palettes[palette_index]
                if palette_index is not None and palette_index < len(land_palettes)
                else land_palettes[0]
            )

        mask_resource_id = select_mask_resource(
            elevation_mask, record_type, mask_flags
        )
        mask = mask_images[mask_resource_id - MASK_RESOURCE_BASE]
        position_x, position_y = MASK_POSITION_TABLE[9 * elevation_mask + record_type]
        world_center_x = (map_x - x0 - (map_y - y0) + height - 1) * 32
        world_center_y = ((map_x - x0) + (map_y - y0)) * 16
        target_x = margin + world_center_x + position_x - 64
        target_y = (
            margin
            + world_center_y
            + position_y
            - MASK_VERTICAL_ADJUST[elevation_mask]
            - 10 * offset_code
            - 54
        )
        source_x_origin = (map_x - map_y + MAP_HEIGHT - 1) * 32 + position_x - 64
        source_y_origin = (
            (map_x + map_y) * 16
            + position_y
            - MASK_VERTICAL_ADJUST[elevation_mask]
            - 10 * offset_code
            - 54
        )
        fallback = blend_color(
            terrain_fallback_color(terrain),
            0.84 + 0.04 * (square.texture_variant if square.texture_variant >= 0 else 0),
        )
        for mask_y in range(mask.height):
            row_start = mask_y * mask.width
            if record_type == 0:
                first_column = 0
                while (
                    first_column < mask.width
                    and mask.pixels[row_start + first_column] == 0
                ):
                    first_column += 1
                if first_column == mask.width:
                    continue
                last_column = mask.width - 1
                while mask.pixels[row_start + last_column] == 0:
                    last_column -= 1
                columns = range(first_column, last_column + 1)
            else:
                columns = range(mask.width)

            for mask_x in columns:
                mask_index = mask.pixels[row_start + mask_x]
                if record_type != 0 and mask_index == 0:
                    continue
                target_pixel_x = target_x + mask_x
                target_pixel_y = target_y + mask_y
                # The original dispatches type 9/current records to
                # composite_mask_tile_pixels(), which finds the first and last
                # non-zero mask pixel in each row and copies the whole span.
                # Edge records use composite_land_tile_pixels(), where each
                # non-zero G_MASK pixel's palette red channel controls blending
                # against the existing target.
                target_weight = mask_palette[mask_index][0]
                color = fallback
                if image is not None:
                    source_width = max(1, image.width - 64)
                    source_x = (source_x_origin + mask_x) % source_width
                    source_y = (source_y_origin + mask_y) % image.height
                    pixel_index = image.pixels[source_y * image.width + source_x]
                    color = palette[pixel_index]
                if record_type == 0:
                    put_pixel(target_pixel_x, target_pixel_y, color)
                else:
                    put_edge_pixel(
                        target_pixel_x, target_pixel_y, color, target_weight
                    )

    # build_square_edge_overlays() appends the eight edge records first and the
    # current-square record (type 9) last. The original then calls
    # sub_518370(), which sorts the records by field +0x20 before drawing. Keep
    # the append sequence as the stable tie-breaker and sort before overdraw.
    draw_calls: list[tuple[int, Square, int, int, int, int]] = []

    def queue_record(
        square: Square,
        map_x: int,
        map_y: int,
        record_type: int,
        mask_flags: int = 0,
    ) -> None:
        atlas_index = calculate_land_atlas_index(square)
        sort_key = map_x + MAP_WIDTH * (map_y + atlas_index * MAP_HEIGHT)
        draw_calls.append(
            (sort_key, square, map_x, map_y, record_type, mask_flags)
        )

    for local_y in range(height):
        for local_x in range(width):
            x = x0 + local_x
            y = y0 + local_y
            square = squares[y * MAP_WIDTH + x]
            for bit, record_type, dx, dy in EDGE_RECORDS:
                if not (square.edge_mask & bit):
                    continue
                neighbour_x = x + dx
                neighbour_y = y + dy
                if not (0 <= neighbour_x < MAP_WIDTH and 0 <= neighbour_y < MAP_HEIGHT):
                    continue
                neighbour = squares[neighbour_y * MAP_WIDTH + neighbour_x]
                queue_record(
                    neighbour,
                    neighbour_x,
                    neighbour_y,
                    record_type,
                    select_edge_mask_flags(square, neighbour),
                )

            # The current square is appended after all of its edge records.
            # Its G_MASK entry carries the elevation-dependent shape and
            # vertical offset of the tile.
            queue_record(square, x, y, 0)

    for _, square, map_x, map_y, record_type, mask_flags in sorted(
        draw_calls, key=lambda item: item[0]
    ):
        draw_record(square, map_x, map_y, record_type, mask_flags)

    # The original draw_square_mesh_overlays() consumes a separate runtime
    # overlay-state grid. It is not serialized in SQUA, so this renderer only
    # enables it explicitly. In elevated-only mode, the test marks squares whose
    # SQUA +0x0A selects a non-zero MESH variant; this is a diagnostic view, not
    # a claim that the original always draws these squares on the bare map.
    if mesh_state is not None:
        if not 0 <= mesh_state < len(MESH_STATE_BASES):
            raise ValueError(f"mesh state must be 0..{len(MESH_STATE_BASES) - 1}")
        mesh_base = MESH_STATE_BASES[mesh_state]
        mesh_alpha = MESH_STATE_ALPHA[mesh_state]
        for local_y in range(height):
            for local_x in range(width):
                x = x0 + local_x
                y = y0 + local_y
                square = squares[y * MAP_WIDTH + x]
                elevation_mask = square.elevation_mask
                if not 0 <= elevation_mask < 16:
                    elevation_mask = 0
                if mesh_elevated_only and elevation_mask == 0:
                    continue

                resource_id = mesh_base + elevation_mask
                mesh_counters[resource_id] += 1
                image = mesh_images[resource_id - MESH_RESOURCE_BASE]
                # square_to_isometric_anchor() returns world_x - 32. The
                # previous diagnostic omitted this left-edge adjustment.
                anchor_x = margin + (local_x - local_y + height - 1) * 32 - 32
                anchor_y = margin + (local_x + local_y) * 16 - 10 * (
                    square.offset if square.offset >= 0 else 0
                )
                for image_y in range(image.height):
                    for image_x in range(image.width):
                        pixel_index = image.pixels[image_y * image.width + image_x]
                        if pixel_index == 0:
                            continue
                        put_pixel(
                            anchor_x + image_x,
                            anchor_y + image_y,
                            mesh_palette[pixel_index],
                            mesh_alpha,
                        )

    return output_width, output_height, bytes(canvas), counters, mesh_counters


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("scenario", type=Path)
    parser.add_argument("data_dir", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--x", type=int, default=49)
    parser.add_argument("--y", type=int, default=34)
    parser.add_argument("--width", type=int, default=80)
    parser.add_argument("--height", type=int, default=60)
    parser.add_argument(
        "--mesh-state",
        type=int,
        default=None,
        help="diagnostic runtime overlay state (0..5); omitted for bare terrain",
    )
    parser.add_argument(
        "--mesh-elevated-only",
        action="store_true",
        help="with --mesh-state, draw only squares whose SQUA +0x0A is non-zero",
    )
    parser.add_argument(
        "--land-palette",
        choices=("auto", "S", "N", "A", "W"),
        default="auto",
        help="P_Land palette; auto follows the scenario start month",
    )
    args = parser.parse_args()

    if args.x < 0 or args.y < 0 or args.x + args.width > MAP_WIDTH or args.y + args.height > MAP_HEIGHT:
        raise ValueError("requested region is outside the 200x200 map")

    squares = read_squares(args.scenario)
    land_images = parse_image_stream(args.data_dir / "G_LLand.s9")
    land_palette_name = select_land_palette_name(args.scenario, args.land_palette)
    land_palettes = parse_palettes(
        args.data_dir / f"P_Land_{land_palette_name}.s9"
    )
    mask_images = parse_image_stream(args.data_dir / "G_MASK.s9")
    mask_palettes = parse_palettes(args.data_dir / "P_MASK.s9")
    mesh_images = parse_image_stream(args.data_dir / "G_MESH.s9")
    mesh_palettes = parse_palettes(args.data_dir / "P_MESH.s9")
    if len(land_images) != 771:
        raise ValueError(f"expected 771 G_LLand resources, found {len(land_images)}")
    if len(mask_images) != 162:
        raise ValueError(f"expected 162 G_MASK resources, found {len(mask_images)}")
    if len(mask_palettes) != 1:
        raise ValueError(f"expected one P_MASK palette, found {len(mask_palettes)}")
    if len(mesh_images) != MESH_RESOURCE_COUNT:
        raise ValueError(
            f"expected {MESH_RESOURCE_COUNT} G_MESH resources, found {len(mesh_images)}"
        )
    if len(mesh_palettes) != 1:
        raise ValueError(f"expected one P_MESH palette, found {len(mesh_palettes)}")

    width, height, rgb, resource_counts, mesh_counts = render_region(
        squares,
        land_images,
        land_palettes,
        mask_images,
        mask_palettes[0],
        mesh_images,
        mesh_palettes[0],
        args.x,
        args.y,
        args.width,
        args.height,
        args.mesh_state,
        args.mesh_elevated_only,
    )
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_bytes(encode_rgb_png(width, height, rgb))

    print(f"scenario={args.scenario}")
    print(f"region=x{args.x}:y{args.y}:w{args.width}:h{args.height}")
    print(f"output={args.output.resolve()}")
    print(f"dimensions={width}x{height}")
    print(f"resource_ids={len(resource_counts)}")
    print(f"fallback_squares={resource_counts[-1]}")
    print(f"mesh_state={args.mesh_state}")
    print(f"mesh_resource_ids={len(mesh_counts)}")
    print(f"mesh_squares={sum(mesh_counts.values())}")
    print(f"land_palette=P_Land_{land_palette_name}.s9")


if __name__ == "__main__":
    main()

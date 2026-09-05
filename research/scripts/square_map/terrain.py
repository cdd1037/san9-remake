"""Terrain, bridge, mask, sorting, and isometric coordinate rules."""

from __future__ import annotations

import collections

from .scenario import (
    BRIDGE_RESOURCE_BASE,
    BRIDGE_RESOURCE_COUNT,
    LAND_RESOURCE_BASE,
    MAP_HEIGHT,
    MAP_WIDTH,
    MASK_EDGE_FLAG1_BASE,
    MASK_EDGE_FLAG2_BASE,
    MASK_RESOURCE_BASE,
    Square,
)

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


def select_bridge_resource(square: Square) -> int | None:
    """Return the confirmed G_LHashi resource for a bridge square."""

    if square.terrain != 11 or not (square.flags & 0x0200):
        return None
    if not 0 <= square.bridge_orientation_code < BRIDGE_RESOURCE_COUNT:
        return None
    return BRIDGE_RESOURCE_BASE + square.bridge_orientation_code

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
    (10006, 10197, 0),
    (10198, 10254, 1),
    (10255, 10311, 2),
    (10312, 10314, 3),
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
    (10331, 10522, 8),
    (10523, 10524, 9),
    (10525, 10716, 10),
    (10717, 10773, 11),
    (10774, 10776, 12),
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


def land_source_x(
    source_origin_x: int,
    run_start_x: int,
    pixel_x: int,
    image_width: int,
) -> int:
    """Return the source X for one continuous land-texture scanline run."""

    source_period = max(1, image_width - 64)
    source_run_start = (source_origin_x + run_start_x) % source_period
    return source_run_start + pixel_x - run_start_x


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


def calculate_elevation_transition_mask(
    squares: list[Square],
    x: int,
    y: int,
    width: int = MAP_WIDTH,
    height: int = MAP_HEIGHT,
) -> int:
    """Rebuild ``squa +0x0A`` from the surrounding base offsets.

    Each bit is one tile corner. A corner rises when any of the three neighbors
    touching it has a greater base offset. Coordinates outside the map have
    offset ``-1`` and therefore never raise a border corner.
    """

    def offset_at(sample_x: int, sample_y: int) -> int:
        if not 0 <= sample_x < width or not 0 <= sample_y < height:
            return -1
        return squares[sample_y * width + sample_x].offset

    current = offset_at(x, y)
    mask = 0
    corner_neighbors = (
        (0x01, ((0, -1), (-1, 0), (-1, -1))),
        (0x08, ((0, -1), (1, 0), (1, -1))),
        (0x02, ((0, 1), (1, 0), (1, 1))),
        (0x04, ((0, 1), (-1, 0), (-1, 1))),
    )
    for bit, neighbors in corner_neighbors:
        if any(offset_at(x + dx, y + dy) > current for dx, dy in neighbors):
            mask |= bit
    return mask


def calculate_terrain_transition_mask(
    squares: list[Square],
    x: int,
    y: int,
    width: int = MAP_WIDTH,
    height: int = MAP_HEIGHT,
) -> int:
    """Rebuild ``squa +0x07`` from the confirmed land-atlas ordering key.

    Cardinal neighbors contribute when their complete atlas key is greater
    than the current square's. A diagonal contributes only when it is also
    strictly greater than both cardinal neighbors touching that corner. This
    makes bridge/water behavior fall out of the shared ordering table instead
    of requiring a terrain-specific compatibility branch.
    """

    def atlas_at(sample_x: int, sample_y: int) -> int:
        if not 0 <= sample_x < width or not 0 <= sample_y < height:
            return -1
        return calculate_land_atlas_index(squares[sample_y * width + sample_x])

    current = atlas_at(x, y)
    mask = 0
    cardinal_neighbors = (
        (0x80, 0, -1),
        (0x40, 0, 1),
        (0x20, -1, 0),
        (0x10, 1, 0),
    )
    for bit, dx, dy in cardinal_neighbors:
        if atlas_at(x + dx, y + dy) > current:
            mask |= bit

    diagonal_neighbors = (
        (0x01, -1, -1, ((0, -1), (-1, 0))),
        (0x02, 1, -1, ((0, -1), (1, 0))),
        (0x04, 1, 1, ((0, 1), (1, 0))),
        (0x08, -1, 1, ((0, 1), (-1, 0))),
    )
    for bit, dx, dy, adjacent in diagonal_neighbors:
        diagonal = atlas_at(x + dx, y + dy)
        if diagonal > current and all(
            diagonal > atlas_at(x + ax, y + ay) for ax, ay in adjacent
        ):
            mask |= bit
    return mask


LandDrawCall = tuple[int, Square, int, int, int, int]


def sort_land_draw_calls(draw_calls: list[LandDrawCall]) -> list[LandDrawCall]:
    """Reproduce the original linked-list insertion order efficiently.

    The list remains ordered by field +0x20. For equal keys, the original
    inserts before the equal-key group when the key is below the current
    head/tail midpoint, and after it otherwise. Grouping calls in deques keeps
    that exact tie behavior without reproducing the linked list's O(n²) scan.
    """

    groups: dict[int, collections.deque[LandDrawCall]] = collections.defaultdict(
        collections.deque
    )
    minimum_key: int | None = None
    maximum_key: int | None = None
    for call in draw_calls:
        key = call[0]
        if minimum_key is None or maximum_key is None:
            groups[key].append(call)
            minimum_key = maximum_key = key
            continue

        midpoint = (minimum_key + maximum_key) // 2
        if key >= midpoint:
            groups[key].append(call)
        else:
            groups[key].appendleft(call)
        minimum_key = min(minimum_key, key)
        maximum_key = max(maximum_key, key)

    return [call for key in sorted(groups) for call in groups[key]]


def region_square_center(
    local_x: int, local_y: int, region_height: int
) -> tuple[int, int]:
    """Return the original square center in the region-normalized canvas."""

    return (
        (local_x - local_y + region_height - 1) * 32,
        (local_x + local_y) * 16,
    )


def global_square_center(map_x: int, map_y: int) -> tuple[int, int]:
    """Return the original square center in full-map isometric coordinates."""

    return (
        (map_x - map_y + MAP_WIDTH) * 32,
        (map_x + map_y + 1) * 16,
    )


def region_square_anchor(
    local_x: int, local_y: int, region_height: int
) -> tuple[int, int]:
    """Return square_to_isometric_anchor() in the same normalized canvas."""

    center_x, center_y = region_square_center(local_x, local_y, region_height)
    return center_x - 32, center_y - 16


def region_building_base(
    local_x: int,
    local_y: int,
    region_height: int,
    square_offset: int,
) -> tuple[int, int]:
    """Return the original building base after square-height adjustment."""

    if square_offset < 0:
        raise ValueError("building square must have a valid +0x08 offset")
    center_x, center_y = region_square_center(local_x, local_y, region_height)
    return center_x, center_y - 10 * (square_offset - 1)

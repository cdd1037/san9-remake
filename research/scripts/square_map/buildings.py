"""Building resource and composition selection rules."""

from __future__ import annotations

from .building_compositions import BUILDING_COMPOSITIONS

from .scenario import (
    BUILDING_ADD_RESOURCE_BASE,
    BUILDING_ADD_RESOURCE_COUNT,
    BuildingSnapshot,
    BuildingSpriteRecord,
    FixedBuilding,
)


def durability_frame(
    durability: int, max_durability: int, frame_count: int
) -> int | None:
    """Quantize a scenario durability snapshot into a static damage frame."""

    if frame_count <= 0 or durability == 0xFFFF or max_durability <= 0:
        return None
    ratio = max(0.0, min(1.0, durability / max_durability))
    frame = int((1.0 - ratio) * frame_count)
    return min(frame_count - 1, max(0, frame))


BUILDING_PALETTE_RANGES = (
    (5663, 5673, 0),
    (5674, 5684, 1),
    (5685, 5695, 2),
    (5696, 5737, 3),
    (5738, 5842, 4),
    (5843, 5946, 5),
)


def building_palette_index_for_resource(resource_id: int) -> int | None:
    for first, last, palette_index in BUILDING_PALETTE_RANGES:
        if first <= resource_id <= last:
            return palette_index
    return None


FIXED_BUILDING_BASE_COMPOSITION = (
    0,
    15,
    19,
    24,
    29,
    34,
    39,
    43,
    48,
    53,
    58,
    62,
    66,
    71,
    76,
    81,
    86,
)
FOUR_FRAME_FIXED_BUILDING_CODES = frozenset(
    (0, 2, 3, 4, 5, 7, 9, 12, 13, 16)
)


def building_resource_location(resource_id: int) -> tuple[str, int] | None:
    """Return (resource stream, palette index) for a building sprite."""

    palette_index = building_palette_index_for_resource(resource_id)
    if palette_index is not None:
        return "G_LBUILD", palette_index
    add_index = resource_id - BUILDING_ADD_RESOURCE_BASE
    if 0 <= add_index < BUILDING_ADD_RESOURCE_COUNT:
        return "G_LBAdd", add_index // 4
    return None


def fixed_building_damage_frame(
    fixed_building: FixedBuilding, durability: int
) -> int | None:
    """Apply the original fixed-building durability thresholds."""

    max_durability = fixed_building.max_durability
    if durability == 0xFFFF or max_durability <= 0:
        return None
    frame_count = 4 if fixed_building.code in FOUR_FRAME_FIXED_BUILDING_CODES else 3
    if durability * 100 < max_durability * 20:
        return 0
    if frame_count == 4 and durability * 100 < max_durability * 40:
        return 1
    if durability * 100 < max_durability * 60:
        return 1 if frame_count == 3 else 2
    return 2 if frame_count == 3 else 3


def city_composition_style(city_terrain_code: int | None) -> int:
    """Map city terrain/appearance code to the three city sprite groups."""

    if city_terrain_code == 1:
        return 2
    if city_terrain_code == 2:
        return 0
    return 1


def city_development_frame(
    current_income: int | None, current_harvest: int | None
) -> int:
    """Apply the original three-step city development resource selector."""

    if current_income is None or current_harvest is None:
        return 0
    development = max(current_income, current_harvest)
    if development < 301:
        return 0
    if development < 601:
        return 1
    return 2


def building_sprite_resource_id(
    snapshot: BuildingSnapshot, sprite: BuildingSpriteRecord
) -> int:
    """Resolve a table resource-offset flag against a city snapshot."""

    if not sprite.resource_offset_flag or not snapshot.is_city:
        return sprite.resource_id
    return sprite.resource_id + city_development_frame(
        snapshot.current_income, snapshot.current_harvest
    )


def select_building_composition(
    fixed_building: FixedBuilding,
    durability: int,
    city_terrain_code: int | None,
) -> int | None:
    """Select the original multi-sprite composition table entry."""

    if not 0 <= fixed_building.code < len(FIXED_BUILDING_BASE_COMPOSITION):
        return None
    frame = fixed_building_damage_frame(fixed_building, durability)
    if frame is None:
        return None
    if fixed_building.code == 0:
        composition_id = 1 + city_composition_style(city_terrain_code) * 5 + frame
    else:
        composition_id = FIXED_BUILDING_BASE_COMPOSITION[fixed_building.code] + 1 + frame
    return composition_id if composition_id in BUILDING_COMPOSITIONS else None

"""Static building-layer rendering for scenario map exports."""

from __future__ import annotations

import collections

from .canvas import RgbCanvas
from .building_compositions import BUILDING_COMPOSITIONS
from .buildings import (
    building_resource_location,
    building_sprite_resource_id,
    select_building_composition,
)
from .resources import BuildingRenderResources
from .scenario import (
    BUILDING_ADD_RESOURCE_BASE,
    BUILDING_DYNAMIC_ALPHA,
    BUILDING_RESOURCE_BASE,
    MAP_WIDTH,
    BuildingSnapshot,
    FixedBuilding,
    Square,
)
from .terrain import region_building_base

def draw_buildings(
    squares: list[Square],
    fixed_buildings: list[FixedBuilding],
    snapshots: list[BuildingSnapshot],
    resources: BuildingRenderResources,
    x0: int,
    y0: int,
    width: int,
    height: int,
    margin: int,
    canvas: RgbCanvas,
) -> collections.Counter[int]:
    """Draw the frozen building snapshot in original composition order."""

    counters: collections.Counter[int] = collections.Counter()
    for snapshot in sorted(snapshots, key=lambda item: (item.x + item.y, item.x)):
        if not (x0 - 5 <= snapshot.x < x0 + width + 5):
            continue
        if not (y0 - 5 <= snapshot.y < y0 + height + 5):
            continue

        fixed_building = fixed_buildings[snapshot.fixed_building_code]
        composition_id = select_building_composition(
            fixed_building,
            snapshot.durability,
            snapshot.city_terrain_code,
        )
        if composition_id is None:
            counters[-1] += 1
            continue
        counters[composition_id] += 1

        square = squares[snapshot.y * MAP_WIDTH + snapshot.x]
        base_x, base_y = region_building_base(
            snapshot.x - x0,
            snapshot.y - y0,
            height,
            square.offset,
        )
        for sprite in BUILDING_COMPOSITIONS[composition_id]:
            resource_id = building_sprite_resource_id(snapshot, sprite)
            location = building_resource_location(resource_id)
            if location is None:
                continue
            stream_name, palette_index = location
            if stream_name == "G_LBUILD":
                if not 0 <= palette_index < len(resources.palettes):
                    continue
                image = resources.images[resource_id - BUILDING_RESOURCE_BASE]
                palette = resources.palettes[palette_index]
                palette_alphas = (
                    resources.palette_alphas[palette_index]
                    if sprite.dynamic_flag
                    and palette_index < len(resources.palette_alphas)
                    else None
                )
            else:
                if not 0 <= palette_index < len(resources.add_palettes):
                    continue
                image = resources.add_images[
                    resource_id - BUILDING_ADD_RESOURCE_BASE
                ]
                palette = resources.add_palettes[palette_index]
                palette_alphas = (
                    resources.add_palette_alphas[palette_index]
                    if sprite.dynamic_flag
                    and palette_index < len(resources.add_palette_alphas)
                    else None
                )

            canvas.draw_indexed(
                image,
                palette,
                margin + base_x + sprite.offset_x,
                margin + base_y + sprite.offset_y,
                BUILDING_DYNAMIC_ALPHA
                if sprite.dynamic_flag and palette_alphas is None
                else 255,
                palette_alphas,
            )

    return counters

"""Pixel composition for terrain and map-object layers."""

from __future__ import annotations

import collections

from .canvas import RgbCanvas
from .boundaries import draw_boundaries
from .render_buildings import draw_buildings
from .resources import MapVisualResources
from .scenario import (
    BRIDGE_RESOURCE_BASE,
    LAND_RESOURCE_BASE,
    MAP_HEIGHT,
    MAP_WIDTH,
    MASK_RESOURCE_BASE,
    MESH_RESOURCE_BASE,
    MESH_STATE_ALPHA,
    MESH_STATE_BASES,
    BuildingSnapshot,
    FixedBuilding,
    Square,
)
from .terrain import (
    EDGE_RECORDS,
    MASK_POSITION_TABLE,
    MASK_VERTICAL_ADJUST,
    LandDrawCall,
    blend_color,
    calculate_land_atlas_index,
    global_square_center,
    land_source_x,
    palette_index_for_resource,
    region_square_anchor,
    region_square_center,
    select_bridge_resource,
    select_edge_mask_flags,
    select_land_resource,
    select_mask_resource,
    sort_land_draw_calls,
    terrain_fallback_color,
)


def render_region(
    squares: list[Square],
    resources: MapVisualResources,
    x0: int,
    y0: int,
    width: int,
    height: int,
    mesh_state: int | None = None,
    mesh_elevated_only: bool = False,
    draw_boundary_layer: bool = False,
    fixed_buildings: list[FixedBuilding] | None = None,
    building_snapshots: list[BuildingSnapshot] | None = None,
) -> tuple[
    int,
    int,
    bytes,
    collections.Counter[int],
    collections.Counter[int],
    collections.Counter[int],
    collections.Counter[int],
    collections.Counter[int],
]:
    margin = 128
    output_width = (width + height) * 32 + margin * 2
    output_height = (width + height) * 16 + margin * 2
    canvas = RgbCanvas(output_width, output_height)
    counters: collections.Counter[int] = collections.Counter()
    mesh_counters: collections.Counter[int] = collections.Counter()
    bridge_counters: collections.Counter[int] = collections.Counter()
    boundary_counters: collections.Counter[int] = collections.Counter()
    building_counters: collections.Counter[int] = collections.Counter()

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
            palette = resources.land_palettes[0]
        else:
            counters[resource_id] += 1
            image = resources.land_images[resource_id - LAND_RESOURCE_BASE]
            palette_index = palette_index_for_resource(resource_id)
            palette = (
                resources.land_palettes[palette_index]
                if palette_index is not None
                and palette_index < len(resources.land_palettes)
                else resources.land_palettes[0]
            )

        mask_resource_id = select_mask_resource(
            elevation_mask, record_type, mask_flags
        )
        mask = resources.mask_images[mask_resource_id - MASK_RESOURCE_BASE]
        position_x, position_y = MASK_POSITION_TABLE[9 * elevation_mask + record_type]
        world_center_x, world_center_y = region_square_center(
            map_x - x0, map_y - y0, height
        )
        target_x = margin + world_center_x + position_x - 64
        target_y = (
            margin
            + world_center_y
            + position_y
            - MASK_VERTICAL_ADJUST[elevation_mask]
            - 10 * offset_code
            - 54
        )
        source_center_x, source_center_y = global_square_center(map_x, map_y)
        source_x_origin = source_center_x + position_x - 64
        source_y_origin = (
            source_center_y
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
                target_weight = resources.mask_palette[mask_index][0]
                color = fallback
                if image is not None:
                    run_start_x = first_column if record_type == 0 else 0
                    source_x = land_source_x(
                        source_x_origin,
                        run_start_x,
                        mask_x,
                        image.width,
                    )
                    source_y = (source_y_origin + mask_y) % image.height
                    pixel_index = image.pixels[source_y * image.width + source_x]
                    color = palette[pixel_index]
                if record_type == 0:
                    canvas.put(target_pixel_x, target_pixel_y, color)
                else:
                    canvas.put_mask_edge(
                        target_pixel_x, target_pixel_y, color, target_weight
                    )

    # build_square_edge_overlays() appends the eight edge records first and the
    # current-square record (type 9) last. The original then reinserts every
    # record into a sorted linked list; equal keys are not globally stable.
    draw_calls: list[LandDrawCall] = []

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

    for _, square, map_x, map_y, record_type, mask_flags in sort_land_draw_calls(
        draw_calls
    ):
        draw_record(square, map_x, map_y, record_type, mask_flags)

    # draw_visible_bridge_overlays() is a confirmed static layer: it accepts
    # terrain 11 squares carrying flags bit 9 and uses SQUA +0x0C as the
    # north-south/east-west G_LHashi selector. Its draw call uses the
    # square_to_isometric_anchor() result shifted left by 64 pixels.
    for local_y in range(height):
        for local_x in range(width):
            square = squares[(y0 + local_y) * MAP_WIDTH + x0 + local_x]
            resource_id = select_bridge_resource(square)
            if resource_id is None:
                continue
            bridge_counters[resource_id] += 1
            image = resources.bridge_images[resource_id - BRIDGE_RESOURCE_BASE]
            anchor_x, anchor_y = region_square_anchor(local_x, local_y, height)
            canvas.draw_indexed(
                image,
                resources.bridge_palette,
                margin + anchor_x - 64,
                margin + anchor_y,
            )

    if draw_boundary_layer:
        if resources.boundaries is None:
            raise ValueError("boundary resources were not loaded")
        boundary_counters = draw_boundaries(
            canvas,
            squares,
            resources.boundaries,
            x0,
            y0,
            width,
            height,
            margin,
        )

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
                image = resources.mesh_images[resource_id - MESH_RESOURCE_BASE]
                anchor_x, anchor_y = region_square_anchor(
                    local_x, local_y, height
                )
                anchor_y -= 10 * (square.offset if square.offset >= 0 else 0)
                for image_y in range(image.height):
                    for image_x in range(image.width):
                        pixel_index = image.pixels[image_y * image.width + image_x]
                        if pixel_index == 0:
                            continue
                        canvas.put(
                            margin + anchor_x + image_x,
                            margin + anchor_y + image_y,
                            resources.mesh_palette[pixel_index],
                            mesh_alpha,
                        )

    if fixed_buildings is not None and building_snapshots is not None:
        building_counters = draw_buildings(
            squares,
            fixed_buildings,
            building_snapshots,
            resources.buildings,
            x0,
            y0,
            width,
            height,
            margin,
            canvas,
        )

    return (
        output_width,
        output_height,
        canvas.to_bytes(),
        counters,
        mesh_counters,
        bridge_counters,
        boundary_counters,
        building_counters,
    )

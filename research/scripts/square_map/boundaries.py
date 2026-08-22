"""Selection and drawing of the optional scenario boundary layer."""

from __future__ import annotations

import collections
from dataclasses import dataclass

from .canvas import RgbCanvas
from .resources import BoundaryRenderResources
from .scenario import BOUNDARY_RESOURCE_BASE, MAP_WIDTH, Square
from .terrain import region_square_anchor


@dataclass(frozen=True)
class BoundaryDirection:
    bit: int
    offset_x: int
    offset_y: int
    primary_elevation_bit: int
    secondary_elevation_bit: int
    uses_first_resource_triplet: bool


# IDA 0x65E778 table consumed by build_square_boundary_segments (0x5191D0).
# The direction order is north, east, south, west, matching squa +0x0D.
BOUNDARY_DIRECTIONS = (
    BoundaryDirection(0x01, 32, 0, 0x01, 0x08, False),
    BoundaryDirection(0x02, 32, 16, 0x08, 0x02, True),
    BoundaryDirection(0x04, 0, 16, 0x04, 0x02, False),
    BoundaryDirection(0x08, 0, 0, 0x01, 0x04, True),
)


def select_boundary_resource(
    elevation_mask: int,
    low_boundary_mask: int,
    high_boundary_mask: int,
    direction: BoundaryDirection,
) -> tuple[int, bool] | None:
    """Select a pale region or red city boundary segment and its vertical state."""

    primary_elevated = bool(elevation_mask & direction.primary_elevation_bit)
    secondary_elevated = bool(elevation_mask & direction.secondary_elevation_bit)
    if primary_elevated:
        shape = 0 if secondary_elevated else 1
    elif secondary_elevated:
        shape = 2
    else:
        shape = 0

    if high_boundary_mask & direction.bit:
        style_base = 10800
    elif low_boundary_mask & direction.bit:
        style_base = 10794
    else:
        return None

    orientation_offset = 0 if direction.uses_first_resource_triplet else 3
    return style_base + orientation_offset + shape, primary_elevated


def draw_boundaries(
    canvas: RgbCanvas,
    squares: list[Square],
    resources: BoundaryRenderResources,
    x0: int,
    y0: int,
    width: int,
    height: int,
    margin: int,
) -> collections.Counter[int]:
    """Draw serialized scenario boundaries over the rendered terrain region."""

    counters: collections.Counter[int] = collections.Counter()
    for local_y in range(height):
        for local_x in range(width):
            square = squares[(y0 + local_y) * MAP_WIDTH + x0 + local_x]
            boundary_mask = square.boundary_mask if square.boundary_mask >= 0 else 0
            if boundary_mask == 0:
                continue
            elevation_mask = (
                square.elevation_mask if 0 <= square.elevation_mask < 16 else 0
            )
            low_boundary_mask = boundary_mask & 0x0F
            high_boundary_mask = (boundary_mask >> 4) & 0x0F
            anchor_x, anchor_y = region_square_anchor(local_x, local_y, height)
            offset_code = square.offset if square.offset >= 0 else 0

            for direction in BOUNDARY_DIRECTIONS:
                selection = select_boundary_resource(
                    elevation_mask,
                    low_boundary_mask,
                    high_boundary_mask,
                    direction,
                )
                if selection is None:
                    continue
                resource_id, primary_elevated = selection
                counters[resource_id] += 1
                image = resources.images[resource_id - BOUNDARY_RESOURCE_BASE]
                canvas.draw_indexed(
                    image,
                    resources.palette,
                    margin + anchor_x + direction.offset_x,
                    margin
                    + anchor_y
                    + direction.offset_y
                    + 10 * (int(not primary_elevated) - offset_code),
                )
    return counters

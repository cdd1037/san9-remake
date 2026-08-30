"""Confirmed static portions of the original main-map edge-cloud renderer."""

from __future__ import annotations

from collections.abc import Callable, Sequence
from dataclasses import dataclass

from .canvas import RgbCanvas
from .resources import CloudRenderResources
from .scenario import MAP_HEIGHT, MAP_WIDTH, Square
from .terrain import region_square_anchor

CLOUD_RESOURCE_BASE = 10777
CLOUD_FILL_RESOURCE = 10783
CLOUD_FILL_PALETTE = 1
CLOUD_OBSTRUCTED_PALETTE = 0
CLOUD_FOREGROUND_PALETTE = 4
CLOUD_REPEAT_X = 1536
CLOUD_REPEAT_Y = 768
# render_region normalizes the original full-map world rectangle by (-32,-16).
FULL_MAP_ORIGIN_X = -32
FULL_MAP_ORIGIN_Y = -16


@dataclass(frozen=True)
class CloudPlacement:
    resource_id: int
    x: int
    y: int


@dataclass(frozen=True)
class EdgeSample:
    offset: int
    is_water: bool


# Each table is one 1536x768 period along a side of the map diamond.  These
# triples are the stack tables consumed by draw_map_edge_clouds's side loops.
NORTH_WEST_CLOUDS = (
    CloudPlacement(10788, -322, -104),
    CloudPlacement(10790, -536, 0),
    CloudPlacement(10788, -736, 0),
    CloudPlacement(10789, -592, 120),
    CloudPlacement(10790, -1312, 160),
    CloudPlacement(10789, -1104, 160),
    CloudPlacement(10788, -888, 216),
    CloudPlacement(10789, -1064, 232),
    CloudPlacement(10789, -1064, 312),
    CloudPlacement(10790, -1360, 424),
    CloudPlacement(10790, -1536, 504),
    CloudPlacement(10789, -1536, 568),
)

NORTH_EAST_CLOUDS = (
    CloudPlacement(10793, 1200, 40),
    CloudPlacement(10793, 164, 50),
    CloudPlacement(10792, 288, 120),
    CloudPlacement(10792, 880, 120),
    CloudPlacement(10791, 456, 200),
    CloudPlacement(10793, 720, 320),
    CloudPlacement(10791, 1176, 336),
    CloudPlacement(10793, 936, 424),
    CloudPlacement(10792, 1096, 496),
    CloudPlacement(10792, 1208, 568),
    CloudPlacement(10792, 1360, 616),
    CloudPlacement(10791, 1488, 696),
)

SOUTH_WEST_CLOUDS = (
    CloudPlacement(10793, 8, 0),
    CloudPlacement(10792, 232, 120),
    CloudPlacement(10791, 400, 208),
    CloudPlacement(10793, 16, 216),
    CloudPlacement(10793, 608, 328),
    CloudPlacement(10792, 360, 360),
    CloudPlacement(10792, 792, 376),
    CloudPlacement(10792, 880, 460),
    CloudPlacement(10793, 1032, 536),
    CloudPlacement(10791, 240, 560),
    CloudPlacement(10791, 1208, 616),
    CloudPlacement(10792, 1408, 688),
)

SOUTH_EAST_CLOUDS = (
    CloudPlacement(10789, -48, -128),
    CloudPlacement(10788, -328, -40),
    CloudPlacement(10790, -464, 56),
    CloudPlacement(10789, -552, 136),
    CloudPlacement(10788, -808, 232),
    CloudPlacement(10790, -920, 288),
    CloudPlacement(10788, -1128, 392),
    CloudPlacement(10789, -1128, 412),
    CloudPlacement(10790, -912, 424),
    CloudPlacement(10788, -416, 440),
    CloudPlacement(10789, -1264, 504),
    CloudPlacement(10790, -1464, 552),
    CloudPlacement(10789, -1096, 632),
)


def _edge_position(
    squares: list[Square], x: int, y: int, direction: int, margin: int
) -> tuple[int, int]:
    anchor_x, anchor_y = region_square_anchor(x, y, MAP_HEIGHT)
    direction_offsets = {
        1: (32, 0),
        2: (32, 32),
        4: (0, 16),
        8: (64, 16),
    }
    dx, dy = direction_offsets[direction]
    square = squares[y * MAP_WIDTH + x]
    offset = max(0, square.offset)
    elevated_edge = 1 if square.elevation_mask & direction else 0
    return (
        margin + anchor_x + dx,
        margin + anchor_y + dy + 10 - 10 * (offset + elevated_edge),
    )


def build_edge_polylines(
    squares: list[Square], margin: int
) -> tuple[list[tuple[int, int]], list[tuple[int, int]]]:
    """Build the two scanline boundaries used by draw_map_edge_clouds."""

    left: list[tuple[int, int]] = []
    right: list[tuple[int, int]] = []
    last_sum = MAP_WIDTH + MAP_HEIGHT - 2
    corner_sum = MAP_HEIGHT - 1
    for diagonal in range(last_sum + 1):
        if diagonal < corner_sum:
            left.append(_edge_position(squares, 0, diagonal, 1, margin))
            right.append(_edge_position(squares, diagonal, 0, 1, margin))
        elif diagonal == corner_sum:
            left.append(_edge_position(squares, 0, diagonal, 1, margin))
            left.append(_edge_position(squares, 0, diagonal, 4, margin))
            right.append(_edge_position(squares, diagonal, 0, 1, margin))
            right.append(_edge_position(squares, diagonal, 0, 8, margin))
        else:
            x = diagonal - corner_sum
            left.append(_edge_position(squares, x, MAP_HEIGHT - 1, 4, margin))
            right.append(_edge_position(squares, MAP_WIDTH - 1, x, 8, margin))
    return left, right


def build_edge_samples(
    squares: list[Square],
) -> tuple[list[EdgeSample], list[EdgeSample]]:
    """Return the west/south and north/east samples indexed by x+y."""

    left: list[EdgeSample] = []
    right: list[EdgeSample] = []
    last_sum = MAP_WIDTH + MAP_HEIGHT - 2
    corner_sum = MAP_HEIGHT - 1
    for diagonal in range(last_sum + 1):
        if diagonal <= corner_sum:
            left_square = squares[diagonal * MAP_WIDTH]
            right_square = squares[diagonal]
        else:
            edge_index = diagonal - corner_sum
            left_square = squares[(MAP_HEIGHT - 1) * MAP_WIDTH + edge_index]
            right_square = squares[edge_index * MAP_WIDTH + MAP_WIDTH - 1]
        left.append(
            EdgeSample(
                max(0, left_square.offset),
                left_square.terrain in (4, 7),
            )
        )
        right.append(
            EdgeSample(
                max(0, right_square.offset),
                right_square.terrain in (4, 7),
            )
        )
    return left, right


def _polyline_x_by_y(points: list[tuple[int, int]]) -> dict[int, int]:
    result: dict[int, int] = {}
    for (x0, y0), (x1, y1) in zip(points, points[1:]):
        if y0 == y1:
            result[y0] = (x0 + x1) // 2
            continue
        start_y, end_y = sorted((y0, y1))
        for y in range(start_y, end_y + 1):
            x = x0 + (y - y0) * (x1 - x0) // (y1 - y0)
            result[y] = x
    return result


def _draw_cloud_fill(
    canvas: RgbCanvas,
    resources: CloudRenderResources,
    left_edge: list[tuple[int, int]],
    right_edge: list[tuple[int, int]],
    margin: int,
) -> None:
    image = resources.images[CLOUD_FILL_RESOURCE - CLOUD_RESOURCE_BASE]
    palette = resources.palettes[CLOUD_FILL_PALETTE]
    tile_rows = [
        b"".join(
            bytes(palette[pixel])
            for pixel in image.pixels[
                y * image.width : (y + 1) * image.width
            ]
        )
        for y in range(image.height)
    ]
    left_by_y = _polyline_x_by_y(left_edge)
    right_by_y = _polyline_x_by_y(right_edge)
    first_y = max(0, min(min(left_by_y), min(right_by_y)))
    last_y = min(canvas.height - 1, max(max(left_by_y), max(right_by_y)))

    for target_y in range(canvas.height):
        if target_y < first_y or target_y > last_y:
            runs = ((0, canvas.width),)
        else:
            left_x = left_by_y.get(target_y)
            right_x = right_by_y.get(target_y)
            if left_x is None or right_x is None:
                continue
            runs = ((0, max(0, left_x)), (min(canvas.width, right_x), canvas.width))
        source_y = (target_y - margin - FULL_MAP_ORIGIN_Y) % image.height
        tile_row = tile_rows[source_y]
        for start_x, end_x in runs:
            target_x = start_x
            while target_x < end_x:
                source_x = (target_x - margin - FULL_MAP_ORIGIN_X) % image.width
                count = min(end_x - target_x, image.width - source_x)
                source_start = source_x * 3
                source_end = source_start + count * 3
                target_start = (target_y * canvas.width + target_x) * 3
                canvas.pixels[target_start : target_start + count * 3] = (
                    tile_row[source_start:source_end]
                )
                target_x += count


def _adjust_foreground_y(
    world_y: int,
    samples: list[EdgeSample],
    north_side: bool,
) -> int:
    """Apply the original 16-sample edge-height formula."""

    start = (world_y + (400 if north_side else 0)) // 16
    end = min(start + 15, len(samples) - 1)
    start = max(0, start)
    sample_count = max(1, end - start + 1)
    # The original sums [start,end) but divides by the inclusive span. The
    # extra one in the numerator is the flat-edge baseline.
    offset_sum = sum(sample.offset for sample in samples[start:end])
    return world_y - 10 * (sample_count + offset_sum) // sample_count


def _intersects_any(
    left: int,
    top: int,
    right: int,
    bottom: int,
    bounds: Sequence[tuple[int, int, int, int]],
) -> bool:
    return any(
        left <= obstacle_right
        and obstacle_left <= right
        and top <= obstacle_bottom
        and obstacle_top <= bottom
        for obstacle_left, obstacle_top, obstacle_right, obstacle_bottom in bounds
    )


def _draw_side_clouds(
    canvas: RgbCanvas,
    resources: CloudRenderResources,
    placements: tuple[CloudPlacement, ...],
    base_for_period: Callable[[int], tuple[int, int]],
    samples: list[EdgeSample],
    north_side: bool,
    margin: int,
    obstacle_bounds: Sequence[tuple[int, int, int, int]],
) -> int:
    count = 0
    for period in range(-1, 7):
        base_x, base_y = base_for_period(period)
        for placement in placements:
            world_y = _adjust_foreground_y(
                base_y + placement.y, samples, north_side
            )
            x = margin + FULL_MAP_ORIGIN_X + base_x + placement.x
            y = margin + FULL_MAP_ORIGIN_Y + world_y
            resource_id = placement.resource_id
            palette_index = CLOUD_FOREGROUND_PALETTE
            image = resources.images[resource_id - CLOUD_RESOURCE_BASE]
            if _intersects_any(
                x,
                y,
                x + image.width - 1,
                y + image.height - 1,
                obstacle_bounds,
            ):
                resource_id -= 11
                palette_index = CLOUD_OBSTRUCTED_PALETTE
                image = resources.images[resource_id - CLOUD_RESOURCE_BASE]
            if x >= canvas.width or y >= canvas.height:
                continue
            if x + image.width <= 0 or y + image.height <= 0:
                continue
            canvas.draw_indexed(
                image,
                resources.palettes[palette_index],
                x,
                y,
                palette_alphas=resources.palette_alphas[palette_index],
            )
            count += 1
    return count


def draw_map_edge_cloud_fill(
    canvas: RgbCanvas,
    squares: list[Square],
    resources: CloudRenderResources,
    margin: int,
) -> None:
    """Draw the tiled background cloud outside the terrain diamond."""

    left_edge, right_edge = build_edge_polylines(squares, margin)
    _draw_cloud_fill(canvas, resources, left_edge, right_edge, margin)


def draw_map_edge_cloud_foreground(
    canvas: RgbCanvas,
    squares: list[Square],
    resources: CloudRenderResources,
    margin: int,
    obstacle_bounds: Sequence[tuple[int, int, int, int]] = (),
) -> int:
    """Draw queued foreground clouds after static map visual objects."""

    left_samples, right_samples = build_edge_samples(squares)
    map_mid_x = (MAP_WIDTH + MAP_HEIGHT) * 16
    map_mid_y = (MAP_WIDTH + MAP_HEIGHT) * 8
    map_right = (MAP_WIDTH + MAP_HEIGHT) * 32
    count = 0
    count += _draw_side_clouds(
        canvas,
        resources,
        NORTH_WEST_CLOUDS,
        lambda period: (
            map_mid_x - CLOUD_REPEAT_X * period,
            CLOUD_REPEAT_Y * period,
        ),
        left_samples,
        True,
        margin,
        obstacle_bounds,
    )
    count += _draw_side_clouds(
        canvas,
        resources,
        NORTH_EAST_CLOUDS,
        lambda period: (
            map_mid_x + CLOUD_REPEAT_X * period,
            CLOUD_REPEAT_Y * period,
        ),
        right_samples,
        True,
        margin,
        obstacle_bounds,
    )
    count += _draw_side_clouds(
        canvas,
        resources,
        SOUTH_WEST_CLOUDS,
        lambda period: (
            CLOUD_REPEAT_X * period,
            map_mid_y + CLOUD_REPEAT_Y * period,
        ),
        left_samples,
        False,
        margin,
        obstacle_bounds,
    )
    count += _draw_side_clouds(
        canvas,
        resources,
        SOUTH_EAST_CLOUDS,
        lambda period: (
            map_right - CLOUD_REPEAT_X * period,
            map_mid_y + CLOUD_REPEAT_Y * period,
        ),
        right_samples,
        False,
        margin,
        obstacle_bounds,
    )
    return count

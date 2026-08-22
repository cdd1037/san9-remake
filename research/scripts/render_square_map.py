#!/usr/bin/env python3
"""Render an isometric scenario map from confirmed S9 map resources."""

from __future__ import annotations

import argparse
from pathlib import Path

from square_map.graphics import encode_rgb_png
from square_map.render_map import render_region
from square_map.resources import MapVisualResources
from square_map.scenario import (
    MAP_HEIGHT,
    MAP_WIDTH,
    read_building_snapshot,
    read_squares,
)


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
        "--mesh-state", type=int, default=None,
        help="diagnostic runtime overlay state (0..5); omitted for bare terrain",
    )
    parser.add_argument(
        "--mesh-elevated-only", action="store_true",
        help="with --mesh-state, draw only squares whose SQUA +0x0A is non-zero",
    )
    parser.add_argument(
        "--land-palette", choices=("auto", "S", "N", "A", "W"), default="auto",
        help="P_Land palette; auto follows the scenario start month",
    )
    args = parser.parse_args()

    if (
        args.x < 0 or args.y < 0
        or args.x + args.width > MAP_WIDTH
        or args.y + args.height > MAP_HEIGHT
    ):
        raise ValueError("requested region is outside the 200x200 map")

    squares = read_squares(args.scenario)
    resources = MapVisualResources.load(
        args.data_dir, args.scenario, args.land_palette
    )
    fixed_buildings, building_snapshots = read_building_snapshot(args.scenario)

    (
        width, height, rgb, resource_counts, mesh_counts,
        bridge_counts, building_counts,
    ) = render_region(
        squares, resources, args.x, args.y, args.width, args.height,
        args.mesh_state, args.mesh_elevated_only,
        fixed_buildings=fixed_buildings,
        building_snapshots=building_snapshots,
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
    print(f"bridge_resource_ids={len(bridge_counts)}")
    print(f"bridge_squares={sum(bridge_counts.values())}")
    print(f"building_composition_ids={len(building_counts)}")
    print(f"building_objects={sum(building_counts.values())}")
    print(f"building_fallback_objects={building_counts[-1]}")
    print(f"land_palette=P_Land_{resources.land_palette_name}.s9")


if __name__ == "__main__":
    main()

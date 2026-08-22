"""Loading and validation of the map renderer's visual resource set."""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

from .graphics import parse_image_stream, parse_palette_alphas, parse_palettes
from .scenario import (
    BOUNDARY_RESOURCE_COUNT,
    BRIDGE_PALETTE_FILENAME,
    BRIDGE_PALETTE_INDEX,
    BRIDGE_RESOURCE_COUNT,
    BUILDING_ADD_RESOURCE_COUNT,
    BUILDING_RESOURCE_COUNT,
    MESH_RESOURCE_COUNT,
    ImageRecord,
    select_land_palette_name,
)

Palette = list[tuple[int, int, int]]


@dataclass(frozen=True)
class BuildingRenderResources:
    images: list[ImageRecord]
    palettes: list[Palette]
    palette_alphas: list[list[int]]
    add_images: list[ImageRecord]
    add_palettes: list[Palette]
    add_palette_alphas: list[list[int]]


@dataclass(frozen=True)
class BoundaryRenderResources:
    images: list[ImageRecord]
    palette: Palette


@dataclass(frozen=True)
class MapVisualResources:
    land_images: list[ImageRecord]
    land_palettes: list[Palette]
    mask_images: list[ImageRecord]
    mask_palette: Palette
    mesh_images: list[ImageRecord]
    mesh_palette: Palette
    bridge_images: list[ImageRecord]
    bridge_palette: Palette
    boundaries: BoundaryRenderResources | None
    buildings: BuildingRenderResources
    land_palette_name: str

    @classmethod
    def load(
        cls,
        data_dir: Path,
        scenario: Path,
        requested_land_palette: str,
        include_boundaries: bool = False,
    ) -> MapVisualResources:
        land_palette_name = select_land_palette_name(
            scenario, requested_land_palette
        )
        land_images = parse_image_stream(data_dir / "G_LLand.s9")
        land_palettes = parse_palettes(
            data_dir / f"P_Land_{land_palette_name}.s9"
        )
        mask_images = parse_image_stream(data_dir / "G_MASK.s9")
        mask_palettes = parse_palettes(data_dir / "P_MASK.s9")
        mesh_images = parse_image_stream(data_dir / "G_MESH.s9")
        mesh_palettes = parse_palettes(data_dir / "P_MESH.s9")
        bridge_images = parse_image_stream(data_dir / "G_LHashi.s9")
        bridge_palettes = parse_palettes(data_dir / BRIDGE_PALETTE_FILENAME)
        boundary_images = (
            parse_image_stream(data_dir / "G_MAPMKR.s9")
            if include_boundaries
            else []
        )
        boundary_palettes = (
            parse_palettes(data_dir / "P_MAPMKR.s9")
            if include_boundaries
            else []
        )
        building_images = parse_image_stream(data_dir / "G_LBUILD.s9")
        building_palettes = parse_palettes(data_dir / "P_LBUILD.s9")
        building_alphas = parse_palette_alphas(data_dir / "P_LBUILD.s9")
        add_images = parse_image_stream(data_dir / "G_LBAdd.s9")
        add_palettes = parse_palettes(data_dir / "P_BAdd.s9")
        add_alphas = parse_palette_alphas(data_dir / "P_BAdd.s9")

        expected_counts = (
            ("G_LLand", land_images, 771),
            ("G_MASK", mask_images, 162),
            ("P_MASK", mask_palettes, 1),
            ("G_MESH", mesh_images, MESH_RESOURCE_COUNT),
            ("P_MESH", mesh_palettes, 1),
            ("G_LHashi", bridge_images, BRIDGE_RESOURCE_COUNT),
            ("G_LBUILD", building_images, BUILDING_RESOURCE_COUNT),
            ("P_LBUILD", building_palettes, 6),
            ("G_LBAdd", add_images, BUILDING_ADD_RESOURCE_COUNT),
            ("P_BAdd", add_palettes, 7),
        )
        for resource_name, records, expected in expected_counts:
            if len(records) != expected:
                raise ValueError(
                    f"expected {expected} {resource_name} records, "
                    f"found {len(records)}"
                )
        if include_boundaries:
            if len(boundary_images) != BOUNDARY_RESOURCE_COUNT:
                raise ValueError(
                    f"expected {BOUNDARY_RESOURCE_COUNT} G_MAPMKR records, "
                    f"found {len(boundary_images)}"
                )
            if len(boundary_palettes) != 1:
                raise ValueError(
                    "expected 1 P_MAPMKR palette, "
                    f"found {len(boundary_palettes)}"
                )
        if len(bridge_palettes) <= BRIDGE_PALETTE_INDEX:
            raise ValueError(
                f"expected {BRIDGE_PALETTE_FILENAME} palette index "
                f"{BRIDGE_PALETTE_INDEX}, found {len(bridge_palettes)} palettes"
            )

        return cls(
            land_images,
            land_palettes,
            mask_images,
            mask_palettes[0],
            mesh_images,
            mesh_palettes[0],
            bridge_images,
            bridge_palettes[BRIDGE_PALETTE_INDEX],
            BoundaryRenderResources(boundary_images, boundary_palettes[0])
            if include_boundaries
            else None,
            BuildingRenderResources(
                building_images,
                building_palettes,
                building_alphas,
                add_images,
                add_palettes,
                add_alphas,
            ),
            land_palette_name,
        )

"""Scenario records and shared map-domain models."""

from __future__ import annotations

import struct
from dataclasses import dataclass
from pathlib import Path

MAP_WIDTH = 200
MAP_HEIGHT = 200
SQUARE_RECORD_SIZE = 14
SQUA_SIZE = MAP_WIDTH * MAP_HEIGHT * SQUARE_RECORD_SIZE
SCENARIO_DATE_OFFSET = 0x2C
LAND_RESOURCE_BASE = 10006
MASK_RESOURCE_BASE = 10806
BRIDGE_RESOURCE_BASE = 10004
BRIDGE_RESOURCE_COUNT = 2
# The static resource registry maps G_LHashi IDs 10004/10005 to global palette
# slot 89. P_LndAdd.s9 loads its first palette into slot 89 and its second into
# slot 90 for G_LMask; P_MapEtc.s9 slots 112/113 belong elsewhere.
BRIDGE_PALETTE_FILENAME = "P_LndAdd.s9"
BRIDGE_PALETTE_INDEX = 0
BUILDING_RESOURCE_BASE = 5663
BUILDING_RESOURCE_COUNT = 284
BUILDING_ADD_RESOURCE_BASE = 5947
BUILDING_ADD_RESOURCE_COUNT = 28
BUILDING_DYNAMIC_ALPHA = 64
# BitBltKBMPPaletteAlpha treats the palette's fourth byte as a 0..128 weight.
# P_LBUILD palette 5 happens to use only 0..64, so its strongest entries remain
# approximately half-covered; they are not normalized to full opacity.
KOEI_PALETTE_ALPHA_OPAQUE = 128
MASK_EDGE_FLAG1_BASE = 10950
MASK_EDGE_FLAG2_BASE = 10959
MESH_RESOURCE_BASE = 10972
MESH_RESOURCE_COUNT = 96
MESH_STATE_BASES = (10972, 11004, 11036, 11020, 10988, 11052)
# create_square_mesh_visual() stores (alpha << 8) | 2 at resource +0x14.
# The low byte selects alpha-blend mode 2; the high byte is the source alpha.
MESH_STATE_ALPHA = (50, 25, 20, 22, 20, 20)
BOUNDARY_RESOURCE_BASE = 10794
BOUNDARY_RESOURCE_COUNT = 12


@dataclass(frozen=True)
class Square:
    terrain: int
    edge_mask: int
    offset: int
    elevation_mask: int
    texture_variant: int
    flags: int
    zone_code: int
    bridge_orientation_code: int
    boundary_mask: int


@dataclass(frozen=True)
class FixedBuilding:
    code: int
    max_durability: int
    shape_length: int
    shape_width: int
    appearance_id: int


@dataclass(frozen=True)
class BuildingSnapshot:
    zone_code: int
    fixed_building_code: int
    force_code: int
    city_code: int
    durability: int
    max_durability: int
    x: int
    y: int
    is_city: bool
    city_terrain_code: int | None
    current_income: int | None
    current_harvest: int | None


@dataclass(frozen=True)
class BuildingSpriteRecord:
    """One static record from the original building composition table."""

    dynamic_flag: int
    resource_offset_flag: int
    suppress_flag: int
    resource_id: int
    offset_x: int
    offset_y: int
    z_offset: int


@dataclass(frozen=True)
class ImageRecord:
    width: int
    height: int
    pixels: bytes


def parse_scenario_sections(data: bytes) -> dict[str, bytes]:
    """Read RIFF/S9SC section payloads without interpreting their records."""

    if len(data) < 12 or data[:4] != b"RIFF" or data[8:12] != b"S9SC":
        raise ValueError("scenario is not a RIFF/S9SC archive")
    declared_size = struct.unpack_from("<I", data, 4)[0] + 8
    if declared_size != len(data):
        raise ValueError(
            f"scenario RIFF size {declared_size} != file size {len(data)}"
        )

    sections: dict[str, bytes] = {}
    position = 12
    while position < declared_size:
        if position + 8 > declared_size:
            raise ValueError("truncated scenario section header")
        section_id = data[position : position + 4].decode("ascii")
        section_size = struct.unpack_from("<I", data, position + 4)[0]
        payload_start = position + 8
        payload_end = payload_start + section_size
        if payload_end > declared_size:
            raise ValueError(f"scenario section {section_id} exceeds RIFF")
        if section_id in sections:
            raise ValueError(f"duplicate scenario section {section_id}")
        sections[section_id] = data[payload_start:payload_end]
        position = payload_end + (section_size & 1)
    return sections

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
    section = parse_scenario_sections(path.read_bytes()).get("squa")
    if section is None or len(section) != SQUA_SIZE:
        raise ValueError(f"{path}: SQUARE chunk has an unexpected size")

    records: list[Square] = []
    position = 0
    for _ in range(MAP_WIDTH * MAP_HEIGHT):
        serialized_id, flags, zone = struct.unpack_from("<3H", section, position)
        aux, edge_mask, offset, terrain, elevation_mask, texture_variant, bridge, boundary = (
            section[position + 6 : position + 14]
        )
        del serialized_id, aux
        records.append(
            Square(
                terrain=-1 if terrain == 0xFF else terrain,
                edge_mask=0 if edge_mask == 0xFF else edge_mask,
                offset=-1 if offset == 0xFF else offset,
                elevation_mask=-1 if elevation_mask == 0xFF else elevation_mask,
                texture_variant=-1
                if texture_variant == 0xFF
                else texture_variant,
                flags=0 if flags == 0xFFFF else flags,
                zone_code=-1 if zone == 0xFFFF else zone,
                bridge_orientation_code=-1 if bridge == 0xFF else bridge,
                boundary_mask=-1 if boundary == 0xFF else boundary,
            )
        )
        position += SQUARE_RECORD_SIZE
    return records


def read_building_snapshot(
    path: Path,
) -> tuple[list[FixedBuilding], list[BuildingSnapshot]]:
    """Read fixed-building definitions and city/zone snapshot records."""

    sections = parse_scenario_sections(path.read_bytes())
    fixed_section = sections.get("fbld")
    city_section = sections.get("city")
    zone_section = sections.get("bldg")
    if fixed_section is None or len(fixed_section) != 17 * 28:
        raise ValueError(f"{path}: fixed-building chunk has an unexpected size")
    if city_section is None or len(city_section) != 50 * 56:
        raise ValueError(f"{path}: city chunk has an unexpected size")
    if zone_section is None or len(zone_section) != 200 * 28:
        raise ValueError(f"{path}: building-zone chunk has an unexpected size")

    fixed_buildings: list[FixedBuilding] = []
    for code in range(17):
        values = struct.unpack_from("<11H6s", fixed_section, code * 28)
        fixed_buildings.append(
            FixedBuilding(
                code=code,
                max_durability=values[4],
                shape_length=values[6],
                shape_width=values[7],
                appearance_id=values[8],
            )
        )

    snapshots: list[BuildingSnapshot] = []
    city_terrain_by_code: dict[int, int] = {}
    for city_code in range(50):
        values = struct.unpack_from("<4I17H6s", city_section, city_code * 56)
        durability = values[16]
        max_durability = values[17]
        x = values[18]
        y = values[19]
        city_terrain_by_code[city_code] = values[20]
        if durability == 0xFFFF or max_durability == 0xFFFF:
            continue
        if x >= MAP_WIDTH or y >= MAP_HEIGHT:
            raise ValueError(f"{path}: city {city_code} has invalid coordinates")
        snapshots.append(
            BuildingSnapshot(
                zone_code=city_code,
                fixed_building_code=0,
                force_code=values[10],
                city_code=city_code,
                durability=durability,
                max_durability=max_durability,
                x=x,
                y=y,
                is_city=True,
                city_terrain_code=values[20],
                current_income=values[14],
                current_harvest=values[15],
            )
        )

    for zone_id in range(200):
        values = struct.unpack_from("<I9H6s", zone_section, zone_id * 28)
        fixed_code = values[2]
        durability = values[7]
        x = values[8]
        y = values[9]
        if fixed_code == 0xFFFF or durability == 0xFFFF:
            continue
        if fixed_code >= len(fixed_buildings):
            raise ValueError(f"{path}: zone {zone_id} has invalid fixed-building code")
        if x >= MAP_WIDTH or y >= MAP_HEIGHT:
            raise ValueError(f"{path}: zone {zone_id} has invalid coordinates")
        snapshots.append(
            BuildingSnapshot(
                zone_code=50 + zone_id,
                fixed_building_code=fixed_code,
                force_code=values[5],
                city_code=values[6],
                durability=durability,
                max_durability=fixed_buildings[fixed_code].max_durability,
                x=x,
                y=y,
                is_city=False,
                city_terrain_code=city_terrain_by_code.get(values[6]),
                current_income=None,
                current_harvest=None,
            )
        )
    return fixed_buildings, snapshots

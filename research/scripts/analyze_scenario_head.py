"""Audit the formal San9 PK scenario head records and their cross-chunk links."""

from __future__ import annotations

import argparse
import json
import struct
from pathlib import Path

from square_map.scenario import parse_scenario_sections

FORMAL_SCENARIO_COUNT = 20
FORMAT_VERSION = 0x20030317
CITY_COUNT = 50
CITY_RECORD_SIZE = 56
FORCE_RECORD_SIZE = 86
DATE_THRESHOLDS = (
    (0, 220, 10, 24),
    (1, 192, 4, 1),
    (2, 192, 5, 1),
    (3, 207, 10, 1),
    (4, 207, 10, 1),
    (5, 207, 10, 1),
    (8, 219, 10, 1),
    (9, 234, 9, 1),
    (10, 227, 10, 1),
    (12, 211, 1, 1),
    (13, 210, 1, 1),
    (14, 200, 11, 1),
    (15, 209, 1, 1),
)


def encode_date(year: int, month: int, day: int) -> int:
    return (year - 130) * 360 + (month - 1) * 30 + day - 1


def decode_date(code: int) -> tuple[int, int, int]:
    year, day_of_year = divmod(code, 360)
    month, day = divmod(day_of_year, 30)
    return 130 + year, 1 + month, 1 + day


def date_initialized_mask(encoded_date: int) -> int:
    mask = 0
    for bit, year, month, day in DATE_THRESHOLDS:
        if encoded_date >= encode_date(year, month, day):
            mask |= 1 << bit
    return mask


def analyze_scenario(path: Path, expected_id: int) -> dict[str, object]:
    sections = parse_scenario_sections(path.read_bytes())
    head = sections.get("head")
    cities = sections.get("city")
    forces = sections.get("forc")
    if head is None or len(head) != 82:
        raise ValueError(f"{path}: expected an 82-byte head section")
    if cities is None or len(cities) != CITY_COUNT * CITY_RECORD_SIZE:
        raise ValueError(f"{path}: unexpected city section")
    if forces is None or len(forces) != CITY_COUNT * FORCE_RECORD_SIZE:
        raise ValueError(f"{path}: unexpected force section")

    values = struct.unpack_from("<5I6H50s", head)
    format_version = values[0]
    world_words = values[1:5]
    scenario_id, packed, encoded_date, alliance_turns, width, height = values[5:11]
    city_force_colors = values[11]
    if format_version != FORMAT_VERSION:
        raise ValueError(f"{path}: unexpected format version {format_version:#010x}")
    if scenario_id != expected_id:
        raise ValueError(f"{path}: scenario id {scenario_id} != {expected_id}")

    color_mismatches: list[dict[str, int]] = []
    for city_id, actual_color in enumerate(city_force_colors):
        force_code = struct.unpack_from(
            "<H", cities, city_id * CITY_RECORD_SIZE + 0x1C
        )[0]
        expected_color = 0xFF
        if force_code < CITY_COUNT:
            color_code = struct.unpack_from(
                "<H", forces, force_code * FORCE_RECORD_SIZE + 0x1C
            )[0]
            if color_code > 0xFF:
                raise ValueError(
                    f"{path}: force {force_code} color {color_code} exceeds one byte"
                )
            expected_color = color_code
        if actual_color != expected_color:
            color_mismatches.append(
                {
                    "city_id": city_id,
                    "force_code": force_code,
                    "actual_color": actual_color,
                    "expected_color": expected_color,
                }
            )

    initialized = date_initialized_mask(encoded_date)
    return {
        "file": path.name,
        "format_version": f"0x{format_version:08X}",
        "scenario_world_flag_words": [f"0x{word:08X}" for word in world_words],
        "scenario_id": scenario_id,
        "land_draw_cache_code": packed & 0xFF,
        "scenario_mode_code": (packed >> 8) & 0x0F,
        "discarded_packed_high_bits": (packed >> 12) & 0x0F,
        "encoded_start_date": encoded_date,
        "start_date": "%03d-%02d-%02d" % decode_date(encoded_date),
        "alliance_duration_turns": alliance_turns,
        "map_width": width,
        "map_height": height,
        "title_message_id": 2 * scenario_id,
        "description_message_id": 2 * scenario_id + 1,
        "date_initialized_world_mask": f"0x{initialized:08X}",
        "file_only_world_mask": f"0x{world_words[0] & ~initialized:08X}",
        "post_load_added_world_mask": f"0x{initialized & ~world_words[0]:08X}",
        "effective_first_world_mask": f"0x{world_words[0] | initialized:08X}",
        "city_force_color_mismatches": color_mismatches,
    }


def analyze_directory(directory: Path) -> dict[str, object]:
    scenarios = [
        analyze_scenario(directory / f"D_Sce{index + 1:03d}.S9", index)
        for index in range(FORMAL_SCENARIO_COUNT)
    ]
    return {
        "scenario_count": len(scenarios),
        "all_city_force_colors_match": all(
            not item["city_force_color_mismatches"] for item in scenarios
        ),
        "all_later_world_words_zero": all(
            item["scenario_world_flag_words"][1:] == ["0x00000000"] * 3
            for item in scenarios
        ),
        "scenarios": scenarios,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("scenario_directory", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    result = analyze_directory(args.scenario_directory)
    rendered = json.dumps(result, ensure_ascii=False, indent=2) + "\n"
    if args.output is None:
        print(rendered, end="")
    else:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(rendered, encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

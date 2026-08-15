"""Dry-run or apply source-backed San9 PK core-record metadata in IDA.

Run inside the exact San9PK-TC-101 project IDB.  The default is read-only:

    apply_core_records.py --report <path>

Add ``--apply`` only after reviewing the report.  This script never patches
program bytes and refuses to overwrite an existing human-defined name.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

import ida_auto
import ida_bytes
import ida_name
import ida_nalt
import ida_segment
import idc


EXPECTED_INPUT_SHA256 = (
    "d20794aeff67301ec2bf8c3becb1e9944c68c6c0588fbfd4bf04e8597f0e5028"
)
AUTO_NAME = re.compile(
    r"^(?:sub|loc|locret|nullsub|j_sub|unk|byte|word|dword|qword|off|asc|stru|algn)_[0-9A-Fa-f]+$"
)

RECORDS = (
    (0x01253C38, "san9_force_records", "San9ForceRecord", 0xD4, 50),
    (0x01258EE0, "san9_person_records", "San9PersonRecord", 0x128, 850),
    (0x0124DB58, "san9_city_records", "San9CityRecord", 0x1F0, 50),
    (0x01238078, "san9_zone_records", "San9ZoneRecord", 0x1BC, 200),
    (0x01500B70, "san9_array_records", "San9ArrayRecord", 0x88, 28),
    (0x014FD4A0, "san9_building_records", "San9BuildingRecord", 0x50, 17),
    (0x014FDAD0, "san9_killer_records", "San9KillerRecord", 0x8C, 41),
)

TYPE_DECLARATIONS = r"""
#pragma pack(push, 1)
typedef struct San9PersonRecord {
    unsigned char unknown_000[0x10];
    char family_name[5];
    char given_name[5];
    unsigned char unknown_01A[0x32];
    unsigned char strength;
    unsigned char unknown_04D[7];
    unsigned char intelligence;
    unsigned char unknown_055[7];
    unsigned char politics;
    unsigned char unknown_05D[7];
    unsigned char command;
    unsigned char unknown_065[0x1F];
    unsigned char status;
    unsigned char unknown_085[0x1F];
    unsigned short tactic_proficiency[9];
    unsigned char unknown_0B6[0x32];
    unsigned short tactic_general_proficiency_bits;
    unsigned char unknown_0EA[0x16];
    unsigned int learned_tactics_bits;
    unsigned char unknown_104[0x24];
} San9PersonRecord;

typedef struct San9ForceRecord {
    unsigned char unknown_000[0x34];
    unsigned char flags_034;
    unsigned char unknown_035[0x83];
    struct San9ForceRecord *self_0B8;
    struct San9PersonRecord *leader_0BC;
    unsigned char unknown_0C0[0x14];
} San9ForceRecord;

typedef struct San9CityRecord {
    unsigned char unknown_000[0x20];
    char name_020[0x31];
    unsigned int tactic_flags_051;
    unsigned char unknown_055[0x173];
    unsigned int max_durability_1C8;
    unsigned char unknown_1CC[8];
    unsigned int max_income_1D4;
    unsigned int max_harvest_1D8;
    unsigned char unknown_1DC[4];
    unsigned int stationed_flag_1E0;
    unsigned char unknown_1E4[0x0C];
} San9CityRecord;

typedef struct San9ZoneRecord {
    unsigned char unknown_000[0x1BC];
} San9ZoneRecord;

typedef struct San9ArrayRecord {
    unsigned char unknown_000[0x10];
    char name_010[0x4C];
    unsigned char capability_05C;
    unsigned char unknown_05D[3];
    unsigned char capability_060;
    unsigned char unknown_061[3];
    unsigned char capability_064;
    unsigned char unknown_065[3];
    unsigned char capability_068;
    unsigned char unknown_069[3];
    unsigned char capability_06C;
    unsigned char unknown_06D[3];
    unsigned char view_070;
    unsigned char unknown_071[3];
    unsigned char range_074;
    unsigned char unknown_075[3];
    unsigned short expense_078;
    unsigned char unknown_07A[0x0E];
} San9ArrayRecord;

typedef struct San9BuildingRecord {
    unsigned char unknown_000[0x50];
} San9BuildingRecord;

typedef struct San9KillerRecord {
    unsigned char unknown_000[0x10];
    char name_010[0x48];
    unsigned short learn_proficiency_058;
    unsigned char unknown_05A[0x0A];
    unsigned char power_064;
    unsigned char unknown_065[0x17];
    unsigned char interval_07C;
    unsigned char unknown_07D[0x0F];
} San9KillerRecord;
#pragma pack(pop)
"""


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument("--apply", action="store_true")
    return parser.parse_args(sys.argv[1:])


def current_input_sha256() -> str:
    try:
        return ida_nalt.retrieve_input_file_sha256().hex()
    except Exception:
        return ""


def main() -> None:
    args = parse_args()
    ida_auto.auto_wait()
    actual_hash = current_input_sha256().lower()
    if actual_hash != EXPECTED_INPUT_SHA256:
        raise RuntimeError(
            f"wrong input sample: expected {EXPECTED_INPUT_SHA256}, got {actual_hash}"
        )

    rows = []
    for address, proposed, type_name, size, count in RECORDS:
        segment = ida_segment.getseg(address)
        current = ida_name.get_name(address) or ""
        auto = bool(current and AUTO_NAME.fullmatch(current))
        collision = ida_name.get_name_ea(idc.BADADDR, proposed)
        collision = collision if collision != idc.BADADDR else None
        if segment is None:
            status = "outside"
        elif current and not auto and current != proposed:
            status = "conflict"
        elif collision is not None and collision != address:
            status = "conflict"
        elif current == proposed:
            status = "already-applied"
        else:
            status = "ready"
        rows.append(
            {
                "address": f"0x{address:08X}",
                "proposed_name": proposed,
                "current_name": current or None,
                "record_type": type_name,
                "record_size": size,
                "record_count": count,
                "segment": ida_segment.get_segm_name(segment) if segment else None,
                "item_size": ida_bytes.get_item_size(address) if segment else None,
                "name_collision": f"0x{collision:08X}" if collision is not None else None,
                "status": status,
            }
        )

    report = {
        "schema_version": 1,
        "mode": "apply" if args.apply else "dry-run",
        "input_sha256": actual_hash,
        "records": rows,
        "types_applied": False,
    }
    if args.apply:
        blocked = [row for row in rows if row["status"] not in ("ready", "already-applied")]
        if blocked:
            raise RuntimeError(f"refusing apply; blocked records: {blocked}")
        declaration_errors = idc.parse_decls(TYPE_DECLARATIONS, idc.PT_SIL)
        if declaration_errors != 0:
            raise RuntimeError("one or more core type declarations failed")
        report["types_applied"] = True
        for row, (address, proposed, type_name, size, count) in zip(rows, RECORDS):
            if row["status"] == "ready" and not ida_name.set_name(
                address, proposed, ida_name.SN_NOCHECK
            ):
                raise RuntimeError(f"failed to name 0x{address:X} as {proposed}")
            idc.set_cmt(
                address,
                f"RE-SAN9PK-0003: {count} records, stride 0x{size:X}; source-backed working layout.",
                1,
            )
            if not idc.SetType(address, f"{type_name}[{count}]"):
                raise RuntimeError(f"failed to apply {type_name}[{count}] at 0x{address:X}")
            row["status"] = "applied"

    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"mode": report["mode"], "records": rows}, ensure_ascii=False))


if __name__ == "__main__":
    main()

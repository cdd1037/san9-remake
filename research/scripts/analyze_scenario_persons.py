"""Profile formal scenario persons; observations are not rejection rules."""

from __future__ import annotations

import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import struct

from square_map.scenario import parse_scenario_sections

RECORD_SIZE = 134
RECORD_COUNT = 800
FIELDS = {
    "serialized_id": 0x04, "biography_code": 0x10, "status": 0x1C,
    "leadership": 0x1E, "war": 0x20, "intelligence": 0x22,
    "politics": 0x24, "office_code": 0x26, "lifespan": 0x28,
    "health": 0x2A, "affinity": 0x30, "righteousness": 0x32,
    "ambition": 0x34, "personality": 0x36,
}


def profile_payload(payload: bytes) -> dict:
    if len(payload) != RECORD_COUNT * RECORD_SIZE:
        raise ValueError("expected 800 person records of 134 bytes")
    values = {key: Counter() for key in FIELDS}
    values.update({"proficiency": Counter(), "loyalty": Counter()})
    mismatches = []
    flags_or = tactics_or = 0
    for index in range(RECORD_COUNT):
        record = payload[index * RECORD_SIZE:(index + 1) * RECORD_SIZE]
        for key, offset in FIELDS.items():
            values[key][struct.unpack_from("<H", record, offset)[0]] += 1
        serialized_id = struct.unpack_from("<H", record, 4)[0]
        if serialized_id != index:
            mismatches.append({"record_index": index, "serialized_id": serialized_id})
        values["proficiency"].update(struct.unpack_from("<9H", record, 0x38))
        values["loyalty"].update(record[0x4E:0x80])
        flags_or |= struct.unpack_from("<I", record)[0]
        tactics_or |= int.from_bytes(record[0x80:0x86], "little")
    return {
        "record_count": RECORD_COUNT,
        "serialized_id_mismatches": mismatches,
        "duplicate_serialized_ids": sorted(k for k, n in values["serialized_id"].items() if n > 1),
        "flags_or": f"0x{flags_or:08X}",
        "tactics_or": f"0x{tactics_or:012X}",
        "fields": {key: {"min": min(counts), "max": max(counts),
                          "counts": dict(sorted(counts.items()))}
                   for key, counts in values.items()},
    }


def analyze_directory(directory: Path) -> dict:
    scenarios = []
    for index in range(1, 21):
        path = directory / f"D_Sce{index:03d}.S9"
        data = path.read_bytes()
        result = profile_payload(parse_scenario_sections(data)["prsn"])
        scenarios.append({"file": path.name, "source_sha256": hashlib.sha256(data).hexdigest(), **result})
    return {"schema_version": 1, "sample": "SRC-SAN9PK-TC-101",
            "finding": "RE-SAN9PK-0009", "scope": "formal-scenarios-only",
            "scenario_count": len(scenarios), "record_count": sum(s["record_count"] for s in scenarios),
            "observations_are_validation_rules": False, "scenarios": scenarios}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    result = analyze_directory(args.directory)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Profiled {result['scenario_count']} scenarios / {result['record_count']} records")


if __name__ == "__main__":
    main()

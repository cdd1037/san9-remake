"""Reconcile source, legacy-IDB, and project-IDB catalogs without modifying IDA."""

from __future__ import annotations

import argparse
import csv
import json
import re
from pathlib import Path


AUTO_OR_MECHANICAL = re.compile(
    r"^(?:sub|loc|locret|nullsub|j_sub|unk|byte|word|dword|qword|off|asc|"
    r"stru|algn|def|jpt|funcs|unknown_libname)_[0-9A-Fa-f]+$"
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-classification", type=Path, required=True)
    parser.add_argument("--legacy", type=Path, required=True)
    parser.add_argument("--current", type=Path, required=True)
    parser.add_argument("--json", type=Path, required=True)
    parser.add_argument("--csv", type=Path, required=True)
    return parser.parse_args()


def load(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))


def address_in_segments(address: int, catalog: dict) -> bool:
    return any(
        int(segment["start"], 16) <= address < int(segment["end"], 16)
        for segment in catalog.get("segments", [])
    )


def semantic_legacy_names(catalog: dict) -> list[dict]:
    return [
        row
        for row in catalog.get("names", [])
        if not row.get("auto_name")
        and not AUTO_OR_MECHANICAL.fullmatch(row.get("name", ""))
        and not row.get("name", "").startswith(("a", "?", "_", "$"))
    ]


def main() -> int:
    args = parse_args()
    legacy = load(args.legacy)
    current = load(args.current)
    source_rows = load(args.source_classification)
    current_names = {
        int(row["address"], 16): row for row in current.get("names", [])
    }
    current_functions = {
        int(row["address"], 16): row for row in current.get("functions", [])
    }

    rows: list[dict] = []
    for source in source_rows:
        rows.append(
            {
                "address": source.get("address"),
                "name": source.get("source_name"),
                "object_type": source.get("candidate_type"),
                "module": source.get("module"),
                "source": source.get("source_id"),
                "source_location": source.get("source_location"),
                "old_bytes": source.get("old_bytes"),
                "target_bytes": source.get("target_bytes"),
                "match_method": source.get("match_method"),
                "status": source.get("status", "pending"),
                "current_name": source.get("current_name"),
                "notes": source.get("notes"),
            }
        )

    for old in semantic_legacy_names(legacy):
        address = int(old["address"], 16)
        target = current_functions.get(address) or current_names.get(address)
        current_name = current_names.get(address, {}).get("name")
        old_bytes = old.get("bytes_hex_32")
        target_bytes = target.get("bytes_hex_32") if target else None
        if not address_in_segments(address, current):
            status, match = "outside", "address-outside-target"
        elif target is None:
            status, match = "pending", "target-window-not-exported"
        elif old.get("kind") == "function" and address not in current_functions:
            status, match = "conflict", "code-data-kind-conflict"
        elif old_bytes and target_bytes and old_bytes == target_bytes:
            if current_name == old.get("name"):
                status, match = "applied", "matching-32-byte-window-and-name"
            elif current_name and not AUTO_OR_MECHANICAL.fullmatch(current_name):
                status, match = "conflict", "matching-bytes-current-name-wins"
            else:
                status, match = "pending", "matching-32-byte-window"
        else:
            status, match = "target-mismatch", "32-byte-window-mismatch"
        rows.append(
            {
                "address": old["address"],
                "name": old["name"],
                "object_type": old.get("kind"),
                "module": "legacy-idb",
                "source": "SRC-LEGACY-IDB",
                "source_location": old["address"],
                "old_bytes": old_bytes,
                "target_bytes": target_bytes,
                "match_method": match,
                "status": status,
                "current_name": current_name,
                "notes": old.get("comment") or old.get("repeatable_comment"),
            }
        )

    counts: dict[str, int] = {}
    for row in rows:
        counts[row["status"]] = counts.get(row["status"], 0) + 1
    payload = {
        "schema_version": 1,
        "legacy_input_sha256": legacy.get("metadata", {}).get("input_sha256"),
        "current_input_sha256": current.get("metadata", {}).get("input_sha256"),
        "status_counts": counts,
        "candidates": rows,
    }
    args.json.parent.mkdir(parents=True, exist_ok=True)
    args.csv.parent.mkdir(parents=True, exist_ok=True)
    args.json.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
    fieldnames = list(rows[0]) if rows else []
    with args.csv.open("w", encoding="utf-8-sig", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)
    print(json.dumps({"total": len(rows), "statuses": counts}, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

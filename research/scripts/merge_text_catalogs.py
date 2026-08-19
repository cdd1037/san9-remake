#!/usr/bin/env python3
"""Validate and merge byte-faithful San9 research text catalogs."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

from legacy_text import convert_text_segments, display_text_from_segments


def _record_sort_key(record: dict[str, object]) -> tuple[str, int, str]:
    if "record_index" in record:
        position = int(record["record_index"])
    elif "virtual_address" in record:
        position = int(str(record["virtual_address"]), 16)
    else:
        position = 0
    return str(record["source_id"]), position, str(record["locator"])


def merge_catalogs(
    catalogs: list[dict[str, object]],
    *,
    simplified: bool = False,
) -> dict[str, object]:
    sources: dict[str, dict[str, object]] = {}
    records: dict[tuple[str, str], dict[str, object]] = {}
    converter = None
    if simplified:
        from opencc import OpenCC

        converter = OpenCC("t2s")

    for catalog in catalogs:
        if catalog.get("schema_version") != 1:
            raise ValueError("all catalogs must use schema_version 1")
        for source in catalog.get("sources", []):
            source_id = str(source["source_id"])
            existing = sources.get(source_id)
            if existing is not None and existing != source:
                raise ValueError(f"conflicting source metadata: {source_id}")
            sources[source_id] = dict(source)
        for record in catalog.get("records", []):
            row = dict(record)
            source_id = str(row["source_id"])
            if source_id not in sources:
                raise ValueError(f"record references unknown source: {source_id}")
            key = source_id, str(row["locator"])
            if key in records:
                raise ValueError(f"duplicate record locator: {key}")
            if converter is not None:
                segments = row.get("segments")
                if not isinstance(segments, list):
                    raise ValueError(f"record has no segment list: {key}")
                converted = convert_text_segments(segments, converter.convert)
                row["simplified_text"] = display_text_from_segments(converted)
            records[key] = row

    ordered_sources = sorted(sources.values(), key=lambda source: str(source["source_id"]))
    ordered_records = sorted(records.values(), key=_record_sort_key)
    return {
        "schema_version": 1,
        "authority_language": "zh-TW",
        "derived_language": "zh-CN" if simplified else None,
        "sources": ordered_sources,
        "records": ordered_records,
        "diagnostics": {
            "input_catalog_count": len(catalogs),
            "source_count": len(ordered_sources),
            "record_count": len(ordered_records),
        },
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("inputs", nargs="+", type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--simplified", action="store_true")
    args = parser.parse_args()

    catalogs = [json.loads(path.read_text(encoding="utf-8")) for path in args.inputs]
    merged = merge_catalogs(catalogs, simplified=args.simplified)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(
        json.dumps(merged, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(
        f"merged {merged['diagnostics']['source_count']} sources and "
        f"{merged['diagnostics']['record_count']} records -> {args.output}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

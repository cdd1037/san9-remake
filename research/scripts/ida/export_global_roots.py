"""Export high-reference global/static data roots without changing the IDB.

The report is built from code-to-data references observed in the current IDA
database. It is a navigation aid, not a semantic classifier: names and
categories remain candidates until initialization, consumers, and lifetime
evidence close the loop. Set SAN9_IDA_GLOBAL_ROOTS_OUTPUT before execution.
"""

from __future__ import annotations

import hashlib
import json
import os
from collections import defaultdict
from pathlib import Path
from typing import Any

import ida_bytes
import ida_funcs
import ida_ida
import ida_idaapi
import ida_kernwin
import ida_name
import ida_nalt
import ida_segment
import idautils
import idc


TARGET_SEGMENTS = {".data", ".rdata"}
MAX_ROWS = 500


def hex_or_none(value: int | None) -> str | None:
    return None if value is None or value == ida_idaapi.BADADDR else f"0x{value:X}"


def segment_name(ea: int) -> str | None:
    segment = ida_segment.getseg(ea)
    return ida_segment.get_segm_name(segment) if segment else None


def safe_name(ea: int) -> str:
    try:
        return ida_name.get_name(ea) or ""
    except Exception:
        return ""


def safe_type(ea: int) -> str | None:
    try:
        return idc.get_type(ea)
    except Exception:
        return None


def has_user_name(ea: int) -> bool:
    try:
        return bool(ida_bytes.has_user_name(ida_bytes.get_full_flags(ea)))
    except Exception:
        return False


def classify(ea: int, name: str, target: int | None) -> str:
    lowered = name.lower()
    if name.startswith("runtime_class_"):
        return "runtime_class_record"
    if name.startswith("message_map_"):
        return "message_map"
    if "vtable" in lowered or "vftable" in lowered:
        return "vtable"
    if name.startswith("jpt_") or name.startswith("jsub_"):
        return "jump_table"
    if target is not None and ida_funcs.get_func(target):
        return "function_pointer"
    try:
        if ida_bytes.is_strlit(ida_bytes.get_full_flags(ea)):
            return "string"
    except Exception:
        pass
    if name and has_user_name(ea):
        return "named_data"
    return "data_object"


def pointer_target(ea: int) -> int | None:
    try:
        value = int(ida_bytes.get_dword(ea))
    except Exception:
        return None
    if value == 0 or value == ida_idaapi.BADADDR:
        return None
    return value


def collect_refs() -> tuple[dict[int, set[int]], dict[int, set[int]], bool]:
    data_refs: dict[int, set[int]] = defaultdict(set)
    function_refs: dict[int, set[int]] = defaultdict(set)
    cancelled = False
    for index, function_ea in enumerate(idautils.Functions()):
        if index % 250 == 0 and ida_kernwin.user_cancelled():
            cancelled = True
            break
        function = ida_funcs.get_func(function_ea)
        if not function:
            continue
        for instruction_ea in idautils.FuncItems(function_ea):
            source_segment = segment_name(instruction_ea)
            if source_segment != ".text":
                continue
            for target in idautils.DataRefsFrom(instruction_ea):
                if segment_name(target) not in TARGET_SEGMENTS:
                    continue
                head = ida_bytes.get_item_head(target)
                data_refs[head].add(instruction_ea)
                function_refs[head].add(function_ea)
    return data_refs, function_refs, cancelled


def export_rows() -> list[dict[str, Any]]:
    data_refs, function_refs, cancelled = collect_refs()
    if cancelled:
        raise RuntimeError("global-root export cancelled before completion")
    rows: list[dict[str, Any]] = []
    for ea, sources in data_refs.items():
        name = safe_name(ea)
        target = pointer_target(ea)
        rows.append(
            {
                "address": hex_or_none(ea),
                "end": hex_or_none(ea + ida_bytes.get_item_size(ea)),
                "size": ida_bytes.get_item_size(ea),
                "name": name,
                "auto_name": not has_user_name(ea),
                "type": safe_type(ea),
                "segment": segment_name(ea),
                "code_xref_count": len(sources),
                "source_function_count": len(function_refs[ea]),
                "source_functions": [
                    {"address": hex_or_none(source), "name": safe_name(source)}
                    for source in sorted(function_refs[ea])[:100]
                ],
                "pointer_target": hex_or_none(target),
                "category": classify(ea, name, target),
            }
        )
    rows.sort(key=lambda row: (-row["code_xref_count"], int(row["address"], 16)))
    return rows[:MAX_ROWS]


def main() -> None:
    output_value = os.environ.get("SAN9_IDA_GLOBAL_ROOTS_OUTPUT")
    if not output_value:
        raise RuntimeError("SAN9_IDA_GLOBAL_ROOTS_OUTPUT is not set")
    output = Path(output_value)
    output.parent.mkdir(parents=True, exist_ok=True)

    input_sha256 = None
    try:
        value = ida_nalt.retrieve_input_file_sha256()
        input_sha256 = bytes(value).hex() if value else None
    except Exception:
        pass

    rows = export_rows()
    core = {
        "input_sha256": input_sha256,
        "ida_version": ida_kernwin.get_kernel_version(),
        "architecture": "64-bit" if ida_ida.inf_is_64bit() else "32-bit",
        "candidate_count": len(rows),
        "source_segments": sorted(TARGET_SEGMENTS),
        "max_rows": MAX_ROWS,
    }
    digest = hashlib.sha256(json.dumps(core, sort_keys=True).encode("utf-8")).hexdigest()
    report = {
        "schema_version": 1,
        "core_stats": core,
        "core_stats_sha256": digest,
        "candidates": rows,
    }
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"output": str(output), "core_stats": core, "core_stats_sha256": digest}, ensure_ascii=False))


if __name__ == "__main__":
    main()

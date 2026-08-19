"""Export MFC-style message-map candidates without changing the IDB.

Set SAN9_IDA_MESSAGE_MAPS_OUTPUT to the JSON output path before execution.
The scanner recognizes the 8-byte map header (base map pointer plus entry
array pointer) and 24-byte entries ending at an all-zero sentinel. It keeps
the result as evidence candidates; it does not create data, types, names or
comments.
"""

from __future__ import annotations

import hashlib
import json
import os
import re
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


ENTRY_SIZE = 0x18
MAX_ENTRIES = 128
CLASS_NAME = re.compile(r"^C[A-Z][A-Za-z0-9_]{2,}$")


def hex_or_none(value: int | None) -> str | None:
    return None if value is None or value == ida_idaapi.BADADDR else f"0x{value:X}"


def segment_name(ea: int) -> str | None:
    segment = ida_segment.getseg(ea)
    return ida_segment.get_segm_name(segment) if segment else None


def get_dword(ea: int) -> int:
    return int(ida_bytes.get_dword(ea))


def in_rdata_or_data(ea: int, allow_zero: bool = False) -> bool:
    if allow_zero and ea == 0:
        return True
    return segment_name(ea) in {".rdata", ".data"}


def get_c_string(ea: int) -> str | None:
    try:
        raw = ida_bytes.get_strlit_contents(ea, -1, ida_nalt.STRTYPE_C)
        if raw is None:
            return None
        if isinstance(raw, bytes):
            return raw.decode("latin-1", errors="replace")
        return str(raw)
    except Exception:
        return None


def valid_entry(ea: int) -> bool:
    message = get_dword(ea)
    code = get_dword(ea + 4)
    first_id = get_dword(ea + 8)
    last_id = get_dword(ea + 12)
    signature = get_dword(ea + 16)
    handler = get_dword(ea + 20)
    if segment_name(handler) != ".text":
        return False
    if any(value > 0xFFFF for value in (message, code, first_id, last_id)):
        return False
    if last_id and last_id < first_id:
        return False
    return signature <= 0x1000


def zero_entry(ea: int) -> bool:
    for offset in range(0, ENTRY_SIZE, 4):
        if get_dword(ea + offset) != 0:
            return False
    return True


def parse_entries(ea: int) -> tuple[list[dict[str, Any]], bool]:
    entries: list[dict[str, Any]] = []
    sentinel = False
    for index in range(MAX_ENTRIES):
        entry_ea = ea + index * ENTRY_SIZE
        if zero_entry(entry_ea):
            sentinel = True
            break
        if not valid_entry(entry_ea):
            break
        values = [get_dword(entry_ea + offset) for offset in range(0, ENTRY_SIZE, 4)]
        entries.append(
            {
                "index": index,
                "address": hex_or_none(entry_ea),
                "message": hex(values[0]),
                "code": hex(values[1]),
                "first_id": hex(values[2]),
                "last_id": hex(values[3]),
                "signature": hex(values[4]),
                "handler_address": hex_or_none(values[5]),
                "handler_name": ida_name.get_name(values[5]) or None,
                "handler_function_start": hex_or_none(
                    ida_funcs.get_func(values[5]).start_ea if ida_funcs.get_func(values[5]) else None
                ),
            }
        )
    return entries, sentinel


def runtime_record_at(ea: int) -> dict[str, Any] | None:
    name_ea = get_dword(ea)
    name = get_c_string(name_ea)
    if not name or not CLASS_NAME.fullmatch(name):
        return None
    object_size = get_dword(ea + 4)
    schema = get_dword(ea + 8)
    create_ea = get_dword(ea + 12)
    base_ea = get_dword(ea + 16)
    next_ea = get_dword(ea + 20)
    if not 1 <= object_size <= 0x10000 or schema not in {0, 0xFFFF}:
        return None
    if create_ea and segment_name(create_ea) != ".text":
        return None
    if not in_rdata_or_data(base_ea, True) or not in_rdata_or_data(next_ea, True):
        return None
    return {
        "address": hex_or_none(ea),
        "name_address": hex_or_none(name_ea),
        "name": name,
        "object_size": object_size,
        "schema": hex(schema),
        "create_address": hex_or_none(create_ea),
        "base_address": hex_or_none(base_ea),
        "next_address": hex_or_none(next_ea),
    }


def nearby_runtime_record(ea: int) -> dict[str, Any] | None:
    start = max(ida_segment.get_segm_by_name(".rdata").start_ea, ea - 0x200)
    candidate = None
    pos = start & ~3
    while pos <= ea:
        row = runtime_record_at(pos)
        if row:
            candidate = row
        pos += 4
    return candidate


def xrefs_to(ea: int) -> list[dict[str, Any]]:
    rows = []
    for ref in idautils.XrefsTo(ea, 0):
        function = ida_funcs.get_func(ref.frm)
        rows.append(
            {
                "from": hex_or_none(ref.frm),
                "type": ref.type,
                "function": hex_or_none(function.start_ea) if function else None,
                "function_name": ida_name.get_name(function.start_ea) if function else None,
            }
        )
    return rows


def main() -> None:
    output_value = os.environ.get("SAN9_IDA_MESSAGE_MAPS_OUTPUT")
    if not output_value:
        raise RuntimeError("SAN9_IDA_MESSAGE_MAPS_OUTPUT is not set")
    output = Path(output_value)
    output.parent.mkdir(parents=True, exist_ok=True)

    input_sha256 = None
    try:
        value = ida_nalt.retrieve_input_file_sha256()
        input_sha256 = bytes(value).hex() if value else None
    except Exception:
        pass

    rdata = ida_segment.get_segm_by_name(".rdata")
    if not rdata:
        raise RuntimeError("required .rdata segment is missing")
    rows: list[dict[str, Any]] = []
    seen_entries: set[int] = set()
    cancelled = False
    pos = rdata.start_ea
    while pos + 8 <= rdata.end_ea:
        if pos % 0x4000 == 0 and ida_kernwin.user_cancelled():
            cancelled = True
            break
        base_map = get_dword(pos)
        entries_ea = get_dword(pos + 4)
        if not in_rdata_or_data(base_map, True) or segment_name(entries_ea) != ".rdata":
            pos += 4
            continue
        entries, sentinel = parse_entries(entries_ea)
        if not entries or not sentinel or entries_ea in seen_entries:
            pos += 4
            continue
        seen_entries.add(entries_ea)
        rows.append(
            {
                "address": hex_or_none(pos),
                "name": ida_name.get_name(pos) or None,
                "segment": segment_name(pos),
                "base_map_address": hex_or_none(base_map),
                "entry_array_address": hex_or_none(entries_ea),
                "entry_count": len(entries),
                "sentinel_found": sentinel,
                "runtime_class_nearby": nearby_runtime_record(pos),
                "xrefs": xrefs_to(pos),
                "entries": entries,
            }
        )
        pos += 4

    if cancelled:
        raise RuntimeError("message-map export cancelled before completion")

    rows.sort(key=lambda row: int(row["address"], 16))
    core = {
        "input_sha256": input_sha256,
        "ida_version": ida_kernwin.get_kernel_version(),
        "architecture": "64-bit" if ida_ida.inf_is_64bit() else "32-bit",
        "entry_size": ENTRY_SIZE,
        "candidate_map_count": len(rows),
        "candidate_entry_count": sum(row["entry_count"] for row in rows),
    }
    digest = hashlib.sha256(json.dumps(core, sort_keys=True).encode("utf-8")).hexdigest()
    report = {
        "schema_version": 1,
        "core_stats": core,
        "core_stats_sha256": digest,
        "maps": rows,
    }
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"output": str(output), "core_stats": core, "core_stats_sha256": digest}, ensure_ascii=False))


if __name__ == "__main__":
    main()

"""Export candidate MFC-style runtime class records without changing the IDB.

Set SAN9_IDA_RUNTIME_CLASSES_OUTPUT to the JSON output path before execution.
The scanner checks class-name-relative candidates, aligned .rdata records whose
first field directly points to a class-name string, and referenced .data
records reached through validated base/next pointers. It reports validation
evidence but does not rename, type, define data, or save the database.
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
import idc


CLASS_NAME = re.compile(r"^(?:C[A-Z][A-Za-z0-9_]{2,}|\.?A[UV].+)$")
RECORD_SIZE = 0x18


def hex_or_none(value: int | None) -> str | None:
    return None if value is None or value == ida_idaapi.BADADDR else f"0x{value:X}"


def get_dword(ea: int) -> int | None:
    try:
        return int(ida_bytes.get_dword(ea))
    except Exception:
        return None


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


def in_image(ea: int) -> bool:
    return ea != 0 and ida_segment.getseg(ea) is not None


def in_rdata_or_data(ea: int) -> bool:
    segment = ida_segment.getseg(ea)
    if not segment:
        return False
    return ida_segment.get_segm_name(segment) in {".rdata", ".data"}


def segment_name(ea: int) -> str | None:
    segment = ida_segment.getseg(ea)
    return ida_segment.get_segm_name(segment) if segment else None


def is_function(ea: int) -> bool:
    if ea == 0:
        return True
    function = ida_funcs.get_func(ea)
    return bool(function and function.start_ea == ea)


def xref_count(ea: int) -> int:
    try:
        return sum(1 for _ in idautils.XrefsTo(ea, 0))
    except Exception:
        return 0


def build_record(record_ea: int, string_ea: int, name: str, detection: str) -> dict[str, Any]:
    fields = [get_dword(record_ea + offset) for offset in range(0, RECORD_SIZE, 4)]
    name_ptr, object_size, schema, create_ea, base_ea, next_ea = fields
    name_ptr_matches = name_ptr == string_ea
    size_valid = object_size is not None and 1 <= object_size <= 0x10000
    schema_valid = schema in {0, 0xFFFF} if schema is not None else False
    create_valid = create_ea is not None and is_function(create_ea)
    base_valid = base_ea in {0, None} or in_rdata_or_data(base_ea)
    next_valid = next_ea in {0, None} or in_rdata_or_data(next_ea)
    record_valid = all(value is not None for value in fields) and all(
        (name_ptr_matches, size_valid, schema_valid, create_valid, base_valid, next_valid)
    )
    score = sum(
        [
            name_ptr_matches,
            size_valid,
            schema_valid,
            create_valid,
            base_valid,
            next_valid,
        ]
    )
    confidence = "high" if record_valid and score == 6 else "medium" if score >= 4 else "low"
    return {
        "record_address": hex_or_none(record_ea),
        "name_address": hex_or_none(string_ea),
        "name": name,
        "detection": detection,
        "current_ida_name": ida_name.get_name(record_ea) or "",
        "current_ida_type": idc.get_type(record_ea),
        "current_name_is_user": bool(ida_bytes.has_user_name(ida_bytes.get_full_flags(record_ea))),
        "object_size": object_size,
        "schema": None if schema is None else hex(schema),
        "create_address": hex_or_none(create_ea),
        "base_address": hex_or_none(base_ea),
        "next_address": hex_or_none(next_ea),
        "base_name": get_c_string(get_dword(base_ea)) if in_rdata_or_data(base_ea or 0) else None,
        "next_name": get_c_string(get_dword(next_ea)) if in_rdata_or_data(next_ea or 0) else None,
        "record_xref_count": xref_count(record_ea),
        "name_xref_count": xref_count(string_ea),
        "evidence": {
            "name_ptr_matches": name_ptr_matches,
            "object_size_valid": size_valid,
            "schema_valid": schema_valid,
            "create_pointer_is_function_or_null": create_valid,
            "base_pointer_in_data_or_null": base_valid,
            "next_pointer_in_data_or_null": next_valid,
        },
        "parseable": record_valid,
        "confidence": confidence,
    }


def find_record_for_string(string_ea: int, name: str) -> dict[str, Any]:
    return build_record(string_ea - RECORD_SIZE, string_ea, name, "string_minus_record_size")


def scan_direct_records(rdata: Any) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    address = rdata.start_ea
    while address + RECORD_SIZE <= rdata.end_ea:
        name_ea = get_dword(address)
        name = get_c_string(name_ea or 0)
        if name and CLASS_NAME.fullmatch(name):
            rows.append(build_record(address, name_ea, name, "direct_field_scan"))
        address += 4
    return rows


def scan_referenced_data_records(rows: list[dict[str, Any]]) -> list[dict[str, Any]]:
    """Follow base/next pointers into .data for framework root records."""
    pending: list[int] = []
    for row in rows:
        for key in ("base_address", "next_address"):
            value = row.get(key)
            if value and value != "0x0":
                address = int(value, 16)
                if segment_name(address) == ".data":
                    pending.append(address)
    seen: set[int] = set()
    extra: list[dict[str, Any]] = []
    while pending:
        address = pending.pop()
        if address in seen:
            continue
        seen.add(address)
        name_ea = get_dword(address)
        name = get_c_string(name_ea or 0)
        if not name or not CLASS_NAME.fullmatch(name):
            continue
        row = build_record(address, name_ea, name, "referenced_data_scan")
        extra.append(row)
        for key in ("base_address", "next_address"):
            value = row.get(key)
            if value and value != "0x0":
                target = int(value, 16)
                if segment_name(target) == ".data":
                    pending.append(target)
    return extra


def main() -> None:
    output_value = os.environ.get("SAN9_IDA_RUNTIME_CLASSES_OUTPUT")
    if not output_value:
        raise RuntimeError("SAN9_IDA_RUNTIME_CLASSES_OUTPUT is not set")
    output = Path(output_value)
    output.parent.mkdir(parents=True, exist_ok=True)

    input_sha256 = None
    try:
        value = ida_nalt.retrieve_input_file_sha256()
        input_sha256 = bytes(value).hex() if value else None
    except Exception:
        pass

    string_rows: list[dict[str, Any]] = []
    cancelled = False
    strings = idautils.Strings()
    try:
        strings.setup()
    except Exception:
        pass
    for index, item in enumerate(strings):
        if index % 250 == 0 and ida_kernwin.user_cancelled():
            cancelled = True
            break
        try:
            name = str(item)
            if not CLASS_NAME.fullmatch(name):
                continue
            string_rows.append(find_record_for_string(int(item.ea), name))
        except Exception:
            continue

    rdata = ida_segment.get_segm_by_name(".rdata")
    if not rdata:
        raise RuntimeError("required .rdata segment is missing")
    if cancelled:
        raise RuntimeError("runtime-class export cancelled before completion")
    direct_rows = scan_direct_records(rdata)
    direct_rows.extend(scan_referenced_data_records(direct_rows))
    rows_by_address: dict[str, dict[str, Any]] = {}
    for row in string_rows + direct_rows:
        address = row["record_address"]
        previous = rows_by_address.get(address)
        if previous is None or row["detection"] == "direct_field_scan":
            rows_by_address[address] = row
    rows = list(rows_by_address.values())
    rows.sort(key=lambda row: int(row["record_address"], 16))
    parseable = [row for row in rows if row["parseable"]]
    core = {
        "input_sha256": input_sha256,
        "ida_version": ida_kernwin.get_kernel_version(),
        "architecture": "64-bit" if ida_ida.inf_is_64bit() else "32-bit",
        "record_size": RECORD_SIZE,
        "class_name_candidates": len(string_rows),
        "record_candidates": len(rows),
        "parseable_records": len(parseable),
        "high_confidence_records": len([row for row in parseable if row["confidence"] == "high"]),
    }
    digest = hashlib.sha256(json.dumps(core, sort_keys=True).encode("utf-8")).hexdigest()
    report = {
        "schema_version": 1,
        "core_stats": core,
        "core_stats_sha256": digest,
        "records": rows,
    }
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"output": str(output), "core_stats": core, "core_stats_sha256": digest}, ensure_ascii=False))


if __name__ == "__main__":
    main()

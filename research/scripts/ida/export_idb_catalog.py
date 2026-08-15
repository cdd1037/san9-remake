"""Export a read-only IDA database catalog as JSON.

Run from IDA in batch mode:
    ida.exe -A -S"export_idb_catalog.py OUTPUT.json" INPUT.idb

The script does not rename, type, patch, or otherwise modify database items.
"""

from __future__ import annotations

import hashlib
import json
import re
import sys
from pathlib import Path

import ida_bytes
import ida_funcs
import ida_idaapi
import ida_kernwin
import ida_name
import ida_nalt
import ida_segment
import ida_typeinf
import idautils
import idc


AUTO_NAME = re.compile(
    r"^(?:sub|loc|locret|nullsub|j_sub|unk|byte|word|dword|qword|off|asc|stru|algn)_[0-9A-Fa-f]+$"
)


def hex_or_none(value: int | None) -> str | None:
    return None if value is None or value == ida_idaapi.BADADDR else f"0x{value:X}"


def fingerprint(ea: int, size: int = 32) -> str | None:
    data = ida_bytes.get_bytes(ea, size)
    return None if not data else hashlib.sha256(data).hexdigest()


def byte_window(ea: int, size: int = 32) -> str | None:
    data = ida_bytes.get_bytes(ea, size)
    return None if not data else data.hex().upper()


def input_sha256() -> str | None:
    try:
        value = ida_nalt.retrieve_input_file_sha256()
        return bytes(value).hex() if value else None
    except Exception:
        return None


def export_segments() -> list[dict[str, object]]:
    rows: list[dict[str, object]] = []
    for start in idautils.Segments():
        segment = ida_segment.getseg(start)
        if not segment:
            continue
        rows.append(
            {
                "name": ida_segment.get_segm_name(segment),
                "start": hex_or_none(segment.start_ea),
                "end": hex_or_none(segment.end_ea),
                "permissions": segment.perm,
            }
        )
    return rows


def export_names() -> list[dict[str, object]]:
    rows: list[dict[str, object]] = []
    for ea, name in idautils.Names():
        function = ida_funcs.get_func(ea)
        function_start = function.start_ea if function and function.start_ea == ea else None
        item_size = ida_bytes.get_item_size(ea)
        rows.append(
            {
                "address": hex_or_none(ea),
                "name": name,
                "auto_name": bool(AUTO_NAME.fullmatch(name)),
                "kind": "function" if function_start is not None else "data",
                "function_end": hex_or_none(function.end_ea) if function_start is not None else None,
                "item_size": item_size,
                "type": idc.get_type(ea),
                "comment": idc.get_cmt(ea, 0),
                "repeatable_comment": idc.get_cmt(ea, 1),
                "bytes_hex_32": byte_window(ea),
                "bytes_sha256_32": fingerprint(ea),
            }
        )
    return rows


def export_functions() -> list[dict[str, object]]:
    rows: list[dict[str, object]] = []
    for ea in idautils.Functions():
        function = ida_funcs.get_func(ea)
        if not function:
            continue
        rows.append(
            {
                "address": hex_or_none(ea),
                "end": hex_or_none(function.end_ea),
                "name": ida_name.get_name(ea),
                "comment": idc.get_func_cmt(ea, 0),
                "repeatable_comment": idc.get_func_cmt(ea, 1),
                "type": idc.get_type(ea),
                "bytes_hex_32": byte_window(ea),
                "bytes_sha256_32": fingerprint(ea),
            }
        )
    return rows


def export_local_types() -> list[dict[str, object]]:
    rows: list[dict[str, object]] = []
    try:
        library = ida_typeinf.get_idati()
        ordinal_count = ida_typeinf.get_ordinal_count(library)
    except Exception as error:
        return [{"error": str(error)}]

    for ordinal in range(1, ordinal_count + 1):
        try:
            name = ida_typeinf.get_numbered_type_name(library, ordinal)
            if not name:
                continue
            info = ida_typeinf.tinfo_t()
            if not info.get_numbered_type(library, ordinal):
                continue
            size = info.get_size()
            rows.append(
                {
                    "ordinal": ordinal,
                    "name": name,
                    "declaration": info.dstr(),
                    "size": None if size == ida_idaapi.BADADDR else size,
                    "is_udt": info.is_udt(),
                    "is_enum": info.is_enum(),
                }
            )
        except Exception as error:
            rows.append({"ordinal": ordinal, "error": str(error)})
    return rows


def main() -> int:
    argv = list(getattr(idc, "ARGV", sys.argv))
    if len(argv) < 2:
        print("usage: export_idb_catalog.py OUTPUT.json")
        return 2

    output = Path(argv[1]).resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    payload = {
        "schema_version": 1,
        "metadata": {
            "ida_version": ida_kernwin.get_kernel_version(),
            "idb_path": idc.get_idb_path(),
            "input_path": ida_nalt.get_input_file_path(),
            "input_sha256": input_sha256(),
            "imagebase": hex_or_none(ida_nalt.get_imagebase()),
        },
        "segments": export_segments(),
        "names": export_names(),
        "functions": export_functions(),
        "local_types": export_local_types(),
    }
    output.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"exported IDB catalog: {output}")
    return 0


if __name__ == "__main__":
    # IDA's batch runner reports SystemExit as a script failure even for code 0.
    main()

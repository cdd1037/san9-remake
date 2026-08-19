"""Export a repeatable, read-only IDA health baseline.

The script only queries IDA state. It does not rename, type, patch, or save the
database. Set SAN9_IDA_HEALTH_OUTPUT to the JSON output path before execution.
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
import ida_gdl
import ida_hexrays
import ida_ida
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
RUNTIME_CLASS = re.compile(r"(?:^|[^A-Za-z0-9_])(?:\.?AV|\.?AU|C[A-Z][A-Za-z0-9_]{2,})(?:$|[^A-Za-z0-9_])")
VTABLE_NAME = re.compile(r"(?:vftable|vtable|__vftable|\?\?_7)", re.IGNORECASE)
JUMP_TABLE_NAME = re.compile(r"^(?:jpt|jsub|switch)_[0-9A-Fa-f]+$", re.IGNORECASE)


def hex_or_none(value: int | None) -> str | None:
    return None if value is None or value == ida_idaapi.BADADDR else f"0x{value:X}"


def safe_type(ea: int) -> str | None:
    try:
        return idc.get_type(ea)
    except Exception:
        return None


def safe_name(ea: int) -> str:
    try:
        return ida_name.get_name(ea) or ""
    except Exception:
        return ""


def is_auto_name(name: str) -> bool:
    return bool(AUTO_NAME.fullmatch(name))


def has_user_name(ea: int) -> bool:
    try:
        return bool(ida_bytes.has_user_name(ida_bytes.get_full_flags(ea)))
    except Exception:
        return False


def segment_for(ea: int) -> dict[str, Any] | None:
    segment = ida_segment.getseg(ea)
    if not segment:
        return None
    return {
        "name": ida_segment.get_segm_name(segment),
        "start": hex_or_none(segment.start_ea),
        "end": hex_or_none(segment.end_ea),
        "permissions": segment.perm,
    }


def xref_count(ea: int, code_only: bool = False) -> int:
    try:
        refs = idautils.CodeRefsTo(ea, 0) if code_only else idautils.XrefsTo(ea, 0)
        return sum(1 for _ in refs)
    except Exception:
        return 0


def function_flags(function: ida_funcs.func_t) -> dict[str, bool]:
    flags = function.flags
    return {
        "library": bool(flags & ida_funcs.FUNC_LIB),
        "thunk": bool(flags & ida_funcs.FUNC_THUNK),
        "noreturn": bool(flags & ida_funcs.FUNC_NORET),
        "far": bool(flags & ida_funcs.FUNC_FAR),
    }


def function_row(ea: int) -> dict[str, Any] | None:
    function = ida_funcs.get_func(ea)
    if not function or function.start_ea != ea:
        return None
    name = safe_name(ea)
    flags = function_flags(function)
    row = {
        "address": hex_or_none(ea),
        "end": hex_or_none(function.end_ea),
        "size": function.end_ea - function.start_ea,
        "name": name,
        "auto_name": not has_user_name(ea),
        "type": safe_type(ea),
        "comment": idc.get_func_cmt(ea, 0),
        "repeatable_comment": idc.get_func_cmt(ea, 1),
        "code_xref_count": xref_count(ea, code_only=True),
        "flags": flags,
    }
    try:
        row["basic_block_count"] = sum(1 for _ in ida_gdl.FlowChart(function))
    except Exception as error:
        row["basic_block_count_error"] = str(error)
    return row


def export_segments() -> list[dict[str, Any]]:
    rows = []
    for start in idautils.Segments():
        segment = ida_segment.getseg(start)
        if segment:
            rows.append(
                {
                    "name": ida_segment.get_segm_name(segment),
                    "start": hex_or_none(segment.start_ea),
                    "end": hex_or_none(segment.end_ea),
                    "size": segment.end_ea - segment.start_ea,
                    "permissions": segment.perm,
                }
            )
    return rows


def export_entries() -> list[dict[str, Any]]:
    rows = []
    for entry in idautils.Entries():
        ordinal, ea, name = entry[0], entry[1], entry[-1]
        rows.append({"ordinal": ordinal, "address": hex_or_none(ea), "name": name})
    return rows


def export_imports() -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    try:
        module_count = ida_nalt.get_import_module_qty()
        for module_index in range(module_count):
            module = ida_nalt.get_import_module_name(module_index) or ""

            def callback(ea: int, name: str | None, ordinal: int, module: str = module) -> bool:
                rows.append(
                    {
                        "module": module,
                        "address": hex_or_none(ea),
                        "name": name,
                        "ordinal": ordinal,
                    }
                )
                return True

            ida_nalt.enum_import_names(module_index, callback)
    except Exception as error:
        rows.append({"error": str(error)})
    return rows


def export_names() -> tuple[list[dict[str, Any]], list[dict[str, Any]]]:
    all_rows: list[dict[str, Any]] = []
    data_rows: list[dict[str, Any]] = []
    for ea, name in idautils.Names():
        function = ida_funcs.get_func(ea)
        is_function_start = bool(function and function.start_ea == ea)
        row = {
            "address": hex_or_none(ea),
            "name": name,
            "auto_name": not has_user_name(ea),
            "kind": "function" if is_function_start else "data",
            "segment": segment_for(ea),
            "type": safe_type(ea),
            "item_size": ida_bytes.get_item_size(ea),
            "xref_count": xref_count(ea),
            "code_xref_count": xref_count(ea, code_only=True),
            "comment": idc.get_cmt(ea, 0),
            "repeatable_comment": idc.get_cmt(ea, 1),
        }
        all_rows.append(row)
        if not is_function_start:
            data_rows.append(row)
    return all_rows, data_rows


def export_local_types() -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
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
                    "is_func": info.is_func(),
                    "is_ptr": info.is_ptr(),
                }
            )
        except Exception as error:
            rows.append({"ordinal": ordinal, "error": str(error)})
    return rows


def export_strings() -> tuple[list[dict[str, Any]], list[dict[str, Any]]]:
    rows: list[dict[str, Any]] = []
    runtime_rows: list[dict[str, Any]] = []
    strings = idautils.Strings()
    try:
        strings.setup()
    except Exception:
        pass
    for item in strings:
        try:
            value = str(item)
            ea = int(item.ea)
            row = {
                "address": hex_or_none(ea),
                "length": int(item.length),
                "value": value,
                "xref_count": xref_count(ea),
            }
            rows.append(row)
            if RUNTIME_CLASS.search(value):
                runtime_rows.append(row)
        except Exception:
            continue
    return rows, runtime_rows


def scan_code_data_anomalies(segments: list[dict[str, Any]]) -> dict[str, Any]:
    undefined_ranges: list[dict[str, Any]] = []
    code_in_nonexec: list[dict[str, Any]] = []
    data_in_exec: list[dict[str, Any]] = []
    counters = {
        "undefined_bytes_in_exec": 0,
        "code_items_in_nonexec": 0,
        "data_items_in_exec": 0,
    }
    for start in idautils.Segments():
        segment = ida_segment.getseg(start)
        if not segment:
            continue
        executable = bool(segment.perm & ida_segment.SEGPERM_EXEC)
        unknown_start = None
        ea = segment.start_ea
        while ea < segment.end_ea:
            flags = ida_bytes.get_full_flags(ea)
            unknown = ida_bytes.is_unknown(flags)
            code = ida_bytes.is_code(flags)
            data = ida_bytes.is_data(flags)
            next_ea = ida_bytes.next_head(ea, segment.end_ea)
            if next_ea == ida_idaapi.BADADDR or next_ea <= ea:
                next_ea = segment.end_ea
            item_span = next_ea - ea
            if executable and unknown:
                counters["undefined_bytes_in_exec"] += item_span
                if unknown_start is None:
                    unknown_start = ea
            elif unknown_start is not None:
                if len(undefined_ranges) < 500:
                    undefined_ranges.append(
                        {
                            "segment": ida_segment.get_segm_name(segment),
                            "start": hex_or_none(unknown_start),
                            "end": hex_or_none(ea),
                            "size": ea - unknown_start,
                        }
                    )
                unknown_start = None
            if code and not executable:
                counters["code_items_in_nonexec"] += 1
                if len(code_in_nonexec) < 500:
                    code_in_nonexec.append(
                        {
                            "segment": ida_segment.get_segm_name(segment),
                            "address": hex_or_none(ea),
                            "name": safe_name(ea),
                        }
                    )
            if data and executable:
                counters["data_items_in_exec"] += 1
                if len(data_in_exec) < 500:
                    data_in_exec.append(
                        {
                            "segment": ida_segment.get_segm_name(segment),
                            "address": hex_or_none(ea),
                            "name": safe_name(ea),
                            "size": ida_bytes.get_item_size(ea),
                        }
                    )
            ea = next_ea
        if unknown_start is not None and len(undefined_ranges) < 500:
            undefined_ranges.append(
                {
                    "segment": ida_segment.get_segm_name(segment),
                    "start": hex_or_none(unknown_start),
                    "end": hex_or_none(segment.end_ea),
                    "size": segment.end_ea - unknown_start,
                }
            )
    return {
        "counters": counters,
        "undefined_ranges_in_exec": undefined_ranges,
        "code_in_nonexec": code_in_nonexec,
        "data_in_exec": data_in_exec,
    }


def decompile_failures(function_rows: list[dict[str, Any]]) -> list[dict[str, Any]]:
    failures: list[dict[str, Any]] = []
    try:
        if not ida_hexrays.init_hexrays_plugin():
            return [{"error": "Hex-Rays plugin unavailable"}]
    except Exception as error:
        return [{"error": str(error)}]
    for index, row in enumerate(function_rows):
        if index % 250 == 0 and ida_kernwin.user_cancelled():
            failures.append({"error": "cancelled", "processed": index})
            break
        try:
            if ida_hexrays.decompile(int(row["address"], 16)) is None:
                failures.append(
                    {
                        "address": row["address"],
                        "name": row["name"],
                        "reason": "decompile returned None",
                    }
                )
        except Exception as error:
            failures.append(
                {
                    "address": row["address"],
                    "name": row["name"],
                    "reason": str(error),
                }
            )
    return failures


def overlap_candidates(function_rows: list[dict[str, Any]]) -> list[dict[str, Any]]:
    candidates: list[dict[str, Any]] = []
    rows = sorted(function_rows, key=lambda row: int(row["address"], 16))
    previous = None
    for row in rows:
        if previous and int(row["address"], 16) < int(previous["end"], 16):
            candidates.append(
                {
                    "previous": {"address": previous["address"], "end": previous["end"], "name": previous["name"]},
                    "current": {"address": row["address"], "end": row["end"], "name": row["name"]},
                }
            )
            if len(candidates) >= 500:
                break
        if previous is None or int(row["end"], 16) > int(previous["end"], 16):
            previous = row
    return candidates


def core_stats(
    metadata: dict[str, Any],
    segments: list[dict[str, Any]],
    entries: list[dict[str, Any]],
    imports: list[dict[str, Any]],
    functions: list[dict[str, Any]],
    names: list[dict[str, Any]],
    data_names: list[dict[str, Any]],
    strings: list[dict[str, Any]],
    types: list[dict[str, Any]],
    anomalies: dict[str, Any],
    failures: list[dict[str, Any]],
) -> dict[str, Any]:
    function_names = [row for row in functions if row["name"]]
    return {
        "metadata": {
            "input_sha256": metadata.get("input_sha256"),
            "input_size": metadata.get("input_size"),
            "imagebase": metadata.get("imagebase"),
            "ida_version": metadata.get("ida_version"),
            "architecture": metadata.get("architecture"),
        },
        "segments": [{key: row[key] for key in ("name", "start", "end", "size", "permissions")} for row in segments],
        "entrypoint_count": len(entries),
        "import_count": len([row for row in imports if "error" not in row]),
        "function_count": len(functions),
        "named_function_count": len([row for row in function_names if not row["auto_name"]]),
        "auto_named_function_count": len([row for row in functions if row["auto_name"]]),
        "library_function_count": len([row for row in functions if row["flags"]["library"]]),
        "thunk_count": len([row for row in functions if row["flags"]["thunk"]]),
        "noreturn_count": len([row for row in functions if row["flags"]["noreturn"]]),
        "function_comment_count": len([row for row in functions if row["comment"] or row["repeatable_comment"]]),
        "named_entity_count": len(names),
        "named_data_entity_count": len(data_names),
        "string_count": len(strings),
        "local_type_count": len(types),
        "decompile_failure_count": len(failures),
        "undefined_bytes_in_exec": anomalies["counters"]["undefined_bytes_in_exec"],
        "code_items_in_nonexec": anomalies["counters"]["code_items_in_nonexec"],
        "data_items_in_exec": anomalies["counters"]["data_items_in_exec"],
    }


def main() -> None:
    output_value = os.environ.get("SAN9_IDA_HEALTH_OUTPUT")
    if not output_value:
        raise RuntimeError("SAN9_IDA_HEALTH_OUTPUT is not set")
    output = Path(output_value)
    output.parent.mkdir(parents=True, exist_ok=True)

    input_sha256 = None
    try:
        value = ida_nalt.retrieve_input_file_sha256()
        input_sha256 = bytes(value).hex() if value else None
    except Exception:
        pass
    input_size = None
    try:
        input_size = ida_nalt.retrieve_input_file_size()
    except Exception:
        pass
    try:
        architecture = "64-bit" if ida_ida.inf_is_64bit() else "32-bit"
    except Exception:
        architecture = None

    metadata = {
        "ida_version": ida_kernwin.get_kernel_version(),
        "idb_path": idc.get_idb_path(),
        "input_path": ida_nalt.get_input_file_path(),
        "input_sha256": input_sha256,
        "input_size": input_size,
        "imagebase": hex_or_none(ida_nalt.get_imagebase()),
        "module": ida_nalt.get_root_filename(),
        "architecture": architecture,
    }
    segments = export_segments()
    entries = export_entries()
    imports = export_imports()
    all_names, data_names = export_names()
    function_rows = [row for ea in idautils.Functions() if (row := function_row(ea))]
    string_rows, runtime_rows = export_strings()
    local_types = export_local_types()
    anomalies = scan_code_data_anomalies(segments)
    failures = decompile_failures(function_rows)
    if any(row.get("error") == "cancelled" for row in failures):
        raise RuntimeError("health baseline cancelled before completion")

    for row in function_rows:
        row["candidate_rank"] = row["code_xref_count"]
    high_ref_unnamed = sorted(
        [row for row in function_rows if row["auto_name"] and row["code_xref_count"] >= 5],
        key=lambda row: (-row["code_xref_count"], int(row["address"], 16)),
    )[:500]
    short_functions = sorted(
        [row for row in function_rows if row["size"] <= 5],
        key=lambda row: (row["size"], int(row["address"], 16)),
    )[:500]
    large_functions = sorted(
        [row for row in function_rows if row["size"] >= 4096],
        key=lambda row: (-row["size"], int(row["address"], 16)),
    )[:500]
    high_ref_untyped_globals = sorted(
        [row for row in data_names if not row["type"] and row["xref_count"] >= 5],
        key=lambda row: (-row["xref_count"], int(row["address"], 16)),
    )[:500]
    jump_table_names = [row for row in data_names if JUMP_TABLE_NAME.fullmatch(row["name"])]
    vtable_names = [row for row in data_names if VTABLE_NAME.search(row["name"])]

    candidates = {
        "high_ref_unnamed_functions": high_ref_unnamed,
        "short_functions": short_functions,
        "large_functions": large_functions,
        "high_ref_untyped_globals": high_ref_untyped_globals,
        "overlapping_functions": overlap_candidates(function_rows),
        "runtime_class_strings": sorted(runtime_rows, key=lambda row: (-row["xref_count"], int(row["address"], 16))),
        "jump_table_names": jump_table_names,
        "vtable_or_vftable_names": vtable_names,
    }
    stats = core_stats(
        metadata,
        segments,
        entries,
        imports,
        function_rows,
        all_names,
        data_names,
        string_rows,
        local_types,
        anomalies,
        failures,
    )
    core_digest = hashlib.sha256(json.dumps(stats, ensure_ascii=False, sort_keys=True).encode("utf-8")).hexdigest()
    report = {
        "schema_version": 1,
        "metadata": metadata,
        "core_stats": stats,
        "core_stats_sha256": core_digest,
        "segments": segments,
        "entrypoints": entries,
        "imports": imports,
        "functions": function_rows,
        "names": all_names,
        "strings": string_rows,
        "local_types": local_types,
        "decompile_failures": failures,
        "anomalies": anomalies,
        "candidates": candidates,
    }
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"output": str(output), "core_stats_sha256": core_digest, "core_stats": stats}, ensure_ascii=False))


if __name__ == "__main__":
    main()

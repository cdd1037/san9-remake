"""Export named virtual-table candidates without changing the IDB.

Set SAN9_IDA_VTABLE_OUTPUT to the JSON output path before execution. The
exporter records raw slots, target segments, function starts, names and data
cross-references. It deliberately does not infer a final table boundary or
rename/type any slot.
"""

from __future__ import annotations

import hashlib
import json
import os
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


PROBE_SLOTS = 32


def hex_or_none(value: int | None) -> str | None:
    return None if value is None or value == ida_idaapi.BADADDR else f"0x{value:X}"


def segment_name(ea: int) -> str | None:
    segment = ida_segment.getseg(ea)
    return ida_segment.get_segm_name(segment) if segment else None


def target_row(value: int) -> dict[str, Any]:
    function = ida_funcs.get_func(value)
    return {
        "value": hex_or_none(value),
        "segment": segment_name(value),
        "function_start": hex_or_none(function.start_ea) if function else None,
        "name": ida_name.get_name(value) or None,
        "is_code_item": bool(ida_bytes.is_code(ida_bytes.get_full_flags(value))),
    }


def xref_list(ea: int) -> list[str]:
    return [hex(ref.frm) for ref in idautils.XrefsTo(ea, 0)]


def export_candidate(ea: int, name: str) -> dict[str, Any]:
    slots = []
    for slot in range(PROBE_SLOTS):
        slot_ea = ea + slot * 4
        value = int(ida_bytes.get_dword(slot_ea))
        slots.append(
            {
                "slot": slot,
                "address": hex_or_none(slot_ea),
                "target": target_row(value),
            }
        )
    return {
        "address": hex_or_none(ea),
        "name": name,
        "segment": segment_name(ea),
        "probe_slots": PROBE_SLOTS,
        "data_xrefs": xref_list(ea),
        "slots": slots,
    }


def main() -> None:
    output_value = os.environ.get("SAN9_IDA_VTABLE_OUTPUT")
    if not output_value:
        raise RuntimeError("SAN9_IDA_VTABLE_OUTPUT is not set")
    output = Path(output_value)
    output.parent.mkdir(parents=True, exist_ok=True)

    input_sha256 = None
    try:
        value = ida_nalt.retrieve_input_file_sha256()
        input_sha256 = bytes(value).hex() if value else None
    except Exception:
        pass

    rows = []
    cancelled = False
    for index in range(ida_name.get_nlist_size()):
        if index % 250 == 0 and ida_kernwin.user_cancelled():
            cancelled = True
            break
        ea = ida_name.get_nlist_ea(index)
        name = ida_name.get_name(ea) or ""
        lowered = name.lower()
        if "vtable" not in lowered and "vftable" not in lowered and "??_7" not in name:
            continue
        if segment_name(ea) != ".rdata":
            continue
        rows.append(export_candidate(ea, name))

    if cancelled:
        raise RuntimeError("vtable export cancelled before completion")

    rows.sort(key=lambda row: int(row["address"], 16))
    core = {
        "input_sha256": input_sha256,
        "ida_version": ida_kernwin.get_kernel_version(),
        "architecture": "64-bit" if ida_ida.inf_is_64bit() else "32-bit",
        "probe_slots": PROBE_SLOTS,
        "candidate_count": len(rows),
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

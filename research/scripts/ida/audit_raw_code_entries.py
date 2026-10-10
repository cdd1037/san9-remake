"""Read-only PE32 entry audit independent of IDA names and pointer item flags.

Set SAN9_IDA_ENTRY_AUDIT_OUTPUT to a path under local/generated/research.
Raw matches are candidates, not proof of functions: strings, tables and
instruction operands can accidentally contain executable addresses.
"""

from __future__ import annotations

import json
import os
import struct
from pathlib import Path

import ida_bytes
import ida_funcs
import ida_ida
import ida_name
import ida_segment
import ida_ua
import idautils


def executable(address: int) -> bool:
    segment = ida_segment.getseg(address)
    return bool(segment and segment.perm & ida_segment.SEGPERM_EXEC)


def describe(address: int) -> dict:
    function = ida_funcs.get_func(address)
    return {
        "address": f"0x{address:08X}",
        "name": ida_name.get_name(address),
        "owner": f"0x{function.start_ea:08X}" if function else None,
    }


def audit() -> dict:
    if not ida_ida.inf_is_32bit_exactly():
        raise ValueError("This source-format audit requires a PE32 database")
    raw = {}
    for start in idautils.Segments():
        segment = ida_segment.getseg(start)
        if segment.perm & ida_segment.SEGPERM_EXEC:
            continue
        data = ida_bytes.get_bytes(start, segment.end_ea - start)
        if not data:
            continue
        for offset in range((-start) % 4, len(data) - 3, 4):
            target = struct.unpack_from("<I", data, offset)[0]
            if executable(target) and not ida_funcs.get_func(target):
                raw.setdefault(target, []).append(f"0x{start + offset:08X}")
    calls = {}
    for start in idautils.Functions():
        for address in idautils.FuncItems(start):
            instruction = ida_ua.insn_t()
            if not ida_ua.decode_insn(instruction, address):
                continue
            if instruction.get_canon_mnem() != "call":
                continue
            operand = instruction.ops[0]
            if operand.type != ida_ua.o_near or not executable(operand.addr):
                continue
            function = ida_funcs.get_func(operand.addr)
            if not function or function.start_ea != operand.addr:
                calls.setdefault(operand.addr, []).append(f"0x{address:08X}")
    return {
        "schema_version": 1,
        "raw_unowned_targets": [
            {**describe(target), "sources": sources}
            for target, sources in sorted(raw.items())
        ],
        "direct_call_boundary_candidates": [
            {**describe(target), "sources": sources}
            for target, sources in sorted(calls.items())
        ],
    }


if __name__ == "__main__":
    result = audit()
    destination = Path(os.environ["SAN9_IDA_ENTRY_AUDIT_OUTPUT"])
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"Entry audit written to {destination}")

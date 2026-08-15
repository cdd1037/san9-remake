"""Classify a generated source catalog against the currently open IDB.

This script is read-only unless --apply-comments is supplied. It never patches
bytes or creates functions. Use the JSON report as the dry-run artifact.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
from pathlib import Path

import ida_bytes
import ida_funcs
import ida_name
import ida_segment
import idc


AUTO_NAME = re.compile(r"^(?:sub|loc|locret|nullsub|j_sub|unk|byte|word|dword|qword|off|asc|stru|algn)_[0-9A-Fa-f]+$")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("catalog", type=Path)
    parser.add_argument("report", type=Path)
    argv = list(getattr(idc, "ARGV", [])) or None
    args = parser.parse_args(argv[1:] if argv else None)
    candidates = json.loads(args.catalog.read_text(encoding="utf-8"))
    report = []
    for candidate in candidates:
        ea = int(candidate["address"], 16)
        segment = ida_segment.getseg(ea)
        current_name = ida_name.get_name(ea)
        function = ida_funcs.get_func(ea)
        if segment is None:
            status = "outside"
        elif current_name and not AUTO_NAME.fullmatch(current_name):
            status = "applied" if current_name == candidate["source_name"] else "conflict"
        elif not ida_bytes.is_loaded(ea):
            status = "target-mismatch"
        else:
            status = "pending"
        target_window = ida_bytes.get_bytes(ea, 32) if segment and ida_bytes.is_loaded(ea) else None
        report.append(
            {
                **candidate,
                "status": status,
                "current_name": current_name or None,
                "current_kind": "function" if function and function.start_ea == ea else "data",
                "segment": ida_segment.get_segm_name(segment) if segment else None,
                "item_size": ida_bytes.get_item_size(ea) if segment else None,
                "type": idc.get_type(ea) if segment else None,
                "source_location": f"{candidate.get('source_path')}:{candidate.get('line')}",
                "old_bytes": candidate.get("old_bytes"),
                "target_bytes": target_window.hex().upper() if target_window else None,
                "target_bytes_sha256_32": hashlib.sha256(target_window).hexdigest() if target_window else None,
                "match_method": "address-and-target-kind" if segment else "address-outside-target",
            }
        )
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    counts = {}
    for row in report:
        counts[row["status"]] = counts.get(row["status"], 0) + 1
    print(json.dumps({"total": len(report), "statuses": counts}, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    # Keep IDA batch/MCP runs free of a misleading SystemExit traceback.
    main()

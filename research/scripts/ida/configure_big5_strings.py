"""Discover, export, and optionally define strict-CP950 EXE strings in IDA.

The default mode is read-only. Set ``SAN9_IDA_BIG5_APPLY=1`` or pass
``--apply`` only after reviewing the dry-run report. The script changes IDB
metadata only; it never patches input bytes.
"""

from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path
from typing import Any

import ida_auto
import ida_bytes
import ida_funcs
import ida_idaapi
import ida_loader
import ida_name
import ida_nalt
import ida_segment
import idautils
import idc


SCRIPT_DIR = Path(__file__).resolve().parent
sys.path.insert(0, str(SCRIPT_DIR.parent))

from legacy_text import scan_null_terminated_big5  # noqa: E402


EXPECTED_INPUT_SHA256 = (
    "d20794aeff67301ec2bf8c3becb1e9944c68c6c0588fbfd4bf04e8597f0e5028"
)
MAX_STRING_BYTES = 512


def _parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--report",
        type=Path,
        default=Path(os.environ.get("SAN9_IDA_BIG5_OUTPUT", "artifacts/ida/exe-strings.json")),
    )
    parser.add_argument(
        "--apply",
        action="store_true",
        default=os.environ.get("SAN9_IDA_BIG5_APPLY") == "1",
    )
    argv = list(getattr(idc, "ARGV", sys.argv))
    return parser.parse_args(argv[1:] if argv else [])


def _input_sha256() -> str:
    value = ida_nalt.retrieve_input_file_sha256()
    return bytes(value).hex() if value else ""


def _xref_rows(address: int) -> list[dict[str, object]]:
    rows: list[dict[str, object]] = []
    for xref in idautils.XrefsTo(address, 0):
        function = ida_funcs.get_func(xref.frm)
        rows.append(
            {
                "from": f"0x{xref.frm:X}",
                "type": int(xref.type),
                "function": (
                    {
                        "address": f"0x{function.start_ea:X}",
                        "name": ida_name.get_name(function.start_ea),
                    }
                    if function
                    else None
                ),
            }
        )
    return rows


def _file_offset(address: int) -> str | None:
    offset = ida_loader.get_fileregion_offset(address)
    return None if offset == -1 or offset == ida_idaapi.BADADDR else f"0x{offset:X}"


def _span_status(address: int, size: int) -> tuple[bool, bool]:
    first_flags = ida_bytes.get_full_flags(address)
    already_string = ida_bytes.is_strlit(first_flags)
    if already_string:
        return True, True
    for current in range(address, address + size):
        if not ida_bytes.is_unknown(ida_bytes.get_full_flags(current)):
            return False, False
    return True, False


def _cjk_count(text: str) -> int:
    return sum("\u3400" <= character <= "\u9FFF" for character in text)


def _candidate_status(
    segment_name: str,
    text: str,
    xref_count: int,
    span_available: bool,
    already_string: bool,
) -> str:
    if already_string:
        return "already-applied"
    if not span_available:
        return "blocked-overlap"
    cjk_count = _cjk_count(text)
    if segment_name == ".rdata" and cjk_count >= 2:
        return "ready"
    if xref_count > 0 and cjk_count >= 1:
        return "ready"
    return "review"


def _scan_candidates(source_id: str) -> list[dict[str, object]]:
    rows: list[dict[str, object]] = []
    for start in idautils.Segments():
        segment = ida_segment.getseg(start)
        if not segment or segment.perm & ida_segment.SEGPERM_EXEC:
            continue
        segment_name = ida_segment.get_segm_name(segment)
        if segment_name != ".rdata":
            continue
        data = ida_bytes.get_bytes(segment.start_ea, segment.end_ea - segment.start_ea)
        if not data:
            continue
        for candidate in scan_null_terminated_big5(
            data,
            max_raw_length=MAX_STRING_BYTES,
            primary_encoding="cp950",
        ):
            address = segment.start_ea + candidate.offset
            file_offset = _file_offset(address)
            if file_offset is None:
                continue
            xrefs = _xref_rows(address)
            span_available, already_string = _span_status(
                address,
                len(candidate.raw) + 1,
            )
            cjk_count = _cjk_count(candidate.text)
            if not span_available or cjk_count == 0:
                continue
            status = _candidate_status(
                segment_name,
                candidate.text,
                len(xrefs),
                span_available,
                already_string,
            )
            rows.append(
                {
                    "source_id": source_id,
                    "source_kind": "exe",
                    "locator": f"San9PK.exe@VA:0x{address:X}",
                    "virtual_address": f"0x{address:X}",
                    "file_offset": file_offset,
                    "segment": segment_name,
                    "raw_hex": candidate.raw.hex(" ").upper(),
                    "segments": candidate.segments,
                    "traditional_text": candidate.text,
                    "encoding": "cp950",
                    "decode_status": "exact",
                    "discovery": "nul-delimited-strict-big5",
                    "xref_count": len(xrefs),
                    "xrefs": xrefs,
                    "cjk_count": cjk_count,
                    "status": status,
                }
            )
    return sorted(rows, key=lambda row: int(str(row["virtual_address"]), 16))


def _existing_ascii_records(source_id: str) -> list[dict[str, object]]:
    rows: list[dict[str, object]] = []
    strings = idautils.Strings()
    strings.setup()
    for item in strings:
        value = str(item)
        if not value or any(ord(character) >= 0x80 for character in value):
            continue
        address = int(item.ea)
        raw = ida_bytes.get_bytes(address, len(value.encode("ascii")))
        if raw is None:
            continue
        segment = ida_segment.getseg(address)
        xrefs = _xref_rows(address)
        rows.append(
            {
                "source_id": source_id,
                "source_kind": "exe",
                "locator": f"San9PK.exe@VA:0x{address:X}",
                "virtual_address": f"0x{address:X}",
                "file_offset": _file_offset(address),
                "segment": ida_segment.get_segm_name(segment) if segment else None,
                "raw_hex": raw.hex(" ").upper(),
                "segments": [{"kind": "text", "value": value}],
                "traditional_text": value,
                "encoding": "ascii",
                "decode_status": "exact",
                "discovery": "ida-existing-string",
                "xref_count": len(xrefs),
                "xrefs": xrefs,
                "status": "existing",
            }
        )
    return rows


def _find_encoding(name: str) -> int | None:
    for index in range(1, ida_nalt.get_encoding_qty() + 1):
        current = ida_nalt.get_encoding_name(index)
        if current and current.casefold() == name.casefold():
            return index
    return None


def _apply_candidates(rows: list[dict[str, object]]) -> tuple[int, int, int]:
    encoding_index = _find_encoding("Big5")
    if encoding_index is None:
        encoding_index = ida_nalt.add_encoding("Big5")
    if not encoding_index:
        raise RuntimeError("IDA rejected the Big5 encoding")
    string_type = ida_nalt.make_str_type(ida_nalt.STRTYPE_C, encoding_index)
    applied = 0
    verified = 0
    failed = 0
    for row in rows:
        if row["status"] not in ("ready", "already-applied"):
            continue
        address = int(str(row["virtual_address"]), 16)
        raw_size = len(bytes.fromhex(str(row["raw_hex"])))
        if row["status"] == "ready":
            if not ida_bytes.create_strlit(address, raw_size + 1, string_type):
                row["status"] = "apply-failed"
                failed += 1
                continue
            applied += 1
        idc.set_cmt(
            address,
            "[RE-SAN9PK-0050] Strict CP950/Windows Big5 C string; raw bytes retained in local catalog.",
            1,
        )
        value = ida_bytes.get_strlit_contents(address, raw_size, string_type)
        rendered = value.decode("utf-8") if value else None
        if rendered != row["traditional_text"]:
            row["status"] = "verify-failed"
            row["ida_text"] = rendered
            failed += 1
            continue
        row["status"] = "verified"
        row["ida_text"] = rendered
        verified += 1
    ida_auto.auto_wait()
    return applied, verified, failed


def main() -> None:
    args = _parse_args()
    actual_hash = _input_sha256()
    if actual_hash.lower() != EXPECTED_INPUT_SHA256:
        raise RuntimeError(
            f"unexpected input SHA-256: {actual_hash or '(missing)'}"
        )
    source_id = f"San9PK.exe:{actual_hash[:12]}"
    rows = _scan_candidates(source_id)
    ascii_rows = _existing_ascii_records(source_id)
    applied = 0
    verified = 0
    failed = 0
    if args.apply:
        applied, verified, failed = _apply_candidates(rows)

    status_counts: dict[str, int] = {}
    all_records = sorted(
        ascii_rows + rows,
        key=lambda row: int(str(row["virtual_address"]), 16),
    )
    for row in all_records:
        status = str(row["status"])
        status_counts[status] = status_counts.get(status, 0) + 1
    report: dict[str, Any] = {
        "schema_version": 1,
        "mode": "apply" if args.apply else "dry-run",
        "authority_language": "zh-TW",
        "derived_language": None,
        "sources": [
            {
                "source_id": source_id,
                "source_kind": "exe",
                "file_name": "San9PK.exe",
                "sha256": actual_hash,
                "imagebase": f"0x{ida_nalt.get_imagebase():X}",
            }
        ],
        "records": all_records,
        "diagnostics": {
            "candidate_count": len(rows),
            "existing_ascii_count": len(ascii_rows),
            "status_counts": status_counts,
            "applied_count": applied,
            "verified_count": verified,
            "failed_count": failed,
            "strict_cp950": True,
            "hkscs_fallback": False,
        },
    }
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(
        json.dumps(report, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(json.dumps(report["diagnostics"], ensure_ascii=False))
    if failed:
        raise RuntimeError(f"{failed} Big5 strings failed apply verification")


if __name__ == "__main__":
    main()

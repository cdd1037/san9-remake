"""Build a complete, auditable catalog of San9PKHard patch candidates.

The source is old Visual C++ code with several registration styles.  This
scanner deliberately records syntax observations before assigning semantics:
address symbols/arrays, ASM_PATCH entries, byte thunks, WriteProcessMemory
calls, and MFC DDX configuration bindings.  It never executes the tool or
writes the game image.
"""

from __future__ import annotations

import argparse
import ast
import bisect
import hashlib
import json
import re
import struct
from collections import Counter, defaultdict
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable

from verify_patch_points import pe_image_layout, virtual_to_file_offset


SOURCE_SUFFIXES = {".h", ".cpp"}
CONFIG_NAMES = {
    "San9PKHardPara.txt": "global-parameters",
    "San9PKHardPerson.txt": "person-configuration",
    "San9PKHardCity.txt": "city-configuration",
    "San9PKHardArray.txt": "formation-configuration",
    "San9PKHardSkill.txt": "tactic-configuration",
}
SIZE_CONSTANTS = {"BYTE_SIZE": 1, "WORD_SIZE": 2, "DWORD_SIZE": 4}


@dataclass(frozen=True)
class Source:
    path: Path
    relative: str
    text: str
    lines: list[str]
    comment_spans: list[tuple[int, int]]


def decode_source(data: bytes) -> str:
    for encoding in ("utf-8-sig", "gb18030"):
        try:
            return data.decode(encoding)
        except UnicodeDecodeError:
            pass
    return data.decode("latin1")


def load_sources(root: Path) -> list[Source]:
    sources = []
    for path in sorted(root.rglob("*")):
        if path.is_file() and path.suffix.lower() in SOURCE_SUFFIXES:
            text = decode_source(path.read_bytes())
            comment_spans = [
                match.span()
                for match in re.finditer(r"/\*.*?\*/|//[^\r\n]*", text, flags=re.S)
            ]
            sources.append(Source(path, path.relative_to(root).as_posix(), text, text.splitlines(), comment_spans))
    return sources


def line_number(text: str, offset: int) -> int:
    return text.count("\n", 0, offset) + 1


def offset_is_commented(source: Source, offset: int) -> bool:
    starts = [span[0] for span in source.comment_spans]
    index = bisect.bisect_right(starts, offset) - 1
    return index >= 0 and offset < source.comment_spans[index][1]


def strip_comments(text: str) -> str:
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    return re.sub(r"//[^\r\n]*", "", text)


def safe_int(expression: str, constants: dict[str, int] | None = None) -> int | None:
    constants = constants or {}
    expr = strip_comments(expression).strip()
    expr = re.sub(r"\b([0-9A-Fa-f]+)[Hh]\b", lambda m: "0x" + m.group(1), expr)
    expr = re.sub(r"\(\s*LPVOID\s*\)", "", expr)
    expr = re.sub(r"\b(?:DWORD|WORD|BYTE|UINT|int|LPVOID)\b", "", expr)
    expr = re.sub(
        r"\b[A-Za-z_]\w*\b",
        lambda match: str(constants.get(match.group(0), match.group(0))),
        expr,
    )
    if not re.fullmatch(r"[0-9A-Fa-fxX()+*\-/\s]+", expr):
        return None
    try:
        tree = ast.parse(expr, mode="eval")
        value = evaluate_integer_ast(tree.body)
    except (SyntaxError, TypeError, ZeroDivisionError, ValueError):
        return None
    return value if isinstance(value, int) else None


def evaluate_integer_ast(node: ast.AST) -> int:
    if isinstance(node, ast.Constant) and isinstance(node.value, int):
        return node.value
    if isinstance(node, ast.UnaryOp) and isinstance(node.op, (ast.UAdd, ast.USub)):
        value = evaluate_integer_ast(node.operand)
        return value if isinstance(node.op, ast.UAdd) else -value
    if isinstance(node, ast.BinOp) and isinstance(node.op, (ast.Add, ast.Sub, ast.Mult, ast.Div, ast.FloorDiv)):
        left, right = evaluate_integer_ast(node.left), evaluate_integer_ast(node.right)
        if isinstance(node.op, ast.Add): return left + right
        if isinstance(node.op, ast.Sub): return left - right
        if isinstance(node.op, ast.Mult): return left * right
        return left // right
    raise ValueError("unsupported integer expression")


def balanced_extent(text: str, start: int, open_char: str, close_char: str) -> int | None:
    depth = 0
    quote = None
    escaped = False
    for index in range(start, len(text)):
        char = text[index]
        if quote:
            if escaped:
                escaped = False
            elif char == "\\":
                escaped = True
            elif char == quote:
                quote = None
            continue
        if char in "\"'":
            quote = char
        elif char == open_char:
            depth += 1
        elif char == close_char:
            depth -= 1
            if depth == 0:
                return index
    return None


def split_arguments(text: str) -> list[str]:
    result, start = [], 0
    paren = bracket = brace = 0
    for index, char in enumerate(text):
        if char == "(": paren += 1
        elif char == ")": paren -= 1
        elif char == "[": bracket += 1
        elif char == "]": bracket -= 1
        elif char == "{": brace += 1
        elif char == "}": brace -= 1
        elif char == "," and not (paren or bracket or brace):
            result.append(text[start:index].strip())
            start = index + 1
    result.append(text[start:].strip())
    return result


def initializer_arrays(sources: Iterable[Source], type_name: str) -> list[dict[str, object]]:
    pattern = re.compile(rf"\b{type_name}\s+([A-Za-z_]\w*)\s*((?:\[[^\]]*\])+)?\s*=\s*\{{")
    result = []
    for source in sources:
        for match in pattern.finditer(source.text):
            opening = source.text.find("{", match.start())
            closing = balanced_extent(source.text, opening, "{", "}")
            if closing is None:
                continue
            line = line_number(source.text, match.start())
            result.append({
                "name": match.group(1),
                "dimensions": match.group(2) or "",
                "body": source.text[opening + 1:closing],
                "source": source.relative,
                "line": line,
                "active": not offset_is_commented(source, match.start()),
            })
    return result


def collect_constants(sources: Iterable[Source]) -> tuple[dict[str, int], list[dict[str, object]]]:
    constants: dict[str, int] = dict(SIZE_CONSTANTS)
    observations: list[dict[str, object]] = []
    define_pattern = re.compile(r"^\s*#define\s+([A-Za-z_]\w*)\s+([^\r\n/]+)", re.M)
    scalar_pattern = re.compile(r"\b(?:const\s+)?(?:DWORD|WORD|BYTE|UINT|int|BOOL)\s+([A-Za-z_]\w*)\s*=\s*([^,;{}]+)[,;]")
    # Constructor initializers in this source use scalar expressions.  Keep
    # this deliberately line-bounded: a permissive nested-expression regex
    # backtracks catastrophically across the 600 KiB injection headers.
    ctor_pattern = re.compile(r"\b(m_[A-Za-z_]\w*)\s*\(\s*([^(),\r\n]+)\s*\)")
    assignment_pattern = re.compile(r"\b(m_[A-Za-z_]\w*)\s*=\s*([^;{}]+);")
    candidates: list[tuple[Source, re.Match[str], str]] = []
    for source in sources:
        candidates.extend((source, match, "define") for match in define_pattern.finditer(source.text))
        candidates.extend((source, match, "scalar") for match in scalar_pattern.finditer(source.text))
        candidates.extend((source, match, "constructor") for match in ctor_pattern.finditer(source.text))
        candidates.extend((source, match, "assignment") for match in assignment_pattern.finditer(source.text))
    # Multiple passes resolve simple symbolic expressions without evaluating C++.
    for _ in range(4):
        changed = False
        for source, match, _kind in candidates:
            name, expression = match.group(1), match.group(2).strip()
            value = safe_int(expression, constants)
            if value is not None and constants.get(name) != value:
                constants[name] = value
                changed = True
        if not changed:
            break
    seen = set()
    for source, match, kind in candidates:
        name, expression = match.group(1), match.group(2).strip()
        if not ("addr" in name.lower() or "address" in name.lower()):
            continue
        line = line_number(source.text, match.start())
        key = (source.relative, line, name, expression, kind)
        if key in seen:
            continue
        seen.add(key)
        value = safe_int(expression, constants)
        active = not offset_is_commented(source, match.start())
        observations.append({
            "symbol": name,
            "expression": expression,
            "address": f"0x{value:X}" if value is not None else None,
            "source": source.relative,
            "line": line,
            "syntax": kind,
            "active": active,
            "classification": classify_address(value, name),
            "exclusion_reason": "commented-out" if not active else ("unresolved-expression" if value is None else None),
        })
    return constants, observations


def classify_address(value: int | None, name: str) -> str:
    lowered = name.lower()
    if value is None:
        return "unresolved"
    if value < 0x400000:
        return "relative-offset-or-count"
    if any(word in lowered for word in ("page", "flag", "counter", "info", "config")):
        return "runtime-or-injected-data"
    return "image-address-candidate"


def collect_byte_arrays(sources: Iterable[Source]) -> list[dict[str, object]]:
    arrays = initializer_arrays(sources, "BYTE")
    result = []
    for item in arrays:
        name = str(item["name"])
        if not (name.startswith("code") or name.endswith("Thunk")):
            continue
        clean = strip_comments(str(item["body"]))
        values = [int(token, 16) for token in re.findall(r"\b0x([0-9A-Fa-f]{1,2})\b", clean)]
        result.append({
            "symbol": name,
            "source": item["source"],
            "line": item["line"],
            "active": item["active"],
            "role": "old-bytes" if "Old" in name else "replacement-bytes",
            "byte_count": len(values),
            "hex": bytes(values).hex(" ").upper() if "[" not in str(item["dimensions"]).strip("[]") else None,
            "dimensions": item["dimensions"],
            "exclusion_reason": "commented-out" if not item["active"] else None,
        })
    declared = {str(item["symbol"]) for item in result}
    declaration_pattern = re.compile(
        r"\bBYTE\s+([A-Za-z_]\w*(?:OldThunk|OldCode))\s*\[\s*sizeof\s*\(\s*([A-Za-z_]\w*)\s*\)\s*\]\s*;"
    )
    for source in sources:
        for match in declaration_pattern.finditer(source.text):
            if match.group(1) in declared:
                continue
            line = line_number(source.text, match.start())
            active = not offset_is_commented(source, match.start())
            result.append({
                "symbol": match.group(1),
                "source": source.relative,
                "line": line,
                "active": active,
                "role": "runtime-captured-old-bytes",
                "byte_count": None,
                "hex": None,
                "dimensions": f"[sizeof({match.group(2)})]",
                "sized_from": match.group(2),
                "exclusion_reason": "commented-out" if not active else None,
            })
    return result


def collect_address_arrays(sources: Iterable[Source], constants: dict[str, int]) -> list[dict[str, object]]:
    result = []
    for item in initializer_arrays(sources, "DWORD"):
        name = str(item["name"])
        if not (name.lower().startswith("dwaddr") or "address" in name.lower()):
            continue
        expressions = split_arguments(strip_comments(str(item["body"])))
        for index, expression in enumerate(expressions):
            if not expression:
                continue
            value = safe_int(expression, constants)
            result.append({
                "symbol": name,
                "index": index,
                "expression": expression,
                "address": f"0x{value:X}" if value is not None else None,
                "source": item["source"],
                "line": item["line"],
                "active": item["active"],
                "classification": classify_address(value, name),
                "exclusion_reason": "commented-out" if not item["active"] else ("unresolved-expression" if value is None else None),
            })
    return result


def collect_patch_entries(sources: Iterable[Source], constants: dict[str, int]) -> list[dict[str, object]]:
    result = []
    for item in initializer_arrays(sources, "ASM_PATCH"):
        name = str(item["name"])
        body = strip_comments(str(item["body"]))
        entry_pattern = re.compile(r"\{\s*([^,{}]+)\s*,\s*sizeof\s*\(\s*([A-Za-z_]\w*)\s*\)\s*,\s*([A-Za-z_]\w*)\s*\}")
        for index, match in enumerate(entry_pattern.finditer(body)):
            expression, size_symbol, code_symbol = match.groups()
            value = safe_int(expression, constants)
            active = bool(item["active"])
            result.append({
                "patch": name,
                "index": index,
                "address_expression": expression.strip(),
                "address": f"0x{value:X}" if value is not None else None,
                "code_symbol": code_symbol,
                "size_symbol": size_symbol,
                "role": "restore-old-bytes" if "Old" in code_symbol else "install-replacement",
                "source": item["source"],
                "line": item["line"],
                "active": active,
                "exclusion_reason": "commented-out" if not active else ("unresolved-address" if value is None else None),
            })
    return result


def collect_write_calls(sources: Iterable[Source], constants: dict[str, int]) -> list[dict[str, object]]:
    result = []
    marker = "WriteProcessMemory"
    for source in sources:
        offset = 0
        while True:
            found = source.text.find(marker, offset)
            if found < 0:
                break
            opening = source.text.find("(", found + len(marker))
            closing = balanced_extent(source.text, opening, "(", ")") if opening >= 0 else None
            offset = found + len(marker)
            if closing is None:
                continue
            args = split_arguments(strip_comments(source.text[opening + 1:closing]))
            line = line_number(source.text, found)
            active = not offset_is_commented(source, found)
            destination = args[1] if len(args) > 1 else None
            data = args[2] if len(args) > 2 else None
            size = args[3] if len(args) > 3 else None
            dest_symbols = re.findall(r"\b(?:m_|dwAddr|[A-Za-z_]\w*Addr)[A-Za-z0-9_]*", destination or "")
            # Old-value symbols consistently use a capital `Old`.  A
            # case-insensitive substring test misclassifies names containing
            # `Soldier` as restore operations.
            role = "restore-old-bytes" if data and "Old" in data else "direct-write"
            exclusion = None
            if not active:
                exclusion = "commented-out"
            elif destination and re.search(r"(?:forceAddr|personAddr|cityAddr|zoneAddr|buildingAddr|killerAddr|arrayAddr)", destination):
                exclusion = "runtime-record-write"
            address = safe_int(destination, constants) if destination else None
            result.append({
                "source": source.relative,
                "line": line,
                "active": active,
                "destination_expression": destination,
                "destination_symbols": dest_symbols,
                "address": f"0x{address:X}" if address is not None else None,
                "classification": (
                    "dynamic-or-patch-structure-write" if address is None
                    else classify_address(address, dest_symbols[0] if dest_symbols else "literal")
                ),
                "data_expression": data,
                "size_expression": size,
                "role": role,
                "exclusion_reason": exclusion,
            })
    return result


def collect_config_bindings(sources: Iterable[Source]) -> list[dict[str, object]]:
    result = []
    pattern = re.compile(r"\b(DDX_[A-Za-z_]\w*)\s*\(\s*pDX\s*,\s*([A-Za-z_]\w*)\s*,\s*([A-Za-z_]\w*)\s*\)")
    for source in sources:
        for match in pattern.finditer(source.text):
            line = line_number(source.text, match.start())
            active = not offset_is_commented(source, match.start())
            result.append({
                "binding": match.group(1),
                "control": match.group(2),
                "field": match.group(3),
                "source": source.relative,
                "line": line,
                "active": active,
                "exclusion_reason": "commented-out" if not active else None,
            })
    return result


def collect_config_bit_mappings(sources: Iterable[Source]) -> list[dict[str, object]]:
    result = []
    save_pattern = re.compile(r"m_dwComboData\s*\|=\s*(m_[A-Za-z_]\w*)\s*<<\s*(\d+)")
    load_pattern = re.compile(r"(m_[A-Za-z_]\w*)\s*=\s*\(m_dwComboData\s*>>\s*(\d+)\)\s*&\s*1")
    for source in sources:
        for direction, pattern in (("save", save_pattern), ("load", load_pattern)):
            for match in pattern.finditer(source.text):
                line = line_number(source.text, match.start())
                active = not offset_is_commented(source, match.start())
                result.append({
                    "direction": direction,
                    "field": match.group(1),
                    "bit": int(match.group(2)),
                    "source": source.relative,
                    "line": line,
                    "active": active,
                    "exclusion_reason": "commented-out" if not active else None,
                })
    return result


def collect_old_value_symbols(sources: Iterable[Source], constants: dict[str, int]) -> list[dict[str, object]]:
    patterns = (
        ("scalar", re.compile(r"\b(?:const\s+)?(?:DWORD|WORD|BYTE|UINT|int|BOOL)\s+([A-Za-z_]\w*Old[A-Za-z0-9_]*)\s*=\s*([^,;{}]+)[,;]")),
        ("constructor", re.compile(r"\b(m_[A-Za-z_]\w*Old[A-Za-z0-9_]*)\s*\(\s*([^(),\r\n]+)\s*\)")),
    )
    result, seen = [], set()
    for source in sources:
        for syntax, pattern in patterns:
            for match in pattern.finditer(source.text):
                line = line_number(source.text, match.start())
                key = (source.relative, line, match.group(1), syntax)
                if key in seen:
                    continue
                seen.add(key)
                value = safe_int(match.group(2), constants)
                active = not offset_is_commented(source, match.start())
                result.append({
                    "symbol": match.group(1),
                    "expression": match.group(2).strip(),
                    "value": value,
                    "source": source.relative,
                    "line": line,
                    "active": active,
                    "exclusion_reason": "commented-out" if not active else ("unresolved-expression" if value is None else None),
                })
    return result


def collect_scalar_old_records(
    writes: list[dict[str, object]],
    constants: dict[str, int],
) -> list[dict[str, object]]:
    records = []
    for write in writes:
        if not write["active"] or write["role"] != "restore-old-bytes":
            continue
        destination = str(write.get("destination_expression") or "")
        data = str(write.get("data_expression") or "")
        size_expression = str(write.get("size_expression") or "")
        if "patch" in destination and ".dwAddr" in destination:
            continue
        old_match = re.search(r"\b([A-Za-z_]\w*Old[A-Za-z0-9_]*)\b", data)
        old_symbol = old_match.group(1) if old_match else None
        value = constants.get(old_symbol) if old_symbol else None
        size = safe_int(size_expression, constants)
        address = safe_int(destination, constants)
        expected = None
        if value is not None and size in (1, 2, 4, 8):
            expected = (value & ((1 << (size * 8)) - 1)).to_bytes(size, "little").hex(" ").upper()
        if size is None and old_symbol and size_expression == f"sizeof({old_symbol})" and old_symbol.startswith(("m_dw", "dw")):
            size = 4
            if value is not None:
                expected = (value & 0xFFFFFFFF).to_bytes(4, "little").hex(" ").upper()
        runtime_data = bool(old_symbol and "Friendship" in old_symbol)
        records.append({
            "address": f"0x{address:X}" if address is not None else None,
            "destination_expression": destination,
            "old_symbol": old_symbol,
            "size_expression": size_expression,
            "byte_count": size,
            "expected_old_hex": expected,
            "source": write["source"],
            "line": write["line"],
            "status": (
                "runtime-data-restore" if runtime_data
                else "registered" if expected and address is not None
                else "registered-unresolved-scalar-restore"
            ),
        })
    return records


def collect_config_files(root: Path) -> list[dict[str, object]]:
    result = []
    for name, semantic in CONFIG_NAMES.items():
        path = root / name
        data = path.read_bytes()
        text = decode_source(data)
        values = [line.strip() for line in text.splitlines() if line.strip()]
        result.append({
            "path": name,
            "semantic": semantic,
            "value_count": len(values),
            "all_integer": all(re.fullmatch(r"-?\d+", value) for value in values),
            "sha256": hashlib.sha256(data).hexdigest().upper(),
        })
    return result


def collect_file_inventory(root: Path) -> list[dict[str, object]]:
    result = []
    for path in sorted(item for item in root.rglob("*") if item.is_file()):
        relative = path.relative_to(root).as_posix()
        if path.suffix.lower() in SOURCE_SUFFIXES:
            classification, reason = "source-scanned", None
        elif path.name in CONFIG_NAMES:
            classification, reason = "config-scanned", None
        elif path.suffix.lower() in {".rc", ".rc2", ".clw"}:
            classification, reason = "ui-resource-excluded", "no executable patch registration; controls join through DDX bindings"
        elif path.suffix.lower() in {".dsp", ".dsw", ".plg"}:
            classification, reason = "build-metadata-excluded", "no executable patch registration"
        else:
            classification, reason = "binary-ide-asset-excluded", "non-source IDE or icon artifact"
        data = path.read_bytes()
        result.append({
            "path": relative,
            "classification": classification,
            "exclusion_reason": reason,
            "size": len(data),
            "sha256": hashlib.sha256(data).hexdigest().upper(),
        })
    return result


def expected_old_records(
    patch_entries: list[dict[str, object]],
    byte_arrays: list[dict[str, object]],
) -> list[dict[str, object]]:
    arrays = {item["symbol"]: item for item in byte_arrays}
    records = []
    for entry in patch_entries:
        if entry["role"] != "restore-old-bytes":
            continue
        array = arrays.get(entry["code_symbol"])
        records.append({
            "address": entry["address"],
            "patch": entry["patch"],
            "code_symbol": entry["code_symbol"],
            "expected_old_hex": array.get("hex") if array else None,
            "byte_count": array.get("byte_count") if array else None,
            "source": entry["source"],
            "line": entry["line"],
            "status": (
                "runtime-captured" if array and array.get("role") == "runtime-captured-old-bytes"
                else "registered" if array and array.get("hex")
                else "missing-or-nested-old-bytes"
            ),
        })
    return records


def verify_old_records(image: Path | None, records: list[dict[str, object]]) -> dict[str, object] | None:
    if image is None:
        return None
    data = image.read_bytes()
    image_base, sections = pe_image_layout(data)
    counts: Counter[str] = Counter()
    results = []
    for record in records:
        address_text = record.get("address")
        old_hex = record.get("expected_old_hex")
        if not address_text or not old_hex:
            status = "not-verifiable"
            actual = None
        else:
            try:
                address = int(str(address_text), 0)
                expected = bytes.fromhex(str(old_hex))
                offset = virtual_to_file_offset(address, image_base, sections)
                actual_bytes = data[offset:offset + len(expected)]
                actual = actual_bytes.hex(" ").upper()
                status = "direct-match" if actual_bytes == expected else "conflict"
            except ValueError:
                status = "not-file-backed"
                actual = None
        counts[status] += 1
        results.append({**record, "verification": status, "actual_hex": actual})
    return {
        "image_sha256": hashlib.sha256(data).hexdigest().upper(),
        "image_base": f"0x{image_base:X}",
        "counts": dict(sorted(counts.items())),
        "records": results,
    }


def registration_issues(
    address_symbols: list[dict[str, object]],
    address_arrays: list[dict[str, object]],
    patch_entries: list[dict[str, object]],
    byte_arrays: list[dict[str, object]],
    writes: list[dict[str, object]],
    bit_mappings: list[dict[str, object]],
) -> dict[str, list[dict[str, object]]]:
    issues: dict[str, list[dict[str, object]]] = defaultdict(list)
    values: defaultdict[str, list[dict[str, object]]] = defaultdict(list)
    for item in [*address_symbols, *address_arrays, *patch_entries]:
        if item.get("active") and item.get("address"):
            values[str(item["address"])].append(item)
    for address, items in values.items():
        logical = {(str(item.get("symbol") or item.get("patch")), str(item.get("index", ""))) for item in items}
        if len(logical) > 1:
            issues["duplicate_addresses"].append({"address": address, "registrations": sorted(f"{a}[{b}]" for a, b in logical)})
    arrays = {str(item["symbol"]): item for item in byte_arrays if item.get("active")}
    grouped: defaultdict[str, list[dict[str, object]]] = defaultdict(list)
    for entry in patch_entries:
        if entry.get("active"):
            grouped[str(entry["patch"])].append(entry)
            if str(entry["code_symbol"]) not in arrays:
                issues["missing_code_arrays"].append({"patch": entry["patch"], "code_symbol": entry["code_symbol"]})
    for patch, entries in grouped.items():
        installs = [entry for entry in entries if entry["role"] == "install-replacement"]
        restores = [entry for entry in entries if entry["role"] == "restore-old-bytes"]
        if installs and not restores and patch.endswith("Thunk"):
            issues["missing_old_bytes"].append({"patch": patch, "reason": "thunk-has-no-restore-entry"})
        if installs and restores:
            install_addresses = {entry["address"] for entry in installs}
            restore_addresses = {entry["address"] for entry in restores}
            if install_addresses != restore_addresses:
                issues["registration_errors"].append({
                    "patch": patch,
                    "reason": "install-restore-address-mismatch",
                    "install": sorted(str(value) for value in install_addresses),
                    "restore": sorted(str(value) for value in restore_addresses),
                })
            for install in installs:
                new_array = arrays.get(str(install["code_symbol"]))
                candidates = [old for old in restores if old["address"] == install["address"]]
                for old in candidates:
                    old_array = arrays.get(str(old["code_symbol"]))
                    if (
                        new_array and old_array
                        and new_array["byte_count"] is not None
                        and old_array["byte_count"] is not None
                        and new_array["byte_count"] != old_array["byte_count"]
                    ):
                        issues["registration_errors"].append({
                            "patch": patch,
                            "reason": "new-old-length-mismatch",
                            "new_symbol": install["code_symbol"],
                            "new_length": new_array["byte_count"],
                            "old_symbol": old["code_symbol"],
                            "old_length": old_array["byte_count"],
                        })
    restored_addresses = {
        str(item["address"]) for item in writes
        if item.get("active") and item.get("role") == "restore-old-bytes" and item.get("address")
    }
    missing = defaultdict(list)
    for item in writes:
        if (
            item.get("active") and item.get("role") == "direct-write"
            and item.get("classification") == "image-address-candidate"
            and item.get("address") not in restored_addresses
        ):
            missing[str(item["address"])].append({"source": item["source"], "line": item["line"]})
    for address, sites in sorted(missing.items()):
        issues["missing_old_bytes"].append({
            "address": address,
            "reason": "direct-image-write-has-no-resolved-restore-write",
            "write_sites": sites,
        })
    saved = {(str(item["field"]), int(item["bit"])) for item in bit_mappings if item["active"] and item["direction"] == "save"}
    loaded = {(str(item["field"]), int(item["bit"])) for item in bit_mappings if item["active"] and item["direction"] == "load"}
    if saved != loaded:
        issues["registration_errors"].append({
            "reason": "config-bit-save-load-mismatch",
            "save_only": sorted(f"{field}:{bit}" for field, bit in saved - loaded),
            "load_only": sorted(f"{field}:{bit}" for field, bit in loaded - saved),
        })
    return {key: value for key, value in sorted(issues.items())}


def build_catalog(root: Path, image: Path | None = None) -> dict[str, object]:
    sources = load_sources(root)
    constants, address_symbols = collect_constants(sources)
    address_arrays = collect_address_arrays(sources, constants)
    byte_arrays = collect_byte_arrays(sources)
    patch_entries = collect_patch_entries(sources, constants)
    writes = collect_write_calls(sources, constants)
    bindings = collect_config_bindings(sources)
    bit_mappings = collect_config_bit_mappings(sources)
    configs = collect_config_files(root)
    old_symbols = collect_old_value_symbols(sources, constants)
    thunk_old_records = expected_old_records(patch_entries, byte_arrays)
    scalar_old_records = collect_scalar_old_records(writes, constants)
    old_records = [*thunk_old_records, *scalar_old_records]
    issues = registration_issues(address_symbols, address_arrays, patch_entries, byte_arrays, writes, bit_mappings)
    verification = verify_old_records(image, old_records)
    sections = {
        "address_symbols": address_symbols,
        "address_array_entries": address_arrays,
        "patch_entries": patch_entries,
        "byte_arrays": byte_arrays,
        "write_calls": writes,
        "config_bindings": bindings,
        "config_bit_mappings": bit_mappings,
        "config_files": configs,
        "file_inventory": collect_file_inventory(root),
        "old_value_symbols": old_symbols,
        "old_byte_records": old_records,
    }
    summary = {name: len(values) for name, values in sections.items()}
    summary.update({
        "source_files": len(sources),
        "active_write_calls": sum(bool(item["active"]) for item in writes),
        "excluded_write_calls": sum(bool(item["exclusion_reason"]) for item in writes),
        "issue_counts": {name: len(values) for name, values in issues.items()},
    })
    return {
        "schema_version": 1,
        "source_root": "research/upsteam/San9PKHard",
        "scope": "SRC-SAN9PKHARD candidates; source claims only, not original behavior",
        "summary": summary,
        "issues": issues,
        "sections": sections,
        "sample_verification": verification,
    }


def build_status(catalog: dict[str, object]) -> dict[str, object]:
    sections = catalog["sections"]
    section_status = {}
    for name, items in sections.items():
        canonical = json.dumps(items, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")
        active = sum(item.get("active") is True for item in items if isinstance(item, dict))
        inactive = sum(item.get("active") is False for item in items if isinstance(item, dict))
        section_status[name] = {
            "count": len(items),
            "active": active,
            "excluded_or_inactive": inactive,
            "sha256": hashlib.sha256(canonical).hexdigest().upper(),
        }
    canonical_catalog = json.dumps(catalog, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")
    old_status = Counter(str(item["status"]) for item in sections["old_byte_records"])
    file_classes = Counter(str(item["classification"]) for item in sections["file_inventory"])
    return {
        "schema_version": 1,
        "source_root": catalog["source_root"],
        "catalog_sha256": hashlib.sha256(canonical_catalog).hexdigest().upper(),
        "summary": catalog["summary"],
        "section_status": section_status,
        "old_byte_status": dict(sorted(old_status.items())),
        "file_classification": dict(sorted(file_classes.items())),
        "issues": catalog["issues"],
        "source_files": sections["file_inventory"],
        "config_files": sections["config_files"],
        "sample_verification": catalog["sample_verification"],
        "reproduce": "python research/scripts/build_san9pkhard_catalog.py --source-root research/upsteam/San9PKHard --output tmp/san9pkhard-catalog.json --summary-output research/patch-points/san9pkhard-catalog-status.json",
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--summary-output", type=Path)
    parser.add_argument("--image", type=Path)
    args = parser.parse_args()
    catalog = build_catalog(args.source_root, args.image)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(catalog, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    if args.summary_output:
        args.summary_output.parent.mkdir(parents=True, exist_ok=True)
        args.summary_output.write_text(
            json.dumps(build_status(catalog), ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8",
        )
    print(json.dumps(catalog["summary"], ensure_ascii=False, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

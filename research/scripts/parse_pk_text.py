#!/usr/bin/env python3
"""Decode the local PK message archives into UTF-8 readable text.

This is a research-only tool. It handles the LS11 wrapper used by the two
message archives and the inner indexed byte-string table observed in the
繁中 PK 1.0.1.0 samples. It intentionally does not depend on the product
code. The optional ``--simplified`` mode requires
``opencc-python-reimplemented`` and applies OpenCC ``t2s`` conversion only to
the rendered display text. ``--format bbcode`` additionally lowers confirmed
script semantics into standard color tags for explicit style controls, literal
newlines, runtime placeholders, and an aggregate unresolved-metadata report.
Object-formatting effects are not inserted into the template unless
``--bbcode-runtime-effects`` is requested.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import struct
from dataclasses import dataclass
from pathlib import Path


LS11_MAGIC = {b"LS11", b"Ls12"}
LS11_DICTIONARY_OFFSET = 16
LS11_DICTIONARY_SIZE = 256
LS11_DESCRIPTOR_OFFSET = LS11_DICTIONARY_OFFSET + LS11_DICTIONARY_SIZE
TERMINATOR = b"\x05\x05\x05"

# Runtime palette blocks reconstructed in RE-SAN9PK-0026. These are used only
# for the BBCode projection; the legacy renderer continues to expose the raw
# control bytes.
FONT_PALETTE_COLORS = {
    0: "#000000",
    1: "#98B0F8",
    2: "#F06850",
    3: "#D880F8",
    4: "#88F880",
    5: "#603000",
    6: "#F8F850",
    7: "#F0F0F0",
    8: "#404040",
    9: "#5828C8",
    10: "#B82000",
    11: "#800090",
    12: "#005028",
    13: "#D8D8D8",
    14: "#F8F8B0",
    15: "#A4A4A4",
    16: "#40F8B0",
    17: "#F0B080",
    18: "#00F098",
    19: "#402800",
    20: "#002878",
    21: "#983810",
    22: "#E0C850",
    23: "#004010",
    24: "#770000",
    25: "#5A9FD8",
    26: "#988058",
    27: "#A4D168",
    28: "#7EBAC2",
    29: "#C0E0E8",
    30: "#000000",
    31: "#000000",
}

OBJECT_SELECTOR_RANGES = (
    ("person", 0x01, 0x09),
    ("area", 0x0B, 0x13),
    ("item", 0x15, 0x1D),
    ("force", 0x1F, 0x27),
    ("building", 0x29, 0x31),
    ("unit", 0x33, 0x3B),
    ("building", 0x3D, 0x45),
    ("heihou", 0x47, 0x4F),
    ("fixed_building", 0x51, 0x59),
    ("strategy", 0x5B, 0x63),
    ("number", 0x65, 0x6D),
    ("string", 0x79, 0x81),
)

META_TOKEN_NAMES = {
    0x04: "condition.begin",
    0x05: "resource.switch",
    0x06: "condition.open",
    0x07: "condition.close",
    0x08: "condition.enabled",
    0x09: "condition.disabled",
}


@dataclass(frozen=True)
class ArchiveInfo:
    source: Path
    raw_size: int
    raw_sha256: str
    decoded_size: int
    block_count: int
    record_count: int
    table_end: int
    first_offset: int
    last_offset: int
    common_terminator_count: int
    nonstandard_terminator_count: int
    control_record_count: int
    decode_error_record_count: int
    extension_record_count: int
    empty_record_count: int


def read_bit(data: bytes, bit_position: int) -> int:
    byte = data[bit_position // 8]
    return (byte >> (7 - (bit_position % 8))) & 1


def decode_ls11_block(compressed: bytes, dictionary: bytes, expected_size: int) -> bytes:
    """Decode one LS11 LZ/dictionary bitstream block."""

    output = bytearray()
    bit_position = 0
    bit_count = len(compressed) * 8
    mask_length = 0
    copy_distance = 0

    while bit_position < bit_count:
        mask_length += 1
        first_bit = read_bit(compressed, bit_position)
        bit_position += 1
        if first_bit != 0:
            continue

        code_mask = (1 << mask_length) - 2
        code_value = 0
        for _ in range(mask_length):
            if bit_position >= bit_count:
                raise ValueError("LS11 code reaches the end of its bitstream")
            code_value = (code_value << 1) | read_bit(compressed, bit_position)
            bit_position += 1

        code = code_mask + code_value
        if copy_distance:
            copy_count = 3 + code
            if copy_distance > len(output):
                raise ValueError("LS11 copy distance points before decoded output")
            for _ in range(copy_count):
                output.append(output[-copy_distance])
            copy_distance = 0
        elif code < 256:
            output.append(dictionary[code])
        else:
            copy_distance = code - 256
        mask_length = 0

    if len(output) < expected_size:
        raise ValueError(
            f"LS11 block decoded to {len(output)} bytes, expected {expected_size}"
        )
    return bytes(output[:expected_size])


def decode_ls11(data: bytes) -> tuple[bytes, int]:
    if data[:4] not in LS11_MAGIC:
        raise ValueError(f"unsupported LS11 magic: {data[:4]!r}")
    if len(data) < LS11_DESCRIPTOR_OFFSET + 12:
        raise ValueError("LS11 file is shorter than its first block descriptor")

    dictionary = data[
        LS11_DICTIONARY_OFFSET : LS11_DICTIONARY_OFFSET + LS11_DICTIONARY_SIZE
    ]
    descriptor_position = LS11_DESCRIPTOR_OFFSET
    descriptors: list[tuple[int, int, int]] = []
    while True:
        if descriptor_position + 4 > len(data):
            raise ValueError("LS11 descriptor table has no terminator")
        if data[descriptor_position : descriptor_position + 4] == b"\0" * 4:
            break
        if descriptor_position + 12 > len(data):
            raise ValueError("LS11 descriptor is truncated")
        descriptors.append(struct.unpack_from(">III", data, descriptor_position))
        descriptor_position += 12

    decoded_blocks: list[bytes] = []
    for compressed_size, uncompressed_size, offset in descriptors:
        end = offset + compressed_size
        if end > len(data):
            raise ValueError("LS11 block exceeds the source file")
        compressed = data[offset:end]
        if compressed_size == uncompressed_size:
            decoded_blocks.append(compressed)
        else:
            decoded_blocks.append(
                decode_ls11_block(compressed, dictionary, uncompressed_size)
            )
    return b"".join(decoded_blocks), len(descriptors)


def split_indexed_records(decoded: bytes) -> tuple[int, int, list[bytes], list[int]]:
    """Read the u16 count/u32 offset table and return record byte slices."""

    if len(decoded) < 2:
        raise ValueError("decoded archive is shorter than its record count")
    record_count = struct.unpack_from("<H", decoded, 0)[0]
    table_end = 2 + 4 * record_count
    if table_end > len(decoded):
        raise ValueError("record offset table exceeds decoded archive")
    offsets = list(struct.unpack_from(f"<{record_count}I", decoded, 2))
    if not offsets:
        raise ValueError("record table is empty")
    if offsets[0] != table_end:
        raise ValueError(
            f"first record offset 0x{offsets[0]:X} does not equal table end 0x{table_end:X}"
        )
    if any(start > end for start, end in zip(offsets, offsets[1:])):
        raise ValueError("record offsets are not monotonic")
    if offsets[-1] > len(decoded):
        raise ValueError("last record offset exceeds decoded archive")

    records = [
        decoded[start : (offsets[index + 1] if index + 1 < record_count else len(decoded))]
        for index, start in enumerate(offsets)
    ]
    return record_count, table_end, records, offsets


def _append_segment(
    segments: list[dict[str, str]],
    kind: str,
    value: str | None = None,
    hex_value: str | None = None,
) -> None:
    if kind == "text" and not value:
        return
    if kind == "control" and not hex_value:
        return
    if segments and segments[-1]["kind"] == kind:
        if kind == "text":
            segments[-1]["value"] += value or ""
        else:
            segments[-1]["hex"] += " " + (hex_value or "")
        return
    if kind == "text":
        segments.append({"kind": "text", "value": value or ""})
    else:
        segments.append({"kind": "control", "hex": hex_value or ""})


def decode_big5_segments(data: bytes) -> tuple[list[dict[str, str]], bool, bool]:
    """Decode Big5-family text into ordered display/control segments.

    A few rare pairs in the local Taiwan sample are outside Python's strict
    Big5 and CP950 tables but are present in Big5-HKSCS. They are accepted as
    a display fallback and remain identifiable through the archive summary.
    Bytes below 0x20 (except line feed) and undecodable bytes are retained as
    control segments instead of being silently discarded.
    """

    segments: list[dict[str, str]] = []
    index = 0
    had_decode_error = False
    used_extension = False
    while index < len(data):
        value = data[index]
        if value == 0x0A:
            _append_segment(segments, "text", "\n")
            index += 1
            continue
        if 0x20 <= value <= 0x7E:
            _append_segment(segments, "text", chr(value))
            index += 1
            continue
        if value < 0x20 or value == 0x7F:
            _append_segment(segments, "control", hex_value=f"{value:02X}")
            index += 1
            continue
        if 0x81 <= value <= 0xFE and index + 1 < len(data):
            pair = data[index : index + 2]
            try:
                decoded = pair.decode("big5")
            except UnicodeDecodeError:
                try:
                    decoded = pair.decode("big5hkscs")
                except UnicodeDecodeError:
                    _append_segment(segments, "control", hex_value=f"{value:02X}")
                    had_decode_error = True
                    index += 1
                else:
                    used_extension = True
                    _append_segment(segments, "text", decoded)
                    index += 2
            else:
                _append_segment(segments, "text", decoded)
                index += 2
            continue
        _append_segment(segments, "control", hex_value=f"{value:02X}")
        if value >= 0x80:
            had_decode_error = True
        index += 1
    return segments, had_decode_error, used_extension


def display_text_from_segments(segments: list[dict[str, str]]) -> str:
    result: list[str] = []
    for segment in segments:
        if segment["kind"] == "text":
            result.append(segment["value"])
            continue
        result.extend(f"<0x{value}>" for value in segment["hex"].split())
    return "".join(result)


def render_big5(data: bytes) -> tuple[str, bool, bool]:
    segments, had_decode_error, used_extension = decode_big5_segments(data)
    return display_text_from_segments(segments), had_decode_error, used_extension


def _escape_bbcode_text(value: str) -> str:
    """Escape literal text that could otherwise be parsed as BBCode."""

    return value.replace("\\", "\\\\").replace("[", "\\[").replace("]", "\\]")


def _record_metadata(
    report: dict[str, object],
    *,
    record_index: int,
    record_offset: int,
    payload_position: int,
    kind: str,
    status: str,
    signature: str,
    raw: bytes,
    context: bytes,
) -> None:
    """Add one occurrence to a compact aggregate-plus-samples report."""

    categories = report["categories"]
    assert isinstance(categories, dict)
    key = f"{status}:{kind}:{signature}"
    category = categories.setdefault(
        key,
        {
            "kind": kind,
            "status": status,
            "signature": signature,
            "count": 0,
            "samples": [],
        },
    )
    assert isinstance(category, dict)
    category["count"] = int(category["count"]) + 1
    samples = category["samples"]
    assert isinstance(samples, list)
    if len(samples) < 10:
        samples.append(
            {
                "index": record_index,
                "decoded_offset": f"0x{record_offset + payload_position:X}",
                "hex": raw.hex(" "),
                "context": context.hex(" "),
            }
        )


def _new_bbcode_report(source: Path) -> dict[str, object]:
    return {
        "source": source.name,
        "format": "bbcode",
        "newline": "literal\\n",
        "categories": {},
    }


def _decode_big5_at(data: bytes, position: int) -> tuple[str, int, bool] | None:
    """Decode one visible Big5/ASCII unit for the BBCode projection."""

    value = data[position]
    if value == 0x0A:
        return "\n", 1, False
    if 0x20 <= value <= 0x7E:
        return chr(value), 1, False
    if 0x81 <= value <= 0xFE and position + 1 < len(data):
        pair = data[position : position + 2]
        try:
            return pair.decode("big5"), 2, False
        except UnicodeDecodeError:
            try:
                return pair.decode("big5hkscs"), 2, True
            except UnicodeDecodeError:
                return None
    return None


def _selector_info(selector: int) -> tuple[str, int] | None:
    for category, first, last in OBJECT_SELECTOR_RANGES:
        if first <= selector <= last:
            return category, selector - first
    return None


def _consume_expression(data: bytes, position: int) -> int:
    """Conservatively consume an expression's opaque operand bytes.

    The original evaluator consumes ASCII arithmetic/comparison tokens and
    nested 02 object values, then returns to visible Big5 text or a script
    meta-token. We do not evaluate the expression here; we only prevent its
    operand bytes from being emitted as visible text.
    """

    cursor = position
    while cursor < len(data):
        value = data[cursor]
        if value == 0x0A or data[cursor : cursor + 2] == b"\x05\x05":
            break
        if value >= 0x81:
            break
        if value == 0x02:
            if cursor + 1 >= len(data):
                cursor += 1
                break
            selector = data[cursor + 1]
            if selector == 0x8C:
                cursor += 2
                while cursor < len(data) and 0x30 <= data[cursor] <= 0x39:
                    cursor += 1
                if cursor < len(data):
                    cursor += 1
            elif _selector_info(selector) is not None and cursor + 2 < len(data):
                cursor += 3
            else:
                cursor += 2
            continue
        cursor += 1
    return cursor


def _style_transition(
    style: int | None,
    number: int,
    suffix: int,
    *,
    report: dict[str, object],
    record_index: int,
    record_offset: int,
    payload_position: int,
    raw: bytes,
    context: bytes,
) -> tuple[str, int | None]:
    """Translate a recognized b-style escape into standard BBCode."""

    if suffix != ord("b"):
        suffix_text = chr(suffix) if 0x20 <= suffix <= 0x7E else f"0x{suffix:02X}"
        _record_metadata(
            report,
            record_index=record_index,
            record_offset=record_offset,
            payload_position=payload_position,
            kind="style",
            status="unresolved",
            signature=f"{number}{suffix_text}",
            raw=raw,
            context=context,
        )
        return f"{{{{meta.style:{raw.hex(' ')}}}}}", style

    output = "[/color]" if style is not None else ""
    if number >= 32:
        return output, None
    color = FONT_PALETTE_COLORS.get(number)
    if color is None:
        _record_metadata(
            report,
            record_index=record_index,
            record_offset=record_offset,
            payload_position=payload_position,
            kind="style",
            status="unknown",
            signature=f"{number}b",
            raw=raw,
            context=context,
        )
        return output + f"{{{{raw:{raw.hex(' ')}}}}}", None
    _record_metadata(
        report,
        record_index=record_index,
        record_offset=record_offset,
        payload_position=payload_position,
        kind="style-effect",
        status="lossy",
        signature=f"{number}b:non-color-effects",
        raw=raw,
        context=context,
    )
    return output + f"[color={color}]", number


def _bbcode_placeholder(
    data: bytes,
    position: int,
    end: int,
    *,
    record_index: int,
    record_offset: int,
    report: dict[str, object],
    kind: str,
    status: str,
    signature: str,
    value: str,
) -> str:
    raw = data[position:end]
    context = data[max(0, position - 8) : min(len(data), end + 16)]
    _record_metadata(
        report,
        record_index=record_index,
        record_offset=record_offset,
        payload_position=position,
        kind=kind,
        status=status,
        signature=signature,
        raw=raw,
        context=context,
    )
    return value


def build_bbcode_record(
    data: bytes,
    *,
    record_index: int,
    record_offset: int,
    converter: object | None,
    report: dict[str, object],
    include_runtime_effects: bool = False,
) -> str:
    """Render one decoded record as BBCode while preserving unresolved script."""

    output: list[str] = []
    position = 0
    style: int | None = None
    while position < len(data):
        value = data[position]

        if value == 0x02:
            if position + 1 >= len(data):
                output.append(
                    _bbcode_placeholder(
                        data,
                        position,
                        position + 1,
                        record_index=record_index,
                        record_offset=record_offset,
                        report=report,
                        kind="control",
                        status="unknown",
                        signature="02",
                        value="{{raw:02}}",
                    )
                )
                position += 1
                continue

            selector = data[position + 1]
            if selector == 0x8C:
                cursor = position + 2
                while cursor < len(data) and 0x30 <= data[cursor] <= 0x39:
                    cursor += 1
                if cursor == position + 2 or cursor >= len(data):
                    end = min(len(data), position + 2)
                    output.append(
                        _bbcode_placeholder(
                            data,
                            position,
                            end,
                            record_index=record_index,
                            record_offset=record_offset,
                            report=report,
                            kind="style",
                            status="unknown",
                            signature="02 8C",
                            value=f"{{{{raw:{data[position:end].hex(' ')}}}}}",
                        )
                    )
                    position = end
                    continue
                suffix = data[cursor]
                end = cursor + 1
                number = int(data[position + 2 : cursor].decode("ascii"))
                raw = data[position:end]
                context = data[max(0, position - 8) : min(len(data), end + 16)]
                transition, style = _style_transition(
                    style,
                    number,
                    suffix,
                    report=report,
                    record_index=record_index,
                    record_offset=record_offset,
                    payload_position=position,
                    raw=raw,
                    context=context,
                )
                output.append(transition)
                position = end
                continue

            selector_info = _selector_info(selector)
            if selector_info is not None:
                category, slot = selector_info
                if position + 2 >= len(data):
                    end = min(len(data), position + 2)
                    output.append(
                        _bbcode_placeholder(
                            data,
                            position,
                            end,
                            record_index=record_index,
                            record_offset=record_offset,
                            report=report,
                            kind="object",
                            status="unknown",
                            signature=f"selector=0x{selector:02X}",
                            value=f"{{{{raw:{data[position:end].hex(' ')}}}}}",
                        )
                    )
                    position = end
                    continue
                property_byte = data[position + 2]
                end = position + 3
                if category == "person" and property_byte == 0xDA:
                    placeholder = f"{{{{person[{slot}].name}}}}"
                    output.append(
                        f"[color=#98B0F8]{placeholder}[/color]"
                        if include_runtime_effects
                        else placeholder
                    )
                elif category == "unit" and property_byte == 0x79:
                    placeholder = f"{{{{unit[{slot}].related_location}}}}"
                    output.append(
                        f"[color=#88F880]{placeholder}[/color]"
                        if include_runtime_effects
                        else placeholder
                    )
                elif category == "number":
                    output.append(
                        _bbcode_placeholder(
                            data,
                            position,
                            end,
                            record_index=record_index,
                            record_offset=record_offset,
                            report=report,
                            kind="object",
                            status="unresolved",
                            signature=f"{category}[{slot}]",
                            value=f"{{{{number[{slot}]}}}}",
                        )
                    )
                elif category == "string":
                    output.append(
                        _bbcode_placeholder(
                            data,
                            position,
                            end,
                            record_index=record_index,
                            record_offset=record_offset,
                            report=report,
                            kind="object",
                            status="unresolved",
                            signature=f"{category}[{slot}]",
                            value=f"{{{{string[{slot}]}}}}",
                        )
                    )
                else:
                    output.append(
                        _bbcode_placeholder(
                            data,
                            position,
                            end,
                            record_index=record_index,
                            record_offset=record_offset,
                            report=report,
                            kind="object",
                            status="unresolved",
                            signature=f"{category}[{slot}].property=0x{property_byte:02X}",
                            value=(
                                f"{{{{object.{category}[{slot}].property[0x{property_byte:02X}]}}}}"
                            ),
                        )
                    )
                position = end
                continue

            if 0xC9 <= selector <= 0xDF:
                end = _consume_expression(data, position + 2)
                if end <= position + 2:
                    end = min(len(data), position + 2)
                raw = data[position:end]
                output.append(
                    _bbcode_placeholder(
                        data,
                        position,
                        end,
                        record_index=record_index,
                        record_offset=record_offset,
                        report=report,
                        kind="expression",
                        status="unresolved",
                        signature=f"selector=0x{selector:02X}",
                        value=f"{{{{meta.expression:{raw.hex(' ')}}}}}",
                    )
                )
                position = end
                continue

            end = position + 2
            output.append(
                _bbcode_placeholder(
                    data,
                    position,
                    end,
                    record_index=record_index,
                    record_offset=record_offset,
                    report=report,
                    kind="control",
                    status="unknown",
                    signature=f"selector=0x{selector:02X}",
                    value=f"{{{{raw:{data[position:end].hex(' ')}}}}}",
                )
            )
            position = end
            continue

        if value == 0x01:
            if position + 1 >= len(data):
                end = position + 1
            else:
                command = data[position + 1]
                if command in (ord("J"), ord("C")):
                    end = min(len(data), position + 4)
                elif command in (ord("S"), ord("R")):
                    end = _consume_expression(data, position + 2)
                else:
                    end = min(len(data), position + 2)
            raw = data[position:end]
            command_text = chr(data[position + 1]) if position + 1 < end else "?"
            output.append(
                _bbcode_placeholder(
                    data,
                    position,
                    end,
                    record_index=record_index,
                    record_offset=record_offset,
                    report=report,
                    kind="macro",
                    status="unresolved",
                    signature=f"01{command_text}",
                    value=f"{{{{meta.macro:{raw.hex(' ')}}}}}",
                )
            )
            position = end
            continue

        if value == 0x05 and data[position : position + 2] == b"\x05\x05":
            if position + 2 >= len(data):
                end = len(data)
                token = "truncated"
            else:
                token_value = data[position + 2]
                end = min(len(data), position + 3)
                token = META_TOKEN_NAMES.get(token_value, f"0x{token_value:02X}")
                if token_value == 0x04:
                    expression_end = _consume_expression(data, end)
                    if expression_end > end:
                        end = expression_end
            raw = data[position:end]
            output.append(
                _bbcode_placeholder(
                    data,
                    position,
                    end,
                    record_index=record_index,
                    record_offset=record_offset,
                    report=report,
                    kind="meta",
                    status="unresolved",
                    signature=token,
                    value=f"{{{{meta.token:{raw.hex(' ')}}}}}",
                )
            )
            position = end
            continue

        if value in (0x1A, 0x1B):
            end = min(len(data), position + 2)
            command = chr(data[position + 1]) if position + 1 < end else "?"
            raw = data[position:end]
            output.append(
                _bbcode_placeholder(
                    data,
                    position,
                    end,
                    record_index=record_index,
                    record_offset=record_offset,
                    report=report,
                    kind="layout-control",
                    status="unresolved",
                    signature=f"0x{value:02X}{command}",
                    value=f"{{{{meta.layout:{raw.hex(' ')}}}}}",
                )
            )
            position = end
            continue

        if value == 0x0A:
            output.append("\n")
            position += 1
            continue

        decoded = _decode_big5_at(data, position)
        if decoded is not None:
            text, length, used_extension = decoded
            if converter is not None:
                text = converter.convert(text)
            output.append(_escape_bbcode_text(text))
            if used_extension:
                raw = data[position : position + length]
                _record_metadata(
                    report,
                    record_index=record_index,
                    record_offset=record_offset,
                    payload_position=position,
                    kind="encoding",
                    status="recognized-extension",
                    signature="Big5-HKSCS",
                    raw=raw,
                    context=data[max(0, position - 8) : min(len(data), position + 16)],
                )
            position += length
            continue

        end = position + 1
        raw = data[position:end]
        output.append(
            _bbcode_placeholder(
                data,
                position,
                end,
                record_index=record_index,
                record_offset=record_offset,
                report=report,
                kind="encoding",
                status="unknown",
                signature=f"0x{value:02X}",
                value=f"{{{{raw:{raw.hex(' ')}}}}}",
            )
        )
        position = end

    if style is not None:
        output.append("[/color]")
    return "".join(output)


def strip_common_terminator(record: bytes) -> tuple[bytes, bool]:
    if record.endswith(TERMINATOR):
        return record[: -len(TERMINATOR)], True
    return record, False


def format_archive(
    source: Path,
    decoded: bytes,
    block_count: int,
    simplified: bool,
) -> tuple[str, ArchiveInfo]:
    record_count, table_end, records, offsets = split_indexed_records(decoded)
    common_terminator_count = sum(record.endswith(TERMINATOR) for record in records)
    nonstandard_terminator_count = record_count - common_terminator_count
    control_record_count = sum(
        any(value < 0x20 and value != 0x0A for value in strip_common_terminator(record)[0])
        for record in records
    )

    rendered_records: list[str] = []
    decode_error_record_count = 0
    extension_record_count = 0
    empty_record_count = 0
    converter = None
    if simplified:
        from opencc import OpenCC

        converter = OpenCC("t2s")
    for index, (offset, record) in enumerate(zip(offsets, records)):
        body, has_common_terminator = strip_common_terminator(record)
        text, had_decode_error, used_extension = render_big5(body)
        if converter is not None:
            text = converter.convert(text)
        decode_error_record_count += had_decode_error
        extension_record_count += used_extension
        empty_record_count += not body
        trailer = TERMINATOR.hex(" ") if has_common_terminator else "none"
        rendered_records.append(
            f"[{index:05d}] offset=0x{offset:08X} raw_length={len(record)} "
            f"payload_length={len(body)} trailer={trailer}\n"
            f"{text if text else '(empty)'}\n"
            "---"
        )

    info = ArchiveInfo(
        source=source,
        raw_size=source.stat().st_size,
        raw_sha256=hashlib.sha256(source.read_bytes()).hexdigest(),
        decoded_size=len(decoded),
        block_count=block_count,
        record_count=record_count,
        table_end=table_end,
        first_offset=offsets[0],
        last_offset=offsets[-1],
        common_terminator_count=common_terminator_count,
        nonstandard_terminator_count=nonstandard_terminator_count,
        control_record_count=control_record_count,
        decode_error_record_count=decode_error_record_count,
        extension_record_count=extension_record_count,
        empty_record_count=empty_record_count,
    )
    header = "\n".join(
        [
            f"# {source.name} readable conversion",
            "# output_encoding: UTF-8",
            f"# display_language: {'Simplified Chinese (OpenCC t2s)' if simplified else 'Traditional Chinese'}",
            "# text_encoding: Big5; Big5-HKSCS fallback is used for rare extension pairs",
            "# controls and still-undecodable bytes are rendered as <0xNN>",
            "# layout: LS11 wrapper -> decoded indexed table -> u16le count + u32le offsets",
            f"# source: {source.as_posix()}",
            f"# raw_size: {info.raw_size}",
            f"# raw_sha256: {info.raw_sha256}",
            f"# decoded_size: {info.decoded_size}",
            f"# ls11_block_count: {info.block_count}",
            f"# record_count: {info.record_count}",
            f"# offset_table_end: 0x{info.table_end:X}",
            f"# first_offset: 0x{info.first_offset:X}",
            f"# last_offset: 0x{info.last_offset:X}",
            f"# common_trailer_05x3: {info.common_terminator_count}/{info.record_count}",
            f"# nonstandard_trailer: {info.nonstandard_terminator_count}/{info.record_count}",
            f"# records_with_controls: {info.control_record_count}/{info.record_count}",
            f"# records_with_decode_errors: {info.decode_error_record_count}/{info.record_count}",
            f"# records_using_big5hkscs_fallback: {info.extension_record_count}/{info.record_count}",
            f"# empty_payloads: {info.empty_record_count}/{info.record_count}",
            "",
        ]
    )
    return header + "\n".join(rendered_records) + "\n", info


def _catalog_identity(source: Path) -> tuple[str, str, str]:
    stem = source.stem.lower()
    if stem == "m_msgpk":
        return "messages", "msg", "messages.json"
    if stem == "m_rtdnpk":
        return "biographies", "rtdn", "biographies.json"
    return stem, stem, f"{source.stem}.json"


def _metadata_identity(source: Path) -> str:
    stem = source.stem.lower()
    if stem == "m_msgpk":
        return "messages.metadata.json"
    if stem == "m_rtdnpk":
        return "biographies.metadata.json"
    return f"{source.stem}.metadata.json"


def build_json_archive(
    source: Path,
    decoded: bytes,
    block_count: int,
    simplified: bool,
) -> tuple[list[dict[str, object]], ArchiveInfo]:
    """Build a minimal engine-ready list without losing source order."""

    record_count, table_end, records, offsets = split_indexed_records(decoded)
    common_terminator_count = sum(record.endswith(TERMINATOR) for record in records)
    nonstandard_terminator_count = record_count - common_terminator_count
    control_record_count = sum(
        any(value < 0x20 and value != 0x0A for value in strip_common_terminator(record)[0])
        for record in records
    )
    converter = None
    if simplified:
        from opencc import OpenCC

        converter = OpenCC("t2s")

    entries: list[dict[str, object]] = []
    decode_error_record_count = 0
    extension_record_count = 0
    empty_record_count = 0
    for index, (offset, record) in enumerate(zip(offsets, records)):
        body, has_common_terminator = strip_common_terminator(record)
        segments, had_decode_error, used_extension = decode_big5_segments(body)
        if converter is not None:
            for segment in segments:
                if segment["kind"] == "text":
                    segment["value"] = converter.convert(segment["value"])
        display_text = display_text_from_segments(segments)
        decode_error_record_count += had_decode_error
        extension_record_count += used_extension
        empty_record_count += not body
        entries.append({"index": index, "content": display_text})

    info = ArchiveInfo(
        source=source,
        raw_size=source.stat().st_size,
        raw_sha256=hashlib.sha256(source.read_bytes()).hexdigest(),
        decoded_size=len(decoded),
        block_count=block_count,
        record_count=record_count,
        table_end=table_end,
        first_offset=offsets[0],
        last_offset=offsets[-1],
        common_terminator_count=common_terminator_count,
        nonstandard_terminator_count=nonstandard_terminator_count,
        control_record_count=control_record_count,
        decode_error_record_count=decode_error_record_count,
        extension_record_count=extension_record_count,
        empty_record_count=empty_record_count,
    )
    return entries, info


def build_bbcode_archive(
    source: Path,
    decoded: bytes,
    block_count: int,
    simplified: bool,
    include_runtime_effects: bool = False,
) -> tuple[list[dict[str, object]], ArchiveInfo, dict[str, object]]:
    """Build a BBCode catalog and an aggregate unresolved-metadata report."""

    record_count, table_end, records, offsets = split_indexed_records(decoded)
    common_terminator_count = sum(record.endswith(TERMINATOR) for record in records)
    nonstandard_terminator_count = record_count - common_terminator_count
    control_record_count = sum(
        any(value < 0x20 and value != 0x0A for value in strip_common_terminator(record)[0])
        for record in records
    )
    converter = None
    if simplified:
        from opencc import OpenCC

        converter = OpenCC("t2s")

    entries: list[dict[str, object]] = []
    report = _new_bbcode_report(source)
    decode_error_record_count = 0
    extension_record_count = 0
    empty_record_count = 0
    for index, (offset, record) in enumerate(zip(offsets, records)):
        body, has_common_terminator = strip_common_terminator(record)
        content = build_bbcode_record(
            body,
            record_index=index,
            record_offset=offset,
            converter=converter,
            report=report,
            include_runtime_effects=include_runtime_effects,
        )
        entries.append({"index": index, "content": content})
        _, had_decode_error, used_extension = render_big5(body)
        decode_error_record_count += had_decode_error
        extension_record_count += used_extension
        empty_record_count += not body

    report["record_count"] = record_count
    report["common_terminator_count"] = common_terminator_count
    report["nonstandard_terminator_count"] = nonstandard_terminator_count
    report["category_count"] = len(report["categories"])
    report["occurrence_count"] = sum(
        int(category["count"])
        for category in report["categories"].values()
    )
    report["categories"] = sorted(
        report["categories"].values(),
        key=lambda category: (category["status"], category["kind"], category["signature"]),
    )

    info = ArchiveInfo(
        source=source,
        raw_size=source.stat().st_size,
        raw_sha256=hashlib.sha256(source.read_bytes()).hexdigest(),
        decoded_size=len(decoded),
        block_count=block_count,
        record_count=record_count,
        table_end=table_end,
        first_offset=offsets[0],
        last_offset=offsets[-1],
        common_terminator_count=common_terminator_count,
        nonstandard_terminator_count=nonstandard_terminator_count,
        control_record_count=control_record_count,
        decode_error_record_count=decode_error_record_count,
        extension_record_count=extension_record_count,
        empty_record_count=empty_record_count,
    )
    return entries, info, report


def write_metadata_report(path: Path, report: dict[str, object]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def convert_json(
    source: Path,
    output: Path,
    simplified: bool = False,
    output_format: str = "legacy",
    metadata_report: Path | None = None,
    include_runtime_effects: bool = False,
) -> ArchiveInfo:
    raw = source.read_bytes()
    decoded, block_count = decode_ls11(raw)
    if output_format == "bbcode":
        entries, info, report = build_bbcode_archive(
            source,
            decoded,
            block_count,
            simplified,
            include_runtime_effects=include_runtime_effects,
        )
    else:
        entries, info = build_json_archive(source, decoded, block_count, simplified)
        report = None
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(
        json.dumps(entries, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    if report is not None and metadata_report is not None:
        write_metadata_report(metadata_report, report)
    return info


def convert(
    source: Path,
    output: Path,
    simplified: bool = False,
    output_format: str = "legacy",
    metadata_report: Path | None = None,
    include_runtime_effects: bool = False,
) -> ArchiveInfo:
    raw = source.read_bytes()
    decoded, block_count = decode_ls11(raw)
    if output_format == "bbcode":
        entries, info, report = build_bbcode_archive(
            source,
            decoded,
            block_count,
            simplified,
            include_runtime_effects=include_runtime_effects,
        )
        rendered_records = []
        for entry in entries:
            rendered_records.append(
                f"[{int(entry['index']):05d}]\n{entry['content']}\n---"
            )
        text = "\n".join(rendered_records) + "\n"
    else:
        text, info = format_archive(source, decoded, block_count, simplified)
        report = None
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(text, encoding="utf-8", newline="\n")
    if report is not None and metadata_report is not None:
        write_metadata_report(metadata_report, report)
    return info


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "sources",
        nargs="+",
        type=Path,
        help="PK message archives to convert",
    )
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=Path("artifacts/text"),
        help="directory for UTF-8 readable files (default: artifacts/text)",
    )
    parser.add_argument(
        "--simplified",
        action="store_true",
        help="convert rendered Traditional Chinese text to Simplified Chinese with OpenCC t2s",
    )
    parser.add_argument(
        "--format",
        choices=("legacy", "bbcode"),
        default="legacy",
        help="output projection; legacy preserves raw controls, bbcode emits source-faithful semantic BBCode",
    )
    parser.add_argument(
        "--bbcode-runtime-effects",
        action="store_true",
        help="in bbcode mode, also wrap confirmed object values with their original runtime colors",
    )
    parser.add_argument(
        "--json-output-dir",
        type=Path,
        default=None,
        help="also write engine-ready messages.json/biographies.json-style catalogs to this directory",
    )
    parser.add_argument(
        "--metadata-report-dir",
        type=Path,
        default=None,
        help="write aggregate unresolved-control reports; defaults to the selected output directory in bbcode mode",
    )
    args = parser.parse_args()

    report_dir = args.metadata_report_dir
    if args.format == "bbcode" and report_dir is None:
        report_dir = args.json_output_dir or args.output_dir

    for source in args.sources:
        if args.format == "bbcode":
            suffix = ".bbcode.zh-CN.txt" if args.simplified else ".bbcode.txt"
        else:
            suffix = ".readable.zh-CN.txt" if args.simplified else ".readable.txt"
        output = args.output_dir / f"{source.stem}{suffix}"
        report_path = (
            report_dir / _metadata_identity(source) if report_dir is not None else None
        )
        info = convert(
            source,
            output,
            simplified=args.simplified,
            output_format=args.format,
            metadata_report=report_path if args.json_output_dir is None else None,
            include_runtime_effects=args.bbcode_runtime_effects,
        )
        print(
            f"{source} -> {output}: raw={info.raw_size}, decoded={info.decoded_size}, "
            f"records={info.record_count}, common_05x3={info.common_terminator_count}"
        )
        if args.json_output_dir is not None:
            _, _, json_name = _catalog_identity(source)
            json_output = args.json_output_dir / json_name
            json_info = convert_json(
                source,
                json_output,
                simplified=args.simplified,
                output_format=args.format,
                metadata_report=report_path,
                include_runtime_effects=args.bbcode_runtime_effects,
            )
            print(
                f"{source} -> {json_output}: records={json_info.record_count}, "
                f"language={'zh-CN' if args.simplified else 'zh-TW'}, format={args.format}"
            )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

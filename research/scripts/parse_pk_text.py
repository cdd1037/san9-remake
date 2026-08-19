#!/usr/bin/env python3
"""Decode the local PK message archives into UTF-8 readable text.

This is a research-only tool. It handles the LS11 wrapper used by the two
message archives and the inner indexed byte-string table observed in the
繁中 PK 1.0.1.0 samples. It intentionally does not depend on the product
code. The optional ``--simplified`` mode requires
``opencc-python-reimplemented`` and applies OpenCC ``t2s`` conversion only to
the rendered display text.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import struct
from dataclasses import dataclass
from pathlib import Path

from legacy_text import (
    convert_text_segments,
    decode_big5_segments,
    display_text_from_segments,
    render_big5,
)


LS11_MAGIC = {b"LS11", b"Ls12"}
LS11_DICTIONARY_OFFSET = 16
LS11_DICTIONARY_SIZE = 256
LS11_DESCRIPTOR_OFFSET = LS11_DICTIONARY_OFFSET + LS11_DICTIONARY_SIZE
TERMINATOR = b"\x05\x05\x05"

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


def build_research_catalog(
    source: Path,
    simplified: bool = False,
) -> tuple[dict[str, object], list[dict[str, object]], dict[str, int]]:
    """Build the byte-faithful archive projection shared with EXE exports."""

    raw = source.read_bytes()
    decoded, block_count = decode_ls11(raw)
    record_count, table_end, records, offsets = split_indexed_records(decoded)
    catalog_name, source_kind, _ = _catalog_identity(source)
    source_id = f"{source.name}:{hashlib.sha256(raw).hexdigest()[:12]}"
    converter = None
    if simplified:
        from opencc import OpenCC

        converter = OpenCC("t2s")

    entries: list[dict[str, object]] = []
    diagnostics = {"decode_error_records": 0, "extension_records": 0}
    for index, (offset, record) in enumerate(zip(offsets, records)):
        body, has_common_terminator = strip_common_terminator(record)
        segments, had_decode_error, used_extension = decode_big5_segments(body)
        traditional = display_text_from_segments(segments)
        simplified_text = (
            display_text_from_segments(convert_text_segments(segments, converter.convert))
            if converter is not None
            else None
        )
        diagnostics["decode_error_records"] += int(had_decode_error)
        diagnostics["extension_records"] += int(used_extension)
        entry: dict[str, object] = {
            "source_id": source_id,
            "source_kind": source_kind,
            "locator": f"{source.name}#record:{index}",
            "record_index": index,
            "decoded_offset": f"0x{offset:X}",
            "raw_hex": body.hex(" ").upper(),
            "segments": segments,
            "traditional_text": traditional,
            "encoding": "big5-hkscs" if used_extension else "big5",
            "decode_status": "error" if had_decode_error else "exact",
            "terminator": TERMINATOR.hex(" ").upper() if has_common_terminator else None,
        }
        if simplified_text is not None:
            entry["simplified_text"] = simplified_text
        entries.append(entry)

    source_row: dict[str, object] = {
        "source_id": source_id,
        "source_kind": source_kind,
        "catalog_name": catalog_name,
        "file_name": source.name,
        "sha256": hashlib.sha256(raw).hexdigest(),
        "raw_size": len(raw),
        "decoded_size": len(decoded),
        "block_count": block_count,
        "record_count": record_count,
        "offset_table_end": f"0x{table_end:X}",
    }
    return source_row, entries, diagnostics


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


def convert_json(
    source: Path,
    output: Path,
    simplified: bool = False,
) -> ArchiveInfo:
    raw = source.read_bytes()
    decoded, block_count = decode_ls11(raw)
    entries, info = build_json_archive(source, decoded, block_count, simplified)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(
        json.dumps(entries, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    return info


def convert(
    source: Path,
    output: Path,
    simplified: bool = False,
) -> ArchiveInfo:
    raw = source.read_bytes()
    decoded, block_count = decode_ls11(raw)
    text, info = format_archive(source, decoded, block_count, simplified)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(text, encoding="utf-8", newline="\n")
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
        "--json-output-dir",
        type=Path,
        default=None,
        help="also write engine-ready messages.json/biographies.json-style catalogs to this directory",
    )
    parser.add_argument(
        "--research-catalog-output-dir",
        type=Path,
        default=None,
        help="write one byte-faithful research catalog per input archive",
    )
    args = parser.parse_args()

    for source in args.sources:
        suffix = ".readable.zh-CN.txt" if args.simplified else ".readable.txt"
        output = args.output_dir / f"{source.stem}{suffix}"
        info = convert(
            source,
            output,
            simplified=args.simplified,
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
            )
            print(
                f"{source} -> {json_output}: records={json_info.record_count}, "
                f"language={'zh-CN' if args.simplified else 'zh-TW'}"
            )
        if args.research_catalog_output_dir is not None:
            source_row, entries, diagnostics = build_research_catalog(
                source,
                simplified=args.simplified,
            )
            catalog = {
                "schema_version": 1,
                "authority_language": "zh-TW",
                "derived_language": "zh-CN" if args.simplified else None,
                "sources": [source_row],
                "records": entries,
                "diagnostics": diagnostics,
            }
            research_output = (
                args.research_catalog_output_dir / f"{source.stem}.research.json"
            )
            research_output.parent.mkdir(parents=True, exist_ok=True)
            research_output.write_text(
                json.dumps(catalog, ensure_ascii=False, indent=2) + "\n",
                encoding="utf-8",
            )
            print(
                f"{source} -> {research_output}: research_records={len(entries)}"
            )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

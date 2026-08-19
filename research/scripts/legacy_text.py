"""Shared byte-faithful decoding helpers for legacy San9 text.

The module deliberately has no IDA or product-code dependencies so the same
decoder can be used by archive parsers, IDA exporters, and unit tests.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Callable


TextSegment = dict[str, str]


@dataclass(frozen=True)
class CStringCandidate:
    offset: int
    raw: bytes
    segments: list[TextSegment]
    text: str
    used_extension: bool


def _append_segment(
    segments: list[TextSegment],
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


def decode_big5_segments(
    data: bytes,
    *,
    allow_hkscs: bool = True,
    primary_encoding: str = "big5",
) -> tuple[list[TextSegment], bool, bool]:
    """Decode ASCII/Big5 while retaining controls and undecodable bytes.

    Returns ``(segments, had_decode_error, used_extension)``. Big5-HKSCS is
    opt-in policy: resource archives use it for known extension pairs, whereas
    executable string discovery uses strict Big5 to avoid binary false hits.
    """

    segments: list[TextSegment] = []
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
                decoded = pair.decode(primary_encoding)
            except UnicodeDecodeError:
                if allow_hkscs:
                    try:
                        decoded = pair.decode("big5hkscs")
                    except UnicodeDecodeError:
                        decoded = None
                    else:
                        used_extension = True
                else:
                    decoded = None
                if decoded is None:
                    _append_segment(segments, "control", hex_value=f"{value:02X}")
                    had_decode_error = True
                    index += 1
                else:
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


def display_text_from_segments(segments: list[TextSegment]) -> str:
    result: list[str] = []
    for segment in segments:
        if segment["kind"] == "text":
            result.append(segment["value"])
        else:
            result.extend(f"<0x{value}>" for value in segment["hex"].split())
    return "".join(result)


def render_big5(
    data: bytes,
    *,
    allow_hkscs: bool = True,
    primary_encoding: str = "big5",
) -> tuple[str, bool, bool]:
    segments, had_decode_error, used_extension = decode_big5_segments(
        data,
        allow_hkscs=allow_hkscs,
        primary_encoding=primary_encoding,
    )
    return display_text_from_segments(segments), had_decode_error, used_extension


def convert_text_segments(
    segments: list[TextSegment],
    converter: Callable[[str], str],
) -> list[TextSegment]:
    """Return a converted copy without changing controls or the source list."""

    converted: list[TextSegment] = []
    for segment in segments:
        if segment["kind"] == "text":
            converted.append({"kind": "text", "value": converter(segment["value"])})
        else:
            converted.append(dict(segment))
    return converted


def contains_big5_pair(data: bytes, *, primary_encoding: str = "big5") -> bool:
    index = 0
    while index + 1 < len(data):
        if data[index] >= 0x80:
            try:
                data[index : index + 2].decode(primary_encoding)
            except UnicodeDecodeError:
                return False
            return True
        index += 1
    return False


def scan_null_terminated_big5(
    data: bytes,
    *,
    min_raw_length: int = 2,
    max_raw_length: int = 512,
    primary_encoding: str = "big5",
) -> list[CStringCandidate]:
    """Find strict Big5 C-string candidates in a byte region.

    This deliberately emits only structurally valid candidates. Callers add
    source-specific confidence signals such as segment membership and xrefs.
    """

    candidates: list[CStringCandidate] = []
    position = 0
    while position < len(data):
        end = data.find(b"\0", position)
        if end < 0:
            break
        raw = data[position:end]
        if min_raw_length <= len(raw) <= max_raw_length and contains_big5_pair(
            raw,
            primary_encoding=primary_encoding,
        ):
            segments, had_error, used_extension = decode_big5_segments(
                raw,
                allow_hkscs=False,
                primary_encoding=primary_encoding,
            )
            text = display_text_from_segments(segments)
            has_controls = any(segment["kind"] == "control" for segment in segments)
            if not had_error and not has_controls and all(
                character.isprintable() or character in "\r\n\t" for character in text
            ):
                candidates.append(
                    CStringCandidate(position, raw, segments, text, used_extension)
                )
        position = end + 1
    return candidates

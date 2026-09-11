"""Strict CP950 C-string candidates for IDA research, not a message converter."""
from dataclasses import dataclass


@dataclass(frozen=True)
class CStringCandidate:
    offset: int
    raw: bytes
    segments: list[dict[str, str]]
    text: str
    used_extension: bool = False


def scan_null_terminated_big5(data: bytes, *, min_raw_length: int = 2,
                             max_raw_length: int = 512,
                             primary_encoding: str = "cp950") -> list[CStringCandidate]:
    candidates = []
    position = 0
    while position < len(data):
        end = data.find(b"\0", position)
        if end < 0:
            break
        raw = data[position:end]
        if min_raw_length <= len(raw) <= max_raw_length and any(b >= 128 for b in raw):
            try:
                text = raw.decode(primary_encoding, errors="strict")
            except UnicodeDecodeError:
                pass
            else:
                if all(c.isprintable() or c in "\r\n\t" for c in text):
                    candidates.append(CStringCandidate(position, raw, [{"kind": "text", "value": text}], text))
        position = end + 1
    return candidates

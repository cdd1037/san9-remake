"""Verify declared San9PKHard old bytes against an exact PE image.

The verifier never loads or executes the target and never writes the image or
the IDA database.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import struct
from pathlib import Path


def pe_image_layout(data: bytes) -> tuple[int, list[tuple[int, int, int, int]]]:
    if data[:2] != b"MZ":
        raise ValueError("not a PE image")
    pe_offset = struct.unpack_from("<I", data, 0x3C)[0]
    if data[pe_offset : pe_offset + 4] != b"PE\0\0":
        raise ValueError("invalid PE signature")
    coff = pe_offset + 4
    section_count = struct.unpack_from("<H", data, coff + 2)[0]
    optional_size = struct.unpack_from("<H", data, coff + 16)[0]
    optional = coff + 20
    if struct.unpack_from("<H", data, optional)[0] != 0x10B:
        raise ValueError("only PE32 images are supported")
    image_base = struct.unpack_from("<I", data, optional + 28)[0]
    section_table = optional + optional_size
    sections = []
    for index in range(section_count):
        entry = section_table + index * 40
        virtual_size, virtual_address, raw_size, raw_offset = struct.unpack_from(
            "<IIII", data, entry + 8
        )
        sections.append((virtual_address, max(virtual_size, raw_size), raw_offset, raw_size))
    return image_base, sections


def virtual_to_file_offset(address: int, image_base: int, sections: list[tuple[int, int, int, int]]) -> int:
    rva = address - image_base
    for virtual_address, mapped_size, raw_offset, raw_size in sections:
        if virtual_address <= rva < virtual_address + mapped_size:
            delta = rva - virtual_address
            if delta >= raw_size:
                raise ValueError(f"address 0x{address:X} has no file-backed bytes")
            return raw_offset + delta
    raise ValueError(f"address 0x{address:X} is outside file-backed sections")


def verify(image_path: Path, manifest_path: Path) -> dict[str, object]:
    data = image_path.read_bytes()
    image_base, sections = pe_image_layout(data)
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    points = []
    for point in manifest["points"]:
        address = int(str(point["address"]), 0)
        expected = bytes.fromhex(point["expected_old_hex"])
        offset = virtual_to_file_offset(address, image_base, sections)
        actual = data[offset : offset + len(expected)]
        points.append({
            **point,
            "address": f"0x{address:X}",
            "actual_hex": actual.hex(" ").upper(),
            "status": "direct-match" if actual == expected else "conflict",
        })
    counts = {status: sum(point["status"] == status for point in points) for status in ("direct-match", "conflict")}
    return {
        "image": {
            "path": str(image_path),
            "sha256": hashlib.sha256(data).hexdigest().upper(),
            "image_base": f"0x{image_base:X}",
        },
        "manifest": str(manifest_path),
        "counts": counts,
        "points": points,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--image", type=Path, required=True)
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    report = verify(args.image, args.manifest)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(report["counts"], ensure_ascii=False))
    return 1 if report["counts"]["conflict"] else 0


if __name__ == "__main__":
    raise SystemExit(main())

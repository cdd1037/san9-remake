#!/usr/bin/env python3
"""Export all base L/S avatars with P_Face palette 0 into one flat directory."""

from __future__ import annotations

import argparse
import binascii
import json
import struct
import zlib
from pathlib import Path


PALETTE_RECORD_SIZE = 40 + 256 * 4


def read_u32(data: bytes, offset: int) -> int:
    return struct.unpack_from("<I", data, offset)[0]


def png_chunk(kind: bytes, payload: bytes) -> bytes:
    return (
        struct.pack(">I", len(payload))
        + kind
        + payload
        + struct.pack(">I", binascii.crc32(kind + payload) & 0xFFFFFFFF)
    )


def encode_rgb_png(width: int, height: int, rgb: bytes) -> bytes:
    rows = b"".join(b"\x00" + rgb[y * width * 3 : (y + 1) * width * 3] for y in range(height))
    return (
        b"\x89PNG\r\n\x1a\n"
        + png_chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
        + png_chunk(b"IDAT", zlib.compress(rows, 9))
        + png_chunk(b"IEND", b"")
    )


def parse_palette(path: Path) -> bytes:
    data = path.read_bytes()
    count = read_u32(data, 0)
    expected_size = 4 + count * PALETTE_RECORD_SIZE
    if count != 2 or len(data) != expected_size:
        raise ValueError(
            f"unexpected P_Face.s9 layout: count={count}, "
            f"size={len(data)}, expected_size={expected_size}"
        )
    return data[4 + 40 : 4 + PALETTE_RECORD_SIZE]


def parse_image_stream(path: Path) -> list[tuple[int, int, bytes]]:
    data = path.read_bytes()
    total = read_u32(data, 0)
    if total != len(data):
        raise ValueError(f"unexpected image file size: {len(data)} != {total}")

    records: list[tuple[int, int, bytes]] = []
    position = 4
    while position < len(data):
        if position + 16 > len(data):
            raise ValueError("truncated avatar record header")
        record_size, row_stride, width, height = struct.unpack_from("<4I", data, position)
        if record_size < 16 or position + record_size > len(data):
            raise ValueError(f"invalid avatar record at offset {position}")
        pixels = data[position + 16 : position + record_size]
        if row_stride != width or len(pixels) != row_stride * height:
            raise ValueError(f"record {len(records)} has inconsistent pixel length")
        records.append((width, height, pixels))
        position += record_size
    return records


def render_rgb(palette: bytes, pixels: bytes) -> bytes:
    return b"".join(palette[index * 4 : index * 4 + 3] for index in pixels)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("data_dir", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()

    palette = parse_palette(args.data_dir / "P_Face.s9")
    args.output.mkdir(parents=True, exist_ok=True)
    manifest: list[dict[str, int | str]] = []

    for size in ("L", "S"):
        for portrait_id, (width, height, pixels) in enumerate(
            parse_image_stream(args.data_dir / f"G_Face{size}.s9")
        ):
            filename = f"{size}_{portrait_id:03d}.png"
            rgb = render_rgb(palette, pixels)
            (args.output / filename).write_bytes(encode_rgb_png(width, height, rgb))
            manifest.append(
                {
                    "size": size,
                    "portrait_id": portrait_id,
                    "palette_index": 0,
                    "width": width,
                    "height": height,
                    "file": filename,
                }
            )

    (args.output / "manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )
    print(f"exported={len(manifest)}")
    print(f"output={args.output.resolve()}")


if __name__ == "__main__":
    main()

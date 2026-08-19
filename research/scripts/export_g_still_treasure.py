#!/usr/bin/env python3
"""Export the built-in treasure image range from G_Still.s9.

The game resolves treasure image_id through image-control mode 3 as:
    G_Still record = image_id + 172

This script deliberately writes opaque RGB PNGs.  The palette fourth byte and
the renderer's color-key behavior are not treated as confirmed alpha.
"""

from __future__ import annotations

import argparse
import binascii
import json
import struct
import zlib
from pathlib import Path


PALETTE_RECORD_SIZE = 40 + 256 * 4
TREASURE_RECORD_BASE = 172
TREASURE_COUNT = 100


def u32(data: bytes, offset: int) -> int:
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


def parse_s9(path: Path) -> tuple[list[bytes], list[tuple[int, int, bytes]]]:
    data = path.read_bytes()
    palette_count = u32(data, 0)
    image_stream_offset = 4 + palette_count * PALETTE_RECORD_SIZE
    if image_stream_offset + 4 > len(data):
        raise ValueError("palette stream exceeds file length")

    palettes = [
        data[4 + index * PALETTE_RECORD_SIZE + 40 : 4 + (index + 1) * PALETTE_RECORD_SIZE]
        for index in range(palette_count)
    ]
    image_total = u32(data, image_stream_offset)
    position = image_stream_offset + 4
    # Like the outer file length, this stream length includes its own u32.
    image_end = image_stream_offset + image_total
    if image_end != len(data):
        raise ValueError(f"image stream end {image_end} != file length {len(data)}")

    records: list[tuple[int, int, bytes]] = []
    while position < image_end:
        if position + 16 > image_end:
            raise ValueError("truncated image record header")
        record_size, row_stride, width, height = struct.unpack_from("<4I", data, position)
        if record_size < 16 or position + record_size > image_end:
            raise ValueError(f"invalid record at offset {position}: size={record_size}")
        pixels = data[position + 16 : position + record_size]
        if row_stride != width or len(pixels) != row_stride * height:
            raise ValueError(
                f"record at offset {position}: {width}x{height} != {len(pixels)} pixels"
            )
        records.append((width, height, pixels))
        position += record_size

    if len(records) != palette_count:
        raise ValueError(f"palette count {palette_count} != image count {len(records)}")
    return palettes, records


def render_rgb(palette: bytes, pixels: bytes) -> bytes:
    return b"".join(palette[index * 4 : index * 4 + 3] for index in pixels)


def nearest_thumbnail(rgb: bytes, width: int, height: int, target: int) -> bytes:
    output = bytearray(target * target * 3)
    for y in range(target):
        source_y = min(height - 1, y * height // target)
        for x in range(target):
            source_x = min(width - 1, x * width // target)
            source = (source_y * width + source_x) * 3
            destination = (y * target + x) * 3
            output[destination : destination + 3] = rgb[source : source + 3]
    return bytes(output)


def make_montage(images: list[tuple[int, int, bytes]], columns: int, cell_size: int) -> bytes:
    rows = (len(images) + columns - 1) // columns
    canvas = bytearray([32] * (columns * cell_size * rows * cell_size * 3))
    canvas_width = columns * cell_size
    for image_index, (width, height, rgb) in enumerate(images):
        thumbnail = nearest_thumbnail(rgb, width, height, cell_size - 2)
        cell_x = (image_index % columns) * cell_size + 1
        cell_y = (image_index // columns) * cell_size + 1
        for y in range(cell_size - 2):
            destination = ((cell_y + y) * canvas_width + cell_x) * 3
            source = y * (cell_size - 2) * 3
            canvas[destination : destination + (cell_size - 2) * 3] = thumbnail[
                source : source + (cell_size - 2) * 3
            ]
    return encode_rgb_png(canvas_width, rows * cell_size, bytes(canvas))


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("input", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()

    palettes, records = parse_s9(args.input)
    if TREASURE_RECORD_BASE + TREASURE_COUNT > len(records):
        raise ValueError("G_Still does not contain the confirmed treasure range")

    args.output.mkdir(parents=True, exist_ok=True)
    manifest: list[dict[str, int | str]] = []
    rendered: list[tuple[int, int, bytes]] = []
    for image_id in range(TREASURE_COUNT):
        record_index = TREASURE_RECORD_BASE + image_id
        width, height, pixels = records[record_index]
        rgb = render_rgb(palettes[record_index], pixels)
        filename = f"treasure_{image_id:03d}_record_{record_index:03d}.png"
        (args.output / filename).write_bytes(encode_rgb_png(width, height, rgb))
        manifest.append(
            {
                "image_id": image_id,
                "record_index": record_index,
                "width": width,
                "height": height,
                "palette_index": record_index,
                "file": filename,
            }
        )
        rendered.append((width, height, rgb))

    (args.output / "manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )
    (args.output / "montage_first_20.png").write_bytes(make_montage(rendered[:20], 5, 244))
    (args.output / "montage_all_100.png").write_bytes(make_montage(rendered, 10, 84))

    print(f"exported={TREASURE_COUNT}")
    print(f"output={args.output.resolve()}")
    print(f"dimensions={sorted({(item['width'], item['height']) for item in manifest})}")


if __name__ == "__main__":
    main()

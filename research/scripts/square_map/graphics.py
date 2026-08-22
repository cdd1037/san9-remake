"""Indexed S9 image/palette decoding and RGB PNG output."""

from __future__ import annotations

import binascii
import struct
import zlib
from pathlib import Path

from .scenario import ImageRecord, KOEI_PALETTE_ALPHA_OPAQUE

def png_chunk(kind: bytes, payload: bytes) -> bytes:
    return (
        struct.pack(">I", len(payload))
        + kind
        + payload
        + struct.pack(">I", binascii.crc32(kind + payload) & 0xFFFFFFFF)
    )


def encode_rgb_png(width: int, height: int, rgb: bytes) -> bytes:
    rows = b"".join(
        b"\x00" + rgb[y * width * 3 : (y + 1) * width * 3]
        for y in range(height)
    )
    return (
        b"\x89PNG\r\n\x1a\n"
        + png_chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
        + png_chunk(b"IDAT", zlib.compress(rows, 9))
        + png_chunk(b"IEND", b"")
    )


def parse_image_stream(path: Path) -> list[ImageRecord]:
    data = path.read_bytes()
    return _parse_image_stream_bytes(data, path)


def _parse_image_stream_bytes(data: bytes, path: Path) -> list[ImageRecord]:
    total = struct.unpack_from("<I", data, 0)[0]
    if total != len(data):
        raise ValueError(f"{path}: stream size {total} != file size {len(data)}")

    records: list[ImageRecord] = []
    position = 4
    while position < total:
        if position + 16 > total:
            raise ValueError(f"{path}: truncated image header at {position}")
        record_size, row_stride, width, height = struct.unpack_from(
            "<4I", data, position
        )
        if record_size < 16 or position + record_size > total:
            raise ValueError(f"{path}: invalid image record at {position}")
        pixels = data[position + 16 : position + record_size]
        if row_stride != width or len(pixels) != row_stride * height:
            raise ValueError(f"{path}: inconsistent {width}x{height} image record")
        records.append(ImageRecord(width, height, pixels))
        position += record_size
    return records


def parse_palette_image_archive(
    path: Path,
) -> tuple[list[list[tuple[int, int, int]]], list[ImageRecord]]:
    """Read a palette-prefix archive followed by a complete image stream."""

    data = path.read_bytes()
    if len(data) < 4:
        raise ValueError(f"{path}: truncated palette/image archive")
    palette_count = struct.unpack_from("<I", data, 0)[0]
    palette_record_size = 40 + 256 * 4
    image_offset = 4 + palette_count * palette_record_size
    if image_offset + 4 > len(data):
        raise ValueError(f"{path}: palette prefix exceeds file size")

    palettes: list[list[tuple[int, int, int]]] = []
    for index in range(palette_count):
        start = 4 + index * palette_record_size + 40
        palette_bytes = data[start : start + 256 * 4]
        palettes.append(
            [
                tuple(palette_bytes[offset : offset + 3])
                for offset in range(0, len(palette_bytes), 4)
            ]
        )
    images = _parse_image_stream_bytes(data[image_offset:], path)
    return palettes, images


def parse_palettes(path: Path) -> list[list[tuple[int, int, int]]]:
    data = path.read_bytes()
    count = struct.unpack_from("<I", data, 0)[0]
    record_size = 40 + 256 * 4
    expected_size = 4 + count * record_size
    if len(data) != expected_size:
        raise ValueError(f"{path}: palette file size {len(data)} != {expected_size}")

    palettes: list[list[tuple[int, int, int]]] = []
    for index in range(count):
        start = 4 + index * record_size + 40
        palette_bytes = data[start : start + 256 * 4]
        palettes.append(
            [
                tuple(palette_bytes[offset : offset + 3])
                for offset in range(0, len(palette_bytes), 4)
            ]
        )
    return palettes


def parse_palette_alphas(path: Path) -> list[list[int]]:
    """Read the reserved/alpha byte from each indexed palette entry."""

    data = path.read_bytes()
    count = struct.unpack_from("<I", data, 0)[0]
    record_size = 40 + 256 * 4
    expected_size = 4 + count * record_size
    if len(data) != expected_size:
        raise ValueError(f"{path}: palette file size {len(data)} != {expected_size}")

    alphas: list[list[int]] = []
    for index in range(count):
        start = 4 + index * record_size + 40
        alphas.append([data[start + offset + 3] for offset in range(0, 256 * 4, 4)])
    return alphas


def palette_alpha_blend(
    destination: tuple[int, int, int],
    source: tuple[int, int, int],
    alpha: int,
) -> tuple[int, int, int]:
    """Emulate KOEIGrpX's MMX palette-alpha path in RGB output space."""

    alpha = max(0, min(255, alpha))
    if alpha == 0:
        return destination
    source_555 = tuple(channel & 0xF8 for channel in source)
    if alpha >= KOEI_PALETTE_ALPHA_OPAQUE:
        return source_555
    destination_555 = tuple(channel & 0xF8 for channel in destination)
    return tuple(
        (
            source_channel * alpha
            + destination_channel * (KOEI_PALETTE_ALPHA_OPAQUE - 1 - alpha)
        )
        // KOEI_PALETTE_ALPHA_OPAQUE
        & 0xF8
        for source_channel, destination_channel in zip(
            source_555, destination_555
        )
    )

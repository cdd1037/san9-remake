"""Independent LS11 container reader for research verification; no message conversion."""
import struct

LS11_MAGIC = {b"LS11", b"Ls12"}
LS11_DICTIONARY_OFFSET = 16
LS11_DICTIONARY_SIZE = 256
LS11_DESCRIPTOR_OFFSET = LS11_DICTIONARY_OFFSET + LS11_DICTIONARY_SIZE
TERMINATOR = b"\x05\x05\x05"

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

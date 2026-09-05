using System.Buffers.Binary;

namespace San9.Importer.San9PkTc101.Containers;

public static class Ls11Archive
{
    public static byte[] Decode(byte[] source)
    {
        if (source.Length < 284 || !(source.AsSpan(0, 4).SequenceEqual("LS11"u8) || source.AsSpan(0, 4).SequenceEqual("Ls12"u8)))
            throw new InvalidDataException("Invalid LS11 archive.");
        List<(int Packed, int Size, int Offset)> blocks = [];
        int cursor = 272, total = 0;
        while (true)
        {
            if (cursor + 4 > source.Length) throw new InvalidDataException("Missing LS11 descriptor terminator.");
            int packed = BinaryPrimitives.ReadInt32BigEndian(source.AsSpan(cursor));
            if (packed == 0) break;
            if (cursor + 12 > source.Length) throw new InvalidDataException("Truncated LS11 descriptor.");
            int size = BinaryPrimitives.ReadInt32BigEndian(source.AsSpan(cursor + 4));
            int offset = BinaryPrimitives.ReadInt32BigEndian(source.AsSpan(cursor + 8));
            if (packed < 0 || size <= 0 || offset < 0 || (long)offset + packed > source.Length || (long)total + size > 64 * 1024 * 1024)
                throw new InvalidDataException("Invalid LS11 block bounds.");
            blocks.Add((packed, size, offset)); total += size; cursor += 12;
        }
        using var result = new MemoryStream(total);
        foreach (var block in blocks)
        {
            if (block.Offset < cursor + 4) throw new InvalidDataException("LS11 block overlaps table.");
            if (block.Packed == block.Size) { result.Write(source, block.Offset, block.Size); continue; }
            byte[] output = new byte[block.Size]; int written = 0, bit = 0;
            int ReadBit()
            {
                if (bit >= (long)block.Packed * 8) throw new InvalidDataException("Truncated LS11 code.");
                int value = (source[block.Offset + bit / 8] >> (7 - bit % 8)) & 1; bit++; return value;
            }
            int ReadCode()
            {
                int length = 1;
                while (ReadBit() != 0) if (++length > 25) throw new InvalidDataException("Oversized LS11 code.");
                int value = 0;
                for (int j = 0; j < length; j++) value = (value << 1) | ReadBit();
                return ((1 << length) - 2) + value;
            }
            while (written < output.Length)
            {
                int code = ReadCode();
                if (code < 256) output[written++] = source[16 + code];
                else
                {
                    int distance = code - 256;
                    if (distance <= 0 || distance > written) throw new InvalidDataException("Invalid LS11 copy distance.");
                    int length = ReadCode() + 3;
                    // The final encoded match may extend past the declared uncompressed size.
                    for (int j = 0; j < length && written < output.Length; j++, written++) output[written] = output[written - distance];
                }
            }
            result.Write(output);
        }
        return result.ToArray();
    }

}

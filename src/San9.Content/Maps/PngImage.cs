using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace San9.Content.Maps;

// Package PNG contract: lossless, 8-bit, non-interlaced, filter 0, RGB/RGBA/gray.
public sealed record PngImage(int Width, int Height, int Channels, byte[] Pixels)
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];
    public byte[] Encode()
    {
        Validate();
        using var output = new MemoryStream();
        output.Write(Signature);
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), Height);
        header[8] = 8; header[9] = Channels switch { 1 => 0, 3 => 2, 4 => 6, _ => throw new InvalidDataException() };
        Chunk(output, "IHDR", header);
        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            for (int y = 0; y < Height; y++) { z.WriteByte(0); z.Write(Pixels, y * Width * Channels, Width * Channels); }
        Chunk(output, "IDAT", compressed.ToArray());
        Chunk(output, "IEND", []);
        return output.ToArray();
    }

    public static PngImage Decode(byte[] data)
    {
        if (data.Length < 33 || !data.AsSpan(0, 8).SequenceEqual(Signature)) throw new InvalidDataException("Invalid PNG signature.");
        int w = 0, h = 0, channels = 0, pos = 8; bool ended = false;
        using var compressed = new MemoryStream();
        while (pos < data.Length)
        {
            if (pos + 12 > data.Length) throw new InvalidDataException("Truncated PNG chunk.");
            int length = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(pos));
            if (length < 0 || (long)pos + length + 12 > data.Length) throw new InvalidDataException("Invalid PNG chunk length.");
            string type = Encoding.ASCII.GetString(data, pos + 4, 4);
            var payload = data.AsSpan(pos + 8, length);
            if (Crc(data.AsSpan(pos + 4, length + 4)) != BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos + 8 + length)))
                throw new InvalidDataException("PNG checksum mismatch.");
            if (type == "IHDR")
            {
                if (pos != 8 || length != 13 || payload[8] != 8 || payload[10] != 0 || payload[11] != 0 || payload[12] != 0)
                    throw new InvalidDataException("Unsupported PNG header.");
                w = BinaryPrimitives.ReadInt32BigEndian(payload); h = BinaryPrimitives.ReadInt32BigEndian(payload[4..]);
                channels = payload[9] switch { 0 => 1, 2 => 3, 6 => 4, _ => throw new InvalidDataException("Unsupported PNG color type.") };
                if (w is <= 0 or > 16384 || h is <= 0 or > 16384 || (long)w * h * channels > 400_000_000) throw new InvalidDataException("PNG too large.");
            }
            else if (type == "IDAT") compressed.Write(payload);
            else if (type == "IEND") { ended = true; pos += length + 12; break; }
            else throw new InvalidDataException($"Unsupported map PNG chunk: {type}");
            pos += length + 12;
        }
        if (!ended || pos != data.Length || channels == 0) throw new InvalidDataException("Incomplete PNG.");
        compressed.Position = 0;
        using var z = new ZLibStream(compressed, CompressionMode.Decompress);
        byte[] pixels = new byte[checked(w * h * channels)];
        for (int y = 0; y < h; y++)
        {
            if (z.ReadByte() != 0) throw new InvalidDataException("Unsupported map PNG filter.");
            z.ReadExactly(pixels.AsSpan(y * w * channels, w * channels));
        }
        if (z.ReadByte() != -1) throw new InvalidDataException("Excess PNG pixels.");
        return new(w, h, channels, pixels);
    }

    private void Validate()
    {
        if (Width <= 0 || Height <= 0 || Channels is not (1 or 3 or 4) || (long)Width * Height * Channels != Pixels.Length)
            throw new InvalidDataException("Invalid image buffer.");
    }
    private static void Chunk(Stream stream, string type, byte[] payload)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, payload.Length); stream.Write(word);
        byte[] body = new byte[payload.Length + 4]; Encoding.ASCII.GetBytes(type).CopyTo(body, 0); payload.CopyTo(body, 4);
        stream.Write(body); BinaryPrimitives.WriteUInt32BigEndian(word, Crc(body)); stream.Write(word);
    }
    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte b in bytes) { crc ^= b; for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0); }
        return ~crc;
    }
}

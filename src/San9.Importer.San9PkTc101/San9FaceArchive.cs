using System.Collections.ObjectModel;

namespace San9.Importer.San9PkTc101;

public enum San9FaceArchiveKind
{
    BaseLarge,
    BaseSmall,
    BaseThumbnail,
    ExpansionLarge,
    ExpansionSmall,
    ExpansionThumbnail,
}

public sealed record San9FaceArchiveLayout(
    long FileSize,
    int ImageCount,
    int RecordSize,
    int Width,
    int Height,
    int RowStride)
{
    public long GetImageDataOffset(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        if (index >= ImageCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return 4L + (long)index * RecordSize + 16;
    }
}

public sealed class San9FacePaletteLayout
{
    internal San9FacePaletteLayout(long fileSize, IReadOnlyList<long> paletteOffsets)
    {
        FileSize = fileSize;
        PaletteOffsets = paletteOffsets;
    }

    public long FileSize { get; }

    public IReadOnlyList<long> PaletteOffsets { get; }
}

public static class San9FaceArchive
{
    public const int ImageRecordHeaderSize = 16;
    public const int PaletteEntryCount = 256;
    public const int PaletteEntrySize = 4;

    private const int PaletteDataSize = PaletteEntryCount * PaletteEntrySize;

    public static San9FaceArchiveLayout ReadKnownImageLayout(
        Stream stream,
        San9FaceArchiveKind kind)
    {
        San9FaceArchiveLayout layout = ReadImageLayout(stream);
        (int count, int width, int height) = kind switch
        {
            San9FaceArchiveKind.BaseLarge => (786, 240, 240),
            San9FaceArchiveKind.BaseSmall => (786, 64, 80),
            San9FaceArchiveKind.BaseThumbnail => (786, 32, 40),
            San9FaceArchiveKind.ExpansionLarge => (50, 240, 240),
            San9FaceArchiveKind.ExpansionSmall => (50, 64, 80),
            San9FaceArchiveKind.ExpansionThumbnail => (50, 32, 40),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        if (layout.ImageCount != count || layout.Width != width || layout.Height != height)
        {
            throw new InvalidDataException(
                $"Face archive does not match {kind}: expected {count} images at " +
                $"{width}x{height}, found {layout.ImageCount} at {layout.Width}x{layout.Height}.");
        }

        return layout;
    }

    public static San9FaceArchiveLayout ReadImageLayout(Stream stream)
    {
        ValidateFileStream(stream, "Face image");
        if (stream.Length < sizeof(uint))
        {
            throw new InvalidDataException("Face archive is too short for its length header.");
        }

        using BinaryReader reader = new(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        uint declaredSize = reader.ReadUInt32();
        if (declaredSize != stream.Length)
        {
            throw new InvalidDataException(
                $"Face archive declares {declaredSize} bytes, but the stream contains {stream.Length}.");
        }

        if (stream.Length < 4 + ImageRecordHeaderSize)
        {
            throw new InvalidDataException("Face archive contains no complete image record.");
        }

        int recordSize = ReadPositiveInt32(reader, "record size");
        int rowStride = ReadPositiveInt32(reader, "row stride");
        int width = ReadPositiveInt32(reader, "width");
        int height = ReadPositiveInt32(reader, "height");

        long expectedRecordSize = ImageRecordHeaderSize + (long)rowStride * height;
        if (recordSize != expectedRecordSize || rowStride != width)
        {
            throw new InvalidDataException(
                "Face record dimensions do not match its record size or packed row layout.");
        }

        long payloadSize = stream.Length - 4;
        if (payloadSize % recordSize != 0)
        {
            throw new InvalidDataException("Face archive ends with a partial image record.");
        }

        long rawCount = payloadSize / recordSize;
        if (rawCount > int.MaxValue)
        {
            throw new InvalidDataException("Face archive contains too many image records.");
        }

        int imageCount = (int)rawCount;
        for (int index = 1; index < imageCount; index++)
        {
            stream.Position = 4L + (long)index * recordSize;
            int candidateRecordSize = reader.ReadInt32();
            int candidateRowStride = reader.ReadInt32();
            int candidateWidth = reader.ReadInt32();
            int candidateHeight = reader.ReadInt32();
            if (candidateRecordSize != recordSize ||
                candidateRowStride != rowStride ||
                candidateWidth != width ||
                candidateHeight != height)
            {
                throw new InvalidDataException($"Face image record {index} has inconsistent metadata.");
            }
        }

        return new(stream.Length, imageCount, recordSize, width, height, rowStride);
    }

    public static San9FacePaletteLayout ReadPaletteLayout(Stream stream)
    {
        ValidateFileStream(stream, "Face palette");
        if (stream.Length < sizeof(int))
        {
            throw new InvalidDataException("Face palette file is too short for its record count.");
        }

        using BinaryReader reader = new(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        int paletteCount = ReadPositiveInt32(reader, "palette count");
        long maximumPaletteCount = (stream.Length - sizeof(int)) / (sizeof(int) + PaletteDataSize);
        if (paletteCount > maximumPaletteCount)
        {
            throw new InvalidDataException("Face palette count exceeds the file boundary.");
        }

        List<long> paletteOffsets = new(paletteCount);

        for (int index = 0; index < paletteCount; index++)
        {
            if (stream.Length - stream.Position < sizeof(int))
            {
                throw new InvalidDataException($"Palette record {index} is truncated.");
            }

            int headerSize = ReadPositiveInt32(reader, "palette header size");
            long recordStart = stream.Position - sizeof(int);
            long paletteOffset = checked(recordStart + headerSize);
            long nextRecord = checked(paletteOffset + PaletteDataSize);
            if (headerSize < sizeof(int) || nextRecord > stream.Length)
            {
                throw new InvalidDataException($"Palette record {index} exceeds the file boundary.");
            }

            paletteOffsets.Add(paletteOffset);
            stream.Position = nextRecord;
        }

        if (stream.Position != stream.Length)
        {
            throw new InvalidDataException("Face palette file has trailing or unparsed data.");
        }

        return new(stream.Length, new ReadOnlyCollection<long>(paletteOffsets));
    }

    public static San9FacePaletteLayout ReadKnownPaletteLayout(Stream stream)
    {
        San9FacePaletteLayout layout = ReadPaletteLayout(stream);
        if (layout.FileSize != 2_132 ||
            layout.PaletteOffsets.Count != 2 ||
            layout.PaletteOffsets[0] != 0x2C ||
            layout.PaletteOffsets[1] != 0x454)
        {
            throw new InvalidDataException("Face palette file does not match the registered layout.");
        }

        return layout;
    }

    private static int ReadPositiveInt32(BinaryReader reader, string fieldName)
    {
        int value = reader.ReadInt32();
        if (value <= 0)
        {
            throw new InvalidDataException($"Face {fieldName} must be positive.");
        }

        return value;
    }

    private static void ValidateFileStream(Stream stream, string description)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException($"{description} stream must be readable and seekable.", nameof(stream));
        }

        if (stream.Position != 0)
        {
            throw new ArgumentException($"{description} stream must be positioned at the beginning.", nameof(stream));
        }
    }
}

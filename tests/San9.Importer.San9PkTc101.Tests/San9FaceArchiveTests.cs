using San9.Importer.San9PkTc101;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class San9FaceArchiveTests
{
    [Fact]
    public void ReadImageLayout_ParsesRepeatedPackedImageRecords()
    {
        using MemoryStream stream = BuildFaceArchive(imageCount: 3, width: 2, height: 3);

        San9FaceArchiveLayout layout = San9FaceArchive.ReadImageLayout(stream);

        Assert.Equal(3, layout.ImageCount);
        Assert.Equal(2, layout.Width);
        Assert.Equal(3, layout.Height);
        Assert.Equal(22, layout.RecordSize);
        Assert.Equal(20, layout.GetImageDataOffset(0));
        Assert.Equal(64, layout.GetImageDataOffset(2));
    }

    [Fact]
    public void ReadImageLayout_RejectsInconsistentRecordMetadata()
    {
        using MemoryStream stream = BuildFaceArchive(imageCount: 2, width: 2, height: 3);
        stream.Position = 4 + 22 + 8;
        using (BinaryWriter writer = new(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(4);
        }

        stream.Position = 0;
        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => San9FaceArchive.ReadImageLayout(stream));

        Assert.Contains("record 1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadKnownImageLayout_AcceptsRegisteredExpansionThumbnailLayout()
    {
        using MemoryStream stream = BuildFaceArchive(imageCount: 50, width: 32, height: 40);

        San9FaceArchiveLayout layout = San9FaceArchive.ReadKnownImageLayout(
            stream,
            San9FaceArchiveKind.ExpansionThumbnail);

        Assert.Equal(50, layout.ImageCount);
        Assert.Equal(64_804, layout.FileSize);
    }

    [Fact]
    public void ReadKnownImageLayout_RejectsSelfConsistentUnknownLayout()
    {
        using MemoryStream stream = BuildFaceArchive(imageCount: 49, width: 32, height: 40);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => San9FaceArchive.ReadKnownImageLayout(
                stream,
                San9FaceArchiveKind.ExpansionThumbnail));

        Assert.Contains("expected 50", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadPaletteLayout_ParsesTwoPaletteRecords()
    {
        using MemoryStream stream = BuildPaletteArchive(paletteCount: 2, headerSize: 40);

        San9FacePaletteLayout layout = San9FaceArchive.ReadPaletteLayout(stream);

        Assert.Equal(2, layout.PaletteOffsets.Count);
        Assert.Equal(44, layout.PaletteOffsets[0]);
        Assert.Equal(1_108, layout.PaletteOffsets[1]);
        Assert.Equal(2_132, layout.FileSize);
    }

    [Fact]
    public void ReadKnownPaletteLayout_AcceptsRegisteredLayout()
    {
        using MemoryStream stream = BuildPaletteArchive(paletteCount: 2, headerSize: 40);

        San9FacePaletteLayout layout = San9FaceArchive.ReadKnownPaletteLayout(stream);

        Assert.Equal([44L, 1_108L], layout.PaletteOffsets);
    }

    [Fact]
    public void ReadPaletteLayout_RejectsCountBeyondFileBoundary()
    {
        using MemoryStream stream = new(BitConverter.GetBytes(int.MaxValue));

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => San9FaceArchive.ReadPaletteLayout(stream));

        Assert.Contains("count", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static MemoryStream BuildFaceArchive(int imageCount, int width, int height)
    {
        int recordSize = San9FaceArchive.ImageRecordHeaderSize + width * height;
        MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(4 + imageCount * recordSize);
            for (int index = 0; index < imageCount; index++)
            {
                writer.Write(recordSize);
                writer.Write(width);
                writer.Write(width);
                writer.Write(height);
                writer.Write(new byte[width * height]);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static MemoryStream BuildPaletteArchive(int paletteCount, int headerSize)
    {
        MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(paletteCount);
            for (int index = 0; index < paletteCount; index++)
            {
                writer.Write(headerSize);
                writer.Write(new byte[headerSize - sizeof(int)]);
                writer.Write(new byte[San9FaceArchive.PaletteEntryCount * San9FaceArchive.PaletteEntrySize]);
            }
        }

        stream.Position = 0;
        return stream;
    }
}

using System.Buffers.Binary;
using San9.Importer.San9PkTc101.Images;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class TextureArchiveTests
{
    private static byte[] OffsetArchive()
    {
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
        w.Write(2); w.Write(12); w.Write(34);
        w.Write(22); w.Write(3); w.Write(3); w.Write(2); w.Write(new byte[] { 0, 1, 2, 3, 4, 255 });
        w.Write(18); w.Write(1); w.Write(1); w.Write(2); w.Write(new byte[] { 5, 6 });
        return stream.ToArray();
    }

    [Fact]
    public void OffsetArchivePreservesEveryIndexAndRecordBoundary()
    {
        var archive = IndexedImageArchive.ReadArchive(OffsetArchive());
        Assert.Empty(archive.Palettes); Assert.Equal(2, archive.Images.Length);
        Assert.Equal(new byte[] { 0, 1, 2, 3, 4, 255 }, archive.Images[0].Pixels);
        Assert.Equal(3, archive.Images[0].Width); Assert.Equal(2, archive.Images[0].Height);
        Assert.Equal(new byte[] { 5, 6 }, archive.Images[1].Pixels);
    }

    [Theory]
    [InlineData(4, 13)]
    [InlineData(8, 12)]
    [InlineData(12, 21)]
    [InlineData(16, 4)]
    public void InvalidOffsetsOverlapsAndDimensionsAreRejected(int offset, int value)
    {
        var bytes = OffsetArchive(); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);
        Assert.Throws<InvalidDataException>(() => IndexedImageArchive.ReadOffsetImages(bytes));
    }

    [Fact]
    public void RadarBitmapConvertsBottomUpBgrRowsAndPadding()
    {
        var bytes = new byte[62]; bytes[0] = (byte)'B'; bytes[1] = (byte)'M';
        void Int(int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);
        Int(10, 54); Int(14, 40); Int(18, 1); Int(22, 2); bytes[26] = 1; bytes[28] = 24;
        bytes[54] = 1; bytes[55] = 2; bytes[56] = 3;
        bytes[58] = 4; bytes[59] = 5; bytes[60] = 6;
        var image = TextureLibraryCompiler.DecodeBitmap(bytes);
        Assert.Equal(new byte[] { 6, 5, 4, 3, 2, 1 }, image.Pixels);
    }
}

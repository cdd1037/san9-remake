using San9.Importer.San9PkTc101;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class MapImageArchiveTests
{
    [Fact]
    public void TransparentBorderExportsNeighborColorWithoutChangingCoverage()
    {
        byte[] palette = new byte[1024];
        new byte[] { 0, 248, 0, 0, 180, 120, 60, 64 }.CopyTo(palette, 0);
        byte[] indices = new byte[25]; indices[12] = 1;
        var decoded = MapImageArchive.Decode(new(5, 5, indices), palette, true, true);
        var roundTrip = San9.Content.Maps.PngImage.Decode(decoded.Encode());
        for (int i = 0; i < 25; i++)
        {
            Assert.Equal(new byte[] { 180, 120, 60 }, roundTrip.Pixels.AsSpan(i * 4, 3).ToArray());
            Assert.Equal(i == 12 ? 128 : 0, roundTrip.Pixels[i * 4 + 3]);
        }
        // Halfway between the visible center and transparent border, straight-alpha
        // filtering must retain the visible hue instead of introducing key green.
        for (int c = 0; c < 3; c++)
        {
            double sampledColor = (roundTrip.Pixels[12 * 4 + c] + roundTrip.Pixels[11 * 4 + c]) / 2.0;
            double sampledAlpha = (roundTrip.Pixels[12 * 4 + 3] + roundTrip.Pixels[11 * 4 + 3]) / 510.0;
            Assert.Equal(palette[4 + c] * 64 / 255.0, sampledColor * sampledAlpha, 10);
        }
        Assert.Equal(248, palette[1]); Assert.Equal(1, indices[12]);
    }

    [Fact]
    public void BorderUsesNearestSeedAndHandlesEmptyAndOpaqueImages()
    {
        byte[] palette = new byte[1024];
        new byte[] { 255, 0, 255, 0, 180, 120, 60, 128, 40, 80, 120, 128 }.CopyTo(palette, 0);
        var decoded = MapImageArchive.Decode(new(5, 1, [1, 0, 0, 0, 2]), palette, true, true);
        Assert.Equal(new byte[] { 180, 120, 60, 0 }, decoded.Pixels.AsSpan(8, 4).ToArray());
        Assert.Equal(new byte[] { 40, 80, 120, 0 }, decoded.Pixels.AsSpan(12, 4).ToArray());
        Assert.Equal(new byte[8], MapImageArchive.Decode(new(1, 2, [0, 0]), palette, true, true).Pixels);
        Assert.Equal(new byte[] { 180, 120, 60, 255, 40, 80, 120, 255 }, MapImageArchive.Decode(new(2, 1, [1, 2]), palette, true, true).Pixels);
        Assert.Equal(new byte[] { 255, 0, 255 }, MapImageArchive.Decode(new(1, 1, [0]), palette, false, false).Pixels);
    }

    [Fact]
    public void PaletteAlphaUsesOriginalScaleAndCompleteTransparentKey()
    {
        byte[] palette = new byte[1024];
        byte[][] colors = [[1, 2, 3, 0], [100, 150, 200, 64], [100, 150, 200, 128], [100, 150, 200, 255], [1, 2, 3, 0], [1, 2, 3, 64]];
        for (int i = 0; i < colors.Length; i++) colors[i].CopyTo(palette, i * 4);
        var image = new IndexedMapImage(6, 1, [0, 1, 2, 3, 4, 5]);
        var decoded = MapImageArchive.Decode(image, palette, true, true);
        Assert.Equal(new byte[] { 0, 128, 255, 255, 0, 128 }, Enumerable.Range(0, 6).Select(i => decoded.Pixels[i * 4 + 3]).ToArray());
        var keyOnly = MapImageArchive.Decode(image, palette, true, false);
        Assert.Equal(255, keyOnly.Pixels[7]); Assert.Equal(0, keyOnly.Pixels[19]); Assert.Equal(255, keyOnly.Pixels[23]);
    }
    [Fact]
    public void ImageContainerRequiresExactCountStrideAndLength()
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(24); writer.Write(20); writer.Write(2); writer.Write(2); writer.Write(2); writer.Write(new byte[] { 1, 2, 3, 4 });
        var bytes = stream.ToArray(); Assert.Single(MapImageArchive.ReadImages(bytes, 1));
        Assert.Throws<InvalidDataException>(() => MapImageArchive.ReadImages(bytes, 2));
        bytes[8] = 3; Assert.Throws<InvalidDataException>(() => MapImageArchive.ReadImages(bytes, 1));
        Assert.Throws<InvalidDataException>(() => MapImageArchive.ReadPalettes(new byte[12], 1));
    }
}

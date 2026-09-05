using San9.Importer.San9PkTc101;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class MapAnimationCompilerTests
{
    [Fact]
    public void WaterQualificationUsesOnlySpecifiedBridgeOffsets()
    {
        foreach (int terrain in new[] { 9, 10, 15, 16 }) Assert.True(MapAnimationCompiler.HasWater(terrain, 10, 10, (_, _) => false));
        foreach (var (x, y) in new[] { (11, 9), (7, 9), (9, 11), (9, 7) })
            Assert.True(MapAnimationCompiler.HasWater(12, 10, 10, (bx, by) => bx == x && by == y));
        Assert.False(MapAnimationCompiler.HasWater(12, 10, 10, (x, y) => x == 11 && y == 10));
        Assert.False(MapAnimationCompiler.HasWater(11, 10, 10, (_, _) => true));
        Assert.False(MapAnimationCompiler.HasWater(1, 10, 10, (_, _) => true));
    }

    [Fact]
    public void FrameExtractionKeepsOtherFrameColorsOutsideTransparentDilation()
    {
        var sheet = new IndexedMapImage(4, 2, [1, 0, 2, 0, 0, 0, 0, 0]);
        byte[] palette = new byte[1024];
        new byte[] { 0, 248, 0, 0, 255, 0, 0, 128, 0, 0, 255, 128 }.CopyTo(palette, 0);
        var frame = MapImageArchive.Frame(sheet, 2, 1, 1, 0);
        var decoded = MapImageArchive.Decode(frame, palette, true, true);
        Assert.Equal(new byte[] { 0, 0, 255, 0 }, decoded.Pixels.AsSpan(4, 4).ToArray());
        Assert.Equal(new byte[] { 2, 0, 0, 0 }, frame.Pixels);
        Assert.Throws<InvalidDataException>(() => MapImageArchive.Frame(sheet, 3, 1, 0, 0));
        Assert.Throws<InvalidDataException>(() => MapImageArchive.Frame(sheet, 2, 1, 2, 0));
        Assert.Throws<InvalidDataException>(() => MapImageArchive.ReadCombined(new byte[8], 4, 52));
    }
}

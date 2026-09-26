using San9.Importer.San9PkTc101.Images;
using San9.Importer.San9PkTc101.Interface;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class NineSliceArtTests
{
    private static IndexedImage Part(int width, int height, byte value) =>
        new(width, height, Enumerable.Repeat(value, width * height).ToArray());

    [Fact]
    public void AssemblyPreservesUnequalEdgesAndTransparentPixelsWithoutScaling()
    {
        var image = NineSliceArt.Assemble([
            new(2, 2, [0, 9, 10, 11]), Part(1, 2, 1), Part(3, 2, 2),
            Part(2, 3, 3), Part(1, 3, 4), Part(3, 3, 5),
            Part(2, 1, 6), Part(1, 1, 7), Part(3, 1, 8)]);
        Assert.Equal(6, image.Width);
        Assert.Equal(6, image.Height);
        Assert.Equal(new byte[] {
            0,9,1,2,2,2, 10,11,1,2,2,2,
            3,3,4,5,5,5, 3,3,4,5,5,5, 3,3,4,5,5,5,
            6,6,7,8,8,8 }, image.Pixels);
    }

    [Fact]
    public void MisalignedPartsAreRejected()
    {
        var parts = Enumerable.Range(0, 9).Select(i => Part(2, 2, (byte)i)).ToArray();
        parts[4] = Part(3, 2, 4);
        Assert.Throws<InvalidDataException>(() => NineSliceArt.Assemble(parts));
    }
}

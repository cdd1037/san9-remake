using San9.Importer.San9PkTc101.Images;
using San9.Importer.San9PkTc101.Interface;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class MapHoverArtTests
{
    [Fact]
    public void CornersReplaceUnderlyingPixelsIncludingTransparentIndex()
    {
        var parts = Enumerable.Range(0, 9).Select(i => new IndexedImage(2, 2, Enumerable.Repeat((byte)i, 4).ToArray())).ToArray();
        var image = MapHoverArt.Compose(parts, 7, 9);
        Assert.Equal(0, image.Pixels[0]);
        Assert.Equal(2, image.Pixels[6]);
        Assert.Equal(6, image.Pixels[8 * 7]);
        Assert.Equal(8, image.Pixels[^1]);
        Assert.Equal(4, image.Pixels[4 * 7 + 3]);
        Assert.Equal(1, image.Pixels[3]);
        Assert.Equal(3, image.Pixels[4 * 7]);
    }
}

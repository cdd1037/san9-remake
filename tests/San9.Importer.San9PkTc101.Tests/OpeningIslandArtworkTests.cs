using San9.Content.Images;
using San9.Importer.San9PkTc101.Images;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class OpeningIslandArtworkTests
{
    [Fact]
    public void FramedIslandMovesWithoutStampingItsFrameOntoTheMainland()
    {
        var background = new PngImage(4, 4, 4, Enumerable.Repeat(new byte[] { 12, 24, 48, 128 }, 16).SelectMany(p => p).ToArray());
        var inset = new PngImage(4, 4, 4, Enumerable.Repeat(new byte[] { 48, 96, 32, 255 }, 16).SelectMany(p => p).ToArray());
        inset.Pixels.AsSpan(5 * 4, 4).Fill(255);
        new byte[] { 12, 24, 48, 255 }.CopyTo(inset.Pixels, 3 * 4);
        byte[] mask = new byte[16]; mask[5] = 46;

        var layers = OpeningIslandArtwork.Separate(background, inset, 0, 0, 1, 1, new(4, 4, mask));

        Assert.Equal(background.Pixels, layers.Background.Pixels);
        Assert.Equal(255, layers.Island.Pixels[5 * 4 + 3]);
        Assert.Equal(inset.Pixels[(3 * 4)..(4 * 4)], layers.Island.Pixels[(3 * 4)..(4 * 4)]);
        Assert.Equal(0, layers.Island.Pixels[15 * 4 + 3]);
        Assert.Equal(255, inset.Pixels[15 * 4 + 3]);
    }

    [Fact]
    public void PreviewSeaPanelBecomesTransparentWhileMainlandCoverageIsPreserved()
    {
        var source = new PngImage(3, 1, 4, [48, 96, 32, 192, 12, 24, 48, 255, 0, 0, 0, 0]);

        var result = OpeningIslandArtwork.RemovePreviewSeaFrame(source);

        Assert.Equal(source.Pixels[..4], result.Pixels[..4]);
        Assert.Equal(0, result.Pixels[7]);
        Assert.Equal(0, result.Pixels[11]);
        Assert.Equal(255, source.Pixels[7]);
    }
}

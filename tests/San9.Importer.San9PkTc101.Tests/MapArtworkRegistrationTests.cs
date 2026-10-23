using San9.Importer.San9PkTc101.Images;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class MapArtworkRegistrationTests
{
    [Fact]
    public void RegisteredLandmarksSampleTheirSourceWithoutChangingThePalette()
    {
        var source = new IndexedImage(8, 4, Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());
        var registered = MapArtworkRegistration.Align(source, [new(2, 1, 5, 2), new(7, 3, 7, 3)]);
        Assert.Equal(source.Pixels[2 * 8 + 5], registered.Pixels[1 * 8 + 2]);
        Assert.Equal(source.Pixels[3 * 8 + 7], registered.Pixels[3 * 8 + 7]);
        Assert.All(registered.Pixels, pixel => Assert.Contains(pixel, source.Pixels));
        Assert.Equal(source.Pixels, MapArtworkRegistration.Align(source, [new(2, 1, 2, 1)]).Pixels);
    }
}

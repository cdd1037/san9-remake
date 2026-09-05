using San9.Content.Packages;
using San9.Content.Images;
using San9.Content.Maps;

namespace San9.Content.Tests;

public sealed class MapImageTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    public void PackagePngRoundTripPreservesEveryByte(int channels)
    {
        byte[] bytes = Enumerable.Range(0, 37 * 19 * channels).Select(i => (byte)(i * 73)).ToArray();
        var decoded = PngImage.Decode(new PngImage(37, 19, channels, bytes).Encode());
        Assert.Equal(37, decoded.Width); Assert.Equal(19, decoded.Height); Assert.Equal(channels, decoded.Channels); Assert.Equal(bytes, decoded.Pixels);
    }
    [Fact]
    public void CorruptedPngIsRejectedBeforeDecompression()
    {
        var bytes = new PngImage(2, 1, 3, [1, 2, 3, 4, 5, 6]).Encode(); bytes[45] ^= 1;
        Assert.Throws<InvalidDataException>(() => PngImage.Decode(bytes));
        Assert.Throws<InvalidDataException>(() => PngImage.Decode(bytes[..^5]));
    }
    [Theory]
    [InlineData("../outside.png")]
    [InlineData("../../outside.png")]
    [InlineData("C:/outside.png")]
    public void AssetPathsCannotEscapePackage(string path) => Assert.Throws<InvalidDataException>(() => PackageFiles.Resolve(Path.GetTempPath(), path));
}

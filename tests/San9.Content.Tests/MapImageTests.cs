using San9.Content.Packages;
using San9.Content.Images;
using San9.Content.Maps;

namespace San9.Content.Tests;

public sealed class MapImageTests
{
    [Fact]
    public async Task ParallelAssetLoadingPreservesIdentityAndRejectsCorruption()
    {
        string root = Path.Combine(Path.GetTempPath(), "san9-images-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var assets = Enumerable.Range(0, 12).Select(i =>
            {
                byte[] png = new PngImage(1, 1, 3, [(byte)i, 42, 255]).Encode();
                string name = $"{i}.png";
                File.WriteAllBytes(Path.Combine(root, name), png);
                return new ImageAsset(i.ToString(), name, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(png)), 1, 1, 3);
            }).ToArray();
            await ImageAssetFiles.VerifyAsync(root, assets);
            var decoded = ImageAssetFiles.Load(root, assets);
            for (int i = 0; i < assets.Length; i++) Assert.Equal(new byte[] { (byte)i, 42, 255 }, decoded[i.ToString()].Pixels);
            var changed = new PngImage(1, 1, 3, [99, 42, 255]);
            File.WriteAllBytes(Path.Combine(root, "5.png"), changed.Encode());
            Assert.Equal(changed.Pixels, ImageAssetFiles.Load(root, assets)["5"].Pixels);
            await Assert.ThrowsAsync<InvalidDataException>(() => ImageAssetFiles.VerifyAsync(root, assets));
            Assert.Throws<InvalidDataException>(() => ImageAssetFiles.Load(root, [assets[5] with { Width = 2 }]));
            File.WriteAllBytes(Path.Combine(root, "5.png"), [0, 1, 2]);
            await Assert.ThrowsAsync<InvalidDataException>(() => ImageAssetFiles.VerifyAsync(root, assets));
            Assert.Throws<InvalidDataException>(() => ImageAssetFiles.Load(root, assets));
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ImageAssetFiles.VerifyAsync(root, assets, cancellation.Token));
            Assert.ThrowsAny<OperationCanceledException>(() => ImageAssetFiles.Load(root, assets, cancellation.Token));
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void CatalogRejectsOverlapsOutOfBoundsAndConflictingFileMetadata()
    {
        var file = new ImageFile("atlas.png", new string('0', 64), 16, 16, 4);
        var a = new ImageAsset("a", file, 2, 2, 4, 4);
        ImageAssetFiles.ValidateCatalog([a, a with { Id = "alias" }, a with { Id = "b", X = 8 }]);
        Assert.Throws<InvalidDataException>(() => ImageAssetFiles.ValidateCatalog([a, a with { Id = "b", X = 4 }]));
        Assert.Throws<InvalidDataException>(() => ImageAssetFiles.ValidateCatalog([a with { X = 14 }]));
        Assert.Throws<InvalidDataException>(() => ImageAssetFiles.ValidateCatalog([a, a with { Id = "b", File = file with { Channels = 3 } }]));
    }
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    public void PackagePngRoundTripPreservesEveryByte(int channels)
    {
        byte[] bytes = Enumerable.Range(0, 37 * 19 * channels).Select(i => (byte)(i * 73)).ToArray();
        var encoded = new PngImage(37, 19, channels, bytes).Encode();
        // Independent bitwise reference checks every emitted PNG chunk's CRC.
        for (int offset = 8; offset < encoded.Length;)
        {
            int length = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(encoded.AsSpan(offset));
            uint crc = uint.MaxValue;
            foreach (byte value in encoded.AsSpan(offset + 4, length + 4))
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u);
            }
            Assert.Equal(~crc, System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(encoded.AsSpan(offset + 8 + length)));
            offset += length + 12;
        }
        var decoded = PngImage.Decode(encoded);
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

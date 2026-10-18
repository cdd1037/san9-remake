using System.Buffers.Binary;
using San9.Content.Images;
using San9.Importer.San9PkTc101.Images;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class ZoomTextureCompilerTests
{
    [Fact]
    public void FeatheredBackgroundAndInterfacePreservePaletteCoverageWithoutFadingBodiesOrEffects()
    {
        byte[] exe = new byte[0x264000];
        for (int t = 0; t < 23; t++)
            for (int v = 0; v < 6; v++)
                BinaryPrimitives.WriteInt32LittleEndian(exe.AsSpan(0x2635bc + t * 52 + v * 8), -1);
        byte[] images = Images(), palettes = Palettes();
        Dictionary<string, PngImage> decoded = [];
        byte[] Read(string path) => Path.GetFileName(path) switch
        {
            "G_ZoomIn.s9" => images,
            "P_ZoomIn.s9" => palettes,
            _ => throw new InvalidOperationException($"Unexpected source: {path}")
        };
        ImageAsset Save(string id, string path, PngImage image)
        {
            decoded.Add(id, image);
            return new(id, path, image.Width, image.Height, image.Channels);
        }

        var library = TextureLibraryCompiler.Compile(exe, "synthetic", Read, Save, null,
            CancellationToken.None, "g_zoomin");
        var archive = Assert.Single(library.Archives);
        Assert.Equal(83, archive.Variants.Length);
        foreach (var variant in archive.Variants)
        {
            var image = decoded[variant.Image.Id];
            // All source palettes deliberately contain identical half-covered and opaque colors.
            // Only native palette-alpha consumers may preserve that half coverage; applying it
            // globally would incorrectly fade body sheets and additive particle source colors.
            bool paletteCoverage = variant.ImageIndex is >= 0 and <= 57 or 141 or 142;
            Assert.Equal(new byte[] { 0, paletteCoverage ? (byte)128 : (byte)255, 255 },
                Enumerable.Range(0, 3).Select(i => image.Pixels[i * 4 + 3]).ToArray());
            Assert.EndsWith(paletteCoverage ? "/alpha/full/source.png" : "/key/full/source.png", variant.Image.Path);
            Assert.Equal(new byte[] { 40, 80, 120 }, image.Pixels.AsSpan(4, 3).ToArray());
        }
    }

    private static byte[] Images()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(145);
        for (int i = 0; i < 145; i++) writer.Write(4 + 145 * 4 + i * 19);
        for (int i = 0; i < 145; i++)
        {
            writer.Write(19); writer.Write(3); writer.Write(3); writer.Write(1);
            writer.Write(new byte[] { 0, 1, 2 });
        }
        return stream.ToArray();
    }

    private static byte[] Palettes()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(102);
        for (int i = 0; i < 102; i++)
        {
            writer.Write(new byte[40]);
            byte[] palette = new byte[1024];
            new byte[] { 0, 0, 0, 128, 40, 80, 120, 64, 80, 120, 160, 128 }.CopyTo(palette, 0);
            writer.Write(palette);
        }
        return stream.ToArray();
    }
}

using San9.Content.Images;

namespace San9.Importer.San9PkTc101.Images;

/// <summary>Source dimensions and a transient pixel recipe. Packing decides when pixels are needed.</summary>
public sealed record TextureSourceImage(int Width, int Height, int Channels, Func<PngImage> Decode)
{
    public static implicit operator TextureSourceImage(PngImage image) =>
        new(image.Width, image.Height, image.Channels, () => image);

    public static TextureSourceImage Indexed(IndexedImage image, byte[] palette, bool transparent, bool paletteAlpha) =>
        new(image.Width, image.Height, transparent ? 4 : 3,
            () => IndexedImageArchive.Decode(image, palette, transparent, paletteAlpha));
}

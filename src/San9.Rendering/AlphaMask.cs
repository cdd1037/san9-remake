using San9.Content.Images;
namespace San9.Rendering;

public sealed class AlphaMask
{
    private readonly byte[] _bits;
    public int Width { get; }
    public int Height { get; }
    public AlphaMask(PngImage image, ImageAsset region, byte threshold)
    {
        Width = region.Width; Height = region.Height; _bits = new byte[(Width * Height + 7) / 8];
        for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++)
            if (image.Channels != 4 || image.Pixels[((y + region.Y) * image.Width + x + region.X) * 4 + 3] >= threshold)
            { int bit = y * Width + x; _bits[bit / 8] |= (byte)(1 << (bit % 8)); }
    }
    public bool Contains(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height && (_bits[(y * Width + x) / 8] & (1 << ((y * Width + x) % 8))) != 0;
}

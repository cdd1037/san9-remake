using San9.Content.Images;
namespace San9.Rendering;

public sealed class AlphaMask
{
    private readonly byte[] _bits;
    public (int X, int Y, int Width, int Height) Bounds { get; }
    public int Width { get; }
    public int Height { get; }
    public AlphaMask(PngImage image, ImageAsset region, byte threshold)
    {
        Width = region.Width; Height = region.Height; _bits = new byte[(Width * Height + 7) / 8];
        int left = Width, top = Height, right = 0, bottom = 0;
        for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++)
            if (image.Channels != 4 || image.Pixels[((y + region.Y) * image.Width + x + region.X) * 4 + 3] >= threshold)
            { int bit = y * Width + x; _bits[bit / 8] |= (byte)(1 << (bit % 8));
                left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x + 1); bottom = Math.Max(bottom, y + 1); }
        Bounds = right > left ? (left, top, right - left, bottom - top) : (0, 0, 0, 0);
    }
    public bool Contains(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height && (_bits[(y * Width + x) / 8] & (1 << ((y * Width + x) % 8))) != 0;
}

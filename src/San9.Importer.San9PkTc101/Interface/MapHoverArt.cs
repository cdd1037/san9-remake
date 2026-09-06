using San9.Importer.San9PkTc101.Images;

namespace San9.Importer.San9PkTc101.Interface;

public static class MapHoverArt
{
    public static IndexedImage Compose(IReadOnlyList<IndexedImage> parts, int width, int height)
    {
        if (parts.Count != 9) throw new ArgumentException("Expected nine frame parts.", nameof(parts));
        byte[] pixels = new byte[width * height];
        void Tile(int part, int x, int y, int w, int h)
        {
            var source = parts[part];
            for (int row = 0; row < h; row++)
            for (int col = 0; col < w; col++)
                pixels[(y + row) * width + x + col] = source.Pixels[row % source.Height * source.Width + col % source.Width];
        }
        Tile(4, 0, 0, width, height);
        Tile(1, 0, 0, width, parts[1].Height);
        Tile(7, 0, height - parts[7].Height, width, parts[7].Height);
        Tile(3, 0, 0, parts[3].Width, height);
        Tile(5, width - parts[5].Width, 0, parts[5].Width, height);
        foreach (int part in new[] { 0, 2, 6, 8 })
            Tile(part, part % 3 == 2 ? width - parts[part].Width : 0,
                part >= 6 ? height - parts[part].Height : 0, parts[part].Width, parts[part].Height);
        return new(width, height, pixels);
    }
}

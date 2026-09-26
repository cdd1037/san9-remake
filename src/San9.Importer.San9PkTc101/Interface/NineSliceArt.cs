using San9.Importer.San9PkTc101.Images;

namespace San9.Importer.San9PkTc101.Interface;

public static class NineSliceArt
{
    public static IndexedImage Assemble(IReadOnlyList<IndexedImage> parts)
    {
        if (parts.Count != 9) throw new ArgumentException("Expected nine frame parts.", nameof(parts));
        int[] widths = [parts[0].Width, parts[1].Width, parts[2].Width];
        int[] heights = [parts[0].Height, parts[3].Height, parts[6].Height];
        int width = widths.Sum(), height = heights.Sum();
        byte[] pixels = new byte[checked(width * height)];
        int top = 0;
        for (int row = 0; row < 3; top += heights[row++])
        {
            int left = 0;
            for (int column = 0; column < 3; left += widths[column++])
            {
                var part = parts[row * 3 + column];
                if (part.Width != widths[column] || part.Height != heights[row])
                    throw new InvalidDataException("Frame parts do not form a rectangular nine-slice grid.");
                for (int y = 0; y < part.Height; y++)
                    part.Pixels.AsSpan(y * part.Width, part.Width).CopyTo(pixels.AsSpan((top + y) * width + left));
            }
        }
        return new(width, height, pixels);
    }
}

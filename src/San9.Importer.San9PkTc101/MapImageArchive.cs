using San9.Content.Maps;

namespace San9.Importer.San9PkTc101;

public sealed record IndexedMapImage(int Width, int Height, byte[] Pixels);

public static class MapImageArchive
{
    public static (byte[][] Palettes, IndexedMapImage[] Images) ReadCombined(byte[] bytes, int paletteCount, int imageCount)
    {
        int offset = checked(4 + paletteCount * 1064);
        if (bytes.Length < offset + 4) throw new InvalidDataException("Truncated palette/image archive.");
        return (ReadPalettes(bytes[..offset], paletteCount), ReadImages(bytes[offset..], imageCount));
    }

    public static IndexedMapImage Frame(IndexedMapImage sheet, int columns, int rows, int column, int row)
    {
        if (columns <= 0 || rows <= 0 || sheet.Width % columns != 0 || sheet.Height % rows != 0 || column < 0 || column >= columns || row < 0 || row >= rows)
            throw new InvalidDataException("Invalid animation frame grid.");
        int width = sheet.Width / columns, height = sheet.Height / rows;
        byte[] pixels = new byte[width * height];
        for (int y = 0; y < height; y++) sheet.Pixels.AsSpan((row * height + y) * sheet.Width + column * width, width).CopyTo(pixels.AsSpan(y * width));
        return new(width, height, pixels);
    }

    public static IndexedMapImage[] ReadImages(byte[] bytes, int expectedCount)
    {
        using var r = new BinaryReader(new MemoryStream(bytes));
        if (bytes.Length < 4 || r.ReadUInt32() != bytes.Length) throw new InvalidDataException("Map image length mismatch.");
        List<IndexedMapImage> images = [];
        while (r.BaseStream.Position < bytes.Length)
        {
            long start = r.BaseStream.Position;
            if (start + 16 > bytes.Length) throw new InvalidDataException("Truncated map image record.");
            uint size = r.ReadUInt32(), stride = r.ReadUInt32(), width = r.ReadUInt32(), height = r.ReadUInt32();
            if (width is 0 or > 4096 || height is 0 or > 4096 || stride != width || size != 16 + (long)stride * height || start + size > bytes.Length)
                throw new InvalidDataException("Invalid map image dimensions.");
            images.Add(new((int)width, (int)height, r.ReadBytes(checked((int)(stride * height)))));
            if (images.Count > expectedCount) throw new InvalidDataException("Excess map images.");
        }
        if (images.Count != expectedCount) throw new InvalidDataException("Map image count mismatch.");
        return images.ToArray();
    }
    public static byte[][] ReadPalettes(byte[] bytes, int expectedCount)
    {
        using var r = new BinaryReader(new MemoryStream(bytes));
        if (bytes.Length != 4 + expectedCount * 1064 || r.ReadUInt32() != expectedCount) throw new InvalidDataException("Map palette count/length mismatch.");
        byte[][] palettes = new byte[expectedCount][];
        for (int i = 0; i < expectedCount; i++) { r.BaseStream.Position += 40; palettes[i] = r.ReadBytes(1024); }
        return palettes;
    }
    public static PngImage Decode(IndexedMapImage image, byte[] palette, bool transparent, bool paletteAlpha)
    {
        if (palette.Length != 1024) throw new InvalidDataException("Invalid palette.");
        int channels = transparent ? 4 : 3;
        byte[] pixels = new byte[image.Pixels.Length * channels];
        for (int i = 0; i < image.Pixels.Length; i++)
        {
            int p = image.Pixels[i] * 4;
            palette.AsSpan(p, 3).CopyTo(pixels.AsSpan(i * channels, 3));
            if (transparent) pixels[i * channels + 3] = palette.AsSpan(p, 4).SequenceEqual(palette.AsSpan(0, 4)) ? (byte)0 :
                paletteAlpha ? (byte)((Math.Min((int)palette[p + 3], 128) * 255 + 64) / 128) : (byte)255;
        }
        if (transparent) ExtendTransparentColors(pixels, image.Width, image.Height);
        return new(image.Width, image.Height, channels, pixels);
    }

    internal static void ExtendTransparentColors(byte[] pixels, int width, int height)
    {
        // Multi-source breadth-first dilation: preserve coverage and visible colors,
        // but give invisible texels neighboring colors for ordinary bilinear sampling.
        int count = width * height;
        var assigned = new bool[count];
        var queue = new int[count];
        int head = 0, tail = 0;
        for (int i = 0; i < count; i++)
            if (pixels[i * 4 + 3] != 0) { assigned[i] = true; queue[tail++] = i; }
        if (tail == 0) { Array.Clear(pixels); return; }
        while (head < tail)
        {
            int source = queue[head++], x = source % width, y = source / width;
            for (int ny = Math.Max(0, y - 1); ny <= Math.Min(height - 1, y + 1); ny++)
                for (int nx = Math.Max(0, x - 1); nx <= Math.Min(width - 1, x + 1); nx++)
                {
                    int target = ny * width + nx;
                    if (assigned[target]) continue;
                    pixels.AsSpan(source * 4, 3).CopyTo(pixels.AsSpan(target * 4, 3));
                    assigned[target] = true; queue[tail++] = target;
                }
        }
    }
}

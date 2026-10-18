using San9.Content.Images;

namespace San9.Rendering;

/// <summary>Display-only minimap specified by BEH-UI-0001.</summary>
public static class MinimapRenderer
{
    public static int TerrainFamily(int terrain) => terrain switch
    {
        0 => 0, 1 or 13 or 14 => 6, 2 or 4 or 7 => 1, 6 => 3,
        9 or 12 => 7, 10 or 15 or 16 => 4, 11 => 5, 3 or 5 or 8 => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(terrain))
    };

    public static PngImage Compile(int width, int height, IReadOnlyList<San9.Content.Maps.MapSquare> squares, byte[] colors)
    {
        if (width <= 0 || height <= 0 || (long)width * height != squares.Count || colors.Length != 32)
            throw new InvalidDataException("Invalid minimap inputs.");
        var geometry = new MinimapGeometry(width, height);
        byte[] pixels = new byte[320 * 160 * 4];
        for (int y = 0; y < 160; y++)
        {
            for (int x = 0; x < 320; x++)
            {
                if (!geometry.TryGrid((x + .5) / 320, (y + .5) / 160, out double gx, out double gy)) continue;
                int sx = (int)Math.Floor(gx + .5), sy = (int)Math.Floor(gy + .5);
                int color = TerrainFamily(squares[sy * width + sx].Terrain) * 4;
                int target = (y * 320 + x) * 4;
                for (int channel = 0; channel < 3; channel++)
                    pixels[target + channel] = (byte)(colors[color + channel] & (channel == 1 ? 252 : 248));
                pixels[target + 3] = 255;
            }
        }
        return new(320, 160, 4, pixels);
    }

}

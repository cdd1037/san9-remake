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

    public static PngImage Compile(IReadOnlyList<San9.Content.Maps.MapSquare> squares, byte[] colors)
    {
        if (squares.Count != 40000 || colors.Length != 32) throw new InvalidDataException("Invalid minimap inputs.");
        byte[] pixels = new byte[320 * 160 * 4];
        for (int y = 1; y < 160; y++)
        {
            int span = 4 * (Math.Min(y, 160 - y) - 1), left = (320 - span) / 2;
            for (int x = left; x < left + span; x++)
            {
                var (sx, sy) = SampleSquare(x, y);
                int color = TerrainFamily(squares[sy * 200 + sx].Terrain) * 4;
                int target = (y * 320 + x) * 4;
                for (int channel = 0; channel < 3; channel++)
                    pixels[target + channel] = (byte)(colors[color + channel] & (channel == 1 ? 252 : 248));
                pixels[target + 3] = 255;
            }
        }
        return new(320, 160, 4, pixels);
    }

    public static (int X, int Y) SampleSquare(int pixelX, int pixelY)
    {
        int worldX = pixelX * 40, worldY = pixelY * 40;
        int projectedX = worldX + 2 * worldY - 6432;
        int x = (int)Math.Floor(projectedX / 64d);
        int y = (int)Math.Floor((2 * worldY - worldX + 6368) / 64d);
        // The source picker owns this shared sloping edge on the preceding X tile.
        // Vertices and the other diagonal retain ordinary floor ownership.
        if (projectedX % 64 == 0 && worldX % 32 + 2 * (worldY % 16) == 32) x--;
        return (Math.Clamp(x, 0, 199), Math.Clamp(y, 0, 199));
    }

}

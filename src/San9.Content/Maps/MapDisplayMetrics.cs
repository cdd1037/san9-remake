namespace San9.Content.Maps;

/// <summary>Isometric cell metrics; map dimensions belong to each map instance.</summary>
public static class MapDisplayMetrics
{
    public const int HalfCellWidth = 32;
    public const int HalfCellHeight = 16;
    public const int OriginX = 96;
    public const int OriginY = 112;
    public const int ElevationStep = 10;
    public static (int Width, int Height) CanvasSize(int width, int height) =>
        (checked((width + height) * HalfCellWidth + 256), checked((width + height) * HalfCellHeight + 256));
}

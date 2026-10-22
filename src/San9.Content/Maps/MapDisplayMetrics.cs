namespace San9.Content.Maps;

/// <summary>The currently supported display profile; independent of domain world dimensions.</summary>
public static class MapDisplayMetrics
{
    public const int Width = 200;
    public const int Height = 200;
    public const int LastColumn = Width - 1;
    public const int LastRow = Height - 1;
    public const int CanvasWidth = 13056;
    public const int CanvasHeight = 6656;
    public const int HalfCellWidth = 32;
    public const int HalfCellHeight = 16;
    public const int OriginX = 96;
    public const int OriginY = 112;
    public const int ElevationStep = 10;
}

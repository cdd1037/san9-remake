namespace San9.Rendering;

public static class BuildingInfoGeometry
{
    private static readonly (int X, int Y)[] Offsets =
        [(12, -128), (12, -74), (28, -68), (6, -66), (80, -106), (58, -110),
         (36, -64), (48, -56), (40, -70), (16, -78), (25, -74), (-63, -81),
         (16, -80), (-76, -78), (10, -68), (-63, -72), (-2, -97)];

    public static (int X, int Y) Anchor(int x, int y, int type, int elevation, int mapHeight)
    {
        var center = MapGeometry.Center(x, y, mapHeight);
        var offset = Offsets[type];
        return (center.X + offset.X, center.Y - 10 * (elevation - 1) + offset.Y);
    }

    public static int FillWidth(int current, int maximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximum);
        return (int)Math.Clamp((68L * current + maximum - 68) / maximum, 0, 68);
    }

    public static string FillState(int current, int maximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximum);
        return current < 20L * maximum / 100 ? "critical" : current < 40L * maximum / 100 ? "warning" : "normal";
    }
}

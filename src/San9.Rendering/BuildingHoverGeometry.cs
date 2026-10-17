namespace San9.Rendering;

public static class BuildingHoverGeometry
{
    public const int Width = 160;
    public static (float X, float Y) Place(float x, float y, int height, float viewportWidth, float viewportHeight) =>
        (Math.Clamp(x + Width > viewportWidth ? x - Width : x, 0, Math.Max(0, viewportWidth - Width)),
         Math.Clamp(y, 0, Math.Max(0, viewportHeight - height)));
}

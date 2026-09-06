namespace San9.Rendering;

public static class BuildingHoverGeometry
{
    public const int Width = 160;
    public static (float X, float Y) Place(float x, float y, int height) =>
        (Math.Clamp(x > 643 ? x - Width : x, 0, 1024 - Width), Math.Clamp(y, 0, 736 - height));
}

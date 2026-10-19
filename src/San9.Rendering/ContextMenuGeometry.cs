namespace San9.Rendering;

public static class ContextMenuGeometry
{
    public static (float X, float Y) Submenu(float x, float y, float parentWidth, float width, float height,
        float viewportWidth, float viewportHeight)
    {
        foreach (var point in new[] { (x + parentWidth, y), (x + parentWidth, y + 24 - height),
            (x - width, y), (x - width, y + 24 - height) })
            if (point.Item1 >= 0 && point.Item1 + width <= viewportWidth && point.Item2 >= 0 && point.Item2 + height <= viewportHeight)
                return point;
        return (Math.Clamp(x + parentWidth, 0, Math.Max(0, viewportWidth - width)),
            Math.Clamp(y, 0, Math.Max(0, viewportHeight - height)));
    }
}

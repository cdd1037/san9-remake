namespace San9.Rendering;

/// <summary>Menu-local dimensions within the actual logical viewport; reserve the command summary at the left.</summary>
public static class CommandMenuGeometry
{
    public const int ButtonWidth = 68, ButtonHeight = 28, PrimaryWidth = 90;
    public static (float X, float Y) Primary(float clickX, float clickY, int count, float width, float height) =>
        (Math.Clamp(clickX - 13, Math.Min(268, Math.Max(0, width - PrimaryWidth)), Math.Max(0, width - PrimaryWidth)),
         Math.Clamp(clickY - 18, 0, Math.Max(0, height - Math.Max(278, 32 + count * ButtonHeight))));

    public static (float X, float Y) Secondary(float buttonX, float buttonY, int count, float viewportWidth, float viewportHeight)
    {
        int height = count * ButtonHeight;
        foreach (var point in new[] { (buttonX + ButtonWidth, buttonY), (buttonX + ButtonWidth, buttonY + ButtonHeight - height),
            (buttonX - ButtonWidth, buttonY), (buttonX - ButtonWidth, buttonY + ButtonHeight - height) })
            if (point.Item1 >= 0 && point.Item1 + ButtonWidth <= viewportWidth && point.Item2 >= 0 && point.Item2 + height <= viewportHeight) return point;
        throw new ArgumentOutOfRangeException(nameof(count), "Command column does not fit the gameplay viewport.");
    }

    public static (float X, float Y) Illustration(float primaryX, float primaryY, float viewportWidth)
    {
        float x = primaryX + 144;
        if (x + 188 > viewportWidth) x = primaryX + PrimaryWidth - 332 + (primaryX + PrimaryWidth + 58 <= viewportWidth ? 58 : 0);
        return (x, primaryY);
    }
}

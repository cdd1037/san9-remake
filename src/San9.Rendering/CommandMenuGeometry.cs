namespace San9.Rendering;

/// <summary>Logical 1024×768 viewport. RE-SAN9PK-0073: placement excludes the bottom message band.</summary>
public static class CommandMenuGeometry
{
    public const int ButtonWidth = 68, ButtonHeight = 28, PrimaryWidth = 90;
    public static (float X, float Y) Primary(float clickX, float clickY, int count) =>
        (Math.Clamp(clickX - 13, 268, 1024 - PrimaryWidth), Math.Clamp(clickY - 18, 0, 736 - Math.Max(278, 32 + count * ButtonHeight)));

    public static (float X, float Y) Secondary(float buttonX, float buttonY, int count)
    {
        int height = count * ButtonHeight;
        foreach (var point in new[] { (buttonX + ButtonWidth, buttonY), (buttonX + ButtonWidth, buttonY + ButtonHeight - height),
            (buttonX - ButtonWidth, buttonY), (buttonX - ButtonWidth, buttonY + ButtonHeight - height) })
            if (point.Item1 >= 0 && point.Item1 + ButtonWidth <= 1024 && point.Item2 >= 0 && point.Item2 + height <= 736) return point;
        throw new ArgumentOutOfRangeException(nameof(count), "Command column does not fit the gameplay viewport.");
    }

    public static (float X, float Y) Illustration(float primaryX, float primaryY)
    {
        float x = primaryX + 144;
        if (x + 188 > 1024) x = primaryX + PrimaryWidth - 332 + (primaryX + PrimaryWidth + 58 <= 1024 ? 58 : 0);
        return (x, primaryY);
    }
}

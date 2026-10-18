namespace San9.Rendering;

/// <summary>Clip native health art without stretching its gradient or retaining a pixel at zero health.</summary>
public static class DuelInterfacePlayback
{
    public static int HealthWidth(int fullWidth, double hitPoints) => hitPoints <= 0 ? 0 :
        Math.Clamp((int)Math.Floor(fullWidth * hitPoints / 100), 1, fullWidth);
}

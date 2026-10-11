namespace San9.Rendering;

/// <summary>Deterministic stepped opacity, sampled from the first fade step.</summary>
public static class BattleEffectFade
{
    public const double Duration = .7;
    public static float Opacity(double elapsed) => elapsed < 0 ? 1 :
        Math.Max(0, 255 - ((int)Math.Floor(elapsed * 10 + 1e-6) + 1) * 32) / 255f;
}

namespace San9.Rendering;

/// <summary>Time-based linear opacity for scene overlays and individual illustrations.</summary>
public static class SceneOpacity
{
    public static float Reveal(double elapsed, double duration) => 1 - Darken(elapsed, duration);
    public static float Darken(double elapsed, double duration) => duration <= 0 ? 1 : (float)Math.Clamp(elapsed / duration, 0, 1);
}

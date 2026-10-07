namespace San9.Rendering;

public static class FlagVisuals
{
    public static int GlyphInk(int color) => color is 3 or 6 or 7 or 8 or 11 or 16 or 18 or 21 or 22 or 23 or 24 or
        26 or 27 or 29 or 31 or 33 or 38 or 39 or 40 or 46 or 49 or 50 ? 1 : 0;

    public static IEnumerable<string> Layers(int direction, int color, int glyph)
    {
        yield return $"unit-flag-{direction}-pole-back";
        yield return $"unit-flag-{direction}-cloth-{color}";
        if (glyph >= 0) yield return $"unit-flag-{direction}-glyph-{GlyphInk(color)}-{glyph}";
        yield return $"unit-flag-{direction}-pole-front";
    }
}

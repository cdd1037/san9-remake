namespace San9.Content.Images;

public static class TextureGroups
{
    public static bool IsPortrait(string role) => role.StartsWith("information-person-", StringComparison.Ordinal) ||
        role.StartsWith("catalog-person-", StringComparison.Ordinal) || role.StartsWith("information-treasure-", StringComparison.Ordinal) ||
        role.StartsWith("detail-person-", StringComparison.Ordinal) || role.StartsWith("detail-treasure-portrait-", StringComparison.Ordinal);

    public static string Interface(string role) => IsPortrait(role) ? "portrait" :
        role.StartsWith("detail-", StringComparison.Ordinal) ? "detail" :
        role.StartsWith("catalog-", StringComparison.Ordinal) ? "catalog" :
        role.StartsWith("information-", StringComparison.Ordinal) ? "information" :
        role.StartsWith("overview-", StringComparison.Ordinal) || role.StartsWith("button-overview-", StringComparison.Ordinal) ? "overview" : "hud";
    // Keep complete seasonal clips together, but bound unrelated glyph/color residency.
    public static string MapAnimation(string clip, int frameSet)
    {
        string[] parts = clip.Split('-');
        string family = parts[0] switch
        {
            "water" => "water",
            "soldier" => "guards",
            "flag" when parts[1] == "pole" => "flags/poles",
            "flag" when parts[1] == "cloth" => "flags/cloth/" + (int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture) / 16).ToString("D2", System.Globalization.CultureInfo.InvariantCulture),
            "flag" when parts[1] == "glyph" => "flags/glyphs/" + parts[2] + "/" + (int.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture) / 32).ToString("D2", System.Globalization.CultureInfo.InvariantCulture),
            _ => throw new InvalidDataException($"Unknown map animation family: {clip}")
        };
        return $"map/animations/{family}/set-{frameSet}";
    }
}

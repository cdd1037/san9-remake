namespace San9.Content.Images;

public static class TextureGroups
{
    public static bool IsPortrait(string role) => role.StartsWith("information-person-", StringComparison.Ordinal) ||
        role.StartsWith("catalog-person-", StringComparison.Ordinal) || role.StartsWith("information-treasure-", StringComparison.Ordinal) ||
        role.StartsWith("detail-person-", StringComparison.Ordinal) || role.StartsWith("detail-treasure-portrait-", StringComparison.Ordinal);

    public static string Interface(string role) => IsPortrait(role) ? "portrait" :
        role.StartsWith("command-", StringComparison.Ordinal) ? "command" :
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
            "building" when parts[1] == "smoke" => "buildings/smoke",
            "unit" when parts[1] == "idle" => "units/bodies/" + parts[2],
            "unit" when parts[1] == "action" => "units/actions/" + parts[2],
            "unit" when parts[1] == "retirement" => "units/retirement/" + parts[2],
            "unit" when parts[1] is "condition" or "influence" or "wake" or "trail" or "engagement" or "vision" => "units/" + parts[1],
            "rockfall" when parts[1] == "impact" => "projectiles/rockfall-impact",
            "unit" when parts[1] == "flag" && parts[3] == "pole" => "units/flags/" + parts[2] + "/poles",
            "unit" when parts[1] == "flag" && parts[3] == "cloth" => "units/flags/" + parts[2] + "/cloth/" + (int.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture) / 16).ToString("D2", System.Globalization.CultureInfo.InvariantCulture),
            "unit" when parts[1] == "flag" && parts[3] == "glyph" => "units/flags/" + parts[2] + "/glyphs/" + parts[4] + "/" + (int.Parse(parts[5], System.Globalization.CultureInfo.InvariantCulture) / 32).ToString("D2", System.Globalization.CultureInfo.InvariantCulture),
            "flag" when parts[1] == "pole" => "flags/poles",
            "flag" when parts[1] == "cloth" => "flags/cloth/" + (int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture) / 16).ToString("D2", System.Globalization.CultureInfo.InvariantCulture),
            "flag" when parts[1] == "glyph" => "flags/glyphs/" + parts[2] + "/" + (int.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture) / 32).ToString("D2", System.Globalization.CultureInfo.InvariantCulture),
            _ => throw new InvalidDataException($"Unknown map animation family: {clip}")
        };
        return $"map/animations/{family}/set-{frameSet}";
    }
}

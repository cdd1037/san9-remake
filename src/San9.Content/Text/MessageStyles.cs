namespace San9.Content.Text;

/// <summary>A null foreground inherits the enclosing text color.</summary>
public sealed record MessageStylePreset(string? Foreground, string Outline);

/// <summary>Finite message styles. Source palette indices belong to the importer.</summary>
public static class MessageStyles
{
    public const string Close = "[/preset]";
    public const string TagPattern = @"\[preset=(?<id>[^\]]*)\]|\[/preset\]";

    public static void ValidateMarkup(string markup)
    {
        int depth = 0;
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(markup, TagPattern))
        {
            if (match.Groups["id"].Success)
            {
                _ = Get(match.Groups["id"].Value);
                if (++depth >= 64) throw new InvalidDataException("Message preset nesting exceeds limit.");
            }
            else if (depth-- == 0) throw new InvalidDataException("Unbalanced message preset.");
        }
        if (depth != 0) throw new InvalidDataException("Unclosed message preset.");
    }

    public static MessageStylePreset Get(string preset) => preset switch
    {
        "blue-outline" => new(null, "98B0F8"),
        "green-outline" => new(null, "88F880"),
        "peach-outline" => new(null, "F0B080"),
        "yellow-outline" => new(null, "F8F8B0"),
        "blue" => new("7EBAC2", "000000"),
        "green" => new("A4D168", "000000"),
        "peach" => new("F0B080", "000000"),
        "yellow" => new("F8F8B0", "000000"),
        _ => throw new InvalidDataException($"Unknown message style preset: {preset}.")
    };

    public static string Open(string preset)
    {
        _ = Get(preset);
        return $"[preset={preset}]";
    }
}

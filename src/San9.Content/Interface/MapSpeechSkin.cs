namespace San9.Content.Interface;

public enum MapSpeechPlacement { LeftBelow, LeftAbove, RightAbove, RightBelow }

public static class MapSpeechSkin
{
    public static string Role(MapSpeechPlacement placement, string part) => $"map-speech-{placement.ToString().ToLowerInvariant()}-{part}";
    public static IEnumerable<string> RequiredRoles => Enum.GetValues<MapSpeechPlacement>()
        .SelectMany(placement => new[] { "left", "middle", "right" }.Select(part => Role(placement, part)));
}

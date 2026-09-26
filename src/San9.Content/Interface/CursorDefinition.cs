namespace San9.Content.Interface;

public sealed record CursorDefinition(string Asset, int HotspotX, int HotspotY)
{
    public static IReadOnlyList<string> EdgeScrollRoles { get; } = Array.AsReadOnly(
        new[] { "up", "right", "down", "left", "up-right", "down-right", "down-left", "up-left" }
            .SelectMany(direction => new[] { $"scroll-{direction}-idle", $"scroll-{direction}-active" }).ToArray());
}

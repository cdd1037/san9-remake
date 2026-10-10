namespace San9.Content.Interface;

public sealed record TargetMarkerOffset(int X, int Y);

public static class TargetSelectionSkin
{
    public static IReadOnlyList<string> RequiredRoles { get; } = new[] { "target-title-left", "target-title-middle", "target-title-right", "target-footer" }
        .Concat(new[] { "list", "cancel" }.SelectMany(b => Enumerable.Range(0, 4).Select(f => $"target-{b}-{f}")))
        .Concat(new[] { "enter", "attack", "hostile", "friendly" }.SelectMany(b => Enumerable.Range(0, 4).Select(f => $"target-marker-{b}-{f}")))
        .Concat(Enumerable.Range(0, 8).Select(f => $"target-route-{f}"))
        .Concat(new[] { "spring-summer", "autumn-winter" }.SelectMany(s => Enumerable.Range(0, 4).Select(f => $"target-waypoint-{s}-{f}"))).ToArray();
}

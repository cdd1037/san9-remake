namespace San9.Content.Interface;

public static class OfficerDetailSkin
{
    public static IReadOnlyList<string> RequiredRoles { get; } = Array.AsReadOnly(new[]
    {
        "detail-unit-list-top", "detail-unit-list-bottom",
        "detail-treasure-list-top", "detail-treasure-list-bottom", "detail-treasure-description",
        "detail-leader-background", "detail-leader-background-1", "detail-leader-radar-grid",
        "detail-leader-label-left", "detail-leader-label-middle", "detail-leader-label-right",
        "detail-admin-label-left", "detail-admin-label-middle", "detail-admin-label-right",
        "detail-status-badge", "detail-training-badge",
        "detail-background", "detail-list-top", "detail-list-bottom", "detail-portrait-frame", "detail-radar-background", "detail-radar-grid",
        "detail-panel-center", "detail-button-shadow", "detail-biography-field", "detail-tactics-field"
    }.Concat(new[] { "top-left", "top", "top-right", "left", "right", "bottom-left", "bottom", "bottom-right" }.Select(p => "detail-panel-" + p))
    .Concat(new[] { "label", "field", "title", "name", "tactic-category" }.SelectMany(p => new[] { "left", "middle", "right" }.Select(s => "detail-" + p + "-" + s)))
    .Concat(new[] { "tab", "treasure", "close", "close-icon", "help" }.SelectMany(p => Enumerable.Range(0, 4).Select(i => "detail-" + p + (i == 0 ? "" : $"-{i}"))))
    .Concat(Enumerable.Range(0, 9).SelectMany(i => new[] { $"detail-tactic-{i}", $"detail-tactic-{i}-1" })).ToArray());
}

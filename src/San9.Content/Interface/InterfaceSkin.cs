using San9.Content.Images;

namespace San9.Content.Interface;

public sealed record InterfaceSkin(int SchemaVersion, ImageAsset[] Assets, Dictionary<string,string> Roles, Dictionary<string,string> Colors,
    Dictionary<string, CursorDefinition> Cursors)
{
    private static readonly string[] BaseUiAssets = ["information-menu-button-left", "information-menu-button-middle", "information-menu-button-right", "top-frame", "radar-frame", "sidebar-frame", "roster-scroll-decoration",
        "scroll-track-top", "scroll-track-middle", "scroll-track-bottom",
        "scroll-thumb-top", "scroll-thumb-middle", "scroll-thumb-bottom", "button-scroll-up", "button-scroll-down",
        "field-left", "field-middle", "field-right", "ruler-frame", "region-frame",
        "message-left", "message-middle", "message-right", "button-pause", "turn-progress-frame", "turn-progress-fill",
        "roster-row-left", "roster-row-middle", "roster-row-right",
        "bar-morale-frame", "bar-morale-normal", "bar-morale-warning", "bar-morale-critical",
        "bar-durability-frame", "bar-durability-normal", "bar-durability-warning", "bar-durability-critical",
        "icon-merchant", "icon-training", "icon-emperor",
        "season-spring", "season-summer", "season-autumn", "season-winter",
        "button-territory", "button-facilities", "button-officers", "button-units",
        "button-information", "button-functions", "button-help", "button-map-mode", "button-advance",
        "overview-background", "overview-terrain", "overview-borders", "overview-ranking",
        "button-overview-forces", "button-overview-facilities", "button-overview-units"];
    public static IReadOnlyList<string> RequiredUiAssets { get; } = Array.AsReadOnly(BaseUiAssets.SelectMany(id =>
        (id.StartsWith("button-", StringComparison.Ordinal) || id.StartsWith("roster-row-", StringComparison.Ordinal) || id.StartsWith("information-menu-button-", StringComparison.Ordinal))
            ? Enumerable.Range(0, id == "button-territory" ? 6 : 4).Select(frame => frame == 0 ? id : $"{id}-{frame}")
            : [id]).Concat(Enumerable.Range(0, 50).Select(i => $"overview-city-{i}"))
        .Concat(Enumerable.Range(0, 51).SelectMany(i => new[] {$"overview-marker-{i}", $"overview-facility-{i}"}))
        .Concat(Enumerable.Range(0, 50).Select(i => $"overview-unit-{i}"))
        .Concat(new[] { "top-left", "top", "top-right", "left", "center", "right", "bottom-left", "bottom", "bottom-right" }.Select(p => "information-menu-" + p))
        .Concat(new[] { "top-left", "top", "top-right", "left", "right", "bottom-left", "bottom", "bottom-right", "ornament" }.Select(p => "information-frame-" + p))
        .Concat(new[] { "left", "middle", "right" }.SelectMany(p => new[] { "information-title-" + p, "information-footer-" + p }))
        .Concat(Enumerable.Range(0, 51).SelectMany(c => Enumerable.Range(0, 4).Select(f => $"information-map-force-{c}" + (f == 0 ? "" : $"-{f}"))))
        .Concat(Enumerable.Range(0, 4).Select(f => "information-map-city" + (f == 0 ? "" : $"-{f}")))
        .Concat(new[] { "left", "middle", "right", "close", "map-left", "map-middle", "map-right" }.SelectMany(p => Enumerable.Range(0, 4).Select(f => "information-action-" + p + (f == 0 ? "" : $"-{f}"))))
        .Concat(new[] { "catalog-header-left", "catalog-header-middle", "catalog-header-right", "catalog-row-left", "catalog-row-middle", "catalog-row-right", "catalog-help", "catalog-scroll-up", "catalog-scroll-down" }
            .SelectMany(p => Enumerable.Range(0,4).Select(f => p + (f == 0 ? "" : $"-{f}"))))
        .Concat(Enumerable.Range(0,6).Select(f => "catalog-tab" + (f == 0 ? "" : $"-{f}")))
        .Concat(new[] { "top-left", "top", "top-right", "bottom-left", "bottom", "bottom-right" }.Select(p => "catalog-frame-" + p))
        .Concat(new[] { "catalog-sort-up", "catalog-sort-down", "catalog-portrait-frame" })
        .Concat(new[] { "track", "thumb" }.SelectMany(p => new[] { "top", "middle", "bottom" }.Select(s => "catalog-scroll-" + p + "-" + s)))
        .Concat(new[] { "top-left", "top", "top-right", "left", "right", "bottom-left", "bottom", "bottom-right" }.Select(p => "catalog-tabs-" + p))
        .Concat(new[] { "own", "other", "friendly" }.SelectMany(t =>
            new[] { "city", "facility" }.Select(p => $"hover-{t}-{p}")))
        .Concat(CommandMenuSkin.RequiredRoles).Append("hover-divider").Concat(OfficerDetailSkin.RequiredRoles).Append("information-background")
        .Concat(new[] { "strategy", "execution" }.SelectMany(p => Enumerable.Range(0, 4).Select(f => $"turn-title-{p}-{f}")))
        .Concat(new[] { "yes", "no" }.SelectMany(p => Enumerable.Range(0, 4).Select(f => $"dialog-{p}-{f}")))
        .Append("dialog-frame")
        .Concat(new[] { "arrow-up-right", "arrow-down-right", "crossed-arrows", "empty-square", "question", "turn-arrow", "book", "waves", "circle" }
            .SelectMany(id => new[] { $"text-icon-{id}-16", $"text-icon-{id}-24" })).Append("text-icon-city-16").ToArray());

    public void Validate()
    {
        if (SchemaVersion != 25 || Assets.Select(a => a.Id).Distinct().Count() != Assets.Length)
            throw new InvalidDataException("Invalid interface skin version or duplicate assets.");
        ImageAssetFiles.ValidateCatalog(Assets);
        var assets = Assets.ToDictionary(a => a.Id);
        if (Cursors is null || CursorDefinition.EdgeScrollRoles.Any(role => !Cursors.ContainsKey(role)) ||
            Cursors.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value is null ||
                !assets.TryGetValue(p.Value.Asset, out var image) || image.Width > 128 || image.Height > 128 ||
                p.Value.HotspotX < 0 || p.Value.HotspotX >= image.Width ||
                p.Value.HotspotY < 0 || p.Value.HotspotY >= image.Height))
            throw new InvalidDataException("Invalid interface cursors or hotspots.");
        if (Enumerable.Range(0, 52).Any(i => !Colors.ContainsKey($"city-marker-{i}")) ||
            Enumerable.Range(0, 51).Any(i => !Colors.ContainsKey($"overview-force-{i}")) ||
            !Colors.ContainsKey("roster-available") || !Colors.ContainsKey("roster-unavailable") ||
            Colors.Values.Any(c => c.Length != 6 || !uint.TryParse(c, System.Globalization.NumberStyles.HexNumber, null, out _)))
            throw new InvalidDataException("Missing or invalid interface colors.");
        if (RequiredUiAssets.Any(id => !Roles.TryGetValue(id, out var asset) || !assets.ContainsKey(asset)) ||
            Roles.Values.Any(id => !assets.ContainsKey(id)) ||
            Assets.Any(a => a.Width <= 0 || a.Height <= 0))
            throw new InvalidDataException("Invalid interface skin assets.");
    }
}

using San9.Content.Images;

namespace San9.Content.Interface;

public sealed record InterfaceSkin(int SchemaVersion, ImageAsset[] Assets, Dictionary<string,string> Roles, Dictionary<string,string> Colors)
{
    private static readonly string[] BaseUiAssets = ["top-frame", "radar-frame", "sidebar-frame", "roster-scroll-decoration",
        "scroll-track-top", "scroll-track-middle", "scroll-track-bottom",
        "scroll-thumb-top", "scroll-thumb-middle", "scroll-thumb-bottom", "button-scroll-up", "button-scroll-down",
        "field-left", "field-middle", "field-right", "ruler-frame", "region-frame",
        "message-left", "message-middle", "message-right", "city-prefix",
        "roster-row-left", "roster-row-middle", "roster-row-right",
        "bar-morale-frame", "bar-morale-normal", "bar-morale-warning", "bar-morale-critical",
        "bar-durability-frame", "bar-durability-normal", "bar-durability-warning", "bar-durability-critical",
        "icon-merchant", "icon-training", "icon-emperor",
        "season-spring", "season-summer", "season-autumn", "season-winter",
        "button-territory", "button-facilities", "button-officers", "button-units",
        "button-information", "button-functions", "button-help", "button-zoom", "button-advance"];
    public static IReadOnlyList<string> RequiredUiAssets { get; } = Array.AsReadOnly(BaseUiAssets.SelectMany(id =>
        (id.StartsWith("button-", StringComparison.Ordinal) || id.StartsWith("roster-row-", StringComparison.Ordinal))
            ? Enumerable.Range(0, id == "button-territory" ? 6 : 4).Select(frame => frame == 0 ? id : $"{id}-{frame}")
            : [id]).ToArray());

    public void Validate()
    {
        if (SchemaVersion != 6 || Assets.Select(a => a.Id).Distinct().Count() != Assets.Length)
            throw new InvalidDataException("Invalid interface skin version or duplicate assets.");
        var assets = Assets.ToDictionary(a => a.Id);
        if (Enumerable.Range(0, 52).Any(i => !Colors.ContainsKey($"city-marker-{i}")) ||
            !Colors.ContainsKey("roster-available") || !Colors.ContainsKey("roster-unavailable") ||
            Colors.Values.Any(c => c.Length != 6 || !uint.TryParse(c, System.Globalization.NumberStyles.HexNumber, null, out _)))
            throw new InvalidDataException("Missing or invalid interface colors.");
        if (RequiredUiAssets.Any(id => !Roles.TryGetValue(id, out var asset) || !assets.ContainsKey(asset)) ||
            Roles.Values.Any(id => !assets.ContainsKey(id)) ||
            Assets.Any(a => a.Width <= 0 || a.Height <= 0 || a.Sha256.Length != 64))
            throw new InvalidDataException("Invalid interface skin assets.");
    }
}

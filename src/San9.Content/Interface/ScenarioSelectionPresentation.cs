using San9.Content.Images;

namespace San9.Content.Interface;

public sealed record ScenarioPreviewPoint(int X, int Y);
public sealed record ScenarioPreviewCategory(string Id, string Label, string Hint, ScenarioPreviewPoint Position);
public sealed record ScenarioPreviewEntry(string Id, string CategoryId, int Year, int Month, string Title,
    string Description, Dictionary<int, string> CityColors);
public sealed record ScenarioPreviewInset(StartMenuSprite Sprite, int CityId);

/// <summary>Pre-session scenario descriptions and preview ownership, independent of playable world packages.</summary>
public sealed record ScenarioSelectionPresentation(string Title, string Prompt, string BackLabel,
    StartMenuSprite[] Foreground, OpeningMapDefinition Map,
    StartMenuSprite DescriptionPanel, StartMenuSprite CategoryBar, string CategoryHighlight,
    StartMenuSprite PreviewHeading, StartMenuSprite Back, StartMenuSprite[] Rows,
    ScenarioPreviewCategory[] Categories, ScenarioPreviewEntry[] Entries)
{
    [System.Text.Json.Serialization.JsonRequired] public ForceSelectionPresentation Forces { get; init; } = null!;
    [System.Text.Json.Serialization.JsonRequired] public OpeningMapPreview Preview { get; init; } = null!;
    [System.Text.Json.Serialization.JsonRequired] public OpeningSettingsPresentation Settings { get; init; } = null!;
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<string> Assets => Foreground.Append(DescriptionPanel)
        .Append(CategoryBar).Append(PreviewHeading).Append(Back).Concat(Rows).Select(s => s.Asset)
        .Append(CategoryHighlight).Concat(Map.Assets).Concat(Forces.Assets).Concat(Settings.Assets)
        .Concat([Preview.Background.Asset, Preview.Inset.Asset]).Distinct();

    public void Validate(IEnumerable<ImageAsset> images)
    {
        var catalog = images.DistinctBy(i => i.Id).ToDictionary(i => i.Id);
        Map.Validate(catalog);
        Settings.Validate(catalog);
        Forces.Validate(catalog);
        var categories = Categories.Select(c => c.Id).ToHashSet();
        if (new[] { Preview.ScaleX, Preview.ScaleY, Preview.OffsetX, Preview.OffsetY }.Any(v => !double.IsFinite(v)) ||
            Preview.ScaleX <= 0 || Preview.ScaleY <= 0 ||
            string.IsNullOrWhiteSpace(Title) || string.IsNullOrWhiteSpace(Prompt) || string.IsNullOrWhiteSpace(BackLabel) ||
            Categories.Length == 0 || categories.Count != Categories.Length ||
            Categories.Any(c => string.IsNullOrWhiteSpace(c.Id) || string.IsNullOrWhiteSpace(c.Label) || string.IsNullOrWhiteSpace(c.Hint)) ||
            Entries.Length == 0 || Entries.Select(e => e.Id).Distinct().Count() != Entries.Length ||
            Entries.Any(e => string.IsNullOrWhiteSpace(e.Id) || !categories.Contains(e.CategoryId) ||
                e.Year < 1 || e.Month is < 1 or > 12 || string.IsNullOrWhiteSpace(e.Title) || string.IsNullOrWhiteSpace(e.Description) ||
                e.CityColors.Any(c => !Map.Cities.ContainsKey(c.Key) || !Map.Colors.ContainsKey(c.Value))) ||
            Categories.Any(c => !Entries.Any(e => e.CategoryId == c.Id)) ||
            Assets.Any(a => !catalog.ContainsKey(a)) ||
            Rows.Length == 0 || Rows.Any(r => r.Columns != 4 || catalog[r.Asset].Width % r.Columns != 0) ||
            Back.Columns != 4 || catalog[Back.Asset].Width % Back.Columns != 0)
            throw new InvalidDataException("Invalid scenario selection presentation.");
    }
}

public sealed record ForceSelectionPresentation(StartMenuSprite[] Foreground, StartMenuSprite Heading,
    StartMenuSprite PortraitSlots, StartMenuSprite Choose, StartMenuSprite Confirm, StartMenuSprite Back)
{
    public static IReadOnlyList<string> RequiredSkinRoles { get; } = Array.AsReadOnly(new[]
    {
        "background", "panel", "frame-top-left", "frame-top-right", "frame-bottom-left", "frame-bottom-right",
        "top", "bottom", "left", "right", "title-left", "title-middle", "title-right", "footer-left", "footer-middle", "footer-right"
    }.Select(role => "opening-" + role).ToArray());
    [System.Text.Json.Serialization.JsonRequired] public Dictionary<string, ImageAsset> MapSkin { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<string> Assets => Foreground.Concat([Heading, PortraitSlots, Choose, Confirm, Back]).Select(s => s.Asset);

    public void Validate(IReadOnlyDictionary<string, ImageAsset> images)
    {
        if (Foreground.Length == 0 || Assets.Any(a => !images.ContainsKey(a)) ||
            new[] { Choose, Confirm, Back }.Any(s => s.Columns != 4 || images[s.Asset].Width % 4 != 0) ||
            RequiredSkinRoles.Any(role => !MapSkin.ContainsKey(role)))
            throw new InvalidDataException("Invalid force selection presentation.");
        var files = images.Values.Select(a => a.File).ToHashSet();
        foreach (var asset in MapSkin.Values)
        {
            asset.Validate();
            if (!files.Contains(asset.File)) throw new InvalidDataException("Unknown opening skin image file.");
        }
    }
}

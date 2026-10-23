using San9.Content.Images;

namespace San9.Content.Interface;

public sealed record ScenarioPreviewPoint(int X, int Y);
public sealed record ScenarioPreviewCategory(string Id, string Label, string Hint, ScenarioPreviewPoint Position);
public sealed record ScenarioPreviewEntry(string Id, string CategoryId, int Year, int Month, string Title,
    string Description, Dictionary<int, string> CityColors);
public sealed record ScenarioPreviewInset(StartMenuSprite Sprite, int CityId);

/// <summary>Pre-session scenario descriptions and preview ownership, independent of playable world packages.</summary>
public sealed record ScenarioSelectionPresentation(string Title, string Prompt, string BackLabel,
    StartMenuSprite[] Foreground, StartMenuSprite Map, ScenarioPreviewInset Inset,
    StartMenuSprite DescriptionPanel, StartMenuSprite CategoryBar, string CategoryHighlight,
    StartMenuSprite PreviewHeading, StartMenuSprite Back, StartMenuSprite[] Rows,
    Dictionary<int, ScenarioPreviewPoint> CityPositions, Dictionary<string, string> MarkerAssets,
    ScenarioPreviewCategory[] Categories, ScenarioPreviewEntry[] Entries)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<string> Assets => Foreground.Append(Map).Append(Inset.Sprite).Append(DescriptionPanel)
        .Append(CategoryBar).Append(PreviewHeading).Append(Back).Concat(Rows).Select(s => s.Asset)
        .Append(CategoryHighlight).Concat(MarkerAssets.Values).Distinct();

    public void Validate(IEnumerable<ImageAsset> images)
    {
        var catalog = images.DistinctBy(i => i.Id).ToDictionary(i => i.Id);
        var categories = Categories.Select(c => c.Id).ToHashSet();
        if (string.IsNullOrWhiteSpace(Title) || string.IsNullOrWhiteSpace(Prompt) || string.IsNullOrWhiteSpace(BackLabel) ||
            Categories.Length == 0 || categories.Count != Categories.Length ||
            Categories.Any(c => string.IsNullOrWhiteSpace(c.Id) || string.IsNullOrWhiteSpace(c.Label) || string.IsNullOrWhiteSpace(c.Hint)) ||
            Entries.Length == 0 || Entries.Select(e => e.Id).Distinct().Count() != Entries.Length ||
            Entries.Any(e => string.IsNullOrWhiteSpace(e.Id) || !categories.Contains(e.CategoryId) ||
                e.Year < 1 || e.Month is < 1 or > 12 || string.IsNullOrWhiteSpace(e.Title) || string.IsNullOrWhiteSpace(e.Description) ||
                e.CityColors.Any(c => !CityPositions.ContainsKey(c.Key) || !MarkerAssets.ContainsKey(c.Value))) ||
            Categories.Any(c => !Entries.Any(e => e.CategoryId == c.Id)) ||
            !CityPositions.ContainsKey(Inset.CityId) || Assets.Any(a => !catalog.ContainsKey(a)) ||
            Rows.Length == 0 || Rows.Any(r => r.Columns != 4 || catalog[r.Asset].Width % r.Columns != 0) ||
            Back.Columns != 4 || catalog[Back.Asset].Width % Back.Columns != 0)
            throw new InvalidDataException("Invalid scenario selection presentation.");
    }
}

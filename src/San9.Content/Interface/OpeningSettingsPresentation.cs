using San9.Content.Images;

namespace San9.Content.Interface;

/// <summary>Opening settings artwork, independent of the source archive layout.</summary>
public sealed record OpeningSettingsPresentation(StartMenuSprite[] Foreground, StartMenuSprite[] Bars,
    string ChoiceHighlight, StartMenuAnimation Illustration, StartMenuSprite Combat,
    StartMenuSprite Confirm, StartMenuSprite Back, StartMenuSprite CombatDialog)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<string> Assets => Foreground.Concat(Bars).Concat([Combat, Confirm, Back, CombatDialog])
        .Select(s => s.Asset).Append(ChoiceHighlight).Concat(Illustration.Frames).Distinct();

    public void Validate(IReadOnlyDictionary<string, ImageAsset> images)
    {
        if (Foreground.Length == 0 || Bars.Length != 2 || Assets.Any(a => !images.ContainsKey(a)) ||
            Illustration.Frames.Length == 0 || Illustration.Columns <= 0 ||
            !double.IsFinite(Illustration.FrameSeconds) || Illustration.FrameSeconds <= 0 ||
            !float.IsFinite(Illustration.Opacity) || Illustration.Opacity is < 0 or > 1 ||
            Illustration.Frames.Any(a => images[a].Width % Illustration.Columns != 0) ||
            new[] { Combat, Confirm, Back }.Any(s => s.Columns != 4 || images[s.Asset].Width % 4 != 0))
            throw new InvalidDataException("Invalid opening settings presentation.");
    }
}

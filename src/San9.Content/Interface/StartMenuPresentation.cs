using San9.Content.Images;

namespace San9.Content.Interface;

public sealed record StartMenuSprite(string Asset, int X, int Y, int Columns = 1);
public sealed record StartMenuAnimation(string[] Frames, int Columns, int X, int Y, double FrameSeconds, float Opacity = 1);
public sealed record StartMenuEntry(string Id, string Label, string Hint, StartMenuSprite Button,
    StartMenuAnimation Illustration, StartMenuAnimation[] Glints);

/// <summary>Standalone opening menu art and text, independent of session creation and commands.</summary>
public sealed record StartMenuPresentation(StartMenuSprite[] Background, StartMenuAnimation[] Ambience,
    StartMenuSprite[] Foreground, StartMenuSprite Help, StartMenuSprite Close, string Prompt,
    StartMenuEntry[] Entries)
{
    [System.Text.Json.Serialization.JsonRequired] public string QuitPrompt { get; init; } = "";
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<string> Assets => Sprites.Select(s => s.Asset).Distinct();
    private IEnumerable<StartMenuSprite> Sprites => Background.Concat(Foreground)
        .Append(Help).Append(Close).Concat(Entries.Select(e => e.Button))
        .Concat(Animations.SelectMany(a => a.Frames.Select(f => new StartMenuSprite(f, a.X, a.Y, a.Columns))));
    private IEnumerable<StartMenuAnimation> Animations => Ambience.Concat(Entries.SelectMany(e => e.Glints.Append(e.Illustration)));

    public void Validate(IEnumerable<ImageAsset> images)
    {
        var catalog = images.DistinctBy(i => i.Id).ToDictionary(i => i.Id);
        var animations = Animations;
        if (Entries.Length == 0 || Entries.Select(e => e.Id).Distinct().Count() != Entries.Length ||
            string.IsNullOrWhiteSpace(QuitPrompt) || string.IsNullOrWhiteSpace(Prompt) || Entries.Any(e => string.IsNullOrWhiteSpace(e.Id) ||
                string.IsNullOrWhiteSpace(e.Label) || string.IsNullOrWhiteSpace(e.Hint) || e.Button.Columns != 4) ||
            Help.Columns != 4 || Close.Columns != 4 ||
            Sprites.Any(s => !catalog.TryGetValue(s.Asset, out var image) || s.Columns <= 0 || image.Width % s.Columns != 0) ||
            animations.Any(a => a.Frames.Length == 0 || !double.IsFinite(a.FrameSeconds) || a.FrameSeconds <= 0 ||
                !float.IsFinite(a.Opacity) || a.Opacity is < 0 or > 1 ||
                a.Frames.Select(f => (catalog[f].Width, catalog[f].Height)).Distinct().Count() != 1))
            throw new InvalidDataException("Invalid opening menu presentation.");
    }
}

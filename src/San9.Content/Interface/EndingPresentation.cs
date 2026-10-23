using San9.Content.Images;

namespace San9.Content.Interface;

public enum EndingSceneKind { Opening, Map, Inset, SuccessionYear, StableSuccession, UnstableSuccession }
public sealed record EndingSprite(string Asset, int X, int Y, float Opacity = 1);
public sealed record EndingScrollLayer(EndingSprite Sprite, int MinimumX, int MaximumX, int RepeatOffset);
public sealed record EndingStageMarker(EndingSprite Frame, EndingSprite Picture);
public sealed record EndingMapPresentation(string Background, EndingScrollLayer[] Scenery, EndingSprite[] Overlays, EndingStageMarker[] Stages);
public sealed record EndingCue(EndingSceneKind Scene, string? Picture = null, int CompletedStages = 0);
public sealed record EndingTitleLayer(EndingSprite Sprite, int Columns, double FrameSeconds);

/// <summary>Authored ending composition and timing; the world outcome only selects narrative cues.</summary>
public sealed record EndingPresentationDefinition(string Opening, EndingMapPresentation Expansion, EndingMapPresentation Domestic,
    EndingSprite InsetFrame, EndingSprite[] Curtains, string SuccessionYearPicture, string StablePicture, string UnstablePicture,
    Dictionary<string, EndingCue> Cues, int[] ReplacedCountryTitleIds,
    int TextX, int TextY, int TextWidth, double MapFadeSeconds, double ScrollPixelsPerSecond,
    double TextSecondsPerCell, double SuccessionScrollSeconds, double StableHoldSeconds, double UnstableHoldSeconds, double MusicFadeSeconds,
    string Logo, string Title, EndingTitleLayer[] TitleAnimations, EndingSprite Copyright, double LogoSeconds)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<string> TitleAssets => new[] { Logo, Title, Copyright.Asset }.Concat(TitleAnimations.Select(a => a.Sprite.Asset)).Distinct();
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<string> SceneAssets => new[] { Opening, InsetFrame.Asset, SuccessionYearPicture, StablePicture, UnstablePicture }
        .Concat(Curtains.Select(s => s.Asset)).Concat(Cues.Values.Select(c => c.Picture).OfType<string>())
        .Concat(new[] { Expansion, Domestic }.SelectMany(m => new[] { m.Background }
            .Concat(m.Scenery.Select(s => s.Sprite.Asset)).Concat(m.Overlays.Select(s => s.Asset))
            .Concat(m.Stages.SelectMany(s => new[] { s.Frame.Asset, s.Picture.Asset })))).Distinct();
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<string> Assets => SceneAssets.Concat(TitleAssets).Distinct();

    public void Validate(IEnumerable<ImageAsset> images, IEnumerable<string> messageRoles, IEnumerable<int> countryTitles)
    {
        var catalog = images.DistinctBy(i => i.Id).ToDictionary(i => i.Id);
        var ids = catalog.Keys.ToHashSet();
        var sequenceRoles = messageRoles.Where(r => r is "introduction" or "succession-year" or "stable-succession" or "unstable-succession" or "successor-conflict" ||
            r.StartsWith("expansion-", StringComparison.Ordinal) || r.StartsWith("domestic-", StringComparison.Ordinal));
        if (Assets.Any(a => !ids.Contains(a)) || Cues.Keys.Except(messageRoles).Any() ||
            !Cues.Keys.ToHashSet().SetEquals(sequenceRoles) ||
            TitleAnimations.Any(a => a.Columns <= 0 || !double.IsFinite(a.FrameSeconds) || a.FrameSeconds <= 0 ||
                catalog[a.Sprite.Asset].Width % a.Columns != 0) ||
            ReplacedCountryTitleIds.Except(countryTitles).Any() || ReplacedCountryTitleIds.Distinct().Count() != ReplacedCountryTitleIds.Length ||
            TextX < 0 || TextY < 0 || TextWidth <= 0 || TextX + TextWidth > 1024 || TextY + 72 > 768 ||
            new[] { MapFadeSeconds, ScrollPixelsPerSecond, TextSecondsPerCell, SuccessionScrollSeconds, StableHoldSeconds, UnstableHoldSeconds, MusicFadeSeconds, LogoSeconds }
                .Any(n => !double.IsFinite(n) || n <= 0) ||
            Cues.Values.Any(c => !Enum.IsDefined(c.Scene) || c.CompletedStages < 0 ||
                c.CompletedStages > Math.Min(Expansion.Stages.Length, Domestic.Stages.Length) || c.Scene == EndingSceneKind.Inset && c.Picture is null) ||
            new[] { Expansion, Domestic }.Any(m => m.Scenery.Any(s => s.MinimumX >= s.MaximumX || s.RepeatOffset <= 0)) ||
            new[] { Expansion, Domestic }.SelectMany(m => m.Scenery.Select(s => s.Sprite).Concat(m.Overlays)
                .Concat(m.Stages.SelectMany(s => new[] { s.Frame, s.Picture }))).Concat(Curtains).Append(InsetFrame)
                .Concat(TitleAnimations.Select(a => a.Sprite)).Append(Copyright)
                .Any(s => !float.IsFinite(s.Opacity) || s.Opacity is < 0 or > 1))
            throw new InvalidDataException("Invalid ending presentation.");
    }
}

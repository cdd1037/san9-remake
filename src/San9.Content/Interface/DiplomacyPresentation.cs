using San9.Content.Images;

namespace San9.Content.Interface;

public enum DiplomacyActorRole { Ruler, Adviser, Envoy, LeftGuard, RightGuard }
public sealed record DiplomacySprite(string Asset, int Columns, int FrameMilliseconds);
public sealed record DiplomacyLayer(DiplomacySprite Sprite, int X, int Y, int Depth = 0);
public sealed record DiplomacyActorDefinition(DiplomacyActorRole Role, bool Female, bool RulerEnvoy,
    DiplomacyLayer Rest, DiplomacySprite Salute, DiplomacySprite Bow,
    DiplomacyLayer Shadow, DiplomacySprite SaluteShadow, DiplomacySprite BowShadow);

/// <summary>Authored meeting composition, independent of source archives and engine objects.</summary>
public sealed record DiplomacyPresentationDefinition(DiplomacyLayer Floor, DiplomacyLayer Furniture,
    DiplomacyLayer[] Ambience, string TitleLeft, string TitleMiddle, string TitleRight,
    DiplomacyActorDefinition[] Actors, string TintColor, float TintOpacity, string HostTitle, string EnvoyTitle)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<DiplomacySprite> Sprites => new[] { Floor, Furniture }.Concat(Ambience).Select(l => l.Sprite)
        .Concat(Actors.SelectMany(a => new[] { a.Rest.Sprite, a.Salute, a.Bow, a.Shadow.Sprite, a.SaluteShadow, a.BowShadow }));
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<string> Assets => Sprites.Select(s => s.Asset).Concat([TitleLeft, TitleMiddle, TitleRight]).Distinct();

    public void Validate(IEnumerable<ImageAsset> images)
    {
        var catalog = images.DistinctBy(i => i.Id).ToDictionary(i => i.Id);
        if (TintColor.Length != 6 || !uint.TryParse(TintColor, System.Globalization.NumberStyles.HexNumber, null, out _) ||
            !float.IsFinite(TintOpacity) || TintOpacity is < 0 or > 1 ||
            Assets.Any(a => !catalog.ContainsKey(a)) || Sprites.Any(s => s.Columns < 1 || s.FrameMilliseconds < 1 ||
            catalog[s.Asset].Width % s.Columns != 0) ||
            Actors.Select(a => (a.Role, a.Female, a.RulerEnvoy)).Distinct().Count() != Actors.Length ||
            Actors.Any(a => !Enum.IsDefined(a.Role) ||
                !Synchronized(a.Rest.Sprite, a.Shadow.Sprite) ||
                !Synchronized(a.Salute, a.SaluteShadow) || !Synchronized(a.Bow, a.BowShadow)))
            throw new InvalidDataException("Invalid diplomacy scene resources.");
        try
        {
            _ = string.Format(System.Globalization.CultureInfo.InvariantCulture, HostTitle, "force", "place");
            _ = string.Format(System.Globalization.CultureInfo.InvariantCulture, EnvoyTitle, "envoy", "force");
        }
        catch (FormatException error) { throw new InvalidDataException("Invalid diplomacy title template.", error); }
        foreach (var role in Enum.GetValues<DiplomacyActorRole>())
            foreach (bool female in role is DiplomacyActorRole.LeftGuard or DiplomacyActorRole.RightGuard ? new[] { false } : new[] { false, true })
                foreach (bool ruler in role == DiplomacyActorRole.Envoy ? new[] { false, true } : new[] { false })
                    if (!Actors.Any(a => a.Role == role && a.Female == female && a.RulerEnvoy == ruler))
                        throw new InvalidDataException("Incomplete diplomacy scene actors.");
    }

    // A single actor clock selects the same frame for the body and its independently positioned shadow.
    private static bool Synchronized(DiplomacySprite body, DiplomacySprite shadow) =>
        body.Columns == shadow.Columns && body.FrameMilliseconds == shadow.FrameMilliseconds;
}

using San9.Content.Images;
using San9.Content.Text;

namespace San9.Content.Interface;

public enum DuelAction { Idle, Challenge, Return, Victory, Attack, HeavyAttack, Prepare, Parry, Hit, Defeated, EntryPreparation, FinisherPreparation }
public sealed record DuelAnimation(string Asset, int Columns, int FrameMilliseconds)
{
    public bool Additive { get; init; }
    public float Opacity { get; init; } = 1;
    public int[] Frames { get; init; } = [];
    public int[] Intervals { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public double Duration => (Intervals.Length == 0 ? Columns * FrameMilliseconds : Intervals.Sum()) / 1000.0;
}
public sealed record DuelBody(bool Female, bool Right, Dictionary<DuelAction, DuelAnimation> Actions);
public sealed record DuelBackdrop(int[] Terrains, int[] Seasons, string Asset);
public sealed record DuelAudience(bool Right, DuelAnimation Idle, DuelAnimation Cheer, DuelAnimation Defeat, BattlePoint[] Positions);
public sealed record DuelPositions(BattlePoint LeftEntry, BattlePoint RightEntry, BattlePoint LeftCombat, BattlePoint RightCombat, int MovementPixelsPerSecond);
public enum DuelParticleKind { StrongHit, Cheer, Dismay }
public sealed record DuelParticleDefinition(DuelParticleKind Kind, DuelAnimation Animation, BattlePoint[] LeftPositions, BattlePoint[] RightPositions);
public sealed record DuelFlagPositions(BattlePoint[] Left, BattlePoint[] Right);
public sealed record DuelUiRect(int X, int Y, int Width, int Height);
public sealed record DuelPanelLayout(bool Right, string Asset, BattlePoint Position, DuelUiRect Portrait,
    DuelUiRect Name, DuelUiRect WarLabel, DuelUiRect War, DuelUiRect HealthLabel, string HealthAsset, BattlePoint HealthPosition);
public sealed record DuelSpeechLayout(bool Right, string StartAsset, string MiddleAsset, string EndAsset,
    BattlePoint Position, int ReferenceWidth, BattlePoint TextOffset, int MaximumTextWidth, int LineHeight);
public sealed record DuelInterfaceDefinition(BattlePoint BackdropPosition, string TintColor, float TintOpacity,
    DuelPanelLayout[] Panels, DuelSpeechLayout[] Speech)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<string> Assets => Panels.SelectMany(p => new[] { p.Asset, p.HealthAsset })
        .Concat(Speech.SelectMany(s => new[] { s.StartAsset, s.MiddleAsset, s.EndAsset }));
}

/// <summary>Semantic duel resources. All source archive and sound numbers belong to the importer.</summary>
public sealed record DuelPresentationDefinition(Dictionary<string, DecisionMessageDefinition> Messages,
    DuelBody[] Bodies, DuelBackdrop[] Backdrops, string DefaultBackdrop, Dictionary<string, string> Sounds,
    BattleSprite[][] Finishers, BattleSprite[] Resolution)
{
    public DuelAudience[] Audience { get; init; } = [];
    public required DuelFlagPositions Flags { get; init; }
    public required DuelParticleDefinition[] Particles { get; init; }
    public required DuelInterfaceDefinition Interface { get; init; }
    public DuelPositions Positions { get; init; } = new(new(0, 0), new(0, 0), new(0, 0), new(0, 0), 60);
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<string> Assets => Bodies.SelectMany(b => b.Actions.Values).Select(a => a.Asset)
        .Concat(Audience.SelectMany(a => new[] { a.Idle.Asset, a.Cheer.Asset, a.Defeat.Asset }))
        .Concat(Particles.Select(p => p.Animation.Asset))
        .Concat(Interface.Assets)
        .Concat(Backdrops.Select(b => b.Asset)).Append(DefaultBackdrop)
        .Concat(Finishers.SelectMany(f => f).Concat(Resolution).Select(s => s.Asset)).Distinct();

    public void Validate(TextCatalog texts, IReadOnlyDictionary<string, string> sounds, IEnumerable<int> people,
        IReadOnlyDictionary<string, ImageAsset> images)
    {
        string[] roles = ["entry", "answer", "victory", "introduction", "strong", "finisher", "strong-hit", "finisher-hit", "parry", "won", "lost", "death"];
        string[] bindings = ["actor", "opponent", "source-commander", "target-commander", "force"];
        string[] soundRoles = ["ambience", "entry", "entry-prepare", "return", "parry", "defeat", "win", "lose", "prepare", "hit", "heavy-hit", "strong", "cheer", "despair", "finisher", "finisher-effect", "resolution-effect"];
        if (roles.Except(Messages.Keys).Any() || soundRoles.Except(Sounds.Keys).Any() || Sounds.Values.Any(s => !sounds.ContainsKey(s)) ||
            Messages.Values.Any(m => m.Bindings.Values.Except(bindings).Any() || m.Speaker is { } speaker && !bindings.Contains(speaker)))
            throw new InvalidDataException("Invalid duel message or sound roles.");
        new DecisionPresentations([], []) { Shared = Messages }.Validate(texts, sounds, people.ToHashSet());
        if (Bodies.Length != 4 || Bodies.Select(b => (b.Female, b.Right)).Distinct().Count() != 4 ||
            Bodies.Any(b => Enum.GetValues<DuelAction>().Except(b.Actions.Keys).Any()) || Finishers.Length != 8 ||
            Finishers.Any(f => f.Length == 0) || Resolution.Length == 0 || Assets.Any(id => !images.ContainsKey(id)))
            throw new InvalidDataException("Incomplete duel scene resources.");
        if (Audience.Length == 0 || Audience.Any(a => a.Positions.Length == 0) || Positions.MovementPixelsPerSecond <= 0)
            throw new InvalidDataException("Invalid duel audience or positions.");
        if (Flags.Left.Length == 0 || Flags.Right.Length == 0 ||
            !Particles.Select(p => p.Kind).Order().SequenceEqual(Enum.GetValues<DuelParticleKind>().Order()) ||
            Particles.Any(p => p.LeftPositions.Length == 0 || p.RightPositions.Length == 0))
            throw new InvalidDataException("Invalid duel flags or particles.");
        if (Interface.Panels.Length != 2 || Interface.Panels.Select(p => p.Right).Distinct().Count() != 2 ||
            Interface.Speech.Length != 2 || Interface.Speech.Select(p => p.Right).Distinct().Count() != 2 ||
            !float.IsFinite(Interface.TintOpacity) || Interface.TintOpacity is < 0 or > 1 ||
            Interface.TintColor.Length != 6 || Interface.TintColor.Any(c => !Uri.IsHexDigit(c)) ||
            Interface.Panels.Any(p => new[] { p.Portrait, p.Name, p.WarLabel, p.War, p.HealthLabel }.Any(r => r.Width <= 0 || r.Height <= 0)) ||
            Interface.Speech.Any(s => s.MaximumTextWidth <= 0 || s.LineHeight <= 0 || s.ReferenceWidth <= 0))
            throw new InvalidDataException("Invalid duel interface layout.");
        foreach (var animation in Bodies.SelectMany(b => b.Actions.Values).Concat(Audience.SelectMany(a => new[] { a.Idle, a.Cheer, a.Defeat })).Concat(Particles.Select(p => p.Animation)))
            if (animation.Columns <= 0 || animation.FrameMilliseconds <= 0 || !float.IsFinite(animation.Opacity) || animation.Opacity is < 0 or > 1 || images[animation.Asset].Width % animation.Columns != 0 ||
                animation.Frames.Length != animation.Intervals.Length || animation.Frames.Any(f => f < 0 || f >= animation.Columns) || animation.Intervals.Any(t => t <= 0))
                throw new InvalidDataException("Invalid duel animation.");
        foreach (var effect in Finishers.SelectMany(f => f).Concat(Resolution))
            if (effect.Columns <= 0 || effect.FrameMilliseconds <= 0 || images[effect.Asset].Width % effect.Columns != 0 ||
                effect.Sound is { } sound && !sounds.ContainsKey(sound)) throw new InvalidDataException("Invalid duel effect.");
    }
}

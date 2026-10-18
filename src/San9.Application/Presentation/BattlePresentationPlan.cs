using System.Collections.Frozen;
using System.Collections.Immutable;
using San9.Content.Interface;
using San9.Content.Maps;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Rendering;

namespace San9.Application.Presentation;

public enum BattleMessageChannel { MapSpeech, Dialogue, DuelDialogue }
public sealed record BattleMessageCue(string Id, double Time, CommandMessage Message,
    BattleMessageChannel Channel = BattleMessageChannel.MapSpeech)
{
    public string? SceneId { get; init; }
}
public sealed record BattleRecordCue(double Time, MapMessageRecord Record, string? SoundRole = null);
public sealed record BattleVisualCue(string Id, double Start, double End, BattleObjectContext Source,
    BattleObjectContext Target, int TacticId, int ActorId, bool Blocked, bool Doubled,
    uint SoldierLoss, int DurabilityLoss, int MoraleLoss, BattleSprite? Effect, BattleZoomDefinition? Zoom)
{
    public uint SoldierGain { get; init; }
    public bool ScreenEffect { get; init; }
    public double? FadeOutStart { get; init; }
    public double? EntranceStart { get; init; }
    public int MoraleGain { get; init; }
    public BattleZoomPlayback? Playback { get; init; }
    public BattleOfficerPlayback? Officers { get; init; }
    public ImmutableArray<BattleSceneLayer> Backdrop { get; init; } = [];
}
public sealed record BattleValueCue(double Time, int TargetId, bool IsSettlement, long Soldiers, int Morale, int Durability, UnitCondition? Condition = null);
public sealed record BattleMapEffectCue(string Id, double Start, BattleObjectContext Target, BattleSprite Sprite);
public sealed record BattleMapNumberCue(string Id, double Start, BattleObjectContext Target, long Amount);

/// <summary>Occurrence ordered, immutable presentation. Text choices and identities are fixed before playback.</summary>
public sealed class BattlePresentationPlan
{
    public IReadOnlyList<DuelSceneCue> Duels { get; }
    public IReadOnlyList<BattleMessageCue> Messages { get; }
    public IReadOnlyList<BattleRecordCue> Records { get; }
    public IReadOnlyList<BattleVisualCue> Visuals { get; }
    public IReadOnlySet<int> PresentedFacts { get; }
    public double Duration { get; }
    public IReadOnlyList<BattleValueCue> Values { get; }
    public IReadOnlyList<BattleMapEffectCue> MapEffects { get; }
    public IReadOnlyList<BattleMapNumberCue> MapNumbers { get; }
    public BattleMapNumberDefinition? NumberDefinition { get; }
    internal BattlePresentationPlan(List<BattleMessageCue> messages, List<BattleRecordCue> records, List<BattleVisualCue> visuals, List<BattleValueCue> values,
        List<BattleMapEffectCue> effects, List<BattleMapNumberCue> numbers, BattleMapNumberDefinition? numberDefinition, HashSet<int> facts, double duration, List<DuelSceneCue> duels)
    {
        Duels = duels.AsReadOnly();
        Messages = messages.AsReadOnly(); Records = records.AsReadOnly(); Visuals = visuals.AsReadOnly(); Values = values.AsReadOnly();
        MapEffects = effects.AsReadOnly(); MapNumbers = numbers.AsReadOnly(); NumberDefinition = numberDefinition;
        PresentedFacts = facts.ToFrozenSet(); Duration = duration;
    }

    public static BattlePresentationPlan Create(StrategySnapshot before, StrategySnapshot after,
        IReadOnlyList<DomainEvent> events, WorldMessageContext context, TextCatalog texts, CommandPresentation content,
        IReadOnlyDictionary<int, int>? buildingTypes = null, MapPackage? map = null, bool tacticScenesEnabled = true, bool duelScenesEnabled = true) =>
        new BattlePresentationBuilder(before, after, events, context, texts, content, buildingTypes, map, tacticScenesEnabled, duelScenesEnabled).Build();
}

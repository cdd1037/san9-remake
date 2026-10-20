using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

public enum MissionTargetKind { Officer, Settlement, Unit, Force }

/// <summary>A revision-bound, read-only preparation for the public strategy and diplomacy commands.</summary>
public sealed class MissionDraft
{
    private readonly GameSession session;
    private readonly Guid commandId = Guid.NewGuid();
    private readonly Lazy<ImmutableHashSet<int>> inspiration;
    // Eligibility depends on the frozen revision and these inputs; stale drafts never consult this cache.
    private readonly Dictionary<(int Actor, int Target, int? Second, GiftOffer Gift), CommandRejection> eligibility = [];
    public StrategySnapshot View { get; }
    public SettlementState Home { get; }
    public string Kind { get; }
    public bool IsDiplomacy => Kind is "gift" or "request" or "return" or "surrender";
    public CommandTargetMarker MapMarker(bool secondary = false) => Kind switch
    {
        "false-report" or "disorder" or "burn" or "seize" => CommandTargetMarker.Hostile,
        "inspire" or "rescue" => CommandTargetMarker.Friendly,
        "request" when secondary => CommandTargetMarker.Attack,
        _ => CommandTargetMarker.Enter
    };
    public bool IsCurrent => session.Snapshot.Revision == View.World.Revision;
    private bool CanPrepare => IsCurrent && !session.IsExecuting;
    private bool NeedsSecondary => Kind is "rumor" or "request" or "return";
    public ImmutableArray<int> Actors { get; }
    public int? ActorId { get; private set; }
    public int? TargetId { get; private set; }
    public int? SecondaryId { get; private set; }
    public bool? ActorKnowsInspiration => ActorId is int id ? KnowsInspiration(id) : null;
    public bool KnowsInspiration(int id) => inspiration.Value.Contains(id);
    public string ActorPriorityField => Kind is "burn" or "seize" ? "war" : Kind == "rescue" ? "leadership" : IsDiplomacy ? "politics" : "intelligence";
    public int? TargetRegionId => TargetKind == MissionTargetKind.Unit && TargetId is int id ?
        View.RegionAt(View.World.Units.Single(u => u.Id == id).Position) : null;
    public ImmutableArray<int> Prisoners => Kind == "return" ? Targets.SelectMany(force => RawSecondary(force)
        .Where(person => EligibilityFor(ActorId!.Value, force, person) == CommandRejection.None)).Distinct().Order().ToImmutableArray() : [];
    public GiftOffer Gift { get; private set; } = new(GiftKind.Money, 1000, null);
    public MissionTargetKind TargetKind => Kind switch
    {
        "alienate" => MissionTargetKind.Officer, "burn" or "seize" => MissionTargetKind.Settlement,
        "false-report" or "disorder" or "inspire" or "rescue" => MissionTargetKind.Unit,
        _ => MissionTargetKind.Force
    };
    public MissionTargetKind SecondaryKind => Kind == "rumor" ? MissionTargetKind.Force :
        Kind == "return" ? MissionTargetKind.Officer : MissionTargetKind.Settlement;
    public static bool Supports(string kind) => kind is "alienate" or "burn" or "seize" or "rumor" or
        "false-report" or "disorder" or "inspire" or "rescue" or "gift" or "request" or "return" or "surrender";

    public MissionDraft(GameSession session, int homeId, string kind)
    {
        if (!Supports(kind)) throw new ArgumentException("Unknown mission.", nameof(kind));
        this.session = session; View = session.View; Kind = kind;
        inspiration = new(() =>
        {
            var force = View.World.Forces.Single(f => f.Id == View.PlayerForceId);
            var treasures = View.Rules.Treasures;
            var tactics = session.MilitaryRules?.Tactics.Values.Where(t => t.Effect == TacticEffect.Inspire).Select(t => t.Id).ToHashSet() ?? [];
            return View.World.Officers.Where(p => p.OwnerId == force.Id &&
                tactics.Overlaps(TacticAvailability.ForOfficer(p,force,View.World.Treasures,treasures).Keys))
                .Select(p => p.Id).ToImmutableHashSet();
        });
        Home = View.World.Settlements.Single(s => s.Id == homeId);
        Actors = View.Officers.Where(p => p.OwnerId == View.PlayerForceId && p.SettlementId == homeId && p.IsAvailable)
            .OrderByDescending(p => ActorPriorityField switch { "war" => p.Abilities.War, "leadership" => p.Abilities.Leadership,
                "politics" => p.Abilities.Politics, _ => p.Abilities.Intelligence })
            .ThenByDescending(p => Kind == "inspire" && KnowsInspiration(p.Id)).ThenBy(p => p.Id)
            .Select(p => p.Id).ToImmutableArray();
        ActorId = Actors.IsEmpty ? null : Actors[0];
        if (Kind != "gift") return;
        var root = View.World.Corps.Single(c => c.OwnerId == View.PlayerForceId && c.IsRoot);
        if (root.Treasury.Money > 0) Gift = new(GiftKind.Money, Math.Min(1000, root.Treasury.Money), null);
        else if (root.Treasury.Food > 0) Gift = new(GiftKind.Food, Math.Min(1000, root.Treasury.Food), null);
        else if (!Treasures.IsEmpty) Gift = new(GiftKind.Treasure, 0, Treasures[0]);
    }

    private UnitStrategyKind UnitKind => Kind switch
    {
        "false-report" => UnitStrategyKind.FalseReport, "disorder" => UnitStrategyKind.Disorder,
        "inspire" => UnitStrategyKind.Inspire, "rescue" => UnitStrategyKind.Reinforce,
        _ => throw new InvalidOperationException()
    };
    public ImmutableArray<int> Targets => Candidates(TargetKind, false).ToImmutableArray();
    public ImmutableArray<int> SecondaryTargets => Candidates(SecondaryKind, true).ToImmutableArray();
    public ImmutableArray<int> Treasures => IsCurrent ? session.GetGiftTreasures(View.PlayerForceId) : [];
    private IEnumerable<int> Candidates(MissionTargetKind type, bool secondary)
    {
        if (!CanPrepare || ActorId is not int actor || secondary && !NeedsSecondary) return [];
        IEnumerable<int> ids = type switch
        {
            MissionTargetKind.Officer => View.Officers.Select(p => p.Id),
            MissionTargetKind.Settlement => View.Settlements.Where(s => !s.IsDestroyed).Select(s => s.Id),
            MissionTargetKind.Unit => View.World.Units.Where(u => !u.IsEmbedded && !u.IsPersonnel && u.Soldiers > 0).Select(u => u.Id),
            _ => View.Forces.Where(f => !f.IsEliminated && f.Id != View.PlayerForceId).Select(f => f.Id)
        };
        if (NeedsSecondary)
        {
            if (!secondary) return ids.Where(id => RawSecondary(id).Any(second => EligibilityFor(actor, id, second) == CommandRejection.None)).Order();
            return TargetId is int target ? RawSecondary(target).Where(id => EligibilityFor(actor, target, id) == CommandRejection.None).Order() : [];
        }
        return ids.Where(id => EligibilityFor(actor, id, null) == CommandRejection.None).Order();
    }
    private IEnumerable<int> RawSecondary(int target) => Kind switch
    {
        "rumor" => View.Forces.Where(f => !f.IsEliminated && f.Id != target && f.Id != View.PlayerForceId).Select(f => f.Id),
        "request" => View.Settlements.Where(s => !s.IsDestroyed).Select(s => s.Id),
        "return" => View.Officers.Where(p => p.Status == OfficerStatus.Captive && p.OwnerId == View.PlayerForceId).Select(p => p.Id),
        _ => []
    };
    private CommandRejection EligibilityFor(int actor, int target, int? second)
    {
        if (!CanPrepare) return CommandRejection.NotStable;
        var key = (actor,target,second,Gift);
        if (!eligibility.TryGetValue(key,out var rejection))
        {
            rejection = session.GetPreparationEligibility(new("player"), CommandFor(actor,target,second));
            eligibility.Add(key,rejection);
        }
        return rejection;
    }
    private CommandAdvice AdviceFor(int actor, int target, int? second)
    {
        int force = View.PlayerForceId;
        return Kind switch
        {
            "alienate" => session.GetAlienateAdvice(force, actor, target),
            "burn" => session.GetBurnAdvice(force, actor, target),
            "seize" => session.GetSeizeAdvice(force, actor, target),
            "rumor" => session.GetGossipAdvice(force, actor, target, second!.Value),
            "gift" => session.GetGiftAdvice(force, actor, target, Gift),
            "request" => session.GetAttackRequestAdvice(force, actor, target, second!.Value),
            "return" => session.GetPrisonerReturnAdvice(force, actor, target, second!.Value),
            "surrender" => session.GetSurrenderAdvice(force, actor, target),
            _ => session.GetUnitStrategyAdvice(force, actor, UnitKind, target)
        };
    }
    public CommandAdvice? Advice => CanPrepare && Complete ? AdviceFor(ActorId!.Value, TargetId!.Value, SecondaryId) : null;
    public CommandPreparationPreview Preview => !CanPrepare ? new(CommandRejection.NotStable, null, null) : !Complete ?
        new(CommandRejection.InvalidTask, null, null) : session.GetMissionPreparation(new("player"), CreateCommand());
    public CommandRejection Rejection => Preview.Rejection;
    private bool Complete => ActorId is not null && TargetId is not null &&
        (!NeedsSecondary || SecondaryId is not null);
    public bool CanExecute => Rejection == CommandRejection.None;
    public bool SetActor(int id)
    {
        if (!CanPrepare || !Actors.Contains(id)) return false;
        ActorId = id;
        if (TargetId is int target && !Targets.Contains(target)) { TargetId = null; SecondaryId = null; }
        if (SecondaryId is int second && !SecondaryTargets.Contains(second)) SecondaryId = null;
        return true;
    }
    public bool SetTarget(int id, bool secondary = false)
    {
        if (!(secondary ? SecondaryTargets : Targets).Contains(id)) return false;
        if (secondary) SecondaryId = id;
        else { TargetId = id; SecondaryId = null; }
        return true;
    }
    public bool SetGift(GiftOffer offer)
    {
        if (!CanPrepare || !offer.IsValid || offer.Kind == GiftKind.Treasure && !Treasures.Contains(offer.TreasureId!.Value)) return false;
        Gift = offer; return true;
    }
    public bool SetPrisoner(int id)
    {
        if (!Prisoners.Contains(id)) return false;
        TargetId = Targets.First(force => EligibilityFor(ActorId!.Value, force, id) == CommandRejection.None);
        SecondaryId = id; return true;
    }
    public long? ArrivalDaysToOfficer(int id)
    {
        if (!IsCurrent || ActorId is not int actor) return null;
        GameCommand? command = Kind == "alienate" ? new AlienateCommand(commandId,View.PlayerForceId,View.World.Simulation.NextSequence,actor,id) :
            Kind == "return" && Prisoners.Contains(id) ? new ReturnPrisonerCommand(commandId,View.PlayerForceId,View.World.Simulation.NextSequence,actor,
                Targets.First(force => EligibilityFor(actor,force,id) == CommandRejection.None),id) : null;
        return command is not null && session.GetMissionPreparation(new("player"),command).Arrival is {} arrival ? arrival.DayNumber - View.World.Clock.DayNumber : null;
    }
    public GameCommand CreateCommand()
    {
        if (!Complete) throw new InvalidOperationException("Incomplete mission.");
        return CommandFor(ActorId!.Value,TargetId!.Value,SecondaryId);
    }
    public long? ArrivalDaysToSettlement(int id)
    {
        if (!IsCurrent || ActorId is not int actor || Kind is not ("burn" or "seize")) return null;
        return session.GetMissionPreparation(new("player"),CommandFor(actor,id,null)).Arrival is {} arrival ?
            arrival.DayNumber - View.World.Clock.DayNumber : null;
    }
    private GameCommand CommandFor(int actor, int target, int? second)
    {
        int force = View.PlayerForceId;
        long sequence = View.World.Simulation.NextSequence;
        return Kind switch
        {
            "alienate" => new AlienateCommand(commandId, force, sequence, actor, target),
            "burn" => new BurnCommand(commandId, force, sequence, actor, target),
            "seize" => new SeizeCommand(commandId, force, sequence, actor, target),
            "rumor" => new GossipCommand(commandId, force, sequence, actor, target, second!.Value),
            "gift" => new GiftCommand(commandId, force, sequence, actor, target, Gift),
            "request" => new RequestAttackCommand(commandId, force, sequence, actor, target, second!.Value),
            "return" => new ReturnPrisonerCommand(commandId, force, sequence, actor, target, second!.Value),
            "surrender" => new DemandSurrenderCommand(commandId, force, sequence, actor, target),
            _ => new UnitStrategyCommand(commandId, force, sequence, actor, UnitKind, target)
        };
    }
}

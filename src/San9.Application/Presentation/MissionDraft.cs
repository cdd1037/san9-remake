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
    private readonly GiftOffer? availableGift;
    private bool actorEdited;
    private readonly Dictionary<(int Actor, int Target, int? Second), CommandAdvice> strategyAdvice = [];
    private readonly Dictionary<(int Target, bool Secondary), AdviserOpinion> targetOpinions = [];
    public bool HasRecommendations => !IsDiplomacy;
    public bool SupportsMultipleActors => Kind is "alienate" or "burn" or "seize";
    public ImmutableArray<int> Members { get; private set; } = [];
    public int FavorableMemberCount => Members.Count(id => ActorAdvice(id)?.Opinion == AdviserOpinion.Favorable);
    // Eligibility depends on the frozen revision and these inputs; stale drafts never consult this cache.
    private readonly Dictionary<(int Actor, int Target, int? Second, GiftOffer? Gift), CommandRejection> eligibility = [];
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
    public bool CanSelectActor => CanPrepare && (Actors.Length > 1 || SupportsMultipleActors && Members.IsEmpty && !Actors.IsEmpty);
    public bool CanSelectTarget => CanPrepare && CanChoose(Kind == "return" ? Prisoners : Targets,
        Kind == "return" ? SecondaryId : TargetId);
    public bool CanSelectSecondaryTarget => CanPrepare && CanChoose(SecondaryTargets, SecondaryId);
    private static bool CanChoose(ImmutableArray<int> candidates, int? selected) =>
        candidates.Length > 1 || selected is null && !candidates.IsEmpty;
    public int? ActorId => Members.IsEmpty ? null : Members[0];
    public int? TargetId { get; private set; }
    public int? SecondaryId { get; private set; }
    public bool? ActorKnowsInspiration => ActorId is int id ? KnowsInspiration(id) : null;
    public bool KnowsInspiration(int id) => inspiration.Value.Contains(id);
    public string ActorPriorityField => Kind is "burn" or "seize" ? "war" : Kind == "rescue" ? "leadership" : IsDiplomacy ? "politics" : "intelligence";
    public int? TargetRegionId => TargetKind == MissionTargetKind.Unit && TargetId is int id ?
        View.RegionAt(View.World.Units.Single(u => u.Id == id).Position) : null;
    public ImmutableArray<int> Prisoners => Kind == "return" ? Targets.SelectMany(force => RawSecondary(force)
        .Where(person => EligibilityFor(ActorId!.Value, force, person) == CommandRejection.None)).Distinct().Order().ToImmutableArray() : [];
    public GiftOffer? Gift { get; private set; }
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
            var force = View.World.Forces.Single(f => f.Id == View.RequirePlayerForce());
            var treasures = View.Rules.Treasures;
            var tactics = session.MilitaryRules?.Tactics.Values.Where(t => t.Effect == TacticEffect.Inspire).Select(t => t.Id).ToHashSet() ?? [];
            return View.World.Officers.Where(p => p.OwnerId == force.Id &&
                tactics.Overlaps(TacticAvailability.ForOfficer(p,force,View.World.Treasures,treasures).Keys))
                .Select(p => p.Id).ToImmutableHashSet();
        });
        Home = View.World.Settlements.Single(s => s.Id == homeId);
        Actors = View.Officers.Where(p => p.OwnerId == View.RequirePlayerForce() && p.SettlementId == homeId && p.IsAvailable)
            .OrderByDescending(p => ActorPriorityField switch { "war" => p.Abilities.War, "leadership" => p.Abilities.Leadership,
                "politics" => p.Abilities.Politics, _ => p.Abilities.Intelligence })
            .ThenByDescending(p => Kind == "inspire" && KnowsInspiration(p.Id)).ThenBy(p => p.Id)
            .Select(p => p.Id).ToImmutableArray();
        Members = Actors.Take(1).ToImmutableArray();
        if (Kind == "gift")
        {
            var root = View.World.Corps.Single(c => c.OwnerId == View.RequirePlayerForce() && c.IsRoot);
            // Candidate and journey queries need an affordable offer, but do not select a gift for the player.
            if (root.Treasury.Money >= GiftOffer.MinimumMoney) availableGift = new(GiftKind.Money, GiftOffer.MinimumMoney, null);
            else if (root.Treasury.Food >= GiftOffer.MinimumFood) availableGift = new(GiftKind.Food, GiftOffer.MinimumFood, null);
            else if (!Treasures.IsEmpty) availableGift = new(GiftKind.Treasure, 0, Treasures[0]);
        }
        if (Kind == "return")
        {
            var prisoners = Prisoners;
            if (prisoners.Length == 1) SetPrisoner(prisoners[0]);
        }
        else if (Kind != "rumor")
        {
            var targets = Targets;
            if (targets.Length == 1) SetTarget(targets[0]);
            if (Kind == "request" && TargetId is not null)
            {
                var secondary = SecondaryTargets;
                if (secondary.Length == 1) SetTarget(secondary[0], secondary: true);
            }
        }
        RefreshSelection();
    }

    private CommandAdvice StrategyAdvice(int actor, int target, int? second)
    {
        var key = (actor, target, second);
        if (!strategyAdvice.TryGetValue(key, out var advice))
        {
            advice = EligibilityFor(actor, target, second) == CommandRejection.None
                ? AdviceFor(actor, target, second)
                : new(CommandRejection.InvalidTask, null, AdviserOpinion.Unavailable);
            strategyAdvice.Add(key, advice);
        }
        return advice;
    }

    public CommandAdvice? ActorAdvice(int actor) => CanPrepare && HasRecommendations && Actors.Contains(actor) && TargetsComplete
        ? StrategyAdvice(actor, TargetId!.Value, SecondaryId) : null;

    public AdviserOpinion TargetOpinion(int target, bool secondary = false)
    {
        if (!CanPrepare || !HasRecommendations) return AdviserOpinion.Unavailable;
        if (targetOpinions.TryGetValue((target, secondary), out var cached)) return cached;
        if (!(secondary ? SecondaryTargets : Targets).Contains(target) ||
            View.World.Forces.Single(f => f.Id == View.RequirePlayerForce()).AdviserId is null)
            return AdviserOpinion.Unavailable;
        IEnumerable<int> actors = actorEdited ? Members : Actors;
        int first = secondary ? TargetId!.Value : target;
        IEnumerable<int?> seconds = Kind != "rumor" ? [null] : secondary ? [target] :
            SecondaryId is int chosen ? [chosen] : RawSecondary(first).Select(id => (int?)id);
        bool hasOpinion = false, hasUnknown = false;
        foreach (int candidate in actors)
        {
            foreach (int? second in seconds)
            {
                var advice = StrategyAdvice(candidate, first, second);
                if (advice.Rejection != CommandRejection.None) continue;
                if (advice.Opinion == AdviserOpinion.Favorable)
                    return targetOpinions[(target, secondary)] = AdviserOpinion.Favorable;
                hasOpinion = true;
                hasUnknown |= advice.Opinion != AdviserOpinion.Unfavorable;
            }
        }
        var result = hasOpinion && !hasUnknown ? AdviserOpinion.Unfavorable : AdviserOpinion.Unavailable;
        targetOpinions.Add((target, secondary), result);
        return result;
    }

    public static int AdvicePriority(AdviserOpinion opinion) => opinion switch
    {
        AdviserOpinion.Favorable => 2, AdviserOpinion.Unfavorable => 1, _ => 0
    };
    public static string AdviceSymbol(AdviserOpinion opinion) => opinion switch
    {
        AdviserOpinion.Favorable => "○", AdviserOpinion.Unfavorable => "×", _ => ""
    };
    public IEnumerable<int> RecommendedActors => OrderActors(Actors);
    private IEnumerable<int> OrderActors(IEnumerable<int> actors) =>
        actors.OrderByDescending(id => AdvicePriority(ActorAdvice(id)?.Opinion ?? AdviserOpinion.Unavailable));

    private void RefreshSelection()
    {
        if (HasRecommendations && !actorEdited && TargetsComplete)
            Members = RecommendedActors.Take(1).ToImmutableArray();
        // Keep the selected order frozen for departure presentation after the command commits.
        Members = OrderActors(Actors.Where(Members.Contains)).ToImmutableArray();
    }

    private UnitStrategyKind UnitKind => Kind switch
    {
        "false-report" => UnitStrategyKind.FalseReport, "disorder" => UnitStrategyKind.Disorder,
        "inspire" => UnitStrategyKind.Inspire, "rescue" => UnitStrategyKind.Reinforce,
        _ => throw new InvalidOperationException()
    };
    public ImmutableArray<int> Targets => Candidates(TargetKind, false).ToImmutableArray();
    public ImmutableArray<int> SecondaryTargets => Candidates(SecondaryKind, true).ToImmutableArray();
    public ImmutableArray<int> Treasures => IsCurrent ? session.GetGiftTreasures(View.RequirePlayerForce()) : [];
    private IEnumerable<int> Candidates(MissionTargetKind type, bool secondary)
    {
        if (!CanPrepare || secondary && !NeedsSecondary) return [];
        int? candidate = ActorId ?? Actors.Select(id => (int?)id).FirstOrDefault();
        if (candidate is not int actor) return [];
        IEnumerable<int> ids = type switch
        {
            MissionTargetKind.Officer => View.Officers.Select(p => p.Id),
            MissionTargetKind.Settlement => View.Settlements.Where(s => !s.IsDestroyed).Select(s => s.Id),
            MissionTargetKind.Unit => View.World.Units.Where(u => !u.IsEmbedded && !u.IsPersonnel && u.Soldiers > 0).Select(u => u.Id),
            _ => View.Forces.Where(f => !f.IsEliminated && f.Id != View.RequirePlayerForce()).Select(f => f.Id)
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
        "rumor" => View.Forces.Where(f => !f.IsEliminated && f.Id != target && f.Id != View.RequirePlayerForce()).Select(f => f.Id),
        "request" => View.Settlements.Where(s => !s.IsDestroyed).Select(s => s.Id),
        "return" => View.Officers.Where(p => p.Status == OfficerStatus.Captive && p.OwnerId == View.RequirePlayerForce()).Select(p => p.Id),
        _ => []
    };
    private CommandRejection EligibilityFor(int actor, int target, int? second)
    {
        if (!CanPrepare) return CommandRejection.NotStable;
        if (Kind == "gift" && Gift is null && availableGift is null) return CommandRejection.InvalidTask;
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
        int force = View.RequirePlayerForce();
        return Kind switch
        {
            "alienate" => session.GetAlienateAdvice(force, actor, target),
            "burn" => session.GetBurnAdvice(force, actor, target),
            "seize" => session.GetSeizeAdvice(force, actor, target),
            "rumor" => session.GetGossipAdvice(force, actor, target, second!.Value),
            "gift" => session.GetGiftAdvice(force, actor, target, Gift!),
            "request" => session.GetAttackRequestAdvice(force, actor, target, second!.Value),
            "return" => session.GetPrisonerReturnAdvice(force, actor, target, second!.Value),
            "surrender" => session.GetSurrenderAdvice(force, actor, target),
            _ => session.GetUnitStrategyAdvice(force, actor, UnitKind, target)
        };
    }
    public CommandAdvice? Advice => CanPrepare && Complete ? AdviceFor(ActorId!.Value, TargetId!.Value, SecondaryId) : null;
    public CommandPreparationPreview Preview
    {
        get
        {
            if (!CanPrepare) return new(CommandRejection.NotStable, null, null);
            if (ActorId is not int actor || TargetId is not int target || Kind == "gift" && Gift is null && availableGift is null)
                return new(CommandRejection.InvalidTask, null, null);
            int? second = SecondaryId;
            // A request travels to the receiving ruler regardless of which facility is later selected.
            // Preview an eligible completion without selecting it or enabling submission/advice.
            if (Kind == "request" && second is null)
            {
                var targets = SecondaryTargets;
                if (!targets.IsEmpty) second = targets[0];
            }
            return NeedsSecondary && second is null ? new(CommandRejection.InvalidTask, null, null) :
                session.GetMissionPreparation(new("player"), SupportsMultipleActors ? CreateCommand() : CommandFor(actor, target, second));
        }
    }
    public CommandRejection Rejection => CanPrepare && !Complete ? CommandRejection.InvalidTask : Preview.Rejection;
    private bool TargetsComplete => TargetId is not null &&
        (!NeedsSecondary || SecondaryId is not null);
    private bool Complete => !Members.IsEmpty && TargetsComplete && (Kind != "gift" || Gift is not null);
    public bool CanExecute => Rejection == CommandRejection.None;
    public bool SetActor(int id)
    {
        if (SupportsMultipleActors) return SetMembers([id]);
        if (!CanPrepare || !Actors.Contains(id)) return false;
        Members = [id];
        actorEdited = true;
        targetOpinions.Clear();
        if (TargetId is int target && !Targets.Contains(target)) { TargetId = null; SecondaryId = null; }
        if (SecondaryId is int second && !SecondaryTargets.Contains(second)) SecondaryId = null;
        return true;
    }
    public bool CanSelectMembers(IReadOnlyCollection<int> ids) => CanPrepare && SupportsMultipleActors &&
        ids.Distinct().Count() == ids.Count && ids.All(Actors.Contains) &&
        (TargetId is not int target || ids.All(id => EligibilityFor(id, target, null) == CommandRejection.None));

    public bool SetMembers(IEnumerable<int> ids)
    {
        int[] selected = ids.ToArray();
        if (!CanSelectMembers(selected)) return false;
        Members = Actors.Where(selected.Contains).ToImmutableArray();
        actorEdited = true;
        targetOpinions.Clear();
        RefreshSelection();
        return true;
    }

    public long? ArrivalDaysForActor(int actor) => CanPrepare && Actors.Contains(actor) && TargetId is int target &&
        (!NeedsSecondary || SecondaryId is not null)
        ? session.GetMissionPreparation(new("player"), CommandFor(actor, target, SecondaryId)).JourneyDays : null;
    public bool SetTarget(int id, bool secondary = false)
    {
        if (!(secondary ? SecondaryTargets : Targets).Contains(id)) return false;
        targetOpinions.Clear();
        if (secondary) SecondaryId = id;
        else
        {
            TargetId = id;
            if (Kind != "request" || SecondaryId is int current && !SecondaryTargets.Contains(current)) SecondaryId = null;
        }
        RefreshSelection();
        return true;
    }
    public bool SetGift(GiftOffer offer)
    {
        if (!CanPrepare || !offer.IsValid || offer.Kind == GiftKind.Treasure && !Treasures.Contains(offer.TreasureId!.Value)) return false;
        Gift = offer; return true;
    }
    public (uint Minimum, uint Maximum) GiftAmountRange(GiftKind kind)
    {
        if (Kind != "gift") throw new InvalidOperationException("The draft is not a gift preparation.");
        uint minimum = GiftOffer.MinimumAmount(kind);
        var treasury = View.World.Corps.Single(c => c.OwnerId == View.RequirePlayerForce() && c.IsRoot).Treasury;
        return (minimum, Math.Min(GiftOffer.MaximumAmount(kind), kind == GiftKind.Money ? treasury.Money : treasury.Food));
    }
    public BoundedNumberInput? GiftAmountInput(GiftKind kind)
    {
        var (minimum, maximum) = GiftAmountRange(kind);
        if (!CanPrepare || maximum < minimum) return null;
        uint initial = ActorId is int actor && TargetId is int target ?
            AdvisedAmount.Find(minimum, maximum, preferMinimum: true,
                amount => session.GetGiftAdvice(View.RequirePlayerForce(), actor, target, new(kind, amount, null))) ?? minimum : minimum;
        return new(minimum, maximum, initial);
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
        GameCommand? command = Kind == "alienate" ? new AlienateCommand(commandId,View.RequirePlayerForce(),View.World.Simulation.NextSequence,actor,id) :
            Kind == "return" && Prisoners.Contains(id) ? new ReturnPrisonerCommand(commandId,View.RequirePlayerForce(),View.World.Simulation.NextSequence,actor,
                Targets.First(force => EligibilityFor(actor,force,id) == CommandRejection.None),id) : null;
        return command is not null ? session.GetMissionPreparation(new("player"),command).JourneyDays : null;
    }
    public GameCommand CreateCommand()
    {
        if (!Complete) throw new InvalidOperationException("Incomplete mission.");
        int force = View.RequirePlayerForce(), target = TargetId!.Value;
        long sequence = View.World.Simulation.NextSequence;
        if (SupportsMultipleActors) return Kind switch
        {
            "alienate" => new AlienateOfficersCommand(commandId, force, sequence, Members, target),
            "burn" => new BurnOfficersCommand(commandId, force, sequence, Members, target),
            _ => new SeizeOfficersCommand(commandId, force, sequence, Members, target)
        };
        return CommandFor(ActorId!.Value,TargetId!.Value,SecondaryId);
    }
    public long? ArrivalDaysToSettlement(int id)
    {
        if (!IsCurrent || ActorId is not int actor || Kind is not ("burn" or "seize")) return null;
        return session.GetMissionPreparation(new("player"),CommandFor(actor,id,null)).JourneyDays;
    }
    private GameCommand CommandFor(int actor, int target, int? second)
    {
        int force = View.RequirePlayerForce();
        long sequence = View.World.Simulation.NextSequence;
        return Kind switch
        {
            "alienate" => new AlienateCommand(commandId, force, sequence, actor, target),
            "burn" => new BurnCommand(commandId, force, sequence, actor, target),
            "seize" => new SeizeCommand(commandId, force, sequence, actor, target),
            "rumor" => new GossipCommand(commandId, force, sequence, actor, target, second!.Value),
            "gift" => new GiftCommand(commandId, force, sequence, actor, target, Gift ?? availableGift!),
            "request" => new RequestAttackCommand(commandId, force, sequence, actor, target, second!.Value),
            "return" => new ReturnPrisonerCommand(commandId, force, sequence, actor, target, second!.Value),
            "surrender" => new DemandSurrenderCommand(commandId, force, sequence, actor, target),
            _ => new UnitStrategyCommand(commandId, force, sequence, actor, UnitKind, target)
        };
    }
}

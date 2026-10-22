using System.Collections.Immutable;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

/// <summary>Ordered strategy and diplomacy work; candidates are local, effects use public commands.</summary>
internal sealed partial class OriginalAiActions(SimulationEngine engine, ScenarioInitialState content,
    MilitaryRulebook? military, int corpsId, CommandContext context, OriginalAiRandom random,
    Func<GameCommand, bool> submit)
{
    private int pathQueries;
    private OriginalAiGeography? geography;
    private OriginalAiProjection? domestic;
    private WorldSnapshot? abilitySnapshot;
    private ImmutableDictionary<int, OfficerAbilities>? abilities;

    // Topology and ownership depend on settlements, not officer action flags or treasury changes.
    // Keep one lazy view per work and rebuild it when its published input changes.
    private OriginalAiGeography Geography
    {
        get
        {
            var world = World;
            if (geography is null || !geography.Matches(world))
            {
                geography = new(content, military, world);
                domestic = null;
            }
            return geography!;
        }
    }
    private OriginalDomesticInputs Domestic()
    {
        var currentGeography = Geography;
        domestic ??= new(engine, content, corpsId, currentGeography, content.OriginalAi.RecruitmentEnabled);
        return domestic.Domestic();
    }
    private WorldSnapshot World => engine.Snapshot;
    private CorpsState Corps => World.Corps.Single(c => c.Id == corpsId);
    private int Owner => Corps.OwnerId;
    private ImmutableDictionary<int, OfficerAbilities> Abilities
    {
        get
        {
            var world = World;
            if (!ReferenceEquals(abilitySnapshot, world))
            {
                abilities = engine.GetAbilities(world);
                abilitySnapshot = world;
            }
            return abilities!;
        }
    }
    private OfficerAbilities Ability(OfficerState p) => Abilities[p.Id];
    private int Relation(int first, int second) => World.Relations.FirstOrDefault(r =>
        r.FirstId == Math.Min(first, second) && r.SecondId == Math.Max(first, second))?.Value ?? 50;
    private bool Direct(SettlementState s) => s.OwnerId == Owner && (s.CorpsId == corpsId || s.CorpsId is null && Corps.IsRoot);
    private bool Direct(UnitState u) => u.OwnerId == Owner && (u.CorpsId == corpsId || u.CorpsId is null && Corps.IsRoot);
    private bool Available(OfficerState p) => !p.IsDormant && p.IsServing && p.OwnerId == Owner && !p.Acted && p.UnitId is null &&
        p.SettlementId is int home && World.Settlements.Any(s => s.Id == home && Direct(s) && !s.IsDestroyed &&
            !s.IsDormant && s.Kind == SettlementKind.City && s.Durability > 0 && s.Facility?.BuilderUnitId is null && engine.CanStartSettlementWork(s.Id)) &&
        !World.Simulation.Tasks.Any(t => t.OfficerId == p.Id);
    private int City(int id) => content.RegionCityIds.GetValueOrDefault(id, id);
    private bool Near(int source, int target) => City(source) == City(target) ||
        content.CityConnections.Any(c => c.FromCityId == City(source) && c.ToCityId == City(target));
    private WorldPosition? PlacePosition(int id) => military is not null && World.Settlements.FirstOrDefault(s => s.Id == id) is { } s &&
        military.TryPlace(s, out var p) ? p.Position : null;
    private WorldPosition? RulerPosition(int force)
    {
        var leader = World.Forces.FirstOrDefault(f => f.Id == force)?.LeaderId;
        var ruler = World.Officers.FirstOrDefault(p => p.Id == leader);
        return ruler?.UnitId is int unit ? World.Units.FirstOrDefault(u => u.Id == unit)?.Position :
            ruler?.SettlementId is int home ? PlacePosition(home) : null;
    }
    private static int Geometry(WorldPosition a, WorldPosition b) => (int)Math.Ceiling(Math.Sqrt(
        Math.Pow((double)a.X - b.X, 2) + Math.Pow((double)a.Y - b.Y, 2)) / 30);
    private bool InRange(OfficerState actor, WorldPosition? target, OriginalAiWorkRule rule) =>
        target is { } b && PlacePosition(actor.SettlementId!.Value) is { } a && Geometry(a, b) <= rule.MaximumGeometricDays;
    private GameCommand Command(Func<Guid, int, long, GameCommand> create)
    {
        long sequence = World.Simulation.NextSequence;
        return create(SimulationIdentity.Command(sequence, Owner), Owner, sequence);
    }
    internal static bool Handles(OriginalAiWork work) => work is OriginalAiWork.FalseReport or OriginalAiWork.Confuse or
        OriginalAiWork.Encourage or OriginalAiWork.Reinforce or OriginalAiWork.Alienate or OriginalAiWork.Burn or
        OriginalAiWork.Seize or OriginalAiWork.Gossip or OriginalAiWork.Gift or OriginalAiWork.RequestAttack or
        OriginalAiWork.ReturnPrisoner or OriginalAiWork.Surrender;
    private sealed record Target(int Id, int? Second = null);
    private sealed record Pair(OfficerState Actor, GameCommand Command, int Threshold);

    internal OriginalAiWorkReport Run(OriginalAiWork work, int limit, OriginalAiState state, OriginalAiCorpsState strategy)
    {
        using var diagnostics = new PathSearchDiagnostics();
        pathQueries = 0;
        int used = 0, accepted = 0, rejected = 0, empty = 0;
        OriginalAiWorkReport Report(OriginalAiWorkEnd end) => new(corpsId, work, end, used, accepted)
            { Rejected = rejected, EmptyPlans = empty, PathQueries = pathQueries, PathSearches = diagnostics.Queries,
                PathExpanded = diagnostics.Expanded, PathEndpointExpanded = diagnostics.EndpointExpanded };
        bool reduced = state.Forces.Single(f => f.ForceId == Owner).Group == OriginalAiGroup.ReducedActivity;
        bool schemes = work is >= OriginalAiWork.FalseReport and <= OriginalAiWork.Gossip;
        if (reduced && (schemes || work == OriginalAiWork.RequestAttack) && random.Percent(50))
            return Report(OriginalAiWorkEnd.ProbabilityFailed);
        if (reduced && work == OriginalAiWork.Surrender) return Report(OriginalAiWorkEnd.PreparationFailed);
        int? opponent = strategy.TargetForceId ?? World.Settlements.FirstOrDefault(s => s.Id == strategy.TargetSettlementId)?.OwnerId;
        if (work is OriginalAiWork.Alienate or OriginalAiWork.Burn or OriginalAiWork.Seize or OriginalAiWork.Gossip &&
            opponent is int other && state.Forces.FirstOrDefault(f => f.ForceId == other)?.Group == OriginalAiGroup.Favored && random.Percent(75))
            return Report(OriginalAiWorkEnd.ProbabilityFailed);
        var rule = state.Policy.WorkRules[work];
        var targets = Targets(work, state, opponent).ToList();
        if (targets.Count == 0) return Report(OriginalAiWorkEnd.PreparationFailed);
        int Skill(OfficerState p) => work switch
        {
            OriginalAiWork.Burn or OriginalAiWork.Seize => Ability(p).War,
            OriginalAiWork.Reinforce => Ability(p).Leadership,
            >= OriginalAiWork.Gift and <= OriginalAiWork.Surrender => Ability(p).Politics,
            _ => Ability(p).Intelligence
        };
        var actors = World.Officers.Where(Available)
            .Where(p => work != OriginalAiWork.Reinforce || ReliefSourceReady(p))
            .Where(p => work == OriginalAiWork.Gift ||
            Skill(p) >= (work == OriginalAiWork.RequestAttack ? 50 : 45) && Skill(p) <= 115)
            .Where(p => targets.Any(t => SourceInRange(work, p, t, rule))).ToList();
        if (work == OriginalAiWork.Alienate) actors = actors.OrderBy(Skill).ToList();
        if (work is OriginalAiWork.Gossip or OriginalAiWork.RequestAttack or OriginalAiWork.Surrender)
            actors = actors.OrderByDescending(Skill).ToList();
        if (actors.Count == 0) return Report(OriginalAiWorkEnd.PreparationFailed);
        while (used < limit && targets.Count > 0 && actors.Count > 0 && World.Simulation.Boundary == SimulationBoundary.Stable)
        {
            used++;
            var offer = work == OriginalAiWork.Gift ? Gift(Domestic()) : null;
            if (work == OriginalAiWork.Gift && offer is null) return Report(OriginalAiWorkEnd.ReserveReached);
            Pair? selected = null;
            if (work == OriginalAiWork.RequestAttack)
            {
                selected = SelectRequestPair(targets, actors, rule);
                if (selected is not null)
                {
                    actors.RemoveAll(p => p.Id == selected.Actor.Id);
                    var request = (RequestAttackCommand)selected.Command;
                    targets.RemoveAll(t => t.Id == request.TargetForceId || t.Second == request.TargetSettlementId);
                }
            }
            else
            {
                foreach (var target in work == OriginalAiWork.Gift ? targets.Take(1).ToArray() : targets.ToArray())
                {
                    selected = SelectPair(work, target, actors, rule, offer);
                    if (selected is null) continue;
                    // Consume a selected pair once, before relief validation or public submission.
                    actors.RemoveAll(p => p.Id == selected.Actor.Id);
                    if (work != OriginalAiWork.Gossip)
                        targets.Remove(target);
                    if (work == OriginalAiWork.Reinforce && !RetainsRelief(selected.Actor))
                    {
                        // Work 28 consumes the pair before the final garrison check, then retries
                        // within this attempt with every actor from a failed source removed.
                        int? source = selected.Actor.SettlementId;
                        actors.RemoveAll(p => p.SettlementId == source);
                        selected = null;
                        continue;
                    }
                    break;
                }
            }
            if (selected is null)
            {
                if (work == OriginalAiWork.Gift) targets.RemoveAt(0);
                empty++;
                continue;
            }
            if (submit(selected.Command)) accepted++; else rejected++;
        }
        return Report(used >= limit ? OriginalAiWorkEnd.AttemptsExhausted : OriginalAiWorkEnd.NoCandidates);
    }

    private Pair? SelectPair(OriginalAiWork work, Target target, List<OfficerState> actors,
        OriginalAiWorkRule rule, GiftOffer? offer)
    {
        var candidates = actors.Where(Available).Where(p => Spatial(work, p, target, rule));
        if (work == OriginalAiWork.RequestAttack)
        {
            var position = PlacePosition(target.Second!.Value);
            candidates = candidates.OrderBy(p => position is { } b && PlacePosition(p.SettlementId!.Value) is { } a
                ? Geometry(a, b) : int.MaxValue);
        }
        Pair? best = null;
        int bestRank = int.MinValue;
        foreach (var actor in candidates)
        {
            var command = Create(work, actor.Id, target, offer);
            var preview = engine.GetMissionDecision(context, command);
            if (preview.Advice.Rejection != CommandRejection.None || preview.Threshold is not int threshold) continue;
            int score = preview.Advice.Opinion switch
            {
                AdviserOpinion.Favorable => 100,
                AdviserOpinion.Unfavorable => 0,
                AdviserOpinion.Uncertain when work is OriginalAiWork.Gift or OriginalAiWork.RequestAttack or OriginalAiWork.ReturnPrisoner => 50,
                AdviserOpinion.Unavailable when work == OriginalAiWork.RequestAttack => 50,
                AdviserOpinion.Unavailable when work == OriginalAiWork.ReturnPrisoner => Math.Min(random.Next(Best(World.Officers.Single(p => p.Id == target.Id)) + 20), 100),
                _ => threshold
            };
            int minimum = work is OriginalAiWork.Gift or OriginalAiWork.Surrender ? 33 :
                work is OriginalAiWork.FalseReport or OriginalAiWork.Confuse ? 15 : 20;
            if (score < minimum) continue;
            var pair = new Pair(actor, command, threshold);
            // B16: preserve distance order, checking the actual envoy, before consuming a combination.
            if (work == OriginalAiWork.RequestAttack) return pair;
            int rank = work == OriginalAiWork.ReturnPrisoner
                ? Ability(actor).Politics - Math.Min(Sum(actor) / 46, 10) : threshold;
            if (best is null || rank > bestRank)
            {
                best = pair;
                bestRank = rank;
            }
        }
        return best;
    }

    private Pair? SelectRequestPair(List<Target> targets, List<OfficerState> actors, OriginalAiWorkRule rule)
    {
        foreach (var force in targets.GroupBy(t => t.Id))
        {
            Pair? best = null;
            foreach (var target in force)
            {
                var pair = SelectPair(OriginalAiWork.RequestAttack, target, actors, rule, null);
                if (pair is not null && (best is null || pair.Threshold > best.Threshold)) best = pair;
            }
            if (best is not null) return best;
        }
        return null;
    }

    private int Best(OfficerState p) { var a = Ability(p); return Math.Max(Math.Max(a.War, a.Leadership), Math.Max(a.Intelligence, a.Politics)); }
    private int Sum(OfficerState p) { var a = Ability(p); return a.War + a.Leadership + a.Intelligence + a.Politics; }
    internal static GiftOffer? Gift(OriginalDomesticInputs input)
    {
        long money = Math.Min(10000, Math.Max(0, (input.Money - input.SalaryDemand - 2000) / 2));
        long food = Math.Min(10000, Math.Max(0, (input.Food - input.RationDemand * 3 / 2 - 10000) / 2));
        if (money < GiftOffer.MinimumMoney && food < GiftOffer.MinimumFood) return null;
        return money * 10 >= food ? new(GiftKind.Money, (uint)money, null) : new(GiftKind.Food, (uint)food, null);
    }
    private GameCommand Create(OriginalAiWork work, int actor, Target target, GiftOffer? offer) => Command((id, owner, sequence) => work switch
    {
        OriginalAiWork.FalseReport => new UnitStrategyCommand(id, owner, sequence, actor, UnitStrategyKind.FalseReport, target.Id),
        OriginalAiWork.Confuse => new UnitStrategyCommand(id, owner, sequence, actor, UnitStrategyKind.Disorder, target.Id),
        OriginalAiWork.Encourage => new UnitStrategyCommand(id, owner, sequence, actor, UnitStrategyKind.Inspire, target.Id),
        OriginalAiWork.Reinforce => new UnitStrategyCommand(id, owner, sequence, actor, UnitStrategyKind.Reinforce, target.Id),
        OriginalAiWork.Alienate => new AlienateCommand(id, owner, sequence, actor, target.Id),
        OriginalAiWork.Burn => new BurnCommand(id, owner, sequence, actor, target.Id),
        OriginalAiWork.Seize => new SeizeCommand(id, owner, sequence, actor, target.Id),
        OriginalAiWork.Gossip => new GossipCommand(id, owner, sequence, actor, target.Id, target.Second!.Value),
        OriginalAiWork.Gift => new GiftCommand(id, owner, sequence, actor, target.Id, offer!),
        OriginalAiWork.RequestAttack => new RequestAttackCommand(id, owner, sequence, actor, target.Id, target.Second!.Value),
        OriginalAiWork.ReturnPrisoner => new ReturnPrisonerCommand(id, owner, sequence, actor, target.Second!.Value, target.Id),
        OriginalAiWork.Surrender => new DemandSurrenderCommand(id, owner, sequence, actor, target.Id),
        _ => throw new ArgumentOutOfRangeException(nameof(work))
    });
}

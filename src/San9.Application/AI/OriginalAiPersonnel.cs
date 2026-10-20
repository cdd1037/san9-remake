using System.Collections.Immutable;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

/// <summary>One ordered personnel work. Candidate lists belong to the work, commands belong to Core.</summary>
internal sealed class OriginalAiPersonnel(SimulationEngine engine, ScenarioInitialState content, int corpsId,
    OriginalAiGeography geography, CommandContext context, OriginalAiRandom random,
    Func<GameCommand, bool> submit)
{
    private const int MaximumSearchJourneyDays = 255;
    private WorldSnapshot World => engine.Snapshot;
    // Reuse each projection while its immutable input collections remain unchanged.
    // Commands still refresh current records, abilities and task availability before the next candidate.
    private WorldSnapshot? _projectedWorld;
    private CorpsState _corps = null!;
    private ForceState _force = null!;
    private OfficerState? _ruler;
    private ImmutableDictionary<int, OfficerAbilities> _abilities = ImmutableDictionary<int, OfficerAbilities>.Empty;
    private ImmutableArray<OfficerState> _own = [];
    private ImmutableArray<SettlementState> _places = [];
    private HashSet<int> _placeIds = [];
    private HashSet<int> _cityIds = [];
    private HashSet<int> _busy = [];
    private void Refresh()
    {
        var world = World;
        if (ReferenceEquals(world, _projectedWorld)) return;
        var corps = world.Corps.Single(c => c.Id == corpsId);
        var force = world.Forces.Single(f => f.Id == corps.OwnerId);
        bool ownerChanged = _projectedWorld is null || _corps.OwnerId != corps.OwnerId;
        _corps = corps;
        _force = force;
        _ruler = world.Officers.FirstOrDefault(p => p.Id == force.LeaderId);
        _abilities = engine.GetAbilities(world);
        if (_projectedWorld is null || _projectedWorld.Officers != world.Officers || ownerChanged)
            _own = world.Officers.Where(p => !p.IsDormant && p.IsServing && p.OwnerId == corps.OwnerId).ToImmutableArray();
        if (_projectedWorld is null || _projectedWorld.Settlements != world.Settlements ||
            _projectedWorld.Corps != world.Corps)
        {
            _places = world.Settlements.Where(s => !s.IsDormant && !s.IsDestroyed && s.OwnerId == corps.OwnerId &&
                (s.CorpsId == corpsId || s.CorpsId is null && corps.IsRoot) &&
                s.CanHostPersons && s.Durability > 0 && s.Facility?.BuilderUnitId is null).ToImmutableArray();
            _placeIds = _places.Select(s => s.Id).ToHashSet();
            _cityIds = _places.Where(s => s.Kind == SettlementKind.City).Select(s => s.Id).ToHashSet();
        }
        if (_projectedWorld is null || _projectedWorld.Simulation.Tasks != world.Simulation.Tasks)
            _busy = world.Simulation.Tasks.Select(t => t.OfficerId).ToHashSet();
        _projectedWorld = world;
    }
    private CorpsState Corps { get { Refresh(); return _corps; } }
    private int Owner => Corps.OwnerId;
    private ForceState Force { get { Refresh(); return _force; } }
    private OfficerState? Ruler { get { Refresh(); return _ruler; } }
    private OfficerAbilities Ability(OfficerState p) { Refresh(); return _abilities[p.Id]; }
    private long ServiceYears(OfficerState p) => p.StatusStartYear is int year ? (long)World.Clock.Year - year + 1 : 0;
    private ImmutableArray<OfficerState> Own { get { Refresh(); return _own; } }
    private ImmutableArray<SettlementState> Places { get { Refresh(); return _places; } }
    private bool Available(OfficerState p)
    {
        Refresh();
        return p.IsServing && p.OwnerId == _corps.OwnerId && !p.Acted && p.UnitId is null &&
            p.SettlementId is int home && _placeIds.Contains(home) && !_busy.Contains(p.Id);
    }
    private IEnumerable<OfficerState> Actors(int politics) => Own.Where(p => Available(p) && engine.CanStartSettlementWork(p.SettlementId!.Value) &&
        _cityIds.Contains(p.SettlementId!.Value) && Ability(p).Politics >= politics && Ability(p).Politics <= 115)
        .OrderByDescending(p => Ability(p).Politics);
    private GameCommand Command(Func<Guid, int, long, GameCommand> create)
    {
        long sequence = World.Simulation.NextSequence;
        return create(SimulationIdentity.Command(sequence, Owner), Owner, sequence);
    }
    private bool Legal(GameCommand command) => engine.GetPreparationEligibility(context, command) == CommandRejection.None;
    private int Relation(int? other) => other is int id && World.Forces.Any(f => f.Id == id && !f.IsEliminated)
        ? World.Relations.FirstOrDefault(r => r.FirstId == Math.Min(id, Owner) && r.SecondId == Math.Max(id, Owner))?.Value ?? 100 : 100;
    private IEnumerable<TreasureState> Holdings(int person) => World.Treasures.Where(t => t.Discovered && t.HolderId == person);
    private int Threshold(OfficerState target) => Ruler is { } ruler ? engine.GetRecruitmentThreshold(ruler.Id, target.Id) : 0;
    private bool ConfiscatableCaptive(OfficerState person) => Detained(person) && person.LoyaltyTo(Owner) <= 60 &&
        (person.OwnerId is not int origin || !World.Forces.Any(f => f.Id == origin && !f.IsEliminated) || Relation(origin) / 20 <= 0);

    internal static bool Handles(OriginalAiWork work) => work is OriginalAiWork.Adviser or OriginalAiWork.Offices or
        OriginalAiWork.Reward or OriginalAiWork.GrantTreasure or OriginalAiWork.ConfiscateTreasure or
        OriginalAiWork.MoveOfficers or OriginalAiWork.Search or OriginalAiWork.RecruitLocal or OriginalAiWork.RecruitEnemy;

    internal OriginalAiWorkReport Run(OriginalAiWork work, int limit, OriginalAiState state,
        IReadOnlyDictionary<int, long> quotas)
    {
        int used = 0, submitted = 0, rejected = 0, empty = 0;
        OriginalAiWorkReport Report(OriginalAiWorkEnd end = OriginalAiWorkEnd.NoCandidates) =>
            new(corpsId, work, end, used, submitted) { Rejected = rejected, EmptyPlans = empty };
        void Send(GameCommand command)
        {
            used++;
            if (submit(command)) submitted++; else rejected++;
        }
        switch (work)
        {
            case OriginalAiWork.Adviser:
                var ordinary = Own.Where(p => p.Status == OfficerStatus.Serving).ToArray();
                if (ordinary.Length < 5) return Report(OriginalAiWorkEnd.PreparationFailed);
                var current = World.Officers.FirstOrDefault(p => p.Id == Force.AdviserId);
                int lower = Math.Max(80, Math.Min(current is null ? 80 : Ability(current).Intelligence + 1, 115));
                var candidates = ordinary.Where(p => p.Loyalty >= 80 && Ability(p).Intelligence >= lower && Ability(p).Intelligence <= 115);
                if (current is not null) candidates = candidates.Append(current);
                var adviser = candidates.OrderByDescending(p => Ability(p).Intelligence)
                    .ThenByDescending(ServiceYears).FirstOrDefault();
                if (adviser is not null && adviser.Id != current?.Id) Send(Command((id, f, seq) => new AppointAdviserCommand(id, f, seq, adviser.Id)));
                break;
            case OriginalAiWork.Offices:
                var assignments = Offices();
                if (!assignments.IsEmpty) Send(Command((id, f, seq) => new AssignOfficesCommand(id, f, seq, assignments)));
                break;
            case OriginalAiWork.Reward:
                // The source task emits one batch. Each public reward still pays its own root account.
                int budget = (int)Math.Min(20, Math.Max(0, ((long)Corps.Treasury.Money - 2000) / 100));
                foreach (var p in Own.Where(p => p.Loyalty < 100 && !p.RewardedThisXun)
                    .OrderByDescending(p => RewardScore(p)).ToArray())
                {
                    if (used >= budget) break;
                    var command = Command((id, f, seq) => new RewardOfficerCommand(id, f, seq, p.Id));
                    if (Legal(command)) Send(command);
                }
                break;
            case OriginalAiWork.GrantTreasure:
                if (Ruler is not { } ruler || ruler.Righteousness <= 5 && ruler.Ambition >= 11) return Report(OriginalAiWorkEnd.PreparationFailed);
                var recipient = Own.Where(p => p.Id != ruler.Id && p.Loyalty is >= 70 and <= 100)
                    .OrderBy(p => p.Loyalty).FirstOrDefault();
                if (recipient is null || Holdings(recipient.Id).Count() > 4 || Holdings(ruler.Id).Count() <= 2) return Report(OriginalAiWorkEnd.PreparationFailed);
                var pool = GrantPool(ruler.Id, recipient.Id);
                if (pool.Length != 0)
                {
                    int treasure = pool[random.Next(pool.Length)];
                    Send(Command((id, f, seq) => new GrantTreasureCommand(id, f, seq, treasure, recipient.Id)));
                }
                break;
            case OriginalAiWork.ConfiscateTreasure:
                var holder = World.Officers.Where(p => !p.IsDormant && p.Id != Ruler?.Id &&
                    (p.IsServing && p.OwnerId == Owner && p.Loyalty <= 50 || ConfiscatableCaptive(p)) &&
                    !(p.BloodlineId is int family && family == Ruler?.BloodlineId) && Holdings(p.Id).Count() >= 2)
                    .OrderByDescending(p => p.OwnerId == Owner ? 255 - p.Loyalty : 255 + p.Loyalty).FirstOrDefault();
                if (holder is null) break;
                var confiscatable = Holdings(holder.Id).Where(t => engine.GetTreasureDefinition(t.Id) is { } d && !d.FavoredOwnerIds.Contains(holder.Id))
                    .OrderByDescending(t => engine.GetTreasureDefinition(t.Id)!.LoyaltyBonus).ToArray();
                if (confiscatable.Length > 1) confiscatable = confiscatable.Skip(1).ToArray();
                if (confiscatable.Length != 0)
                {
                    int treasure = confiscatable[random.Next(confiscatable.Length)].Id;
                    Send(Command((id, f, seq) => new ConfiscateTreasureCommand(id, f, seq, treasure)));
                }
                break;
            case OriginalAiWork.MoveOfficers:
                foreach (var plan in Moves(quotas))
                {
                    if (used >= limit) break;
                    if (plan is { } move) Send(Command((id, f, seq) => new MoveOfficersCommand(id, f, seq, move.People, move.Destination)));
                    else { used++; empty++; }
                }
                break;
            case OriginalAiWork.Search:
            case OriginalAiWork.RecruitLocal:
            case OriginalAiWork.RecruitEnemy:
                var ownGroup = state.Forces.Single(f => f.ForceId == Owner).Group;
                if (work != OriginalAiWork.Search && ownGroup == OriginalAiGroup.ReducedActivity && random.Percent(50))
                    return Report(OriginalAiWorkEnd.ProbabilityFailed);
                var strategy = state.Corps.Single(c => c.CorpsId == corpsId);
                int? opponent = strategy.TargetForceId ?? World.Settlements.FirstOrDefault(s => s.Id == strategy.TargetSettlementId)?.OwnerId;
                if (work == OriginalAiWork.RecruitEnemy)
                {
                    if (opponent is not int enemy || !OriginalAiStrategy.IsOpponent(World, Owner, enemy)) return Report(OriginalAiWorkEnd.PreparationFailed);
                    if (state.Forces.Any(f => f.ForceId == opponent && f.Group == OriginalAiGroup.Favored) && random.Percent(75))
                        return Report(OriginalAiWorkEnd.ProbabilityFailed);
                }
                // A source with an available actor necessarily has a nonempty physical member list.
                var actors = Actors(work == OriginalAiWork.RecruitLocal ? 35 : 45).Select(p => p.Id).ToList();
                // Original work 23 filters actual undiscovered people before probing the shared search command.
                var undiscoveredPlaces = work == OriginalAiWork.Search ? World.Officers
                    .Where(p => !p.IsDormant && p.Status == OfficerStatus.Undiscovered)
                    .Select(p => p.SettlementId ?? p.RegionId).OfType<int>().ToHashSet() : null;
                var targets = work == OriginalAiWork.Search ? engine.GetSearchPlaces(Owner).Where(undiscoveredPlaces!.Contains).ToList() :
                    World.Officers.Where(p => !p.IsDormant && p.IsVisible && (work == OriginalAiWork.RecruitEnemy
                        ? p.OwnerId == opponent && p.Status is OfficerStatus.Governor or OfficerStatus.Strategist or OfficerStatus.Serving && p.Loyalty <= 90
                        : p.Status == OfficerStatus.Free || Detained(p)))
                    .OrderBy(p => work == OriginalAiWork.RecruitEnemy ? p.Loyalty : -Maximum(Ability(p))).Select(p => p.Id).ToList();
                if (work == OriginalAiWork.Search)
                    actors = actors.Where(actor => targets.Any(target => engine.GetPersonnelJourney(Owner, actor, target,
                        PersonnelJourneyKind.Search).DistanceDays is >= 0 and <= MaximumSearchJourneyDays)).ToList();
                while (used < limit && targets.Count != 0 && actors.Count != 0)
                {
                    (int Actor, int Target)? pair = null;
                    foreach (int target in targets)
                    {
                        var feasible = actors.Select(actor =>
                        {
                            var preview = engine.GetPersonnelJourney(Owner, actor, target,
                                work == OriginalAiWork.Search ? PersonnelJourneyKind.Search : PersonnelJourneyKind.Recruit);
                            int threshold = preview.Available && work != OriginalAiWork.Search ? preview.RecruitmentThreshold!.Value : 0;
                            int score = !preview.Available ? 0 : work == OriginalAiWork.Search
                                ? preview.Advice.Opinion == AdviserOpinion.Unfavorable ? 0 : 100
                                : preview.Advice.Opinion switch { AdviserOpinion.Favorable => 100, AdviserOpinion.Unfavorable => 0, _ => threshold };
                            return (Actor: actor, Score: score, Threshold: threshold);
                        }).Where(p => p.Score >= 20);
                        var selected = feasible.OrderByDescending(p => work == OriginalAiWork.Search
                            ? Ability(World.Officers.Single(o => o.Id == p.Actor)).Politics : p.Threshold).FirstOrDefault();
                        if (selected.Score < 20) continue;
                        pair = (selected.Actor, target); break;
                    }
                    if (pair is not { } chosen)
                    {
                        // No submission, state change or draw can make a later attempt feasible.
                        // Preserve the work's empty-attempt accounting without rescanning the same pairs.
                        empty += limit - used;
                        used = limit;
                        break;
                    }
                    actors.Remove(chosen.Actor); targets.Remove(chosen.Target);
                    Send(Command((id, f, seq) => work == OriginalAiWork.Search
                        ? new SearchCommand(id, f, seq, [chosen.Actor], chosen.Target)
                        : new RecruitOfficerCommand(id, f, seq, chosen.Actor, chosen.Target)));
                }
                break;
        }
        return Report(used >= limit && work != OriginalAiWork.Reward ? OriginalAiWorkEnd.AttemptsExhausted : OriginalAiWorkEnd.NoCandidates);
    }

    internal static long RewardScore(OfficerState p) => 255L * p.Loyalty + PersonnelRewardMath.Gain(p.Loyalty, p.Righteousness);
    internal static int Maximum(OfficerAbilities a) => Math.Max(Math.Max(a.War, a.Intelligence), Math.Max(a.Politics, a.Leadership));
    private static long Sum(OfficerAbilities a) => (long)a.War + a.Intelligence + a.Politics + a.Leadership;

    private sealed record OfficeSlot(int? Id, uint Capacity, int Priority, bool Special, bool HasBonus);

    private ImmutableArray<OfficeAssignment> Offices()
    {
        if (Ruler is not { } ruler) return [];
        var people = Own.Where(p => p.Id != ruler.Id && p.UnitId is null && p.SettlementId is not null &&
            !World.Simulation.Tasks.Any(t => t.OfficerId == p.Id && t.Journey is not null)).ToList();
        var available = engine.GetAvailableOffices(Owner).Select(o => o.OfficeId).ToHashSet();
        bool allPresent = people.Count == Own.Count();
        foreach (var p in Own.Where(p => !allPresent || !people.Contains(p)))
            if (p.OfficeId is int office) available.Remove(office);
        var offices = content.Information.Offices.Where(o => available.Contains(o.Id)).OrderByDescending(o => o.GovernorPriority)
            .Select(o => new OfficeSlot(o.Id, o.CommandCapacity, o.GovernorPriority, o.FillBeforeOrdinaryOffices,
                o.AbilityBonuses != default)).ToList();
        // The original no-office entry is a semantic unassignment option, never a content identity.
        offices.Add(new(null, 10000, 0, false, false));
        var assigned = new Dictionary<int, int?>();
        var remaining = people.Select(p => p.Id).ToHashSet();
        long SeniorityScore(OfficerState p) => ServiceYears(p) + (p.Loyalty >= 150 ? 5 : 0);
        void Fill(IEnumerable<OfficeSlot> slots, IEnumerable<OfficerState> candidates, Func<OfficerState, bool> eligible)
        {
            var ordered = new Queue<OfficerState>(candidates.Where(p => remaining.Contains(p.Id)));
            foreach (var office in slots.ToArray())
            {
                if (!ordered.TryDequeue(out var person)) break;
                remaining.Remove(person.Id);
                if (!eligible(person)) break;
                assigned[person.Id] = office.Id;
                offices.Remove(office);
            }
        }
        if (Available(ruler))
            Fill(offices.Where(o => o.Special),
                people.OrderByDescending(p => Ability(p).Leadership + Ability(p).Intelligence + SeniorityScore(p)), _ => true);
        Fill(offices.OrderByDescending(o => o.HasBonus ? -1 : o.Priority).TakeWhile(o => !o.HasBonus),
            people.OrderByDescending(p => Ability(p).Leadership + SeniorityScore(p)), p => Ability(p).Leadership >= 60);
        long payroll = OriginalAiProjection.SalaryDemand(World, Corps, content.Information.Offices.ToDictionary(o => o.Id, o => o.Salary));
        if (Corps.Treasury.Money > payroll)
            Fill(offices.OrderByDescending(o => o.Capacity == 10000 ? o.Priority : -1).TakeWhile(o => o.Capacity <= 10000),
                people.OrderByDescending(p => Ability(p).Intelligence + Ability(p).Politics + SeniorityScore(p)),
                p => Math.Max(Ability(p).Intelligence, Ability(p).Politics) >= 70);
        return assigned.Where(a => World.Officers.Single(p => p.Id == a.Key).OfficeId != a.Value)
            .Select(a => new OfficeAssignment(a.Key, a.Value)).ToImmutableArray();
    }
    private int[] GrantPool(int holder, int recipient)
    {
        var items = Holdings(holder).Select(t => engine.GetTreasureDefinition(t.Id)).OfType<TreasureRule>();
        return DiplomacyTreasures.Candidates(World.Treasures, new(items), holder, recipient).ToArray();
    }

    private IEnumerable<(ImmutableArray<int> People, int Destination)?> Moves(IReadOnlyDictionary<int, long> quotas)
    {
        var places = Places.ToArray();
        // Actual occupancy and the batch's all-serving quota are different inputs.
        var counts = OriginalAiStaffing.Counts(World, corpsId);
        var surplus = places.ToDictionary(s => s.Id, s => counts.GetValueOrDefault(s.Id) - quotas.GetValueOrDefault(s.Id));
        var sources = places.Where(s => surplus[s.Id] > 0 && engine.CanStartSettlementWork(s.Id)).ToList();
        if (sources.Count == 0 && !places.Any(s => counts.GetValueOrDefault(s.Id) > 1 && engine.CanStartSettlementWork(s.Id))) yield break;
        var destinations = places.Where(s => surplus[s.Id] < 0).OrderBy(s => surplus[s.Id]).ToArray();
        foreach (var destination in destinations)
        {
            var available = Own.Where(p => Available(p) && engine.CanStartSettlementWork(p.SettlementId!.Value)).ToLookup(p => p.SettlementId!.Value);
            var source = sources.Where(s => available.Contains(s.Id)).OrderByDescending(s => surplus[s.Id]).FirstOrDefault();
            bool fallback = source is null && destination.Kind == SettlementKind.City && counts.GetValueOrDefault(destination.Id) == 0;
            // B11 repairs the original fallback's reuse of the exhausted surplus list.
            // Emergency staffing may cross the quota, but must leave a resident behind.
            if (fallback) source = places.Where(s => s.Id != destination.Id && counts.GetValueOrDefault(s.Id) > 1 && available.Contains(s.Id))
                .OrderByDescending(s => counts.GetValueOrDefault(s.Id)).FirstOrDefault();
            if (source is null) { yield return null; continue; }
            IEnumerable<OfficerState> people = available[source.Id];
            bool frontier = geography.Priority(destination.Id, Owner);
            long Capacity(OfficerState p) => content.Information.Offices.FirstOrDefault(o => o.Id == p.OfficeId)?.CommandCapacity ?? 10000;
            long Score(OfficerState p) => frontier && destination.Kind != SettlementKind.City ? Ability(p).War - Ability(p).Politics + 115L :
                !frontier && destination.Kind == SettlementKind.City ? Capacity(p) + Ability(p).War - Ability(p).Politics + 115 : Capacity(p) + Maximum(Ability(p));
            people = geography.Priority(source.Id, Owner) ? people.OrderBy(p => Sum(Ability(p))) : people.OrderByDescending(p => Sum(Ability(p)));
            people = fallback ? people.OrderByDescending(p => -(content.Information.Offices.FirstOrDefault(o => o.Id == p.OfficeId)?.GovernorPriority ?? 0) + 2L * Ability(p).Politics - Ability(p).War)
                : frontier ? people.OrderByDescending(Score) : people.OrderBy(Score);
            int count = fallback ? 1 : (int)Math.Min(10, Math.Min(surplus[source.Id], -surplus[destination.Id]));
            var selected = people.Take(count).Select(p => p.Id).ToImmutableArray();
            if (selected.IsEmpty) { sources.Remove(source); yield return null; continue; }
            surplus[source.Id] -= selected.Length;
            if (surplus[source.Id] <= 0) sources.Remove(source);
            var before = World;
            yield return (selected, destination.Id);
            if (!ReferenceEquals(before, World)) counts[source.Id] -= selected.Length;
        }
    }

    private bool Detained(OfficerState p) => p.Status == OfficerStatus.Captive &&
        (p.SettlementId is int home && World.Settlements.Any(s => s.Id == home && s.OwnerId == Owner) ||
         p.UnitId is int unit && World.Units.Any(u => u.Id == unit && u.OwnerId == Owner));

    internal static PrisonerDisposition Disposition(int threshold, int relation, int affinity, OfficerAbilities abilities) =>
        threshold < 1 && relation >= 60 ? PrisonerDisposition.Release :
        threshold < 1 && relation <= 39 && Maximum(abilities) <= 89 && affinity > 40 || threshold >= 1 && relation < 10
            ? PrisonerDisposition.Execute : PrisonerDisposition.Retain;

    internal OriginalPrisonerProgress PreparePrisoners()
    {
        var execute = new List<int>();
        var release = new List<int>();
        foreach (var p in World.Officers.Where(p => !p.IsDormant && Detained(p)).OrderBy(p => Sum(Ability(p))))
        {
            var disposition = Disposition(Threshold(p), Relation(p.OwnerId), Ruler is { } ruler ? PersonnelRulebook.AffinityDistance(p, ruler) : 0, Ability(p));
            if (disposition == PrisonerDisposition.Execute) execute.Add(p.Id);
            if (disposition == PrisonerDisposition.Release) release.Add(p.Id);
        }
        return new(execute.ToImmutableArray(), release.ToImmutableArray(), 0, 0, 0);
    }

    internal OriginalPrisonerProgress Dispose(OriginalPrisonerProgress progress, int limit)
    {
        bool emptyProduction = false;
        while (progress.Attempts < limit && (!progress.Execute.IsEmpty || !progress.Release.IsEmpty))
        {
            emptyProduction = true;
            bool execute = !progress.Execute.IsEmpty;
            int person = execute ? progress.Execute[0] : progress.Release[0];
            progress = execute ? progress with { Execute = progress.Execute.RemoveAt(0) } : progress with { Release = progress.Release.RemoveAt(0) };
            var prisoner = World.Officers.Single(p => p.Id == person);
            if (!Detained(prisoner)) continue;
            int chance = execute ? Math.Max(0, 40 - Relation(prisoner.OwnerId)) : 50;
            if (execute && !World.Simulation.Controls.Any(c => c.ForceId == prisoner.OwnerId && c.Kind == ControllerKind.Player)) chance /= 10;
            if (!random.Percent(chance)) continue;
            bool accepted = submit(Command((id, f, seq) => execute ? new ExecutePrisonerCommand(id, f, seq, person) : new ExileOfficerCommand(id, f, seq, person)));
            progress = progress with { Attempts = progress.Attempts + 1, Submitted = progress.Submitted + (accepted ? 1 : 0), Rejected = progress.Rejected + (accepted ? 0 : 1) };
            emptyProduction = false;
            if (World.Simulation.Boundary != SimulationBoundary.Stable) break;
        }
        if (emptyProduction) progress = progress with { Attempts = progress.Attempts + 1, EmptyPlans = progress.EmptyPlans + 1 };
        return progress;
    }
}

internal enum PrisonerDisposition { Retain, Execute, Release }
internal sealed record OriginalPrisonerProgress(ImmutableArray<int> Execute, ImmutableArray<int> Release, int Attempts, int Submitted, int Rejected)
{
    internal int EmptyPlans { get; init; }
}

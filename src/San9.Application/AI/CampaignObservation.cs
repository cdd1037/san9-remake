using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

/// <summary>Projects legal own actions and public military facts. The planner receives no world or engine handle.</summary>
internal sealed class CampaignObservationCapture : IDisposable
{
    private readonly SimulationEngine engine;
    private readonly long revision;
    private readonly IEnumerator<Action<AllocationWork>?> steps;
    private Func<CampaignObservation> snapshot = null!;
    internal bool Finished { get; private set; }
    private int decisionRounds;
    private Func<bool> hasCandidates = () => false;
    internal CampaignObservation Observation
    {
        get
        {
            var value = snapshot();
            return value with { Complete = Finished && value.Complete };
        }
    }

    internal CampaignObservationCapture(SimulationEngine engine, ControlSeat seat,
        ImmutableArray<(string ControllerId, EconomicObservation Observation)> economies,
        SupportObservation support, MilitaryRulebook rules, ArrivalProjectionCache routes,
        ImmutableArray<CampaignGoal> commitments = default)
    {
        this.engine = engine;
        revision = engine.Snapshot.Revision;
        steps = Project(engine, seat, economies, support, rules, routes, commitments).GetEnumerator();
        Finished = !steps.MoveNext();
    }

    internal void Advance(AllocationWork work, int maximumSteps = int.MaxValue, bool stopAtDecision = false)
    {
        if (engine.Snapshot.Revision != revision)
            throw new InvalidOperationException("Campaign observation cannot resume after the world changes.");
        int initialRound = decisionRounds;
        for (int count = 0; count < maximumSteps && !Finished && work.Spend(); count++)
        {
            steps.Current?.Invoke(work);
            Finished = !steps.MoveNext();
            if (stopAtDecision && decisionRounds > initialRound && hasCandidates()) break;
        }
    }

    public void Dispose() => steps.Dispose();

    internal static CampaignObservation Capture(SimulationEngine engine, ControlSeat seat,
        ImmutableArray<(string ControllerId, EconomicObservation Observation)> economies,
        SupportObservation support, MilitaryRulebook rules, AllocationWork work, ArrivalProjectionCache routes,
        ImmutableArray<CampaignGoal> commitments = default)
    {
        using var capture = new CampaignObservationCapture(engine, seat, economies, support, rules, routes, commitments);
        capture.Advance(work);
        return capture.Observation;
    }

    private IEnumerable<Action<AllocationWork>?> Project(SimulationEngine engine, ControlSeat seat,
        ImmutableArray<(string ControllerId, EconomicObservation Observation)> economies,
        SupportObservation support, MilitaryRulebook rules, ArrivalProjectionCache routes,
        ImmutableArray<CampaignGoal> commitments)
    {
        var world = engine.Snapshot;
        var abilities = engine.GetAbilities(world);
        var settlementsById = world.Settlements.ToDictionary(s => s.Id);
        var officersById = world.Officers.ToDictionary(p => p.Id);
        var defense = new GarrisonAssessment(world, seat.ForceId, rules, abilities, support.Arrivals);
        var force = world.Forces.Single(f => f.Id == seat.ForceId);
        var knownTactics = new KnownTactics(engine, world, seat.ForceId, rules);
        ImmutableArray<TacticRule> TacticsFor(int person) => knownTactics.For(officersById[person]);
        var crewsBySource = new Dictionary<int, ImmutableArray<(ImmutableArray<int> Members, bool Seed)>>();
        var equipmentCache = new Dictionary<(string Members, int Formation, uint Soldiers, uint Defenders), CampaignEquipment>();
        var trainingCache = new Dictionary<(int Source, uint Soldiers, int Morale, string Members), TrainingPreparation[]>();
        var routePlanner = new CampaignRoutePlanner(world.Settlements.Where(s => s.OwnerId == seat.ForceId &&
            !s.IsDormant && !s.IsDestroyed && rules.IsHarbor(s)).OrderBy(s => s.Id).Select(rules.SettlementArea).ToImmutableArray(), routes);
        var occupancyByOwner = new Dictionary<int, ImmutableArray<MapOccupancy>>();
        ImmutableArray<MapOccupancy> Occupancy(int owner)
        {
            if (occupancyByOwner.TryGetValue(owner, out var saved)) return saved;
            return occupancyByOwner[owner] = MilitaryRouteQuery.Occupancy(world, rules, owner, new(), includeUnits: false).ToImmutableArray();
        }
        var accounts = economies.SelectMany(e => e.Observation.Accounts).DistinctBy(a => a.Id).ToImmutableArray();
        var controlled = economies.SelectMany(e => e.Observation.Domestic.Settlements.Select(s => (e.ControllerId, Place: s))).ToArray();
        var places = ImmutableArray.CreateBuilder<CampaignPlace>();
        var forces = ImmutableArray.CreateBuilder<CampaignForce>();
        var waves = ImmutableArray.CreateBuilder<CampaignWave>();
        var returns = ImmutableArray.CreateBuilder<AllocationCandidate>();
        var consolidation = ImmutableArray.CreateBuilder<AllocationCandidate>();
        var preparations = ImmutableArray.CreateBuilder<AllocationCandidate>();
        var sources = ImmutableArray.CreateBuilder<CampaignSource>();
        bool analysisReady = false;
        var unresolvedUnits = world.Units.Where(u => u.OwnerId == seat.ForceId && !u.IsEmbedded && !u.IsPersonnel &&
            u.FormationId is not null && rules.Formation(u).Combat is not null).Select(u => u.Id).ToHashSet();
        bool complete = support.Complete || support.DefenseComplete;
        CampaignObservation Result() => new(world.Clock, seat.ForceId, places.ToImmutable(), forces.ToImmutable(), waves.ToImmutable(),
            returns.ToImmutable(), consolidation.ToImmutable(), accounts, complete)
            { Preparations = preparations.ToImmutable(), Sources = sources.ToImmutable(), AnalysisReady = analysisReady, UnresolvedUnitIds = unresolvedUnits.Order().ToImmutableArray(), ActiveUnitIds = world.Units.Where(u => u.OwnerId == seat.ForceId).Select(u => u.Id).ToImmutableArray(),
                Purchases = economies.SelectMany(e => e.Observation.Purchases.Select(p => new CampaignPurchase(e.ControllerId, p))).ToImmutableArray(),
                WaitingSources = world.Simulation.Tasks.Where(t => t.ForceId == seat.ForceId && t.Kind == "personnel-move")
                    .Select(t => t.Journey?.DestinationPlaceId).OfType<int>().Concat(world.Units.Where(u => u.OwnerId == seat.ForceId &&
                        u.Order is { Kind: MilitaryOrderKind.Transport, SettlementId: not null }).Select(u => u.Order!.SettlementId!.Value))
                    .Distinct().Order().ToImmutableArray() };
        snapshot = Result;
        hasCandidates = () => forces.Count > 0 || preparations.Count > 0;
        bool Enemy(int? owner) => owner != seat.ForceId && (owner is not int other ||
            world.Simulation.Alliance?.Protects(seat.ForceId, other) != true &&
            !world.Simulation.AttackRequests.Any(r => r.Connects(seat.ForceId, other)) &&
            (world.Relations.FirstOrDefault(r => r.FirstId == Math.Min(other, seat.ForceId) && r.SecondId == Math.Max(other, seat.ForceId))?.Value ?? 0) < 40);
        bool Authorized(string controller, SettlementState target)
        {
            var delegation = controller.StartsWith("corps:", StringComparison.Ordinal)
                ? world.Corps.Single(c => CorpsControl.ControllerId(c.Id) == controller).Delegation : new GeneralDelegation();
            return delegation switch { DomesticDelegation => false, CaptureSettlementDelegation d => target.Id == d.SettlementId,
                DefeatForceDelegation d => target.OwnerId == d.ForceId, _ => true };
        }
        var eligibility = new Dictionary<string, Dictionary<GameCommand, bool>>(StringComparer.Ordinal);
        var queryId = SimulationIdentity.Command(world.Simulation.NextSequence, seat.ForceId);
        bool Legal(string controller, GameCommand intent)
        {
            if (!eligibility.TryGetValue(controller, out var commands))
                eligibility.Add(controller, commands = new(PlanningIntentEquality.Instance));
            if (commands.TryGetValue(intent, out bool accepted)) return accepted;
            var query = intent with { CommandId = queryId,
                Sequence = world.Simulation.NextSequence };
            return commands[intent] = engine.GetPreparationEligibility(new(controller), query) == CommandRejection.None;
        }
        ArrivalEstimate Route(AllocationWork work, WorldPosition from, int formation, int? naval, SettlementState target, int owner,
            int preparationDays = 0, int remainder = 0, WorldPosition? waypoint = null, NavigationLookahead? lookahead = null)
        {
            // Close approach is required to finish a siege; maximum ranged reach alone is insufficient.
            var navigation = work;
            var estimate = routes.Evaluate(new(rules.Travel(formation, naval), from, rules.SettlementArea(target),
                Occupancy(owner),
                PreparationDays: preparationDays, MovementRemainder: remainder, Waypoint: waypoint, Lookahead: lookahead,
                AllowTargetOverlap: target.OwnerId == owner), navigation);
            if (estimate.Status == ArrivalEstimateStatus.Incomplete) complete = false;
            return estimate;
        }
        var publicPlaces = world.Settlements.Where(s => !s.IsDormant && !s.IsDestroyed && s.CanHostPersons &&
            (s.OwnerId == seat.ForceId || Enemy(s.OwnerId)) && rules.TryPlace(s, out var p) && p.Combat is not null).OrderBy(s => s.Id).ToArray();
        var activeTargets = commitments.IsDefault ? [] : commitments.Where(g => g.ForceId == seat.ForceId).Select(g => g.TargetId).ToArray();
        var ongoing = commitments.IsDefault ? [] : commitments.Where(g => g.ForceId == seat.ForceId &&
            g.Phase != CampaignPhase.CoolingDown).ToArray();
        bool NeedsNewForce(SettlementState target) => !ongoing.Any(g => g.TargetId == target.Id &&
            (g.ExpectedOwnerId != target.OwnerId || g.Phase is CampaignPhase.Withdrawing or CampaignPhase.Consolidating));
        // Work limits expansion, not the set of legal objectives. A distant objective must not disappear
        // permanently simply because six nearer places exist.
        var targets = publicPlaces;
        long NearestControlled(SettlementState target) => controlled.Select(c =>
            MapCoordinates.Distance(rules.Place(settlementsById[c.Place.Id]).Position,
                rules.Place(target).Position)).DefaultIfEmpty(long.MaxValue).Min();
        foreach (var target in targets.OrderBy(NearestControlled).ThenBy(s => s.Id))
        {
            yield return null;
            // Foreign bases expose base abilities of visible members, never treasure modifiers or private tasks.
            int leadership = world.Officers.Where(p => p.IsServing && p.SettlementId == target.Id &&
                (target.OwnerId == seat.ForceId || p.IsVisible)).Select(p => target.OwnerId == seat.ForceId
                    ? abilities[p.Id].Leadership : p.BaseAbilities.Leadership).DefaultIfEmpty(50).Max();
            places.Add(new(target.Id, target.OwnerId, target.Soldiers, target.Morale, target.Durability, target.MaxDurability,
                target.PublicOrder, leadership, rules.Place(target).Combat!, 15000L + target.Commerce * 10L + target.Cultivation * 10L)
                { CanRetaliate = target.OwnerId is not null && rules.FacilityAttack(target).Attack is not null });
        }
        var formations = rules.Formations.Values.Where(f => f.Combat is not null && f.Attack is not null &&
            rules.CanSelectFormation(force, f.Id) && rules.ValidUnitFormations(f.Id, rules.NavalFormationForDeployment(f.Id)))
            .OrderBy(f => f.DeploymentCost).ThenBy(f => f.Id).ToArray();
        foreach (var step in routes.Situation(world, seat.ForceId, rules).ProjectSteps(targets, rules,
            includeGarrisons: true, attackRange: false,
            threat => waves.Add(new(threat.TargetId, threat.EarliestDays, threat.Source.Soldiers))))
            yield return _ => step?.Invoke();
        analysisReady = support.Complete || support.DefenseComplete;
        var travelingPeople = world.Simulation.Tasks.Where(t => t.Journey is not null).Select(t => t.OfficerId).ToHashSet();
        foreach (var (_, home) in controlled)
        {
            yield return null;
            var source = settlementsById[home.Id];
            var guards = world.Officers.Where(p => p.OwnerId == seat.ForceId && p.IsServing && p.SettlementId == home.Id && !travelingPeople.Contains(p.Id))
                .Select(p => new CampaignGuard(p.Id, abilities[p.Id].Leadership)).ToImmutableArray();
            sources.Add(new(source.Id, source.Soldiers, source.Wounded, source.ConsumesFood, guards,
                guards.Select(g => g.Leadership).Append(50).Distinct().ToImmutableDictionary(l => l, l => defense.NeedWithLeadership(source, l))));
        }
        foreach (var (controller, home) in controlled.Where(c => activeTargets.Contains(c.Place.Id)))
        {
            var people = home.OfficerIds;
            var account = accounts.Single(a => a.Id == home.AccountId);
            // Capture survivors become normal residents; repair and patrol use exactly the same public commands.
            foreach (var activity in new[] { DomesticActivity.Patrol, DomesticActivity.Repair, DomesticActivity.Train })
            {
                yield return null;
                if (people.Length == 0 || home.UnavailableActivities.Contains(activity) || activity == DomesticActivity.Patrol && home.PublicOrder >= 500 ||
                    activity == DomesticActivity.Repair && home.Durability >= home.MaxDurability / 2 ||
                    activity == DomesticActivity.Train && home.Morale >= 65) continue;
                int actor = people.OrderByDescending(id => activity == DomesticActivity.Train ? abilities[id].Leadership : abilities[id].Politics).First();
                var command = new DevelopCommand(Guid.Empty, seat.ForceId, 0, new(seat.ForceId, home.Id, [actor], activity));
                yield return null;
                if (Legal(controller, command)) consolidation.Add(new(controller, account.Id, command, [actor], 1, 1000, activity == DomesticActivity.Train ? 0 : 150, 0,
                    home.Id, home.Id, Purpose: "consolidate"));
            }
        }
        foreach (var unit in world.Units.Where(u => u.OwnerId == seat.ForceId && !u.IsEmbedded && !u.IsPersonnel &&
            u.FormationId is not null && rules.Formation(u).Combat is not null).OrderBy(u => u.Id))
        {
            yield return null;
            var scope = economies.FirstOrDefault(e => CorpsControl.CanControlUnit(world, seat, new(e.ControllerId), unit.Id));
            if (scope.Observation is null) { unresolvedUnits.Remove(unit.Id); continue; }
            bool unitComplete = true;
            int accountId = unit.CorpsId ?? world.Corps.Single(c => c.OwnerId == seat.ForceId && c.IsRoot).Id;
            bool returning = unit.Order is { Kind: MilitaryOrderKind.Return, SettlementId: int destination } &&
                targets.Any(t => t.Id == destination && t.OwnerId == seat.ForceId);
            var consolidationTargets = commitments.IsDefault ? [] : commitments.Where(g => g.ForceId == seat.ForceId &&
                g.UnitIds.Contains(unit.Id)).Select(g => g.TargetId).ToArray();
            foreach (var home in targets.Where(t => t.OwnerId == seat.ForceId && !returning && consolidationTargets.Length > 0)
                .OrderBy(t => consolidationTargets.Contains(t.Id) ? 0 : 1)
                .ThenBy(t => MapCoordinates.Distance(unit.Position, rules.Place(t).Position)).ThenBy(t => t.Id))
            {
                yield return null;
                var retreat = new OrderUnitCommand(Guid.Empty, seat.ForceId, 0, unit.Id, new(MilitaryOrderKind.Return, null, home.Id));
                yield return null;
                if (!Legal(scope.ControllerId, retreat)) continue;
                ArrivalEstimate estimate = null!;
                yield return work => estimate = Route(work, unit.Position, unit.FormationId!.Value, unit.NavalFormationId, home, seat.ForceId);
                if (estimate.Status != ArrivalEstimateStatus.Known) continue;
                returns.Add(new(scope.ControllerId, accountId, retreat, [], 0, unit.Soldiers, 0, 0, unit.ServicePlaceId, home.Id, estimate,
                    Purpose: "withdraw:" + unit.Id));
                break;
            }
            if (unit.Order is not null && unit.Order.Kind != MilitaryOrderKind.AttackSettlement)
            { unresolvedUnits.Remove(unit.Id); continue; }
            foreach (var target in targets.Where(t => Enemy(t.OwnerId) && Authorized(scope.ControllerId, t) &&
                (unit.Order is null || t.Id == unit.Order.SettlementId)))
            {
                bool needsOrder = unit.Order is null;
                var repair = new OrderUnitCommand(Guid.Empty, seat.ForceId, 0, unit.Id, new(MilitaryOrderKind.AttackSettlement, null, target.Id));
                if (needsOrder) yield return null;
                if (needsOrder && !Legal(scope.ControllerId, repair)) continue;
                ArrivalEstimate arrival = null!;
                yield return work => arrival = Route(work, unit.Position, unit.FormationId!.Value, unit.NavalFormationId, target, seat.ForceId,
                    Math.Max(unit.DepartureDaysRemaining, unit.Condition == UnitCondition.Confused ? unit.ConditionDaysRemaining : 0),
                    unit.MovementRemainder, unit.Waypoint, unit.Lookahead);
                if (arrival.Status == ArrivalEstimateStatus.Incomplete) unitComplete = false;
                if (arrival.Status != ArrivalEstimateStatus.Known) continue;
                var action = new AllocationCandidate(scope.ControllerId, accountId, needsOrder ? repair : new WaitCommand(Guid.Empty, seat.ForceId, 0), [], 2, 0, 0, 0,
                    unit.ServicePlaceId, target.Id, arrival, Purpose: "campaign");
                int leadership = world.Officers.Where(p => p.UnitId == unit.Id).Select(p => abilities[p.Id].Leadership).DefaultIfEmpty(50).Max();
                yield return null;
                var unitMembers = world.Officers.Where(p => p.IsServing && p.UnitId == unit.Id && p.OwnerId == seat.ForceId).ToImmutableArray();
                var currentFormation = rules.Formation(unit);
                var estimate = CampaignEquipmentSearch.Estimate(unitMembers, abilities,
                    unitMembers.ToImmutableDictionary(p => p.Id, p => TacticsFor(p.Id)), currentFormation, rules.LinkRules,
                    unitMembers.ToImmutableDictionary(p => p.Id, p => p.EquippedTacticId),
                    unitMembers.ToImmutableDictionary(p => p.Id, p => rules.Rank(unit, p.Id)),
                    (currentFormation.Tactics?.AutomaticTactics ?? []).Select(id => rules.Tactics[id]).ToImmutableArray());
                forces.Add(new(action, unit.Soldiers, unit.Morale, leadership, rules.Formation(unit).Combat!, rules.Formation(unit).Attack?.DamagesWalls == true,
                    unit.Id, NeedsOrder: needsOrder) { Tactics = estimate });
            }
            if (unitComplete) unresolvedUnits.Remove(unit.Id);
        }
        // First compare routes and deliverable resources. Detailed crews belong only to relevant directions.
        var approaches = new List<(int Source, int Target, int Days, long Power)>();
        decimal DirectionValue(int targetId, int days, int mode)
        {
            var target = settlementsById[targetId];
            decimal value = places.First(p => p.Id == targetId).Value;
            return mode switch
            {
                0 => 1m / (10 + days),
                1 => value / (10 + days),
                _ => value / ((10m + days) * (1000m + target.Soldiers + target.Durability * 100m))
            };
        }

        var routeProfiles = formations.GroupBy(f => (f.TravelMode, f.Footprint))
            .Select(g => g.OrderByDescending(f => f.MovementBudget).ThenBy(f => f.Id).First()).ToArray();
        foreach (var (controller, home) in controlled)
        foreach (var target in targets.Where(t => Enemy(t.OwnerId) && NeedsNewForce(t) && Authorized(controller, t))
            .OrderBy(t => MapCoordinates.Distance(rules.Place(settlementsById[home.Id]).Position, rules.Place(t).Position)).ThenBy(t => t.Id))
        {
            yield return null;
            var source = settlementsById[home.Id];
            int earliest = routeProfiles.Select(f => NavigationArrivalForecast.EarliestDays(rules.Travel(f.Id, rules.NavalFormationForDeployment(f.Id)),
                rules.Place(source).Position, rules.SettlementArea(target))).DefaultIfEmpty(int.MaxValue).Min();
            if (!activeTargets.Contains(target.Id) && Enumerable.Range(0, 3).All(mode => approaches.Any(a => a.Source == source.Id &&
                DirectionValue(a.Target, a.Days, mode) >= DirectionValue(target.Id, earliest, mode)))) continue;
            int? days = null;
            foreach (var profile in routeProfiles)
            {
                int? naval = rules.NavalFormationForDeployment(profile.Id);
                var travel = rules.Travel(profile.Id, naval);
                var start = MilitaryRouteQuery.DeploymentPosition(travel, rules.SettlementArea(source), Occupancy(seat.ForceId));
                if (start is null) continue;
                ArrivalEstimate arrival = new(ArrivalEstimateStatus.Incomplete);
                yield return work => arrival = routes.Evaluate(new(travel, start.Value, rules.SettlementArea(target), Occupancy(seat.ForceId)), work, sharedTerrain: true);
                if (arrival.Status == ArrivalEstimateStatus.Incomplete) complete = false;
                if (arrival.Status == ArrivalEstimateStatus.Known) days = Math.Min(days ?? int.MaxValue, arrival.Days!.Value);
            }
            if (days is null) continue;
            long spare = Math.Max(0, source.Soldiers - (long)defense.Need(source).Required);
            int leadership = home.OfficerIds.Select(id => abilities[id].Leadership).DefaultIfEmpty(0).Max();
            approaches.Add((home.Id, target.Id, days.Value, spare * leadership));
        }
        // Keep distinct time, strategic return and resistance advantages, rather than all geographical pairs.
        var relevant = approaches.Where(a => activeTargets.Contains(a.Target) || Enumerable.Range(0, 3).Any(mode =>
            !approaches.Any(b => b.Source == a.Source && DirectionValue(b.Target, b.Days, mode) > DirectionValue(a.Target, a.Days, mode)))).ToArray();
        var fronts = relevant.Where(a => a.Days <= relevant.Where(b => b.Target == a.Target).Min(b => b.Days) + 9 ||
            !relevant.Any(b => b.Target == a.Target && b.Days < a.Days && b.Power >= a.Power)).ToArray();
        var projections = new List<IEnumerator<Action<AllocationWork>?>>();
        Action<AllocationWork> nextCrew = _ => { };
        foreach (var approach in fronts.OrderBy(a => activeTargets.Contains(a.Target) ? 0 : 1).ThenBy(a => a.Days).ThenBy(a => a.Target).ThenBy(a => a.Source))
        {
            var home = controlled.Single(c => c.Place.Id == approach.Source);
            projections.Add(ProjectSource(settlementsById[approach.Target], home.ControllerId, home.Place).GetEnumerator());
        }
        // Rear-area resources fill a concrete front's missing force; they are never counted as already delivered.
        if (rules.TransportFormations is { } transportPair)
        foreach (var direction in fronts.GroupBy(a => a.Target))
        {
            var front = direction.OrderBy(a => a.Days).ThenByDescending(a => a.Power).ThenBy(a => a.Source).First();
            var destination = settlementsById[front.Source];
            var enemy = settlementsById[front.Target];
            long desired = Math.Min(destination.SoldierCapacity, defense.Need(destination).Required + Math.Max(1000L, enemy.Soldiers * 2L));
            long incoming = support.Arrivals.Where(a => !a.Hostile && a.UnitId is not null && a.TargetId == destination.Id && a.Estimate.Status == ArrivalEstimateStatus.Known)
                .Sum(a => (long)a.Soldiers);
            long missing = desired - destination.Soldiers - incoming;
            if (missing < 1000) continue;
            foreach (var (controller, home) in controlled.Where(c => c.Place.Id != destination.Id && !fronts.Any(a => a.Source == c.Place.Id && a.Target == front.Target) && Authorized(c.ControllerId, enemy)))
            {
                yield return null;
                var source = settlementsById[home.Id];
                int? actor = home.OfficerIds.OrderBy(id => abilities[id].Politics).ThenBy(id => id).Cast<int?>().FirstOrDefault();
                if (actor is not int person) continue;
                long spare = source.Soldiers - (long)defense.Need(source, person).Required;
                uint capacity = rules.Formations[transportPair.LandFormationId].SoldierCapacity;
                if (transportPair.NavalFormationId is int vessel) capacity = Math.Min(capacity, rules.Formations[vessel].SoldierCapacity);
                uint count = (uint)Math.Max(0, Math.Min(missing, Math.Min(spare, capacity)));
                if (count < 1000) continue;
                var travel = rules.Travel(transportPair.LandFormationId, transportPair.NavalFormationId);
                var start = MilitaryRouteQuery.DeploymentPosition(travel, rules.SettlementArea(source), Occupancy(seat.ForceId));
                if (start is null) continue;
                ArrivalEstimate arrival = new(ArrivalEstimateStatus.Incomplete);
                yield return work => arrival = routes.Evaluate(new(travel, start.Value, rules.SettlementArea(destination), Occupancy(seat.ForceId)), work, sharedTerrain: true);
                if (arrival.Status == ArrivalEstimateStatus.Incomplete) complete = false;
                if (arrival.Status != ArrivalEstimateStatus.Known) continue;
                var account = accounts.Single(a => a.Id == home.AccountId);
                int receiving = controlled.Single(c => c.Place.Id == destination.Id).Place.AccountId;
                var receiver = accounts.Single(a => a.Id == receiving);
                var command = new TransportUnitCommand(Guid.Empty, seat.ForceId, 0, source.Id, [person], count, destination.Id);
                if (!Legal(controller, command)) continue;
                long cost = rules.Formations[transportPair.LandFormationId].DeploymentCost +
                    (transportPair.NavalFormationId is int ship ? rules.Formations[ship].DeploymentCost : 0L);
                long food = account.Id == receiving ? FoodSupplyForecast.TransferExpenditure(source.Soldiers, destination.Soldiers, count,
                    arrival.Days!.Value, account.HorizonDays, source.Wounded, destination.Wounded, source.ConsumesFood, destination.ConsumesFood) :
                    FoodSupplyForecast.DeploymentExpenditure(source.Soldiers, count, account.HorizonDays, source.Wounded, source.ConsumesFood);
                preparations.Add(new(controller, account.Id, command, [person], 3, count - arrival.Days!.Value * 100L, cost, food,
                    source.Id, destination.Id, arrival, Purpose: "campaign-support")
                {
                    ReceivingSoldierTarget = (uint)desired,
                    ReceivingAccountId = receiving == account.Id ? null : receiving,
                    ReceivingFood = receiving == account.Id ? 0 : FoodSupplyForecast.ReinforcementExpenditure(destination.Soldiers, count,
                        Math.Max(0, receiver.HorizonDays - arrival.Days.Value), destination.Wounded, destination.ConsumesFood),
                    Campaign = new(seat.ForceId, controller, enemy.Id, enemy.OwnerId, CampaignPhase.Preparing, world.Clock, world.Clock,
                        enemy.Soldiers + enemy.Durability * 100L, arrival.Days.Value + front.Days, [], places.First(p => p.Id == enemy.Id).CaptureConditions())
                        { SourceIds = [source.Id, destination.Id] }
                });
            }
        }
        try
        {
            while (projections.Count > 0)
            {
                int roundEnd = projections.Count;
                for (int i = 0; i < roundEnd;)
                {
                    var projection = projections[i];
                    bool more;
                    do
                    {
                        more = projection.MoveNext();
                        if (more) yield return projection.Current;
                    } while (more && projection.Current != nextCrew);
                    if (more) i++;
                    else { projection.Dispose(); projections.RemoveAt(i); roundEnd--; }
                }
                decisionRounds++;
                yield return null;
            }
        }
        finally
        {
            foreach (var projection in projections) projection.Dispose();
        }

        IEnumerable<Action<AllocationWork>?> ProjectSource(SettlementState target, string controller, DomesticSettlementObservation home)
        {
            yield return null;
            var source = settlementsById[home.Id];
            var account = accounts.Single(a => a.Id == home.AccountId);
            var people = home.OfficerIds.OrderByDescending(id => abilities[id].Leadership).ThenBy(id => abilities[id].Politics).ThenBy(id => id).ToArray();
            // Visit all formations for a crew before expanding its size. Single-person, low-cost
            // alternatives remain available, and no administrative rank excludes a military option.
            if (!crewsBySource.TryGetValue(source.Id, out var crews))
                crewsBySource[source.Id] = crews = CampaignCrews.Enumerate(people,
                    formations.Select(f => f.OfficerCapacity).DefaultIfEmpty(0).Max(), abilities, TacticsFor).ToImmutableArray();
            for (int crewIndex = 0; crewIndex < crews.Length; crewIndex++)
            {
                var crew = crews[crewIndex].Members;
                var reducedForces = new List<CampaignForce>();
                foreach (var formation in CampaignFormations.Select(formations, crew, TacticsFor))
                {
                    yield return null;
                    int? naval = rules.NavalFormationForDeployment(formation.Id);
                    if (!rules.CanAttackTarget(formation.Attack, rules.Place(target).Position, true, rules.IsHarbor(target)) &&
                        (naval is not int shipAttack || !rules.CanAttackTarget(rules.Formations[shipAttack].Attack, rules.Place(target).Position, true, rules.IsHarbor(target)))) continue;
                    int memberCapacity = Math.Min(formation.OfficerCapacity, naval is int ship ? rules.Formations[ship].OfficerCapacity : formation.OfficerCapacity);
                    if (crew.Length > memberCapacity) continue;
                    var members = crew;
                    GameCommand? next = null;
                    int preparationDays = 0;
                    if (members.IsEmpty)
                    {
                        if (world.Simulation.Tasks.Any(t => t.Kind == "personnel-move" && t.Journey?.DestinationPlaceId == source.Id)) continue;
                        foreach (var donor in controlled.Where(d => d.ControllerId == controller && d.Place.Id != source.Id && d.Place.OfficerIds.Length >= 2))
                        {
                            yield return null;
                            int person = donor.Place.OfficerIds.OrderByDescending(id => abilities[id].Leadership).ThenBy(id => id).First();
                            var donorNeed = support.GarrisonRequirements.FirstOrDefault(r => r.SettlementId == donor.Place.Id && r.DepartingOfficerId == person);
                            if (donorNeed is null || donorNeed.Soldiers > donor.Place.Soldiers) continue;
                            var journey = engine.GetPersonnelJourney(seat.ForceId, person, source.Id, PersonnelJourneyKind.Move);
                            if (!journey.Available || journey.Days is not int days) continue;
                            members = [person];
                            preparationDays = Math.Max(10, (days + 9) / 10 * 10);
                            next = new MoveOfficersCommand(Guid.Empty, seat.ForceId, 0, members, source.Id);
                            break;
                        }
                    }
                    if (members.IsEmpty) continue;
                    uint reserve = defense.NeedAfterDeparture(source, members).Required;
                    // A near mobilization that can beat our departure's return window remains at the original base.
                    reserve = (uint)Math.Min(uint.MaxValue, Math.Max(reserve, waves.Where(w => w.TargetId == source.Id && w.Days <= 12).Sum(w => (long)w.Soldiers)));
                    uint soldiers = (uint)Math.Min(Math.Max(0, source.Soldiers - (long)reserve), Math.Min(formation.SoldierCapacity,
                        naval is int n ? rules.Formations[n].SoldierCapacity : formation.SoldierCapacity));
                    uint projectedGarrison = source.Soldiers;
                    long preparationCost = 0;
                    uint recruitment = next is null && source.Kind == SettlementKind.City && !source.RecruitUsed
                        ? (uint)DomesticDevelopment.RecruitCount(source, checked((uint)Math.Min(uint.MaxValue, account.Money)), members.Select(id => abilities[id]).ToArray()) : 0;
                    if (soldiers < 1000 && recruitment > 0)
                    {
                        projectedGarrison += recruitment;
                        soldiers = (uint)Math.Min(Math.Max(0, projectedGarrison - (long)reserve), Math.Min(formation.SoldierCapacity,
                            naval is int shipCapacity ? rules.Formations[shipCapacity].SoldierCapacity : formation.SoldierCapacity));
                        next = new DevelopCommand(Guid.Empty, seat.ForceId, 0, new(seat.ForceId, source.Id, members, DomesticActivity.Recruit) { RecruitAmount = recruitment });
                        preparationDays = 10;
                        preparationCost = (recruitment + 9L) / 10;
                    }
                    if (soldiers < 1000) continue;
                    var equipmentKey = (string.Join(",", members.Order()), formation.Id, soldiers, target.Soldiers);
                    if (!equipmentCache.TryGetValue(equipmentKey, out var equipment))
                    {
                        var search = new CampaignEquipmentSearch(members.Select(id => officersById[id]).ToImmutableArray(),
                            abilities, members.ToImmutableDictionary(id => id, TacticsFor), formation, rules.LinkRules, soldiers, target.Soldiers,
                            (formation.Tactics?.AutomaticTactics ?? []).Select(id => rules.Tactics[id]).ToImmutableArray());
                        foreach (var step in search.Steps()) yield return _ => step();
                        equipmentCache[equipmentKey] = equipment = search.Best;
                    }
                    var tactics = equipment.Tactics;
                    var command = new DeployUnitCommand(Guid.Empty, seat.ForceId, 0, source.Id, members, soldiers, formation.Id,
                        new(MilitaryOrderKind.AttackSettlement, null, target.Id)) { NavalFormationId = naval, Tactics = tactics,
                        Policies = new(AutonomousRetreat: true), Ranks = equipment.Ranks };
                    yield return null;
                    if (!Legal(controller, next ?? command)) continue;
                    // Preparation never bypasses a technology qualification on the future formation.
                    if (next is not null && new[] { formation, naval is int shipRule ? rules.Formations[shipRule] : formation }.Any(f =>
                        !rules.CanSelectFormation(force, f.Id) ||
                        f.RequiredTacticId is int required && !members.Any(id => TacticsFor(id).Any(t => t.Id == required)))) continue;
                    var occupancy = Occupancy(seat.ForceId);
                    var start = MilitaryRouteQuery.DeploymentPosition(rules.Travel(formation.Id, naval), rules.SettlementArea(source), occupancy);
                    if (start is null) continue;
                    CampaignRoute route = null!;
                    yield return work => route = routePlanner.Choose(new(rules.Travel(formation.Id, naval), start.Value, rules.SettlementArea(target),
                        occupancy, PreparationDays: preparationDays),
                        naval is int vesselId && rules.Formations[vesselId].MovementBudget > formation.MovementBudget, work);
                    var estimate = route.Arrival;
                    if (estimate.Status == ArrivalEstimateStatus.Incomplete) complete = false;
                    if (estimate.Status != ArrivalEstimateStatus.Known) continue;
                    reserve = (uint)Math.Min(uint.MaxValue, Math.Max(reserve,
                        waves.Where(w => w.TargetId == source.Id && w.Days <= estimate.Days + 12).Sum(w => (long)w.Soldiers)));
                    soldiers = Math.Min(soldiers, (uint)Math.Max(0, projectedGarrison - (long)reserve));
                    if (soldiers < 1000) continue;
                    // The selected route must survive into the public command. Execution validates the waypoint
                    // only for the chosen deployment, rather than exact-path probing every formation candidate.
                    command = command with { Soldiers = soldiers, Waypoint = route.Waypoint };
                    long cost = (long)formation.DeploymentCost + (naval is int vessel ? rules.Formations[vessel].DeploymentCost : 0);
                    long food = FoodSupplyForecast.DeploymentExpenditure(projectedGarrison, soldiers, account.HorizonDays, source.Wounded, source.ConsumesFood) +
                        FoodSupplyForecast.ReinforcementExpenditure(source.Soldiers, projectedGarrison - source.Soldiers, account.HorizonDays, source.Wounded, source.ConsumesFood);
                    var candidate = new AllocationCandidate(controller, account.Id, next ?? command, members, 2, 0, cost, food,
                        source.Id, target.Id, estimate, Purpose: "campaign")
                        { OfficerOpportunityCost = members.Sum(id => (long)abilities[id].Politics) * Math.Max(1, estimate.Days!.Value / 10), MoneyCost = cost + preparationCost };
                    int projectedMorale = projectedGarrison > source.Soldiers ? (int)((source.Soldiers * (long)source.Morale + recruitment * (long)EconomyMath.RecruitMorale(force.Trust)) / projectedGarrison) : source.Morale;
                    forces.Add(new(candidate, soldiers, projectedMorale, members.Max(id => abilities[id].Leadership),
                        formation.Combat!, formation.Attack!.DamagesWalls, TacticValue: equipment.Estimate.Damage / CampaignTacticEstimate.Scale)
                        { Tactics = equipment.Estimate });
                    if (next is null && !home.UnavailableActivities.Contains(DomesticActivity.Train))
                        foreach (var trainingStep in ProjectTraining()) yield return trainingStep;
                    IEnumerable<Action<AllocationWork>?> ProjectTraining()
                    {
                        var key = (source.Id, source.Soldiers, source.Morale, string.Join(",", members.Order()));
                        if (!trainingCache.TryGetValue(key, out var training))
                            trainingCache[key] = training = TrainingPlans.Enumerate(source.Soldiers, source.Morale, force.Trust,
                                home.OfficerIds, members, abilities).ToArray();
                        foreach (var preparation in training)
                        {
                            yield return null;
                            var trained = TrainingPlans.Schedule(candidate with
                            {
                                Arrival = estimate with { Days = estimate.Days + preparation.DepartureDays },
                                OfficerOpportunityCost = candidate.OfficerOpportunityCost + preparation.Trainers
                                    .Sum(id => TrainingPlans.OpportunityCost(abilities[id]))
                            }, preparation);
                            if (!Legal(controller, trained.Intent)) continue;
                            long trainingReserve = Math.Max(reserve, waves.Where(w => w.TargetId == source.Id &&
                                w.Days <= estimate.Days + preparation.DepartureDays + 12).Sum(w => (long)w.Soldiers));
                            if (soldiers + trainingReserve > source.Soldiers) continue;
                            forces.Add(new(trained, soldiers, preparation.Morale, members.Max(id => abilities[id].Leadership),
                                formation.Combat!, formation.Attack!.DamagesWalls,
                                TacticValue: equipment.Estimate.Damage / CampaignTacticEstimate.Scale) { Tactics = equipment.Estimate });
                            yield return null;
                        }
                    }
                    if (next is null)
                        foreach (uint amount in new[] { soldiers / 2, Math.Min(soldiers, Math.Max(1000u, target.Soldiers)), 1000u }
                            .Where(s => s >= 1000 && s < soldiers).Distinct())
                        {
                            reducedForces.Add(new(candidate with { Intent = command with { Soldiers = amount },
                                AdditionalFood = FoodSupplyForecast.DeploymentExpenditure(source.Soldiers, amount, account.HorizonDays, source.Wounded, source.ConsumesFood) },
                                amount, projectedMorale, members.Max(id => abilities[id].Leadership), formation.Combat!, formation.Attack!.DamagesWalls,
                                TacticValue: equipment.Estimate.Damage / CampaignTacticEstimate.Scale) { Tactics = equipment.Estimate });
                        }
                    if (next is null && recruitment > 0 && soldiers < formation.SoldierCapacity)
                    {
                        uint futureGarrison = checked(source.Soldiers + recruitment);
                        uint futureSoldiers = Math.Min(formation.SoldierCapacity, futureGarrison - reserve);
                        if (naval is int navalCapacity) futureSoldiers = Math.Min(futureSoldiers, rules.Formations[navalCapacity].SoldierCapacity);
                        var recruit = new DevelopCommand(Guid.Empty, seat.ForceId, 0, new(seat.ForceId, source.Id, members, DomesticActivity.Recruit) { RecruitAmount = recruitment });
                        yield return null;
                        if (Legal(controller, recruit)) forces.Add(new(candidate with { Intent = recruit, MoneyCost = cost + (recruitment + 9L) / 10,
                            Arrival = estimate with { Days = estimate.Days + 10 }, AdditionalFood = FoodSupplyForecast.DeploymentExpenditure(futureGarrison,
                                futureSoldiers, account.HorizonDays, source.Wounded, source.ConsumesFood) + FoodSupplyForecast.ReinforcementExpenditure(source.Soldiers,
                                recruitment, account.HorizonDays, source.Wounded, source.ConsumesFood) }, futureSoldiers,
                            (int)((source.Soldiers * (long)source.Morale + recruitment * (long)EconomyMath.RecruitMorale(force.Trust)) / futureGarrison),
                            members.Max(id => abilities[id].Leadership), formation.Combat!, formation.Attack!.DamagesWalls));
                    }
                }
                foreach (var alternative in reducedForces) { yield return null; forces.Add(alternative); }
                if (crewIndex > 0 || crews.Length == 1) yield return nextCrew;
            }
        }
        // Position value is optional and runs only after actionable forces and existing commitments.
        foreach (var target in targets.Where(t => t.OwnerId != seat.ForceId && ongoing.Length == 0))
        {
            long mobility = 0;
            if (target.OwnerId != seat.ForceId && rules.TransportFormations is { } transport)
                foreach (var home in targets.Where(s => s.OwnerId == seat.ForceId && s.Id != target.Id)
                    .OrderBy(s => MapCoordinates.Distance(rules.Place(target).Position, rules.Place(s).Position)).ThenBy(s => s.Id).Take(3))
                {
                    yield return null;
                    // Position value only ranks potential targets; it neither proves reachability nor funds an action.
                    int days = NavigationArrivalForecast.EarliestDays(rules.Travel(transport.LandFormationId, transport.NavalFormationId),
                        rules.Place(target).Position, rules.SettlementArea(home), movementRemainder: 0);
                    mobility += Math.Max(0, 30 - days) * 200L;

                }
            int index = Enumerable.Range(0, places.Count).First(i => places[i].Id == target.Id);
            places[index] = places[index] with { Value = places[index].Value + mobility };
        }
        yield break;
    }
}

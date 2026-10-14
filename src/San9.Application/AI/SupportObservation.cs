using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

public sealed record ArrivalProjection(int? UnitId, int? SourceId, int TargetId, ArrivalEstimate Estimate,
    uint Soldiers, long EffectiveStrength, bool Hostile, string Assumption);
public sealed record SupportObservation(ImmutableArray<AllocationCandidate> Candidates,
    ImmutableArray<ArrivalProjection> Arrivals, bool Complete)
{
    public ImmutableArray<AccountSupplyChange> FoodChanges { get; init; } = [];
    public ImmutableArray<GarrisonRequirement> GarrisonRequirements { get; init; } = [];
    public bool DefenseComplete { get; init; }
}
public sealed record GarrisonRequirement(int SettlementId, int? DepartingOfficerId, uint Soldiers);
public sealed record AccountSupplyChange(int AccountId, FoodSupplyChange Change);

public static class EconomicSupplyProjection
{
    public static ImmutableArray<(string ControllerId, EconomicObservation Observation)> Apply(
        ImmutableArray<(string ControllerId, EconomicObservation Observation)> economies, ImmutableArray<AccountSupplyChange> changes) =>
        economies.Select(e => (e.ControllerId, e.Observation with { Accounts = e.Observation.Accounts.Select(a => a with
            { RequiredFood = FoodSupplyForecast.RequiredFood(a.DailyConsumption, a.HorizonDays,
                changes.Where(c => c.AccountId == a.Id).Select(c => c.Change)) }).ToImmutableArray() })).ToImmutableArray();
}

internal sealed class ArrivalProjectionCache
{
    // Path geometry is independent of preparation, daily budget, movement remainder and navigation lookahead.
    // This cache lives for one control batch and survives unrelated world changes through its input values.
    private readonly Dictionary<(BattlefieldMap Map, Footprint Shape, TravelMode Mode, WorldPosition Start,
        MapOccupancy Target, OccupancyKey Occupancy, bool Adjacent, bool Rectangular, int Range),
        (RouteCostEstimate Estimate, long Allowance)> estimates = [];
    private MilitarySituation? situation;
    private WorldSnapshot? situationWorld;
    private int situationObserver;
    private MilitaryRulebook? situationRules;

    internal MilitarySituation Situation(WorldSnapshot world, int observer, MilitaryRulebook rules)
    {
        // Support and campaign share one frozen observation. Rebuild after any committed world change.
        if (ReferenceEquals(situationWorld, world) && situationObserver == observer && ReferenceEquals(situationRules, rules))
            return situation!;
        situation = MilitarySituation.Capture(world, observer, rules);
        situationWorld = world;
        situationObserver = observer;
        situationRules = rules;
        return situation;
    }

    private sealed class OccupancyKey(ImmutableArray<MapOccupancy> values) : IEquatable<OccupancyKey>
    {
        public bool Equals(OccupancyKey? other) => other is not null && values.AsSpan().SequenceEqual(other.Values.AsSpan());
        private ImmutableArray<MapOccupancy> Values => values;
        public override bool Equals(object? obj) => obj is OccupancyKey other && Equals(other);
        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (var value in values) hash.Add(value);
            return hash.ToHashCode();
        }
    }

    internal ArrivalEstimate Evaluate(NavigationArrivalInput input, AllocationWork work, bool sharedTerrain = false)
    {
        var travel = input.Travel;
        if (input.AllowTargetOverlap && input.Waypoint is null && new MapOccupancy(input.Start, travel.Shape).Overlaps(input.Target))
            return new(ArrivalEstimateStatus.Known, input.PreparationDays);
        var occupied = new OccupancyKey(input.Occupancy);
        RouteCostEstimate Leg(WorldPosition start, MapOccupancy target, bool adjacent, bool rectangular, int range)
        {
            var route = Search(sharedTerrain ? travel.Map.PlanningMap() : travel.Map, start, target, adjacent, rectangular, range);
            return sharedTerrain && route.Status == RouteReachability.Reachable
                ? route with { Cost = travel.Map.Reweight(route.TerrainCost) } : route;
        }
        RouteCostEstimate Search(BattlefieldMap map, WorldPosition start, MapOccupancy target, bool adjacent, bool rectangular, int range)
        {
            var key = (map, travel.Shape, travel.Mode, start, target, occupied, adjacent, rectangular, range);
            long allowance = Math.Min(work.Remaining, Math.Max(1, work.Maximum / 2));
            if (estimates.TryGetValue(key, out var saved) &&
                (saved.Estimate.Status != RouteReachability.Incomplete || allowance <= saved.Allowance)) return saved.Estimate;
            if (allowance == 0) return new(RouteReachability.Incomplete);
            var search = new AllocationWork(allowance);
            var estimate = map.EstimateTravelCost(start, target, travel.Shape, travel.Mode,
                input.Occupancy, search.Navigation(), adjacent, range, rectangular);
            work.Charge(allowance - search.Remaining);
            estimates[key] = (estimate, allowance);
            return estimate;
        }
        long cost = 0;
        var start = input.Start;
        if (input.Waypoint is { } waypoint)
        {
            var first = Leg(start, new(waypoint, new(1, 1)), false, false, 1);
            if (first.Status != RouteReachability.Reachable) return Result(first.Status);
            cost = first.Cost;
            start = waypoint;
        }
        var route = Leg(start, input.Target, input.Adjacent, input.RectangularReach, input.Range);
        if (route.Status != RouteReachability.Reachable) return Result(route.Status);
        cost = checked(cost + route.Cost);
        // Coarse route travel cost excludes avoidance penalties; actual daily passage is resolved during movement.
        long days = cost == 0 ? 0 : Math.Max(1, (Math.Max(0, cost - input.MovementRemainder) + travel.DailyBudget - 1) / travel.DailyBudget);
        return new(ArrivalEstimateStatus.Known, checked(input.PreparationDays + (int)days));

        static ArrivalEstimate Result(RouteReachability status) => new(status == RouteReachability.Incomplete
            ? ArrivalEstimateStatus.Incomplete : ArrivalEstimateStatus.Unreachable);
    }

}

/// <summary>Projects public force delivery and own personnel supply before pure allocation.</summary>
internal static class SupportObservationCapture
{
    internal static SupportObservation Capture(SimulationEngine engine, ControlSeat seat,
        ImmutableArray<(string ControllerId, EconomicObservation Observation)> economies,
        MilitaryRulebook? rules, AllocationWork work, ArrivalProjectionCache? cache = null)
    {
        if (rules is null) return new([], [], true);
        cache ??= new();
        var world = engine.Snapshot;
        var own = world.Settlements.Where(s => !s.IsDormant && !s.IsDestroyed && s.OwnerId == seat.ForceId &&
            s.CanHostPersons && s.Durability > 0 && rules.TryPlace(s, out _)).OrderBy(s => s.Id).ToArray();
        var controlled = economies.SelectMany(e => e.Observation.Domestic.Settlements.Select(s => (e.ControllerId, Settlement: s)))
            .ToDictionary(e => e.Settlement.Id);
        var accounts = economies.SelectMany(e => e.Observation.Accounts).DistinctBy(a => a.Id).ToDictionary(a => a.Id);
        var abilities = engine.GetAbilities(world);
        var residents = world.Officers.Where(p => p.OwnerId == seat.ForceId && p.IsServing).ToLookup(p => p.SettlementId);
        var traveling = world.Simulation.Tasks.Where(t => t.Journey is not null).Select(t => t.OfficerId).ToHashSet();
        var occupied = MilitaryRouteQuery.Occupancy(world, rules, seat.ForceId, new(), includeUnits: false).ToImmutableArray();
        var arrivals = ImmutableArray.CreateBuilder<ArrivalProjection>();
        var candidates = ImmutableArray.CreateBuilder<AllocationCandidate>();
        var foodChanges = ImmutableArray.CreateBuilder<AccountSupplyChange>();
        bool complete = true, defenseComplete = false;
        ImmutableArray<GarrisonRequirement> requirements = [];
        SupportObservation Result() => new(candidates.ToImmutable(), arrivals.ToImmutable(), complete) { FoodChanges = foodChanges.ToImmutable(), GarrisonRequirements = requirements, DefenseComplete = defenseComplete };
        bool Spend() { if (work.Spend()) return true; complete = false; return false; }
        int Leadership(SettlementState place, int? departing = null) => residents[place.Id].Where(p => p.Id != departing && !traveling.Contains(p.Id))
            .Select(p => abilities[p.Id].Leadership).DefaultIfEmpty(50).Max();
        long Defense(SettlementState place, uint soldiers, int? departing = null)
        {
            var strength = new CombatStrength(soldiers, place.Morale, Leadership(place, departing), 100, 100);
            return rules.Place(place).Combat is { } profile ? CombatMath.FormationStrength(strength, profile).Defense * soldiers / 1000 :
                CombatMath.PolicyStrength(strength);
        }
        ArrivalEstimate Route(WorldPosition from, int formation, int? naval, SettlementState target, int preparation = 0,
            int remainder = 0, WorldPosition? waypoint = null, NavigationLookahead? lookahead = null)
        {
            if (!Spend()) return new(ArrivalEstimateStatus.Incomplete);
            var travel = rules.Travel(formation, naval);
            // Public building occupancy only. Foreign orders, task rolls and hidden field state never shape routes.
            return cache.Evaluate(new(travel, from, rules.SettlementArea(target), occupied,
                PreparationDays: preparation, MovementRemainder: remainder,
                Waypoint: waypoint, Lookahead: lookahead), work);
        }
        var threats = cache.Situation(world, seat.ForceId, rules).Project(own, rules, work, includeGarrisons: false, attackRange: true);
        foreach (var threat in threats.Arrivals)
            arrivals.Add(new(threat.Source.UnitId, null, threat.TargetId,
                new(ArrivalEstimateStatus.Known, threat.EarliestDays), threat.Source.Soldiers, threat.Source.Strength, true,
                "nearest earliest-arrival threat envelope; public force; optimistic terrain and no obstacles"));
        if (!threats.Complete) { complete = false; return Result(); }
        foreach (var unit in world.Units.Where(u => u.OwnerId == seat.ForceId && u.FormationId is not null &&
            u.Order is { Kind: MilitaryOrderKind.Return or MilitaryOrderKind.Transport, SettlementId: not null }).OrderBy(u => u.Id))
        {
            if (!Spend()) return Result();
            var target = own.FirstOrDefault(s => s.Id == unit.Order!.SettlementId);
            if (target is null) continue;
            int preparation = Math.Max(unit.DepartureDaysRemaining, unit.Condition == UnitCondition.Confused ? Math.Max(0, unit.ConditionDaysRemaining - 1) : 0);
            var estimate = unit.EffectiveOrder != unit.Order ? new ArrivalEstimate(ArrivalEstimateStatus.Incomplete) :
                Route(unit.Position, unit.FormationId!.Value, unit.NavalFormationId, target, preparation, unit.MovementRemainder,
                    waypoint: unit.Waypoint, lookahead: unit.Lookahead);
            arrivals.Add(new(unit.Id, unit.ServicePlaceId, target.Id, estimate, unit.Soldiers, Defense(target, unit.Soldiers), false,
                "existing own order; preparation and confusion; single-route ETA; frozen public buildings"));
            if (estimate.Status == ArrivalEstimateStatus.Incomplete) { complete = false; continue; }
        }
        int root = world.Corps.Single(c => c.OwnerId == seat.ForceId && c.IsRoot).Id;
        foodChanges.AddRange(ProjectFood(world, seat.ForceId, arrivals));
        accounts = EconomicSupplyProjection.Apply(economies, foodChanges.ToImmutable()).SelectMany(e => e.Observation.Accounts)
            .DistinctBy(a => a.Id).ToDictionary(a => a.Id);
        // Only committed arrivals can satisfy demand; prospective alternatives are not reinforcements yet.
        var committed = arrivals.ToImmutable();
        var defense = new GarrisonAssessment(world, seat.ForceId, rules, abilities, committed);
        (uint Required, int? Deadline) Need(SettlementState target, int? departing = null) => defense.Need(target, departing);
        requirements = own.SelectMany(s =>
            new int?[] { null }.Concat(controlled.TryGetValue(s.Id, out var scope)
                ? scope.Settlement.OfficerIds.Select(id => (int?)id) : [])
                .Select(id => new GarrisonRequirement(s.Id, id, Need(s, id).Required))).ToImmutableArray();
        defenseComplete = true;

        foreach (var target in own.OrderBy(s => Need(s).Deadline ?? int.MaxValue).ThenBy(s => s.Id))
        {
            if (!Spend()) return Result();
            var need = Need(target);
            long incoming = committed.Where(a => !a.Hostile && a.TargetId == target.Id && a.Estimate.Status == ArrivalEstimateStatus.Known &&
                (need.Deadline is null || a.Estimate.Days <= need.Deadline)).Sum(a => (long)a.Soldiers);
            long deficit = Math.Min(need.Required - (long)target.Soldiers, target.SoldierCapacity - (long)target.Soldiers - incoming);
            if (deficit < 1000 || rules.TransportFormations is not { } pair) continue;
            foreach (var source in own.Where(s => s.Id != target.Id && controlled.ContainsKey(s.Id)).OrderByDescending(s => s.Soldiers).ThenBy(s => s.Id))
            {
                if (!Spend()) return Result();
                var sourceNeed = Need(source);
                long spare = source.Soldiers - (long)sourceNeed.Required;
                if (spare < 1000 || source.ConfusionDaysRemaining > 0) continue;
                var scope = controlled[source.Id];
                var officers = scope.Settlement.OfficerIds.OrderBy(id => abilities[id].Politics)
                    .ThenByDescending(id => abilities[id].Leadership).ThenBy(id => id).ToArray();
                uint capacity = pair.NavalFormationId is int naval ? Math.Min(rules.Formations[pair.LandFormationId].SoldierCapacity,
                    rules.Formations[naval].SoldierCapacity) : rules.Formations[pair.LandFormationId].SoldierCapacity;
                uint count = (uint)Math.Min(spare, Math.Min(deficit, capacity));
                var start = MilitaryRouteQuery.DeploymentPosition(rules.Travel(pair.LandFormationId, pair.NavalFormationId), rules.SettlementArea(source), occupied);
                if (start is null) { arrivals.Add(new(null, source.Id, target.Id, new(ArrivalEstimateStatus.Unreachable), count, 0, false, "no deployment entrance")); continue; }
                if (need.Deadline is int earliestDeadline && NavigationArrivalForecast.EarliestDays(
                    rules.Travel(pair.LandFormationId, pair.NavalFormationId), start.Value, rules.SettlementArea(target),
                    movementRemainder: 0) > earliestDeadline) continue;
                var estimate = Route(start.Value, pair.LandFormationId, pair.NavalFormationId, target);
                arrivals.Add(new(null, source.Id, target.Id, estimate, count, Defense(target, count), false, "prospective transport; single-route ETA; frozen public buildings"));
                if (estimate.Status == ArrivalEstimateStatus.Incomplete) { complete = false; continue; }
                if (estimate.Status != ArrivalEstimateStatus.Known || need.Deadline is int limit && estimate.Days > limit) continue;
                if (!accounts.TryGetValue(scope.Settlement.AccountId, out var account)) continue;
                int receivingAccount = target.CorpsId ?? root;
                long cost = (long)rules.Formations[pair.LandFormationId].DeploymentCost +
                    (pair.NavalFormationId is int ship ? rules.Formations[ship].DeploymentCost : 0);
                AllocationCandidate Proposal(int officer, uint allocated, int preparation, GameCommand intent, string purpose)
                {
                    int travelDays = estimate.Days!.Value;
                    int arrivalDays = checked(preparation + travelDays);
                    int horizon = Math.Max(0, account.HorizonDays - preparation);
                    long expense = receivingAccount == account.Id
                        ? FoodSupplyForecast.TransferExpenditure(source.Soldiers, target.Soldiers, allocated, travelDays, horizon,
                            source.Wounded, target.Wounded, source.ConsumesFood, target.ConsumesFood)
                        : FoodSupplyForecast.DeploymentExpenditure(source.Soldiers, allocated, Math.Min(travelDays, horizon), source.Wounded, source.ConsumesFood);
                    long receivingFood = receivingAccount == account.Id || !accounts.TryGetValue(receivingAccount, out var receiver) ? 0 :
                        FoodSupplyForecast.ReinforcementExpenditure(target.Soldiers, allocated, Math.Max(0, receiver.HorizonDays - arrivalDays), target.Wounded, target.ConsumesFood);
                    // Costs describe the complete rescue, even when its next command is only a personnel move.
                    return new(scope.ControllerId, account.Id, intent, [officer], need.Deadline is null ? 4 : 0,
                        Defense(target, allocated), cost, expense, source.Id, target.Id,
                        estimate with { Days = arrivalDays }, need.Deadline, purpose)
                        { ReceivingAccountId = receivingFood == 0 ? null : receivingAccount, ReceivingFood = receivingFood,
                            OfficerOpportunityCost = (long)abilities[officer].Politics * Math.Max(1, (arrivalDays + 9) / 10) };
                }
                if (officers.Length == 0)
                {
                    // Soldiers cannot act without a commander. Move one from the same authorized scope, preserving the donor's administrator.
                    if (world.Simulation.Tasks.Any(t => t.Kind == "personnel-move" && t.Journey?.DestinationPlaceId == source.Id)) continue;
                    foreach (var donor in controlled.Values.Where(c => c.ControllerId == scope.ControllerId && c.Settlement.Id != source.Id &&
                        c.Settlement.OfficerIds.Length >= 2).OrderBy(c => c.Settlement.Id))
                    {
                        if (!Spend()) return Result();
                        int actor = donor.Settlement.OfficerIds.OrderBy(id => abilities[id].Politics).ThenBy(id => id).First();
                        var donorPlace = own.Single(s => s.Id == donor.Settlement.Id);
                        if (Need(donorPlace, actor).Required > Math.Max(donorPlace.Soldiers, Need(donorPlace).Required)) continue;
                        var journey = engine.GetPersonnelJourney(seat.ForceId, actor, source.Id, PersonnelJourneyKind.Move);
                        if (!journey.Available || journey.Days is not int days) continue;
                        int preparation = Math.Max(10, (days + 9) / 10 * 10);
                        var prepared = estimate with { Days = estimate.Days + preparation };
                        arrivals.Add(new(null, source.Id, target.Id, prepared, count, Defense(target, count), false, "officer journey; next command boundary; transport"));
                        if (need.Deadline is int deadline && prepared.Days > deadline) continue;
                        candidates.Add(Proposal(actor, count, preparation,
                            new MoveOfficersCommand(Guid.Empty, seat.ForceId, 0, [actor], source.Id), "personnel"));
                        break;
                    }
                    continue;
                }
                foreach (int officer in officers)
                {
                    if (!Spend()) return Result();
                    uint allocated = (uint)Math.Min(count, Math.Max(0, source.Soldiers - (long)Need(source, officer).Required));
                    if (allocated < 1000) continue;
                    var command = new TransportUnitCommand(Guid.Empty, seat.ForceId, 0, source.Id, [officer], allocated, target.Id);
                    candidates.Add(Proposal(officer, allocated, 0, command, "support"));
                }
            }
        }
        // A different authorized account can deliver surplus food, but cannot pay another account's present bills.
        if (rules.TransportFormations is { } supplyPair)
        foreach (var target in own.Where(s => controlled.ContainsKey(s.Id)).OrderBy(s => s.Id))
        {
            if (!Spend()) return Result();
            int targetAccount = target.CorpsId ?? root;
            if (!accounts.TryGetValue(targetAccount, out var receiver) || receiver.Food >= receiver.RequiredFood || receiver.DailyConsumption == 0) continue;
            int deadline = (int)Math.Min(receiver.HorizonDays, receiver.Food / receiver.DailyConsumption);
            foreach (var source in own.Where(s => controlled.ContainsKey(s.Id) && (s.CorpsId ?? root) != targetAccount).OrderBy(s => s.Id))
            {
                if (!Spend()) return Result();
                var scope = controlled[source.Id];
                if (!accounts.TryGetValue(scope.Settlement.AccountId, out var donor) || donor.Food <= donor.RequiredFood || scope.Settlement.OfficerIds.IsEmpty ||
                    source.ConfusionDaysRemaining > 0 || source.Soldiers < (long)Need(source).Required + 1000 ||
                    rules.Formations[supplyPair.LandFormationId].SoldierCapacity < 1000 ||
                    supplyPair.NavalFormationId is int ship && rules.Formations[ship].SoldierCapacity < 1000) continue;
                var start = MilitaryRouteQuery.DeploymentPosition(rules.Travel(supplyPair.LandFormationId, supplyPair.NavalFormationId), rules.SettlementArea(source), occupied);
                if (start is null) continue;
                if (NavigationArrivalForecast.EarliestDays(rules.Travel(supplyPair.LandFormationId, supplyPair.NavalFormationId),
                    start.Value, rules.SettlementArea(target), movementRemainder: 0) > deadline) continue;
                var estimate = Route(start.Value, supplyPair.LandFormationId, supplyPair.NavalFormationId, target);
                arrivals.Add(new(null, source.Id, target.Id, estimate, 1000, 0, false, "surplus food delivery; receiver consumption deadline"));
                if (estimate.Status == ArrivalEstimateStatus.Incomplete) { complete = false; continue; }
                if (estimate.Status != ArrivalEstimateStatus.Known || estimate.Days > deadline) continue;
                long transit = FoodSupplyForecast.DeploymentExpenditure(source.Soldiers, 1000, estimate.Days!.Value, source.Wounded, source.ConsumesFood);
                long required = receiver.RequiredFood - receiver.Food + FoodSupplyForecast.ReinforcementExpenditure(target.Soldiers, 1000,
                    Math.Max(0, receiver.HorizonDays - estimate.Days.Value), target.Wounded, target.ConsumesFood);
                long amount = Math.Min(required, donor.Food - donor.RequiredFood - transit);
                if (amount <= 0) continue;
                uint cargo = (uint)Math.Min(uint.MaxValue, amount);
                int actor = scope.Settlement.OfficerIds.OrderBy(id => abilities[id].Politics).ThenBy(id => id).First();
                if (source.Soldiers < Need(source, actor).Required + 1000) continue;
                var command = new TransportUnitCommand(Guid.Empty, seat.ForceId, 0, source.Id, [actor], 1000, target.Id, CargoFood: cargo);
                long cost = (long)rules.Formations[supplyPair.LandFormationId].DeploymentCost +
                    (supplyPair.NavalFormationId is int naval ? rules.Formations[naval].DeploymentCost : 0);
                candidates.Add(new(scope.ControllerId, donor.Id, command, [actor], 0, cargo, cost, cargo + transit,
                    source.Id, target.Id, estimate, deadline));
            }
        }
        return Result();
    }

    internal static ImmutableArray<AccountSupplyChange> ProjectFood(WorldSnapshot world, int forceId, IEnumerable<ArrivalProjection> arrivals)
    {
        var foodChanges = ImmutableArray.CreateBuilder<AccountSupplyChange>();
        int root = world.Corps.Single(c => c.OwnerId == forceId && c.IsRoot).Id;
        Dictionary<int, (uint Soldiers, uint Wounded)> receivingPopulations = [];
        foreach (var arrival in arrivals.Where(a => !a.Hostile && a.UnitId is not null && a.Estimate.Status == ArrivalEstimateStatus.Known)
            .OrderBy(a => a.Estimate.Days).ThenBy(a => a.UnitId))
        {
            var unit = world.Units.Single(u => u.Id == arrival.UnitId);
            var target = world.Settlements.Single(s => s.Id == arrival.TargetId);
            int days = arrival.Estimate.Days!.Value;
            var previous = receivingPopulations.GetValueOrDefault(target.Id, (target.Soldiers, target.Wounded));
            var next = (Soldiers: checked(previous.Soldiers + unit.Soldiers), Wounded: checked(previous.Wounded + unit.Wounded));
            receivingPopulations[target.Id] = next;
            foodChanges.Add(new(unit.CorpsId ?? root, new(days, -FoodSupplyForecast.FieldConsumption(unit.Soldiers, unit.Wounded))));
            long garrison = target.ConsumesFood ? FoodSupplyForecast.GarrisonConsumption(next.Soldiers, next.Wounded) -
                FoodSupplyForecast.GarrisonConsumption(previous.Soldiers, previous.Wounded) : 0;
            foodChanges.Add(new(target.CorpsId ?? root, new(days, garrison, unit.CargoFood)));
        }
        return foodChanges.ToImmutable();
    }

}

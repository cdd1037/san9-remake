using System.Collections.Immutable;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

internal sealed partial class OriginalAiMilitary
{
    private long Defense(SettlementState target) => target.Soldiers + World.Units.Where(u => FieldUnit(u) &&
        !u.HasEnteredDailyCycle && u.ServicePlaceId == target.Id).Sum(u => (long)u.Soldiers);
    private long Demand(SettlementState target) => target.OwnerId is not null
        ? (long)target.Soldiers * (1000 - Math.Max(0, 200 - target.Durability)) / 1000
        : Related(target.Id).Where(s => s.OwnerId != Owner).Select(s => (long)s.Soldiers).DefaultIfEmpty(0).Sum() /
            Math.Max(1, Related(target.Id).Count(s => s.OwnerId != Owner)) * 40 / 100;
    private IEnumerable<SettlementState> Related(int place) => World.Settlements.Where(s => !s.IsDormant && !s.IsDestroyed && Near(place, s.Id));

    private SettlementState[] AttackSources(SettlementState target)
    {
        var places = WorkSources.ToDictionary(s => s.Id);
        int city = City(target.Id);
        var neighbors = content.CityConnections.Where(c => c.FromCityId == city).Select(c => c.ToCityId).ToArray();
        var candidates = WorkSources.Where(s => City(s.Id) == city)
            .Concat(neighbors.Select(id => places.GetValueOrDefault(id)).OfType<SettlementState>())
            .Concat(Geography.Neighbors(target.Id).Where(p => neighbors.Contains(p.CityId))
                .Select(p => places.GetValueOrDefault(p.Id)).OfType<SettlementState>())
            .DistinctBy(s => s.Id);
        return candidates.Where(s => s.Soldiers > 0 && s.Morale >= 90 && HasClearAttackRoute(s, target)).ToArray();
    }

    private bool HasClearAttackRoute(SettlementState source, SettlementState target) =>
        Commanders(People(source.Id)).FirstOrDefault() is { } commander && Basic([commander]) is { } pair &&
        RouteFrom(source, pair.Land, pair.Naval, new(MilitaryOrderKind.AttackSettlement, null, target.Id), Policies()) is
            { HostileBuilding: false };

    private long AttackUsableStrength(SettlementState[] sources, SettlementState target, OriginalAiGroup group)
    {
        Dictionary<int, long>? enemyReserves = null;
        if (group == OriginalAiGroup.Favored && sources.Any(Fortified))
        {
            var opponents = World.Forces.Where(f => OriginalAiStrategy.IsOpponent(World, Owner, f.Id))
                .Select(f => f.Id).ToHashSet();
            // Share one scan across sources, but rebuild for each plan's current world and excluded target.
            enemyReserves = World.Settlements.Where(s => s.Id != target.Id &&
                !s.IsDormant && !s.IsDestroyed && s.Durability > 0 && s.Facility?.BuilderUnitId is null &&
                s.OwnerId is int owner && opponents.Contains(owner) &&
                (!rules.TryPlace(s, out var place) || place.FacilityTypeId is not int type || !Policy.RouteObstacleFacilityIds.Contains(type)))
                .GroupBy(s => City(s.Id)).ToDictionary(g => g.Key, g => g.Max(s => (long)s.Soldiers) / 2);
        }
        return sources.Sum(source =>
        {
            // Estimate against the same garrison and pressure reserve as allocation.
            // A weak source may still contribute its defensive shortfall.
            long reserve = Reserve(source);
            if (enemyReserves is not null && Fortified(source))
                reserve = Math.Max(reserve, Geography.NearbyCities(City(source.Id))
                    .Max(city => enemyReserves.GetValueOrDefault(city)));
            return source.Soldiers - reserve;
        });
    }

    private bool Strength(SettlementState target, SettlementState[] sources, OriginalAiGroup group)
    {
        if (!sources.Any(s => World.Units.Any(u => Departed(u) && ProjectedOrder(u)?.Kind == MilitaryOrderKind.AttackSettlement && ProjectedOrder(u)?.SettlementId == s.Id)) &&
            World.Units.Any(u => Departed(u) && u.Soldiers >= 10000 && ProjectedOrder(u)?.Kind == MilitaryOrderKind.AttackSettlement &&
                ProjectedOrder(u)?.SettlementId == target.Id && (u.OwnerId == Owner || World.Simulation.Alliance is { } common &&
                    common.MemberForceIds.Contains(Owner) && common.MemberForceIds.Contains(u.OwnerId) ||
                    World.Simulation.AttackRequests.Any(r => r.RequestedForceId == u.OwnerId && r.TargetSettlementId == target.Id && r.RequesterForceId == Owner))))
            return true;
        long soldiers = sources.Sum(s => (long)s.Soldiers), usable = AttackUsableStrength(sources, target, group), defense = Defense(target);
        if (World.Simulation.Alliance is { } alliance && alliance.MemberForceIds.Contains(Owner) && alliance.TargetForceId == target.OwnerId)
            return usable > defense * 75 / 100;
        if (Force.LeaderId is int ruler && Policy.AggressiveRulerIds.Contains(ruler)) return usable > defense;
        if (group == OriginalAiGroup.Favored) return usable > defense * 150 / 100;
        return usable > defense * 125 / 100 || group == OriginalAiGroup.ReducedActivity && soldiers >= 70000 &&
            random.Percent((World.Officers.FirstOrDefault(p => p.Id == Force.LeaderId)?.Ambition ?? 0) + 5);
    }

    // Allocation is simulated before member selection. Ordinary attack intentionally retains duplicate source entries.
    private sealed record Allocation(int SourceId, uint Soldiers);
    private Allocation[] Allocate(SettlementState target, SettlementState[] candidates, bool request)
    {
        var remaining = candidates.ToDictionary(s => s.Id, s => (long)s.Soldiers);
        var counts = candidates.ToDictionary(s => s.Id, _ => 0);
        var output = new List<Allocation>();
        long need = 2 * Demand(target);
        while (remaining.Count > 0 && output.Count < (request ? 2 : 5))
        {
            int id = remaining.OrderByDescending(p => p.Value).First().Key;
            var source = candidates.Single(s => s.Id == id);
            var people = Commanders(People(id));
            int index = counts[id];
            if (index >= people.Length - 1) { remaining.Remove(id); continue; }
            long amount = Math.Min(remaining[id] - Reserve(source), Capacity(people[index]));
            if (amount < MinimumAttack(source)) { remaining.Remove(id); continue; }
            output.Add(new(id, (uint)amount)); need -= amount; remaining[id] -= amount; counts[id]++;
            if (request) remaining.Remove(id);
            if (!request && need < 0) break;
        }
        return output.ToArray();
    }

    private void Attack(OriginalAiState state, OriginalAiCorpsState strategy, bool request)
    {
        if (!request && Corps.Treasury.Money < 500) return;
        var obligation = World.Simulation.AttackRequests.FirstOrDefault(r => r.RequestedForceId == Owner && r.RemainingXun > 0);
        if (request && (obligation is null || !World.Forces.Any(f => f.Id == obligation.RequesterForceId && !f.IsEliminated))) return;
        var group = state.Forces.FirstOrDefault(f => f.ForceId == Owner)?.Group ?? OriginalAiGroup.Unassigned;
        var cities = WorkSources.Where(s => s.Soldiers > 0 && s.Morale >= (request ? 70 : 90) && People(s.Id).Length > 0)
            .GroupBy(s => City(s.Id)).OrderByDescending(g => g.Sum(s => (long)s.Soldiers)).Select(g => g.Key).ToArray();
        if (request) cities = cities.Take(1).ToArray();
        foreach (int city in cities)
        {
            if (!More) break;
            var all = World.Settlements.Where(s => Target(s) && Near(city, s.Id)).ToArray();
            IEnumerable<SettlementState> targets;
            if (request) targets = World.Settlements.Where(s => Target(s) && s.Id == obligation!.TargetSettlementId);
            else if (Force.IsBarbarian)
                targets = all.Where(s => s.OwnerId is not null && (s.Kind == SettlementKind.City
                    ? random.Percent((int)Math.Pow(Math.Max(0, 89 - (World.Relations.FirstOrDefault(r =>
                        r.FirstId == Math.Min(Owner, s.OwnerId!.Value) && r.SecondId == Math.Max(Owner, s.OwnerId.Value))?.Value ?? 50)), 2) / 100)
                    : s.Soldiers >= 50000 && random.Percent(40))).OrderByDescending(s => s.Soldiers);
            else
            {
                bool Desired(SettlementState s) => s.OwnerId == strategy.TargetForceId && strategy.TargetForceId is not null;
                targets = all.Where(s => s.Id == strategy.TargetSettlementId)
                    .Concat(all.Where(s => s.Kind == SettlementKind.City && Desired(s)).OrderBy(s => Related(s.Id).Where(Target).Sum(Defense)))
                    .Concat(all.Where(s => s.Kind == SettlementKind.City && s.OwnerId is not null && !Desired(s)).OrderBy(s => Related(s.Id).Where(Target).Sum(Defense)))
                    .Concat(all.Where(s => s.Kind == SettlementKind.City && s.OwnerId is null))
                    .Concat(all.Where(s => s.Kind != SettlementKind.City).OrderBy(Defense));
            }
            // Target selection returns one candidate for this city. A failed plan
            // consumes the city; it does not reopen the city's fallback target list.
            var originalTarget = targets.FirstOrDefault();
            if (originalTarget is null) continue;
            var target = World.Settlements.FirstOrDefault(s => s.Id == originalTarget.Id && Target(s));
            if (target is null) continue;
            var sources = WorkSources.Where(s => (request ? Near(s.Id, target.Id) : City(s.Id) == city) && s.Soldiers >= (request ? 10000 : 1) &&
                s.Morale >= (request ? 70 : 90) && People(s.Id).Length >= (request ? 2 : 1)).OrderByDescending(s => s.Soldiers).ToArray();
            // City requests may reinforce an existing attack; other buildings reject an existing friendly assignment.
            if (request && sources.Length > 0 && target.Kind != SettlementKind.City && World.Units.Any(u =>
                FieldUnit(u) && u.OwnerId == Owner && u.EffectiveOrder?.SettlementId == target.Id)) return;
            ImmutableArray<int> routeRegions = [];
            SettlementState? routeSource = null;
            if (request)
                sources = sources.Where(s => HasClearAttackRoute(s, target)).ToArray();
            else if (sources.Length > 0)
            {
                routeSource = sources[0];
                target = AttackTargetAlongRoute(routeSource, target, out routeRegions);
                if (target is null) continue;
                sources = AttackSources(target);
            }
            if (sources.Length == 0 || !request && !Strength(target, sources, group)) continue;
            if (routeSource is not null && AlternateConstructionRegion(routeSource, routeRegions) is int region)
            {
                Construct(city, group, region);
                continue;
            }
            var allocations = Allocate(target, sources, request);
            if (allocations.Length == 0) continue;
            long assigned = 0;
            foreach (var allocation in allocations)
            {
                if (!More || request && assigned > 2 * Demand(target)) break;
                if (request && !World.Simulation.AttackRequests.Contains(obligation!)) break;
                var source = WorkSources.FirstOrDefault(s => s.Id == allocation.SourceId);
                target = World.Settlements.FirstOrDefault(s => s.Id == target.Id && Target(s));
                if (target is null) break;
                if (source is null) continue;
                var people = AttackPeople(source.Id, request);
                if (people.Length == 0) { Send(null); continue; }
                uint soldiers = (uint)Math.Max(0, Math.Min(allocation.Soldiers, Math.Min(source.Soldiers - Reserve(source), Capacity(people[0]))));
                if (soldiers < MinimumAttack(source)) { Send(null); continue; }
                var command = Deployment(source, people, soldiers, new(MilitaryOrderKind.AttackSettlement, null, target.Id), target, null, false);
                if (Send(command)) assigned += command!.Soldiers;
            }
        }
    }

    private SettlementState? AttackTargetAlongRoute(SettlementState source, SettlementState target, out ImmutableArray<int> regions)
    {
        regions = [];
        bool ConstructionConflict(int region) => World.Units.Any(u => Direct(u) && Active(u) && u.EffectiveOrder is
            { Kind: MilitaryOrderKind.Construct, Construction: { } construction } && construction.RegionId == region);
        if (ConstructionConflict(target.Id)) return null;
        var people = Commanders(People(source.Id));
        if (people.Length == 0 || Basic([people[0]]) is not { } pair || !rules.TryPlace(source, out var origin)) return null;
        var travel = rules.Travel(pair.Land, pair.Naval);
        paths++;
        var path = terrainRoutes.FindRouteIntoRange(travel.Map, origin.Position, rules.SettlementArea(target), travel.Shape, travel.Mode);
        if (!path.Reachable) return null;
        var encountered = ImmutableArray.CreateBuilder<int>();
        var visited = new HashSet<int>();
        foreach (var step in path.Steps)
        {
            if (rules.RegionAt(step) is int region && visited.Add(region)) encountered.Add(region);
            var building = BuildingAlongRoute(step, s => s.Id != source.Id && !s.IsDestroyed && !s.IsDormant &&
                rules.TryPlace(s, out var place) && !(place.FacilityTypeId is int type && Policy.RouteObstacleFacilityIds.Contains(type)));
            if (building is null) continue;
            if (ConstructionConflict(building.Id)) return null;
            if (Target(building))
            {
                regions = encountered.ToImmutable();
                return building;
            }
        }
        regions = encountered.ToImmutable();
        return target;
    }

    private int? AlternateConstructionRegion(SettlementState source, ImmutableArray<int> regions)
    {
        if (regions.Length <= 2 || source.Kind != SettlementKind.City) return null;
        var intermediate = regions[1..^1];
        int candidate = intermediate[^1];
        if (!content.UnbuiltPlaces.Any(p => p.Id == candidate) ||
            World.Settlements.Any(s => s.Id == candidate && !s.IsDestroyed)) return null;
        var target = World.Settlements.FirstOrDefault(s => s.Id == regions[^1] && !s.IsDestroyed && !s.IsDormant);
        if (target is null || target.Soldiers <= source.Soldiers / 2 ||
            World.Units.Any(u => Direct(u) && Active(u) && ProjectedOrder(u)?.SettlementId == target.Id) ||
            World.Units.Any(u => Active(u) && rules.RegionAt(u.Position) == candidate) ||
            intermediate.Any(id => World.Settlements.Any(s => s.Id == id && s.OwnerId == Owner && !s.IsDestroyed))) return null;
        return random.Percent(25 * intermediate.Length) ? candidate : null;
    }

    private OfficerState[] AttackPeople(int home, bool request)
    {
        var ordered = Commanders(People(home));
        if (ordered.Length < 2) return [];
        var selected = new List<OfficerState> { ordered[0] };
        foreach (var p in ordered.Skip(1).Take((ordered.Length - 1) / 2))
        {
            if (selected.Count >= 5) break;
            if (random.Percent(request ? Ability(p).War + Ability(p).Leadership : Math.Max(0, 100 - PersonnelRulebook.AffinityDistance(p, ordered[0])))) selected.Add(p);
        }
        return selected.ToArray();
    }

    private void Construct(int city, OriginalAiGroup group, int targetRegion)
    {
        if (!More || rules.ConstructionFormations is not { } fixedPair) return;
        var source = WorkSources.Where(s => City(s.Id) == city && s.Morale >= 90 && People(s.Id).Length >= 2)
            .OrderByDescending(s => s.Soldiers).FirstOrDefault();
        if (source is null) return;
        var people = AttackPeople(source.Id, false);
        if (people.Length == 0) return;
        uint soldiers = (uint)Math.Max(0, Math.Min(source.Soldiers - Reserve(source), Capacity(people[0])));
        if (soldiers < MinimumAttack(source)) return;
        var region = content.UnbuiltPlaces.FirstOrDefault(p => p.Id == targetRegion &&
            !World.Settlements.Any(s => s.Id == p.Id && !s.IsDestroyed));
        if (region is null) return;
        // Preserve authored order when site scores tie; dictionary order is not a tie-breaker.
        var site = content.Military!.ConstructionSites.Where(s => s.RegionId == region.Id)
            .Select(s => (Site: s, Route: RouteFrom(source, fixedPair.LandFormationId, fixedPair.NavalFormationId,
                new(MilitaryOrderKind.Move, s.Position, null))))
            .Where(p => p.Route is not null)
            .OrderBy(p => p.Route!.CellCount + MapCoordinates.Distance(region.Position, p.Site.Position) / MapCoordinates.UnitsPerCell)
            .Select(p => p.Site).FirstOrDefault();
        if (site is null) return;
        var facilities = site.FacilityIds.Select(id => rules.Facilities[id]).Where(f => f.Buildable &&
            f.ConstructionCost <= Corps.Treasury.Money && (f.RequiredTacticId is not int required ||
                TacticAvailability.HasForMembers(required, people, Force, World.Treasures, Treasures))).ToArray();
        FacilityRule[] shape = [];
        if (group == OriginalAiGroup.ReducedActivity && random.Percent(33))
        {
            if (site.PreferenceDirections.HasFlag(ConstructionDirection.Horizontal))
                shape = facilities.Where(f => f.Footprint.Width > f.Footprint.Height || f.Footprint == new Footprint(3, 3)).ToArray();
            else if (site.PreferenceDirections.HasFlag(ConstructionDirection.Vertical))
                shape = facilities.Where(f => f.Footprint.Height > f.Footprint.Width || f.Footprint == new Footprint(3, 3)).ToArray();
        }
        if (shape.Length == 0) shape = facilities.Where(f => Policy.OrdinaryFacilityIds.Contains(f.Id)).ToArray();
        var top = (Corps.Treasury.Money <= 5000 ? shape.OrderBy(f => f.ConstructionCost) : shape.OrderByDescending(f => f.ConstructionCost)).Take(3).ToArray();
        if (top.Length == 0) return;
        var facility = top[random.Next(top.Length)];
        var command = Command((id, owner, sequence) => new ConstructFacilityCommand(id, owner, sequence, source.Id,
            people.Select(p => p.Id).ToImmutableArray(), Math.Min(soldiers, rules.Formations[fixedPair.LandFormationId].SoldierCapacity),
            region.Id, facility.Id, site.Position) { Policies = Policies() });
        Send(Legal(command) ? command : null);
    }
}

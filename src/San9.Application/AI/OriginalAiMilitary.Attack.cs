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
        return candidates.Where(s => s.Soldiers > 0 && s.Morale >= 90)
            .Where(s => Commanders(People(s.Id)).FirstOrDefault() is { } commander && Basic([commander]) is { } pair &&
                RouteFrom(s, pair.Land, pair.Naval, new(MilitaryOrderKind.AttackSettlement, null, target.Id), Policies()) is
                    { HostileBuilding: false }).ToArray();
    }

    private bool Strength(SettlementState target, SettlementState[] sources, OriginalAiGroup group)
    {
        if (!sources.Any(s => World.Units.Any(u => Departed(u) && ProjectedOrder(u)?.Kind == MilitaryOrderKind.AttackSettlement && ProjectedOrder(u)?.SettlementId == s.Id)) &&
            World.Units.Any(u => Departed(u) && u.Soldiers >= 10000 && ProjectedOrder(u)?.Kind == MilitaryOrderKind.AttackSettlement &&
                ProjectedOrder(u)?.SettlementId == target.Id && (u.OwnerId == Owner || World.Simulation.Alliance is { } common &&
                    common.MemberForceIds.Contains(Owner) && common.MemberForceIds.Contains(u.OwnerId) ||
                    World.Simulation.AttackRequests.Any(r => r.RequestedForceId == u.OwnerId && r.TargetSettlementId == target.Id && r.RequesterForceId == Owner))))
            return true;
        long soldiers = sources.Sum(s => (long)s.Soldiers), usable = sources.Sum(s => Math.Max(0L, s.Soldiers - Reserve(s))), defense = Defense(target);
        if (World.Simulation.Alliance is { } alliance && alliance.MemberForceIds.Contains(Owner) && alliance.TargetForceId == target.OwnerId)
            return usable > defense * 75 / 100;
        if (Force.LeaderId is int ruler && Policy.AggressiveRulerIds.Contains(ruler)) return usable > defense;
        if (group == OriginalAiGroup.Favored) return usable > defense * 150 / 100;
        return usable > defense * 125 / 100 || group == OriginalAiGroup.ReducedAttempts && soldiers >= 70000 &&
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
        if (Corps.Treasury.Money < 500) return;
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
                bool Desired(SettlementState s) => s.Id == strategy.TargetSettlementId || s.OwnerId == strategy.TargetForceId && strategy.TargetForceId is not null;
                targets = all.Where(s => s.Kind == SettlementKind.City && Desired(s)).OrderBy(s => Related(s.Id).Where(Target).Sum(Defense))
                    .Concat(all.Where(s => s.Kind == SettlementKind.City && s.OwnerId is not null && !Desired(s)).OrderBy(s => Related(s.Id).Where(Target).Sum(Defense)))
                    .Concat(all.Where(s => s.Kind == SettlementKind.City && s.OwnerId is null))
                    .Concat(all.Where(s => s.Kind != SettlementKind.City).OrderBy(Defense));
            }
            bool planned = false;
            foreach (var originalTarget in targets)
            {
                if (!More || planned) break;
                var target = World.Settlements.FirstOrDefault(s => s.Id == originalTarget.Id && Target(s));
                if (target is null) continue;
                var sources = WorkSources.Where(s => (request ? Near(s.Id, target.Id) : City(s.Id) == city) && s.Soldiers >= (request ? 10000 : 1) &&
                    s.Morale >= (request ? 70 : 90) && People(s.Id).Length >= (request ? 2 : 1)).OrderByDescending(s => s.Soldiers).ToArray();
                // City requests may reinforce an existing attack; other buildings reject an existing friendly assignment.
                if (request && sources.Length > 0 && target.Kind != SettlementKind.City && World.Units.Any(u =>
                    FieldUnit(u) && u.OwnerId == Owner && u.EffectiveOrder?.SettlementId == target.Id)) return;
                ImmutableArray<int> routeRegions = [];
                if (!request && sources.Length > 0)
                {
                    target = AttackTargetAlongRoute(sources[0], target, out routeRegions);
                    if (target is null) continue;
                    sources = AttackSources(target);
                }
                if (sources.Length == 0 || !request && !Strength(target, sources, group)) continue;
                if (!request && AlternateConstructionRegion(sources[0], routeRegions) is int region)
                {
                    Construct(city, group, region);
                    planned = true;
                    break;
                }
                var allocations = Allocate(target, sources, request);
                if (allocations.Length == 0) continue;
                planned = true;
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
            if (!planned && !request && !Force.IsBarbarian) Construct(city, group);
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
        var path = travel.Map.FindRouteIntoRange(origin.Position, rules.SettlementArea(target), travel.Shape, travel.Mode, []);
        if (!path.Reachable) return null;
        regions = path.Steps.Select(rules.RegionAt).OfType<int>().Distinct().ToImmutableArray();
        foreach (var step in path.Steps)
        {
            var building = BuildingAlongRoute(step, s => s.Id != source.Id && !s.IsDestroyed && !s.IsDormant &&
                rules.TryPlace(s, out var place) && !(place.FacilityTypeId is int type && Policy.RouteObstacleFacilityIds.Contains(type)));
            if (building is null) continue;
            if (ConstructionConflict(building.Id)) return null;
            if (Target(building)) return building;
        }
        return target;
    }

    private int? AlternateConstructionRegion(SettlementState source, ImmutableArray<int> regions)
    {
        if (regions.Length <= 2 || source.Kind != SettlementKind.City ||
            !content.UnbuiltPlaces.Any(p => p.Id == regions[^1]) ||
            World.Settlements.Any(s => s.Id == regions[^1] && !s.IsDestroyed)) return null;
        var next = World.Settlements.FirstOrDefault(s => s.Id == regions[1] && !s.IsDestroyed && !s.IsDormant);
        if (next is null || next.Soldiers <= source.Soldiers / 2 ||
            World.Units.Any(u => Direct(u) && Active(u) && ProjectedOrder(u)?.SettlementId == next.Id) ||
            World.Units.Any(u => Active(u) && rules.RegionAt(u.Position) == regions[^1]) ||
            regions.Skip(1).Any(id => World.Settlements.Any(s => s.Id == id && s.OwnerId == Owner && !s.IsDestroyed))) return null;
        return random.Percent(25 * regions.Length) ? regions[^1] : null;
    }

    private OfficerState[] AttackPeople(int home, bool request)
    {
        var ordered = Commanders(People(home));
        if (ordered.Length < 2) return [];
        var selected = new List<OfficerState> { ordered[0] };
        foreach (var p in ordered.Skip(1).Take(ordered.Length / 2))
        {
            if (selected.Count >= 5) break;
            if (random.Percent(request ? Ability(p).War + Ability(p).Leadership : Math.Max(0, 100 - PersonnelRulebook.AffinityDistance(p, ordered[0])))) selected.Add(p);
        }
        return selected.ToArray();
    }

    private void Construct(int city, OriginalAiGroup group, int? targetRegion = null)
    {
        if (!More || rules.ConstructionFormations is not { } fixedPair) return;
        var source = WorkSources.Where(s => City(s.Id) == city && s.Morale >= 90 && People(s.Id).Length >= 2)
            .OrderByDescending(s => s.Soldiers).FirstOrDefault();
        if (source is null) return;
        var people = AttackPeople(source.Id, false);
        if (people.Length == 0) return;
        uint soldiers = (uint)Math.Max(0, Math.Min(source.Soldiers - Reserve(source), Capacity(people[0])));
        if (soldiers < MinimumAttack(source)) return;
        var regions = content.UnbuiltPlaces.Where(p => (targetRegion is int selected ? p.Id == selected : Near(city, p.Id)) &&
            !World.Settlements.Any(s => s.Id == p.Id && !s.IsDestroyed)).ToArray();
        foreach (var region in regions)
        {
            // The lookup uses tuple hashes; only authored order can break equal site scores.
            var sites = content.Military!.ConstructionSites.Where(s => s.RegionId == region.Id)
                .Select(s => (Site: s, Route: RouteFrom(source, fixedPair.LandFormationId, fixedPair.NavalFormationId,
                    new(MilitaryOrderKind.Move, s.Position, null))))
                .Where(p => p.Route is not null).OrderBy(p => p.Route!.CellCount + MapCoordinates.Distance(region.Position, p.Site.Position) / MapCoordinates.UnitsPerCell).ToArray();
            foreach (var candidate in sites.Take(1))
            {
                var facilities = candidate.Site.FacilityIds.Select(id => rules.Facilities[id]).Where(f => f.Buildable &&
                    f.ConstructionCost <= Corps.Treasury.Money && (f.RequiredTacticId is not int required ||
                        TacticAvailability.HasForMembers(required, people, Force, World.Treasures, Treasures))).ToArray();
                bool preferred = group == OriginalAiGroup.ReducedAttempts && random.Percent(33);
                var horizontal = facilities.Where(f => f.Footprint.Width > f.Footprint.Height || f.Footprint == new Footprint(3, 3)).ToArray();
                var shape = preferred ? horizontal.Length > 0 ? horizontal : facilities.Where(f =>
                    f.Footprint.Height > f.Footprint.Width || f.Footprint == new Footprint(3, 3)).ToArray() : [];
                if (shape.Length == 0) shape = facilities.Where(f => Policy.OrdinaryFacilityIds.Contains(f.Id)).ToArray();
                var top = (Corps.Treasury.Money <= 5000 ? shape.OrderBy(f => f.ConstructionCost) : shape.OrderByDescending(f => f.ConstructionCost)).Take(3).ToArray();
                if (top.Length == 0) continue;
                var facility = top[random.Next(top.Length)];
                var command = Command((id, owner, sequence) => new ConstructFacilityCommand(id, owner, sequence, source.Id,
                    people.Select(p => p.Id).ToImmutableArray(), Math.Min(soldiers, rules.Formations[fixedPair.LandFormationId].SoldierCapacity),
                    region.Id, facility.Id, candidate.Site.Position) { Policies = Policies() });
                Send(Legal(command) ? command : null);
                return;
            }
        }
    }
}

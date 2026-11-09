using System.Collections.Immutable;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

/// <summary>Batch-local demand, rebuilt after ownership changes; never stored in the world or content.</summary>
internal static class OriginalAiStaffing
{
    internal static int? ServicePlace(WorldSnapshot world, OfficerState person) => person.UnitId is int unit
        ? world.Units.FirstOrDefault(u => u.Id == unit)?.ServicePlaceId : person.SettlementId;

    internal static Dictionary<int, long> Counts(WorldSnapshot world, int corpsId)
    {
        var corps = world.Corps.Single(c => c.Id == corpsId);
        var places = world.Settlements.Where(s => s.OwnerId == corps.OwnerId && !s.IsDormant && !s.IsDestroyed &&
            (s.CorpsId == corpsId || s.CorpsId is null && corps.IsRoot)).Select(s => s.Id).ToHashSet();
        var traveling = world.Simulation.Tasks.Where(t => t.Journey is not null).Select(t => t.OfficerId).ToHashSet();
        return world.Officers.Where(p => !p.IsDormant && p.IsServing && p.OwnerId == corps.OwnerId && p.UnitId is null &&
                !traveling.Contains(p.Id) && p.SettlementId is int home && places.Contains(home))
            .GroupBy(p => p.SettlementId!.Value).ToDictionary(g => g.Key, g => (long)g.Count());
    }

    internal static ImmutableDictionary<int, long> Quotas(WorldSnapshot world, int corpsId, OriginalAiGeography geography)
    {
        var corps = world.Corps.Single(c => c.Id == corpsId);
        var allPlaces = world.Settlements.Where(s => s.OwnerId == corps.OwnerId && !s.IsDormant && !s.IsDestroyed &&
            (s.CorpsId == corpsId || s.CorpsId is null && corps.IsRoot)).ToArray();
        var units = world.Units.Where(u => u.OwnerId == corps.OwnerId && (u.CorpsId == corpsId || u.CorpsId is null && corps.IsRoot)).ToArray();
        var placeIds = allPlaces.Select(s => s.Id).ToHashSet();
        var unitIds = units.Select(u => u.Id).ToHashSet();
        long people = world.Officers.LongCount(p => !p.IsDormant && p.IsServing && p.OwnerId == corps.OwnerId &&
            (p.UnitId is int unit ? unitIds.Contains(unit) : p.SettlementId is int home && placeIds.Contains(home)));
        long denominator = Math.Max(1, allPlaces.Where(s => s.Kind == SettlementKind.City).Sum(s => (long)s.DraftPopulation) / 2 +
            allPlaces.Sum(s => (long)s.Soldiers + s.Wounded) + units.Sum(u => (long)u.Soldiers + u.Wounded));
        return allPlaces.Where(s => s.CanHostPersons && s.Facility?.BuilderUnitId is null)
            .ToImmutableDictionary(s => s.Id, s => Demand(people, s.Soldiers, s.DraftPopulation, s.Wounded, denominator,
                s.Kind == SettlementKind.City ? s.MerchantPresenceChance == 100 ? 2 : 1 :
                s.Soldiers > 0 && !geography.Priority(s.Id, corps.OwnerId) ? 1 : 0));
    }

    internal static long Demand(long people, uint soldiers, uint draft, uint wounded, long denominator, int minimum) =>
        Math.Max(minimum, checked(people * ((long)soldiers + draft / 2 + wounded)) / Math.Max(1, denominator));

    internal static OriginalAiWorkReport Organize(SimulationEngine engine, ScenarioInitialState content, int corpsId,
        OriginalAiGeography geography, Func<GameCommand, bool> submit)
    {
        var initial = engine.Snapshot;
        var root = initial.Corps.Single(c => c.Id == corpsId);
        int owner = root.OwnerId;
        int used = 0, submitted = 0, rejected = 0;
        void Send(Func<Guid, long, GameCommand> create)
        {
            long seq = engine.Snapshot.Simulation.NextSequence;
            used++;
            if (submit(create(SimulationIdentity.Command(seq, owner), seq))) submitted++; else rejected++;
        }
        var cities = initial.Settlements.Where(s => s.OwnerId == owner && s.Kind == SettlementKind.City &&
            !s.IsDormant && !s.IsDestroyed).ToArray();
        var children = initial.Corps.Where(c => c.OwnerId == owner && !c.IsRoot).ToArray();
        int maximum = Math.Min(cities.Length / 7 + 1, 7);
        if (cities.Length >= 7 && children.Length >= maximum)
            return new(corpsId, OriginalAiWork.CreateCorps, OriginalAiWorkEnd.PreparationFailed, 0, 0);
        var world = initial;
        var own = world.Officers.Where(p => !p.IsDormant && p.IsServing && p.OwnerId == owner).ToArray();
        int? City(OfficerState p) => ServicePlace(world, p) is int home ?
            geography.Places.FirstOrDefault(s => s.Id == home)?.CityId : null;
        var groups = cities.Where(s => content.CityProvinceIds.ContainsKey(s.Id)).GroupBy(s => content.CityProvinceIds[s.Id])
            .Select(g =>
            {
                var ids = g.Select(s => s.Id).ToHashSet();
                var people = own.Where(p => City(p) is int city && ids.Contains(city)).ToArray();
                int frontier = people.Count(p => City(p) is int city && geography.Frontier(city, owner));
                var governor = people.Where(p => p.UnitId is null && p.IsCommander &&
                        cities.Any(s => s.Id == p.SettlementId) && !world.Simulation.Tasks.Any(t => t.OfficerId == p.Id))
                    .OrderByDescending(p => content.Information.Offices.FirstOrDefault(o => o.Id == p.OfficeId)?.GovernorPriority ?? 0)
                    .FirstOrDefault();
                return (Cities: g.ToArray(), People: people, Frontier: frontier, Governor: governor);
            }).Where(g => g.Cities.Length > 1 && g.Frontier > 0 && g.Governor is not null &&
                !g.People.Any(p => p.Status is OfficerStatus.Ruler or OfficerStatus.Governor))
            .OrderByDescending(g => g.Cities.Length + g.Frontier).ToArray();
        bool KeepsItsRegion(CorpsState child)
        {
            if (cities.Length < 7) return false;
            var district = cities.Where(s => s.CorpsId == child.Id).ToArray();
            if (district.Length < 2 || !content.CityProvinceIds.TryGetValue(district[0].Id, out int province)) return false;
            var region = cities.Where(s => content.CityProvinceIds.TryGetValue(s.Id, out int id) && id == province)
                .Select(s => s.Id).ToHashSet();
            if (!region.SetEquals(district.Select(s => s.Id))) return false;
            var people = own.Where(p => City(p) is int city && region.Contains(city)).ToArray();
            return !people.Any(p => p.Status == OfficerStatus.Ruler) &&
                people.Any(p => City(p) is int city && geography.Frontier(city, owner)) &&
                people.Any(p => p.Id == child.LeaderId && p.UnitId is null && p.IsCommander &&
                    cities.Any(s => s.Id == p.SettlementId) && !world.Simulation.Tasks.Any(t => t.OfficerId == p.Id));
        }
        foreach (var child in children.Where(c => !KeepsItsRegion(c)))
            Send((id, seq) => new DissolveCorpsCommand(id, owner, seq, child.Id));
        if (cities.Length >= 7)
        {
            foreach (var group in groups.Take(maximum - children.Length))
            {
                var current = engine.Snapshot;
                root = current.Corps.Single(c => c.Id == corpsId);
                var places = current.Settlements.Where(s => s.OwnerId == owner && !s.IsDormant && !s.IsDestroyed &&
                    (s.CorpsId == corpsId || s.CorpsId is null)).ToArray();
                long people = current.Officers.LongCount(p => !p.IsDormant && p.IsServing && p.OwnerId == owner &&
                    ServicePlace(current, p) is int home && places.Any(s => s.Id == home));
                long soldiers = current.Settlements.Where(s => s.OwnerId == owner && !s.IsDormant && !s.IsDestroyed)
                    .Sum(s => (long)s.Soldiers + s.Wounded) + current.Units.Where(u => u.OwnerId == owner)
                    .Sum(u => (long)u.Soldiers + u.Wounded);
                var cityIds = group.Cities.Select(s => s.Id).ToHashSet();
                var districtIds = geography.Places.Where(p => cityIds.Contains(p.CityId)).Select(p => p.Id).ToHashSet();
                var districtPlaces = current.Settlements.Where(s => s.OwnerId == owner && !s.IsDormant && !s.IsDestroyed &&
                    s.CanHostPersons && s.Facility?.BuilderUnitId is null && districtIds.Contains(s.Id)).ToArray();
                var serviceIds = districtPlaces.Select(s => s.Id).ToHashSet();
                long districtSoldiers = districtPlaces.Sum(s => (long)s.Soldiers + s.Wounded) +
                    current.Units.Where(u => u.OwnerId == owner && u.ServicePlaceId is int home && serviceIds.Contains(home))
                        .Sum(u => (long)u.Soldiers + u.Wounded);
                uint money = people == 0 ? 0 : (uint)(root.Treasury.Money * ((double)group.People.Length / people));
                uint food = (uint)(root.Treasury.Food * (soldiers == 0 ? (double)group.Cities.Length / places.Count(s => s.Kind == SettlementKind.City) : districtSoldiers / (double)soldiers));
                Send((id, seq) => new CreateCorpsCommand(id, owner, seq, group.Cities.Select(s => s.Id).ToImmutableArray(), money, food, new GeneralDelegation(), CorpsControlMode.Automatic)
                    { GovernorId = group.Governor!.Id });
            }
        }
        return new(corpsId, OriginalAiWork.CreateCorps, OriginalAiWorkEnd.NoCandidates, used, submitted) { Rejected = rejected };
    }
}

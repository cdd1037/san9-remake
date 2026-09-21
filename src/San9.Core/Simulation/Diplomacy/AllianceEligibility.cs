using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record AllianceTargetCandidate(int ForceId, long MilitaryPopulation, int CityCount,
    ImmutableArray<int> EligibleMemberForceIds);
public sealed record AllianceFormationCandidates(AllianceTargetCandidate? LargestRealm, AllianceTargetCandidate? LowPrestige);

internal static class AllianceEligibility
{
    internal static AllianceFormationCandidates Find(WorldMutation world, SimulationRules rules)
    {
        var players = world.Simulation.Controls.Where(c => c.Kind == ControllerKind.Player).Select(c => c.ForceId).ToHashSet();
        var forces = world.Forces.Values.Where(f => !f.IsEliminated && (!f.IsBarbarian || players.Contains(f.Id))).OrderBy(f => f.Id).ToArray();
        var places = world.Settlements.Values.Where(s => !s.IsDestroyed).ToArray();
        var militaryPopulation = forces.ToDictionary(f => f.Id, f =>
            SoldierTotals.Force(world, f.Id) +
            places.Where(s => s.OwnerId == f.Id && s.Kind == SettlementKind.City).Sum(s => (long)s.DraftPopulation));
        var cities = forces.ToDictionary(f => f.Id, f => places.Count(s => s.OwnerId == f.Id && s.Kind == SettlementKind.City));
        var largest = forces.Where(f => militaryPopulation[f.Id] > 300000 && cities[f.Id] > 0)
            .OrderByDescending(f => cities[f.Id]).ThenBy(f => f.Id).FirstOrDefault();
        var lowest = forces.Where(f => militaryPopulation[f.Id] > 200000 && f.BaseTrust < 30)
            .OrderBy(f => f.BaseTrust).ThenBy(f => f.Id).FirstOrDefault();
        int? Region(SettlementState place) => rules.Military is { } military && military.TryPlace(place, out var location)
            ? military.RegionAt(location.Position) : null;
        var owners = places.Where(s => s.OwnerId is not null).Select(s => (Region: Region(s), Owner: s.OwnerId!.Value))
            .Where(p => p.Region is not null).ToLookup(p => p.Region!.Value, p => p.Owner);
        var neighbors = (rules.Military?.TerritoryConnections(places) ?? [])
            .SelectMany(c => owners[c.FromRegionId].SelectMany(from => owners[c.ToRegionId].Select(to => (From: from, To: to)))).ToHashSet();
        int? City(SettlementState place) => place.Kind == SettlementKind.City ? place.Id :
            Region(place) is int region && rules.Personnel.RegionCities.TryGetValue(region, out int city) ? city : null;
        var provinces = places.Where(s => s.OwnerId is not null).Select(s => (Owner: s.OwnerId!.Value, City: City(s)))
            .Where(p => p.City is int city && rules.Personnel.CityProvinces.ContainsKey(city))
            .Select(p => (p.Owner, Province: rules.Personnel.CityProvinces[p.City!.Value])).ToHashSet();
        AllianceTargetCandidate? Candidate(ForceState? target)
        {
            if (target is null) return null;
            bool Eligible(ForceState member)
            {
                if (member.Id == target.Id || MilitaryDiplomacy.Relation(world, target.Id, member.Id) >= 60) return false;
                if (target.LeaderId is int first && member.LeaderId is int second &&
                    (world.Officers[first].ParentId == second || world.Officers[second].ParentId == first)) return false;
                return neighbors.Contains((target.Id, member.Id)) || provinces.Any(p => p.Owner == target.Id && provinces.Contains((member.Id, p.Province)));
            }
            var members = forces.Where(Eligible).Select(f => f.Id).ToImmutableArray();
            return members.Length < 3 ? null : new(target.Id, militaryPopulation[target.Id], cities[target.Id], members);
        }
        return new(Candidate(largest), Candidate(lowest));
    }
}

using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public static class EndingSnapshotFactory
{
    public static EndingSnapshot Create(WorldSnapshot world, int winningForceId, EndingDefinition definition, OfficerAbilityRules abilities)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(abilities);
        var force = world.Forces.SingleOrDefault(f => f.Id == winningForceId && !f.IsEliminated && !f.IsBarbarian)
            ?? throw new ArgumentException("The winning force must be an active ordinary force.", nameof(winningForceId));
        var cities = world.Settlements.Where(s => s.Kind == SettlementKind.City).ToDictionary(s => s.Id);
        definition.Validate(cities.Keys.ToHashSet(), world.Treasures.Select(t => t.Id).ToHashSet());
        var members = world.Officers.Where(o => o.OwnerId == winningForceId && o.IsServing).OrderBy(o => o.Id).ToArray();
        if (force.LeaderId is not int rulerId || !members.Any(o => o.Id == rulerId))
            throw new ArgumentException("The winning force must have a serving ruler.", nameof(world));
        var currentAbilities = abilities.EvaluateMany(members, world.Treasures);
        var officers = members.Select(o => new EndingOfficer(o.Id, currentAbilities[o.Id], o.Affinity, o.BloodlineId,
            o.TacticProficiencies[4], o.TacticProficiencies[6], o.LearnedTactics.Length)).ToImmutableArray();
        var treasuries = world.Corps.Where(c => c.OwnerId == winningForceId && c.Treasury is not null).Select(c => c.Treasury!).ToArray();
        var holdings = world.Settlements.Where(s => s.OwnerId == winningForceId && !s.IsDestroyed);
        var units = world.Units.Where(u => u.OwnerId == winningForceId);
        var allMembers = world.Officers.Where(o => o.OwnerId == winningForceId).Select(o => o.Id).ToHashSet();
        var culturalTreasures = definition.CulturalTreasureIds.ToHashSet();
        long Sum(ImmutableArray<int> ids, Func<SettlementState, int> field) => ids.Sum(id => (long)field(cities[id]));
        bool Owns(ImmutableArray<int> ids) => ids.All(id => cities[id].OwnerId == winningForceId && !cities[id].IsDestroyed);
        return new(rulerId, officers,
            holdings.Sum(s => (long)s.Soldiers + s.Wounded) + units.Sum(u => (long)u.Soldiers + u.Wounded),
            force.Money + treasuries.Sum(t => (long)t.Money), force.Food + treasuries.Sum(t => (long)t.Food), force.Trust,
            world.Forces.Any(f => !f.IsEliminated && f.IsBarbarian),
            Sum(definition.ExpansionPublicOrderCityIds, s => s.PublicOrder), Owns(definition.ExpansionGatewayCityIds),
            Sum(definition.ExpansionCommerceCityIds, s => s.Commerce), Sum(definition.DomesticDurabilityCityIds, s => s.Durability),
            Owns(definition.DomesticGatewayCityIds), Sum(definition.DomesticPublicOrderCityIds, s => s.PublicOrder),
            world.Treasures.Count(t => culturalTreasures.Contains(t.Id) && t.HolderId is int holder && allMembers.Contains(holder)));
    }
}

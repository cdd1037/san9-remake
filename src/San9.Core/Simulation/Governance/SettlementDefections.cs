using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public enum SettlementDefectionKind { Abandoned, Isolated, Besieged }
public sealed record SettlementDefectedEvent(GameClock Clock, int SettlementId, int PreviousForceId, int ForceId,
    SettlementDefectionKind Kind, int? CommanderId, ImmutableArray<int> FollowerIds) : DomainEvent;

/// <summary>Once-per-xun eligibility. Settlement ownership, geography and physical presence are distinct inputs.</summary>
internal static class SettlementDefections
{
    internal const string RandomStream = "event-settlement-defection";

    internal static void Apply(WorldMutation world, SimulationRules rules, List<DomainEvent> events)
    {
        if (!world.Simulation.SettlementDefectionsEnabled || world.Clock.DayNumber - world.Simulation.Started.DayNumber < 30) return;
        foreach (int id in world.Settlements.Keys.Order().ToArray())
        {
            var place = world.Settlements[id];
            if (!Eligible(place) || place.OwnerId is not int owner || !DefectionPressure.OrdinaryForce(world, owner) ||
                City(place, rules) is not int city || !world.Settlements.TryGetValue(city, out var center)) continue;
            var present = Present(world, id);
            var commander = LeadershipMaintenance.Commander(world, id);
            if (present.Length == 0 && commander is null)
            {
                if (place.Kind != SettlementKind.City && place.Soldiers == 0 && Eligible(center) &&
                    center.OwnerId is int recipient && recipient != owner && DefectionPressure.OrdinaryForce(world, recipient) &&
                    !world.Settlements.Values.Any(s => s.Id != id && Active(s) && s.OwnerId == owner && City(s, rules) == city) &&
                    !DefectionPressure.FieldUnits(world).Any(u => u.OwnerId == owner && City(u, rules) == city))
                    SettlementAffiliation.Transfer(world, place, recipient, null, present, SettlementDefectionKind.Abandoned, rules, events);
            }
            else if (commander is not null)
            {
                if (commander.Ambition >= 15 || present.Any(p => p.Id == world.Forces[owner].LeaderId)) continue;
                int? recipient = IsolatedRecipient(world, place, city, commander, rules);
                var kind = SettlementDefectionKind.Isolated;
                if (recipient is null)
                {
                    recipient = DefectionPressure.BesiegedRecipient(world, new(place.Id, true), commander, rules, RandomStream);
                    kind = SettlementDefectionKind.Besieged;
                }
                if (recipient is int target)
                    SettlementAffiliation.Transfer(world, place, target, commander, present, kind, rules, events);
            }
            if (world.Simulation.TerminalReason is not null) return;
        }
    }

    internal static bool Active(SettlementState place) => !place.IsDestroyed && !place.IsDormant && place.Durability > 0;
    private static bool Eligible(SettlementState place) => Active(place) && place.CanHostPersons && place.Facility?.BuilderUnitId is null;
    internal static OfficerState[] Present(WorldMutation world, int id)
    {
        var traveling = world.Simulation.Tasks.Where(t => t.Journey is not null).Select(t => t.OfficerId).ToHashSet();
        return world.Officers.Values.Where(p => p.IsServing && p.OwnerId == world.Settlements[id].OwnerId &&
            p.SettlementId == id && !traveling.Contains(p.Id)).OrderBy(p => p.Id).ToArray();
    }

    internal static int? City(SettlementState place, SimulationRules rules)
    {
        if (place.Kind == SettlementKind.City) return place.Id;
        int? region = place.Facility is { } facility ? rules.Military?.RegionAt(facility.Position) : place.Id;
        return region is int id && rules.Personnel.RegionCities.TryGetValue(id, out int city) ? city : null;
    }

    internal static int? City(UnitState unit, SimulationRules rules) => rules.Military?.RegionAt(unit.Position) is int region &&
        rules.Personnel.RegionCities.TryGetValue(region, out int city) ? city : null;

    private static int? Best(WorldMutation world, IEnumerable<int> ids) => ids.Distinct()
        .OrderByDescending(id => world.Forces[id].Trust).ThenBy(id => id).Select(id => (int?)id).FirstOrDefault();

    private static int? IsolatedRecipient(WorldMutation world, SettlementState place, int city, OfficerState deciding,
        SimulationRules rules)
    {
        int owner = place.OwnerId!.Value;
        bool Enemy(int? other) => other is int id && MilitaryDiplomacy.AutomaticEnemy(world, owner, id);
        if (rules.Personnel.CityConnections.Where(c => c.FromCityId == city)
            .Any(c => !world.Settlements.TryGetValue(c.ToCityId, out var neighbor) || !Enemy(neighbor.OwnerId))) return null;
        var holdings = world.Settlements.Values.Where(s => Active(s) && City(s, rules) == city).ToArray();
        int? recipient = Best(world, holdings.Where(s => Enemy(s.OwnerId)).Select(s => s.OwnerId!.Value));
        if (recipient is not int target || !DefectionPressure.OrdinaryForce(world, target)) return null;
        var units = DefectionPressure.FieldUnits(world).Where(u => City(u, rules) == city).ToArray();
        long Strength(int force) => holdings.Where(s => s.OwnerId == force).Sum(s => (long)s.Soldiers + s.Wounded) +
            units.Where(u => u.OwnerId == force).Sum(u => (long)u.Soldiers + u.Wounded) +
            (world.Settlements[city].OwnerId == force ? world.Settlements[city].DraftPopulation : 0);
        return DefectionPressure.Roll(world, deciding, owner, target, Strength(owner), Strength(target), RandomStream) ? target : null;
    }

}

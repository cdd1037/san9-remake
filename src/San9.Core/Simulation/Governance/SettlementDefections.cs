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
            if (!Eligible(place) || place.OwnerId is not int owner || !OrdinaryForce(world, owner) ||
                City(place, rules) is not int city || !world.Settlements.TryGetValue(city, out var center)) continue;
            var present = Present(world, id);
            var commander = LeadershipMaintenance.Commander(world, id);
            if (present.Length == 0 && commander is null)
            {
                if (place.Kind != SettlementKind.City && place.Soldiers == 0 && Eligible(center) &&
                    center.OwnerId is int recipient && recipient != owner && OrdinaryForce(world, recipient) &&
                    !world.Settlements.Values.Any(s => s.Id != id && Active(s) && s.OwnerId == owner && City(s, rules) == city) &&
                    !FieldUnits(world).Any(u => u.OwnerId == owner && City(u, rules) == city))
                    SettlementAffiliation.Transfer(world, place, recipient, null, present, SettlementDefectionKind.Abandoned, rules, events);
            }
            else if (commander is not null)
            {
                bool rulerPresent = present.Any(p => p.Id == world.Forces[owner].LeaderId);
                var deciding = rulerPresent ? world.Officers[world.Forces[owner].LeaderId!.Value] : commander;
                if (deciding.Ambition >= 15 || rulerPresent &&
                    (world.Simulation.Controls.Any(c => c.ForceId == owner && c.Kind == ControllerKind.Player) ||
                     world.Settlements.Values.Any(s => s.Id != id && s.OwnerId == owner && Active(s)) ||
                     FieldUnits(world).Any(u => u.OwnerId == owner))) continue;
                int? recipient = IsolatedRecipient(world, place, city, deciding, rulerPresent, rules);
                var kind = SettlementDefectionKind.Isolated;
                if (recipient is null)
                {
                    recipient = BesiegedRecipient(world, place, deciding, rulerPresent, rules);
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
    private static bool OrdinaryForce(WorldMutation world, int id) => world.Forces.TryGetValue(id, out var force) &&
        !force.IsEliminated && !force.IsDormant && !force.IsBarbarian && force.LeaderId is not null;

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

    private static IEnumerable<UnitState> FieldUnits(WorldMutation world) => world.Units.Values.Where(u => !u.IsEmbedded && !u.IsPersonnel);

    private static int? Best(WorldMutation world, IEnumerable<int> ids) => ids.Distinct()
        .OrderByDescending(id => world.Forces[id].Trust).ThenBy(id => id).Select(id => (int?)id).FirstOrDefault();

    private static int? IsolatedRecipient(WorldMutation world, SettlementState place, int city, OfficerState deciding,
        bool rulerPresent, SimulationRules rules)
    {
        int owner = place.OwnerId!.Value;
        bool Enemy(int? other) => other is int id && MilitaryDiplomacy.AutomaticEnemy(world, owner, id);
        if (rules.Personnel.CityConnections.Where(c => c.FromCityId == city)
            .Any(c => !world.Settlements.TryGetValue(c.ToCityId, out var neighbor) || !Enemy(neighbor.OwnerId))) return null;
        var holdings = world.Settlements.Values.Where(s => Active(s) && City(s, rules) == city).ToArray();
        int? recipient = Best(world, holdings.Where(s => Enemy(s.OwnerId)).Select(s => s.OwnerId!.Value));
        if (recipient is not int target || !OrdinaryForce(world, target)) return null;
        var units = FieldUnits(world).Where(u => City(u, rules) == city).ToArray();
        long Strength(int force) => holdings.Where(s => s.OwnerId == force).Sum(s => (long)s.Soldiers + s.Wounded) +
            units.Where(u => u.OwnerId == force).Sum(u => (long)u.Soldiers + u.Wounded) +
            (world.Settlements[city].OwnerId == force ? world.Settlements[city].DraftPopulation : 0);
        return Roll(world, deciding, rulerPresent, owner, target, Strength(owner), Strength(target)) ? target : null;
    }

    private static int? BesiegedRecipient(WorldMutation world, SettlementState place, OfficerState deciding,
        bool rulerPresent, SimulationRules rules)
    {
        if (rules.Military is not { } military || !military.TryPlace(place, out var definition)) return null;
        int owner = place.OwnerId!.Value;
        var attackers = FieldUnits(world).Where(u => u.Soldiers > 0 &&
            u.Condition is UnitCondition.Normal or UnitCondition.HighSpirits or UnitCondition.NoFormation &&
            u.EffectiveOrder is { Kind: MilitaryOrderKind.AttackSettlement } order && order.SettlementId == place.Id &&
            u.FormationId is int formation && military.Formations.ContainsKey(formation) &&
            MilitaryDiplomacy.AutomaticEnemy(world, owner, u.OwnerId) &&
            CellDistance(u.Position, definition.Position) <= 1 &&
            MilitaryCombat.UnitRange(world, u, order, military) is > 0 and var range &&
            MilitaryCombat.Area(u, military).IntersectsExpanded(new(definition.Position, definition.Footprint), range)).ToArray();
        if (attackers.Length < 2 || Best(world, attackers.Select(u => u.OwnerId)) is not int target || !OrdinaryForce(world, target)) return null;
        if (FieldUnits(world).Any(u => u.OwnerId == owner && CellDistance(u.Position, definition.Position) <= 10)) return null;
        long strength = attackers.Where(u => u.OwnerId == target).Sum(u => (long)u.Soldiers);
        return Roll(world, deciding, rulerPresent, owner, target, place.Soldiers, strength) ? target : null;
    }

    private static long CellDistance(WorldPosition first, WorldPosition second)
    {
        var a = MapCoordinates.Cell(first);
        var b = MapCoordinates.Cell(second);
        return MapCoordinates.Distance(new(a.X, a.Y), new(b.X, b.Y));
    }

    private static bool Roll(WorldMutation world, OfficerState deciding, bool rulerPresent, int owner, int target,
        long ownStrength, long targetStrength)
    {
        int loyalty = rulerPresent ? 255 : deciding.LoyaltyTo(owner);
        int willingness = rulerPresent ? 0 : deciding.LoyaltyTo(target);
        long chance = targetStrength / Math.Max(1000, ownStrength) + (willingness - loyalty) / 2 +
            (19 - deciding.Ambition) / 5 * (world.Forces[target].Trust - world.Forces[owner].Trust) / 50;
        return (long)(world.NextRandom(RandomStream) % 100) < chance;
    }
}

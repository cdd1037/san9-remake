using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Directed engagements derived from current orders, ownership and attack reach.</summary>
internal static class MilitaryEngagements
{
    internal static EngagementRelations Build(IWorldQuery world, MilitaryRulebook military)
    {
        Dictionary<BattleSource, BattleSource> targets = [];
        Dictionary<BattleSource, int> incoming = [];
        HashSet<BattleSource> engaged = [];
        Dictionary<BattleSource, BattleSource> contacts = [];
        void Add(BattleSource source, BattleSource target)
        {
            targets.Add(source, target);
            incoming[target] = incoming.GetValueOrDefault(target) + 1;
            if (source.Area(world, military).IntersectsExpanded(target.Area(world, military), MapCoordinates.UnitsPerCell))
            {
                contacts.Add(source, target);
                if (!source.IsSettlement) engaged.Add(source);
                engaged.Add(target);
            }
        }
        foreach (var unit in world.Units.Values)
            if (FindTarget(world, unit, military) is { } target) Add(new(unit.Id, false), target);
        if (world.Units.Count > 0)
            foreach (var settlement in world.Settlements.Values)
                if (FindFacilityTarget(world, settlement, military) is { } target) Add(new(settlement.Id, true), new(target.Id, false));
        // Prefer an entrant's own close target, then the first attacker in stable identity order.
        var opponents = contacts.Where(p => engaged.Contains(p.Key)).ToDictionary();
        foreach (var (source, target) in contacts.OrderBy(p => p.Key.IsSettlement).ThenBy(p => p.Key.Id))
            opponents.TryAdd(target, source);
        return new(targets, incoming, opponents);
    }

    private static BattleSource? FindTarget(IWorldQuery world, UnitState unit, MilitaryRulebook military)
    {
        if (unit.IsEmbedded || unit.IsPersonnel || unit.Soldiers == 0 || unit.Morale == 0 ||
            unit.Condition is UnitCondition.Confused or UnitCondition.Routed ||
            unit.FormationId is not int formation || unit.EffectiveOrder is not { IsAttack: true } order ||
            !MilitaryCombat.ValidTarget(world, unit.OwnerId, order, military.Formation(unit), military) ||
            !MilitaryCombat.Area(unit, military).IntersectsExpanded(MilitaryCombat.TargetArea(world, order, military),
                MilitaryCombat.UnitRange(world, unit, order, military))) return null;
        return new(order.UnitId ?? order.SettlementId!.Value, order.UnitId is null);
    }

    private static UnitState? FindFacilityTarget(IWorldQuery world, SettlementState settlement, MilitaryRulebook military)
    {
        if (!FacilityCombat.Active(settlement) || !military.TryPlace(settlement, out _) ||
            military.FacilityAttack(settlement).Attack is null) return null;
        var area = new BattleSource(settlement.Id, true).Area(world, military);
        bool work = MilitaryDiplomacy.Work(world, settlement.Id, military);
        UnitState? closest = null;
        long distance = long.MaxValue;
        foreach (var unit in world.Units.Values)
        {
            if (unit.IsPersonnel || unit.IsEmbedded || unit.OwnerId == settlement.OwnerId ||
                !work && !MilitaryDiplomacy.AutomaticEnemy(world, settlement.OwnerId!.Value, unit.OwnerId) ||
                unit.FormationId is not int formation || military.Formation(unit).Combat is null) continue;
            var targetArea = MilitaryCombat.Area(unit, military);
            long candidateDistance = targetArea.DistanceTo(area);
            if (closest is not null && (candidateDistance > distance || candidateDistance == distance && unit.Id >= closest.Id) ||
                !targetArea.IntersectsExpanded(area, MilitaryCombat.FacilityRange(world, settlement, unit, military))) continue;
            closest = unit;
            distance = candidateDistance;
        }
        return closest;
    }

    internal static UnitState? FacilityTarget(WorldMutation world, SettlementState settlement, MilitaryRulebook military)
    {
        var target = world.Engagements(military).Target(new(settlement.Id, true));
        return target is { } value ? world.Units[value.Id] : null;
    }

    internal static int Intensity(WorldMutation world, BattleSource subject, BattleSource opponent, MilitaryRulebook military) =>
        world.Engagements(military).Intensity(subject, opponent);

    internal static bool Engaged(WorldMutation world, UnitState unit, MilitaryRulebook military) =>
        world.Engagements(military).Engaged(new(unit.Id, false));
}

/// <summary>A derived view owned by one mutation; queries never modify it or enumerate the world.</summary>
internal sealed class EngagementRelations(Dictionary<BattleSource, BattleSource> targets, Dictionary<BattleSource, int> incoming,
    Dictionary<BattleSource, BattleSource> opponents)
{
    internal IEnumerable<BattleSource> Participants => opponents.Keys;
    internal BattleSource Opponent(BattleSource participant) => opponents[participant];
    internal BattleSource? Target(BattleSource source) => targets.TryGetValue(source, out var target) ? target : null;
    internal int IncomingCount(BattleSource subject) => incoming.GetValueOrDefault(subject);
    internal bool Engaged(BattleSource subject) => opponents.ContainsKey(subject);
    internal int Intensity(BattleSource subject, BattleSource opponent) =>
        IncomingCount(subject) + (Target(opponent) == subject ? 0 : 1);
}

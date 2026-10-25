using San9.Core.Domain;

namespace San9.Core.Simulation;

internal static class UnitPolicyQueries
{
    internal static IEnumerable<UnitState> ActiveFieldUnits(WorldMutation world) =>
        world.OrderedUnits.Where(u => !u.IsEmbedded && !u.IsPersonnel && u.FormationId is not null &&
            u.HasEnteredDailyCycle);

    internal static IEnumerable<BattleSource> Combatants(WorldMutation world, MilitaryRulebook rules) =>
        world.OrderedUnits.Where(u => !u.IsEmbedded && !u.IsPersonnel && u.FormationId is not null)
            .Select(u => new BattleSource(u.Id, false))
            .Concat(world.Settlements.Values.Where(s => !s.IsDestroyed && !s.IsDormant &&
                rules.TryPlace(s, out var place) && place.Combat is not null).Select(s => new BattleSource(s.Id, true)));

    internal static IEnumerable<BattleSource> ActiveCombatants(WorldMutation world, MilitaryRulebook rules) =>
        Combatants(world, rules).Where(actor => actor.IsSettlement || world.Units[actor.Id].HasEnteredDailyCycle);

    internal static IEnumerable<BattleSource> NearbyEnemies(WorldMutation world, UnitState source,
        MilitaryRulebook rules, int radius) => ActiveCombatants(world, rules)
        .Where(actor => (actor.IsSettlement || actor.Id != source.Id) &&
            Owner(world, actor) is int owner && MilitaryDiplomacy.AutomaticEnemy(world, source.OwnerId, owner) &&
            MilitaryCombat.Area(source, rules).IntersectsExpanded(actor.Area(world, rules), radius));

    internal static IEnumerable<BattleSource> VisibleForeignCombatants(WorldMutation world, UnitState source,
        MilitaryRulebook rules)
    {
        if (rules.Formation(source).Vision is not { } vision) return [];
        int additional = 0;
        foreach (var band in rules.VisionSizeBands)
        {
            if (source.Soldiers < band.MinimumSoldiers) break;
            additional = band.AdditionalRadius;
        }
        return ActiveCombatants(world, rules).Where(actor => (actor.IsSettlement || world.Units[actor.Id].EffectiveOrder is not null) &&
            Owner(world, actor) != source.OwnerId && FormationVisionGeometry.Contains(source.Position, source.Facing,
                actor.Area(world, rules).Position, vision, additional));
    }

    private static int? Owner(WorldMutation world, BattleSource actor) => actor.IsSettlement
        ? world.Settlements[actor.Id].OwnerId : world.Units[actor.Id].OwnerId;

    internal static BattleSource? Target(WorldMutation world, MilitaryOrder? order, MilitaryRulebook rules) =>
        order?.UnitId is int unit && world.Units.ContainsKey(unit) ? new(unit, false) :
        order?.SettlementId is int place && world.Settlements.TryGetValue(place, out var settlement) &&
            !settlement.IsDestroyed && !settlement.IsDormant && rules.TryPlace(settlement, out var definition) &&
            definition.Combat is not null ? new(place, true) : null;

    internal static long StrengthRatio(WorldMutation world, UnitState source, UnitState target) =>
        StrengthRatio(world, source, MilitaryCombat.Strength(world, target));

    internal static long StrengthRatio(WorldMutation world, UnitState source, CombatStrength target)
    {
        long own = CombatMath.PolicyStrength(MilitaryCombat.Strength(world, source));
        long other = CombatMath.PolicyStrength(target);
        return (long)System.Numerics.BigInteger.Min(long.MaxValue - 25,
            (System.Numerics.BigInteger)own * 100 / other);
    }
}

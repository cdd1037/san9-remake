using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

internal static class SiegeContributions
{
    internal static SettlementState Credit(WorldMutation world, SettlementState place, BattleSource source, uint soldiers, int walls = 0)
    {
        if (!place.CanHostPersons || place.IsDestroyed || !source.Exists(world)) return place;
        int owner = source.Owner(world);
        if (!MilitaryDiplomacy.CanFight(world, owner, place.OwnerId)) return place;
        long score = checked(20L * walls + soldiers);
        if (score <= 0) return place;
        var ledger = place.SiegeContributions ?? ImmutableDictionary<int, SiegeContribution>.Empty;
        var previous = ledger.GetValueOrDefault(owner) ?? new(0, ImmutableDictionary<int, long>.Empty);
        var units = source.IsSettlement ? previous.Units : previous.Units.SetItem(source.Id,
            checked(previous.Units.GetValueOrDefault(source.Id) + score));
        return place with { SiegeContributions = ledger.SetItem(owner, new(checked(previous.Score + score), units)) };
    }

    internal static UnitState[] Candidates(WorldMutation world, int settlementId, MilitaryRulebook rules)
    {
        var place = world.Settlements[settlementId];
        var area = new BattleSource(settlementId, true).Area(world, rules);
        return world.Units.Values.Where(u => !u.IsEmbedded && !u.IsPersonnel && u.FormationId is not null &&
            u.Soldiers > 0 && u.Morale > 0 && u.DepartureDaysRemaining == 0 &&
            MilitaryDiplomacy.CanFight(world, u.OwnerId, place.OwnerId) &&
            u.EffectiveOrder is { Kind: MilitaryOrderKind.AttackSettlement } order && order.SettlementId == settlementId &&
            MilitaryCombat.Area(u, rules).IntersectsExpanded(area, 5)).OrderBy(u => u.Id).ToArray();
    }

    internal static BattleSource? Receiver(WorldMutation world, int settlementId, BattleSource attacker, UnitState[] units)
    {
        var ledger = world.Settlements[settlementId].SiegeContributions;
        if (units.Length == 0) return null;
        int attackerOwner = attacker.Owner(world);
        int owner = units.Select(u => u.OwnerId).Distinct()
            .OrderByDescending(id => ledger?.GetValueOrDefault(id)?.Score ?? 0)
            .ThenByDescending(id => id == attackerOwner).ThenBy(id => id).First();
        var shares = ledger?.GetValueOrDefault(owner)?.Units;
        int receiver = units.Where(u => u.OwnerId == owner)
            .OrderByDescending(u => shares?.GetValueOrDefault(u.Id) ?? 0)
            .ThenByDescending(u => !attacker.IsSettlement && u.Id == attacker.Id).ThenBy(u => u.Id).First().Id;
        return new(receiver, false);
    }
}

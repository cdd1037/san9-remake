using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record DuelRoster(int AttackingUnitId, int DefendingUnitId, ImmutableArray<int> Attackers, ImmutableArray<int> Defenders);

internal static class DuelParticipants
{
    internal static DuelRoster? Select(WorldMutation world, UnitState attacker, UnitState defender, MilitaryRulebook rules)
    {
        if (attacker.IsPersonnel || attacker.IsEmbedded || defender.IsPersonnel || defender.IsEmbedded ||
            attacker.FormationId is not int attackFormation || defender.FormationId is not int defenseFormation ||
            rules.Formations[attackFormation].TravelMode != TravelMode.Land || rules.Formations[defenseFormation].TravelMode != TravelMode.Land ||
            rules.Map.IsWater(attacker.Position) || rules.Map.IsWater(defender.Position)) return null;
        OfficerState[] Front(UnitState unit) => unit.MemberOrder.Select(id => world.Officers[id])
            .Where(p => p.IsServing && p.OwnerId == unit.OwnerId && p.UnitId == unit.Id && unit.MemberRanks[p.Id] == BattleRank.Front).ToArray();
        var attackFront = Front(attacker);
        var defenseFront = Front(defender);
        var first = attackFront.Where(p => p.Injury == 0 && p.TacticCooldown == 0 && !p.Acted && p.Personality != 3)
            .OrderByDescending(p => world.Abilities(p).War).FirstOrDefault();
        var second = defenseFront.Where(p => p.Injury == 0).OrderByDescending(p => world.Abilities(p).War).FirstOrDefault();
        if (first is null || second is null) return null;
        long chance = 3L * (rules.Formations[attackFormation].DoublesDuelChance ? 2 : 1) * attackFront.Length;
        if (chance < 100 && world.NextRandom("duel-trigger") % 100 >= (ulong)chance) return null;
        if ((long)world.Abilities(first).War + 6 - 3 * first.Personality < world.Abilities(second).War) return null;

        ImmutableArray<int> Roster(OfficerState initial, OfficerState[] front, FormationRule formation)
        {
            var selected = new List<OfficerState>();
            foreach (var candidate in front)
            {
                if (candidate.Id == initial.Id || candidate.Injury != 0 || candidate.TacticCooldown != 0 || candidate.Acted) continue;
                int support = rules.LinkRules.Chance(initial, candidate, formation.Tactics?.LinkMultiplier ?? 2);
                if (support > 0 && (support >= 100 || world.NextRandom("duel-support") % 100 < (uint)support)) selected.Add(candidate);
            }
            return selected.OrderByDescending(p => world.Abilities(p).War).Select(p => p.Id).Prepend(initial.Id).ToImmutableArray();
        }
        return new(attacker.Id, defender.Id, Roster(first, attackFront, rules.Formations[attackFormation]),
            Roster(second, defenseFront, rules.Formations[defenseFormation]));
    }
}

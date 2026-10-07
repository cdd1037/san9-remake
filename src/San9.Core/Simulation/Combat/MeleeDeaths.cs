using San9.Core.Domain;

namespace San9.Core.Simulation;

internal static class MeleeDeaths
{
    internal static int Chance(int war, BattleRank rank) => (int)Math.Clamp(80L - war - (rank switch
    {
        BattleRank.Front => 0,
        BattleRank.Middle => 15,
        BattleRank.Rear => 30,
        _ => throw new ArgumentOutOfRangeException(nameof(rank))
    }), 0, 30);

    // The enclosing defeat owns unit removal, capture, succession and force cleanup.
    internal static void Apply(WorldMutation world, UnitState unit, SimulationRules rules, List<DomainEvent> events)
    {
        var military = rules.Military!;
        if (!BattleDeathRules.Enabled(world, military) || unit.IsEmbedded || unit.IsPersonnel) return;
        int formation = unit.Condition is UnitCondition.NoFormation or UnitCondition.Confused or UnitCondition.Routed &&
            military.UnformedFormationId is int unformed ? unformed : military.Formation(unit).Id;
        bool useRanks = military.Formations[formation].MeleeDeathUsesRanks;
        foreach (var person in world.ServingUnitMembers(unit.Id).Where(p => p.OwnerId == unit.OwnerId)
            .OrderBy(p => p.Id).ToArray())
        {
            if (BattleDeathRules.IsProtected(world, person, unit.CommanderId == person.Id, OfficerDeathCause.Melee)) continue;
            var rank = !useRanks ? BattleRank.Front : military.Rank(unit, person.Id);
            if (!TacticCombat.Roll(world, "battle-melee-death", Chance(world.Abilities(person).War, rank))) continue;
            OfficerDeath.Remove(world, person.Id, OfficerDeathCause.Melee, rules.Personnel, events, afterDailyCommands: true);
            OfficerDeath.SettleTreasures(world, person.Id, world.Forces[unit.OwnerId].LeaderId, events);
        }
    }
}

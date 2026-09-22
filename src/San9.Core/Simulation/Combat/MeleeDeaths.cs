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
            military.UnformedFormationId is int unformed ? unformed : unit.FormationId!.Value;
        bool useRanks = military.Formations[formation].MeleeDeathUsesRanks;
        foreach (var person in world.Officers.Values.Where(p => p.IsServing && p.OwnerId == unit.OwnerId && p.UnitId == unit.Id)
            .OrderBy(p => p.Id).ToArray())
        {
            if (BattleDeathRules.IsProtected(world, person, unit.CommanderId == person.Id, OfficerDeathCause.Melee)) continue;
            var rank = !useRanks ? BattleRank.Front : unit.MemberRanks.TryGetValue(person.Id, out var position) ? position :
                throw new InvalidOperationException("Missing field member rank.");
            if (!TacticCombat.Roll(world, "battle-melee-death", Chance(world.Abilities(person).War, rank))) continue;
            OfficerDeath.Remove(world, person.Id, OfficerDeathCause.Melee, rules.Personnel, events, afterDailyCommands: true);
            OfficerDeath.SettleTreasures(world, person.Id, world.Forces[unit.OwnerId].LeaderId, events);
        }
    }
}

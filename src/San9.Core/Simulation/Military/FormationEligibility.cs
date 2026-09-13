using San9.Core.Domain;

namespace San9.Core.Simulation;

internal static class FormationEligibility
{
    internal static bool CanUse(WorldMutation world, int owner, IEnumerable<int> members,
        FormationRule formation, TreasureRulebook treasures)
    {
        if (formation.RequiredTacticId is not int required) return true;
        return MilitaryTactics.HasTactic(world, owner, members, required, treasures);
    }
}

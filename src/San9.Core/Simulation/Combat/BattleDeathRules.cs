using San9.Core.Domain;

namespace San9.Core.Simulation;

internal static class BattleDeathRules
{
    internal static bool Enabled(WorldMutation world, MilitaryRulebook rules) =>
        world.Simulation.BattleDeathsEnabled && rules.BattleDeathsAllowed;

    // Evaluate against the combat context; sniping retains this result before carrier losses.
    internal static bool IsProtected(WorldMutation world, OfficerState person, bool isCommander,
        OfficerDeathCause cause)
    {
        if (person.OwnerId is not int owner || world.Forces[owner].IsBarbarian || isCommander) return true;
        var abilities = person.BaseAbilities;
        if (Math.Max(Math.Max(abilities.War, abilities.Intelligence), Math.Max(abilities.Politics, abilities.Leadership)) >= 80)
            return true;
        // Ruler immunity belongs to melee deaths, not to duel or sniping deaths.
        return cause == OfficerDeathCause.Melee &&
            (person.Status == OfficerStatus.Ruler || world.Forces[owner].LeaderId == person.Id);
    }
}

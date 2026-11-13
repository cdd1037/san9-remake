using San9.Core.Domain;

namespace San9.Core.Simulation;

internal static class FormationEligibility
{
    internal static bool CanUse(WorldMutation world, int owner, IEnumerable<int> members,
        FormationRule formation, TreasureRulebook treasures, OfficerState? commander = null, bool personalFormation = false)
    {
        bool personal = personalFormation && world.Simulation.NewGame.FormationKnowledgeEnabled &&
            formation.TravelMode == TravelMode.Land && commander?.FormationProficiencies is not null;
        if (formation.MixedTroopsOnly && !world.Simulation.NewGame.FormationKnowledgeEnabled ||
            personal && !FormationKnowledge.Knows(commander!, formation.Id) ||
            !personal && world.Simulation.NewGame.MixedTroopsEnabled && formation.ResearchProjectId is string project &&
            !world.Forces[owner].CompletedResearch.Contains(project))
            return false;
        if (personal || formation.RequiredTacticId is not int required) return true;
        return MilitaryTactics.HasTactic(world, owner, members, required, treasures);
    }
}

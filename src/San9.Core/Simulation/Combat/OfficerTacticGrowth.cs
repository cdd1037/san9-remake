using San9.Core.Domain;

namespace San9.Core.Simulation;

internal static class OfficerTacticGrowth
{
    internal static void Increase(WorldMutation world, int officerId, int category, int amount, List<DomainEvent> events)
    {
        var officer = world.Officers[officerId];
        int before = officer.TacticProficiencies[category];
        int next = before >= 1000 ? before : Math.Min(1000, before + amount * (officer.MasteredCategories.Contains(category) ? 3 : 1));
        if (next == before) return;
        world.Replace(officer with { TacticProficiencies = officer.TacticProficiencies.SetItem(category, next) });
        events.Add(new TacticProficiencyGrownEvent(world.Clock, officerId, category, before, next));
    }

    internal static void TryLearn(WorldMutation world, int officerId, TacticRule tactic, string stream, List<DomainEvent> events)
    {
        var officer = world.Officers[officerId];
        int proficiency = officer.TacticProficiencies[tactic.Category!.Value];
        if (officer.LearnedTactics.Contains(tactic.Id) || proficiency < tactic.TrainingRequirement ||
            !TacticCombat.Roll(world, stream, Math.Min(70, (proficiency - tactic.TrainingRequirement) / 5 + 30))) return;
        world.Replace(officer with { LearnedTactics = officer.LearnedTactics.Add(tactic.Id) });
        events.Add(new TacticLearnedEvent(world.Clock, officer.Id, tactic.Id));
    }
}

using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

internal static class OriginalAiResearch
{
    internal static void TryStart(SimulationEngine engine, MilitaryRulebook? military, ControlSeat seat, List<DomainEvent> events,
        EquipmentRulebook? equipment = null)
    {
        var world = engine.Snapshot;
        if (seat.Kind != ControllerKind.Automatic || military is null ||
            world.Forces.FirstOrDefault(f => f.Id == seat.ForceId) is not { ActiveResearch: null } force) return;
        var root = world.Corps.FirstOrDefault(c => c.OwnerId == force.Id && c.IsRoot);
        if (root is null) return;
        var context = new CommandContext(root.ControlMode == CorpsControlMode.Automatic
            ? CorpsControl.ControllerId(root.Id) : seat.ControllerId);
        var place = world.Settlements.Where(s => s.OwnerId == force.Id && !s.IsDormant && !s.IsDestroyed &&
                s.CanHostPersons && (s.CorpsId is null || s.CorpsId == root.Id) &&
                CorpsControl.CanControlSettlement(world, seat, context, s.Id))
            .OrderBy(s => s.Id).FirstOrDefault();
        if (place is null) return;
        var personnel = world.Officers.Where(person => person.OwnerId == force.Id && person.IsServing && !person.IsDormant).ToArray();
        var fieldUnits = world.Units.Where(unit => unit.OwnerId == force.Id && !unit.IsPersonnel && !unit.IsEmbedded).ToArray();
        bool DependsOn(string target, string prerequisite)
        {
            if (target == prerequisite) return true;
            return military.ResearchProjects.TryGetValue(target, out var definition) &&
                definition.Prerequisites.Any(parent => DependsOn(parent, prerequisite));
        }
        int Demand(ResearchProjectDefinition candidate)
        {
            int score = 0;
            if (equipment is not null && equipment.Items.Values.Any(item => !item.IsOrdinaryArmament && item.ResearchProjectId is string research && DependsOn(research, candidate.Id)))
                score += personnel.Length * 2;
            if (military.Armaments.Values.Any(item => item.ResearchProjectId is string research && DependsOn(research, candidate.Id)))
                score += fieldUnits.Length * 2 + personnel.Length;
            foreach (var type in military.TroopTypes.Values)
            foreach (var level in type.LevelResearchProjects.Where(pair => DependsOn(pair.Value, candidate.Id)))
            {
                var tactics = type.TacticMinimumLevels.Where(pair => pair.Value <= level.Key).Select(pair => pair.Key).ToHashSet();
                score += personnel.Count(person => person.EquippedTacticId is int equipped && tactics.Contains(equipped)) * 4;
                score += personnel.Count(person => person.LearnedTactics.Any(tactics.Contains));
                score += fieldUnits.Sum(unit => unit.Detachments.Values.Count(detachment => detachment.TroopTypeId == type.Id)) * 2;
            }
            foreach (var formation in military.Formations.Values.Where(f => f.ResearchProjectId is string id && DependsOn(id, candidate.Id)))
            {
                if (formation.TravelMode == TravelMode.Water)
                    score += fieldUnits.Count(unit => military.Map.IsWater(unit.Position)) * 6;
                else if (military.Purpose(formation.Id) == UnitPurpose.Combat)
                    score += fieldUnits.Count(unit => unit.CommandOrder?.Kind == MilitaryOrderKind.AttackSettlement) * 2;
            }
            foreach (var training in military.NavalTraining.Values.Where(t => t.ResearchProjectId is string id && DependsOn(id, candidate.Id)))
                score += fieldUnits.Count(unit => unit.NavalFormationId is not null && military.Map.IsWater(unit.Position)) * 6;
            return score;
        }
        var project = military.ResearchProjects.Values.Where(p => !military.IsLegacyFormationResearch(p.Id) && !force.CompletedResearch.Contains(p.Id) &&
                ResearchModuleRules.IsAvailable(military, equipment ?? EquipmentRulebook.Empty, world.Simulation.NewGame, p.Id) &&
                p.Prerequisites.All(force.CompletedResearch.Contains) && p.Cost <= root.Treasury.Money / 2)
            .OrderByDescending(Demand).ThenBy(p => p.Cost).ThenBy(p => p.Id, StringComparer.Ordinal).FirstOrDefault();
        if (project is null) return;
        long sequence = world.Simulation.NextSequence;
        var command = new StartResearchCommand(SimulationIdentity.Command(sequence, seat.ForceId), seat.ForceId,
            sequence, place.Id, project.Id);
        var result = engine.Execute(context, command);
        if (result.Submission.Accepted) events.AddRange(result.Events);
    }
}

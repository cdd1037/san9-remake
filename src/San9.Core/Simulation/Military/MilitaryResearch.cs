using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record StartResearchCommand(Guid CommandId, int IssuerId, long Sequence, int SettlementId, string ProjectId)
    : GameCommand(CommandId, IssuerId, Sequence);
public sealed record ResearchStartedEvent(GameClock Clock, int ForceId, int SettlementId, string ProjectId, int Days) : DomainEvent;
public sealed record ResearchCompletedEvent(GameClock Clock, int ForceId, string ProjectId) : DomainEvent;
public sealed record ResearchCancelledEvent(GameClock Clock, int ForceId, string ProjectId) : DomainEvent;

internal static class MilitaryResearch
{
    internal static void CancelForce(WorldMutation world, int forceId, List<DomainEvent> events)
    {
        if (world.Forces.TryGetValue(forceId, out var force) && force.ActiveResearch is { } research)
        {
            world.Replace(force with { ActiveResearch = null });
            events.Add(new ResearchCancelledEvent(world.Clock, forceId, research.ProjectId));
        }
        EquipmentProduction.CancelForce(world, forceId, events);
        ArmamentProduction.CancelForce(world, forceId, events);
    }
    internal static void CancelAt(WorldMutation world, int settlementId, List<DomainEvent> events)
    {
        EquipmentProduction.CancelAt(world, settlementId, events);
        ArmamentProduction.CancelAt(world, settlementId, events);
        foreach (var force in world.Forces.Values.Where(f => f.ActiveResearch?.SettlementId == settlementId).OrderBy(f => f.Id).ToArray())
        {
            string project = force.ActiveResearch!.ProjectId;
            world.Replace(force with { ActiveResearch = null });
            events.Add(new ResearchCancelledEvent(world.Clock, force.Id, project));
        }
    }

    internal static CommandRejection Start(WorldMutation world, StartResearchCommand command, SimulationRules rules, List<DomainEvent> events)
    {
        if (rules.Military is not { } military ||
            string.IsNullOrWhiteSpace(command.ProjectId) || military.IsLegacyFormationResearch(command.ProjectId) || !military.ResearchProjects.TryGetValue(command.ProjectId, out var project) ||
            !ResearchModuleRules.IsAvailable(military, rules.Equipment, world.Simulation.NewGame, command.ProjectId) ||
            !world.Forces.TryGetValue(command.IssuerId, out var force) || force.IsEliminated || force.IsDormant ||
            !world.Settlements.TryGetValue(command.SettlementId, out var place) || place.OwnerId != force.Id ||
            place.IsDormant || place.IsDestroyed || !place.CanHostPersons || force.ActiveResearch is not null ||
            force.CompletedResearch.Contains(project.Id) || project.Prerequisites.Any(p => !force.CompletedResearch.Contains(p)))
            return CommandRejection.InvalidTask;
        var account = ResourceAccounts.Resolve(world, force.Id, place.CorpsId);
        if (ResourceAccounts.Balance(world, account, ResourceKind.Money) < project.Cost) return CommandRejection.InsufficientFunds;
        if (project.Cost > 0)
            ResourceAccounts.Change(world, account, ResourceKind.Money, -project.Cost, "research", place.Id, events, true);
        world.Replace(force with { ActiveResearch = new(project.Id, place.Id, project.Days) });
        events.Add(new ResearchStartedEvent(world.Clock, force.Id, place.Id, project.Id, project.Days));
        return CommandRejection.None;
    }

    internal static void AdvanceDay(WorldMutation world, MilitaryRulebook? military, List<DomainEvent> events)
    {
        foreach (var force in world.Forces.Values.Where(f => f.ActiveResearch is not null).OrderBy(f => f.Id).ToArray())
        {
            var progress = force.ActiveResearch!;
            if (force.IsEliminated || !world.Settlements.TryGetValue(progress.SettlementId, out var place) ||
                place.OwnerId != force.Id || place.IsDestroyed || place.IsDormant || military is null ||
                !military.ResearchProjects.ContainsKey(progress.ProjectId))
            {
                world.Replace(force with { ActiveResearch = null });
                events.Add(new ResearchCancelledEvent(world.Clock, force.Id, progress.ProjectId));
                continue;
            }
            if (progress.DaysRemaining > 1)
                world.Replace(force with { ActiveResearch = progress with { DaysRemaining = progress.DaysRemaining - 1 } });
            else
            {
                world.Replace(force with { ActiveResearch = null, CompletedResearch = force.CompletedResearch.Add(progress.ProjectId).Order(StringComparer.Ordinal).ToImmutableArray() });
                if (military.IsLegacyFormationResearch(progress.ProjectId))
                {
                    var formations = military.Formations.Values.Where(f => f.MixedTroopsOnly && f.ResearchProjectId == progress.ProjectId).ToArray();
                    foreach (var officer in world.Officers.Values.Where(o => o.OwnerId == force.Id && o.FormationProficiencies is not null).ToArray())
                    {
                        var skills = officer.FormationProficiencies!;
                        foreach (var formation in formations)
                            if (!skills.ContainsKey(formation.Id)) skills = skills.Add(formation.Id, 0);
                        world.Replace(officer with { FormationProficiencies = skills });
                    }
                }
                events.Add(new ResearchCompletedEvent(world.Clock, force.Id, progress.ProjectId));
            }
        }
    }
}

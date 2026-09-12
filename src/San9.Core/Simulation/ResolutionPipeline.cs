using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public enum ResolutionStage
{
    TerminalCheck, XunEvents, XunUpdate, MonthEvents, AppearanceAndIndependence,
    QuarterPrelude, QuarterEvents, QuarterUpdate, AnnualAppearance, AnnualCleanup,
    BeforeDailyCommands, DailyCommands, DailyMapUpdate, UnitAndBuildingEvents,
}

public sealed record StageCompletedEvent(GameClock Clock, ResolutionStage Stage) : DomainEvent;
public sealed record TaskStartedEvent(PersistentTask Task) : DomainEvent;
public sealed record TaskCompletedEvent(Guid TaskId, GameClock Clock) : DomainEvent;
public sealed record TaskCancelledEvent(Guid TaskId, GameClock Clock, string Reason) : DomainEvent;
public sealed record SimulationEndedEvent(GameClock Clock, string Reason) : DomainEvent;
public sealed record ControlEndedEvent(string ControllerId, int ForceId) : DomainEvent;

// Rules are compiled domain code, not callbacks loaded from content or saves.
internal class SimulationRules
{
    internal EconomyRulebook Economy { get; init; } = EconomyRulebook.Empty;
    internal TreasureRulebook Treasures { get; init; } = TreasureRulebook.Empty;
    internal PersonnelRulebook Personnel { get; init; } = PersonnelRulebook.Empty;
    internal GovernanceRulebook Governance { get; init; } = new([]);
    internal virtual TaskRule? FindTask(string kind) => kind is PersonnelMissions.RecruitKind or PersonnelMissions.MoveKind or PersonnelMissions.SearchKind
        ? new PersonnelMissionRule(Personnel, Economy, Governance, Treasures, kind) : kind == PersonnelReturns.Kind ? new PersonnelReturnRule(Personnel, Governance) : null;
    internal virtual void ApplyStage(ResolutionStage stage, WorldMutation world, List<DomainEvent> events) { }
}

internal abstract class TaskRule
{
    // Calendar-day offset from submission to the daily stage that resolves this task.
    internal abstract int DurationDays(PersistentTask task);
    internal virtual bool HasValidPayload(PersistentTask task) => task.Journey is null;
    internal abstract bool Validate(WorldMutation world, PersistentTask task);
    internal abstract void Complete(WorldMutation world, PersistentTask task, List<DomainEvent> events);
}

internal sealed class ResolutionPipeline(SimulationRules rules)
{
    internal void Prepare(WorldMutation world, List<DomainEvent> events)
    {
        var boundary = world.Clock.Boundary;
        List<ResolutionStage> stages = [ResolutionStage.TerminalCheck, ResolutionStage.XunEvents, ResolutionStage.XunUpdate];
        if (boundary.Month) stages.Add(ResolutionStage.MonthEvents);
        stages.Add(ResolutionStage.AppearanceAndIndependence);
        if (boundary.Quarter) stages.AddRange([ResolutionStage.QuarterPrelude, ResolutionStage.QuarterEvents, ResolutionStage.QuarterUpdate]);
        if (boundary.Year) stages.AddRange([ResolutionStage.AnnualAppearance, ResolutionStage.AnnualCleanup]);
        foreach (var stage in stages)
            if (!Run(stage, world, events)) return;
        world.SetSimulation(world.Simulation with { Boundary = SimulationBoundary.Stable, ActiveControl = 0 });
    }

    internal void AdvanceDay(WorldMutation world, List<DomainEvent> events)
    {
        if (!Run(ResolutionStage.BeforeDailyCommands, world, events)) return;
        ResolveTasks(world, events);
        if (!CheckTerminal(world, events) || !Run(ResolutionStage.DailyCommands, world, events)) return;
        world.SetClock(world.Clock.NextDay());
        world.SetSimulation(world.Simulation with { Tasks = world.Simulation.Tasks
            .Select(t => t with { ProgressDays = checked(t.ProgressDays + 1) }).ToImmutableArray() });
        if (!Run(ResolutionStage.DailyMapUpdate, world, events)) return;
        // The later unit/building phase settles returns already due in the completed day,
        // including those created by that day's commands. Advancing the date is not another countdown tick.
        ResolveTasks(world, events, returningAfterDateAdvance: true);
        Run(ResolutionStage.UnitAndBuildingEvents, world, events);
    }

    private bool Run(ResolutionStage stage, WorldMutation world, List<DomainEvent> events)
    {
        if (!CheckTerminal(world, events)) return false;
        if (stage == ResolutionStage.XunUpdate)
        {
            XunRecovery.Apply(world);
            PrisonerCycle.Apply(world, events);
        }
        if (stage == ResolutionStage.QuarterUpdate) PersonnelLoyaltyCycle.Apply(world, events);
        EconomicCycle.Apply(stage, world, rules.Economy, events);
        rules.ApplyStage(stage, world, events);
        events.Add(new StageCompletedEvent(world.Clock, stage));
        return CheckTerminal(world, events);
    }

    internal static bool CheckTerminal(WorldMutation world, List<DomainEvent> events)
    {
        if (world.Simulation.TerminalReason is not string reason) return true;
        foreach (var task in world.Simulation.Tasks.OrderBy(t => t.Sequence).ThenBy(t => t.OriginSettlementId).ThenBy(t => t.OfficerId).ThenBy(t => t.Id))
            events.Add(new TaskCancelledEvent(task.Id, world.Clock, "terminal"));
        world.SetSimulation(world.Simulation with { Boundary = SimulationBoundary.Terminal, Tasks = [] });
        if (!events.OfType<SimulationEndedEvent>().Any()) events.Add(new SimulationEndedEvent(world.Clock, reason));
        return false;
    }

    private void ResolveTasks(WorldMutation world, List<DomainEvent> events, bool returningAfterDateAdvance = false)
    {
        foreach (var task in world.Simulation.Tasks
            .Where(t => !returningAfterDateAdvance || t.Kind == PersonnelReturns.Kind && t.Due.DayNumber < world.Clock.DayNumber)
            .OrderBy(t => t.Sequence).ThenBy(t => t.OriginSettlementId).ThenBy(t => t.OfficerId).ThenBy(t => t.Id).ToArray())
        {
            if (!world.Simulation.Tasks.Any(t => t.Id == task.Id)) continue;
            var rule = rules.FindTask(task.Kind) ?? throw new InvalidOperationException($"Unsupported task '{task.Kind}'.");
            bool valid = TaskReferencesValid(world, task) && rule.Validate(world, task);
            if (valid && world.Clock.DayNumber < task.Due.DayNumber) continue;
            world.SetSimulation(world.Simulation with { Tasks = world.Simulation.Tasks.Remove(task) });
            if (world.Officers.TryGetValue(task.OfficerId, out var officer))
                world.Replace(officer with { Acted = true });
            if (valid)
            {
                rule.Complete(world, task, events);
                events.Add(new TaskCompletedEvent(task.Id, world.Clock));
            }
            else
            {
                events.Add(new TaskCancelledEvent(task.Id, world.Clock, "invalid-target-or-executor"));
                if (task.Journey is not null) PersonnelReturns.Start(world, task, rules.Personnel, events, true);
            }
            if (!CheckTerminal(world, events)) return;
        }
    }

    internal static bool TaskReferencesValid(WorldMutation world, PersistentTask task) =>
        world.Forces.ContainsKey(task.ForceId) &&
        world.Officers.TryGetValue(task.OfficerId, out var officer) && officer.IsServing && officer.OwnerId == task.ForceId &&
        officer.SettlementId == task.OriginSettlementId && world.Settlements.TryGetValue(task.OriginSettlementId, out var target) &&
        target.OwnerId == task.ForceId && target.CanHostPersons && target.Durability > 0;
}

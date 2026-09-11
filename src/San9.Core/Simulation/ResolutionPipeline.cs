using System.Collections.Immutable;

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
// The empty registry intentionally rejects unimplemented gameplay tasks.
internal class SimulationRules
{
    internal virtual TaskRule? FindTask(string kind) => null;
    internal virtual void ApplyStage(ResolutionStage stage, WorldMutation world, List<DomainEvent> events) { }
}

internal abstract class TaskRule
{
    internal abstract int DurationDays { get; }
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
        Run(ResolutionStage.UnitAndBuildingEvents, world, events);
    }

    private bool Run(ResolutionStage stage, WorldMutation world, List<DomainEvent> events)
    {
        if (!CheckTerminal(world, events)) return false;
        if (stage == ResolutionStage.XunUpdate) XunRecovery.Apply(world);
        rules.ApplyStage(stage, world, events);
        events.Add(new StageCompletedEvent(world.Clock, stage));
        return CheckTerminal(world, events);
    }

    internal static bool CheckTerminal(WorldMutation world, List<DomainEvent> events)
    {
        if (world.Simulation.TerminalReason is not string reason) return true;
        foreach (var task in world.Simulation.Tasks.OrderBy(t => t.Sequence).ThenBy(t => t.Id))
            events.Add(new TaskCancelledEvent(task.Id, world.Clock, "terminal"));
        world.SetSimulation(world.Simulation with { Boundary = SimulationBoundary.Terminal, Tasks = [] });
        if (!events.OfType<SimulationEndedEvent>().Any()) events.Add(new SimulationEndedEvent(world.Clock, reason));
        return false;
    }

    private void ResolveTasks(WorldMutation world, List<DomainEvent> events)
    {
        foreach (var task in world.Simulation.Tasks.OrderBy(t => t.Sequence).ThenBy(t => t.Id).ToArray())
        {
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
            else events.Add(new TaskCancelledEvent(task.Id, world.Clock, "invalid-target-or-executor"));
            if (!CheckTerminal(world, events)) return;
        }
    }

    internal static bool TaskReferencesValid(WorldMutation world, PersistentTask task) =>
        world.Forces.ContainsKey(task.ForceId) &&
        world.Officers.TryGetValue(task.OfficerId, out var officer) && officer.IsServing && officer.OwnerId == task.ForceId &&
        officer.SettlementId == task.TargetId && world.Settlements.TryGetValue(task.TargetId, out var target) && target.OwnerId == task.ForceId;
}

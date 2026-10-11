using San9.Core.Simulation;

namespace San9.Application.AI;

/// <summary>Application boundary: observes, plans, binds current identity, then asks the shared validator.</summary>
internal static class DomesticCommandSelection
{
    internal static GameCommand Choose(WorldSnapshot snapshot, Func<CommandContext, GameCommand, CommandSubmissionResult> validate,
        ControlSeat seat, CommandContext context, Guid commandId, int turn, bool scripted = false)
    {
        var observation = DomesticObservation.Capture(snapshot, seat, context);
        // At most one place visit, six activities and five participant sets per activity.
        var budget = new AiPlanningBudget(Math.Max(1L, observation.Settlements.Length * 37L), 0);
        var plan = DomesticPlanner.Plan(observation, budget, turn, scripted);
        foreach (var candidate in plan.Candidates)
        {
            var command = new DevelopCommand(commandId, seat.ForceId, snapshot.Simulation.NextSequence, candidate);
            if (validate(context, command).Accepted) return command;
        }
        return new WaitCommand(commandId, seat.ForceId, snapshot.Simulation.NextSequence);
    }
}

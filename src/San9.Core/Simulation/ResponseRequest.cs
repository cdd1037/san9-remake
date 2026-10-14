namespace San9.Core.Simulation;

/// <summary>Identity and authority of the current decision, independent of the active turn.</summary>
public sealed record ResponseRequest(string Kind, ControlSeat Owner)
{
    internal object Decision { get; init; } = null!;
    internal ResponseStep? Step { get; init; }
}

internal sealed record ResponseStep(Guid TaskId, int ResolutionStep, SearchEncounter? SearchEncounter,
    CompensationOffer? AttackOffer, CompensationOffer? PrisonerOffer);

public sealed partial class SimulationEngine
{
    public ResponseRequest? GetPendingResponse()
    {
        if (_world.IsExecuting) throw new InvalidOperationException("Simulation is executing.");
        var sim = Snapshot.Simulation;
        if (sim.Boundary != SimulationBoundary.AwaitingDecision) return null;
        (string kind, int force, object pending) = sim switch
        {
            { PendingCapturedRuler: { } p } => ("captured-ruler", p.CaptorId, (object)p),
            { PendingExecutionSuccession: { } p } => ("execution-succession", p.RespondingForceId, (object)p),
            { PendingSniping: { } p } => ("sniping-succession", p.RespondingForceId, (object)p),
            { PendingDuel: { } p } => ("duel-succession", p.RespondingForceId, (object)p),
            { PendingTacticStudy: { } p } => ("tactic-study", p.RespondingForceId, (object)p),
            { PendingDailyTask: { } p } => ("daily-task", p.RespondingForceId, (object)p),
            { PendingEvent: { } p } => (p.GetType().Name, p.RespondingForceId, (object)p),
            _ => throw new InvalidOperationException("Awaiting decision has no response identity.")
        };
        var owner = sim.Controls.Single(c => c.ForceId == force);
        ResponseStep? step = null;
        if (sim.PendingDailyTask is { } daily)
        {
            var task = sim.Tasks.Single(t => t.Id == daily.RemainingTaskIds[0]);
            step = new(task.Id, task.ResolutionStep, task.SearchEncounter, task.AttackRequest?.Offer, task.PrisonerReturn?.Offer);
            if (task.ForceId == daily.RespondingForceId)
            {
                var world = _world.BeginMutation(_rules.Abilities, _rules.Economy);
                int? corpsId = PersonnelLocations.Corps(world, world.Officers[task.OfficerId]) ??
                    world.Corps.Values.SingleOrDefault(c => c.OwnerId == force && c.IsRoot)?.Id;
                if (corpsId is int id && world.Corps[id].ControlMode == San9.Core.Domain.CorpsControlMode.Automatic)
                    owner = owner with { ControllerId = CorpsControl.ControllerId(id), Kind = ControllerKind.Automatic };
            }
        }
        return new(kind, owner) { Decision = pending, Step = step };
    }
}

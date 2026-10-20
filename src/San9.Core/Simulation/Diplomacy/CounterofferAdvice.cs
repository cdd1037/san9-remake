namespace San9.Core.Simulation;

public sealed partial class SimulationEngine
{
    /// <summary>Predicts the requester's answer without submitting the recipient's proposed terms.</summary>
    public CommandAdvice GetCounterofferAdvice(Guid taskId, CompensationOffer offer)
    {
        if (_world.IsExecuting || GetPendingResponse() is not { } response ||
            Snapshot.Simulation.PendingDailyTask is not { } pending || pending.RemainingTaskIds[0] != taskId)
            return new(CommandRejection.InvalidTask, null, AdviserOpinion.Unavailable);
        var world = _world.BeginMutation(_rules.Abilities, _rules.Economy);
        var task = world.Simulation.Tasks.Single(t => t.Id == taskId);
        if (task.ResolutionStep != 0 || task.AttackRequest is null && task.PrisonerReturn is null ||
            !_rules.FindTask(task.Kind)!.AcceptsResponse(world, task,
                new RespondDailyTaskCommand(Guid.Empty, response.Owner.ForceId, world.Simulation.NextSequence, taskId, 0, true) { Offer = offer }))
            return new(CommandRejection.InvalidTask, null, AdviserOpinion.Unavailable);
        int target = task.AttackRequest?.TargetForceId ?? task.PrisonerReturn!.TargetForceId;
        if (PrisonerReturnDiplomacy.Player(world, task.ForceId)) return AdviserJudgment.Unpredictable(world, target);
        if (world.Forces[target].AdviserId is not int adviser) return new(CommandRejection.None, null, AdviserOpinion.NoAdviser);
        bool accepts = task.AttackRequest is { } attack
            ? AttackRequestDiplomacy.Accept(world, task with { AttackRequest = attack with { Offer = offer } }, _rules)
            : PrisonerReturnDiplomacy.Accept(world, task with { PrisonerReturn = task.PrisonerReturn! with { Offer = offer } }, _rules);
        return new(CommandRejection.None, adviser, accepts ? AdviserOpinion.Favorable : AdviserOpinion.Unfavorable);
    }
}

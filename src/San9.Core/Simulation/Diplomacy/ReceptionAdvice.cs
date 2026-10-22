namespace San9.Core.Simulation;

public sealed partial class SimulationEngine
{
    /// <summary>The receiving adviser evaluates the arrived mission's retained roll before the player answers.</summary>
    public CommandAdvice GetDiplomacyReceptionAdvice(Guid taskId)
    {
        if (_world.IsExecuting || Snapshot.Simulation.PendingDailyTask is not { } pending ||
            pending.RemainingTaskIds[0] != taskId)
            return new(CommandRejection.InvalidTask, null, AdviserOpinion.Unavailable);
        var world = PreparationWorld();
        var task = world.Simulation.Tasks.Single(t => t.Id == taskId);
        int? target = task.Gift?.TargetForceId ?? task.Surrender?.TargetForceId;
        if (target != pending.RespondingForceId || target is not int host || task.Journey?.SuccessRoll is not int roll)
            return new(CommandRejection.InvalidTask, null, AdviserOpinion.Unavailable);
        if (world.Forces[host].AdviserId is not int adviser)
            return new(CommandRejection.None, null, AdviserOpinion.NoAdviser);
        int threshold = task.Gift is { } gift
            ? GiftDiplomacy.Threshold(world, task.ForceId, task.OfficerId, host, gift.Offer, _rules)
            : SurrenderDiplomacy.Threshold(world, task.ForceId, task.OfficerId, host, _rules);
        return new(CommandRejection.None, adviser, roll < threshold ? AdviserOpinion.Favorable : AdviserOpinion.Unfavorable);
    }
}

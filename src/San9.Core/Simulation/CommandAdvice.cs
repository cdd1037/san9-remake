using San9.Core.Domain;

namespace San9.Core.Simulation;

public enum AdviserOpinion { Unavailable, NoAdviser, Uncertain, Favorable, Unfavorable }
public sealed record CommandAdvice(CommandRejection Rejection, int? AdviserId, AdviserOpinion Opinion);

public static class AdviserAccuracy
{
    public static int Percent(int intelligence) => (int)Math.Min(100, 60L +
        Math.Max(0, intelligence - 70) + Math.Max(0, intelligence - 90));
}

internal static class AdviserJudgment
{
    internal static CommandAdvice Unpredictable(WorldMutation world, int forceId) =>
        new(CommandRejection.None, world.Forces[forceId].AdviserId, AdviserOpinion.Unavailable);

    internal static CommandAdvice Evaluate(WorldMutation world, int forceId, ConditionalRandomPurpose purpose,
        int actorId, int targetId, double threshold) => Evaluate(world, forceId,
            ConditionalRandom.Percentile(world.Simulation.InitialSeed, purpose, world.Clock, actorId, targetId), threshold);

    internal static CommandAdvice Evaluate(WorldMutation world, int forceId, ConditionalRandomPurpose purpose,
        int actorId, int firstTargetId, int secondTargetId, int threshold) => Evaluate(world, forceId,
            ConditionalRandom.Percentile(world.Simulation.InitialSeed, purpose, world.Clock, actorId, firstTargetId, secondTargetId), threshold);

    private static CommandAdvice Evaluate(WorldMutation world, int forceId, int roll, double threshold)
    {
        if (world.Forces[forceId].AdviserId is not int adviserId)
            return new(CommandRejection.None, null, AdviserOpinion.NoAdviser);
        int intelligence = world.Abilities(world.Officers[adviserId]).Intelligence;
        int accuracy = AdviserAccuracy.Percent(intelligence);
        bool favorable = (roll < threshold) == (roll < accuracy);
        return new(CommandRejection.None, adviserId, favorable ? AdviserOpinion.Favorable : AdviserOpinion.Unfavorable);
    }
}

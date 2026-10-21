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
        world.Forces[forceId].AdviserId is int adviserId
            ? new(CommandRejection.None, adviserId, AdviserOpinion.Unavailable)
            : new(CommandRejection.None, null, AdviserOpinion.NoAdviser);

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

    internal static CommandAdvice EvaluateNegotiation(WorldMutation world, int forceId, int roll, int mistakeRoll,
        int freeThreshold, int exchangeThreshold, bool hasPayment)
    {
        if (world.Forces[forceId].AdviserId is not int adviserId)
            return new(CommandRejection.None, null, AdviserOpinion.NoAdviser);
        var outcome = roll < freeThreshold ? AdviserOpinion.Favorable :
            roll < exchangeThreshold || hasPayment ? AdviserOpinion.Uncertain : AdviserOpinion.Unfavorable;
        if (roll >= AdviserAccuracy.Percent(world.Abilities(world.Officers[adviserId]).Intelligence))
            outcome = outcome switch
            {
                AdviserOpinion.Favorable => mistakeRoll < 50 ? AdviserOpinion.Uncertain : AdviserOpinion.Unfavorable,
                AdviserOpinion.Uncertain => mistakeRoll < 50 ? AdviserOpinion.Favorable : AdviserOpinion.Unfavorable,
                _ => mistakeRoll < 50 ? AdviserOpinion.Favorable : AdviserOpinion.Uncertain
            };
        return new(CommandRejection.None, adviserId, outcome);
    }
}

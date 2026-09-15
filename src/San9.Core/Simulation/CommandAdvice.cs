using San9.Core.Domain;

namespace San9.Core.Simulation;

public enum AdviserOpinion { Unavailable, NoAdviser, Uncertain, Favorable, Unfavorable }
public sealed record CommandAdvice(CommandRejection Rejection, int? AdviserId, AdviserOpinion Opinion);

internal static class AdviserJudgment
{
    // The opinion describes the current chance category, never the saved execution draw.
    internal static CommandAdvice Evaluate(WorldMutation world, int forceId, string purpose, int actorId, int targetId, double threshold)
        => Evaluate(world, forceId, purpose, actorId, targetId.ToString(System.Globalization.CultureInfo.InvariantCulture), threshold);

    internal static CommandAdvice Evaluate(WorldMutation world, int forceId, string purpose, int actorId,
        int firstTargetId, int secondTargetId, int threshold) => Evaluate(world, forceId, purpose, actorId,
        FormattableString.Invariant($"{firstTargetId}:{secondTargetId}"), threshold);

    private static CommandAdvice Evaluate(WorldMutation world, int forceId, string purpose, int actorId,
        string targets, double threshold)
    {
        if (world.Forces[forceId].AdviserId is not int adviserId)
            return new(CommandRejection.None, null, AdviserOpinion.NoAdviser);
        var adviser = world.Officers[adviserId];
        int intelligence = world.Abilities(adviser).Intelligence;
        int accuracy = (int)Math.Min(100, 60L + Math.Max(0, intelligence - 70) + Math.Max(0, intelligence - 90));
        if (threshold == 50) return new(CommandRejection.None, adviserId, AdviserOpinion.Uncertain);
        ulong seed = StableRandom.DeriveSeed(world.Simulation.InitialSeed, FormattableString.Invariant(
            $"adviser:{purpose}:{world.Clock.DayNumber}:{actorId}:{targets}:{adviserId}:{intelligence}:{threshold}"));
        bool accurate = new StableRandom(seed).NextUInt64() % 100 < (ulong)accuracy;
        bool favorable = threshold > 50;
        if (!accurate) favorable = !favorable;
        return new(CommandRejection.None, adviserId, favorable ? AdviserOpinion.Favorable : AdviserOpinion.Unfavorable);
    }
}

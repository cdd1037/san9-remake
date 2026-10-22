using San9.Core.Simulation;

namespace San9.Application.Presentation;

/// <summary>Finds the player's preferred edge of a single favorable interval using public adviser opinions.</summary>
internal static class AdvisedAmount
{
    public static uint? Find(uint minimum, uint maximum, bool preferMinimum, Func<uint, CommandAdvice> judge)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minimum, maximum);
        uint preferred = preferMinimum ? minimum : maximum;
        var advice = judge(preferred);
        if (!Predictable(advice)) return null;
        if (advice.Opinion == AdviserOpinion.Favorable) return preferred;
        if (minimum == maximum) return null;
        advice = judge(preferMinimum ? maximum : minimum);
        if (!Predictable(advice) || advice.Opinion != AdviserOpinion.Favorable) return null;

        // Gift opinions and money counteroffers each have at most one boundary as the amount changes.
        // The gift boundary may reverse when the adviser is mistaken; endpoint checks retain that judgment.
        uint low = minimum, high = maximum;
        while (high - low > 1)
        {
            uint middle = low + (high - low) / 2;
            advice = judge(middle);
            if (!Predictable(advice)) return null;
            if ((advice.Opinion == AdviserOpinion.Favorable) == preferMinimum) high = middle;
            else low = middle;
        }
        return preferMinimum ? high : low;
    }

    private static bool Predictable(CommandAdvice advice) => advice.Rejection == CommandRejection.None &&
        advice.AdviserId is not null && advice.Opinion is AdviserOpinion.Favorable or AdviserOpinion.Unfavorable;
}

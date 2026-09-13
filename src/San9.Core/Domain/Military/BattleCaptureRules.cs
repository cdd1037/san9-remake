namespace San9.Core.Domain;

public enum BattleRank { Front, Middle, Rear }

/// <summary>Capture probability from resolved battle inputs; does not inspect global armies or consume random state.</summary>
public static class BattleCaptureRules
{
    public static int Chance(int effectiveWar, int nearbyAttackers, BattleRank rank,
        bool hasRetreatRoute, bool hasEscapeMount, bool doublesCapture)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(effectiveWar);
        ArgumentOutOfRangeException.ThrowIfNegative(nearbyAttackers);
        if (!Enum.IsDefined(rank)) throw new ArgumentOutOfRangeException(nameof(rank));
        if (!hasRetreatRoute) return 100;
        if (hasEscapeMount || nearbyAttackers == 0) return 0;
        long ability = Math.Max(0, 3L * Math.Max(0, effectiveWar - 90) + 2L * Math.Min(effectiveWar, 90) - 160) +
            Math.Max(0, Math.Min(effectiveWar, 80) - 60) + Math.Max(1, Math.Min(effectiveWar, 60) / 2);
        long baseChance = (120 - ability) / 3;
        if (baseChance <= 0) return 0;
        long chance = nearbyAttackers * baseChance;
        if (doublesCapture) chance *= 2;
        chance = rank switch
        {
            BattleRank.Front => chance * 3 / 2,
            BattleRank.Rear => chance / 2,
            _ => chance
        };
        return (int)Math.Min(100, chance);
    }
}

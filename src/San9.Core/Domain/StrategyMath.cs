namespace San9.Core.Domain;

/// <summary>Strategy arithmetic uses current effective abilities; only effect formulas explicitly convert them.</summary>
public static class StrategyMath
{
    public static int FalseReportThreshold(int intelligence, int targetIntelligence, bool defensiveFormation) =>
        OpposedThreshold(intelligence, targetIntelligence, 35, defensiveFormation);

    public static int DisorderThreshold(int intelligence, int targetLeadership, bool defensiveFormation) =>
        OpposedThreshold(intelligence, targetLeadership, 30, defensiveFormation);

    private static int OpposedThreshold(int actor, int target, int maximum, bool defensiveFormation)
    {
        Abilities(actor, target);
        int result = (int)Math.Min(maximum, 3 * Math.Max(0, (long)actor - target + 10) / 4);
        return defensiveFormation ? result / 2 : result;
    }

    public static int InspireThreshold(int intelligence, bool knowsInspiration)
    {
        Abilities(intelligence);
        return (intelligence - 30) / 2 + (knowsInspiration ? 20 : 0);
    }

    public static int ReinforcementThreshold(int leadership)
    {
        Abilities(leadership);
        return Signed(5L * (leadership - 30) / 4);
    }

    public static int AlienateThreshold(int intelligence, int targetIntelligence, int righteousness)
    {
        Abilities(intelligence, targetIntelligence);
        Righteousness(righteousness);
        return Signed(Math.Min(40, (long)intelligence + 5 * (7 - righteousness) - targetIntelligence));
    }

    public static int GossipThreshold(int intelligence, int rulerIntelligence, int? adviserIntelligence)
    {
        Abilities(intelligence, rulerIntelligence);
        if (adviserIntelligence is int adviser) Abilities(adviser);
        return Signed(Math.Min(40, intelligence - ((long)rulerIntelligence + (adviserIntelligence ?? 60)) / 2));
    }

    public static int BurnThreshold(int war, int commanderLeadership, int durability)
    {
        Abilities(war, commanderLeadership);
        ArgumentOutOfRangeException.ThrowIfNegative(durability);
        return durability <= 100 ? 0 : Signed(Math.Min(50, war - 2L * commanderLeadership / 3 - 20));
    }

    public static int SeizeMoneyThreshold(int war, int commanderLeadership, uint soldiers)
    {
        Abilities(war, commanderLeadership);
        return Signed(Math.Min(25, war - soldiers / 1000L - (2L * commanderLeadership + 30) / 3));
    }

    public static int AlienateLoyaltyLoss(int righteousness, int variation)
    {
        Righteousness(righteousness);
        Roll(variation, 3);
        return 4 - righteousness / 5 + variation;
    }

    public static int BurnDurabilityLoss(int war, int durability, int variation)
    {
        Abilities(war);
        ArgumentOutOfRangeException.ThrowIfNegative(durability);
        Roll(variation, 5);
        // Damage cannot repair a fortification or cross the ten-point survival floor.
        return (int)Math.Clamp(EconomyMath.ExecutedAbility(war) / 7 + (long)durability / 50 + variation - 3,
            0, Math.Max(0, durability - 10));
    }

    public static uint BurnFoodLoss(int war, uint soldiers, uint food, int variation)
    {
        Abilities(war);
        Roll(variation, 100);
        return (uint)Math.Min(food, (ulong)soldiers * (ulong)EconomyMath.ExecutedAbility(war) / 2000 + (uint)variation);
    }

    public static uint SeizeMoney(uint soldiers, uint wounded, uint money, int variation)
    {
        Roll(variation, 50);
        return (uint)Math.Min(money, ((ulong)soldiers + wounded) / 80 + (uint)variation + 10);
    }

    public static uint FailedReinforcement(int leadership)
    {
        Abilities(leadership);
        return (uint)Math.Min(3000, 15L * EconomyMath.ExecutedAbility(leadership));
    }

    private static int Signed(long value) => (int)Math.Clamp(value, int.MinValue, int.MaxValue);
    private static void Abilities(int first, int second = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(first);
        ArgumentOutOfRangeException.ThrowIfNegative(second);
    }
    private static void Righteousness(int value)
    {
        if (value is < 0 or > 15) throw new ArgumentOutOfRangeException(nameof(value));
    }
    private static void Roll(int value, int exclusiveMaximum)
    {
        if (value < 0 || value >= exclusiveMaximum) throw new ArgumentOutOfRangeException(nameof(value));
    }
}

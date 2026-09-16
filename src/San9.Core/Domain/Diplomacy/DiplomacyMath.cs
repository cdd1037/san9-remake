namespace San9.Core.Domain;

public enum ForceRelationBand { Hostile, Unfriendly, Neutral, Friendly, Intimate }

public static class DiplomacyMath
{
    public static ForceRelationBand RelationBand(int relation)
    {
        Relation(relation);
        return (ForceRelationBand)Math.Min(4, relation / 20);
    }

    public static int GiftMoneyThreshold(int politics, int relation, uint money) =>
        GiftBase(politics, relation) + (int)(money / 200);

    public static int GiftFoodThreshold(int politics, int relation, uint food) =>
        GiftBase(politics, relation) + (int)(food / 2000);

    public static int GiftTreasureThreshold(int politics, int relation, int loyaltyBonus)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(loyaltyBonus);
        return Signed((long)GiftBase(politics, relation) + 2L * loyaltyBonus);
    }

    private static int GiftBase(int politics, int relation)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(politics);
        Relation(relation);
        return (int)(EconomyMath.ExecutedAbility(politics) / 10) + Math.Max(0, relation - 60);
    }

    public static int FreeAttackRequestThreshold(int politics, int relation)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(politics);
        Relation(relation);
        return Signed((long)politics + relation - 80);
    }

    public static uint PrisonerCompensation(OfficerAbilities prisoner, int envoyPolitics, int relation)
    {
        if (!prisoner.IsNonnegative) throw new ArgumentOutOfRangeException(nameof(prisoner));
        ArgumentOutOfRangeException.ThrowIfNegative(envoyPolitics);
        Relation(relation);
        long[] abilities = [prisoner.War, prisoner.Intelligence, prisoner.Politics, prisoner.Leadership];
        Array.Sort(abilities);
        long average = (abilities[2] + abilities[3]) / 2;
        return (uint)Math.Clamp(average * average / 4 - 8L * ((long)envoyPolitics + relation), 500, uint.MaxValue);
    }

    public static int GiftAcceptedRelation(int relation)
    {
        Relation(relation);
        return Math.Min(100, relation + Math.Max(60 - relation, 20));
    }

    public static int AttackRequestAcceptedRelation(int relation)
    {
        Relation(relation);
        return Math.Min(100, relation + Math.Max(40 - relation, 10));
    }

    public static int SurrenderRejectedRelation(int relation)
    {
        Relation(relation);
        return Math.Max(0, relation - Math.Max(10, relation - 39));
    }

    public static int AllianceMemberRelation(int relation)
    {
        Relation(relation);
        return Math.Clamp(relation + 20, 60, 100);
    }

    public static int AllianceTargetRelation(int relation)
    {
        Relation(relation);
        return Math.Clamp(relation - 20, 0, 35);
    }

    private static int Signed(long value) => (int)Math.Clamp(value, int.MinValue, int.MaxValue);
    private static void Relation(int value)
    {
        if (value is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(value));
    }
}

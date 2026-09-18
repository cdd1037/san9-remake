namespace San9.Core.Domain;

[Flags]
public enum DuelAdvantages
{
    None = 0,
    DominantParry = 1,
    DominantAttack = 2,
    DominantDoubleStrike = 4,
}

public enum DuelStrikeKind { Parry, Normal, DoubleStrike, FinishingStrike }

public readonly record struct DuelFighter(int Martial, int Personality, int HitPoints, int Injury, DuelAdvantages Advantages = DuelAdvantages.None);
public readonly record struct DuelStrikeResult(DuelStrikeKind Kind, int Damage, int DefenderInjury);

public static class DuelStrike
{
    public static long Score(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        long x = value;
        return Math.Max(0, 3 * Math.Max(x - 90, 0) + 2 * Math.Min(x, 90) - 160) +
            Math.Max(0, Math.Min(x, 80) - 60) + Math.Max(1, Math.Min(x, 60) / 2);
    }

    public static DuelStrikeResult Resolve(DuelFighter attacker, DuelFighter defender, Func<ulong> nextRandom)
    {
        Validate(attacker);
        Validate(defender);
        ArgumentNullException.ThrowIfNull(nextRandom);
        long delta = (long)attacker.Martial - defender.Martial;
        bool Chance(long threshold) => threshold > 0 && (threshold >= 100 || (long)(nextRandom() % 100) < threshold);
        long parry = defender.Advantages.HasFlag(DuelAdvantages.DominantParry) && delta <= -20
            ? 40 : Math.Min(8, 2 * defender.Personality + 2 - delta / 5);
        if (Chance(parry)) return new(DuelStrikeKind.Parry, 0, defender.Injury);

        int cap = attacker.Advantages.HasFlag(DuelAdvantages.DominantAttack) && delta >= 20 ? 33 : 22;
        long damage = Math.Min(cap, Math.Max(5, (Score(attacker.Martial) - Score(defender.Martial)) / 3 + 20)) +
            (long)(nextRandom() % (ulong)((100 - attacker.HitPoints) / 20 + 2));
        if (Chance(Math.Min(8, 4 * delta / 5 + 2 * (4 - attacker.Personality))))
        {
            int injury = Math.Min(7, defender.Injury + 1 + (int)(nextRandom() % (delta >= 0 ? 3ul : 2ul)));
            return new(DuelStrikeKind.FinishingStrike,
                (int)Math.Min(defender.HitPoints, 3 * damage + Math.Max((defender.HitPoints - attacker.HitPoints) / 2, 10)), injury);
        }
        int doubleCap = attacker.Advantages.HasFlag(DuelAdvantages.DominantDoubleStrike) && delta >= 20 ? 24 : 12;
        bool doubled = Chance(Math.Min(doubleCap, 3 * (4 - attacker.Personality) + delta / 5));
        return new(doubled ? DuelStrikeKind.DoubleStrike : DuelStrikeKind.Normal,
            (int)Math.Min(defender.HitPoints, damage * (doubled ? 2 : 1)), defender.Injury);
    }

    private static void Validate(DuelFighter fighter)
    {
        if (fighter.Martial < 0 || fighter.Personality is < 0 or > 3 || fighter.HitPoints is < 1 or > 100 ||
            fighter.Injury is < 0 or > 14 || (fighter.Advantages & ~(DuelAdvantages.DominantParry |
                DuelAdvantages.DominantAttack | DuelAdvantages.DominantDoubleStrike)) != 0)
            throw new ArgumentOutOfRangeException(nameof(fighter));
    }
}

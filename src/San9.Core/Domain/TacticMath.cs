using System.Numerics;

namespace San9.Core.Domain;

public readonly record struct MartialTacticMember(int Power, int Proficiency, int War, bool Linked);

/// <summary>Pure tactic arithmetic. Opportunity, resistance, targets and state changes belong to resolution.</summary>
public static class TacticMath
{
    public static int IntelligenceSuccessChance(int intelligence, int defenderIntelligence)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(intelligence);
        ArgumentOutOfRangeException.ThrowIfNegative(defenderIntelligence);
        long difference = EconomyMath.ExecutedAbility(intelligence) - EconomyMath.ExecutedAbility(defenderIntelligence) - 5;
        return (int)Math.Clamp(100 + 6 * difference / 5, 0, 100);
    }

    public static uint MartialDamage(IEnumerable<MartialTacticMember> members, bool doubled,
        uint ordinaryDamage, int effect, bool abnormalTarget, uint targetSoldiers)
    {
        ArgumentNullException.ThrowIfNull(members);
        if (effect is not (1 or 10 or 15)) throw new ArgumentOutOfRangeException(nameof(effect));
        BigInteger total = 0;
        foreach (var member in members)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(member.Power);
            ArgumentOutOfRangeException.ThrowIfNegative(member.Proficiency);
            ArgumentOutOfRangeException.ThrowIfNegative(member.War);
            BigInteger damage = (BigInteger)member.Power * (member.Proficiency / 2 + 1000L) *
                BigInteger.Max(70, (BigInteger)EconomyMath.ExecutedAbility(member.War) + 45) / 1000;
            total += member.Linked ? damage / 2 : damage;
        }
        if (doubled) total *= 2;
        return Cap((total + ordinaryDamage) * effect / 10 * (abnormalTarget ? 13 : 10) / 10, targetSoldiers);
    }

    public static int ConfusionDays(int proficiency, int intelligence, int effect)
    {
        Validate(proficiency, intelligence, effect);
        return effect == 0 ? 0 : (int)(effect * BigInteger.Min(10, Quality(proficiency) + EconomyMath.ExecutedAbility(intelligence) / 20) / 10 + 1);
    }

    public static int TauntMoraleLoss(int proficiency, int intelligence, int power, int effect, int morale)
    {
        Validate(proficiency, intelligence, effect);
        ValidatePower(power, 10);
        if (morale is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(morale));
        return (int)BigInteger.Min(morale, effect * BigInteger.Max(3, Quality(proficiency) + EconomyMath.ExecutedAbility(intelligence) / (10 - power)) / 10);
    }

    public static uint Defection(int proficiency, int intelligence, int power, int effect, uint soldiers, bool garrison)
    {
        Validate(proficiency, intelligence, effect);
        ValidatePower(power, 30);
        BigInteger amount = effect * ((BigInteger)soldiers * (Quality(proficiency) + EconomyMath.ExecutedAbility(intelligence) / (30 - power) + 4) / 100) / 10;
        return Cap(amount, garrison ? Math.Min(soldiers, 3000u) : soldiers);
    }

    public static uint IllusionDamage(int proficiency, int intelligence, int power, int effect, uint soldiers, bool sorcery)
    {
        Validate(proficiency, intelligence, effect);
        ValidatePower(power, 33);
        return Cap(effect * BigInteger.Max(500, (BigInteger)soldiers *
            (Quality(proficiency) + EconomyMath.ExecutedAbility(intelligence) / (33 - power) + (sorcery ? 8 : 7)) / 100) / 10, soldiers);
    }

    public static uint TrapDamage(int proficiency, int intelligence, int power, int effect, uint soldiers)
    {
        Validate(proficiency, intelligence, effect);
        ValidatePower(power, 20);
        return Cap(effect * BigInteger.Max(500, (BigInteger)soldiers *
            (2 * Quality(proficiency) + EconomyMath.ExecutedAbility(intelligence) / (20 - power) + 13) / 100) / 10, soldiers);
    }

    public static int InspireMorale(int proficiency, int intelligence, int power)
    {
        Validate(proficiency, intelligence, 10);
        ValidatePower(power, 15);
        return (int)BigInteger.Min(30, BigInteger.Max(6, 2 * Quality(proficiency) + EconomyMath.ExecutedAbility(intelligence) / (15 - power)));
    }

    public static uint HealSoldiers(int proficiency, int intelligence, int power, uint wounded, uint capacity)
    {
        Validate(proficiency, intelligence, 10);
        ValidatePower(power, 20);
        var percent = BigInteger.Min(100, 3 * Quality(proficiency) + EconomyMath.ExecutedAbility(intelligence) / (20 - power));
        return Cap(BigInteger.Max(1000, (BigInteger)wounded * percent / 100), Math.Min(wounded, capacity));
    }

    private static BigInteger Quality(int proficiency) => ((BigInteger)proficiency + 100) / 250 + 1;
    private static uint Cap(BigInteger amount, uint maximum) => (uint)BigInteger.Min(maximum, amount);
    private static void ValidatePower(int power, int denominator)
    {
        if (power < 0 || power >= denominator) throw new ArgumentOutOfRangeException(nameof(power));
    }
    private static void Validate(int proficiency, int intelligence, int effect)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(proficiency);
        ArgumentOutOfRangeException.ThrowIfNegative(intelligence);
        if (effect is not (0 or 3 or 10)) throw new ArgumentOutOfRangeException(nameof(effect));
    }
}

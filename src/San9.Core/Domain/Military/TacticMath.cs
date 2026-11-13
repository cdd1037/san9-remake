using System.Numerics;

namespace San9.Core.Domain;

public readonly record struct MartialTacticMember(int Power, int Proficiency, int War, bool Linked)
{
    public ulong StrengthNumerator { get; init; }
    public ulong StrengthDenominator { get; init; }
    public uint? DamageModifierBasisPoints { get; init; }
}
public readonly record struct MartialTacticDamage(uint Calculated, uint Applied);
public readonly record struct MartialTacticContext(uint SourceSoldiers, int SourceLeadership,
    int TargetDurability, bool RangedAgainstGarrison);

/// <summary>Pure tactic arithmetic. Opportunity, resistance, targets and state changes belong to resolution.</summary>
public static class TacticMath
{
    public static int AllocationChance(BattleRank rank, int members, bool favored, int? ability = null,
        TacticAllocationCoefficients? allocation = null)
    {
        int count = (allocation ?? TacticAllocationCoefficients.Default).ForMembers(members);
        if (ability is int value)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            count = (int)(count * Math.Min(150, EconomyMath.ExecutedAbility(value) + 60) / 100);
        }
        int position = rank switch { BattleRank.Front => 5, BattleRank.Middle => 4, BattleRank.Rear => 3, _ => throw new ArgumentException("Unknown battle rank.") };
        return position * count * (favored ? 2 : 1) * 5 / 8;
    }

    public static int SnipingChance(int proficiency, int formationBonus, bool adjusted = false, int coefficient = 5)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(proficiency);
        if (formationBonus is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(formationBonus));
        if (coefficient is < 0 or > 10) throw new ArgumentOutOfRangeException(nameof(coefficient));
        long chance = adjusted
            ? (formationBonus > 0 ? formationBonus - 5 : 0) + Math.Min(50, proficiency / (40 - 2 * coefficient))
            : formationBonus + (long)(proficiency / 100) * (proficiency / 100) / 2;
        return (int)Math.Clamp(chance, 0, 100);
    }

    public static int IntelligenceSuccessChance(int intelligence, int defenderIntelligence,
        bool adjusted = false, bool learnedCounter = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(intelligence);
        ArgumentOutOfRangeException.ThrowIfNegative(defenderIntelligence);
        long difference = EconomyMath.ExecutedAbility(intelligence) - EconomyMath.ExecutedAbility(defenderIntelligence) - 5;
        return (int)Math.Clamp(100 + (adjusted ? (6 * difference - (learnedCounter ? 86 : 0)) * 7 / 30 : 6 * difference / 5), 0, 100);
    }

    public static MartialTacticDamage MartialDamage(IEnumerable<MartialTacticMember> members, bool doubled,
        int effect, bool abnormalTarget, uint targetSoldiers, CompanionSettings? companion = null,
        MartialTacticContext context = default)
    {
        ArgumentNullException.ThrowIfNull(members);
        if (effect is not (1 or 10 or 15)) throw new ArgumentOutOfRangeException(nameof(effect));
        companion ??= CompanionSettings.Default;
        companion.Validate();
        ArgumentOutOfRangeException.ThrowIfNegative(context.SourceLeadership);
        ArgumentOutOfRangeException.ThrowIfNegative(context.TargetDurability);
        BigInteger total = 0;
        foreach (var member in members)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(member.Power);
            ArgumentOutOfRangeException.ThrowIfNegative(member.Proficiency);
            ArgumentOutOfRangeException.ThrowIfNegative(member.War);
            BigInteger damage = (BigInteger)member.Power * (member.Proficiency / 2 + 1000L) *
                BigInteger.Max(70, (BigInteger)EconomyMath.ExecutedAbility(member.War) + 45) / 1000;
            if (member.StrengthDenominator != 0)
                damage = damage * member.StrengthNumerator / member.StrengthDenominator;
            if (member.DamageModifierBasisPoints is uint modifier)
                damage = damage * modifier / 10000;
            total += member.Linked ? damage / 2 : damage;
        }
        if (doubled) total *= 2;
        if (context.SourceSoldiers >= 10000 && companion.BoostMartialTacticDamage)
        {
            long bonus = Math.Min(Math.Max(0L, (long)context.SourceLeadership - 60) * 1000, context.SourceSoldiers);
            total = ((100000 + bonus) / 16) * total / 6250;
        }
        else if (context.SourceSoldiers < 10000 && companion.ReduceSmallUnitTacticDamage)
            total = EconomyMath.SquareRoot(context.SourceSoldiers) * total / 100;
        if (context.RangedAgainstGarrison && companion.ReduceRangedTacticDamageAgainstFacilities)
            total = total * Math.Max(1024L, 2048L - context.TargetDurability) / 2048;
        total = total * companion.MartialTacticDamageCoefficient / 10;
        uint calculated = Cap(total * effect / 10 * (abnormalTarget ? 13 : 10) / 10, uint.MaxValue);
        return new(calculated, Math.Min(calculated, targetSoldiers));
    }

    public static int ConfusionDays(int proficiency, int intelligence, int effect, int? power = null)
    {
        Validate(proficiency, intelligence, effect);
        long executed = EconomyMath.ExecutedAbility(intelligence);
        BigInteger term = Quality(proficiency) + executed / 20;
        if (power is int strength)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(strength);
            var quality = Quality(proficiency);
            if (quality > 1 && executed >= 30) quality--;
            term = strength == 0 ? 0 : quality + executed / Math.Max(16L, 40L - strength);
        }
        return effect == 0 ? 0 : (int)(effect * BigInteger.Min(10, term) / 10 + 1);
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

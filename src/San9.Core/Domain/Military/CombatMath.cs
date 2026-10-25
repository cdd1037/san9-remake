using System.Numerics;

namespace San9.Core.Domain;

public readonly record struct CombatStrength(uint Soldiers, int Morale, int Leadership, int Intensity, int Effect)
{
    internal void Validate()
    {
        if (Morale is < 0 or > 100 || Leadership < 0 || Intensity <= 0 || Effect <= 0)
            throw new ArgumentException("Invalid combat strength.");
    }
}

public readonly record struct CombatLoss(uint RemainingSoldiers, uint AddedWounded, uint Dead, uint WoundedOverflow);

/// <summary>Exact integer combat arithmetic shared by field troops and garrisons.</summary>
public static class CombatMath
{
    public static long PolicyStrength(CombatStrength value)
    {
        value.Validate();
        return (long)BigInteger.Min(long.MaxValue, BigInteger.Max(1, Power(value) * value.Soldiers / 100));
    }

    internal static BigInteger PolicyTargetPriority(CombatStrength value, bool commanderIsRuler)
    {
        value.Validate();
        return BigInteger.Max(0, Power(value) - (commanderIsRuler ? 20 : 0)) * value.Soldiers;
    }

    /// <summary>Unopposed attack and defense terms for a formation preview, without target wall defense.</summary>
    public static (long AgainstUnits, long AgainstGarrison, long Defense) FormationStrength(CombatStrength strength, CombatProfile profile)
    {
        strength.Validate();
        profile.Validate();
        long Term(int coefficient, int divisor) => (long)BigInteger.Min(long.MaxValue,
            BigInteger.Max(1, FormationPower(strength, coefficient, divisor)));
        return (Term(profile.AgainstUnits, 25), Term(profile.AgainstGarrison, 25), Term(profile.Defense, 15));
    }

    public static uint AdjustComputerDamage(uint damage, uint targetSoldiers, int bonusPercent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bonusPercent);
        return (uint)Math.Min(targetSoldiers, (ulong)damage * (100UL + (uint)bonusPercent) / 100);
    }

    public static uint TroopDamage(CombatStrength attacker, CombatStrength defender,
        CombatProfile attackingFormation, CombatProfile defendingFormation, bool targetIsGarrison, int durability, bool usesRockfallPower = false)
    {
        ArgumentNullException.ThrowIfNull(attackingFormation);
        ArgumentNullException.ThrowIfNull(defendingFormation);
        attackingFormation.Validate();
        defendingFormation.Validate();
        if (!targetIsGarrison && durability != 0) throw new ArgumentException("Field troops cannot receive wall defense.");
        return TroopDamage(attacker, defender, targetIsGarrison ? attackingFormation.AgainstGarrison : attackingFormation.AgainstUnits,
            defendingFormation.Defense, durability, usesRockfallPower);
    }

    public static uint TroopDamage(CombatStrength attacker, CombatStrength defender,
        int attackCoefficient, int defenseCoefficient, int durability, bool usesRockfallPower = false)
    {
        attacker.Validate();
        defender.Validate();
        ArgumentOutOfRangeException.ThrowIfNegative(attackCoefficient);
        ArgumentOutOfRangeException.ThrowIfNegative(defenseCoefficient);
        ArgumentOutOfRangeException.ThrowIfNegative(durability);
        if (attacker.Soldiers == 0 || defender.Soldiers == 0) return 0;
        var attack = BigInteger.Max(1, usesRockfallPower ? RockfallPower(attacker) : FormationPower(attacker, attackCoefficient, 25));
        var defense = BigInteger.Max(1, FormationPower(defender, defenseCoefficient, 15) + (long)durability * 2 / 5);
        var margin = BigInteger.Max(0, attack - defense + 900);
        var a = BigInteger.Max(1, margin * margin / 2000);
        ulong b = ((ulong)attacker.Soldiers + defender.Soldiers) / 70 + 400;
        return (uint)BigInteger.Min(defender.Soldiers, a * b / 1200);
    }

    /// <summary>Wall attack term shown in unit previews, before damage scaling and target durability limits.</summary>
    public static long WallAttackStrength(uint soldiers, int leadership, int wallCoefficient)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(leadership);
        ArgumentOutOfRangeException.ThrowIfNegative(wallCoefficient);
        long troopStrength = (long)(SquareRoot(soldiers) * SquareRoot((ulong)EconomyMath.ExecutedAbility(leadership) * 100) / 3000);
        return troopStrength + 3L * (Math.Max(0L, wallCoefficient - 2L) / 9) + 1;
    }

    public static int DurabilityDamage(uint soldiers, int leadership, int wallCoefficient, int durability)
    {
        long strength = WallAttackStrength(soldiers, leadership, wallCoefficient);
        ArgumentOutOfRangeException.ThrowIfNegative(durability);
        if (soldiers == 0 || durability == 0) return 0;
        return (int)Math.Min(durability, Math.Max(1, strength / 3));
    }

    public static CombatLoss Loss(uint soldiers, uint requested, int woundedPercent, uint wounded, uint woundedCapacity)
    {
        if (woundedPercent is < 0 or > 100 || wounded > woundedCapacity)
            throw new ArgumentException("Invalid wounded allocation.");
        uint actual = Math.Min(soldiers, requested);
        uint intended = (uint)((ulong)actual * (uint)woundedPercent / 100);
        uint added = Math.Min(intended, woundedCapacity - wounded);
        return new(soldiers - actual, added, actual - added, intended - added);
    }

    private static BigInteger FormationPower(CombatStrength strength, int coefficient, int divisor) =>
        Power(strength) * 2 * coefficient / divisor;

    private static BigInteger Power(CombatStrength value)
    {
        uint cap = CommandCappedSoldiers(value);
        var term = SquareRoot(cap / (uint)value.Intensity) / 6 + (BigInteger)value.Leadership * 2 + 30;
        var adjusted = (BigInteger)value.Effect * term / 10;
        return (value.Morale + 100) * (5 * adjusted / ((long)value.Intensity + 4)) / 200;
    }

    private static uint CommandCappedSoldiers(CombatStrength value)
    {
        var command = (BigInteger)value.Leadership * 5 / 2;
        return (uint)BigInteger.Min(value.Soldiers, command * command);
    }

    private static BigInteger RockfallPower(CombatStrength value) =>
        4 * (SquareRoot(CommandCappedSoldiers(value)) / 6 + (BigInteger)value.Leadership * 2 + 30) / 5;

    private static ulong SquareRoot(ulong value)
    {
        ulong low = 0, high = Math.Min(value, uint.MaxValue) + 1;
        while (low + 1 < high)
        {
            ulong middle = low + (high - low) / 2;
            if (middle <= value / middle) low = middle;
            else high = middle;
        }
        return low;
    }
}

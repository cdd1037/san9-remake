using San9.Core.Domain;

namespace San9.Core.Tests;

public sealed class CombatMathTests
{
    private static readonly CombatStrength Troops = new(10000, 100, 80, 1, 10);

    [Fact]
    public void RockfallReplacesAttackPowerWithoutReapplyingMoraleIntensityOrCoefficient()
    {
        // Rockfall power=164, defense=206, margin=858, a=368, b=685, damage=210.
        Assert.Equal(210u, CombatMath.TroopDamage(Troops, Troops, 15, 10, 0, usesRockfallPower: true));
        Assert.Equal(210u, CombatMath.TroopDamage(Troops with { Morale = 10, Intensity = 3, Effect = 5 },
            Troops, 99, 10, 0, usesRockfallPower: true));
        Assert.NotEqual(210u, CombatMath.TroopDamage(Troops with { Morale = 10, Intensity = 3, Effect = 5 }, Troops, 99, 10, 0));
        Assert.Equal(0u, CombatMath.TroopDamage(Troops with { Soldiers = 0 }, Troops, 99, 10, 0, usesRockfallPower: true));
    }

    [Fact]
    public void ExplicitProfilesSelectTargetCoefficientAndRejectFieldWallDefense()
    {
        var profile = new CombatProfile(15, 10, 20, 10, 50);
        Assert.Equal(287u, CombatMath.TroopDamage(Troops, Troops, profile, profile, false, 0));
        Assert.Equal(231u, CombatMath.TroopDamage(Troops, Troops, profile, profile, true, 0));
        Assert.Throws<ArgumentException>(() => CombatMath.TroopDamage(Troops, Troops, profile, profile, false, 100));
        Assert.Throws<ArgumentException>(() => CombatMath.TroopDamage(Troops, Troops, profile with { Defense = -1 }, profile, false, 0));
    }

    [Fact]
    public void NormalDamageMatchesIntegerWorkedExampleAndDurabilityProtectsGarrison()
    {
        // Base 206; attack 309; defense 206; a=503, b=685; loss=287.
        Assert.Equal(287u, CombatMath.TroopDamage(Troops, Troops, 15, 10, 0));
        Assert.Equal(231u, CombatMath.TroopDamage(Troops, Troops, 10, 10, 0));
        Assert.True(CombatMath.TroopDamage(Troops, Troops, 15, 10, 500) < 287);
        Assert.Equal(3, CombatMath.DurabilityDamage(10000, 80, 20, 100));
        Assert.Equal(2, CombatMath.DurabilityDamage(10000, 80, 20, 2));
    }

    [Fact]
    public void RaisingDefenseNeverIncreasesDamageEvenPastNegativeAttackMargin()
    {
        uint previous = uint.MaxValue;
        for (int defense = 0; defense <= 200; defense++)
        {
            uint damage = CombatMath.TroopDamage(Troops, Troops, 15, defense, 0);
            Assert.True(damage <= previous);
            previous = damage;
        }
        Assert.Equal(0u, previous);
    }

    [Fact]
    public void ExtremeLegalParametersRemainBoundedWithoutOverflow()
    {
        var enormous = new CombatStrength(uint.MaxValue, 100, int.MaxValue, 1, int.MaxValue);
        Assert.Equal(10000u, CombatMath.TroopDamage(enormous, Troops, int.MaxValue, 1, 0));
        // The minimum power term remains one; the population term still scales with both armies.
        Assert.Equal(51131u, CombatMath.TroopDamage(Troops, enormous, 1, int.MaxValue, int.MaxValue));
        Assert.InRange(CombatMath.DurabilityDamage(uint.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue), 1, int.MaxValue);
        Assert.InRange(CombatMath.TroopDamage(enormous with { Intensity = int.MaxValue }, enormous,
            int.MaxValue, int.MaxValue, 0), 0u, uint.MaxValue);
    }

    [Theory]
    [InlineData(5u, 9u, 50, 0u, 10u, 0u, 2u, 3u, 0u)]
    [InlineData(5u, 5u, 80, 9u, 10u, 0u, 1u, 4u, 3u)]
    [InlineData(100u, 30u, 50, 10u, 10u, 70u, 0u, 30u, 15u)]
    [InlineData(100u, 0u, 50, 0u, 10u, 100u, 0u, 0u, 0u)]
    public void WoundedAndDeadAccountForEveryActualLoss(uint soldiers, uint requested, int percent,
        uint wounded, uint capacity, uint remaining, uint added, uint dead, uint overflow)
    {
        var result = CombatMath.Loss(soldiers, requested, percent, wounded, capacity);
        Assert.Equal(new CombatLoss(remaining, added, dead, overflow), result);
        Assert.Equal((ulong)soldiers, (ulong)result.RemainingSoldiers + result.AddedWounded + result.Dead);
    }

    [Fact]
    public void EmptyCombatantsCannotAttackAndInvalidInputsAreRejected()
    {
        Assert.Equal(0u, CombatMath.TroopDamage(Troops with { Soldiers = 0 }, Troops, 15, 10, 0));
        Assert.Equal(0u, CombatMath.TroopDamage(Troops, Troops with { Soldiers = 0 }, 15, 10, 0));
        Assert.Equal(0, CombatMath.DurabilityDamage(0, 80, 20, 100));
        Assert.Equal(0, CombatMath.DurabilityDamage(10000, 80, 20, 0));
        Assert.Throws<ArgumentException>(() => CombatMath.TroopDamage(Troops with { Intensity = 0 }, Troops, 15, 10, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CombatMath.TroopDamage(Troops, Troops, -1, 10, 0));
        Assert.Throws<ArgumentException>(() => CombatMath.Loss(100, 10, 101, 0, 10));
        Assert.Throws<ArgumentException>(() => CombatMath.Loss(100, 10, 50, 11, 10));
    }
}

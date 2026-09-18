using San9.Core.Domain;

namespace San9.Core.Tests;

public sealed class DuelStrikeTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(59, 29)]
    [InlineData(60, 30)]
    [InlineData(61, 31)]
    [InlineData(80, 50)]
    [InlineData(81, 52)]
    [InlineData(90, 70)]
    [InlineData(100, 100)]
    [InlineData(int.MaxValue, 6442450741L)]
    public void ScoreHasSpecifiedBreakpointsAndSupportsAuthoredAbilities(int martial, long expected) =>
        Assert.Equal(expected, DuelStrike.Score(martial));

    [Theory]
    [InlineData(0, 99, 99, DuelStrikeKind.Parry, 0, 0, 1)]
    [InlineData(99, 0, 2, DuelStrikeKind.FinishingStrike, 73, 3, 4)]
    [InlineData(99, 99, 0, DuelStrikeKind.DoubleStrike, 42, 0, 4)]
    [InlineData(99, 99, 99, DuelStrikeKind.Normal, 21, 0, 4)]
    public void FixedVectorsProveShortCircuitOrder(ulong parry, ulong finish, ulong last,
        DuelStrikeKind kind, int damage, int injury, int draws)
    {
        var random = new Queue<ulong>([parry, 1, finish, last]);
        var result = DuelStrike.Resolve(new(80, 1, 100, 0), new(80, 1, 100, 0), random.Dequeue);
        Assert.Equal(new(kind, damage, injury), result);
        Assert.Equal(4 - draws, random.Count);
    }

    [Theory]
    [InlineData(19, 22)]
    [InlineData(20, 33)]
    public void DominantAttackStartsAtTwentyPointAdvantage(int delta, int expected)
    {
        var random = new Queue<ulong>(delta == 19 ? [99, 0, 99, 99] : [0, 99, 99]);
        Assert.Equal(expected, DuelStrike.Resolve(new(80 + delta, 1, 100, 0, DuelAdvantages.DominantAttack),
            new(80, 1, 100, 0), random.Dequeue).Damage);
        Assert.Empty(random);
    }

    [Theory]
    [InlineData(19, DuelStrikeKind.Normal)]
    [InlineData(20, DuelStrikeKind.Parry)]
    public void DominantParryStartsAtTwentyPointAdvantage(int delta, DuelStrikeKind expected)
    {
        var random = new Queue<ulong>([39, 0, 99, 99]);
        Assert.Equal(expected, DuelStrike.Resolve(new(80, 1, 100, 0),
            new(80 + delta, 1, 100, 0, DuelAdvantages.DominantParry), random.Dequeue).Kind);
    }

    [Theory]
    [InlineData(19, DuelStrikeKind.Normal)]
    [InlineData(20, DuelStrikeKind.DoubleStrike)]
    public void DominantDoubleStrikeStartsAtTwentyPointAdvantage(int delta, DuelStrikeKind expected)
    {
        var random = new Queue<ulong>(delta == 19 ? [99, 0, 99, 12] : [0, 99, 12]);
        Assert.Equal(expected, DuelStrike.Resolve(new(80 + delta, 0, 100, 0, DuelAdvantages.DominantDoubleStrike),
            new(80, 1, 100, 0), random.Dequeue).Kind);
        Assert.Empty(random);
    }

    [Fact]
    public void NegativeProbabilitiesSkipDrawsAndExtremeAbilityDoesNotOverflow()
    {
        var random = new Queue<ulong>([99, 0]);
        var result = DuelStrike.Resolve(new(0, 3, 100, 0), new(int.MaxValue, 0, 100, 0), random.Dequeue);
        Assert.Equal(new(DuelStrikeKind.Normal, 5, 0), result);
        Assert.Empty(random);
    }

    [Fact]
    public void FinisherCapsDamageAndInjury()
    {
        var random = new Queue<ulong>([6, 0, 2]);
        var result = DuelStrike.Resolve(new(int.MaxValue, 0, 1, 0), new(0, 0, 9, 6), random.Dequeue);
        Assert.Equal(new(DuelStrikeKind.FinishingStrike, 9, 7), result);
        Assert.Empty(random);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(15)]
    public void ZeroOrNegativeParryLeavesFirstDrawForBaseDamage(int advantage)
    {
        var random = new Queue<ulong>([1, 99, 99]);
        var result = DuelStrike.Resolve(new(80 + advantage, 0, 100, 0), new(80, 0, 100, 0), random.Dequeue);
        Assert.Equal(new(DuelStrikeKind.Normal, 23, 0), result);
        Assert.Empty(random);
    }

    [Fact]
    public void ZeroFinisherLeavesNextDrawForDoubleStrike()
    {
        var random = new Queue<ulong>([99, 0, 0]);
        var result = DuelStrike.Resolve(new(70, 0, 100, 0), new(80, 0, 100, 0), random.Dequeue);
        Assert.Equal(new(DuelStrikeKind.DoubleStrike, 34, 0), result);
        Assert.Empty(random);
    }

    [Fact]
    public void ZeroDoubleStrikeAndNegativeFinisherConsumeOnlyParryAndBaseDraws()
    {
        var random = new Queue<ulong>([99, 0]);
        var result = DuelStrike.Resolve(new(65, 3, 100, 0), new(80, 0, 100, 0), random.Dequeue);
        Assert.Equal(new(DuelStrikeKind.Normal, 15, 0), result);
        Assert.Empty(random);
    }

    [Fact]
    public void InvalidInputConsumesNoRandom()
    {
        ulong Unexpected() => throw new InvalidOperationException("Unexpected random draw.");
        var fighter = new DuelFighter(80, 1, 100, 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => DuelStrike.Resolve(fighter with { HitPoints = 0 }, fighter, Unexpected));
        Assert.Throws<ArgumentOutOfRangeException>(() => DuelStrike.Resolve(fighter, fighter with { Martial = -1 }, Unexpected));
        Assert.Throws<ArgumentOutOfRangeException>(() => DuelStrike.Resolve(fighter, fighter with { Advantages = (DuelAdvantages)8 }, Unexpected));
    }

    [Theory]
    [InlineData(79, 80, 2, 99, 20)]
    [InlineData(80, 79, 2, 99, 20)]
    [InlineData(76, 80, 2, 99, 19)]
    public void SignedDivisionTruncatesTowardZero(int attack, int defense, ulong parry, ulong finish, int expected)
    {
        var random = new Queue<ulong>([parry, 0, finish, 99]);
        Assert.Equal(expected, DuelStrike.Resolve(new(attack, 0, 100, 0), new(defense, 0, 100, 0), random.Dequeue).Damage);
    }

    [Theory]
    [InlineData(100, 1, 21)]
    [InlineData(81, 1, 21)]
    [InlineData(80, 2, 22)]
    [InlineData(1, 5, 25)]
    public void WoundedAttackerExpandsBaseDamageRandomRange(int hp, ulong roll, int expected)
    {
        var random = new Queue<ulong>([99, roll, 99, 99]);
        Assert.Equal(expected, DuelStrike.Resolve(new(80, 1, hp, 0), new(80, 1, 100, 0), random.Dequeue).Damage);
    }

    [Theory]
    [InlineData(79, 1)]
    [InlineData(80, 3)]
    public void FinisherInjuryRangeDependsOnMartialDifference(int martial, int injury)
    {
        var random = new Queue<ulong>([99, 0, 0, 2]);
        Assert.Equal(injury, DuelStrike.Resolve(new(martial, 0, 100, 0), new(80, 0, 100, 0), random.Dequeue).DefenderInjury);
    }

    [Theory]
    [InlineData(99, 99, DuelStrikeKind.Normal)]
    [InlineData(99, 0, DuelStrikeKind.DoubleStrike)]
    public void AllDamagingResultsClampToRemainingHitPoints(ulong finish, ulong doubled, DuelStrikeKind expected)
    {
        var random = new Queue<ulong>([99, 0, finish, doubled]);
        var result = DuelStrike.Resolve(new(80, 1, 100, 0), new(80, 1, 1, 0), random.Dequeue);
        Assert.Equal(expected, result.Kind);
        Assert.Equal(1, result.Damage);
    }
}

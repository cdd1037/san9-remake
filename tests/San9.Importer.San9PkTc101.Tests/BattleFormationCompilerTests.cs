using San9.Content.Interface;
using San9.Importer.San9PkTc101.Interface;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class BattleFormationCompilerTests
{
    [Theory]
    [InlineData(0,2,30)]
    [InlineData(1,2,45)]
    [InlineData(3,7,22)]
    [InlineData(4,7,33)]
    [InlineData(6,9,29)]
    [InlineData(9,11,45)]
    [InlineData(10,11,36)]
    [InlineData(12,13,13)]
    [InlineData(12,14,9)]
    [InlineData(12,15,5)]
    [InlineData(12,16,3)]
    [InlineData(13,16,3)]
    [InlineData(14,16,3)]
    [InlineData(15,16,3)]
    [InlineData(12,22,3)]
    public void NativeFormationFamiliesPreserveTheirMembership(int mode, int body, int count)
    {
        var formation = BattleFormationCompiler.Compile(mode, body, -1, _ => 0);
        Assert.Equal(count, formation.Sum(line => line.Count));
    }

    [Fact]
    public void FortressUsesFlanksAndGarrisonInsteadOfTheOrdinaryFieldFormation()
    {
        var formation = BattleFormationCompiler.Compile(0, 10, 2, address => address);
        Assert.Equal(39, formation.Sum(line => line.Count));
        Assert.Equal(12, formation.Where(line => line.Jitter == 0).Sum(line => line.Count));
        Assert.Equal(27, formation.Where(line => line.Group == 1).Sum(line => line.Count));
    }

    [Fact]
    public void MotionClassificationIsIndependentOfTheAvailableSpriteSlots()
    {
        Assert.Equal(BattleZoomActionKind.Melee, BattleFormationCompiler.Action(14).Kind);
        Assert.Equal(BattleZoomActionKind.AssaultWave, BattleFormationCompiler.Action(7).Kind);
        Assert.Equal(BattleZoomActionKind.ContinuousVolley, BattleFormationCompiler.Action(11).Kind);
        Assert.True(BattleFormationCompiler.Action(11).AlternateGroups);
        Assert.True(BattleFormationCompiler.Action(11).IntervalMilliseconds < BattleFormationCompiler.Action(10).IntervalMilliseconds);
    }

    [Theory]
    [InlineData(7,4)]
    [InlineData(8,6)]
    public void BothMovingArcherTacticsDeclareTheirProjectileOrigins(int mode, int count)
    {
        var action = BattleFormationCompiler.Action(mode);
        var emission = Assert.IsType<BattleWaveEmission>(action.WaveEmission);
        Assert.All(emission.Origins, phase => Assert.Equal(count, phase.Length * emission.ProjectilesPerOrigin));
        Assert.DoesNotContain(Enumerable.Range(0,16).Where(i => i is not (7 or 8)), i => BattleFormationCompiler.Action(i).WaveEmission is not null);
    }
}

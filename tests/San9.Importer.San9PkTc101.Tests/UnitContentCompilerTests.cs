using San9.Core.Domain;
using San9.Importer.San9PkTc101.Scenarios;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class UnitContentCompilerTests
{
    private static San9ScenarioUnitRecord Source => new(
        7, 1000, 0, 0, 0, 7, 1, 0, 100, 38, 0, 0, 27, 3, 0, 190, 180);

    [Theory]
    [InlineData(123u, 123u)]
    [InlineData(60000u, 50000u)]
    public void WoundedUsesSourceLoadingRuleAndMoraleIsPreserved(uint source, uint expected)
    {
        var unit = Assert.Single(UnitContentCompiler.Compile([Source with { WoundedSoldierCount = source, Morale = 73 }], _ => 1));
        Assert.Equal(expected, unit.Wounded);
        Assert.Equal(73, unit.Morale);
        Assert.Throws<InvalidDataException>(() => UnitContentCompiler.Compile([Source with { Morale = 101 }], _ => 1));
    }

    [Fact]
    public void ServiceBaseSurvivesWhilePositionAndRootOwnershipAreConvertedIndependently()
    {
        var unit = Assert.Single(UnitContentCompiler.Compile([Source], id => id == 3 ? 1 : null));
        Assert.Equal(7, unit.Id);
        Assert.Equal(1, unit.OwnerId);
        Assert.Equal(3, unit.CorpsId);
        Assert.Equal(27, unit.ServicePlaceId);
        Assert.Equal(new WorldPosition(952, 902), unit.Position);
    }

    [Theory]
    [InlineData(249, 249)]
    [InlineData(250, null)]
    [InlineData(65535, null)]
    public void SourceServiceSentinelDoesNotBecomeACoordinateOrCorpsReference(int source, int? expected)
    {
        var unit = Assert.Single(UnitContentCompiler.Compile([Source with { HomeBaseCode = (ushort)source }], id => 1));
        Assert.Equal(expected, unit.ServicePlaceId);
    }

    [Fact]
    public void InactiveSlotsDoNotRequireReferencesButActiveInvalidCoordinatesFail()
    {
        var invalid = Source with { CoordinateX = ushort.MaxValue, ForceCode = ushort.MaxValue };
        Assert.Empty(UnitContentCompiler.Compile([invalid with { UnitStatusCode = 0 }], _ => throw new InvalidOperationException()));
        Assert.Throws<InvalidDataException>(() => UnitContentCompiler.Compile([invalid], id => 1));
        Assert.Throws<InvalidDataException>(() => UnitContentCompiler.Compile([Source], id => null));
    }

    [Theory]
    [InlineData(37, UnitOrderClass.NonMilitary)]
    [InlineData(38, UnitOrderClass.Military)]
    [InlineData(52, UnitOrderClass.Military)]
    [InlineData(53, UnitOrderClass.NonMilitary)]
    [InlineData(70, UnitOrderClass.NonMilitary)]
    [InlineData(71, UnitOrderClass.None)]
    [InlineData(65535, UnitOrderClass.None)]
    public void SourceOrderClassUsesTheNormalizedCommandWithoutSynthesizingPersonnelFlags(int command, UnitOrderClass expected)
    {
        var unit = Assert.Single(UnitContentCompiler.Compile([Source with { CommandCode = (ushort)command }], _ => 1));
        Assert.Equal(expected, unit.OrderClass);
        Assert.False(unit.IsPersonnel);
        Assert.False(unit.IsEmbedded);
    }

    [Theory]
    [InlineData(1, UnitCondition.Normal)]
    [InlineData(2, UnitCondition.Engaged)]
    [InlineData(3, UnitCondition.Trap)]
    [InlineData(4, UnitCondition.Disguised)]
    [InlineData(5, UnitCondition.NoFormation)]
    [InlineData(6, UnitCondition.Confused)]
    [InlineData(7, UnitCondition.Routed)]
    [InlineData(8, UnitCondition.HighSpirits)]
    public void ConditionAndBothClassificationBitsSurviveIndependently(int status, UnitCondition expected)
    {
        var unit = Assert.Single(UnitContentCompiler.Compile([Source with { UnitStatusCode = (ushort)status,
            RuntimeFlags0C = 3, CommandCode = 21 }], _ => 1));
        Assert.Equal(expected, unit.Condition);
        Assert.Equal(UnitOrderClass.NonMilitary, unit.OrderClass);
        Assert.True(unit.IsPersonnel);
        Assert.True(unit.IsEmbedded);
    }
}

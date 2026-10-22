using San9.Core.Domain;
using San9.Importer.San9PkTc101.Scenarios;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class UnitContentCompilerTests
{
    private static San9ScenarioUnitRecord Source => new(
        7, 1000, 0, 0, 0, 7, 1, 0, 100, 38, 0, 0, 27, 3, 0, 190, 180);

    [Theory]
    [InlineData(12, true, false, false, false)]
    [InlineData(13, false, false, false, true)]
    [InlineData(14, false, true, false, false)]
    [InlineData(15, false, false, true, false)]
    public void PolicyFlagsBecomeIndependentSemanticFields(int bit, bool approach, bool pursuit, bool after, bool retreat)
    {
        var unit = Assert.Single(UnitContentCompiler.Compile([Source with { RuntimeFlags0C = 1u << bit }], _ => 1));
        Assert.Equal(new UnitPolicies(approach, pursuit, after, retreat), unit.Policies);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(13, 24)]
    [InlineData(14, 25)]
    [InlineData(15, 20)]
    [InlineData(16, 20)]
    [InlineData(17, 20)]
    [InlineData(18, 20)]
    [InlineData(19, 20)]
    [InlineData(20, null)]
    [InlineData(27, 20)]
    public void ScenarioDefaultsPreserveLandAndIndependentNavalChoices(int land, int? naval)
    {
        var unit = Assert.Single(UnitContentCompiler.Compile([Source with { FormationCode = (ushort)land }], _ => 1));
        Assert.Equal(land, unit.FormationId);
        Assert.Equal(naval, unit.NavalFormationId);
    }

    [Fact]
    public void DailyCycleEntryPreservesTheSourceFlagWithoutInventingCommandHistory()
    {
        var units = new[] { 0u, 8u }.Select(flags => Assert.Single(UnitContentCompiler.Compile(
            [Source with { RuntimeFlags0C = flags }], _ => 1))).ToArray();
        Assert.False(units[0].HasEnteredDailyCycle);
        Assert.True(units[1].HasEnteredDailyCycle);
        Assert.All(units, u => Assert.False(u.CommandedThisXun));
    }

    [Fact]
    public void TimedConditionPreservesItsSourceDuration()
    {
        var unit = Assert.Single(UnitContentCompiler.Compile([Source with { UnitStatusCode = 6, StatusDurationDays = 13 }], _ => 1));
        Assert.Equal(UnitCondition.Confused, unit.Condition);
        Assert.Equal(13, unit.ConditionDaysRemaining);
    }

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
        Assert.Equal(WorldDirection.South, unit.Facing);
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

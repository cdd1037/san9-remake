using San9.Core.Domain;
using San9.Importer.San9PkTc101.Scenarios;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class TreasureContentCompilerTests
{
    [Theory]
    [InlineData(0, 0, 0, 0, 0)]
    [InlineData(1, 9, 0, 0, 0)]
    [InlineData(2, 0, 0, 0, 9)]
    [InlineData(3, 0, 0, 9, 0)]
    [InlineData(4, 0, 0, 9, 0)]
    [InlineData(5, 0, 9, 0, 0)]
    [InlineData(6, 0, 0, 9, 0)]
    [InlineData(7, 0, 0, 0, 0)]
    [InlineData(8, 0, 0, 0, 0)]
    [InlineData(9, 0, 9, 0, 0)]
    [InlineData(10, 0, 0, 0, 0)]
    [InlineData(11, 0, 0, 0, 0)]
    [InlineData(12, 0, 0, 0, 0)]
    public void SourceCategoriesBecomeExplicitAbilityBonuses(ushort category, int war, int intelligence, int politics, int leadership)
    {
        var item = new San9ScenarioItemRecord(0, 0, 0, 0, category, 8, 9, ushort.MaxValue, 1, 2, ushort.MaxValue);
        Assert.Equal(new OfficerAbilities(war, intelligence, politics, leadership), TreasureContentCompiler.AbilityBonuses(item));
    }

    [Fact]
    public void InitialStatesKeepBuriedUnownedAndDisabledRecordsAndSourceLocations()
    {
        San9ScenarioItemRecord Item(ushort id, ushort state, ushort holder, ushort region) =>
            new(id, id, id, 0, 0, 10, 0, ushort.MaxValue, holder, state, region);
        var records = new[] { Item(0, 1, ushort.MaxValue, 117), Item(1, 2, 600, 38),
            Item(2, 2, ushort.MaxValue, ushort.MaxValue), Item(100, 0, ushort.MaxValue, 15) };
        var states = TreasureContentCompiler.InitialStates(records);
        Assert.Equal(4, states.Length);
        Assert.Equal(new TreasureState(0, null, TreasureDiscovery.Buried, 117), states[0]);
        Assert.Equal(new TreasureState(1, 600, TreasureDiscovery.Discovered, 38), states[1]);
        Assert.Equal(new TreasureState(2, null, TreasureDiscovery.Discovered, null), states[2]);
        Assert.Equal(new TreasureState(100, null, TreasureDiscovery.Disabled, 15), states[3]);
        Assert.Throws<InvalidDataException>(() => TreasureContentCompiler.InitialStates([Item(100, 1, ushort.MaxValue, 15)]));
    }
}

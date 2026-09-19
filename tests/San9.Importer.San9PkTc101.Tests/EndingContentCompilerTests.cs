using San9.Content.Scenarios;
using San9.Importer.San9PkTc101.Scenarios;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class EndingContentCompilerTests
{
    [Fact]
    public void ConvertsSourceVictoryGeographyAndSemanticCulturalItemMembership()
    {
        var terminal = EndingContentCompiler.Terminal();
        Assert.Equal(350, terminal.EndYear);
        Assert.Equal(Enumerable.Range(0, 45), terminal.CoreCityIds);
        var ending = EndingContentCompiler.Ending([
            new TreasureDefinition(901, "treasure.a", 11, 0, 0, null), new(902, "treasure.b", 12, 0, 0, null),
            new(903, "treasure.c", 10, 0, 0, null)]);
        Assert.Equal(new[] { 901, 902 }, ending.CulturalTreasureIds);
        Assert.Equal(new[] { 48, 49 }, ending.ExpansionGatewayCityIds);
        Assert.Equal(new[] { 46, 47 }, ending.DomesticGatewayCityIds);
        Assert.Equal(new[] { 17, 18, 20, 19 }, ending.ExpansionPublicOrderCityIds);
        Assert.Equal(new[] { 24, 25, 44, 43, 42, 23 }, ending.ExpansionCommerceCityIds);
        Assert.Equal(new[] { 1, 0, 18, 25, 43 }, ending.DomesticDurabilityCityIds);
        Assert.Equal(new[] { 15, 29, 16, 40, 23 }, ending.DomesticPublicOrderCityIds);
    }
}

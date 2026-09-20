using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Importer.San9PkTc101.Scenarios;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class CommunitySearchContentTests
{
    [Fact]
    public void SourceDefinitionsCoverEightEventsAndAllSitesWithoutRuntimeSourceSlots()
    {
        var places = Enumerable.Range(0, 300).ToHashSet();
        var definitions = EventContentCompiler.CommunitySearches(0, places);
        Assert.Equal(8, definitions.Length);
        Assert.Equal(new[] { -17, -18, -20, -21, -22, -30, -31, -32 }, definitions.Select(d => d.Priority));
        foreach (var definition in definitions.Cast<CommunitySearchEventDefinition>()) definition.Validate(places);
        var monuments = Assert.IsType<CommemorationSearchEventDefinition>(definitions[5]);
        var sacrifices = Assert.IsType<CommemorationSearchEventDefinition>(definitions[6]);
        var training = Assert.IsType<TrainingSiteSearchEventDefinition>(definitions[7]);
        Assert.Equal(12, monuments.Sites.Length); Assert.Equal(16, sacrifices.Sites.Length); Assert.Equal(6, training.Sites.Length);
        Assert.Equal(new[] { 20, 15, 18, 13, 29 }, monuments.Sites.Single(s => s.PlaceId == 30).RewardTacticIds);
        Assert.Equal(CommunityAbility.Politics, monuments.Sites.Single(s => s.PlaceId == 30).RewardAbility);
        Assert.Equal(28, sacrifices.Sites.Single(s => s.PlaceId == 30).RequiredTreasureId);
        Assert.Equal(new[] { 8, 11, 10, 15, 14 }, sacrifices.Sites.Single(s => s.PlaceId == 30).RewardTacticIds);
        Assert.Null(sacrifices.ChanceDivisor);
        Assert.Equal(2, training.Sites.Count(s => s.Category == 4)); Assert.Equal(4, training.Sites.Count(s => s.Category == 2));
        Assert.Equal(8, EventContentCompiler.CommunitySearches(1, places).Length);
        Assert.Empty(EventContentCompiler.CommunitySearches(2, places)); Assert.Empty(EventContentCompiler.CommunitySearches(9, places));
    }
    [Fact]
    public void SourceGlobalBitsTranslateToTheExactSiteHistoryIncludingTheWordBoundary()
    {
        var definitions = EventContentCompiler.CommunitySearches(0, Enumerable.Range(0, 300).ToHashSet()).OfType<CommemorationSearchEventDefinition>().ToArray();
        var sites = definitions.SelectMany(d => d.Sites).ToArray();
        for (int i = 0; i < sites.Length; i++)
        {
            uint[] flags = [0, 0, 0, 0]; int bit = 21 + i; flags[bit / 32] |= 1u << (bit % 32);
            Assert.Equal(sites[i].ConsumptionKey, Assert.Single(EventContentCompiler.ConsumedKeys(flags, new(190, 1, 1))));
        }
    }
    [Fact]
    public void FilteredSourcePlacesRetainOnlySupportedReferences()
    {
        var definitions = EventContentCompiler.CommunitySearches(0, new HashSet<int> { 30 });
        Assert.Equal(7, definitions.Length);
        foreach (var d in definitions.OfType<CommemorationSearchEventDefinition>()) Assert.Equal(30, Assert.Single(d.Sites).PlaceId);
    }
}

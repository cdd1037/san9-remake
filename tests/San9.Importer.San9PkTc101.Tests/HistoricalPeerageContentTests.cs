using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Importer.San9PkTc101.Scenarios;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class HistoricalPeerageContentTests
{
    private static ScenarioInitialState Scenario() => new(ScenarioInitialState.CurrentSchemaVersion, "7", 225, 1, 1, null,
        [new(0, "wei", 0, 0) { RankId = 0, CountryTitleId = 0 },
         new(1, "shu", 0, 0) { RankId = 0, CountryTitleId = 1 },
         new(2, "wu", 0, 0) { RankId = 1, LeaderId = 349 },
         new(3, "other", 0, 0) { RankId = 1 }], [], [], [])
    {
        Officers = [.. new[] { 326, 621, 347, 349 }.Select(id => new OfficerInitialState($"person.{id}",
            new OfficerState(id, null, null, new(50, 50, 50, 50), false) { BloodlineId = id == 349 ? 347 : id }))],
        Information = new([new(0, "emperor", 0, 0, 0, 2), new(1, "king", 0, 0, 0, 1),
            new(2, "none", 0, 0, 0, 0) { IsUnranked = true }], [], [], [], [])
        {
            CountryTitles = [.. new[] { (0, 326), (1, 621), (2, 347) }.Select(pair =>
                new CountryTitleDefinition(new(pair.Item1, true, true, false, [], [pair.Item2]), $"title.{pair.Item1}", -1))]
        }
    };

    [Fact]
    public void ExplicitSourceBindingsAssignRegimesAndKeepUnidentifiedForcesGeneric()
    {
        var result = HistoricalPeerageContent.Apply(Scenario(), true);
        Assert.Equal(HistoricalPeeragePresets.Wei, result.Forces[0].PersonalPeerageRegimeId);
        Assert.Equal(HistoricalPeeragePresets.Shu, result.Forces[1].PersonalPeerageRegimeId);
        Assert.Equal(HistoricalPeeragePresets.Wu, result.Forces[2].PersonalPeerageRegimeId);
        Assert.Equal(PersonalPeeragePreset.GenericId, result.Forces[3].PersonalPeerageRegimeId);
        Assert.Equal(HistoricalPeeragePresets.Wu, result.Information.CountryTitles.Single(t => t.Id == 2).Rule.PersonalPeerageRegimeId);
        var wei = new PersonalPeerageRegime(result.PersonalPeerageRegimes!.Single(r => r.Id == HistoricalPeeragePresets.Wei).Periods);
        Assert.Equal(221, wei.Period(221)!.EndYear);
        Assert.Equal(222, wei.Period(222)!.StartYear);
        Assert.Contains(326, wei.Period(222)!.RoyalBloodlines);
        Assert.DoesNotContain(621, wei.Period(222)!.RoyalBloodlines);
    }

    [Fact]
    public void FictionalOrAmbiguousSourcesDoNotAcquireInferredRegimes()
    {
        var source = Scenario();
        var fictional = HistoricalPeerageContent.Apply(source, false);
        Assert.All(fictional.Forces, f => Assert.Equal(PersonalPeeragePreset.GenericId, f.PersonalPeerageRegimeId));
        Assert.Single(fictional.PersonalPeerageRegimes!);
        var altered = source with { Information = source.Information with { CountryTitles = source.Information.CountryTitles
            .Select(t => t.Id == 0 ? t with { Rule = t.Rule with { PreferredBloodlineIds = [999] } } : t).ToArray() } };
        var result = HistoricalPeerageContent.Apply(altered, true);
        Assert.All(result.Forces, f => Assert.Equal(PersonalPeeragePreset.GenericId, f.PersonalPeerageRegimeId));
        Assert.Single(result.PersonalPeerageRegimes!);
    }
}

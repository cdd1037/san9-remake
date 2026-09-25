using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Content.Packages;
using San9.Content.Scenarios;
using San9.Core.Domain;

namespace San9.Content.Tests;

public sealed class PoliticalContentTests
{
    private static ScenarioInitialState Scenario() => new(ScenarioInitialState.CurrentSchemaVersion, "politics", 190, 1, 1, 0, [], [], [], [])
    {
        PoliticalEvents = new(1000, new("historical.ended")),
        Information = InformationDefinitions.Empty with
        {
            Ranks = [new(7000, "entry", 0, 0, 0, 2), new(15, "king", 0, 500, 2, 8), new(99, "emperor", 0, 1000, 5, 30)],
            CountryTitles = [new(new(4000, true, true, false, [], [6000]), "title", 50)]
        }
    };

    [Theory]
    [InlineData("rank")]
    [InlineData("title")]
    [InlineData("province")]
    [InlineData("ruler")]
    [InlineData("glyph")]
    [InlineData("key")]
    [InlineData("ownership")]
    public void PoliticalDefinitionsRejectUnknownReferencesAndInvalidDisplayValues(string defect)
    {
        var scenario = Scenario();
        scenario.Information.Validate(scenario); // Bloodline identity need not reference a living person.
        if (defect == "rank") scenario = scenario with { Information = scenario.Information with { Ranks = [scenario.Information.Ranks[0]] } };
        if (defect == "title") scenario = scenario with { ProvinceCountryTitleIds = ImmutableDictionary<int, int>.Empty.Add(1, 9999) };
        if (defect == "province") scenario = scenario with { ProvinceCountryTitleIds = ImmutableDictionary<int, int>.Empty.Add(1, 4000) };
        if (defect == "key") scenario = scenario with { PoliticalEvents = scenario.PoliticalEvents! with { CourtEndedConsumptionKey = default } };
        if (defect == "ownership") scenario = scenario with { Forces = [new(1, "a", 0, 0) { CountryTitleId = 4000 }, new(2, "b", 0, 0) { CountryTitleId = 4000 }] };
        if (defect is "ruler" or "glyph") scenario = scenario with { Information = scenario.Information with
        { CountryTitles = scenario.Information.CountryTitles.Select(t => defect == "ruler" ? t with { Rule = t.Rule with { PreferredRulerIds = [6000] } } : t with { FlagGlyph = 186 }).ToArray() } };
        Assert.Throws<InvalidDataException>(() => scenario.Information.Validate(scenario));
    }

    [Fact]
    public void TitleIdentityAndRuleFlagsAreRequiredAndCopyDoesNotAliasMutableArray()
    {
        var scenario = Scenario(); var copy = scenario.Information.Copy();
        var original = copy.CountryTitles[0]; scenario.Information.CountryTitles[0] = original with { NameKey = "changed" };
        Assert.Equal("title", copy.CountryTitles[0].NameKey);
        string json = JsonSerializer.Serialize(original.Rule, ScenarioContentFiles.DataJsonOptions);
        var node = JsonNode.Parse(json)!.AsObject(); Assert.True(node.Remove("manualSelectionAllowed"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CountryTitleRule>(node.ToJsonString(), ScenarioContentFiles.DataJsonOptions));
    }
}

using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Content.Packages;
using San9.Content.Scenarios;

namespace San9.Content.Tests;

public sealed class RankDefinitionTests
{
    private static ScenarioInitialState Scenario() => new(ScenarioInitialState.CurrentSchemaVersion,
        "ranks", 190, 1, 1, 0, [], [], [], []);

    [Fact]
    public void IndependentPrecedenceAllowsSparseIdentitiesAndEqualOrDecreasingTrustThresholds()
    {
        RankDefinition[] ranks = [new(8000, "low", 10000, 900, 0, 7),
            new(3, "middle", 20000, 900, 2, 20), new(91, "high", 30000, 100, 3, 100)];
        (InformationDefinitions.Empty with { Ranks = ranks }).Validate(Scenario());
        string json = JsonSerializer.Serialize(ranks, ScenarioContentFiles.DataJsonOptions);
        Assert.Equal(ranks, JsonSerializer.Deserialize<RankDefinition[]>(json, ScenarioContentFiles.DataJsonOptions));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(7)]
    public void NegativeOrDuplicatePrecedenceIsRejected(int precedence)
    {
        var information = InformationDefinitions.Empty with { Ranks = [new(8000, "a", 10000, 100, 0, 7),
            new(3, "b", 20000, 500, 0, precedence)] };
        Assert.Throws<InvalidDataException>(() => information.Validate(Scenario()));
    }

    [Fact]
    public void SerializedRankCannotSilentlyInferMissingPrecedenceFromTrustOrIdentity()
    {
        var json = JsonNode.Parse(JsonSerializer.Serialize(new RankDefinition(99, "rank", 10000, 100, 0, 0),
            ScenarioContentFiles.DataJsonOptions))!.AsObject();
        Assert.True(json.Remove("precedence"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RankDefinition>(json.ToJsonString(), ScenarioContentFiles.DataJsonOptions));
    }
}

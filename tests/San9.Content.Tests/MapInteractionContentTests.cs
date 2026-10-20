using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Content.Packages;
using San9.Content.Scenarios;
using San9.Core.Domain;

namespace San9.Content.Tests;

public sealed class MapInteractionContentTests
{
    [Fact]
    public void ContextExclusionsRequireKnownUniqueFacilityReferencesAndSurviveCopyAndJson()
    {
        var scenario = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "map", 190, 1, 1, 0, [], [], [], [])
        {
            Military = new(1, 1, [0], [new(0, 1, 1)], [], [])
            {
                Facilities = [new(8000, 0, 100, new(1, 1), 0, false, false, false, 1000, null, null, null, [])]
            },
            Information = InformationDefinitions.Empty with { MapContextExcludedFacilityIds = [8000] }
        };
        scenario.Information.Validate(scenario);
        Assert.Equal(new[] { 8000 }, scenario.Information.Copy().MapContextExcludedFacilityIds);
        var json = JsonSerializer.Serialize(scenario.Information, ScenarioContentFiles.DataJsonOptions);
        var restored = JsonSerializer.Deserialize<InformationDefinitions>(json, ScenarioContentFiles.DataJsonOptions)!;
        Assert.Equal(new[] { 8000 }, restored.MapContextExcludedFacilityIds);
        foreach (var ids in new int[][] { [8001], [8000, 8000] })
            Assert.Throws<InvalidDataException>(() => (restored with { MapContextExcludedFacilityIds = [.. ids] }).Validate(scenario));
        var node = JsonNode.Parse(json)!.AsObject();
        Assert.True(node.Remove("mapContextExcludedFacilityIds"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<InformationDefinitions>(node.ToJsonString(), ScenarioContentFiles.DataJsonOptions));
    }
}

using System.Text.Json;
using San9.Core.Domain;

namespace San9.Application.Tests;

public sealed class UnitServicePlaceTests
{
    [Fact]
    public void ServicePlaceIsAContentReferenceIndependentOfPositionAndOwnership()
    {
        var baseline = G01ScenarioTests.Fixture();
        var unit = new UnitState(10, 1, new(900, 800), 100) { ServicePlaceId = 2 };
        (baseline with { Units = [unit] }).Validate();
        Assert.Throws<InvalidDataException>(() => (baseline with { Units = [unit with { ServicePlaceId = 999 }] }).Validate());
        Assert.Throws<InvalidDataException>(() => (baseline with { Units = [unit with { ServicePlaceId = -1 }] }).Validate());
        (baseline with { Units = [unit with { ServicePlaceId = null }] }).Validate();
    }

    [Fact]
    public void MissingServicePlaceIsRejectedInsteadOfInferredFromCoordinates()
    {
        var unit = new UnitState(10, 1, new(900, 800), 100) { ServicePlaceId = 2 };
        var json = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(unit))!.AsObject();
        json.Remove(nameof(UnitState.ServicePlaceId));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<UnitState>(json.ToJsonString()));
    }
}

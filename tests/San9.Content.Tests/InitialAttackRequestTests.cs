using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Content.Scenarios;

namespace San9.Content.Tests;

public sealed class InitialAttackRequestTests
{
    private static ScenarioInitialState Fixture() => new(ScenarioInitialState.CurrentSchemaVersion, "requests", 190, 1, 1, 1000,
        [new(1000, "first", 0, 0, 0, 100), new(2000, "second", 1, 0, 0, 100), new(3000, "third", 2, 0, 0, 100)],
        [new(4000, "target", 3000, 0, 0, 0, 100, 100, 0, [])], [], [])
        { AttackRequests = [new(2000, 1000, 4000, 19), new(1000, 2000, 4000, 12)] };

    [Fact]
    public void AuthoredRequestsSupportIndependentIdsAndEitherOrder()
    {
        var content = Fixture();
        content.Validate();
        var restored = JsonSerializer.Deserialize<ScenarioInitialState>(JsonSerializer.Serialize(content))!;
        restored.Validate();
        Assert.Equal(content.AttackRequests.ToArray(), restored.AttackRequests.ToArray());
    }

    [Fact]
    public void InvalidPartiesTargetsDurationsAndDuplicateHoldersAreRejected()
    {
        var content = Fixture();
        ImmutableArray<AttackRequestInitialState>[] invalid = [default, [null!], [new(1000, 1000, 4000, 19)],
            [new(9000, 2000, 4000, 19)], [new(1000, 9000, 4000, 19)], [new(1000, 2000, 9000, 19)],
            [new(3000, 2000, 4000, 19)], [new(1000, 3000, 4000, 19)], [new(1000, 2000, 4000, 0)],
            [new(1000, 2000, 4000, -1)], [new(1000, 2000, 4000, 19), new(1000, 2000, 4000, 10)]];
        foreach (var requests in invalid)
            Assert.Throws<InvalidDataException>(() => (content with { AttackRequests = requests }).Validate());
        Assert.Throws<InvalidDataException>(() => (content with { Settlements = [content.Settlements[0] with { OwnerId = null }] }).Validate());
    }

    [Theory]
    [InlineData("RequestedForceId")]
    [InlineData("RequesterForceId")]
    [InlineData("TargetSettlementId")]
    [InlineData("RemainingXun")]
    [InlineData(null)]
    public void MissingFieldsAreRejected(string? field)
    {
        var json = JsonNode.Parse(JsonSerializer.Serialize(Fixture()))!;
        if (field is null) json.AsObject().Remove("AttackRequests");
        else json["AttackRequests"]![0]!.AsObject().Remove(field);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ScenarioInitialState>(json.ToJsonString()));
    }
}

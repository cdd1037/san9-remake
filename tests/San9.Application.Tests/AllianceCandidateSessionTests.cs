using San9.Tests;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class AllianceCandidateSessionTests
{
    private static ScenarioInitialState Definition() => new(ScenarioInitialState.CurrentSchemaVersion, "alliance-candidates", 190, 2, 1, 1,
        Enumerable.Range(1, 4).Select(id => new ForceInitialState(id, $"force.{id}", id, 50) { BaseTrust = 40 }).ToArray(),
        Enumerable.Range(1, 4).Select(id => new SettlementInitialState(1000 + id, $"city.{id}", id, id, 0,
            id == 1 ? 100000u : 0, 100, 100, id == 1 ? 50 : 0, []) { DraftPopulation = id == 1 ? 200001u : 0 }).ToArray(),
        [], []) { CityProvinceIds = Enumerable.Range(1, 4).ToDictionary(id => 1000 + id, _ => 9001) , Information = new([], [], [], Enumerable.Range(1, 4).Select(id => new San9.Core.Domain.CorpsState(id, id, null, new(0, 100000)) { IsRoot = true }).ToArray(), []) };

    [Fact]
    public void SessionOwnsProvinceMembershipAndRestoresReadOnlyCandidateQueries()
    {
        var definition = Definition();
        var session = GameSession.Start(definition, 42);
        var before = session.Capture();
        var candidates = session.GetAllianceCandidates();
        Assert.Equal(new[] { 2, 3, 4 }, candidates.LargestRealm!.EligibleMemberForceIds);
        StateAssert.Equal(before, session.Capture());
        definition.CityProvinceIds[1004] = 88;
        Assert.Equal(new[] { 2, 3, 4 }, session.GetAllianceCandidates().LargestRealm!.EligibleMemberForceIds);
        Assert.Null(GameSession.Start(definition, 42).GetAllianceCandidates().LargestRealm);

        var source = Definition();
        var restored = SaveService.Decode(SaveService.Encode(session), source, GameSession.EngineFactory(source));
        Assert.Equal(CanonicalJson.Text(candidates), CanonicalJson.Text(restored.GetAllianceCandidates()));
        StateAssert.Equal(before, restored.Capture());
    }

    [Fact]
    public void ProvinceContentRejectsInvalidReferencesAndMissingRequiredField()
    {
        var definition = Definition();
        Assert.Throws<InvalidDataException>(() => (definition with { CityProvinceIds = new() { [12345] = 0 } }).Validate());
        Assert.Throws<InvalidDataException>(() => (definition with { CityProvinceIds = new() { [1001] = -1 } }).Validate());
        var json = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(definition))!.AsObject();
        json.Remove("CityProvinceIds");
        Assert.Throws<System.Text.Json.JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<ScenarioInitialState>(json.ToJsonString()));
    }
}

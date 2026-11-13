using San9.Content.Scenarios;
using San9.Core.Domain;
using System.Collections.Immutable;

namespace San9.Content.Tests;

public sealed class ScenarioIdentityTests
{
    [Fact]
    public void FormationEvidenceRequiresUniqueValidEntriesForKnownFormations()
    {
        var officer = new OfficerInitialState("person", new(1, 9000, 1000, new(50, 50, 50, 50), false)
        { FormationProficiencies = ImmutableSortedDictionary<int, int>.Empty.Add(7, 100) });
        var good = new FormationEvidence(7, 100, FormationEvidenceKind.ExperienceInference, "来源", "基于经历的游戏推定");
        var scenario = Fixture() with { Officers = [officer with { InitialFormationEvidence = [good] }],
            Military = new(75, 1, Enumerable.Repeat(0, 75).ToImmutableArray(), [new(0, 1, null)], [new(7, 1, 0, 10000, 5, new(1, 1), TravelMode.Land)],
                Fixture().Settlements.Select(s => new BattlefieldPlace(s.Id, s.Position, new(1, 1))).ToImmutableArray()) };
        scenario.Validate();
        Assert.Throws<InvalidDataException>(() => (scenario with { Officers = [officer with { InitialFormationEvidence = [good, good] }] }).Validate());
        Assert.Throws<InvalidDataException>(() => (scenario with { Officers = [officer with { InitialFormationEvidence = [good with { FormationId = 8 }] }] }).Validate());
        Assert.Throws<InvalidDataException>(() => (scenario with { Officers = [officer with { InitialFormationEvidence = [good with { Source = "" }] }] }).Validate());
        Assert.Throws<InvalidDataException>(() => (scenario with { Officers = [officer with { InitialFormationEvidence = [good with { Proficiency = 1001 }] }] }).Validate());
    }
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void RemovedPersistentConditionsCannotEnterInitialWorldContent(int conditionId)
    {
        var scenario = Fixture() with { Units = [new(1, 9000, new(2, 2), 100)] };
        scenario.Validate();
        var options = San9.Content.Packages.ScenarioContentFiles.DataJsonOptions;
        var json = System.Text.Json.JsonSerializer.SerializeToNode(scenario, options)!;
        json["units"]![0]!["condition"] = conditionId;
        var invalid = System.Text.Json.JsonSerializer.Deserialize<ScenarioInitialState>(json.ToJsonString(), options)!;
        Assert.Throws<InvalidDataException>(() => invalid.Validate());
    }

    [Fact]
    public void OldMemberOrderContentRequiresReconversion()
    {
        var options = San9.Content.Packages.ScenarioContentFiles.DataJsonOptions;
        var scenario = Fixture() with { Units = [new(1, 9000, new(2, 2), 100)] };
        var json = System.Text.Json.JsonSerializer.SerializeToNode(scenario, options)!;
        var unit = json["units"]![0]!.AsObject();
        Assert.False(unit.ContainsKey("memberOrder"));
        unit["memberOrder"] = System.Text.Json.JsonSerializer.SerializeToNode(new[] { 10 });
        json["schemaVersion"] = ScenarioInitialState.CurrentSchemaVersion - 1;
        var previous = System.Text.Json.JsonSerializer.Deserialize<ScenarioInitialState>(json.ToJsonString(), options)!;
        Assert.Throws<InvalidDataException>(() => previous.Validate());
    }

    private static ScenarioInitialState Fixture() => new(ScenarioInitialState.CurrentSchemaVersion, "expanded", 190, 1, 1, 9000,
        [new(9000, "force", 0, 0)],
        Enumerable.Range(0, 75).Select(i => new SettlementInitialState(1000 + i, $"city.{i}", 9000,
            i, 0, 0, 100, 100, 0, []) { Kind = SettlementKind.City, MerchantPresenceChance = 100 }).ToArray(),
        new() { [2000] = "region" }, new() { [2000] = 1074 }) { Information = new([], [], [], [new San9.Core.Domain.CorpsState(9000, 9000, null, new(100, 100)) { IsRoot = true }], []) };

    [Fact]
    public void CityIdentityAndCountAreIndependentOfOriginalSlots()
    {
        Fixture().Validate();
    }

    [Fact]
    public void MilitaryCityConnectionsPreserveExplicitDirectionsAndRequirePublishedData()
    {
        var scenario = Fixture() with { MilitaryCityConnections = [new(1000, 1074)] };
        scenario.Validate();
        var options = San9.Content.Packages.ScenarioContentFiles.DataJsonOptions;
        var json = System.Text.Json.JsonSerializer.SerializeToNode(scenario, options)!;
        var decoded = System.Text.Json.JsonSerializer.Deserialize<ScenarioInitialState>(json.ToJsonString(), options)!;
        Assert.Equal(scenario.MilitaryCityConnections.ToArray(), decoded.MilitaryCityConnections.ToArray());
        Assert.Empty(decoded.CityConnections);
        json.AsObject().Remove("militaryCityConnections");
        Assert.Throws<System.Text.Json.JsonException>(() =>
            System.Text.Json.JsonSerializer.Deserialize<ScenarioInitialState>(json.ToJsonString(), options));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("facility")]
    [InlineData("self")]
    [InlineData("duplicate")]
    [InlineData("default")]
    public void MilitaryCityConnectionsRejectInvalidCityEdges(string invalid)
    {
        var scenario = Fixture();
        scenario = scenario with { MilitaryCityConnections = invalid switch
        {
            "unknown" => [new(1000, 9999)],
            "facility" => [new(1000, 1074)],
            "self" => [new(1000, 1000)],
            "duplicate" => [new(1000, 1074), new(1000, 1074)],
            _ => default
        } };
        if (invalid == "facility") scenario = scenario with { Settlements = scenario.Settlements
            .Select(s => s.Id == 1074 ? s with { Kind = SettlementKind.Facility } : s).ToArray(), RegionCityIds = [] };
        Assert.Throws<InvalidDataException>(() => scenario.Validate());
    }

    [Fact]
    public void RegionMustReferenceAnExistingCityRatherThanANameEntry()
    {
        var scenario = Fixture();
        Assert.Throws<InvalidDataException>(() => (scenario with { RegionCityIds = new() { [2000] = 2000 } }).Validate());
        var settlements = scenario.Settlements.ToArray();
        settlements[^1] = settlements[^1] with { Kind = SettlementKind.Facility, Icons = [] };
        Assert.Throws<InvalidDataException>(() => (scenario with { Settlements = settlements }).Validate());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1000)]
    public void FacilityCannotAcquireCityIconsThroughItsId(int id)
    {
        var scenario = Fixture();
        var settlements = scenario.Settlements.ToArray();
        settlements[0] = settlements[0] with { Id = id, Kind = SettlementKind.Facility };
        Assert.Throws<InvalidDataException>(() => (scenario with { Settlements = settlements }).Validate());
    }
}

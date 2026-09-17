using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Content.Packages;
using San9.Content.Scenarios;
using San9.Core.Domain;

namespace San9.Content.Tests;

public sealed class OfficerLifecycleContentTests
{
    private static ScenarioInitialState Scenario() => new(ScenarioInitialState.CurrentSchemaVersion, "appearance", 190, 1, 1, 9000,
        [new(9000, "force", 0, 100, 100, 0)],
        [new(1000, "city", 9000, 0, 0, 0, 100, 100, 0, [])], [], [])
    {
        Officers = [new("future", new(80000, null, 1000, new(50, 50, 50, 50), false)
        { Status = OfficerStatus.NotAppeared, BirthYear = 190, AppearanceYear = 222, BloodlineId = 100000 })]
    };

    [Fact]
    public void FamilyIdentityIsNotLimitedToOfficerIdsAndAppearanceIsIndependentOfBirth()
    {
        var scenario = Scenario();
        scenario.Validate();
        var decoded = JsonSerializer.Deserialize<ScenarioInitialState>(JsonSerializer.Serialize(scenario, ScenarioContentFiles.DataJsonOptions),
            ScenarioContentFiles.DataJsonOptions)!;
        decoded.Validate();
        Assert.Equal(222, Assert.Single(decoded.Officers).State.AppearanceYear);
        Assert.Equal(100000, Assert.Single(decoded.Officers).State.BloodlineId);
    }

    [Fact]
    public void FuturePersonRequiresAnAuthoredFallbackPlace()
    {
        var scenario = Scenario();
        scenario = scenario with { Officers = [scenario.Officers[0] with
            { State = scenario.Officers[0].State with { SettlementId = null } }] };
        Assert.Throws<InvalidDataException>(scenario.Validate);
    }

    [Theory]
    [InlineData("appearanceYear")]
    [InlineData("bloodlineId")]
    [InlineData("annualAppearancesEnabled")]
    [InlineData("naturalDeathsEnabled")]
    [InlineData("lifespanTier")]
    [InlineData("successionPriority")]
    public void MissingAppearanceInputsRejectOldOrIncompleteContent(string property)
    {
        var root = JsonNode.Parse(JsonSerializer.Serialize(Scenario(), ScenarioContentFiles.DataJsonOptions))!;
        if (property is "annualAppearancesEnabled" or "naturalDeathsEnabled") root.AsObject().Remove(property);
        else root["officers"]![0]!["state"]!.AsObject().Remove(property);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ScenarioInitialState>(root.ToJsonString(), ScenarioContentFiles.DataJsonOptions));
    }

    [Theory]
    [InlineData(-1, 100000)]
    [InlineData(222, -1)]
    public void InvalidAppearanceOrFamilyIdentityIsRejected(int year, int bloodline)
    {
        var scenario = Scenario();
        scenario = scenario with { Officers = [scenario.Officers[0] with
            { State = scenario.Officers[0].State with { AppearanceYear = year, BloodlineId = bloodline } }] };
        Assert.Throws<InvalidDataException>(scenario.Validate);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public void NegativeMortalityAndSuccessionInputsAreRejected(int lifespan, int priority)
    {
        var scenario = Scenario();
        scenario = scenario with { Officers = [scenario.Officers[0] with
            { State = scenario.Officers[0].State with { LifespanTier = lifespan, SuccessionPriority = priority } }] };
        Assert.Throws<InvalidDataException>(scenario.Validate);
    }
}

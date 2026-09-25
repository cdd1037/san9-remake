using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Content.Packages;
using San9.Content.Scenarios;
using San9.Core.Domain;

namespace San9.Content.Tests;

public sealed class OfficerLifecycleContentTests
{
    [Fact]
    public void RelativeBirthInputRoundTripsAndIsRequiredEvenWhenNull()
    {
        var person = new OfficerInitialState("person", new(1, null, null, new(50, 50, 50, 50), false)
            { Status = OfficerStatus.Special }) { AgeAtScenarioStart = 34 };
        var json = JsonSerializer.Serialize(person, ScenarioContentFiles.DataJsonOptions);
        var restored = JsonSerializer.Deserialize<OfficerInitialState>(json, ScenarioContentFiles.DataJsonOptions)!;
        Assert.Equal(156, restored.CreateState(190).BirthYear);
        Assert.Equal(0, restored.State.BirthYear);
        var root = JsonNode.Parse(json)!.AsObject();
        root.Remove("ageAtScenarioStart");
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<OfficerInitialState>(root.ToJsonString(), ScenarioContentFiles.DataJsonOptions));
    }

    [Theory]
    [InlineData("battleDeathsAllowed")]
    [InlineData("duelCapturesAllowed")]
    public void MilitaryDeathAndCapturePoliciesAreRequired(string property)
    {
        var definition = new MilitaryDefinition(1, 1, [0], [new(0, 1, null)], [], []);
        var root = JsonNode.Parse(JsonSerializer.Serialize(definition, ScenarioContentFiles.DataJsonOptions))!.AsObject();
        root.Remove(property);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<MilitaryDefinition>(root.ToJsonString(), ScenarioContentFiles.DataJsonOptions));
    }

    [Fact]
    public void DuelFormationTraitIsRequiredAndRoundTrips()
    {
        var formation = new FormationRule(9000, 10, 0, 10000, 10, new(1, 1), TravelMode.Land) { DoublesDuelChance = true };
        var json = JsonSerializer.Serialize(formation, ScenarioContentFiles.DataJsonOptions);
        Assert.True(JsonSerializer.Deserialize<FormationRule>(json, ScenarioContentFiles.DataJsonOptions)!.DoublesDuelChance);
        var root = JsonNode.Parse(json)!.AsObject();
        root.Remove("doublesDuelChance");
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<FormationRule>(root.ToJsonString(), ScenarioContentFiles.DataJsonOptions));
    }

    private static ScenarioInitialState Scenario() => new(ScenarioInitialState.CurrentSchemaVersion, "appearance", 190, 1, 1, 9000,
        [new(9000, "force", 0, 0)],
        [new(1000, "city", 9000, 0, 0, 0, 100, 100, 0, [])], [], [])
    {
        Officers = [new("future", new(80000, null, 1000, new(50, 50, 50, 50), false)
        { Status = OfficerStatus.NotAppeared, BirthYear = 190, AppearanceYear = 222, BloodlineId = 100000 })]
    , Information = new([], [], [], [new San9.Core.Domain.CorpsState(9000, 9000, null, new(100, 100)) { IsRoot = true }], []) };

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
    [InlineData("freeOfficerMigrationEnabled")]
    [InlineData("lifespanTier")]
    [InlineData("successionPriority")]
    [InlineData("duelAdvantages")]
    public void MissingAppearanceInputsRejectOldOrIncompleteContent(string property)
    {
        var root = JsonNode.Parse(JsonSerializer.Serialize(Scenario(), ScenarioContentFiles.DataJsonOptions))!;
        if (property is "annualAppearancesEnabled" or "naturalDeathsEnabled" or "freeOfficerMigrationEnabled") root.AsObject().Remove(property);
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

    [Theory]
    [InlineData(-1)]
    [InlineData(8)]
    public void UndefinedDuelAdvantagesAreRejected(int flags)
    {
        var scenario = Scenario();
        scenario = scenario with { Officers = [scenario.Officers[0] with
            { State = scenario.Officers[0].State with { DuelAdvantages = (DuelAdvantages)flags } }] };
        Assert.Throws<InvalidDataException>(scenario.Validate);
    }
}

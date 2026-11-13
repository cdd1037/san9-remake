using System.Collections.Immutable;
using System.Text.Json;
using San9.Content.Packages;
using San9.Content.Scenarios;
using San9.Core.Domain;

namespace San9.Content.Tests;

public sealed class PersonalPeerageContentTests
{
    [Fact]
    public void LegacyPresetIsExplicitGenericAndPreservesAuthoredRegimes()
    {
        var scenario = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "fixture", 190, 1, 1, null,
            [new ForceInitialState(1, "force", 0, 100)], [], [], [])
        { Information = InformationDefinitions.Empty with { Ranks = [
            new RankDefinition(9, "无", 0, 0, 0, 0) { IsUnranked = true },
            new RankDefinition(5, "官阶", 0, 0, 0, 1), new RankDefinition(1, "最高", 0, 0, 0, 2)] } };
        var upgraded = PersonalPeeragePreset.UpgradeLegacy(scenario);
        var regime = Assert.Single(upgraded.PersonalPeerageRegimes!);
        Assert.Equal("通用爵制（重制设定）", regime.Name);
        Assert.Equal(PersonalPeeragePreset.GenericId, upgraded.Forces[0].PersonalPeerageRegimeId);
        Assert.Equal(new[] { 5, 1 }, regime.Periods[0].AuthorizedRanks[PersonalPeerageGrade.VillageMarquis].ToArray());
        Assert.Equal(new[] { 1 }, regime.Periods[0].AuthorizedRanks[PersonalPeerageGrade.King].ToArray());
        Assert.Empty(regime.Periods[0].RoyalBloodlines);
        Assert.Same(upgraded, PersonalPeeragePreset.UpgradeLegacy(upgraded));
    }

    [Fact]
    public void RegimeAndForceAssignmentSurviveAuthoredDefinitionsAndJson()
    {
        var period = new PersonalPeeragePeriod(180, 220,
            ImmutableDictionary<PersonalPeerageGrade, ImmutableArray<int>>.Empty.Add(PersonalPeerageGrade.VillageMarquis, [3]), [9])
        { GradeRules = PersonalPeeragePolicy.Default.Grades.Values.OrderBy(r => r.Grade).ToImmutableArray(), RevocationPenalty = 8 };
        var scenario = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "fixture", 190, 1, 1, null, [], [], [], [])
        { PersonalPeerageRegimes = [new("han", [period])] };
        var definitions = ScenarioDefinitions.From(scenario);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(definitions, PackageFiles.JsonOptions);
        var restored = JsonSerializer.Deserialize<ScenarioDefinitions>(bytes, PackageFiles.JsonOptions)!;
        var definition = Assert.Single(restored.PersonalPeerageRegimes!);
        Assert.Equal("han", definition.Id);
        Assert.Equal(new[] { 3 }, definition.Periods[0].AuthorizedRanks[PersonalPeerageGrade.VillageMarquis].ToArray());
        Assert.Equal(new[] { 9 }, definition.Periods[0].RoyalBloodlines.ToArray());
        Assert.Equal(6, definition.Periods[0].GradeRules!.Value.Length);
        Assert.Equal(8, definition.Periods[0].RevocationPenalty);
        var force = new ForceInitialState(1, "f", 0, 100) { PersonalPeerageRegimeId = "han" };
        var roundtrip = JsonSerializer.Deserialize<ForceInitialState>(JsonSerializer.Serialize(force, PackageFiles.JsonOptions), PackageFiles.JsonOptions)!;
        Assert.Equal("han", roundtrip.PersonalPeerageRegimeId);
    }
}

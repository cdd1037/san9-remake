using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Importer.San9PkTc101.Scenarios;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class CuratedFormationEvidenceTests
{
    [Theory]
    [InlineData(324, "曹仁", "子孝", 28)]
    [InlineData(113, "甘宁", "兴霸", 29)]
    public void CombatExperienceIsExplicitlyAnInferenceAndPreservesAuthoredData(int id, string name, string courtesy, int formation)
    {
        var officer = new OfficerInitialState("name", new OfficerState(id, null, null, new(50, 50, 50, 50), false))
        { CourtesyNameKey = "courtesy" };
        var scenario = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "fixture", 210, 1, 1, null, [], [], [], [])
        {
            Officers = [officer], Military = new(1, 1, [0], [new(0, 1, null)],
                [new(formation, 1, 0, 10000, 5, new(1, 1), TravelMode.Land)], [])
        };
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, new() { ["name"] = name, ["courtesy"] = courtesy });
        var evidence = Assert.Single(CuratedFormationEvidence.Apply(scenario, texts).Officers[0].InitialFormationEvidence!);
        Assert.Equal(FormationEvidenceKind.ExperienceInference, evidence.Kind);
        Assert.Equal(formation, evidence.FormationId);
        Assert.Contains("史书并未记载", evidence.Explanation);
        Assert.Null(CuratedFormationEvidence.Apply(scenario with { Year = 209 }, texts).Officers[0].InitialFormationEvidence);
        var custom = officer with { InitialFormationEvidence = [evidence with { Proficiency = 42 }] };
        Assert.Equal(42, Assert.Single(CuratedFormationEvidence.Apply(scenario with { Officers = [custom] }, texts)
            .Officers[0].InitialFormationEvidence!).Proficiency);
        texts.Entries["courtesy"] = "他字";
        Assert.Null(CuratedFormationEvidence.Apply(scenario, texts).Officers[0].InitialFormationEvidence);
    }

    [Fact]
    public void ZhangLiaoInferenceRequiresPostBattleDateAndMatchingIdentity()
    {
        var officer = new OfficerInitialState("name", new OfficerState(422, null, null, new(50, 50, 50, 50), false))
        { CourtesyNameKey = "courtesy" };
        var scenario = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "fixture", 216, 1, 1, null, [], [], [], [])
        {
            Officers = [officer], Military = new(1, 1, [0], [new(0, 1, null)],
                [new(30, 1, 0, 10000, 5, new(1, 1), TravelMode.Land)], [])
        };
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, new() { ["name"] = "张辽", ["courtesy"] = "文远" });
        var evidence = Assert.Single(CuratedFormationEvidence.Apply(scenario, texts).Officers[0].InitialFormationEvidence!);
        Assert.Equal(FormationEvidenceKind.ExperienceInference, evidence.Kind);
        Assert.Equal(30, evidence.FormationId);
        Assert.Contains("并未记载", evidence.Explanation);
        Assert.Null(CuratedFormationEvidence.Apply(scenario with { Year = 215 }, texts).Officers[0].InitialFormationEvidence);
        texts.Entries["name"] = "他人";
        Assert.Null(CuratedFormationEvidence.Apply(scenario, texts).Officers[0].InitialFormationEvidence);
    }

    [Fact]
    public void RecordsRequireVerifiedIdentityDateAndDoNotOverwriteAuthoredValues()
    {
        var officer = new OfficerInitialState("name", new OfficerState(278, null, null, new(50, 50, 50, 50), false))
        { CourtesyNameKey = "courtesy" };
        var scenario = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "fixture", 214, 1, 1, null, [], [], [], [])
        {
            Officers = [officer], Military = new(1, 1, [0], [new(0, 1, null)],
                [new(28, 1, 0, 10000, 5, new(1, 1), TravelMode.Land)], [])
        };
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, new() { ["name"] = "诸葛亮", ["courtesy"] = "孔明" });
        var evidence = Assert.Single(CuratedFormationEvidence.Apply(scenario, texts).Officers[0].InitialFormationEvidence!);
        Assert.Equal(FormationEvidenceKind.ExperienceInference, evidence.Kind);
        Assert.Equal(28, evidence.FormationId);
        Assert.Contains("不能认定", evidence.Explanation);
        Assert.Null(CuratedFormationEvidence.Apply(scenario with { Year = 213 }, texts).Officers[0].InitialFormationEvidence);
        texts.Entries["courtesy"] = "他字";
        Assert.Null(CuratedFormationEvidence.Apply(scenario, texts).Officers[0].InitialFormationEvidence);
        texts.Entries["courtesy"] = "孔明";
        var authored = officer with { State = officer.State with { FormationProficiencies = System.Collections.Immutable.ImmutableSortedDictionary<int, int>.Empty } };
        Assert.Null(CuratedFormationEvidence.Apply(scenario with { Officers = [authored] }, texts).Officers[0].InitialFormationEvidence);
    }
}

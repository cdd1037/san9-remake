using System.Collections.Immutable;
using System.Text.Json;
using San9.Content.Packages;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Content.Tests;

public sealed class OfficerFormationProficiencyTests
{
    [Fact]
    public void ScenarioCompositionPreservesPersonalTitleIndependentlyOfOffice()
    {
        var profile = OfficerDefaults.Create(1);
        var state = new OfficerState(1, 1, 1, new(50, 50, 50, 50), false)
        { OfficeId = 7, PersonalPeerage = new(PersonalPeerageGrade.VillageMarquis, "汉寿亭侯"),
            BattleMerit = ImmutableSortedDictionary<int, int>.Empty.Add(1, 4),
            ServiceMerit = ImmutableSortedDictionary<int, int>.Empty.Add(1, 5) };
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, new() { [profile.NameKey] = profile.Name });
        var evidence = new PersonalPeerageEvidence(state.PersonalPeerage!, new(189, 1, 1), new(218, 12, 30), PersonalPeerageEvidenceKind.Historical, "测试来源");
        var history = evidence with { From = new(220, 1, 1), Until = new(225, 12, 30), Source = "后期来源" };
        var placement = ScenarioOfficerState.From(new(profile.NameKey, state) { InitialPersonalPeerage = evidence, InitialPersonalPeerageHistory = [history] }, profile, texts, 50);
        var json = JsonSerializer.Serialize(placement, PackageFiles.JsonOptions);
        var compiled = JsonSerializer.Deserialize<ScenarioOfficerState>(json, PackageFiles.JsonOptions)!.Compile(profile);
        var restored = compiled.State;
        Assert.Equal(state.PersonalPeerage, restored.PersonalPeerage);
        Assert.Equal(7, restored.OfficeId);
        Assert.Equal(4, restored.BattleMeritFor(1));
        Assert.Equal(5, restored.ServiceMeritFor(1));
        Assert.Equal(evidence, compiled.InitialPersonalPeerage);
        Assert.Equal(history, Assert.Single(compiled.InitialPersonalPeerageHistory!));
    }

    [Fact]
    public void FormationKnowledgeSurvivesProfileOverridesAndJson()
    {
        var legacy = OfficerDefaults.Create(1);
        var authored = legacy with { FormationProficiencies = ImmutableSortedDictionary<int, int>.Empty.Add(7, 0).Add(28, 750) };
        var overrides = OfficerDefaultsOverrides.Between(legacy, authored);
        var json = JsonSerializer.Serialize(overrides, PackageFiles.JsonOptions);
        var restored = JsonSerializer.Deserialize<OfficerDefaultsOverrides>(json, PackageFiles.JsonOptions)!.Apply(legacy);
        Assert.NotNull(restored.FormationProficiencies);
        Assert.Equal(0, restored.FormationProficiencies[7]);
        Assert.Equal(750, restored.FormationProficiencies[28]);
        Assert.Null(legacy.FormationProficiencies);
    }

    [Fact]
    public void FormationEvidenceSurvivesScenarioCompositionAndIsNotMistakenForHistoricalFact()
    {
        var profile = OfficerDefaults.Create(1);
        var state = new OfficerState(1, 1, 1, new(50, 50, 50, 50), false)
        { FormationProficiencies = ImmutableSortedDictionary<int, int>.Empty.Add(7, 300) };
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, new() { [profile.NameKey] = profile.Name });
        var evidence = new FormationEvidence(7, 300, FormationEvidenceKind.ExperienceInference, "测试来源", "根据人物经历作游戏推定");
        var placement = ScenarioOfficerState.From(new(profile.NameKey, state) { InitialFormationEvidence = [evidence] }, profile, texts, 50);
        var json = JsonSerializer.Serialize(placement, PackageFiles.JsonOptions);
        var compiled = JsonSerializer.Deserialize<ScenarioOfficerState>(json, PackageFiles.JsonOptions)!.Compile(profile);
        Assert.Equal(evidence, Assert.Single(compiled.InitialFormationEvidence!));
        Assert.Equal(300, compiled.State.FormationProficiencies![7]);
        Assert.False(new FormationEvidence(7, 300, FormationEvidenceKind.Historical, "", "无来源").IsValid);
    }
}

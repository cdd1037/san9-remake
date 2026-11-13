using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Importer.San9PkTc101.Packages;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class LegacyHistoricalPeerageEvidenceTests
{
    [Fact]
    public void JiangWanTitleUsesOnlyConservativeYearsAndVerifiedIdentity()
    {
        var person = new OfficerInitialState("name", new OfficerState(253, 1, 1, new(50, 50, 50, 50), false)) { CourtesyNameKey = "courtesy" };
        var scenario = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "fixture", 240, 1, 1, null, [], [], [], []) { Officers = [person] };
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, new() { ["name"] = "蒋琬", ["courtesy"] = "公琰" });
        var evidence = LegacyHistoricalPeerageEvidence.Apply(scenario, texts, true).Officers[0].InitialPersonalPeerage!;
        Assert.Equal(new PersonalPeerage(PersonalPeerageGrade.VillageMarquis, "安阳亭侯"), evidence.Peerage);
        Assert.Equal(PersonalPeerageEvidenceKind.Historical, evidence.Kind);
        Assert.False(evidence.Applies(new(238, 12, 30)));
        Assert.True(evidence.Applies(new(239, 1, 1)));
        Assert.True(evidence.Applies(new(245, 12, 30)));
        Assert.False(evidence.Applies(new(246, 1, 1)));
        Assert.Null(LegacyHistoricalPeerageEvidence.Apply(scenario, texts, false).Officers[0].InitialPersonalPeerage);
        texts.Entries["courtesy"] = "他字";
        Assert.Null(LegacyHistoricalPeerageEvidence.Apply(scenario, texts, true).Officers[0].InitialPersonalPeerage);
    }

    [Fact]
    public void ZhangFeiTitleRequiresMatchingIdentityAndExcludesPromotionYear()
    {
        var person = new OfficerInitialState("name", new OfficerState(416, 1, 1, new(50, 50, 50, 50), false)) { CourtesyNameKey = "courtesy" };
        var scenario = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "fixture", 215, 1, 1, null, [], [], [], []) { Officers = [person] };
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, new() { ["name"] = "张飞", ["courtesy"] = "益德" });
        var evidence = LegacyHistoricalPeerageEvidence.Apply(scenario, texts, true).Officers[0].InitialPersonalPeerage!;
        Assert.Equal(new PersonalPeerage(PersonalPeerageGrade.VillageMarquis, "新亭侯"), evidence.Peerage);
        Assert.False(evidence.Applies(new(214, 12, 30)));
        Assert.True(evidence.Applies(new(215, 1, 1)));
        Assert.True(evidence.Applies(new(220, 12, 30)));
        Assert.False(evidence.Applies(new(221, 1, 1)));
        Assert.Null(LegacyHistoricalPeerageEvidence.Apply(scenario, texts, false).Officers[0].InitialPersonalPeerage);
        texts.Entries["courtesy"] = "他字";
        Assert.Null(LegacyHistoricalPeerageEvidence.Apply(scenario, texts, true).Officers[0].InitialPersonalPeerage);
    }

    [Theory]
    [InlineData(324, "曹仁", "子孝", "安平亭侯")]
    [InlineData(319, "曹洪", "子廉", "国明亭侯")]
    [InlineData(446, "程昱", "仲德", "安国亭侯")]
    public void DocumentedMarquisesRespectIdentityDatesAndExistingContent(int id, string name, string courtesy, string title)
    {
        var person = new OfficerInitialState("name", new OfficerState(id, 1, 1, new(50, 50, 50, 50), false)) { CourtesyNameKey = "courtesy" };
        var scenario = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "fixture", 215, 1, 1, null, [], [], [], []) { Officers = [person] };
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, new() { ["name"] = name, ["courtesy"] = courtesy });
        var evidence = LegacyHistoricalPeerageEvidence.Apply(scenario, texts, true).Officers[0].InitialPersonalPeerage!;
        Assert.Equal(new PersonalPeerage(PersonalPeerageGrade.VillageMarquis, title), evidence.Peerage);
        Assert.Equal(PersonalPeerageEvidenceKind.Historical, evidence.Kind);
        Assert.False(evidence.Applies(new(213, 12, 30)));
        Assert.True(evidence.Applies(new(214, 1, 1)));
        Assert.True(evidence.Applies(new(219, 12, 30)));
        Assert.False(evidence.Applies(new(220, 1, 1)));
        Assert.Null(LegacyHistoricalPeerageEvidence.Apply(scenario, texts, false).Officers[0].InitialPersonalPeerage);
        var custom = evidence with { Peerage = new(PersonalPeerageGrade.DistrictMarquis, "自定义乡侯") };
        var existing = scenario with { Officers = [person with { InitialPersonalPeerage = custom }] };
        Assert.Equal(custom, LegacyHistoricalPeerageEvidence.Apply(existing, texts, true).Officers[0].InitialPersonalPeerage);
        var history = scenario with { Officers = [person with { InitialPersonalPeerageHistory = [custom] }] };
        Assert.Equal(custom, Assert.Single(LegacyHistoricalPeerageEvidence.Apply(history, texts, true).Officers[0].InitialPersonalPeerageHistory!));
        texts.Entries["courtesy"] = "他字";
        Assert.Null(LegacyHistoricalPeerageEvidence.Apply(scenario, texts, true).Officers[0].InitialPersonalPeerage);
    }

    [Fact]
    public void XuHuangTitleUsesOnlyVerifiedIdentityAndConservativeYears()
    {
        var person = new OfficerInitialState("name", new OfficerState(279, 1, 1, new(50, 50, 50, 50), false)) { CourtesyNameKey = "courtesy" };
        var scenario = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "fixture", 208, 1, 1, null, [], [], [], []) { Officers = [person] };
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, new() { ["name"] = "徐晃", ["courtesy"] = "公明" });
        var record = LegacyHistoricalPeerageEvidence.Apply(scenario, texts, true).Officers[0].InitialPersonalPeerage!;
        Assert.Equal(new PersonalPeerage(PersonalPeerageGrade.VillageMarquis, "都亭侯"), record.Peerage);
        Assert.Equal(PersonalPeerageEvidenceKind.Historical, record.Kind);
        Assert.False(record.Applies(new(200, 12, 30)));
        Assert.True(record.Applies(new(201, 1, 1)));
        Assert.True(record.Applies(new(219, 12, 30)));
        Assert.False(record.Applies(new(220, 1, 1)));
        Assert.Null(LegacyHistoricalPeerageEvidence.Apply(scenario, texts, false).Officers[0].InitialPersonalPeerage);
        texts.Entries["courtesy"] = "他字";
        Assert.Null(LegacyHistoricalPeerageEvidence.Apply(scenario, texts, true).Officers[0].InitialPersonalPeerage);
    }

    [Fact]
    public void ZhangLiaoTitlesUseConservativePeriodsAndVerifiedIdentity()
    {
        var person = new OfficerInitialState("name", new OfficerState(422, 1, 1, new(50, 50, 50, 50), false)) { CourtesyNameKey = "courtesy" };
        var scenario = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "fixture", 211, 1, 1, null, [], [], [], []) { Officers = [person] };
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, new() { ["name"] = "张辽", ["courtesy"] = "文远" });
        var records = LegacyHistoricalPeerageEvidence.Apply(scenario, texts, true).Officers[0].InitialPersonalPeerageHistory!;
        Assert.Equal(PersonalPeerageGrade.ImperialMarquis, records.Single(e => e.Applies(new(200, 1, 1))).Peerage.Grade);
        Assert.Equal("都亭侯", records.Single(e => e.Applies(new(211, 1, 1))).Peerage.Title);
        Assert.Equal("都亭侯", records.Single(e => e.Applies(new(219, 1, 1))).Peerage.Title);
        Assert.DoesNotContain(records, e => e.Applies(new(220, 1, 1)));
        Assert.Equal(PersonalPeerageGrade.CountyMarquis, records.Single(e => e.Applies(new(221, 1, 1))).Peerage.Grade);
        Assert.DoesNotContain(records, e => e.Applies(new(222, 1, 1)));
        Assert.Null(LegacyHistoricalPeerageEvidence.Apply(scenario, texts, false).Officers[0].InitialPersonalPeerageHistory);
        texts.Entries["courtesy"] = "他字";
        Assert.Null(LegacyHistoricalPeerageEvidence.Apply(scenario, texts, true).Officers[0].InitialPersonalPeerageHistory);
    }

    [Fact]
    public void KnownLegacyIdentityGetsSourcedConservativeDatesOnlyInHistoricalScenarios()
    {
        var person = new OfficerInitialState("name", new OfficerState(95, 1, 1, new(50, 50, 50, 50), false)) { CourtesyNameKey = "courtesy" };
        var scenario = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "fixture", 211, 1, 1, null, [], [], [], []) { Officers = [person] };
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, new() { ["name"] = "关羽", ["courtesy"] = "云长" });
        var record = LegacyHistoricalPeerageEvidence.Apply(scenario, texts, true).Officers[0].InitialPersonalPeerage!;
        Assert.Equal("汉寿亭侯", record.Peerage.Title);
        Assert.Equal(PersonalPeerageEvidenceKind.Historical, record.Kind);
        Assert.Equal(201, record.From.Year);
        Assert.Equal(218, record.Until!.Value.Year);
        Assert.Contains("ctext.org", record.Source);
        Assert.Null(LegacyHistoricalPeerageEvidence.Apply(scenario, texts, false).Officers[0].InitialPersonalPeerage);
        var other = scenario with { Officers = [person with { State = person.State with { Id = 96 } }] };
        Assert.Null(LegacyHistoricalPeerageEvidence.Apply(other, texts, true).Officers[0].InitialPersonalPeerage);
        texts.Entries["courtesy"] = "他字";
        Assert.Null(LegacyHistoricalPeerageEvidence.Apply(scenario, texts, true).Officers[0].InitialPersonalPeerage);
    }

    [Fact]
    public void ZhaoYunReceivesOnlyTheFullySupportedYearsAndCorrectGrade()
    {
        var person = new OfficerInitialState("name", new OfficerState(376, 1, 1, new(50, 50, 50, 50), false)) { CourtesyNameKey = "courtesy" };
        var scenario = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "fixture", 225, 1, 1, null, [], [], [], []) { Officers = [person] };
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, new() { ["name"] = "赵云", ["courtesy"] = "子龙" });
        var evidence = LegacyHistoricalPeerageEvidence.Apply(scenario, texts, true).Officers[0].InitialPersonalPeerage!;
        Assert.Equal(new PersonalPeerage(PersonalPeerageGrade.VillageMarquis, "永昌亭侯"), evidence.Peerage);
        Assert.False(evidence.Applies(new(223, 12, 30)));
        Assert.True(evidence.Applies(new(224, 1, 1)));
        Assert.True(evidence.Applies(new(228, 12, 30)));
        Assert.False(evidence.Applies(new(229, 1, 1)));
        Assert.Null(LegacyHistoricalPeerageEvidence.Apply(scenario, texts, false).Officers[0].InitialPersonalPeerage);
    }

    [Fact]
    public void WeiYanTimelineKeepsPromotionYearUnassigned()
    {
        var person = new OfficerInitialState("name", new OfficerState(119, 1, 1, new(50, 50, 50, 50), false)) { CourtesyNameKey = "courtesy" };
        var scenario = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "fixture", 225, 1, 1, null, [], [], [], []) { Officers = [person] };
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, new() { ["name"] = "魏延", ["courtesy"] = "文长" });
        var records = LegacyHistoricalPeerageEvidence.Apply(scenario, texts, true).Officers[0].InitialPersonalPeerageHistory!;
        Assert.Equal(2, records.Length);
        Assert.Equal("都亭侯", records.Single(e => e.Applies(new(225, 1, 1))).Peerage.Title);
        Assert.DoesNotContain(records, e => e.Applies(new(230, 1, 1)));
        Assert.Equal("南郑侯", records.Single(e => e.Applies(new(232, 1, 1))).Peerage.Title);
    }

    [Fact]
    public void JiangWeiTimelineUsesDistinctGradesAndAvoidsThePromotionYear()
    {
        var person = new OfficerInitialState("name", new OfficerState(129, 1, 1, new(50, 50, 50, 50), false)) { CourtesyNameKey = "courtesy" };
        var scenario = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "fixture", 234, 1, 1, null, [], [], [], []) { Officers = [person] };
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, new() { ["name"] = "姜维", ["courtesy"] = "伯约" });
        var records = LegacyHistoricalPeerageEvidence.Apply(scenario, texts, true).Officers[0].InitialPersonalPeerageHistory!;
        Assert.Equal(PersonalPeerageGrade.VillageMarquis, records.Single(e => e.Applies(new(233, 1, 1))).Peerage.Grade);
        Assert.DoesNotContain(records, e => e.Applies(new(234, 1, 1)));
        Assert.Equal(PersonalPeerageGrade.CountyMarquis, records.Single(e => e.Applies(new(235, 1, 1))).Peerage.Grade);
    }

    [Fact]
    public void CaoCaoTimelineKeepsPersonalMarquisDukeAndKingSeparateByDate()
    {
        var person = new OfficerInitialState("name", new OfficerState(326, 1, 1, new(50, 50, 50, 50), false)) { CourtesyNameKey = "courtesy" };
        var scenario = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "fixture", 200, 1, 1, null, [], [], [], []) { Officers = [person] };
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, new() { ["name"] = "曹操", ["courtesy"] = "孟德" });
        var records = LegacyHistoricalPeerageEvidence.Apply(scenario, texts, true).Officers[0].InitialPersonalPeerageHistory!;
        Assert.Equal("武平侯", records.Single(e => e.Applies(new(200, 1, 1))).Peerage.Title);
        Assert.Equal(PersonalPeerageGrade.Duke, records.Single(e => e.Applies(new(214, 1, 1))).Peerage.Grade);
        Assert.Equal(PersonalPeerageGrade.King, records.Single(e => e.Applies(new(218, 1, 1))).Peerage.Grade);
        Assert.DoesNotContain(records, e => e.Applies(new(213, 1, 1)));
    }

    [Fact]
    public void LiYanTitleStopsBeforeTheYearOfRevocation()
    {
        var person = new OfficerInitialState("name", new OfficerState(591, 1, 1, new(50, 50, 50, 50), false)) { CourtesyNameKey = "courtesy" };
        var scenario = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "fixture", 225, 1, 1, null, [], [], [], []) { Officers = [person] };
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, new() { ["name"] = "李严", ["courtesy"] = "正方" });
        var evidence = LegacyHistoricalPeerageEvidence.Apply(scenario, texts, true).Officers[0].InitialPersonalPeerage!;
        Assert.Equal(new PersonalPeerage(PersonalPeerageGrade.DistrictMarquis, "都乡侯"), evidence.Peerage);
        Assert.True(evidence.Applies(new(230, 12, 30)));
        Assert.False(evidence.Applies(new(231, 1, 1)));
    }
}

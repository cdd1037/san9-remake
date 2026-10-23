using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Tests;

public sealed class EndingPresentationTests
{
    [Fact]
    public void EndingChronicleCanTargetTheWinnerWithoutChangingTheObservedForce()
    {
        var content = EndingSessionTests.Fixture();
        var entries = content.Officers.ToDictionary(o => o.NameKey, o => "Officer" + o.State.Id);
        foreach (var place in content.Settlements) entries.Add(place.NameKey, "City" + place.Id);
        foreach (var force in content.Forces) entries.Add(force.NameKey, "Force" + force.Id);
        var texts = ChronicleTestContent.AddTo(new(TextCatalog.CurrentSchemaVersion, entries));
        var session = GameSession.Start(content, 1);
        var before = session.Capture();
        Assert.Equal("Officer1", ChroniclePresentation.Read(session, texts, ChronicleTestContent.Definition()).RulerName);
        var other = ChroniclePresentation.Read(session, texts, ChronicleTestContent.Definition(), 2);
        Assert.Equal("Officer4", other.RulerName);
        Assert.Contains(other.Lines, line => line.BbCode.Contains("Officer4", StringComparison.Ordinal));
        San9.Tests.StateAssert.Equal(before, session.Capture());
    }

    [Fact]
    public void EndingCountryTitlePreviewAndSelectionStayInsideThePresentation()
    {
        var original = EndingSessionTests.Fixture();
        var titles = new[] { 10, 11, 12 }.Select(id => new San9.Content.Scenarios.CountryTitleDefinition(
            new(id, true, true, false, [], []), "title." + id, -1)).ToArray();
        var content = original with
        {
            Information = original.Information with { CountryTitles = titles },
            Forces = original.Forces.Select(f => f with { CountryTitleId = f.Id == 2 ? 12 : null }).ToArray()
        };
        var session = GameSession.Start(content, 1);
        var before = session.Capture();
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, titles.ToDictionary(t => t.NameKey, t => "国" + t.Id))
        { Messages = new() { ["country"] = new([new MessageValue(new("property", "force.country-name", Arguments: [new("binding", "force.0")]))]) } };
        var presentation = new DecisionPresentations([], [])
        { Shared = new() { ["ending-confirm-country-title"] = new("country", new() { ["force.0"] = "force" }) } };
        var draft = new EndingDraft(session, texts, presentation);
        Assert.Equal(new[] { 10, 11 }, draft.CountryTitleCandidates([]));
        Assert.Equal("国11", draft.PreviewCountryTitle(11, []).Message.BbCode);
        Assert.Throws<ArgumentOutOfRangeException>(() => draft.SelectCountryTitle(12, []));
        draft.SelectCountryTitle(10, []);
        Assert.Equal("国11", draft.PreviewCountryTitle(11, []).Message.BbCode);
        Assert.Equal("国10", draft.Message("confirm-country-title").Message.BbCode);
        San9.Tests.StateAssert.Equal(before, session.Capture());
    }

    [Fact]
    public void OfficeCatalogCombinesAbilityPagesAndExcludesEarlierAppointments()
    {
        var original = EndingSessionTests.Fixture();
        var content = original with
        {
            Officers = original.Officers.Select(o => o with { State = o.State with
            {
                BaseAbilities = o.State.BaseAbilities with { Leadership = o.State.Id == 7 ? 99 : 60, Politics = o.State.Id == 3 ? 95 : 50 },
                TacticProficiencies = [0, 0, 0, 0, 400, 0, 600, 0, 0]
            } }).ToArray()
        };
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion,
            content.Officers.ToDictionary(o => o.NameKey, o => "Officer" + o.State.Id)
                .Concat(content.Settlements.Select(s => new KeyValuePair<string, string>(s.NameKey, "City" + s.Id)))
                .ToDictionary(p => p.Key, p => p.Value));
        var draft = new EndingDraft(GameSession.Start(content, 1), texts, new([], []));
        var rows = EndingOfficeCatalog.Read(draft, texts);
        Assert.Equal(7, rows[0].Id);
        Assert.DoesNotContain(rows, r => r.Id == draft.Snapshot.RulerId);
        Assert.All(rows, row =>
        {
            Assert.Equal("400", row.Cells[3].Text);
            Assert.Equal("600", row.Cells[4].Text);
        });
        draft.Select(7);
        Assert.DoesNotContain(EndingOfficeCatalog.Read(draft, texts), r => r.Id == 7);
        draft.Select(2);
        draft.Select(8);
        var successor = Assert.Single(EndingOfficeCatalog.Read(draft, texts));
        Assert.Equal(3, successor.Id);
        Assert.Equal("95", successor.Cells[1].Text);
    }

    [Theory]
    [InlineData(60, "unstable-succession")]
    [InlineData(80, "successor-conflict")]
    public void FailedSuccessionUsesOneExplanationBeforeTheFinalResult(int politics, string explanation)
    {
        var original = EndingSessionTests.Fixture();
        var content = original with
        {
            Officers = original.Officers.Select(o => o with { State = o.State with
            {
                BaseAbilities = o.State.BaseAbilities with { Politics = o.State.Id == 2 ? politics : 60 },
                BloodlineId = null
            } }).ToArray()
        };
        var session = GameSession.Start(content, 1);
        var draft = Draft(session);
        foreach (int person in new[] { 2, 3, 7, 8 }) draft.Select(person);
        Assert.Equal(SessionDisposition.Accepted, session.Apply(session.Request(draft.CreateOperation())).Disposition);
        Assert.False(session.Ending!.StableSuccession);
        Assert.Equal(new[] { "succession-year", explanation, "result-fallen-state" },
            draft.Results().TakeLast(3).Select(m => m.Message.BbCode));
        Assert.Single(draft.Results(), m => m.Message.BbCode is "unstable-succession" or "successor-conflict");
    }

    [Fact]
    public void FourthStageStillPresentsStableSuccessionWithoutChangingItsOutcome()
    {
        var original = EndingSessionTests.Fixture();
        var content = original with
        {
            Ending = original.Ending! with { DomesticGatewayCityIds = [1] },
            Forces = original.Forces.Select(f => f with { Trust = 1000, BaseTrust = 1000 }).ToArray(),
            Settlements = original.Settlements.Select(s => s with { Durability = 2000, MaxDurability = 2000 }).ToArray(),
            Officers = original.Officers.Select(o => o with { State = o.State with
            {
                TacticProficiencies = [0, 0, 0, 0, 0, 0, 600, 0, 0],
                Affinity = 0, BaseAbilities = o.State.BaseAbilities with { Politics = 0 }
            } }).ToArray()
        };
        var session = GameSession.Start(content, 1);
        var draft = Draft(session);
        draft.Policy = EndingPolicy.DomesticProsperity;
        foreach (int person in new[] { 2, 3, 7, 8 }) draft.Select(person);
        Assert.Equal(SessionDisposition.Accepted, session.Apply(session.Request(draft.CreateOperation())).Disposition);
        Assert.Equal(new(EndingOutcome.CityState, 4, null), session.Ending);
        Assert.Equal(new[] { "succession-year", "stable-succession", "result-city-state" },
            draft.Results().TakeLast(3).Select(m => m.Message.BbCode));
    }

    [Theory]
    [InlineData(EndingPolicy.ContinentalExpansion, "expansion-undersized")]
    [InlineData(EndingPolicy.DomesticProsperity, "domestic-undersized")]
    public void SmallCourtChoosesPolicyAndSkipsOnlyAppointmentsAndSuccession(EndingPolicy policy, string chapter)
    {
        var session = GameSession.Start(EndingSessionTests.Fixture() with { Officers = G01ScenarioTests.Fixture().Officers }, 1);
        var draft = Draft(session);
        draft.Policy = policy;
        Assert.False(draft.NeedsOfficeholders);
        Assert.Equal("prepare-undersized", draft.PreparationMessage().Message.BbCode);
        Assert.Equal(SessionDisposition.Accepted, session.Apply(session.Request(draft.CreateOperation())).Disposition);
        var messages = draft.Results().Select(m => m.Message.BbCode).ToArray();
        Assert.Contains(chapter, messages);
        Assert.DoesNotContain("succession-year", messages);
        Assert.Equal("result-fallen-state", messages[^1]);
    }

    private static EndingDraft Draft(GameSession session)
    {
        string[] roles = ["prepare-undersized", "introduction", "expansion-start", "expansion-undersized",
            "expansion-soldier-shortage", "domestic-start", "domestic-undersized", "domestic-building-ready",
            "domestic-government-start", "domestic-government-ready", "domestic-defense-start", "domestic-defense-ready",
            "domestic-culture-start", "domestic-order-shortage", "succession-year", "stable-succession",
            "unstable-succession", "successor-conflict", "result-fallen-state", "result-city-state"];
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, [])
        { Messages = roles.ToDictionary(r => r, r => new MessageDefinition([new MessageLiteral(r)])) };
        return new(session, texts, new([], [])
        { Shared = roles.ToDictionary(r => "ending-" + r, r => new DecisionMessageDefinition(r, [])) });
    }
}

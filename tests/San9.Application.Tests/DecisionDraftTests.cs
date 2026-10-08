using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class DecisionDraftTests
{
    [Theory]
    [MemberData(nameof(CommunitySearchSessionTests.Choices), MemberType = typeof(CommunitySearchSessionTests))]
    public void CommunityOptionsSubmitTheirActualChoice(string kind, CommunitySearchChoice choice)
    {
        var session = SearchSession(CommunitySearchSessionTests.Definition(kind), 20);
        var prompt = session.GetCommunitySearchPrompt()!;
        var draft = SearchDraft(session, kind, prompt.Options.Select(o => o.Choice.ToString()));
        var answer = draft.Options.Single(o => o.Role == choice.ToString())
            .CreateCommand(Guid.NewGuid(), session.Snapshot.Simulation.NextSequence, null, 0);
        var result = session.Execute(new(draft.ControllerId), answer);
        Assert.True(result.Submission.Accepted);
        var resolved = Assert.Single(result.Events.OfType<CommunitySearchResolvedEvent>());
        Assert.Equal(choice, resolved.Choice);
        if (kind == "volunteers" && choice == CommunitySearchChoice.Accept)
            Assert.Equal(prompt.OfferedSoldiers, resolved.Soldiers);
    }

    [Theory]
    [MemberData(nameof(SearchConflictSessionTests.Choices), MemberType = typeof(SearchConflictSessionTests))]
    public void ConflictOptionsSubmitTheirActualChoice(string kind, SearchConflictChoice choice)
    {
        var session = SearchSession(SearchConflictSessionTests.Definition(kind), 20);
        var prompt = session.GetSearchConflictPrompt()!;
        var draft = SearchDraft(session, kind, prompt.Options.Select(o => o.Choice.ToString()));
        var answer = draft.Options.Single(o => o.Role == choice.ToString())
            .CreateCommand(Guid.NewGuid(), session.Snapshot.Simulation.NextSequence, null, 0);
        var result = session.Execute(new(draft.ControllerId), answer);
        Assert.True(result.Submission.Accepted);
        Assert.Equal(choice, Assert.Single(result.Events.OfType<SearchConflictResolvedEvent>()).Choice);
    }

    [Theory]
    [InlineData("invest", MerchantInvestmentChoice.Invest)]
    [InlineData("decline", MerchantInvestmentChoice.Decline)]
    [InlineData("execute", MerchantInvestmentChoice.Execute)]
    public void MerchantOptionsSubmitTheirActualChoice(string role, MerchantInvestmentChoice choice)
    {
        ulong seed = Enumerable.Range(1, 100).Select(i => (ulong)i).First(value =>
            new StableRandom(StableRandom.DeriveSeed(value, "event-search-eligibility")).NextUInt64() % 100 < 80);
        var session = SearchSession(MerchantInvestmentSessionTests.Definition(), seed);
        var draft = SearchDraft(session, "merchant-investment", ["invest", "decline", "execute"]);
        var option = draft.Options.Single(o => o.Role == role);
        var answer = option.CreateCommand(Guid.NewGuid(), session.Snapshot.Simulation.NextSequence,
            null, choice == MerchantInvestmentChoice.Invest ? option.Minimum : 0);
        var result = session.Execute(new(draft.ControllerId), answer);
        Assert.True(result.Submission.Accepted);
        Assert.Equal(choice, Assert.Single(result.Events.OfType<MerchantInvestmentResolvedEvent>()).Choice);
    }

    private static GameSession SearchSession(San9.Content.Scenarios.ScenarioInitialState definition, ulong seed)
    {
        var session = GameSession.Start(definition, seed);
        Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, 0, 2, 1)).Submission.Accepted);
        session.AdvanceXun();
        return session;
    }

    private static DecisionDraft SearchDraft(GameSession session, string role, IEnumerable<string> choices)
    {
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, [])
        { Messages = new() { ["prompt"] = new([new MessageLiteral("选择")]) } };
        var presentation = CommandPresentation.Empty with
        {
            Decisions = new(new()
            {
                [role] = new(DecisionWindowKind.VerticalChoice,
                new("prompt", []), [], [], choices.ToDictionary(c => c, c => c))
            }, new() { ["citizen"] = 1 })
            {
                Subjects = new()
                {
                    ["monument.1"] = "纪念地",
                    ["merchant.salt"] = "盐田",
                    ["civic-person.0"] = "商人",
                    ["civic-person.1"] = "官吏"
                }
            }
        };
        return DecisionDraft.Read(session, texts, presentation)!;
    }

    [Fact]
    public void ShanyangProtectionAnnouncesOnlyAfterTheSecondCountryAnswerCommits()
    {
        var session = GameSession.Start(ShanyangGongSessionTests.Definition(), 17);
        session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, 0, 12, 1)); session.AdvanceXun();
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, new() { ["title.a"] = "汉" })
        {
            Messages = new()
            {
                ["prompt"] = new([new MessageLiteral("选择")]),
                ["protected"] = new([new MessageValue(new("property", "force.country-name", Arguments: [new("binding", "force.0")]))])
            }
        };
        var protectedMessage = new DecisionMessageDefinition("protected", new() { ["force.0"] = "force" });
        var presentation = CommandPresentation.Empty with
        {
            Decisions = new(new()
            {
                ["shanyang-gong"] = new(DecisionWindowKind.VerticalChoice, new("prompt", []), [], new() { ["accept"] = [protectedMessage] }, new() { ["accept"] = "保护", ["decline"] = "处斩" }),
                ["shanyang-title"] = new(DecisionWindowKind.CountrySelection, new("prompt", []), [], new() { ["choose"] = [protectedMessage] }, new() { ["choose"] = "国号" })
            }, [])
        };
        var draft = DecisionDraft.Read(session, texts, presentation)!;
        var response = draft.Options.Single(o => o.Role == "accept").CreateCommand(Guid.NewGuid(), session.Snapshot.Simulation.NextSequence, null, 0);
        var first = session.Execute(new(draft.ControllerId), response);
        Assert.True(first.Submission.Accepted);
        Assert.Empty(draft.Results("accept", first.Events, null, 0));
        draft = DecisionDraft.Read(session, texts, presentation)!;
        Assert.Equal("shanyang-title", draft.Role);
        response = draft.Options.Single().CreateCommand(Guid.NewGuid(), session.Snapshot.Simulation.NextSequence, 101, 0);
        var second = session.Execute(new(draft.ControllerId), response);
        Assert.True(second.Submission.Accepted);
        Assert.Equal("汉", Assert.Single(draft.Results("choose", second.Events, 101, 0)).Message.BbCode);
    }
    [Fact]
    public void CountryPreviewAndNavigationDoNotPublishUntilAnExplicitAnswer()
    {
        var session = GameSession.Start(PoliticalEventSessionTests.Scenario(true), 17);
        session.AdvanceXun(); session.AdvanceXun(); session.AdvanceXun();
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, new() { ["title.a"] = "汉" })
        {
            Messages = new()
            {
                ["prompt"] = new([new MessageLiteral("选择")]),
                ["confirm"] = new([new MessageValue(new("property", "force.country-name", Arguments: [new("binding", "force.0")]))])
            }
        };
        var definition = new DecisionPresentationDefinition(DecisionWindowKind.CountrySelection, new("prompt", []), [], [], new() { ["choose"] = "国号" })
        {
            SelectionConfirmation = new("confirm", new() { ["force.0"] = "force" })
        };
        var presentation = CommandPresentation.Empty with { Decisions = new(new() { ["country-title"] = definition }, []) };
        string hash = session.Snapshot.StateHash;
        var draft = DecisionDraft.Read(session, texts, presentation)!;
        Assert.Equal("player", draft.ControllerId);
        Assert.Equal(new[] { 500 }, draft.Options.Single().Candidates);
        Assert.Equal("汉", draft.ConfirmSelection(500)!.BbCode);
        Assert.Equal(hash, session.Snapshot.StateHash);
        var command = draft.Options.Single().CreateCommand(Guid.NewGuid(), session.Snapshot.Simulation.NextSequence, 500, 0);
        Assert.True(session.Execute(new(draft.ControllerId), command).Submission.Accepted);
        Assert.Equal(500, session.Snapshot.Forces.Single(f => f.Id == 1).CountryTitleId);
        Assert.Null(DecisionDraft.Read(session, texts, presentation));
    }

    [Theory]
    [InlineData("accept", false)]
    [InlineData("decline", true)]
    public void InsultChoiceMapsIgnoringAndExecutionToTheActualDomainMeaning(string choice, bool execution)
    {
        var session = GameSession.Start(ScholarInsultSessionTests.Definition(), 1);
        session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, 0, 2, 1)); session.AdvanceXun();
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, []) { Messages = new() { ["prompt"] = new([new MessageLiteral("选择")]) } };
        var presentation = CommandPresentation.Empty with
        {
            Decisions = new(new()
            {
                ["scholar-insult"] = new(DecisionWindowKind.VerticalChoice,
            new("prompt", []), [], [], new() { ["accept"] = "无视", ["decline"] = "处斩" })
            }, [])
        };
        var draft = DecisionDraft.Read(session, texts, presentation)!;
        var answer = draft.Options.Single(o => o.Role == choice).CreateCommand(Guid.NewGuid(), session.Snapshot.Simulation.NextSequence, null, 0);
        var result = session.Execute(new(draft.ControllerId), answer);
        Assert.True(result.Submission.Accepted);
        Assert.Equal(execution, Assert.Single(result.Events.OfType<ScholarInsultResolvedEvent>()).AttemptedExecution);
    }

    [Fact]
    public void EndingBacktrackingReleasesOnlyTheLastCandidateAndSubmissionDoesNotAdvanceTime()
    {
        var session = GameSession.Start(EndingSessionTests.Fixture(), 1);
        var draft = new EndingDraft(session, new(TextCatalog.CurrentSchemaVersion, []), DecisionPresentations.Empty);
        string hash = session.Snapshot.StateHash;
        Assert.DoesNotContain(draft.Snapshot.RulerId, draft.Candidates);
        draft.Select(2); draft.Select(3); draft.Select(7); draft.Back();
        Assert.Contains(7, draft.Candidates); Assert.DoesNotContain(2, draft.Candidates);
        Assert.Throws<ArgumentOutOfRangeException>(() => draft.Select(2));
        draft.Select(7); draft.Select(8);
        Assert.Equal(hash, session.Snapshot.StateHash);
        var operation = draft.CreateOperation();
        Assert.Equal(SessionDisposition.Accepted, session.Apply(session.Request(operation)).Disposition);
        Assert.Equal(hash, session.Snapshot.StateHash);
        Assert.Equal(EndingOutcome.SouthernDynasty, session.Ending!.Outcome);
    }
}

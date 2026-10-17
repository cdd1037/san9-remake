using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class SearchRecruitmentPresentationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PendingDiscoveryProvidesDecisionPersonWithoutRevealingCatalogEntry(bool accept)
    {
        var basis = CommunitySearchSessionTests.Definition("volunteers");
        var definition = basis with
        {
            SearchEvents = [],
            Officers = [.. basis.Officers.Select(p => p.State.Id == 2
                ? p with { State = p.State with { BaseAbilities = new(50, 50, 500, 50) } } : p),
                new("hidden", new(3, null, 1, new(80, 70, 60, 90), false)
                { Status = OfficerStatus.Undiscovered, Injury = 2 })]
        };
        var session = GameSession.Start(definition, 190);
        Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, 0, [2], 1)).Submission.Accepted);
        session.AdvanceXun();
        Assert.Equal(3, session.GetSearchRecruitmentPrompt()!.DiscoveredOfficerId);
        var before = session.Snapshot;
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, [])
        { Messages = new() { ["prompt"] = new([new MessageLiteral("选择")]) } };
        var presentation = CommandPresentation.Empty with
        {
            Decisions = new(new()
            {
                ["search-recruitment"] = new(DecisionWindowKind.PersonDisposition, new("prompt", []), [], [],
                    new() { ["accept"] = "登用", ["decline"] = "静观" })
            }, [])
        };

        var draft = DecisionDraft.Read(session, texts, presentation)!;
        var person = Assert.IsType<DecisionPerson>(draft.SubjectPerson);
        Assert.Equal(3, person.Id);
        Assert.Equal("hidden", person.NameKey);
        Assert.Equal(1, session.Snapshot.Officers.Single(o => o.Id == person.Id).Injury);
        Assert.Equal(new OfficerAbilities(72, 63, 54, 81), person.Abilities);
        Assert.DoesNotContain(draft.Before.Officers, o => o.Id == person.Id);
        Assert.Equal(OfficerStatus.Undiscovered, session.Snapshot.Officers.Single(o => o.Id == person.Id).Status);
        Assert.Same(before, session.Snapshot);

        var answer = draft.Options.Single(o => o.Role == (accept ? "accept" : "decline"))
            .CreateCommand(Guid.NewGuid(), session.Snapshot.Simulation.NextSequence, null, 0);
        var result = session.Execute(new(draft.ControllerId), answer);
        Assert.True(result.Submission.Accepted);
        Assert.Single(result.Events.OfType<SearchResolvedEvent>());
        Assert.Null(session.GetSearchRecruitmentPrompt());
        Assert.NotEqual(SimulationBoundary.AwaitingDecision, session.Snapshot.Simulation.Boundary);
        Assert.Contains(session.View.Officers, o => o.Id == person.Id);
    }
}

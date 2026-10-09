using System.Collections.Immutable;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class FacilityDemolitionPresentationTests
{
    private static ScenarioInitialState Definition()
    {
        var basis = G01ScenarioTests.Fixture();
        return basis with
        {
            Settlements = [.. basis.Settlements, basis.Settlements[0] with
                { Id = 3, X = 3, NameKey = "city.3", Kind = SettlementKind.Facility, IsFacilityListEntry = true }],
            Officers = basis.Officers.Select(p => p.State.Id == 2 ? p with
                { State = p.State with { SettlementId = 3, IsCommander = true } } : p).ToArray(),
            RegionCityIds = new() { [1] = 1, [2] = 2, [3] = 1 },
            RegionNameKeys = new() { [1] = "city.1", [2] = "city.2", [3] = "city.3" },
            Military = new(8, 8, Enumerable.Repeat(0, 64).ToImmutableArray(), [new(0, 1, null)],
                [new(7, 1, 50, 10000, 5, new(1, 1), TravelMode.Land)],
                [new(1, new(7, 7), new(1, 1)), new(2, new(12, 12), new(1, 1)),
                    new(3, new(17, 7), new(1, 1)) { CanDemolish = true }])
        };
    }

    [Fact]
    public void ConfirmationDoesNotMutateAndSuccessfulResultRetainsDestroyedFacilityName()
    {
        var session = GameSession.Start(Definition(), 42);
        var draft = new FacilityDemolitionDraft(session, 3);
        var definition = new DemolitionPresentationDefinition("confirm", "dialogue", "result", "removed");
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, new()
            { ["city.1"] = "洛阳", ["city.3"] = "河南阵", ["officer.2"] = "主官" })
        {
            Messages = new()
            {
                ["confirm"] = new([new MessageLiteral("撤除"), new MessageValue(new("property", "settlement.name-with-city",
                    Arguments: [new("binding", "building.0")]))]),
                ["dialogue"] = new([new MessageValue(new("property", "person.name", Arguments: [new("binding", "person.0")]))]),
                ["result"] = new([new MessageValue(new("property", "settlement.name-with-city", Arguments: [new("binding", "building.0")]))])
            }
        };
        var context = session.Messages(texts);
        string beforeHash = session.Snapshot.StateHash;
        Assert.Equal("撤除河南阵〈洛阳〉", draft.Confirmation(context, definition).BbCode);
        Assert.True(draft.CanExecute);
        Assert.Equal(beforeHash, session.Snapshot.StateHash);
        var result = session.Execute(new("player"), draft.CreateCommand(Guid.NewGuid(), session.Snapshot.Simulation.NextSequence));
        Assert.True(result.Submission.Accepted, result.Submission.Rejection.ToString());
        Assert.False(draft.CanExecute);
        Assert.Throws<InvalidOperationException>(() => draft.CreateCommand(Guid.NewGuid(), session.Snapshot.Simulation.NextSequence));
        string hash = session.Snapshot.StateHash;
        var messages = FacilityDemolitionPresentation.Create(draft.View, context, definition,
            Assert.Single(result.Events.OfType<FacilityDemolishedEvent>()), _ => 0);
        Assert.Equal(2, messages.Count);
        Assert.Equal(2, messages[0].SpeakerId);
        Assert.Null(messages[0].SoundRole);
        Assert.Equal("河南阵〈洛阳〉", messages[1].Message.BbCode);
        Assert.Equal("removed", messages[1].SoundRole);
        Assert.Null(messages[1].SpeakerId);
        Assert.True(session.Snapshot.Settlements.Single(s => s.Id == 3).IsDestroyed);
        Assert.Equal(hash, session.Snapshot.StateHash);
    }

    [Fact]
    public void UnrelatedWorldChangeInvalidatesPendingConfirmation()
    {
        var session = GameSession.Start(Definition(), 42);
        var draft = new FacilityDemolitionDraft(session, 3);
        Assert.True(session.Execute(new("player"), new WaitCommand(Guid.NewGuid(), 1, 0)).Submission.Accepted);
        Assert.False(draft.CanExecute);
        Assert.Throws<InvalidOperationException>(() => draft.CreateCommand(Guid.NewGuid(), session.Snapshot.Simulation.NextSequence));
        Assert.False(session.Snapshot.Settlements.Single(s => s.Id == 3).IsDestroyed);
    }
}

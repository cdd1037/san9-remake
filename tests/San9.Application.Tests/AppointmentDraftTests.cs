using San9.Tests;
using San9.Application.Persistence;
using San9.Application.Presentation;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class AppointmentDraftTests
{
    private static ScenarioInitialState Fixture()
    {
        var basis = G01ScenarioTests.Fixture();
        return basis with {
            RegionNameKeys = new() { [1] = "city.1", [2] = "city.2" },
            RegionCityIds = new() { [1] = 1, [2] = 2 }, CityConnections = [new(1, 2)],
            Officers = [.. basis.Officers.Select(p => p with { State = p.State.WithLoyalty(p.State.OwnerId!.Value, 80) }),
                new("captive", new OfficerState(7, 2, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Captive })],
            Information = basis.Information with {
                Offices = [new(401, "office.a", 20000, 8), new(902, "office.b", 18000, 4)],
                Treasures = [new(301, "treasure.a", 1, 0, 8, null), new(702, "treasure.b", 1, 0, 5, null)],
                TreasureStates = [new(301, 1, TreasureDiscovery.Discovered, null), new(702, 2, TreasureDiscovery.Discovered, null)]
            }
        };
    }

    [Theory]
    [InlineData("strategist")]
    [InlineData("rank")]
    [InlineData("reward")]
    [InlineData("bestow")]
    [InlineData("confiscate")]
    [InlineData("execute")]
    [InlineData("exile")]
    public void EachFlowUsesRecordedCommandsAndSurvivesSaveAndReplay(string kind)
    {
        var definition = Fixture(); var session = GameSession.Start(definition, 190);
        var draft = new AppointmentDraft(session, 1, kind);
        int singleTarget = kind == "strategist" ? draft.Candidates[0] : 2;
        var before = session.Snapshot;
        Assert.Equal(kind is "strategist" or "confiscate" or "execute", draft.CanExecute);
        if (kind == "rank") Assert.True(draft.AssignOffice(2, 401));
        else if (kind == "confiscate") Assert.True(draft.SetTreasure(702));
        else {
            Assert.True(draft.SetMembers(kind == "execute" ? [7] : kind is "reward" or "exile" ? [3, 2] : [singleTarget]));
            if (kind == "bestow") Assert.True(draft.SetTreasure(301));
        }
        StateAssert.Equal(before, session.Snapshot);
        Assert.True(draft.CanExecute);
        var result = draft.Submit();
        Assert.Equal(CommandRejection.None, result.Rejection);
        Assert.Equal(kind is "reward" or "exile" ? 2 : 1, result.CompletedCommands);
        Assert.False(draft.IsCurrent);
        Assert.Throws<InvalidOperationException>(() => draft.Submit());
        switch (kind) {
            case "strategist": Assert.Equal(singleTarget, session.Snapshot.Forces.Single(f => f.Id == 1).AdviserId); break;
            case "rank": Assert.Equal(401, session.Snapshot.Officers.Single(p => p.Id == 2).OfficeId); break;
            case "reward": Assert.Equal(draft.Money - 200, session.Snapshot.Corps.Single(c => c.Id == 1).Treasury.Money); break;
            case "bestow": Assert.Equal(2, session.Snapshot.Treasures.Single(t => t.Id == 301).HolderId); break;
            case "confiscate": Assert.Equal(1, session.Snapshot.Treasures.Single(t => t.Id == 702).HolderId); break;
            case "execute": Assert.Equal(OfficerStatus.Dead, session.Snapshot.Officers.Single(p => p.Id == 7).Status); break;
            case "exile": Assert.All(session.Snapshot.Officers.Where(p => p.Id is 2 or 3), p => Assert.Equal(OfficerStatus.Free, p.Status)); break;
        }
        StateAssert.Equal(session.Capture(), SaveService.Decode(SaveService.Encode(session), definition).Capture());
    }

    [Fact]
    public void OfficeReassignmentAndRemovalRemainLocalUntilOneAtomicSubmission()
    {
        var definition = Fixture();
        definition = definition with { Officers = definition.Officers.Select(p => p.State.Id is 2 or 3
            ? p with { State = p.State with { OfficeId = p.State.Id == 2 ? 401 : 902 } } : p).ToArray() };
        var session = GameSession.Start(definition, 190); var draft = new AppointmentDraft(session, 1, "rank");
        var plan = draft.OfficePlan!;
        var discarded = plan.Fork();
        discarded.RemoveAll();
        Assert.Equal(401, plan.OfficeFor(2));
        Assert.Equal(902, plan.OfficeFor(3));
        var edited = plan.Fork();
        Assert.True(edited.Assign(2, 902)); Assert.Null(edited.OfficeFor(3));
        Assert.True(edited.Assign(3, 401));
        Assert.Empty(draft.Changes);
        plan.Accept(edited);
        Assert.Equal(2, draft.Changes.Length);
        Assert.Equal(401, session.Snapshot.Officers.Single(p => p.Id == 2).OfficeId);
        Assert.Equal(CommandRejection.None, draft.Submit().Rejection);
        Assert.Equal(902, session.Snapshot.Officers.Single(p => p.Id == 2).OfficeId);
        var remove = new AppointmentDraft(session, 1, "rank");
        Assert.True(remove.AssignOffice(2, null)); Assert.Equal(CommandRejection.None, remove.Submit().Rejection);
        Assert.Null(session.Snapshot.Officers.Single(p => p.Id == 2).OfficeId);
    }

    [Fact]
    public void RewardRejectsForeignDuplicateOrStaleChoices()
    {
        var definition = Fixture(); var session = GameSession.Start(definition, 190);
        var draft = new AppointmentDraft(session, 1, "reward");
        Assert.False(draft.SetMembers([1])); Assert.False(draft.SetMembers([5])); Assert.False(draft.SetMembers([2, 2]));
        Assert.True(draft.SetMembers([2]));
        Assert.True(session.Execute(new("player"), new RewardOfficerCommand(Guid.NewGuid(), 1, 0, 3)).Submission.Accepted);
        var before = session.Snapshot;
        Assert.False(draft.SetMembers([2])); Assert.False(draft.CanExecute);
        Assert.Throws<InvalidOperationException>(() => draft.Submit()); StateAssert.Equal(before, session.Snapshot);
        Assert.False(AppointmentDraft.Supports("delegate"));
    }

    [Fact]
    public void RewardCapsSelectionAtAffordableCountAndTreasureChoicesFollowTheHolder()
    {
        var definition = Fixture();
        definition = definition with { Information = definition.Information with {
            Corps = definition.Information.Corps.Select(c => c.Id == 1 ? c with { Treasury = new(150, 2000) } : c).ToArray()
        } };
        var session = GameSession.Start(definition, 190);
        var reward = new AppointmentDraft(session, 1, "reward");
        Assert.Equal(1, reward.SelectionLimit); Assert.False(reward.SetMembers([2, 3]));
        Assert.True(reward.SetMembers([2])); Assert.Equal(100u, reward.Cost);
        var bestow = new AppointmentDraft(session, 1, "bestow");
        Assert.Equal(new[] { 301 }, bestow.TreasureIds); Assert.False(bestow.SetTreasure(702));
        Assert.True(bestow.SetTreasure(301)); Assert.False(bestow.CanExecute);
        Assert.True(bestow.SetMembers([3])); Assert.Equal(CommandRejection.None, bestow.Submit().Rejection);
        Assert.NotNull(AppointmentDraft.UnavailableReasonKey(session.View, 1, "bestow"));
        Assert.Contains(301, new AppointmentDraft(session, 1, "confiscate").TreasureIds);
    }

    [Fact]
    public void ExileCanReleasePrisonersAndReportsRejectionWithoutClaimingCompletion()
    {
        var definition = Fixture(); var session = GameSession.Start(definition, 190);
        var release = new AppointmentDraft(session, 1, "exile"); Assert.True(release.SetMembers([7]));
        Assert.Single(release.Submit().Events.OfType<PrisonerReleasedEvent>());
        var disconnected = GameSession.Start(definition with { CityConnections = [] }, 190);
        var exile = new AppointmentDraft(disconnected, 1, "exile"); Assert.True(exile.SetMembers([2, 3]));
        var before = disconnected.Snapshot; var result = exile.Submit();
        Assert.Equal(CommandRejection.NoDestination, result.Rejection); Assert.Equal(0, result.CompletedCommands);
        StateAssert.Equal(before, disconnected.Snapshot);
    }
}

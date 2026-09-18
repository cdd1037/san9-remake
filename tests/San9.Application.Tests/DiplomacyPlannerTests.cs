using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Application.Persistence;
using System.Collections.Immutable;

namespace San9.Application.Tests;

public sealed class DiplomacyPlannerTests
{
    [Fact]
    public void StrongNeighborCanBeAskedToSurrenderAndPublicExecutionMergesOwnership()
    {
        var basis = G01ScenarioTests.Fixture();
        var content = basis with
        {
            RegionNameKeys = new() { [1] = "home", [2] = "neighbor" },
            RegionCityIds = new() { [1] = 1, [2] = 2 },
            Forces = basis.Forces.Select(f => f.Id == 1 ? f with { AdviserId = 3, Trust = 900, BaseTrust = 900 } : f).ToArray(),
            Officers = basis.Officers.Select(o => o.State.Id == 3 ? o with { State = o.State with
                { Status = OfficerStatus.Strategist, BaseAbilities = new(50, 100, 50, 50) } } : o).ToArray(),
            Settlements = basis.Settlements.Select(s => s with { Soldiers = s.Id == 1 ? 200000u : 10000u }).ToArray(),
            Military = new(5, 5, Enumerable.Repeat(0, 25).ToImmutableArray(), [new(0, 1, null)], [],
                [new(1, MapCoordinates.Center(new(1, 1)), new(1, 1)), new(2, MapCoordinates.Center(new(2, 2)), new(1, 1))])
            {
                RegionIds = Enumerable.Range(0, 25).Select(i => (int?)(i % 5 < 2 ? 1 : 2)).ToImmutableArray(),
                TerritoryExits = Enumerable.Repeat(TerritoryExits.All, 25).ToImmutableArray()
            }
        };
        var session = GameSession.Start(content, 42);
        var seat = session.Snapshot.Simulation.Controls[0];
        var command = Assert.IsType<DemandSurrenderCommand>(DiplomacyPlanner.Choose(session, seat, Guid.NewGuid()));
        Assert.True(session.Execute(new(seat.ControllerId), command).Submission.Accepted);
        var result = session.AdvanceXun();
        Assert.True(Assert.Single(result.Events.OfType<SurrenderResolvedEvent>()).Accepted);
        Assert.True(session.Snapshot.Forces.Single(f => f.Id == 2).IsEliminated);
    }

    [Fact]
    public void AutomaticEnvoyPausesInMemoryAndReplaysFromStableSave()
    {
        var content = G01ScenarioTests.Fixture();
        content = content with { Forces = content.Forces.Select(f => f.Id == 2 ? f with { Money = 30000 } : f).ToArray(),
            Information = content.Information with { Relations = [new(1, 2, 60)] } };
        var session = GameSession.Start(content, 42);
        session.BeginRecording();
        var restored = SaveService.Decode(SaveService.Encode(session), content);
        restored.AdvanceXun();
        var result = session.AdvanceXun();
        var dispatched = Assert.Single(result.Events.OfType<TaskStartedEvent>());
        Assert.NotNull(dispatched.Task.Gift);
        Assert.Equal(2, dispatched.Task.ForceId);
        Assert.Equal(SimulationBoundary.AwaitingDecision, session.Snapshot.Simulation.Boundary);
        Assert.Equal(1, session.Snapshot.Simulation.PendingDailyTask!.RespondingForceId);
        Assert.Equal(SaveFailure.NotStable, Assert.Throws<SaveException>(() => SaveService.Encode(session)).Failure);
        var request = DecisionResponses.Decline(session);
        var answered = session.Apply(request);
        Assert.Equal(SessionDisposition.Accepted, answered.Disposition);
        Assert.Single(answered.Events.OfType<XunAdvancedEvent>());
        Assert.Equal(CanonicalJson.Text(answered.Events), CanonicalJson.Text(restored.Apply(request).Events));
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(restored));
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), content, GameSession.EngineFactory(content));
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(replay.Session));
    }

    [Fact]
    public void AttackRequestUsesPublicAdviceAndValidatesAnEnemyOfBothParties()
    {
        var basis = G01ScenarioTests.Fixture();
        var content = basis with
        {
            Forces = [basis.Forces[0] with { Money = 10000, AdviserId = 3 }, basis.Forces[1],
                new(3, "enemy", 3, 1000, 1000, 100) { LeaderId = 7 }],
            Settlements = [.. basis.Settlements, basis.Settlements[1] with { Id = 3, OwnerId = 3, NameKey = "third" }],
            Officers = [.. basis.Officers.Select(o => o.State.Id == 3 ? o with { State = o.State with
                { Status = OfficerStatus.Strategist, BaseAbilities = new(50, 100, 50, 50) } } : o),
                new("enemy-ruler", new(7, 3, 3, new(50, 50, 50, 50), false))],
            Information = basis.Information with { Relations = [new(1, 2, 60), new(1, 3, 0), new(2, 3, 0)] }
        };
        var session = GameSession.Start(content, 42);
        var seat = session.Snapshot.Simulation.Controls[0];
        string before = session.Snapshot.StateHash;
        var command = Assert.IsType<RequestAttackCommand>(DiplomacyPlanner.Choose(session, seat, Guid.NewGuid()));
        Assert.Equal(2, command.TargetForceId);
        Assert.Equal(3, command.TargetSettlementId);
        Assert.Equal(before, session.Snapshot.StateHash);
        Assert.True(session.Execute(new(seat.ControllerId), command).Submission.Accepted);
    }

    [Fact]
    public void CaptiveRecoveryIsReadOnlyValidatedAndDoesNotDispatchASecondEnvoy()
    {
        var content = G01ScenarioTests.Fixture();
        content = content with { Officers = content.Officers.Select(o => o.State.Id == 3
            ? o with { State = o.State with { Status = OfficerStatus.Captive, SettlementId = 2 } } : o).ToArray() };
        var session = GameSession.Start(content, 42);
        var seat = session.Snapshot.Simulation.Controls[0];
        var id = Guid.NewGuid();
        string before = session.Snapshot.StateHash;
        var command = Assert.IsType<ReturnPrisonerCommand>(DiplomacyPlanner.Choose(session, seat, id));
        Assert.Equal(command, DiplomacyPlanner.Choose(session, seat, id));
        Assert.Equal(before, session.Snapshot.StateHash);
        Assert.Equal(3, command.PrisonerId);
        Assert.True(session.Execute(new(seat.ControllerId), command).Submission.Accepted);
        Assert.IsType<WaitCommand>(DiplomacyPlanner.Choose(session, seat, Guid.NewGuid()));
    }

    [Fact]
    public void GiftBudgetPreservesReserveAndTargetsOnlyRelationsWorthImproving()
    {
        var content = G01ScenarioTests.Fixture();
        content = content with { Forces = content.Forces.Select(f => f.Id == 1 ? f with { Money = 30000 } : f).ToArray(),
            Information = content.Information with { Relations = [new(1, 2, 60)] } };
        var session = GameSession.Start(content, 42);
        var seat = session.Snapshot.Simulation.Controls[0];
        var gift = Assert.IsType<GiftCommand>(DiplomacyPlanner.Choose(session, seat, Guid.NewGuid()));
        Assert.Equal(2000u, gift.Offer.Amount);
        Assert.True(session.Execute(new(seat.ControllerId), gift).Submission.Accepted);
        content = content with { Forces = content.Forces.Select(f => f.Id == 1 ? f with { Money = 19999 } : f).ToArray() };
        session = GameSession.Start(content, 42);
        Assert.IsType<WaitCommand>(DiplomacyPlanner.Choose(session, seat, Guid.NewGuid()));
    }
}

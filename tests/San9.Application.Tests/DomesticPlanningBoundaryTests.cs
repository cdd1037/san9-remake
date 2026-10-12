using San9.Application.AI;
using San9.Application.Persistence;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class DomesticPlanningBoundaryTests
{
    [Fact]
    public void DelegatingPlayerRootRevokesPlayerObservationAndRunsUnderCorpsAuthority()
    {
        var initial = G01ScenarioTests.Fixture();
        initial = initial with { Information = initial.Information with { Corps = initial.Information.Corps
            .Select(c => c.OwnerId == 1 ? c with { ControlMode = CorpsControlMode.Automatic } : c).ToArray() } };
        var session = GameSession.Start(initial, 190);
        var world = session.Snapshot;
        var seat = world.Simulation.Controls[0];
        Assert.Empty(DomesticObservation.Capture(world, seat, new("player")).Settlements);
        Assert.Equal(1, Assert.Single(DomesticObservation.Capture(world, seat, new(CorpsControl.ControllerId(1))).Settlements).Id);
        var result = session.Apply(session.Request(new EndControlOperation()));
        Assert.Equal(SessionDisposition.Accepted, result.Disposition);
        Assert.Equal(1, Assert.Single(result.Events.OfType<DevelopmentCompletedEvent>()).Command.ForceId);
    }

    [Fact]
    public void ObservationUsesSharedAuthorityAndDoesNotReserveDelegatedPersonnel()
    {
        var initial = G01ScenarioTests.Fixture();
        initial = initial with
        {
            Settlements = [initial.Settlements[0] with { CorpsId = 1 }, initial.Settlements[1],
                initial.Settlements[0] with { Id = 3, CorpsId = 3 }],
            Officers = initial.Officers.Select(p => p.State.Id == 2 ? p with { State = p.State with { SettlementId = 3 } } : p).ToArray(),
            Information = initial.Information with { Corps = [.. initial.Information.Corps,
                new(3, 1, 2, new(500, 1000)) { ControlMode = CorpsControlMode.Automatic }] }
        };
        var session = GameSession.Start(initial, 190);
        var world = session.Snapshot;
        var seat = world.Simulation.Controls[0];
        var player = DomesticObservation.Capture(world, seat, new("player"));
        var delegated = DomesticObservation.Capture(world, seat, new(CorpsControl.ControllerId(3)));
        Assert.Equal(1, Assert.Single(player.Settlements).Id);
        Assert.DoesNotContain(2, player.Settlements[0].OfficerIds);
        Assert.Equal(3, Assert.Single(delegated.Settlements).Id);
        Assert.Equal(new[] { 2 }, delegated.Settlements[0].OfficerIds);
        Assert.Equal(world.Corps.Single(c => c.Id == 3).Treasury.Money, delegated.Settlements[0].Money);
        Assert.Empty(DomesticObservation.Capture(world, seat, new("forged")).Settlements);

        var command = Assert.IsType<DevelopCommand>(DomesticCommandSelection.Choose(world, session.Validate,
            seat, new(CorpsControl.ControllerId(3)), Guid.NewGuid(), 0));
        Assert.True(session.Validate(new(CorpsControl.ControllerId(3)), command).Accepted);
        Assert.Equal(CommandRejection.NotActiveController, session.Validate(new("player"), command).Rejection);
    }

    [Fact]
    public void ForeignStateAndLaterSourceChangesCannotAlterCapturedPlan()
    {
        var session = GameSession.Start(G01ScenarioTests.Fixture(), 190);
        var before = session.Snapshot;
        var seat = before.Simulation.Controls[0];
        var observed = DomesticObservation.Capture(before, seat, new("player"));
        var changed = before with
        {
            Settlements = before.Settlements.Select(s => s.OwnerId == 2 ? s with { Soldiers = 99999, Morale = 0 } : s).ToArray(),
            Corps = before.Corps.Select(c => c.OwnerId == 2 ? c with { Treasury = new(999999, 999999) } : c).ToArray(),
            Officers = before.Officers.Select(p => p.OwnerId == 2 ? p with { Acted = true } : p).ToArray()
        };
        Assert.Equal(CanonicalJson.Hash(observed), CanonicalJson.Hash(DomesticObservation.Capture(changed, seat, new("player"))));
        string expected = CanonicalJson.Hash(DomesticPlanner.Plan(observed, new(100, 0), 0));
        var command = session.ChooseDomesticCommand(seat, Guid.NewGuid(), 0);
        Assert.True(session.Execute(new("player"), command).Submission.Accepted);
        Assert.Equal(expected, CanonicalJson.Hash(DomesticPlanner.Plan(observed, new(100, 0), 0)));
        Assert.NotEqual(CanonicalJson.Hash(observed), CanonicalJson.Hash(DomesticObservation.Capture(session.Snapshot, seat, new("player"))));
    }

    [Fact]
    public void WorkLimitReturnsDeterministicPrefixAndDistinguishesExhaustion()
    {
        var initial = G01ScenarioTests.Fixture();
        var session = GameSession.Start(initial with { Settlements = initial.Settlements.Select(s => s with { Soldiers = 5000, Morale = 50 }).ToArray() }, 190);
        var observed = DomesticObservation.Capture(session.Snapshot, session.Snapshot.Simulation.Controls[0], new("player"));
        var small = DomesticPlanner.Plan(observed, new(3, 0), 0);
        var full = DomesticPlanner.Plan(observed, new(100, 0), 0);
        Assert.True(small.BudgetExhausted);
        Assert.Equal(3, small.Evaluations);
        Assert.Single(small.Candidates);
        Assert.False(full.BudgetExhausted);
        Assert.Equal(CanonicalJson.Hash(full.Candidates[0]), CanonicalJson.Hash(small.Candidates[0]));
        Assert.False(DomesticPlanner.Plan(observed with { Settlements = [] }, new(1, 0), 0).BudgetExhausted);
        Assert.Throws<ArgumentException>(() => DomesticPlanner.Plan(observed, new(0, 1), 0));
    }

    [Fact]
    public void UsedAndSaturatedActivitiesAreExcludedBeforeAllocatingPeople()
    {
        var session = GameSession.Start(G01ScenarioTests.Fixture(), 190);
        var world = session.Snapshot;
        world = world with { Settlements = world.Settlements.Select(s => s with
            { TrainUsed = true, Morale = 50, Cultivation = s.MaxCultivation, RepairUsed = true }).ToArray() };
        var observed = DomesticObservation.Capture(world, world.Simulation.Controls[0], new("player"));
        var plan = DomesticPlanner.Plan(observed, new(100, 0), 0);
        Assert.NotEmpty(plan.Candidates);
        Assert.DoesNotContain(plan.Candidates, c => c.Activity is DomesticActivity.Train or DomesticActivity.Cultivation or DomesticActivity.Repair);
    }

    [Theory]
    [InlineData(CorpsControlMode.Direct)]
    [InlineData(CorpsControlMode.Automatic)]
    public void ComputerRootRunsOnceAcrossAdvanceEntryPointsAndReplays(CorpsControlMode mode)
    {
        var initial = G01ScenarioTests.Fixture();
        initial = initial with { Information = initial.Information with { Corps = initial.Information.Corps
            .Select(c => c.OwnerId == 2 ? c with { ControlMode = mode } : c).ToArray() } };
        var whole = GameSession.Start(initial, 190);
        var split = GameSession.Start(initial, 190);
        whole.BeginRecording();
        var result = whole.Apply(whole.Request(new AdvanceOperation()));
        Assert.Equal(SessionDisposition.Accepted, result.Disposition);
        var development = Assert.Single(result.Events.OfType<DevelopmentCompletedEvent>());
        Assert.Equal(2, development.Command.ForceId);
        foreach (var seat in split.Snapshot.Simulation.Controls)
            Assert.Equal(SessionDisposition.Accepted, split.Apply(split.Request(new EndControlOperation(), seat.ControllerId)).Disposition);
        Assert.Equal(SessionDisposition.Accepted, split.Apply(split.Request(new AdvanceOperation())).Disposition);
        Assert.Equal(whole.Snapshot.StateHash, split.Snapshot.StateHash);
        var replay = RecordingService.Replay(RecordingService.Encode(whole.EndRecording()), initial, GameSession.EngineFactory(initial));
        Assert.Equal(whole.Capture().StateHash, replay.Session.Capture().StateHash);
        var restored = SaveService.Decode(SaveService.Encode(whole), initial);
        Assert.Equal(whole.AdvanceXun().Snapshot.StateHash, restored.AdvanceXun().Snapshot.StateHash);
    }
}

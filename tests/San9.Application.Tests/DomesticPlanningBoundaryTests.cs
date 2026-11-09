using San9.Tests;
using San9.Application.AI;
using San9.Application.Persistence;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class DomesticPlanningBoundaryTests
{
    private static DomesticObservation Observe(WorldSnapshot world, ControlSeat seat, CommandContext context) =>
        DomesticObservation.Capture(world, seat, context, new SimulationEngine(WorldState.Restore(world)).GetAbilities(world));

    [Fact]
    public void PlayerScriptUsesCurrentInjuryAdjustedAbilitiesAndLeavesCapturedInputsUnchanged()
    {
        var content = G01ScenarioTests.Fixture();
        content = content with
        {
            Settlements = content.Settlements.Select(s => s with { Soldiers = 5000, Morale = 50 }).ToArray(),
            Officers = content.Officers.Select(p => p.State.OwnerId == 1 ? p with { State = p.State with
                { BaseAbilities = new(80, 80, 80, 80), Injury = p.State.Id == 1 ? 7 : 0 } } : p).ToArray()
        };
        var session = GameSession.Start(content, 190);
        var engine = GameSession.EngineFactory(content)(WorldState.Restore(session.Snapshot));
        var world = engine.Snapshot;
        var seat = world.Simulation.Controls[0];
        var current = engine.GetAbilities(world);
        var observed = DomesticObservation.Capture(world, seat, new("player"), current);
        Assert.True(observed.Abilities[1].War < world.Officers.Single(p => p.Id == 1).BaseAbilities.War);
        Assert.All(world.Officers.Where(p => p.OwnerId == 2), p => Assert.DoesNotContain(p.Id, observed.Abilities.Keys));
        var expected = DomesticPlanner.Plan(observed, new(1000, 0), 0).Candidates.First(c => c.Activity == DomesticActivity.Train);
        var actual = Assert.IsType<DevelopCommand>(session.ChooseDomesticCommand(seat, Guid.NewGuid(), 0));
        Assert.Equal(DomesticActivity.Train, actual.Development.Activity);
        Assert.Equal(expected.OfficerIds.ToArray(), actual.Development.OfficerIds.ToArray());
        Assert.DoesNotContain(1, actual.Development.OfficerIds);
        StateAssert.Equal(world, engine.Snapshot);
        StateAssert.Equal(world, session.Snapshot);
    }

    [Fact]
    public void PlayerTrainingScriptPrefersStrongWarOfficersOverStrongLeaders()
    {
        var content = G01ScenarioTests.Fixture();
        content = content with
        {
            Settlements = content.Settlements.Select(s => s with { Soldiers = 5000, Morale = 50 }).ToArray(),
            Officers = content.Officers.Select(p => p.State.Id switch
            {
                1 => p with { State = p.State with { BaseAbilities = new(20, 20, 20, 100) } },
                2 => p with { State = p.State with { BaseAbilities = new(100, 20, 20, 20) } },
                3 => p with { State = p.State with { BaseAbilities = new(80, 20, 20, 30) } },
                _ => p
            }).ToArray()
        };
        var session = GameSession.Start(content, 190);
        var world = session.Snapshot;
        var seat = world.Simulation.Controls[0];
        var observation = Observe(world, seat, new("player"));
        var plan = DomesticPlanner.Plan(observation, new(1000, 0), 0);
        var pairs = plan.Candidates.Where(c => c.Activity == DomesticActivity.Train && c.OfficerIds.Length == 2).ToArray();
        Assert.NotEmpty(pairs);
        Assert.All(pairs, c => Assert.Equal(new[] { 2, 3 }, c.OfficerIds.ToArray()));
        var command = Assert.IsType<DevelopCommand>(session.ChooseDomesticCommand(seat, Guid.NewGuid(), 0));
        Assert.Equal(DomesticActivity.Train, command.Development.Activity);
        Assert.Equal(new[] { 2 }, command.Development.OfficerIds.ToArray());
        Assert.Same(world, session.Snapshot);
    }

    [Fact]
    public void HandingOverPlayerForceRevokesPlayerContextAndRunsOriginalWork()
    {
        var initial = G01ScenarioTests.Fixture();
        initial = initial with { Information = initial.Information with { Corps = initial.Information.Corps
            .ToArray() } };
        var session = GameSession.Start(initial, 190, playerForceController: ControllerKind.Automatic);
        var world = session.Snapshot;
        var seat = world.Simulation.Controls[0];
        Assert.Empty(Observe(world, seat, new("player")).Settlements);
        Assert.Equal(1, Assert.Single(Observe(world, seat, new(CorpsControl.ControllerId(1))).Settlements).Id);
        var result = session.Apply(session.Request(new EndControlOperation(), seat.ControllerId));
        Assert.Equal(SessionDisposition.Accepted, result.Disposition);
        Assert.NotEmpty(result.Events.OfType<DevelopmentCompletedEvent>());
        Assert.True(session.Snapshot.Simulation.NextSequence > world.Simulation.NextSequence);
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
                new(3, 1, 2, new(500, 1000))] }
        };
        var session = GameSession.Start(initial, 190);
        var world = session.Snapshot;
        var seat = world.Simulation.Controls[0];
        var player = Observe(world, seat, new("player"));
        var delegated = Observe(world, seat, new(CorpsControl.ControllerId(3)));
        Assert.Equal(1, Assert.Single(player.Settlements).Id);
        Assert.DoesNotContain(2, player.Settlements[0].OfficerIds);
        Assert.Equal(3, Assert.Single(delegated.Settlements).Id);
        Assert.Equal(new[] { 2 }, delegated.Settlements[0].OfficerIds);
        Assert.Equal(world.Corps.Single(c => c.Id == 3).Treasury.Money, delegated.Settlements[0].Money);
        Assert.Empty(Observe(world, seat, new("forged")).Settlements);

        var command = Assert.IsType<DevelopCommand>(DomesticCommandSelection.Choose(world, new SimulationEngine(WorldState.Restore(world)).GetAbilities(world), session.Validate,
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
        var observed = Observe(before, seat, new("player"));
        var changed = before with
        {
            Settlements = before.Settlements.Select(s => s.OwnerId == 2 ? s with { Soldiers = 99999, Morale = 0 } : s).ToArray(),
            Corps = before.Corps.Select(c => c.OwnerId == 2 ? c with { Treasury = new(999999, 999999) } : c).ToArray(),
            Officers = before.Officers.Select(p => p.OwnerId == 2 ? p with { Acted = true } : p).ToArray()
        };
        Assert.Equal(CanonicalJson.Text(observed), CanonicalJson.Text(Observe(changed, seat, new("player"))));
        string expected = CanonicalJson.Text(DomesticPlanner.Plan(observed, new(100, 0), 0));
        var command = session.ChooseDomesticCommand(seat, Guid.NewGuid(), 0);
        Assert.True(session.Execute(new("player"), command).Submission.Accepted);
        Assert.Equal(expected, CanonicalJson.Text(DomesticPlanner.Plan(observed, new(100, 0), 0)));
        Assert.NotEqual(CanonicalJson.Text(observed), CanonicalJson.Text(Observe(session.Snapshot, seat, new("player"))));
    }

    [Fact]
    public void WorkLimitReturnsDeterministicPrefixAndDistinguishesExhaustion()
    {
        var initial = G01ScenarioTests.Fixture();
        var session = GameSession.Start(initial with { Settlements = initial.Settlements.Select(s => s with { Soldiers = 5000, Morale = 50 }).ToArray() }, 190);
        var observed = Observe(session.Snapshot, session.Snapshot.Simulation.Controls[0], new("player"));
        var small = DomesticPlanner.Plan(observed, new(3, 0), 0);
        var full = DomesticPlanner.Plan(observed, new(100, 0), 0);
        Assert.True(small.BudgetExhausted);
        Assert.Equal(3, small.Evaluations);
        Assert.Single(small.Candidates);
        Assert.False(full.BudgetExhausted);
        Assert.Equal(CanonicalJson.Text(full.Candidates[0]), CanonicalJson.Text(small.Candidates[0]));
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
        var observed = Observe(world, world.Simulation.Controls[0], new("player"));
        var plan = DomesticPlanner.Plan(observed, new(100, 0), 0);
        Assert.NotEmpty(plan.Candidates);
        Assert.DoesNotContain(plan.Candidates, c => c.Activity is DomesticActivity.Train or DomesticActivity.Cultivation or DomesticActivity.Repair);
    }

    [Fact]
    public void ComputerRootRunsOriginalWorkAcrossAdvanceEntryPointsAndReplays()
    {
        var initial = G01ScenarioTests.Fixture();
        initial = initial with { Information = initial.Information with { Corps = initial.Information.Corps
            .Select(c => c.OwnerId == 2 ? c with {  } : c).ToArray() } };
        var whole = GameSession.Start(initial, 190);
        var split = GameSession.Start(initial, 190);
        var result = whole.Apply(whole.Request(new AdvanceOperation()));
        Assert.Equal(SessionDisposition.Accepted, result.Disposition);
        Assert.NotEmpty(result.Events.OfType<DevelopmentCompletedEvent>());
        Assert.True(whole.Snapshot.Simulation.NextSequence > 0);
        foreach (var seat in split.Snapshot.Simulation.Controls)
            Assert.Equal(SessionDisposition.Accepted, split.Apply(split.Request(new EndControlOperation(), seat.ControllerId)).Disposition);
        Assert.Equal(SessionDisposition.Accepted, split.Apply(split.Request(new AdvanceOperation())).Disposition);
        StateAssert.Equal(whole.Snapshot, split.Snapshot);
        var restored = SaveService.Decode(SaveService.Encode(whole), initial);
        StateAssert.Equal(whole.AdvanceXun().Snapshot, restored.AdvanceXun().Snapshot);
    }
}

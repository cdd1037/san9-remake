using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class TerminalEvaluationTests
{
    private static WorldSnapshot Snapshot(int year = 349) => new(new(year, 1, 1), new(new(year, 1, 1), 1)
    {
        Controls = [new("human", 8, ControllerKind.Player), new("computer", 2, ControllerKind.Automatic)]
    })
    {
        Forces = [new(8, 0, 0, 0), new(2, 0, 0, 0), new(70, 0, 0, 0) { IsBarbarian = true }],
        Settlements = [new(101, 8, 0, 1, 1, 0, 0, 0, 0, 0, 0), new(205, 2, 0, 1, 1, 0, 0, 0, 0, 0, 0)]
    };

    [Theory]
    [InlineData(349, TerminalRoute.Continue, null)]
    [InlineData(350, TerminalRoute.ReturnToTitle, "time-limit")]
    [InlineData(351, TerminalRoute.ReturnToTitle, "time-limit")]
    public void CalendarLimitIsExplicit(int year, TerminalRoute route, string? reason) =>
        Assert.Equal(new(route, reason, null), TerminalEvaluation.Evaluate(Snapshot(year), new([101, 205], 350)));

    [Theory]
    [InlineData(8, TerminalRoute.PlayerVictoryEnding)]
    [InlineData(2, TerminalRoute.ReturnToTitle)]
    public void TerritoryWinnerPrecedesYearAndControlDeterminesRoute(int owner, TerminalRoute route)
    {
        var world = Snapshot(350);
        world = world with { Settlements = world.Settlements.Select(s => s with { OwnerId = owner }).ToArray() };
        Assert.Equal(new(route, "conquest", owner), TerminalEvaluation.Evaluate(world, new([101, 205], 350)));
        Assert.Equal(3, world.Forces.Count);
    }

    [Fact]
    public void UniqueOrdinaryForceDoesNotNeedCitiesOrBarbarianElimination()
    {
        var world = Snapshot();
        world = world with { Forces = world.Forces.Select(f => f.Id == 2 ? f with { IsEliminated = true } : f).ToArray() };
        Assert.Equal(new(TerminalRoute.PlayerVictoryEnding, "conquest", 8), TerminalEvaluation.Evaluate(world, new([101, 205], 350)));
    }

    [Fact]
    public void PriorDefeatCannotBecomeVictoryAndEmptyCitySetIsNotVacuousVictory()
    {
        var world = Snapshot(350);
        world = world with { Simulation = world.Simulation with { TerminalReason = "player-defeated" } };
        Assert.Equal(new(TerminalRoute.ReturnToTitle, "player-defeated", null), TerminalEvaluation.Evaluate(world, new([101], 350)));
        Assert.Equal(TerminalRoute.Continue, TerminalEvaluation.Evaluate(Snapshot(), new([], null)).Route);
    }

    [Fact]
    public void TerminalCheckStopsBeforeWorldUpdatesAndRestoreDoesNotRepeat()
    {
        var world = SimulationEngineTests.CreateWorld(new(350, 1, 1));
        var initial = world.BeginMutation();
        initial.RebuildForceTrust();
        world.RestoreDerivedTrust(initial);
        var before = world.CreateSnapshot();
        var rules = new SimulationEngineTests.FixtureRules { Terminal = new([1, 2], 350),
            OnStage = (_, _, _) => throw new Exception("No stage may run after the terminal gate.") };
        var engine = new SimulationEngine(world, rules);
        Assert.Equal(SimulationBoundary.Terminal, engine.Snapshot.Simulation.Boundary);
        Assert.Equal(before.Forces, engine.Snapshot.Forces);
        Assert.Equal(before.Clock, engine.Snapshot.Clock);
        Assert.Single(engine.InitialEvents.OfType<SimulationEndedEvent>());
        Assert.Empty(engine.AdvanceXun().Events);
        var restored = new SimulationEngine(WorldState.Restore(engine.CaptureStableState()), rules);
        Assert.Empty(restored.InitialEvents);
        Assert.Equal(engine.Snapshot.StateHash, restored.Snapshot.StateHash);
    }

    [Theory]
    [InlineData(false, "player-defeated")]
    [InlineData(true, null)]
    public void OnlyLastPlayerEliminationEndsTheSession(bool anotherPlayerSurvives, string? expected)
    {
        var world = SimulationEngineTests.CreateWorld();
        var mutation = world.BeginMutation();
        mutation.SetSimulation(mutation.Simulation with { Controls = [new("survivor", 2,
            anotherPlayerSurvives ? ControllerKind.Player : ControllerKind.Automatic)] });
        List<DomainEvent> events = [new ForceEliminatedEvent(mutation.Clock, 1, true)];
        ResolutionPipeline.CheckConquest(mutation, events);
        Assert.Equal(expected, mutation.Simulation.TerminalReason);
    }
}

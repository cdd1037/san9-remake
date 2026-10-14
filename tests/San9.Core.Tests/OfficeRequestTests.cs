using San9.Tests;
using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class OfficeRequestTests
{
    private static readonly SimulationRules Rules = new()
    {
        Personnel = new([new(1, new(0, 0)), new(2, new(30, 0)), new(3, new(60, 0))]),
        Military = new(new(3, 1, [0, 0, 0], [new(0, 1, null)], [], [])
        { RegionIds = [1, 2, 3], TerritoryExits = [TerritoryExits.East, TerritoryExits.East, TerritoryExits.None] })
    };

    private static WorldState World(ulong seed = 0, bool player = true, int count = 1, bool enabled = true) => WorldState.Create(new(190, 1, 1), seed,
        [new(1, 1000) { LeaderId = 1, BaseTrust = 1000 }],
        [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
        new[] { new OfficerState(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler } }
            .Concat(Enumerable.Range(10, count).Select(id => new OfficerState(id, null, 1, new(50, 50, 50, 50), false)
                { Status = OfficerStatus.Free, BirthYear = 170, StatusStartYear = 180 }.WithLoyalty(1, 100))),
        controls: [new("player", 1, player ? ControllerKind.Player : ControllerKind.Automatic)],
        freeOfficerMigrationEnabled: false, officeRequestsEnabled: enabled, corps: [new San9.Core.Domain.CorpsState(1, 1, 1, new(10000, 10000)) { IsRoot = true }]);

    private static WorldMutation Mature(WorldState world)
    {
        var mutation = world.BeginMutation();
        mutation.SetClock(new(190, 2, 1));
        mutation.SetSimulation(mutation.Simulation with { ActiveControl = 1 });
        return mutation;
    }

    [Theory]
    [InlineData(false, 30)]
    [InlineData(true, 29)]
    public void DisabledOrBeforeThirtyDaysDoesNotConsumeRandom(bool enabled, int days)
    {
        var world = Mature(World(enabled: enabled));
        if (days == 29) world.SetClock(new(190, 1, 30));
        List<DomainEvent> events = [];
        OfficeRequests.Begin(world, Rules, events);
        Assert.Empty(events);
        Assert.Empty(world.Simulation.RandomStreams);
    }

    [Theory]
    [InlineData(49, 0, false)]
    [InlineData(50, 69, true)]
    [InlineData(50, 70, false)]
    public void LoyaltyAndStrictRequestThreshold(int loyalty, int roll, bool offered)
    {
        ulong seed = TestSeeds.ForNamedRoll("event-office-request", roll);
        var world = Mature(World(seed));
        world.Replace(world.Officers[10].WithLoyalty(1, loyalty));
        List<DomainEvent> events = [];
        OfficeRequests.Begin(world, Rules, events);
        Assert.Equal(offered, world.Simulation.PendingEvent is OfficeRequestEventSession);
        Assert.Equal(loyalty >= 50 ? 1 : 0, world.Simulation.RandomStreams.Length);
        Assert.Null(world.Officers[10].OwnerId);
    }

    [Fact]
    public void RefusalUsesDirectedAdjacentRegionsAndLowersLoyalty()
    {
        var world = Mature(World());
        List<DomainEvent> events = [];
        OfficeRequests.Begin(world, Rules, events);
        Assert.Equal(CommandRejection.None, OfficeRequests.Respond(world, Rules,
            new(Guid.NewGuid(), 1, 0, 10, EventChoice.Reject), events));
        var person = world.Officers[10];
        Assert.Null(person.SettlementId);
        Assert.Equal(2, person.RegionId);
        Assert.Equal(45, person.LoyaltyTo(1));
        Assert.Equal(190, person.StatusStartYear);
        Assert.Null(person.OwnerId);
        Assert.DoesNotContain(world.Simulation.RandomStreams, s => s.Name == "event-office-request-acceptance");
        Assert.Equal(new FreeOfficerMigratedEvent(world.Clock, 10, 1, 2), Assert.Single(events.OfType<FreeOfficerMigratedEvent>()));
    }

    [Fact]
    public void InvalidatedTargetDoesNotReceivePartialEffectsAndSelectorContinues()
    {
        var world = Mature(World(count: 2));
        List<DomainEvent> events = [];
        OfficeRequests.Begin(world, Rules, events);
        world.Replace(world.Officers[10] with { SettlementId = null, RegionId = 2 });
        var before = world.Officers[10];
        OfficeRequests.Respond(world, Rules, new(Guid.NewGuid(), 1, 0, 10, EventChoice.Accept), events);
        Assert.Equal(before, world.Officers[10]);
        Assert.True(Assert.Single(events.OfType<OfficeRequestResolvedEvent>()).Invalidated);
        Assert.Equal(11, Assert.IsType<OfficeRequestEventSession>(world.Simulation.PendingEvent).OfficerId);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(11, false)]
    public void AutomaticControllerChoosesThroughTheCommonResponseRule(int distance, bool accepted)
    {
        var world = Mature(World(player: false));
        world.Replace(world.Officers[10] with { Affinity = distance });
        world.Replace(world.Officers[10].WithLoyalty(1, 255));
        List<DomainEvent> events = [];
        OfficeRequests.Begin(world, Rules, events);
        Assert.IsType<OfficeRequestEventSession>(world.Simulation.PendingEvent);
        Assert.Equal(CommandRejection.None, OfficeRequests.Respond(world, Rules,
            new(Guid.NewGuid(), 1, 0, 10, accepted ? EventChoice.Accept : EventChoice.Reject), events));
        Assert.Equal(accepted ? EventChoice.Accept : EventChoice.Reject, Assert.Single(events.OfType<OfficeRequestResolvedEvent>()).Choice);
        Assert.Equal(accepted ? 1 : (int?)null, world.Officers[10].OwnerId);
        Assert.Equal(accepted ? 100 : 45, world.Officers[10].LoyaltyTo(1));
        Assert.DoesNotContain(world.Simulation.RandomStreams, s => s.Name == "event-office-request-acceptance");
    }

    [Fact]
    public void PublicResponsesResumeOnceRejectWrongInputsAndKeepQueriesReadOnly()
    {
        var engine = new SimulationEngine(World(count: 2), Rules);
        for (int i = 0; i < 3; i++) { engine.EndControl(new("player")); engine.AdvanceXun(); }
        Assert.Equal(10, engine.GetOfficeRequestPrompt()!.OfficerId);
        var hash = engine.Snapshot;
        _ = engine.GetOfficeRequestPrompt();
        StateAssert.Equal(hash, engine.Snapshot);
        Assert.Throws<InvalidOperationException>(() => engine.CaptureStableState());
        foreach (var command in new GameCommand[]
        {
            new RespondOfficeRequestCommand(Guid.NewGuid(), 1, 0, 11, EventChoice.Accept),
            new RespondOfficeRequestCommand(Guid.NewGuid(), 1, 0, 10, (EventChoice)99),
            new WaitCommand(Guid.NewGuid(), 1, 0)
        })
        {
            Assert.False(engine.Execute(new("player"), command).Submission.Accepted);
            StateAssert.Equal(hash, engine.Snapshot);
        }
        var first = new RespondOfficeRequestCommand(Guid.NewGuid(), 1, 0, 10, EventChoice.Accept);
        var answer = engine.Execute(new("player"), first);
        Assert.True(answer.Submission.Accepted);
        Assert.Equal(11, engine.GetOfficeRequestPrompt()!.OfficerId);
        Assert.DoesNotContain(answer.Events.OfType<StageCompletedEvent>(), e => e.Stage == ResolutionStage.XunUpdate);
        Assert.False(engine.Execute(new("player"), first).Submission.Accepted);
        var last = engine.Execute(new("player"), new RespondOfficeRequestCommand(Guid.NewGuid(), 1, 1, 11, EventChoice.Accept));
        Assert.True(last.Submission.Accepted);
        Assert.Equal(new GameClock(190, 2, 1), last.Snapshot.Clock);
        Assert.Equal(SimulationBoundary.Stable, last.Snapshot.Simulation.Boundary);
        Assert.All(last.Snapshot.Officers.Where(p => p.Id is 10 or 11), p => Assert.False(p.Acted));
        Assert.True(engine.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1,
            engine.Snapshot.Simulation.NextSequence, 10, 1)).Submission.Accepted);
        Assert.DoesNotContain(last.Events.OfType<StageCompletedEvent>(), e => e.Stage == ResolutionStage.XunUpdate);
        var restored = new SimulationEngine(WorldState.Restore(engine.CaptureStableState()), Rules);
        Assert.Empty(restored.InitialEvents);
        StateAssert.Equal(engine.Snapshot, restored.Snapshot);
    }
}

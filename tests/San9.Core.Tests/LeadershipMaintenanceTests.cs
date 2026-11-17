using San9.Tests;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class LeadershipMaintenanceTests
{
    private static readonly GovernanceRulebook Governance = new([new(100, 20000, 1), new(101, 15000, 1)]);
    private static readonly TreasureRulebook Treasures = new([new(1, new(0, 0, 0, 50), 0)]);

    private static SimulationEngine Engine(out WorldState world)
    {
        world = WorldState.Create(new(190, 1, 1), 42, [new(1, 100)],
            [new(1, 1, 1000, 100, 100, 80, 0, 0, 100, 0, 100), new(2, 1, 1000, 100, 100, 80, 0, 0, 100, 0, 100)],
            [new(11, 1, 1, new(50, 50, 50, 80), false) { IsCommander = true },
                new(12, 1, 1, new(50, 50, 50, 60), false),
                new(21, 1, 2, new(50, 50, 50, 80), false) { IsCommander = true },
                new(22, 1, 2, new(50, 50, 50, 60), false)],
            treasures: [new(1, new RegionTreasurePlacement(1))],
            controls: [new("player", 1, ControllerKind.Player)], corps: [new San9.Core.Domain.CorpsState(1, 1, null, new(10000, 100000)) { IsRoot = true }]);
        return new(world, new([new(100, 0), new(101, 0)], []), PersonnelRulebook.Empty, Treasures, Governance);
    }

    [Fact]
    public void BatchAppointmentsPublishOnlyFinalCommandersAndReplayAfterRestore()
    {
        var engine = Engine(out _);
        var origin = engine.CaptureStableState();
        var command = new AssignOfficesCommand(Guid.NewGuid(), 1, 0, [new(12, 100), new(22, 101)]);
        var result = engine.Execute(new("player"), command);
        Assert.True(result.Submission.Accepted);
        Assert.Equal([12, 22], result.Events.OfType<CommanderChangedEvent>().Select(e => e.OfficerId!.Value).ToArray());
        Assert.Equal([12, 22], engine.Snapshot.Officers.Where(p => p.IsCommander).Select(p => p.Id).ToArray());
        var restored = new SimulationEngine(WorldState.Restore(origin), new([new(100, 0), new(101, 0)], []),
            PersonnelRulebook.Empty, Treasures, Governance);
        EventAssert.Equal(result.Events, restored.Execute(new("player"), command).Events);
        StateAssert.Equal(engine.Snapshot, restored.Snapshot);
        var wait = engine.Execute(new("player"), new WaitCommand(Guid.NewGuid(), 1, 1));
        Assert.Empty(wait.Events.OfType<CommanderChangedEvent>());
    }

    [Fact]
    public void TreasureAndInjuryChangesTriggerSelectionWithoutCallerRepair()
    {
        Engine(out var world);
        var mutation = world.BeginMutation(new(Treasures, Governance));
        mutation.Replace(mutation.Treasures[1] with { Placement = new OfficerTreasurePlacement(12)});
        List<DomainEvent> events = [];
        mutation.MaintainLeadership(events);
        Assert.Equal(12, LeadershipMaintenance.Commander(mutation, 1)!.Id);
        mutation.Replace(mutation.Officers[12] with { Injury = 9 });
        mutation.MaintainLeadership(events);
        Assert.Equal(11, LeadershipMaintenance.Commander(mutation, 1)!.Id);
        Assert.Equal([12, 11], events.OfType<CommanderChangedEvent>().Select(e => e.OfficerId!.Value).ToArray());
        mutation.MaintainLeadership(events);
        Assert.Equal(2, events.Count);
    }

    [Fact]
    public void BatchedMovesAndAppointmentsMatchAfterForkAndRestore()
    {
        Engine(out var world);
        var initial = world.BeginMutation(new(Treasures, Governance));
        initial.Replace(initial.Officers[11] with { Injury = 1 });
        initial.Replace(initial.Officers[11] with { Injury = 0 });
        initial.MaintainLeadership([]);
        world.Commit(initial);
        var fork = world.Fork();
        var restored = WorldState.Restore(world.CreateSnapshot());

        static List<DomainEvent> Change(WorldState state)
        {
            var mutation = state.BeginMutation(new(Treasures, Governance));
            mutation.Replace(mutation.Officers[11] with { SettlementId = 2, IsCommander = false });
            mutation.Replace(mutation.Officers[21] with { SettlementId = 1, IsCommander = false });
            mutation.Replace(mutation.Treasures[1] with { Placement = new OfficerTreasurePlacement(12)});
            mutation.Replace(mutation.Officers[22] with { OfficeId = 100 });
            List<DomainEvent> events = [];
            mutation.MaintainLeadership(events);
            mutation.MaintainLeadership(events);
            state.Commit(mutation);
            return events;
        }

        var events = Change(fork);
        EventAssert.Equal(events, Change(restored));
        StateAssert.Equal(fork.CreateSnapshot(), restored.CreateSnapshot());
        Assert.Equal([(1, (int?)11, (int?)12), (2, (int?)21, (int?)22)],
            events.OfType<CommanderChangedEvent>().Select(e => (e.PlaceId, e.PreviousId, e.OfficerId)).ToArray());
        Assert.Equal([12, 22], fork.CreateSnapshot().Officers.Where(p => p.IsCommander).Select(p => p.Id).ToArray());
    }

    [Fact]
    public void UnrelatedChangesPreserveAnExistingAppointmentInsteadOfReselecting()
    {
        Engine(out var world);
        var mutation = world.BeginMutation(new(Treasures, Governance));
        mutation.Replace(mutation.Officers[11] with { IsCommander = false });
        mutation.Replace(mutation.Officers[12] with { IsCommander = true, Acted = true, TacticCooldown = 2 });
        List<DomainEvent> events = [];
        mutation.MaintainLeadership(events);
        Assert.Equal(12, LeadershipMaintenance.Commander(mutation, 1)!.Id);
        Assert.Empty(events);
    }
}

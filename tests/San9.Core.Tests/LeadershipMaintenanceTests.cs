using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class LeadershipMaintenanceTests
{
    private static readonly GovernanceRulebook Governance = new([new(100, 20000, 1), new(101, 15000, 1)]);
    private static readonly TreasureRulebook Treasures = new([new(1, new(0, 0, 0, 50), 0)]);

    private static SimulationEngine Engine(out WorldState world)
    {
        world = WorldState.Create(new(190, 1, 1), 42, [new(1, 10000, 100000, 100)],
            [new(1, 1, 1000, 100, 100, 80, 0, 0, 100, 0, 100), new(2, 1, 1000, 100, 100, 80, 0, 0, 100, 0, 100)],
            [new(11, 1, 1, new(50, 50, 50, 80), false) { IsCommander = true },
                new(12, 1, 1, new(50, 50, 50, 60), false),
                new(21, 1, 2, new(50, 50, 50, 80), false) { IsCommander = true },
                new(22, 1, 2, new(50, 50, 50, 60), false)],
            treasures: [new(1, null, TreasureDiscovery.Discovered, null)],
            controls: [new("player", 1, ControllerKind.Player)]);
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
        Assert.Equal(CanonicalJson.Text(result.Events), CanonicalJson.Text(restored.Execute(new("player"), command).Events));
        Assert.Equal(engine.Snapshot.StateHash, restored.Snapshot.StateHash);
        var wait = engine.Execute(new("player"), new WaitCommand(Guid.NewGuid(), 1, 1));
        Assert.Empty(wait.Events.OfType<CommanderChangedEvent>());
    }

    [Fact]
    public void TreasureAndInjuryChangesTriggerSelectionWithoutCallerRepair()
    {
        Engine(out var world);
        var mutation = world.BeginMutation(new(Treasures, Governance));
        mutation.Replace(mutation.Treasures[1] with { HolderId = 12 });
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

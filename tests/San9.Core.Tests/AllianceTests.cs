using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class AllianceTests
{
    private static WorldState World(int duration = 36) => WorldState.Create(new(190, 1, 1), 123,
        [new(1, 0, 0, 100), new(2, 0, 0, 200), new(3, 0, 0, 50), new(4, 0, 0, 10)], [], [],
        alliance: new(3, 2, [1, 2, 4], duration));

    [Fact]
    public void StartingAllianceProtectsMembersButNotTargetAndExpiresExactlyOnce()
    {
        var world = World();
        var engine = new SimulationEngine(world);
        Assert.Equal(36, engine.Snapshot.Simulation.Alliance!.RemainingXun);
        Assert.False(MilitaryCombat.Hostile(world.BeginMutation(), 1, 2));
        Assert.False(MilitaryCombat.Hostile(world.BeginMutation(), 2, 1));
        Assert.True(MilitaryCombat.Hostile(world.BeginMutation(), 1, 3));
        List<AllianceEndedEvent> ends = [];
        for (int xun = 1; xun <= 36; xun++)
        {
            ends.AddRange(engine.AdvanceXun().Events.OfType<AllianceEndedEvent>());
            Assert.Equal(xun == 36 ? null : 36 - xun, engine.Snapshot.Simulation.Alliance?.RemainingXun);
        }
        Assert.Equal(AllianceEndReason.Expired, Assert.Single(ends).Reason);
        Assert.True(MilitaryCombat.Hostile(world.BeginMutation(), 1, 2));
        Assert.Empty(engine.AdvanceXun().Events.OfType<AllianceEndedEvent>());
    }

    [Fact]
    public void RestoringAllianceDoesNotConsumeAnotherXunAndContinuationMatches()
    {
        var original = new SimulationEngine(World(2));
        original.AdvanceXun();
        var restored = new SimulationEngine(WorldState.Restore(original.CaptureStableState()));
        Assert.Empty(restored.InitialEvents);
        Assert.Equal(1, restored.Snapshot.Simulation.Alliance!.RemainingXun);
        var expected = original.AdvanceXun();
        var actual = restored.AdvanceXun();
        Assert.Equal(expected.Snapshot.StateHash, actual.Snapshot.StateHash);
        Assert.Equal(CanonicalJson.Text(expected.Events), CanonicalJson.Text(actual.Events));
    }

    [Theory]
    [InlineData(2, AllianceEndReason.LeaderEliminated)]
    [InlineData(3, AllianceEndReason.TargetEliminated)]
    public void LosingLeaderOrTargetEndsAlliance(int force, AllianceEndReason reason)
    {
        var mutation = World().BeginMutation();
        List<DomainEvent> events = [];
        AllianceDiplomacy.ForceEliminated(mutation, force, events);
        AllianceDiplomacy.ForceEliminated(mutation, force, events);
        Assert.Null(mutation.Simulation.Alliance);
        Assert.Equal(reason, Assert.Single(events.OfType<AllianceEndedEvent>()).Reason);
    }

    [Fact]
    public void LosingOrdinaryMemberPreservesRemainingAllianceUntilTooSmall()
    {
        var mutation = World().BeginMutation();
        List<DomainEvent> events = [];
        AllianceDiplomacy.ForceEliminated(mutation, 4, events);
        Assert.Equal(new[] { 1, 2 }, mutation.Simulation.Alliance!.MemberForceIds);
        Assert.Single(events.OfType<AllianceMemberRemovedEvent>());
        AllianceDiplomacy.ForceEliminated(mutation, 1, events);
        Assert.Null(mutation.Simulation.Alliance);
        Assert.Equal(AllianceEndReason.InsufficientMembers, Assert.Single(events.OfType<AllianceEndedEvent>()).Reason);
    }

    [Fact]
    public void InvalidAllianceReferencesAndExpiredStateCannotBeRestored()
    {
        var valid = new SimulationEngine(World()).CaptureStableState();
        foreach (var invalid in new AllianceState[]
        {
            new(1, 2, [1, 2], 1), new(3, 4, [1, 2], 1), new(3, 2, [1, 2, 9000], 1),
            new(3, 2, [1, 2], 0), new(3, 2, [1, 1, 2], 1), new(3, 2, [2], 1)
        }) Assert.Throws<ArgumentException>(() => WorldState.Restore(valid with { Simulation = valid.Simulation with { Alliance = invalid } }));
    }
}

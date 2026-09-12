using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class PrisonerCycleTests
{
    private static WorldState World(int distance = 50, int loyalty = 80, int? original = 2, bool leader = true,
        OfficerStatus status = OfficerStatus.Captive) => WorldState.Create(new(190, 1, 1), 190,
        [new(1, 1000, 1000, 0) { LeaderId = 1 }, new(2, 1000, 1000, 0) { LeaderId = leader ? 2 : null }],
        [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100), new(2, 2, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
        [new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
         new(2, 2, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
         new OfficerState(3, original, 1, new(50, 50, 50, 50), true)
         { Status = status, Affinity = distance, Righteousness = 15, RewardedThisXun = true }.WithLoyalty(1, 130).WithLoyalty(2, loyalty)],
        controls: [new("player", 1, ControllerKind.Player)]);

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 25)]
    [InlineData(3, 25)]
    [InlineData(4, 40)]
    [InlineData(35, 85)]
    [InlineData(36, 100)]
    [InlineData(49, 115)]
    [InlineData(50, 115)]
    public void ThresholdUsesIntegerSquareRoot(int distance, int expected) =>
        Assert.Equal(expected, PrisonerCycle.LoyaltyThreshold(distance));

    [Theory]
    [InlineData(0, 9, true)]
    [InlineData(0, 10, false)]
    [InlineData(1, 24, true)]
    [InlineData(1, 25, false)]
    [InlineData(35, 84, true)]
    [InlineData(35, 85, false)]
    [InlineData(36, 99, true)]
    public void CycleUsesStrictProbabilityBoundaryAndIgnoresRighteousness(int distance, int roll, bool losesLoyalty)
    {
        var world = World(distance);
        var mutation = world.BeginMutation();
        ulong state = StateForRoll(roll);
        mutation.SetSimulation(mutation.Simulation with { RandomStreams = [new("personnel-prisoner-loyalty", state)] });
        List<DomainEvent> events = [];
        PrisonerCycle.Apply(mutation, events);
        Assert.Equal(losesLoyalty ? 79 : 80, mutation.Officers[3].LoyaltyTo(2));
        Assert.Equal(130, mutation.Officers[3].LoyaltyTo(1));
        Assert.Equal(OfficerStatus.Captive, mutation.Officers[3].Status);
        Assert.Equal(1, mutation.Officers[3].SettlementId);
        var expected = new StableRandom(state);
        expected.NextUInt64();
        Assert.Equal(expected.State, Assert.Single(mutation.Simulation.RandomStreams).State);
        Assert.Equal(losesLoyalty ? 1 : 0, events.OfType<OfficerLoyaltyChangedEvent>().Count());
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(255, 254)]
    public void MissingOriginalRulerUsesDistanceFiftyAndLoyaltyFloorsAtZero(int loyalty, int expected)
    {
        var world = World(0, loyalty, leader: false);
        var mutation = world.BeginMutation();
        PrisonerCycle.Apply(mutation, []);
        Assert.Equal(expected, mutation.Officers[3].LoyaltyTo(2));
        Assert.Single(mutation.Simulation.RandomStreams);
    }

    [Fact]
    public void UnknownOriginalForceStillDrawsButDoesNotChangeAnyLoyalty()
    {
        var mutation = World(original: null).BeginMutation();
        var person = mutation.Officers[3];
        List<DomainEvent> events = [];
        PrisonerCycle.Apply(mutation, events);
        Assert.Equal(person, mutation.Officers[3]);
        Assert.Single(mutation.Simulation.RandomStreams);
        Assert.Empty(events);
    }

    [Fact]
    public void NonPrisonersDoNotEnterTheCycle()
    {
        var mutation = World(original: null, status: OfficerStatus.Free).BeginMutation();
        PrisonerCycle.Apply(mutation, []);
        Assert.Empty(mutation.Simulation.RandomStreams);
        Assert.Equal(80, mutation.Officers[3].LoyaltyTo(2));
    }

    [Fact]
    public void XunUpdateRunsWithoutRecruitmentAndRestoreDoesNotRepeatIt()
    {
        var engine = new SimulationEngine(World());
        Assert.Equal(79, engine.Snapshot.Officers.Single(p => p.Id == 3).LoyaltyTo(2));
        Assert.False(engine.Snapshot.Officers.Single(p => p.Id == 3).RewardedThisXun);
        var restored = new SimulationEngine(WorldState.Restore(engine.CaptureStableState()));
        Assert.Equal(engine.Snapshot.StateHash, restored.Snapshot.StateHash);
        engine.EndControl(new("player"));
        restored.EndControl(new("player"));
        Assert.Equal(CanonicalJson.Text(engine.AdvanceXun().Events), CanonicalJson.Text(restored.AdvanceXun().Events));
        Assert.Equal(78, engine.Snapshot.Officers.Single(p => p.Id == 3).LoyaltyTo(2));
        Assert.Equal(engine.Snapshot.StateHash, restored.Snapshot.StateHash);
    }

    private static ulong StateForRoll(int roll)
    {
        for (ulong state = 1; ; state++)
            if (new StableRandom(state).NextUInt64() % 100 == (ulong)roll) return state;
    }
}

using System.Text.Json;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class OfficerInjuryRecoveryTests
{
    private static WorldState World(int injury, GameClock? clock = null) => WorldState.Create(clock ?? new(190, 1, 1), 190,
        [new(1, 1000, 1000, 0) { LeaderId = 1 }],
        [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
        [new(1, 1, 1, new(80, 70, 60, 50), false) { Status = OfficerStatus.Ruler, Injury = injury }],
        controls: [new("player", 1, ControllerKind.Player)]);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(7, 6)]
    [InlineData(14, 13)]
    public void InitialXunRecoversOnceAndDoesNotConsumeRandom(int injury, int expected)
    {
        var engine = new SimulationEngine(World(injury));
        var person = Assert.Single(engine.Snapshot.Officers);
        Assert.Equal(expected, person.Injury);
        Assert.Equal(new(80, 70, 60, 50), person.BaseAbilities);
        Assert.Empty(engine.Snapshot.Simulation.RandomStreams);
        var changes = engine.InitialEvents.OfType<OfficerInjuryRecoveredEvent>().ToArray();
        if (injury == 0) Assert.Empty(changes);
        else Assert.Equal(new OfficerInjuryRecoveredEvent(new(190, 1, 1), 1, injury, expected), Assert.Single(changes));
    }

    [Fact]
    public void RecoveryUpdatesEffectiveAbilitiesWithoutChangingBaseAbilities()
    {
        var world = World(2);
        var before = Assert.Single(world.CreateSnapshot().Officers);
        Assert.Equal(64, OfficerAbilityRules.Empty.Evaluate(before, []).War);
        var engine = new SimulationEngine(world);
        var after = Assert.Single(engine.Snapshot.Officers);
        Assert.Equal(72, OfficerAbilityRules.Empty.Evaluate(after, []).War);
        Assert.Equal(before.BaseAbilities, after.BaseAbilities);
    }

    [Theory]
    [InlineData(OfficerStatus.Serving)]
    [InlineData(OfficerStatus.Captive)]
    [InlineData(OfficerStatus.Free)]
    [InlineData(OfficerStatus.Undiscovered)]
    [InlineData(OfficerStatus.NotAppeared)]
    [InlineData(OfficerStatus.Dead)]
    [InlineData(OfficerStatus.Special)]
    public void SharedInjuryFieldRecoversWithoutStatusOrVisibilityFiltering(OfficerStatus status)
    {
        var mutation = World(0).BeginMutation();
        mutation.Replace(new OfficerState(1, status == OfficerStatus.Serving ? 1 : null, 1, new(50, 50, 50, 50), true)
        { Status = status, Injury = 3 });
        List<DomainEvent> events = [];
        OfficerInjuryRecovery.Apply(mutation, events);
        Assert.Equal(2, mutation.Officers[1].Injury);
        Assert.Equal(status, mutation.Officers[1].Status);
        Assert.True(mutation.Officers[1].Acted);
        Assert.Equal(1, Assert.Single(events.OfType<OfficerInjuryRecoveredEvent>()).OfficerId);
        Assert.Empty(mutation.Simulation.RandomStreams);
    }

    [Theory]
    [InlineData(190, 1, 1)]
    [InlineData(190, 3, 21)]
    [InlineData(190, 12, 21)]
    public void StableRestoreAndQuarterOrYearBoundariesDoNotDoubleRecover(int year, int month, int day)
    {
        var engine = new SimulationEngine(World(3, new(year, month, day)));
        var restored = new SimulationEngine(WorldState.Restore(engine.CaptureStableState()));
        Assert.Equal(2, Assert.Single(restored.Snapshot.Officers).Injury);
        Assert.Empty(restored.InitialEvents);
        for (int step = 0; step < 3; step++)
        {
            engine.EndControl(new("player"));
            restored.EndControl(new("player"));
            var events = engine.AdvanceXun().Events;
            var replay = restored.AdvanceXun().Events;
            Assert.Equal(CanonicalJson.Text(events), CanonicalJson.Text(replay));
            Assert.Equal(engine.Snapshot.StateHash, restored.Snapshot.StateHash);
            Assert.Equal(Math.Max(0, 1 - step), Assert.Single(engine.Snapshot.Officers).Injury);
            Assert.Equal(step < 2 ? 1 : 0, events.OfType<OfficerInjuryRecoveredEvent>().Count());
        }
    }

    [Fact]
    public void RecoveryEventRoundTripsThroughDomainEventContract()
    {
        DomainEvent original = new OfficerInjuryRecoveredEvent(new(190, 1, 1), 5000, 7, 6);
        string json = JsonSerializer.Serialize(original);
        Assert.Contains("officer-injury-recovered", json);
        Assert.Equal(original, JsonSerializer.Deserialize<DomainEvent>(json));
    }
}

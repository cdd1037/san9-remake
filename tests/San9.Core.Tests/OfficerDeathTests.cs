using System.Text.Json;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class OfficerDeathTests
{
    private static WorldState World() => WorldState.Create(new(190, 1, 1), 42,
        [new(1, 1000, 1000, 0) { LeaderId = 1, AdviserId = 11 },
         new(2, 1000, 1000, 0) { LeaderId = 2 }],
        [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100), new(2, 2, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
        [new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
         new(2, 2, 2, new(50, 50, 100, 50), false) { Status = OfficerStatus.Ruler },
         new(11, 1, 1, new(70, 70, 70, 70), false)
         { Status = OfficerStatus.Strategist, ParentId = 1, BloodlineId = 9000, LearnedTactics = [2],
             TacticCooldown = 3, RewardedThisXun = true }],
        treasures: [new(8, 11, TreasureDiscovery.Discovered, null), new(7, 11, TreasureDiscovery.Discovered, null)],
        controls: [new("enemy", 2, ControllerKind.Player)], relations: [new(1, 2, 50)]);

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    public void RemovalSettlesBelongingsOnceAndPreservesGenealogy(int? recipient)
    {
        var world = World();
        _ = new SimulationEngine(world, EconomyRulebook.Empty, PersonnelRulebook.Empty,
            new([new(7, default, 0), new(8, default, 0)]));
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Officers[11] with { EquippedTacticId = 2 });
        var randomBefore = mutation.Simulation.RandomStreams;
        var events = new List<DomainEvent>();
        Assert.True(OfficerDeath.Remove(mutation, 11, OfficerDeathCause.ForceEliminated, PersonnelRulebook.Empty, events, true));
        OfficerDeath.SettleTreasures(mutation, 11, recipient, events);
        int eventCount = events.Count;
        Assert.False(OfficerDeath.Remove(mutation, 11, OfficerDeathCause.NoRetreat, PersonnelRulebook.Empty, events, true));
        OfficerDeath.SettleTreasures(mutation, 11, recipient, events);
        Assert.Equal(eventCount, events.Count);
        world.Commit(mutation);
        var snapshot = world.CreateSnapshot();
        var person = snapshot.Officers.Single(p => p.Id == 11);
        Assert.Equal(OfficerStatus.Dead, person.Status);
        Assert.Null(person.OwnerId);
        Assert.Null(person.SettlementId);
        Assert.Null(person.RegionId);
        Assert.Null(person.UnitId);
        Assert.Null(person.EquippedTacticId);
        Assert.Equal(0, person.TacticCooldown);
        Assert.Null(snapshot.Forces.Single(f => f.Id == 1).AdviserId);
        Assert.Equal(1, person.ParentId);
        Assert.Equal(9000, person.BloodlineId);
        Assert.Equal(new[] { 2 }, person.LearnedTactics);
        Assert.All(snapshot.Treasures, item => Assert.Equal(recipient, item.HolderId));
        Assert.Equal(randomBefore, snapshot.Simulation.RandomStreams);
        Assert.Equal(new OfficerDiedEvent(snapshot.Clock, 11, OfficerDeathCause.ForceEliminated,
            OfficerStatus.Strategist, 1, 1, null, null), Assert.Single(events.OfType<OfficerDiedEvent>()));
        Assert.Equal(new[] { 7, 8 }, recipient is null
            ? events.OfType<TreasureDroppedEvent>().Select(e => e.TreasureId)
            : events.OfType<TreasureTransferredEvent>().Select(e => e.TreasureId));
        var restored = WorldState.Restore(JsonSerializer.Deserialize<WorldSnapshot>(CanonicalJson.Text(snapshot), CanonicalJson.Options)!);
        Assert.Equal(snapshot.StateHash, restored.CreateSnapshot().StateHash);
        Assert.Equal(CanonicalJson.Text(events), CanonicalJson.Text(
            JsonSerializer.Deserialize<DomainEvent[]>(JsonSerializer.Serialize(events))));
    }

    [Fact]
    public void DeathCancelsRecruitmentAndReturnsTheSurvivingExecutor()
    {
        var world = World();
        var places = new PersonnelRulebook([new(1, new(0, 0)), new(2, new(300, 0))],
            new Dictionary<int, int> { [1] = 1, [2] = 2 }, [new(1, 2)]);
        var engine = new SimulationEngine(world, EconomyRulebook.Empty, places,
            new([new(7, default, 0), new(8, default, 0)]));
        Assert.Equal(CommandRejection.None, engine.Execute(new("enemy"),
            new RecruitOfficerCommand(Guid.NewGuid(), 2, 0, 2, 11)).Submission.Rejection);
        var mission = Assert.Single(engine.Snapshot.Simulation.Tasks);
        var mutation = world.BeginMutation();
        var events = new List<DomainEvent>();
        OfficerDeath.Remove(mutation, 11, OfficerDeathCause.ForceEliminated, places, events, false);
        OfficerDeath.SettleTreasures(mutation, 11, null, events);
        world.Commit(mutation);
        Assert.Equal(mission.Id, Assert.Single(events.OfType<TaskCancelledEvent>()).TaskId);
        Assert.Equal("personnel-return", Assert.Single(world.CreateSnapshot().Simulation.Tasks).Kind);
        Assert.Equal(2, world.CreateSnapshot().Officers.Single(p => p.Id == 2).OwnerId);
        engine.EndControl(new("enemy"));
        Assert.Single(engine.AdvanceXun().Events.OfType<PersonnelReturnedEvent>());
        Assert.Empty(engine.Snapshot.Simulation.Tasks);
    }

    [Fact]
    public void DeadRecipientIsRejectedBeforeAnyTreasureTransfer()
    {
        var mutation = World().BeginMutation();
        var events = new List<DomainEvent>();
        OfficerDeath.Remove(mutation, 11, OfficerDeathCause.NoRetreat, PersonnelRulebook.Empty, events, true);
        Assert.Throws<InvalidOperationException>(() => OfficerDeath.SettleTreasures(mutation, 11, 11, events));
        Assert.All(mutation.Treasures.Values, item => Assert.Equal(11, item.HolderId));
        Assert.Single(events);
    }
}

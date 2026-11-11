using San9.Tests;
using System.Collections.Immutable;
using System.Text.Json;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class OfficerDeathTests
{
    private static WorldState World() => WorldState.Create(new(190, 1, 1), 42,
        [new(1, 0) { LeaderId = 1, AdviserId = 11 },
         new(2, 0) { LeaderId = 2 }],
        [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100), new(2, 2, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
        [new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
         new(2, 2, 2, new(50, 50, 100, 50), false) { Status = OfficerStatus.Ruler },
         new(11, 1, 1, new(70, 70, 70, 70), false)
         { Status = OfficerStatus.Strategist, ParentId = 1, BloodlineId = 9000, LearnedTactics = [2],
             TacticCooldown = 3, RewardedThisXun = true }],
        treasures: [new(8, 11, TreasureDiscovery.Discovered, null), new(7, 11, TreasureDiscovery.Discovered, null)],
        controls: [new("enemy", 2, ControllerKind.Player)], relations: [new(1, 2, 50)], corps: [new San9.Core.Domain.CorpsState(1, 1, 1, new(1000, 1000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 2, new(1000, 1000)) { IsRoot = true }]);

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
        Assert.True(OfficerDeath.Remove(mutation, 11, OfficerDeathCause.ForceEliminated, PersonnelRulebook.Empty, events, true,
            out int? treasureDropRegionId));
        OfficerDeath.SettleTreasures(mutation, 11, recipient, treasureDropRegionId, events);
        int eventCount = events.Count;
        Assert.False(OfficerDeath.Remove(mutation, 11, OfficerDeathCause.NoRetreat, PersonnelRulebook.Empty, events, true, out _));
        OfficerDeath.SettleTreasures(mutation, 11, recipient, treasureDropRegionId, events);
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
        Assert.All(snapshot.Treasures, item =>
        {
            Assert.Equal(recipient is null ? TreasureDiscovery.Buried : TreasureDiscovery.Discovered, item.Discovery);
            Assert.Equal(recipient is null ? 1 : (int?)null, item.DiscoveryRegionId);
        });
        Assert.Equal(randomBefore, snapshot.Simulation.RandomStreams);
        Assert.Equal(new OfficerDiedEvent(snapshot.Clock, 11, OfficerDeathCause.ForceEliminated,
            OfficerStatus.Strategist, 1, 1, null, null), Assert.Single(events.OfType<OfficerDiedEvent>()));
        Assert.Equal(new[] { 7, 8 }, recipient is null
            ? events.OfType<TreasureDroppedEvent>().Select(e => e.TreasureId)
            : events.OfType<TreasureTransferredEvent>().Select(e => e.TreasureId));
        var restored = WorldState.Restore(JsonSerializer.Deserialize<WorldSnapshot>(CanonicalJson.Text(snapshot), CanonicalJson.Options)!);
        StateAssert.Equal(snapshot, restored.CreateSnapshot());
        EventAssert.Equal(events, Assert.IsType<DomainEvent[]>(JsonSerializer.Deserialize<DomainEvent[]>(JsonSerializer.Serialize(events))));
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
            new RecruitOfficersCommand(Guid.NewGuid(), 2, 0, [2], 11)).Submission.Rejection);
        var mission = Assert.Single(engine.Snapshot.Simulation.Tasks);
        var mutation = world.BeginMutation();
        var events = new List<DomainEvent>();
        OfficerDeath.Remove(mutation, 11, OfficerDeathCause.ForceEliminated, places, events, false, out int? treasureDropRegionId);
        OfficerDeath.SettleTreasures(mutation, 11, null, treasureDropRegionId, events);
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
        OfficerDeath.Remove(mutation, 11, OfficerDeathCause.NoRetreat, PersonnelRulebook.Empty, events, true, out int? treasureDropRegionId);
        Assert.Throws<InvalidOperationException>(() => OfficerDeath.SettleTreasures(mutation, 11, 11, treasureDropRegionId, events));
        Assert.All(mutation.Treasures.Values, item => Assert.Equal(11, item.HolderId));
        Assert.Single(events);
    }

    [Theory]
    [InlineData(false, 1, null, 2, 1)]
    [InlineData(false, null, 30, 2, 30)]
    [InlineData(true, 1, null, 2, 1)]
    [InlineData(true, null, null, 2, 2)]
    [InlineData(true, null, null, null, null)]
    public void DroppedTreasuresUseServicePlaceCapturedBeforeLocationCleanup(bool deployed, int? servicePlace,
        int? region, int? previousLocation, int? expectedLocation)
    {
        var mutation = World().BeginMutation();
        if (deployed)
            mutation.AddUnit(new(20, 1, new(300, 0), 1000) { ServicePlaceId = servicePlace });
        mutation.Replace(mutation.Officers[11] with
        {
            Status = deployed ? OfficerStatus.Serving : OfficerStatus.Free,
            OwnerId = deployed ? 1 : null, SettlementId = deployed ? null : servicePlace,
            RegionId = region, UnitId = deployed ? 20 : null
        });
        foreach (var item in mutation.Treasures.Values.ToArray())
            mutation.Replace(item with { DiscoveryRegionId = previousLocation });
        var events = new List<DomainEvent>();
        Assert.True(OfficerDeath.Remove(mutation, 11, OfficerDeathCause.Natural, PersonnelRulebook.Empty, events, false,
            out int? treasureDropRegionId));
        if (deployed) mutation.RemoveUnit(20);
        OfficerDeath.SettleTreasures(mutation, 11, null, treasureDropRegionId, events);
        Assert.All(mutation.Treasures.Values, item =>
        {
            Assert.Null(item.HolderId);
            Assert.Equal(TreasureDiscovery.Buried, item.Discovery);
            Assert.Equal(expectedLocation, item.DiscoveryRegionId);
        });
        Assert.Equal(2, events.OfType<TreasureDroppedEvent>().Count());
        Assert.Empty(mutation.Simulation.RandomStreams);
    }

    [Fact]
    public void FreeOfficerNaturalDeathLeavesTreasureThatCanBeFoundAfterRestore()
    {
        var initial = World();
        _ = new SimulationEngine(initial, EconomyRulebook.Empty, PersonnelRulebook.Empty,
            new([new(7, default, 0), new(8, default, 0)]));
        var baseline = initial.CreateSnapshot();
        var world = WorldState.Restore(baseline with
        {
            Forces = baseline.Forces.Select(f => f with { AdviserId = null }).ToImmutableArray(),
            Officers = baseline.Officers.Select(p => p.Id == 11
                ? p with { OwnerId = null, Status = OfficerStatus.Free, BirthYear = 90 }
                : p with { BaseAbilities = new(50, 50, 500, 50) }).ToImmutableArray(),
            Treasures = [new(7, 11, TreasureDiscovery.Discovered, 2)]
        });
        var mutation = world.BeginMutation();
        mutation.SetClock(new(191, 1, 1));
        mutation.SetSimulation(mutation.Simulation with { Controls = [new("player", 1, ControllerKind.Player)] });
        var events = new List<DomainEvent>();
        NaturalMortality.Apply(mutation, new(), events);
        world.Commit(mutation);
        Assert.Single(events.OfType<TreasureDroppedEvent>());
        var dropped = Assert.Single(world.CreateSnapshot().Treasures);
        Assert.Equal(TreasureDiscovery.Buried, dropped.Discovery);
        Assert.Equal(1, dropped.DiscoveryRegionId);

        var places = new PersonnelRulebook([new(1, new(0, 0)), new(2, new(300, 0))]);
        var restored = WorldState.Restore(JsonSerializer.Deserialize<WorldSnapshot>(
            CanonicalJson.Text(world.CreateSnapshot()), CanonicalJson.Options)!);
        var engine = new SimulationEngine(restored, EconomyRulebook.Empty, places, new([new(7, default, 0)]));
        Assert.True(engine.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, 0, [1], 1)).Submission.Accepted);
        engine.EndControl(new("player"));
        var found = Assert.Single(engine.AdvanceXun().Events.OfType<SearchResolvedEvent>());
        Assert.Equal(SearchResultKind.Treasure, found.Result);
        Assert.Equal(7, found.TreasureId);
        Assert.Equal(1, Assert.Single(engine.Snapshot.Treasures).HolderId);
        Assert.Equal(TreasureDiscovery.Discovered, Assert.Single(engine.Snapshot.Treasures).Discovery);
    }
}

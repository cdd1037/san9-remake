using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class ForceTrustTests
{
    private static SettlementState City(int id, int owner) => new(id, owner, 1000, 100, 100, 100, 800, 100, 100, 100, 100);

    [Fact]
    public void ExpiredRequestMustUseReducedTrustForSalaryLoyaltyRoll()
    {
        ulong seed = Enumerable.Range(1, 10000).Select(i => (ulong)i).First(i =>
            new StableRandom(StableRandom.DeriveSeed(i, "personnel-salary-loyalty")).NextUInt64() % 100 == 2);
        var state = WorldState.Create(new(190, 2, 1), seed,
            [new(1, 100) { BaseTrust = 100, LeaderId = 1 }, new(2, 100), new(3, 100)],
            [City(10, 1), City(20, 2), City(30, 3)],
            [new(1, 1, 10, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
             new(11, 1, 10, new(50, 50, 50, 50), false) { ForceLoyalties = System.Collections.Immutable.ImmutableDictionary<int, int>.Empty.Add(1, 70) }],
            attackRequests: [new(1, 2, 30, 3, 1)], corps: [new San9.Core.Domain.CorpsState(1, 1, 1, new(1000, 1000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, null, new(1000, 1000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(3, 3, null, new(1000, 1000)) { IsRoot = true }]);
        var engine = new SimulationEngine(state);
        Assert.Equal(80, engine.Snapshot.Forces.Single(f => f.Id == 1).Trust);
        Assert.Equal(70, engine.Snapshot.Officers.Single(p => p.Id == 11).Loyalty);
    }

    [Fact]
    public void ExpiredAttackRequestMustChangeEffectiveTrustAtStableXunBoundary()
    {
        var state = WorldState.Create(new(190, 2, 1), 42,
            [new(1, 100) { BaseTrust = 100 }, new(2, 100), new(3, 100)],
            [City(10, 1), City(20, 2), City(30, 3)], [],
            attackRequests: [new(1, 2, 30, 3, 1)], corps: [new San9.Core.Domain.CorpsState(1, 1, null, new(1000, 1000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, null, new(1000, 1000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(3, 3, null, new(1000, 1000)) { IsRoot = true }]);
        var engine = new SimulationEngine(state);
        Assert.Empty(engine.Snapshot.Simulation.AttackRequests);
        Assert.Equal(80, engine.Snapshot.Forces.Single(f => f.Id == 1).BaseTrust);
        Assert.Equal(SimulationBoundary.Stable, engine.Snapshot.Simulation.Boundary);
        Assert.Equal(80, engine.Snapshot.Forces.Single(f => f.Id == 1).Trust);
    }

    private sealed class ObserveXunRules : SimulationRules
    {
        internal int ObservedTrust { get; private set; }
        internal int ForceId { get; init; } = 1;
        internal override void ApplyStage(ResolutionStage stage, WorldMutation world, List<DomainEvent> events)
        {
            if (stage == ResolutionStage.XunUpdate) ObservedTrust = world.Forces[ForceId].Trust;
        }
    }

    [Fact]
    public void ExpiredRequestMustAffectTrustBeforeSalaryAndOfficeRequestPhaseEnds()
    {
        var state = WorldState.Create(new(190, 2, 1), 42,
            [new(1, 100) { BaseTrust = 100 }, new(2, 100), new(3, 100)],
            [City(10, 1), City(20, 2), City(30, 3)], [],
            attackRequests: [new(1, 2, 30, 3, 1)], corps: [new San9.Core.Domain.CorpsState(1, 1, null, new(1000, 1000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, null, new(1000, 1000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(3, 3, null, new(1000, 1000)) { IsRoot = true }]);
        var rules = new ObserveXunRules();
        var engine = new SimulationEngine(state, rules);
        Assert.Equal(80, engine.Snapshot.Forces.Single(f => f.Id == 1).Trust);
        Assert.Equal(80, rules.ObservedTrust);
    }

    [Fact]
    public void CompletedCityAttackMustUpdateTrustBeforeNextXunConsumers()
    {
        var state = WorldState.Create(new(190, 2, 1), 42,
            [new(1, 100) { BaseTrust = 100 }, new(2, 200) { BaseTrust = 100 }],
            [City(10, 1), City(20, 2) with { Population = 1000000 }], [],
            units: [new(1, 1, new(2, 2), 1000) { Morale = 100 }], corps: [new San9.Core.Domain.CorpsState(1, 1, null, new(1000, 1000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, null, new(1000, 1000)) { IsRoot = true }]);
        var world = state.BeginMutation();
        List<DomainEvent> events = [];
        world.RebuildForceTrust();
        Assert.Equal(200, world.Forces[2].Trust);
        CityBattleEffects.CompleteAttack(world, 1, 20, 2, events);
        Assert.Equal(792, world.Settlements[20].PublicOrder);
        Assert.Equal(198, world.Forces[2].Trust);
        world.SetClock(new(190, 2, 11));
        var rules = new ObserveXunRules { ForceId = 2 };
        new ResolutionPipeline(rules).Prepare(world, events);
        Assert.Equal(198, world.Forces[2].Trust);
        Assert.Equal(198, rules.ObservedTrust);
    }

    private static void InitializeTrust(WorldState state, EconomyRulebook economy)
    {
        var mutation = state.BeginMutation(economy: economy);
        mutation.RebuildForceTrust();
        state.RestoreDerivedTrust(mutation);
    }

    private static EconomyRulebook CourtRules => EconomyRulebook.Empty with
    {
        ImperialSealId = 5,
        ImperialCourt = new([3, 4], []),
        RegionCityIds = System.Collections.Immutable.ImmutableDictionary<int, int>.Empty.Add(99, 20)
    };

    private static WorldState Assets() => WorldState.Create(new(190, 2, 1), 42,
        [new(1, 0) { BaseTrust = 100, LeaderId = 1 },
         new(2, 0) { BaseTrust = 200, LeaderId = 2 }],
        [City(10, 1) with { Population = 1000000 }, City(20, 2)],
        [new(1, 1, 10, new(), false) { Status = OfficerStatus.Ruler },
         new(2, 2, 20, new(), false) { Status = OfficerStatus.Ruler },
         new(3, null, 10, new(), false) { Status = OfficerStatus.Free },
         new(4, null, 20, new(), false) { Status = OfficerStatus.Free }],
        treasures: [new(5, 1, TreasureDiscovery.Discovered, null)], corps: [new San9.Core.Domain.CorpsState(1, 1, 1, new(1000, 1000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 2, new(1000, 1000)) { IsRoot = true }]);

    [Fact]
    public void CityInputsTransferAndDormancyImmediatelyAffectBothOwners()
    {
        var state = Assets(); InitializeTrust(state, EconomyRulebook.Empty);
        var world = state.BeginMutation();
        Assert.Equal(200, world.Forces[1].Trust);
        world.Replace(world.Settlements[10] with { DraftPopulation = 1000000 });
        Assert.Equal(300, world.Forces[1].Trust);
        world.Replace(world.Settlements[10] with { PublicOrder = 550 });
        Assert.Equal(200, world.Forces[1].Trust);
        world.Replace(world.Settlements[10] with { OwnerId = 2 });
        Assert.Equal(100, world.Forces[1].Trust);
        Assert.Equal(300, world.Forces[2].Trust);
        world.Replace(world.Settlements[10] with { IsDormant = true });
        Assert.Equal(200, world.Forces[2].Trust);
        world.Replace(world.AllSettlements[10] with { IsDormant = false, Kind = SettlementKind.Facility });
        Assert.Equal(200, world.Forces[2].Trust);
        world.Replace(world.Settlements[10] with { Kind = SettlementKind.City });
        Assert.Equal(300, world.Forces[2].Trust);
        world.Replace(world.Forces[2] with { IsDormant = true });
        world.Replace(world.Settlements[10] with { PublicOrder = 800 });
        world.Replace(world.AllForces[2] with { IsDormant = false });
        Assert.Equal(400, world.Forces[2].Trust);
    }

    [Fact]
    public void SealDiscoveryTransferHolderActivationAndLeaderChangeMaintainTrust()
    {
        var state = Assets(); InitializeTrust(state, CourtRules);
        var world = state.BeginMutation(economy: CourtRules);
        Assert.Equal(400, world.Forces[1].Trust);
        world.Replace(world.Treasures[5] with { Discovery = TreasureDiscovery.Buried, HolderId = null });
        Assert.Equal(300, world.Forces[1].Trust);
        world.Replace(world.Treasures[5] with { Discovery = TreasureDiscovery.Discovered, HolderId = 2 });
        Assert.Equal(300, world.Forces[2].Trust);
        world.Replace(world.Officers[2] with { IsDormant = true });
        Assert.Equal(200, world.Forces[2].Trust);
        world.Replace(world.AllOfficers[2] with { IsDormant = false });
        Assert.Equal(300, world.Forces[2].Trust);
        world.Replace(world.Forces[2] with { LeaderId = null }); world.Replace(ResourceAccounts.Root(world, 2) with { LeaderId = null });
        Assert.Equal(200, world.Forces[2].Trust);
    }

    [Fact]
    public void EmperorTravelLocationSuccessionAndCourtEndMoveTheBonus()
    {
        var state = Assets(); InitializeTrust(state, CourtRules);
        var world = state.BeginMutation(economy: CourtRules);
        var travel = new PersistentTask(Guid.NewGuid(), "personnel-search", "player", 1, 3, 10,
            world.Clock, world.Clock, 0, 0) { Journey = new(10, null, 0) };
        world.SetSimulation(world.Simulation with { Tasks = [travel] });
        Assert.Equal(300, world.Forces[1].Trust);
        world.SetSimulation(world.Simulation with { Tasks = [] });
        Assert.Equal(400, world.Forces[1].Trust);
        world.Replace(world.Officers[3] with { SettlementId = null, RegionId = 99 });
        Assert.Equal(300, world.Forces[1].Trust);
        Assert.Equal(300, world.Forces[2].Trust);
        world.Replace(world.Officers[3] with { Status = OfficerStatus.Dead });
        Assert.Equal(300, world.Forces[2].Trust);
        world.Replace(world.Officers[4] with { SettlementId = 10 });
        Assert.Equal(400, world.Forces[1].Trust);
        Assert.Equal(200, world.Forces[2].Trust);
        world.Replace(world.Settlements[10] with { OwnerId = 2 });
        Assert.Equal(200, world.Forces[1].Trust);
        Assert.Equal(400, world.Forces[2].Trust);
        world.SetSimulation(world.Simulation with { ImperialCourtEnded = true });
        Assert.Equal(300, world.Forces[2].Trust);
    }

    [Fact]
    public void StaleForceWriteCannotRestoreOldTrustAndEventsReportNetChanges()
    {
        var state = Assets(); InitializeTrust(state, EconomyRulebook.Empty);
        var snapshot = state.CreateSnapshot();
        var world = state.BeginMutation(); var old = world.Forces[1];
        world.Replace(world.Settlements[10] with { PublicOrder = 300 });
        world.Replace(old with { RankId = 50 });
        Assert.Equal(100, world.Forces[1].Trust);
        world.Replace(world.Forces[1] with { BaseTrust = 120 });
        Assert.Equal(120, world.Forces[1].Trust);
        List<DomainEvent> events = []; world.MaintainDerivedState(events);
        Assert.Equal(new ForceTrustChangedEvent(world.Clock, 1, 200, 120),
            Assert.Single(events.OfType<ForceTrustChangedEvent>()));
        events.Clear(); world.MaintainDerivedState(events);
        Assert.Empty(events);
        // Discarding a candidate transaction leaves the published world and its derived values intact.
        Assert.Same(snapshot, state.CreateSnapshot());
        world.Replace(world.Forces[1] with { BaseTrust = 200 });
        world.Replace(world.Forces[1] with { BaseTrust = 120 });
        world.MaintainDerivedState(events);
        Assert.Empty(events);
    }

    [Fact]
    public void RestoreReconstructsTrustWithoutChangingRevisionSequenceOrEmittingGameplayEvents()
    {
        var rules = new SimulationRules { Economy = CourtRules };
        var engine = new SimulationEngine(Assets(), rules);
        var snapshot = engine.CaptureStableState();
        var stale = snapshot with { Forces = snapshot.Forces.Select(f => f with { Trust = 1 }).ToArray() };
        var restored = new SimulationEngine(WorldState.Restore(stale), rules);
        Assert.Equal(snapshot.Revision, restored.Snapshot.Revision);
        Assert.Equal(snapshot.Simulation.NextSequence, restored.Snapshot.Simulation.NextSequence);
        Assert.Equal(snapshot.StateHash, restored.Snapshot.StateHash);
        Assert.Empty(restored.InitialEvents);
    }
}

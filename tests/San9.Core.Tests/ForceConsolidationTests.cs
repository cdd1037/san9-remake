using San9.Tests;
using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class ForceConsolidationTests
{
    private static SettlementState City(int id, int owner, uint troops) =>
        new(id, owner, troops, 100, 100, 60, 500, 0, 100, 0, 100) { ConsumesFood = false };
    private static OfficerState Person(int id, int owner, int place, OfficerStatus status = OfficerStatus.Serving) =>
        new OfficerState(id, owner, place, new(50, 50, 50, 50), false) { Status = status }.WithLoyalty(owner, 60);
    private static WorldState World() => WorldState.Create(new(190, 1, 1), 17,
        [new(1, 100) { LeaderId = 11 }, new(2, 100) { LeaderId = 21, BaseTrust = 100 },
         new(3, 100) { LeaderId = 31, BaseTrust = 200 }],
        [City(1, 1, 50000), City(2, 1, 50000), City(3, 2, 10000), City(4, 3, 10000)],
        [Person(11, 1, 1, OfficerStatus.Ruler), Person(21, 2, 3, OfficerStatus.Ruler), Person(22, 2, 3),
         Person(31, 3, 4, OfficerStatus.Ruler), Person(32, 3, 4)],
        relations: [new(1, 2, 0), new(1, 3, 0), new(2, 3, 100)],
        controls: [new("player", 1, ControllerKind.Player), new("ai:2", 2, ControllerKind.Automatic), new("ai:3", 3, ControllerKind.Automatic)],
        annualAppearancesEnabled: false, naturalDeathsEnabled: false, freeOfficerMigrationEnabled: false, officeRequestsEnabled: false, corps: [new San9.Core.Domain.CorpsState(1, 1, 11, new(100, 100)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 21, new(200, 300)) { IsRoot = true }, new San9.Core.Domain.CorpsState(3, 3, 31, new(400, 500)) { IsRoot = true }]);

    private static SimulationRules Rules(TerritoryExits exits = TerritoryExits.All) => new()
    {
        Personnel = new(Enumerable.Range(1, 4).Select(i => new PersonPlace(i, new(5 * (i - 1) + 2, 2)))),
        Military = new(new(4, 1, [0, 0, 0, 0], [new(0, 1, null)],
            [new(9, 1, 0, 10000, 5, new(1, 1), TravelMode.Land)],
            Enumerable.Range(1, 4).Select(i => new BattlefieldPlace(i, new(5 * (i - 1) + 2, 2), new(1, 1))).ToImmutableArray())
            { RegionIds = [1, 2, 3, 4], TerritoryExits = [exits, exits, exits, exits] })
    };

    private static WorldMutation Pending(WorldState world)
    {
        var mutation = world.BeginMutation();
        mutation.SetSimulation(mutation.Simulation with { ForceConsolidationPending = true });
        return mutation;
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    public void HostProhibitionAndScenarioEligibilityRemainIndependent(bool scenarioEnabled, bool prohibited, bool merges)
    {
        var world = World().BeginMutation(companion: new() { DisableComputerForceConsolidation = prohibited });
        world.SetSimulation(world.Simulation with { ForceConsolidationEnabled = scenarioEnabled, ForceConsolidationPending = true });
        List<DomainEvent> events = [];
        ForceConsolidation.Apply(world, Rules(), events);
        Assert.Equal(merges, events.OfType<ForcesConsolidatedEvent>().Any());
        Assert.Equal(scenarioEnabled, world.Simulation.ForceConsolidationEnabled);
        Assert.Equal(!merges, world.Simulation.ForceConsolidationPending);
        if (!merges)
        {
            Assert.Empty(events);
            Assert.Empty(world.Simulation.RandomStreams);
            Assert.False(world.Forces[2].IsEliminated);
            Assert.False(world.Forces[3].IsEliminated);
            Assert.Equal(10000u, world.Settlements[3].Soldiers);
        }
    }

    [Fact]
    public void MergeReturnsForeignEnvoyBeforeDailyCommandsAcrossRestore()
    {
        var world = World();
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Corps[1] with { Treasury = new(1000,100) });
        mutation.SetClock(new(190, 12, 21));
        world.Commit(mutation);
        var rules = new SimulationRules
        {
            Military = Rules().Military,
            Personnel = new([new(1, new(0, 0)), new(2, new(5, 0)),
                new(3, new(600, 0)), new(4, new(605, 0))])
        };
        var engine = new SimulationEngine(world, rules);
        Assert.True(engine.Execute(new("player"), new GiftCommand(Guid.NewGuid(), 1, 0, 11, 2,
            new(GiftKind.Money, 1000, null))).Submission.Accepted);
        mutation = Pending(world);
        world.Commit(mutation);
        var restored = new SimulationEngine(WorldState.Restore(engine.CaptureStableState()), rules);

        AdvanceXunResult Advance(SimulationEngine current)
        {
            foreach (var control in current.Snapshot.Simulation.Controls) current.EndControl(new(control.ControllerId));
            return current.AdvanceXun();
        }

        var events = Advance(engine).Events;
        EventAssert.Equal(events, Advance(restored).Events);
        StateAssert.Equal(engine.Snapshot, restored.Snapshot);
        Assert.Equal(2, Assert.Single(events.OfType<ForcesConsolidatedEvent>()).AbsorbedId);
        var returning = Assert.Single(engine.Snapshot.Simulation.Tasks);
        Assert.False(returning.Journey!.ReturnLeg!.AfterDailyCommands);
        Assert.True(returning.Journey.ReturnLeg.DiplomaticEnvoy);
        Assert.Equal(10, returning.Journey.ReturnLeg.CountdownDays);
        Assert.Equal(new GameClock(191, 1, 10), returning.Due);
        Assert.DoesNotContain(events, e => e is GiftResolvedEvent);
        restored = new SimulationEngine(WorldState.Restore(engine.CaptureStableState()), rules);
        var completion = Advance(engine).Events;
        EventAssert.Equal(completion, Advance(restored).Events);
        Assert.Equal(new GameClock(191, 1, 10), Assert.Single(completion.OfType<PersonnelReturnedEvent>()).Clock);
        Assert.Empty(engine.Snapshot.Simulation.Tasks);
        StateAssert.Equal(engine.Snapshot, restored.Snapshot);
    }

    [Fact]
    public void MergeWithFullSuccessorInventoryPreservesReturningFieldArmamentsInReserve()
    {
        var world = World(); var mutation = Pending(world);
        mutation.Replace(mutation.Settlements[4] with { Soldiers = 12000 });
        mutation.Replace(mutation.Forces[3] with { ArmamentInventory = ImmutableDictionary<string, uint>.Empty.Add("spear", uint.MaxValue) });
        mutation.SetSimulation(mutation.Simulation with { NextUnitId = 51 });
        mutation.AddUnit(new(50, 2, new(12, 2), 1000)
        {
            FormationId = 9, ServicePlaceId = 3, CommanderId = 22,
            MemberPositions = ImmutableDictionary<int, FormationPosition>.Empty.Add(22, new(0, BattleRank.Front)),
            Detachments = ImmutableDictionary<int, DetachmentState>.Empty.Add(22,
                new DetachmentState(1, 1, 1000) { Weapon = new("spear", 600, 1000, 0) })
        });
        mutation.Replace(mutation.Officers[22] with { SettlementId = null, UnitId = 50 });
        List<DomainEvent> events = [];
        ForceConsolidation.Apply(mutation, Rules(), events);
        Assert.Equal(3, mutation.Units[50].OwnerId);

        mutation.Replace(mutation.Officers[22] with { UnitId = null, SettlementId = 3 });
        Assert.Equal(uint.MaxValue, mutation.Forces[3].ArmamentInventory!["spear"]);
        Assert.Equal(600ul, mutation.Forces[3].ArmamentReserve!["spear"]);
        mutation.RemoveUnit(50);
        mutation.SetSimulation(mutation.Simulation with { Boundary = SimulationBoundary.Stable });
        world.Commit(mutation);
        var restored = WorldState.Restore(world.CreateSnapshot());
        var resumed = restored.BeginMutation();
        Assert.Equal(600ul, resumed.Forces[3].ArmamentReserve!["spear"]);
        resumed.Replace(resumed.Forces[3] with { ArmamentInventory = ImmutableDictionary<string, uint>.Empty.Add("spear", uint.MaxValue - 400) });
        ForceLifecycle.RetryArmamentTransfers(resumed, events);
        Assert.Equal(uint.MaxValue, resumed.Forces[3].ArmamentInventory!["spear"]);
        Assert.Equal(200ul, resumed.Forces[3].ArmamentReserve!["spear"]);
    }

    [Fact]
    public void MergeKeepsFieldDetachmentArmamentsAndTransfersStockWithoutDuplication()
    {
        var mutation = Pending(World());
        mutation.Replace(mutation.Forces[2] with { ArmamentInventory = ImmutableDictionary<string, uint>.Empty.Add("spear", 300) });
        mutation.Replace(mutation.Settlements[4] with { Soldiers = 12000 });
        mutation.SetSimulation(mutation.Simulation with { NextUnitId = 51 });
        mutation.AddUnit(new(50, 2, new(12, 2), 1000)
        {
            FormationId = 9, ServicePlaceId = 3, CommanderId = 22,
            MemberPositions = ImmutableDictionary<int, FormationPosition>.Empty.Add(22, new(0, BattleRank.Front)),
            Detachments = ImmutableDictionary<int, DetachmentState>.Empty.Add(22,
                new DetachmentState(1, 1, 1000) { Weapon = new("spear", 600, 1000, 0) })
        });
        mutation.Replace(mutation.Officers[22] with { SettlementId = null, UnitId = 50 });
        List<DomainEvent> events = [];

        ForceConsolidation.Apply(mutation, Rules(), events);

        Assert.True(mutation.Forces[2].IsEliminated);
        Assert.Equal(3, mutation.Units[50].OwnerId);
        Assert.Equal(600u, mutation.Units[50].Detachments[22].Weapon!.RemainingSets);
        Assert.Equal(300u, mutation.Forces[3].ArmamentInventory!["spear"]);
        Assert.Null(mutation.Forces[2].ArmamentInventory);
        Assert.Equal(3, mutation.Officers[22].OwnerId);
    }

    [Fact]
    public void MergeTransfersArmamentStockAndMigrationCreditToTheSurvivor()
    {
        var mutation = Pending(World());
        mutation.Replace(mutation.Forces[2] with { ArmamentInventory = ImmutableDictionary<string, uint>.Empty.Add("spear", 900), ArmamentMigrationCredit = 40 });
        mutation.Replace(mutation.Forces[3] with { ArmamentInventory = ImmutableDictionary<string, uint>.Empty.Add("spear", 100).Add("armor", 300), ArmamentMigrationCredit = 60 });
        List<DomainEvent> events = [];

        ForceConsolidation.Apply(mutation, Rules(), events);

        Assert.Null(mutation.Forces[2].ArmamentInventory);
        Assert.Null(mutation.Forces[2].ArmamentMigrationCredit);
        Assert.Equal(1000u, mutation.Forces[3].ArmamentInventory!["spear"]);
        Assert.Equal(300u, mutation.Forces[3].ArmamentInventory!["armor"]);
        Assert.Equal(100, mutation.Forces[3].ArmamentMigrationCredit);
        Assert.True(Assert.Single(events.OfType<ArmamentAssetsTransferredEvent>()).Transferred);
    }

    [Fact]
    public void MergeRetainsAssetsIfTheSurvivorCannotReceiveThemWithoutOverflow()
    {
        var mutation = Pending(World());
        mutation.Replace(mutation.Forces[2] with { ArmamentInventory = ImmutableDictionary<string, uint>.Empty.Add("spear", 1), ArmamentMigrationCredit = 40 });
        mutation.Replace(mutation.Forces[3] with { ArmamentInventory = ImmutableDictionary<string, uint>.Empty.Add("spear", uint.MaxValue), ArmamentMigrationCredit = 60 });
        List<DomainEvent> events = [];

        ForceConsolidation.Apply(mutation, Rules(), events);

        Assert.Equal(1u, mutation.Forces[2].ArmamentInventory!["spear"]);
        Assert.Equal(40, mutation.Forces[2].ArmamentMigrationCredit);
        Assert.Equal(uint.MaxValue, mutation.Forces[3].ArmamentInventory!["spear"]);
        Assert.Equal(60, mutation.Forces[3].ArmamentMigrationCredit);
        Assert.False(Assert.Single(events.OfType<ArmamentAssetsTransferredEvent>()).Transferred);
        Assert.Equal(3, mutation.Forces[2].ArmamentEscrowSuccessorId);

        List<DomainEvent> retry = [];
        ForceLifecycle.RetryArmamentTransfers(mutation, retry);
        Assert.Empty(retry);
        mutation.Replace(mutation.Forces[3] with { ArmamentInventory = ImmutableDictionary<string, uint>.Empty.Add("spear", uint.MaxValue - 1) });
        ForceLifecycle.RetryArmamentTransfers(mutation, retry);
        Assert.True(Assert.Single(retry.OfType<ArmamentAssetsTransferredEvent>()).Transferred);
        Assert.Equal(uint.MaxValue, mutation.Forces[3].ArmamentInventory!["spear"]);
        Assert.Equal(100, mutation.Forces[3].ArmamentMigrationCredit);
        Assert.Null(mutation.Forces[2].ArmamentInventory);
        Assert.Null(mutation.Forces[2].ArmamentEscrowSuccessorId);
    }

    [Fact]
    public void DeferredArmamentAssetsFollowTheSuccessorIfThatForceAlsoRetires()
    {
        var mutation = Pending(World());
        mutation.Replace(mutation.Forces[2] with { ArmamentInventory = ImmutableDictionary<string, uint>.Empty.Add("spear", 1) });
        mutation.Replace(mutation.Forces[3] with { ArmamentInventory = ImmutableDictionary<string, uint>.Empty.Add("spear", uint.MaxValue) });
        List<DomainEvent> events = [];
        ForceConsolidation.Apply(mutation, Rules(), events);
        Assert.Equal(3, mutation.Forces[2].ArmamentEscrowSuccessorId);

        ForceLifecycle.Retire(mutation, 3, events, 1);
        Assert.Equal(1, mutation.Forces[2].ArmamentEscrowSuccessorId);
        Assert.Equal(uint.MaxValue, mutation.Forces[1].ArmamentInventory!["spear"]);
        mutation.Replace(mutation.Forces[1] with { ArmamentInventory = ImmutableDictionary<string, uint>.Empty.Add("spear", uint.MaxValue - 1) });
        ForceLifecycle.RetryArmamentTransfers(mutation, events);
        Assert.Null(mutation.Forces[2].ArmamentInventory);
        Assert.Equal(uint.MaxValue, mutation.Forces[1].ArmamentInventory!["spear"]);
    }

    [Fact]
    public void MergeCancelsAbsorbedForcesArmamentProductionImmediately()
    {
        var mutation = Pending(World());
        mutation.Replace(mutation.Forces[2] with { ArmamentProduction = new("iron-spear", 3, 1, 20) });
        List<DomainEvent> events = [];

        ForceConsolidation.Apply(mutation, Rules(), events);

        Assert.True(mutation.Forces[2].IsEliminated);
        Assert.Null(mutation.Forces[2].ArmamentProduction);
        var cancelled = Assert.Single(events.OfType<ArmamentProductionCancelledEvent>());
        Assert.Equal((2, 3, "iron-spear", 1u),
            (cancelled.ForceId, cancelled.SettlementId, cancelled.DefinitionId, cancelled.Batches));
    }

    [Fact]
    public void SuccessfulMergeMovesAllPeopleAssetsRewardsAndConsumesOnce()
    {
        var world = World();
        var mutation = Pending(world);
        mutation.Replace(mutation.Forces[2] with { CountryTitleId = 900 });
        mutation.Replace(mutation.Forces[3] with { CountryTitleId = 500 });
        List<DomainEvent> events = [];
        ForceConsolidation.Apply(mutation, Rules(), events);
        Assert.Null(mutation.Forces[2].CountryTitleId);
        Assert.Equal(500, mutation.Forces[3].CountryTitleId);
        var result = Assert.Single(events.OfType<ForcesConsolidatedEvent>());
        Assert.Equal((3, 2, 200L, 200u, 300L, 300u),
            (result.SurvivorId, result.AbsorbedId, result.MoneyRemoved, result.MoneyReceived, result.FoodRemoved, result.FoodReceived));
        Assert.False(mutation.Simulation.ForceConsolidationPending);
        Assert.True(mutation.Forces[2].IsEliminated);
        Assert.All(mutation.AllOfficers.Values, p => Assert.False(p.ForceLoyalties.ContainsKey(2)));
        Assert.Equal(260, mutation.Forces[3].BaseTrust);
        Assert.Equal(600u, mutation.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 3).Treasury.Money);
        Assert.Equal(800u, mutation.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 3).Treasury.Food);
        Assert.Equal(100, mutation.Officers[21].Loyalty);
        Assert.Equal(85, mutation.Officers[22].Loyalty);
        Assert.Equal(20000u, mutation.Settlements[3].Soldiers);
        Assert.Equal(20000u, mutation.Settlements[4].Soldiers);
        Assert.Equal(100, mutation.Settlements[3].Morale);
        Assert.DoesNotContain(mutation.Officers.Values, p => p.OwnerId == 2);
        world.Commit(mutation);
        var next = world.BeginMutation();
        ForceConsolidation.Apply(next, Rules(), events);
        Assert.Single(events.OfType<ForcesConsolidatedEvent>());
        WorldInvariants.Validate(next);
    }

    [Theory]
    [InlineData(49999, true)]
    [InlineData(50000, false)]
    [InlineData(50001, false)]
    public void BothCandidatesMustBeStrictlyBelowHalfPlayerSoldiers(uint soldiers, bool merges)
    {
        var world = Pending(World());
        world.Replace(world.Settlements[3] with { Soldiers = soldiers });
        world.Replace(world.Settlements[4] with { Soldiers = soldiers });
        List<DomainEvent> events = [];
        ForceConsolidation.Apply(world, Rules(), events);
        Assert.Equal(merges, events.OfType<ForcesConsolidatedEvent>().Any());
        Assert.Equal(!merges, world.Simulation.ForceConsolidationPending);
    }

    [Theory]
    [InlineData(false, false, 3)]
    [InlineData(true, false, 2)]
    [InlineData(false, true, 3)]
    [InlineData(true, true, 0)]
    public void LeadershipPreferenceOverridesStrengthAndProtectsTwoPreferredRulers(bool first, bool second, int survivor)
    {
        var world = Pending(World());
        world.Replace(world.Officers[21] with { PrefersConsolidationLeadership = first });
        world.Replace(world.Officers[31] with { PrefersConsolidationLeadership = second });
        List<DomainEvent> events = [];
        ForceConsolidation.Apply(world, Rules(), events);
        Assert.Equal(survivor, events.OfType<ForcesConsolidatedEvent>().SingleOrDefault()?.SurvivorId ?? 0);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("untriggered")]
    [InlineData("barbarian")]
    [InlineData("alliance")]
    [InlineData("one-player-city")]
    [InlineData("no-neighbors")]
    public void IneligibleWorldDoesNotRollOrMerge(string reason)
    {
        var world = Pending(World());
        if (reason == "disabled") world.SetSimulation(world.Simulation with { ForceConsolidationEnabled = false });
        if (reason == "untriggered") world.SetSimulation(world.Simulation with { ForceConsolidationPending = false });
        if (reason == "barbarian") world.Replace(world.Forces[2] with { IsBarbarian = true });
        if (reason == "alliance") world.SetSimulation(world.Simulation with { Alliance = new(1, 2, [2, 3], 1) });
        if (reason == "one-player-city") world.Replace(world.Settlements[2] with { Kind = SettlementKind.Facility });
        List<DomainEvent> events = [];
        ForceConsolidation.Apply(world, Rules(reason == "no-neighbors" ? TerritoryExits.None : TerritoryExits.All), events);
        Assert.Empty(events);
        Assert.Empty(world.Simulation.RandomStreams);
    }

    [Fact]
    public void FailedProbabilityRetainsTriggerAndRestoredRetryMatches()
    {
        var world = World();
        _ = new SimulationEngine(world, Rules());
        var mutation = Pending(world);
        mutation.Replace(new ForceRelation(2, 3, 0));
        List<DomainEvent> failed = [];
        ForceConsolidation.Apply(mutation, Rules(), failed);
        Assert.Empty(failed);
        Assert.True(mutation.Simulation.ForceConsolidationPending);
        Assert.Contains(mutation.Simulation.RandomStreams, r => r.Name == "event-force-consolidation");
        world.Commit(mutation);
        var restored = WorldState.Restore(world.CreateSnapshot());
        string Resume(WorldState current)
        {
            var next = current.BeginMutation();
            next.Replace(new ForceRelation(2, 3, 100));
            List<DomainEvent> events = [];
            ForceConsolidation.Apply(next, Rules(), events);
            current.Commit(next);
            Assert.Single(events.OfType<ForcesConsolidatedEvent>());
            return CanonicalJson.Text(events);
        }
        Assert.Equal(Resume(world), Resume(restored));
        StateAssert.Equal(world.CreateSnapshot(), restored.CreateSnapshot());
    }

    [Fact]
    public void CityAcquisitionSetsTriggerButOtherMutationsDoNot()
    {
        var mutation = World().BeginMutation();
        mutation.Replace(mutation.Settlements[3] with { Soldiers = 1 });
        Assert.False(mutation.Simulation.ForceConsolidationPending);
        mutation.Replace(mutation.Settlements[3] with { OwnerId = 3 });
        Assert.False(mutation.Simulation.ForceConsolidationPending);
        mutation.Replace(mutation.Settlements[3] with { OwnerId = 1 });
        Assert.True(mutation.Simulation.ForceConsolidationPending);
    }

    [Theory]
    [InlineData(TerritoryExits.West, true)]
    [InlineData(TerritoryExits.East, false)]
    public void NeighborsKeepTheirDirection(TerritoryExits exits, bool merges)
    {
        var world = Pending(World());
        List<DomainEvent> events = [];
        ForceConsolidation.Apply(world, Rules(exits), events);
        Assert.Equal(merges, events.OfType<ForcesConsolidatedEvent>().Any());
    }

    [Fact]
    public void CaptivesKeepThirdPartyDetentionAndMutualCaptivesRecover()
    {
        var initial = World().CreateSnapshot();
        var world = WorldState.Create(initial.Clock, 17, initial.Forces, initial.Settlements,
            initial.Officers.Concat([Person(23, 2, 1, OfficerStatus.Captive), Person(24, 2, 4, OfficerStatus.Captive),
                Person(33, 3, 3, OfficerStatus.Captive), Person(12, 1, 3, OfficerStatus.Captive)]),
            relations: initial.Relations, controls: initial.Simulation.Controls, corps: initial.Corps);
        var mutation = Pending(world);
        List<DomainEvent> events = [];
        ForceConsolidation.Apply(mutation, Rules(), events);
        Assert.Equal(3, mutation.Officers[23].OwnerId);
        Assert.Equal(OfficerStatus.Captive, mutation.Officers[23].Status);
        Assert.Equal(1, mutation.Officers[23].SettlementId);
        Assert.Equal(85, mutation.Officers[23].Loyalty);
        Assert.True(mutation.Officers[24].IsServing);
        Assert.True(mutation.Officers[33].IsServing);
        Assert.Equal(OfficerStatus.Captive, mutation.Officers[12].Status);
        Assert.Equal(1, mutation.Officers[12].OwnerId);
        Assert.Equal(2, events.OfType<PrisonerRecoveredEvent>().Count());
        world.Commit(mutation);
    }

    [Fact]
    public void ChildTreasuriesUnitsAndResourceSaturationAreAccountedFor()
    {
        var initial = World().CreateSnapshot();
        var world = WorldState.Create(initial.Clock, 17,
            initial.Forces,
            initial.Settlements.Select(s => s.Id == 3 ? s with { CorpsId = 21 } : s.Id == 4 ? s with { CorpsId = 30, SoldierCapacity = 15000 } : s),
            initial.Officers.Concat([Person(23, 2, 3) with { SettlementId = null, UnitId = 50 }]),
            units: [new(50, 2, new(12, 2), 1000) { FormationId = 9, ServicePlaceId = 3, CorpsId = 21, Morale = 70,
                CommanderId = 23, CargoMoney = 99, CargoFood = 88,
                Order = new(MilitaryOrderKind.Move, new(7, 2), null),
                MemberPositions = San9.Tests.FormationTestData.Members(ImmutableDictionary<int, BattleRank>.Empty.Add(23, BattleRank.Front)) }],
            corps: [..initial.Corps.Where(c => c.OwnerId == 1), new(20, 2, 21, new(200, 300)) { IsRoot = true }, new(21, 2, 22, new(2000, 3000)), new(30, 3, 31, new(TreasuryRules.Capacity - 100, 500)) { IsRoot = true }],
            relations: initial.Relations, controls: initial.Simulation.Controls);
        var mutation = Pending(world);
        // Keep force 3 as survivor despite force 2's additional field troops.
        mutation.Replace(mutation.Officers[31] with { PrefersConsolidationLeadership = true });
        List<DomainEvent> events = [];
        ForceConsolidation.Apply(mutation, Rules(), events);
        var result = Assert.Single(events.OfType<ForcesConsolidatedEvent>());
        Assert.Equal(2200, result.MoneyRemoved);
        Assert.Equal(100u, result.MoneyReceived);
        Assert.Equal(3300, result.FoodRemoved);
        Assert.Equal(3300u, result.FoodReceived);
        Assert.DoesNotContain(mutation.Corps.Values, c => c.OwnerId == 2);
        Assert.Equal(30, mutation.Settlements[3].CorpsId);
        Assert.Equal(15000u, mutation.Settlements[4].Soldiers);
        var unit = mutation.Units[50];
        Assert.Equal(3, unit.OwnerId);
        Assert.Equal(30, unit.CorpsId);
        Assert.Equal(23, unit.CommanderId);
        Assert.Equal(1000u, unit.Soldiers);
        Assert.Equal(99u, unit.CargoMoney);
        Assert.Equal(88u, unit.CargoFood);
        Assert.Equal(100, unit.Morale);
        Assert.Equal(new(MilitaryOrderKind.Return, null, 3), unit.Order);
        Assert.Contains(events.OfType<ResourceChangedEvent>(), e => e.SaturationLoss == 2100);
        Assert.Contains(events.OfType<ResourceChangedEvent>(), e => e.SaturationLoss == 5000);
        world.Commit(mutation);
    }

    [Fact]
    public void MergeSettlesCarriedGiftsSeparatelyFromTreasuryAndCancelsBothDirections()
    {
        var world = World();
        var funded = world.BeginMutation();
        foreach (var corps in funded.Corps.Values.Where(c => c.OwnerId is 2 or 3).ToArray())
            funded.Replace(corps with { Treasury = corps.Treasury with { Money = 2000 } });
        world.Commit(funded);
        var engine = new SimulationEngine(world, Rules());
        uint sourceMoney = engine.Snapshot.Corps.Single(f => f.IsRoot && f.OwnerId == 2).Treasury.Money;
        uint targetMoney = engine.Snapshot.Corps.Single(f => f.IsRoot && f.OwnerId == 3).Treasury.Money;
        engine.EndControl(new("player"));
        var outgoing = new GiftCommand(Guid.NewGuid(), 2, 0, 22, 1, new(GiftKind.Money, 1000, null));
        Assert.True(engine.Execute(new("ai:2"), outgoing).Submission.Accepted);
        engine.EndControl(new("ai:2"));
        var incoming = new GiftCommand(Guid.NewGuid(), 3, 1, 32, 2, new(GiftKind.Money, 1000, null));
        Assert.True(engine.Execute(new("ai:3"), incoming).Submission.Accepted);
        var mutation = Pending(world);
        List<DomainEvent> events = [];
        ForceConsolidation.Apply(mutation, Rules(), events);
        Assert.Equal(sourceMoney - 1000, Assert.Single(events.OfType<ForcesConsolidatedEvent>()).MoneyRemoved);
        Assert.Equal(sourceMoney + targetMoney - 2000, mutation.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 3).Treasury.Money);
        Assert.Equal(new ReturnCargo(ResourceKind.Money, 1000), Assert.Single(mutation.Simulation.Tasks).ReturnCargo);
        Assert.Equal(outgoing.CommandId, Assert.Single(events.OfType<ReturnCargoLostEvent>()).TaskId);
        Assert.Equal(new[] { outgoing.CommandId, incoming.CommandId }.Order(),
            events.OfType<TaskCancelledEvent>().Select(e => e.TaskId).Order());
        Assert.DoesNotContain(mutation.Simulation.Tasks, t => t.Gift is not null || t.ForceId == 2);
        Assert.DoesNotContain(events, e => e is GiftResolvedEvent);
        world.Commit(mutation);
    }
}

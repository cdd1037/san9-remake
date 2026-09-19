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
        [new(1, 100, 100, 100) { LeaderId = 11 }, new(2, 200, 300, 100) { LeaderId = 21, BaseTrust = 100 },
         new(3, 400, 500, 100) { LeaderId = 31, BaseTrust = 200 }],
        [City(1, 1, 50000), City(2, 1, 50000), City(3, 2, 10000), City(4, 3, 10000)],
        [Person(11, 1, 1, OfficerStatus.Ruler), Person(21, 2, 3, OfficerStatus.Ruler), Person(22, 2, 3),
         Person(31, 3, 4, OfficerStatus.Ruler), Person(32, 3, 4)],
        relations: [new(1, 2, 0), new(1, 3, 0), new(2, 3, 100)],
        controls: [new("player", 1, ControllerKind.Player), new("ai:2", 2, ControllerKind.Automatic), new("ai:3", 3, ControllerKind.Automatic)],
        annualAppearancesEnabled: false, naturalDeathsEnabled: false, freeOfficerMigrationEnabled: false, officeRequestsEnabled: false);

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
        Assert.Equal(260, mutation.Forces[3].BaseTrust);
        Assert.Equal(600u, mutation.Forces[3].Money);
        Assert.Equal(800u, mutation.Forces[3].Food);
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
        Assert.Equal(world.CreateSnapshot().StateHash, restored.CreateSnapshot().StateHash);
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
            relations: initial.Relations, controls: initial.Simulation.Controls);
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
            initial.Forces.Select(f => f.Id == 3 ? f with { Money = 999900 } : f),
            initial.Settlements.Select(s => s.Id == 3 ? s with { CorpsId = 21 } : s.Id == 4 ? s with { CorpsId = 30, SoldierCapacity = 15000 } : s),
            initial.Officers.Concat([Person(23, 2, 3) with { SettlementId = null, UnitId = 50 }]),
            units: [new(50, 2, new(12, 2), 1000) { FormationId = 9, ServicePlaceId = 3, CorpsId = 21, Morale = 70,
                CommanderId = 23, CargoMoney = 99, CargoFood = 88,
                MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(23, BattleRank.Front) }],
            corps: [new(20, 2, 21, null), new(21, 2, 22, new(2000, 3000)), new(30, 3, 31, null)],
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
        Assert.Contains(events.OfType<ResourceChangedEvent>(), e => e.SaturationLoss == 2100);
        Assert.Contains(events.OfType<ResourceChangedEvent>(), e => e.SaturationLoss == 5000);
        world.Commit(mutation);
    }

    [Fact]
    public void MergeRefundsReservedGiftsAndCancelsBothDirectionsBeforeTreasuryTransfer()
    {
        var world = World();
        var engine = new SimulationEngine(world, Rules());
        uint sourceMoney = engine.Snapshot.Forces.Single(f => f.Id == 2).Money;
        uint targetMoney = engine.Snapshot.Forces.Single(f => f.Id == 3).Money;
        engine.EndControl(new("player"));
        var outgoing = new GiftCommand(Guid.NewGuid(), 2, 0, 22, 1, new(GiftKind.Money, 100, null));
        Assert.True(engine.Execute(new("ai:2"), outgoing).Submission.Accepted);
        engine.EndControl(new("ai:2"));
        var incoming = new GiftCommand(Guid.NewGuid(), 3, 1, 32, 2, new(GiftKind.Money, 100, null));
        Assert.True(engine.Execute(new("ai:3"), incoming).Submission.Accepted);
        var mutation = Pending(world);
        List<DomainEvent> events = [];
        ForceConsolidation.Apply(mutation, Rules(), events);
        Assert.Equal(sourceMoney, Assert.Single(events.OfType<ForcesConsolidatedEvent>()).MoneyRemoved);
        Assert.Equal(sourceMoney + targetMoney, mutation.Forces[3].Money);
        Assert.Equal(new[] { outgoing.CommandId, incoming.CommandId }.Order(),
            events.OfType<TaskCancelledEvent>().Select(e => e.TaskId).Order());
        Assert.DoesNotContain(mutation.Simulation.Tasks, t => t.Gift is not null || t.ForceId == 2);
        Assert.DoesNotContain(events, e => e is GiftResolvedEvent);
        world.Commit(mutation);
    }
}

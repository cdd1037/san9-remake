using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class MilitarySupplyTests
{
    [Theory]
    [InlineData(100, 0, 0)]
    [InlineData(100, 1, 1)]
    [InlineData(100, 50, 5)]
    [InlineData(100, 51, 6)]
    [InlineData(100, 100, 10)]
    [InlineData(long.MaxValue, long.MaxValue, 10)]
    public void LossUsesAccountShortageRatioWithoutOverflow(long demand, long missing, int expected) =>
        Assert.Equal(expected, MilitarySupply.MoraleLoss(demand, missing));

    private static SimulationRules Rules() => new()
    {
        Military = new(new(6, 1, [0, 0, 0, 0, 0, 0], [new(0, 1, null)],
            [new(10, 3, 0, 10000, 1, new(1, 1), TravelMode.Land)],
            [new(1, new(2, 2), new(1, 1)), new(2, new(27, 2), new(1, 1))])),
        Personnel = new([new(1, new(2, 2)), new(2, new(27, 2))])
    };

    private static WorldState World(uint food, int morale = 70) => WorldState.Create(new(190, 1, 1), 42,
        [new(1, 1000, food, 0) { LeaderId = 11 }, new(2, 1000, 1000, 0) { LeaderId = 22 }],
        [new(1, 1, 1000, 100, 100, 90, 0, 0, 100, 0, 100), new(2, 2, 1000, 100, 100, 90, 0, 0, 100, 0, 100)],
        [new(11, 1, null, new(70, 70, 70, 70), false) { UnitId = 0, Status = OfficerStatus.Ruler },
         new(22, 2, 2, new(70, 70, 70, 70), false) { Status = OfficerStatus.Ruler }],
        units: [new(0, 1, new(7, 2), 500) { Morale = morale, FormationId = 10, ServicePlaceId = 1,
            MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(11, BattleRank.Front) }]);

    [Fact]
    public void PartialSupplyAffectsOnlyTheDebitedAccountAndDoesNotDrawRandom()
    {
        var world = World(2).BeginMutation();
        List<DomainEvent> events = [];
        var shortages = EconomicCycle.Apply(ResolutionStage.BeforeDailyCommands, world, EconomyRulebook.Empty, events);
        MilitarySupply.Apply(world, shortages, Rules(), events);
        Assert.Equal(65, world.Units[0].Morale);
        Assert.Equal(85, world.Settlements[1].Morale);
        Assert.Equal(90, world.Settlements[2].Morale);
        Assert.Equal(0u, world.Forces[1].Food);
        Assert.Equal(998u, world.Forces[2].Food);
        Assert.Empty(world.Simulation.RandomStreams);
    }

    [Fact]
    public void IndependentCorpsFoodCannotCoverAnotherAccountShortage()
    {
        var world = World(0).BeginMutation();
        world.Replace(world.Forces[2] with { LeaderId = null });
        world.Replace(world.Officers[22] with { OwnerId = 1, Status = OfficerStatus.Governor });
        world.AddCorps(new(10, 1, 22, new(1000, 1000)));
        world.Replace(world.Settlements[2] with { OwnerId = 1, CorpsId = 10 });
        List<DomainEvent> events = [];
        var shortages = EconomicCycle.Apply(ResolutionStage.BeforeDailyCommands, world, EconomyRulebook.Empty, events);
        MilitarySupply.Apply(world, shortages, Rules(), events);
        Assert.Equal(60, world.Units[0].Morale);
        Assert.Equal(80, world.Settlements[1].Morale);
        Assert.Equal(90, world.Settlements[2].Morale);
        Assert.Equal(998u, world.Corps[10].Treasury!.Food);
        WorldInvariants.Validate(world);
    }

    [Fact]
    public void StarvationTerminatesOnceAndClearsOtherUnitsTargets()
    {
        var world = World(0, 5);
        var mutation = world.BeginMutation();
        mutation.AddUnit(new(1, 2, new(15, 2), 500) { FormationId = 10, Morale = 90,
            Order = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 0 } });
        mutation.SetSimulation(mutation.Simulation with { NextUnitId = 2 });
        world.Commit(mutation);
        var engine = new SimulationEngine(world, Rules());
        var result = engine.AdvanceXun();
        Assert.Single(result.Events.OfType<UnitTerminatedEvent>());
        Assert.DoesNotContain(engine.Snapshot.Units, u => u.Id == 0);
        Assert.Null(Assert.Single(engine.Snapshot.Units).Order);
        Assert.Equal(1, engine.Snapshot.Officers.Single(p => p.Id == 11).SettlementId);
        Assert.Null(engine.Snapshot.Officers.Single(p => p.Id == 11).UnitId);
        Assert.Empty(engine.AdvanceXun().Events.OfType<UnitTerminatedEvent>());
    }

    [Fact]
    public void DeliveredFoodStopsFurtherSupplyMoraleLoss()
    {
        var mutation = World(2).BeginMutation();
        List<DomainEvent> events = [];
        var shortages = EconomicCycle.Apply(ResolutionStage.BeforeDailyCommands, mutation, EconomyRulebook.Empty, events);
        MilitarySupply.Apply(mutation, shortages, Rules(), events);
        mutation.AddUnit(new(1, 1, new(3, 2), 100) { FormationId = 10, Morale = 70, CargoFood = 100,
            Order = new(MilitaryOrderKind.Transport, null, 1) });
        mutation.SetSimulation(mutation.Simulation with { NextUnitId = 2 });
        MilitaryMovement.AdvanceDay(mutation, Rules().Military, new([]), events);
        Assert.Single(events.OfType<UnitAbsorbedEvent>());
        events.Clear();
        shortages = EconomicCycle.Apply(ResolutionStage.BeforeDailyCommands, mutation, EconomyRulebook.Empty, events);
        MilitarySupply.Apply(mutation, shortages, Rules(), events);
        Assert.Empty(events.OfType<SupplyMoraleLostEvent>());
        Assert.Equal(65, mutation.Units[0].Morale);
    }
}

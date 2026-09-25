using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class RedCliffsTests
{
    private static RedCliffsDefinition Definition => new(12, 2, [11], 3, [3, 4], 20, 1, 30000, 90, 50, 15, 15, new("red-cliffs"));
    private static readonly PersonPlace[] Places = [new(1, new(2, 2)), new(2, new(37, 2)), new(3, new(7, 2)), new(4, new(22, 2)), new(5, new(37, 12))];
    private static MilitaryRulebook Military => new(new(8, 3,
        Enumerable.Range(0, 24).Select(i => i == 11 ? 0 : 1).ToImmutableArray(),
        [new(0, 1, 1), new TerrainPassage(1, 1, 1) { IsWater = true }],
        [new(1, 1, 0, 100000, 5, new(1, 1), TravelMode.Water)],
        Places.Select(p => new BattlefieldPlace(p.Id, p.Position, new(1, 1))).ToImmutableArray())
    { RegionIds = Enumerable.Range(0, 24).Select(i => (int?)(i / 8 == 2 ? 5 : i % 8 == 0 ? 1 : i % 8 == 7 ? 2 : i % 8 == 4 ? 4 : 3)).ToImmutableArray() });
    private static SimulationRules Rules(RedCliffsDefinition? definition = null, ThreeVisitsDefinition? visits = null,
        ChainedPlotDefinition? plot = null, TerminalDefinition? terminal = null) => new()
    {
        RedCliffs = definition ?? Definition, Military = Military, Personnel = new(Places),
        ThreeVisits = visits, ChainedPlot = plot, Terminal = terminal
    };
    private static OfficerState Officer(int id, int owner, int? home, int? unit = null, OfficerStatus status = OfficerStatus.Serving) =>
        new OfficerState(id, owner, home, new(50, 50, 50, 50), false) { Status = status, UnitId = unit }.WithLoyalty(owner, 100);
    private static WorldState World() => WorldState.Create(new(190, 2, 1), 17,
        [new(1, 980) { BaseTrust = 980, LeaderId = 11 }, new(2, 10) { BaseTrust = 10, LeaderId = 21 },
         new(3, 100) { BaseTrust = 100, LeaderId = 31 }],
        Places.Select(p => new SettlementState(p.Id, p.Id is 2 or 4 ? 2 : p.Id == 5 ? 3 : 1,
            p.Id == 4 ? 9999u : 1000u, 100, 100, 60, 100, 100, 100, 100, 100)
            { Kind = p.Id <= 2 || p.Id == 5 ? SettlementKind.City : SettlementKind.Facility, ConsumesFood = false, Wounded = 20 }),
        [Officer(11, 1, 1, status: OfficerStatus.Ruler), Officer(12, 1, 3), Officer(13, 1, 1).WithLoyalty(1, 250), Officer(14, 1, null, 10),
         Officer(15, 1, 2, status: OfficerStatus.Captive), Officer(21, 2, 2, status: OfficerStatus.Ruler), Officer(22, 2, null, 20),
         Officer(23, 2, null, 21), Officer(24, 2, null, 22), Officer(31, 3, 5, status: OfficerStatus.Ruler), Officer(32, 3, null, 30)],
        units: [Unit(10, 1, 7, 7, 1000, 14, 1), Unit(20, 2, 12, 7, 20001, 22, 2) with
            { Order = new(MilitaryOrderKind.Move, new(32, 7), null), EngagementTargetId = 10, Wounded = 23, Morale = 61, CargoFood = 99 },
            Unit(21, 2, 17, 7, 10000, 23, 2), Unit(22, 2, 37, 12, 10000, 24, 2), Unit(30, 3, 22, 7, 10000, 32, 5)],
        controls: [new("player", 1, ControllerKind.Player), new("enemy", 2, ControllerKind.Automatic), new("third", 3, ControllerKind.Automatic)],
        relations: [new(1, 2, 19)], annualAppearancesEnabled: false, naturalDeathsEnabled: false, officeRequestsEnabled: false,
        freeOfficerMigrationEnabled: false, forceConsolidationEnabled: false, forceEmergenceEnabled: false, cityIndependenceEnabled: false, corps: [new San9.Core.Domain.CorpsState(1, 1, 11, new(10000, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 21, new(10000, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(3, 3, 31, new(10000, 10000)) { IsRoot = true }]);
    private static UnitState Unit(int id, int owner, int x, int y, uint soldiers, int commander, int home) =>
        new(id, owner, new(x, y), soldiers) { FormationId = 1, ServicePlaceId = home, CommanderId = commander, Morale = 60,
            OrderClass = UnitOrderClass.Military };

    [Fact]
    public void VictoryDamagesOnlyRegionalEnemyWaterUnitsAndGarrisonsAndAppliesSingleRewards()
    {
        var state = World(); var world = state.BeginMutation(); List<DomainEvent> events = [];
        Assert.True(RedCliffs.Apply(world, Rules(), events));
        var result = Assert.Single(events.OfType<RedCliffsEvent>());
        Assert.Equal(new[] { 20 }, result.AffectedUnitIds); Assert.Equal(new[] { 4 }, result.AffectedSettlementIds);
        Assert.Equal(26999, result.SoldiersLost); Assert.Equal((20, 10), (result.BaseTrustGain, result.EnemyBaseTrustLoss));
        Assert.Equal(2001u, world.Units[20].Soldiers); Assert.Equal(1000u, world.Settlements[4].Soldiers);
        Assert.Equal((23u, 61, 99u), (world.Units[20].Wounded, world.Units[20].Morale, world.Units[20].CargoFood));
        Assert.Equal((20u, 60, 100), (world.Settlements[4].Wounded, world.Settlements[4].Morale, world.Settlements[4].Durability));
        Assert.Equal(new MilitaryOrder(MilitaryOrderKind.Return, null, 2), world.Units[20].Order); Assert.Null(world.Units[20].EngagementTargetId);
        Assert.Equal(1000u, world.Units[10].Soldiers); Assert.Equal(10000u, world.Units[21].Soldiers);
        Assert.Equal(10000u, world.Units[22].Soldiers); Assert.Equal(10000u, world.Units[30].Soldiers);
        Assert.Equal(100, world.Officers[11].Loyalty); Assert.Equal(115, world.Officers[12].Loyalty);
        Assert.Equal(255, world.Officers[13].Loyalty); Assert.Equal(115, world.Officers[14].Loyalty); Assert.Equal(100, world.Officers[15].Loyalty);
        Assert.Equal(1000, world.Forces[1].Trust); Assert.Equal(0, world.Forces[2].Trust);
        Assert.Empty(world.Simulation.RandomStreams); Assert.False(RedCliffs.Apply(world, Rules(), events));
        state.Commit(world);
    }

    [Theory]
    [InlineData("relation")]
    [InlineData("troops")]
    [InlineData("player")]
    [InlineData("city-empty")]
    [InlineData("same-force")]
    [InlineData("wrong-place")]
    [InlineData("friendly-on-land")]
    [InlineData("friendly-personnel")]
    [InlineData("friendly-embedded")]
    [InlineData("work")]
    public void IneligibleCandidateHasNoConsumptionRewardsOrRandom(string failure)
    {
        var world = World().BeginMutation();
        switch (failure)
        {
            case "relation": ForceRelations.Set(world, 1, 2, 20, []); break;
            case "troops": world.Replace(world.Settlements[4] with { Soldiers = 9998 }); break;
            case "player": world.SetSimulation(world.Simulation with { Controls = [new("ai", 1, ControllerKind.Automatic)] }); break;
            case "city-empty": world.Replace(world.Settlements[2] with { OwnerId = null }); break;
            case "same-force": world.Replace(world.Settlements[2] with { OwnerId = 1 }); break;
            case "wrong-place": world.Replace(world.Officers[12] with { SettlementId = 1 }); break;
            case "friendly-on-land": world.Replace(world.Units[10] with { Position = new(17, 7) }); break;
            case "friendly-personnel": world.Replace(world.Units[10] with { IsPersonnel = true }); break;
            case "friendly-embedded": world.Replace(world.Units[10] with { IsEmbedded = true }); break;
            case "work": world.Replace(world.Settlements[4] with { CanHostPersons = false }); break;
        }
        List<DomainEvent> events = []; Assert.False(RedCliffs.Apply(world, Rules(), events));
        Assert.Empty(events); Assert.Empty(world.Simulation.ConsumedEventKeys); Assert.Empty(world.Simulation.RandomStreams);
        Assert.Equal(20001u, world.Units[20].Soldiers); Assert.Equal(980, world.Forces[1].BaseTrust);
    }

    [Fact]
    public void ExcludedEnemyRulerBlocksButOtherRulersAreNotHardcoded()
    {
        var world = World().BeginMutation(); List<DomainEvent> events = [];
        Assert.False(RedCliffs.Apply(world, Rules(Definition with { ExcludedRulerIds = [21] }), events));
        Assert.True(RedCliffs.Apply(world, Rules(), events));
    }

    [Fact]
    public void OfficerUsesCurrentUnitRegionRatherThanServicePlaceAndNeedNotBeRulerOrServing()
    {
        var state = World(); var world = state.BeginMutation();
        world.Replace(world.Officers[12] with { SettlementId = null, UnitId = 20, Status = OfficerStatus.Captive });
        List<DomainEvent> events = []; Assert.True(RedCliffs.Apply(world, Rules(), events));
        Assert.Equal(100, world.Officers[12].Loyalty); Assert.Equal(OfficerStatus.Captive, world.Officers[12].Status); state.Commit(world);
    }

    [Fact]
    public void TravelingOfficerCannotQualifyThroughHisAdministrativeHome()
    {
        var world = World().BeginMutation();
        Assert.Equal(CommandRejection.None, PersonnelMissions.StartSearch(world, new("player"), new(Guid.NewGuid(), 1, 0, 12, 1), Rules().Personnel, []));
        Assert.False(RedCliffs.Apply(world, Rules(), []));
        Assert.Empty(world.Simulation.ConsumedEventKeys);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IdleOrHomelessUnitsAreDamagedAndDisengagedWithoutInventingAnOrder(bool homeless)
    {
        var state = World(); var world = state.BeginMutation();
        var previous = world.Units[20];
        world.Replace(previous with { ServicePlaceId = homeless ? null : 2, Order = homeless ? previous.Order : null });
        RedCliffs.Apply(world, Rules(), []);
        Assert.Equal(homeless ? previous.Order : null, world.Units[20].Order);
        Assert.Null(world.Units[20].EngagementTargetId); Assert.Equal(2001u, world.Units[20].Soldiers); state.Commit(world);
    }

    [Fact]
    public void SourceCutoffAndEndedCourtDoNotBecomeRuntimeGates()
    {
        var world = World().BeginMutation(); world.SetClock(new(240, 2, 1));
        world.SetSimulation(world.Simulation with { ImperialCourtEnded = true });
        Assert.True(RedCliffs.Apply(world, Rules(), []));
    }

    [Fact]
    public void SmallUnitLossRoundsDownPerObjectAndNeverErasesPositiveTroopsAtNinetyPercent()
    {
        var state = World(); var world = state.BeginMutation();
        world.Replace(world.Units[20] with { Soldiers = 9 }); world.Replace(world.Settlements[4] with { Soldiers = 30000 });
        RedCliffs.Apply(world, Rules(), []);
        Assert.Equal(1u, world.Units[20].Soldiers); Assert.Equal(3000u, world.Settlements[4].Soldiers); state.Commit(world);
    }

    [Fact]
    public void ReturnCancelsAnIncompleteFacilityWithoutCountingItsGarrison()
    {
        var state = World(); var world = state.BeginMutation();
        var plan = new ConstructionPlan(4, 1, new(22, 2), 22);
        world.Replace(world.Settlements[4] with { CanHostPersons = false, Facility = new(1, new(22, 2), 20) });
        world.Replace(world.Units[20] with { Order = new(MilitaryOrderKind.Construct, new(22, 2), 4) { Construction = plan } });
        List<DomainEvent> events = []; Assert.False(RedCliffs.Apply(world, Rules(), events));
        Assert.True(RedCliffs.Apply(world, Rules(Definition with { MinimumEnemySoldiers = 20001 }), events));
        Assert.Empty(Assert.Single(events.OfType<RedCliffsEvent>()).AffectedSettlementIds);
        Assert.True(world.Settlements[4].IsDestroyed); Assert.Null(world.Settlements[4].Facility!.BuilderUnitId);
        Assert.Equal(MilitaryOrderKind.Return, world.Units[20].Order!.Kind); state.Commit(world);
    }

    [Fact]
    public void AuthoredTotalLossUsesSharedTerminationAndPreservesValidPeopleAndReferences()
    {
        var state = World(); var world = state.BeginMutation(); List<DomainEvent> events = [];
        Assert.True(RedCliffs.Apply(world, Rules(Definition with { SoldierLossPercent = 100 }), events));
        Assert.False(world.Units.ContainsKey(20)); Assert.Null(world.Officers[22].UnitId);
        Assert.Equal(0u, world.Settlements[4].Soldiers); Assert.Single(events.OfType<UnitTerminatedEvent>());
        state.Commit(world);
    }

    [Fact]
    public void EarlierChainedPlotAndThreeVisitsExpiryEachDeferRedCliffsByOneXun()
    {
        var visits = new ThreeVisitsDefinition(11, 13, 12, [14], 3, 95, 360, 150, 10, new("visits.start"), new("visits.first"), new("visits.end"));
        var plot = new ChainedPlotDefinition(11, 14, 12, new(190, 1, 1), 95, 1000, [], [], 96, 80, 2, new("plot.start"), new("plot.end"));
        var state = World(); var world = state.BeginMutation();
        world.Replace(world.Officers[13] with { Status = OfficerStatus.Special, OwnerId = null });
        world.Replace(world.Units[20] with { Order = null, EngagementTargetId = null });
        world.SetSimulation(world.Simulation with { ConsumedEventKeys = [plot.StartedKey, visits.StartedKey], ThreeVisitsStarted = null });
        state.Commit(world);
        var engine = new SimulationEngine(state, Rules(visits: visits, plot: plot));
        Assert.Equal(ChainedPlotStep.CourtUnavailable, Assert.Single(engine.InitialEvents.OfType<ChainedPlotEvent>()).Step);
        Assert.Empty(engine.InitialEvents.OfType<RedCliffsEvent>());
        foreach (var control in engine.Snapshot.Simulation.Controls) engine.EndControl(new(control.ControllerId));
        var expiry = engine.AdvanceXun();
        Assert.Single(expiry.Events.OfType<ThreeVisitsEvent>()); Assert.Empty(expiry.Events.OfType<RedCliffsEvent>());
        foreach (var control in engine.Snapshot.Simulation.Controls) engine.EndControl(new(control.ControllerId));
        Assert.Single(engine.AdvanceXun().Events.OfType<RedCliffsEvent>());
    }

    [Fact]
    public void CampaignDeadlineStopsTheEventBeforeItsEffects()
    {
        var engine = new SimulationEngine(World(), Rules(terminal: new([], 189)));
        Assert.Equal(SimulationBoundary.Terminal, engine.Snapshot.Simulation.Boundary);
        Assert.Empty(engine.InitialEvents.OfType<RedCliffsEvent>());
        Assert.DoesNotContain(Definition.ConsumptionKey, engine.Snapshot.Simulation.ConsumedEventKeys);
        Assert.Equal(20001u, engine.Snapshot.Units.Single(u => u.Id == 20).Soldiers);
    }
}

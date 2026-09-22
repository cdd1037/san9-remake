using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class ChainedPlotTests
{
    internal static ChainedPlotDefinition Definition => new(11, 12, 13, new(192, 4, 1), 95, 1000,
        [13, 14], [15], 96, 80, 2, new("plot.started"), new("plot.completed"));
    private static SimulationRules Rules(ThreeVisitsDefinition? visits = null, TerminalDefinition? terminal = null) => new()
    {
        ChainedPlot = Definition, ThreeVisits = visits, Terminal = terminal,
        Economy = new([], []) { ImperialCourt = new([99], []) },
        Personnel = new([new(1, new(0, 0)), new(2, new(2000, 0)), new(3, new(4000, 0))]),
        Treasures = new([new(7, default, 0), new(8, default, 0)]),
        Military = new(new(10, 1, Enumerable.Repeat(0, 10).ToImmutableArray(), [new(0, 1, null)],
            [new(1, 5, 0, 10000, 2, new(1, 1), TravelMode.Land)],
            Enumerable.Range(1, 3).Select(id => new BattlefieldPlace(id, new(id * 5 + 2, 2), new(1, 1))).ToImmutableArray()))
    };
    private static WorldState World(bool started = false) => WorldState.Create(new(192, 4, 1), 17,
        [new(1, 10000, 10000, 500) { LeaderId = 11, BaseTrust = 500 }, new(2, 10000, 10000, 100) { LeaderId = 21 },
         new(3, 10000, 10000, 100) { LeaderId = 31 }],
        Enumerable.Range(1, 4).Select(id => new SettlementState(id, id >= 3 ? id - 1 : 1, 100, 100, 100, 50, 100, 100, 100, 100, 100)
            { Kind = SettlementKind.City, ConsumesFood = false }),
        Enumerable.Range(11, 10).Select(id => new OfficerState(id, 1, id is 14 or 18 ? 2 : id == 19 ? 3 : 1,
            new(50, 50, 50, 50), false) { Status = id == 11 ? OfficerStatus.Ruler : id == 19 ? OfficerStatus.Captive : OfficerStatus.Serving,
                BloodlineId = id is 11 or >= 17 ? 700 : id, Affinity = id == 12 ? 60 : 0 }.WithLoyalty(1, id is 13 or 14 ? 90 : 100))
            .Append(new(21, 2, 3, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler })
            .Append(new(31, 3, 4, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler })
            .Append(new(99, null, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Special }),
        treasures: [new(7, 11, TreasureDiscovery.Discovered, null), new(8, 17, TreasureDiscovery.Discovered, null)],
        controls: [new("player", 1, ControllerKind.Player), new("ai", 2, ControllerKind.Automatic)],
        consumedEventKeys: started ? [Definition.StartedKey] : [],
        annualAppearancesEnabled: false, naturalDeathsEnabled: false, freeOfficerMigrationEnabled: false,
        officeRequestsEnabled: false, forceConsolidationEnabled: false, forceEmergenceEnabled: false, cityIndependenceEnabled: false);

    [Theory]
    [InlineData(1000, 94, 94, true)]
    [InlineData(1000, 95, 94, false)]
    [InlineData(1000, 94, 95, false)]
    [InlineData(999, 255, 255, true)]
    public void PreludeUsesStrictLoyaltyConjunctionOrTrustThreshold(int trust, int first, int second, bool expected)
    {
        var world = World().BeginMutation(Rules().Abilities);
        world.Replace(world.Forces[1] with { BaseTrust = trust });
        world.Replace(world.Officers[12].WithLoyalty(1, first)); world.Replace(world.Officers[13].WithLoyalty(1, second));
        List<DomainEvent> events = []; Assert.Equal(expected, ChainedPlot.Apply(world, Rules(), events));
        Assert.Equal(expected ? 1 : 0, events.Count); Assert.Equal(expected ? 1 : 0, world.Simulation.ConsumedEventKeys.Length);
        Assert.Equal(OfficerStatus.Ruler, world.Officers[11].Status); Assert.Empty(world.Simulation.RandomStreams);
    }

    [Theory]
    [InlineData("early")]
    [InlineData("court-ended")]
    [InlineData("emperor-dead")]
    [InlineData("court-lost")]
    [InlineData("ruler")]
    [InlineData("captive")]
    [InlineData("wrong-force")]
    public void IneligiblePreludeDoesNotConsumeAndHasNoWorldEffects(string failure)
    {
        var world = World().BeginMutation(Rules().Abilities);
        switch (failure)
        {
            case "early": world.SetClock(new(192, 3, 30)); break;
            case "court-ended": world.SetSimulation(world.Simulation with { ImperialCourtEnded = true }); break;
            case "emperor-dead": world.Replace(world.Officers[99] with { Status = OfficerStatus.Dead }); break;
            case "court-lost": world.Replace(world.Officers[99] with { SettlementId = 3 }); break;
            case "ruler": world.Replace(world.Officers[11] with { Status = OfficerStatus.Serving }); break;
            case "captive": world.Replace(world.Officers[12] with { Status = OfficerStatus.Captive }); break;
            case "wrong-force": world.Replace(world.Officers[13] with { OwnerId = 2 }); break;
        }
        List<DomainEvent> events = []; Assert.False(ChainedPlot.Apply(world, Rules(), events));
        Assert.Empty(events); Assert.Empty(world.Simulation.ConsumedEventKeys); Assert.Empty(world.Simulation.RandomStreams);
    }

    [Theory]
    [InlineData("ruler", ChainedPlotStep.RulerUnavailable)]
    [InlineData("successor", ChainedPlotStep.ConspiratorsUnavailable)]
    [InlineData("instigator", ChainedPlotStep.ConspiratorsUnavailable)]
    [InlineData("court", ChainedPlotStep.CourtUnavailable)]
    [InlineData("court-ended", ChainedPlotStep.CourtUnavailable)]
    public void FailedSecondStageConsumesWithoutDeathOrLoyaltyEffects(string failure, ChainedPlotStep expected)
    {
        var world = World(true).BeginMutation(Rules().Abilities);
        switch (failure)
        {
            case "ruler": world.Replace(world.Officers[11] with { Status = OfficerStatus.Dead }); break;
            case "successor": world.Replace(world.Officers[12] with { Status = OfficerStatus.Captive }); break;
            case "instigator": world.Replace(world.Officers[13] with { OwnerId = 2 }); break;
            case "court": world.Replace(world.Officers[99] with { SettlementId = 3 }); break;
            case "court-ended": world.SetSimulation(world.Simulation with { ImperialCourtEnded = true }); world.Replace(world.Officers[11] with { Status = OfficerStatus.Dead }); break;
        }
        List<DomainEvent> events = []; Assert.True(ChainedPlot.Apply(world, Rules(), events));
        Assert.Equal(expected, Assert.Single(events.OfType<ChainedPlotEvent>()).Step);
        Assert.Single(events); Assert.Contains(Definition.CompletedKey, world.Simulation.ConsumedEventKeys);
        Assert.False(ChainedPlot.Apply(world, Rules(), events)); Assert.Single(events);
        Assert.Equal(100, world.Officers[16].Loyalty); Assert.Equal(11, world.Treasures[7].HolderId);
    }

    [Fact]
    public void CoupPurgesOnlyColocatedServingFamilyAndTransfersTreasuresWithSingleLoyaltyPolicy()
    {
        var state = World(true); var world = state.BeginMutation(Rules().Abilities);
        Assert.Equal(CommandRejection.None, PersonnelMissions.StartSearch(world, new("player"), new(Guid.NewGuid(), 1, 0, 20, 2), Rules().Personnel, []));
        world.SetSimulation(world.Simulation with { NextSequence = 1 });
        // A supporter in the old bloodline takes precedence over the opposition clamp, but is away from the purge.
        world.Replace(world.Officers[14] with { BloodlineId = 700 });
        world.Replace(world.Officers[13].WithLoyalty(1, 254)); world.Replace(world.Officers[18].WithLoyalty(1, 40));
        List<DomainEvent> events = []; Assert.True(ChainedPlot.Apply(world, Rules(), events));
        Assert.Equal(new[] { 17, 11 }, Assert.Single(events.OfType<ChainedPlotEvent>()).KilledOfficerIds);
        Assert.Equal(12, world.Forces[1].LeaderId); Assert.Equal(OfficerStatus.Ruler, world.Officers[12].Status);
        Assert.Equal(500, world.Forces[1].BaseTrust); Assert.All(world.Treasures.Values, t => Assert.Equal(12, t.HolderId));
        Assert.Equal(254, world.Officers[13].Loyalty); Assert.Equal(96, world.Officers[14].Loyalty);
        Assert.Equal(80, world.Officers[15].Loyalty); Assert.Equal(75, world.Officers[16].Loyalty);
        Assert.Equal(40, world.Officers[18].Loyalty); Assert.Equal(80, world.Officers[19].Loyalty); Assert.Equal(80, world.Officers[20].Loyalty);
        Assert.Equal(OfficerStatus.Captive, world.Officers[19].Status); Assert.Contains(world.Simulation.Tasks, t => t.OfficerId == 20);
        Assert.DoesNotContain(events.OfType<OfficerLoyaltyChangedEvent>(), e => e.Reason == "ruler-succession");
        Assert.Empty(world.Simulation.RandomStreams); state.Commit(world);
    }

    [Fact]
    public void TravelingRulerDoesNotPurgePeopleAtHisServicePlaceAndHisMissionIsCancelled()
    {
        var state = World(true); var world = state.BeginMutation(Rules().Abilities);
        Assert.Equal(CommandRejection.None, PersonnelMissions.StartSearch(world, new("player"), new(Guid.NewGuid(), 1, 0, 11, 2), Rules().Personnel, []));
        world.SetSimulation(world.Simulation with { NextSequence = 1 });
        List<DomainEvent> events = []; ChainedPlot.Apply(world, Rules(), events);
        Assert.Equal(11, Assert.Single(Assert.Single(events.OfType<ChainedPlotEvent>()).KilledOfficerIds));
        Assert.Empty(world.Simulation.Tasks); Assert.Equal(OfficerStatus.Serving, world.Officers[17].Status); state.Commit(world);
    }

    [Fact]
    public void SuccessorCorpsAndTreasuryBecomeDirectAndOldGovernorIsDemoted()
    {
        var state = World(true); var world = state.BeginMutation(Rules().Abilities);
        world.AddCorps(new(100, 1, 11, null)); world.AddCorps(new(200, 1, 18, new(300, 400)) { ControlMode = CorpsControlMode.Automatic });
        world.Replace(world.Settlements[1] with { CorpsId = 100 }); world.Replace(world.Settlements[2] with { CorpsId = 200 });
        world.Replace(world.Officers[12] with { SettlementId = 2 }); world.Replace(world.Officers[18] with { Status = OfficerStatus.Governor });
        ChainedPlot.Apply(world, Rules(), []); state.Commit(world);
        var corps = Assert.Single(world.Corps.Values); Assert.Equal(12, corps.LeaderId); Assert.Null(corps.Treasury);
        Assert.Equal(CorpsControlMode.Direct, corps.ControlMode); Assert.Equal(OfficerStatus.Serving, world.Officers[18].Status);
        Assert.Equal((10300u, 10400u), (world.Forces[1].Money, world.Forces[1].Food));
    }

    [Theory]
    [InlineData("leader", AllianceEndReason.LeaderDied)]
    [InlineData("target", AllianceEndReason.TargetDied)]
    [InlineData("member", null)]
    public void AllianceEndsOnlyWhenTheAffectedForceIsLeaderOrTarget(string role, AllianceEndReason? reason)
    {
        var state = World(true); var world = state.BeginMutation(Rules().Abilities);
        world.SetSimulation(world.Simulation with { Alliance = role switch
        { "leader" => new(3, 1, [1, 2], 30), "target" => new(1, 2, [2, 3], 30), _ => new(3, 2, [1, 2], 30) } });
        List<DomainEvent> events = []; ChainedPlot.Apply(world, Rules(), events);
        if (reason is { } expected) { Assert.Null(world.Simulation.Alliance); Assert.Equal(expected, Assert.Single(events.OfType<AllianceEndedEvent>()).Reason); }
        else { Assert.NotNull(world.Simulation.Alliance); Assert.Empty(events.OfType<AllianceEndedEvent>()); }
        state.Commit(world);
    }

    [Fact]
    public void XunTableStopsAfterPreludeThenResolvesCoupWithoutAnotherDateOrLoyaltyGate()
    {
        var engine = new SimulationEngine(World(), Rules());
        Assert.Equal(ChainedPlotStep.Prelude, Assert.Single(engine.InitialEvents.OfType<ChainedPlotEvent>()).Step);
        Assert.Equal(OfficerStatus.Ruler, engine.Snapshot.Officers.Single(p => p.Id == 11).Status);
        foreach (var seat in engine.Snapshot.Simulation.Controls) engine.EndControl(new(seat.ControllerId));
        var result = engine.AdvanceXun();
        Assert.Equal(new GameClock(192, 4, 11), result.Snapshot.Clock);
        Assert.Equal(ChainedPlotStep.Coup, Assert.Single(result.Events.OfType<ChainedPlotEvent>()).Step);
        Assert.Equal(12, result.Snapshot.Forces.Single(f => f.Id == 1).LeaderId);
    }

    [Fact]
    public void StartedPlotIgnoresRestoredTrustAndLoyaltyAndSparesAnAuthoredSameFamilySuccessor()
    {
        var state = World(true); var world = state.BeginMutation(Rules().Abilities);
        world.Replace(world.Forces[1] with { BaseTrust = 1000 });
        world.Replace(world.Officers[12].WithLoyalty(1, 255) with { BloodlineId = 700 });
        world.Replace(world.Officers[13].WithLoyalty(1, 255));
        List<DomainEvent> events = []; Assert.True(ChainedPlot.Apply(world, Rules(), events));
        Assert.Equal(ChainedPlotStep.Coup, Assert.Single(events.OfType<ChainedPlotEvent>()).Step);
        Assert.Equal(OfficerStatus.Ruler, world.Officers[12].Status);
        Assert.False(ChainedPlot.Apply(world, Rules(), events)); state.Commit(world);
    }

    [Fact]
    public void ThreeVisitsExpiryWaitsUntilBothHigherPriorityPlotStagesHaveFinished()
    {
        var visits = new ThreeVisitsDefinition(11, 16, 13, [12, 14], 1, 95, 360, 150, 10,
            new("visits.start"), new("visits.first"), new("visits.end"));
        var state = World(); var world = state.BeginMutation(Rules().Abilities);
        world.Replace(world.Officers[16] with { Status = OfficerStatus.Special, OwnerId = null });
        world.SetSimulation(world.Simulation with { ConsumedEventKeys = [visits.StartedKey], ThreeVisitsStarted = new(191, 4, 1) });
        state.Commit(world);
        var engine = new SimulationEngine(state, Rules(visits));
        Assert.Equal(ChainedPlotStep.Prelude, Assert.Single(engine.InitialEvents.OfType<ChainedPlotEvent>()).Step);
        Assert.Empty(engine.InitialEvents.OfType<ThreeVisitsEvent>());
        foreach (var seat in engine.Snapshot.Simulation.Controls) engine.EndControl(new(seat.ControllerId));
        var coup = engine.AdvanceXun();
        Assert.Single(coup.Events.OfType<ChainedPlotEvent>()); Assert.Empty(coup.Events.OfType<ThreeVisitsEvent>());
        foreach (var seat in engine.Snapshot.Simulation.Controls) engine.EndControl(new(seat.ControllerId));
        var expiry = engine.AdvanceXun();
        Assert.Equal(ThreeVisitsStep.Expired, Assert.Single(expiry.Events.OfType<ThreeVisitsEvent>()).Step);
        Assert.Equal(new GameClock(192, 4, 21), expiry.Snapshot.Clock);
    }

    [Fact]
    public void TerminalBoundaryDoesNotProducePlotOrKillAnyone()
    {
        var engine = new SimulationEngine(World(true), Rules(terminal: new([], 191)));
        Assert.Equal(SimulationBoundary.Terminal, engine.Snapshot.Simulation.Boundary);
        Assert.Empty(engine.InitialEvents.OfType<ChainedPlotEvent>());
        Assert.Equal(OfficerStatus.Ruler, engine.Snapshot.Officers.Single(p => p.Id == 11).Status);
        Assert.DoesNotContain(Definition.CompletedKey, engine.Snapshot.Simulation.ConsumedEventKeys);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FieldPurgeKeepsOtherUnitsAndReelectsOrTerminatesTheRulersUnit(bool survivor)
    {
        var state = World(true); var world = state.BeginMutation(Rules().Abilities);
        world.SetSimulation(world.Simulation with { NextUnitId = 52 });
        world.AddUnit(new(50, 1, new(12, 2), 1000) { FormationId = 1, ServicePlaceId = 1, CommanderId = 11,
            MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(11, BattleRank.Front).Add(17, BattleRank.Rear) });
        world.AddUnit(new(51, 1, new(12, 2), 1000) { FormationId = 1, ServicePlaceId = 1, CommanderId = 18 });
        foreach (int id in new[] { 11, 17 }) world.Replace(world.Officers[id] with { SettlementId = null, UnitId = 50 });
        world.Replace(world.Officers[18] with { SettlementId = null, UnitId = 51 });
        if (survivor) world.Replace(world.Officers[16] with { SettlementId = null, UnitId = 50 });
        List<DomainEvent> events = []; ChainedPlot.Apply(world, Rules(), events);
        Assert.Equal(new[] { 17, 11 }, Assert.Single(events.OfType<ChainedPlotEvent>()).KilledOfficerIds);
        Assert.True(world.Units.ContainsKey(51)); Assert.Equal(survivor, world.Units.ContainsKey(50));
        if (survivor) Assert.Equal(16, world.Units[50].CommanderId);
        else Assert.Equal(50, Assert.Single(events.OfType<UnitTerminatedEvent>()).UnitId);
        state.Commit(world);
    }
}

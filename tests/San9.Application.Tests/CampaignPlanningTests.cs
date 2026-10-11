using System.Collections.Immutable;
using San9.Application.AI;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class CampaignPlanningTests
{
    private static ScenarioInitialState Fixture() => new(ScenarioInitialState.CurrentSchemaVersion, "campaign", 190, 1, 1, 1,
        [new(1, "opponent", 1, 100) { LeaderId = 1 }, new(2, "automatic", 2, 100) { LeaderId = 2 }],
        [new(1, "rear", 1, 18, 1, 1000, 1000, 1000, 100, []),
         new(2, "source", 2, 0, 1, 14000, 1000, 1000, 100, []) { MerchantPresenceChance = 100 },
         new(3, "objective", 1, 5, 1, 2000, 30, 100, 100, [])], [], [])
    {
        Officers = [new("enemy-ruler", new(1, 1, 1, new(70, 70, 70, 70), false) { Status = OfficerStatus.Ruler }),
            new("ruler", new(2, 2, 2, new(70, 70, 95, 70), false) { Status = OfficerStatus.Ruler }),
            new("general", new OfficerState(3, 2, 2, new(80, 70, 20, 90), false).WithLoyalty(2, 100))],
        Military = new(20, 3, Enumerable.Repeat(0, 60).ToImmutableArray(), [new(0, 1, null)],
            [new(7, 10, 10, 20000, 5, new(1, 1), TravelMode.Land)
                { Combat = new(20, 20, 50, 20, 50), Attack = new(1, true),
                  CaptureRanks = [BattleRank.Middle, BattleRank.Middle, BattleRank.Middle, BattleRank.Middle, BattleRank.Middle] },
             new(70, 10, 10, 20000, 5, new(1, 1), TravelMode.Land)],
            [new(1, new(92, 7), new(1, 1)) { Combat = new(10, 10, 0, 10, 50) },
             new(2, new(2, 7), new(1, 1)) { Combat = new(10, 10, 0, 10, 50) },
             new(3, new(27, 7), new(1, 1)) { Combat = new(10, 10, 0, 10, 50) }]) { TransportFormations = new(70, null) },
        Information = new([], [], [], [new CorpsState(1, 1, 1, new(100000, 100000)) { IsRoot = true },
            new CorpsState(2, 2, 2, new(100000, 100000)) { IsRoot = true }], [])
    };

    private static CampaignObservation Observe(GameSession session, ScenarioInitialState definition, long budget = 65536)
    {
        var snapshot = session.Snapshot;
        var engine = GameSession.EngineFactory(definition)(WorldState.Restore(snapshot with
            { Simulation = snapshot.Simulation with { ActiveControl = 1 } }));
        var seat = session.Snapshot.Simulation.Controls.Single(s => s.ForceId == 2);
        var economy = EconomicObservation.Capture(engine, seat, new(seat.ControllerId), budget, quoteAll: true);
        var work = new AllocationWork(budget);
        var routes = new ArrivalProjectionCache();
        var support = SupportObservationCapture.Capture(engine, seat, [(seat.ControllerId, economy)], session.MilitaryRules, work, routes);
        return CampaignObservationCapture.Capture(engine, seat, [(seat.ControllerId, economy)], support, session.MilitaryRules!, work, routes, session.AiState.Campaigns);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(42)]
    [InlineData(190)]
    public void AutomaticCampaignCapturesConsolidatesAndRestoresWithReplay(ulong seed)
    {
        var definition = Fixture();
        var session = GameSession.Start(definition, seed);
        session.AllocationDiagnosticsEnabled = true;
        session.BeginRecording();
        var result = session.Apply(session.Request(new PrepareAdvanceOperation()));
        Assert.True(result.Events.OfType<UnitDeployedEvent>().Any(e => e.SettlementId == 2), System.Text.Json.JsonSerializer.Serialize(session.AllocationDiagnostics.Batches));
        Assert.Single(session.AiState.Campaigns);
        Assert.Equal(3, Assert.Single(session.Snapshot.Units).Order!.SettlementId);
        Assert.True(Assert.Single(session.Snapshot.Units).Policies.AutonomousRetreat);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        List<DomainEvent> events = [];
        for (int i = 0; i < 6; i++)
        {
            var next = session.AdvanceXun();
            Assert.Equal(CanonicalJson.Hash(next.Events), CanonicalJson.Hash(restored.AdvanceXun().Events));
            Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
            events.AddRange(next.Events);
        }
        Assert.Contains(events.OfType<SettlementCapturedEvent>(), e => e.SettlementId == 3);
        Assert.Equal(2, session.Snapshot.Settlements.Single(s => s.Id == 3).OwnerId);
        Assert.Contains(events.OfType<DevelopmentCompletedEvent>(), e => e.Command.SettlementId == 3 &&
            e.Command.Activity is DomesticActivity.Patrol or DomesticActivity.Repair);
        Assert.DoesNotContain(session.Snapshot.Units, u => u.Order?.SettlementId == 3 && u.Order.Kind == MilitaryOrderKind.AttackSettlement);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        Assert.Equal(session.Capture().StateHash, replay.Session.Capture().StateHash);
    }

    [Fact]
    public void ForeignPrivateTasksAndAbilitiesDoNotChangeTheCampaignObservation()
    {
        var definition = Fixture();
        var session = GameSession.Start(definition, 42);
        var captured = session.Capture();
        var changed = GameSession.Restore(definition, captured with { World = captured.World with
            { Officers = captured.World.Officers.Select(p => p.OwnerId == 1 ? p with { Acted = true, TacticCooldown = 9 } : p).ToArray() } });
        Assert.Equal(CanonicalJson.Hash(Observe(session, definition)), CanonicalJson.Hash(Observe(changed, definition)));
    }

    [Fact]
    public void OwnershipChangeWithdrawsInsteadOfLaunchingAnotherObjective()
    {
        var definition = Fixture();
        var session = GameSession.Start(definition, 42);
        session.Apply(session.Request(new PrepareAdvanceOperation()));
        var capture = session.Capture();
        var unit = Assert.Single(capture.World.Units);
        var changed = GameSession.Restore(definition, capture with { World = capture.World with
            { Settlements = capture.World.Settlements.Select(s => s.Id == 3 ? s with { OwnerId = null, CorpsId = null } : s).ToArray(),
              Units = capture.World.Units.Select(u => u with { CommandedThisXun = false }).ToArray() } });
        var decision = CampaignPlanner.Decide(Observe(changed, definition), changed.AiState.Campaigns, new(65536));
        var retreat = Assert.Single(decision.Candidates);
        Assert.Equal(unit.Id, Assert.IsType<OrderUnitCommand>(retreat.Intent).UnitId);
        Assert.Equal(MilitaryOrderKind.Return, ((OrderUnitCommand)retreat.Intent).Order.Kind);
        Assert.Equal(CampaignPhase.Withdrawing, Assert.Single(decision.Goals).Phase);
    }

    private static CampaignForce Force(int source, uint count, int days = 1, int cost = 10) => new(
        new("ai:2", 2, new DeployUnitCommand(Guid.Empty, 2, 0, source, [source + 10], count, 7,
            new(MilitaryOrderKind.AttackSettlement, null, 3)), [source + 10], 2, 0, cost, 100,
            source, 3, new(ArrivalEstimateStatus.Known, days), Purpose: "campaign"), count, 100, 90, new(20, 20, 30, 20, 50), true);
    private static CampaignPlace Target(uint soldiers = 10000, int walls = 1000) => new(3, 1, soldiers, 100, walls,
        walls, 100, 70, new(20, 20, 0, 20, 50), 50000, "public-conditions");
    private static CampaignObservation Input(ImmutableArray<CampaignForce> forces) =>
        new(new(190, 1, 1), 2, [Target()], forces, [], [], [], [new(2, 100000, 100000, 1000) { HorizonDays = 90 }], true);

    [Fact]
    public void JointPlanRequiresEveryAccountAndRejectsLateAssembly()
    {
        var observation = Input([Force(2, 10000), Force(4, 10000)]);
        Assert.Equal(2, CampaignPlanner.Decide(observation, [], new(65536)).Candidates.Length);
        Assert.Empty(CampaignPlanner.Decide(observation with { Accounts = [new(2, 15, 100000, 1000) { HorizonDays = 90 }] }, [], new(65536)).Candidates);
        Assert.Empty(CampaignPlanner.Decide(Input([Force(2, 10000), Force(4, 10000, 20)]), [], new(65536)).Candidates);
    }

    [Fact]
    public void OccupationThatCannotSurviveCounterattackIsNotAnAdvantage()
    {
        var observation = Input([Force(2, 10000)]);
        var forecast = CampaignCombatForecast.Evaluate(Target(1000, 5), observation.Forces, [], 0, () => true);
        Assert.True(forecast.Feasible);
        var counter = CampaignCombatForecast.Evaluate(Target(1000, 5), observation.Forces, [new(3, forecast.Days + 1, 30000)], 0, () => true);
        Assert.False(counter.Feasible);
    }

    [Fact]
    public void IncompleteObservationPreservesTheCommitmentAndCannotOrderAWithdrawal()
    {
        var goal = new CampaignGoal(2, "ai:2", 3, 1, CampaignPhase.Attacking, new(190, 1, 1), new(190, 1, 1), 100000, 20, [7], "conditions");
        var incomplete = Input([]) with { Places = [], Complete = false };
        var decision = CampaignPlanner.Decide(incomplete, [goal], new(65536));
        Assert.Empty(decision.Candidates);
        Assert.Equal(goal, Assert.Single(decision.Goals));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RealPreparationBuysCampaignFoodOrTrainsBeforeDeployment(bool food)
    {
        var basis = Fixture();
        var definition = basis with
        {
            Settlements = basis.Settlements.Select(s => !food && s.Id == 2 ? s with { Morale = 60 } : s).ToArray(),
            Information = basis.Information with { Corps = basis.Information.Corps.Select(c => food && c.OwnerId == 2
                ? c with { Treasury = new(100000, 6000) } : c).ToArray() }
        };
        var session = GameSession.Start(definition, 42);
        var first = session.Apply(session.Request(new PrepareAdvanceOperation()));
        if (food) Assert.Contains(first.Events, e => e is FoodTradeCompletedEvent);
        else Assert.Contains(first.Events.OfType<DevelopmentCompletedEvent>(), e => e.Command.Activity == DomesticActivity.Train);
        if (!food)
        {
            Assert.Empty(first.Events.OfType<UnitDeployedEvent>());
            Assert.Equal(CampaignPhase.Preparing, Assert.Single(session.AiState.Campaigns).Phase);
        }
        else Assert.True(first.Events.ToList().FindIndex(e => e is FoodTradeCompletedEvent) <
            first.Events.ToList().FindIndex(e => e is UnitDeployedEvent));
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        List<DomainEvent> events = [..first.Events];
        for (int i = 0; i < 8; i++)
        {
            var next = session.AdvanceXun();
            Assert.Equal(CanonicalJson.Hash(next.Events), CanonicalJson.Hash(restored.AdvanceXun().Events));
            Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
            events.AddRange(next.Events);
        }
        Assert.Contains(events.OfType<UnitDeployedEvent>(), e => e.SettlementId == 2);
        Assert.Contains(events.OfType<SettlementCapturedEvent>(), e => e.SettlementId == 3);
    }

    [Fact]
    public void ReadyLocalPlanSurvivesAnUnnecessaryDistantDonor()
    {
        var observation = Input([Force(2, 12000), Force(4, 12000, 40)]) with { Places = [Target(1000, 20)] };
        var action = Assert.Single(CampaignPlanner.Decide(observation, [], new(65536)).Candidates);
        Assert.Equal(2, action.SourceId);
    }

    [Fact]
    public void NoProgressWithdrawsAndCooldownExpiresWhenPublicConditionsChange()
    {
        var retreat = new AllocationCandidate("ai:2", 2, new OrderUnitCommand(Guid.Empty, 2, 0, 7,
            new(MilitaryOrderKind.Return, null, 2)), [], 0, 10000, 0, 0, 2, 2, new(ArrivalEstimateStatus.Known, 5), Purpose: "withdraw:7");
        var active = Force(2, 10000) with { UnitId = 7 };
        var goal = new CampaignGoal(2, "ai:2", 3, 1, CampaignPhase.Attacking, new(190, 1, 1), new(190, 1, 1), 1, 10, [7], "public-conditions");
        var observation = Input([active]) with { Clock = new(190, 2, 1), ActiveUnitIds = [7], Returns = [retreat] };
        var stalled = CampaignPlanner.Decide(observation, [goal], new(65536));
        Assert.Equal(retreat, Assert.Single(stalled.Candidates));
        Assert.Equal(CampaignPhase.Withdrawing, Assert.Single(stalled.Goals).Phase);
        var exited = CampaignPlanner.Decide(observation with { Forces = [], ActiveUnitIds = [], Returns = [] }, stalled.Goals, new(65536));
        Assert.Equal(CampaignPhase.CoolingDown, Assert.Single(exited.Goals).Phase);
        Assert.Empty(CampaignPlanner.Decide(observation with { Forces = [Force(2, 20000)] }, exited.Goals, new(65536)).Candidates);
        var changed = observation with { Forces = [Force(2, 20000)], Places = [Target(100, 10) with { Conditions = "changed" }] };
        Assert.NotEmpty(CampaignPlanner.Decide(changed, exited.Goals, new(65536)).Candidates);
    }

    [Fact]
    public void AlternatingFeintsDoNotRetargetTheCommittedArmyButARealCounterattackDoes()
    {
        var active = Force(2, 10000) with { UnitId = 7 };
        var goal = new CampaignGoal(2, "ai:2", 3, 1, CampaignPhase.Attacking, new(190, 1, 1), new(190, 1, 1), 100000, 20, [7], "public-conditions");
        var retreat = new AllocationCandidate("ai:2", 2, new OrderUnitCommand(Guid.Empty, 2, 0, 7,
            new(MilitaryOrderKind.Return, null, 2)), [], 0, 10000, 0, 0, 2, 2, new(ArrivalEstimateStatus.Known, 5), Purpose: "withdraw:7");
        var observation = Input([active]) with { Places = [Target(1000, 5)], ActiveUnitIds = [7], Returns = [retreat] };
        ImmutableArray<CampaignGoal> memory = [goal];
        foreach (int threatened in new[] { 4, 5, 4, 5, 4, 5 })
        {
            var decision = CampaignPlanner.Decide(observation with { Waves = [new(threatened, 1, 2000)] }, memory, new(65536));
            Assert.Empty(decision.Candidates);
            Assert.Equal(3, Assert.Single(decision.Goals).TargetId);
            memory = decision.Goals;
        }
        var emergency = CampaignPlanner.Decide(observation with { Waves = [new(3, 2, 40000)] }, memory, new(65536));
        Assert.Equal(retreat, Assert.Single(emergency.Candidates));
    }

    [Fact]
    public void ExhaustedComparisonDoesNotTreatAnUnknownPlanAsAProvenFailure()
    {
        var active = Force(2, 10000) with { UnitId = 7 };
        var goal = new CampaignGoal(2, "ai:2", 3, 1, CampaignPhase.Attacking, new(190, 1, 1), new(190, 1, 1), 1, 20, [7], "public-conditions");
        var observation = Input([active]) with { ActiveUnitIds = [7] };
        var decision = CampaignPlanner.Decide(observation, [goal], new(2));
        Assert.Empty(decision.Candidates);
        Assert.Equal(goal, Assert.Single(decision.Goals));
    }

    [Fact]
    public void UnsustainableSupplyCannotPreventALegalResourceReducingReturn()
    {
        var home = new DomesticObservation(AiObservation.FromSnapshot(2, GameSession.Start(Fixture(), 42).Snapshot), []);
        var economy = new EconomicObservation(home, [new(2, 0, 0, 10000)], [], 0);
        var retreat = new AllocationCandidate("ai:2", 2, new OrderUnitCommand(Guid.Empty, 2, 0, 7,
            new(MilitaryOrderKind.Return, null, 2)), [], 0, 10000, 0, 0, Arrival: new(ArrivalEstimateStatus.Known, 5));
        var decision = SharedAllocator.Decide([("ai:2", economy)], [retreat], [], 100, new HashSet<string>(), new HashSet<string>());
        Assert.Equal(retreat, decision.Candidate);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PersonnelAndSoldierBottlenecksPrepareAnExecutableCampaign(bool personnel)
    {
        var basis = Fixture();
        var definition = personnel ? basis with
        {
            Settlements = [..basis.Settlements, new(4, "donor", 2, 3, 2, 3000, 100, 100, 100, [])],
            Officers = [..basis.Officers.Select(p => p.State.Id == 3 ? p with { State = p.State with { SettlementId = 4 } } : p),
                new("administrator", new OfficerState(4, 2, 4, new(70, 70, 95, 70), false).WithLoyalty(2, 100))],
            Military = basis.Military! with { Places = basis.Military.Places.Add(new(4, new(17, 12), new(1, 1)) { Combat = new(10, 10, 0, 10, 50) }) }
        } : basis with { Settlements = basis.Settlements.Select(s => s.Id == 2 ? s with { Soldiers = 3000, DraftPopulation = 10000 } : s).ToArray() };
        var session = GameSession.Start(definition, 42);
        var first = session.Apply(session.Request(new PrepareAdvanceOperation()));
        if (personnel) Assert.Contains(first.Events.OfType<TaskStartedEvent>(), e => e.Task.Kind == "personnel-move");
        else Assert.Contains(first.Events.OfType<DevelopmentCompletedEvent>(), e => e.Command.Activity == DomesticActivity.Recruit);
        Assert.Empty(first.Events.OfType<UnitDeployedEvent>());
        Assert.Equal(CampaignPhase.Preparing, Assert.Single(session.AiState.Campaigns).Phase);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        List<DomainEvent> events = [];
        for (int i = 0; i < 8; i++)
        {
            var next = session.AdvanceXun();
            Assert.Equal(CanonicalJson.Hash(next.Events), CanonicalJson.Hash(restored.AdvanceXun().Events));
            Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
            events.AddRange(next.Events);
        }
        Assert.Contains(events.OfType<UnitDeployedEvent>(), e => e.SettlementId == 2);
        Assert.Contains(events.OfType<SettlementCapturedEvent>(), e => e.SettlementId == 3);
    }

    [Fact]
    public void ADepartureRetainsTheReserveForAnEnemyGarrisonMobilizationWindow()
    {
        var basis = Fixture();
        var definition = basis with { Settlements = basis.Settlements.Select(s => s.Id == 3 ? s with { Soldiers = 12000, Durability = 20 } : s).ToArray() };
        var session = GameSession.Start(definition, 42);
        var observed = Observe(session, definition);
        var source = session.Snapshot.Settlements.Single(s => s.Id == 2);
        var threat = Assert.Single(observed.Waves.Where(w => w.TargetId == 2));
        var forces = observed.Forces.Where(f => f.Action.SourceId == 2).ToArray();
        Assert.NotEmpty(forces);
        Assert.All(forces, f => Assert.True(source.Soldiers - f.Soldiers >= threat.Soldiers));
    }

    [Fact]
    public void ReachableSupportWindowsGiveNearbyObjectivesRealMobilityValue()
    {
        var definition = Fixture();
        var observed = Observe(GameSession.Start(definition, 42), definition);
        Assert.True(observed.Complete);
        Assert.True(observed.Places.Single(p => p.Id == 3).Value > observed.Places.Single(p => p.Id == 1).Value);
    }

    [Fact]
    public void ACompatibleLearnedTacticIsEquippedByThePublicDeployment()
    {
        var basis = Fixture();
        var definition = basis with
        {
            Officers = basis.Officers.Select(p => p.State.Id == 3 ? p with { State = p.State with { LearnedTactics = [42] } } : p).ToArray(),
            Military = basis.Military! with { Tactics = [new(42, 0, TacticEffect.Martial, 10, 3, 0, true, true, [7])] }
        };
        var session = GameSession.Start(definition, 42);
        var result = session.Apply(session.Request(new PrepareAdvanceOperation()));
        var unit = Assert.Single(result.Events.OfType<UnitDeployedEvent>());
        Assert.Equal(42, session.Snapshot.Officers.Single(p => p.UnitId == unit.UnitId).EquippedTacticId);
    }

    [Fact]
    public void FastWeakerFormationCompetesOnActualObjectiveValue()
    {
        var strong = Force(2, 10000, 8);
        var fast = Force(2, 10000, 1) with { Profile = new(5, 5, 5, 5, 50) };
        fast = fast with { Action = fast.Action with
            { Intent = ((DeployUnitCommand)fast.Action.Intent) with { FormationId = 8 } } };
        var observation = Input([strong, fast]) with { Places = [Target(0) with { OwnerId = null, CanRetaliate = false }] };
        var decision = CampaignPlanner.Decide(observation, [], new(65536));
        Assert.Equal(8, Assert.IsType<DeployUnitCommand>(Assert.Single(decision.Candidates).Intent).FormationId);
    }

    [Fact]
    public void DefeatedReinforcementsAreNotCountedAgainAfterCapture()
    {
        var force = Force(2, 5000) with { Profile = new(1000, 1000, 5, 1000, 50), DamagesWalls = false };
        var forecast = CampaignCombatForecast.Evaluate(Target(1000) with { CanRetaliate = false },
            [force], [new(3, 0, 6000)], 0, () => true);
        Assert.True(forecast.Feasible);
        Assert.Equal(0, forecast.Losses);
    }

    [Fact]
    public void WallCollapseDoesNotEraseSurvivingReinforcements()
    {
        var force = Force(2, 5000) with { Profile = new(0, 0, 100, 1000, 50) };
        var forecast = CampaignCombatForecast.Evaluate(Target(10000, 1) with { CanRetaliate = false },
            [force], [new(3, 0, 8000)], 0, () => true);
        Assert.False(forecast.Feasible);
    }

    [Fact]
    public void JointPreparationCannotBorrowTheSameOfficerTwice()
    {
        var first = Force(2, 10000);
        var second = Force(4, 10000);
        second = second with { Action = second.Action with { Officers = first.Action.Officers,
            Intent = ((DeployUnitCommand)second.Action.Intent) with { OfficerIds = first.Action.Officers } } };
        Assert.Empty(CampaignPlanner.Decide(Input([first, second]), [], new(65536)).Candidates);
    }

    [Fact]
    public void ConsolidationReturnsRemainingUnitsEvenWhenOnlyAnotherHomeIsReachable()
    {
        var goal = new CampaignGoal(2, "ai:2", 3, 1, CampaignPhase.Attacking,
            new(190, 1, 1), new(190, 1, 1), 100000, 20, [7], "conditions");
        var retreat = new AllocationCandidate("ai:2", 2, new OrderUnitCommand(Guid.Empty, 2, 0, 7,
            new(MilitaryOrderKind.Return, null, 2)), [], 0, 10000, 0, 0,
            2, 2, new(ArrivalEstimateStatus.Known, 5), Purpose: "withdraw:7");
        var observation = Input([]) with { Places = [Target() with { OwnerId = 2 }], ActiveUnitIds = [7], Returns = [retreat] };
        Assert.Equal(retreat, Assert.Single(CampaignPlanner.Decide(observation, [goal], new(65536)).Candidates));
    }
}

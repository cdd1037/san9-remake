using San9.Tests;
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

    [Fact]
    public void HarborRouteSurvivesCandidateCommandRestoreAndActualMovement()
    {
        var original = Fixture();
        var definition = original with
        {
            Settlements = original.Settlements.Select(s => s with { X = s.Id == 3 ? 10 : s.X, Y = 0 })
                .Append(new(4, "harbor", 2, 3, 2, 3000, 100, 100, 100, [])).ToArray(),
            Military = original.Military! with
            {
                Height = 5,
                TerrainIds = Enumerable.Range(0, 100).Select(i => i / 20 > 0 && i % 20 is > 0 and < 10 ? 1 : 0).ToImmutableArray(),
                Terrains = [new(0, 1, null), new(1, null, 1) { IsWater = true }],
                Formations = original.Military.Formations.Select(f => f with { MovementBudget = 2 })
                    .Append(original.Military.Formations[0] with { Id = 8, MovementBudget = 20, TravelMode = TravelMode.Water }).ToImmutableArray(),
                BasicNavalFormationId = 8,
                Places = original.Military.Places.Select(p => p with { Position = new(p.Id == 3 ? 52 : p.Position.X, 2), FacilityTypeId = 10 })
                    .Append(new(4, new(17, 12), new(1, 1)) { FacilityTypeId = 9 }).ToImmutableArray(),
                Facilities = [new(9, 0, 100, new(1, 1), 0, false, true, true, 500000, null, null, null, []) { IsHarbor = true },
                    new(10, 0, 1000, new(1, 1), 0, false, true, true, 500000, null, null, null, [])]
            }
        };
        var session = GameSession.Start(definition, 42);
        var observation = Observe(session, definition);
        var choice = observation.Forces.First(f => f.Action.TargetId == 3 && f.Action.Intent is DeployUnitCommand { Waypoint: not null });
        var deploy = (DeployUnitCommand)choice.Action.Intent;
        session.EndControl(new("player"));
        var command = deploy with { CommandId = Guid.NewGuid(), Sequence = session.Snapshot.Simulation.NextSequence };
        var submitted = session.Execute(new("ai:2"), command);
        Assert.True(submitted.Submission.Accepted, submitted.Submission.Rejection.ToString());
        int unitId = Assert.Single(submitted.Events.OfType<UnitDeployedEvent>()).UnitId;
        Assert.Equal(command.Waypoint, session.Snapshot.Units.Single(u => u.Id == unitId).Waypoint);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        Assert.Equal(command.Waypoint, restored.Snapshot.Units.Single(u => u.Id == unitId).Waypoint);
        bool reached = false;
        for (int xun = 0; xun < 8 && !reached; xun++)
        {
            var result = session.AdvanceXun();
            EventAssert.Equal(result.Events, restored.AdvanceXun().Events);
            reached |= result.Events.OfType<UnitWaypointReachedEvent>().Any(e => e.Unit.Id == unitId && e.Position == command.Waypoint);
        }
        Assert.True(reached);
    }

    [Fact]
    public void DeploymentPreparationMatchesSubmissionWithoutMovingTheArmy()
    {
        var definition = Fixture();
        var session = GameSession.Start(definition, 190);
        var world = session.Snapshot;
        var engine = GameSession.EngineFactory(definition)(WorldState.Restore(world with
            { Simulation = world.Simulation with { ActiveControl = 1 } }));
        var command = new DeployUnitCommand(Guid.NewGuid(), 2, world.Simulation.NextSequence, 2, [3], 5000, 7,
            new(MilitaryOrderKind.AttackSettlement, null, 3));
        var before = engine.Snapshot;
        foreach (var query in new[] { command, command with { Soldiers = 50000 }, command with { OfficerIds = [1] },
            command with { DepartureDelayDays = 20 } })
        foreach (var context in new[] { new CommandContext("ai:2"), new CommandContext("forged") })
            Assert.Equal(engine.Validate(context, query).Rejection, engine.GetPreparationEligibility(context, query));
        StateAssert.Equal(before, engine.Snapshot);
        Assert.Equal(CommandRejection.None, engine.GetPreparationEligibility(new("ai:2"), command));
        Assert.True(engine.Execute(new("ai:2"), command).Submission.Accepted);
        Assert.NotEqual(CommandRejection.None, engine.GetPreparationEligibility(new("ai:2"), command with
            { CommandId = Guid.NewGuid(), Sequence = engine.Snapshot.Simulation.NextSequence }));
    }

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
        if (!result.Events.OfType<UnitDeployedEvent>().Any(e => e.SettlementId == 2))
            Assert.Fail(System.Text.Json.JsonSerializer.Serialize(session.AllocationDiagnostics.Batches));
        Assert.Single(session.AiState.Campaigns);
        Assert.Equal(3, Assert.Single(session.Snapshot.Units).Order!.SettlementId);
        Assert.True(Assert.Single(session.Snapshot.Units).Policies.AutonomousRetreat);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        List<DomainEvent> events = [];
        for (int i = 0; i < 6; i++)
        {
            var next = session.AdvanceXun();
            EventAssert.Equal(next.Events, restored.AdvanceXun().Events);
            StateAssert.Equal(session.Capture(), restored.Capture());
            events.AddRange(next.Events);
        }
        Assert.Contains(events.OfType<SettlementCapturedEvent>(), e => e.SettlementId == 3);
        Assert.Equal(2, session.Snapshot.Settlements.Single(s => s.Id == 3).OwnerId);
        Assert.Contains(events.OfType<DevelopmentCompletedEvent>(), e => e.Command.SettlementId == 3 &&
            e.Command.Activity is DomesticActivity.Patrol or DomesticActivity.Repair);
        Assert.DoesNotContain(session.Snapshot.Units, u => u.Order?.SettlementId == 3 && u.Order.Kind == MilitaryOrderKind.AttackSettlement);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
    }

    [Fact]
    public void ForeignPrivateTasksAndAbilitiesDoNotChangeTheCampaignObservation()
    {
        var definition = Fixture();
        var session = GameSession.Start(definition, 42);
        var captured = session.Capture();
        var changed = GameSession.Restore(definition, captured with { World = captured.World with
            { Officers = captured.World.Officers.Select(p => p.OwnerId == 1 ? p with { Acted = true, TacticCooldown = 9 } : p).ToArray() } });
        Assert.Equal(CanonicalJson.Text(Observe(session, definition)), CanonicalJson.Text(Observe(changed, definition)));
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
        walls, 100, 70, new(20, 20, 0, 20, 50), 50000);
    private static CampaignObservation Input(ImmutableArray<CampaignForce> forces) =>
        new(new(190, 1, 1), 2, [Target()], forces, [], [], [], [new(2, 100000, 100000, 1000) { HorizonDays = 90 }], true);

    [Theory]
    [InlineData(512)]
    [InlineData(2048)]
    [InlineData(8192)]
    [InlineData(65536)]
    public void JointSearchCanSkipAnUnaffordableNearSource(long budget)
    {
        var observation = Input([Force(2, 12000, cost: 100), Force(4, 12000, days: 2)]) with
        {
            Places = [Target(1000, 20)],
            Accounts = [new(2, 20, 100000, 1000) { HorizonDays = 90 }]
        };
        var work = new AllocationWork(budget);
        var result = CampaignPlanner.Decide(observation, [], work);
        Assert.Equal(4, Assert.Single(result.Candidates).SourceId);
        Assert.InRange(work.Remaining, 0, budget);
        var repeat = CampaignPlanner.Decide(observation, [], new(budget));
        Assert.Equal(result.Candidates.Select(c => (c.SourceId, c.Benefit)), repeat.Candidates.Select(c => (c.SourceId, c.Benefit)));
    }

    [Theory]
    [InlineData(512)]
    [InlineData(2048)]
    [InlineData(8192)]
    [InlineData(65536)]
    public void JointSearchSubstitutesAnOfficerInsteadOfDiscardingTheWholeAssembly(long budget)
    {
        var first = Force(2, 10000);
        var second = Force(4, 10000);
        var conflicting = second with { Leadership = 99, Action = second.Action with
        {
            Officers = first.Action.Officers,
            Intent = ((DeployUnitCommand)second.Action.Intent) with { OfficerIds = first.Action.Officers }
        } };
        var observation = Input([first, conflicting, second]);
        var work = new AllocationWork(budget);
        var result = CampaignPlanner.Decide(observation, [], work);
        Assert.Equal(2, result.Candidates.Length);
        Assert.Equal(2, result.Candidates.SelectMany(c => c.Officers).Distinct().Count());
        Assert.InRange(work.Remaining, 0, budget);
    }

    [Theory]
    [InlineData(2048)]
    [InlineData(8192)]
    [InlineData(65536)]
    public void JointSearchFindsAMiddleSpeedFormationThatCanAssemble(long budget)
    {
        var first = Force(2, 10000, days: 10);
        var slow = Force(4, 10000, days: 20) with { Profile = new(20, 20, 38, 20, 50), TacticValue = 10000 };
        var fast = Force(4, 100, days: 1);
        var compatible = Force(4, 9999, days: 11);
        compatible = compatible with { Action = compatible.Action with
            { Intent = ((DeployUnitCommand)compatible.Action.Intent) with { FormationId = 8 } } };
        var result = CampaignPlanner.Decide(Input([first, slow, fast, compatible]), [], new(budget));
        Assert.Equal(2, result.Candidates.Length);
        Assert.Contains(result.Candidates, c => c.Intent is DeployUnitCommand { FormationId: 8 });
        Assert.All(result.Candidates, c => Assert.Equal(11, c.Arrival!.Days));
    }

    [Fact]
    public void JointSearchMatchesSmallExhaustiveOracleAndKeepsItsIncumbentAsBudgetGrows()
    {
        CampaignForce[] left = [Force(2, 10000), Force(2, 11000, cost: 1000)];
        CampaignForce[] right = [Force(4, 10000), Force(4, 11000, cost: 2000)];
        var observation = Input([.. left, .. right]);
        long optimum = 0;
        // Independent finite oracle: either city can stay home or choose either deployment.
        for (int a = -1; a < left.Length; a++)
        for (int b = -1; b < right.Length; b++)
        {
            ImmutableArray<CampaignForce> chosen = [.. a < 0 ? Array.Empty<CampaignForce>() : [left[a]],
                .. b < 0 ? Array.Empty<CampaignForce>() : [right[b]]];
            if (chosen.IsEmpty) continue;
            var normal = CampaignCombatForecast.Evaluate(observation.Places[0], chosen, [], 0, () => true);
            var delayed = CampaignCombatForecast.Evaluate(observation.Places[0], chosen, [], 2, () => true);
            if (!normal.Feasible || !delayed.Feasible) continue;
            optimum = Math.Max(optimum, observation.Places[0].Value - delayed.Losses - delayed.Days * 100L - chosen.Sum(f => f.Action.MoneyCost));
        }
        Assert.True(optimum > 0);
        long previous = 0;
        foreach (long budget in new long[] { 512, 2048, 8192, 65536 })
        {
            var work = new AllocationWork(budget);
            var result = CampaignPlanner.Decide(observation, [], work);
            long value = result.Candidates.Select(c => c.Benefit).DefaultIfEmpty().Max();
            Assert.InRange(value, previous, optimum);
            Assert.InRange(work.Remaining, 0, budget);
            previous = value;
        }
        Assert.Equal(optimum, previous);
    }

    [Fact]
    public void UnfinishedAlternativesDoNotDiscardAValidatedLocalCampaign()
    {
        var observed = Input([Force(2, 12000)]) with
            { Places = [Target(1000, 20)], Complete = false, AnalysisReady = true };
        Assert.NotEmpty(CampaignPlanner.Decide(observed, [], new(65536)).Candidates);
        Assert.Empty(CampaignPlanner.Decide(observed with { AnalysisReady = false }, [], new(65536)).Candidates);
    }

    [Fact]
    public void UnresolvedCommittedArrivalIsNotEvidenceOfAnInvalidOrder()
    {
        var goal = new CampaignGoal(2, "ai:2", 3, 1, CampaignPhase.Attacking,
            new(190, 1, 1), new(190, 1, 1), 100000, 20, [7], Target(1000, 5).CaptureConditions());
        var observed = Input([]) with { Complete = false, AnalysisReady = true, ActiveUnitIds = [7], UnresolvedUnitIds = [7] };
        var decision = CampaignPlanner.Decide(observed, [goal], new(65536));
        Assert.Empty(decision.Candidates);
        Assert.Equal(goal, Assert.Single(decision.Goals));
    }

    [Fact]
    public void DistantGarrisonsDoNotConsumeRouteSearchBeforeALocalCampaign()
    {
        var basis = Fixture();
        var distant = Enumerable.Range(10, 40).Select(id => basis.Settlements[0] with
            { Id = id, NameKey = "distant-" + id, X = 1000 + id * 3, Soldiers = 10000 }).ToArray();
        var definition = basis with
        {
            Settlements = [..basis.Settlements, ..distant],
            Military = basis.Military! with { Width = 1200, TerrainIds = Enumerable.Repeat(0, 3600).ToImmutableArray(),
                Places = basis.Military.Places.AddRange(distant.Select(s => new BattlefieldPlace(s.Id, s.Position, new(1, 1))
                    { Combat = new(10, 10, 0, 10, 50) })) }
        };
        var session = GameSession.Start(definition, 42);
        using (var diagnostics = new PathSearchDiagnostics())
        {
            var threats = MilitarySituation.Capture(session.Snapshot, 2, session.MilitaryRules!)
                .Project(session.Snapshot.Settlements.Where(s => s.Id is 2 or 3).ToArray(), session.MilitaryRules!, new(65536), true, false);
            Assert.True(threats.Complete);
            Assert.Equal(0, diagnostics.Queries);
            Assert.Equal(threats.Arrivals.Length, threats.Arrivals.Select(a => (a.Source.UnitId, a.Source.SettlementId)).Distinct().Count());
            Assert.All(threats.Arrivals.Where(a => a.Source.SettlementId >= 10), a => Assert.True(a.EarliestDays > 100));
        }
        var observed = Observe(session, definition, 8192);
        Assert.True(observed.AnalysisReady);
        Assert.Contains(CampaignPlanner.Decide(observed, [], new(8192)).Candidates, c => c.TargetId == 3);
    }

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
        var goal = new CampaignGoal(2, "ai:2", 3, 1, CampaignPhase.Attacking, new(190, 1, 1), new(190, 1, 1), 100000, 20, [7], Target(1000, 5).CaptureConditions());
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
            EventAssert.Equal(next.Events, restored.AdvanceXun().Events);
            StateAssert.Equal(session.Capture(), restored.Capture());
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
        var goal = new CampaignGoal(2, "ai:2", 3, 1, CampaignPhase.Attacking, new(190, 1, 1), new(190, 1, 1), 1, 10, [7], Target(1000, 5).CaptureConditions());
        var observation = Input([active]) with { Clock = new(190, 2, 1), ActiveUnitIds = [7], Returns = [retreat] };
        var stalled = CampaignPlanner.Decide(observation, [goal], new(65536));
        Assert.Equal(retreat, Assert.Single(stalled.Candidates));
        Assert.Equal(CampaignPhase.Withdrawing, Assert.Single(stalled.Goals).Phase);
        var exited = CampaignPlanner.Decide(observation with { Forces = [], ActiveUnitIds = [], Returns = [] }, stalled.Goals, new(65536));
        Assert.Equal(CampaignPhase.CoolingDown, Assert.Single(exited.Goals).Phase);
        Assert.Empty(CampaignPlanner.Decide(observation with { Forces = [Force(2, 20000)] }, exited.Goals, new(65536)).Candidates);
        var changed = observation with { Forces = [Force(2, 20000)], Places = [Target(100, 10)] };
        Assert.NotEmpty(CampaignPlanner.Decide(changed, exited.Goals, new(65536)).Candidates);
    }

    [Fact]
    public void AlternatingFeintsDoNotRetargetTheCommittedArmyButARealCounterattackDoes()
    {
        var active = Force(2, 10000) with { UnitId = 7 };
        var goal = new CampaignGoal(2, "ai:2", 3, 1, CampaignPhase.Attacking, new(190, 1, 1), new(190, 1, 1), 100000, 20, [7], Target(1000, 5).CaptureConditions());
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
        var goal = new CampaignGoal(2, "ai:2", 3, 1, CampaignPhase.Attacking, new(190, 1, 1), new(190, 1, 1), 1, 20, [7], Target(1000, 5).CaptureConditions());
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
            EventAssert.Equal(next.Events, restored.AdvanceXun().Events);
            StateAssert.Equal(session.Capture(), restored.Capture());
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
            new(190, 1, 1), new(190, 1, 1), 100000, 20, [7], Target(1000, 5).CaptureConditions());
        var retreat = new AllocationCandidate("ai:2", 2, new OrderUnitCommand(Guid.Empty, 2, 0, 7,
            new(MilitaryOrderKind.Return, null, 2)), [], 0, 10000, 0, 0,
            2, 2, new(ArrivalEstimateStatus.Known, 5), Purpose: "withdraw:7");
        var observation = Input([]) with { Places = [Target() with { OwnerId = 2 }], ActiveUnitIds = [7], Returns = [retreat] };
        Assert.Equal(retreat, Assert.Single(CampaignPlanner.Decide(observation, [goal], new(65536)).Candidates));
    }
}

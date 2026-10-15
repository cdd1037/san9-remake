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
    public void OtherOfficerTrainingCanBeFollowedByDepartureInTheSameControlPhase()
    {
        var basis = Fixture();
        var definition = basis with { Settlements = basis.Settlements.Select(s => s.Id == 2 ? s with { Morale = 50 } : s).ToArray() };
        var session = GameSession.Start(definition, 42);
        var observation = Observe(session, definition);
        Assert.Contains(observation.Forces, f => f.Action.TargetId == 3 && f.Action.Intent is DeployUnitCommand && f.Morale == 50);
        var option = observation.Forces.First(f => f.Action.TargetId == 3 &&
            f.Action.Intent is DevelopCommand { Development.Activity: DomesticActivity.Train } &&
            f.Action.Continuation is { DelayDays: 0, Action.Intent: DeployUnitCommand });
        var training = (DevelopCommand)option.Action.Intent;
        var departure = (DeployUnitCommand)option.Action.Continuation!.Action.Intent;
        Assert.Empty(training.Development.OfficerIds.Intersect(departure.OfficerIds));
        session.EndControl(new("player"));
        var trained = session.Execute(new("ai:2"), training with { CommandId = Guid.NewGuid(), Sequence = session.Snapshot.Simulation.NextSequence });
        Assert.True(trained.Submission.Accepted);
        int morale = session.Snapshot.Settlements.Single(s => s.Id == 2).Morale;
        Assert.True(morale >= option.Morale);
        var deployed = session.Execute(new("ai:2"), departure with { CommandId = Guid.NewGuid(), Sequence = session.Snapshot.Simulation.NextSequence });
        Assert.True(deployed.Submission.Accepted);
        int unit = Assert.Single(deployed.Events.OfType<UnitDeployedEvent>()).UnitId;
        Assert.Equal(morale, session.Snapshot.Units.Single(u => u.Id == unit).Morale);
    }

    [Fact]
    public void ConfigurationDoesNotSpendTheLastDeploymentMoneyBeforeCampaignComparison()
    {
        var basis = Fixture();
        var definition = basis with
        {
            Officers = basis.Officers.Select(p => p.State.Id == 3 ? p with
                { State = p.State.WithLoyalty(2, 60) with { Righteousness = 0 } } : p).ToArray(),
            Information = basis.Information with { Corps = basis.Information.Corps.Select(c => c.Id == 2
                ? c with { Treasury = new(110, 100000) } : c).ToArray() }
        };
        var session = GameSession.Start(definition, 42);
        var result = session.Apply(session.Request(new PrepareAdvanceOperation()));
        Assert.Contains(result.Events.OfType<UnitDeployedEvent>(), e => e.SettlementId == 2);
        Assert.DoesNotContain(result.Events.OfType<OfficerRewardedEvent>(), e => e.OfficerId == 3);
    }

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
    [InlineData(512)]
    [InlineData(2048)]
    [InlineData(8192)]
    [InlineData(65536)]
    public void CrewObservationPreservesIndividualAndComplementaryAlternatives(long budget)
    {
        var basis = Fixture();
        var definition = basis with
        {
            Officers = [..basis.Officers,
                new("specialist", new OfficerState(4, 2, 2, new(90, 60, 10, 30), false).WithLoyalty(2, 100)),
                new("support", new OfficerState(5, 2, 2, new(70, 60, 10, 20), false).WithLoyalty(2, 100))]
        };
        var session = GameSession.Start(definition, 42);
        var observation = Observe(session, definition, budget);
        var crews = observation.Forces.Where(f => f.Action.TargetId == 3 && f.Action.Intent is DeployUnitCommand)
            .Select(f => string.Join(",", f.Action.Officers.Order())).ToHashSet();
        Assert.NotEmpty(crews);
        var expected = Enumerable.Range(1, 15).Select(mask => string.Join(",",
            Enumerable.Range(0, 4).Where(bit => (mask & (1 << bit)) != 0).Select(bit => bit + 2))).ToHashSet();
        Assert.True(crews.IsSubsetOf(expected));
        if (budget == 65536)
            foreach (int person in Enumerable.Range(2, 4)) Assert.Contains(crews, crew => crew.Split(',').Contains(person.ToString()));
        Assert.All(observation.Forces, f => Assert.Equal(f.Action.Officers.Length, f.Action.Officers.Distinct().Count()));
    }

    [Fact]
    public void AnAvailableAdministratorCanLeadAnImmediateSuccessfulAttack()
    {
        var basis = Fixture();
        var definition = basis with { Officers = basis.Officers.Where(p => p.State.Id != 3).ToArray() };
        var session = GameSession.Start(definition, 42);
        var first = session.Apply(session.Request(new PrepareAdvanceOperation()));
        var deployment = Assert.Single(first.Events.OfType<UnitDeployedEvent>());
        Assert.Equal(2, Assert.Single(deployment.OfficerIds));
        var events = new List<DomainEvent>();
        for (int i = 0; i < 4; i++) events.AddRange(session.AdvanceXun().Events);
        Assert.Contains(events.OfType<SettlementCapturedEvent>(), e => e.SettlementId == 3);
    }

    [Fact]
    public void WholeCrewDepartureUsesTheRemainingCommanderInsteadOfSingleDepartureMaxima()
    {
        var basis = Fixture();
        var definition = basis with { Officers = [..basis.Officers,
            new("reserve", new OfficerState(4, 2, 2, new(20, 20, 20, 20), false).WithLoyalty(2, 100))] };
        var session = GameSession.Start(definition, 42);
        var world = session.Snapshot;
        var source = world.Settlements.Single(s => s.Id == 2);
        var defense = new GarrisonAssessment(world, 2, session.MilitaryRules!,
            world.Officers.ToImmutableDictionary(p => p.Id, p => p.BaseAbilities),
            [new(99, null, 2, new(ArrivalEstimateStatus.Known, 3), 10000, 20000, true, "public attack")]);
        uint together = defense.NeedAfterDeparture(source, [2, 3]).Required;
        Assert.True(together > Math.Max(defense.Need(source, 2).Required, defense.Need(source, 3).Required));
        Assert.Equal(together, defense.NeedAfterDeparture(source, [3, 2]).Required);
        Assert.Equal(defense.Need(source).Required, defense.NeedAfterDeparture(source, [999]).Required);
    }

    [Fact]
    public void CampaignObservationResumesWithoutRepeatingProjectionOrRoutesAndRejectsChangedWorld()
    {
        var definition = Fixture();
        var session = GameSession.Start(definition, 42);
        var world = session.Snapshot;
        var engine = GameSession.EngineFactory(definition)(WorldState.Restore(world with
            { Simulation = world.Simulation with { ActiveControl = 1 } }));
        var seat = world.Simulation.Controls.Single(s => s.ForceId == 2);
        var economy = EconomicObservation.Capture(engine, seat, new(seat.ControllerId), 65536, quoteAll: true);
        var support = SupportObservationCapture.Capture(engine, seat, [(seat.ControllerId, economy)], session.MilitaryRules, new(65536));
        var fullWork = new AllocationWork(65536);
        CampaignObservation full;
        long fullQueries;
        using (var diagnostics = new PathSearchDiagnostics())
        {
            full = CampaignObservationCapture.Capture(engine, seat, [(seat.ControllerId, economy)], support,
                session.MilitaryRules!, fullWork, new());
            fullQueries = diagnostics.Queries;
        }
        using var capture = new CampaignObservationCapture(engine, seat, [(seat.ControllerId, economy)], support, session.MilitaryRules!, new());
        var slicedWork = new AllocationWork(65536);
        using (var diagnostics = new PathSearchDiagnostics())
        {
            while (!capture.Finished && slicedWork.Remaining > 0)
            {
                Assert.False(capture.Observation.Complete);
                capture.Advance(slicedWork, maximumSteps: 1);
            }
            Assert.Equal(fullQueries, diagnostics.Queries);
        }
        Assert.True(capture.Finished);
        Assert.Equal(fullWork.Remaining, slicedWork.Remaining);
        var sliced = capture.Observation;
        Assert.Equal(full.Complete, sliced.Complete);
        Assert.Equal(full.Places.AsEnumerable(), sliced.Places.AsEnumerable());
        Assert.Equal(full.Waves.AsEnumerable(), sliced.Waves.AsEnumerable());
        Assert.Equal(full.Forces.Length, sliced.Forces.Length);
        Assert.NotEmpty(full.Forces);
        for (int index = 0; index < full.Forces.Length; index++)
        {
            var expected = full.Forces[index];
            var actual = sliced.Forces[index];
            Assert.True(PlanningIntentEquality.Instance.Equals(expected.Action.Intent, actual.Action.Intent));
            Assert.Equal(expected.Action.Officers.AsEnumerable(), actual.Action.Officers.AsEnumerable());
            Assert.Equal(expected.Action, actual.Action with { Intent = expected.Action.Intent, Officers = expected.Action.Officers });
            Assert.Equal(expected, actual with { Action = expected.Action });
        }
        using var invalidated = new CampaignObservationCapture(engine, seat, [(seat.ControllerId, economy)], support, session.MilitaryRules!, new());
        invalidated.Advance(new(1));
        engine.SetPkSettings(true, true, true);
        var changed = engine.Snapshot;
        Assert.Throws<InvalidOperationException>(() => invalidated.Advance(new(65536)));
        StateAssert.Equal(changed, engine.Snapshot);
    }

    [Theory]
    [InlineData(512)]
    [InlineData(1024)]
    public void CampaignObservationBorrowsUnusedWorkAfterServingOrdinaryOpportunities(long budget)
    {
        var basis = Fixture();
        var definition = basis with { Military = basis.Military! with
        {
            Formations = basis.Military.Formations.Select(f => f with { Combat = f.Combat is null ? null : new(0, 0, 0, 20, 50),
                Attack = f.Attack is null ? null : new(1, false) }).ToImmutableArray()
                .AddRange(Enumerable.Range(100, 60).Select(id => basis.Military.Formations[0] with
                    { Id = id, Combat = new(0, 0, 0, 20, 50), Attack = new(1, false) }))
                .Add(basis.Military.Formations[0] with { Id = 180 })
        } };
        var session = GameSession.Start(definition, 42, aiPolicy: AiPolicyConfiguration.Standard with { WorkBudget = budget });
        session.AllocationDiagnosticsEnabled = true;
        var result = session.Apply(session.Request(new PrepareAdvanceOperation()));
        var turn = Assert.Single(session.AllocationDiagnostics.Batches.Where(b => b.IsTurnSummary && b.ForceId == 2));
        var deployment = Assert.Single(result.Events.OfType<UnitDeployedEvent>());
        Assert.Equal(180, session.Snapshot.Units.Single(u => u.Id == deployment.UnitId).FormationId);
        Assert.True(turn.WorkByKind.GetValueOrDefault("campaign-continuation") > 0);
        Assert.InRange(turn.Work, 0, budget);
        Assert.Equal(turn.Work, turn.WorkByKind.Values.Sum());
        Assert.DoesNotContain(session.AllocationDiagnostics.Actions, a => a.Outcome == AllocationOutcome.Rejected);
        for (int xun = 0; xun < 3 && session.Snapshot.Settlements.Single(s => s.Id == 3).OwnerId != 2; xun++)
            session.AdvanceXun();
        Assert.Equal(2, session.Snapshot.Settlements.Single(s => s.Id == 3).OwnerId);
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
        Assert.Contains(session.AiState.Campaigns, c => c.TargetId == 3);
        Assert.NotEmpty(session.Snapshot.Units);
        Assert.Contains(session.Snapshot.Units, u => u.Order!.SettlementId == 3);
        Assert.All(session.Snapshot.Units, u => Assert.True(u.Policies.AutonomousRetreat));
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
    public void OwnershipChangeWithdrawsAffectedUnitsAndPreservesTheOtherFront()
    {
        var definition = Fixture();
        var session = GameSession.Start(definition, 42);
        session.Apply(session.Request(new PrepareAdvanceOperation()));
        var capture = session.Capture();
        var units = capture.World.Units.Where(u => u.Order?.SettlementId == 3).ToArray();
        Assert.NotEmpty(units);
        var changed = GameSession.Restore(definition, capture with { World = capture.World with
            { Settlements = capture.World.Settlements.Select(s => s.Id == 3 ? s with { OwnerId = null, CorpsId = null } : s).ToArray(),
              Units = capture.World.Units.Select(u => u with { CommandedThisXun = false }).ToArray() } });
        var decision = CampaignPlanner.Decide(Observe(changed, definition), changed.AiState.Campaigns, new(65536));
        Assert.Equal(units.Select(u => u.Id).Order(), decision.Candidates.Select(c => Assert.IsType<OrderUnitCommand>(c.Intent).UnitId).Order());
        Assert.All(decision.Candidates, c => Assert.Equal(MilitaryOrderKind.Return, ((OrderUnitCommand)c.Intent).Order.Kind));
        Assert.Equal(CampaignPhase.Withdrawing, Assert.Single(decision.Goals, g => g.TargetId == 3).Phase);
        Assert.All(decision.Goals.Where(g => g.TargetId != 3), g => Assert.NotEqual(CampaignPhase.Withdrawing, g.Phase));
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

    [Theory]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(512)]
    [InlineData(2048)]
    [InlineData(8192)]
    public void CampaignSearchServesAnotherObjectiveBeforeRefiningAnUnwinnableOne(long budget)
    {
        var difficult = Enumerable.Range(0, 12).Select(i => Force(2, (uint)(5000 + i)) with
            { TacticValue = i }).ToImmutableArray();
        var attainable = Force(4, 12000);
        attainable = attainable with { Action = attainable.Action with { TargetId = 5,
            Intent = ((DeployUnitCommand)attainable.Action.Intent) with { Order = new(MilitaryOrderKind.AttackSettlement, null, 5) } } };
        var observation = Input(difficult.Add(attainable)) with
        {
            Places = [Target(50000, 10000) with { Value = 100000, CanRetaliate = false }, Target(1000, 20) with { Id = 5 }]
        };
        var work = new AllocationWork(budget);
        var result = CampaignPlanner.Decide(observation, [], work);
        Assert.Equal(5, Assert.Single(result.Candidates).TargetId);
        Assert.InRange(work.Remaining, 0, budget);
    }

    [Fact]
    public void TravelBeforeContactDoesNotConsumeCombatBudgetOrLoseEarlierReinforcements()
    {
        var target = Target(1000, 100) with { CanRetaliate = false };
        int nearWork = 0, farWork = 0;
        var near = CampaignCombatForecast.Evaluate(target, [Force(2, 20000, days: 10)],
            [new(3, 8, 2000), new(3, 11, 3000)], 0, () => ++nearWork <= 100);
        var far = CampaignCombatForecast.Evaluate(target, [Force(2, 20000, days: 10000)],
            [new(3, 9998, 2000), new(3, 10001, 3000)], 0, () => ++farWork <= 100);
        Assert.True(near.Feasible);
        Assert.Equal(near with { Days = near.Days + 9990 }, far);
        Assert.Equal(nearWork, farWork);

        // Reinforcements reaching a wall before the army are still surviving field forces after collapse.
        var exposed = CampaignCombatForecast.Evaluate(target with { Durability = 1 },
            [Force(2, 5000, days: 10000) with { Profile = new(0, 0, 100, 1000, 50) }],
            [new(3, 9998, 8000)], 0, () => true);
        Assert.False(exposed.Feasible);
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

    [Theory]
    [InlineData(512)]
    [InlineData(2048)]
    [InlineData(8192)]
    [InlineData(65536)]
    public void SameCityJointPlanRespectsCombinedSoldiersAndRemainingCommander(long budget)
    {
        var first = Force(2, 10000);
        var second = Force(2, 10000) with { Action = first.Action with { Officers = [13],
            Intent = ((DeployUnitCommand)first.Action.Intent) with { OfficerIds = [13] } } };
        var source = new CampaignSource(2, 24000, 0, true, [new(12, 90), new(13, 90)],
            new Dictionary<int, uint> { [90] = 3000, [50] = 4000 }.ToImmutableDictionary());
        var observation = Input([first, second]) with { Sources = [source] };
        var plan = CampaignPlanner.Decide(observation, [], new(budget));
        Assert.Equal(2, plan.Candidates.Length);
        Assert.Equal(20000, plan.Candidates.Sum(c => (long)((DeployUnitCommand)c.Intent).Soldiers));
        var weakReserve = source with { Reserves = source.Reserves.SetItem(50, 10000) };
        Assert.Empty(CampaignPlanner.Decide(observation with { Sources = [weakReserve] }, [], new(budget)).Candidates);
        Assert.Empty(CampaignPlanner.Decide(observation with { Sources = [source with { Soldiers = 19000 }] }, [], new(budget)).Candidates);
    }

    [Theory]
    [InlineData(512)]
    [InlineData(2048)]
    [InlineData(8192)]
    [InlineData(65536)]
    public void CompetingFrontsUseAnAlternativeCommanderAndShareTheAccount(long budget)
    {
        var first = Force(2, 5000);
        var contested = first with { Action = first.Action with { TargetId = 4,
            Intent = ((DeployUnitCommand)first.Action.Intent) with { Order = new(MilitaryOrderKind.AttackSettlement, null, 4) } } };
        var alternative = Force(6, 5000, days: 2) with { Leadership = 40 };
        alternative = alternative with { Action = alternative.Action with { TargetId = 4,
            Intent = ((DeployUnitCommand)alternative.Action.Intent) with { Order = new(MilitaryOrderKind.AttackSettlement, null, 4) } } };
        var target = Target(0, 1) with { OwnerId = null, CanRetaliate = false };
        var input = Input([first, contested, alternative]) with { Places = [target, target with { Id = 4, Value = 40000 }] };
        var plan = CampaignPlanner.Decide(input, [], new(budget));
        Assert.Equal(2, plan.Candidates.Length);
        Assert.Equal(new[] { 3, 4 }, plan.Candidates.Select(c => c.TargetId!.Value).Order().ToArray());
        Assert.Equal(2, plan.Candidates.SelectMany(c => c.Officers).Distinct().Count());
        var poor = input with { Accounts = [input.Accounts[0] with { Money = 10 }] };
        Assert.Single(CampaignPlanner.Decide(poor, [], new(budget)).Candidates);
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
    public void CampaignPreparationAndLowMoraleChoicesExecuteAndReplay(bool food)
    {
        var basis = Fixture();
        uint garrisonFood = checked((uint)Observe(GameSession.Start(basis, 42), basis).Accounts.Single(a => a.Id == 2).RequiredFood);
        var definition = basis with
        {
            Settlements = basis.Settlements.Select(s => !food && s.Id == 2 ? s with { Morale = 60 } : s).ToArray(),
            Information = basis.Information with { Corps = basis.Information.Corps.Select(c => food && c.OwnerId == 2
                ? c with { Treasury = new(100000, garrisonFood) } : c).ToArray() }
        };
        var session = GameSession.Start(definition, 42);
        var first = session.Apply(session.Request(new PrepareAdvanceOperation()));
        if (food) Assert.Contains(first.Events, e => e is FoodTradeCompletedEvent);
        if (!food)
        {
            Assert.Contains(first.Events, e => e is UnitDeployedEvent ||
                e is DevelopmentCompletedEvent { Command.Activity: DomesticActivity.Train });
            if (!first.Events.OfType<UnitDeployedEvent>().Any())
                Assert.Contains(session.AiState.Campaigns, g => g.Phase == CampaignPhase.Preparing);
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
            Officers = [..basis.Officers.Select(p => p.State.OwnerId == 2 ? p with { State = p.State with { SettlementId = 4 } } : p),
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

    [Theory]
    [InlineData(7ul)]
    [InlineData(42ul)]
    [InlineData(190ul)]
    public void LowerLeadershipMartialSpecialistChangesTheCampaignAndExecutesLegally(ulong seed)
    {
        var basis = Fixture();
        var definition = basis with
        {
            Settlements = basis.Settlements.Select(s => s.Id == 3 ? s with { Durability = 1000, MaxDurability = 1000, Soldiers = 5000 } : s).ToArray(),
            Officers = [..basis.Officers, new("specialist", new OfficerState(4, 2, 2, new(100, 50, 0, 30), false)
                .WithLoyalty(2, 100) with { LearnedTactics = [42], TacticProficiencies = [1000, 0, 0, 0, 0, 0, 0, 0, 0] })],
            Military = basis.Military! with
            {
                Tactics = [new(42, 0, TacticEffect.Martial, 20, 3, 0, true, true, [7])],
                Formations = basis.Military.Formations.Select(f => f.Id == 7 ? f with
                    { Attack = new(1, false), Tactics = new(100, 0, 2, [0], []) } : f).ToImmutableArray()
            }
        };
        var session = GameSession.Start(definition, seed);
        var observation = Observe(session, definition);
        observation = observation with { Places = observation.Places.Where(p => p.Id == 3).ToImmutableArray(),
            Forces = observation.Forces.Where(f => f.Action.TargetId == 3).ToImmutableArray() };
        var baseline = CampaignPlanner.Decide(observation with
            { Forces = observation.Forces.Select(f => f with { Tactics = null, TacticValue = 0 }).ToImmutableArray() }, [], new(65536));
        var improved = CampaignPlanner.Decide(observation, [], new(65536));
        long SpecialistTroops(CampaignDecision decision) => decision.Candidates.Where(c => c.Officers.Contains(4))
            .Sum(c => (long)((DeployUnitCommand)c.Intent).Soldiers);
        Assert.True(SpecialistTroops(improved) > SpecialistTroops(baseline));
        var first = session.Apply(session.Request(new PrepareAdvanceOperation()));
        Assert.Contains(first.Events.OfType<UnitDeployedEvent>(), e => e.OfficerIds.Contains(4));
        var events = new List<DomainEvent>();
        for (int i = 0; i < 6; i++) events.AddRange(session.AdvanceXun().Events);
        Assert.Contains(events.OfType<TacticActivatedEvent>(), e => e.OfficerIds.Contains(4));
        Assert.Contains(events.OfType<SettlementCapturedEvent>(), e => e.SettlementId == 3);
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
        var unit = Assert.Single(result.Events.OfType<UnitDeployedEvent>(), e => e.OfficerIds.Contains(3));
        Assert.Equal(42, session.Snapshot.Officers.Single(p => p.Id == 3 && p.UnitId == unit.UnitId).EquippedTacticId);
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

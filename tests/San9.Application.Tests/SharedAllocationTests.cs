using System.Collections.Immutable;
using San9.Application.AI;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class SharedAllocationTests
{
    private static ScenarioInitialState Fixture(int targetX = 5, int enemyX = 17) => new(
        ScenarioInitialState.CurrentSchemaVersion, "shared-allocation", 190, 1, 1, 1,
        [new(1, "opponent", 1, 100) { LeaderId = 1 }, new(2, "automatic", 2, 100) { LeaderId = 2 }],
        [new(1, "enemy", 1, enemyX, 1, 20000, 100, 100, 100, []),
         new(2, "source", 2, 0, 1, 12000, 100, 100, 100, []) { MerchantPresenceChance = 100 },
         new(3, "target", 2, targetX, 1, 1000, 100, 100, 100, []) { MerchantPresenceChance = 100 }], [], [])
    {
        Officers = [new("enemy-ruler", new(1, 1, 1, new(70, 70, 70, 70), false) { Status = OfficerStatus.Ruler }),
            new("ruler", new(2, 2, 2, new(70, 70, 90, 70), false) { Status = OfficerStatus.Ruler }),
            new("general", new(3, 2, 2, new(70, 70, 20, 70), false))],
        Military = new(20, 3, Enumerable.Repeat(0, 60).ToImmutableArray(), [new(0, 1, null)],
            [new(7, 2, 10, 20000, 5, new(1, 1), TravelMode.Land)
                { Combat = new(10, 10, 10, 10, 50), Attack = new(1, true),
                  CaptureRanks = [BattleRank.Middle, BattleRank.Middle, BattleRank.Middle, BattleRank.Middle, BattleRank.Middle] },
             new(70, 2, 10, 20000, 5, new(1, 1), TravelMode.Land)],
            [new(1, new(enemyX * 5 + 2, 7), new(1, 1)) { Combat = new(10, 10, 0, 10, 50) },
             new(2, new(2, 7), new(1, 1)) { Combat = new(10, 10, 0, 10, 50) },
             new(3, new(targetX * 5 + 2, 7), new(1, 1)) { Combat = new(10, 10, 0, 10, 50) }]) { TransportFormations = new(70, null) },
        Information = new([], [], [], [new CorpsState(1, 1, 1, new(100000, 100000)) { IsRoot = true },
            new CorpsState(2, 2, 2, new(100000, 100000)) { IsRoot = true }], [])
    };

    private static GameSession WithThreat(ScenarioInitialState initial, ulong seed = 42)
    {
        var session = GameSession.Start(initial, seed);
        Assert.True(session.Execute(new("player"), new DeployUnitCommand(Guid.NewGuid(), 1, 0, 1, [1], 8000, 7,
            new(MilitaryOrderKind.AttackSettlement, null, 3))).Submission.Accepted);
        return session;
    }

    private static (EconomicObservation Economy, SupportObservation Support) Observe(GameSession session, ScenarioInitialState definition, long budget = 65536)
    {
        var world = session.Snapshot;
        var engine = GameSession.EngineFactory(definition)(WorldState.Restore(world));
        var seat = world.Simulation.Controls.Single(s => s.ForceId == 2);
        var economy = EconomicObservation.Capture(engine, seat, new(seat.ControllerId), budget, quoteAll: true);
        var support = SupportObservationCapture.Capture(engine, seat, [(seat.ControllerId, economy)], session.MilitaryRules, new(budget));
        return (economy, support);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(42)]
    [InlineData(190)]
    public void TimelyRescuePreservesAdministratorAndForecastMatchesPublicDelivery(ulong seed)
    {
        var definition = Fixture();
        var session = WithThreat(definition, seed);
        var (_, observed) = Observe(session, definition);
        var planned = observed.Candidates.Where(c => c.Priority == 0).MinBy(c => c.OfficerOpportunityCost)!;
        Assert.True(planned.Arrival!.Days <= planned.DeadlineDays);
        Assert.Equal(3, Assert.Single(planned.Officers));
        var before = session.Snapshot;
        session.AllocationDiagnosticsEnabled = true;
        session.BeginRecording();
        var first = session.Apply(session.Request(new PrepareAdvanceOperation()));
        var deployment = Assert.Single(first.Events.OfType<UnitDeployedEvent>());
        Assert.Equal(planned.Officers.ToArray(), deployment.OfficerIds.ToArray());
        var unit = session.Snapshot.Units.Single(u => u.Id == deployment.UnitId);
        Assert.Equal(UnitPurpose.Transport, session.MilitaryRules!.Purpose(unit.FormationId!.Value));
        Assert.True(session.Snapshot.Settlements.Single(s => s.Id == 2).Soldiers >= 3000);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        List<DomainEvent> events = [];
        for (int i = 0; i < 3; i++)
        {
            var result = session.AdvanceXun();
            Assert.Equal(CanonicalJson.Hash(result.Events), CanonicalJson.Hash(restored.AdvanceXun().Events));
            Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
            events.AddRange(result.Events);
            if (i == 0) Assert.DoesNotContain(result.Events.OfType<UnitDeployedEvent>(), e => e.SettlementId == 2);
        }
        var arrival = Assert.Single(events.OfType<UnitAbsorbedEvent>(), e => e.UnitId == unit.Id);
        Assert.Equal((long)planned.Arrival.Days!.Value, arrival.Clock.DayNumber - before.Clock.DayNumber);
        var diagnostic = Assert.Single(session.AllocationDiagnostics.Actions, a => a.UnitId == unit.Id);
        Assert.Equal(AllocationOutcome.Completed, diagnostic.Outcome);
        Assert.Equal(0, diagnostic.ArrivalError);
        Assert.Equal(0, diagnostic.FoodExpenditureError);
        Assert.Equal(0, diagnostic.SoldierLoss);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        Assert.Equal(session.Capture().StateHash, replay.Session.Capture().StateHash);
    }

    [Fact]
    public void LateRouteAndIncompleteSearchAreDifferentAndNeitherAuthorizesRescue()
    {
        var definition = Fixture(12, 15);
        var session = WithThreat(definition);
        var (_, observed) = Observe(session, definition);
        Assert.True(observed.Complete);
        Assert.DoesNotContain(observed.Candidates, c => c.Priority == 0);
        Assert.Contains(observed.Arrivals, a => !a.Hostile && a.SourceId == 2 && a.Estimate.Status == ArrivalEstimateStatus.Known);
        var (_, incomplete) = Observe(session, definition, 1);
        Assert.False(incomplete.Complete);
        Assert.Contains(incomplete.Arrivals, a => a.Estimate.Status == ArrivalEstimateStatus.Incomplete);
        var result = session.Apply(session.Request(new PrepareAdvanceOperation()));
        Assert.DoesNotContain(result.Events.OfType<UnitDeployedEvent>(), e => e.SettlementId == 2 &&
            session.Snapshot.Units.Any(u => u.Id == e.UnitId && u.Order?.Kind == MilitaryOrderKind.Transport));
    }

    [Fact]
    public void ForeignOrdersAndDepartureStateDoNotBecomePlanningKnowledge()
    {
        var definition = Fixture();
        var session = WithThreat(definition);
        var before = session.Capture();
        var changed = GameSession.Restore(definition, before with { World = before.World with
        {
            Units = before.World.Units.Select(u => u.OwnerId == 1 ? u with { Order = new(MilitaryOrderKind.Move, new(77, 2), null),
                DepartureDaysRemaining = 9, MovementRemainder = 0 } : u).ToArray()
        } });
        Assert.Equal(CanonicalJson.Hash(Observe(session, definition).Support), CanonicalJson.Hash(Observe(changed, definition).Support));
    }

    [Fact]
    public void SupplyPurchaseLeavesTheOnlyTimelyRescueOfficerAvailable()
    {
        var definition = Fixture();
        definition = definition with { Information = definition.Information with { Corps = definition.Information.Corps.Select(c =>
            c.OwnerId == 2 ? c with { Treasury = new(100000, 0) } : c).ToArray() } };
        var session = WithThreat(definition);
        session.AllocationDiagnosticsEnabled = true;
        var result = session.Apply(session.Request(new PrepareAdvanceOperation()));
        var purchase = Assert.Single(result.Events.OfType<FoodTradeCompletedEvent>());
        Assert.Equal(2, Assert.Single(purchase.OfficerIds));
        var rescue = Assert.Single(result.Events.OfType<UnitDeployedEvent>());
        Assert.Equal(3, Assert.Single(rescue.OfficerIds));
        Assert.Empty(session.AiState.FoodSecurity);
    }

    [Fact]
    public void OfficerBottleneckGeneratesAMoveWithTheNextCommandBoundaryIncluded()
    {
        var definition = Fixture(enemyX: 19);
        definition = definition with { Officers = definition.Officers.Select(o => o.State.OwnerId == 2
            ? o with { State = o.State with { SettlementId = 3 } } : o).ToArray() };
        var session = WithThreat(definition);
        var (_, observation) = Observe(session, definition);
        var move = Assert.Single(observation.Candidates, c => c.Intent is MoveOfficersCommand);
        var transfer = Assert.IsType<MoveOfficersCommand>(move.Intent);
        Assert.Equal(2, transfer.DestinationId);
        Assert.True(move.Arrival!.Days >= 10);
        Assert.True(move.Arrival.Days <= move.DeadlineDays);
        var result = session.Apply(session.Request(new PrepareAdvanceOperation()));
        Assert.Contains(result.Events.OfType<TaskStartedEvent>(), e => e.Task.Kind == "personnel-move");
        Assert.DoesNotContain(result.Events.OfType<DevelopmentCompletedEvent>(), e => e.Command.SettlementId == 2 && e.Command.Activity == DomesticActivity.Recruit);
    }

    [Fact]
    public void IndependentAutomaticAccountsDeliverFoodWithoutBorrowingOrDuplicatingAnInFlightShipment()
    {
        var definition = Fixture();
        definition = definition with
        {
            Settlements = definition.Settlements.Select(s => s.Id == 3 ? s with { CorpsId = 3, MerchantPresenceChance = 0 } : s).ToArray(),
            Officers = [.. definition.Officers, new("receiver", new(4, 2, 3, new(70, 70, 70, 70), false))],
            Information = definition.Information with { Corps = [.. definition.Information.Corps,
                new(3, 2, 4, new(0, 200)) { ControlMode = CorpsControlMode.Automatic }] }
        };
        var session = GameSession.Start(definition, 42);
        session.AllocationDiagnosticsEnabled = true;
        var first = session.Apply(session.Request(new PrepareAdvanceOperation()));
        Assert.Equal(SessionDisposition.Accepted, first.Disposition);
        var deployment = Assert.Single(first.Events.OfType<UnitDeployedEvent>());
        var transport = Assert.Single(session.Snapshot.Units);
        Assert.Equal(deployment.UnitId, transport.Id);
        Assert.True(transport.CargoFood > 0);
        Assert.Equal(200u, session.Snapshot.Corps.Single(c => c.Id == 3).Treasury.Food);
        Assert.Equal(0u, session.Snapshot.Corps.Single(c => c.Id == 3).Treasury.Money);
        Assert.DoesNotContain(session.AiState.FoodSecurity, g => g.AccountId == 3);
        var next = session.AdvanceXun();
        Assert.Empty(next.Events.OfType<UnitDeployedEvent>());
        Assert.Equal((uint)next.Events.OfType<ResourceChangedEvent>().Where(e => e.Account == new ResourceAccount(ResourceOwnerKind.Corps, 3) && e.Resource == ResourceKind.Money).Sum(e => e.Actual),
            session.Snapshot.Corps.Single(c => c.Id == 3).Treasury.Money);
        var delivered = session.AdvanceXun();
        Assert.Contains(delivered.Events.OfType<UnitAbsorbedEvent>(), e => e.UnitId == transport.Id);
        Assert.True(session.Snapshot.Corps.Single(c => c.Id == 3).Treasury.Food > 200);
        Assert.Equal(0, Assert.Single(session.AllocationDiagnostics.Actions, a => a.UnitId == transport.Id).ArrivalError);
    }

    [Fact]
    public void DiagnosticsAreBoundedAndDoNotChangeEventsStateOrWork()
    {
        var definition = Fixture();
        var plain = WithThreat(definition);
        var detailed = GameSession.Restore(definition, plain.Capture());
        detailed.AllocationDiagnosticsEnabled = true;
        var a = plain.Apply(plain.Request(new PrepareAdvanceOperation()));
        var b = detailed.Apply(detailed.Request(new PrepareAdvanceOperation()));
        Assert.Equal(CanonicalJson.Hash(a.Events), CanonicalJson.Hash(b.Events));
        Assert.Equal(plain.Capture().StateHash, detailed.Capture().StateHash);
        Assert.NotEmpty(detailed.AllocationDiagnostics.Actions);
        Assert.Empty(plain.AllocationDiagnostics.Actions);
        Assert.All(detailed.AllocationDiagnostics.Actions, trace => Assert.InRange(trace.WorkRemaining, 0, detailed.AiState.Configuration.WorkBudget));
        Assert.True(detailed.AllocationDiagnostics.Actions.Length <= AllocationDiagnostics.Capacity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void ExplicitNavigationMatchesTheWorldForecastWithPreparationAndWaypoint(int preparation)
    {
        var definition = Fixture();
        var session = GameSession.Start(definition, 42);
        var rules = session.MilitaryRules!;
        var occupied = MilitaryRouteQuery.Occupancy(session.Snapshot, rules, 2, new(), includeUnits: false).ToImmutableArray();
        var start = rules.Place(session.Snapshot.Settlements.Single(s => s.Id == 2)).Position;
        var target = rules.SettlementArea(session.Snapshot.Settlements.Single(s => s.Id == 3));
        var via = new WorldPosition(12, 2);
        var explicitEstimate = NavigationArrivalForecast.Evaluate(new(rules.Travel(70, null), start, target, occupied,
            PreparationDays: preparation, Waypoint: via), new(100000));
        var existing = new MilitaryArrivalForecast(session.Snapshot, rules).EstimateDeployment(new(2, 70, null,
            new(MilitaryOrderKind.Transport, null, 3), new(), preparation, via));
        Assert.Equal(existing, explicitEstimate);
    }

    [Fact]
    public void AnOnlyBuyerCanResolveFoodEvenWhenItCannotAlsoDepartAsTheRescueCommander()
    {
        var definition = Fixture();
        definition = definition with
        {
            Officers = definition.Officers.Where(o => o.State.Id != 3).ToArray(),
            Information = definition.Information with { Corps = definition.Information.Corps.Select(c => c.OwnerId == 2
                ? c with { Treasury = new(100000, 0) } : c).ToArray() }
        };
        var session = WithThreat(definition);
        var result = session.Apply(session.Request(new PrepareAdvanceOperation()));
        Assert.Single(result.Events.OfType<FoodTradeCompletedEvent>());
        Assert.Empty(result.Events.OfType<UnitDeployedEvent>());
        Assert.Empty(session.AiState.FoodSecurity);
    }

    [Fact]
    public void RearDefenseAccountsForTheLeadershipOfTheOfficerWhoWouldLeave()
    {
        var definition = Fixture();
        definition = definition with
        {
            Settlements = definition.Settlements.Select(s => s.Id == 1 ? s with { Soldiers = 40000 } : s).ToArray(),
            Officers = [.. definition.Officers.Select(o => o.State.Id == 2 ? o with { State = o.State with { BaseAbilities = new(70, 70, 90, 10) } } :
                o.State.Id == 3 ? o with { State = o.State with { BaseAbilities = new(70, 70, 20, 90) } } : o),
                new("second-enemy", new(5, 1, 1, new(70, 70, 70, 70), false))]
        };
        var session = WithThreat(definition);
        Assert.True(session.Execute(new("player"), new DeployUnitCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence,
            1, [5], 8000, 7, new(MilitaryOrderKind.AttackSettlement, null, 2))).Submission.Accepted);
        var captured = session.Capture();
        session = GameSession.Restore(definition, captured with { World = captured.World with
            { Units = captured.World.Units.Select(u => u.CommanderId == 5 ? u with { Position = new(7, 7) } : u).ToArray() } });
        var support = Observe(session, definition).Support;
        Assert.Contains(support.Candidates, c => c.SourceId == 2 && c.Officers.Contains(2));
        Assert.DoesNotContain(support.Candidates, c => c.SourceId == 2 && c.Officers.Contains(3));
        var result = session.Apply(session.Request(new PrepareAdvanceOperation()));
        var deployed = Assert.Single(result.Events.OfType<UnitDeployedEvent>());
        Assert.Equal(2, Assert.Single(deployed.OfficerIds));
        Assert.Equal(2, session.Snapshot.Officers.Single(o => o.Id == 3).SettlementId);
    }

    [Fact]
    public void ATransferThatCannotFinishPreparationBeforeTheThreatIsNotIssued()
    {
        var definition = Fixture(12, 15);
        definition = definition with { Officers = definition.Officers.Select(o => o.State.OwnerId == 2
            ? o with { State = o.State with { SettlementId = 3 } } : o).ToArray() };
        var session = WithThreat(definition);
        var support = Observe(session, definition).Support;
        Assert.DoesNotContain(support.Candidates, c => c.Intent is MoveOfficersCommand);
        Assert.Contains(support.Arrivals, a => !a.Hostile && a.SourceId == 2 &&
            a.Estimate.Status == ArrivalEstimateStatus.Known && a.Estimate.Days > 7);
        var result = session.Apply(session.Request(new PrepareAdvanceOperation()));
        Assert.DoesNotContain(result.Events.OfType<TaskStartedEvent>(), e => e.Task.Kind == "personnel-move");
    }

    [Fact]
    public void CommittedReinforcementMovesTheRescueDeadlineToTheUncoveredWave()
    {
        var definition = Fixture(enemyX: 19);
        definition = definition with
        {
            Settlements = definition.Settlements.Select(s => s.Id == 2 ? s with { Soldiers = 24000 } : s).ToArray(),
            Officers = [.. definition.Officers,
                new("transport", new(4, 2, 2, new(70, 70, 20, 70), false)),
                new("second-enemy", new(5, 1, 1, new(70, 70, 70, 70), false))]
        };
        var session = GameSession.Start(definition, 42);
        Assert.True(session.Execute(new("player"), new DeployUnitCommand(Guid.NewGuid(), 1, 0, 1, [1], 3000, 7,
            new(MilitaryOrderKind.AttackSettlement, null, 3))).Submission.Accepted);
        Assert.True(session.Execute(new("player"), new DeployUnitCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence,
            1, [5], 8000, 7, new(MilitaryOrderKind.AttackSettlement, null, 3))).Submission.Accepted);
        session.EndControl(new("player"));
        string controller = session.Snapshot.Simulation.Controls.Single(s => s.ForceId == 2).ControllerId;
        Assert.True(session.Execute(new(controller), new TransportUnitCommand(Guid.NewGuid(), 2,
            session.Snapshot.Simulation.NextSequence, 2, [4], 6000, 3)).Submission.Accepted);
        var capture = session.Capture();
        capture = capture with { World = capture.World with { Units = capture.World.Units.Select(u => u with
            { Position = u.CommanderId == 1 ? new(37, 7) : u.CommanderId == 5 ? new(87, 7) : new(22, 7) }).ToArray() } };
        session = GameSession.Restore(definition, capture);
        var support = Observe(session, definition).Support;
        var threats = support.Arrivals.Where(a => a.Hostile).OrderBy(a => a.Estimate.Days).ToArray();
        var rescue = Assert.Single(support.Candidates.Where(c => c.Officers.Contains(3) && c.TargetId == 3));
        Assert.True(rescue.Arrival!.Days > threats[0].Estimate.Days);
        Assert.True(rescue.Arrival.Days <= threats[1].Estimate.Days);
        Assert.Equal(threats[1].Estimate.Days, rescue.DeadlineDays);
        var result = session.Apply(session.Request(new PrepareAdvanceOperation()));
        Assert.Contains(result.Events.OfType<UnitDeployedEvent>(), e => e.SettlementId == 2);

        // The same reinforcements cannot cover an earlier threat when their departure is delayed.
        var late = GameSession.Restore(definition, capture with { World = capture.World with
            { Units = capture.World.Units.Select(u => u.OwnerId == 2 ? u with { DepartureDaysRemaining = 9 } : u).ToArray() } });
        Assert.DoesNotContain(Observe(late, definition).Support.Candidates, c => c.TargetId == 3 && c.Priority == 0);
    }

    [Fact]
    public void PartialQuotesLeaveEnoughBudgetToBuyFood()
    {
        var definition = Fixture();
        definition = definition with
        {
            Officers = [.. definition.Officers, .. Enumerable.Range(4, 8).Select(id =>
                new OfficerInitialState("extra-" + id, new(id, 2, 2, new(70, 70, 70, 70), false)))],
            Information = definition.Information with { Corps = definition.Information.Corps.Select(c => c.OwnerId == 2
                ? c with { Treasury = new(100000, 0) } : c).ToArray() }
        };
        var session = GameSession.Start(definition, 42, aiPolicy: AiPolicyConfiguration.Standard with { WorkBudget = 16 });
        session.AllocationDiagnosticsEnabled = true;
        var result = session.Apply(session.Request(new PrepareAdvanceOperation()));
        Assert.Equal(SessionDisposition.Accepted, result.Disposition);
        Assert.Single(result.Events.OfType<FoodTradeCompletedEvent>());
        Assert.True(session.Snapshot.Corps.Single(c => c.Id == 2).Treasury.Food > 0);
        Assert.All(session.AllocationDiagnostics.Batches, b => Assert.InRange(b.Work, 0, 16));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OfficerPreparationRequiresTheWholeRescueToBeAffordable(bool moneyShortage)
    {
        var definition = Fixture(enemyX: 19);
        definition = definition with
        {
            Settlements = definition.Settlements.Select(s => s with { MerchantPresenceChance = 0 }).ToArray(),
            Officers = definition.Officers.Select(o => o.State.OwnerId == 2
                ? o with { State = o.State with { SettlementId = 3 } } : o).ToArray()
        };
        var funded = WithThreat(definition);
        var observation = Observe(funded, definition);
        var move = Assert.Single(observation.Support.Candidates, c => c.Intent is MoveOfficersCommand);
        Assert.True(move.MoneyCost > 0);
        Assert.True(move.AdditionalFood > 0);
        long baselineFood = Assert.Single(observation.Economy.Accounts).RequiredFood;
        var captured = funded.Capture();
        var session = GameSession.Restore(definition, captured with { World = captured.World with
        {
            Corps = captured.World.Corps.Select(c => c.OwnerId == 2 ? c with
                { Treasury = moneyShortage ? new(0, 100000) : new(100000, checked((uint)baselineFood)) } : c).ToArray()
        } });
        var result = session.Apply(session.Request(new PrepareAdvanceOperation()));
        Assert.Equal(SessionDisposition.Accepted, result.Disposition);
        Assert.DoesNotContain(result.Events.OfType<TaskStartedEvent>(), e => e.Task.Kind == "personnel-move");
    }
}

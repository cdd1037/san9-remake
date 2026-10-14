using San9.Tests;
using San9.Application.AI;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class EconomicSessionTests
{
    [Fact]
    public void SuppliedAccountCanSpendExactlyThePublicSingleOfficerDevelopmentCost()
    {
        var initial = G01ScenarioTests.Fixture();
        initial = initial with
        {
            Settlements = initial.Settlements.Select(s => s.OwnerId == 2 ? s with { Soldiers = 10000, Morale = 100 } : s).ToArray(),
            Information = initial.Information with { Corps = initial.Information.Corps.Select(c => c.OwnerId == 2
                ? c with { Treasury = new(50, 100000) } : c).ToArray() }
        };
        var session = GameSession.Start(initial, 190);
        // Opening preparation pays salaries; set the comparison balance at the actual decision boundary.
        var world = session.Snapshot with { Corps = session.Snapshot.Corps.Select(c => c.OwnerId == 2
            ? c with { Treasury = c.Treasury with { Money = 50 } } : c).ToArray() };
        var engine = GameSession.EngineFactory(initial)(WorldState.Restore(world with
            { Simulation = world.Simulation with { ActiveControl = 1 } }));
        var seat = world.Simulation.Controls[1];
        var observation = EconomicObservation.Capture(engine, seat, new(seat.ControllerId), 1000);
        var decision = EconomicPlanner.Decide(observation, [], seat.ControllerId, 1000, true);
        var development = Assert.IsType<DevelopIntent>(decision.Intent);
        Assert.Single(development.OfficerIds);
        var result = engine.Execute(new(seat.ControllerId), new DevelopCommand(Guid.NewGuid(), 2,
            engine.Snapshot.Simulation.NextSequence, development.Development));
        Assert.True(result.Submission.Accepted);
        Assert.Equal(50, Assert.Single(result.Events.OfType<DevelopmentCompletedEvent>()).Cost);
        Assert.Equal(0u, engine.Snapshot.Corps.Single(c => c.OwnerId == 2 && c.IsRoot).Treasury.Money);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectedSupplyIntentIsNotSelectedAgainIncludingRescueFallback(bool reserveBuyers)
    {
        var session = GameSession.Start(Shortage(), 190);
        var seat = session.Snapshot.Simulation.Controls.Single(s => s.ForceId == 2);
        var observed = EconomicObservation.Capture(new(WorldState.Restore(session.Snapshot)), seat, new(seat.ControllerId), 1000);
        System.Collections.Immutable.ImmutableArray<AllocationCandidate> candidates = reserveBuyers
            ? [new(seat.ControllerId, observed.Accounts[0].Id, new WaitCommand(Guid.Empty, 2, 0),
                observed.Domestic.Settlements[0].OfficerIds, 0, 100, 0, 0,
                Arrival: new(ArrivalEstimateStatus.Known, 1), DeadlineDays: 1)] : [];
        var rejected = new RejectedPlanningIntents();
        var first = SharedAllocator.Decide([(seat.ControllerId, observed)], candidates, [], 1000,
            new HashSet<string>(), new HashSet<string>(), rejected);
        var purchase = Assert.IsType<PurchaseFoodIntent>(first.Economic);
        rejected.Add(purchase);
        var retry = SharedAllocator.Decide([(seat.ControllerId, observed)], candidates, [], 1000,
            new HashSet<string>(), new HashSet<string>(), rejected);
        Assert.IsType<PurchaseFoodIntent>(retry.Economic);
        Assert.True(retry.Economic is null || !rejected.Contains(retry.Economic));
    }

    private static ScenarioInitialState Shortage(int merchantChance = 100)
    {
        var initial = G01ScenarioTests.Fixture();
        return initial with
        {
            Settlements = initial.Settlements.Select(s => s.OwnerId == 2
                ? s with { Soldiers = 90000, Morale = 100, MerchantPresenceChance = merchantChance } : s).ToArray(),
            Information = initial.Information with { Corps = initial.Information.Corps.Select(c => c.OwnerId == 2
                ? c with { Treasury = new(100000, 0) } : c).ToArray() }
        };
    }

    [Theory]
    [InlineData(190)]
    [InlineData(7)]
    [InlineData(101)]
    public void RealSupplyCommitmentSurvivesMultipleXunSaveAndRecordingUntilCompleted(ulong seed)
    {
        var initial = Shortage();
        var continuous = GameSession.Start(initial, seed);
        continuous.BeginRecording();
        var first = continuous.Apply(continuous.Request(new PrepareAdvanceOperation()));
        Assert.Equal(SessionDisposition.Accepted, first.Disposition);
        Assert.Single(first.Events.OfType<FoodTradeCompletedEvent>());
        var goal = Assert.Single(continuous.AiState.FoodSecurity);
        var resumed = SaveService.Decode(SaveService.Encode(continuous), initial);
        int trades = 1;
        for (int i = 0; i < 6; i++)
        {
            var a = continuous.AdvanceXun();
            var b = resumed.AdvanceXun();
            EventAssert.Equal(a.Events, b.Events);
            StateAssert.Equal(continuous.Capture(), resumed.Capture());
            trades += a.Events.OfType<FoodTradeCompletedEvent>().Count();
            Assert.All(continuous.AiState.FoodSecurity, g => Assert.Equal(goal.Started, g.Started));
            resumed = SaveService.Decode(SaveService.Encode(resumed), initial);
        }
        Assert.True(trades >= 2);
        Assert.Empty(continuous.AiState.FoodSecurity);
        Assert.True(EconomicOutlook.Read(continuous.Snapshot, new Dictionary<int, int>(), 2, 2).Food.Sufficient);
        var replay = RecordingService.Replay(RecordingService.Encode(continuous.EndRecording()), initial, GameSession.EngineFactory(initial));
        StateAssert.Equal(continuous.Capture(), replay.Session.Capture());
    }

    [Fact]
    public void UnfinishedQuotesRetainKnownShortageAndDoNotInvalidateAccountFacts()
    {
        var session = GameSession.Start(Shortage(), 190);
        var seat = session.Snapshot.Simulation.Controls.Single(s => s.ForceId == 2);
        var engine = new SimulationEngine(WorldState.Restore(session.Snapshot));
        var observed = EconomicObservation.Capture(engine, seat, new(seat.ControllerId), 1);
        Assert.True(observed.Complete);
        Assert.False(observed.QuotesComplete);
        Assert.Empty(observed.Purchases);
        var decision = EconomicPlanner.Decide(observed, [], seat.ControllerId, 100, allowDevelopment: false);
        Assert.Single(decision.Goals);
        Assert.Null(decision.Intent);
        var unchanged = EconomicObservation.CapturePurchases(engine, 2, observed, new HashSet<int>(), 100);
        Assert.False(unchanged.QuotesComplete);
        Assert.Equal(observed.Accounts, unchanged.Accounts);
    }

    [Fact]
    public void PurchaseUsesEnoughOfficersToCloseTheGapInOneCityTrade()
    {
        var initial = Shortage();
        var session = GameSession.Start(initial, 190);
        var seat = session.Snapshot.Simulation.Controls.Single(s => s.ForceId == 2);
        var observed = EconomicObservation.Capture(new(WorldState.Restore(session.Snapshot)), seat, new(seat.ControllerId), 1000);
        uint singleMaximum = observed.Purchases.Where(p => p.OfficerIds.Length == 1).Max(p => p.Maximum);
        var group = observed.Purchases.MaxBy(p => p.Maximum)!;
        Assert.True(group.Maximum > singleMaximum);
        var needed = singleMaximum + 1;
        var required = observed.Accounts.Single().RequiredFood;
        initial = initial with { Information = initial.Information with { Corps = initial.Information.Corps.Select(c => c.OwnerId == 2
            ? c with { Treasury = c.Treasury with { Food = (uint)(required - needed) } } : c).ToArray() } };
        session = GameSession.Start(initial, 190, aiPolicy: AiPolicyConfiguration.Standard with { MaximumPurchases = 1 });
        var result = session.Apply(session.Request(new PrepareAdvanceOperation()));
        var purchase = Assert.Single(result.Events.OfType<FoodTradeCompletedEvent>());
        Assert.True(purchase.OfficerIds.Length > 1);
        Assert.Equal(needed, purchase.FoodAmount);
        Assert.Empty(session.AiState.FoodSecurity);
        Assert.True(EconomicOutlook.Read(session.Snapshot, new Dictionary<int, int>(), 2, 2).Food.Sufficient);
    }

    [Fact]
    public void ProjectionStopsBeforeQuotesAndPreservesGoalsForUnobservedAccounts()
    {
        var initial = Shortage();
        initial = initial with
        {
            Settlements = [.. initial.Settlements, initial.Settlements[1] with { Id = 3, CorpsId = 3 }],
            Officers = [.. initial.Officers, initial.Officers[5] with
                { NameKey = "officer.7", State = initial.Officers[5].State with { Id = 7, SettlementId = 3 } }],
            Information = initial.Information with { Corps = [.. initial.Information.Corps, new(3, 2, 7, new(0, 0))] }
        };
        var session = GameSession.Start(initial, 190, aiPolicy: AiPolicyConfiguration.Standard with { WorkBudget = 1 });
        var seat = session.Snapshot.Simulation.Controls.Single(s => s.ForceId == 2);
        var observed = EconomicObservation.Capture(new(WorldState.Restore(session.Snapshot)), seat, new(seat.ControllerId), 1);
        Assert.Equal(1, observed.ProjectionWork);
        Assert.False(observed.Complete);
        Assert.Single(observed.Accounts);
        Assert.Empty(observed.Purchases);
        var before = session.Capture();
        var prior = new FoodSecurityGoal(2, seat.ControllerId, 3, before.World.Clock);
        session = GameSession.Restore(initial, before with { Ai = before.Ai with { FoodSecurity = [prior] } });
        Assert.Equal(SessionDisposition.Accepted, session.Apply(session.Request(new PrepareAdvanceOperation())).Disposition);
        Assert.Contains(prior, session.AiState.FoodSecurity);
        Assert.Equal(2, session.AiState.FoodSecurity.Length);
    }

    [Fact]
    public void SharedAccountIsNotBoughtTwiceAndUnavailableSupplyRetainsItsOwnGoal()
    {
        var initial = Shortage();
        initial = initial with
        {
            Settlements = [initial.Settlements[0], initial.Settlements[1] with { Soldiers = 1000 },
                initial.Settlements[1] with { Id = 3, CorpsId = 2, Soldiers = 1000 },
                initial.Settlements[1] with { Id = 4, CorpsId = 3, Soldiers = 1000 }],
            Officers = [.. initial.Officers,
                initial.Officers[4] with { NameKey = "officer.7", State = initial.Officers[4].State with { Id = 7, SettlementId = 3 } },
                initial.Officers[5] with { NameKey = "officer.8", State = initial.Officers[5].State with { Id = 8, SettlementId = 4 } }],
            Information = initial.Information with { Corps = [.. initial.Information.Corps, new(3, 2, 8, new(0, 0))] }
        };
        var session = GameSession.Start(initial, 9);
        long required = EconomicOutlook.Read(session.Snapshot, new Dictionary<int, int>(), 2, 2).Food.Expenditure;
        var result = session.Apply(session.Request(new PrepareAdvanceOperation()));
        var trade = Assert.Single(result.Events.OfType<FoodTradeCompletedEvent>());
        Assert.Equal((uint)required, trade.FoodAmount);
        Assert.Equal(3, Assert.Single(session.AiState.FoodSecurity).AccountId);
        Assert.Equal(0u, session.Snapshot.Corps.Single(c => c.Id == 3).Treasury.Food);
        Assert.Equal(0u, session.Snapshot.Corps.Single(c => c.Id == 3).Treasury.Money);
    }

    [Fact]
    public void BudgetAndMemoryOnlyDecisionAreSavedAndRejectedRequestsCannotPublishChanges()
    {
        var initial = Shortage(0);
        var session = GameSession.Start(initial, 190,
            aiPolicy: AiPolicyConfiguration.Standard with { WorkBudget = 1, MaximumPurchases = 1 });
        session.BeginRecording();
        var before = session.Capture();
        var invalid = session.Request(new EndControlOperation(), "ai:2");
        Assert.Equal(SessionDisposition.Rejected, session.Apply(invalid).Disposition);
        StateAssert.Equal(before, session.Capture());
        Assert.Empty(session.CaptureRecording().Operations);
        var request = session.Request(new PrepareAdvanceOperation());
        var result = session.Apply(request);
        Assert.Equal(SessionDisposition.Accepted, result.Disposition);
        Assert.Empty(result.Events.OfType<FoodTradeCompletedEvent>());
        Assert.Empty(result.Events.OfType<DevelopmentCompletedEvent>());
        Assert.Single(session.AiState.FoodSecurity);
        var captured = session.Capture();
        Assert.Equal(SessionDisposition.AlreadyProcessed, session.Apply(request).Disposition);
        Assert.Same(captured, session.Capture());
        var resumed = SaveService.Decode(SaveService.Encode(session), initial);
        Assert.Equal(1, resumed.AiState.Configuration.WorkBudget);
        StateAssert.Equal(captured, resumed.Capture());
        StateAssert.NotEqual(captured, (captured with { Ai = new() }));
        Assert.Throws<ArgumentException>(() => GameSession.Restore(initial, captured with { Ai = captured.Ai with
        { Configuration = captured.Ai.Configuration with { WorkBudget = 0 } } }));
        Assert.Throws<ArgumentException>(() => GameSession.Restore(initial, captured with { Ai = captured.Ai with
        { FoodSecurity = [new(2, null!, 2, captured.World.Clock)] } }));
    }

    [Fact]
    public void ForeignStateDoesNotChangeObservedEconomicDecisionAndWorkIsBounded()
    {
        var session = GameSession.Start(Shortage(), 5);
        var before = session.Snapshot;
        var seat = before.Simulation.Controls.Single(s => s.ForceId == 2);
        var changed = before with
        {
            Corps = before.Corps.Select(c => c.OwnerId == 1 ? c with { Treasury = new(90000, 90000) } : c).ToArray(),
            Officers = before.Officers.Select(o => o.OwnerId == 1 ? o with { Acted = true } : o).ToArray()
        };
        var observed = EconomicObservation.Capture(new(WorldState.Restore(before)), seat, new(seat.ControllerId), 1000);
        var hidden = EconomicObservation.Capture(new(WorldState.Restore(changed)), seat, new(seat.ControllerId), 1000);
        Assert.Equal(CanonicalJson.Text(observed), CanonicalJson.Text(hidden));
        var decision = EconomicPlanner.Decide(observed, [], seat.ControllerId, 1, true);
        Assert.True(decision.Exhausted);
        Assert.Equal(1, decision.Work);
        Assert.Null(decision.Intent);
        Assert.Single(decision.Goals);
        var full = EconomicPlanner.Decide(observed, [], seat.ControllerId, 100, true);
        Assert.IsType<PurchaseFoodIntent>(full.Intent);
        Assert.True(full.Work <= 100);
        Assert.Equal(CanonicalJson.Text(full), CanonicalJson.Text(EconomicPlanner.Decide(hidden, [], seat.ControllerId, 100, true)));
    }

    [Fact]
    public void DelegatedScopeIsClearedOnRevocationWithoutTakingPlayerResources()
    {
        var initial = Shortage(0);
        initial = initial with
        {
            Settlements = initial.Settlements.Select(s => s.OwnerId == 1 ? s with { Soldiers = 30000, Morale = 100 } : s).ToArray(),
            Information = initial.Information with { Corps = initial.Information.Corps.Select(c => c.OwnerId == 1
                ? c with { ControlMode = CorpsControlMode.Automatic, Treasury = new(20000, 0) } : c).ToArray() }
        };
        var session = GameSession.Start(initial, 190);
        session.AdvanceXun();
        Assert.Contains(session.AiState.FoodSecurity, g => g.ControllerId == "corps:1");
        var computerGoal = session.AiState.FoodSecurity.Single(g => g.ForceId == 2);
        Assert.True(session.Execute(new("player"), new SetCorpsControlCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, 1, CorpsControlMode.Direct)).Submission.Accepted);
        Assert.Equal(computerGoal, Assert.Single(session.AiState.FoodSecurity));
        var treasury = session.Snapshot.Corps.Single(c => c.Id == 1).Treasury;
        session.Apply(session.Request(new PrepareAdvanceOperation()));
        Assert.Equal(treasury.Money, session.Snapshot.Corps.Single(c => c.Id == 1).Treasury.Money);
    }

    [Fact]
    public void FailureAfterAutomaticActionsRollsBackWorldGoalsRecordsAndRecording()
    {
        var initial = Shortage();
        var rules = new FailingDailyRules();
        var session = GameSession.Start(initial, 190, w => new(w, rules));
        session.AllocationDiagnosticsEnabled = true;
        session.BeginRecording();
        var before = session.Capture();
        var request = session.Request(new AdvanceOperation());
        var result = session.Apply(request);
        Assert.True(rules.SawPurchase);
        Assert.Equal(SessionDisposition.Rejected, result.Disposition);
        StateAssert.Equal(before, session.Capture());
        Assert.Empty(session.Records);
        Assert.Empty(session.AiState.FoodSecurity);
        Assert.Empty(session.CaptureRecording().Operations);
        Assert.Empty(session.AllocationDiagnostics.WorkByForce);
        Assert.Empty(session.AllocationDiagnostics.AcceptedCommands);
        rules.Fail = false;
        Assert.Equal(SessionDisposition.Accepted, session.Apply(request).Disposition);
        Assert.Single(session.AiState.FoodSecurity);
        Assert.Single(session.CaptureRecording().Operations);
        Assert.NotEmpty(session.AllocationDiagnostics.AcceptedCommands);
        Assert.NotEmpty(session.AllocationDiagnostics.WorkByForce);
    }

    private sealed class FailingDailyRules : SimulationRules
    {
        internal bool Fail = true;
        internal bool SawPurchase;
        internal override void ApplyStage(ResolutionStage stage, WorldMutation world, List<DomainEvent> events)
        {
            if (stage != ResolutionStage.BeforeDailyCommands || !Fail) return;
            SawPurchase = world.Corps[2].Treasury.Food > 0;
            throw new InvalidOperationException("Deliberate failure after automatic planning.");
        }
    }

    [Theory]
    [InlineData(CorpsControlMode.Direct)]
    [InlineData(CorpsControlMode.Automatic)]
    public void XunAndDailyEntriesProduceTheSameSupplyStateWithoutDuplicateRootWork(CorpsControlMode mode)
    {
        var initial = Shortage();
        initial = initial with { Information = initial.Information with { Corps = initial.Information.Corps
            .Select(c => c.OwnerId == 2 ? c with { ControlMode = mode } : c).ToArray() } };
        var whole = GameSession.Start(initial, 190);
        var daily = GameSession.Start(initial, 190);
        var xun = whole.AdvanceXun();
        var preparation = daily.Apply(daily.Request(new PrepareAdvanceOperation()));
        Assert.Single(xun.Events.OfType<FoodTradeCompletedEvent>());
        Assert.Single(preparation.Events.OfType<FoodTradeCompletedEvent>());
        for (int i = 0; i < 10; i++)
            Assert.Equal(SessionDisposition.Accepted, daily.Apply(daily.Request(new AdvanceDayOperation())).Disposition);
        StateAssert.Equal(whole.Snapshot, daily.Snapshot);
        Assert.Equal(CanonicalJson.Text(whole.AiState), CanonicalJson.Text(daily.AiState));
    }
}

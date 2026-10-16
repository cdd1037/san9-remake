using System.Collections.Immutable;
using San9.Application.AI;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Tests;

namespace San9.Application.Tests;

public sealed class OriginalAiCycleTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RationReserveRoundsEachGarrisonEvenWithoutAvailableWork(bool canHostPersons)
    {
        var content = G01ScenarioTests.Fixture();
        content = content with { Settlements = [content.Settlements[0] with { Soldiers = 1 }, content.Settlements[1],
            content.Settlements[0] with { Id = 3, Soldiers = 1, CanHostPersons = canHostPersons }] };
        var session = GameSession.Start(content, 190);
        var engine = GameSession.EngineFactory(content)(WorldState.Restore(session.Snapshot));
        var input = OriginalAiProjection.Domestic(engine, content, engine.Snapshot.Corps.Single(c => c.Id == 1),
            new(content, null, engine.Snapshot), true);
        Assert.Equal(360, input.RationDemand); // Two daily rations for the 180 days until July.
        Assert.Equal(canHostPersons ? 2 : 1, input.Sites.Length);
    }

    [Fact]
    public void InvalidSavedPolicyIsReportedAsInvalidState()
    {
        var content = G01ScenarioTests.Fixture();
        var envelope = SaveService.Capture(GameSession.Start(content, 190));
        var state = envelope.Session.OriginalAi;
        envelope = envelope with { Session = envelope.Session with { OriginalAi = state with
            { Policy = state.Policy with { WorkRules = state.Policy.WorkRules.Remove(OriginalAiWork.Trade) } } } };
        var error = Assert.Throws<SaveException>(() => SaveService.Decode(CanonicalJson.Encode(envelope), content));
        Assert.Equal(SaveFailure.InvalidState, error.Failure);
    }

    [Fact]
    public void FailedCompositeAdvanceRollsBackEconomicCommandsStrategyAndOperationIdentity()
    {
        var content = G01ScenarioTests.Fixture();
        var session = GameSession.Start(content, 190, world => new SimulationEngine(world, new FailingAdvanceRules()),
            playerForceController: ControllerKind.Automatic);
        var before = session.Capture();
        var request = session.Request(new AdvanceOperation());
        Assert.Equal(SessionDisposition.Rejected, session.Apply(request).Disposition);
        StateAssert.Equal(before, session.Capture());
        Assert.Empty(session.LastOriginalAiWork);
        Assert.Equal(before.NextOperationSequence, session.Request(new AdvanceOperation()).Sequence);
    }

    private sealed class FailingAdvanceRules : SimulationRules
    {
        internal override void ApplyStage(ResolutionStage stage, WorldMutation world, List<DomainEvent> events)
        {
            if (stage == ResolutionStage.BeforeDailyCommands) throw new InvalidOperationException("Injected failure after automatic orders.");
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TradeConsumesRejectedOrEmptyCityAndContinuesWithoutPrivateResourceLoss(bool rejectFirst)
    {
        var content = G01ScenarioTests.Fixture();
        content = content with
        {
            Settlements = [content.Settlements[0] with { MerchantPresenceChance = 100 }, content.Settlements[1],
                content.Settlements[0] with { Id = 3, MerchantPresenceChance = 100 }],
            Officers = content.Officers.Select(o => o.State.Id is 2 or 3 ? o with { State = o.State with { SettlementId = 3 } } :
                o.State.Id == 1 && !rejectFirst ? o with { State = o.State with { BaseAbilities = o.State.BaseAbilities with { Politics = 1 } } } : o).ToArray(),
            Information = content.Information with { Corps = content.Information.Corps.Select(c => c.Id == 1 ? c with { Treasury = new(100000, 0) } : c).ToArray() },
        };
        var engine = GameSession.EngineFactory(content)(WorldState.Restore(GameSession.Start(content, 190).Snapshot));
        var geography = new OriginalAiGeography(content, null, engine.Snapshot);
        OriginalDomesticInputs Capture() => OriginalAiProjection.Domestic(engine, content, engine.Snapshot.Corps.Single(c => c.Id == 1), geography, true);
        uint money = Capture().Money;
        int calls = 0;
        var report = OriginalAiEconomy.Trade(1, 2, Capture, () => engine.Snapshot,
            (place, people, direction) => engine.GetFoodTradeQuote(1, place, people, direction),
            (place, people, plan) =>
            {
                calls++;
                var before = engine.Snapshot;
                long sequence = before.Simulation.NextSequence;
                var result = engine.Execute(new(place == 1 ? "forged" : "player"),
                    new FoodTradeCommand(SimulationIdentity.Command(sequence, 1), 1, sequence, place, people, plan.Direction, plan.FoodAmount));
                if (place == 1) StateAssert.Equal(before, engine.Snapshot);
                return result.Submission.Accepted;
            });
        Assert.Equal(2, report.Attempts);
        Assert.Equal(1, report.Submitted);
        Assert.Equal(rejectFirst ? 2 : 1, calls);
        Assert.Equal(rejectFirst ? 1 : 0, report.Rejected);
        Assert.Equal(rejectFirst ? 0 : 1, report.EmptyPlans);
        Assert.False(engine.Snapshot.Officers.Single(o => o.Id == 1).Acted);
        uint food = Capture().Food;
        Assert.True(food > 0);
        Assert.Equal(money - food, Capture().Money);
    }

    [Fact]
    public void BarbarianControlKeepsItsOwnWorkOrderAndDoesNotSearchResidualPeople()
    {
        var content = G01ScenarioTests.Fixture();
        content = content with { Forces = content.Forces.Select(f => f.Id == 2 ? f with { IsBarbarian = true } : f).ToArray() };
        var session = GameSession.Start(content, 190);
        session.EndControl(new("player"));
        var result = session.EndControl(new("ai:2"));
        Assert.Contains(result.Events.OfType<DevelopmentCompletedEvent>(), e => e.Command.Activity == DomesticActivity.Train);
        Assert.DoesNotContain(result.Events.OfType<DevelopmentCompletedEvent>(), e => e.Command.Activity is DomesticActivity.Commerce or DomesticActivity.Cultivation or DomesticActivity.Patrol);
        Assert.DoesNotContain(session.Snapshot.Simulation.Tasks, t => t.ForceId == 2 && t.Kind == "personnel-search");
        var order = session.LastOriginalAiWork.Select(r => r.Work).ToArray();
        Assert.True(Array.IndexOf(order, OriginalAiWork.Train) < Array.IndexOf(order, OriginalAiWork.Recruit));
        Assert.Equal(OriginalAiWork.Residual, order[^1]);
    }

    [Fact]
    public void NoAvailablePeopleFinishAutomaticControlWithoutInventingCommands()
    {
        var content = G01ScenarioTests.Fixture();
        var session = GameSession.Start(content, 190);
        Assert.True(session.Develop(new(1, 1, [1, 2, 3], DomesticActivity.Commerce)).Accepted);
        Assert.True(session.Execute(new("player"), new SetCorpsControlCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, 1, CorpsControlMode.Automatic)).Submission.Accepted);
        long sequence = session.Snapshot.Simulation.NextSequence;
        session.EndControl(new("player"));
        Assert.Equal(sequence, session.Snapshot.Simulation.NextSequence);
        Assert.All(session.LastOriginalAiWork, r => Assert.Equal(0, r.Submitted));
        Assert.Equal(1, session.Snapshot.Simulation.ActiveControl);
    }

    [Fact]
    public void ProjectionUsesCurrentOwnAbilitiesAndIgnoresForeignPrivateResources()
    {
        var content = G01ScenarioTests.Fixture();
        var world = GameSession.Start(content, 190).Snapshot;
        world = world with { Officers = world.Officers.Select(o => o.Id == 2 ? o with { Injury = 7 } : o).ToArray() };
        var changed = world with { Corps = world.Corps.Select(c => c.OwnerId == 2 ? c with { Treasury = new(700000, 800000) } : c).ToArray(),
            Officers = world.Officers.Select(o => o.OwnerId == 2 ? o with { BaseAbilities = new(1, 1, 1, 1) } : o).ToArray() };
        var first = GameSession.EngineFactory(content)(WorldState.Restore(world));
        var second = GameSession.EngineFactory(content)(WorldState.Restore(changed));
        var corps = world.Corps.Single(c => c.Id == 1);
        var a = OriginalAiProjection.Domestic(first, content, corps, new(content, null, world), true);
        var b = OriginalAiProjection.Domestic(second, content, corps, new(content, null, changed), true);
        Assert.Equal(CanonicalJson.Text(a), CanonicalJson.Text(b));
        Assert.Equal(first.GetAbilities(first.Snapshot)[2], a.Abilities[2]);
        Assert.NotEqual(world.Officers.Single(o => o.Id == 2).BaseAbilities, a.Abilities[2]);
        Assert.DoesNotContain(4, a.Abilities.Keys);
    }

    [Theory]
    [InlineData(7, OriginalAiMode.NoFrontier)]
    [InlineData(8, OriginalAiMode.Domestic)]
    public void RetainedModeUsesOneStrictEightPercentUpdateDraw(int draw, OriginalAiMode expected)
    {
        var content = G01ScenarioTests.Fixture();
        var session = GameSession.Start(content, 190);
        var world = session.Snapshot;
        var state = session.OriginalAi with { Corps = [new(1, OriginalAiMode.Domestic, null, null)] };
        int calls = 0;
        var random = new OriginalAiRandom(maximum => { Assert.Equal(100, maximum); calls++; return draw; });
        var result = OriginalAiStrategy.PrepareCorps(world, world.Corps.Single(c => c.Id == 1), state, new(content, null, world), random);
        Assert.Equal(expected, result.Mode);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void ExplicitDelegationAndInvalidTargetsReplaceModeAndTargetAtomicallyWithoutUpdateRoll()
    {
        var content = G01ScenarioTests.Fixture();
        var session = GameSession.Start(content, 190);
        var world = session.Snapshot;
        var corps = world.Corps.Single(c => c.Id == 1) with { Delegation = new DomesticDelegation() };
        var state = session.OriginalAi with { Corps = [new(1, OriginalAiMode.TargetForce, 2, null)] };
        var result = OriginalAiStrategy.PrepareCorps(world, corps, state, new(content, null, world),
            new OriginalAiRandom(_ => throw new InvalidOperationException("Explicit delegation must not roll.")));
        Assert.Equal(new OriginalAiCorpsState(1, OriginalAiMode.Domestic, null, null), result);
        var lost = world with { Forces = world.Forces.Select(f => f.Id == 2 ? f with { IsEliminated = true } : f).ToArray() };
        Assert.Equal(new OriginalAiCorpsState(1, OriginalAiMode.General, null, null), Assert.Single(state.Reconcile(lost).Corps));
    }

    [Theory]
    [InlineData(false, OriginalAiGroup.Unassigned, 2)]
    [InlineData(true, OriginalAiGroup.Unassigned, 3)]
    [InlineData(false, OriginalAiGroup.ReducedAttempts, 1)]
    [InlineData(true, OriginalAiGroup.ReducedAttempts, 2)]
    public void AttemptsKeepDifficultyAndGroupIndependent(bool advanced, OriginalAiGroup group, int expected)
    {
        Assert.Equal(expected, OriginalAiWorkGate.Attempts(new(0, 0, 3, true, true), advanced, group));
        Assert.Equal(1, OriginalAiWorkGate.Attempts(new(0, 0, 1, true, true), advanced, group));
    }

    [Fact]
    public void OpeningWaitAndStableCalendarPhaseAreSeparate()
    {
        var start = new GameClock(190, 1, 1);
        var recruitment = new OriginalAiWorkRule(60, 0, 255, true, true);
        Assert.False(OriginalAiWorkGate.Calendar(new(190, 2, 21), start, 9001, recruitment, false));
        Assert.True(OriginalAiWorkGate.Calendar(new(190, 3, 1), start, 9001, recruitment, false));
        Assert.False(OriginalAiWorkGate.Calendar(new(190, 1, 21), start, 9001, recruitment, true));
        Assert.True(OriginalAiWorkGate.Calendar(new(190, 2, 1), start, 9001, recruitment, true));
        var periodic = new OriginalAiWorkRule(0, 30, 3, true, true);
        Assert.True(OriginalAiWorkGate.Calendar(start, start, 9, periodic, false));
        Assert.False(OriginalAiWorkGate.Calendar(start, start, 10, periodic, false));
        Assert.True(OriginalAiWorkGate.Calendar(new(190, 1, 21), start, 10, periodic, false));
        Assert.Equal(255, OriginalAiWorkGate.Attempts(recruitment, true, OriginalAiGroup.Unassigned));
    }

    [Fact]
    public void ReadinessKeepsFixedPublicOrderAndMoraleDenominators()
    {
        var places = new[] { new SettlementState(1, 1, 0, 100, 100, 100, 1000, 100, 100, 100, 100),
            new SettlementState(2, 1, 0, 100, 100, 100, 1000, 100, 100, 100, 100) };
        Assert.Equal((133L, 150L), OriginalAiStrategy.Readiness(places));
    }

    [Theory]
    [InlineData(49, 90, OriginalAiMode.Domestic)]
    [InlineData(50, 50, OriginalAiMode.General)]
    [InlineData(50, 49, OriginalAiMode.MilitaryPreparation)]
    [InlineData(51, 50, OriginalAiMode.General)]
    public void StrategyFallbackUsesStrictEconomyAndMilitaryThresholds(int economic, int military, OriginalAiMode expected)
    {
        var content = G01ScenarioTests.Fixture();
        content = content with { CityConnections = [new(1, 2)],
            RegionNameKeys = new() { [101] = "first", [102] = "second" }, RegionCityIds = new() { [101] = 1, [102] = 2 },
            Settlements = content.Settlements.Select(s => s.Id == 1 ? s with { Commerce = economic, MaxCommerce = 100,
                Cultivation = economic, MaxCultivation = 100, PublicOrder = economic * 10, Durability = military, MaxDurability = 100, Morale = military } : s).ToArray(),
            Information = content.Information with { Relations = [new(1, 2, 100)] } };
        var session = GameSession.Start(content, 190);
        var world = session.Snapshot;
        var state = OriginalAiStrategy.PrepareGlobal(world, session.OriginalAi);
        var result = OriginalAiStrategy.PrepareCorps(world, world.Corps.Single(c => c.OwnerId == 1), state,
            new(content, null, world), new(new StableRandom(42)));
        Assert.Equal(expected, result.Mode);
    }

    [Fact]
    public void MultipleOpponentsUseTheActuallySortedEligibleTable()
    {
        var content = G01ScenarioTests.Fixture();
        content = content with
        {
            Forces = [.. content.Forces, new(3, "third", 3, 50), new(4, "fourth", 4, 50)],
            Settlements = [.. content.Settlements, content.Settlements[0] with { Id = 3, OwnerId = 3 }, content.Settlements[0] with { Id = 4, OwnerId = 4 }],
            CityConnections = [new(1, 2), new(1, 3), new(1, 4)],
            RegionNameKeys = new() { [101] = "first", [102] = "second", [103] = "third", [104] = "fourth" },
            RegionCityIds = new() { [101] = 1, [102] = 2, [103] = 3, [104] = 4 },
            CityProvinceIds = new() { [1] = 1, [2] = 1, [3] = 1, [4] = 1 },
            Information = content.Information with { Corps = [.. content.Information.Corps,
                new(3, 3, null, new(10000, 10000)) { IsRoot = true }, new(4, 4, null, new(10000, 10000)) { IsRoot = true }],
                Relations = [new(1, 2, 50), new(1, 3, 0), new(1, 4, 40)] },
        };
        var session = GameSession.Start(content, 190);
        var world = session.Snapshot;
        var state = OriginalAiStrategy.PrepareGlobal(world, session.OriginalAi);
        var result = OriginalAiStrategy.PrepareCorps(world, world.Corps.Single(c => c.OwnerId == 1), state,
            new(content, null, world), new(new StableRandom(42)));
        Assert.Equal(OriginalAiMode.TargetForce, result.Mode);
        Assert.Equal(3, result.TargetForceId);
        Assert.Null(result.TargetSettlementId);
    }

    [Fact]
    public void FamilyRankSnapshotSurvivesOffGatePreparationAndParameterRestore()
    {
        var content = G01ScenarioTests.Fixture();
        content = content with { OriginalAi = content.OriginalAi with { FamilyRankBonuses = ImmutableDictionary<int, int>.Empty.Add(99, 200), Advanced = true },
            Officers = content.Officers.Select(o => o.State.Id == 1 ? o with { State = o.State with { BloodlineId = 99 } } : o).ToArray() };
        var session = GameSession.Start(content, 190);
        var ranked = OriginalAiStrategy.PrepareGlobal(session.Snapshot, session.OriginalAi);
        Assert.Equal(1, ranked.DiplomaticOrder[0]);
        var changed = session.Snapshot with { Clock = new(190, 1, 11), Forces = session.Snapshot.Forces.Select(f => f with { Trust = f.Id == 1 ? 0 : 100 }).ToArray() };
        var retained = OriginalAiStrategy.PrepareGlobal(changed, ranked);
        Assert.Equal(ranked.DiplomaticOrder, retained.DiplomaticOrder);
        Assert.Equal(ranked.RankedClock, retained.RankedClock);
        var saved = session.Capture() with { OriginalAi = retained, World = changed with { Forces = session.Snapshot.Forces } };
        var restored = GameSession.Restore(content with { OriginalAi = OriginalAiDefinition.Default }, saved);
        Assert.True(restored.OriginalAi.Policy.Advanced);
        Assert.Equal(200, restored.OriginalAi.Policy.FamilyRankBonuses[99]);
        StateAssert.Equal(saved, restored.Capture());
    }

    [Fact]
    public void DelegatedCorpsSpendTheirOwnAccountWhileDirectPlayerCorpsRemainIdle()
    {
        var content = G01ScenarioTests.Fixture();
        content = content with
        {
            Settlements = [.. content.Settlements, content.Settlements[0] with { Id = 3, CorpsId = 3 }],
            Officers = content.Officers.Select(o => o.State.Id is 2 or 3 ? o with { State = o.State with { SettlementId = 3 } } : o).ToArray(),
            Information = content.Information with { Corps = [.. content.Information.Corps,
                new(3, 1, 2, new(1000, 10000)) { ControlMode = CorpsControlMode.Automatic, Delegation = new DomesticDelegation() }] },
        };
        var session = GameSession.Start(content, 190);
        uint rootMoney = session.Snapshot.Corps.Single(c => c.Id == 1).Treasury.Money;
        var result = session.Apply(session.Request(new EndControlOperation()));
        Assert.Equal(SessionDisposition.Accepted, result.Disposition);
        Assert.Contains(result.Events.OfType<DevelopmentCompletedEvent>(), e => e.Command.SettlementId == 3);
        Assert.DoesNotContain(result.Events.OfType<DevelopmentCompletedEvent>(), e => e.Command.SettlementId != 3);
        Assert.Equal(rootMoney, session.Snapshot.Corps.Single(c => c.Id == 1).Treasury.Money);
        Assert.Equal(OriginalAiMode.Domestic, Assert.Single(session.OriginalAi.Corps).Mode);
        Assert.All(session.LastOriginalAiWork, report => Assert.Equal(3, report.CorpsId));
    }

    [Fact]
    public void ProductionTradeRunsBeforeDomesticWorkAndFullCycleRestoresAndReplays()
    {
        var content = G01ScenarioTests.Fixture();
        content = content with { Settlements = content.Settlements.Select(s => s with { MerchantPresenceChance = 100 }).ToArray(),
            OriginalAi = content.OriginalAi with { Advanced = true, WorkRules = content.OriginalAi.WorkRules.SetItem(OriginalAiWork.Trade, new(0, 0, 1, false, true)) },
            Information = content.Information with { Corps = content.Information.Corps.Select(c => c with { Treasury = new(100000, 0) }).ToArray() } };
        var session = GameSession.Start(content, 190, playerForceController: ControllerKind.Automatic);
        session.BeginRecording();
        var request = session.Request(new PrepareAdvanceOperation());
        var result = session.Apply(request);
        Assert.Equal(SessionDisposition.Accepted, result.Disposition);
        Assert.NotEmpty(result.Events.OfType<FoodTradeCompletedEvent>());
        Assert.All(session.LastOriginalAiWork.Where(r => r.Work == OriginalAiWork.Trade), r => Assert.Equal(1, r.Submitted));
        var beforeRepeat = session.Capture();
        Assert.Equal(SessionDisposition.AlreadyProcessed, session.Apply(request).Disposition);
        StateAssert.Equal(beforeRepeat, session.Capture());
        var restored = SaveService.Decode(SaveService.Encode(session), content);
        session.AdvanceXun();
        restored.AdvanceXun();
        StateAssert.Equal(session.Capture(), restored.Capture());
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), content, GameSession.EngineFactory(content), verifyEvents: true);
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
    }

    [Theory]
    [InlineData(10000, 100, 8000, 8000)]
    [InlineData(10000, 2000, 8000, 0)]
    [InlineData(10000, 2000, 7999, 7500)]
    [InlineData(10000, 2000, 2999, 0)]
    public void TradeRetainsStrictReserveEndpointsAndFiveHundredRounding(uint money, uint food, uint limit, uint expected)
    {
        var input = new OriginalDomesticInputs(1, money, food, 0, 0, true, [], ImmutableDictionary<int, OfficerAbilities>.Empty);
        var plan = OriginalAiEconomy.TradeAmount(input, 1, limit, 0);
        Assert.Equal(expected, plan?.FoodAmount ?? 0);
    }

    [Fact]
    public void ZeroBalanceTradeKeepsSmallUnroundedQuantitiesAndPublicLimits()
    {
        var input = new OriginalDomesticInputs(1, 100, 0, 100, 101, true, [], ImmutableDictionary<int, OfficerAbilities>.Empty);
        Assert.Equal(new OriginalFoodTradePlan(FoodTradeDirection.Buy, 67), OriginalAiEconomy.TradeAmount(input, 2, 1000, 1000));
        input = input with { Money = 0, Food = 100 };
        Assert.Equal(new OriginalFoodTradePlan(FoodTradeDirection.Sell, 66), OriginalAiEconomy.TradeAmount(input, 2, 1000, 1000));
        Assert.Equal(30u, OriginalAiEconomy.TradeAmount(input, 2, 1000, 30)!.FoodAmount);
    }

    [Fact]
    public void ResidualAllFreeSourceLeavesBestAdjustedLeaderAndStopsWhenOnlyOneRemains()
    {
        var content = G01ScenarioTests.Fixture();
        content = content with { Settlements = content.Settlements.Select(s => s with { Morale = 100 }).ToArray() };
        var session = GameSession.Start(content, 190);
        var engine = GameSession.EngineFactory(content)(WorldState.Restore(session.Snapshot));
        var corps = engine.Snapshot.Corps.Single(c => c.Id == 1);
        var geography = new OriginalAiGeography(content, null, engine.Snapshot);
        var input = OriginalAiProjection.Domestic(engine, content, corps, geography, true);
        var searches = new List<int>();
        var report = OriginalAiEconomy.Residual(1, false, 100, () => input, () => engine.Snapshot,
            _ => throw new InvalidOperationException(), (person, _) => { searches.Add(person); return false; });
        Assert.Equal(new[] { 2, 3 }, searches);
        Assert.Equal(2, report.Attempts);
        input = input with { Sites = input.Sites.Select(s => s with { Settlement = s.Settlement with { OfficerIds = [1] } }).ToImmutableArray() };
        var singleWorld = engine.Snapshot with { Officers = engine.Snapshot.Officers.Where(o => o.OwnerId != 1 || o.Id == 1).ToArray() };
        report = OriginalAiEconomy.Residual(1, false, 100, () => input, () => singleWorld, _ => false, (_, _) => throw new InvalidOperationException());
        Assert.Equal(OriginalAiWorkEnd.PreparationFailed, report.End);
        Assert.Equal(0, report.Attempts);
    }
}

using San9.Tests;
using System.Collections.Immutable;
using San9.Application.AI;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class DomainOpportunityTests
{
    [Fact]
    public void CooldownEvidenceComparesByValueAfterPersistence()
    {
        var subject = new OpportunityPerson(2, OfficerStatus.Serving, 3, null, 75);
        var evidence = new OpportunityConditions(80, 6, 60, subject, new(8, 9), null, 40);
        var restored = StrictJson.Deserialize<OpportunityConditions>(CanonicalJson.Encode(evidence));
        Assert.Equal(evidence, restored);
        Assert.NotEqual(evidence, restored with { Subject = subject with { Loyalty = 80 } });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void RestoreRejectsOpportunityWithMissingController(string? controller)
    {
        var content = G01ScenarioTests.Fixture();
        var session = GameSession.Start(content, 190);
        var saved = session.Capture();
        var attempt = new OpportunityAttempt(2, controller!, "SearchCommand:2", Option().Conditions, Guid.NewGuid(), 5,
            saved.World.Clock, saved.World.Clock, "failed");
        Assert.Throws<ArgumentException>(() => GameSession.Restore(content,
            saved with { Ai = saved.Ai with { Opportunities = [attempt] } }));
    }

    private static OpportunityOption Option(AdviserOpinion opinion = AdviserOpinion.Unfavorable) => new(
        new("ai:2", 2, new RecruitOfficerCommand(Guid.Empty, 2, 0, 5, 3), [5], 5, 2000, 100, 0,
            Purpose: "personnel") { OfficerOpportunityCost = 100 },
        new(CommandRejection.None, 6, opinion), 60, 10, "RecruitOfficerCommand:3", new(100, 6, 60, null, new(8, 8), null, null));

    [Fact]
    public void ReliableAdviceOverridesThePriorButFallibleAdviceRemainsEvidence()
    {
        var clock = new GameClock(190, 1, 1);
        var unfavorable = Option();
        Assert.Empty(OpportunityPlanner.Plan(new(clock, 100, [unfavorable], true), [], new(100)));
        Assert.Single(OpportunityPlanner.Plan(new(clock, 60, [unfavorable], true), [], new(100)));
        Assert.Equal(100, OpportunityPlanner.Estimate(10, AdviserOpinion.Favorable, 100));
        Assert.Equal(0, OpportunityPlanner.Estimate(90, AdviserOpinion.Unfavorable, 100));
        Assert.Equal(60, OpportunityPlanner.Estimate(60, AdviserOpinion.NoAdviser, 100));
        Assert.Equal(100, AdviserAccuracy.Percent(100));
        Assert.Equal(60, AdviserAccuracy.Percent(70));
        var expensive = Option(AdviserOpinion.Favorable) with { Candidate = unfavorable.Candidate with { MoneyCost = 3000 } };
        Assert.Empty(OpportunityPlanner.Plan(new(clock, 100, [expensive], true), [], new(100)));
    }

    [Fact]
    public void VisiblePressureProducesAnAffordableGiftButPeaceHasNoGiftDemand()
    {
        var basis = MissionScenario.Create(4);
        var definition = basis with { Information = basis.Information with
            { Relations = [new(1, 2, 30)], Corps = basis.Information.Corps.Select(c => c.OwnerId == 2
                ? c with { Treasury = new(30000, 30000) } : c).ToArray() } };
        var session = GameSession.Start(definition, 42);
        var engine = GameSession.EngineFactory(definition)(WorldState.Restore(session.Snapshot));
        engine.EndControl(new("player"));
        var seat = engine.Snapshot.Simulation.Controls.Single(c => c.ForceId == 2);
        var economy = EconomicObservation.Capture(engine, seat, new(seat.ControllerId), 10000, quoteAll: true);
        var pressure = new SupportObservation([], [new(10, 1, 2, new(ArrivalEstimateStatus.Known, 10),
            5000, 5000, true, "public pressure")], true);
        var before = engine.Snapshot;
        var observed = OpportunityObservationCapture.Capture(engine, seat, [(seat.ControllerId, economy)],
            pressure, [], session.MilitaryRules, new(10000));
        StateAssert.Equal(before, engine.Snapshot);
        var gifts = OpportunityPlanner.Plan(observed, [], new(10000)).Where(c => c.Intent is GiftCommand).ToArray();
        Assert.NotEmpty(gifts);
        Assert.All(gifts, gift =>
        {
            Assert.Equal(2000, gift.MoneyCost);
            Assert.True(engine.Validate(new(seat.ControllerId), gift.Intent with
                { CommandId = Guid.NewGuid(), Sequence = engine.Snapshot.Simulation.NextSequence }).Accepted);
        });
        var peaceful = OpportunityObservationCapture.Capture(engine, seat, [(seat.ControllerId, economy)],
            new([], [], true), [], session.MilitaryRules, new(10000));
        Assert.DoesNotContain(peaceful.Options, c => c.Candidate.Intent is GiftCommand);
    }

    [Fact]
    public void FailureFeedbackMatchesTheActionAndReopensOnlyAfterChangedConditionsOrExpiry()
    {
        var session = GameSession.Start(G01ScenarioTests.Fixture(), 190);
        var option = Option(AdviserOpinion.Favorable);
        var attempt = new OpportunityAttempt(2, "ai:2", option.Key, option.Conditions, Guid.NewGuid(), 5,
            session.Snapshot.Clock, session.Snapshot.Clock, "pending");
        var unrelated = OpportunityPlanner.Feedback([attempt], session.Snapshot,
            [new RecruitmentResolvedEvent(session.Snapshot.Clock, 5, 99, 0, 0, false)]);
        Assert.Equal("finished", Assert.Single(unrelated).Outcome);
        var failed = OpportunityPlanner.Feedback([attempt], session.Snapshot,
            [new RecruitmentResolvedEvent(session.Snapshot.Clock, 5, 3, 0, 0, false)]);
        Assert.Equal("failed", Assert.Single(failed).Outcome);
        var observation = new OpportunityObservation(session.Snapshot.Clock, 100, [option], true);
        Assert.Empty(OpportunityPlanner.Plan(observation, failed, new(100)));
        Assert.Single(OpportunityPlanner.Plan(observation with { Options = [option with
            { Conditions = option.Conditions with { ActorBand = new(9, 8) } }] }, failed, new(100)));
        Assert.Single(OpportunityPlanner.Plan(observation with { Clock = failed[0].RetryAfter }, failed, new(100)));
        var restored = GameSession.Restore(G01ScenarioTests.Fixture(), session.Capture() with
            { Ai = session.AiState with { Opportunities = failed } });
        var decoded = SaveService.Decode(SaveService.Encode(restored), G01ScenarioTests.Fixture());
        Assert.Equal(CanonicalJson.Text(failed), CanonicalJson.Text(decoded.AiState.Opportunities));
        Assert.Empty(OpportunityPlanner.Plan(observation, decoded.AiState.Opportunities, new(100)));
    }

    private static SimulationEngine Personnel(int intelligence = 100, int injury = 6, int loyalty = 100, int candidateIntelligence = 80,
        int righteousness = 15)
    {
        var rules = new SimulationRules
        {
            Personnel = new([new(1, new(0, 0))]),
            Economy = new([new(7, 2)], []),
            Governance = new([new(7, 10000, 0) { AbilityBonuses = new(0, 10, 0, 0), Salary = 2 }]),
            Treasures = new([new(9, new(0, 10, 0, 0), 0)])
        };
        var world = WorldState.Create(new(190, 1, 1), 42,
            [new(1, 100) { LeaderId = 1, AdviserId = 2 }],
            [new(1, 1, 0, 100, 100, 0, 0, 0, 0, 0, 0)],
            [new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
             new OfficerState(2, 1, 1, new(50, intelligence, 50, 50), false) { Status = OfficerStatus.Strategist, Injury = injury, OfficeId = 7 }.WithLoyalty(1, 100),
             new OfficerState(3, 1, 1, new(50, candidateIntelligence, 50, 50), false) { Righteousness = righteousness }.WithLoyalty(1, loyalty)],
            treasures: [new(9, 2, TreasureDiscovery.Discovered, null)],
            controls: [new("automatic", 1, ControllerKind.Automatic)],
            corps: [new(1, 1, 1, new(10000, 10000)) { IsRoot = true }]);
        return new(world, rules);
    }

    private static ImmutableArray<AllocationCandidate> Configuration(SimulationEngine engine)
    {
        var seat = engine.Snapshot.Simulation.Controls[0];
        var economy = EconomicObservation.Capture(engine, seat, new(seat.ControllerId), 1000, quoteAll: true);
        return PersonnelConfiguration.Capture(engine, seat, [(seat.ControllerId, economy)], new(1000));
    }

    [Fact]
    public void OnePointCanMakeAdvicePerfectAndMustRemainACandidate()
    {
        var engine = Personnel(intelligence: 79, injury: 0, candidateIntelligence: 100);
        var appointment = Assert.Single(Configuration(engine), c => c.Intent is AppointAdviserCommand);
        Assert.Equal(3, Assert.IsType<AppointAdviserCommand>(appointment.Intent).OfficerId);
    }

    [Fact]
    public void OfficeComparisonDoesNotCountItsOwnBonusAsHolderPotential()
    {
        // The holder has 75 intelligence without the office (65 base + 10 treasure),
        // versus 80 for the candidate. Reassignment improves long-term gain without adding payroll.
        var engine = Personnel(intelligence: 65, injury: 0);
        var office = Assert.Single(Configuration(engine), c => c.Intent is AssignOfficesCommand);
        Assert.Equal(0, office.MoneyCost);
        Assert.Contains(Assert.IsType<AssignOfficesCommand>(office.Intent).Assignments, a => a.OfficerId == 3 && a.OfficeId == 7);
    }

    [Fact]
    public void DiscretionaryMissionsRespectTheCommandersDepartureAndTroopReserve()
    {
        var definition = MissionScenario.Create();
        var session = GameSession.Start(definition, 42);
        var engine = GameSession.EngineFactory(definition)(WorldState.Restore(session.Snapshot));
        var seat = engine.Snapshot.Simulation.Controls.Single(c => c.ForceId == 1);
        var economy = EconomicObservation.Capture(engine, seat, new(seat.ControllerId), 10000, quoteAll: true);
        OpportunityObservation Capture(uint required) => OpportunityObservationCapture.Capture(engine, seat,
            [(seat.ControllerId, economy)], new([], [], true) { GarrisonRequirements =
                economy.Domestic.Settlements.SelectMany(s => s.OfficerIds.Select(id => new GarrisonRequirement(s.Id, id, required))).ToImmutableArray() },
            [], session.MilitaryRules, new(10000));
        var safe = Capture(3000);
        Assert.Contains(safe.Options, o => o.Candidate.Intent is UnitStrategyCommand { Strategy: UnitStrategyKind.Reinforce });
        Assert.DoesNotContain(Capture(8000).Options, o => o.Candidate.Intent is UnitStrategyCommand { Strategy: UnitStrategyKind.Reinforce });
        Assert.Empty(Capture(11000).Options);
    }

    [Fact]
    public void ReinforcementCannotSpendAnotherCorpsFoodReserve()
    {
        var basis = MissionScenario.Create();
        var definition = basis with
        {
            Settlements = [..basis.Settlements, new(4, "receiver", 1, 15, 1, 0, 100, 100, 0, []) { CorpsId = 4 }],
            Officers = [..basis.Officers, new("receiver-governor", new(4, 1, 4, new(50, 50, 50, 50), false))],
            Units = basis.Units.Select(u => u.Id == 10 ? u with { CorpsId = 4 } : u).ToArray(),
            Military = basis.Military! with { Places = basis.Military.Places.Add(new(4, new(77, 7), new(1, 1))) },
            Information = basis.Information with { Corps = [..basis.Information.Corps, new(4, 1, 4, new(10000, 10000))] }
        };
        var session = GameSession.Start(definition, 42);
        var engine = GameSession.EngineFactory(definition)(WorldState.Restore(session.Snapshot));
        var seat = engine.Snapshot.Simulation.Controls.Single(c => c.ForceId == 1);
        var economy = EconomicObservation.Capture(engine, seat, new(seat.ControllerId), 10000, quoteAll: true);
        var observation = OpportunityObservationCapture.Capture(engine, seat, [(seat.ControllerId, economy)],
            new([], [], true), [], session.MilitaryRules, new(10000));
        var reinforcement = observation.Options.First(o => o.Candidate.SourceId == 1 &&
            o.Candidate.Intent is UnitStrategyCommand { Strategy: UnitStrategyKind.Reinforce }).Candidate;
        Assert.Equal(4, reinforcement.ReceivingAccountId);
        Assert.True(reinforcement.ReceivingFood > 0);
        var poorReceiver = economy with { Accounts = economy.Accounts.Select(a => a.Id == 4 ? a with { Food = a.RequiredFood } : a).ToImmutableArray() };
        Assert.Null(SharedAllocator.Decide([(seat.ControllerId, poorReceiver)], [reinforcement], [], 1000,
            new HashSet<string> { seat.ControllerId }, new HashSet<string>()).Candidate);
        Assert.Equal(reinforcement, SharedAllocator.Decide([(seat.ControllerId, economy)], [reinforcement], [], 1000,
            new HashSet<string> { seat.ControllerId }, new HashSet<string>()).Candidate);
    }

    [Fact]
    public void TemporaryInjuryChangesAdviserWithoutStrippingLongTermEquipmentAndOffice()
    {
        var engine = Personnel();
        var before = engine.Snapshot;
        var candidates = Configuration(engine);
        StateAssert.Equal(before, engine.Snapshot);
        Assert.DoesNotContain(candidates, c => c.Intent is ConfiscateTreasureCommand or AssignOfficesCommand);
        var appointment = Assert.IsType<AppointAdviserCommand>(Assert.Single(candidates).Intent);
        Assert.Equal(3, appointment.OfficerId);
        Assert.True(engine.Execute(new("automatic"), appointment with { CommandId = Guid.NewGuid(), Sequence = 0 }).Submission.Accepted);
        Assert.Equal(2, Assert.Single(engine.Snapshot.Treasures).HolderId);
        Assert.Equal(7, engine.Snapshot.Officers.Single(p => p.Id == 2).OfficeId);
        var healthy = new SimulationEngine(WorldState.Restore(engine.Snapshot with
            { Officers = engine.Snapshot.Officers.Select(p => p.Id == 2 ? p with { Injury = 0 } : p).ToArray() }),
            new SimulationRules { Personnel = new([new(1, new(0, 0))]),
            Economy = new([new(7, 2)], []),
                Governance = new([new(7, 10000, 0) { AbilityBonuses = new(0, 10, 0, 0), Salary = 2 }]),
                Treasures = new([new(9, new(0, 10, 0, 0), 0)]) });
        Assert.Equal(2, Assert.IsType<AppointAdviserCommand>(Assert.Single(Configuration(healthy)).Intent).OfficerId);
    }

    [Fact]
    public void LastingPostValueCanJustifyReassignmentAndRewardsHaveNoOpeningWindow()
    {
        var lasting = Configuration(Personnel(intelligence: 40, injury: 0));
        Assert.Contains(lasting, c => c.Intent is AssignOfficesCommand);
        Assert.Contains(lasting, c => c.Intent is ConfiscateTreasureCommand);
        var engine = Personnel(loyalty: 80);
        var reward = Assert.IsType<RewardOfficerCommand>(Assert.Single(Configuration(engine), c => c.Intent is RewardOfficerCommand).Intent);
        Assert.True(engine.Execute(new("automatic"), reward with { CommandId = Guid.NewGuid(), Sequence = 0 }).Submission.Accepted);
        Assert.DoesNotContain(Configuration(engine), c => c.Intent is RewardOfficerCommand);
    }

    [Fact]
    public void ConfiscationAndGrantCompleteFromOneConfigurationBatch()
    {
        var engine = Personnel(intelligence: 40, injury: 0);
        List<DomainEvent> events = [];
        List<AllocationTrace> traces = [];
        List<AllocationBatchTrace> batches = [];
        var seat = engine.Snapshot.Simulation.Controls[0];
        var economy = EconomicObservation.Capture(engine, seat, new(seat.ControllerId), 10000);
        var configurationWork = new AllocationWork(10000);
        PersonnelConfiguration.Capture(engine, seat, [(seat.ControllerId, economy)], configurationWork);
        AllocationTurnRunner.Run(engine, seat, null, new(), events, traces, batches);
        var transfers = events.OfType<TreasureTransferredEvent>().ToArray();
        Assert.Equal(new[] { "confiscate", "grant" }, transfers.Select(e => e.Reason));
        Assert.Equal(3, transfers[1].HolderId);
        Assert.Equal(3, engine.Snapshot.Treasures.Single().HolderId);
        Assert.DoesNotContain(traces, t => t.Outcome == AllocationOutcome.Rejected);
        Assert.Equal(configurationWork.Maximum - configurationWork.Remaining,
            batches.Single(b => b.IsTurnSummary).WorkByKind["configuration"]);
    }

    [Fact]
    public void AbilityChangesKeepConfigurationButInvalidateDependentForecasts()
    {
        var engine = Personnel(intelligence: 40, injury: 0);
        var seat = engine.Snapshot.Simulation.Controls[0];
        var economy = EconomicObservation.Capture(engine, seat, new(seat.ControllerId), 1000);
        var configuration = Configuration(engine);
        var office = configuration.First(c => c.Intent is AssignOfficesCommand);
        var batch = new AllocationObservations { Economies = [(seat.ControllerId, economy)], Configuration = configuration,
            Support = new([], [], true), Field = [], Opportunities = [], Campaign = [] };
        var command = office.Intent with { CommandId = Guid.NewGuid(), Sequence = engine.Snapshot.Simulation.NextSequence };
        var submitted = engine.Execute(new(seat.ControllerId), command);
        Assert.True(submitted.Submission.Accepted);
        batch.Applied(engine, seat, null, command, office.Intent, office, submitted.Events);
        Assert.Equal(configuration.Select(c => c.Intent), batch.Configuration.Select(c => c.Intent));
        Assert.True(batch.Opportunities.IsDefault && batch.Field.IsDefault && batch.Campaign.IsDefault);
        Assert.NotNull(batch.Support);
        var fresh = EconomicObservation.Capture(engine, seat, new(seat.ControllerId), 1000);
        Assert.Equal(fresh.Domestic.Abilities[3], batch.Economies[0].Observation.Domestic.Abilities[3]);
    }

    [Fact]
    public void ZeroGainRewardNeverBecomesAnAiCandidate()
    {
        Assert.Equal(0, PersonnelRewardMath.Gain(60, 19));
        Assert.DoesNotContain(Configuration(Personnel(loyalty: 60, righteousness: 19)), c => c.Intent is RewardOfficerCommand);
        Assert.Contains(Configuration(Personnel(loyalty: 60, righteousness: 15)), c => c.Intent is RewardOfficerCommand);
    }

    [Fact]
    public void RewardUpdatesMoneyWithoutReplacingDomesticObservation()
    {
        var engine = Personnel(loyalty: 60);
        var seat = engine.Snapshot.Simulation.Controls[0];
        var economy = EconomicObservation.Capture(engine, seat, new(seat.ControllerId), 1000);
        var batch = new AllocationObservations { Economies = [(seat.ControllerId, economy)] };
        var intent = new RewardOfficerCommand(Guid.Empty, 1, 0, 3);
        var command = intent with { CommandId = Guid.NewGuid(), Sequence = engine.Snapshot.Simulation.NextSequence };
        var result = engine.Execute(new(seat.ControllerId), command);
        Assert.True(result.Submission.Accepted);
        batch.Applied(engine, seat, null, command, intent, null, result.Events);
        Assert.Same(economy.Domestic, batch.Economies[0].Observation.Domestic);
        Assert.Equal(economy.Accounts[0].Money - 100, batch.Economies[0].Observation.Accounts[0].Money);
    }

    [Fact]
    public void AutomaticSearchAnswersRecruitmentAndReplaysWithoutUserInput()
    {
        var definition = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "automatic-search", 190, 1, 1, 2,
            [new(1, "automatic", 1, 100) { LeaderId = 1 }, new(2, "player", 2, 100) { LeaderId = 4 }],
            [new(1, "home", 1, 0, 0, 0, 100, 100, 0, []), new(2, "player-home", 2, 1, 0, 0, 100, 100, 0, [])], [], [])
        {
            Officers = [new("ruler", new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }),
                new("searcher", new(2, 1, 1, new(50, 50, 500, 50), false)),
                new("hidden", new OfficerState(3, null, 1, new(50, 50, 50, 50), false)
                    { Status = OfficerStatus.Undiscovered, ForceLoyalties = ImmutableDictionary<int, int>.Empty.Add(1, 240) }),
                new("other-ruler", new(4, 2, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler })],
            Information = InformationDefinitions.Empty with { Corps = [new(1, 1, 1, new(10000, 10000)) { IsRoot = true }, new(2, 2, 4, new(10000, 10000)) { IsRoot = true }] }
        };
        var session = GameSession.Start(definition, 190);
        session.BeginRecording();
        session.EndControl(new("player"));
        Assert.True(session.Execute(new("ai:1"), new SearchCommand(Guid.NewGuid(), 1, 0, 2, 1)).Submission.Accepted);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        var result = session.Apply(session.Request(new AdvanceOperation()));
        Assert.Equal(SessionDisposition.Accepted, result.Disposition);
        Assert.Null(session.GetPendingResponse());
        Assert.Contains(result.Events.OfType<SearchResolvedEvent>(), e => e.OfficerId == 2 && e.Recruited);
        EventAssert.Equal(result.Events, restored.AdvanceXun().Events);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
    }
}

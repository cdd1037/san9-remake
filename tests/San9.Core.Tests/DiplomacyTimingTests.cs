using San9.Tests;
using System.Collections.Immutable;
using System.Text.Json;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class DiplomacyTimingTests
{
    private static SimulationRules Rules(int distance) => new()
    {
        Personnel = new([new(1, new(0, 0)), new(2, new(distance, 0)), new(3, new(900, 0))])
    };

    private static WorldState World(GameClock clock) => WorldState.Create(clock, 42,
        [new ForceState(1, 100) { LeaderId = 11 },
         new ForceState(2, 100) { LeaderId = 21 },
         new ForceState(3, 100) { LeaderId = 31 }],
        [new(1, 1, 10000, 100, 100, 0, 0, 0, 0, 0, 0),
         new(2, 2, 10000, 100, 100, 0, 0, 0, 0, 0, 0),
         new(3, 3, 10000, 100, 100, 0, 0, 0, 0, 0, 0)],
        [new OfficerState(11, 1, 1, new(50, 50, 100, 50), false) { Status = OfficerStatus.Ruler },
         new(12, 1, 1, new(50, 50, 100, 50), false),
         new OfficerState(13, 1, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Captive },
         new OfficerState(21, 2, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
         new OfficerState(31, 3, 3, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }],
        controls: [new("first", 1, ControllerKind.Player), new("second", 2, ControllerKind.Player),
            new("third", 3, ControllerKind.Automatic)], corps: [new San9.Core.Domain.CorpsState(1, 1, 11, new(10000, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 21, new(10000, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(3, 3, 31, new(10000, 10000)) { IsRoot = true }]);

    private static GameCommand Command(int kind) => kind switch
    {
        0 => new GiftCommand(Guid.NewGuid(), 1, 0, 12, 2, new(GiftKind.Money, 1000, null)),
        1 => new RequestAttackCommand(Guid.NewGuid(), 1, 0, 12, 2, 3),
        2 => new ReturnPrisonerCommand(Guid.NewGuid(), 1, 0, 12, 2, 13),
        _ => new DemandSurrenderCommand(Guid.NewGuid(), 1, 0, 12, 2)
    };

    private static AdvanceXunResult Advance(SimulationEngine engine)
    {
        foreach (var seat in engine.Snapshot.Simulation.Controls) engine.EndControl(new(seat.ControllerId));
        return engine.AdvanceXun();
    }

    public static IEnumerable<object[]> GrowthCases()
    {
        for (int kind = 0; kind < 3; kind++)
        {
            yield return [kind, true, 99];
            yield return [kind, false, 99];
            yield return [kind, true, 100];
        }
    }

    [Theory]
    [MemberData(nameof(GrowthCases))]
    public void DiplomacyGrowthOccursOnlyAtSuccessfulSettlement(int kind, bool accept, int politics)
    {
        var world = World(new(190, 1, 1));
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Officers[12] with
        { BaseAbilities = mutation.Officers[12].BaseAbilities with { Politics = politics } });
        mutation.SetSimulation(mutation.Simulation with { RandomStreams = [new("diplomacy-politics-growth", 52)] });
        world.Commit(mutation);
        var rules = Rules(90);
        var engine = new SimulationEngine(world, rules);
        Assert.True(engine.Execute(new("first"), Command(kind)).Submission.Accepted);
        var restored = new SimulationEngine(WorldState.Restore(engine.CaptureStableState()), rules);
        var quoteId = Guid.NewGuid();
        var answerId = Guid.NewGuid();

        IReadOnlyList<DomainEvent> Settle(SimulationEngine current)
        {
            Assert.DoesNotContain(Advance(current).Events, e => e is OfficerAbilityGrownEvent);
            var task = Assert.Single(current.Snapshot.Simulation.Tasks);
            var response = new RespondDailyTaskCommand(quoteId, 2, 1, task.Id, 0, kind == 0 ? accept : true)
                { Offer = kind == 0 ? null : new(CompensationKind.Money, 100, null, null) };
            var result = current.Execute(new("second"), response);
            Assert.True(result.Submission.Accepted);
            if (kind != 0)
            {
                Assert.DoesNotContain(result.Events, e => e is OfficerAbilityGrownEvent);
                Assert.Equal(52UL, current.Snapshot.Simulation.RandomStreams.Single(s => s.Name == "diplomacy-politics-growth").State);
                result = current.Execute(new("first"), new RespondDailyTaskCommand(answerId, 1, 2, task.Id, 1, accept));
                Assert.True(result.Submission.Accepted);
            }
            return result.Events;
        }

        var events = Settle(engine);
        Assert.Equal(CanonicalJson.Text(events), CanonicalJson.Text(Settle(restored)));
        StateAssert.Equal(engine.Snapshot, restored.Snapshot);
        bool grows = accept && politics < 100;
        var growth = events.OfType<OfficerAbilityGrownEvent>().ToArray();
        if (grows)
        {
            var actual = Assert.Single(growth);
            Assert.Equal(new OfficerAbilityGrownEvent(actual.Clock, 12, "politics", 99, 100), actual);
        }
        else Assert.Empty(growth);
        Assert.Equal(grows ? 100 : politics, engine.Snapshot.Officers.Single(p => p.Id == 12).BaseAbilities.Politics);
        var random = new StableRandom(52);
        random.NextUInt64();
        Assert.Equal(grows ? random.State : 52UL,
            engine.Snapshot.Simulation.RandomStreams.Single(s => s.Name == "diplomacy-politics-growth").State);
    }

    [Fact]
    public void QuarterlyRulerDeathReturnsEnvoyBeforeDailyCommandsAcrossRestore()
    {
        var world = World(new(190, 1, 1));
        var mutation = world.BeginMutation();
        mutation.SetClock(new(190, 12, 21));
        mutation.Replace(mutation.Officers[21] with { BirthYear = 90 });
        foreach (var home in mutation.Settlements.Values.ToArray())
            mutation.Replace(home with { Durability = 100 });
        mutation.SetSimulation(mutation.Simulation with
        {
            NaturalDeathsEnabled = true,
            Controls = [new("first", 1, ControllerKind.Player),
                new("second", 2, ControllerKind.Automatic), new("third", 3, ControllerKind.Automatic)]
        });
        world.Commit(mutation);
        var rules = Rules(600);
        var engine = new SimulationEngine(world, rules);
        Assert.True(engine.Execute(new("first"), Command(0)).Submission.Accepted);
        var restored = new SimulationEngine(WorldState.Restore(engine.CaptureStableState()), rules);

        var events = Advance(engine).Events;
        EventAssert.Equal(events, Advance(restored).Events);
        StateAssert.Equal(engine.Snapshot, restored.Snapshot);
        Assert.Equal(2, Assert.Single(events.OfType<ForceEliminatedEvent>()).ForceId);
        var returning = Assert.Single(engine.Snapshot.Simulation.Tasks);
        Assert.Equal(PersonnelReturns.Kind, returning.Kind);
        Assert.False(returning.Journey!.ReturnLeg!.AfterDailyCommands);
        Assert.True(returning.Journey.ReturnLeg.DiplomaticEnvoy);
        Assert.Equal(10, returning.Journey.ReturnLeg.CountdownDays);
        Assert.Equal(new GameClock(191, 1, 10), returning.Due);
        restored = new SimulationEngine(WorldState.Restore(engine.CaptureStableState()), rules);
        var completion = Advance(engine).Events;
        EventAssert.Equal(completion, Advance(restored).Events);
        Assert.Equal(new GameClock(191, 1, 10), Assert.Single(completion.OfType<PersonnelReturnedEvent>()).Clock);
        Assert.Empty(engine.Snapshot.Simulation.Tasks);
    }

    private sealed class DialogRules : SimulationRules
    {
        internal override TaskRule? FindTask(string kind) => kind == "dialog-fixture" ? new DialogRule() : base.FindTask(kind);

        private sealed class DialogRule : TaskRule
        {
            internal override int DurationDays(PersistentTask task) => 2;
            internal override bool Validate(WorldMutation world, PersistentTask task) => true;
            internal override int? DecisionRecipient(WorldMutation world, PersistentTask task) => 1;
            internal override void CompleteDecision(WorldMutation world, PersistentTask task, bool accept, List<DomainEvent> events) { }
        }
    }

    [Theory]
    [InlineData(30, 1)]
    [InlineData(31, 2)]
    [InlineData(90, 3)]
    [InlineData(300, 10)]
    public void BoundaryDiplomacyReturnConsumesTheNewDatesFirstDailyStage(int distance, int days)
    {
        var world = World(new(190, 1, 1));
        var mutation = world.BeginMutation();
        mutation.SetSimulation(mutation.Simulation with { Controls = mutation.Simulation.Controls
            .Select(c => c.ForceId == 2 ? c with { Kind = ControllerKind.Automatic } : c).ToImmutableArray() });
        world.Commit(mutation);
        var rules = Rules(distance);
        var engine = new SimulationEngine(world, rules);
        Assert.True(engine.Execute(new("first"), Command(0)).Submission.Accepted);
        Advance(engine);
        var returning = Assert.Single(engine.Snapshot.Simulation.Tasks);
        Assert.Equal(new GameClock(190, 1, 11), returning.Started);
        Assert.Equal(days, returning.Journey!.ReturnLeg!.CountdownDays);
        var expectedDate = new GameClock(190, 1, 10 + days);
        Assert.Equal(expectedDate, returning.Due);
        var restored = new SimulationEngine(WorldState.Restore(engine.CaptureStableState()), rules);
        var result = Advance(engine);
        EventAssert.Equal(result.Events, Advance(restored).Events);
        Assert.Equal(expectedDate, Assert.Single(result.Events.OfType<PersonnelReturnedEvent>()).Clock);
    }

    private sealed class RecoveryDialogRules : SimulationRules
    {
        internal override TaskRule? FindTask(string kind) => kind == "recovery-dialog" ? new RecoveryRule(this) : base.FindTask(kind);
        private sealed class RecoveryRule(SimulationRules rules) : TaskRule
        {
            internal override int DurationDays(PersistentTask task) => 2;
            internal override bool Validate(WorldMutation world, PersistentTask task) => true;
            internal override int? DecisionRecipient(WorldMutation world, PersistentTask task) => 1;
            internal override void CompleteDecision(WorldMutation world, PersistentTask task, bool accept, List<DomainEvent> events)
            {
                Assert.Equal(CommandRejection.None, PrisonerRelease.Recover(world, 13, 2, task.Sequence,
                    rules.Personnel, rules.Economy, events));
            }
        }
    }

    [Fact]
    public void DailyDecisionRecoveryDoesNotConsumeItsNewReturnOnTheCreationDay()
    {
        var world = World(new(190, 1, 1));
        var mutation = world.BeginMutation();
        foreach (var place in mutation.Settlements.Values.ToArray()) mutation.Replace(place with { Durability = 100 });
        world.Commit(mutation);
        var rules = new RecoveryDialogRules { Personnel = Rules(90).Personnel };
        var engine = new SimulationEngine(world, rules);
        Assert.True(engine.Execute(new("first"), new StartTaskCommand(Guid.NewGuid(), 1, 0,
            "recovery-dialog", 12, 1)).Submission.Accepted);
        Advance(engine);
        Assert.Equal(new GameClock(190, 1, 3), engine.Snapshot.Clock);
        var pending = engine.Snapshot.Simulation.PendingDailyTask!;
        var response = new RespondDailyTaskCommand(Guid.NewGuid(), 1, 1, pending.RemainingTaskIds[0], 0, true);
        var result = engine.Execute(new("first"), response);
        Assert.True(result.Submission.Accepted);
        var returning = Assert.Single(result.Events.OfType<TaskStartedEvent>()).Task;
        Assert.Equal(3, returning.Journey!.ReturnLeg!.CountdownDays);
        Assert.Equal(new GameClock(190, 1, 6), returning.Due);
        Assert.Equal(new GameClock(190, 1, 6), Assert.Single(result.Events.OfType<PersonnelReturnedEvent>()).Clock);
    }

    [Fact]
    public void DailyDecisionResumesIntoBoundaryDiplomacyWithoutRepeatingTheDay()
    {
        var rules = new DialogRules { Personnel = Rules(90).Personnel };
        var engine = new SimulationEngine(World(new(190, 1, 1)), rules);
        var dialog = new StartTaskCommand(Guid.NewGuid(), 1, 0, "dialog-fixture", 11, 1);
        Assert.True(engine.Execute(new("first"), dialog).Submission.Accepted);
        var gift = ((GiftCommand)Command(0)) with { Sequence = 1 };
        Assert.True(engine.Execute(new("first"), gift).Submission.Accepted);
        var all = Advance(engine).Events.ToList();
        Assert.Equal(new GameClock(190, 1, 3), engine.Snapshot.Clock);
        Assert.Equal(TaskResolutionPhase.DailyCommands, engine.Snapshot.Simulation.PendingDailyTask!.Phase);
        engine = new SimulationEngine(WorldState.Restore(engine.Snapshot), rules);
        var firstAnswer = engine.Execute(new("first"), new RespondDailyTaskCommand(Guid.NewGuid(), 1, 2, dialog.CommandId, 0, true));
        Assert.True(firstAnswer.Submission.Accepted);
        all.AddRange(firstAnswer.Events);
        Assert.Equal(new GameClock(190, 1, 11), engine.Snapshot.Clock);
        Assert.Equal(TaskResolutionPhase.XunBoundary, engine.Snapshot.Simulation.PendingDailyTask!.Phase);
        Assert.DoesNotContain(all, e => e is XunAdvancedEvent);
        engine = new SimulationEngine(WorldState.Restore(engine.Snapshot), rules);
        var secondAnswer = engine.Execute(new("second"), new RespondDailyTaskCommand(Guid.NewGuid(), 2, 3, gift.CommandId, 0, false));
        Assert.True(secondAnswer.Submission.Accepted);
        all.AddRange(secondAnswer.Events);
        Assert.Equal(new GameClock(190, 1, 11), engine.Snapshot.Clock);
        Assert.Single(all.OfType<XunAdvancedEvent>());
        foreach (var stage in new[] { ResolutionStage.BeforeDailyCommands, ResolutionStage.DailyCommands,
            ResolutionStage.DailyMapUpdate, ResolutionStage.UnitAndBuildingEvents })
            Assert.Equal(10, all.OfType<StageCompletedEvent>().Count(e => e.Stage == stage));
        Assert.Single(all.OfType<StageCompletedEvent>(), e => e.Stage == ResolutionStage.XunUpdate);
    }

    public static IEnumerable<object[]> Journeys()
    {
        for (int kind = 0; kind < 4; kind++)
            foreach (var (distance, days) in new[] { (0, 10), (1, 10), (300, 10), (301, 20), (600, 20), (601, 30) })
                yield return [kind, distance, days];
    }

    [Theory]
    [MemberData(nameof(Journeys))]
    public void AllDiplomacyArrivesOnRoundedXunBoundaryBeforeWorldUpdate(int kind, int distance, int days)
    {
        // Crosses both month and year boundaries for multi-xun journeys.
        var started = new GameClock(190, 12, 21);
        var rules = Rules(distance);
        var engine = new SimulationEngine(World(started), rules);
        Assert.True(engine.Execute(new("first"), Command(kind)).Submission.Accepted);
        var task = Assert.Single(engine.Snapshot.Simulation.Tasks);
        Assert.Equal(days, task.Due.DayNumber - started.DayNumber);
        for (int elapsed = 10; elapsed < days; elapsed += 10)
        {
            var earlier = Advance(engine);
            Assert.Null(earlier.Snapshot.Simulation.PendingDailyTask);
            Assert.Contains(earlier.Snapshot.Simulation.Tasks, t => t.Id == task.Id);
        }
        var arrived = Advance(engine);
        Assert.Equal(task.Due, arrived.Snapshot.Clock);
        Assert.Equal(TaskResolutionPhase.XunBoundary, arrived.Snapshot.Simulation.PendingDailyTask!.Phase);
        Assert.DoesNotContain(arrived.Events, e => e is XunAdvancedEvent ||
            e is StageCompletedEvent { Stage: ResolutionStage.XunUpdate });
        Assert.Equal(10, arrived.Events.OfType<StageCompletedEvent>().Count(e => e.Stage == ResolutionStage.BeforeDailyCommands));
        Assert.Equal(10, arrived.Events.OfType<StageCompletedEvent>().Count(e => e.Stage == ResolutionStage.DailyMapUpdate));
        var saved = engine.Snapshot;
        var restored = new SimulationEngine(WorldState.Restore(saved), rules);
        var response = new RespondDailyTaskCommand(Guid.NewGuid(), 2, 1, task.Id, 0, false);
        var answered = engine.Execute(new("second"), response);
        Assert.True(answered.Submission.Accepted);
        EventAssert.Equal(answered.Events, restored.Execute(new("second"), response).Events);
        StateAssert.Equal(engine.Snapshot, restored.Snapshot);
        Assert.Equal(task.Due, engine.Snapshot.Clock);
        Assert.Single(answered.Events.OfType<XunAdvancedEvent>());
        Assert.Single(answered.Events.OfType<StageCompletedEvent>(), e => e.Stage == ResolutionStage.XunUpdate);
        Assert.DoesNotContain(answered.Events.OfType<StageCompletedEvent>(), e => e.Stage is
            ResolutionStage.BeforeDailyCommands or ResolutionStage.DailyCommands or ResolutionStage.DailyMapUpdate);
        Assert.DoesNotContain(engine.Snapshot.Simulation.Tasks, t => t.Id == task.Id);
        if (distance == 0) Assert.Empty(engine.Snapshot.Simulation.Tasks);
        else
        {
            var returning = Assert.Single(engine.Snapshot.Simulation.Tasks);
            Assert.Equal((distance + 29) / 30, returning.Journey!.ReturnLeg!.CountdownDays);
            Assert.True(returning.Journey.ReturnLeg.DiplomaticEnvoy);
            Assert.Null(returning.Gift);
            Assert.Null(returning.PrisonerReturn);
            Assert.Null(returning.AttackRequest);
            Assert.Null(returning.Surrender);
            _ = new SimulationEngine(WorldState.Restore(JsonSerializer.Deserialize<WorldSnapshot>(
                CanonicalJson.Text(engine.Snapshot), CanonicalJson.Options)!), rules);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void InterruptedOutboundAndSubsequentReturnRerouteUseDifferentCorrections(int kind)
    {
        var rules = new SimulationRules { Personnel = new([new(1, new(0, 0)), new(2, new(330, 0)), new(3, new(690, 0))]) };
        var engine = new SimulationEngine(World(new(190, 1, 1)), rules);
        Assert.True(engine.Execute(new("first"), Command(kind)).Submission.Accepted);
        var state = WorldState.Restore(engine.Snapshot).BeginMutation();
        var outgoing = Assert.Single(state.Simulation.Tasks) with { ProgressDays = 1 };
        state.Replace(state.Settlements[1] with { OwnerId = 2 });
        state.Replace(state.Settlements[3] with { OwnerId = 1 });
        state.SetSimulation(state.Simulation with { Tasks = [] });
        List<DomainEvent> events = [];
        PersonnelReturns.Start(state, outgoing, rules.Personnel, events, false);
        var returning = Assert.Single(state.Simulation.Tasks);
        Assert.Equal(1, returning.Journey!.ReturnLeg!.CountdownDays);
        Assert.True(returning.Journey.ReturnLeg.DiplomaticEnvoy);

        // The new return is type 37: D=23 and R=1 produce 22, not a diplomatic rounded correction.
        state.Replace(state.Settlements[3] with { OwnerId = 3 });
        state.Replace(state.Settlements[1] with { OwnerId = 1 });
        state.SetSimulation(state.Simulation with { Tasks = [] });
        PersonnelReturns.Start(state, returning, rules.Personnel, events, false);
        var rerouted = Assert.Single(state.Simulation.Tasks);
        Assert.Equal(22, rerouted.Journey!.ReturnLeg!.CountdownDays);
        Assert.True(rerouted.Journey.ReturnLeg.DiplomaticEnvoy);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AllDiplomacyFreezesLiveSourceAndDestinationCoordinates(int kind)
    {
        var rules = new SimulationRules
        {
            Personnel = Rules(30).Personnel,
            Military = new(new MilitaryDefinition(200, 1, Enumerable.Repeat(0, 200).ToImmutableArray(),
                [new(0, 1, null)], [], [])
            {
                Facilities = [new(1, 100, 100, new(1, 1), 0, true, true, true, 500000, null, null, null, [])],
                ConstructionSites = [new(1, new(60, 0), [1]), new(1, new(90, 0), [1]),
                    new(2, new(390, 0), [1]), new(2, new(690, 0), [1])]
            })
        };
        var world = World(new(190, 1, 1));
        var state = world.BeginMutation();
        state.Replace(state.Settlements[1] with { Kind = SettlementKind.Facility, Facility = new(1, new(60, 0)) });
        state.Replace(state.Settlements[2] with { Kind = SettlementKind.Facility, Facility = new(1, new(390, 0)) });
        world.Commit(state);
        var engine = new SimulationEngine(world, rules);
        Assert.True(engine.Execute(new("first"), Command(kind)).Submission.Accepted);
        var outgoing = Assert.Single(engine.Snapshot.Simulation.Tasks);
        Assert.Equal(new WorldPosition(60, 0), outgoing.Journey!.Route.Origin);
        Assert.Equal(new WorldPosition(390, 0), outgoing.Journey.Route.Destination);
        Assert.Equal(20, outgoing.Due.DayNumber - outgoing.Started.DayNumber);
        state = world.BeginMutation();
        state.Replace(state.Settlements[1] with { Facility = new(1, new(90, 0)) });
        state.Replace(state.Settlements[2] with { Facility = new(1, new(690, 0)) });
        world.Commit(state);
        var saved = JsonSerializer.Deserialize<WorldSnapshot>(CanonicalJson.Text(world.CreateSnapshot()), CanonicalJson.Options)!;
        var restored = new SimulationEngine(WorldState.Restore(saved), rules);
        Assert.Equal(outgoing.Journey.Route, Assert.Single(restored.Snapshot.Simulation.Tasks).Journey!.Route);
    }

    [Fact]
    public void MultipleBoundaryDecisionsCompleteTheXunOnlyAfterLastAnswer()
    {
        var engine = new SimulationEngine(World(new(190, 1, 1)), Rules(90));
        var first = (GiftCommand)Command(0);
        Assert.True(engine.Execute(new("first"), first).Submission.Accepted);
        var second = first with { CommandId = Guid.NewGuid(), Sequence = 1, OfficerId = 11 };
        Assert.True(engine.Execute(new("first"), second).Submission.Accepted);
        var all = Advance(engine).Events.ToList();
        foreach (var id in new[] { first.CommandId, second.CommandId })
        {
            Assert.Equal(id, engine.Snapshot.Simulation.PendingDailyTask!.RemainingTaskIds[0]);
            var result = engine.Execute(new("second"), new RespondDailyTaskCommand(Guid.NewGuid(), 2,
                engine.Snapshot.Simulation.NextSequence, id, 0, false));
            Assert.True(result.Submission.Accepted);
            all.AddRange(result.Events);
            if (id == first.CommandId) Assert.DoesNotContain(result.Events, e => e is XunAdvancedEvent or StageCompletedEvent);
        }
        Assert.Equal(new GameClock(190, 1, 11), engine.Snapshot.Clock);
        Assert.Single(all.OfType<XunAdvancedEvent>());
        Assert.Equal(2, all.OfType<GiftResolvedEvent>().Count());
        foreach (var stage in new[] { ResolutionStage.BeforeDailyCommands, ResolutionStage.DailyCommands,
            ResolutionStage.DailyMapUpdate, ResolutionStage.UnitAndBuildingEvents })
            Assert.Equal(10, all.OfType<StageCompletedEvent>().Count(e => e.Stage == stage));
        Assert.Single(all.OfType<StageCompletedEvent>(), e => e.Stage == ResolutionStage.XunUpdate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void FieldRulerPositionDeterminesTravelInsteadOfRegionCenter(int kind)
    {
        var world = World(new(190, 1, 1)).BeginMutation();
        var position = new WorldPosition(302, 2);
        world.AddUnit(new UnitState(50, 2, position, 1000));
        world.Replace(world.Officers[21] with { SettlementId = null, UnitId = 50 });
        var rules = new SimulationRules
        {
            Personnel = Rules(90).Personnel,
            Military = new(new(200, 3, Enumerable.Repeat(0, 600).ToImmutableArray(), [new(0, 1, null)], [], [])
                { RegionIds = Enumerable.Repeat<int?>(2, 600).ToImmutableArray() })
        };
        List<DomainEvent> events = [];
        var rejection = Command(kind) switch
        {
            GiftCommand c => GiftDiplomacy.Start(world, new("first"), c, rules, events),
            RequestAttackCommand c => AttackRequestDiplomacy.Start(world, new("first"), c, rules, events),
            ReturnPrisonerCommand c => PrisonerReturnDiplomacy.Start(world, new("first"), c, rules, events),
            DemandSurrenderCommand c => SurrenderDiplomacy.Start(world, new("first"), c, rules, events),
            _ => throw new InvalidOperationException()
        };
        Assert.Equal(CommandRejection.None, rejection);
        var task = Assert.Single(world.Simulation.Tasks);
        Assert.Equal(position, task.Journey!.Route.Destination);
        Assert.Equal(new GameClock(190, 1, 21), task.Due);
        Assert.True(rules.FindTask(task.Kind)!.HasValidPayload(task));
        Assert.Equal(20, rules.FindTask(task.Kind)!.DurationDays(task));
    }
}

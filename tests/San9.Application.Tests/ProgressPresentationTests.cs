using San9.Tests;
using System.Collections.Immutable;
using San9.Application.Persistence;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class ProgressPresentationTests
{
    [Fact]
    public void ContactAndWaypointFactsPublishIndependentSpeechesAndRecordsOnceAtTheirLocations()
    {
        var session = GameSession.Start(Scenario(), 42);
        var before = session.View;
        var roles = ProgressPresentationDefinition.MapSpeechRoles.Concat(["unit-contact", "settlement-contact"]).Distinct().ToArray();
        var texts = MissionScenario.Texts(before) with
        { Messages = roles.ToDictionary(r => r, r => new MessageDefinition([new MessageLiteral(r)])) };
        var progress = new ProgressPresentationDefinition(new()
        { ["unit-contact"] = new("unit-contact", []), ["settlement-contact"] = new("settlement-contact", []),
            ["waypoint-reached"] = new("waypoint-reached", []) }, [])
        { MapSpeeches = ProgressPresentationDefinition.MapSpeechRoles.ToDictionary(r => r, r => new DecisionMessageDefinition(r, [], "actor")) };
        var content = CommandPresentation.Empty with { Progress = progress };
        var own = new BattleObjectContext(10, false, 1, 3, new(62, 12), 1, false);
        var enemy = new BattleObjectContext(20, false, 2, 23, new(67, 12), 2, false);
        var attack = new BattleDamageEvent(before.World.Clock, own.Id, enemy.Id, null, 10, 0, 10, 0, 0)
            { Source = own, Target = enemy };
        var reply = attack with { AttackerId = enemy.Id, TargetUnitId = own.Id, Source = enemy, Target = own };
        DomainEvent[] facts = [attack, new UnitWaypointReachedEvent(before.World.Clock, own, new(47, 12)), reply,
            new BattleContactStartedEvent(before.World.Clock, own, enemy, true, 5000, 5000),
            attack, new BattleContactStartedEvent(before.World.Clock, enemy, own, false, 5000, 5000), reply];
        var output = ExecutionPublication.Read(before, before, session.Messages(texts), texts, content, facts);
        Assert.Equal(2, output.Records.Count);
        Assert.All(output.Records, r => Assert.Equal(1, r.ForceColorCode));
        Assert.Equal(MapCoordinates.Cell(new(47, 12)), output.Records[0].Location);
        var arrival = Assert.Single(output.Messages, m => m.Message.Key == "waypoint-reached").MapSpeech!;
        Assert.Equal(output.Records[0].Location, arrival.Location);
        Assert.Null(arrival.UnitId);
        Assert.Equal(3, output.Messages.Count);
        var early = ExecutionPublication.Read(before, before, session.Messages(texts), texts, content, facts, endIndex: 3);
        var late = ExecutionPublication.Read(before, before, session.Messages(texts), texts, content, facts, startIndex: 3);
        Assert.Equal(output.Messages, early.Messages.Concat(late.Messages));
        Assert.Equal(output.Records, early.Records.Concat(late.Records));
        Assert.True(output.FeedbackIds.SetEquals(early.FeedbackIds.Concat(late.FeedbackIds)));
        Assert.Equal(2, Assert.Single(output.Messages, m => m.Message.Key == "contact-defense").MapSpeech!.ForceColorCode);
        var plan = BattlePresentationPlan.Create(before, before, facts, session.Messages(texts), texts, content);
        Assert.Equal(2, plan.Records.Count);
        Assert.Equal(3, plan.Messages.Count);
        var phase = Assert.Single(plan.MapBatches);
        Assert.Equal((0, 6), (phase.FirstFactIndex, phase.LastFactIndex));
        Assert.All(plan.Values, value => Assert.Equal(1, value.Time));
        var rest = ExecutionPublication.Read(before, before, session.Messages(texts), texts, content, facts, presentedFeedback: plan.PresentedFeedback);
        Assert.Empty(rest.Records); Assert.Empty(rest.Messages);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CaptureAndDestructionRecordsUseThePlayerParticipantColor(bool enemyAttacker)
    {
        var session = GameSession.Start(Scenario(), 42);
        var before = session.View;
        var source = new BattleObjectContext(enemyAttacker ? 20 : 10, false, enemyAttacker ? 2 : 1,
            enemyAttacker ? 23 : 3, new(12, 7), enemyAttacker ? 2 : 1, false);
        int place = enemyAttacker ? 1 : 2, previousOwner = enemyAttacker ? 1 : 2;
        string[] roles = ["settlement-won", "settlement-lost", "territory-changed", "territory-lost",
            "facility-won", "facility-destroyed", "enemy-captured", "person-captured"];
        var texts = MissionScenario.Texts(before) with
        { Messages = roles.ToDictionary(role => role, role => new MessageDefinition([new MessageLiteral(role)])) };
        var content = new ProgressPresentationDefinition(roles.ToDictionary(role => role, role => new DecisionMessageDefinition(role, [])), []);
        DomainEvent[] facts = [new SettlementCapturedEvent(before.World.Clock, place, previousOwner, new(source.Id, false), false) { Source = source, Result = before.World.Settlements.Single(s => s.Id == place) },
            new FacilityDestroyedEvent(before.World.Clock, place, new(source.Id, false)) { Source = source, PreviousOwnerId = previousOwner,
                Result = before.World.Settlements.Single(s => s.Id == place) with { IsDestroyed = true, OwnerId = null } },
            new OfficerCapturedEvent(before.World.Clock, enemyAttacker ? 3 : 23, source.ForceId!.Value, place, null)];
        var records = ProgressMessages.Read(before, before, session.Messages(texts), texts, content, facts);
        Assert.Equal(4, records.Count);
        Assert.All(records, record => Assert.Equal(1, record.ForceColorCode));
    }

    [Fact]
    public void StrategyContextDialogueNeverPublishesExecutionRecords()
    {
        var session = GameSession.Start(Scenario(),42);
        var clock = session.Snapshot.Clock.Advance();
        var texts = MissionScenario.Texts(session.View) with { Messages = new()
        {
            ["advice"] = new([new MessageLiteral("旬初建议")])
        } };
        var content = new ProgressPresentationDefinition([],ProgressPresentationDefinition.ContextRoles
            .ToDictionary(role => role, _ => new DecisionMessageDefinition("advice",[],Speaker:"actor")));
        var state = new StrategicContextState(clock,[],ProgressPresentationDefinition.ContextRoles
            .Select(role => new StrategicContextPrompt(role,2,1,1,PlaceId:1,CityId:1)).ToImmutableArray(),[]);

        var messages = StrategicContext.Produce(state,session.View,session.Messages(texts),texts,content);

        Assert.Equal(state.Prompts.Length,messages.Count);
        Assert.All(messages,message =>
        {
            Assert.Equal("旬初建议",message.Message.BbCode);
            Assert.Equal(clock,message.Message.Clock);
            Assert.Equal(2,message.SpeakerId);
            Assert.Null(message.MapSpeech);
        });
    }

    [Fact]
    public void OrderPromptUsesCurrentRulerWithoutAdviserAndDoesNotAlterRestoredAdvice()
    {
        var session = GameSession.Start(Scenario(false),42);
        var texts = MissionScenario.Texts(session.View) with { Messages = new()
        {
            ["order"] = new([new MessageValue(new("property","person.name",Arguments:[new("binding","person.0")])),
                new MessageLiteral("大人，请下令。")])
        } };
        var content = new ProgressPresentationDefinition([],[])
        { OrderPrompt = new("order",new() { ["person.0"]="ruler" },Sound:"prompt"), StrategyTitleSound="title" };
        var restored = GameSession.Restore(Scenario(false),session.Capture());
        var hash = restored.Snapshot;
        var message = Assert.Single(StrategyEntryPresentation.Produce(restored.View,restored.Messages(texts),texts,content));
        Assert.Equal("ruler大人，请下令。",message.Message.BbCode);
        Assert.Null(message.SpeakerId);
        Assert.Equal("prompt",message.SoundRole);
        StateAssert.Equal(hash, restored.Snapshot);
        Assert.Equal(session.StrategicContext,restored.StrategicContext);
    }

    private static San9.Content.Scenarios.ScenarioInitialState Scenario(bool adviser = true)
    {
        var scenario = MissionScenario.Create();
        return scenario with
        {
            Forces = scenario.Forces.Select(f => f.Id == 1 ? f with { AdviserId = adviser ? 2 : null } : f).ToArray(),
            Officers = scenario.Officers.Select(p => p.State.Id == 2 ? p with { State = p.State with { Status = adviser ? OfficerStatus.Strategist : OfficerStatus.Serving } } : p).ToArray()
        };
    }
    private static StrategySnapshot Entry(bool adviser = true, bool threat = true)
    {
        var definition = Scenario(adviser);
        var world = GameSession.Start(definition,42).Snapshot;
        return new(definition,world with
        {
            Clock = world.Clock.Advance(),
            Units = world.Units.Select(u => u.Id == 20 && threat
                ? u with { OrderClass = UnitOrderClass.Military, Order = new(MilitaryOrderKind.AttackSettlement,null,1) } : u).ToImmutableArray()
        });
    }

    [Fact]
    public void EnemyReportsTakePriorityAndPersistPerUnitWithoutRerolling()
    {
        var view = Entry();
        var state = StrategicContext.Prepare(view,StrategicContextState.Empty,_ => 0);
        Assert.Equal("enemy-city",Assert.Single(state.Prompts.Where(p => p.Role.StartsWith("enemy-",StringComparison.Ordinal))).Role);
        Assert.Equal(new[] { 20 },state.ReportedEnemyUnits.ToArray());
        Assert.Same(state,StrategicContext.Prepare(view,state,_ => throw new Exception("Rerolled entry")));
        var next = new StrategySnapshot(Scenario(),view.World with { Clock = view.World.Clock.Advance() });
        Assert.DoesNotContain(StrategicContext.Prepare(next,state,_ => 99).Prompts,p => p.Role.StartsWith("enemy-",StringComparison.Ordinal));
    }

    [Fact]
    public void NoAdviserMeansNoContextAndNoEnemyReportConsumed()
    {
        var state = StrategicContext.Prepare(Entry(false),StrategicContextState.Empty,_ => throw new Exception("No speaker"));
        Assert.Empty(state.Prompts);
        Assert.Empty(state.ReportedEnemyUnits);
    }

    [Fact]
    public void SameForceAtOnePlaceReportsOnceButDifferentEnemyForcesRemainIndependent()
    {
        var view = Entry();
        var enemy = view.World.Units.Single(u => u.Id == 20);
        var world = view.World with { Units = view.World.Units.Concat(new[] { enemy with { Id = 24 }, enemy with { Id = 30, OwnerId = 3, CommanderId = 31 } }).ToImmutableArray() };
        var state = StrategicContext.Prepare(new(Scenario(),world),StrategicContextState.Empty,_ => 0);
        Assert.Equal(new[] { 2,3 },state.Prompts.Where(p => p.Role == "enemy-city").Select(p => p.EnemyForceId!.Value).ToArray());
        Assert.Equal(new[] { 20,24,30 },state.ReportedEnemyUnits.ToArray());
    }

    [Theory]
    [InlineData(70, 9, true)]
    [InlineData(70, 10, false)]
    [InlineData(90, 29, true)]
    [InlineData(90, 30, false)]
    [InlineData(100, 49, true)]
    [InlineData(100, 50, false)]
    public void NonThreatAdviceRespectsTheIntelligenceProbabilityGate(int intelligence, int roll, bool expected)
    {
        var view = Entry(threat: false);
        var world = view.World with
        {
            Clock = new(190, 4, 1),
            Officers = view.World.Officers.Select(p => p.Id == 2
                ? p with { BaseAbilities = p.BaseAbilities with { Intelligence = intelligence } } : p).ToImmutableArray(),
            Corps = view.World.Corps.Select(c => c.OwnerId == 1 ? c with { Treasury = new(0, 0) } : c).ToImmutableArray()
        };
        var state = StrategicContext.Prepare(new(Scenario(), world), StrategicContextState.Empty, _ => roll);
        if (expected) Assert.Equal("food-shortage", Assert.Single(state.Prompts).Role);
        else Assert.Empty(state.Prompts);
    }

    [Theory]
    [InlineData(49, true)]
    [InlineData(50, false)]
    public void AccurateHiddenLocationStillRequiresTheHintProbabilityGate(int roll, bool expected)
    {
        var (definition, world) = HiddenOfficerEntry(100);
        int choices = 0;
        var state = StrategicContext.Prepare(new(definition, world), StrategicContextState.Empty,
            count => count == 100 && ++choices > 1 ? roll : 0);
        Assert.Equal(expected, state.Clues.Any(c => c.OfficerId == 40));
    }

    [Fact]
    public void DesperateDefensePrecedesOtherResponsesAndUsesLocalOfficer()
    {
        var view = Entry();
        var world = view.World with
        {
            Settlements = view.World.Settlements.Select(s => s.Id == 1 ? s with { Soldiers = 1000 } : s).ToImmutableArray(),
            Officers = view.World.Officers.Select(p => p.Id == 1 ? p with { Personality = 3 } : p).ToImmutableArray()
        };
        var state = StrategicContext.Prepare(new(Scenario(),world),StrategicContextState.Empty,_ => 0);
        Assert.Equal(["enemy-city","defense-desperate"],state.Prompts.Select(p => p.Role));
        Assert.Equal(1,state.Prompts[1].ActorId);
    }

    [Theory]
    [InlineData(80,"person-region",true)]
    [InlineData(95,"person-city-anonymous",false)]
    [InlineData(100,"person-city",false)]
    public void CluePrecisionFollowsIntelligenceAndDoesNotIncludeEnemyLocations(int intelligence,string role,bool regionOnly)
    {
        var (definition, world) = HiddenOfficerEntry(intelligence);
        var state = StrategicContext.Prepare(new(definition, world), StrategicContextState.Empty, _ => 0);
        Assert.Equal(role,Assert.Single(state.Prompts).Role);
        var clue = Assert.Single(state.Clues);
        Assert.Equal(1,clue.PlaceId);
        Assert.Equal(regionOnly,clue.RegionOnly);
        Assert.Equal(intelligence >= 100 ? 40 : null,clue.OfficerId);
    }

    private static (San9.Content.Scenarios.ScenarioInitialState Definition, WorldSnapshot World) HiddenOfficerEntry(int intelligence)
    {
        var view = Entry(threat: false);
        var world = view.World with
        {
            // Seed 3 permits this location at every tested intelligence level.
            Simulation = view.World.Simulation with { InitialSeed = 3 },
            Officers = view.World.Officers.Select(p => p.Id == 2
                ? p with { BaseAbilities = p.BaseAbilities with { Intelligence = intelligence } }
                : p.Id == 22 ? p with { OwnerId = null, Status = OfficerStatus.Undiscovered } : p).ToImmutableArray()
                .Add(new(40, null, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Undiscovered })
        };
        // The hidden officer in the enemy city must never be disclosed.
        var definition = Scenario() with
        {
            Officers = Scenario().Officers.Append(new("hidden", world.Officers.Single(p => p.Id == 40))).ToArray()
        };
        return (definition, world);
    }

    [Theory]
    [InlineData(1UL, false)]
    [InlineData(3UL, true)]
    public void LocationAccuracyCanRejectCluesEvenWhenBothPresentationRollsPass(ulong seed, bool expected)
    {
        var (definition, world) = HiddenOfficerEntry(70);
        world = world with { Simulation = world.Simulation with { InitialSeed = seed } };
        var state = StrategicContext.Prepare(new(definition, world), StrategicContextState.Empty, _ => 0);
        Assert.Equal(expected, !state.Clues.IsEmpty);
    }

    [Fact]
    public void SaveRetainContextPublication()
    {
        var definition = Scenario();
        var session = GameSession.Start(definition,42);
        session.AdvanceXun();
        var loaded = SaveService.Decode(SaveService.Encode(session),definition);
        Assert.Equal(session.StrategicContext.Clock,SaveService.Load(SaveService.Encode(session),definition).StrategicContext.Clock);
        StateAssert.Equal(session.Capture(), loaded.Capture());
        Assert.Equal(session.StrategicContext.Clock,loaded.StrategicContext.Clock);
        Assert.Same(loaded.StrategicContext,StrategicContext.Prepare(loaded.View,loaded.StrategicContext,_ => throw new Exception("Rerolled save")));
    }

    [Fact]
    public void SaveRejectsContextWithAnUnknownDisclosedPlace()
    {
        var definition = Scenario();
        var session = GameSession.Start(definition,42);
        var envelope = SaveService.Capture(session);
        var context = session.StrategicContext with { Prompts = [new("enemy-city",2,1,1,PlaceId:9999,CityId:1,EnemyForceId:2)] };
        var error = Assert.Throws<SaveException>(() => SaveService.Load(envelope with
            { Session = envelope.Session with { StrategicContexts = envelope.Session.StrategicContexts.SetItem(1, context) } },definition));
        Assert.Equal(SaveFailure.InvalidState,error.Failure);
    }

    [Fact]
    public void EnemyPrisonerDeathIsVisibleToThePlayerDetentionOwner()
    {
        var definition = Scenario();
        var session = GameSession.Start(definition,42);
        var before = new StrategySnapshot(definition,session.Snapshot with { Officers = session.Snapshot.Officers.Select(p => p.Id == 22
            ? p with { Status = OfficerStatus.Captive, SettlementId = 1 } : p).ToImmutableArray() });
        var texts = MissionScenario.Texts(before) with { Messages = new() { ["death"] = new([new MessageLiteral("death")]) } };
        var content = new ProgressPresentationDefinition(new() { ["captive-death"] = new("death",[]) },[]);
        var death = new OfficerDiedEvent(before.World.Clock.NextDay(),22,OfficerDeathCause.Natural,OfficerStatus.Captive,2,1,null,null);
        var record = Assert.Single(ProgressMessages.Read(before,before,session.Messages(texts,before.World),texts,content,[death]));
        Assert.Equal(new MapCell(0,1),record.Location);
        Assert.Equal(death.Clock,record.Message.Clock);
        Assert.Null(record.ForceColorCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CaptureLocationUsesOnlyMovementBeforeTheFactEvenWhenTheUnitLaterDisappears(bool movedBefore)
    {
        var definition = Scenario();
        var session = GameSession.Start(definition,42);
        var before = session.View;
        var clock = before.World.Clock.NextDay();
        var after = new StrategySnapshot(definition,before.World with { Units = before.World.Units.Select(u => u.Id == 10
            ? u with { Position = new(130,12) } : u).ToImmutableArray() });
        var texts = MissionScenario.Texts(before) with { Messages = new() { ["capture"] = new([new MessageLiteral("capture")]) } };
        var content = CommandPresentation.Empty with { Progress = new(new()
            { ["enemy-captured"] = new("capture",[]), ["unit-lost"] = new("capture",[]) },[]) };
        List<DomainEvent> events = [];
        if (movedBefore) events.Add(new UnitMovedEvent(clock,10,new(12,7),new(64,12),1,NavigationStatus.Moving));
        events.Add(new OfficerCapturedEvent(clock,22,1,null,10));
        events.Add(new UnitMovedEvent(clock,10,new(64,12),new(130,12),1,NavigationStatus.Moving));
        events.Add(new UnitTerminatedEvent(clock,10,null,5000)
            { Reason = UnitTerminationReason.Defeated, Target = new(10,false,1,2,new(130,12),1,false), Source = null });
        var record = ExecutionPublication.Read(before,after,session.Messages(texts,before.World),texts,content,events).Records[0];
        Assert.Equal(movedBefore ? MapCoordinates.Cell(new(64,12)) : before.World.Units.Single(u => u.Id == 10).Cell,record.Location);
    }

    [Fact]
    public void DefeatRecordRetainsCapturedIdentityLocationAndOccurrenceClockAfterRemoval()
    {
        var definition = Scenario();
        var session = GameSession.Start(definition,42);
        var before = session.View;
        var after = new StrategySnapshot(definition,before.World with { Units = before.World.Units.Where(u => u.Id != 20).ToImmutableArray(),
            Officers = before.World.Officers.Select(p => p.UnitId == 20 ? p with { UnitId = null, SettlementId = 2 } : p).ToImmutableArray() });
        var clock = before.World.Clock.NextDay();
        var texts = MissionScenario.Texts(before) with { Messages = new() { ["defeat"] = new([
            new MessageValue(new("property","person.name",Arguments:[new("binding","person.0")])),new MessageLiteral("/"),
            new MessageValue(new("property","military-target.commander-name",Arguments:[new("binding","unit.0")]))]) } };
        var content = new ProgressPresentationDefinition(new() { ["unit-defeated"] = new("defeat",new()
            { ["person.0"]="other",["unit.0"]="unit" }) },[]);
        DomainEvent[] events = [new UnitTerminatedEvent(clock,20,new(10,false),5000)
        { Reason = UnitTerminationReason.Defeated,
            Target = new(20,false,2,23,new(67,12),2,false),
            Source = new(10,false,1,2,new(62,12),1,false)
        }];
        var record = Assert.Single(ProgressMessages.Read(before,after,session.Messages(texts,before.World),texts,content,events));
        Assert.Equal(clock,record.Message.Clock);
        Assert.Equal("enemy-commander/actor",record.Message.BbCode);
        Assert.Equal(1,record.ForceColorCode);
        Assert.Equal(MapCoordinates.Cell(new(67,12)),record.Location);
        Assert.Equal(record,Assert.Single(ProgressMessages.Read(before,after,session.Messages(texts,before.World),texts,content,events)));
        var foreign = ((UnitTerminatedEvent)events[0]) with { Source = new(30,false,3,31,new(62,12),3,false) };
        texts.Messages["foreign-loss"] = new([new MessageLiteral("foreign-loss")]);
        content.Records.Add("unit-lost-other", new("foreign-loss", []));
        var detailed = Assert.Single(ProgressMessages.Read(before,after,session.Messages(texts,before.World),texts,content,[foreign]));
        Assert.Equal(MapReportImportance.Detailed, detailed.Importance);
        Assert.Null(detailed.ForceColorCode);
        Assert.Equal(record.Location, detailed.Location);
    }

    [Fact]
    public void TerminationReasonDeterminesReportsWithoutDependingOnEventBatch()
    {
        var session = GameSession.Start(Scenario(),42);
        var before = session.View;
        var clock = before.World.Clock.NextDay();
        var texts = MissionScenario.Texts(before) with { Messages = new()
            { ["loss"] = new([new MessageLiteral("loss")]) } };
        var content = CommandPresentation.Empty with
            { Progress = new(new() { ["unit-lost"] = new("loss",[]) },[])
                { MapSpeeches = new() { ["unit-defeat"] = new("loss", [], "actor") } } };
        var loss = new UnitTerminatedEvent(clock,10,null,5000)
            { Reason = UnitTerminationReason.Defeated, Target = new(10,false,1,2,new(62,12),1,false), Source = null };
        var surrender = new ForceSurrenderedEvent(clock,1,2,0,0,0,0,[],[]);
        IReadOnlyList<MapMessageRecord> Read(params DomainEvent[] events) =>
            ExecutionPublication.Read(before,before,session.Messages(texts),texts,content,events).Records;

        var dissolved = loss with { Reason = UnitTerminationReason.Surrendered };
        Assert.Empty(Read(dissolved));
        Assert.Empty(Read(dissolved,surrender));
        Assert.Single(Read(loss,surrender));
        Assert.Single(Read(loss,surrender with { ForceId = 3 }));
        Assert.Single(Read(loss,surrender with { Clock = clock.NextDay() }));
        Assert.Single(Read(loss with { Winner = new(20,false),
            Source = new(20,false,2,23,new(67,12),2,false) },surrender));
    }
}

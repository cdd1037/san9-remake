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
    [Theory]
    [InlineData(true, false, "unit-defeated", "battle-wounded-captured", 1)]
    [InlineData(true, true, "unit-defeated", "battle-wounded-captured", 1)]
    [InlineData(false, true, "unit-lost", "battle-wounded-lost", 2)]
    [InlineData(false, false, "unit-lost-other", null, 0)]
    public void VictoryAndWoundedReportsFollowTheReportingSideWithTwoPlayers(bool winnerPlayer, bool loserPlayer,
        string defeatRole, string? woundedRole, int color)
    {
        var session = GameSession.Start(Scenario(), 42);
        var world = session.Snapshot;
        var before = session.View.ForWorld(world with { Simulation = world.Simulation with
        { Controls = world.Simulation.Controls.Select(c => c with { Kind =
            c.ForceId == 1 && winnerPlayer || c.ForceId == 2 && loserPlayer ? ControllerKind.Player : ControllerKind.Automatic }).ToImmutableArray() } });
        var victor = new BattleObjectContext(10, false, 1, 2, new(62,12), 1, false);
        var defeated = new BattleObjectContext(20, false, 2, 23, new(67,12), 2, false);
        var roles = ProgressPresentationDefinition.RecordRoles;
        var texts = MissionScenario.Texts(before) with { Messages = roles.ToDictionary(r=>r, r=>new MessageDefinition([new MessageLiteral(r)])) };
        var content = new ProgressPresentationDefinition(roles.ToDictionary(r=>r, r=>new DecisionMessageDefinition(r, [])), []);
        DomainEvent[] facts = [new UnitDefeatedEvent(world.Clock, defeated, victor, UnitTerminationReason.Defeated),
            new BattleWoundedCapturedEvent(world.Clock, defeated, victor, 50),
            new BattleVictoryMoraleChangedEvent(world.Clock, victor, 100, 100)];
        var records = ProgressMessages.Read(before, before, session.Messages(texts), texts, content, facts);
        var expected = new List<string> { defeatRole };
        if (woundedRole is not null) expected.Add(woundedRole);
        if (winnerPlayer) expected.Add("battle-victory-morale");
        Assert.Equal(expected, records.Select(r=>r.Message.Key));
        Assert.Equal(color == 0 ? null : (int?)color, records[0].ForceColorCode);
        Assert.All(records.Skip(1), r=>Assert.Equal(MapReportImportance.Detailed, r.Importance));
        Assert.DoesNotContain(ProgressMessages.Read(before, before, session.Messages(texts), texts, content,
            [new BattleWoundedCapturedEvent(world.Clock, defeated, victor, 0)]), r=>r.Message.Key.StartsWith("battle-wounded", StringComparison.Ordinal));
    }

    [Fact]
    public void DelegatedVictoryKeepsItsMoraleStateWithoutPublishingDirectPlayerRewardReports()
    {
        var session = GameSession.Start(Scenario(), 42);
        var before = session.View.ForWorld(session.Snapshot with
        { Corps = session.Snapshot.Corps.Append(session.Snapshot.Corps.Single(c => c.Id == 1) with { Id = 100, IsRoot = false }).ToImmutableArray() });
        var victor = new BattleObjectContext(10, false, 1, 2, new(62,12), 1, false) { CorpsId = 100 };
        var defeated = new BattleObjectContext(20, false, 2, 23, new(67,12), 2, false);
        var roles = ProgressPresentationDefinition.RecordRoles;
        var texts = MissionScenario.Texts(before) with { Messages = roles.ToDictionary(r=>r, r=>new MessageDefinition([new MessageLiteral(r)])) };
        var content = CommandPresentation.Empty with { Progress = new(roles.ToDictionary(r=>r, r=>new DecisionMessageDefinition(r, [])), [])
        { MapSpeeches = new() { ["unit-defeat"] = new("unit-lost", [], "actor") } } };
        DomainEvent[] facts = [new UnitDefeatedEvent(before.World.Clock, defeated, victor, UnitTerminationReason.Defeated),
            new BattleWoundedCapturedEvent(before.World.Clock, defeated, victor, 100),
            new BattleVictoryMoraleChangedEvent(before.World.Clock, victor, 60, 70)];
        var records = ExecutionPublication.Read(before,before,session.Messages(texts),texts,content,facts).Records;
        Assert.Equal("unit-lost-other", Assert.Single(records).Message.Key);
        Assert.Null(records[0].ForceColorCode);
        var plan = BattlePresentationPlan.Create(before,before,facts,session.Messages(texts),texts,content);
        Assert.Equal(10, Assert.Single(plan.Values).Morale);
    }

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
        DomainEvent[] facts = [new SettlementCapturedEvent(before.World.Clock, place, previousOwner, new(source.Id, false), false) { Source = source, Attacker = source, Result = before.World.Settlements.Single(s => s.Id == place) },
            new FacilityDestroyedEvent(before.World.Clock, place, new(source.Id, false)) { Source = source, PreviousOwnerId = previousOwner,
                Result = before.World.Settlements.Single(s => s.Id == place) with { IsDestroyed = true, OwnerId = null } },
            new OfficerCapturedEvent(before.World.Clock, enemyAttacker ? 3 : 23, source.ForceId!.Value, place, null)];
        var records = ProgressMessages.Read(before, before, session.Messages(texts), texts, content, facts);
        Assert.Equal(4, records.Count);
        Assert.All(records, record => Assert.Equal(1, record.ForceColorCode));
    }

    [Fact]
    public void CaptureReportsSeparateTheBreakingCommanderFromTheReceivingForce()
    {
        var definition = Scenario();
        var before = GameSession.Start(definition, 42).View;
        var texts = MissionScenario.Texts(before);
        foreach (string role in new[] { "settlement-won", "settlement-lost" })
            texts.Messages[role] = new([new MessageValue(new("property", "person.name", Arguments: [new("binding", "person.0")]))]);
        foreach (string role in new[] { "territory-changed", "territory-lost" })
            texts.Messages[role] = new([new MessageValue(new("property", "force.name", Arguments: [new("binding", "force.0")]))]);
        var content = new ProgressPresentationDefinition(new()
        {
            ["settlement-won"] = new("settlement-won", new() { ["person.0"] = "actor" }),
            ["settlement-lost"] = new("settlement-lost", new() { ["person.0"] = "actor" }),
            ["territory-changed"] = new("territory-changed", new() { ["force.0"] = "force" }),
            ["territory-lost"] = new("territory-lost", new() { ["force.0"] = "force" })
        }, []);
        var attacker = new BattleObjectContext(10, false, 1, 3, new(12, 7), 1, false);
        var recipient = new BattleObjectContext(30, false, 3, 31, new(12, 7), 3, false);
        var fact = new SettlementCapturedEvent(before.World.Clock, 2, 2, new(30, false), false)
            { Source = recipient, Attacker = attacker, Result = before.World.Settlements.Single(s => s.Id == 2) with { OwnerId = 3 } };
        foreach (int player in new[] { 1, 2, 3 })
        {
            var view = before.ForWorld(before.World with { Simulation = before.World.Simulation with
                { Controls = [new("player", player, ControllerKind.Player)] } });
            var records = ProgressMessages.Read(view, view, new(definition, view.World, texts), texts, content, [fact]);
            Assert.Equal(2, records.Count);
            Assert.Equal("commander", records[0].Message.BbCode);
            Assert.Equal(texts.Resolve(view.Forces.Single(f => f.Id == 3).NameKey), records[1].Message.BbCode);
            Assert.Equal(player == 2 ? "territory-lost" : "territory-changed", records[1].Message.Key);
        }
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
        session.ConfigureCompanion(new() { InfiniteLifespan = true });
        Assert.Equal(messages.Count - 3, StrategicContext.Produce(state,session.View,session.Messages(texts),texts,content).Count);
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

    [Theory]
    [InlineData(3000, 30000, "reinforce-from")]
    [InlineData(30000, 5000, "reinforce-to")]
    public void QuarterMilitaryAdviceComparesEnemyGarrisonWithItsFriendlyNeighbor(int ownSoldiers, int enemySoldiers, string role)
    {
        var (definition, world) = QuarterMilitaryEntry(ownSoldiers, enemySoldiers);
        var view = new StrategySnapshot(definition, world);
        var state = StrategicContext.Prepare(view, StrategicContextState.Empty, _ => 0);
        var prompt = Assert.Single(state.Prompts);

        Assert.Equal(role, prompt.Role);
        Assert.Equal(1, prompt.ForceId);
        Assert.Equal(2, prompt.EnemyForceId);
        Assert.Equal(2, prompt.PlaceId);
        Assert.Equal(1, prompt.OtherPlaceId);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(null)]
    [InlineData(2)]
    public void QuarterMilitaryAdviceDoesNotDescribeOwnNeutralOrFriendlyGarrisonsAsEnemies(int? owner)
    {
        var (definition, world) = QuarterMilitaryEntry(3000, 30000);
        world = world with
        {
            Settlements = world.Settlements.Select(s => s.Id == 2 ? s with { OwnerId = owner } : s).ToImmutableArray(),
            Relations = [new(1, 2, 100)]
        };
        var state = StrategicContext.Prepare(new(definition, world), StrategicContextState.Empty, _ => 0);
        Assert.DoesNotContain(state.Prompts, p => p.Role is "reinforce-from" or "reinforce-to");
    }

    private static (San9.Content.Scenarios.ScenarioInitialState Definition, WorldSnapshot World) QuarterMilitaryEntry(int ownSoldiers, int enemySoldiers)
    {
        var definition = Scenario();
        definition = definition with { Military = definition.Military! with
        {
            RegionIds = Enumerable.Range(0, 90).Select(i => (int?)(i % 30 < 10 ? 1 : 2)).ToImmutableArray(),
            TerritoryExits = Enumerable.Repeat(TerritoryExits.All, 90).ToImmutableArray()
        } };
        var world = GameSession.Start(definition, 42).Snapshot;
        return (definition, world with
        {
            Clock = new(190, 4, 1),
            Settlements = world.Settlements.Select(s => s.Id == 1 ? s with { Soldiers = (uint)ownSoldiers } :
                s.Id == 2 ? s with { Soldiers = (uint)enemySoldiers } : s).ToImmutableArray()
        });
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
    [InlineData(70, 0, 9, true)]
    [InlineData(70, 0, 10, false)]
    [InlineData(90, 0, 29, true)]
    [InlineData(90, 0, 30, false)]
    [InlineData(100, 0, 49, true)]
    [InlineData(100, 0, 50, false)]
    [InlineData(100, 5, 9, true)]
    [InlineData(100, 5, 30, false)]
    public void NonThreatAdviceRespectsTheCurrentIntelligenceProbabilityGate(int intelligence, int injury, int roll, bool expected)
    {
        var view = Entry(threat: false);
        var world = view.World with
        {
            Clock = new(190, 4, 1),
            Officers = view.World.Officers.Select(p => p.Id == 2
                ? p with { BaseAbilities = p.BaseAbilities with { Intelligence = intelligence }, Injury = injury } : p).ToImmutableArray(),
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
    [InlineData(80, 0, 0, "person-region", true)]
    [InlineData(95, 0, 0, "person-city-anonymous", false)]
    [InlineData(100, 0, 0, "person-city", false)]
    [InlineData(100, 5, 0, "person-region", true)]
    [InlineData(70, 0, 30, "person-city", false)]
    public void CluePrecisionFollowsCurrentIntelligenceAndDoesNotIncludeEnemyLocations(int intelligence, int injury, int bonus, string role, bool regionOnly)
    {
        var (definition, world) = HiddenOfficerEntry(intelligence, injury, bonus);
        var state = StrategicContext.Prepare(new(definition, world), StrategicContextState.Empty, _ => 0);
        Assert.Equal(role,Assert.Single(state.Prompts).Role);
        var clue = Assert.Single(state.Clues);
        Assert.Equal(1,clue.PlaceId);
        Assert.Equal(regionOnly,clue.RegionOnly);
        Assert.Equal(role == "person-city" ? 40 : null,clue.OfficerId);
    }

    private static (San9.Content.Scenarios.ScenarioInitialState Definition, WorldSnapshot World) HiddenOfficerEntry(int intelligence, int injury = 0, int bonus = 0)
    {
        var view = Entry(threat: false);
        var world = view.World with
        {
            // Seed 3 permits this location at every tested intelligence level.
            Simulation = view.World.Simulation with { InitialSeed = 3 },
            Officers = view.World.Officers.Select(p => p.Id == 2
                ? p with { BaseAbilities = p.BaseAbilities with { Intelligence = intelligence }, Injury = injury, OfficeId = bonus > 0 ? 7 : null }
                : p.Id == 22 ? p with { OwnerId = null, Status = OfficerStatus.Undiscovered } : p).ToImmutableArray()
                .Add(new(40, null, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Undiscovered })
        };
        // The hidden officer in the enemy city must never be disclosed.
        var definition = Scenario() with
        {
            Officers = Scenario().Officers.Append(new("hidden", world.Officers.Single(p => p.Id == 40))).ToArray(),
            Information = Scenario().Information with { Offices = [new(7, "office", 10000, 0) { AbilityBonuses = new(0, bonus, 0, 0) }] }
        };
        return (definition, world);
    }

    [Theory]
    [InlineData(1, false, null, true)]
    [InlineData(null, false, null, true)]
    [InlineData(2, false, null, false)]
    [InlineData(1, true, null, true)]
    [InlineData(null, true, null, true)]
    [InlineData(2, true, null, false)]
    [InlineData(2, true, 1, true)]
    [InlineData(1, true, 2, false)]
    public void HiddenCluesRespectCurrentCityAndIndependentBuildingOwnership(int? cityOwner, bool built, int? placeOwner, bool expected)
    {
        var (definition, initial) = HiddenOfficerEntry(100);
        const int place = 4;
        var position = new WorldPosition(57, 7);
        definition = definition with
        {
            UnbuiltPlaces = built ? [] : [new(place, position)],
            Settlements = built ? definition.Settlements.Append(definition.Settlements[0] with
                { Id = place, Kind = SettlementKind.Facility, NameKey = "search-place" }).ToArray() : definition.Settlements,
            RegionNameKeys = new(definition.RegionNameKeys) { [place] = "search-place" },
            RegionCityIds = new(definition.RegionCityIds) { [place] = 2 }
        };
        foreach (bool treasure in new[] { false, true })
        {
            var world = initial with
            {
                Officers = initial.Officers.Where(p => p.Status != OfficerStatus.Undiscovered || p.Id == 40 && !treasure)
                    .Select(p => p.Id == 40 ? p with { SettlementId = built ? place : null, RegionId = built ? null : place } : p).ToImmutableArray(),
                Settlements = initial.Settlements.Select(s => s.Id == 2 ? s with { OwnerId = cityOwner } : s).ToImmutableArray(),
                Treasures = treasure ? [new(50, null, TreasureDiscovery.Buried, place)] : []
            };
            if (built) world = world with
            {
                Settlements = world.Settlements.Append(initial.Settlements[0] with
                    { Id = place, OwnerId = placeOwner, Kind = SettlementKind.Facility }).ToImmutableArray()
            };
            var state = StrategicContext.Prepare(new(definition, world), StrategicContextState.Empty, _ => 0);
            if (expected)
            {
                Assert.Equal(treasure ? "treasure-place" : "person-place", Assert.Single(state.Prompts).Role);
                Assert.Equal(place, Assert.Single(state.Clues).PlaceId);
            }
            else
            {
                Assert.Empty(state.Prompts);
                Assert.Empty(state.Clues);
            }
        }
    }

    [Theory]
    [InlineData(1UL, 70, 0, false)]
    [InlineData(3UL, 70, 0, true)]
    [InlineData(1UL, 100, 5, false)]
    public void LocationAccuracyUsesCurrentIntelligenceEvenWhenBothPresentationRollsPass(ulong seed, int intelligence, int injury, bool expected)
    {
        var (definition, world) = HiddenOfficerEntry(intelligence, injury);
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

    [Theory]
    [InlineData("enemy-city", 9999, 2)]
    [InlineData("enemy-city", 2, null)]
    [InlineData("enemy-place", 2, 1)]
    [InlineData("reinforce-from", 2, null)]
    [InlineData("reinforce-to", 2, 1)]
    public void SaveRejectsInvalidMilitaryAdvice(string role, int place, int? enemy)
    {
        var definition = Scenario();
        var session = GameSession.Start(definition, 42);
        var envelope = SaveService.Capture(session);
        var context = session.StrategicContext with
        {
            Prompts = [new(role, 2, 1, 1, PlaceId: place, CityId: 2, EnemyForceId: enemy, OtherPlaceId: 1)]
        };
        var error = Assert.Throws<SaveException>(() => SaveService.Load(envelope with
        {
            Session = envelope.Session with { StrategicContexts = envelope.Session.StrategicContexts.SetItem(1, context) }
        }, definition));
        Assert.Equal(SaveFailure.InvalidState, error.Failure);
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
        DomainEvent[] events = [new UnitDefeatedEvent(clock, new(20,false,2,23,new(67,12),2,false),
            new(10,false,1,2,new(62,12),1,false), UnitTerminationReason.Defeated)];
        var record = Assert.Single(ProgressMessages.Read(before,after,session.Messages(texts,before.World),texts,content,events));
        Assert.Equal(clock,record.Message.Clock);
        Assert.Equal("enemy-commander/actor",record.Message.BbCode);
        Assert.Equal(1,record.ForceColorCode);
        Assert.Equal(MapCoordinates.Cell(new(67,12)),record.Location);
        Assert.Equal(record,Assert.Single(ProgressMessages.Read(before,after,session.Messages(texts,before.World),texts,content,events)));
        var foreign = ((UnitDefeatedEvent)events[0]) with { Source = new(30,false,3,31,new(62,12),3,false) };
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
        var loss = new UnitDefeatedEvent(clock, new(10,false,1,2,new(62,12),1,false), null, UnitTerminationReason.Defeated);
        var surrender = new ForceSurrenderedEvent(clock,1,2,0,0,0,0,[],[]);
        IReadOnlyList<MapMessageRecord> Read(params DomainEvent[] events) =>
            ExecutionPublication.Read(before,before,session.Messages(texts),texts,content,events).Records;

        var dissolved = loss with { Reason = UnitTerminationReason.Surrendered };
        Assert.Empty(Read(dissolved));
        Assert.Empty(Read(dissolved,surrender));
        Assert.Single(Read(loss,surrender));
        Assert.Single(Read(loss,surrender with { ForceId = 3 }));
        Assert.Single(Read(loss,surrender with { Clock = clock.NextDay() }));
        Assert.Single(Read(loss with { Source = new(20,false,2,23,new(67,12),2,false) },surrender));
    }
}

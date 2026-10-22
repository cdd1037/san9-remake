using San9.Tests;
using System.Collections.Immutable;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class BattlePresentationTests
{
    [Fact]
    public void EliminationDemobilizationRemovesTheArmyWithoutACombatDefeatReport()
    {
        var (session, texts, content, occurrence) = Fixture();
        DomainEvent[] facts = [new UnitTerminatedEvent(session.Snapshot.Clock, occurrence.Source.Id, null, 0)
            { Target = occurrence.Source, Source = null, Reason = UnitTerminationReason.ForceEliminated }];
        var plan = BattlePresentationPlan.Create(session.View, session.View, facts, session.Messages(texts), texts, content);
        Assert.Equal(occurrence.Source.Id, Assert.Single(plan.MapChanges).RemovedUnitId);
        Assert.Empty(plan.Records);
        Assert.Empty(plan.Messages);
    }

    [Fact]
    public void DefeatRemovesAtImpactAndAdditionalCaptivesDoNotExtendTheDay()
    {
        var (session, texts, content, occurrence) = Fixture();
        string[] roles = ["enemy-captured", "person-captured", "unit-defeated"];
        foreach (string role in roles) texts.Messages[role] = new([new MessageLiteral(role)]);
        content = content with { Progress = new(roles.ToDictionary(r => r, r => new DecisionMessageDefinition(r, [])), []) };
        BattlePresentationPlan Plan(int count)
        {
            List<DomainEvent> facts = [new BattleDamageEvent(session.Snapshot.Clock, 10, 20, null, 500, 0, 500, 0, 0)
                { Source = occurrence.Source, Target = occurrence.Target }];
            facts.AddRange(new[] { 21, 22, 23 }.Take(count).Select(id =>
                new OfficerCapturedEvent(session.Snapshot.Clock, id, 1, 1, null)));
            facts.Add(new UnitTerminatedEvent(session.Snapshot.Clock, 20, new(10, false), 0)
                { Reason = UnitTerminationReason.Defeated, Source = occurrence.Source, Target = occurrence.Target });
            return BattlePresentationPlan.Create(session.View, session.View, facts, session.Messages(texts), texts, content);
        }
        var one = Plan(1); var three = Plan(3);
        Assert.Equal(one.Duration, three.Duration);
        Assert.Equal(Assert.Single(three.Values).Time, Assert.Single(three.MapChanges).Time);
        Assert.All(three.Records, c => Assert.Equal(Assert.Single(three.MapChanges).Time, c.Time));
        Assert.DoesNotContain(new BattleMapFrame(session.View, [], []).RemovedUnits, id => id == 20);
        Assert.Contains(20, new BattleMapFrame(session.View, three.MapChanges, three.Values).RemovedUnits);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    public void RecordColorsFollowReportingForcesIndependentlyOfSpeechAndDamage(bool enemySource, bool chain, bool support)
    {
        var (session, texts, content, occurrence) = Fixture();
        if (enemySource) occurrence = occurrence with { Source = occurrence.Target, Target = occurrence.Source };
        content.Battle!.Tactics[99] = content.Battle.Tactics[99] with
        { Support = support, Zoom = support ? null : content.Battle.Tactics[99].Zoom };
        var source = occurrence.Source; var target = occurrence.Target;
        ImmutableArray<int> participants = enemySource ? [23, 22] : [3, 2];
        DomainEvent[] facts = [new TacticActivatedEvent(session.Snapshot.Clock, source.Id, false, target.Id, false, 99, participants.Select(id => new TacticParticipant(id, 99)).ToImmutableArray(), false, 10)
            { Occurrence = occurrence, IsChain = chain },
            new BattleDamageEvent(session.Snapshot.Clock, source.Id, target.Id, null, 500, 0, 500, 0, 0)
            { TacticId = 99, Source = source, Target = target }];
        var plan = BattlePresentationPlan.Create(session.View, session.View, facts, session.Messages(texts), texts, content, tacticScenesEnabled: false);
        string role = support ? "support" : (chain ? "chain-" : "activation-") + (enemySource ? "enemy" : "own");
        var activation = Assert.Single(plan.Records, c => c.Record.Message.Key == role).Record;
        Assert.Equal(1, activation.ForceColorCode);
        var reportingSide = enemySource ? target : source;
        Assert.Equal(MapCoordinates.Cell((support ? source : reportingSide).Position!.Value), activation.Location);
        Assert.All(plan.Records.Where(c => c.Record.Message.Key == "link"), c => Assert.Equal(enemySource ? (int?)null : 1, c.Record.ForceColorCode));
        if (!support)
        {
            var damage = Assert.Single(plan.Records, c => c.Record.Message.Key == "damage").Record;
            Assert.Equal(enemySource ? 1 : (int?)null, damage.ForceColorCode);
            Assert.Equal(MapCoordinates.Cell(target.Position!.Value), damage.Location);
        }
        var speech = Assert.Single(plan.Messages, c => c.Message.Message.Key == (chain ? "chain-confused" : "action"));
        if (support) Assert.Equal(enemySource ? 2 : 1, speech.Message.MapSpeech!.ForceColorCode);
        else Assert.Equal(BattleMessageChannel.Dialogue, speech.Channel);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void DisabledTacticScenesKeepDialoguesWithoutCreatingZoom(bool blocked, bool chain)
    {
        var (session, texts, content, occurrence) = Fixture();
        var before = session.View;
        var fact = new TacticActivatedEvent(before.World.Clock, 10, false, 20, false, 99, [new(3, 99), new(2, 99)], false, blocked ? 0 : 10)
        { Occurrence = occurrence, IsChain = chain };
        var damage = new BattleDamageEvent(before.World.Clock, 10, 20, null, 500, 0, 500, 0, 0)
        { TacticId = 99, Source = occurrence.Source, Target = occurrence.Target };
        DomainEvent[] facts = [fact, damage];
        var baseline = BattlePresentationPlan.Create(session.View, session.View, facts,
            session.Messages(texts), texts, content);
        Assert.Contains(baseline.Visuals, visual => visual.Zoom is not null);
        var plan = BattlePresentationPlan.Create(before, before, facts, session.Messages(texts, before.World), texts, content,
            tacticScenesEnabled: false);
        Assert.DoesNotContain(plan.Visuals, visual => visual.Zoom is not null);
        Assert.Equal(baseline.Records.Select(c => c.Record.Message.Key), plan.Records.Select(c => c.Record.Message.Key));
        Assert.Equal(baseline.Messages.Select(c => (c.Id, c.Message, c.Channel)),
            plan.Messages.Select(c => (c.Id, c.Message, c.Channel)));
        Assert.All(plan.Messages, message => Assert.Equal(BattleMessageChannel.Dialogue, message.Channel));
        Assert.Equal(baseline.Values.Select(value => (value.TargetId, value.Soldiers, value.Morale, value.Durability)),
            plan.Values.Select(value => (value.TargetId, value.Soldiers, value.Morale, value.Durability)));
        Assert.DoesNotContain(plan.Messages, message => message.Message.Message.Key == "announcement");
        Assert.Contains(plan.Messages, message => message.Message.Message.Key == (chain ? "chain-confused" : "action"));
        Assert.Contains(plan.Messages, message => message.Message.Message.Key == "link-0");
        Assert.Equal(1, Assert.Single(plan.Records, c => c.Record.Message.Key == (chain ? "chain-own" : "activation-own")).Record.ForceColorCode);
        Assert.Null(Assert.Single(plan.Records, c => c.Record.Message.Key == (blocked ? "blocked" : "damage")).Record.ForceColorCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefectionRecordUsesThePlayerReportingSide(bool enemySource)
    {
        var (session, texts, content, occurrence) = Fixture(false);
        if (enemySource) occurrence = occurrence with { Source = occurrence.Target, Target = occurrence.Source };
        texts.Messages.Add("defection", new([new MessageLiteral("defection")]));
        content.Battle!.Messages.Add("defection", new("defection", []));
        var fact = new TacticDefectionEvent(session.Snapshot.Clock, 3, 99, occurrence, 200, 200);
        var plan = BattlePresentationPlan.Create(session.View, session.View, [fact], session.Messages(texts), texts, content);
        var record = Assert.Single(plan.Records).Record;
        Assert.Equal(1, record.ForceColorCode);
        Assert.Equal(MapCoordinates.Cell(occurrence.Target.Position!.Value), record.Location);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NextSceneWaitsForLongMapEffectsAndTheirResultFeedback(bool support)
    {
        var (session, texts, content, occurrence) = Fixture(false);
        var martial = Fixture().Content.Battle!.Tactics[99];
        var battle = content.Battle!;
        battle.Tactics[100] = martial;
        battle.Tactics[99] = battle.Tactics[99] with
        { Support = support, EffectAtSource = support, MapEffect = new("long-effect", 8, 1500) };
        content = content with { Battle = battle with
        { MapNumbers = new("digits", 12, 20, 1000, 8, 4), ConditionEffects = new() { [UnitCondition.Confused] = new("state", 8, 1500) } } };
        var clock = session.Snapshot.Clock;
        List<DomainEvent> facts = [new TacticActivatedEvent(clock, 10, false, 20, false, 99, [new(3, 99)], false, 10) { Occurrence = occurrence }];
        if (!support)
        {
            facts.Add(new IntelligenceEffectResolvedEvent(clock, new(10, false), 20, false, 99, 10) { ActorId = 3, Occurrence = occurrence });
            facts.Add(new BattleDamageEvent(clock, 10, 20, null, 500, 0, 500, 0, 0)
            { TacticId = 99, Source = occurrence.Source, Target = occurrence.Target });
            facts.Add(new BattleConditionChangedEvent(clock, 20, false, UnitCondition.Confused, 3));
        }
        facts.Add(new TacticActivatedEvent(clock, 10, false, 20, false, 100, [new(3, 100)], false, 10) { Occurrence = occurrence });
        var plan = BattlePresentationPlan.Create(session.View, session.View, facts, session.Messages(texts), texts, content);
        var next = Assert.Single(plan.Visuals, c => c.Zoom is not null);
        Assert.All(plan.Visuals.Where(c => c.Zoom is null), c => Assert.True(c.End <= next.EntranceStart));
        Assert.All(plan.MapEffects, c => Assert.True(c.Start + c.Sprite.Columns * c.Sprite.FrameMilliseconds / 1000.0 <= next.EntranceStart));
        Assert.All(plan.MapNumbers, c => Assert.True(c.Start + San9.Rendering.BattleMapNumberPlayback.Duration(plan.NumberDefinition!) <= next.EntranceStart));
    }

    [Fact]
    public void OrdinaryAttackKeepsLossValuesWithoutMapNumbersWhileVisibleTacticShowsThem()
    {
        var (session, texts, content, occurrence) = Fixture();
        content = content with { Battle = content.Battle! with { MapNumbers = new("digits", 12, 20, 125, 8, 4) } };
        var damage = new BattleDamageEvent(session.Snapshot.Clock, 10, 20, null, 500, 0, 500, 0, 0)
        { Source = occurrence.Source, Target = occurrence.Target };
        var tactic = damage with { TacticId = 99 };
        var hidden = tactic with { Source = occurrence.Source with { ForceId = 3 }, SoldierLoss = 700 };
        var plan = BattlePresentationPlan.Create(session.View, session.View, [damage, tactic, hidden], session.Messages(texts), texts, content);
        Assert.Equal(1, plan.Values[0].Time);
        Assert.Equal(0, Assert.Single(plan.Attacks).FirstFactIndex);
        var number = Assert.Single(plan.MapNumbers);
        Assert.Equal(-500, number.Amount);
        Assert.Equal(occurrence.Target, number.Target);
        Assert.Equal([-500L, -500L, -700L], plan.Values.Select(value => value.Soldiers));
        var offscreen = BattlePresentationPlan.Create(session.View, session.View, [damage], session.Messages(texts), texts, content);
        Assert.Equal(1, offscreen.Duration);
    }

    [Fact]
    public void ExplicitFormationOverrideWinsEvenWhenItSharesTheDefaultBody()
    {
        var (session, texts, content, occurrence) = Fixture();
        var tactic = content.Battle!.Tactics[99];
        var zoom = tactic.Zoom!;
        content.Battle.Tactics[99] = tactic with { Zoom = zoom with
        {
            SourceFormations = new() { [7] = zoom.Source },
            ImperialSource = zoom.Source with { Idle = new("imperial", 8, 125) }
        } };
        var scenario = MissionScenario.Create();
        scenario = scenario with { Information = scenario.Information with { Ranks = [new(100, "home", 100, 0, 0, 10)] } };
        var before = new StrategySnapshot(scenario, session.View.World with
        { Forces = session.View.World.Forces.Select(f => f.Id == 1 ? f with { RankId = 100 } : f).ToImmutableArray() });
        var fact = new TacticActivatedEvent(before.World.Clock, 10, false, 20, false, 99, [new(3, 99)], false, 10) { Occurrence = occurrence };
        var plan = BattlePresentationPlan.Create(before, before, [fact], session.Messages(texts, before.World), texts, content);
        Assert.Equal(zoom.Source, Assert.Single(plan.Visuals).Zoom!.Source);
    }

    [Theory]
    [InlineData(UnitCondition.Confused)]
    [InlineData(UnitCondition.NoFormation)]
    [InlineData(UnitCondition.Disguised)]
    [InlineData(UnitCondition.Routed)]
    [InlineData(UnitCondition.HighSpirits)]
    public void NewStateProducesOneMapEffectAfterTheZoomButExtensionDoesNot(UnitCondition condition)
    {
        var (session, texts, content, occurrence) = Fixture();
        content = content with
        {
            Battle = content.Battle! with
            {
                ConditionEffects = new() { [condition] = new("status", 8, 100, Sound: "state-sound") },
                MapNumbers = new("digits", 12, 20, 125, 8, 4)
            }
        };
        DomainEvent[] facts = [new TacticActivatedEvent(session.Snapshot.Clock,10,false,20,false,99, [new(3, 99)],false,10) { Occurrence = occurrence },
            new BattleDamageEvent(session.Snapshot.Clock,10,20,null,500,0,500,0,0) { TacticId=99,Target=occurrence.Target },
            new BattleConditionChangedEvent(session.Snapshot.Clock,20,false,condition,3),
            new BattleConditionChangedEvent(session.Snapshot.Clock,20,false,condition,5)];
        var after = new StrategySnapshot(MissionScenario.Create(), session.View.World with
        { Units = [], Officers = session.View.World.Officers.Select(p => p with { UnitId = null }).ToImmutableArray() });
        var plan = BattlePresentationPlan.Create(session.View, after, facts, session.Messages(texts), texts, content);
        var zoom = Assert.Single(plan.Visuals);
        var effect = Assert.Single(plan.MapEffects);
        var number = Assert.Single(plan.MapNumbers);
        Assert.Equal(zoom.End, effect.Start);
        Assert.Equal(zoom.End, number.Start);
        Assert.Equal(-500, number.Amount);
        Assert.Equal(occurrence.Target, number.Target);
        Assert.Equal("state-sound", effect.Sprite.Sound);
        Assert.All(plan.Values.Where(v => v.Condition is not null), v => Assert.Equal(zoom.End, v.Time));
        Assert.True(plan.Duration >= number.Start + 1);
    }

    [Theory]
    [InlineData(UnitCondition.Disguised, 1, true)]
    [InlineData(UnitCondition.NoFormation, 1, true)]
    [InlineData(UnitCondition.HighSpirits, 1, true)]
    [InlineData(UnitCondition.Disguised, 3, false)]
    public void StandaloneStrategyStateUsesItsOwnVisibilityPositionAndTimeline(UnitCondition condition, int sourceForce, bool visible)
    {
        var (session, texts, content, occurrence) = Fixture();
        content = content with { Battle = content.Battle! with
        { ConditionEffects = new() { [condition] = new("status", 8, 100, Sound: "state-sound") } } };
        var target = occurrence.Target with { Position = new(67, 12) };
        DomainEvent[] facts = [new TacticActivatedEvent(session.Snapshot.Clock,10,false,20,false,99,[new(3,99)],false,10)
            { Occurrence = occurrence },
            new BattleConditionChangedEvent(session.Snapshot.Clock,20,false,condition,5)
            { SourceForceId = sourceForce, Target = target }];
        var after = new StrategySnapshot(MissionScenario.Create(), session.View.World with
        { Units = [], Officers = session.View.World.Officers.Select(p => p with { UnitId = null }).ToImmutableArray() });
        var plan = BattlePresentationPlan.Create(session.View, after, facts, session.Messages(texts), texts, content);
        var scene = Assert.Single(plan.Visuals);
        var value = Assert.Single(plan.Values, v => v.Condition == condition);
        Assert.True(value.Time >= scene.End);
        if (visible)
        {
            var effect = Assert.Single(plan.MapEffects);
            Assert.Equal(target, effect.Target);
            Assert.Equal(value.Time, effect.Start);
            Assert.Equal("state-sound", effect.Sprite.Sound);
            Assert.True(plan.Duration >= effect.Start + 0.8);
        }
        else Assert.Empty(plan.MapEffects);
    }

    [Fact]
    public void RecoveryAndReapplyingAnExistingStateDoNotInventTriggerEffects()
    {
        var (session, texts, content, _) = Fixture();
        var before = new StrategySnapshot(MissionScenario.Create(), session.View.World with
        { Units = session.View.World.Units.Select(u => u.Id == 20 ? u with { Condition = UnitCondition.Confused } : u).ToImmutableArray() });
        content = content with { Battle = content.Battle! with { ConditionEffects = new() { [UnitCondition.Confused] = new("status", 8, 100) } } };
        DomainEvent[] facts = [new BattleConditionChangedEvent(before.World.Clock,20,false,UnitCondition.Confused,5),
            new BattleConditionChangedEvent(before.World.Clock,20,false,UnitCondition.Normal,0)];
        var plan = BattlePresentationPlan.Create(before, before, facts, session.Messages(texts), texts, content);
        Assert.Empty(plan.MapEffects);
        Assert.Equal(UnitCondition.Normal, plan.Values.Last().Condition);
    }
    [Fact]
    public void SceneOfficersUseOccurrenceMembersAfterTheSourceHasDisappeared()
    {
        var (session, texts, content, occurrence) = Fixture();
        var battle = content.Battle!;
        var tactic = battle.Tactics[99];
        battle.Tactics[99] = tactic with
        {
            Zoom = tactic.Zoom! with
            { Officers = new(new("officer", 8, 125), [new(0, 0), new(40, 20)], new(40, 20), new(9, -5), 0) }
        };
        occurrence = occurrence with { SourceMemberIds = [2, 3] };
        var after = new StrategySnapshot(MissionScenario.Create(), session.View.World with
        { Units = [], Officers = session.View.World.Officers.Select(p => p with { UnitId = null }).ToImmutableArray() });
        DomainEvent[] facts = [new TacticActivatedEvent(session.Snapshot.Clock, 10, false, 20, false, 99, [new(3, 99)], false, 10) { Occurrence = occurrence }];
        var plan = BattlePresentationPlan.Create(session.View, after, facts, session.Messages(texts), texts, content);
        var officers = Assert.Single(plan.Visuals).Officers!;
        Assert.Equal(new[] { 2, 3 }, officers.Officers.Select(p => p.PersonId));
        Assert.Equal(session.View.Officers.Where(p => p.Id is 2 or 3).OrderBy(p => p.Id).Select(p => p.FlagGlyph), officers.Officers.Select(p => p.Glyph));
    }
    [Theory]
    [InlineData(7, "sniping-fatal", true)]
    [InlineData(4, "sniping-heavy", true)]
    [InlineData(1, "sniping-light", true)]
    [InlineData(7, "sniping-fatal", false)]
    [InlineData(4, "sniping-heavy", false)]
    [InlineData(1, "sniping-light", false)]
    public void SnipingUsesActualShooterAndVictimBeforePublishingTheInjuryRecord(int injury, string response, bool scenesEnabled)
    {
        var (session, texts, content, occurrence) = Fixture();
        foreach (string role in new[] { "sniping-action", "sniping-fatal", "sniping-heavy", "sniping-light", "sniping-own", "sniping-enemy" })
        {
            texts.Messages.Add(role, new MessageDefinition([new MessageLiteral(role)]));
            content.Battle!.Messages.Add(role, new(role, [], role == "sniping-action" ? "actor" : role is "sniping-own" or "sniping-enemy" ? null : "responder"));
        }
        content = content with
        {
            Battle = content.Battle! with
            { SnipingEffect = Enumerable.Range(0, 4).Select(i => new BattleSprite($"sniping-{i}", 1, 300)).ToArray() }
        };
        DomainEvent[] facts = [new TacticActivatedEvent(session.Snapshot.Clock,10,false,20,false,99, [new(3, 99)],false,10) { Occurrence=occurrence },
            new TacticSnipingResolvedEvent(session.Snapshot.Clock,new(10,false),new(20,false),99,2,21,0,injury)];
        var plan = BattlePresentationPlan.Create(session.View, session.View, facts, session.Messages(texts), texts, content, tacticScenesEnabled: scenesEnabled);
        var action = Assert.Single(plan.Messages, c => c.Message.Message.Key == "sniping-action");
        var reaction = Assert.Single(plan.Messages, c => c.Message.Message.Key == response);
        Assert.Equal(2, action.Message.SpeakerId); Assert.Equal(21, reaction.Message.SpeakerId);
        var injuryRecord = Assert.Single(plan.Records, c => c.Record.Message.Key == "sniping-enemy").Record;
        Assert.Equal(1, injuryRecord.ForceColorCode);
        Assert.Equal(MapCoordinates.Cell(occurrence.Source.Position!.Value), injuryRecord.Location);
        Assert.Equal(BattleMessageChannel.Dialogue, action.Channel);
        Assert.Equal(BattleMessageChannel.Dialogue, reaction.Channel);
        Assert.Null(action.Message.MapSpeech);
        Assert.Null(reaction.Message.MapSpeech);
        if (!scenesEnabled)
        {
            Assert.Empty(plan.Visuals);
            return;
        }
        Assert.Equal(4, plan.Visuals.Count(c => c.ScreenEffect));
        var zoom = Assert.Single(plan.Visuals, c => c.Zoom is not null);
        Assert.InRange(action.Time, zoom.Start, zoom.End);
        Assert.Equal(zoom.Start + zoom.Playback!.ActionEnd, action.Time, 6);
        Assert.True(zoom.Playback.FeedbackStart > zoom.Playback.ActionEnd);
        Assert.True(reaction.Time > action.Time);
        var lastFrame = plan.Visuals.Last(c => c.ScreenEffect);
        Assert.NotNull(lastFrame.FadeOutStart);
        Assert.Equal(reaction.Time, lastFrame.End, 6);
        Assert.Equal(action.Time + 1.5, lastFrame.FadeOutStart!.Value, 6);
        Assert.Equal(action.Time + 2.2, lastFrame.End, 6);
        var record = Assert.Single(plan.Records, c => c.Record.Message.Key == "sniping-enemy");
        Assert.Equal(zoom.End, record.Time); Assert.Equal(record.Time, plan.Duration);
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void LinkedNamesUseCapturedTacticsForEachSpeakerAndRestoreThePrimaryForResponses(bool chain, bool scenesEnabled)
    {
        var (session, texts, content, occurrence) = Fixture();
        var basis = MissionScenario.Create();
        var definition = basis with { Information = basis.Information with
        { Tactics = [new(99, "primary-tactic"), new(100, "linked-tactic")] } };
        texts.Entries["primary-tactic"] = "奋迅";
        texts.Entries["linked-tactic"] = "奋斗";
        foreach (string role in new[] { "action", "link", "link-0", "chain-confused", "hit-reaction" })
        {
            texts.Messages[role] = new([new MessageValue(new("property", "tactic.name", Arguments: [new("binding", "tactic.0")]))]);
            var message = new DecisionMessageDefinition(role, new() { ["tactic.0"] = "tactic" },
                role == "hit-reaction" ? "target-commander" : "actor");
            if (role == "action") content.Battle!.Tactics[99].Messages[role] = message;
            else content.Battle!.Messages[role] = message;
        }
        var before = new StrategySnapshot(definition, session.Snapshot);
        // Equipment no longer describes this occurrence; messages must use the captured facts.
        var after = new StrategySnapshot(definition, session.Snapshot with
        { Officers = session.Snapshot.Officers.Select(p => p with { EquippedTacticId = null }).ToImmutableArray() });
        var fact = new TacticActivatedEvent(session.Snapshot.Clock, 10, false, 20, false, 99,
            [new(3, 99), new(2, 100)], false, 10) { Occurrence = occurrence, IsChain = chain };
        var context = new WorldMessageContext(definition, after.World, texts);
        var plan = BattlePresentationPlan.Create(before, after, [fact], context, texts, content, tacticScenesEnabled: scenesEnabled);
        var first = Assert.Single(plan.Messages, c => c.Message.Message.Key == (chain ? "chain-confused" : "action"));
        var linked = Assert.Single(plan.Messages, c => c.Message.Message.Key == "link-0");
        Assert.Equal(3, first.Message.SpeakerId);
        Assert.Equal("奋迅", first.Message.Message.BbCode);
        Assert.Equal(2, linked.Message.SpeakerId);
        Assert.Equal("奋斗", linked.Message.Message.BbCode);
        Assert.Equal(new[] { "奋迅", "奋斗" }, plan.Records.Where(c => c.Record.Message.Key == "link").Select(c => c.Record.Message.BbCode));
        Assert.Equal("奋迅", Assert.Single(plan.Messages, c => c.Message.Message.Key == "hit-reaction").Message.Message.BbCode);
        Assert.Equal(BattleMessageChannel.Dialogue, linked.Channel);
    }

    private static (GameSession Session, TextCatalog Texts, CommandPresentation Content, TacticOccurrence Occurrence) Fixture(bool martial = true)
    {
        var definition = MissionScenario.Create();
        definition = definition with
        {
            Information = definition.Information with { Tactics = [new(99, "tactic")] },
            Military = definition.Military! with { Tactics = [new(99, 0, martial ? TacticEffect.Martial : TacticEffect.Confusion, 10, 10, 0, true, true, [7])] }
        };
        var session = GameSession.Start(definition, 42);
        string[] roles = ["activation-own","activation-enemy","chain-own","chain-enemy","support","link","damage","blocked",
            "hit-reaction","block-reaction","block-reply","chain-confused","chain-unformed","link-0","link-1","link-2","link-3",
            "announcement","action","hit","reduced","failed","united","counter-confusion","counter-trap","defensive"];
        var texts = MissionScenario.Texts(session.View) with { Messages = roles.ToDictionary(r => r, r => new MessageDefinition([new MessageLiteral(r)])) };
        var common = roles.Take(17).ToDictionary(r => r, r => new DecisionMessageDefinition(r, [], r switch
        {
            "hit-reaction" => "target-commander",
            "block-reaction" => "responder",
            "block-reply" or "chain-confused" or "chain-unformed" => "actor",
            _ when r.StartsWith("link-", StringComparison.Ordinal) => "actor",
            _ => null
        }));
        var messages = roles.Skip(17).ToDictionary(r => r, r => new DecisionMessageDefinition(r, [], r switch
        { "announcement" or "action" => "actor", "hit" or "united" or "defensive" => "target-commander", _ => "responder" }));
        texts.Messages["activation-other"] = new([new MessageLiteral("activation-other")]);
        common.Add("activation-other", new("activation-other", []));
        BattleSprite sprite = new("sheet", 8, 100);
        BattleZoomBody body = new(sprite, sprite, sprite, null)
        { Formation = [new(200, 300, 24, 12, 3, 0)], BlockPoints = [new(500, 200), new(600, 250), new(700, 300)] };
        BattleZoomDefinition zoom = new(body, body, sprite, sprite, "source-panel", "target-panel", [])
        {
            Action = new(BattleZoomActionKind.Melee) { TravelX = 168, TravelY = -84, MinimumStep = 8, MaximumStep = 12 },
            DamageBands = [new(2501, 2), new(4500, 3), new(null, 4)],
            SourceFormations = new() { [7] = body with { AdvanceSound = "formation-advance", AttackSound = "formation-attack" } }
        };
        var battle = new BattlePresentationDefinition(common, new()
        {
            [99] = new(messages, sprite, martial ? zoom : null)
            { UnitedResponse = true, CounterResponse = "counter-confusion", FormationResponse = true }
        }, "hit", "block");
        var content = new CommandPresentation([], null) { Battle = battle, Decisions = new([], new() { ["soldier"] = 22 }) };
        var source = new BattleObjectContext(10, false, 1, 3, new(12, 7), 1, false);
        var target = new BattleObjectContext(20, false, 2, 23, new(67, 12), 2, false);
        return (session, texts, content, new(source, target, 7, 7, UnitCondition.Normal, 22, 21, null, false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LinkedBattleRetainsOccurrenceAfterRemovalAndFinishesResponsesBeforePublication(bool blocked)
    {
        var (session, texts, content, occurrence) = Fixture();
        var before = session.View;
        var after = new StrategySnapshot(MissionScenario.Create(), before.World with
        {
            Units = before.World.Units.Where(u => u.Id != 20).ToImmutableArray(),
            Officers = before.World.Officers.Select(p => p.UnitId == 20 ? p with { UnitId = null, SettlementId = 2 } : p).ToImmutableArray()
        });
        DomainEvent[] facts = [new TacticActivatedEvent(before.World.Clock,10,false,20,false,99, [new(3, 99), new(2, 99)],true,blocked?1:10) { Occurrence=occurrence },
            new BattleDamageEvent(before.World.Clock,10,20,null,500,0,500,0,0)
            { TacticId=99,CalculatedSoldierLoss=6000,Source=occurrence.Source,Target=occurrence.Target }];
        var hash = before.World;
        var plan = BattlePresentationPlan.Create(before, after, facts, session.Messages(texts, before.World), texts, content);
        var repeated = BattlePresentationPlan.Create(before, after, facts, session.Messages(texts, before.World), texts, content);
        var visual = Assert.Single(plan.Visuals);
        Assert.Equal(20, visual.Target.Id); Assert.Equal(new WorldPosition(67, 12), visual.Target.Position);
        Assert.Equal(500u, visual.SoldierLoss);
        Assert.Equal(blocked ? 3 : 4, visual.Playback!.Impacts.Length);
        Assert.Equal(blocked ? 0L : 500L, visual.Playback.Impacts.Sum(p => (long)p.Amount));
        Assert.Equal(blocked, visual.Blocked); Assert.True(visual.Doubled);
        Assert.Equal("formation-advance", visual.Zoom!.AdvanceSound);
        Assert.Equal("formation-attack", visual.Zoom.AttackSound);
        Assert.Equal(new[] { 3, 2 }, plan.Messages.Where(c => c.Channel == BattleMessageChannel.Dialogue && c.Time <= visual.Start).Select(c => c.Message.SpeakerId!.Value));
        Assert.All(plan.Messages, c => { Assert.Equal(BattleMessageChannel.Dialogue, c.Channel); Assert.Null(c.Message.MapSpeech); });
        Assert.Equal(2, plan.Records.Count(c => c.Record.Message.Key == "link"));
        var reaction = Assert.Single(plan.Messages, c => c.Message.Message.Key == (blocked ? "block-reaction" : "hit-reaction"));
        Assert.Equal(blocked ? 22 : 23, reaction.Message.SpeakerId);
        Assert.InRange(reaction.Time, visual.Start, visual.End);
        Assert.True(plan.Duration >= reaction.Time + 4);
        Assert.Equal(visual.End, plan.Duration);
        Assert.Equal(visual.End, Assert.Single(plan.Records, c => c.Record.Message.Key == (blocked ? "blocked" : "damage")).Time);
        Assert.Equal(plan.Messages, repeated.Messages);
        StateAssert.Equal(hash, before.World);
    }

    [Theory]
    [InlineData(UnitCondition.Confused, "chain-confused")]
    [InlineData(UnitCondition.NoFormation, "chain-unformed")]
    public void ChainAndLinkedSpeakersUseOrderedPortraitDialogues(UnitCondition condition, string firstRole)
    {
        var (session, texts, content, occurrence) = Fixture();
        var fact = new TacticActivatedEvent(session.Snapshot.Clock, 10, false, 20, false, 99, [new(3, 99), new(2, 99)], false, 10)
        { Occurrence = occurrence with { TargetCondition = condition }, IsChain = true };
        var plan = BattlePresentationPlan.Create(session.View, session.View, [fact], session.Messages(texts), texts, content);
        var initial = plan.Messages.Where(c => c.Time <= plan.Visuals[0].Start).ToArray();
        Assert.Equal(new[] { firstRole, "link-0" }, initial.Select(c => c.Message.Message.Key));
        Assert.Equal(new[] { 3, 2 }, initial.Select(c => c.Message.SpeakerId!.Value));
        Assert.All(initial, c => { Assert.Equal(BattleMessageChannel.Dialogue, c.Channel); Assert.Null(c.Message.MapSpeech); });
        Assert.Contains(plan.Records, c => c.Record.Message.Key == "chain-own");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void MartialEntranceStartsAtTheDialogueCompletionBoundaryAndActionStartsUnderReveal(int count)
    {
        var (session, texts, content, occurrence) = Fixture();
        var participants = new[] { 3, 2, 21, 22, 23 }.Take(count).ToImmutableArray();
        var fact = new TacticActivatedEvent(session.Snapshot.Clock, 10, false, 20, false, 99, participants.Select(id => new TacticParticipant(id, 99)).ToImmutableArray(), false, 10)
        { Occurrence = occurrence };
        var plan = BattlePresentationPlan.Create(session.View, session.View, [fact], session.Messages(texts), texts, content);
        var scene = Assert.Single(plan.Visuals);
        var initial = plan.Messages.Where(c => c.Message.Message.Key == "action" || c.Message.Message.Key.StartsWith("link-", StringComparison.Ordinal)).ToArray();
        Assert.Equal(count, initial.Length);
        Assert.All(initial, c => Assert.Equal(scene.EntranceStart, c.Time));
        Assert.Equal(scene.EntranceStart!.Value + San9.Rendering.BattleSceneTransition.DarkenDuration, scene.Start);
        Assert.Equal(1, San9.Rendering.BattleSceneTransition.Opacity(scene.Start - scene.EntranceStart.Value));
        Assert.Equal(participants, initial.Select(c => c.Message.SpeakerId!.Value));
    }

    [Theory]
    [InlineData(UnitCondition.HighSpirits, 22, true, "united", 23)]
    [InlineData(UnitCondition.Normal, 22, true, "counter-confusion", 22)]
    [InlineData(UnitCondition.Normal, null, true, "failed", 21)]
    public void FailedAttemptUsesCapturedResponsePriorityWithoutInventingActivation(UnitCondition condition, int? counter, bool defensive, string role, int speaker)
    {
        var (session, texts, content, occurrence) = Fixture(false);
        occurrence = occurrence with { TargetCondition = condition, CounterOfficerId = counter, DefensiveFormation = defensive };
        var fact = new IntelligenceEffectResolvedEvent(session.Snapshot.Clock, new(10, false), 20, false, 99, 0)
        { ActorId = 3, Occurrence = occurrence, IsAttempt = true };
        var plan = BattlePresentationPlan.Create(session.View, session.View, [fact], session.Messages(texts), texts, content);
        Assert.Empty(plan.Visuals);
        var announcement = Assert.Single(plan.Messages, c => c.Channel == BattleMessageChannel.Dialogue);
        Assert.Equal("announcement", announcement.Message.Message.Key);
        Assert.True(Assert.Single(plan.Messages, c => c.Message.Message.Key == "action").Time == announcement.Time);
        Assert.Empty(plan.Records);
        Assert.DoesNotContain(plan.Messages, c => c.Message.Message.Key.StartsWith("activation", StringComparison.Ordinal));
        Assert.Equal(3, plan.Messages.First().Message.SpeakerId);
        var response = Assert.Single(plan.Messages, c => c.Message.Message.Key == role);
        Assert.Equal(speaker, response.Message.SpeakerId);
        Assert.Equal(BattleMessageChannel.MapSpeech, response.Channel);
        Assert.NotNull(response.Message.MapSpeech);
        Assert.Equal(condition == UnitCondition.Normal && counter is null, plan.Messages.Any(c => c.Message.Message.Key == "defensive"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IntelligenceAnnouncementPrecedesMapActionAndKeepsEachTargetResponse(bool support)
    {
        var (session, texts, content, occurrence) = Fixture(false);
        var tactic = content.Battle!.Tactics[99];
        content.Battle.Tactics[99] = tactic with { Support = support, EffectAtSource = support };
        DomainEvent[] facts = support
            ? [new TacticActivatedEvent(session.Snapshot.Clock, 10, false, 20, false, 99, [new(3, 99)], false, 10) { Occurrence = occurrence }]
            : [new TacticActivatedEvent(session.Snapshot.Clock, 10, false, 20, false, 99, [new(3, 99)], false, 10) { Occurrence = occurrence },
               new IntelligenceEffectResolvedEvent(session.Snapshot.Clock, new(10, false), 21, false, 99, 3)
               { ActorId = 3, Occurrence = occurrence with { Target = occurrence.Target with { Id = 21, CommanderId = 21 } } },
               new IntelligenceEffectResolvedEvent(session.Snapshot.Clock, new(10, false), 20, false, 99, 10)
               { ActorId = 3, Occurrence = occurrence }];
        var plan = BattlePresentationPlan.Create(session.View, session.View, facts, session.Messages(texts), texts, content);
        var announcement = Assert.Single(plan.Messages, c => c.Channel == BattleMessageChannel.Dialogue);
        Assert.Equal("announcement", announcement.Message.Message.Key);
        Assert.Equal(3, announcement.Message.SpeakerId);
        Assert.Null(announcement.Message.MapSpeech);
        var action = Assert.Single(plan.Messages, c => c.Message.Message.Key == "action");
        Assert.Equal(announcement.Time, action.Time);
        Assert.Equal(BattleMessageChannel.MapSpeech, action.Channel);
        Assert.Equal(10, action.Message.MapSpeech!.UnitId);
        Assert.All(plan.Messages.Where(c => c != announcement), c =>
        { Assert.Equal(BattleMessageChannel.MapSpeech, c.Channel); Assert.NotNull(c.Message.MapSpeech); });
        if (!support)
        {
            Assert.Contains(plan.Messages, c => c.Message.Message.Key == "hit" && c.Message.SpeakerId == 23 && c.Message.MapSpeech!.UnitId == 20);
            Assert.Contains(plan.Messages, c => c.Message.Message.Key == "reduced" && c.Message.MapSpeech!.UnitId == 21);
        }
        Assert.Equal(plan.Messages, BattlePresentationPlan.Create(session.View, session.View, facts, session.Messages(texts), texts, content).Messages);
    }

    [Fact]
    public void FailedAreaAttemptStillPlaysItsPreludeBeforeTheFailureResponse()
    {
        var (session, texts, content, occurrence) = Fixture(false);
        content.Battle!.Tactics[99] = content.Battle.Tactics[99] with
        { Prelude = [new("first", 1, 500, Sound: "screen"), new("last", 1, 500), new("last", 1, 125) { Opacity = .5f }] };
        var fact = new IntelligenceEffectResolvedEvent(session.Snapshot.Clock, new(10, false), 20, false, 99, 0)
        { ActorId = 3, Occurrence = occurrence, IsAttempt = true };
        var plan = BattlePresentationPlan.Create(session.View, session.View, [fact], session.Messages(texts), texts, content);
        Assert.Equal(3, plan.Visuals.Count);
        Assert.All(plan.Visuals, v => Assert.True(v.ScreenEffect));
        Assert.Equal("screen", plan.Visuals[0].Effect!.Sound);
        Assert.All(plan.Visuals.Skip(1), v => Assert.Null(v.Effect!.Sound));
        Assert.True(Assert.Single(plan.Messages, c => c.Message.Message.Key == "failed").Time >= plan.Visuals[^1].End);
        Assert.Equal(plan.Visuals[^1].End, plan.Duration);
        Assert.Equal(1, Assert.Single(plan.Messages, c => c.Message.Message.Key == "failed").Message.MapSpeech!.DelaySeconds);
        Assert.Empty(plan.Records);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AreaOpeningUsesTheFirstVisibleAffectedTarget(bool playerAffected)
    {
        var (session, texts, content, occurrence) = Fixture(false);
        content.Battle!.Tactics[99] = content.Battle.Tactics[99] with { Prelude = [new("screen", 1, 500)] };
        var hidden = occurrence with { Source = occurrence.Source with { ForceId = 3 } };
        var affected = hidden with { Target = hidden.Target with { Id = 21, ForceId = playerAffected ? 1 : 2, CommanderId = 3 } };
        DomainEvent[] facts = [new TacticActivatedEvent(session.Snapshot.Clock,10,false,20,false,99,[new(3,99)],false,10)
            { Occurrence = hidden },
            new IntelligenceEffectResolvedEvent(session.Snapshot.Clock,new(10,false),21,false,99,3)
            { ActorId = 3, Occurrence = affected },
            new IntelligenceEffectResolvedEvent(session.Snapshot.Clock,new(10,false),20,false,99,10)
            { ActorId = 3, Occurrence = hidden }];
        var plan = BattlePresentationPlan.Create(session.View, session.View, facts, session.Messages(texts), texts, content);
        if (playerAffected)
        {
            Assert.Single(plan.Messages, c => c.Message.Message.Key == "announcement");
            Assert.Single(plan.Messages, c => c.Message.Message.Key == "action");
            Assert.Equal(affected.Target, Assert.Single(plan.Visuals, v => v.ScreenEffect).Target);
            Assert.Single(plan.Messages, c => c.Message.Message.Key == "reduced");
            Assert.DoesNotContain(plan.Messages, c => c.Message.Message.Key == "hit");
        }
        else
        {
            Assert.Empty(plan.Messages);
            Assert.Empty(plan.Visuals);
        }
    }

    [Theory]
    [InlineData(false, 175)]
    [InlineData(true, 175)]
    [InlineData(true, 2000)]
    public void MapEffectStartsAfterOnlyItsAuthoredPreludeAndUsesItsResourceDuration(bool support, int frameMilliseconds)
    {
        var (session, texts, content, occurrence) = Fixture(false);
        var tactic = content.Battle!.Tactics[99];
        var sprite = tactic.MapEffect! with { Columns = 4, FrameMilliseconds = frameMilliseconds };
        content.Battle.Tactics[99] = tactic with
        {
            Support = support,
            EffectAtSource = support,
            MapEffect = sprite,
            Prelude = support ? [] : [new("first", 1, 230), new("second", 1, 410)]
        };
        DomainEvent[] facts = support
            ? [new TacticActivatedEvent(session.Snapshot.Clock, 10, false, 20, false, 99, [new(3, 99)], false, 10) { Occurrence = occurrence }]
            : [new TacticActivatedEvent(session.Snapshot.Clock, 10, false, 20, false, 99, [new(3, 99)], false, 10) { Occurrence = occurrence },
               new IntelligenceEffectResolvedEvent(session.Snapshot.Clock, new(10, false), 20, false, 99, 10)
               { ActorId = 3, Occurrence = occurrence }];
        var plan = BattlePresentationPlan.Create(session.View, session.View, facts, session.Messages(texts), texts, content);
        var dialogue = Assert.Single(plan.Messages, c => c.Channel == BattleMessageChannel.Dialogue);
        var effect = Assert.Single(plan.Visuals, c => !c.ScreenEffect);
        Assert.Equal(dialogue.Time + (support ? 0 : .64), effect.Start, 6);
        Assert.Equal(effect.End, plan.Duration);
        Assert.Equal(dialogue.Id, effect.AfterDialogueId);
        var response = Assert.Single(plan.Messages, c => c.Message.Message.Key == "hit");
        Assert.Equal(1, response.Message.MapSpeech!.DelaySeconds);
        if (support) Assert.Equal(effect.Start + 4 * frameMilliseconds / 1000.0, effect.End, 6);
        else
        {
            var prelude = plan.Visuals.Where(c => c.ScreenEffect).ToArray();
            Assert.Equal(dialogue.Time, prelude[0].Start);
            Assert.Equal(.23, prelude[0].End - prelude[0].Start, 6);
            Assert.Equal(prelude[0].End, prelude[1].Start);
            Assert.Equal(effect.Start, prelude[1].End);
        }
    }

    [Fact]
    public void ComputerBattleRetainsADetailedReportWithoutPlayerScenesOrEffects()
    {
        var (session, texts, content, occurrence) = Fixture();
        occurrence = occurrence with { Source = occurrence.Source with { ForceId = 3 } };
        var fact = new TacticActivatedEvent(session.Snapshot.Clock, 10, false, 20, false, 99, [new(3, 99)], false, 10) { Occurrence = occurrence };
        var plan = BattlePresentationPlan.Create(session.View, session.View, [fact], session.Messages(texts), texts, content);
        Assert.Empty(plan.Messages); Assert.Empty(plan.Visuals);
        var report = Assert.Single(plan.Records).Record;
        Assert.Equal("activation-other", report.Message.Key);
        Assert.Equal(MapReportImportance.Detailed, report.Importance);
        Assert.Null(report.ForceColorCode);

        var repeated = BattlePresentationPlan.Create(session.View, session.View, [fact, fact, fact], session.Messages(texts), texts, content);
        Assert.Equal(plan.Duration, repeated.Duration);
        Assert.All(repeated.Records, c => Assert.Equal(plan.Records[0].Time, c.Time));
    }

    [Theory]
    [InlineData(0, false, "country-body")]
    [InlineData(7, false, "sheet")]
    [InlineData(0, true, "imperial-body")]
    public void CountryBodiesRespectHigherPriorityFormationSelection(int formation, bool imperial, string asset)
    {
        var (session, texts, content, occurrence) = Fixture();
        var tactic = content.Battle!.Tactics[99];
        var zoom = tactic.Zoom!;
        content.Battle.Tactics[99] = tactic with
        {
            Zoom = zoom with
            {
                CountrySources = new() { [4] = zoom.Source with { Idle = new("country-body", 8, 125) } },
                ImperialSource = zoom.Source with { Idle = new("imperial-body", 8, 125) }
            }
        };
        var scenario = MissionScenario.Create();
        scenario = scenario with
        {
            Information = scenario.Information with
            {
                CountryTitles = [new(new(4, true, true, false, [], []), "home", 0)],
                Ranks = [new(100, "home", 100, 0, 0, 10)]
            }
        };
        var before = new StrategySnapshot(scenario, session.View.World with
        { Forces = session.View.World.Forces.Select(f => f.Id == 1 ? f with { CountryTitleId = 4, RankId = imperial ? 100 : null } : f).ToImmutableArray() });
        occurrence = occurrence with { SourceFormationId = formation };
        DomainEvent[] facts = [new TacticActivatedEvent(before.World.Clock, 10, false, 20, false, 99, [new(3, 99)], false, 10) { Occurrence = occurrence }];
        var plan = BattlePresentationPlan.Create(before, before, facts, session.Messages(texts, before.World), texts, content);
        Assert.Equal(asset, Assert.Single(plan.Visuals).Playback!.Definition.Source.Idle.Asset);
    }
}

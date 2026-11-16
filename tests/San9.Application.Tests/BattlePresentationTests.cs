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
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void ActivationOpensOnceAtTheSourceBeforeDialogueRegardlessOfSceneOption(bool martial, bool scenes)
    {
        var (session, texts, content, occurrence) = Fixture(martial);
        var sprite = new BattleSprite("activation", 4, 100, Sound: "preparation");
        content = content with { Battle = content.Battle! with { ActivationEffect = sprite } };
        var fact = new TacticActivatedEvent(session.Snapshot.Clock, 10, false, 20, false, 99,
            [new(3, 99), new(23, 99)], false, 10) { Occurrence = occurrence };
        var plan = BattlePresentationPlan.Create(session.View, session.View, [fact], session.Messages(texts), texts,
            content, tacticScenesEnabled: scenes);
        var opening = Assert.Single(plan.MapEffects);
        Assert.Equal(occurrence.Source, opening.Target);
        Assert.Same(sprite, opening.Sprite);
        Assert.True(opening.BeforeDialogues);
        Assert.Null(opening.AfterDialogueId);
        Assert.Null(plan.MapBatches[0].AfterDialogueId);
        Assert.Equal(plan.Messages[0].Time, opening.Start);
    }

    [Theory]
    [InlineData(UnitStrategyKind.Disorder, true, 68)]
    [InlineData(UnitStrategyKind.Disorder, false, 70)]
    [InlineData(UnitStrategyKind.Inspire, true, 90)]
    [InlineData(UnitStrategyKind.Inspire, false, 66)]
    [InlineData(UnitStrategyKind.FalseReport, true, 70)]
    public void StrategyMoralePublishesItsCapturedResultAfterThePrecedingTactic(
        UnitStrategyKind strategy, bool success, int morale)
    {
        var (session, texts, content, occurrence) = Fixture();
        var result = new UnitStrategyResolvedEvent(session.Snapshot.Clock, 3, 20, strategy, 100, 0, success,
            new(new(67, 12), 2, 23, 2, UnitCondition.Normal, false, false))
        {
            PreviousMorale = 70,
            Audience = new(1, 2, new(2, 2, new(67, 12)), 23, 0, 0, morale, 5000, 0)
        };
        DomainEvent[] facts = [new TacticActivatedEvent(session.Snapshot.Clock, 10, false, 20, false, 99, [new(3, 99)], false, 10)
            { Occurrence = occurrence }, result];
        // The final world deliberately does not contain the target: values belong to the result fact.
        var after = session.View.ForWorld(session.View.World with
        {
            Units = [], Officers = session.View.World.Officers.Select(p => p with { UnitId = null }).ToImmutableArray()
        });
        var plan = BattlePresentationPlan.Create(session.View, after, facts, session.Messages(texts), texts, content);
        if (morale == 70) Assert.Empty(plan.Values);
        else
        {
            var value = Assert.Single(plan.Values);
            Assert.Equal(morale - 70, value.Morale);
            Assert.Equal(20, value.TargetId);
            Assert.Equal(1, value.FactIndex);
            Assert.Equal(Assert.Single(plan.Visuals).End, value.Time);
            Assert.Equal(plan.Messages.Last(c => c.Channel == BattleMessageChannel.Dialogue).Id, value.AfterDialogueId);
        }
    }

    [Fact]
    public void DailyLossesAndRecoveryPublishBeforeCombatAndTheLaterStrategy()
    {
        var (session, texts, content, _) = Fixture();
        var clock = session.Snapshot.Clock;
        var unit = session.View.World.Units.Single(u => u.Id == 20);
        var place = session.View.Settlements.First();
        DomainEvent[] facts =
        [
            new DailyAttritionEvent(clock, new(ResourceOwnerKind.Corps, 2), 20, null, true, false, 50, 25, 25, 70, 67),
            new PeriodicMoraleLostEvent(clock, 20, 67, 66),
            new HighSpiritsExpiredEvent(clock, 20, 66, 36)
                { Unit = new(20, false, 2, 23, unit.Position, 2, false) },
            new BattleDamageEvent(clock, 10, 20, null, 500, 0, 500, 0, 1),
            new UnitStrategyResolvedEvent(clock, 3, 20, UnitStrategyKind.Reinforce, 100, 0, true,
                new(unit.Position, 2, 23, 2, UnitCondition.Normal, false, false))
            {
                DeliveredSoldiers = 500, PreviousMorale = 35,
                Audience = new(2, 2, new(2, 2, unit.Position), 23, 0, 0, 40, unit.Soldiers - 50, 0)
            },
            new DailyAttritionEvent(clock, new(ResourceOwnerKind.Corps, 1), null, place.Id, true, false,
                1, 0, 1, place.Morale, place.Morale - 3),
            new GarrisonMoraleRecoveredEvent(clock, place.Id, place.Morale - 3, place.Morale - 2),
            // A transport has no map object; its loss must not be charged to either army.
            new DailyAttritionEvent(clock, new(ResourceOwnerKind.Corps, 1), null, null, true, false, 30, 15, 15, 70, 67)
                { TaskId = Guid.NewGuid() }
        ];
        var plan = BattlePresentationPlan.Create(session.View, session.View, facts, session.Messages(texts), texts, content);
        var values = plan.Values.Where(v => !v.IsSettlement && v.TargetId == unit.Id).ToArray();
        Assert.Equal(unit.Soldiers - 50, (long)unit.Soldiers + values.Sum(v => v.Soldiers));
        Assert.Equal(40, unit.Morale + values.Sum(v => v.Morale));
        var frame = new BattleMapFrame(session.View, plan.MapChanges, plan.Values);
        Assert.Equal(place.Soldiers - 1, frame.Settlements[place.Id].Soldiers);
        Assert.Equal(place.Morale - 2, frame.Settlements[place.Id].Morale);
        Assert.DoesNotContain(plan.Values, v => v.FactIndex == 7);
    }

    [Theory]
    [InlineData(12, "contact")]
    [InlineData(67, "separated")]
    public void AmbienceUsesOccurrenceContactRatherThanTheTacticBody(int targetX, string expected)
    {
        var (session, texts, content, occurrence) = Fixture();
        var tactic = content.Battle!.Tactics[99];
        content.Battle.Tactics[99] = tactic with { Zoom = tactic.Zoom! with
            { AmbienceSound = "separated", ContactAmbienceSound = "contact" } };
        occurrence = occurrence with { Target = occurrence.Target with { Position = new(targetX, 7) } };
        DomainEvent[] facts = [new TacticActivatedEvent(session.Snapshot.Clock, 10, false, 20, false, 99, [new(3, 99)], false, 10)
            { Occurrence = occurrence }];
        var plan = BattlePresentationPlan.Create(session.View, session.View, facts, session.Messages(texts), texts, content);
        Assert.Equal(expected, Assert.Single(plan.Visuals).Zoom!.AmbienceSound);
    }

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
        Assert.Null(Assert.Single(plan.MapChanges).Retirement);
    }

    [Fact]
    public void DefeatSpeechRetainsTheDefeatedCommanderAndPositionWithoutAReadingWaitOrSound()
    {
        var (session, texts, content, occurrence) = Fixture();
        texts.Messages["retreat"] = new([new MessageValue(new("property", "person.name", Arguments: [new("binding", "person.0")]))]);
        texts.Messages["loss"] = new([new MessageLiteral("loss")]);
        content = content with { Progress = new(new()
        {
            ["unit-defeated"] = new("loss", []), ["unit-lost-other"] = new("loss", []), ["unit-lost"] = new("loss", [])
        }, []) { MapSpeeches = new() { ["unit-defeat"] = new("retreat", new() { ["person.0"] = "actor" }, "actor") } } };
        var lost = new UnitTerminatedEvent(session.Snapshot.Clock, occurrence.Target.Id, new(occurrence.Source.Id, false), 0)
            { Target = occurrence.Target, Source = occurrence.Source, Reason = UnitTerminationReason.Defeated };
        var after = session.View.ForWorld(session.View.World with
        {
            Units = session.View.World.Units.Where(u => u.Id != lost.UnitId).ToImmutableArray(),
            Officers = session.View.World.Officers.Select(o => o.UnitId == lost.UnitId ? o with { UnitId = null, SettlementId = 2 } : o).ToImmutableArray()
        });
        var defeat = new UnitDefeatedEvent(lost.Clock, lost.Target, lost.Source, lost.Reason);
        var plan = BattlePresentationPlan.Create(session.View, after, [defeat, lost], session.Messages(texts), texts, content);
        var cue = Assert.Single(plan.Messages);
        var speech = Assert.IsType<MapSpeechMessage>(cue.Message.MapSpeech);
        Assert.Equal(lost.Target.CommanderId, speech.SpeakerId);
        Assert.Equal(MapCoordinates.Cell(lost.Target.Position!.Value), speech.Location);
        Assert.Equal(lost.Target.Id, speech.UnitId);
        Assert.Equal(BattleMessageChannel.MapSpeech, cue.Channel);
        Assert.Null(cue.Message.SoundRole);
        Assert.Equal(0, speech.DelaySeconds);
        Assert.Equal(1, plan.Duration);
        Assert.NotNull(Assert.Single(plan.MapChanges).Retirement);
        var spectator = session.View.ForWorld(session.View.World with
            { Simulation = session.View.World.Simulation with { Controls = [] } });
        Assert.Single(BattlePresentationPlan.Create(spectator, after, [defeat, lost], session.Messages(texts), texts, content).Messages);
        Assert.Empty(ExecutionPublication.Read(session.View, after, session.Messages(texts), texts, content,
            [defeat, lost], presentedFeedback: plan.PresentedFeedback).Messages);
        var noWinner = defeat with { Source = null };
        Assert.Empty(BattlePresentationPlan.Create(session.View, after, [noWinner], session.Messages(texts), texts, content).Messages);
    }

    [Fact]
    public void MapRemovalStillSeparatesAttacksWithoutAReportOrDialogue()
    {
        var (session, texts, content, occurrence) = Fixture();
        var damage = new BattleDamageEvent(session.Snapshot.Clock, 10, 20, null, 500, 0, 500, 0, 0)
            { Source = occurrence.Source, Target = occurrence.Target };
        DomainEvent[] facts = [damage,
            new UnitTerminatedEvent(session.Snapshot.Clock, occurrence.Target.Id, null, 0)
                { Target = occurrence.Target, Reason = UnitTerminationReason.ForceEliminated },
            damage with { TargetUnitId = null, SettlementId = 2, Target = null }];
        var plan = BattlePresentationPlan.Create(session.View, session.View, facts, session.Messages(texts), texts, content);
        Assert.Empty(plan.Messages);
        Assert.Empty(plan.Records);
        Assert.Equal(2, plan.MapBatches.Count);
        Assert.Equal(0, plan.MapBatches[0].LastFactIndex);
        Assert.Equal(2, plan.MapBatches[1].FirstFactIndex);
        Assert.Equal(occurrence.Target.Id, Assert.Single(plan.MapChanges).RemovedUnitId);
    }

    [Fact]
    public void DefeatRemovesAtImpactAndAdditionalCaptivesDoNotExtendTheDay()
    {
        var (session, texts, content, occurrence) = Fixture();
        string[] roles = ["enemy-captured", "person-captured", "unit-defeated", "unit-defeat"];
        foreach (string role in roles) texts.Messages[role] = new([new MessageLiteral(role)]);
        content = content with { Progress = new(roles.ToDictionary(r => r, r => new DecisionMessageDefinition(r, [])), [])
            { MapSpeeches = new() { ["unit-defeat"] = new("unit-defeat", [], "actor") } } };
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
            { Occurrence = occurrence, IsChain = chain, Opponent = support ? target : null },
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
    public void DisabledTacticScenesKeepParticipantSpeechesAndRecordsWithoutSceneResponses(bool blocked, bool chain)
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
        string[] sceneResponses = ["hit-reaction", "block-reaction", "block-reply"];
        foreach (string role in sceneResponses) content.Battle!.Messages.Remove(role);
        var plan = BattlePresentationPlan.Create(before, before, facts, session.Messages(texts, before.World), texts, content,
            tacticScenesEnabled: false);
        Assert.DoesNotContain(plan.Visuals, visual => visual.Zoom is not null);
        Assert.Equal(baseline.Records.Select(c => c.Record.Message.Key), plan.Records.Select(c => c.Record.Message.Key));
        Assert.Equal(baseline.Messages.Where(c => !sceneResponses.Contains(c.Message.Message.Key)).Select(c => (c.Id, c.Message, c.Channel)),
            plan.Messages.Select(c => (c.Id, c.Message, c.Channel)));
        Assert.DoesNotContain(plan.Messages, c => sceneResponses.Contains(c.Message.Message.Key));
        Assert.Equal(1, plan.Duration);
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
    [InlineData(false, 10)]
    [InlineData(true, 10)]
    [InlineData(false, 3)]
    public void DefectionRecordUsesThePlayerReportingSideOnlyForFullEffect(bool enemySource, int effect)
    {
        var (session, texts, content, occurrence) = Fixture(false);
        if (enemySource) occurrence = occurrence with { Source = occurrence.Target, Target = occurrence.Source };
        texts.Messages.Add("defection", new([new MessageLiteral("defection")]));
        content.Battle!.Messages.Add("defection", new("defection", []));
        content = content with { Battle = content.Battle! with { MapNumbers = new("digits", 12, 20, 125, 8, 4) } };
        var fact = new TacticDefectionEvent(session.Snapshot.Clock, 3, 99, occurrence, 200, 200, effect);
        var plan = BattlePresentationPlan.Create(session.View, session.View, [fact], session.Messages(texts), texts, content);
        Assert.Equal(new long[] { 200, -200 }, plan.MapNumbers.Select(c => c.Amount));
        if (effect < 10) { Assert.Empty(plan.Records); return; }
        var record = Assert.Single(plan.Records).Record;
        Assert.Equal(1, record.ForceColorCode);
        Assert.Equal(MapCoordinates.Cell(occurrence.Target.Position!.Value), record.Location);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MapEffectLifetimeIsOwnedByItsBatchRatherThanTheNextSceneTimeline(bool support)
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
        var batch = Assert.Single(plan.MapBatches);
        Assert.True(batch.LastFactIndex < next.FactIndex);
        Assert.All(plan.Visuals.Where(c => c.IsMapEffect), c => Assert.Equal(batch.Time, c.Start));
        Assert.All(plan.MapEffects, c => Assert.Equal(batch.Time, c.Start));
        Assert.Equal(batch.Time, next.EntranceStart);
        Assert.All(plan.MapNumbers, c => Assert.Equal(batch.Time, c.Start));
    }

    [Fact]
    public void RecoveryBeforeOrdinaryCombatDoesNotDelayTheAttackBoundary()
    {
        var (session, texts, content, occurrence) = Fixture();
        DomainEvent[] facts = [new BattleConditionChangedEvent(session.Snapshot.Clock, 20, false, UnitCondition.Normal, 0),
            new BattleDamageEvent(session.Snapshot.Clock, 10, 20, null, 500, 0, 500, 0, 0)
                { Source = occurrence.Source, Target = occurrence.Target }];
        var plan = BattlePresentationPlan.Create(session.View, session.View, facts, session.Messages(texts), texts, content);
        Assert.Empty(plan.MapEffects);
        Assert.All(plan.Values, value => Assert.Equal(1, value.Time));
        Assert.Equal(1, Assert.Single(plan.MapBatches).Time);
        Assert.Equal(1, plan.Duration);
    }

    [Fact]
    public void TacticLossNumbersIncludeComputerBattlesWhileOrdinaryAttacksOnlyUpdateValues()
    {
        var (session, texts, content, occurrence) = Fixture();
        content = content with { Battle = content.Battle! with { MapNumbers = new("digits", 12, 20, 125, 8, 4) } };
        var damage = new BattleDamageEvent(session.Snapshot.Clock, 10, 20, null, 500, 0, 500, 0, 0)
        { Source = occurrence.Source, Target = occurrence.Target };
        var tactic = damage with { TacticId = 99 };
        var computer = tactic with { Source = occurrence.Source with { ForceId = 3 }, SoldierLoss = 700 };
        var plan = BattlePresentationPlan.Create(session.View, session.View, [damage, tactic, computer], session.Messages(texts), texts, content);
        Assert.Equal(1, plan.Values[0].Time);
        Assert.Equal(0, Assert.Single(plan.MapBatches).FirstFactIndex);
        Assert.Equal([-500L, -700L], plan.MapNumbers.Select(number => number.Amount));
        Assert.Equal([1, 2], plan.MapNumbers.Select(number => number.FactIndex));
        Assert.All(plan.MapNumbers, number => Assert.Equal(occurrence.Target, number.Target));
        Assert.Equal([-500L, -500L, -700L], plan.Values.Select(value => value.Soldiers));
        var offscreen = BattlePresentationPlan.Create(session.View, session.View, [damage], session.Messages(texts), texts, content);
        Assert.Empty(offscreen.MapNumbers);
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
        Assert.Equal(zoom.End, plan.Duration);
        Assert.Equal(effect.FactIndex, Assert.Single(plan.MapBatches).FirstFactIndex);
    }

    [Theory]
    [InlineData(UnitCondition.Disguised, 1)]
    [InlineData(UnitCondition.NoFormation, 1)]
    [InlineData(UnitCondition.HighSpirits, 1)]
    [InlineData(UnitCondition.Disguised, 3)]
    public void StandaloneStrategyStateUsesItsOwnPositionAndTimelineRegardlessOfForce(UnitCondition condition, int sourceForce)
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
        var effect = Assert.Single(plan.MapEffects);
        Assert.Equal(target, effect.Target);
        Assert.Equal(value.Time, effect.Start);
        Assert.Equal("state-sound", effect.Sprite.Sound);
        Assert.Equal(effect.Start, plan.Duration);
    }

    [Theory]
    [InlineData(true, 1, UnitCondition.Normal)]
    [InlineData(true, 1, UnitCondition.HighSpirits)]
    [InlineData(false, 1, UnitCondition.Confused)]
    [InlineData(true, 3, UnitCondition.Normal)]
    public void ReinforcementSuccessEffectUsesTheOccurrenceWithoutRequiringAStateChange(
        bool success, int force, UnitCondition condition)
    {
        var (session, texts, content, occurrence) = Fixture();
        var sprite = new BattleSprite("reinforcement", 8, 100, Sound: "reinforcement-sound");
        content = content with { Battle = content.Battle! with { ReinforcementEffect = sprite } };
        var fact = new UnitStrategyResolvedEvent(session.Snapshot.Clock, 3, 20, UnitStrategyKind.Reinforce, 100, 0, success,
            new(new(67, 12), force, 23, 2, condition, false, false))
        {
            PreviousMorale = 70,
            Audience = new(force, force, new(2, 2, null), 23, 1000, 1000, 75, 5000 + (success ? 3000u : 500u), 90),
            DeliveredSoldiers = success ? 3000u : 500u
        };
        var after = new StrategySnapshot(MissionScenario.Create(), session.View.World with
        { Units = [], Officers = session.View.World.Officers.Select(p => p with { UnitId = null }).ToImmutableArray() });
        DomainEvent[] facts = [new TacticActivatedEvent(session.Snapshot.Clock, 10, false, 20, false, 99, [new(3, 99)], false, 10)
            { Occurrence = occurrence }, fact];
        var plan = BattlePresentationPlan.Create(session.View, after, facts, session.Messages(texts), texts, content);
        var value = Assert.Single(plan.Values);
        Assert.Equal(fact.DeliveredSoldiers, value.Soldiers);
        Assert.Equal(5, value.Morale);
        Assert.Equal(Assert.Single(plan.Visuals).End, value.Time);
        Assert.Equal(plan.Messages.Last(c => c.Channel == BattleMessageChannel.Dialogue).Id, value.AfterDialogueId);
        if (success)
        {
            var effect = Assert.Single(plan.MapEffects);
            Assert.Equal(sprite, effect.Sprite);
            Assert.Equal(fact.TargetUnitId, effect.Target.Id);
            Assert.Equal(fact.Target.Position, effect.Target.Position);
            Assert.Equal(force, effect.Target.ForceId);
            Assert.Equal(fact.Target.CommanderId, effect.Target.CommanderId);
            Assert.Equal(effect.Start, plan.Duration);
            Assert.Equal(effect.Start, value.Time);
            Assert.Equal(effect.AfterDialogueId, value.AfterDialogueId);
            Assert.Equal(effect.FactIndex, Assert.Single(plan.MapBatches).FirstFactIndex);
        }
        else Assert.Empty(plan.MapEffects);
    }

    [Theory]
    [InlineData("burn", SeizeOutcomeKind.Money, true)]
    [InlineData("burn", SeizeOutcomeKind.Money, false)]
    [InlineData("seize", SeizeOutcomeKind.Money, true)]
    [InlineData("seize", SeizeOutcomeKind.Person, true)]
    [InlineData("seize", SeizeOutcomeKind.Item, true)]
    [InlineData("seize", SeizeOutcomeKind.Failure, false)]
    public void SuccessfulBuildingStrategiesPlayEffectsWithoutAnySpeech(string kind, SeizeOutcomeKind outcome, bool success)
    {
        var (session, texts, content, _) = Fixture();
        var place = session.View.Settlements.First();
        var sprite = new BattleSprite(kind, 8, 125);
        content = content with { Battle = content.Battle! with
        { BuildingEffects = new() { [kind] = new() { [0] = [sprite] } } } };
        DomainEvent fact = kind == "burn"
            ? new BurnResolvedEvent(session.Snapshot.Clock, 3, place.Id, 100, 0, success, success ? 10 : 0, 0, 0, 0)
            : new SeizeResolvedEvent(session.Snapshot.Clock, 3, place.Id, 0, 100, 100, 100, true, outcome);
        var plan = BattlePresentationPlan.Create(session.View, session.View, [fact], session.Messages(texts), texts,
            content, new Dictionary<int, int> { [place.Id] = 0 });
        Assert.Empty(plan.Messages);
        if (kind == "burn" && success)
        {
            var value = Assert.Single(plan.Values);
            Assert.Equal(-10, value.Durability);
            var before = new BattleMapFrame(session.View, [], []);
            var result = new BattleMapFrame(session.View, plan.MapChanges, plan.Values);
            Assert.Equal(Math.Max(0, before.Settlements[place.Id].Durability - 10), result.Settlements[place.Id].Durability);
            Assert.Equal(plan.Visuals[0].Start, value.Time);
        }
        else Assert.Empty(plan.Values);
        if (success)
        {
            var effect = Assert.Single(plan.Visuals);
            Assert.Equal(sprite, effect.Effect);
            Assert.Equal(place.Id, effect.Target.Id);
            Assert.True(effect.Target.IsSettlement);
            Assert.True(effect.IsMapEffect);
            Assert.Single(plan.MapBatches);
        }
        else Assert.Empty(plan.Visuals);
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
        var injuryRecord = Assert.Single(plan.Records, c => c.Record.Message.Key == "sniping-enemy").Record;
        Assert.Equal(1, injuryRecord.ForceColorCode);
        Assert.Equal(MapCoordinates.Cell(occurrence.Source.Position!.Value), injuryRecord.Location);
        if (!scenesEnabled)
        {
            Assert.Empty(plan.Visuals);
            Assert.Equal("action", Assert.Single(plan.Messages).Message.Message.Key);
            Assert.Equal(1, plan.Duration);
            return;
        }
        var action = Assert.Single(plan.Messages, c => c.Message.Message.Key == "sniping-action");
        var reaction = Assert.Single(plan.Messages, c => c.Message.Message.Key == response);
        Assert.Equal(2, action.Message.SpeakerId); Assert.Equal(21, reaction.Message.SpeakerId);
        Assert.Equal(BattleMessageChannel.Dialogue, action.Channel);
        Assert.Equal(BattleMessageChannel.Dialogue, reaction.Channel);
        Assert.Null(action.Message.MapSpeech);
        Assert.Null(reaction.Message.MapSpeech);
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
        { Tactics = [new(99, "奋迅"), new(100, "奋斗")] } };
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
        if (scenesEnabled) Assert.Equal("奋迅", Assert.Single(plan.Messages, c => c.Message.Message.Key == "hit-reaction").Message.Message.BbCode);
        else Assert.DoesNotContain(plan.Messages, c => c.Message.Message.Key == "hit-reaction");
        Assert.Equal(BattleMessageChannel.Dialogue, linked.Channel);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HealingUsesActivationAdmissionForAllTargets(bool reverseTargets)
    {
        var (session, texts, content, occurrence) = Fixture(false);
        content.Battle!.Tactics[99] = content.Battle.Tactics[99] with { Support = true };
        content.Battle.Messages["healed"] = content.Battle.Messages["damage"];
        var before = session.View.ForWorld(session.Snapshot with
        {
            Corps = [.. session.Snapshot.Corps, new(4, 1, 2, new(10000, 10000))],
            Simulation = session.Snapshot.Simulation with
            { Controls = [new("player", 1, ControllerKind.Player), new("second-player", 2, ControllerKind.Player)] }
        });
        var source = occurrence.Source with { CorpsId = 4 };
        var ally = occurrence.Target with { ForceId = 1, CorpsId = 1 };
        var opponent = new BattleObjectContext(2, true, 2, 21, null, 2, true) { CorpsId = 2 };
        var targets = reverseTargets ? new[] { ally, source } : [source, ally];
        List<DomainEvent> facts = [];
        foreach (var combatOpponent in new[] { opponent, null })
        {
            facts.Add(new TacticActivatedEvent(before.World.Clock, source.Id, false, targets[0].Id, false, 99, [new(3, 99)], false, 10)
            { Occurrence = occurrence with { Source = source, Target = targets[0] }, Opponent = combatOpponent });
            facts.AddRange(targets.Select(target => new TacticHealingEvent(before.World.Clock, new(source.Id, false), target.Id, 100, [], false)
            { TacticId = 99, ActorId = 3, Occurrence = occurrence with { Source = source, Target = target } }));
        }

        var plan = BattlePresentationPlan.Create(before, before, facts, session.Messages(texts).ForWorld(before.World), texts, content,
            tacticScenesEnabled: false);

        Assert.Equal(3, plan.Messages.Count);
        Assert.All(plan.Messages, cue => Assert.Equal(0, cue.FactIndex));
        Assert.Equal(targets.Select(target => target.Id), plan.Visuals.Select(cue => cue.Target.Id));
        Assert.All(plan.Visuals, cue => Assert.InRange(cue.FactIndex, 1, 2));
        Assert.Equal(4, plan.Values.Count);
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
        foreach (var role in new[] { "failed-report", "secondary-countered" })
        {
            texts.Messages[role] = new([new MessageValue(new("property", "person.name", Arguments: [new("binding", "person.0")]))]);
            messages.Add(role, new(role, new() { ["person.0"] = "responder" }));
        }
        messages.Add("secondary-defensive", messages["defensive"] with { Key = "secondary-defensive" });
        texts.Messages["secondary-defensive"] = new([new MessageLiteral("secondary-defensive")]);
        foreach (var role in new[] { "morale-increased", "morale-decreased" })
        {
            texts.Messages[role] = new([new MessageValue(new("binding", "number.0"))]);
            common.Add(role, new(role, new() { ["number.0"] = "current" }));
        }
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
        content = content with { Battle = content.Battle! with { MapNumbers = new("digits", 12, 20, 125, 8, 4) } };
        var before = session.View;
        var after = new StrategySnapshot(MissionScenario.Create(), before.World with
        {
            Units = before.World.Units.Where(u => u.Id != 20).ToImmutableArray(),
            Officers = before.World.Officers.Select(p => p.UnitId == 20 ? p with { UnitId = null, SettlementId = 2 } : p).ToImmutableArray()
        });
        DomainEvent[] facts = [new TacticActivatedEvent(before.World.Clock,10,false,20,false,99, [new(3, 99), new(2, 99)],true,blocked?1:10) { Occurrence=occurrence },
            new BattleDamageEvent(before.World.Clock,10,20,null,200,0,200,0,0)
            { IsTacticExchange=true,Source=occurrence.Source,Target=occurrence.Target },
            new BattleDamageEvent(before.World.Clock,10,20,null,300,0,300,0,0)
            { TacticId=99,CalculatedSoldierLoss=6000,Source=occurrence.Source,Target=occurrence.Target },
            new BattleDamageEvent(before.World.Clock,20,10,null,200,0,200,0,0)
            { IsTacticExchange=true,Source=occurrence.Target,Target=occurrence.Source }];
        var hash = before.World;
        var plan = BattlePresentationPlan.Create(before, after, facts, session.Messages(texts, before.World), texts, content);
        var repeated = BattlePresentationPlan.Create(before, after, facts, session.Messages(texts, before.World), texts, content);
        var visual = Assert.Single(plan.Visuals);
        Assert.Equal(20, visual.Target.Id); Assert.Equal(new WorldPosition(67, 12), visual.Target.Position);
        Assert.Equal(300u, visual.SoldierLoss);
        Assert.Equal(-500L, plan.Values.Where(v => v.TargetId == 20).Sum(v => v.Soldiers));
        Assert.Equal(-200L, Assert.Single(plan.Values, v => v.TargetId == 10).Soldiers);
        Assert.Single(plan.Values.Select(v => v.Time).Distinct());
        Assert.Equal(-300L, Assert.Single(plan.MapNumbers).Amount);
        Assert.Equal(blocked ? 3 : 4, visual.Playback!.Impacts.Length);
        Assert.Equal(blocked ? 0L : 300L, visual.Playback.Impacts.Sum(p => (long)p.Amount));
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
    [InlineData(0, true, "failed-report")]
    [InlineData(3, true, "reduced-report")]
    [InlineData(3, false, null)]
    [InlineData(10, true, "hit-report")]
    public void IntelligenceReportsKeepTheirBranchAndNumbersSeparateFromMapEffects(int effect, bool reducedReport, string? expectedReport)
    {
        var (session, texts, content, occurrence) = Fixture(false);
        var messages = content.Battle!.Tactics[99].Messages;
        if (reducedReport)
        {
            messages.Add("reduced-report", new("reduced-report", new() { ["person.0"] = "responder" }));
            texts.Messages["reduced-report"] = texts.Messages["failed-report"];
        }
        messages.Add("hit-report", new("hit-report", new() { ["number.0"] = "damage" }));
        texts.Messages["hit-report"] = new([new MessageValue(new("binding", "number.0"))]);
        content = content with { Battle = content.Battle with { MapNumbers = new("digits", 12, 20, 125, 8, 4) } };
        var clock = session.Snapshot.Clock;
        List<DomainEvent> facts =
        [
            new TacticActivatedEvent(clock, 10, false, 20, false, 99, [new(3, 99)], false, effect) { Occurrence = occurrence },
            new IntelligenceEffectResolvedEvent(clock, new(10, false), 20, false, 99, effect) { ActorId = 3, Occurrence = occurrence }
        ];
        if (effect > 0) facts.Add(new BattleDamageEvent(clock, 10, 20, null, 300, 0, 300, 0, 0)
        { TacticId = 99, Source = occurrence.Source, Target = occurrence.Target, CalculatedSoldierLoss = 200 });
        var plan = BattlePresentationPlan.Create(session.View, session.View, facts, session.Messages(texts), texts, content);

        Assert.Equal(effect > 0, plan.Visuals.Any(c => c.IsMapEffect));
        if (effect > 0) Assert.Equal(-300, Assert.Single(plan.MapNumbers).Amount);
        else Assert.Empty(plan.MapNumbers);
        var reports = plan.Records.Where(c => c.Record.Message.Key.EndsWith("-report", StringComparison.Ordinal)).ToArray();
        if (expectedReport is null) Assert.Empty(reports);
        else
        {
            var report = Assert.Single(reports);
            Assert.Equal(expectedReport, report.Record.Message.Key);
            Assert.Equal(effect >= 10 ? "200" : texts.Resolve(session.View.MessageSpeaker(effect == 0 ? 21 : 22).NameKey), report.Record.Message.BbCode);
            Assert.Equal(MapCoordinates.Cell(occurrence.Target.Position!.Value), report.Record.Location);
            Assert.Equal(Assert.Single(plan.Messages, c => c.Channel == BattleMessageChannel.Dialogue).Id, report.AfterDialogueId);
        }
        Assert.Equal(plan.Records, BattlePresentationPlan.Create(session.View, session.View, facts, session.Messages(texts), texts, content).Records);
    }

    [Theory]
    [InlineData(TacticPreventionReason.PassiveCounter)]
    [InlineData(TacticPreventionReason.Formation)]
    public void SecondaryPreventionFollowsMainHitWithoutTurningItIntoFailure(TacticPreventionReason reason)
    {
        var (session, texts, content, occurrence) = Fixture(false);
        content = content with { Battle = content.Battle! with { MapNumbers = new("digits", 12, 20, 125, 8, 4) } };
        var clock = session.Snapshot.Clock;
        DomainEvent[] facts =
        [
            new TacticActivatedEvent(clock, 10, false, 20, false, 99, [new(3, 99)], false, 10) { Occurrence = occurrence },
            new IntelligenceEffectResolvedEvent(clock, new(10, false), 20, false, 99, 10) { ActorId = 3, Occurrence = occurrence },
            new BattleDamageEvent(clock, 10, 20, null, 200, 0, 200, 0, 0) { TacticId = 99, Source = occurrence.Source, Target = occurrence.Target },
            new TacticSecondaryPreventedEvent(clock, 3, 99, occurrence, UnitCondition.Confused, reason, 22)
        ];
        var plan = BattlePresentationPlan.Create(session.View, session.View, facts, session.Messages(texts), texts, content);
        Assert.Single(plan.Visuals, c => c.IsMapEffect);
        Assert.Equal(-200, Assert.Single(plan.MapNumbers).Amount);
        Assert.Contains(plan.Values, c => c.TargetId == 20 && c.Soldiers == -200);
        Assert.Single(plan.Messages, c => c.Message.Message.Key == "hit");
        Assert.DoesNotContain(plan.Messages, c => c.Message.Message.Key == "failed");
        Assert.DoesNotContain(plan.Records, c => c.Record.Message.Key == "failed-report");
        if (reason == TacticPreventionReason.PassiveCounter)
            Assert.Equal(texts.Resolve(session.View.MessageSpeaker(22).NameKey),
                Assert.Single(plan.Records, c => c.Record.Message.Key == "secondary-countered").Record.Message.BbCode);
        else
        {
            var speech = Assert.Single(plan.Messages, c => c.Message.Message.Key == "secondary-defensive");
            Assert.Equal(23, speech.Message.SpeakerId);
            Assert.Equal(20, speech.Message.MapSpeech!.UnitId);
            Assert.Equal(0, speech.Message.MapSpeech.DelaySeconds);
        }
    }

    [Theory]
    [InlineData(70, 80, "morale-increased")]
    [InlineData(70, 60, "morale-decreased")]
    [InlineData(100, 100, null)]
    public void TacticMoraleReportsTheFinalValueOnlyWhenItChanged(int previous, int current, string? role)
    {
        var (session, texts, content, occurrence) = Fixture(false);
        var fact = new TacticMoraleChangedEvent(session.Snapshot.Clock, 20, false, previous, current, 99, occurrence);
        var plan = BattlePresentationPlan.Create(session.View, session.View, [fact], session.Messages(texts), texts, content);
        if (role is null) Assert.Empty(plan.Records);
        else
        {
            var report = Assert.Single(plan.Records).Record;
            Assert.Equal(role, report.Message.Key);
            Assert.Equal(current.ToString(), report.Message.BbCode);
            Assert.Equal(MapCoordinates.Cell(occurrence.Target.Position!.Value), report.Location);
        }
    }

    [Theory]
    [InlineData(UnitCondition.HighSpirits, 22, true, "united", 23)]
    [InlineData(UnitCondition.Normal, 22, true, "counter-confusion", 22)]
    [InlineData(UnitCondition.Normal, null, true, "failed", 21)]
    public void FailedActivationUsesCapturedResponsePriority(UnitCondition condition, int? counter, bool defensive, string role, int speaker)
    {
        var (session, texts, content, occurrence) = Fixture(false);
        occurrence = occurrence with { TargetCondition = condition, CounterOfficerId = counter, DefensiveFormation = defensive };
        var fact = new IntelligenceEffectResolvedEvent(session.Snapshot.Clock, new(10, false), 20, false, 99, 0)
        { ActorId = 3, Occurrence = occurrence };
        var plan = BattlePresentationPlan.Create(session.View, session.View,
            [new TacticActivatedEvent(session.Snapshot.Clock,10,false,20,false,99,[new(3,99)],false,0) { Occurrence=occurrence }, fact],
            session.Messages(texts), texts, content);
        Assert.Empty(plan.Visuals);
        var announcement = Assert.Single(plan.Messages, c => c.Channel == BattleMessageChannel.Dialogue);
        Assert.Equal("announcement", announcement.Message.Message.Key);
        Assert.True(Assert.Single(plan.Messages, c => c.Message.Message.Key == "action").Time == announcement.Time);
        Assert.Contains(plan.Records, c => c.Record.Message.Key == "activation-own");
        var report = Assert.Single(plan.Records, c => c.Record.Message.Key == "failed-report");
        Assert.Equal(texts.Resolve(session.View.MessageSpeaker(21).NameKey), report.Record.Message.BbCode);
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
    public void FailedAreaActivationStillPlaysItsPreludeBeforeTheFailureResponse()
    {
        var (session, texts, content, occurrence) = Fixture(false);
        content.Battle!.Tactics[99] = content.Battle.Tactics[99] with
        { Prelude = [new("first", 1, 500, Sound: "screen"), new("last", 1, 500), new("last", 1, 125) { Opacity = .5f }] };
        var fact = new IntelligenceEffectResolvedEvent(session.Snapshot.Clock, new(10, false), 20, false, 99, 0)
        { ActorId = 3, Occurrence = occurrence };
        var plan = BattlePresentationPlan.Create(session.View, session.View,
            [new TacticActivatedEvent(session.Snapshot.Clock,10,false,20,false,99,[new(3,99)],false,0) { Occurrence=occurrence }, fact],
            session.Messages(texts), texts, content);
        Assert.Equal(3, plan.Visuals.Count);
        Assert.All(plan.Visuals, v => Assert.True(v.ScreenEffect));
        Assert.Equal("screen", plan.Visuals[0].Effect!.Sound);
        Assert.All(plan.Visuals.Skip(1), v => Assert.Null(v.Effect!.Sound));
        Assert.True(Assert.Single(plan.Messages, c => c.Message.Message.Key == "failed").Time >= plan.Visuals[^1].End);
        Assert.Equal(plan.Visuals[^1].End, plan.Duration);
        Assert.Equal(1, Assert.Single(plan.Messages, c => c.Message.Message.Key == "failed").Message.MapSpeech!.DelaySeconds);
        Assert.Contains(plan.Records, c => c.Record.Message.Key == "activation-own");
        Assert.Single(plan.Records, c => c.Record.Message.Key == "failed-report");
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
        Assert.Equal(effect.Start, plan.Duration);
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ComputerBattleRetainsADetailedReportWithoutPlayerScenesOrEffects(bool scenesEnabled)
    {
        var (session, texts, content, occurrence) = Fixture();
        occurrence = occurrence with { Source = occurrence.Source with { ForceId = 3 } };
        var fact = new TacticActivatedEvent(session.Snapshot.Clock, 10, false, 20, false, 99, [new(3, 99)], false, 10) { Occurrence = occurrence };
        var plan = BattlePresentationPlan.Create(session.View, session.View, [fact], session.Messages(texts), texts, content,
            tacticScenesEnabled: scenesEnabled);
        Assert.Empty(plan.Messages); Assert.Empty(plan.Visuals);
        var report = Assert.Single(plan.Records).Record;
        Assert.Equal("activation-other", report.Message.Key);
        Assert.Equal(MapReportImportance.Detailed, report.Importance);
        Assert.Null(report.ForceColorCode);

        var repeated = BattlePresentationPlan.Create(session.View, session.View, [fact, fact, fact], session.Messages(texts), texts, content,
            tacticScenesEnabled: scenesEnabled);
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

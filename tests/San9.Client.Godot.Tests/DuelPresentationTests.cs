using System.Collections.Immutable;
using San9.Application;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Tests;

namespace San9.Client.Godot.Tests;

public sealed class DuelPresentationTests
{
    [Fact]
    public void LaterContestsUseTheirOwnParticipantStateForEntryAndSpeech()
    {
        var (session, texts, content, facts) = Fixture(withTreasures: true);
        var initial = (DuelStartedEvent)facts[0];
        var later = initial with
        {
            Participants = initial.Participants.Select(p => p.Id == 11
                ? p with { Injury = 5, BaseAbilities = new(80, 50, 50, 50) } : p).ToImmutableArray(),
            Treasures = [new(8, 11, TreasureDiscovery.Discovered, null)]
        };
        texts.Messages["entry"] = new([new MessageValue(new("property", "person.war",
            Arguments: [new("binding", "person.0")])), new MessageLiteral(":"),
            new MessageValue(new("query", "has-treasure", Arguments: [new("binding", "person.0"), new("entity", "Treasure", 7)])),
            new MessageLiteral(":"),
            new MessageValue(new("query", "has-treasure", Arguments: [new("binding", "person.0"), new("entity", "Treasure", 8)]))]);
        content.Battle!.Duel!.Messages["entry"] = new("entry", new() { ["person.0"] = "actor" }, "actor");
        DomainEvent[] occurrences = [.. facts.Where(e => e is not OfficerDiedEvent), later,
            new DuelStrikeEvent(initial.Clock, 11, 22, DuelStrikeKind.Normal, 100, 0, 0),
            new DuelPairWonEvent(initial.Clock, 11, 22, 100)];
        var plan = BattlePresentationPlan.Create(session.View, session.View, occurrences,
            session.Messages(texts), texts, content);
        Assert.Equal(2, plan.Duels.Count);
        var entry = plan.Duels[1].Stages.First(s => s.Kind == DuelStageKind.Entry);
        Assert.Equal(5, entry.Left.Injury);
        Assert.Equal(55, entry.Left.War);
        Assert.Equal("55:0:1", plan.Duels[1].Stages.First(s => s.Dialogue is not null).Dialogue!.Message.BbCode);
        Assert.Equal(0, session.View.World.Officers.Single(p => p.Id == 11).Injury);
        Assert.Equal(60, plan.Duels[0].Stages[0].Left.War);
        Assert.Equal("60:1:0", plan.Duels[0].Stages.First(s => s.Dialogue is not null).Dialogue!.Message.BbCode);
        Assert.Equal(11, session.View.World.Treasures.Single(t => t.Id == 7).HolderId);
    }

    [Fact]
    public void ParticlesAndAudienceAnimationsFollowTheirOwnTriggers()
    {
        var (session, texts, content, facts) = Fixture();
        var scene = Assert.Single(BattlePresentationPlan.Create(session.View, session.View, facts,
            session.Messages(texts), texts, content).Duels);
        Assert.Single(scene.Flags.Select(f => f.Direction).Distinct());
        var hits = scene.Particles.Where(p => p.Definition.Kind == DuelParticleKind.StrongHit).ToArray();
        Assert.Equal(2, hits.Length);
        Assert.All(hits, p => Assert.Contains(scene.Stages, s => s.Kind == DuelStageKind.Impact && s.Start == p.Start));
        Assert.Contains(hits, p => p.End > scene.Stages.Single(s => s.Start == p.Start).End);
        var normalHit = scene.Stages.Single(s => s.Kind == DuelStageKind.Impact && s.LeftHitPoints == 40 && s.Left.HitPoints > 40);
        var crowd = scene.Stages.First(s => s.Start >= normalHit.End && s.Kind == DuelStageKind.Audience);
        Assert.DoesNotContain(scene.Particles, p => p.Start == crowd.Start);
        Assert.All(scene.Particles, p => Assert.True(p.End > p.Start));
        var victory = scene.Stages.First(s => s.Kind == DuelStageKind.Victory);
        var dismay = scene.Stages.Last(s => s.Kind == DuelStageKind.Audience && s.Start < victory.Start);
        Assert.Equal(DuelAudienceMood.Dismay, dismay.RightAudience);
        Assert.Equal(0, dismay.RightAudienceAge);
        Assert.Equal(victory.Start - dismay.Start, victory.RightAudienceAge, 6);
    }

    [Fact]
    public void FightersStayAtCombatPositionsAndReplacementDoesNotFadeTheWinner()
    {
        var (session, texts, content, facts) = Fixture();
        var scene = Assert.Single(BattlePresentationPlan.Create(session.View, session.View, facts,
            session.Messages(texts), texts, content).Duels);
        var approaches = scene.Stages.Where(s => s.Kind == DuelStageKind.Approach).ToArray();
        Assert.Equal(2, approaches.Length);
        Assert.Equal(2.2, approaches[0].End - approaches[0].Start, 6);
        Assert.Equal(approaches[1].LeftFrom, approaches[1].LeftTo);
        Assert.Equal(DuelAction.Idle, approaches[1].LeftAction);
        var entry = Assert.Single(scene.Stages, s => s.Kind == DuelStageKind.Entry && s.Right.Id == 23);
        Assert.Equal(1, entry.LeftOpacityFrom);
        Assert.Equal(1, entry.LeftOpacityTo);
        Assert.Equal(0, entry.RightOpacityFrom);
        Assert.Equal(1, entry.RightOpacityTo);
        foreach (var impact in scene.Stages.Where(s => s.Kind == DuelStageKind.Impact))
        {
            var strike = scene.Stages.Last(s => s.Kind == DuelStageKind.Strike && s.End <= impact.Start);
            bool left = strike.LeftAction is DuelAction.Attack or DuelAction.HeavyAttack;
            Assert.Equal(left ? strike.LeftAction : strike.RightAction, left ? impact.LeftAction : impact.RightAction);
            Assert.Equal(strike.End - strike.Start, left ? impact.LeftAnimationAge : impact.RightAnimationAge, 6);
        }
    }

    [Fact]
    public void FullScreenEffectRetainsItsLastFrameDuringFade()
    {
        ImmutableArray<BattleSprite> frames = [new("effect", 8, 100)];
        var last = Assert.IsType<DuelEffectFrame>(DuelSceneCue.SampleEffect(frames, .79));
        Assert.Equal(7, last.Frame);
        Assert.Equal(1, last.Opacity);
        var fading = Assert.IsType<DuelEffectFrame>(DuelSceneCue.SampleEffect(frames, .91));
        Assert.Equal(7, fading.Frame);
        Assert.InRange(fading.Opacity, .49f, .51f);
        Assert.Null(DuelSceneCue.SampleEffect(frames, 1.01));
    }

    [Theory]
    [InlineData(80, 50, true, DuelStrikeKind.Normal, 0, DuelAudienceMood.Cheer, DuelAudienceMood.Dismay)]
    [InlineData(25, 19, true, DuelStrikeKind.Normal, 0, DuelAudienceMood.Idle, DuelAudienceMood.Dismay)]
    [InlineData(50, 60, true, DuelStrikeKind.Parry, 0, DuelAudienceMood.Dismay, DuelAudienceMood.Idle)]
    [InlineData(50, 60, true, DuelStrikeKind.DoubleStrike, 0, DuelAudienceMood.Cheer, DuelAudienceMood.Dismay)]
    [InlineData(60, 50, true, DuelStrikeKind.Normal, 3, DuelAudienceMood.Cheer, DuelAudienceMood.Idle)]
    public void AudienceReactionsFollowHealthAndStrikeConditions(int left, int right, bool attackingLeft,
        DuelStrikeKind kind, int advantage, DuelAudienceMood expectedLeft, DuelAudienceMood expectedRight)
    {
        Assert.Equal((expectedLeft, expectedRight), DuelSceneCue.AudienceReaction(left, right, attackingLeft, kind, advantage));
    }

    [Fact]
    public void CustomWindupRepeatsBodyFramesAndKeepsItsLastPose()
    {
        var animation = new DuelAnimation("body", 8, 100) { Frames = [0, 1, 2, 1, 2, 7], Intervals = [70, 70, 70, 70, 70, 100] };
        Assert.Equal(.45, animation.Duration, 6);
        Assert.Equal(2, DuelSceneCue.AnimationFrame(animation, .15, false));
        Assert.Equal(1, DuelSceneCue.AnimationFrame(animation, .22, false));
        Assert.Equal(7, DuelSceneCue.AnimationFrame(animation, 100, false));
        Assert.Equal(0, DuelSceneCue.AnimationFrame(animation, .46, true));
    }

    [Fact]
    public void PreparedStrikesUseFactHitPointsAndInjuriesAndHealOnlyTheReturningWinner()
    {
        var (session, texts, content, facts) = Fixture();
        var before = session.Snapshot;
        var plan = BattlePresentationPlan.Create(session.View, session.View, facts, session.Messages(texts), texts, content);
        var scene = Assert.Single(plan.Duels);
        var impacts = scene.Stages.Where(s => s.Kind == DuelStageKind.Impact).ToArray();
        Assert.Equal(5, impacts.Length);
        Assert.True(impacts[1].End - impacts[1].Start < content.Battle!.Duel!.Bodies[0].Actions[DuelAction.Hit].Duration);
        foreach (var impact in impacts)
        {
            bool right = impact.RightHitPoints != impact.Right.HitPoints;
            var fighter = right ? impact.Right : impact.Left;
            int hp = right ? impact.RightHitPoints : impact.LeftHitPoints;
            var next = scene.Stages.FirstOrDefault(s => s.Kind == DuelStageKind.Strike && s.Start >= impact.End);
            if (next is not null) Assert.True(next.Start >= impact.Start + DuelSceneCue.HitPointsDuration(fighter.HitPoints, hp));
        }
        Assert.Equal(0, DuelSceneCue.HitPoints(impacts[^1], impacts[^1].End, false));
        Assert.Equal(100, impacts[0].RightHitPoints); // Parry does not invent damage.
        Assert.Equal(30, impacts[1].RightHitPoints);
        Assert.Equal(82.5, DuelSceneCue.HitPoints(impacts[1], impacts[1].Start + .25, true), 6);
        var afterImpact = scene.Sample(impacts[1].End)!;
        Assert.Equal(DuelSceneCue.HitPoints(impacts[1], impacts[1].End, true), DuelSceneCue.HitPoints(afterImpact, afterImpact.Start, true));
        Assert.Equal(30, DuelSceneCue.HitPoints(afterImpact, impacts[1].Start + 1, true));
        Assert.Contains(scene.Stages, s => s.Right.Injury == 2 && s.Right.War == 40);
        var replacement = Assert.Single(scene.Stages, s => s.Kind == DuelStageKind.Entry && s.Right.Id == 23);
        Assert.Equal(40, replacement.Left.HitPoints);
        Assert.Equal(40, replacement.LeftHitPoints);
        var recovery = Assert.Single(scene.Stages, s => s.Kind == DuelStageKind.Dialogue && s.LeftHitPoints > s.Left.HitPoints);
        Assert.Equal(60, recovery.LeftHitPoints);
        Assert.Contains(scene.Stages, s => s.Kind == DuelStageKind.Preparation && s.Start > replacement.Start && s.End <= recovery.Start);
        Assert.Equal(100, replacement.RightHitPoints);
        Assert.Equal(60, impacts[^1].Left.HitPoints);
        Assert.Equal(0, impacts[^1].LeftHitPoints);
        Assert.Equal(new[] { "won", "lost" }, plan.Records.Select(r => r.Record.Message.Key));
        // The contest's last pair is sufficient: a pending succession may defer DuelCompletedEvent.
        Assert.Equal("lost", Assert.Single(plan.Messages, m => m.Channel == BattleMessageChannel.Dialogue && m.SceneId is null && m.Message.Message.Key != "death").Message.Message.Key);
        Assert.True(plan.Messages.Single(m => m.Message.Message.Key == "death").Time > scene.End);
        StateAssert.Equal(before, session.Snapshot);
    }

    [Fact]
    public void DisabledSceneKeepsTheSameNecessaryResultsWithoutSceneResourcesOrSpeech()
    {
        var (session, texts, content, facts) = Fixture();
        BattlePresentationPlan Plan(bool enabled) => BattlePresentationPlan.Create(session.View, session.View, facts,
            session.Messages(texts), texts, content, duelScenesEnabled: enabled);
        var shown = Plan(true); var hidden = Plan(false);
        Assert.Empty(hidden.Duels);
        Assert.DoesNotContain(hidden.Messages, m => m.Channel == BattleMessageChannel.DuelDialogue);
        Assert.Equal(shown.Records.Select(r => r.Record.Message), hidden.Records.Select(r => r.Record.Message));
        Assert.Equal(shown.Messages.Where(m => m.Channel == BattleMessageChannel.Dialogue && m.SceneId is null).Select(m => m.Message), hidden.Messages.Select(m => m.Message));
        Assert.True(hidden.Duration < shown.Duration);
        var repeated = Plan(true);
        Assert.Equal(shown.Messages, repeated.Messages);
        Assert.Equal(shown.Duels[0].Flags.ToArray(), repeated.Duels[0].Flags.ToArray());
        Assert.Equal(shown.Duels[0].Particles.ToArray(), repeated.Duels[0].Particles.ToArray());
        Assert.Equal(shown.Duels[0].Stages.Select(s => (s.Start, s.End, s.Left, s.Right, s.LeftAction, s.RightAction)),
            repeated.Duels[0].Stages.Select(s => (s.Start, s.End, s.Left, s.Right, s.LeftAction, s.RightAction)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PauseDialogueAndSkipNeverPublishBeforeVictoryAndDeathHaveBeenAcknowledged(bool skip)
    {
        var (session, texts, content, facts) = Fixture();
        var before = session.Snapshot;
        var plan = BattlePresentationPlan.Create(session.View, session.View, facts, session.Messages(texts), texts, content);
        var day = new TurnDayPresentation(session.View, session.View, facts, battle: plan);
        day.Advance(10000);
        Assert.Equal("introduction", Assert.Single(day.PendingDialogues).Message.Message.Key);
        day.CompleteDialogue(day.PendingDialogues[0].Id);
        day.Advance(10000);
        Assert.NotEmpty(day.PendingDuelDialogues);
        double gate = day.Elapsed, visual = day.DuelDialogueElapsed;
        day.Advance(.1);
        Assert.Equal(gate, day.Elapsed);
        Assert.Equal(visual + .1, day.DuelDialogueElapsed, 6);
        day.Advance(10, paused: true);
        Assert.Equal(gate, day.Elapsed);
        Assert.Equal(visual + .1, day.DuelDialogueElapsed, 6);
        if (skip) day.SkipVisual();
        else
            for (int guard = 0; guard < 100 && day.PendingDialogues.Count == 0; guard++)
            {
                foreach (var message in day.PendingDuelDialogues) day.CompleteDialogue(message.Id);
                Assert.Equal(0, day.DuelDialogueElapsed);
                day.Advance(10000);
            }
        Assert.Empty(day.PendingDuelDialogues);
        Assert.Equal(plan.Duels[0].End, day.Elapsed);
        Assert.False(day.IsComplete);
        Assert.Equal("lost", Assert.Single(day.PendingDialogues).Message.Message.Key);
        day.SkipVisual(); Assert.Equal(plan.Duels[0].End, day.Elapsed);
        day.CompleteDialogue(day.PendingDialogues[0].Id);
        day.Advance(10000);
        Assert.Equal("death", Assert.Single(day.PendingDialogues).Message.Message.Key);
        Assert.False(day.IsComplete);
        day.CompleteDialogue(day.PendingDialogues[0].Id);
        day.Advance(10000); Assert.True(day.IsComplete);
        StateAssert.Equal(before, session.Snapshot);
    }

    [Fact]
    public void DeathSceneIsConsumedEvenWithoutProgressRecords()
    {
        var (session, texts, content, facts) = Fixture();
        foreach (string role in new[] { "duel-announcement", "ruler-death" })
            texts.Messages[role] = new([new MessageLiteral(role)]);
        content = content with { Events = new(new()
        {
            ["duel-announcement"] = new("duel-announcement", []),
            ["ruler-death"] = new("ruler-death", [], Illustration: "actor")
        }) };
        var view = session.View;
        DomainEvent[] deaths = [facts[^1]];

        var plan = BattlePresentationPlan.Create(view, view, deaths, session.Messages(texts), texts, content);
        var publication = ExecutionPublication.Read(view, view, session.Messages(texts), texts, content,
            deaths, presentedFeedback: plan.PresentedFeedback);

        Assert.Equal(new[] { "death", "ruler-death" }, plan.Messages.Select(m => m.Message.Message.Key));
        Assert.Empty(publication.Messages);
    }

    private static (GameSession Session, TextCatalog Texts, CommandPresentation Content, DomainEvent[] Facts) Fixture(bool withTreasures = false)
    {
        var combat = new CombatProfile(0, 0, 0, 1000, 50);
        OfficerInitialState Person(int id, int force) => new($"person.{id}",
            new(id, force, null, new(50, 50, 50, 50), false) { UnitId = force, Status = id == 23 ? OfficerStatus.Serving : OfficerStatus.Ruler });
        var scenario = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "duel", 190, 1, 1, 1,
            [new(1, "home", 1, 100) { LeaderId = 11 }, new(2, "away", 2, 100) { LeaderId = 22 }],
            [new(1, "home", 1, 0, 0, 100, 100, 100, 100, []), new(2, "away", 2, 3, 0, 100, 100, 100, 100, [])], [], [])
        {
            Officers = [Person(11, 1), Person(22, 2), Person(23, 2)],
            Units = [new(1, 1, new(6, 2), 10000) { FormationId = 1, CommanderId = 11, MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(11, BattleRank.Front) },
                new(2, 2, new(7, 2), 10000) { FormationId = 1, CommanderId = 22, MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(22, BattleRank.Front).Add(23, BattleRank.Rear) }],
            Military = new(4, 1, [0, 0, 0, 0], [new(0, 1, null)],
                [new(1, 5, 0, 100000, 3, new(1, 1), TravelMode.Land) { Combat = combat, Attack = new(1, false),
                    CaptureRanks = [BattleRank.Front, BattleRank.Front, BattleRank.Rear], Tactics = new(0, 2, [], []) }],
                [new(1, new(2, 2), new(1, 1)) { Combat = combat }, new(2, new(17, 2), new(1, 1)) { Combat = combat }]),
            Information = new([], [], [], [new CorpsState(1, 1, 11, new(0, 10000)) { IsRoot = true }, new CorpsState(2, 2, 22, new(0, 10000)) { IsRoot = true }], [])
        };
        if (withTreasures)
            scenario = scenario with { Information = scenario.Information with
            {
                Treasures = [new(7, "treasure.7", 1, 10, 0, null) { AbilityBonuses = new(10, 0, 0, 0) },
                    new(8, "treasure.8", 1, 15, 0, null) { AbilityBonuses = new(15, 0, 0, 0) }],
                TreasureStates = [new(7, 11, TreasureDiscovery.Discovered, null), new(8, null, TreasureDiscovery.Discovered, null)]
            } };
        var session = GameSession.Start(scenario, 42);
        string[] roles = ["entry", "answer", "victory", "introduction", "strong", "finisher", "strong-hit", "finisher-hit", "parry", "won", "lost", "death"];
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, new() { ["home"] = "home", ["away"] = "away", ["person.11"] = "First", ["person.22"] = "Second", ["person.23"] = "Replacement" })
        { Messages = roles.ToDictionary(r => r, r => new MessageDefinition([new MessageLiteral(r)])) };
        var messages = roles.ToDictionary(r => r, r => new DecisionMessageDefinition(r, [], r is "won" or "lost" or "death" or "introduction" ? null : "actor"));
        string[] sounds = ["ambience", "entry", "entry-prepare", "return", "parry", "defeat", "win", "lose", "prepare", "hit", "heavy-hit", "strong", "cheer", "despair", "finisher", "finisher-effect", "resolution-effect"];
        var bodies = new[] { (false, false), (false, true), (true, false), (true, true) }.Select(p => new DuelBody(p.Item1, p.Item2,
            Enum.GetValues<DuelAction>().ToDictionary(a => a, a => new DuelAnimation("body", 8, 100)))).ToArray();
        var definition = new DuelPresentationDefinition(messages, bodies, [], "backdrop", sounds.ToDictionary(s => s, s => s),
            Enumerable.Range(0, 8).Select(i => new[] { new BattleSprite("finisher", 1, 100) }).ToArray(), [new("resolution", 1, 100)])
        {
            Interface = new(new(24, 104), "072746", .7f, [], []),
            Positions = new(new(236, 352), new(576, 184), new(368, 288), new(444, 252), 60),
            Flags = new([new(72, 268)], [new(441, 94)]),
            Particles = Enum.GetValues<DuelParticleKind>().Select(kind => new DuelParticleDefinition(kind, new("particle", 8, 100), [new(0, 0)], [new(1, 1)])).ToArray()
        };
        var content = new CommandPresentation([], null) { Battle = new([], [], "hit", "block") { Duel = definition } };
        var clock = session.Snapshot.Clock;
        DomainEvent[] facts = [new DuelStartedEvent(clock, new(1, 2, [11], [22, 23]),
                session.View.World.Officers.ToImmutableArray(), session.View.World.Treasures.Where(t => t.HolderId is not null).ToImmutableArray()),
            new DuelStrikeEvent(clock, 11, 22, DuelStrikeKind.Parry, 0, 100, 0),
            new DuelStrikeEvent(clock, 11, 22, DuelStrikeKind.DoubleStrike, 70, 30, 2),
            new DuelStrikeEvent(clock, 22, 11, DuelStrikeKind.Normal, 60, 40, 3),
            new DuelStrikeEvent(clock, 11, 22, DuelStrikeKind.FinishingStrike, 30, 0, 4),
            new DuelPairWonEvent(clock, 11, 22, 40),
            new DuelStrikeEvent(clock, 23, 11, DuelStrikeKind.Normal, 60, 0, 4),
            new DuelPairWonEvent(clock, 23, 11, 100),
            new OfficerDiedEvent(clock, 11, OfficerDeathCause.Duel, OfficerStatus.Ruler, 1, null, null, 1)];
        return (session, texts, content, facts);
    }
}

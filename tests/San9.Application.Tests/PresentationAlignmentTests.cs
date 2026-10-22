using System.Collections.Immutable;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Packages;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class PresentationAlignmentTests
{
    private static (GameSession Session, TextCatalog Texts, CommandPresentation Content) Fixture()
    {
        var session = GameSession.Start(MissionScenario.Create(), 42);
        var roles = ProgressPresentationDefinition.RecordRoles.Concat(ProgressPresentationDefinition.FeedbackRoles)
            .Concat(ProgressPresentationDefinition.MapSpeechRoles).Concat(new[] { "employ-secret-meeting", "employ-failure", "employ-result-failure" }).Distinct();
        var texts = MissionScenario.Texts(session.View) with
        { Messages = roles.ToDictionary(r => r, r => new MessageDefinition([new MessageLiteral(r)])) };
        var progress = new ProgressPresentationDefinition(ProgressPresentationDefinition.RecordRoles.ToDictionary(r => r, r => new DecisionMessageDefinition(r, [])), [])
        {
            Feedback = ProgressPresentationDefinition.FeedbackRoles.ToDictionary(r => r, r => new DecisionMessageDefinition(r, [],
                r is "appearance-greeting" or "prisoner-escape-speech" or "battle-capture-speech" or "battle-capture-dialogue" ? "actor" : null)),
            MapSpeeches = ProgressPresentationDefinition.MapSpeechRoles.ToDictionary(r => r, r => new DecisionMessageDefinition(r, [], "actor"))
        };
        return (session, texts, CommandPresentation.Empty with { Progress = progress });
    }

    [Fact]
    public void RecruitmentDialogueHasAPlayableChannelAndIsNotRepublished()
    {
        var (session, texts, content) = Fixture();
        var clock = session.Snapshot.Clock;
        var before = new StrategySnapshot(MissionScenario.Create(), session.Snapshot with
        { Simulation = session.Snapshot.Simulation with { Tasks = [new(Guid.NewGuid(), "personnel-recruit", "player", 1, 2, 1, clock, clock, 0, 0)] } });
        content = content with { Personnel = new(new[] { "employ-secret-meeting", "employ-failure", "employ-result-failure" }
            .ToDictionary(r => r, r => new DecisionMessageDefinition(r, []))) };
        DomainEvent[] facts = [new RecruitmentResolvedEvent(clock, 2, 22, 1, 99, false)
            { Occurrence = new(1, OfficerStatus.Serving, new(2, 2, null), false, false, null) }];
        var plan = BattlePresentationPlan.Create(before, before, facts, session.Messages(texts, before.World), texts, content);
        Assert.Equal(3, plan.Messages.Count);
        Assert.All(plan.Messages, m => Assert.Equal(BattleMessageChannel.Dialogue, m.Channel));
        var remaining = ExecutionPublication.Read(before, before, session.Messages(texts, before.World), texts, content, facts, presentedFeedback: plan.PresentedFeedback);
        Assert.Empty(remaining.Messages); Assert.Empty(remaining.Records);
    }

    [Fact]
    public void RecruitmentDialogueAndRecordsUseTheSameOccurrenceStatus()
    {
        var (session, texts, content) = Fixture();
        var clock = session.Snapshot.Clock;
        var before = new StrategySnapshot(MissionScenario.Create(), session.Snapshot with
        { Simulation = session.Snapshot.Simulation with { Tasks = [new(Guid.NewGuid(), "personnel-recruit", "player", 1, 2, 1, clock, clock, 0, 0)] } });
        string[] roles = ["employ-meeting", "captive-failure", "employ-result-failure"];
        texts = texts with { Messages = texts.Messages.Concat(roles.Where(r => !texts.Messages.ContainsKey(r))
            .Select(r => KeyValuePair.Create(r, new MessageDefinition([new MessageLiteral(r)])))).ToDictionary() };
        content = content with { Personnel = new(roles.ToDictionary(r => r, r => new DecisionMessageDefinition(r, []))) };
        DomainEvent[] facts = [new RecruitmentResolvedEvent(clock, 2, 22, 1, 99, false)
            { Occurrence = new(1, OfficerStatus.Captive, new(2, 1, null), false, false, 1) }];

        var output = ExecutionPublication.Read(before, before, session.Messages(texts, before.World), texts, content, facts);
        Assert.Equal(roles, output.Messages.Select(m => m.Message.Key));
        Assert.Contains(output.Records, r => r.Message.Key == "recruit-meeting");
        var plan = BattlePresentationPlan.Create(before, before, facts, session.Messages(texts, before.World), texts, content);
        Assert.Equal(roles, plan.Messages.Select(m => m.Message.Message.Key));
    }

    [Fact]
    public void PracticeAndArrivalKeepDifferentFeedbackOwnership()
    {
        var (session, texts, content) = Fixture(); var before = session.View; var clock = before.World.Clock;
        DomainEvent[] facts = [new TacticLearnedEvent(clock, 2, 1) { Origin = TacticLearningOrigin.Practice, Occurrence = new(1, 1, null) },
            new TacticLearnedEvent(clock, 2, 2) { Origin = TacticLearningOrigin.Grant, Occurrence = new(1, 1, null) },
            new OfficerAppearedEvent(clock, 22, OfficerStatus.Undiscovered, null, null, 2, null),
            new OfficerAppearedEvent(clock, 22, OfficerStatus.Serving, 1, 1, 1, null)];
        var output = ExecutionPublication.Read(before, before, session.Messages(texts), texts, content, facts);
        Assert.Equal("tactic-learned", Assert.Single(output.Records).Message.Key);
        Assert.Single(output.Messages, m => m.Message.Key == "tactic-learned");
        var greeting = Assert.Single(output.Messages, m => m.Message.Key == "appearance-greeting");
        Assert.Equal("ruler", greeting.Speaker!.ForceName);
        Assert.Equal(22, greeting.SpeakerId);
        Assert.Null(greeting.MapSpeech);
        Assert.False(greeting.WaitForMapSpeech);
        Assert.Equal(22, Assert.Single(output.Messages, m => m.Message.Key == "appearance-introduction").IllustrationPersonId);
        var plan = BattlePresentationPlan.Create(before, before, facts, session.Messages(texts), texts, content);
        Assert.All(plan.Messages, m => Assert.Equal(BattleMessageChannel.Dialogue, m.Channel));
    }

    [Fact]
    public void EscapeUsesPortraitDialogueAtRetainedDetentionAndNoJournalEntry()
    {
        var (session, texts, content) = Fixture(); var before = session.View;
        var after = new StrategySnapshot(MissionScenario.Create(), before.World with
        { Settlements = before.World.Settlements.Select(s => s.Id == 1 ? s with { OwnerId = 3 } : s).ToImmutableArray() });
        DomainEvent[] facts = [new PrisonerEscapedEvent(before.World.Clock, 22, 1, null, 2)
        { Occurrence = new(2, 1, new(32, 7)), CaptorForceId = 1 }];
        var output = ExecutionPublication.Read(before, after, session.Messages(texts), texts, content, facts);
        Assert.Empty(output.Records);
        Assert.Equal(new[] { "prisoner-escape-speech", "prisoner-escape-result" }, output.Messages.Select(m => m.Message.Key));
        var speech = output.Messages[0];
        Assert.Equal(22, speech.SpeakerId);
        Assert.NotNull(speech.Speaker);
        Assert.Equal(MapCoordinates.Cell(new(32, 7)), speech.FocusLocation);
        Assert.All(output.Messages, m => { Assert.Null(m.MapSpeech); Assert.False(m.WaitForMapSpeech); });

        var plan = BattlePresentationPlan.Create(before, after, facts, session.Messages(texts), texts, content);
        Assert.Equal(output.Messages, plan.Messages.Select(m => m.Message));
        Assert.All(plan.Messages, m => Assert.Equal(BattleMessageChannel.Dialogue, m.Channel));
        var remaining = ExecutionPublication.Read(before, after, session.Messages(texts), texts, content, facts,
            presentedFeedback: plan.PresentedFeedback);
        Assert.Empty(remaining.Messages); Assert.Empty(remaining.Records);
    }

    [Fact]
    public void SeparateBattlesKeepTheirGroupsAndSuppressMemberDuplicates()
    {
        var (session, texts, content) = Fixture(); var before = session.View; var clock = before.World.Clock;
        var captor = new BattleObjectContext(10, false, 1, 3, new(32, 7), 1, false);
        var first = new BattleObjectContext(20, false, 2, 21, new(34, 7), 2, false);
        var second = first with { Id = 21, Position = new(36, 7) };
        DomainEvent[] facts = [new OfficerCapturedEvent(clock, 22, 1, null, 10),
            new BattlePersonnelResolvedEvent(clock, first, captor, [22], [], 22),
            new OfficerCapturedEvent(clock, 21, 1, null, 10),
            new BattlePersonnelResolvedEvent(clock, second, captor, [21], [], 21)];
        var plan = BattlePresentationPlan.Create(before, before, facts, session.Messages(texts), texts, content);
        Assert.Equal(2, plan.Records.Count);
        Assert.All(plan.Records, r => Assert.Equal("battle-captured", r.Record.Message.Key));
        Assert.Equal(2, plan.Messages.Count);
        Assert.Equal(2, plan.Messages.Select(m => m.Message.MapSpeech!.Location).Distinct().Count());
        var remaining = ExecutionPublication.Read(before, before, session.Messages(texts), texts, content, facts, presentedFeedback: plan.PresentedFeedback);
        Assert.Empty(remaining.Messages); Assert.Empty(remaining.Records);
        var dialogue = ExecutionPublication.Read(before, before, session.Messages(texts), texts, content, facts, executionMap: false);
        Assert.Equal(2, dialogue.Messages.Count);
        Assert.All(dialogue.Messages, m =>
        {
            Assert.Equal("battle-capture-dialogue", m.Message.Key);
            Assert.NotNull(m.Speaker); Assert.Null(m.MapSpeech); Assert.False(m.WaitForMapSpeech);
        });
    }

    [Fact]
    public void VictimAndThirdPartyStrategyResultsUseDifferentEligibilityAndLevels()
    {
        var (session, texts, content) = Fixture(); var before = session.View; var clock = before.World.Clock;
        StrategyAudience Audience(int target) => new(2, target, new(target, target, null), target == 1 ? 2 : 31, 1000, 1000, 70, 100, 90);
        DomainEvent[] facts = [new BurnResolvedEvent(clock, 22, 1, 1, 99, false, 0, 0, 0, 0)
            { Occurrence = new(2, 1, 500), Audience = Audience(1) },
            new SeizeResolvedEvent(clock, 22, 3, 0, 0, 0, 100, false, SeizeOutcomeKind.Money) { Money = 50, Audience = Audience(3) },
            new AlienateResolvedEvent(clock, 22, 2, 100, 0, true, 5, 1) { Audience = Audience(1) },
            new GossipResolvedEvent(clock, 22, 3, 1, 100, 0, true, 10, 31, false, 0) { Audience = Audience(3) }];
        var output = ExecutionPublication.Read(before, before, session.Messages(texts), texts, content, facts);
        Assert.Equal(new[] { "burn-victim-failure", "seize-money-other", "rumor-observer-success" }, output.Records.Select(r => r.Message.Key));
        Assert.Equal(MapReportImportance.Detailed, output.Records[1].Importance);
        Assert.Null(output.Records[1].ForceColorCode);
        Assert.DoesNotContain(output.Records, r => r.Message.Key.StartsWith("loyalty", StringComparison.Ordinal));
    }

    [Fact]
    public void InjuryUsesTheMapRecordOrDecisionPromptWithoutRepeatingBoth()
    {
        var (session, texts, content) = Fixture(); var before = session.View;
        content.Progress!.Feedback["injury-worsened"] = content.Progress.Feedback["injury-worsened"] with { Sound = "injury" };
        DomainEvent[] facts = [new OfficerInjuryWorsenedEvent(before.World.Clock, 2, 0, 5, new(1, 1, null))];
        var output = ExecutionPublication.Read(before, before, session.Messages(texts), texts, content, facts);
        Assert.Empty(output.Messages); Assert.Equal("injury", Assert.Single(output.Records).SoundRole);
        var prompt = ExecutionPublication.Read(before, before, session.Messages(texts), texts, content, facts, executionMap: false);
        Assert.Empty(prompt.Records); Assert.Equal("injury", Assert.Single(prompt.Messages).SoundRole);
        var awaiting = new StrategySnapshot(MissionScenario.Create(), before.World with
        { Simulation = before.World.Simulation with { Boundary = SimulationBoundary.AwaitingDecision } });
        Assert.Empty(ExecutionPublication.Read(awaiting, before, session.Messages(texts), texts, content, facts).Records);
    }

    [Fact]
    public void MoraleCollapseKeepsItsCauseAfterTheUnitDisappears()
    {
        var (session, texts, content) = Fixture(); var before = session.View;
        var unit = before.World.Units.Single(u => u.Id == 10);
        var target = new BattleObjectContext(unit.Id, false, unit.OwnerId, unit.CommanderId, unit.Position, unit.ServicePlaceId, false);
        var after = new StrategySnapshot(MissionScenario.Create(), before.World with
        {
            Units = before.World.Units.Where(u => u.Id != unit.Id).ToImmutableArray(),
            Officers = before.World.Officers.Select(p => p.UnitId == unit.Id
                ? p with { UnitId = null, SettlementId = unit.ServicePlaceId } : p).ToImmutableArray()
        });
        DomainEvent[] facts = [new UnitTerminatedEvent(before.World.Clock, unit.Id, null, 0)
        { Reason = UnitTerminationReason.MoraleCollapsed, Target = target }];
        var output = ExecutionPublication.Read(before, after, session.Messages(texts), texts, content, facts);
        var record = Assert.Single(output.Records);
        Assert.Equal("unit-morale-collapsed", record.Message.Key);
        Assert.Equal(unit.Cell, record.Location);
    }

    [Fact]
    public void HighSpiritsExpirationDoesNotAlsoReportOrdinaryRecovery()
    {
        var (session, texts, content) = Fixture(); var before = session.View;
        var unit = new BattleObjectContext(10, false, 1, 3, new(32, 7), 1, false);
        DomainEvent[] facts = [new HighSpiritsExpiredEvent(before.World.Clock, 10, 90, 60) { Unit = unit },
            new BattleConditionChangedEvent(before.World.Clock, 10, false, UnitCondition.Normal, 0)
            { Target = unit, PreviousCondition = UnitCondition.HighSpirits }];
        var output = ExecutionPublication.Read(before, before, session.Messages(texts), texts, content, facts);
        Assert.Equal(new[] { "high-spirits-expired", "morale-lost" }, output.Records.Select(r => r.Message.Key));
    }

    [Fact]
    public void HanzhongUsesItsHistoricalOutcomeWithoutAnExtraRankChronicle()
    {
        var (session, _, _) = Fixture(); var before = session.View;
        var facts = ChronicleHistory.Read(before, before,
            [new PeerageChangedEvent(before.World.Clock, 1, null, 1, PeerageChangeKind.HanzhongKing, null)]);
        Assert.Empty(facts);
    }

    [Fact]
    public async Task AuthoredOutcomeBindingsEvaluateAgainstTheOptionalRealPackage()
    {
        if (Environment.GetEnvironmentVariable("SAN9_PRESENTATION_CONTENT") is not { Length: > 0 } root) return;
        var package = await ClientContentFiles.LoadAsync(root); var session = GameSession.Start(package.Scenario, 42);
        var before = session.View; var clock = before.World.Clock; int own = before.RequirePlayerForce();
        int actor = before.World.Officers.First(p => p.IsServing && p.OwnerId == own).Id;
        int place = before.World.Officers.Single(p => p.Id == actor).SettlementId!.Value;
        var enemy = before.World.Forces.First(f => f.Id != own && !f.IsDormant && !f.IsEliminated && f.LeaderId is not null);
        var third = before.World.Forces.First(f => f.Id != own && f.Id != enemy.Id && !f.IsDormant && !f.IsEliminated && f.LeaderId is not null);
        int tactic = before.MilitaryRules!.Tactics.Keys.First();
        var victim = new StrategyAudience(enemy.Id, own, new(own, place, null), actor, 1000, 1000, 71, 150, 90);
        var other = victim with { TargetForceId = third.Id, CommanderId = third.LeaderId, Target = new(third.Id, place, null) };
        var unit = new BattleObjectContext(999, false, own, actor, new(32, 7), place, false);
        DomainEvent[] samples = [new TacticLearnedEvent(clock, actor, tactic) { Origin = TacticLearningOrigin.Practice, Occurrence = new(own, place, null) },
            new OfficerInjuryWorsenedEvent(clock, actor, 0, 5, new(own, place, null)),
            new CommanderChangedEvent(clock, place, actor) { PreviousId = enemy.LeaderId, ForceId = own },
            new UnitCommanderChangedEvent(clock, unit, enemy.LeaderId!.Value, actor),
            new CorpsLeaderChangedEvent(clock, 999, enemy.LeaderId.Value, actor) { Occurrence = new(own, place, null) },
            new CorpsDissolvedEvent(clock, own, 998, 999), new HighSpiritsExpiredEvent(clock, 999, 90, 60) { Unit = unit },
            new OfficerAppearedEvent(clock, actor, OfficerStatus.Serving, actor, own, place, null),
            new TreasuryCapturedEvent(clock, enemy.Id, null, own, null, place, true, null, place, 100, 50),
            new PrisonerEscapedEvent(clock, actor, place, null, place) { Occurrence = new(own, place, null), CaptorForceId = enemy.Id },
            new BattlePersonnelResolvedEvent(clock, unit with { ForceId = enemy.Id }, unit, [enemy.LeaderId.Value], [actor], enemy.LeaderId),
            new UnitPolicyRespondedEvent(clock, unit, UnitPolicyResponse.ConstructionAborted, null, place) { PreviousPlaceId = place },
            new UnitPolicyRespondedEvent(clock, unit, UnitPolicyResponse.TransportAborted, null, place) { PreviousPlaceId = place },
            new UnitPolicyRespondedEvent(clock, unit, UnitPolicyResponse.ReturnTargetChanged, null, place) { PreviousPlaceId = place },
            new AlienateResolvedEvent(clock, enemy.LeaderId.Value, actor, 1, 99, false, 0, own) { Audience = victim },
            new GossipResolvedEvent(clock, enemy.LeaderId.Value, third.Id, own, 100, 0, true, 10, actor, false, 0) { Audience = other },
            .. new[] { true, false }.Select(success => (DomainEvent)new BurnResolvedEvent(clock, enemy.LeaderId.Value, place, 50, success ? 0 : 99, success, 10, success ? 10u : 0u, 0, 0)
                { Audience = victim, Occurrence = new(enemy.Id, own, 490) }),
            .. new[] { victim, other }.SelectMany(audience => new[] { SeizeOutcomeKind.Money, SeizeOutcomeKind.Person, SeizeOutcomeKind.Item, SeizeOutcomeKind.Failure }
                .Select(kind => (DomainEvent)new SeizeResolvedEvent(clock, enemy.LeaderId.Value, place, 0, 1, 1, 1, true, kind)
                { Audience = audience, Money = 10, RecoveredOfficerId = actor, PreviousHolderId = actor, TreasureId = before.World.Treasures.First().Id })),
            .. Enum.GetValues<UnitStrategyKind>().SelectMany(kind => new[] { true, false }.Select(success => (DomainEvent)new UnitStrategyResolvedEvent(clock, enemy.LeaderId.Value, 999,
                kind, 50, success ? 0 : 99, success, new(new(32, 7), own, actor, place, UnitCondition.Normal, false, false))
                { Audience = victim, PreviousMorale = 70, DeliveredSoldiers = kind == UnitStrategyKind.Reinforce ? 50u : 0 }))];
        foreach (var fact in samples)
        {
            var output = ExecutionPublication.Read(before, before, session.Messages(package.Text), package.Text, package.Presentation, [fact]);
            if (fact is SeizeResolvedEvent { Outcome: SeizeOutcomeKind.Failure, Audience.TargetForceId: var target } && target != own)
            { Assert.Empty(output.Messages); Assert.Empty(output.Records); continue; }
            Assert.True(output.Messages.Count + output.Records.Count > 0, fact.GetType().Name);
            Assert.All(output.Messages, m => Assert.NotEmpty(m.Message.BbCode));
            Assert.All(output.Records, r => Assert.NotEmpty(r.Message.BbCode));
        }
    }
}

using System.Collections.Immutable;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Packages;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class ProgressFeedbackTests
{
    [Fact]
    public async Task FeedbackBindingsEvaluateAgainstTheOptionalRealClientPackage()
    {
        if (Environment.GetEnvironmentVariable("SAN9_PRESENTATION_CONTENT") is not { Length: > 0 } root) return;
        var package = await ClientContentFiles.LoadAsync(root);
        var session = GameSession.Start(package.Scenario, 42);
        var view = session.View;
        int force = view.PlayerForceId;
        int actor = view.World.Officers.First(p => p.OwnerId == force && p.IsServing).Id;
        int place = view.World.Officers.Single(p => p.Id == actor).SettlementId!.Value;
        int otherForce = view.World.Forces.First(f => f.Id != force && !f.IsDormant && !f.IsEliminated && f.LeaderId is not null).Id;
        int other = view.World.Forces.Single(f => f.Id == otherForce).LeaderId!.Value;
        var clock = view.World.Clock;
        var unit = new BattleObjectContext(999, false, force, actor, new(12, 7), place, false);
        DomainEvent[] samples = [new SecretContactReportedEvent(clock, force, actor, otherForce, new(force, place, null)),
            new OfficerAbilityGrownEvent(clock, actor, "war", 50, 51) { Occurrence = new(force, place, null) },
            new BattleConditionChangedEvent(clock, 999, false, UnitCondition.Normal, 0) { Target = unit },
            new BattleConditionChangedEvent(clock, place, true, UnitCondition.Normal, 0) { Target = new(place, true, force, null, null, place, true) },
            new BurnResolvedEvent(clock, actor, place, 100, 0, true, 25, 0, 0, 0) { Occurrence = new(force, force, 475) },
            new RecruitmentResolvedEvent(clock, actor, other, 100, 0, true) { Occurrence = new(force, OfficerStatus.Captive, new(otherForce, place, null), false, false, force) },
            .. Enum.GetValues<UnitPolicyResponse>().Select(response => (DomainEvent)new UnitPolicyRespondedEvent(clock, unit, response, other, place))];
        foreach (var fact in samples)
        {
            // Independent binding samples; no fabricated domain chronology.
            var output = ExecutionPublication.Read(view, view, session.Messages(package.Text), package.Text, package.Presentation,
                [fact], executionMap: false);
            if (fact is OfficerAbilityGrownEvent) Assert.Empty(output.Records);
            else Assert.NotEmpty(output.Records);
            Assert.All(output.Records, r => Assert.False(string.IsNullOrWhiteSpace(r.Message.BbCode)));
            Assert.All(output.Messages, m => Assert.False(string.IsNullOrWhiteSpace(m.Message.BbCode)));
            if (fact is OfficerAbilityGrownEvent)
            {
                var prompt = Assert.Single(output.Messages);
                Assert.Equal("messages.7233", prompt.Message.Key);
                Assert.Contains("51", prompt.Message.BbCode);
                Assert.Equal("decision-increase", prompt.SoundRole);
            }
        }
    }

    private static (TextCatalog Texts, CommandPresentation Content) Content(StrategySnapshot view)
    {
        var roles = ProgressPresentationDefinition.RecordRoles.Concat(ProgressPresentationDefinition.MapSpeechRoles)
            .Append("growth-prompt").Distinct();
        var texts = MissionScenario.Texts(view) with { Messages = roles.ToDictionary(r => r, r => new MessageDefinition([new MessageLiteral(r)])) };
        var progress = new ProgressPresentationDefinition(ProgressPresentationDefinition.RecordRoles.ToDictionary(r => r,
            r => new DecisionMessageDefinition(r, [])), [])
        {
            Feedback = new() { ["ability-grown"] = new("growth-prompt", [], Sound: "growth") },
            MapSpeeches = ProgressPresentationDefinition.MapSpeechRoles.ToDictionary(r => r, r => new DecisionMessageDefinition(r, [], "actor"))
        };
        return (texts, CommandPresentation.Empty with { Progress = progress });
    }

    [Fact]
    public void TimedFeedbackKeepsOccurrenceLocationAndIsNotPublishedAgainAfterBattlePlayback()
    {
        var session = GameSession.Start(MissionScenario.Create(), 42);
        var before = session.View;
        var (texts, content) = Content(before);
        var unit = new BattleObjectContext(10, false, 1, 3, new(32, 7), 1, false);
        DomainEvent[] facts = [new SecretContactReportedEvent(before.World.Clock, 1, 2, 3, new(1, null, new(27, 7))),
            new OfficerAbilityGrownEvent(before.World.Clock, 3, "war", 50, 51) { Occurrence = new(1, null, new(32, 7)) },
            new BattleConditionChangedEvent(before.World.Clock, 10, false, UnitCondition.Normal, 0) { Target = unit },
            new UnitPolicyRespondedEvent(before.World.Clock, unit, UnitPolicyResponse.PursueOrdered, 23, 1)];
        var after = new StrategySnapshot(MissionScenario.Create(), before.World with
        { Units = before.World.Units.Select(u => u.Id == 10 ? u with { Position = new(92, 7) } : u).ToImmutableArray() });
        var plan = BattlePresentationPlan.Create(before, after, facts, session.Messages(texts), texts, content);
        Assert.Equal(new[] { "secret-contact", "ability-grown", "unit-recovered", "policy-pursue-ordered" }, plan.Records.Select(r => r.Record.Message.Key));
        Assert.Equal(MapCoordinates.Cell(new(27, 7)), plan.Records[0].Record.Location);
        Assert.All(plan.Records.Skip(1), r => Assert.Equal(MapCoordinates.Cell(new(32, 7)), r.Record.Location));
        Assert.Single(plan.Messages, m => m.Message.MapSpeech is not null);
        var rest = ExecutionPublication.Read(before, after, session.Messages(texts), texts, content, facts, presentedFacts: plan.PresentedFacts);
        Assert.Empty(rest.Records); Assert.Empty(rest.Messages);
        var prompt = ExecutionPublication.Read(before, after, session.Messages(texts), texts, content, [facts[1]], executionMap: false);
        Assert.Equal("growth-prompt", Assert.Single(prompt.Messages).Message.Key);
        Assert.Empty(prompt.Records);
        var awaiting = new StrategySnapshot(MissionScenario.Create(), before.World with
        { Simulation = before.World.Simulation with { Boundary = SimulationBoundary.AwaitingDecision } });
        Assert.Empty(ExecutionPublication.Read(awaiting, after, session.Messages(texts), texts, content, [facts[1]]).Records);
        Assert.Empty(ExecutionPublication.Read(before, after, session.Messages(texts), texts, content, [facts[1]]).Messages);
    }

    [Fact]
    public void AnonymousBurnAndDurabilityRemainDetailedAndUseOccurrenceNumbers()
    {
        var session = GameSession.Start(MissionScenario.Create(), 42);
        var view = session.View;
        var (texts, content) = Content(view);
        var burn = new BurnResolvedEvent(view.World.Clock, 22, 3, 100, 0, true, 50, 0, 0, 0) { Occurrence = new(2, 3, 450) };
        var output = ExecutionPublication.Read(view, view, session.Messages(texts), texts, content, [burn]);
        Assert.Equal(new[] { "burn-other", "burn-durability" }, output.Records.Select(r => r.Message.Key));
        Assert.All(output.Records, r => { Assert.Equal(MapReportImportance.Detailed, r.Importance); Assert.Null(r.ForceColorCode); });
        Assert.Empty(output.Messages);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecruitingPlayerOfficerReportsMeetingAndLossUsingPreTransferIdentity(bool success)
    {
        var session = GameSession.Start(MissionScenario.Create(), 42);
        var view = session.View;
        var (texts, content) = Content(view);
        var result = new RecruitmentResolvedEvent(view.World.Clock, 22, 2, 50, success ? 0 : 99, success)
        { Occurrence = new(2, OfficerStatus.Serving, new(1, 1, null), false, false, null) };
        var output = ExecutionPublication.Read(view, view, session.Messages(texts), texts, content,
            [new SecretContactReportedEvent(view.World.Clock, 1, 2, 2, result.Occurrence.Target), result]);
        Assert.Equal(success ? new[] { "secret-contact", "recruit-lost" } : new[] { "secret-contact" }, output.Records.Select(r => r.Message.Key));
        Assert.All(output.Records, r => Assert.Equal(1, r.ForceColorCode));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FoundingIsPublicAndRetainsNewForceColor(bool independence)
    {
        var session = GameSession.Start(MissionScenario.Create(), 42);
        var view = session.View;
        DomainEvent fact = independence ? new CityIndependenceEvent(view.World.Clock, 2, 1, 2, 22, []) : new ForceFoundedEvent(view.World.Clock, 2, 2, 22, []);
        var facts = ChronicleHistory.Read(view, view, [fact]);
        Assert.Equal(new[] { 1, 2, 3 }, facts.Select(f => f.ForceId));
        Assert.All(facts, f => { Assert.Equal(2, f.ForceColorCode); Assert.Equal(22, f.Values["actor"].Entity!.Id); Assert.Equal(2, f.Values["place"].Entity!.Id); });
    }
}

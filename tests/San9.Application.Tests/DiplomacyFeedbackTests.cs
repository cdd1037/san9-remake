using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class DiplomacyFeedbackTests
{
    [Theory]
    [InlineData("gift", 65)]
    [InlineData("surrender", 65)]
    [InlineData("request", 65)]
    [InlineData("return", 65)]
    [InlineData("surrender", 0)]
    public void RefusalPublishesOutcomeBeforeItsActualRelationConsequence(string kind, int initial)
    {
        var scenario = MissionScenario.Create();
        scenario = scenario with { Information = scenario.Information with { Relations = [new(1, 2, initial)] },
            Officers = [.. scenario.Officers, new("captive", new(13, 2, 1, new(50, 50, 50, 50), false)
                { Status = OfficerStatus.Captive })] };
        var session = GameSession.Start(scenario, 42);
        session.EndControl(new("player"));
        GameCommand command = kind switch
        {
            "gift" => new GiftCommand(Guid.NewGuid(), 2, 0, 22, 1, new(GiftKind.Money, 1000, null)),
            "surrender" => new DemandSurrenderCommand(Guid.NewGuid(), 2, 0, 22, 1),
            "request" => new RequestAttackCommand(Guid.NewGuid(), 2, 0, 22, 1, 3),
            _ => new ReturnPrisonerCommand(Guid.NewGuid(), 2, 0, 22, 1, 13)
        };
        Assert.True(session.Execute(new("ai:2"), command).Submission.Accepted);
        for (int i = 0; i < 4 && session.Snapshot.Simulation.PendingDailyTask is null; i++) session.AdvanceXun();
        Assert.NotNull(session.Snapshot.Simulation.PendingDailyTask);
        var before = session.View;
        var answer = new RespondDailyTaskCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, command.CommandId, 0, false);
        var result = session.Execute(new("player"), answer);
        Assert.True(result.Submission.Accepted);
        var facts = result.Events.ToList();
        int outcome = facts.FindIndex(e => e is GiftResolvedEvent or SurrenderResolvedEvent or AttackRequestResolvedEvent or PrisonerReturnResolvedEvent);
        Assert.True(outcome >= 0);
        if (initial == 0)
        {
            Assert.Empty(result.Events.OfType<ForceRelationChangedEvent>());
            return;
        }
        var relation = Assert.Single(result.Events.OfType<ForceRelationChangedEvent>());
        Assert.True(relation.Notify);
        Assert.Equal(kind == "surrender" ? 39 : 60, relation.Current);
        Assert.True(outcome < facts.IndexOf(relation));
        var (texts, content) = Presentation(before);
        var output = ExecutionPublication.Read(before, session.View, session.Messages(texts, before.World), texts, content, result.Events);
        var report = Assert.Single(output.Records, r => r.Message.Key.StartsWith("record.relation-", StringComparison.Ordinal));
        Assert.Equal(kind == "surrender" ? "record.relation-worsened-band" : "record.relation-worsened", report.Message.Key);
    }

    [Theory]
    [InlineData(65, 60, "worsened", "relation.friendly")]
    [InlineData(60, 55, "worsened-band", "relation.neutral")]
    [InlineData(80, 39, "worsened-band", "relation.unfriendly")]
    [InlineData(60, 65, "improved", "relation.friendly")]
    [InlineData(55, 65, "improved-band", "relation.friendly")]
    public void RelationFeedbackUsesActualBandsAndChoosesOneChannel(int previous, int current, string role, string band)
    {
        var session = GameSession.Start(MissionScenario.Create(), 42);
        var (texts, content) = Presentation(session.View);
        DomainEvent[] facts = [new ForceRelationChangedEvent(session.Snapshot.Clock, 1, 2, previous, current, true)];
        ExecutionOutput Read(bool map, IReadOnlyList<DomainEvent> events) => ExecutionPublication.Read(session.View, session.View,
            session.Messages(texts), texts, content, events, executionMap: map);
        var map = Read(true, facts);
        Assert.Empty(map.Messages);
        Assert.Equal("record.relation-" + role, Assert.Single(map.Records).Message.Key);
        Assert.Contains(band, map.Records[0].Message.BbCode);
        var modal = Read(false, facts);
        Assert.Empty(modal.Records);
        Assert.Equal("prompt.relation-" + role, Assert.Single(modal.Messages).Message.Key);
        Assert.False(modal.Messages[0].DiplomacyDialogue);
        Assert.Equal(current > previous ? "decision-increase" : "decision-decrease", modal.Messages[0].SoundRole);
    }

    [Theory]
    [InlineData(1, 2, 65, 60, false)]
    [InlineData(2, 3, 65, 60, true)]
    [InlineData(1, 2, 0, 0, true)]
    public void SilentRelationChangesPublishNeitherChannel(int first, int second, int previous, int current, bool notify)
    {
        var session = GameSession.Start(MissionScenario.Create(), 42);
        var (texts, content) = Presentation(session.View);
        var fact = new ForceRelationChangedEvent(session.Snapshot.Clock, first, second, previous, current, notify);
        foreach (bool map in new[] { true, false })
        {
            var output = ExecutionPublication.Read(session.View, session.View, session.Messages(texts), texts, content, [fact], executionMap: map);
            Assert.Empty(output.Messages);
            Assert.Empty(output.Records);
        }
    }

    internal static (TextCatalog Texts, CommandPresentation Content) Presentation(StrategySnapshot view)
    {
        var texts = MissionScenario.Texts(view);
        var progress = new ProgressPresentationDefinition([], []) { Feedback = [] };
        foreach (string role in ProgressPresentationDefinition.RelationRoles)
        {
            Dictionary<string, string> bindings = new() { ["string.0"] = "relation" };
            foreach (string channel in new[] { "record", "prompt" })
            {
                string key = channel + "." + role;
                texts.Messages[key] = new([new MessageLiteral(key), new MessageValue(new("binding", "string.0"))]);
                (channel == "record" ? progress.Records : progress.Feedback)[role] = new(key, bindings,
                    Sound: channel == "record" ? null : role.Contains("improved", StringComparison.Ordinal) ? "decision-increase" : "decision-decrease");
            }
        }
        return (texts, CommandPresentation.Empty with { Progress = progress });
    }
}

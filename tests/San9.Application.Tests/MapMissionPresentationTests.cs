using San9.Tests;
using System.Collections.Immutable;
using San9.Application.Persistence;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class MapMissionPresentationTests
{
    [Theory]
    [InlineData(true,UnitCondition.Normal,false,false,"false-report-reaction-success")]
    [InlineData(false,UnitCondition.Normal,false,false,"false-report-reaction-failure")]
    [InlineData(false,UnitCondition.HighSpirits,true,true,"strategy-reaction-united")]
    [InlineData(false,UnitCondition.Normal,true,true,"strategy-reaction-countered")]
    [InlineData(false,UnitCondition.Normal,true,false,"false-report-reaction-defensive")]
    public void MapChannelsUseCapturedTargetAndOriginalReactionPriority(bool success,UnitCondition condition,
        bool defensive,bool countered,string reaction)
    {
        var session = GameSession.Start(MissionScenario.Create(),42);
        var draft = new MissionDraft(session,1,"false-report");
        draft.SetActor(2); draft.SetTarget(20);
        Assert.True(session.Execute(new("player"),draft.CreateCommand()).Submission.Accepted);
        var before = session.View;
        // The prior frame has another commander and position: neither is the occurrence context.
        before = new(MissionScenario.Create(),before.World with { Units = before.World.Units.Select(u => u.Id == 20
            ? u with { CommanderId = 21, Position = new(7,7) } : u).ToImmutableArray() });
        string[] roles = ["false-report-action",reaction,success ? "false-report-success" : "false-report-failure"];
        var texts = MissionScenario.Texts(before) with { Messages = roles.Distinct().ToDictionary(role => role,
            role => new MessageDefinition([new MessageLiteral(role)])) };
        // Authored results must not evaluate the unused fallback summary.
        texts.Entries.Remove(CommandText.Key("result.unit-strategy"));
        texts.Entries.Remove(CommandText.Key("title.false-report"));
        var definitions = roles.Distinct().ToDictionary(role => role,role => new DecisionMessageDefinition(role,[],
            role.EndsWith("-action",StringComparison.Ordinal) ? "actor" : role == roles[2] ? null : "target-person"));
        var target = new UnitStrategyTarget(new(67,12),2,23,2,condition,defensive,countered);
        var clock = before.World.Clock.Advance();
        DomainEvent[] facts = [new UnitStrategyResolvedEvent(clock,2,20,UnitStrategyKind.FalseReport,50,success ? 0 : 99,success,target)];
        ExecutionOutput Read() => MissionPresentation.Results(before,texts,facts,
            content: definitions,context: session.Messages(texts,before.World));
        var messages = Read();
        Assert.Equal(2,messages.Messages.Count);
        var action = Assert.IsType<MapSpeechMessage>(messages.Messages[0].MapSpeech);
        var response = Assert.IsType<MapSpeechMessage>(messages.Messages[1].MapSpeech);
        var record = Assert.Single(messages.Records);
        Assert.Equal(MapReportImportance.Summary, record.Importance);
        Assert.Equal(2,action.SpeakerId);
        Assert.Equal(23,response.SpeakerId);
        Assert.Equal("enemy-commander",response.SpeakerName);
        Assert.Equal(0,action.DelaySeconds);
        Assert.Equal(1,response.DelaySeconds);
        Assert.Equal(reaction,response.Message.Key);
        Assert.Equal(MapCoordinates.Cell(target.Position),record.Location);
        Assert.Equal(record.Location,response.Location);
        Assert.Equal(20,response.UnitId);
        Assert.Equal(clock,response.Message.Clock);
        Assert.Equal(clock,record.Message.Clock);
        var repeated = Read();
        Assert.Equal(messages.Messages, repeated.Messages);
        Assert.Equal(messages.Records, repeated.Records);
        Assert.NotEqual(action.Id, response.Id);
        Assert.NotEqual(action.Id, record.Id);
        Assert.NotEqual(response.Id, record.Id);
        var content = CommandPresentation.Empty with { Missions = definitions };
        var plan = BattlePresentationPlan.Create(before, before, facts, session.Messages(texts,before.World), texts, content);
        Assert.Equal(record, Assert.Single(plan.Records).Record);
        Assert.Equal(messages.Messages.Select(m => m.MapSpeech!.Id), plan.Messages.Select(c => c.Id));
        Assert.All(plan.Messages, c => Assert.Equal(plan.Records[0].Time, c.Time));
        Assert.Equal(new double[] { 0, 1 }, plan.Messages.Select(c => c.Message.MapSpeech!.DelaySeconds));
        Assert.Equal(plan.Records[0].Time, plan.Duration);
        var unpublished = ExecutionPublication.Read(before, before, session.Messages(texts,before.World), texts, content, facts,
            presentedFeedback: plan.PresentedFeedback);
        Assert.Empty(unpublished.Messages);
        Assert.Empty(unpublished.Records);

        // Claiming dialogue must not swallow a different channel from the same occurrence.
        var publication = ExecutionPublication.Read(before, before, session.Messages(texts, before.World), texts, content, facts);
        var messageIds = publication.FeedbackIds.Where(id => id.Contains(":message:", StringComparison.Ordinal)).ToHashSet();
        var recordsOnly = ExecutionPublication.Read(before, before, session.Messages(texts, before.World), texts, content, facts,
            presentedFeedback: messageIds);
        Assert.Empty(recordsOnly.Messages);
        Assert.Equal(publication.Records, recordsOnly.Records);
        Assert.Empty(ExecutionPublication.Read(before, before, session.Messages(texts, before.World), texts, content, facts,
            presentedFeedback: messageIds.Concat(recordsOnly.FeedbackIds).ToHashSet()).Records);

        // Authored result media still plays, independently of the execution record.
        definitions[roles[2]] = definitions[roles[2]] with { Sound = "result-sound" };
        var withMedia = Read();
        var notification = Assert.Single(withMedia.Messages, m => m.SoundRole == "result-sound");
        Assert.Null(notification.MapSpeech);
        Assert.Equal(record, Assert.Single(withMedia.Records));
        Assert.Equal(record.Message, notification.Message);
    }

    [Fact]
    public void RealMissionProducesTargetEventsAndSurvivesSave()
    {
        var content = MissionScenario.Create();
        var session = GameSession.Start(content,42);
        var draft = new MissionDraft(session,1,"false-report");
        draft.SetActor(2); draft.SetTarget(20);
        Assert.True(session.Execute(new("player"),draft.CreateCommand()).Submission.Accepted);
        var result = session.AdvanceXun();
        Assert.NotEmpty(result.Events.OfType<UnitStrategyResolvedEvent>());
        var restored = SaveService.Decode(SaveService.Encode(session), content);
        StateAssert.Equal(session.Capture(), restored.Capture());
    }
}

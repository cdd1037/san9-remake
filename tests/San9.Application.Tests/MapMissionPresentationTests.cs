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
        var definitions = roles.Distinct().ToDictionary(role => role,role => new DecisionMessageDefinition(role,[],
            role.EndsWith("-action",StringComparison.Ordinal) ? "actor" : role == roles[2] ? null : "target-person"));
        var target = new UnitStrategyTarget(new(67,12),2,23,2,condition,defensive,countered);
        var clock = before.World.Clock.Advance();
        var messages = MissionPresentation.Results(before,texts,
            [new UnitStrategyResolvedEvent(clock,2,20,UnitStrategyKind.FalseReport,50,success ? 0 : 99,success,target)],
            content: definitions,context: session.Messages(texts,before.World));
        Assert.Equal(3,messages.Count);
        var action = Assert.IsType<MapSpeechMessage>(messages[0].MapSpeech);
        var response = Assert.IsType<MapSpeechMessage>(messages[1].MapSpeech);
        var record = Assert.IsType<MapMessageRecord>(messages[2].MapRecord);
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
        Assert.Null(messages[1].MapRecord);
        Assert.Null(messages[2].MapSpeech);
    }

    [Fact]
    public void RealMissionRecordingReplaysCapturedTargetsWithoutChangingSaveProtocol()
    {
        var content = MissionScenario.Create();
        var session = GameSession.Start(content,42);
        session.BeginRecording();
        var draft = new MissionDraft(session,1,"false-report");
        draft.SetActor(2); draft.SetTarget(20);
        Assert.True(session.Execute(new("player"),draft.CreateCommand()).Submission.Accepted);
        var result = session.AdvanceXun();
        Assert.NotEmpty(result.Events.OfType<UnitStrategyResolvedEvent>());
        string path = Path.Combine(Path.GetTempPath(),$"san9-map-mission-{Guid.NewGuid():N}.recording.json");
        RecordingReplay replay;
        try
        {
            RecordingService.Write(path,session.CaptureRecording());
            replay = RecordingService.Replay(path,content);
        }
        finally { File.Delete(path); }
        Assert.Equal(session.Snapshot.StateHash,replay.Session.Snapshot.StateHash);
        Assert.Equal(result.Events.OfType<UnitStrategyResolvedEvent>().Select(e => e.Target),
            replay.Events.OfType<UnitStrategyResolvedEvent>().Select(e => e.Target));
    }
}

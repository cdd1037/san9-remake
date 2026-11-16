using San9.Application.Presentation;
using San9.Core.Simulation;

namespace San9.Client.Godot.Tests;

public sealed class ExecutionOutputPlaybackTests
{
    [Fact]
    public void ConsequencesWaitForTheirNarrationAndKeepRecordOnlyStepsInOrder()
    {
        var clock = new GameClock(190, 1, 1);
        CommandMessage Message(string key) => new(new(key, clock, key), null, null);
        MapMessageRecord Record(string key) => new(key, new(key, clock, key), null, null);
        var output = new ExecutionOutput([
            new([], [Record("earlier")]),
            new([Message("meeting"), Message("accepted")], [Record("accepted-record")]),
            new([], [Record("growth")]),
            new([Message("withdrawal")], [Record("withdrawal-record")])]);
        List<string> published = [];
        int finished = 0;
        Action? complete = null;
        ExecutionOutputPlayback.Present(output, (messages, completed) =>
        {
            published.AddRange(messages.Select(m => m.Message.Key));
            complete = completed;
        }, record => published.Add(record.Id), () => finished++);
        Assert.Equal(["earlier", "meeting", "accepted"], published);
        Assert.Equal(0, finished);
        complete!();
        Assert.Equal(["earlier", "meeting", "accepted", "accepted-record", "growth", "withdrawal"], published);
        complete!();
        Assert.Equal("withdrawal-record", published[^1]);
        Assert.Equal(7, published.Count);
        Assert.Equal(1, finished);
    }

    [Fact]
    public void SynchronouslySkippedScenesContinueWithoutGrowingTheCallStack()
    {
        var clock = new GameClock(190, 1, 1);
        var message = new CommandMessage(new("meeting", clock, "meeting"), null, null);
        var record = new MapMessageRecord("result", new("result", clock, "result"));
        var output = new ExecutionOutput(Enumerable.Range(0, 10000)
            .Select(_ => new ExecutionOutputStep([message], [record])).ToArray());
        int records = 0;
        int finished = 0;
        ExecutionOutputPlayback.Present(output, (_, completed) => completed(), _ => records++, () => finished++);
        Assert.Equal(10000, records);
        Assert.Equal(1, finished);
    }
}

namespace San9.Client.Godot.Tests;

public sealed class PresentationCueTimelineTests
{
    private sealed record Cue(string Id, double Time, int Fact, string? After = null);

    [Fact]
    public void BlockedEarlierCuesDoNotHideLaterReadyCuesAndRetainOccurrenceOrderWhenReleased()
    {
        Cue[] cues = [new("late-time", 3, 1), new("fact-gated", 0, 5), new("dialogue-gated", 0, 1, "dialogue"), new("ready", 1, 1)];
        var timeline = new PresentationCueTimeline<Cue>(cues, c => c.Time, c => c.Fact, c => c.After);
        var completed = new HashSet<string>();
        var first = timeline.Read(1, 1, completed);
        Assert.Equal(["ready"], first.Select(c => c.Id));
        Assert.Same(first, timeline.Read(2, 1, completed));
        completed.Add("dialogue");
        var released = timeline.Read(2, 1, completed);
        Assert.Equal(["dialogue-gated", "ready"], released.Select(c => c.Id));
        Assert.Equal(["late-time", "fact-gated", "dialogue-gated", "ready"], timeline.Read(3, 5, completed).Select(c => c.Id));
        // Consumers can retain an earlier result while later cues are admitted.
        Assert.Equal(["ready"], first.Select(c => c.Id));
        Assert.Equal(["dialogue-gated", "ready"], released.Select(c => c.Id));
    }

    [Fact]
    public void CompletedDialoguesDisappearAndSkippedFutureDialoguesNeverBecomePending()
    {
        Cue[] cues = [new("first", 0, 1), new("response", 0, 1, "first"), new("skipped", 2, 1)];
        var timeline = new PresentationCueTimeline<Cue>(cues, c => c.Time, c => c.Fact, c => c.After, c => c.Id);
        var completed = new HashSet<string>();
        var pending = timeline.Read(0, 1, completed);
        Assert.Equal(["first"], pending.Select(c => c.Id));
        completed.UnionWith(["first", "skipped"]);
        Assert.Equal(["response"], timeline.Read(0, 1, completed).Select(c => c.Id));
        completed.Add("response");
        Assert.Empty(timeline.Read(10, 1, completed));
        Assert.Equal(["first"], pending.Select(c => c.Id));
    }
}

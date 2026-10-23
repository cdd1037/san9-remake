using San9.Application.Presentation;

namespace San9.Client.Godot.Tests;

public sealed class ScenarioPreparationTests
{
    private static ScenarioForceSelection Selection() => new("标题", 190, 1, [], []);

    [Fact]
    public async Task BackgroundPreparationAndRepeatedClicksShareOneTaskAndResult()
    {
        int loads = 0;
        var pending = new TaskCompletionSource<ScenarioForceSelection>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var preparation = new ScenarioPreparation(token => { loads++; return pending.Task; });
        Assert.Equal(0, loads);
        var background = preparation.PrepareAsync();
        Assert.Same(background, preparation.PrepareAsync());
        var result = Selection(); pending.SetResult(result);
        Assert.Same(result, await background);
        Assert.Same(background, preparation.PrepareAsync());
        Assert.Equal(1, loads);
    }

    [Fact]
    public async Task FailedPreparationCanRetryWithoutReplacingAHealthyTask()
    {
        int loads = 0;
        using var preparation = new ScenarioPreparation(token => ++loads == 1
            ? Task.FromException<ScenarioForceSelection>(new IOException("unreadable scenario"))
            : Task.FromResult(Selection()));
        var failed = preparation.PrepareAsync();
        await Assert.ThrowsAsync<IOException>(() => failed);
        var retry = preparation.PrepareAsync();
        Assert.NotSame(failed, retry);
        await retry;
        Assert.Same(retry, preparation.PrepareAsync());
        Assert.Equal(2, loads);
    }

    [Fact]
    public async Task LeavingPageCancelsPreparationAndRejectsLateResultsEvenIfReaderIgnoresCancellation()
    {
        CancellationToken readerToken = default;
        var pending = new TaskCompletionSource<ScenarioForceSelection>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var preparation = new ScenarioPreparation(token => { readerToken = token; return pending.Task; });
        var task = preparation.PrepareAsync();
        preparation.Dispose();
        preparation.Dispose();
        Assert.True(readerToken.IsCancellationRequested);
        pending.SetResult(Selection());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Throws<ObjectDisposedException>(() => { _ = preparation.PrepareAsync(); });
    }
}

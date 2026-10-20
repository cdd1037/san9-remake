using San9.Application.Persistence;
using San9.Client.Godot.Persistence;

namespace San9.Client.Godot.Tests;

public sealed class BackgroundSaveQueueTests
{
    [Fact]
    public async Task EncodingFailureDoesNotPreventLaterRequestsFromRunning()
    {
        var queue = new BackgroundSaveQueue();
        var failed = queue.Enqueue(() => throw new System.Text.Json.JsonException("Injected encoding failure"));
        bool written = false;
        var next = queue.Enqueue(() => written = true);
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => failed);
        await next;
        Assert.True(written);
    }

    [Fact]
    public async Task EncodingRunsOffTheOwnerThreadAndAFailedRequestDoesNotBlockTheNextWrite()
    {
        var queue = new BackgroundSaveQueue();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Task? first = null;
        int ownerThread = 0, workerThread = 0;
        var owner = new Thread(() =>
        {
            ownerThread = Environment.CurrentManagedThreadId;
            first = queue.Enqueue(() =>
            {
                workerThread = Environment.CurrentManagedThreadId;
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
                throw new SaveException(SaveFailure.IoFailure, "Injected write failure");
            });
        });
        owner.Start();
        Assert.True(owner.Join(TimeSpan.FromSeconds(10)));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        Assert.NotEqual(ownerThread, workerThread);
        bool secondFinished = false;
        var second = queue.Enqueue(() => secondFinished = true);
        Assert.False(secondFinished);
        release.Set();
        await Assert.ThrowsAsync<SaveException>(() => first!);
        await second;
        Assert.True(secondFinished);
    }
}

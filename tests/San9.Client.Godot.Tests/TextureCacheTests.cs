using San9.Content.Images;
using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class TextureCacheTests
{
    private sealed record Gpu(int Value);
    private static ImageFile File(string name, int width = 1, int height = 1) => new(name, width, height, 4);
    private static PngImage Pixel(byte value = 7) => new(1, 1, 4, [value, 0, 0, 255]);
    private static void Until(TextureCache<Gpu> cache, Func<bool> condition)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            cache.Pump();
            if (watch.Elapsed.TotalSeconds > 5) throw new TimeoutException("Texture cache did not settle.");
            Thread.Sleep(1);
        }
    }
    [Fact]
    public void QueuedDemandOvertakesPrefetchAndCancelledEntriesNeverDecode()
    {
        using var firstGate = new ManualResetEventSlim();
        using var secondGate = new ManualResetEventSlim();
        using var entered = new CountdownEvent(2);
        using var finished = new CountdownEvent(2);
        var decoded = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var cache = new TextureCache<Gpu>((file, token) =>
        {
            if (file.Path is "first" or "second")
            {
                entered.Signal();
                (file.Path == "first" ? firstGate : secondGate).Wait(token);
            }
            else { decoded.Enqueue(file.Path); finished.Signal(); }
            return Pixel();
        }, (_, _) => new(1), _ => { });
        using var blockers = cache.Acquire([File("first"), File("second")]);
        cache.Pump();
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            using var low = cache.Acquire([File("low")], true);
            using var promoted = cache.Acquire([File("promoted")], true);
            var cancelled = cache.Acquire([File("cancelled")], true);
            cache.Pump();
            cancelled.Dispose();
            using var demand = cache.Acquire([File("promoted")]);
            firstGate.Set();
            Assert.True(finished.Wait(TimeSpan.FromSeconds(5)));
            Assert.Equal(new[] { "promoted", "low" }, decoded.ToArray());
            secondGate.Set();
            Until(cache, () => demand.IsReady && low.IsReady && cache.PendingBytes == 0);
            Assert.False(cache.Contains(File("cancelled")));
        }
        finally { firstGate.Set(); secondGate.Set(); }
    }
    [Fact]
    public void SharedRequestsUploadOnceAndReleaseDecodedQueue()
    {
        int reads = 0, uploads = 0;
        using var cache = new TextureCache<Gpu>((_, _) => { Interlocked.Increment(ref reads); return Pixel(); },
            (_, p) => { uploads++; return new(p.Pixels[0]); }, _ => { });
        using var first = cache.Acquire([File("page")]); using var second = cache.Acquire([File("page")]);
        Until(cache, () => first.IsReady && second.IsReady);
        Assert.Equal(1, reads); Assert.Equal(1, uploads); Assert.Equal(4, cache.PinnedBytes);
        Assert.Equal(0, cache.PendingBytes); Assert.Equal(7, cache.Get(File("page")).Value);
    }
    [Fact]
    public void GpuCacheDoesNotRetainDecodedPixelsAfterUpload()
    {
        WeakReference<byte[]>? decoded = null;
        using var cache = new TextureCache<Gpu>((_, _) => { var image = Pixel(); decoded = new(image.Pixels); return image; }, (_, p) => new(p.Pixels[0]), _ => { });
        using var lease = cache.Acquire([File("portrait")]); Until(cache, () => lease.IsReady);
        Until(cache, () => { GC.Collect(); GC.WaitForPendingFinalizers(); return !decoded!.TryGetTarget(out _); });
        Assert.True(lease.IsReady); Assert.Equal(0, cache.PendingBytes);
    }
    [Fact]
    public void SharedUserSurvivesCancellationAndLastUserCancelsWorker()
    {
        using var gate = new ManualResetEventSlim();
        int uploads = 0;
        using var cache = new TextureCache<Gpu>((_, token) => { gate.Wait(token); return Pixel(); }, (_, _) => { uploads++; return new(1); }, _ => { });
        var first = cache.Acquire([File("shared")]); using var second = cache.Acquire([File("shared")]);
        cache.Pump(); first.Dispose(); gate.Set(); Until(cache, () => second.IsReady); Assert.Equal(1, uploads);
        gate.Reset(); var cancelled = cache.Acquire([File("cancelled")]); cache.Pump(); cancelled.Dispose();
        Until(cache, () => cache.PendingBytes == 0); Assert.Equal(1, uploads); Assert.False(cache.Contains(File("cancelled")));
    }
    [Fact]
    public void FailureDoesNotRetryUntilNewRequestAndDoesNotPoisonOtherPages()
    {
        int attempts = 0;
        using var cache = new TextureCache<Gpu>((file, _) => file.Path == "bad" && Interlocked.Increment(ref attempts) == 1 ? throw new InvalidDataException("broken") : Pixel(),
            (_, _) => new(1), _ => { });
        var bad = cache.Acquire([File("bad")]); using var good = cache.Acquire([File("good")]);
        Until(cache, () => bad.Error is not null && good.IsReady);
        for (int i = 0; i < 10; i++) cache.Pump(); Assert.Equal(1, attempts);
        bad.Dispose(); using var retry = cache.Acquire([File("bad")]); Until(cache, () => retry.IsReady); Assert.Equal(2, attempts);
    }
    [Fact]
    public void ByteBudgetProtectsUsersAndEvictsIdlePages()
    {
        var released = new List<Gpu>();
        using var cache = new TextureCache<Gpu>((_, _) => Pixel(), (_, _) => new(1), released.Add, budget: 4);
        var a = cache.Acquire([File("a")]); var b = cache.Acquire([File("b")]); Until(cache, () => a.IsReady && b.IsReady);
        Assert.Equal(8, cache.Bytes); Assert.Empty(released);
        a.Dispose(); cache.Pump(); Assert.False(cache.Contains(File("a"))); Assert.True(b.IsReady);
        Assert.Single(released); b.Dispose();
    }
    [Fact]
    public void DecodeBackpressureReservesBeforeWorkersStart()
    {
        using var gate = new ManualResetEventSlim();
        using var cache = new TextureCache<Gpu>((_, token) => { gate.Wait(token); return Pixel(); }, (_, _) => new(1), _ => { });
        using var lease = cache.Acquire(Enumerable.Range(0, 8).Select(i => File(i.ToString(), 2048, 2048)));
        cache.Pump(); Assert.Equal(32L * 1024 * 1024, cache.PendingBytes);
        cache.Pump(); Assert.Equal(32L * 1024 * 1024, cache.PendingBytes);
    }
    [Fact]
    public void DisposalPreventsLateUploadEvenWhenDecoderIgnoresCancellation()
    {
        using var gate = new ManualResetEventSlim(); using var entered = new ManualResetEventSlim();
        int uploads = 0;
        var cache = new TextureCache<Gpu>((_, _) => { entered.Set(); gate.Wait(); return Pixel(); }, (_, _) => { uploads++; return new(1); }, _ => { });
        var lease = cache.Acquire([File("late")]); cache.Pump(); Assert.True(entered.Wait(3000));
        cache.Dispose(); gate.Set(); lease.Dispose(); cache.Pump(); Assert.Equal(0, uploads); Assert.Equal(0, cache.PendingBytes);
    }
    [Fact]
    public void LatestSelectionAndCloseCannotBeOverwrittenByLateCompletion()
    {
        using var gate = new ManualResetEventSlim(); using var started = new ManualResetEventSlim();
        using var cache = new TextureCache<Gpu>((file, _) => { if (file.Path == "old") { started.Set(); gate.Wait(); } return Pixel(); }, (_, _) => new(1), _ => { });
        using var selection = new ResourceSelection<TextureCache<Gpu>.Lease>();
        selection.Select("old", key => cache.Acquire([File(key)])); cache.Pump(); Assert.True(started.Wait(3000));
        selection.Select("new", key => cache.Acquire([File(key)])); Until(cache, () => selection.Current!.IsReady);
        gate.Set(); Until(cache, () => cache.PendingBytes == 0);
        Assert.Equal("new", selection.Key); Assert.False(cache.Contains(File("old")));
        selection.Dispose(); Assert.Null(selection.Current); cache.Pump(); Assert.Equal(0, cache.PinnedBytes);
    }
    [Fact]
    public void PrefetchCannotExceedBudgetButDemandMayPinMoreThanBudget()
    {
        using var cache = new TextureCache<Gpu>((_, _) => Pixel(), (_, _) => new(1), _ => { }, budget: 4);
        using var resident = cache.Acquire([File("resident")]); Until(cache, () => resident.IsReady);
        using var prefetch = cache.Acquire([File("next")], prefetch: true); cache.Pump();
        Assert.True(prefetch.IsComplete); Assert.False(prefetch.IsReady); Assert.Equal(0, cache.PendingBytes);
        using var demand = cache.Acquire([File("next")]); Until(cache, () => demand.IsReady);
        Assert.Equal(8, cache.PinnedBytes);
    }
    [Fact]
    public void ViewCommitWaitsForAllResourcesAndFailurePreservesOldView()
    {
        using var cache = new TextureCache<Gpu>((file, _) => file.Path == "broken" ? throw new InvalidDataException() : Pixel(), (_, _) => new(1), _ => { });
        var view = new MapViewportTransition(new(0, 0, 1)); view.PresentPrepared(view.Requested);
        view.Request(new(100, 100, 1)); using var target = cache.Acquire([File("building"), File("animation")]);
        Assert.False(view.TryPresent(_ => target.IsReady)); Until(cache, () => target.IsReady);
        bool terrainReady = false;
        Assert.False(view.TryPresent(_ => terrainReady && target.IsReady));
        terrainReady = true; Assert.True(view.TryPresent(_ => terrainReady && target.IsReady)); var presented = view.Presented;
        view.Request(new(200, 200, 1)); using var failed = cache.Acquire([File("broken")]); Until(cache, () => failed.Error is not null);
        Assert.False(view.TryPresent(_ => failed.IsReady)); Assert.Equal(presented, view.Presented);
    }
    [Fact]
    public void BudgetUsesRecencyAndPhysicalByteCounts()
    {
        var released = new List<int>(); using var budget = new ResourceBudget<string, int>(8, released.Add);
        budget.Add("a", 1, 4); budget.Add("b", 2, 4); _ = budget.Get("a"); budget.Add("c", 3, 4); budget.Trim();
        Assert.Equal(new[] { 2 }, released); Assert.True(budget.Contains("a"));
        using var pin = budget.Pin("a"); budget.Add("d", 4, 8); budget.Trim(); Assert.True(budget.Contains("a")); Assert.False(budget.Contains("d"));
    }
    [Fact]
    public void MasksUseLogicalRegionAndThresholdWithoutKeepingPixels()
    {
        var image = new PngImage(3, 1, 4, [0,0,0,0, 0,0,0,127, 0,0,0,128]);
        var region = new ImageAsset("a", File("page", 3), 1, 0, 2, 1);
        var mask = new AlphaMask(image, region, 128); Array.Clear(image.Pixels);
        Assert.False(mask.Contains(0, 0)); Assert.True(mask.Contains(1, 0)); Assert.False(mask.Contains(2, 0));
    }
}

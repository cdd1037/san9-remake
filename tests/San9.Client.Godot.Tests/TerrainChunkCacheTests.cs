using San9.Content.Images;
using San9.Content.Maps;
using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class TerrainChunkCacheTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetiredCompletionKeepsItsReservationAndCannotAffectReacquiredResource(bool fail)
    {
        using var gate = new ManualResetEventSlim();
        using var entered = new ManualResetEventSlim();
        int loads = 0, uploads = 0;
        CancellationToken retiredToken = default;
        var released = new List<Gpu>();
        PngImage Load(CancellationToken token)
        {
            if (Interlocked.Increment(ref loads) != 1) return Pixel(2);
            retiredToken = token;
            entered.Set();
            gate.Wait(); // A loader may ignore cancellation, then complete or fail late.
            if (fail) throw new IOException("retired load failed");
            return Pixel(1);
        }
        var key = new TerrainChunkKey(Renderer(), 0, 0);
        using var cache = new TerrainChunkCache<Gpu>(p => { uploads++; return new(p.Pixels[0]); }, released.Add,
            render: (_, token) => Load(token));
        using var old = cache.Acquire([key]);
        cache.Pump(() => true);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            long reservation = cache.PendingBytes;
            old.Dispose(); old.Dispose();
            Assert.True(retiredToken.IsCancellationRequested);
            using var current = cache.Acquire([key]);
            Until(cache, () => current.IsReady);
            Assert.Equal(reservation, cache.PendingBytes);
            Assert.Equal(1, uploads);
            gate.Set();
            Until(cache, () => cache.PendingBytes == 0);
            Assert.Null(current.Error);
            Assert.Equal(2, current.Get(key).Value);
            Assert.Equal(1, uploads);
            cache.Dispose(); cache.Dispose(); current.Dispose();
            Assert.Equal(2, Assert.Single(released).Value);
            Assert.Equal(0, cache.PendingBytes);
        }
        finally { gate.Set(); }
    }

    [Fact]
    public void FailedAcquireDoesNotKeepAnAbandonedGenerationAlive()
    {
        using var cache = new TerrainChunkCache<Gpu>(_ => new(1), _ => { }, render: (_, _) => Pixel());
        var key = new TerrainChunkKey(Renderer(), 0, 0);
        using (var ready = cache.Acquire([key])) Until(cache, () => ready.IsReady);
        IEnumerable<TerrainChunkKey> Broken()
        {
            yield return key;
            throw new InvalidDataException("broken view");
        }
        Assert.Throws<InvalidDataException>(() => cache.Acquire(Broken()));
        cache.Pump(() => true);
        Assert.Equal(0, cache.Bytes);
    }

    private sealed record Gpu(int Value);
    private static TerrainRenderer Renderer()
    {
        var map = new MapPackage(MapPackage.CurrentSchemaVersion, "fixture", "fixture", "spring", 1, 1, 1024, 512,
            [], [], [], [], [], [], [], [], [], new int?[512], new int?[512], [], new([], []), []);
        return new(map, new() { ["cloud-fill"] = new(1, 1, 3, [1, 2, 3]) }, []);
    }
    private static PngImage Pixel(byte value = 7) => new(1, 1, 3, [value, 0, 0]);
    private static void Until(TerrainChunkCache<Gpu> cache, Func<bool> condition)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            cache.Pump(() => true);
            if (watch.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException();
            Thread.Sleep(1);
        }
    }
    [Fact]
    public void VisibleChunksCanBeReadyWhileBufferedChunksAreStillRendering()
    {
        using var gate = new ManualResetEventSlim();
        using var cache = new TerrainChunkCache<Gpu>(_ => new(1), _ => { }, render: (key, token) =>
        { if (key.X == 1) gate.Wait(token); return Pixel(); });
        var renderer = Renderer(); var visible = new TerrainChunkKey(renderer, 0, 0);
        var buffer = new TerrainChunkKey(renderer, 1, 0);
        using var lease = cache.Acquire([visible, buffer]);
        try
        {
            Until(cache, () => lease.IsAvailable(visible));
            Assert.False(lease.IsAvailable(buffer)); Assert.False(lease.IsReady);
            Assert.False(lease.IsAvailable(new(renderer, 2, 0)));
            gate.Set(); Until(cache, () => lease.IsReady);
            lease.Dispose(); Assert.False(lease.IsAvailable(visible));
        }
        finally { gate.Set(); }
    }
    [Fact]
    public void WorkersContinueWithoutDisplayPumpsWhileUploadsStayOnDisplayThread()
    {
        using var entered = new CountdownEvent(2);
        using var gate = new ManualResetEventSlim();
        using var finished = new CountdownEvent(8);
        int active = 0, peak = 0, started = 0, uploads = 0;
        var order = new System.Collections.Concurrent.ConcurrentQueue<int>();
        object sync = new();
        using var cache = new TerrainChunkCache<Gpu>(_ => { uploads++; return new(1); }, _ => { }, render: (key, token) =>
        {
            lock (sync) { active++; peak = Math.Max(peak, active); }
            order.Enqueue(key.X);
            if (Interlocked.Increment(ref started) <= 2) entered.Signal();
            gate.Wait(token);
            lock (sync) active--;
            finished.Signal(); return Pixel();
        });
        var renderer = Renderer();
        // Released dictionary slots must not reverse the next view's visible-first request order.
        cache.Acquire(Enumerable.Range(100, 8).Select(x => new TerrainChunkKey(renderer, x, 0))).Dispose();
        using var lease = cache.Acquire(Enumerable.Range(0, 8).Select(x => new TerrainChunkKey(renderer, x, 0)));
        cache.Pump(() => false);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.Equal(2, started);
            Assert.Equal(new[] { 0, 1 }, order.Order().ToArray());
        }
        finally { gate.Set(); }
        Assert.True(finished.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(2, peak); Assert.Equal(0, uploads); Assert.False(lease.IsReady);
        Until(cache, () => lease.IsReady); Assert.Equal(8, uploads);
    }
    [Fact]
    public void QueuedTerrainResultsAreBoundedWhileUploadsArePaused()
    {
        using var cache = new TerrainChunkCache<Gpu>(_ => new(1), _ => { }, render: (_, _) => Pixel());
        var renderer = Renderer();
        using var lease = cache.Acquire(Enumerable.Range(0, 100).Select(x => new TerrainChunkKey(renderer, x, 0)));
        cache.Pump(() => false);
        Assert.InRange(cache.PendingBytes, 3 * 512L * 512, TerrainChunkCache<Gpu>.UploadQueueBudget);
        Assert.False(lease.IsReady);
    }
    [Fact]
    public void PrefetchReservesBudgetAcrossTheBatchAndDemandCanExceedIt()
    {
        using var gate = new ManualResetEventSlim();
        const long chunkBytes = (long)TerrainRenderer.ChunkSize * TerrainRenderer.ChunkSize * 3;
        using var cache = new TerrainChunkCache<Gpu>(_ => new(1), _ => { }, budget: chunkBytes,
            render: (_, token) => { gate.Wait(token); return Pixel(); });
        var renderer = Renderer();
        var first = new TerrainChunkKey(renderer, 0, 0);
        var second = new TerrainChunkKey(renderer, 1, 0);
        using var prefetch = cache.Acquire([first, second], prefetch: true);
        cache.Pump(() => false);
        Assert.Equal(chunkBytes, cache.PendingBytes);
        using var demand = cache.Acquire([second]);
        cache.Pump(() => false);
        Assert.Equal(2 * chunkBytes, cache.PendingBytes);
        gate.Set();
        Until(cache, () => prefetch.IsReady && demand.IsReady);
        Assert.Equal(0, cache.PendingBytes);
    }
    [Fact]
    public void TwoViewsShareOneRenderAndClosingPreviewPreservesMainLease()
    {
        int renders = 0, uploads = 0; var released = new List<Gpu>();
        using var cache = new TerrainChunkCache<Gpu>(p => { uploads++; return new(p.Pixels[0]); }, released.Add,
            render: (_, _) => { Interlocked.Increment(ref renders); return Pixel(); });
        var key = new TerrainChunkKey(Renderer(), 0, 0);
        using var main = cache.Acquire([key]); var preview = cache.Acquire([key]);
        Until(cache, () => main.IsReady && preview.IsReady);
        Assert.Same(main.Get(key), preview.Get(key)); Assert.Equal(1, renders); Assert.Equal(1, uploads);
        preview.Dispose(); cache.Pump(() => true);
        Assert.True(main.IsReady); Assert.Empty(released); Assert.Equal(0, cache.PendingBytes);
    }
    [Fact]
    public void DistantPreviewCannotEvictPresentedTerrainEvenWhenBudgetIsExceeded()
    {
        var released = new List<Gpu>();
        using var cache = new TerrainChunkCache<Gpu>(p => new(p.Pixels[0]), released.Add, budget: 3,
            render: (key, _) => Pixel((byte)key.X));
        var renderer = Renderer(); var home = new TerrainChunkKey(renderer, 0, 0); var distant = new TerrainChunkKey(renderer, 10, 5);
        using var main = cache.Acquire([home]); var preview = cache.Acquire([distant]);
        Until(cache, () => main.IsReady && preview.IsReady);
        Assert.Equal(6, cache.Bytes); Assert.Empty(released);
        preview.Dispose(); cache.Pump(() => true);
        Assert.True(main.IsReady); Assert.Equal(3, cache.Bytes); Assert.Equal(10, Assert.Single(released).Value);
    }
    [Fact]
    public void SeasonGenerationsDoNotAliasAndRetireOnlyAfterTheirLastViewCloses()
    {
        int uploads = 0; var released = new List<Gpu>();
        using var cache = new TerrainChunkCache<Gpu>(_ => new(++uploads), released.Add, render: (_, _) => Pixel());
        var springKey = new TerrainChunkKey(Renderer(), 0, 0); var winterKey = new TerrainChunkKey(Renderer(), 0, 0);
        var spring = cache.Acquire([springKey]); using var winter = cache.Acquire([winterKey]);
        Until(cache, () => spring.IsReady && winter.IsReady);
        var old = spring.Get(springKey); Assert.NotSame(old, winter.Get(winterKey));
        spring.Dispose(); cache.Pump(() => true);
        Assert.Equal(old, Assert.Single(released)); Assert.True(winter.IsReady);
    }
    [Fact]
    public void ClosingOneOfTwoPendingConsumersDoesNotCancelSharedWork()
    {
        using var gate = new ManualResetEventSlim();
        using var entered = new ManualResetEventSlim();
        using var cache = new TerrainChunkCache<Gpu>(_ => new(1), _ => { }, render: (_, token) =>
        { entered.Set(); gate.Wait(token); return Pixel(); });
        var key = new TerrainChunkKey(Renderer(), 0, 0);
        using var main = cache.Acquire([key]); var preview = cache.Acquire([key]);
        cache.Pump(() => true); Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        preview.Dispose(); gate.Set(); Until(cache, () => main.IsReady);
        Assert.Null(main.Error);
    }
    [Fact]
    public void AbandonedWorkNeverUploadsAndNewSelectionCompletes()
    {
        using var gate = new ManualResetEventSlim(); using var entered = new ManualResetEventSlim();
        var uploaded = new List<int>();
        using var cache = new TerrainChunkCache<Gpu>(p => { uploaded.Add(p.Pixels[0]); return new(p.Pixels[0]); }, _ => { },
            render: (key, _) => { if (key.X == 0) { entered.Set(); gate.Wait(); } return Pixel((byte)key.X); });
        var renderer = Renderer(); var old = cache.Acquire([new(renderer, 0, 0)]);
        cache.Pump(() => true); Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        old.Dispose(); using var latest = cache.Acquire([new(renderer, 1, 0)]);
        gate.Set(); Until(cache, () => latest.IsReady && cache.PendingBytes == 0);
        Assert.Equal([1], uploaded);
    }
    [Fact]
    public void UploadBudgetAndFailureAreIsolatedBetweenViews()
    {
        int uploads = 0;
        using var cache = new TerrainChunkCache<Gpu>(_ => new(++uploads), _ => { },
            render: (key, _) => key.X == 0 ? throw new InvalidDataException("bad terrain") : Pixel());
        var renderer = Renderer(); using var broken = cache.Acquire([new(renderer, 0, 0)]);
        using var good = cache.Acquire([new(renderer, 1, 0)]);
        cache.Pump(() => false); Assert.Equal(0, uploads); Assert.False(good.IsReady);
        Until(cache, () => broken.Error is not null && good.IsReady);
        Assert.IsType<InvalidDataException>(broken.Error); Assert.Equal(1, uploads);
    }
}

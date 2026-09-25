using San9.Content.Images;
using San9.Content.Maps;
using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class TerrainChunkCacheTests
{
    private sealed record Gpu(int Value);
    private static TerrainRenderer Renderer()
    {
        var map = new MapPackage(9, "fixture", "fixture", "spring", 1, 1, 1024, 512,
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

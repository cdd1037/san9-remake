using San9.Content.Images;
using System.Diagnostics;

namespace San9.Rendering;

// A renderer is an immutable map/season generation. Equal coordinates from different
// generations must never alias. All ownership and uploads run on the display thread.
public readonly record struct TerrainChunkKey(TerrainRenderer Renderer, int X, int Y);

public sealed class TerrainChunkCache<T>(Func<PngImage, T> upload, Action<T> release,
    long budget = 256L * 1024 * 1024,
    Func<TerrainChunkKey, CancellationToken, PngImage>? render = null) : IDisposable where T : class
{
    private sealed class Request(TerrainChunkKey key)
    {
        public TerrainChunkKey Key = key;
        public int Users;
        public long Sequence;
        public volatile bool Prefetch;
        public Task<PngImage>? Work;
        public CancellationTokenSource Cancellation = new();
        public IDisposable? Pin;
        public Exception? Error;
    }
    private readonly ResourceBudget<TerrainChunkKey, T> _cache = new(budget, release);
    private readonly Dictionary<TerrainChunkKey, Request> _requests = [];
    private readonly List<Request> _retired = [];
    private readonly BackgroundWorkQueue<PngImage> _workers = new();
    private readonly object _timingGate = new();
    public const long UploadQueueBudget = 32L * 1024 * 1024;
    private const long ChunkReservationBytes = (long)TerrainRenderer.ChunkSize * TerrainRenderer.ChunkSize * 3;
    private long _requestSequence;
    private bool _disposed;
    public long Bytes => _cache.Bytes;
    public long PendingBytes => (_requests.Values.Count(r => r.Work is not null) + _retired.Count) * ChunkReservationBytes;
    public double RenderMilliseconds { get; private set; }
    public double UploadMilliseconds { get; private set; }
    public Lease Acquire(IEnumerable<TerrainChunkKey> keys, bool prefetch = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var requestedKeys = keys.Distinct().ToArray();
        var requests = requestedKeys.Select(key =>
        {
            if (!_requests.TryGetValue(key, out var r)) _requests.Add(key, r = new(key) { Prefetch = prefetch, Sequence = ++_requestSequence });
            r.Users++;
            if (!prefetch) r.Prefetch = false;
            if (_cache.Contains(key)) r.Pin ??= _cache.Pin(key);
            return r;
        }).ToArray();
        return new(this, requests.Select(r => r.Key).ToArray(),
            () => requests.Select(r => r.Error).FirstOrDefault(e => e is not null), () =>
            {
                if (_disposed) return;
                foreach (var r in requests)
                {
                    if (--r.Users > 0) continue;
                    _requests.Remove(r.Key); r.Pin?.Dispose(); r.Cancellation.Cancel();
                    if (r.Work is null) r.Cancellation.Dispose(); else _retired.Add(r);
                }
            });
    }
    public void Pump(Func<bool> canUpload)
    {
        if (_disposed) return;
        _retired.RemoveAll(r =>
        {
            if (!r.Work!.IsCompleted) return false;
            _ = r.Work.Exception; r.Work = null; r.Cancellation.Dispose();
            return true;
        });
        foreach (var r in _requests.Values.Where(r => r.Work is { IsCompleted: true }).OrderBy(r => r.Prefetch).ThenBy(r => r.Sequence).ToArray())
        {
            if (!canUpload()) break;
            try
            {
                var pixels = r.Work!.GetAwaiter().GetResult();
                var timer = Stopwatch.StartNew();
                _cache.Add(r.Key, upload(pixels), (long)pixels.Width * pixels.Height * pixels.Channels);
                UploadMilliseconds += timer.Elapsed.TotalMilliseconds;
                r.Pin = _cache.Pin(r.Key);
            }
            catch (Exception error) { r.Error = error; }
            finally { r.Work = null; }
        }

        // Scheduling only adds reservations; workers cannot change owner-thread accounting.
        long pendingBytes = PendingBytes, residentBytes = Bytes;
        foreach (var r in _requests.Values.Where(r => r.Work is null && r.Error is null && !_cache.Contains(r.Key)).OrderBy(r => r.Prefetch).ThenBy(r => r.Sequence))
        {
            if (pendingBytes + ChunkReservationBytes > UploadQueueBudget) break;
            if (r.Prefetch && residentBytes + pendingBytes + ChunkReservationBytes > budget) continue;
            pendingBytes += ChunkReservationBytes;
            r.Work = _workers.Enqueue(() =>
            {
                var timer = Stopwatch.StartNew();
                var pixels = render is null ? r.Key.Renderer.RenderChunk(r.Key.X, r.Key.Y, r.Cancellation.Token)
                    : render(r.Key, r.Cancellation.Token);
                lock (_timingGate) RenderMilliseconds += timer.Elapsed.TotalMilliseconds;
                return pixels;
            }, () => r.Prefetch, r.Cancellation.Token);
        }
        var generations = _requests.Keys.Select(k => k.Renderer).ToHashSet();
        _cache.RemoveUnused(key => !generations.Contains(key.Renderer));
        _cache.Trim();
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var r in _requests.Values.Concat(_retired))
        {
            r.Cancellation.Cancel(); r.Pin?.Dispose();
            if (r.Work is { } work) _ = work.ContinueWith(t => { _ = t.Exception; r.Work = null; r.Cancellation.Dispose(); }, TaskScheduler.Default);
            else r.Cancellation.Dispose();
        }
        _requests.Clear(); _retired.Clear(); _cache.Dispose();
    }
    public sealed class Lease : IDisposable
    {
        private readonly TerrainChunkCache<T> _owner;
        private readonly Func<Exception?> _error;
        private Action? _release;
        internal Lease(TerrainChunkCache<T> owner, TerrainChunkKey[] keys, Func<Exception?> error, Action release)
        { _owner = owner; Keys = keys; _error = error; _release = release; }
        public IReadOnlyList<TerrainChunkKey> Keys { get; }
        public bool IsReady => _release is not null && Keys.All(_owner._cache.Contains);
        public bool IsAvailable(TerrainChunkKey key) => _release is not null && Keys.Contains(key) && _owner._cache.Contains(key);
        public Exception? Error => _error();
        public T Get(TerrainChunkKey key) => _owner._cache.Get(key);
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}

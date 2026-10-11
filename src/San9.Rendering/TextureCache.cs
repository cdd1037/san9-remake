using System.Diagnostics;
using San9.Content.Images;

namespace San9.Rendering;

// Pump and lease operations belong to the owner's thread; only loading executes on workers.
public sealed class TextureCache<T> : IDisposable where T : class
{
    private sealed class Request(ImageFile file)
    {
        public ImageFile File = file;
        public int Users;
        public long Sequence;
        public volatile bool Prefetch;
        public bool Skipped;
        public Task<PngImage>? Task;
        public CancellationTokenSource Cancellation = new();
        public Exception? Error;
        public IDisposable? Pin;
        public long Reserved;
    }
    private readonly Dictionary<ImageFile, Request> _requests = [];
    private readonly Func<ImageFile, CancellationToken, PngImage> _load;
    private readonly Func<ImageFile, PngImage, T> _upload;
    private readonly ResourceBudget<ImageFile, T> _resources;
    private readonly List<Request> _retired = [];
    private readonly BackgroundWorkQueue<PngImage> _workers = new();
    private long _requestSequence;
    private bool _disposed;
    public const long UploadQueueBudget = 32L * 1024 * 1024;
    public TextureCache(Func<ImageFile, CancellationToken, PngImage> load, Func<ImageFile, PngImage, T> upload,
        Action<T> release, long budget = 256L * 1024 * 1024)
    { _load = load; _upload = upload; _resources = new(budget, release); }
    public long Bytes => _resources.Bytes;
    public long PinnedBytes => _resources.PinnedBytes;
    public long Evictions => _resources.Evictions;
    public int Count => _resources.Count;
    public long PendingBytes => _requests.Values.Concat(_retired).Sum(r => r.Reserved);
    public long Hits { get; private set; }
    public long Misses { get; private set; }
    public double DecodeMilliseconds { get; private set; }
    public double UploadMilliseconds { get; private set; }
    public long UploadOverruns { get; private set; }
    public T Get(ImageFile file) => _resources.Get(file);
    public bool Contains(ImageFile file) => _resources.Contains(file);
    public void RetryFailed(IEnumerable<ImageFile> files)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (var file in files.Distinct())
            if (_requests.TryGetValue(file, out var request) && request.Error is not null)
                request.Error = null;
    }
    public Lease Acquire(IEnumerable<ImageFile> files, bool prefetch = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var requests = files.Distinct().Select(file =>
        {
            if (!_requests.TryGetValue(file, out var r)) _requests.Add(file, r = new(file) { Prefetch = prefetch, Sequence = ++_requestSequence });
            r.Users++;
            if (!prefetch) { r.Prefetch = false; r.Skipped = false; }
            if (_resources.Contains(file)) { Hits++; r.Pin ??= _resources.Pin(file); } else Misses++;
            return r;
        }).ToArray();
        return new Lease(() => requests.All(r => _resources.Contains(r.File)), () => requests.All(r => r.Skipped || r.Error is not null || _resources.Contains(r.File)), () => requests.Select(r => r.Error).FirstOrDefault(e => e is not null), () =>
        {
            if (_disposed) return;
            foreach (var r in requests)
            {
                if (--r.Users != 0) continue;
                r.Pin?.Dispose(); r.Pin = null;
                r.Cancellation.Cancel();
                _requests.Remove(r.File);
                if (r.Task is not null) _retired.Add(r); else r.Cancellation.Dispose();
            }
        });
    }
    public void Pump(double milliseconds = 2)
    {
        if (_disposed) return;
        foreach (var r in _retired.Where(r => r.Task!.IsCompleted).ToArray())
        { _ = r.Task!.Exception; r.Task = null; r.Reserved = 0; r.Cancellation.Dispose(); _retired.Remove(r); }
        var watch = Stopwatch.StartNew();
        foreach (var r in _requests.Values.OrderBy(r => r.Prefetch).ThenBy(r => r.Sequence).ToArray())
        {
            if (r.Task is not { IsCompleted: true }) continue;
            try
            {
                var pixels = r.Task.GetAwaiter().GetResult();
                if (pixels.Width != r.File.Width || pixels.Height != r.File.Height || pixels.Channels != r.File.Channels)
                    throw new InvalidDataException($"Decoded texture size differs from reservation: {r.File.Path}");
                var upload = Stopwatch.StartNew();
                var resource = _upload(r.File, pixels);
                double elapsed = upload.Elapsed.TotalMilliseconds;
                UploadMilliseconds += elapsed; if (elapsed > milliseconds) UploadOverruns++;
                _resources.Add(r.File, resource, (long)pixels.Width * pixels.Height * pixels.Channels);
                r.Pin = _resources.Pin(r.File);
            }
            catch (Exception error) { r.Error = error; }
            finally { r.Task = null; r.Reserved = 0; }
            if (watch.Elapsed.TotalMilliseconds >= milliseconds) break;
        }
        foreach (var r in _requests.Values.Where(r => r.Task is null && r.Error is null && !r.Skipped && !_resources.Contains(r.File)).OrderBy(r => r.Prefetch).ThenBy(r => r.Sequence))
        {
            long bytes = checked((long)r.File.Width * r.File.Height * r.File.Channels);
            if (r.Prefetch && Bytes + PendingBytes + bytes > _resources.Budget) { r.Skipped = true; continue; }
            if (PendingBytes != 0 && PendingBytes + bytes > UploadQueueBudget) continue;
            r.Reserved = bytes;
            r.Task = _workers.Enqueue(() =>
            {
                var timer = Stopwatch.StartNew();
                var pixels = _load(r.File, r.Cancellation.Token);
                // Only workers write this counter, protected independently of the main-thread state.
                lock (_load) DecodeMilliseconds += timer.Elapsed.TotalMilliseconds;
                return pixels;
            }, () => r.Prefetch, r.Cancellation.Token);
        }
        _resources.Trim();
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        foreach (var r in _requests.Values.Concat(_retired))
        {
            r.Cancellation.Cancel(); r.Pin?.Dispose();
            if (r.Task is { } task) _ = task.ContinueWith(t => { _ = t.Exception; r.Task = null; r.Cancellation.Dispose(); }, TaskScheduler.Default);
            else r.Cancellation.Dispose();
        }
        _requests.Clear(); _retired.Clear(); _resources.Dispose();
    }
    public sealed class Lease : IDisposable
    {
        private readonly Func<bool> _ready, _complete;
        private readonly Func<Exception?> _error;
        private Action? _release;
        internal Lease(Func<bool> ready, Func<bool> complete, Func<Exception?> error, Action release) { _ready = ready; _complete = complete; _error = error; _release = release; }
        public bool IsReady => _release is not null && _ready();
        public bool IsComplete => _release is not null && _complete();
        public Exception? Error => _error();
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}

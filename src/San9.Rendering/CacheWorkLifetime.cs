namespace San9.Rendering;

// Owned by the display thread. Retired work keeps its reservation and cancellation
// source until the worker finishes; disposing the owner never waits for workers.
internal sealed class CacheWorkLifetime<T> : IDisposable
{
    internal sealed class Work
    {
        private readonly CancellationTokenSource _cancellation = new();
        public Task<T>? Task { get; private set; }
        public long Reserved { get; private set; }
        public IDisposable? Pin { get; set; }

        public void Start(BackgroundWorkQueue<T> workers, Func<CancellationToken, T> run, Func<bool> prefetch, long bytes)
        {
            var token = _cancellation.Token;
            Reserved = bytes;
            Task = workers.Enqueue(() => run(token), prefetch, token);
        }

        public void Complete() { Task = null; Reserved = 0; }

        public void Cancel()
        {
            Pin?.Dispose(); Pin = null;
            _cancellation.Cancel();
        }

        public void Reclaim()
        {
            _ = Task?.Exception;
            Complete();
            _cancellation.Dispose();
        }
    }

    private readonly HashSet<Work> _owned = [];
    private readonly List<Work> _retired = [];
    private readonly BackgroundWorkQueue<T> _workers = new();
    public long PendingBytes => _owned.Sum(work => work.Reserved);

    public Work Create()
    {
        var work = new Work();
        _owned.Add(work);
        return work;
    }

    public void Start(Work work, Func<CancellationToken, T> run, Func<bool> prefetch, long bytes) =>
        work.Start(_workers, run, prefetch, bytes);

    public void Retire(Work work)
    {
        work.Cancel();
        if (work.Task is not null) _retired.Add(work);
        else { work.Reclaim(); _owned.Remove(work); }
    }

    public void ReclaimCompleted()
    {
        _retired.RemoveAll(work =>
        {
            if (!work.Task!.IsCompleted) return false;
            work.Reclaim();
            _owned.Remove(work);
            return true;
        });
    }

    public void Dispose()
    {
        foreach (var work in _owned)
        {
            work.Cancel();
            if (work.Task is { } task)
                _ = task.ContinueWith(_ => work.Reclaim(), TaskScheduler.Default);
            else work.Reclaim();
        }
        _owned.Clear(); _retired.Clear();
    }
}

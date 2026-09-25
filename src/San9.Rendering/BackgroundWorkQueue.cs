namespace San9.Rendering;

// Workers continue across display frames. Callers reserve result memory before enqueueing.
internal sealed class BackgroundWorkQueue<T>
{
    private sealed record Work(Func<T> Run, Func<bool> Prefetch, CancellationToken Token,
        TaskCompletionSource<T> Completion);
    private readonly object _gate = new();
    private readonly List<Work> _pending = [];
    private int _workers;

    public Task<T> Enqueue(Func<T> run, Func<bool> prefetch, CancellationToken token)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            _pending.Add(new(run, prefetch, token, completion));
            if (_workers < 2) { _workers++; _ = Task.Run(Drain); }
        }
        return completion.Task;
    }
    private void Drain()
    {
        while (true)
        {
            Work work;
            lock (_gate)
            {
                if (_pending.Count == 0) { _workers--; return; }
                int index = _pending.FindIndex(w => !w.Prefetch());
                if (index < 0) index = 0;
                work = _pending[index]; _pending.RemoveAt(index);
            }
            try { work.Token.ThrowIfCancellationRequested(); work.Completion.SetResult(work.Run()); }
            catch (OperationCanceledException error) { work.Completion.SetCanceled(error.CancellationToken); }
            catch (Exception error) { work.Completion.SetException(error); }
        }
    }
}

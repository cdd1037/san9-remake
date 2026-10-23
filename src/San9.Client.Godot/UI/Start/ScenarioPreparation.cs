using San9.Application.Presentation;

namespace San9.Client.Godot;

/// <summary>One page owns one preparation task. Interactive requests reuse it; only a failed attempt is retried.</summary>
internal sealed class ScenarioPreparation(Func<CancellationToken, Task<ScenarioForceSelection>> load) : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private Task<ScenarioForceSelection>? _task;
    private bool _closed;
    public CancellationToken Token => _lifetime.Token;

    public Task<ScenarioForceSelection> PrepareAsync()
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        if (_task is null || _task.IsFaulted || _task.IsCanceled) _task = LoadAsync();
        return _task;
    }
    private async Task<ScenarioForceSelection> LoadAsync()
    {
        var token = _lifetime.Token;
        var result = await load(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return result;
    }
    public void Dispose()
    {
        if (_closed) return;
        _closed = true;
        _lifetime.Cancel(); _lifetime.Dispose();
    }
}

namespace San9.Rendering;

// The current selection owns its request. Completion only changes that request's state;
// there is no asynchronous callback capable of overwriting a newer selection.
public sealed class ResourceSelection<T> : IDisposable where T : class, IDisposable
{
    public string? Key { get; private set; }
    public T? Current { get; private set; }
    public void Select(string? key, Func<string, T> acquire)
    {
        if (key == Key) return;
        var next = key is null ? null : acquire(key);
        var previous = Current; Current = next; Key = key; previous?.Dispose();
    }
    public void Dispose() { var previous = Current; Current = null; Key = null; previous?.Dispose(); }
}

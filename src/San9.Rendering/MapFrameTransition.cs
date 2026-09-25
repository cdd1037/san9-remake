namespace San9.Rendering;

// Requested resources and the frame being displayed have independent lifetimes.
// A failed or superseded request never replaces the complete presented frame.
public sealed class MapFrameTransition<T>(Func<T, bool> ready, Func<T, Exception?> error, Action<T> retire)
    : IDisposable where T : class, IDisposable
{
    public T? Presented { get; private set; }
    public T? Pending { get; private set; }
    public Exception? Error { get; private set; }
    public void Request(T next)
    {
        var previous = Pending; Pending = next; Error = null; previous?.Dispose();
    }
    public bool TryCommit()
    {
        if (Pending is not { } next) return false;
        if (error(next) is { } failure)
        { Error = failure; Pending = null; next.Dispose(); return false; }
        if (!ready(next)) return false;
        var previous = Presented; Presented = next; Pending = null;
        if (previous is not null) retire(previous);
        return true;
    }
    public void Dispose() { Pending?.Dispose(); Presented?.Dispose(); Pending = null; Presented = null; }
}

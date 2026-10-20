namespace San9.Client.Godot.Persistence;

/// <summary>Serializes background encoding and atomic writes; callers capture immutable data first.</summary>
public sealed class BackgroundSaveQueue
{
    private Task _tail = Task.CompletedTask;

    public Task Enqueue(Action write)
    {
        _tail = WriteAfter(_tail, write);
        return _tail;
    }

    private static async Task WriteAfter(Task previous, Action write)
    {
        try { await previous.ConfigureAwait(false); }
        // The previous caller observes its failure; it must not poison subsequent requests.
        catch (Exception) { }
        await Task.Run(write).ConfigureAwait(false);
    }
}

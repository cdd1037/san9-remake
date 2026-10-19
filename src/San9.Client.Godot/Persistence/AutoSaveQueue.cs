using San9.Application;
using San9.Application.Persistence;

namespace San9.Client.Godot.Persistence;

/// <summary>Captures on the owner thread and writes immutable bytes in request order.</summary>
public sealed class AutoSaveQueue(SlotSaveStore store)
{
    private Task _tail = Task.CompletedTask;

    public Task Enqueue(GameSession session, SaveSlotSummary summary)
    {
        byte[] bytes = SlotSaveStore.Encode(session, summary);
        _tail = WriteAfter(_tail, bytes);
        return _tail;
    }

    private async Task WriteAfter(Task previous, byte[] bytes)
    {
        // A failed write must not prevent the next xun from saving.
        try { await previous.ConfigureAwait(false); }
        catch (SaveException) { }
        await Task.Run(() => store.WriteAutoSave(bytes)).ConfigureAwait(false);
    }
}

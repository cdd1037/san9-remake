using San9.Application;
using San9.Application.Presentation;

namespace San9.Client.Godot.Persistence;

/// <summary>Claims session-owned xun checkpoints; the store serializes encoding and writes.</summary>
public sealed class AutoSaveQueue(SlotSaveStore store)
{
    public async Task<long?> Enqueue(GameSession session, Func<StrategySnapshot, SaveSlotSummary> summary)
    {
        if (session.TakeXunStartCheckpoint() is not { } checkpoint) return null;
        try
        {
            var document = SlotSaveStore.CaptureAutoSave(checkpoint, session.View, summary);
            await store.WriteAutoSaveAsync(document);
            return checkpoint.NextOperationSequence;
        }
        catch
        {
            session.RetryXunStartCheckpoint(checkpoint);
            throw;
        }
    }
}

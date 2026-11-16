using San9.Application.Presentation;

namespace San9.Client.Godot;

/// <summary>Publishes occurrence steps only after their preceding narration has completed.</summary>
internal static class ExecutionOutputPlayback
{
    internal static void Present(ExecutionOutput output,
        Action<IReadOnlyList<CommandMessage>, Action> narrate, Action<MapMessageRecord> record, Action? finished = null)
    {
        int index = 0;
        void Continue()
        {
            while (index < output.Steps.Count)
            {
                var step = output.Steps[index++];
                if (step.Messages.Count > 0)
                {
                    bool completed = false;
                    bool returned = false;
                    narrate(step.Messages, () =>
                    {
                        foreach (var entry in step.Records) record(entry);
                        completed = true;
                        if (returned) Continue();
                    });
                    // Skipped scenes can complete inside narrate; continue them in this loop.
                    returned = true;
                    if (!completed) return;
                }
                else foreach (var entry in step.Records) record(entry);
            }
            finished?.Invoke();
        }
        Continue();
    }
}

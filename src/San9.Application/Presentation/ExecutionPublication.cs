using System.Collections.Frozen;
using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

/// <summary>Projects committed facts in their original order, once per publication.</summary>
public static class ExecutionPublication
{
    public static ExecutionOutput Read(StrategySnapshot before, StrategySnapshot after,
        WorldMessageContext context, TextCatalog texts, CommandPresentation content,
        IReadOnlyList<DomainEvent> events, IReadOnlySet<Guid>? alreadyPresented = null, IReadOnlySet<string>? presentedFeedback = null, bool executionMap = true,
        int startIndex = 0, int? endIndex = null)
    {
        List<CommandMessage> messages = [];
        List<MapMessageRecord> records = [];
        HashSet<string> published = [];
        var identities = new PresentationIdentity(before, after);
        var occurrenceBefore = before;
        var occurrenceContext = context;
        for (int index = 0; index < (endIndex ?? events.Count); index++)
        {
            // Presented facts still update the identity of subsequent facts.
            if (index > 0) identities.Apply(events[index - 1]);
            if (index < startIndex) continue;
            if (!ReferenceEquals(occurrenceBefore, identities.View))
            {
                occurrenceBefore = identities.View;
                occurrenceContext = context.ForWorld(occurrenceBefore.World);
            }
            foreach (var batch in OccurrenceFeedback.Read(before, occurrenceBefore, after, context, occurrenceContext,
                texts, content, events, index, alreadyPresented, executionMap))
            {
                for (int i = 0; i < batch.Output.Messages.Count; i++)
                    if (presentedFeedback?.Contains(batch.MessageId(i)) != true)
                    { messages.Add(batch.Output.Messages[i]); published.Add(batch.MessageId(i)); }
                for (int i = 0; i < batch.Output.Records.Count; i++)
                    if (presentedFeedback?.Contains(batch.RecordId(i)) != true)
                    { records.Add(batch.Output.Records[i]); published.Add(batch.RecordId(i)); }
            }
        }
        return new(messages, records) { FeedbackIds = published.ToFrozenSet() };
    }
}

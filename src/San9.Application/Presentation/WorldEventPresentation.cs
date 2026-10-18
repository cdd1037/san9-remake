using System.Collections.Immutable;
using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

/// <summary>Produces retained scenes from committed facts without consuming gameplay randomness.</summary>
public static class WorldEventPresentation
{
    public static ExecutionOutput Read(StrategySnapshot before, StrategySnapshot after,
        WorldMessageContext context, TextCatalog texts, EventPresentationDefinition content,
        IReadOnlyList<DomainEvent> events, int index)
    {
        var occurrence = PresentationIdentity.BeforeFact(before, after, events, index);
        var messages = ReferenceEquals(occurrence, before) ? context : context.ForWorld(occurrence.World);
        return ReadOccurrence(occurrence, after, messages, texts, content, events, index);
    }

    internal static ExecutionOutput ReadOccurrence(StrategySnapshot before, StrategySnapshot after,
        WorldMessageContext context, TextCatalog texts, EventPresentationDefinition content,
        IReadOnlyList<DomainEvent> events, int index) =>
        new WorldEventSceneBuilder(before, after, context, texts, content, events, index).Build();
}

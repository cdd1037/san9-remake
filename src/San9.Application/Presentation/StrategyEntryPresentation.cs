using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

/// <summary>The fixed order prompt belongs to each strategy entrance, independently of situational advice.</summary>
public static class StrategyEntryPresentation
{
    public static IReadOnlyList<CommandMessage> Produce(StrategySnapshot view, WorldMessageContext context,
        TextCatalog texts, ProgressPresentationDefinition content)
    {
        if (view.World.Simulation.Boundary != SimulationBoundary.Stable) return [];
        var force = view.World.Forces.Single(f => f.Id == view.PlayerForceId);
        if (force.LeaderId is not int ruler || !view.World.Officers.Any(p => p.Id == ruler && !p.IsDormant && p.IsServing)) return [];
        var message = ProgressMessageProducer.Produce(content.OrderPrompt, context, texts,
            new Dictionary<string, MessageBinding> { ["ruler"] = ProgressMessageProducer.Person(ruler) },
            _ => throw new InvalidDataException("Order prompt must not require randomness."));
        return [message];
    }
}

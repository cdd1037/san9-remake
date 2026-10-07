using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

public static class RewardPresentation
{
    public static IReadOnlyList<CommandMessage> Create(StrategySnapshot before, WorldMessageContext context,
        RewardPresentationDefinition definition, IReadOnlyList<OfficerRewardedEvent> results, Func<int, int> random)
    {
        if (results.Count == 0) throw new ArgumentException("Reward presentation requires committed results.", nameof(results));
        int speaker = AppointmentOfficerOrder.Selected(before, results.Select(r => r.OfficerId)).First();
        var first = results.Single(r => r.OfficerId == speaker);
        int ruler = before.World.Forces.Single(f => f.Id == first.ForceId).LeaderId
            ?? throw new InvalidOperationException("Reward requires a ruler.");
        var bindings = new Dictionary<string, MessageBinding> {
            ["person.0"] = MessageBinding.Reference(MessageEntityKind.Person, speaker),
            ["person.1"] = MessageBinding.Reference(MessageEntityKind.Person, ruler),
            ["number.0"] = new(results.Count), ["number.1"] = new(results.Count - 1),
            ["number.2"] = new(before.World.Officers.Single(p => p.Id == speaker).Loyalty + first.Gain),
            ["number.3"] = new(first.Gain) };
        // The source chooses its dialogue from pre-reward loyalty, including the 75 threshold.
        return [new(context.Produce(definition.Dialogue, bindings, random), speaker, null),
            new(context.Produce(definition.Result, bindings, random), null, definition.ResultSound)];
    }
}

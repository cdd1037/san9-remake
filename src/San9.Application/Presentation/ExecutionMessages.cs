using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

public static class ExecutionMessages
{
    /// <summary>Completion is proved by this publication's events, not by a task disappearing from a later frame.</summary>
    public static IReadOnlyList<CommandMessage> Read(StrategySnapshot before, WorldMessageContext context, TextCatalog texts,
        DecisionPresentations presentation, IReadOnlyList<DomainEvent> events)
    {
        List<CommandMessage> result = [];
        var completed = events.OfType<TaskCompletedEvent>().Select(e => e.TaskId).ToHashSet();
        foreach (var task in before.World.Simulation.Tasks.Where(t => t.Kind == "tactic-study" && completed.Contains(t.Id)))
        {
            if (!events.OfType<TacticLearnedEvent>().Any(e => e.OfficerId == task.OfficerId && e.TacticId == task.StudyTacticId)) continue;
            var person = before.World.Officers.Single(p => p.Id == task.OfficerId);
            if (person.OwnerId is not int force || !before.World.Simulation.Controls.Any(c => c.ForceId == force && c.Kind == ControllerKind.Player)) continue;
            Dictionary<string, MessageBinding> values = new()
            {
                ["actor"] = MessageBinding.Reference(MessageEntityKind.Person, task.OfficerId),
                ["ruler"] = MessageBinding.Reference(MessageEntityKind.Person, before.World.Forces.Single(f => f.Id == force).LeaderId!.Value),
                ["place"] = MessageBinding.Reference(MessageEntityKind.Settlement, task.OriginSettlementId),
                ["force"] = MessageBinding.Reference(MessageEntityKind.Force, force),
                ["tactic"] = MessageBinding.Reference(MessageEntityKind.Tactic, task.StudyTacticId!.Value)
            };
            var message = presentation.Shared["study-completed"];
            var inputs = MessageGraph.Requirements(message.Key, texts.Messages).Bindings.ToDictionary(slot => slot, slot => values[message.Bindings[slot]]);
            result.Add(new(context.Produce(message.Key, inputs, Random.Shared.Next), task.OfficerId, message.Sound));
        }
        return result;
    }
}

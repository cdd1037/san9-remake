using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

public static class StrategistPresentation
{
    public static IReadOnlyList<CommandMessage> Create(StrategySnapshot before, WorldMessageContext context,
        StrategistPresentationDefinition definition, AdviserAppointedEvent result, Func<int, int> random)
    {
        int ruler = before.World.Forces.Single(f => f.Id == result.ForceId).LeaderId
            ?? throw new InvalidOperationException("Appointment requires a ruler.");
        MessageBinding Person(int id) => MessageBinding.Reference(MessageEntityKind.Person, id);
        List<CommandMessage> sequence = [new(context.Produce(definition.Appointed,
            new Dictionary<string, MessageBinding> { ["person.0"] = Person(result.OfficerId), ["person.1"] = Person(ruler) }, random), result.OfficerId, null)];
        if (result.PreviousOfficerId is int previous)
        {
            int Intelligence(int id) => before.Officers.Single(p => p.Id == id).Abilities.Intelligence;
            string key = Intelligence(result.OfficerId) > Intelligence(previous) ? definition.SuperiorSuccessor : definition.OtherSuccessor;
            sequence.Add(new(context.Produce(key, new Dictionary<string, MessageBinding> {
                ["person.0"] = Person(previous), ["person.1"] = Person(ruler), ["person.2"] = Person(result.OfficerId) }, random), previous, null));
        }
        sequence.Add(new(context.Produce(definition.Result, new Dictionary<string, MessageBinding> {
            ["person.0"] = Person(result.OfficerId), ["force.0"] = MessageBinding.Reference(MessageEntityKind.Force, result.ForceId) }, random), null, null));
        return sequence.AsReadOnly();
    }
}

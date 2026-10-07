using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

public static class ExilePresentation
{
    public static IReadOnlyList<CommandMessage> Create(StrategySnapshot before, WorldMessageContext context,
        ExilePresentationDefinition definition, IEnumerable<DomainEvent> events, Func<int, int> random)
    {
        var ids = AppointmentOfficerOrder.Selected(before, events.Select(e => e switch {
            OfficerExiledEvent exile => (int?)exile.OfficerId, PrisonerReleasedEvent release => release.OfficerId, _ => null
        }).OfType<int>()).ToArray();
        if (ids.Length == 0) throw new ArgumentException("Exile presentation requires committed results.", nameof(events));
        int representative = ids[0];
        int ruler = before.World.Forces.Single(f => f.Id == before.PlayerForceId).LeaderId
            ?? throw new InvalidOperationException("Exile requires a ruler.");
        bool dismissal = ids.Any(id => before.World.Officers.Single(p => p.Id == id).IsServing);
        var announcement = new Dictionary<string, MessageBinding> {
            ["person.0"] = MessageBinding.Reference(MessageEntityKind.Person, definition.MessengerId),
            ["person.1"] = MessageBinding.Reference(MessageEntityKind.Person, representative) };
        var reply = new Dictionary<string, MessageBinding> {
            ["person.0"] = MessageBinding.Reference(MessageEntityKind.Person, representative),
            ["person.1"] = MessageBinding.Reference(MessageEntityKind.Person, ruler) };
        var result = new Dictionary<string, MessageBinding> {
            ["force.0"] = MessageBinding.Reference(MessageEntityKind.Force, before.PlayerForceId),
            ["number.0"] = new(ids.Length), ["number.1"] = new(ids.Length - 2) };
        for (int i = 0; i < 3; i++) result[$"person.{i}"] = MessageBinding.Reference(MessageEntityKind.Person, i < ids.Length ? ids[i] : representative);
        // Resolve against the pre-command world: dismissal removes the speaker's serving relationship.
        return [new(context.Produce(dismissal ? definition.Dismissal : definition.Release, announcement, random), definition.MessengerId, null),
            new(context.Produce(dismissal ? definition.DismissalReply : definition.ReleaseReply, reply, random), representative, null),
            new(context.Produce(definition.Result, result, random), null, definition.ResultSound)];
    }
}

using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

public static class OfficePresentation
{
    public static IReadOnlyList<CommandMessage> Create(StrategySnapshot before, WorldMessageContext context,
        OfficePresentationDefinition definition, IEnumerable<OfficeAssignedEvent> events, Func<int,int> random)
    {
        var changes = events.ToArray();
        if (changes.Length == 0) throw new ArgumentException("Office presentation requires committed assignments.", nameof(events));
        var offices = before.Information.Offices.ToDictionary(o => o.Id);
        int Bonus(int? id) => id is int office ? offices[office].Salary / 2 : 0;
        List<CommandMessage> messages = [new(context.Produce(definition.Result,
            new Dictionary<string,MessageBinding> { ["force.0"] = MessageBinding.Reference(MessageEntityKind.Force,before.PlayerForceId) },random),null,null)];
        // A salary decrease triggers the notice even when the officer's loyalty was already zero.
        if (changes.Any(e => Bonus(e.OfficeId) < Bonus(e.PreviousId)))
            messages.Add(new(context.Produce(definition.DemotionNotice,new Dictionary<string,MessageBinding>(),random),null,null));
        return messages;
    }
}

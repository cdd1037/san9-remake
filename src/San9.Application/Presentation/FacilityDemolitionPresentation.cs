using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

public static class FacilityDemolitionPresentation
{
    public static IReadOnlyList<CommandMessage> Create(StrategySnapshot before, WorldMessageContext context,
        DemolitionPresentationDefinition definition, FacilityDemolishedEvent completed, Func<int, int> random)
    {
        var facility = before.World.Settlements.Single(s => s.Id == completed.SettlementId);
        var bindings = new Dictionary<string, MessageBinding>
        {
            ["building.0"] = MessageBinding.Reference(MessageEntityKind.Settlement, facility.Id),
            ["force.0"] = MessageBinding.Reference(MessageEntityKind.Force, facility.OwnerId!.Value)
        };
        List<CommandMessage> messages = [];
        var speaker = SettlementStaffing.Read(before, facility.Id).Present
            .Where(p => p.IsServing && p.OwnerId == facility.OwnerId)
            .OrderByDescending(p => p.IsCommander).ThenBy(p => p.Id).FirstOrDefault();
        if (speaker is not null)
        {
            bindings["person.0"] = MessageBinding.Reference(MessageEntityKind.Person, speaker.Id);
            messages.Add(new(context.Produce(definition.Dialogue, bindings, random), speaker.Id, null));
        }
        messages.Add(new(context.Produce(definition.Result, bindings, random), null, definition.ResultSound));
        return messages;
    }
}

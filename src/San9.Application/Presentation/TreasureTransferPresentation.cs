using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

public static class TreasureTransferPresentation
{
    public static IReadOnlyList<CommandMessage> Create(StrategySnapshot before, WorldMessageContext context,
        TreasureTransferPresentationDefinition definition, TreasureTransferredEvent transfer,
        IEnumerable<OfficerLoyaltyChangedEvent> changes, Func<int,int> random)
    {
        bool grant = transfer.Reason == "grant";
        int person = grant ? transfer.HolderId : transfer.PreviousHolderId;
        int ruler = before.World.Forces.Single(f => f.Id == transfer.ForceId).LeaderId!.Value;
        bool serving = before.World.Officers.Single(p => p.Id == person).IsServing;
        var values = new Dictionary<string,MessageBinding> {
            ["person.0"] = MessageBinding.Reference(MessageEntityKind.Person, person),
            ["person.1"] = MessageBinding.Reference(MessageEntityKind.Person, ruler),
            ["treasure.0"] = MessageBinding.Reference(MessageEntityKind.Treasure, transfer.TreasureId) };
        List<CommandMessage> messages = [
            new(context.Produce(grant ? definition.GrantDialogue : serving ? definition.ConfiscateDialogue : definition.CaptiveDialogue, values, random),person,null),
            new(context.Produce(grant ? definition.GrantResult : definition.ConfiscateResult,values,random),null,null) ];
        var loyalty = changes.SingleOrDefault(c => c.OfficerId == person && c.ForceId == transfer.ForceId);
        if (serving && loyalty is not null && loyalty.Previous != loyalty.Current && !(loyalty.Current > loyalty.Previous && loyalty.Current > 100))
        {
            values["number.0"] = new(loyalty.Current);
            values["number.1"] = new(Math.Abs(loyalty.Current - loyalty.Previous));
            string key = loyalty.Previous > 100 || loyalty.Current > 100 ? definition.HiddenLoyaltyDecrease
                : loyalty.Current > loyalty.Previous ? definition.LoyaltyIncrease : definition.LoyaltyDecrease;
            messages.Add(new(context.Produce(key,values,random),null,
                loyalty.Current > loyalty.Previous ? definition.IncreaseSound : definition.DecreaseSound));
        }
        return messages;
    }
}

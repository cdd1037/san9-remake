using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record GrantTreasureCommand(Guid CommandId, int IssuerId, long Sequence, int TreasureId, int OfficerId)
    : GameCommand(CommandId, IssuerId, Sequence);
public sealed record ConfiscateTreasureCommand(Guid CommandId, int IssuerId, long Sequence, int TreasureId)
    : GameCommand(CommandId, IssuerId, Sequence);
public sealed record TreasureTransferredEvent(GameClock Clock, int ForceId, int TreasureId, int PreviousHolderId,
    int HolderId, string Reason) : DomainEvent;

internal static class PersonnelTreasures
{
    internal static CommandRejection Grant(WorldMutation world, GrantTreasureCommand command,
        TreasureRulebook rules, EconomyRulebook economy, List<DomainEvent> events)
    {
        if (!world.Treasures.TryGetValue(command.TreasureId, out var item) || !rules.Items.ContainsKey(command.TreasureId))
            return CommandRejection.UnknownTreasure;
        if (!world.Officers.TryGetValue(command.OfficerId, out var recipient)) return CommandRejection.UnknownPerson;
        int? ruler = world.Forces[command.IssuerId].LeaderId;
        if (ruler is null || !item.Discovered || item.HolderId != ruler) return CommandRejection.TreasureUnavailable;
        if (!IsOwnOfficer(recipient, command.IssuerId, ruler.Value) || IsTravelling(world, recipient.Id))
            return CommandRejection.PersonUnavailable;

        Transfer(world, item, recipient.Id, command.IssuerId, "grant", events);
        int loyalty = (int)Math.Min(255L, (long)recipient.Loyalty + rules.Items[item.Id].LoyaltyBonus);
        RecruitmentRules.SetLoyalty(world, recipient.Id, command.IssuerId, loyalty, "treasure-granted", events);
        return CommandRejection.None;
    }

    internal static CommandRejection Confiscate(WorldMutation world, ConfiscateTreasureCommand command,
        TreasureRulebook rules, EconomyRulebook economy, List<DomainEvent> events)
    {
        if (!world.Treasures.TryGetValue(command.TreasureId, out var item) || !rules.Items.ContainsKey(command.TreasureId))
            return CommandRejection.UnknownTreasure;
        int? ruler = world.Forces[command.IssuerId].LeaderId;
        if (ruler is null || !item.Discovered || item.HolderId is not int holder)
            return CommandRejection.TreasureUnavailable;
        var person = world.Officers[holder];
        bool detainedHere = person.Status == OfficerStatus.Captive && person.SettlementId is int location &&
            world.Settlements[location].OwnerId == command.IssuerId;
        if ((!IsOwnOfficer(person, command.IssuerId, ruler.Value) && !detainedHere) || IsTravelling(world, holder))
            return CommandRejection.PersonUnavailable;

        Transfer(world, item, ruler.Value, command.IssuerId, "confiscate", events);
        int loyalty = (int)Math.Max(0L, person.LoyaltyTo(command.IssuerId) - 3L * rules.Items[item.Id].LoyaltyBonus / 2);
        RecruitmentRules.SetLoyalty(world, holder, command.IssuerId, loyalty, "treasure-confiscated", events);
        return CommandRejection.None;
    }

    private static bool IsOwnOfficer(OfficerState person, int forceId, int rulerId) =>
        person.IsServing && person.Status != OfficerStatus.Ruler && person.Id != rulerId && person.OwnerId == forceId;

    private static bool IsTravelling(WorldMutation world, int officerId) =>
        world.Simulation.Tasks.Any(t => t.OfficerId == officerId && t.Journey is not null);

    internal static void Transfer(WorldMutation world, TreasureState item, int recipientId, int forceId, string reason,
        List<DomainEvent> events)
    {
        int previous = item.HolderId!.Value;
        world.Replace(item with { HolderId = recipientId });
        events.Add(new TreasureTransferredEvent(world.Clock, forceId, item.Id, previous, recipientId, reason));
    }
}

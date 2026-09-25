using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record RewardOfficerCommand(Guid CommandId, int IssuerId, long Sequence, int OfficerId)
    : GameCommand(CommandId, IssuerId, Sequence);

public sealed record OfficerRewardedEvent(GameClock Clock, int ForceId, int OfficerId, int Gain) : DomainEvent;

internal static class PersonnelRewards
{
    internal static CommandRejection Apply(WorldMutation world, RewardOfficerCommand command, List<DomainEvent> events)
    {
        if (!world.Officers.TryGetValue(command.OfficerId, out var person))
            return CommandRejection.UnknownPerson;
        if (!person.IsServing || person.Status == OfficerStatus.Ruler || world.Forces[command.IssuerId].LeaderId == person.Id || person.OwnerId != command.IssuerId ||
            person.SettlementId is not int location || world.Settlements[location].OwnerId != command.IssuerId ||
            world.Simulation.Tasks.Any(t => t.OfficerId == person.Id && t.Journey is not null))
            return CommandRejection.PersonUnavailable;
        if (person.RewardedThisXun) return CommandRejection.AlreadyRewarded;
        if (person.Loyalty >= 100) return CommandRejection.LoyaltyAtMaximum;
        var account = ResourceAccounts.Resolve(world, command.IssuerId, null);
        if (ResourceAccounts.Balance(world, account, ResourceKind.Money) < 100)
            return CommandRejection.InsufficientFunds;

        int gain = Math.Min(Math.Min(50 * (20 - person.Righteousness) / 100, 10), 100 - person.Loyalty);
        ResourceAccounts.Change(world, account, ResourceKind.Money, -100, "personnel-reward", person.Id, events, requireFull: true);
        world.Replace(person.WithLoyalty(command.IssuerId, person.Loyalty + gain) with { RewardedThisXun = true });
        events.Add(new OfficerLoyaltyChangedEvent(world.Clock, person.Id, person.OwnerId!.Value, person.Loyalty, person.Loyalty + gain, "reward"));
        events.Add(new OfficerRewardedEvent(world.Clock, command.IssuerId, person.Id, gain));
        return CommandRejection.None;
    }
}

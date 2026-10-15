using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record RewardOfficerCommand(Guid CommandId, int IssuerId, long Sequence, int OfficerId)
    : GameCommand(CommandId, IssuerId, Sequence);

public sealed record OfficerRewardedEvent(GameClock Clock, int ForceId, int OfficerId, int Gain) : DomainEvent;

internal static class PersonnelRewards
{
    internal static CommandRejection Eligibility(WorldMutation world, RewardOfficerCommand command)
    {
        if (!world.Officers.TryGetValue(command.OfficerId, out var person))
            return CommandRejection.UnknownPerson;
        if (!person.IsServing || person.Status == OfficerStatus.Ruler || world.Forces[command.IssuerId].LeaderId == person.Id || person.OwnerId != command.IssuerId ||
            person.SettlementId is not int location || world.Settlements[location].OwnerId != command.IssuerId ||
            world.IsTraveling(person.Id))
            return CommandRejection.PersonUnavailable;
        if (person.RewardedThisXun) return CommandRejection.AlreadyRewarded;
        if (person.Loyalty >= 100) return CommandRejection.LoyaltyAtMaximum;
        var account = ResourceAccounts.Resolve(world, command.IssuerId, null);
        if (ResourceAccounts.Balance(world, account, ResourceKind.Money) < 100)
            return CommandRejection.InsufficientFunds;

        return CommandRejection.None;
    }

    internal static CommandRejection Apply(WorldMutation world, RewardOfficerCommand command, List<DomainEvent> events)
    {
        var rejection = Eligibility(world, command);
        if (rejection != CommandRejection.None) return rejection;
        var person = world.Officers[command.OfficerId];
        var account = ResourceAccounts.Resolve(world, command.IssuerId, null);
        int gain = PersonnelRewardMath.Gain(person.Loyalty, person.Righteousness);
        ResourceAccounts.Change(world, account, ResourceKind.Money, -100, "personnel-reward", person.Id, events, requireFull: true);
        world.Replace(person.WithLoyalty(command.IssuerId, person.Loyalty + gain) with { RewardedThisXun = true });
        events.Add(new OfficerLoyaltyChangedEvent(world.Clock, person.Id, person.OwnerId!.Value, person.Loyalty, person.Loyalty + gain, "reward"));
        events.Add(new OfficerRewardedEvent(world.Clock, command.IssuerId, person.Id, gain));
        return CommandRejection.None;
    }
}

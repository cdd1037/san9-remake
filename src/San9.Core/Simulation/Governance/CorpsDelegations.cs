using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record SetCorpsDelegationCommand(Guid CommandId, int IssuerId, long Sequence, int CorpsId,
    CorpsDelegation Delegation) : GameCommand(CommandId, IssuerId, Sequence);
public sealed record CorpsDelegationChangedEvent(GameClock Clock, int ForceId, int CorpsId,
    CorpsDelegation Previous, CorpsDelegation Current, string Reason) : DomainEvent;

internal static class CorpsDelegations
{
    internal static bool Valid(WorldMutation world, CorpsState corps, MilitaryRulebook? military) =>
        corps.IsRoot ? corps.Delegation is GeneralDelegation : Valid(world, corps.OwnerId, corps.Delegation, military);

    internal static bool Valid(IWorldQuery world, int owner, CorpsDelegation delegation, MilitaryRulebook? military) => delegation switch
        {
            GeneralDelegation or DomesticDelegation => true,
            DefeatForceDelegation target => MilitaryDiplomacy.AutomaticEnemy(world, owner, target.ForceId),
            CaptureSettlementDelegation target => world.Settlements.TryGetValue(target.SettlementId, out var place) &&
                !place.IsDestroyed && (IsWork(place, military) || MilitaryDiplomacy.AutomaticEnemy(world, owner, place.OwnerId)),
            _ => false
        };

    private static bool IsWork(SettlementState place, MilitaryRulebook? military) =>
        military is not null && military.TryPlace(place, out var definition) && definition.FacilityTypeId is int type &&
        military.Facilities[type].IsWork;

    internal static CommandRejection Eligibility(WorldMutation world, SetCorpsDelegationCommand command, MilitaryRulebook? military)
    {
        if (!world.Corps.TryGetValue(command.CorpsId, out var corps) || corps.OwnerId != command.IssuerId ||
            corps.IsRoot || !Valid(world, corps with { Delegation = command.Delegation }, military))
            return CommandRejection.InvalidCorps;
        return CommandRejection.None;
    }

    internal static CommandRejection Set(WorldMutation world, SetCorpsDelegationCommand command,
        MilitaryRulebook? military, List<DomainEvent> events)
    {
        var rejection = Eligibility(world, command, military);
        if (rejection != CommandRejection.None) return rejection;
        var corps = world.Corps[command.CorpsId];
        Change(world, corps, command.Delegation, "ordered", events);
        return CommandRejection.None;
    }

    internal static void Cleanup(WorldMutation world, MilitaryRulebook? military, List<DomainEvent> events)
    {
        foreach (var corps in world.Corps.Values.OrderBy(c => c.Id).ToArray())
            if (!Valid(world, corps, military)) Change(world, corps, new GeneralDelegation(), "target-invalidated", events);
    }

    private static void Change(WorldMutation world, CorpsState corps, CorpsDelegation delegation, string reason, List<DomainEvent> events)
    {
        if (corps.Delegation == delegation) return;
        world.Replace(corps with { Delegation = delegation });
        events.Add(new CorpsDelegationChangedEvent(world.Clock, corps.OwnerId, corps.Id, corps.Delegation, delegation, reason));
    }
}

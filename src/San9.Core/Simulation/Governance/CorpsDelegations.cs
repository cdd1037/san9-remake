using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record SetCorpsDelegationCommand(Guid CommandId, int IssuerId, long Sequence, int CorpsId,
    CorpsDelegation Delegation) : GameCommand(CommandId, IssuerId, Sequence);
public sealed record CorpsDelegationChangedEvent(GameClock Clock, int ForceId, int CorpsId,
    CorpsDelegation Previous, CorpsDelegation Current, string Reason) : DomainEvent;

internal static class CorpsDelegations
{
    internal static bool Valid(WorldMutation world, CorpsState corps, MilitaryRulebook? military) =>
        corps.IsRoot ? corps.Delegation is GeneralDelegation : corps.Delegation switch
        {
            GeneralDelegation or DomesticDelegation => true,
            DefeatForceDelegation target => MilitaryDiplomacy.AutomaticEnemy(world, corps.OwnerId, target.ForceId),
            CaptureSettlementDelegation target => world.Settlements.TryGetValue(target.SettlementId, out var place) &&
                !place.IsDestroyed && (IsWork(place, military) || MilitaryDiplomacy.AutomaticEnemy(world, corps.OwnerId, place.OwnerId)),
            _ => false
        };

    private static bool IsWork(SettlementState place, MilitaryRulebook? military) =>
        military is not null && military.TryPlace(place, out var definition) && definition.FacilityTypeId is int type &&
        military.Facilities[type].IsWork;

    internal static CommandRejection Set(WorldMutation world, SetCorpsDelegationCommand command,
        MilitaryRulebook? military, List<DomainEvent> events)
    {
        if (!world.Corps.TryGetValue(command.CorpsId, out var corps) || corps.OwnerId != command.IssuerId ||
            corps.IsRoot || !Valid(world, corps with { Delegation = command.Delegation }, military))
            return CommandRejection.InvalidCorps;
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

using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Reports meaningful automatic responses at the order-change boundary.</summary>
internal static class UnitOrderChanges
{
    internal static void Report(WorldMutation world, int id, MilitaryOrder? order, string reason, List<DomainEvent> events,
        MilitaryOrder? previousOrder = null)
    {
        events.Add(new UnitOrderChangedEvent(world.Clock, id, order, reason) { PreviousOrder = previousOrder });
        var unit = world.Units[id];
        UnitPolicyResponse? response = reason switch
        {
            "construction-target-invalid" when order?.Kind == MilitaryOrderKind.Return => UnitPolicyResponse.ConstructionAborted,
            "construction-completed" when order?.Kind == MilitaryOrderKind.Return => UnitPolicyResponse.ReturnAfterAction,
            "target-invalid" when order?.Kind == MilitaryOrderKind.Return => previousOrder?.Kind switch
            {
                MilitaryOrderKind.Transport => UnitPolicyResponse.TransportAborted,
                MilitaryOrderKind.Construct => UnitPolicyResponse.ConstructionAborted,
                MilitaryOrderKind.Return when previousOrder.SettlementId is not null => UnitPolicyResponse.ReturnTargetChanged,
                _ => UnitPolicyResponse.ReturnAfterAction
            },
            "target-captured-entry" => UnitPolicyResponse.EnterCapturedPlace,
            "transfer-return" => UnitPolicyResponse.ReturnAfterAction,
            "transfer-attack" when order?.UnitId is not null => UnitPolicyResponse.AttackUnitAfterAction,
            "transfer-attack" when order?.SettlementId is not null => UnitPolicyResponse.AttackPlaceAfterAction,
            _ => null
        };
        if (response is null || unit.CommanderId is null || order is { Kind: MilitaryOrderKind.Return, SettlementId: null }) return;
        int? other = order?.UnitId is int target ? world.Units.GetValueOrDefault(target)?.CommanderId : null;
        events.Add(UnitPolicyRespondedEvent.Capture(world, unit, response.Value,
            other, order?.SettlementId ?? unit.ServicePlaceId,
            previousOrder is { } previous ? previous.Construction?.RegionId ?? previous.SettlementId : null,
            order?.Kind == MilitaryOrderKind.Return ? UnitPolicyReturnOutcome.Started : UnitPolicyReturnOutcome.NotRequested));
    }
}

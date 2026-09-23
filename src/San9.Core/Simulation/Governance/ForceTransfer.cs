using San9.Core.Domain;

namespace San9.Core.Simulation;

// Shared ownership operations. Event-specific eligibility, personnel outcomes and rewards
// remain with their rule; callers publish the complete change in one world transaction.
internal static class ForceTransfer
{
    internal static void CancelTasks(WorldMutation world, int force, SimulationRules rules, string reason, List<DomainEvent> events, bool afterDailyCommands)
    {
        foreach (var task in world.Simulation.Tasks.Where(t => t.ForceId == force || t.Gift?.TargetForceId == force ||
            t.PrisonerReturn?.TargetForceId == force || t.AttackRequest?.TargetForceId == force || t.Surrender?.TargetForceId == force)
            .OrderBy(t => t.Sequence).ThenBy(t => t.Id).ToArray())
        {
            TaskCancellation.Apply(world, task, reason, events);
            if (task.ForceId == force) ReinforcementTransport.Disband(world, task, reason, events);
            else if (task.Journey is not null) PersonnelReturns.Start(world, task, rules.Personnel, events, afterDailyCommands);
        }
    }

    internal static void Settlements(WorldMutation world, int force, int recipient, int? root, List<DomainEvent> events)
    {
        foreach (var place in world.Settlements.Values.Where(s => s.OwnerId == force).OrderBy(s => s.Id).ToArray())
        {
            world.Replace(place with { OwnerId = recipient, CorpsId = root });
            events.Add(new SettlementTransferredEvent(world.Clock, place.Id, force, recipient));
        }
    }

    internal static (long Removed, uint Received) Treasury(WorldMutation world, int force, int recipient,
        ResourceKind resource, int divisor, string reason, int? source, List<DomainEvent> events)
    {
        var accounts = new[] { new ResourceAccount(ResourceOwnerKind.Force, force) }
            .Concat(world.Corps.Values.Where(c => c.OwnerId == force && c.Treasury is not null).OrderBy(c => c.Id)
                .Select(c => new ResourceAccount(ResourceOwnerKind.Corps, c.Id))).ToArray();
        long total = accounts.Sum(a => (long)ResourceAccounts.Balance(world, a, resource));
        foreach (var account in accounts) ResourceAccounts.Change(world, account, resource,
            -(long)ResourceAccounts.Balance(world, account, resource), reason, source, events);
        var target = new ResourceAccount(ResourceOwnerKind.Force, recipient);
        uint received = (uint)Math.Min(total / divisor, ResourceAccounts.Receivable(world, target, resource));
        ResourceAccounts.Change(world, target, resource, total / divisor, reason, source, events);
        return (total, received);
    }

    internal static void ClearFriendlyOrders(WorldMutation world, string reason, List<DomainEvent> events)
    {
        foreach (var unit in world.Units.Values.OrderBy(u => u.Id).ToArray())
        {
            bool main = unit.Order?.IsAttack == true && MilitaryDiplomacy.TargetOwner(world, unit.Order) == unit.OwnerId;
            bool temporary = unit.EngagementTargetId is int other && world.Units[other].OwnerId == unit.OwnerId;
            if (main || temporary) world.Replace(unit with { Order = main ? null : unit.Order, MovementPlan = main ? null : unit.MovementPlan,
                EngagementTargetId = temporary ? null : unit.EngagementTargetId, AwaitingPostAction = false });
            if (main) events.Add(new UnitOrderChangedEvent(world.Clock, unit.Id, null, reason));
            if (temporary) events.Add(new UnitEngagementChangedEvent(world.Clock, unit.Id, null, reason));
        }
    }
}

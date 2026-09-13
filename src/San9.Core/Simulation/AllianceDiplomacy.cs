using San9.Core.Domain;

namespace San9.Core.Simulation;

public enum AllianceEndReason { Expired, TargetEliminated, LeaderEliminated, InsufficientMembers }
public sealed record AllianceEndedEvent(GameClock Clock, int TargetForceId, AllianceEndReason Reason) : DomainEvent;
public sealed record AllianceMemberRemovedEvent(GameClock Clock, int ForceId) : DomainEvent;

internal static class AllianceDiplomacy
{
    internal static void UpdateXun(WorldMutation world, List<DomainEvent> events)
    {
        if (world.Simulation.Alliance is not { } alliance || world.Clock == world.Simulation.Started) return;
        if (alliance.RemainingXun == 1) End(world, AllianceEndReason.Expired, events);
        else world.SetSimulation(world.Simulation with { Alliance = alliance with { RemainingXun = alliance.RemainingXun - 1 } });
    }

    internal static void ForceEliminated(WorldMutation world, int forceId, List<DomainEvent> events)
    {
        if (world.Simulation.Alliance is not { } alliance) return;
        if (alliance.TargetForceId == forceId) End(world, AllianceEndReason.TargetEliminated, events);
        else if (alliance.LeaderForceId == forceId) End(world, AllianceEndReason.LeaderEliminated, events);
        else if (alliance.MemberForceIds.Contains(forceId))
        {
            if (alliance.MemberForceIds.Length == 2) End(world, AllianceEndReason.InsufficientMembers, events);
            else
            {
                world.SetSimulation(world.Simulation with { Alliance = alliance with { MemberForceIds = alliance.MemberForceIds.Remove(forceId) } });
                events.Add(new AllianceMemberRemovedEvent(world.Clock, forceId));
            }
        }
    }

    private static void End(WorldMutation world, AllianceEndReason reason, List<DomainEvent> events)
    {
        int target = world.Simulation.Alliance!.TargetForceId;
        world.SetSimulation(world.Simulation with { Alliance = null });
        events.Add(new AllianceEndedEvent(world.Clock, target, reason));
    }
}

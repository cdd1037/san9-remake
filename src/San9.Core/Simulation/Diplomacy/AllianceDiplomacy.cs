using San9.Core.Domain;

namespace San9.Core.Simulation;

public enum AllianceEndReason { Expired, TargetEliminated, LeaderEliminated, InsufficientMembers, Betrayal, LeaderDied, TargetDied, SubjugationSucceeded }
public enum AllianceVictoryCause { ForceDefeated, CapturedRulerExecuted }
public sealed record AllianceVictoryEvent(GameClock Clock, int TargetForceId, int VictorForceId,
    AllianceVictoryCause Cause, int BaseTrustGained) : DomainEvent;
public sealed record AllianceEndedEvent(GameClock Clock, int TargetForceId, AllianceEndReason Reason) : DomainEvent;
public sealed record AllianceMemberRemovedEvent(GameClock Clock, int ForceId) : DomainEvent;

internal static class AllianceDiplomacy
{
    internal static void CompleteSubjugation(WorldMutation world, int targetForceId, int victorForceId,
        AllianceVictoryCause cause, List<DomainEvent> events)
    {
        if (world.Simulation.Alliance is not { } alliance || alliance.TargetForceId != targetForceId ||
            !alliance.MemberForceIds.Contains(victorForceId) ||
            !world.Forces.TryGetValue(victorForceId, out var victor) || victor.IsEliminated) return;
        int trust = Math.Min(1000, victor.BaseTrust + 15);
        world.Replace(victor with { BaseTrust = trust });
        events.Add(new AllianceVictoryEvent(world.Clock, targetForceId, victorForceId, cause, trust - victor.BaseTrust));
        End(world, AllianceEndReason.SubjugationSucceeded, events);
    }

    internal static void UpdateXun(WorldMutation world, List<DomainEvent> events)
    {
        if (world.Simulation.Alliance is not { } alliance || world.Clock == world.Simulation.Started) return;
        if (alliance.RemainingXun == 1) End(world, AllianceEndReason.Expired, events);
        else world.SetSimulation(world.Simulation with { Alliance = alliance with { RemainingXun = alliance.RemainingXun - 1 } });
    }

    internal static void RulerDied(WorldMutation world, int forceId, List<DomainEvent> events)
    {
        if (world.Simulation.Alliance is not { } alliance) return;
        if (alliance.LeaderForceId == forceId) End(world, AllianceEndReason.LeaderDied, events);
        else if (alliance.TargetForceId == forceId) End(world, AllianceEndReason.TargetDied, events);
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

    internal static void Betray(WorldMutation world, int forceId, List<DomainEvent> events)
    {
        var alliance = world.Simulation.Alliance!;
        if (alliance.LeaderForceId == forceId || alliance.MemberForceIds.Length < 3) End(world, AllianceEndReason.Betrayal, events);
        else
        {
            world.SetSimulation(world.Simulation with { Alliance = alliance with { MemberForceIds = alliance.MemberForceIds.Remove(forceId) } });
            events.Add(new AllianceMemberRemovedEvent(world.Clock, forceId));
        }
    }

    private static void End(WorldMutation world, AllianceEndReason reason, List<DomainEvent> events)
    {
        int target = world.Simulation.Alliance!.TargetForceId;
        world.SetSimulation(world.Simulation with { Alliance = null });
        events.Add(new AllianceEndedEvent(world.Clock, target, reason));
    }
}

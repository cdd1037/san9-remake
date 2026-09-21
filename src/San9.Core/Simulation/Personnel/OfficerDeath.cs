using San9.Core.Domain;

namespace San9.Core.Simulation;

public enum OfficerDeathCause { NoRetreat, ForceEliminated, Natural, Execution, Duel, HistoricalEvent, Sniping }

public sealed record OfficerDiedEvent(GameClock Clock, int OfficerId, OfficerDeathCause Cause,
    OfficerStatus PreviousStatus, int? PreviousForceId, int? PreviousSettlementId, int? PreviousRegionId,
    int? PreviousUnitId) : DomainEvent;
public sealed record TreasureDroppedEvent(GameClock Clock, int TreasureId, int PreviousHolderId) : DomainEvent;

/// <summary>Personnel removal within an enclosing death, battle or force transaction.
/// The caller must settle succession, remaining unit occupants and force assets before committing.</summary>
internal static class OfficerDeath
{
    internal static void Resolve(WorldMutation world, int officerId, OfficerDeathCause cause, SimulationRules rules, List<DomainEvent> events,
        bool afterDailyCommands, int? successorId = null)
    {
        var previous = world.Officers[officerId];
        int? recipientForce = previous.Status == OfficerStatus.Captive
            ? previous.UnitId is int detention ? world.Units[detention].OwnerId
                : previous.SettlementId is int prison ? world.Settlements[prison].OwnerId : null
            : previous.OwnerId;
        bool ruler = previous.OwnerId is int owner && world.Forces[owner].LeaderId == officerId;
        if (!Remove(world, officerId, cause, rules.Personnel, events, afterDailyCommands)) return;
        if (previous.UnitId is int unit && world.Units.ContainsKey(unit) &&
            !world.Officers.Values.Any(p => p.UnitId == unit && p.IsServing))
            BattleAftermath.TerminateUnit(world, unit, null, rules, events);
        if (ruler && previous.OwnerId is int force &&
            ForceLifecycle.HasCity(world, force) &&
            (successorId is int chosen ? world.Officers[chosen] : RulerSuccession.Automatic(world, force, previous)) is { } successor)
            RulerSuccession.Commit(world, force, previous.Id, successor.Id, events);
        if (previous.OwnerId is int affected) BattleAftermath.Reconcile(world, new HashSet<int> { affected }, rules, events);
        int? recipient = recipientForce is int recipientOwner && !world.Forces[recipientOwner].IsEliminated
            ? world.Forces[recipientOwner].LeaderId : null;
        SettleTreasures(world, officerId, recipient, events);
        world.MaintainLeadership(events);
        EconomicCycle.UpdateTrust(world, rules.Economy, events);
        ResolutionPipeline.CheckConquest(world, events);
    }

    internal static bool Remove(WorldMutation world, int officerId, OfficerDeathCause cause,
        PersonnelRulebook places, List<DomainEvent> events, bool afterDailyCommands)
    {
        var previous = world.Officers[officerId];
        if (previous.Status == OfficerStatus.Dead) return false;
        PersonnelTransitions.CancelMissions(world, officerId, "person-died", events, places, afterDailyCommands);
        foreach (var force in world.Forces.Values.Where(f => f.AdviserId == officerId).ToArray())
            world.Replace(force with { AdviserId = null });
        world.Replace(world.Officers[officerId] with
        {
            Status = OfficerStatus.Dead, StatusStartYear = world.Clock.Year,
            OwnerId = null, SettlementId = null, RegionId = null, UnitId = null,
            OfficeId = null, IsCommander = false, Acted = true,
            EquippedTacticId = null, TacticCooldown = 0, RewardedThisXun = false,
            HasRecruitmentPoliticsBonus = false
        });
        events.Add(new OfficerDiedEvent(world.Clock, officerId, cause, previous.Status, previous.OwnerId,
            previous.SettlementId, previous.RegionId, previous.UnitId));
        return true;
    }

    // Called after the enclosing transaction has resolved the final recipient, if any.
    internal static void SettleTreasures(WorldMutation world, int officerId, int? recipientId, List<DomainEvent> events)
    {
        if (world.Officers[officerId].Status != OfficerStatus.Dead)
            throw new InvalidOperationException("Only a deceased person's belongings can be settled here.");
        OfficerState? recipient = recipientId is int id ? world.Officers[id] : null;
        if (recipient is not null && (recipient.Status == OfficerStatus.Dead || recipient.OwnerId is null))
            throw new InvalidOperationException("Invalid death treasure recipient.");
        foreach (var treasure in world.Treasures.Values.Where(t => t.HolderId == officerId).OrderBy(t => t.Id).ToArray())
        {
            if (recipient is not null)
                PersonnelTreasures.Transfer(world, treasure, recipient.Id, recipient.OwnerId!.Value, "person-died", events);
            else
            {
                world.Replace(treasure with { HolderId = null, Discovery = TreasureDiscovery.Discovered });
                events.Add(new TreasureDroppedEvent(world.Clock, treasure.Id, officerId));
            }
        }
    }
}

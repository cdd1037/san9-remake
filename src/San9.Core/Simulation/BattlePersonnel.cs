using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record OfficerEscapedBattleEvent(GameClock Clock, int OfficerId, int? PreviousUnitId, int SettlementId) : DomainEvent;

internal static class BattlePersonnel
{
    // The battle coordinator supplies the reachable retreat destination and nearby capture participants.
    internal static bool Resolve(WorldMutation world, int officerId, int captorUnitId, int? retreatSettlementId,
        int nearbyAttackers, BattleRank rank, bool hasEscapeMount, bool doublesCapture,
        PersonnelRulebook places, GovernanceRulebook governance, List<DomainEvent> events)
    {
        if (!world.Officers.TryGetValue(officerId, out var person) || !person.IsServing || person.OwnerId is null ||
            !world.Units.TryGetValue(captorUnitId, out var captor) || captor.OwnerId == person.OwnerId)
            throw new ArgumentException("Invalid battle personnel participants.");
        if (retreatSettlementId is int id && (!world.Settlements.TryGetValue(id, out var retreat) ||
            retreat.OwnerId != person.OwnerId || !retreat.CanHostPersons || retreat.Durability <= 0))
            throw new ArgumentException("Invalid retreat destination.");
        int chance = BattleCaptureRules.Chance(world.Abilities(person).War, nearbyAttackers, rank,
            retreatSettlementId is not null, hasEscapeMount, doublesCapture);
        bool captured = chance == 100 || chance > 0 && (int)(world.NextRandom("battle-personnel-capture") % 100) < chance;
        if (captured)
            PersonnelTransitions.Capture(world, officerId, null, captorUnitId, places, events);
        else
        {
            int destination = retreatSettlementId ?? throw new InvalidOperationException("Escape requires a retreat destination.");
            Escape(world, officerId, destination, places, governance, events);
        }
        return captured;
    }

    internal static void Escape(WorldMutation world, int officerId, int destination, PersonnelRulebook places,
        GovernanceRulebook governance, List<DomainEvent> events)
    {
        var person = world.Officers[officerId];
        if (!world.Settlements.TryGetValue(destination, out var home) || home.OwnerId != person.OwnerId ||
            home.IsDestroyed || !home.CanHostPersons || home.Durability == 0) throw new ArgumentException("Invalid escape destination.");
        PersonnelTransitions.CancelMissions(world, officerId, "person-escaped-battle", events, places, true);
        world.Replace(world.Officers[officerId] with { UnitId = null, SettlementId = destination, RegionId = null,
            Status = person.Status == OfficerStatus.Captive ? OfficerStatus.Serving : person.Status, Acted = true, IsCommander = false });
        GovernanceMaintenance.RepairCommander(world, destination, governance, events);
        events.Add(new OfficerEscapedBattleEvent(world.Clock, officerId, person.UnitId, destination));
    }
}

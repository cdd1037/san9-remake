using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record SunMarriageEvent(GameClock Clock, int BrideId, int DonorForceId, int RecipientForceId,
    int? SettlementId, int? UnitId, bool GrantedTactics) : DomainEvent;

internal static class SunMarriage
{
    internal static bool Apply(WorldMutation world, SimulationRules rules, List<DomainEvent> events)
    {
        if (rules.SunMarriage is not { } definition || !world.Clock.Boundary.Quarter ||
            world.Simulation.PendingEvent is not null || world.Simulation.TerminalReason is not null ||
            world.Clock.DayNumber > definition.LatestDate.DayNumber || world.Simulation.ConsumedEventKeys.Contains(definition.ConsumptionKey)) return false;
        if (!world.Officers.TryGetValue(definition.RecipientRulerId, out var ruler) ||
            !world.Officers.TryGetValue(definition.BrideId, out var bride)) return false;
        var forces = world.Forces.Values.Where(f => !f.IsEliminated).OrderBy(f => f.Id).ToArray();
        var donor = forces.FirstOrDefault(f => f.LeaderId is int leader && world.Officers[leader].BloodlineId == definition.DonorBloodlineId);
        if (forces.Length < definition.MinimumActiveForces || donor is null || ruler.Status != OfficerStatus.Ruler ||
            ruler.OwnerId is not int recipient || !world.Forces.TryGetValue(recipient, out var realm) || realm.IsEliminated || realm.LeaderId != ruler.Id ||
            !bride.IsServing || bride.Status == OfficerStatus.Ruler || bride.OwnerId != donor.Id ||
            (recipient != donor.Id && ForceRelations.Get(world, recipient, donor.Id).Value <= definition.RequiredRelationExclusive)) return false;

        int? settlement = ruler.SettlementId, unit = null;
        if (ruler.UnitId is int rulerUnit)
        {
            var field = world.Units[rulerUnit];
            settlement = field.ServicePlaceId;
            if (settlement is not int home || !world.Settlements.TryGetValue(home, out var service) ||
                service.IsDestroyed || !service.CanHostPersons || service.OwnerId != recipient || service.Durability <= 0)
            { settlement = null; unit = rulerUnit; }
        }
        if (settlement is int destination && (!world.Settlements.TryGetValue(destination, out var place) ||
            place.IsDestroyed || !place.CanHostPersons || place.OwnerId != recipient || place.Durability <= 0)) return false;
        if (settlement is null && unit is null) return false;
        bool grants = world.Simulation.Controls.Any(c => c.ForceId == recipient && c.Kind == ControllerKind.Player);
        // The destination and complete eligibility are fixed before any effect is written.
        PersonnelTransitions.RelocateAllegiance(world, bride.Id, recipient, definition.Loyalty, settlement, unit, rules, events,
            "sun-marriage", afterDailyCommands: false);
        if (recipient != donor.Id) ForceRelations.Change(world, recipient, donor.Id, definition.RelationBonus, events);
        if (grants)
            foreach (int person in new[] { ruler.Id, bride.Id })
                foreach (int tactic in definition.GrantedTacticIds) OfficerTacticGrowth.Grant(world, person, tactic, events);
        world.ConsumeEvent(definition.Candidate(), new("sun-marriage", EventOutcomeKind.Completed, false, EventResult.Succeeded));
        events.Add(new SunMarriageEvent(world.Clock, bride.Id, donor.Id, recipient, settlement, unit, grants));
        return true;
    }
}

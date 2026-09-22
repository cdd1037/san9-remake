using San9.Core.Domain;

namespace San9.Core.Simulation;

public enum ThreeVisitsStep { Recommendation, FirstVisit, Recruitment, Expired }
public sealed record ThreeVisitsEvent(GameClock Clock, ThreeVisitsStep Step, int RulerId, int RecruitId, int PlaceId) : DomainEvent;

internal static class ThreeVisits
{
    private static bool Consumed(WorldMutation world, PersistentEventKey key) => world.Simulation.ConsumedEventKeys.Contains(key);
    private static void Consume(WorldMutation world, PersistentEventKey key, EventTrigger trigger) =>
        world.ConsumeEvent(new(key.Value, trigger, 0, EventConsumption.OnSuccess, key),
            new(key.Value, EventOutcomeKind.Completed, false, EventResult.Succeeded));
    private static bool CompanionsPresent(WorldMutation world, ThreeVisitsDefinition definition, int force) =>
        definition.CompanionIds.All(id => world.Officers.TryGetValue(id, out var person) && person.IsServing && person.OwnerId == force);
    private static void Emit(WorldMutation world, ThreeVisitsDefinition definition, ThreeVisitsStep step, List<DomainEvent> events) =>
        events.Add(new ThreeVisitsEvent(world.Clock, step, definition.RulerId, definition.RecruitId, definition.PlaceId));

    internal static void EnterControl(WorldMutation world, SimulationRules rules, List<DomainEvent> events)
    {
        if (rules.ThreeVisits is not { } definition || world.Simulation.Boundary != SimulationBoundary.Stable ||
            world.Simulation.ActiveControl >= world.Simulation.Controls.Length || Consumed(world, definition.StartedKey)) return;
        var seat = world.Simulation.Controls[world.Simulation.ActiveControl];
        if (seat.Kind != ControllerKind.Player || world.Forces[seat.ForceId].LeaderId != definition.RulerId ||
            !world.Officers.TryGetValue(definition.RecruitId, out var recruit) || recruit.Status != OfficerStatus.Undiscovered ||
            !world.Officers.TryGetValue(definition.RecommenderId, out var recommender)) return;
        int? placeOwner = world.Settlements.TryGetValue(definition.PlaceId, out var place) ? place.OwnerId :
            rules.Personnel.RegionCities.TryGetValue(definition.PlaceId, out int city) && world.Settlements.TryGetValue(city, out var related) ? related.OwnerId : null;
        if (placeOwner != seat.ForceId || !CompanionsPresent(world, definition, seat.ForceId) ||
            !recommender.IsServing || recommender.OwnerId != seat.ForceId || recommender.LoyaltyTo(seat.ForceId) <= definition.RequiredLoyaltyExclusive) return;
        world.Replace(world.Officers[definition.RecruitId] with { Status = OfficerStatus.Special });
        Consume(world, definition.StartedKey, EventTrigger.Xun);
        world.SetSimulation(world.Simulation with { ThreeVisitsStarted = world.Clock });
        Emit(world, definition, ThreeVisitsStep.Recommendation, events);
    }

    internal static bool Expire(WorldMutation world, SimulationRules rules, List<DomainEvent> events)
    {
        if (rules.ThreeVisits is not { } definition || !Consumed(world, definition.StartedKey) || Consumed(world, definition.CompletedKey) ||
            !world.Officers.TryGetValue(definition.RecruitId, out var recruit) || recruit.Status != OfficerStatus.Special ||
            world.Simulation.ThreeVisitsStarted is { } started && world.Clock.DayNumber - started.DayNumber < definition.DurationDays) return false;
        world.Replace(world.Officers[definition.RecruitId] with { Status = OfficerStatus.Undiscovered });
        Consume(world, definition.FirstVisitKey, EventTrigger.Xun);
        Consume(world, definition.CompletedKey, EventTrigger.Xun);
        Emit(world, definition, ThreeVisitsStep.Expired, events);
        return true;
    }

    // Called only for a player-controlled direct search, before the remaining search candidates.
    internal static bool Search(WorldMutation world, PersistentTask task, ThreeVisitsDefinition? definition,
        PersonnelRulebook personnel, EconomyRulebook economy, List<DomainEvent> events)
    {
        if (definition is null || task.OfficerId != definition.RulerId || task.Journey!.DestinationPlaceId != definition.PlaceId ||
            world.Officers[task.OfficerId].Status != OfficerStatus.Ruler ||
            !world.Officers.TryGetValue(definition.RecruitId, out var recruit) || recruit.Status != OfficerStatus.Special || !CompanionsPresent(world, definition, task.ForceId)) return false;
        bool first = !Consumed(world, definition.FirstVisitKey);
        if (first ? !Consumed(world, definition.StartedKey) : Consumed(world, definition.CompletedKey)) return false;
        if (first)
            Consume(world, definition.FirstVisitKey, EventTrigger.Search);
        else
        {
            PersonnelTransitions.Recruit(world, definition.RecruitId, task.ForceId, task.OriginSettlementId, events, personnel, true,
                definition.RecruitmentLoyalty);
            PersonnelReturns.Start(world, task, personnel, events, true, definition.RecruitId);
            var force = world.Forces[task.ForceId];
            world.Replace(force with { BaseTrust = (int)Math.Min(1000L, (long)force.BaseTrust + definition.BaseTrustBonus) });
            Consume(world, definition.CompletedKey, EventTrigger.Search);
        }
        Emit(world, definition, first ? ThreeVisitsStep.FirstVisit : ThreeVisitsStep.Recruitment, events);
        PersonnelReturns.Start(world, task, personnel, events, true);
        return true;
    }
}

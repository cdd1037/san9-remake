using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record SearchHealingResolvedEvent(GameClock Clock, string EventId, int SearchingOfficerId,
    int PlaceId, int HealerOfficerId, ImmutableArray<int> HealedOfficerIds) : DomainEvent;
public sealed record BeastSearchPrompt(Guid TaskId, int Step, string EventId, int ForceId, int OfficerId, int PlaceId,
    int BattleChance);
public sealed record BeastSearchResolvedEvent(GameClock Clock, string EventId, int OfficerId, int PlaceId,
    EventResult Result, int BattleChance, int? Roll, int? LearnedTacticId, int Injury, int LifespanTier) : DomainEvent;

internal static class SearchEvents
{
    internal static bool Prepare(WorldMutation world, PersistentTask task, SimulationRules simulationRules, List<DomainEvent> events,
        out SearchEncounter? encounter)
    {
        encounter = null;
        var rules = simulationRules.Personnel;
        // Eligibility owns the position of its probability gate relative to site/consumption checks.
        foreach (var candidate in EventSelection.Order(rules.SearchEvents.Values.Select(e => new EventCandidate(e.Id, EventTrigger.Search, e.Priority)),
            EventTrigger.Search))
        {
            if (rules.SearchEvents[candidate.Id] is TreasureMonumentSearchEventDefinition monument &&
                TreasureMonument.Eligible(world, task, simulationRules, monument))
            {
                encounter = new TreasureMonumentEncounter(monument.Id);
                return true;
            }
            if (rules.SearchEvents[candidate.Id] is ImperialCeremonySearchEventDefinition ceremony &&
                ImperialCeremony.TryResolve(world, task, ceremony, simulationRules.Economy, events)) return true;
            if (rules.SearchEvents[candidate.Id] is HealingSearchEventDefinition healing &&
                !Consumed(world, healing.ConsumptionKey) &&
                TryHeal(world, task, rules, healing, events))
            {
                world.ConsumeEvent(healing.Candidate(healing.ConsumptionKey), new(candidate.Id, EventOutcomeKind.Completed, false, EventResult.Succeeded));
                return true;
            }
            if (rules.SearchEvents[candidate.Id] is ScholarReturnSearchEventDefinition scholar && ScholarReturn.Prepare(world, task, scholar))
            {
                encounter = new ScholarReturnEncounter(scholar.Id);
                return true;
            }
            if (rules.SearchEvents[candidate.Id] is BeastSearchEventDefinition beast)
            {
                int chance = world.Abilities(world.Officers[task.OfficerId]).War / beast.WarChanceDivisor;
                if ((int)(world.NextRandom("event-search-eligibility") % 100) >= chance) continue;
                var selected = new BeastSearchEncounter(beast.Id);
                if (!CanResolveBeast(world, task, rules, selected)) continue;
                encounter = selected;
                return true;
            }
        }
        return false;
    }

    private static bool Consumed(WorldMutation world, PersistentEventKey? key) =>
        key is PersistentEventKey value && world.Simulation.ConsumedEventKeys.Contains(value);

    internal static bool CanResolveBeast(WorldMutation world, PersistentTask task, PersonnelRulebook rules, BeastSearchEncounter encounter) =>
        rules.SearchEvents.GetValueOrDefault(encounter.EventId) is BeastSearchEventDefinition definition &&
        definition.Sites.SingleOrDefault(s => s.PlaceId == task.Journey!.DestinationPlaceId) is { } site &&
        !Consumed(world, site.ConsumptionKey) && world.Officers[task.OfficerId].Injury < 5;

    internal static int BattleChance(WorldMutation world, int officerId, BeastSearchEventDefinition definition) =>
        definition.BattleTacticIds.Any(world.Officers[officerId].LearnedTactics.Contains)
            ? Math.Max(10, world.Abilities(world.Officers[officerId]).War / 5) : 0;

    internal static void ResolveBeast(WorldMutation world, PersistentTask task, PersonnelRulebook rules, EconomyRulebook economy,
        BeastSearchEncounter encounter, bool fight, List<DomainEvent> events)
    {
        if (!CanResolveBeast(world, task, rules, encounter)) throw new InvalidOperationException("Beast encounter is no longer valid.");
        var definition = (BeastSearchEventDefinition)rules.SearchEvents[encounter.EventId];
        var site = definition.Sites.Single(s => s.PlaceId == task.Journey!.DestinationPlaceId);
        int chance = BattleChance(world, task.OfficerId, definition);
        int? roll = fight ? (int)(world.NextRandom("event-beast-battle") % 100) : null;
        var result = !fight ? EventResult.Escaped : roll < chance ? EventResult.Succeeded : EventResult.Defeated;
        int? learned = null;
        if (result == EventResult.Succeeded)
        {
            OfficerAbilityGrowth.Increase(world, task.OfficerId, OfficerAbility.War, definition.WarReward, events);
            learned = site.RewardTacticIds.Where(id => !world.Officers[task.OfficerId].LearnedTactics.Contains(id)).Select(id => (int?)id).FirstOrDefault();
            if (learned is int tactic) OfficerTacticGrowth.Grant(world, task.OfficerId, tactic, events);
            var force = world.Forces[task.ForceId];
            world.Replace(force with { BaseTrust = (int)Math.Min(1000L, (long)force.BaseTrust + definition.BaseTrustReward) });
            EconomicCycle.UpdateTrust(world, economy, events);
        }
        else if (result == EventResult.Defeated)
        {
            var person = world.Officers[task.OfficerId];
            world.Replace(person with { Injury = definition.DefeatInjury,
                LifespanTier = definition.ReduceLifespanOnDefeat ? Math.Max(0, person.LifespanTier - 1) : person.LifespanTier });
        }
        world.ConsumeEvent(definition.Candidate(site.ConsumptionKey), new(definition.Id, EventOutcomeKind.Completed, false, result));
        var after = world.Officers[task.OfficerId];
        events.Add(new BeastSearchResolvedEvent(world.Clock, definition.Id, task.OfficerId, site.PlaceId, result, chance, roll,
            learned, after.Injury, after.LifespanTier));
    }

    private static bool TryHeal(WorldMutation world, PersistentTask task, PersonnelRulebook rules,
        HealingSearchEventDefinition definition, List<DomainEvent> events)
    {
        int place = task.Journey!.DestinationPlaceId;
        int? owner = world.Settlements.TryGetValue(place, out var settlement) ? settlement.OwnerId :
            rules.RegionCities.TryGetValue(place, out int city) && world.Settlements.TryGetValue(city, out var related) ? related.OwnerId : null;
        if (owner != task.ForceId) return false;
        int chance = world.Abilities(world.Officers[task.OfficerId]).War / definition.WarChanceDivisor;
        if ((int)(world.NextRandom("event-search-eligibility") % 100) >= chance) return false;
        var injured = world.Officers.Values.Where(p => p.OwnerId == task.ForceId &&
            (p.IsServing || p.Status == OfficerStatus.Captive) && p.Injury > 0).OrderBy(p => p.Id).ToArray();
        if (injured.Length == 0 || !world.Officers.TryGetValue(definition.HealerOfficerId, out var healer) ||
            healer.Status == OfficerStatus.Dead || definition.ExcludedSearchingOfficerIds.Contains(task.OfficerId)) return false;
        foreach (var person in injured)
        {
            world.Replace(person with { Injury = 0 });
            events.Add(new OfficerInjuryRecoveredEvent(world.Clock, person.Id, person.Injury, 0));
        }
        events.Add(new SearchHealingResolvedEvent(world.Clock, definition.Id, task.OfficerId, place,
            definition.HealerOfficerId, injured.Select(p => p.Id).ToImmutableArray()));
        return true;
    }
}

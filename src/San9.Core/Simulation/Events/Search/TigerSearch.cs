using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record TigerSearchPrompt(Guid TaskId, int Step, string EventId, int ForceId, int OfficerId, int PlaceId, int BattleChance);
public sealed record TigerSearchResolvedEvent(GameClock Clock, string EventId, int ForceId, int OfficerId, int PlaceId,
    bool Accepted, EventResult Result, int BattleChance, int? Roll, int War, int Injury, int LifespanTier, int BaseTrust) : DomainEvent;

internal static class TigerSearch
{
    private static bool BeforeChance(WorldMutation world, PersistentTask task, PersonnelRulebook rules, TigerSearchEventDefinition d)
    {
        if (!world.Forces.TryGetValue(task.ForceId, out var force) || force.IsEliminated ||
            !world.Simulation.Controls.Any(c => c.ForceId == force.Id && c.Kind == ControllerKind.Player) || task.Journey is null) return false;
        int place = task.Journey.DestinationPlaceId;
        int city = rules.RegionCities.GetValueOrDefault(place, place);
        return rules.CityProvinces.TryGetValue(city, out int province) && d.ProvinceIds.Contains(province);
    }

    private static bool AfterChance(WorldMutation world, PersistentTask task, TigerSearchEventDefinition d) =>
        world.Forces[task.ForceId].BaseTrust < d.MaximumBaseTrustExclusive && world.Officers[task.OfficerId].Injury < d.MaximumInjuryExclusive;

    internal static bool Prepare(WorldMutation world, PersistentTask task, PersonnelRulebook rules, TigerSearchEventDefinition d) =>
        BeforeChance(world, task, rules, d) &&
        world.NextRandom("event-search-eligibility") % 100 < (ulong)(world.Abilities(world.Officers[task.OfficerId]).War / d.WarChanceDivisor) &&
        AfterChance(world, task, d);

    internal static bool CanResolve(WorldMutation world, PersistentTask task, SimulationRules rules, TigerSearchEncounter encounter) =>
        rules.Personnel.SearchEvents.GetValueOrDefault(encounter.EventId) is TigerSearchEventDefinition d &&
        BeforeChance(world, task, rules.Personnel, d) && AfterChance(world, task, d);

    internal static int BattleChance(WorldMutation world, int officerId, TigerSearchEventDefinition d) =>
        Math.Max(d.MinimumBattleChance, world.Abilities(world.Officers[officerId]).War);

    internal static void Resolve(WorldMutation world, PersistentTask task, SimulationRules rules,
        TigerSearchEncounter encounter, bool accept, List<DomainEvent> events)
    {
        if (!CanResolve(world, task, rules, encounter)) throw new InvalidOperationException("Tiger encounter is no longer valid.");
        var d = (TigerSearchEventDefinition)rules.Personnel.SearchEvents[encounter.EventId];
        int chance = BattleChance(world, task.OfficerId, d);
        int? roll = accept ? (int)(world.NextRandom("event-tiger-battle") % 100) : null;
        bool won = accept && roll < chance;
        var force = world.Forces[task.ForceId];
        if (won)
        {
            OfficerAbilityGrowth.Increase(world, task.OfficerId, OfficerAbility.War, d.WarReward, events);
            world.Replace(force with { BaseTrust = (int)Math.Min(1000L, (long)force.BaseTrust + d.BaseTrustReward) });
        }
        else
        {
            world.Replace(force with { BaseTrust = Math.Max(0, force.BaseTrust - d.BaseTrustPenalty) });
            if (accept) OfficerInjuries.Apply(world, world.Officers[task.OfficerId] with { Injury = d.DefeatInjury }, events);
        }
        var actor = world.Officers[task.OfficerId];
        events.Add(new TigerSearchResolvedEvent(world.Clock, d.Id, task.ForceId, actor.Id, task.Journey!.DestinationPlaceId,
            accept, won ? EventResult.Succeeded : accept ? EventResult.Defeated : EventResult.Declined, chance, roll,
            actor.BaseAbilities.War, actor.Injury, actor.LifespanTier, world.Forces[force.Id].BaseTrust));
    }
}

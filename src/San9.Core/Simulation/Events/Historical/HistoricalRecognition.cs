using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record FiveTigersRecognizedEvent(GameClock Clock, int ForceId, ImmutableArray<int> OfficerIds, int BaseTrustGain) : DomainEvent;
public sealed record ExpeditionMemorialEvent(GameClock Clock, int ForceId, int OfficerId,
    ImmutableArray<int> RewardedOfficerIds, int BaseTrustGain, bool GrantedTactics) : DomainEvent;

internal static class HistoricalRecognition
{
    private static bool Ready(WorldMutation world, PersistentEventKey key, int minimumDays = 0) =>
        world.Simulation.NewGame.Mode == GameMode.Historical && world.Clock.Boundary.Quarter && world.Simulation.PendingEvent is null && world.Simulation.TerminalReason is null &&
        world.Clock.DayNumber - world.Simulation.Started.DayNumber >= minimumDays && !world.Simulation.ConsumedEventKeys.Contains(key);

    internal static bool ApplyHanzhongKing(WorldMutation world, SimulationRules rules, List<DomainEvent> events)
    {
        if (rules.HanzhongKing is not { } definition || !Ready(world, definition.ConsumptionKey, definition.MinimumElapsedDays) ||
            world.Simulation.ImperialCourtEnded) return false;
        if (!world.Officers.TryGetValue(definition.RulerId, out var ruler)) return false;
        if (ruler.Status != OfficerStatus.Ruler || ruler.OwnerId is not int owner ||
            !world.Forces.TryGetValue(owner, out var force) || force.IsEliminated || force.LeaderId != ruler.Id ||
            force.Trust < definition.RequiredTrust || force.RankId is not int current ||
            rules.Politics.Ranks[current].Precedence >= rules.Politics.Ranks[definition.RankId].Precedence ||
            !world.Settlements.TryGetValue(definition.CityId, out var city) || city.IsDestroyed || city.OwnerId != owner) return false;
        PoliticalEvents.GrantRank(world, rules, owner, definition.RankId, force.CountryTitleId,
            PeerageChangeKind.HanzhongKing, definition.BaseTrustBonus, events);
        Complete(world, definition.Candidate());
        return true;
    }

    internal static bool ApplyFiveTigers(WorldMutation world, SimulationRules rules, List<DomainEvent> events)
    {
        if (rules.FiveTigers is not { } definition || !Ready(world, definition.ConsumptionKey, definition.MinimumElapsedDays)) return false;
        if (definition.OfficerIds.Any(id => !world.Officers.ContainsKey(id))) return false;
        var officers = definition.OfficerIds.Select(id => world.Officers[id]).ToArray();
        if (officers[0].OwnerId is not int owner || !world.Forces.TryGetValue(owner, out var force) || force.IsEliminated ||
            !world.Simulation.Controls.Any(c => c.ForceId == owner && c.Kind == ControllerKind.Player) ||
            officers.Any(p => p.OwnerId != owner || p.Status == OfficerStatus.Ruler || !(p.IsServing || p.Status == OfficerStatus.Captive))) return false;
        int trustGain = AddTrust(world, force, definition.BaseTrustBonus);
        foreach (var person in officers)
        {
            AddLoyalty(world, person, owner, definition.LoyaltyBonus, "five-tigers", events);
            foreach (int tactic in definition.GrantedTacticIds) OfficerTacticGrowth.Grant(world, person.Id, tactic, events);
        }
        Complete(world, definition.Candidate());
        events.Add(new FiveTigersRecognizedEvent(world.Clock, owner, definition.OfficerIds, trustGain));
        return true;
    }

    internal static bool ApplyExpeditionMemorial(WorldMutation world, SimulationRules rules, List<DomainEvent> events)
    {
        if (rules.ExpeditionMemorial is not { } definition || !Ready(world, definition.ConsumptionKey)) return false;
        if (!world.Officers.TryGetValue(definition.OfficerId, out var author)) return false;
        if (!author.IsServing || author.Status == OfficerStatus.Ruler || author.OwnerId is not int owner ||
            !world.Forces.TryGetValue(owner, out var force) || force.IsEliminated || force.CountryTitleId != definition.CountryTitleId ||
            !world.Settlements.TryGetValue(definition.OpposingCityId, out var opposing) || opposing.IsDestroyed ||
            opposing.OwnerId is not int rival || rival == owner || !world.Forces.TryGetValue(rival, out var enemy) || enemy.IsEliminated ||
            !PoliticalEvents.ControlledProvinces(world, rules.Personnel, owner).Contains(definition.ProvinceId)) return false;
        var officers = world.Officers.Values.Where(p => p.OwnerId == owner && p.IsServing).OrderBy(p => p.Id).ToArray();
        bool grants = world.Simulation.Controls.Any(c => c.ForceId == owner && c.Kind == ControllerKind.Player);
        int trustGain = AddTrust(world, force, definition.BaseTrustBonus);
        foreach (var person in officers)
        {
            if (person.Status != OfficerStatus.Ruler) AddLoyalty(world, person, owner, definition.LoyaltyBonus, "expedition-memorial", events);
            if (grants)
                foreach (int tactic in definition.GrantedTacticIds) OfficerTacticGrowth.Grant(world, person.Id, tactic, events);
        }
        Complete(world, definition.Candidate());
        events.Add(new ExpeditionMemorialEvent(world.Clock, owner, author.Id, officers.Select(p => p.Id).ToImmutableArray(), trustGain, grants));
        return true;
    }

    private static int AddTrust(WorldMutation world, ForceState force, int bonus)
    {
        int gain = Math.Min(bonus, 1000 - force.BaseTrust);
        world.Replace(force with { BaseTrust = force.BaseTrust + gain });
        return gain;
    }

    private static void AddLoyalty(WorldMutation world, OfficerState person, int owner, int bonus, string reason, List<DomainEvent> events) =>
        RecruitmentRules.SetLoyalty(world, person.Id, owner, Math.Min(255, person.LoyaltyTo(owner) + bonus), reason, events);

    private static void Complete(WorldMutation world, EventCandidate candidate) =>
        world.ConsumeEvent(candidate, new(candidate.Id, EventOutcomeKind.Completed, false, EventResult.Succeeded));
}

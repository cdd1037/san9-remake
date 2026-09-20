using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record ShanyangGongPrompt(Guid TaskId, int Step, int ForceId, int SearchingOfficerId,
    int EmperorId, int PlaceId, bool ChoosingCountryTitle, ImmutableArray<int> CountryTitleIds);
public sealed record ShanyangGongEvent(GameClock Clock, int ForceId, int SearchingOfficerId,
    int EmperorId, bool Protected, int? CountryTitleId) : DomainEvent;

internal static class ShanyangGong
{
    internal static bool Eligible(WorldMutation world, PersistentTask task, SimulationRules rules)
    {
        if (rules.ShanyangGong is not { } d || task.Journey?.DestinationPlaceId != d.PlaceId ||
            !world.Simulation.ImperialCourtEnded || world.Simulation.ConsumedEventKeys.Contains(d.ConsumptionKey) ||
            world.Officers[d.EmperorId].Status == OfficerStatus.Dead ||
            !world.Forces.TryGetValue(task.ForceId, out var force) || force.IsEliminated || force.HasDeposedEmperor ||
            force.LeaderId is not int leader || force.Trust < d.MinimumTrust ||
            !world.Simulation.Controls.Any(c => c.ForceId == force.Id && c.Kind == ControllerKind.Player) ||
            force.RankId == rules.Politics.ImperialRankId && world.Officers[leader].Surname != d.ImperialSurname)
            return false;
        return PoliticalEvents.ControlledProvinces(world, rules.Personnel, force.Id).Count() >= d.MinimumControlledProvinces;
    }

    internal static ImmutableArray<int> Candidates(WorldMutation world, SimulationRules rules, int forceId) =>
        world.Forces[forceId].CountryTitleId is not null ? [] : rules.Politics.ManualCandidates(world.Forces.Values
            .Where(f => !f.IsEliminated && f.CountryTitleId is not null).Select(f => f.CountryTitleId!.Value).ToHashSet());

    internal static PersistentTask? Resolve(WorldMutation world, PersistentTask task, SimulationRules rules,
        bool protect, int? titleId, List<DomainEvent> events)
    {
        var encounter = (ShanyangGongEncounter)task.SearchEncounter!;
        var candidates = Candidates(world, rules, task.ForceId);
        if (protect && !encounter.ChoosingCountryTitle && !candidates.IsEmpty)
            return task with { SearchEncounter = new ShanyangGongEncounter(true) };
        if (encounter.ChoosingCountryTitle && titleId is null)
            throw new InvalidOperationException("Country title selection is required.");
        Commit(world, task, rules, protect, titleId ?? world.Forces[task.ForceId].CountryTitleId, events);
        PersonnelReturns.Start(world, task, rules.Personnel, events, true);
        return null;
    }

    private static void Commit(WorldMutation world, PersistentTask task, SimulationRules rules, bool protect,
        int? titleId, List<DomainEvent> events)
    {
        var d = rules.ShanyangGong!;
        int owner = task.ForceId;
        if (protect)
        {
            var recipients = world.Officers.Values.Where(p => p.OwnerId == owner && p.IsServing && p.Status != OfficerStatus.Ruler)
                .Select(p => p.Id).ToHashSet();
            foreach (int id in recipients.Order())
                RecruitmentRules.SetLoyalty(world, id, owner, (int)Math.Min(255L, (long)world.Officers[id].LoyaltyTo(owner) + d.LoyaltyBonus),
                    "shanyang-gong-protection", events);
            PoliticalEvents.GrantRank(world, rules, owner, rules.Politics.ImperialRankId!.Value, titleId,
                PeerageChangeKind.ShanyangGong, d.BaseTrustBonus, events, recipients);
        }
        else
        {
            OfficerDeath.Resolve(world, d.EmperorId, OfficerDeathCause.HistoricalEvent, rules, events, afterDailyCommands: true);
            foreach (var person in world.Officers.Values.Where(p => p.OwnerId == owner && p.IsServing &&
                p.Status != OfficerStatus.Ruler && p.Righteousness >= d.MinimumRighteousness).OrderBy(p => p.Id).ToArray())
                RecruitmentRules.SetLoyalty(world, person.Id, owner, Math.Max(0, person.LoyaltyTo(owner) - d.ExecutionLoyaltyPenalty),
                    "shanyang-gong-execution", events);
        }
        foreach (var rival in world.Forces.Values.Where(f => f.Id != owner && !f.IsEliminated && f.LeaderId is not null).OrderBy(f => f.Id))
            if (protect ? rival.RankId == rules.Politics.ImperialRankId : world.Officers[rival.LeaderId!.Value].Surname == d.ImperialSurname)
                ForceRelations.Change(world, owner, rival.Id, -d.RelationPenalty, events);
        var candidate = new EventCandidate(d.ConsumptionKey.Value, EventTrigger.Search, 0, EventConsumption.OnSuccess, d.ConsumptionKey);
        world.ConsumeEvent(candidate, new(candidate.Id, EventOutcomeKind.Completed, false, EventResult.Succeeded));
        events.Add(new ShanyangGongEvent(world.Clock, owner, task.OfficerId, d.EmperorId, protect, world.Forces[owner].CountryTitleId));
    }
}

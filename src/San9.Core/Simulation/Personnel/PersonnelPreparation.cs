using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record PersonnelJourneyPreview(CommandRejection Rejection, int? Days, CommandAdvice Advice)
{
    public int? DistanceDays { get; init; }
    public int? RecruitmentThreshold { get; init; }
    public ThreeVisitsStep? SearchAdvice { get; init; }
    public bool Available => Rejection == CommandRejection.None;
}

public sealed record PersonnelAdvicePreview(CommandAdvice Advice, ThreeVisitsStep? SearchAdvice = null);

internal static class PersonnelSearchChances
{
    internal static long PoliticsScore(int politics) => Math.Max(1, Math.Min(politics, 60) / 2) + Math.Max(0, Math.Min(politics, 80) - 60)
        + 2L * Math.Max(0, Math.Min(politics, 90) - 80) + 3L * Math.Max(0, politics - 90);
    internal static long Person(long score, int count) => 2 * score * (long)Math.Sqrt(100L * count) / 30;
    internal static long Treasure(long score, int count) => score * (long)Math.Sqrt(100L * count) / 90;
}

public sealed partial class SimulationEngine
{
    /// <summary>Public recruitment forecast; shares the resolution formula and never draws or starts a mission.</summary>
    public int GetRecruitmentThreshold(int officerId, int targetOfficerId)
    {
        var world = PreparationWorld();
        if (!world.Officers.TryGetValue(officerId, out var actor) || actor.IsDormant || !actor.IsVisible || actor.OwnerId is not int owner ||
            world.Forces[owner].LeaderId is null || !world.Officers.TryGetValue(targetOfficerId, out var target) ||
            target.IsDormant || !target.IsVisible)
            throw new ArgumentException("Recruitment forecast requires an affiliated visible actor and a visible target.");
        return RecruitmentRules.Threshold(world, officerId, targetOfficerId, _rules);
    }

    public ImmutableArray<int> GetRecruitTargets(int forceId, int officerId)
    {
        if (_world.IsExecuting) throw new InvalidOperationException("Simulation is executing.");
        var world = PreparationWorld();
        return world.Officers.Keys.Where(id => PersonnelMissions.ValidateRecruit(world,
            new(Guid.Empty, forceId, 0, [officerId], id), _rules, out _, out _) == CommandRejection.None).Order().ToImmutableArray();
    }

    /// <summary>Read-only sparse comparisons. Shared qualifications and discovery counts are evaluated once per batch; no routes are calculated.</summary>
    public ImmutableDictionary<(int Actor, int Target), PersonnelAdvicePreview> GetPersonnelAdvice(int forceId,
        PersonnelJourneyKind kind, ImmutableArray<(int Actor, int Target)> comparisons)
    {
        if (comparisons.IsDefault) throw new ArgumentException("Personnel advice requires explicit comparisons.", nameof(comparisons));
        if (kind is not (PersonnelJourneyKind.Search or PersonnelJourneyKind.Recruit)) throw new ArgumentOutOfRangeException(nameof(kind));
        var world = PreparationWorld();
        if (!world.Forces.ContainsKey(forceId)) throw new ArgumentException("Unknown personnel force.", nameof(forceId));
        var pairs = comparisons.Distinct().ToArray();
        var actors = pairs.Select(p => p.Actor).Distinct().ToDictionary(id => id, id => world.Forces[forceId].LeaderId is null
            ? CommandRejection.PersonUnavailable : PersonnelMissions.ActorEligibility(world, forceId, id, _rules));
        var targets = pairs.Select(p => p.Target).Distinct().ToDictionary(id => id, id => kind == PersonnelJourneyKind.Recruit
            ? PersonnelMissions.RecruitTargetEligibility(world, forceId, id, _rules, out _, out _)
            : PersonnelMissions.CanSearch(world, forceId, id, _rules.Personnel) ? CommandRejection.None : CommandRejection.InvalidTask);
        Dictionary<int, int> people = [], legacyItems = [], equipment = [];
        if (kind == PersonnelJourneyKind.Search)
        {
            people = world.Officers.Values.Where(p => p.Status == OfficerStatus.Undiscovered)
                .Where(p => (p.SettlementId ?? p.RegionId) is not null).GroupBy(p => (p.SettlementId ?? p.RegionId)!.Value)
                .ToDictionary(g => g.Key, g => g.Count());
            legacyItems = world.Treasures.Values.Where(t => t.Discovery == TreasureDiscovery.Buried && t.HolderId is null &&
                    t.DiscoveryRegionId is not null && _rules.Treasures.Items.ContainsKey(t.Id))
                .GroupBy(t => t.DiscoveryRegionId!.Value).ToDictionary(g => g.Key, g => g.Count());
            equipment = world.AllEquipment.Values.Where(item => item.IsLost && item.SearchPlaceId is not null &&
                    _rules.Equipment.Items.ContainsKey(item.DefinitionId)).GroupBy(item => item.SearchPlaceId!.Value)
                .ToDictionary(group => group.Key, group => group.Count());
        }
        var result = ImmutableDictionary.CreateBuilder<(int Actor, int Target), PersonnelAdvicePreview>();
        foreach (var (actor, target) in pairs)
        {
            var rejection = actors[actor] != CommandRejection.None ? actors[actor] : targets[target];
            result[(actor, target)] = rejection != CommandRejection.None
                ? new(new(rejection, null, AdviserOpinion.Unavailable))
                : kind == PersonnelJourneyKind.Recruit
                    ? EvaluateRecruitmentAdvice(world, forceId, actor, target, RecruitmentRules.Threshold(world, actor, target, _rules))
                    : EvaluateSearchAdvice(world, forceId, actor, target, people.GetValueOrDefault(target),
                        checked(equipment.GetValueOrDefault(target) + (PersonnelMissions.DirectSearch(world, forceId,
                            world.Settlements[world.Officers[actor].SettlementId!.Value]) ? legacyItems.GetValueOrDefault(target) : 0)));
        }
        return result.ToImmutable();
    }

    private static PersonnelAdvicePreview EvaluateRecruitmentAdvice(WorldMutation world, int forceId, int actor, int target, int threshold) =>
        new(AdviserJudgment.Evaluate(world, forceId, ConditionalRandomPurpose.Recruitment, actor, target, threshold));

    private PersonnelAdvicePreview EvaluateSearchAdvice(WorldMutation world, int forceId, int actor, int target, int people, int items)
    {
        long score = PersonnelSearchChances.PoliticsScore(world.Abilities(world.Officers[actor]).Politics);
        var advice = AdviserJudgment.Evaluate(world, forceId, ConditionalRandomPurpose.SearchDiscovery, actor, target,
            Math.Max(PersonnelSearchChances.Person(score, people), PersonnelSearchChances.Treasure(score, items)));
        if (people == 0 && items == 0 && advice.AdviserId is not null) advice = advice with { Opinion = AdviserOpinion.Unfavorable };
        ThreeVisitsStep? special = null;
        if (_rules.ThreeVisits is { } visits && advice.AdviserId == visits.RecommenderId)
        {
            special = ThreeVisits.SearchStep(world, actor, forceId, target, visits);
            if (special is not null) advice = advice with { Opinion = AdviserOpinion.Favorable };
        }
        return new(advice, special);
    }

    public ImmutableArray<int> GetSearchPlaces(int forceId)
    {
        if (_world.IsExecuting) throw new InvalidOperationException("Simulation is executing.");
        var world = PreparationWorld();
        return _rules.Personnel.Places.Keys.Where(id => PersonnelMissions.CanSearch(world, forceId, id, _rules.Personnel)).Order().ToImmutableArray();
    }

    /// <summary>Validates against the submission rules without creating tasks or consuming random streams.</summary>
    public PersonnelJourneyPreview GetPersonnelJourney(int forceId, int officerId, int targetId, PersonnelJourneyKind kind,
        bool includeAdvice = true)
    {
        if (_world.IsExecuting) throw new InvalidOperationException("Simulation is executing.");
        var world = PreparationWorld();
        int destination = targetId;
        UnitState? unit = null;
        CommandRejection rejection = kind switch
        {
            PersonnelJourneyKind.Move => PersonnelMissions.ValidateMove(world, new(Guid.Empty, forceId, 0, [officerId], targetId), _rules.Personnel, _rules.Military),
            PersonnelJourneyKind.Search => PersonnelMissions.ValidateSearch(world, new(Guid.Empty, forceId, 0, [officerId], targetId), _rules.Personnel, _rules.Military),
            PersonnelJourneyKind.Recruit => PersonnelMissions.ValidateRecruit(world, new(Guid.Empty, forceId, 0, [officerId], targetId), _rules, out destination, out unit),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        if (rejection != CommandRejection.None) return new(rejection, null, new(rejection, null, AdviserOpinion.Unavailable));
        int origin = world.Officers[officerId].SettlementId!.Value;
        var route = PersonnelLocations.Route(world, _rules.Personnel, origin, destination, unit?.Position);
        var advice = new CommandAdvice(CommandRejection.None, null, AdviserOpinion.Unavailable);
        ThreeVisitsStep? searchAdvice = null;
        int? threshold = null;
        if (kind == PersonnelJourneyKind.Recruit)
        {
            threshold = RecruitmentRules.Threshold(world, officerId, targetId, _rules);
            if (includeAdvice)
                advice = EvaluateRecruitmentAdvice(world, forceId, officerId, targetId, threshold.Value).Advice;
        }
        if (kind == PersonnelJourneyKind.Search && includeAdvice)
        {
            int people = world.Officers.Values.Count(p => p.Status == OfficerStatus.Undiscovered && (p.SettlementId ?? p.RegionId) == targetId);
            int items = PersonnelMissions.DirectSearch(world, forceId, world.Settlements[world.Officers[officerId].SettlementId!.Value])
                ? world.Treasures.Values.Count(t => t.Discovery == TreasureDiscovery.Buried && t.HolderId is null &&
                    t.DiscoveryRegionId == targetId && _rules.Treasures.Items.ContainsKey(t.Id)) : 0;
            items = checked(items + world.AllEquipment.Values.Count(item => item.IsLost && item.SearchPlaceId == targetId &&
                _rules.Equipment.Items.ContainsKey(item.DefinitionId)));
            var search = EvaluateSearchAdvice(world, forceId, officerId, targetId, people, items);
            advice = search.Advice;
            searchAdvice = search.SearchAdvice;
        }
        return new(CommandRejection.None, kind == PersonnelJourneyKind.Search ? route.XunRoundedDays : route.Days, advice) { DistanceDays = route.Days, SearchAdvice = searchAdvice, RecruitmentThreshold = threshold };
    }
}

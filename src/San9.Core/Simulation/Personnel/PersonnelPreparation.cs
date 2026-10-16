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
            new(Guid.Empty, forceId, 0, officerId, id), _rules, out _, out _) == CommandRejection.None).Order().ToImmutableArray();
    }

    public ImmutableArray<int> GetSearchPlaces(int forceId)
    {
        if (_world.IsExecuting) throw new InvalidOperationException("Simulation is executing.");
        var world = PreparationWorld();
        return _rules.Personnel.Places.Keys.Where(id => PersonnelMissions.CanSearch(world, forceId, id, _rules.Personnel)).Order().ToImmutableArray();
    }

    /// <summary>Validates against the submission rules without creating tasks or consuming random streams.</summary>
    public PersonnelJourneyPreview GetPersonnelJourney(int forceId, int officerId, int targetId, PersonnelJourneyKind kind)
    {
        if (_world.IsExecuting) throw new InvalidOperationException("Simulation is executing.");
        var world = PreparationWorld();
        int destination = targetId;
        UnitState? unit = null;
        CommandRejection rejection = kind switch
        {
            PersonnelJourneyKind.Move => PersonnelMissions.ValidateMove(world, new(Guid.Empty, forceId, 0, [officerId], targetId), _rules.Personnel),
            PersonnelJourneyKind.Search => PersonnelMissions.ValidateSearch(world, new(Guid.Empty, forceId, 0, officerId, targetId), _rules.Personnel),
            PersonnelJourneyKind.Recruit => PersonnelMissions.ValidateRecruit(world, new(Guid.Empty, forceId, 0, officerId, targetId), _rules, out destination, out unit),
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
            advice = AdviserJudgment.Evaluate(world, forceId, ConditionalRandomPurpose.Recruitment, officerId, targetId, threshold.Value);
        }
        if (kind == PersonnelJourneyKind.Search)
        {
            long score = PersonnelSearchChances.PoliticsScore(world.Abilities(world.Officers[officerId]).Politics);
            int people = world.Officers.Values.Count(p => p.Status == OfficerStatus.Undiscovered && (p.SettlementId ?? p.RegionId) == targetId);
            int items = world.Treasures.Values.Count(t => t.Discovery == TreasureDiscovery.Buried && t.HolderId is null &&
                t.DiscoveryRegionId == targetId && _rules.Treasures.Items.ContainsKey(t.Id));
            advice = AdviserJudgment.Evaluate(world, forceId, ConditionalRandomPurpose.SearchDiscovery, officerId, targetId,
                Math.Max(PersonnelSearchChances.Person(score, people), PersonnelSearchChances.Treasure(score, items)));
            if (people == 0 && items == 0 && advice.AdviserId is not null) advice = advice with { Opinion = AdviserOpinion.Unfavorable };
            if (_rules.ThreeVisits is {} visits && advice.AdviserId == visits.RecommenderId)
            {
                searchAdvice = ThreeVisits.SearchStep(world,officerId,forceId,targetId,visits);
                if (searchAdvice is not null) advice = advice with { Opinion = AdviserOpinion.Favorable };
            }
        }
        return new(CommandRejection.None, kind == PersonnelJourneyKind.Search ? route.XunRoundedDays : route.Days, advice) { DistanceDays = route.Days, SearchAdvice = searchAdvice, RecruitmentThreshold = threshold };
    }
}

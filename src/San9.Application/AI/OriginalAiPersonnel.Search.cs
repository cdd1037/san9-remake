using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

internal sealed partial class OriginalAiPersonnel
{
    private const int MaximumSearchJourneyDays = 255;

    private OriginalAiWorkReport Search(int limit)
    {
        int used = 0, submitted = 0, rejected = 0, empty = 0;
        var corps = Corps;
        var settlements = World.Settlements.Where(s => !s.IsDestroyed).ToDictionary(s => s.Id);
        var legalPlaces = engine.GetSearchPlaces(Owner);
        var district = legalPlaces.Where(id =>
        {
            if (settlements.TryGetValue(id, out var built)) return BelongsToCorps(built, corps);
            return content.RegionCityIds.TryGetValue(id, out int cityId) &&
                settlements.TryGetValue(cityId, out var city) && !city.IsDormant && BelongsToCorps(city, corps);
        }).ToHashSet();
        district.IntersectWith(World.Officers.Where(p => !p.IsDormant && p.Status == OfficerStatus.Undiscovered)
            .Select(p => p.SettlementId ?? p.RegionId).OfType<int>());
        var targets = legalPlaces.Where(district.Contains).ToList();
        var actors = Actors(45).Select(p => p.Id).Where(actor => targets.Any(target =>
            engine.GetPersonnelJourney(Owner, actor, target, PersonnelJourneyKind.Search, includeAdvice: false)
                .DistanceDays is >= 0 and <= MaximumSearchJourneyDays)).ToList();
        while (used < limit && targets.Count > 0 && actors.Count > 0 && World.Simulation.Boundary == SimulationBoundary.Stable)
        {
            (int Actor, int Target)? pair = null;
            foreach (int target in targets)
            {
                foreach (int actor in actors)
                {
                    var preview = engine.GetPersonnelJourney(Owner, actor, target, PersonnelJourneyKind.Search);
                    if (!preview.Available || preview.DistanceDays is not (>= 0 and <= MaximumSearchJourneyDays) ||
                        preview.Advice.Opinion == AdviserOpinion.Unfavorable) continue;
                    pair = (actor, target);
                    break;
                }
                if (pair is not null) break;
            }
            if (pair is not { } chosen)
            {
                empty += limit - used;
                used = limit;
                break;
            }
            actors.Remove(chosen.Actor);
            targets.Remove(chosen.Target);
            used++;
            if (submit(Command((id, force, sequence) => new SearchCommand(id, force, sequence, [chosen.Actor], chosen.Target)))) submitted++;
            else rejected++;
        }
        return new(corpsId, OriginalAiWork.Search,
            used >= limit ? OriginalAiWorkEnd.AttemptsExhausted : OriginalAiWorkEnd.NoCandidates, used, submitted)
            { Rejected = rejected, EmptyPlans = empty };
    }
}

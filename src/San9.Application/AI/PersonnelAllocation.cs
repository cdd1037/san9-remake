using System.Collections.Immutable;
using San9.Core.Simulation;

namespace San9.Application.AI;

internal static class PersonnelAllocation
{
    internal static ImmutableArray<AllocationCandidate> Capture(SimulationEngine engine,
        ImmutableArray<(string ControllerId, EconomicObservation Observation)> economies, AllocationWork work)
    {
        var world = engine.Snapshot;
        var abilities = engine.GetAbilities(world);
        var result = ImmutableArray.CreateBuilder<AllocationCandidate>();
        foreach (var (controller, observation) in economies)
        foreach (var home in observation.Domestic.Settlements)
        {
            if (!work.Spend()) return result.ToImmutable();
            // Three available posts cover administration, deployment and an envoy; supplement an actual local vacancy.
            int existing = world.Officers.Count(p => p.IsServing && p.OwnerId == observation.Domestic.Perspective.ObserverId && p.SettlementId == home.Id);
            int returning = world.Simulation.Tasks.Count(t => t.Kind == "personnel-move" && t.Journey?.DestinationPlaceId == home.Id);
            if (existing + returning >= 3) continue;
            foreach (int actor in home.OfficerIds)
            foreach (var target in world.Officers.Where(p => p.IsVisible && p.OwnerId != observation.Domestic.Perspective.ObserverId &&
                p.Status is San9.Core.Domain.OfficerStatus.Free or San9.Core.Domain.OfficerStatus.Captive && p.SettlementId == home.Id &&
                p.LoyaltyTo(observation.Domestic.Perspective.ObserverId) >= 50).OrderBy(p => p.Id))
            {
                if (!work.Spend()) return result.ToImmutable();
                if (world.Simulation.Tasks.Any(t => t.Kind == "personnel-recruit" && t.Journey?.TargetOfficerId == target.Id)) continue;
                var journey = engine.GetPersonnelJourney(observation.Domestic.Perspective.ObserverId, actor, target.Id,
                    San9.Core.Domain.PersonnelJourneyKind.Recruit);
                if (!journey.Available) continue;
                long benefit = ((long)target.BaseAbilities.Politics + target.BaseAbilities.Leadership) * target.LoyaltyTo(observation.Domestic.Perspective.ObserverId);
                result.Add(new(controller, home.AccountId, new RecruitOfficerCommand(Guid.Empty, observation.Domestic.Perspective.ObserverId, 0, actor, target.Id),
                    [actor], 3, benefit, 0, 0, home.Id, Purpose: "personnel")
                    { OfficerOpportunityCost = (long)abilities[actor].Politics * Math.Max(1, (journey.Days!.Value + 9) / 10) });
            }
        }
        return result.ToImmutable();
    }
}

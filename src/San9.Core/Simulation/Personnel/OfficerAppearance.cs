using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record OfficerAppearedEvent(GameClock Clock, int OfficerId, OfficerStatus Status,
    int? RelatedOfficerId, int? ForceId, int? SettlementId, int? RegionId) : DomainEvent;

internal static class OfficerAppearance
{
    // Scenario initialization makes overdue records discoverable at their authored location.
    // Annual family affiliation is a separate transition, never replayed on restore.
    internal static void Initialize(WorldMutation world, List<DomainEvent> events)
    {
        foreach (var person in Due(world)) Appear(world, person, null, null, null, events);
    }

    internal static void AdvanceYear(WorldMutation world, PersonnelRulebook places, List<DomainEvent> events)
    {
        if (!world.Simulation.AnnualAppearancesEnabled) return;
        foreach (var person in Due(world))
        {
            OfficerState? relative = person.ParentId is int parent ? world.Officers.GetValueOrDefault(parent) : null;
            bool inheritService = relative?.IsServing == true || relative?.Status == OfficerStatus.Captive;
            if (relative is null || relative.Status == OfficerStatus.Dead)
            {
                var relatives = world.Officers.Values.Where(p => p.Id != person.Id && person.BloodlineId is int bloodline &&
                    p.BloodlineId == bloodline && (p.IsServing || p.Status == OfficerStatus.Free))
                    .OrderBy(p => p.BirthYear).ThenBy(p => p.Id).ToArray();
                relative = relatives.FirstOrDefault(p => p.Status == OfficerStatus.Ruler) ?? relatives.FirstOrDefault();
                inheritService = relative?.Status == OfficerStatus.Ruler;
            }
            int? location = null, owner = null;
            if (relative is not null)
            {
                if (inheritService && relative.OwnerId is int force && world.Forces.TryGetValue(force, out var affiliation) && !affiliation.IsEliminated)
                {
                    location = ServiceHome(world, relative, places);
                    if (location is not null) owner = force;
                }
                else if (relative.IsServing || relative.Status == OfficerStatus.Free)
                    location = Home(world, relative) ?? relative.RegionId;
            }
            Appear(world, person, relative?.Id, owner, location, events);
        }
    }

    private static OfficerState[] Due(WorldMutation world) => world.Officers.Values
        .Where(p => p.Status == OfficerStatus.NotAppeared && p.AppearanceYear <= world.Clock.Year)
        .OrderBy(p => p.Id).ToArray();

    private static int? Home(WorldMutation world, OfficerState person) => person.UnitId is int id && world.Units.TryGetValue(id, out var unit)
        ? unit.ServicePlaceId : person.SettlementId;

    private static int? ServiceHome(WorldMutation world, OfficerState relative, PersonnelRulebook places)
    {
        bool Eligible(SettlementState place) => place.OwnerId == relative.OwnerId && place.CanHostPersons &&
            !place.IsDestroyed && place.Durability > 0;
        int? homeId = Home(world, relative);
        if (relative.IsServing && homeId is int home && world.Settlements.TryGetValue(home, out var current) && Eligible(current)) return home;
        WorldPosition? origin = relative.UnitId is int id && world.Units.TryGetValue(id, out var unit) ? unit.Position :
            homeId is int placeId && places.Places.ContainsKey(placeId) ? PersonnelLocations.Position(world, places, placeId) : null;
        return world.Settlements.Values.Where(Eligible)
            .OrderBy(s => origin is { } from && places.Places.ContainsKey(s.Id)
                ? MapCoordinates.Distance(from, PersonnelLocations.Position(world, places, s.Id)) : 0)
            .ThenBy(s => s.Id).Select(s => (int?)s.Id).FirstOrDefault();
    }

    private static void Appear(WorldMutation world, OfficerState person, int? relativeId, int? owner, int? location,
        List<DomainEvent> events)
    {
        int? settlement = location is int place ? world.Settlements.ContainsKey(place) ? place : null : person.SettlementId;
        int? region = location is int regionId ? world.Settlements.ContainsKey(regionId) ? null : regionId : person.RegionId;
        var next = person with
        {
            Status = owner is null ? OfficerStatus.Undiscovered : OfficerStatus.Serving,
            OwnerId = owner, SettlementId = settlement, RegionId = region, UnitId = null,
            OfficeId = null, IsCommander = false, Acted = false,
            StatusStartYear = owner is null ? person.StatusStartYear : world.Clock.Year
        };
        world.Replace(next);
        events.Add(new OfficerAppearedEvent(world.Clock, person.Id, next.Status, relativeId, owner, settlement, region));
    }
}

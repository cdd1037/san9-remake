using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>City-based territory changes shared by command execution and preparation.</summary>
public sealed record CorpsTerritoryPlan(ImmutableHashSet<int> Added, ImmutableHashSet<int> Removed,
    ImmutableHashSet<int> Assigned)
{
    public bool Transfers(int placeId) => Added.Contains(placeId) || Removed.Contains(placeId);

    public static CorpsTerritoryPlan ForCities(IEnumerable<SettlementState> settlements,
        IReadOnlyDictionary<int, int> regionCities, int forceId, int? corpsId,
        IEnumerable<int> cityIds, int? rulerPlaceId)
    {
        var owned = settlements.Where(s => s.OwnerId == forceId).ToArray();
        var selected = cityIds.ToHashSet();
        var current = owned.Where(s => corpsId is not null && s.CorpsId == corpsId && s.Kind == SettlementKind.City)
            .Select(s => s.Id).ToHashSet();
        var addedCities = selected.Except(current).ToHashSet();
        var removedCities = current.Except(selected).ToHashSet();
        bool BelongsTo(SettlementState place, HashSet<int> cities) => cities.Contains(place.Id) ||
            regionCities.TryGetValue(place.Id, out int city) && cities.Contains(city);
        var added = owned.Where(s => s.Id != rulerPlaceId && BelongsTo(s, addedCities))
            .Select(s => s.Id).ToImmutableHashSet();
        var removed = owned.Where(s => s.Id != rulerPlaceId && BelongsTo(s, removedCities))
            .Select(s => s.Id).ToImmutableHashSet();
        var assigned = owned.Where(s => corpsId is not null && s.CorpsId == corpsId && !removed.Contains(s.Id))
            .Select(s => s.Id).Concat(added).ToImmutableHashSet();
        return new(added, removed, assigned);
    }
}

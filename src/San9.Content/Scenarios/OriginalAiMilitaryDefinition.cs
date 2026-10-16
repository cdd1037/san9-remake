using System.Collections.Immutable;
using System.Text.Json.Serialization;
using San9.Core.Domain;

namespace San9.Content.Scenarios;

public enum OriginalAiFormationRole
{
    Infantry, Envelopment, Mobile, Cavalry, Crossbow, MixedRanged, March, Defense,
    SiegeTower, Ram, Catapult, Elephant,
}

/// <summary>Semantic references authored by content; executable numbering belongs to the importer.</summary>
public sealed record OriginalAiMilitaryDefinition
{
    [JsonRequired] public ImmutableDictionary<OriginalAiFormationRole, int> Formations { get; init; } = ImmutableDictionary<OriginalAiFormationRole, int>.Empty;
    [JsonRequired] public ImmutableArray<int> NavalPriority { get; init; } = [];
    [JsonRequired] public ImmutableArray<int> OrdinaryFacilityIds { get; init; } = [];
    [JsonRequired] public ImmutableArray<int> AggressiveRulerIds { get; init; } = [];
    [JsonRequired] public ImmutableArray<int> FortifiedFacilityIds { get; init; } = [];
    [JsonRequired] public ImmutableArray<int> RouteObstacleFacilityIds { get; init; } = [];
    [JsonRequired] public ImmutableArray<int> SlowTerrainIds { get; init; } = [];
    [JsonRequired] public uint FrontierCityReserve { get; init; } = 40000;
    [JsonRequired] public ImmutableDictionary<int, uint> FrontierFacilityReserves { get; init; } = ImmutableDictionary<int, uint>.Empty;
    [JsonRequired] public int? HarborReliefTacticId { get; init; }

    public void Validate(IReadOnlySet<int> people, MilitaryDefinition? military)
    {
        if (Formations is null || Formations.Any(p => !Enum.IsDefined(p.Key) || p.Value < 0) ||
            NavalPriority.IsDefault || OrdinaryFacilityIds.IsDefault || AggressiveRulerIds.IsDefault ||
            FortifiedFacilityIds.IsDefault || RouteObstacleFacilityIds.IsDefault || SlowTerrainIds.IsDefault ||
            FrontierFacilityReserves is null || AggressiveRulerIds.Any(id => !people.Contains(id))) throw new InvalidDataException("Invalid AI military policy.");
        var formations = military?.Formations.Select(f => f.Id).ToHashSet() ?? [];
        var facilities = military?.Facilities.Select(f => f.Id).ToHashSet() ?? [];
        var terrains = military?.Terrains.Select(t => t.Id).ToHashSet() ?? [];
        if (HarborReliefTacticId is int tactic && military?.Tactics.Any(t => t.Id == tactic) != true)
            throw new InvalidDataException("Unknown harbor relief tactic.");
        if (Formations.Values.Concat(NavalPriority).Any(id => !formations.Contains(id)) ||
            OrdinaryFacilityIds.Concat(FortifiedFacilityIds).Concat(RouteObstacleFacilityIds).Concat(FrontierFacilityReserves.Keys).Any(id => !facilities.Contains(id)) ||
            SlowTerrainIds.Any(id => !terrains.Contains(id))) throw new InvalidDataException("Unknown AI military reference.");
        if (military is not null && (Formations.Values.Any(id => military.Formations.Single(f => f.Id == id).TravelMode != TravelMode.Land) ||
            NavalPriority.Any(id => military.Formations.Single(f => f.Id == id).TravelMode != TravelMode.Water)))
            throw new InvalidDataException("Invalid AI formation travel role.");
    }
}

using System.Collections.Immutable;
using System.Text.Json.Serialization;
using San9.Core.Simulation;

namespace San9.Core.Domain;

public sealed record EmperorDeathDefinition(
    [property: JsonRequired] int OfficerId,
    [property: JsonRequired] GameClock ScenarioStartsBefore,
    [property: JsonRequired] GameClock EarliestDate,
    [property: JsonRequired] int SurvivalChancePercent);

public sealed record ImperialCourtDefinition(
    [property: JsonRequired] ImmutableArray<int> SuccessionOrder,
    [property: JsonRequired] ImmutableArray<EmperorDeathDefinition> DeathEvents)
{
    public static ImperialCourtDefinition Empty { get; } = new([], []);

    public void Validate(IReadOnlySet<int>? officers = null)
    {
        if (SuccessionOrder.IsDefault || DeathEvents.IsDefault || SuccessionOrder.Any(id => id < 0) ||
            SuccessionOrder.Distinct().Count() != SuccessionOrder.Length ||
            officers is not null && SuccessionOrder.Any(id => !officers.Contains(id)) ||
            DeathEvents.Any(e => e is null || !SuccessionOrder.Contains(e.OfficerId) || e.SurvivalChancePercent is < 0 or > 100) ||
            DeathEvents.Select(e => e.OfficerId).Distinct().Count() != DeathEvents.Length)
            throw new ArgumentException("Invalid imperial court definition.");
        foreach (var e in DeathEvents)
        {
            _ = new GameClock(e.ScenarioStartsBefore.Year, e.ScenarioStartsBefore.Month, e.ScenarioStartsBefore.Day);
            _ = new GameClock(e.EarliestDate.Year, e.EarliestDate.Month, e.EarliestDate.Day);
        }
    }

    public OfficerState? Resolve(bool ended, IEnumerable<OfficerState> officers)
    {
        if (ended || SuccessionOrder.IsEmpty) return null;
        var people = officers.ToDictionary(p => p.Id);
        return SuccessionOrder.Select(id => people.GetValueOrDefault(id)).FirstOrDefault(p => p is not null && p.Status != OfficerStatus.Dead);
    }

    public static int? City(OfficerState? emperor, IEnumerable<SettlementState> settlements,
        IReadOnlyDictionary<int, int> regionCities, bool traveling)
    {
        if (emperor is null || emperor.UnitId is not null || traveling) return null;
        int? location = emperor.SettlementId ?? emperor.RegionId;
        if (location is not int place) return null;
        var cities = settlements.Where(s => s.Kind == SettlementKind.City && !s.IsDestroyed).ToDictionary(s => s.Id);
        if (cities.ContainsKey(place)) return place;
        return regionCities.TryGetValue(place, out int city) && cities.ContainsKey(city) ? city : null;
    }
}

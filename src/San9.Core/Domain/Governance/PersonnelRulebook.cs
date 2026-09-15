using System.Collections.Immutable;

namespace San9.Core.Domain;

public sealed record PersonPlace(int Id, [property: System.Text.Json.Serialization.JsonRequired] WorldPosition Position);
public sealed record CityConnection(int FromCityId, int ToCityId);

public enum PersonnelJourneyKind { Recruit, Move, Search }

/// <summary>Static places for personnel journeys; ownership is always read from the world.</summary>
public sealed class PersonnelRulebook
{
    public static PersonnelRulebook Empty { get; } = new([]);
    public PersonnelRulebook(IEnumerable<PersonPlace> places, IReadOnlyDictionary<int, int>? regionCities = null,
        IEnumerable<CityConnection>? cityConnections = null, IReadOnlyDictionary<int, int>? cityProvinces = null)
    {
        Places = places.ToImmutableDictionary(p => p.Id);
        RegionCities = (regionCities ?? ImmutableDictionary<int, int>.Empty).ToImmutableDictionary();
        CityConnections = (cityConnections ?? []).ToImmutableArray();
        CityProvinces = (cityProvinces ?? ImmutableDictionary<int, int>.Empty).ToImmutableDictionary();
        if (Places.Values.Any(p => p.Id < 0 || p.Position.X < 0 || p.Position.Y < 0)) throw new ArgumentException("Invalid personnel place.");
        if (CityProvinces.Any(p => !Places.ContainsKey(p.Key) || p.Value < 0) ||
            RegionCities.Any(p => p.Key < 0 || !Places.ContainsKey(p.Value)) ||
            CityConnections.Any(c => c.FromCityId == c.ToCityId || !RegionCities.Values.Contains(c.FromCityId) || !RegionCities.Values.Contains(c.ToCityId)) ||
            CityConnections.Distinct().Count() != CityConnections.Length)
            throw new ArgumentException("Invalid personnel geography.");
    }

    public ImmutableDictionary<int, PersonPlace> Places { get; }
    public ImmutableDictionary<int, int> RegionCities { get; }
    public ImmutableArray<CityConnection> CityConnections { get; }
    public ImmutableDictionary<int, int> CityProvinces { get; }

    public int JourneyDays(int source, int destination, PersonnelJourneyKind kind)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        var from = Places[source];
        var to = Places[destination];
        int days = JourneyDays(from.Position, to.Position);
        return kind == PersonnelJourneyKind.Search ? Math.Max(10, checked((days + 9) / 10 * 10)) : days;
    }

    public static int JourneyDays(WorldPosition source, WorldPosition destination) =>
        checked((int)((MapCoordinates.Distance(source, destination) + 29) / 30));

    /// <summary>Calendar offset of resolution: the first daily stage consumes one countdown day before testing arrival.</summary>
    public int ResolutionDayOffset(int source, int destination, PersonnelJourneyKind kind) =>
        Math.Max(0, JourneyDays(source, destination, kind) - 1);

    public static int AffinityDistance(OfficerState first, OfficerState second)
    {
        int distance = Math.Abs(first.Affinity - second.Affinity);
        return Math.Min(50, Math.Min(distance, 150 - distance));
    }
}

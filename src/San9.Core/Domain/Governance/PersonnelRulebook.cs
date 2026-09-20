using System.Collections.Immutable;
using System.Text.Json.Serialization;
using San9.Core.Simulation;

namespace San9.Core.Domain;

public sealed record PersonPlace(int Id, [property: JsonRequired] WorldPosition Position);
public sealed record CityTacticCourse([property: JsonRequired] int CityId, [property: JsonRequired] int TacticId);
public sealed record CityConnection(int FromCityId, int ToCityId);

public enum PersonnelJourneyKind { Recruit, Move, Search }

/// <summary>Static places for personnel journeys; ownership is always read from the world.</summary>
public sealed class PersonnelRulebook
{
    public static PersonnelRulebook Empty { get; } = new([]);
    public PersonnelRulebook(IEnumerable<PersonPlace> places, IReadOnlyDictionary<int, int>? regionCities = null,
        IEnumerable<CityConnection>? cityConnections = null, IReadOnlyDictionary<int, int>? cityProvinces = null, IEnumerable<CityTacticCourse>? tacticCourses = null,
        IEnumerable<SearchEventDefinition>? searchEvents = null)
    {
        Places = places.ToImmutableDictionary(p => p.Id);
        SearchEvents = (searchEvents ?? []).ToImmutableDictionary(e => e.Id, StringComparer.Ordinal);
        foreach (var entry in SearchEvents.Values)
        {
            if (entry is WomenVisitSearchEventDefinition visit)
            {
                _ = visit.Candidate(null);
                if (visit.Consumption != EventConsumption.None || visit.PoliticsChanceDivisor <= 0 ||
                    visit.MinimumBaseTrustExclusive is < 0 or > 1000 || visit.MaximumTrustExclusive is < 1 or > 1001 ||
                    visit.Sites.IsDefaultOrEmpty || visit.Sites.Any(s => s is null || !Places.ContainsKey(s.PlaceId) ||
                        s.VisitorOfficerId < 0 || !ValidIds(s.RewardTacticIds) || s.RewardTacticIds.IsEmpty || !ValidIds(s.RequiredLeaderIds) ||
                        s.RequiredWorldKey is PersistentEventKey worldKey && string.IsNullOrWhiteSpace(worldKey.Value) ||
                        s.EarliestScenarioStart is GameClock start && start.Year < 1 ||
                        s.AlternativeForceHistoryKey is PersistentEventKey history &&
                            (string.IsNullOrWhiteSpace(history.Value) || s.EarliestScenarioStart is null)) ||
                    visit.Sites.Select(s => s.PlaceId).Distinct().Count() != visit.Sites.Length)
                    throw new ArgumentException("Invalid women visit event definition.");
            }
            else if (entry is HealingSearchEventDefinition healing)
            {
                _ = healing.Candidate(healing.ConsumptionKey);
                if (healing.HealerOfficerId < 0 || healing.WarChanceDivisor <= 0 ||
                    !ValidIds(healing.ExcludedSearchingOfficerIds)) throw new ArgumentException("Invalid healing event definition.");
            }
            else if (entry is ForceActivationSearchEventDefinition activation)
            {
                _ = activation.Candidate(activation.ConsumptionKey);
                if (!Places.ContainsKey(activation.PlaceId) || !Places.ContainsKey(activation.CityId) || activation.ForceId < 0 ||
                    !ValidIds(activation.AttachedSettlementIds) || activation.AttachedSettlementIds.Any(id => id == activation.CityId || !Places.ContainsKey(id)) ||
                    !ValidIds(activation.OfficerIds) || activation.OfficerIds.IsEmpty || activation.MinimumTrust is < 0 or > 1000 ||
                    activation.MinimumProvinces < 0 || activation.CitySoldiers == 0 || activation.BaseTrustReward < 0 ||
                    activation.Consumption != EventConsumption.OnSuccess)
                    throw new ArgumentException("Invalid force activation definition.");
            }
            else if (entry is TreasureMonumentSearchEventDefinition monument)
            {
                _ = monument.Candidate(monument.ConsumptionKey);
                if (!Places.ContainsKey(monument.PlaceId) || monument.TreasureId < 0 || monument.MinimumRankId < 0 ||
                    monument.Cost == 0 || monument.BaseTrustReward < 0 || monument.Consumption != EventConsumption.OnSuccess)
                    throw new ArgumentException("Invalid treasure monument definition.");
            }
            else if (entry is ImperialCeremonySearchEventDefinition ceremony)
            {
                _ = ceremony.Candidate(ceremony.ConsumptionKey);
                if (!Places.ContainsKey(ceremony.PlaceId) || ceremony.RequiredRankId < 0 || ceremony.AbilityReward < 0 ||
                    ceremony.BaseTrustReward < 0 || ceremony.LoyaltyReward < 0)
                    throw new ArgumentException("Invalid imperial ceremony definition.");
            }
            else if (entry is ScholarReturnSearchEventDefinition scholar)
            {
                _ = scholar.Candidate(scholar.ConsumptionKey);
                if (scholar.ScholarOfficerId < 0 || !Places.ContainsKey(scholar.PlaceId) || scholar.EarliestDate.Year < 1 || scholar.LatestDate.Year < 1 ||
                    scholar.EarliestDate.DayNumber > scholar.LatestDate.DayNumber || scholar.MaximumBaseAbilityExclusive <= 0 ||
                    scholar.PoliticsChanceDivisor <= 0 || scholar.MinimumTrustExclusive is < 0 or > 1000 ||
                    scholar.MaximumBaseTrustExclusive is < 1 or > 1001 || scholar.IntelligenceReward < 0 ||
                    scholar.FallbackPoliticsReward < 0 || scholar.BaseTrustReward < 0 || !ValidIds(scholar.RewardTacticIds) ||
                    string.IsNullOrWhiteSpace(scholar.SuccessHistoryKey.Value))
                    throw new ArgumentException("Invalid scholar return event definition.");
            }
            else if (entry is BeastSearchEventDefinition beast)
            {
                if (beast.Sites.IsDefaultOrEmpty || beast.Sites.Any(s => s is null || !Places.ContainsKey(s.PlaceId) || !ValidIds(s.RewardTacticIds)) ||
                    beast.Sites.Select(s => s.PlaceId).Distinct().Count() != beast.Sites.Length ||
                    !ValidIds(beast.BattleTacticIds) || beast.WarChanceDivisor <= 0 || beast.WarReward < 0 ||
                    beast.BaseTrustReward < 0 || beast.DefeatInjury is < 0 or > 14)
                    throw new ArgumentException("Invalid beast event definition.");
                foreach (var site in beast.Sites) _ = beast.Candidate(site.ConsumptionKey);
            }
            else
                throw new ArgumentException("Invalid search event definition.");
        }
        TacticCourses = (tacticCourses ?? []).ToImmutableDictionary(c => c.CityId, c => c.TacticId);
        if (TacticCourses.Any(c => !Places.ContainsKey(c.Key) || c.Value < 0)) throw new ArgumentException("Invalid tactic course.");
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

    public ImmutableDictionary<int, int> TacticCourses { get; }
    public ImmutableDictionary<string, SearchEventDefinition> SearchEvents { get; }
    public ImmutableDictionary<int, PersonPlace> Places { get; }
    public ImmutableDictionary<int, int> RegionCities { get; }
    public ImmutableArray<CityConnection> CityConnections { get; }
    public ImmutableDictionary<int, int> CityProvinces { get; }

    private static bool ValidIds(ImmutableArray<int> values) => !values.IsDefault &&
        values.All(id => id >= 0) && values.Distinct().Count() == values.Length;

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

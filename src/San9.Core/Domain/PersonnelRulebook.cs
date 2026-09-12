using System.Collections.Immutable;

namespace San9.Core.Domain;

public sealed record PersonPlace(int Id, [property: System.Text.Json.Serialization.JsonRequired] WorldPosition Position);

public enum PersonnelJourneyKind { Recruit, Move, Search }

/// <summary>Static places for personnel journeys; ownership is always read from the world.</summary>
public sealed class PersonnelRulebook
{
    public static PersonnelRulebook Empty { get; } = new([]);
    public PersonnelRulebook(IEnumerable<PersonPlace> places)
    {
        Places = places.ToImmutableDictionary(p => p.Id);
        if (Places.Values.Any(p => p.Id < 0 || p.Position.X < 0 || p.Position.Y < 0)) throw new ArgumentException("Invalid personnel place.");
    }

    public ImmutableDictionary<int, PersonPlace> Places { get; }

    public int JourneyDays(int source, int destination, PersonnelJourneyKind kind)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        var from = Places[source];
        var to = Places[destination];
        int days = checked((int)((MapCoordinates.Distance(from.Position, to.Position) + 29) / 30));
        return kind == PersonnelJourneyKind.Search ? Math.Max(10, checked((days + 9) / 10 * 10)) : days;
    }

    public static int AffinityDistance(OfficerState first, OfficerState second)
    {
        int distance = Math.Abs(first.Affinity - second.Affinity);
        return Math.Min(50, Math.Min(distance, 150 - distance));
    }
}

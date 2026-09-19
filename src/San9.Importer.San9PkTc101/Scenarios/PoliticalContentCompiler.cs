using System.Collections.Immutable;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Importer.San9PkTc101.Scenarios;

public static class PoliticalContentCompiler
{
    public static PersistentEventKey CourtEndedKey { get; } = new("shanyang-gong");
    public static PoliticalEventsDefinition? Events(byte mode) => mode is 2 or 9 ? null : new(1000, CourtEndedKey);

    public static CountryTitleDefinition[] Titles(IEnumerable<San9ScenarioCountryTitleRecord> records,
        IReadOnlyList<San9ScenarioPersonRecord> people, IEnumerable<int> retainedOfficerIds,
        Func<int, string> nameKey, Func<int, int> flagGlyph)
    {
        var retained = retainedOfficerIds.ToArray();
        if (retained.Any(id => id < 0 || id >= people.Count)) throw new InvalidDataException("Invalid retained officer identity.");
        var preferredRulers = retained.Where(id => people[id].BiographyResourceCode == 32).Order().ToImmutableArray();
        return records.Select(r => new CountryTitleDefinition(new(r.Id, r.Id is <= 3 or >= 10 and <= 18,
            r.Id is not (>= 4 and <= 8), r.Id == 9, r.Id == 3 ? preferredRulers : [],
            r.Id switch { 0 => [326], 1 => [621], 2 => [347], 19 => [213], _ => [] }), nameKey(r.Id), flagGlyph(r.Id))).ToArray();
    }

    public static ImmutableDictionary<int, int> ProvinceTitles(IEnumerable<San9ScenarioCountryTitleRecord> records)
    {
        var result = ImmutableDictionary.CreateBuilder<int, int>();
        // The source loader writes the reverse link in table order; the later title wins.
        foreach (var record in records.OrderBy(r => r.Id))
        {
            if (record.Area1Id is int first) result[first] = record.Id;
            if (record.Area2Id is int second) result[second] = record.Id;
        }
        return result.ToImmutable();
    }
}

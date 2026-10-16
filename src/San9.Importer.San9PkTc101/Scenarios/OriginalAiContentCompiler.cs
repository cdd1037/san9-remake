using System.Collections.Immutable;
using San9.Content.Scenarios;

namespace San9.Importer.San9PkTc101.Scenarios;

public static class OriginalAiContentCompiler
{
    public static OriginalAiDefinition Compile(IReadOnlyList<San9ScenarioPersonRecord> people, IEnumerable<int> supportedPeople)
    {
        var supported = supportedPeople.ToHashSet();
        var families = new Dictionary<int, int>();
        foreach (var person in people.Where(p => p.BiographyResourceCode is 60 or 36 or 62))
            if (person.BloodlineRootPersonId is int family)
                families[family] = person.BiographyResourceCode == 60 ? 200 : 100;
        return OriginalAiDefinition.Default with
        {
            FamilyRankBonuses = families.ToImmutableDictionary(),
            PreferredStrategyRulerIds = people.Select((p, id) => (Person: p, Id: id))
                .Where(p => supported.Contains(p.Id) && p.Person.BiographyResourceCode is 36 or 59 or 60 or 61 or 62 or 63 or 64)
                .Select(p => p.Id).ToImmutableArray(),
        };
    }
}

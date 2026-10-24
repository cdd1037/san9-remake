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
            Military = new()
            {
                Formations = Enum.GetValues<OriginalAiFormationRole>().ToImmutableDictionary(role => role, role => (int)role),
                NavalPriority = [23, 22, 21], OrdinaryFacilityIds = [6, 7, 8, 9],
                HarborReliefTacticId = 13,
                // Policy references use facility definitions, not the executable's grouped categories.
                FortifiedFacilityIds = [2, 3, 4, 5], RouteObstacleFacilityIds = [10, 11, 12, 13, 14, 15, 16], SlowTerrainIds = [10, 15, 16],
                FrontierFacilityReserves = new Dictionary<int, uint>
                {
                    [1] = 3000, [2] = 30000, [3] = 30000, [4] = 30000, [5] = 30000,
                    [6] = 10000, [8] = 15000, [9] = 15000
                }.ToImmutableDictionary(),
                AggressiveRulerIds = people.Select((p, id) => (Person: p, Id: id))
                    .Where(p => supported.Contains(p.Id) && p.Person.BiographyResourceCode == 3).Select(p => p.Id).ToImmutableArray(),
            },
            DiplomacyFacilityPriorities = new Dictionary<int, int> { [2] = 6, [6] = 5, [10] = 4, [4] = 3, [5] = 2, [3] = 1 }.ToImmutableDictionary(),
            FamilyRankBonuses = families.ToImmutableDictionary(),
            PreferredStrategyRulerIds = people.Select((p, id) => (Person: p, Id: id))
                .Where(p => supported.Contains(p.Id) && p.Person.BiographyResourceCode is 36 or 59 or 60 or 61 or 62 or 63 or 64)
                .Select(p => p.Id).ToImmutableArray(),
        };
    }
}

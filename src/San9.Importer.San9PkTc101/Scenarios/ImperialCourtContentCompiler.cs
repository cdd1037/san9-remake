using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Importer.San9PkTc101.Scenarios;

public static class ImperialCourtContentCompiler
{
    public static ImperialCourtDefinition Compile(byte mode, IReadOnlyList<San9ScenarioPersonRecord> people,
        IEnumerable<int> retainedOfficerIds)
    {
        var retained = retainedOfficerIds.ToHashSet();
        int Person(int biography) => Enumerable.Range(0, people.Count)
            .Where(id => retained.Contains(id) && people[id].BiographyResourceCode == biography)
            .Select(id => (int?)id).FirstOrDefault() ?? throw new InvalidDataException("Missing imperial person reference.");
        var candidates = new[] { Person(563), Person(564), Person(565) }.ToImmutableArray();
        return new(candidates, mode is 2 or 9 ? [] :
            [new(candidates[0], new(189, 4, 1), new(189, 4, 1), 60),
             new(candidates[1], new(189, 8, 1), new(197, 4, 1), 60)]);
    }
}

using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Importer.San9PkTc101.Scenarios;

public static class TacticStudyContentCompiler
{
    // TC101 quarterly teaching table; source identities stay in this version importer.
    public static ImmutableArray<CityTacticCourse> Compile(IReadOnlySet<int> configuredCities) =>
        new CityTacticCourse[] { new(3, 0), new(20, 3), new(0, 6), new(42, 9), new(25, 12),
            new(36, 15), new(14, 19), new(8, 23), new(17, 27) }
            .Where(c => configuredCities.Contains(c.CityId)).OrderBy(c => c.CityId).ToImmutableArray();
}

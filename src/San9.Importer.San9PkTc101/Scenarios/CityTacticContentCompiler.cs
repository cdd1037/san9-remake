using System.Collections.Immutable;

namespace San9.Importer.San9PkTc101.Scenarios;

/// <summary>TC101 new-game city tactics, appended after reading the scenario flags (RE-SAN9PK-0161).</summary>
public static class CityTacticContentCompiler
{
    private static readonly int[] InitialTactics = [
        9, 9, 10, 9, 25, 24, 9, 9, 24, 9,
        9, 25, 25, 9, 23, 24, 10, 9, 23, 9,
        9, 10, 9, 9, 23, 9, 9, 24, 24, 23,
        10, 9, 10, 9, 9, 9, 9, 24, 25, 9,
        11, 9, 10, 30, 9, 26, 11, 11, 11, 30];

    public static ImmutableArray<int> Compile(int sourceCityId, IEnumerable<int> scenarioTactics)
    {
        if ((uint)sourceCityId >= InitialTactics.Length) throw new ArgumentOutOfRangeException(nameof(sourceCityId));
        return scenarioTactics.Append(InitialTactics[sourceCityId]).Distinct().Order().ToImmutableArray();
    }
}

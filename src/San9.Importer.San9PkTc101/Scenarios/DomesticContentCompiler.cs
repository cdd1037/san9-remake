using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Importer.San9PkTc101.Scenarios;

/// <summary>Normalizes documented TC-101 biography pairs to scenario person identities (RE-0095/0096).</summary>
public static class DomesticContentCompiler
{
    public static ImmutableArray<DomesticPair> Compile(IReadOnlyList<San9ScenarioPersonRecord> people)
    {
        int[][] pairs =
        [
            [36,45,45,39,45,84,1,159,1,114,92,110,60,44,43,105,123,30,91,161,117,153,30,117],
            [36,2,2,45,1,45,9,45,49,105,24,47,102,64],
            [36,2,36,174,45,114,60,12,91,123],
            [2,20,2,121,24,166,64,90,375,125],
            [],
            [1,45,23,16,2,1,13,12,13,7,17,122,63,538,538,64,90,212,14,268,160,42,3,104]
        ];
        bool RequiresRuler(int biography) => biography is 3 or 36 or 60 or 63 or 64 or 160;
        var identities = people.Take(700).Select((person, id) => (person, id)).ToLookup(p => (int)p.person.BiographyResourceCode);
        List<DomesticPair> result = [];
        for (int activity = 0; activity < pairs.Length; activity++)
        for (int i = 0; i < pairs[activity].Length; i += 2)
        foreach (var first in identities[pairs[activity][i]])
        foreach (var second in identities[pairs[activity][i + 1]])
            if (first.id != second.id) result.Add(new((DomesticActivity)activity, first.id, second.id,
                RequiresRuler(pairs[activity][i]), RequiresRuler(pairs[activity][i + 1])));
        return result.OrderBy(p => p.Project).ThenBy(p => p.FirstId).ThenBy(p => p.SecondId).ToImmutableArray();
    }
}

using System.Buffers.Binary;
using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Importer.San9PkTc101.Scenarios;

/// <summary>TC-101 biography-keyed data tables, normalized to retained scenario identities (RE-0125).</summary>
public static class TacticLinkContentCompiler
{
    public static TacticLinkRules Compile(byte[] exe, IReadOnlyList<San9ScenarioPersonRecord> people,
        IEnumerable<int> retainedPersonIds, bool relationshipsEnabled)
    {
        const int pairOffset = 0x20f940, pairCount = 58, exclusionOffset = 0x20f928;
        if (exe.Length < pairOffset + pairCount * 12) throw new InvalidDataException("Missing tactic relationship tables.");
        var identities = retainedPersonIds.ToLookup(id => (int)people[id].BiographyResourceCode);
        Dictionary<(int, int), TacticLinkPair> pairs = [];
        int Read(int offset) => BinaryPrimitives.ReadInt32LittleEndian(exe.AsSpan(offset, 4));
        void Add(int firstBiography, int secondBiography, int bonus, bool suppressesSibling)
        {
            if (firstBiography < 0 || secondBiography < 0 || firstBiography == secondBiography || bonus is < 0 or > 100)
                throw new InvalidDataException("Invalid source tactic relationship.");
            foreach (int first in identities[firstBiography])
            foreach (int second in identities[secondBiography])
            {
                var key = (Math.Min(first, second), Math.Max(first, second));
                var existing = pairs.GetValueOrDefault(key);
                pairs[key] = new(key.Item1, key.Item2, suppressesSibling ? existing?.Bonus ?? 0 : bonus,
                    suppressesSibling || existing?.SuppressesSiblingBonus == true);
            }
        }
        for (int i = 0; i < pairCount; i++)
        {
            int offset = pairOffset + i * 12;
            Add(Read(offset), Read(offset + 4), Read(offset + 8), false);
        }
        for (int i = 0; i < 3; i++) Add(Read(exclusionOffset + i * 8), Read(exclusionOffset + i * 8 + 4), 0, true);
        return new(relationshipsEnabled, 20, pairs.Values.OrderBy(p => p.FirstId).ThenBy(p => p.SecondId).ToImmutableArray());
    }
}

using San9.Core.Domain;
using System.Buffers.Binary;
using System.Collections.Immutable;

namespace San9.Importer.San9PkTc101.Scenarios;

public static class TreasureContentCompiler
{
    public const int BuiltInCount = 100;

    public sealed record DiplomacyPreference(bool AutomaticOfferAllowed, ImmutableArray<int> FavoredOwnerIds);

    // RE-0118: source item/biography pairs are converted to retained scenario identities.
    public static IReadOnlyDictionary<int, DiplomacyPreference> DiplomacyPreferences(byte[] exe,
        IReadOnlyList<San9ScenarioPersonRecord> people, IEnumerable<int> retainedPersonIds, IEnumerable<int> retainedTreasureIds)
    {
        const int pairOffset = 0x255860, pairCount = 38, excludedOffset = 0x255998, excludedCount = 3;
        if (exe.Length < excludedOffset + (excludedCount + 1) * 4)
            throw new InvalidDataException("Missing treasure diplomacy tables.");
        int Read(int offset) => BinaryPrimitives.ReadInt32LittleEndian(exe.AsSpan(offset, 4));
        if (Read(pairOffset + pairCount * 8) != -1 || Read(excludedOffset + excludedCount * 4) != -1)
            throw new InvalidDataException("Invalid treasure diplomacy table terminator.");
        int[] personIds = retainedPersonIds.ToArray();
        if (personIds.Any(id => id < 0 || id >= people.Count) || personIds.Distinct().Count() != personIds.Length)
            throw new InvalidDataException("Invalid retained person identities.");
        var identities = personIds.ToLookup(id => (int)people[id].BiographyResourceCode);
        var favorites = retainedTreasureIds.ToDictionary(id => id, _ => new HashSet<int>());
        for (int i = 0; i < pairCount; i++)
        {
            int item = Read(pairOffset + i * 8), biography = Read(pairOffset + i * 8 + 4);
            if (!favorites.TryGetValue(item, out var owners) || biography < 0)
                throw new InvalidDataException("Invalid treasure diplomacy preference.");
            owners.UnionWith(identities[biography]);
        }
        HashSet<int> excluded = [];
        for (int i = 0; i < excludedCount; i++)
        {
            int item = Read(excludedOffset + i * 4);
            if (!favorites.ContainsKey(item) || !excluded.Add(item))
                throw new InvalidDataException("Invalid automatic treasure offer exclusion.");
        }
        return favorites.ToDictionary(pair => pair.Key, pair => new DiplomacyPreference(!excluded.Contains(pair.Key),
            pair.Value.Order().ToImmutableArray()));
    }

    public static OfficerAbilities AbilityBonuses(San9ScenarioItemRecord item) => new(
        item.CategoryId == 1 ? item.AbilityBonus : 0,
        item.CategoryId is 5 or 9 ? item.AbilityBonus : 0,
        item.CategoryId is 3 or 4 or 6 ? item.AbilityBonus : 0,
        item.CategoryId == 2 ? item.AbilityBonus : 0);

    public static TreasureState[] InitialStates(IEnumerable<San9ScenarioItemRecord> records) => records.Select(item =>
    {
        if (item.SerializedId >= BuiltInCount && !item.IsInvalid)
            throw new InvalidDataException("Configured expansion treasure requires expansion text support.");
        var discovery = item.DiscoveryState switch
        {
            San9ScenarioItemDiscoveryState.Invalid => TreasureDiscovery.Disabled,
            San9ScenarioItemDiscoveryState.NotUnearthed => TreasureDiscovery.Buried,
            San9ScenarioItemDiscoveryState.Unearthed => TreasureDiscovery.Discovered,
            _ => throw new InvalidDataException("Invalid treasure discovery state.")
        };
        return new TreasureState(item.SerializedId, item.HolderPersonId, discovery, item.DiscoveryLocationId);
    }).ToArray();
}

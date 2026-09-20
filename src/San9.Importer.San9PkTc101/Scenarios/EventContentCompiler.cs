using System.Collections.Immutable;
using San9.Core.Simulation;
using San9.Core.Domain;

namespace San9.Importer.San9PkTc101.Scenarios;

public static class EventContentCompiler
{
    public static SunMarriageDefinition? SunMarriage(byte scenarioMode,
        IReadOnlyList<San9ScenarioPersonRecord> people, IEnumerable<int> retainedOfficerIds)
    {
        if (scenarioMode is 2 or 9) return null;
        var retained = retainedOfficerIds.ToArray();
        if (retained.Any(id => id < 0 || id >= people.Count)) throw new InvalidDataException("Invalid retained officer identity.");
        int Person(int biography)
        {
            var matches = retained.Where(id => people[id].BiographyResourceCode == biography).ToArray();
            return matches.Length == 1 ? matches[0] : throw new InvalidDataException("Expected one historical person identity.");
        }
        int family = people[Person(62)].BloodlineRootPersonId ?? throw new InvalidDataException("Missing Sun family identity.");
        // Both source date checks are current-date upper bounds; the earlier one dominates.
        return new(Person(36), Person(538), family, 3, new(209, 12, 30), 70, 20, 150, [13, 1], new("sun-marriage"));
    }

    public static QiaoMarriageDefinition? QiaoMarriage(byte scenarioMode,
        IReadOnlyList<San9ScenarioPersonRecord> people, IEnumerable<int> retainedOfficerIds)
    {
        if (scenarioMode is 2 or 9) return null;
        var retained = retainedOfficerIds.ToArray();
        if (retained.Any(id => id < 0 || id >= people.Count)) throw new InvalidDataException("Invalid retained officer identity.");
        int Person(int biography)
        {
            var matches = retained.Where(id => people[id].BiographyResourceCode == biography).ToArray();
            return matches.Length == 1 ? matches[0] : throw new InvalidDataException("Expected one historical person identity.");
        }
        // The source new-game historical setting defaults to enabled; it is independent of scenario mode 1.
        return new(Person(63), Person(91), [Person(567), Person(568)], Person(60), 10, [21],
            new(199, 12, 30), new(209, 12, 30), 3, 7, 20, 25, new("qiao-marriage"));
    }

    public static ImmutableArray<SearchEventDefinition> SearchEvents(byte scenarioMode,
        IReadOnlyList<San9ScenarioPersonRecord> people, IEnumerable<int> retainedOfficerIds, IReadOnlySet<int> configuredRegions)
    {
        if (scenarioMode is 2 or 9) return [];
        var retained = retainedOfficerIds.ToHashSet();
        if (retained.Any(id => id < 0 || id >= people.Count)) throw new InvalidDataException("Invalid retained officer identity.");
        var healers = retained.Where(id => people[id].BiographyResourceCode == 497).ToArray();
        if (healers.Length != 1) throw new InvalidDataException("Expected one retained physician identity.");
        BeastSearchSite[] sites = [new(73, new("shenshou.xihe"), [11, 24, 22, 30, 26]),
            new(178, new("shenshou.wanwen"), [10, 23, 18, 26, 29]), new(128, new("shenshou.jianan"), [13, 24, 8, 29, 30])];
        if (sites.Any(s => !configuredRegions.Contains(s.PlaceId))) throw new InvalidDataException("Missing beast search site.");
        return [new HealingSearchEventDefinition(healers[0], retained.Where(id => people[id].BiographyResourceCode == 60).Order().ToImmutableArray(), 6)
            { Id = "hua-tuo.healing", Priority = -7, Consumption = EventConsumption.None },
            new BeastSearchEventDefinition(sites.ToImmutableArray(), [1, 28], 8, 2, 2, 7, scenarioMode != 1)
            { Id = "shenshou", Priority = -28, Consumption = EventConsumption.OnPresentation }];
    }

    // RE-SAN9PK-0130/0141: preserve beast encounters, Qiao marriage, and
    // Shanyanggong consumption also set by abdication. Source bits stay in this importer.
    public static ImmutableArray<PersistentEventKey> ConsumedKeys(IReadOnlyList<uint> flagWords)
    {
        if (flagWords.Count != 4) throw new InvalidDataException("Expected four scenario flag words.");
        (int Bit, string Key)[] events = [(9, PoliticalContentCompiler.CourtEndedKey.Value), (12, "qiao-marriage"), (13, "sun-marriage"), (17, "shenshou.xihe"), (18, "shenshou.wanwen"), (19, "shenshou.jianan")];
        return events.Where(e => (flagWords[e.Bit / 32] & (1u << (e.Bit % 32))) != 0)
            .Select(e => new PersistentEventKey(e.Key)).OrderBy(k => k.Value, StringComparer.Ordinal).ToImmutableArray();
    }
}

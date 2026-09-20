using System.Collections.Immutable;
using San9.Core.Simulation;
using San9.Core.Domain;

namespace San9.Importer.San9PkTc101.Scenarios;

public static class EventContentCompiler
{
    public static ForceActivationSearchEventDefinition? Penglai(byte scenarioMode,
        IReadOnlyList<San9ScenarioPersonRecord> people, IEnumerable<int> retainedOfficerIds,
        IEnumerable<San9.Content.Scenarios.ForceInitialState> forces)
    {
        if (scenarioMode is 2 or 9) return null;
        var retained = retainedOfficerIds.ToArray();
        if (retained.Any(id => id < 0 || id >= people.Count)) throw new InvalidDataException("Invalid retained officer identity.");
        var force = forces.SingleOrDefault(f => f.CountryTitleId == 4)
            ?? throw new InvalidDataException("Missing Wa force.");
        var participants = retained.Where(id => people[id].BiographyResourceCode is 675 or 676 or 677).Order().ToImmutableArray();
        if (force.LeaderId != HistoricalPerson(people, retained, 675) ||
            !participants.Any(id => people[id].BiographyResourceCode == 676) || !participants.Any(id => people[id].BiographyResourceCode == 677))
            throw new InvalidDataException("Missing Wa participants.");
        return new(86, force.Id, 45, [184], participants, 700, 2, 500000, 10, new("penglai"))
            { Id = "penglai", Priority = -3, Consumption = EventConsumption.OnSuccess };
    }

    public static San9.Content.Scenarios.ScenarioInitialState InitializePenglai(San9.Content.Scenarios.ScenarioInitialState initial)
    {
        var activation = initial.SearchEvents.OfType<ForceActivationSearchEventDefinition>().SingleOrDefault(d => d.Id == "penglai");
        if (activation is null) return initial;
        bool dormant = initial.PlayerForceId != activation.ForceId;
        return initial with
        {
            Forces = initial.Forces.Select(f => f.Id == activation.ForceId ? f with { IsDormant = dormant } : f).ToArray(),
            Settlements = initial.Settlements.Select(s => s.Id == activation.CityId || activation.AttachedSettlementIds.Contains(s.Id)
                ? s with { IsDormant = dormant } : s).ToArray(),
            Officers = initial.Officers.Select(o => activation.OfficerIds.Contains(o.State.Id)
                ? o with { State = o.State with { IsDormant = dormant } } : o).ToArray(),
            ConsumedEventKeys = initial.ConsumedEventKeys.Where(k => k != activation.ConsumptionKey)
                .Concat(dormant ? [] : new[] { activation.ConsumptionKey }).OrderBy(k => k.Value, StringComparer.Ordinal).ToImmutableArray()
        };
    }

    private static int HistoricalPerson(IReadOnlyList<San9ScenarioPersonRecord> people, IEnumerable<int> retainedOfficerIds, int biography)
    {
        var retained = retainedOfficerIds.ToArray();
        if (retained.Any(id => id < 0 || id >= people.Count)) throw new InvalidDataException("Invalid retained officer identity.");
        var matches = retained.Where(id => people[id].BiographyResourceCode == biography).ToArray();
        return matches.Length == 1 ? matches[0] : throw new InvalidDataException("Expected one historical person identity.");
    }

    public static ShanyangGongDefinition? ShanyangGong(byte scenarioMode,
        IReadOnlyList<San9ScenarioPersonRecord> people, IEnumerable<int> retainedOfficerIds) => scenarioMode is 2 or 9 ? null :
        new(HistoricalPerson(people, retainedOfficerIds, 565), 95, "刘", 700, 2, 20, 10, 11, 20, 40, PoliticalContentCompiler.CourtEndedKey);

    public static RedCliffsDefinition? RedCliffs(byte scenarioMode,
        IReadOnlyList<San9ScenarioPersonRecord> people, IEnumerable<int> retainedOfficerIds)
    {
        if (scenarioMode is 2 or 9) return null;
        var retained = retainedOfficerIds.ToArray();
        return new(HistoricalPerson(people, retained, 91), 29, [HistoricalPerson(people, retained, 36)],
            134, [143, 32, 134, 139, 142, 31, 149, 130], 20, 1, 30000, 90, 50, 15, 15, new("red-cliffs"));
    }

    public static ChainedPlotDefinition? ChainedPlot(byte scenarioMode,
        IReadOnlyList<San9ScenarioPersonRecord> people, IEnumerable<int> retainedOfficerIds)
    {
        if (scenarioMode is 2 or 9) return null;
        var retained = retainedOfficerIds.ToArray();
        int Person(int biography) => HistoricalPerson(people, retained, biography);
        return new(Person(68), Person(3), Person(171), new(192, 4, 1), 95, 1000,
            new[] { 171, 266 }.Select(Person).ToImmutableArray(),
            new[] { 69, 236, 392, 160, 417, 42, 292 }.Select(Person).ToImmutableArray(),
            96, 80, 2, new("chained-plot.started"), new("chained-plot.completed"));
    }

    public static ThreeVisitsDefinition? ThreeVisits(byte scenarioMode,
        IReadOnlyList<San9ScenarioPersonRecord> people, IEnumerable<int> retainedOfficerIds)
    {
        if (scenarioMode is 2 or 9) return null;
        var retained = retainedOfficerIds.ToArray();
        return new(HistoricalPerson(people, retained, 36), HistoricalPerson(people, retained, 45),
            HistoricalPerson(people, retained, 39), new[] { 2, 1 }.Select(b => HistoricalPerson(people, retained, b)).ToImmutableArray(),
            138, 95, 360, 150, 10, new("three-visits.started"), new("three-visits.first-visit"), new("three-visits.completed"));
    }

    public static HanzhongKingDefinition? HanzhongKing(byte scenarioMode,
        IReadOnlyList<San9ScenarioPersonRecord> people, IEnumerable<int> retainedOfficerIds) => scenarioMode is 2 or 9 ? null :
        new(HistoricalPerson(people, retainedOfficerIds, 36), 38, 1, 30, 600, 20, new("hanzhong-king"));

    public static FiveTigersDefinition? FiveTigers(byte scenarioMode,
        IReadOnlyList<San9ScenarioPersonRecord> people, IEnumerable<int> retainedOfficerIds)
    {
        if (scenarioMode is 2 or 9) return null;
        var retained = retainedOfficerIds.ToArray();
        return new(new[] { 2, 1, 9, 4, 23 }.Select(b => HistoricalPerson(people, retained, b)).ToImmutableArray(),
            30, 15, 200, [21, 28], new("five-tigers"));
    }

    public static ExpeditionMemorialDefinition? ExpeditionMemorial(byte scenarioMode,
        IReadOnlyList<San9ScenarioPersonRecord> people, IEnumerable<int> retainedOfficerIds) => scenarioMode is 2 or 9 ? null :
        new(HistoricalPerson(people, retainedOfficerIds, 45), 1, 12, 15, 10, 15, [11, 15], new("expedition-memorial"));

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

    public static ImmutableArray<PersistentEventKey> ForceEventHistory(San9ScenarioForceRecord force) =>
        force.HasEventHistoryMarker(6) ? [new("cai-yan.returned")] : [];

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
        if (!configuredRegions.Contains(5) || !configuredRegions.Contains(82) || !configuredRegions.Contains(96) || sites.Any(s => !configuredRegions.Contains(s.PlaceId))) throw new InvalidDataException("Missing special search site.");
        return [new TreasureMonumentSearchEventDefinition(5, 81, 2, 25000, 10000, 5, new("tongque-tower"))
            { Id = "tongque-tower", Priority = -29, Consumption = EventConsumption.OnSuccess },
            new ImperialCeremonySearchEventDefinition(82, 0, 5, 50, 10, new("fengshan"))
            { Id = "fengshan", Priority = -4, Consumption = EventConsumption.OnSuccess },
            new ScholarReturnSearchEventDefinition(HistoricalPerson(people, retained, 569), 96,
                new(195, 1, 1), new(207, 12, 30), 115, 3, 300, 1000, 3000, 2, 2, 2,
                [10, 25, 22, 29], new("cai-yan"), new("cai-yan.returned"))
            { Id = "cai-yan", Priority = -16, Consumption = EventConsumption.OnPresentation },
            new HealingSearchEventDefinition(healers[0], retained.Where(id => people[id].BiographyResourceCode == 60).Order().ToImmutableArray(), 6)
            { Id = "hua-tuo.healing", Priority = -7, Consumption = EventConsumption.None },
            new BeastSearchEventDefinition(sites.ToImmutableArray(), [1, 28], 8, 2, 2, 7, scenarioMode != 1)
            { Id = "shenshou", Priority = -28, Consumption = EventConsumption.OnPresentation }];
    }

    // RE-SAN9PK-0130/0141: preserve supported historical events, beast encounters, and
    // Shanyanggong consumption also set by abdication. Source bits stay in this importer.
    public static ImmutableArray<PersistentEventKey> ConsumedKeys(IReadOnlyList<uint> flagWords, San9ScenarioDate startDate)
    {
        if (flagWords.Count != 4) throw new InvalidDataException("Expected four scenario flag words.");
        (int Bit, string Key)[] events = [(1, "chained-plot.started"), (2, "chained-plot.completed"), (3, "three-visits.started"), (4, "three-visits.first-visit"), (5, "three-visits.completed"), (7, "fengshan"), (8, "hanzhong-king"), (10, "expedition-memorial"), (11, "five-tigers"), (9, PoliticalContentCompiler.CourtEndedKey.Value), (12, "qiao-marriage"), (13, "sun-marriage"), (15, "red-cliffs"), (16, "cai-yan"), (17, "shenshou.xihe"), (18, "shenshou.wanwen"), (19, "shenshou.jianan"), (20, "tongque-tower")];
        long start = new GameClock(startDate.Year, startDate.Month, startDate.Day).DayNumber;
        bool visitsCompleted = start >= new GameClock(207, 10, 1).DayNumber;
        return events.Where(e => (flagWords[e.Bit / 32] & (1u << (e.Bit % 32))) != 0 || visitsCompleted && e.Bit is >= 3 and <= 5 ||
            e.Bit == 1 && start >= new GameClock(192, 4, 1).DayNumber || e.Bit == 2 && start >= new GameClock(192, 5, 1).DayNumber ||
            e.Bit == 9 && start >= new GameClock(234, 9, 1).DayNumber ||
            e.Bit == 15 && start >= new GameClock(209, 1, 1).DayNumber)
            .Select(e => new PersistentEventKey(e.Key)).OrderBy(k => k.Value, StringComparer.Ordinal).ToImmutableArray();
    }
}

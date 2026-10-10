using System.Collections.Immutable;
using San9.Application.Presentation;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Tests;

internal static class MissionScenario
{
    internal static ScenarioInitialState Create(int kind = 0)
    {
        // 190-01-01 starts at category 5 for force 1; select each category through real calendar input.
        int xun = (kind + 3) % 8;
        return new(ScenarioInitialState.CurrentSchemaVersion, "g08-strategy-public", 190, 1 + xun / 3, 1 + xun % 3 * 10, 1,
            [new(1, "home", 1, 100) { LeaderId = 1 },
             new(2, "enemy", 2, 100) { LeaderId = 21 },
             new(3, "third", 3, 100) { LeaderId = 31 }],
            [new(1, "home", 1, 0, 1, 10000, 100, 100, 70, []) { Durability = 500, MaxDurability = 500 },
             new(2, "enemy", 2, 10, 1, 10000, 100, 100, 70, []) { Durability = 500, MaxDurability = 500 },
             new(3, "third", 3, 20, 1, 10000, 100, 100, 70, []) { Durability = 500, MaxDurability = 500 }], [], [])
        {
            RegionNameKeys = new() { [1] = "home", [2] = "enemy", [3] = "third" },
            RegionCityIds = new() { [1] = 1, [2] = 2, [3] = 3 },
            CityConnections = [new(1, 2), new(2, 1)],
            Officers = [new("ruler", new(1, 1, 1, new(90, 90, 90, 90), false) { Status = OfficerStatus.Ruler }),
                new("actor", new(2, 1, 1, new(90, 90, 90, 90), false)),
                new("commander", new(3, 1, null, new(50, 50, 50, 50), false) { UnitId = 10 }),
                new("enemy-ruler", new(21, 2, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }),
                new("enemy-officer", new OfficerState(22, 2, 2, new(50, 50, 50, 50), false).WithLoyalty(2, 80)),
                new("enemy-commander", new(23, 2, null, new(50, 50, 50, 50), false) { UnitId = 20 }),
                new("third-ruler", new(31, 3, 3, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler })],
            Units = [new(10, 1, new(12, 7), 5000) { FormationId = 7, Morale = 70, CommanderId = 3,
                    Order = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 20 } },
                new(20, 2, new(42, 7), 5000) { FormationId = 7, Morale = 70, CommanderId = 23, }],
            Military = new(30, 3, Enumerable.Repeat(0, 90).ToImmutableArray(), [new(0, 1, null)],
                [new(7, 1, 0, 10000, 5, new(1, 1), TravelMode.Land)],
                [new(1, new(2, 7), new(1, 1)), new(2, new(52, 7), new(1, 1)), new(3, new(102, 7), new(1, 1))])
                { RegionIds = Enumerable.Repeat<int?>(1, 90).ToImmutableArray() }
        , Information = new([], [], [], [new San9.Core.Domain.CorpsState(1, 1, 1, new(10000, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 21, new(10000, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(3, 3, 31, new(10000, 10000)) { IsRoot = true }], []) };
    }


    internal static TextCatalog Texts(StrategySnapshot view) => new(TextCatalog.CurrentSchemaVersion,
        San9.Tests.CommandTextFixture.Entries(view.Officers.Select(p => p.NameKey).Concat(view.Forces.Select(f => f.NameKey)).Concat(view.Settlements.Select(s => s.NameKey))
            .Distinct().ToDictionary(key => key, key => key)));
}

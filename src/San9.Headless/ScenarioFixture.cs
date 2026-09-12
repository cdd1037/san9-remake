using San9.Content.Scenarios;

namespace San9.Headless;

internal static class ScenarioFixture
{
    internal static ScenarioInitialState Create() => new(16, "headless-public", 190, 1, 1, 1,
        [new(1, "force.1", 1, 10000, 20000, 50) { LeaderId = 1 }, new(2, "force.2", 2, 10000, 20000, 50) { LeaderId = 4 }],
        [new(1, "city.1", 1, 1, 1, 100, 50, 100, 50, []) { CorpsId = 1, Population = 60000, DraftPopulation = 10000, PublicOrder = 500, Commerce = 300, MaxCommerce = 500, Cultivation = 200, MaxCultivation = 500 },
         new(2, "city.2", 2, 2, 2, 100, 50, 100, 50, []) { CorpsId = 2, Population = 60000, DraftPopulation = 10000, PublicOrder = 500, Commerce = 300, MaxCommerce = 500, Cultivation = 200, MaxCultivation = 500 }], [], [])
    {
        Officers = [new("officer.1", new(1, 1, 1, new(60, 70, 80, 90), false)),
            new("officer.2", new(2, 1, 1, new(50, 60, 100, 70), false)), new("officer.3", new(3, 1, 1, new(40, 80, 70, 60), false)),
            new("officer.4", new(4, 2, 2, new(60, 70, 80, 90), false)), new("officer.5", new(5, 2, 2, new(50, 60, 100, 70), false)),
            new("officer.6", new(6, 2, 2, new(40, 80, 70, 60), false))],
        Information = new([], [], [], [new(1, 1, 1, null), new(2, 2, 4, null)], [])
    };
}

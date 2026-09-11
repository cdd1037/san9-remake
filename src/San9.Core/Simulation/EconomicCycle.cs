using San9.Core.Domain;

namespace San9.Core.Simulation;

internal static class EconomicCycle
{
    internal static void Apply(ResolutionStage stage, WorldMutation world, EconomyRulebook rules, List<DomainEvent> events)
    {
        if (stage == ResolutionStage.XunUpdate) Salaries(world, rules, events);
        if (stage == ResolutionStage.BeforeDailyCommands) Food(world, events);
        if (stage != ResolutionStage.QuarterUpdate || world.Clock.DayNumber - world.Simulation.Started.DayNumber < 30) return;
        Population(world, events);
        UpdateTrust(world, rules, events);
        foreach (var city in world.Settlements.Values.Where(s => s.Kind == SettlementKind.City && s.OwnerId is not null).OrderBy(s => s.Id))
        {
            var account = ResourceAccounts.Resolve(world, city.OwnerId!.Value, city.CorpsId);
            ResourceAccounts.Change(world, account, ResourceKind.Money, EconomyMath.Revenue(city), "quarterly-revenue", city.Id, events);
            if (world.Clock.Month == 7) ResourceAccounts.Change(world, account, ResourceKind.Food, EconomyMath.Harvest(city), "annual-harvest", city.Id, events);
        }
    }

    private static void Salaries(WorldMutation world, EconomyRulebook rules, List<DomainEvent> events)
    {
        int Salary(OfficerState person) => person.OfficeId is int id ? rules.Salaries.Single(s => s.OfficeId == id).Amount : 1;
        foreach (var person in world.Officers.Values.Where(p => p.IsServing && p.Status != OfficerStatus.Ruler && p.OwnerId is not null)
            .OrderByDescending(Salary).ThenBy(p => p.Id))
        {
            var city = world.Settlements[person.SettlementId!.Value];
            var account = ResourceAccounts.Resolve(world, person.OwnerId!.Value, city.CorpsId);
            int salary = Salary(person);
            bool paid = ResourceAccounts.Balance(world, account, ResourceKind.Money) >= salary;
            ResourceAccounts.Change(world, account, ResourceKind.Money, -salary, "salary", person.Id, events, true);
            if (!paid && person.Loyalty > 0)
            {
                int loyalty = Math.Max(0, person.Loyalty - 1);
                world.Replace(person with { Loyalty = loyalty });
                events.Add(new OfficerLoyaltyChangedEvent(world.Clock, person.Id, person.Loyalty, loyalty, "unpaid-salary"));
            }
        }
    }

    private static void Food(WorldMutation world, List<DomainEvent> events)
    {
        Dictionary<ResourceAccount, long> costs = [];
        void Add(int owner, int? corps, long cost)
        {
            var account = ResourceAccounts.Resolve(world, owner, corps);
            costs[account] = costs.GetValueOrDefault(account) + cost;
        }
        foreach (var city in world.Settlements.Values.Where(s => s.OwnerId is not null && s.ConsumesFood))
            Add(city.OwnerId!.Value, city.CorpsId, (city.Soldiers + 499L) / 500);
        foreach (var unit in world.Units.Values) Add(unit.OwnerId, unit.CorpsId, 3 * (unit.Soldiers + 333L) / 1000);
        foreach (var pair in costs.OrderBy(p => p.Key.Kind).ThenBy(p => p.Key.Id))
            if (pair.Value > 0) ResourceAccounts.Change(world, pair.Key, ResourceKind.Food, -pair.Value, "daily-rations", null, events);
    }

    private static void Population(WorldMutation world, List<DomainEvent> events)
    {
        var cities = world.Settlements.Values.Where(s => s.Kind == SettlementKind.City).OrderBy(s => s.Id).ToArray();
        long population = cities.Sum(s => (long)s.Population), order = cities.Sum(s => (long)s.PublicOrder);
        long military = cities.Sum(s => (long)s.DraftPopulation) + world.Settlements.Values.Sum(s => (long)s.Soldiers) + world.Units.Values.Sum(u => (long)u.Soldiers);
        long drafts = Math.Max(0, population - military + 99999) / 100000 * (population / 100) / 10;
        foreach (var city in cities)
        {
            long growth = Math.Max(30000L - city.Population, order == 0 ? 0 : city.PublicOrder * (population / 20) / order - city.Population / 20);
            long draft = order == 0 ? 0 : drafts * city.PublicOrder / order;
            uint nextPopulation = (uint)Math.Clamp(city.Population + growth, 0, 1000000);
            uint nextDraft = (uint)Math.Min(500000, city.DraftPopulation + draft);
            world.Replace(city with { Population = nextPopulation, DraftPopulation = nextDraft });
            var account = new ResourceAccount(ResourceOwnerKind.Settlement, city.Id);
            ResourceAccounts.Report(world, account, ResourceKind.Population, "quarterly-population", city.Id, growth, (long)nextPopulation - city.Population, events);
            ResourceAccounts.Report(world, account, ResourceKind.DraftPopulation, "quarterly-draft-population", city.Id, draft, (long)nextDraft - city.DraftPopulation, events);
        }
    }

    internal static void UpdateTrust(WorldMutation world, EconomyRulebook rules, List<DomainEvent> events)
    {
        foreach (var force in world.Forces.Values.OrderBy(f => f.Id).ToArray())
        {
            long contribution = world.Settlements.Values.Where(s => s.Kind == SettlementKind.City && s.OwnerId == force.Id)
                .Sum(s => ((long)s.Population + s.DraftPopulation) * Math.Max(0, s.PublicOrder - 300) / 1000 / 5000);
            bool seal = rules.ImperialSealId is int item && world.Treasures.TryGetValue(item, out var treasure) &&
                force.LeaderId is not null && treasure.HolderId == force.LeaderId && treasure.Discovered;
            bool emperor = rules.EmperorId is int id && world.Officers.TryGetValue(id, out var person) &&
                person.SettlementId is int city && world.Settlements[city].OwnerId == force.Id && person.Status != OfficerStatus.Dead;
            int trust = (int)Math.Clamp(force.BaseTrust + contribution + (seal ? 100 : 0) + (emperor ? 100 : 0), 0, 1000);
            if (trust == force.Trust) continue;
            world.Replace(force with { Trust = trust });
            events.Add(new ForceTrustChangedEvent(world.Clock, force.Id, force.Trust, trust));
        }
    }
}

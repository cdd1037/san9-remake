using San9.Core.Domain;

namespace San9.Core.Simulation;

internal static class EconomicCycle
{
    internal static IReadOnlyList<DailyRationAccount> Apply(ResolutionStage stage, WorldMutation world, EconomyRulebook rules, List<DomainEvent> events)
    {
        if (stage == ResolutionStage.XunUpdate)
        {
            Salaries(world, rules, events);
            CityMerchants.Refresh(world, events);
            FoodMarket.Refresh(world, rules.FoodMarket, events);
        }
        if (stage == ResolutionStage.BeforeDailyCommands) return Food(world, events);
        if (stage != ResolutionStage.QuarterUpdate || world.Clock.DayNumber - world.Simulation.Started.DayNumber < 30) return [];
        Population(world, events);
        UpdateTrust(world, rules, events);
        Income(world, events);
        return [];
    }

    private static void Income(WorldMutation world, List<DomainEvent> events)
    {
        HashSet<ResourceAccount> supplied = [];
        Dictionary<int, long> foodTargets = [];
        foreach (var city in world.Settlements.Values.Where(s => s.Kind == SettlementKind.City && s.OwnerId is not null).OrderBy(s => s.Id))
        {
            var account = ResourceAccounts.Resolve(world, city.OwnerId!.Value, city.CorpsId);
            if (!world.Forces[city.OwnerId.Value].IsBarbarian)
            {
                OrdinaryIncome(world, city, account, events);
                continue;
            }
            if (!supplied.Add(account)) continue;
            BarbarianSupply(world, city, account, foodTargets, events);
        }
    }

    private static void OrdinaryIncome(WorldMutation world, SettlementState city, ResourceAccount account, List<DomainEvent> events)
    {
        ResourceAccounts.Change(world, account, ResourceKind.Money, EconomyMath.Revenue(city), "quarterly-revenue", city.Id, events);
        if (world.Clock.Month != 7) return;
        ResourceAccounts.Change(world, account, ResourceKind.Food, EconomyMath.Harvest(city), "annual-harvest", city.Id, events);
    }

    private static void BarbarianSupply(WorldMutation world, SettlementState city, ResourceAccount account,
        Dictionary<int, long> foodTargets, List<DomainEvent> events)
    {
        ResourceAccounts.Change(world, account, ResourceKind.Money,
            EconomyRulebook.BarbarianQuarterlyFunds - ResourceAccounts.Balance(world, account, ResourceKind.Money),
            "barbarian-quarterly-funds", city.Id, events);
        if (world.Clock.Month != 7) return;
        int forceId = city.OwnerId!.Value;
        if (!foodTargets.TryGetValue(forceId, out long food))
        {
            long soldiers = world.Settlements.Values.Where(s => s.OwnerId == forceId).Sum(s => (long)s.Soldiers + s.Wounded)
                + world.Units.Values.Where(u => u.OwnerId == forceId).Sum(u => (long)u.Soldiers + u.Wounded);
            foodTargets.Add(forceId, food = EconomyMath.BarbarianAnnualFood(soldiers));
        }
        ResourceAccounts.Change(world, account, ResourceKind.Food,
            food - ResourceAccounts.Balance(world, account, ResourceKind.Food), "barbarian-annual-food", city.Id, events);
    }

    private static void Salaries(WorldMutation world, EconomyRulebook rules, List<DomainEvent> events)
    {
        int Salary(OfficerState person) => person.OfficeId is int id ? rules.Salaries.Single(s => s.OfficeId == id).Amount : 1;
        foreach (var person in world.Officers.Values.Where(p => p.IsServing && p.Status != OfficerStatus.Ruler && p.OwnerId is not null)
            .OrderByDescending(Salary).ThenBy(p => p.Id))
        {
            int? corpsId = PersonnelLocations.Corps(world, person);
            var account = ResourceAccounts.Resolve(world, person.OwnerId!.Value, corpsId);
            int salary = Salary(person);
            bool paid = ResourceAccounts.Balance(world, account, ResourceKind.Money) >= salary;
            ResourceAccounts.Change(world, account, ResourceKind.Money, -salary, "salary", person.Id, events, true);
            var force = world.Forces[person.OwnerId.Value];
            int distance = force.LeaderId is int ruler ? PersonnelRulebook.AffinityDistance(person, world.Officers[ruler]) : 50;
            int chance = EconomyMath.SalaryLoyaltyChance(paid, force.Trust, distance, person.Righteousness);
            int roll = (int)(world.NextRandom("personnel-salary-loyalty") % 100);
            if (roll >= chance) continue;
            int loyalty = Math.Clamp(person.Loyalty + (paid ? 1 : -1), 0, 255);
            if (loyalty == person.Loyalty) continue;
            world.Replace(person.WithLoyalty(force.Id, loyalty));
            events.Add(new OfficerLoyaltyChangedEvent(world.Clock, person.Id, force.Id, person.Loyalty, loyalty,
                paid ? "paid-salary" : "unpaid-salary"));
        }
    }

    private static IReadOnlyList<DailyRationAccount> Food(WorldMutation world, List<DomainEvent> events)
    {
        Dictionary<ResourceAccount, long> costs = [];
        void Add(int owner, int? corps, long cost)
        {
            var account = ResourceAccounts.Resolve(world, owner, corps);
            costs[account] = costs.GetValueOrDefault(account) + cost;
        }
        foreach (var city in world.Settlements.Values.Where(s => s.OwnerId is not null && s.ConsumesFood))
            Add(city.OwnerId!.Value, city.CorpsId, ((long)city.Soldiers + city.Wounded + 499) / 500);
        foreach (var unit in world.Units.Values) Add(unit.OwnerId, unit.CorpsId, 3 * ((long)unit.Soldiers + unit.Wounded + 333) / 1000);
        List<DailyRationAccount> accounts = [];
        foreach (var pair in costs.OrderBy(p => p.Key.Kind).ThenBy(p => p.Key.Id))
        {
            if (pair.Value <= 0) continue;
            ResourceAccounts.Change(world, pair.Key, ResourceKind.Food, -pair.Value, "daily-rations", null, events);
            accounts.Add(new(pair.Key, ResourceAccounts.Balance(world, pair.Key, ResourceKind.Food) == 0));
        }
        return accounts;
    }

    private static void Population(WorldMutation world, List<DomainEvent> events)
    {
        var cities = world.Settlements.Values.Where(s => s.Kind == SettlementKind.City).OrderBy(s => s.Id).ToArray();
        long population = cities.Sum(s => (long)s.Population), order = cities.Sum(s => (long)s.PublicOrder);
        long military = cities.Sum(s => (long)s.DraftPopulation) + world.Settlements.Values.Sum(s => (long)s.Soldiers + s.Wounded) + world.Units.Values.Sum(u => (long)u.Soldiers + u.Wounded);
        long drafts = Math.Max(0, population - military + 99999) / 100000 * (population / 100) / 10;
        foreach (var city in cities)
        {
            long growth = Math.Max(30000L - city.Population, order == 0 ? 0 : city.PublicOrder * (population / 20) / order - city.Population / 20);
            long draft = order == 0 ? 0 : drafts * city.PublicOrder / order;
            uint nextPopulation = (uint)Math.Clamp(city.Population + growth, 0, 1000000);
            uint nextDraft = (uint)Math.Min(Math.Max(500000L, city.DraftPopulation), city.DraftPopulation + draft);
            world.Replace(city with { Population = nextPopulation, DraftPopulation = nextDraft });
            var account = new ResourceAccount(ResourceOwnerKind.Settlement, city.Id);
            ResourceAccounts.Report(world, account, ResourceKind.Population, "quarterly-population", city.Id, growth, (long)nextPopulation - city.Population, events);
            ResourceAccounts.Report(world, account, ResourceKind.DraftPopulation, "quarterly-draft-population", city.Id, draft, (long)nextDraft - city.DraftPopulation, events);
        }
    }

    internal static void UpdateTrust(WorldMutation world, EconomyRulebook rules, List<DomainEvent> events)
    {
        int? emperorCity = ImperialCourt.City(world, rules);
        foreach (var force in world.Forces.Values.Where(f => !f.IsEliminated).OrderBy(f => f.Id).ToArray())
        {
            long contribution = world.Settlements.Values.Where(s => s.Kind == SettlementKind.City && s.OwnerId == force.Id)
                .Sum(s => ((long)s.Population + s.DraftPopulation) * Math.Max(0, s.PublicOrder - 300) / 1000 / 5000);
            bool seal = rules.ImperialSealId is int item && world.Treasures.TryGetValue(item, out var treasure) &&
                force.LeaderId is not null && treasure.HolderId == force.LeaderId && treasure.Discovered;
            bool emperor = emperorCity is int city && world.Settlements[city].OwnerId == force.Id;
            int trust = (int)Math.Clamp(force.BaseTrust + contribution + (seal ? 100 : 0) + (emperor ? 100 : 0), 0, 1000);
            if (trust == force.Trust) continue;
            world.Replace(force with { Trust = trust });
            events.Add(new ForceTrustChangedEvent(world.Clock, force.Id, force.Trust, trust));
        }
    }
}

using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record FoodExhaustedEvent(GameClock Clock, int ForceId, int CorpsId) : DomainEvent;

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
        var rates = EconomicIncomeRates.ForForce(world.Simulation, world.Forces[city.OwnerId!.Value], world.Companion);
        ResourceAccounts.Change(world, account, ResourceKind.Money, rates.Revenue(city), "quarterly-revenue", city.Id, events);
        if (world.Clock.Month != 7) return;
        ResourceAccounts.Change(world, account, ResourceKind.Food, rates.Harvest(city), "annual-harvest", city.Id, events);
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
            long soldiers = SoldierTotals.Living(world, forceId);
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
            var force = world.Forces[person.OwnerId!.Value];
            int peerageSalary = world.Simulation.NewGame.PersonalPeerageEnabled == false ? 0 : rules.PeeragePolicy(force, world.Clock.Year).Salary(person.PersonalPeerage);
            bool paid = ResourceAccounts.Balance(world, account, ResourceKind.Money) >= (long)salary + peerageSalary;
            ResourceAccounts.Change(world, account, ResourceKind.Money, -salary, "salary", person.Id, events, true);
            if (peerageSalary > 0)
                ResourceAccounts.Change(world, account, ResourceKind.Money, -peerageSalary, "peerage-salary", person.Id, events, true);
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
        foreach (var ruler in world.Officers.Values.Where(p => world.Simulation.NewGame.PersonalPeerageEnabled != false && p.Status == OfficerStatus.Ruler && p.OwnerId is not null && p.PersonalPeerage is not null).OrderBy(p => p.Id))
        {
            int salary = rules.PeeragePolicy(world.Forces[ruler.OwnerId!.Value], world.Clock.Year).Salary(ruler.PersonalPeerage);
            if (salary == 0) continue;
            var account = ResourceAccounts.Resolve(world, ruler.OwnerId!.Value, PersonnelLocations.Corps(world, ruler));
            ResourceAccounts.Change(world, account, ResourceKind.Money, -salary, "peerage-salary", ruler.Id, events, true);
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
            Add(city.OwnerId!.Value, city.CorpsId, DailyRations.Garrison(city.Soldiers, city.Wounded));
        foreach (var unit in world.Units.Values) Add(unit.OwnerId, unit.CorpsId, DailyRations.Field(unit.Soldiers, unit.Wounded));
        foreach (var task in world.Simulation.Tasks)
            if (task.Reinforcements is { } troops)
                Add(task.ForceId, ReinforcementTransport.Corps(world, task), DailyRations.Field(troops.Soldiers, troops.Wounded));
        List<DailyRationAccount> accounts = [];
        foreach (var pair in costs.OrderBy(p => p.Key.Kind).ThenBy(p => p.Key.Id))
        {
            if (pair.Value <= 0) continue;
            uint before = ResourceAccounts.Balance(world, pair.Key, ResourceKind.Food);
            bool shortage = before < pair.Value;
            uint remaining = ResourceAccounts.Change(world, pair.Key, ResourceKind.Food, -pair.Value, "daily-rations", null, events);
            if (before > 0 && remaining == 0)
                events.Add(new FoodExhaustedEvent(world.Clock, world.Corps[pair.Key.Id].OwnerId, pair.Key.Id));
            accounts.Add(new(pair.Key, shortage));
        }
        return accounts;
    }

    private static void Population(WorldMutation world, List<DomainEvent> events)
    {
        var cities = world.Settlements.Values.Where(s => !s.IsDestroyed && s.Kind == SettlementKind.City).OrderBy(s => s.Id).ToArray();
        var (population, military, order) = PopulationOutlook.Totals(world);
        foreach (var city in cities)
        {
            long growth = Math.Max(30000L - city.Population, order == 0 ? 0 : city.PublicOrder * (population / 20) / order - city.Population / 20);
            long draft = EconomyMath.DraftIncrease(population, military, order, city.PublicOrder);
            uint nextPopulation = (uint)Math.Clamp(city.Population + growth, 0, 1000000);
            uint nextDraft = (uint)Math.Min(Math.Max(500000L, city.DraftPopulation), city.DraftPopulation + draft);
            world.Replace(city with { Population = nextPopulation, DraftPopulation = nextDraft });
            var account = new ResourceAccount(ResourceOwnerKind.Settlement, city.Id);
            ResourceAccounts.Report(world, account, ResourceKind.Population, "quarterly-population", city.Id, growth, (long)nextPopulation - city.Population, events);
            ResourceAccounts.Report(world, account, ResourceKind.DraftPopulation, "quarterly-draft-population", city.Id, draft, (long)nextDraft - city.DraftPopulation, events);
        }
    }
}

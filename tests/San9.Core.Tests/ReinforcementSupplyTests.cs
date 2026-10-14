using San9.Tests;
using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class ReinforcementSupplyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForceExitCancelsLoadedTasksExactlyOnceWithoutChangingTreasury(bool returning)
    {
        var (world, rules, task) = Create();
        var mutation = world.BeginMutation(rules.Abilities);
        task = task with { Reinforcements = task.Reinforcements! with { Soldiers = 2000, Wounded = 1000 } };
        mutation.SetSimulation(mutation.Simulation with { Tasks = [task] });
        if (returning)
        {
            mutation.SetSimulation(mutation.Simulation with { Tasks = [] });
            PersonnelReturns.Start(mutation, task, rules.Personnel, [], false, arrived: true);
        }
        mutation.Replace(mutation.Settlements[1] with { DraftPopulation = 499990 });
        uint soldiers = mutation.Settlements[1].Soldiers, wounded = mutation.Settlements[1].Wounded;
        var treasury = ResourceAccounts.Root(mutation, 1).Treasury;
        List<DomainEvent> events = [];
        ForceTransfer.CancelTasks(mutation, 1, rules, "force-exit", events, false);
        ForceTransfer.CancelTasks(mutation, 1, rules, "force-exit", events, false);
        Assert.Empty(mutation.Simulation.Tasks);
        Assert.Single(events.OfType<TaskCancelledEvent>());
        Assert.Equal(502990u, mutation.Settlements[1].DraftPopulation);
        Assert.Equal(soldiers, mutation.Settlements[1].Soldiers);
        Assert.Equal(wounded, mutation.Settlements[1].Wounded);
        Assert.Equal(treasury.Money, mutation.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 1).Treasury.Money);
        Assert.Equal(treasury.Food, mutation.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 1).Treasury.Food);
        var returned = Assert.Single(events.OfType<ResourceChangedEvent>());
        Assert.Equal(ResourceKind.DraftPopulation, returned.Resource);
        Assert.Equal(3000, returned.Actual);
        world.Commit(mutation);
        var saved = world.CreateSnapshot();
        StateAssert.Equal(saved, WorldState.Restore(saved).CreateSnapshot());
    }

    private static (WorldState World, SimulationRules Rules, PersistentTask Task) Create(int distance = 300)
    {
        var rules = UnitStrategyTests.Rules();
        var world = UnitStrategyTests.World(friendly: true, distance: distance);
        return (world, rules, UnitStrategyTests.Submit(world, rules, UnitStrategyKind.Reinforce, 0));
    }

    private static List<DomainEvent> Supply(WorldMutation world, SimulationRules rules)
    {
        List<DomainEvent> events = [];
        var accounts = EconomicCycle.Apply(ResolutionStage.BeforeDailyCommands, world, rules.Economy, events);
        MilitaryDailyCycle.Apply(world, accounts, rules, events);
        return events;
    }

    [Theory]
    [InlineData(false, 38u, false)]
    [InlineData(false, 37u, true)]
    [InlineData(true, 38u, false)]
    [InlineData(true, 37u, true)]
    public void OutboundAndReturningTroopsShareAccountShortageWithGarrisonAndArmy(bool returning, uint food, bool shortage)
    {
        var (world, rules, task) = Create(); var m = world.BeginMutation(rules.Abilities);
        if (returning)
        {
            m.SetSimulation(m.Simulation with { Tasks = [] });
            PersonnelReturns.Start(m, task, rules.Personnel, [], false, arrived: true);
        }
        m.Replace(ResourceAccounts.Root(m, 1) with { Treasury = ResourceAccounts.Root(m, 1).Treasury with { Food = food } }); // 7000 garrison:14, 5000 army:15, 3000 escort:9.
        var events = Supply(m, rules);
        var troops = Assert.Single(m.Simulation.Tasks).Reinforcements!;
        Assert.Equal(shortage ? 2970u : 3000u, troops.Soldiers);
        Assert.Equal(shortage ? 15u : 0u, troops.Wounded);
        Assert.Equal(shortage ? 67 : 70, troops.Morale);
        Assert.Equal(shortage ? 6930u : 7000u, m.Settlements[1].Soldiers);
        Assert.Equal(shortage ? 4950u : 5000u, m.Units[5].Soldiers);
        Assert.Equal(0u, m.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 1).Treasury.Food);
        Assert.Equal(shortage ? 3 : 0, events.OfType<DailyAttritionEvent>().Count());
        Assert.Single(events.OfType<ResourceChangedEvent>(), e => e.Reason == "daily-rations" && e.Account == new ResourceAccount(ResourceOwnerKind.Corps, 1));
        if (shortage) Assert.Equal(Assert.Single(m.Simulation.Tasks).Id, Assert.Single(events.OfType<DailyAttritionEvent>(), e => e.TaskId is not null).TaskId);
        world.Commit(m);
    }

    [Fact]
    public void TravelConsumesWoundedRationsAndUsesCurrentCorpsInsteadOfTargetOrEnemyAccount()
    {
        var (world, rules, task) = Create(); var m = world.BeginMutation(rules.Abilities);
        m.AddCorps(new(10, 1, 12, new(1000, 1000)));
        m.Replace(m.Settlements[1] with { CorpsId = 10 });
        m.SetSimulation(m.Simulation with { Tasks = [task with { Reinforcements = task.Reinforcements! with { Soldiers = 2000, Wounded = 1000 } }] });
        Supply(m, rules);
        Assert.Equal(977u, m.Corps[10].Treasury!.Food); // Same nine rations for all 3000 living travelers.
        Assert.Equal(9985u, m.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 1).Treasury.Food); // Target army only.
        m.Replace(m.Settlements[1] with { CorpsId = null });
        Supply(m, rules);
        Assert.Equal(9947u, m.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 1).Treasury.Food);
        Assert.Equal(977u, m.Corps[10].Treasury!.Food);
        m.Replace(m.Settlements[1] with { OwnerId = 2 });
        Supply(m, rules);
        Assert.Equal(9923u, m.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 1).Treasury.Food); // Escort remains charged to its own force.
        Assert.Equal(15000, SoldierTotals.Living(m, 1) + 7000); // Lost garrison excluded; wounded escort retained.
    }

    [Fact]
    public void DeathsReduceDeliveryAndAllWoundedReturnWithoutBecomingActiveSoldiers()
    {
        var (world, rules, task) = Create(distance: 30); var m = world.BeginMutation(rules.Abilities);
        m.Replace(ResourceAccounts.Root(m, 1) with { Treasury = ResourceAccounts.Root(m, 1).Treasury with { Food = 0 } });
        var events = UnitStrategyTests.Day(m, rules);
        Assert.Equal(2970u, Assert.Single(events.OfType<UnitStrategyResolvedEvent>()).DeliveredSoldiers);
        var returning = Assert.Single(m.Simulation.Tasks);
        Assert.Equal(0u, returning.Reinforcements!.Soldiers);
        Assert.Equal(15u, returning.Reinforcements.Wounded);
        m.Replace(ResourceAccounts.Root(m, 1) with { Treasury = ResourceAccounts.Root(m, 1).Treasury with { Food = 10000 } });
        uint soldiers = m.Settlements[1].Soldiers, wounded = m.Settlements[1].Wounded;
        for (int day = 0; day < 10 && m.Simulation.Tasks.Length > 0; day++) UnitStrategyTests.Day(m, rules);
        Assert.Empty(m.Simulation.Tasks);
        Assert.Equal(soldiers, m.Settlements[1].Soldiers);
        Assert.Equal(wounded + 15, m.Settlements[1].Wounded);
    }

    [Theory]
    [InlineData(1u, 70)]
    [InlineData(3000u, 3)]
    public void UnableToDeliverTurnsBackBeforeArrival(uint soldiers, int morale)
    {
        var (world, rules, task) = Create(); var m = world.BeginMutation(rules.Abilities);
        m.Replace(ResourceAccounts.Root(m, 1) with { Treasury = ResourceAccounts.Root(m, 1).Treasury with { Food = 0 } });
        m.SetSimulation(m.Simulation with { Tasks = [task with { Reinforcements = task.Reinforcements! with { Soldiers = soldiers, Morale = morale } }] });
        var events = Supply(m, rules);
        Assert.Equal("reinforcement-unfit", Assert.Single(events.OfType<TaskCancelledEvent>()).Reason);
        var returning = Assert.Single(m.Simulation.Tasks);
        Assert.Equal(PersonnelReturns.Kind, returning.Kind);
        Assert.Equal(0, returning.Journey!.ReturnLeg!.CountdownDays); // No outbound distance has been traveled yet.
        if (soldiers == 1) Assert.Null(returning.Reinforcements);
        else Assert.Equal(15u, returning.Reinforcements!.Wounded);
        world.Commit(m);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WoundedOverflowAndExecutorLossPreserveEverySurvivor(bool captured)
    {
        var (world, rules, task) = Create(); var m = world.BeginMutation(rules.Abilities);
        var woundedTask = task with { Reinforcements = task.Reinforcements! with { Soldiers = 2000, Wounded = 1000 } };
        m.SetSimulation(m.Simulation with { Tasks = [woundedTask] });
        if (captured)
        {
            PersonnelTransitions.Capture(m, 11, 2, null, rules.Personnel, []);
            Assert.Equal(3000u, m.Settlements[1].DraftPopulation);
            PersonnelTransitions.CancelMissions(m, 11, "repeat", [], rules.Personnel, false);
            Assert.Equal(3000u, m.Settlements[1].DraftPopulation);
        }
        else
        {
            m.Replace(m.Settlements[1] with { Soldiers = m.Settlements[1].SoldierCapacity, Wounded = MilitaryRulebook.WoundedCapacity - 100 });
            ReinforcementTransport.Arrive(m, woundedTask, rules.Personnel, []);
            Assert.Equal(MilitaryRulebook.WoundedCapacity, m.Settlements[1].Wounded);
            Assert.Equal(2900u, m.Settlements[1].DraftPopulation);
        }
    }

    [Fact]
    public void DamagedTransportRestoresAndContinuesWithIdenticalEventsAndNoTroopRefill()
    {
        var (world, rules, _) = Create(distance: 900); var m = world.BeginMutation(rules.Abilities);
        m.Replace(ResourceAccounts.Root(m, 1) with { Treasury = ResourceAccounts.Root(m, 1).Treasury with { Food = 0 } });
        world.Commit(m);
        var engine = new SimulationEngine(world, rules);
        engine.EndControl(new("player"));
        engine.AdvanceXun();
        var snapshot = world.CreateSnapshot();
        var restored = WorldState.Restore(JsonSerializer.Deserialize<WorldSnapshot>(CanonicalJson.Text(snapshot), CanonicalJson.Options)!);
        Assert.InRange(Assert.Single(restored.Simulation.Tasks).Reinforcements!.Soldiers, 1u, 2999u);
        var restoredEngine = new SimulationEngine(restored, rules);
        engine.EndControl(new("player")); restoredEngine.EndControl(new("player"));
        var first = engine.AdvanceXun().Events; var second = restoredEngine.AdvanceXun().Events;
        StateAssert.Equal(world.CreateSnapshot(), restored.CreateSnapshot());
        string json = JsonSerializer.Serialize<DomainEvent[]>(first.ToArray(), CanonicalJson.Options);
        Assert.Equal(json, JsonSerializer.Serialize<DomainEvent[]>(second.ToArray(), CanonicalJson.Options));
        Assert.Contains(JsonSerializer.Deserialize<DomainEvent[]>(json, CanonicalJson.Options)!, e => e is DailyAttritionEvent { TaskId: not null });
        var node = JsonNode.Parse(CanonicalJson.Text(Assert.Single(snapshot.Simulation.Tasks)))!;
        Assert.True(node["reinforcements"]!.AsObject().Remove("wounded"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PersistentTask>(node.ToJsonString(), CanonicalJson.Options));
    }
}
